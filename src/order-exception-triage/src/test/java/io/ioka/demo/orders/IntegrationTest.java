// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import com.sun.net.httpserver.HttpServer;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.errors.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.model.SessionsApi.*;
import io.ioka.munarium.client.planes.Params;
import org.junit.jupiter.api.*;
import java.net.*;
import java.net.http.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.jupiter.api.Assertions.*;

@Tag("controlled")
class IntegrationTest {
    private Bootstrap.Grant grant;
    @BeforeEach void setup() throws Exception { grant = Bootstrap.grant(); assertEquals("fixture", grant.provider()); }
    private Path directory(String name) { return AcceptanceTest.report().resolve(name); }
    private OrderEvent event() throws Exception { return Fixtures.events(Path.of("/inputs")).getFirst(); }
    private Inbox inbox(String name) throws Exception { return new Inbox(directory(name), AcceptanceTest.identity(grant)); }
    private static int calls() throws Exception {
        return Json.MAPPER.readTree(HttpClient.newHttpClient().send(HttpRequest.newBuilder(URI.create("http://provider-fixture:11434/calls")).build(), HttpResponse.BodyHandlers.ofString()).body()).path("calls").asInt();
    }
    private static void fixture(String action) throws Exception {
        assertEquals(200, HttpClient.newHttpClient().send(HttpRequest.newBuilder(URI.create("http://provider-fixture:11434/" + action)).build(), HttpResponse.BodyHandlers.discarding()).statusCode());
    }
    @Test void scopedCapabilityCannotOpenAnotherExistingRunbook() throws Exception {
        try (var issuer = Bootstrap.ops(true)) {
            var token = issuer.tokens.mint(new Tokens.IssueTokenRequest(grant.uid(), 0, List.of(), List.of("query"), List.of(grant.runbooks().get("event-001").split("@")[0]), 300L));
            try (var client = Bootstrap.client(token.token(), grant.uid())) {
                assertThrows(ForbiddenException.class, () -> client.sessions.create(grant.runbooks().get("event-002")));
                assertNotNull(client.sessions.create(grant.runbooks().get("event-001")));
            }
        }
    }
    @Test void expiredCapabilityFailsBeforeProviderWork() throws Exception {
        try (var issuer = Bootstrap.ops(true)) {
            var token = issuer.tokens.mint(new Tokens.IssueTokenRequest(grant.uid(), 0, List.of(), List.of("query"), List.of(grant.runbooks().get("event-001").split("@")[0]), 1L));
            // Server 1.1.1 allows 30 seconds of JWT clock skew. Wait beyond the issued expiry and allowance.
            long remaining = java.time.Duration.between(java.time.Instant.now(), java.time.Instant.parse(token.expiresAt()).plusSeconds(32)).toMillis();
            if (remaining > 0) Thread.sleep(remaining);
            int before = calls();
            try (var client = Bootstrap.client(token.token(), grant.uid())) { assertThrows(UnauthenticatedException.class, () -> client.sessions.create(grant.runbooks().get("event-001"))); }
            assertEquals(before, calls());
        }
    }
    @Test void unknownOrDeniedOverrideMakesNoProviderCall() throws Exception {
        try (var client = Bootstrap.client(grant.token(), grant.uid())) {
            String session = client.sessions.create(grant.runbooks().get("event-001")).sessionId(); int before = calls();
            assertThrows(ForbiddenException.class, () -> client.sessions.turn(session, Params.TurnOptions.of("stock shortage").withCompletion(ModelOverride.provider("unapproved-provider"))));
            assertEquals(before, calls());
        }
    }
    @Test void providerOutageStaysUncertainWithoutAutomaticResubmission() throws Exception {
        fixture("fail");
        try (var inbox = inbox("outage")) {
            var worker = new Worker(inbox, grant);
            assertThrows(Exception.class, () -> worker.consume(List.of(event())));
            assertEquals("uncertain", inbox.get("event-001").state());
            int count = calls();
            fixture("reset");
            worker.consume(List.of(event())); worker.reconcile();
            assertEquals(count, calls()); assertEquals("uncertain", inbox.get("event-001").state());
            assertTrue(inbox.packets().isEmpty()); worker.export(directory("outage/packets"));
        } finally { fixture("reset"); }
    }
    @Test void emptyTranscriptRemainsUncertain() throws Exception {
        try (var inbox = inbox("empty"); var client = Bootstrap.client(grant.token(), grant.uid())) {
            inbox.accept(event()); inbox.claim("event-001", client.sessions.create(grant.runbooks().get("event-001")).sessionId(), event().query());
            int count = calls(); new Worker(inbox, grant).reconcile();
            assertEquals("uncertain", inbox.get("event-001").state()); assertEquals(count, calls());
        }
    }
    @Test void multipleMatchingTurnsRequireManualReview() throws Exception {
        try (var inbox = inbox("ambiguous"); var client = Bootstrap.client(grant.token(), grant.uid())) {
            inbox.accept(event()); String session = client.sessions.create(grant.runbooks().get("event-001")).sessionId();
            inbox.claim("event-001", session, event().query());
            // Deliberately construct an ambiguous transcript; this is not the consumer's retry behavior.
            for (int i = 0; i < 2; i++) client.sessions.turn(session, Params.TurnOptions.of(event().query()).withCompletion(null));
            int count = calls(); new Worker(inbox, grant).reconcile();
            assertEquals("uncertain", inbox.get("event-001").state()); assertEquals(count, calls());
        }
    }
    @Test void processCrashRecoversExactlyOneCompletedTurn() throws Exception {
        crash(directory("crash")); int count = calls();
        try (var inbox = inbox("crash")) {
            assertEquals("uncertain", inbox.get("event-001").state());
            var worker = new Worker(inbox, grant); worker.consume(List.of(event())); assertEquals(count, calls());
            worker.reconcile(); assertEquals("complete", inbox.get("event-001").state()); assertTrue(inbox.get("event-001").recovered());
            assertEquals(count, calls()); worker.export(directory("crash/packets"));
            var packet = Json.MAPPER.readTree(inbox.packets().get("event-001")); assertFalse(packet.path("evidence").has("skipped"));
        }
    }
    @Test void preparePendingWorkForServerRestart() throws Exception {
        crash(AcceptanceTest.report().getParent().resolve("restart"));
    }
    private static void crash(Path directory) throws Exception {
        Files.createDirectories(directory);
        var child = new ProcessBuilder("/app/build/install/order-exception-triage/bin/order-exception-triage", "crash-test", directory.toString()).redirectErrorStream(true).redirectOutput(directory.resolve("crash.log").toFile()).start();
        if (!child.waitFor(90, TimeUnit.SECONDS)) { child.destroyForcibly(); fail("Crash subprocess timed out"); }
        assertEquals(71, child.exitValue());
    }
    @Test void droppedStreamRecoversThroughRealServerTranscript() throws Exception {
        var proxy = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        var upstream = HttpClient.newHttpClient();
        proxy.createContext("/", exchange -> {
            try {
                byte[] body = exchange.getRequestBody().readAllBytes();
                var request = HttpRequest.newBuilder(URI.create(Bootstrap.endpoint() + exchange.getRequestURI())).method(exchange.getRequestMethod(), HttpRequest.BodyPublishers.ofByteArray(body));
                for (String key : List.of("Authorization", "X-Munarium-Uid", "Content-Type", "Accept")) {
                    String value = exchange.getRequestHeaders().getFirst(key); if (value != null) request.header(key, value);
                }
                var response = upstream.send(request.build(), HttpResponse.BodyHandlers.ofByteArray());
                exchange.getResponseHeaders().set("Content-Type", response.headers().firstValue("content-type").orElse("application/json"));
                boolean turn = exchange.getRequestMethod().equals("POST") && exchange.getRequestURI().getPath().contains("turn");
                byte[] bytes = turn ? "event: progress\ndata: {\"stage\":\"retrieval\"}\n\n".getBytes(java.nio.charset.StandardCharsets.UTF_8) : response.body();
                exchange.sendResponseHeaders(response.statusCode(), bytes.length); exchange.getResponseBody().write(bytes);
            } catch (Exception e) { exchange.sendResponseHeaders(502, -1); }
            finally { exchange.close(); }
        });
        proxy.start();
        try (var inbox = inbox("stream")) {
            var worker = new Worker(inbox, grant, "http://127.0.0.1:" + proxy.getAddress().getPort());
            assertThrows(Exception.class, () -> worker.consume(List.of(event())));
            assertEquals("uncertain", inbox.get("event-001").state());
            int count = calls();
            var restored = new Worker(inbox, grant); restored.reconcile();
            assertEquals("complete", inbox.get("event-001").state()); assertEquals(count, calls()); restored.export(directory("stream/packets"));
        } finally { proxy.stop(0); }
    }
    @Test void completedOutboxRestoresDeletedExportWithoutCompletion() throws Exception {
        int count;
        try (var inbox = inbox("export")) {
            var worker = new Worker(inbox, grant); worker.consume(List.of(event())); worker.export(directory("export/packets")); count = calls();
        }
        Files.delete(directory("export/packets/event-001.json"));
        try (var inbox = inbox("export")) {
            var worker = new Worker(inbox, grant); worker.consume(List.of(event())); worker.export(directory("export/packets"));
            assertTrue(Files.isRegularFile(directory("export/packets/event-001.json"))); assertEquals(count, calls());
        }
    }
    @Test void invalidRouteCitationAndModelAreRejected() throws Exception {
        try (var client = Bootstrap.client(grant.token(), grant.uid())) {
            var result = client.sessions.turn(client.sessions.create(grant.runbooks().get("event-001")).sessionId(), Params.TurnOptions.of(event().query()).withCompletion(null));
            for (String field : List.of("route", "citations", "model")) {
                var tree = Json.MAPPER.valueToTree(result).deepCopy();
                var completion = (com.fasterxml.jackson.databind.node.ObjectNode) tree.path("completion");
                if (field.equals("model")) completion.put("model", "wrong-model");
                else {
                    var answer = (com.fasterxml.jackson.databind.node.ObjectNode) Json.MAPPER.readTree(completion.path("text").asText());
                    if (field.equals("route")) answer.put("route", "SHIP_NOW"); else answer.putArray("citations").add("fabricated/chunk");
                    completion.put("text", FilesUtil.json(answer));
                }
                var changed = Json.MAPPER.treeToValue(tree, TurnResult.class);
                assertThrows(Exception.class, () -> Worker.validate(changed, grant, "event-001"));
            }
        }
    }
}
