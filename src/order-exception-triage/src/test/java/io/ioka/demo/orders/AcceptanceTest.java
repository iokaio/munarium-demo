// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import io.ioka.munarium.client.model.*;
import org.junit.jupiter.api.*;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import java.nio.file.*;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

class AcceptanceTest {
    static Path report() { return Path.of(Objects.requireNonNull(System.getenv("ORDER_REPORT_DIR"), "Report directory required")); }
    static String identity(Bootstrap.Grant grant) { return grant.uid() + ":" + grant.namespace(); }
    @Tag("controlled") @ParameterizedTest @ValueSource(strings = {"event-001", "event-002", "event-003", "event-004", "event-005", "event-006", "event-007", "event-008"})
    void controlledBusinessCase(String id) throws Exception { qualify(id, "fixture"); }
    @Tag("cloud-openai") @ParameterizedTest @ValueSource(strings = {"event-001", "event-002", "event-007"})
    void openaiBusinessCase(String id) throws Exception { qualify(id, "openai"); }
    @Tag("cloud-anthropic") @ParameterizedTest @ValueSource(strings = {"event-003", "event-004", "event-008"})
    void anthropicBusinessCase(String id) throws Exception { qualify(id, "anthropic"); }
    @Tag("cloud-openrouter") @ParameterizedTest @ValueSource(strings = {"event-005", "event-006"})
    void openrouterBusinessCase(String id) throws Exception { qualify(id, "openrouter"); }
    private void qualify(String id, String provider) throws Exception {
        var grant = Bootstrap.grant(); assertEquals(provider, grant.provider());
        if (provider.equals("openrouter")) {
            int delay = Integer.parseInt(FilesUtil.env("ORDER_OPENROUTER_CASE_DELAY_SECONDS", "0"));
            if (delay < 0 || delay > 60) throw new IllegalArgumentException("OpenRouter case delay must be 0–60 seconds.");
            // Pace independent cases to reduce upstream bursts; never retry a submitted turn.
            Thread.sleep(delay * 1000L);
        }
        var event = Fixtures.events(Path.of("/inputs")).stream().filter(e -> e.eventId().equals(id)).findFirst().orElseThrow();
        var expected = FilesUtil.read(Path.of("/oracle/expected.json")).path(id);
        Path directory = report().resolve(id);
        try (var inbox = new Inbox(directory, identity(grant))) {
            var worker = new Worker(inbox, grant);
            try {
                worker.consume(List.of(event, event));
                assertEquals("complete", inbox.get(id).state());
                assertEquals(1, inbox.packets().size());
                var packet = Json.MAPPER.readTree(inbox.packets().get(id));
                var answer = packet.path("answer");
                assertEquals(expected.path("route").asText(), answer.path("route").asText());
                assertEquals(expected.path("missing_evidence").asBoolean(), answer.path("missing_evidence").asBoolean());
                String explanation = answer.path("explanation").asText().toLowerCase(Locale.ROOT);
                for (var term : expected.path("required_terms")) assertTrue(explanation.contains(term.asText()), "Missing grounded term: " + term.asText());
                assertEquals("review_required", answer.path("disposition").asText());
                worker.consume(List.of(event));
                try (var client = Bootstrap.client(grant.token(), grant.uid())) {
                    var session = client.sessions.get(inbox.get(id).session());
                    assertEquals(1, session.turns().size(), "Duplicate delivery must not submit another turn");
                    assertEquals(grant.runbooks().get(id), session.runbookRef());
                }
                FilesUtil.save(directory.resolve("quality.json"), Map.of("case", id, "passed", true, "provider", provider, "model", grant.model(), "business_assertions", expected, "completion", packet.path("evidence").path("completion")));
            } catch (Exception | AssertionError e) {
                FilesUtil.save(directory.resolve("quality.json"), Map.of("case", id, "passed", false, "provider", provider, "model", grant.model(), "failure_type", e.getClass().getSimpleName())); throw e;
            } finally { worker.export(directory.resolve("packets")); }
        }
    }
}
