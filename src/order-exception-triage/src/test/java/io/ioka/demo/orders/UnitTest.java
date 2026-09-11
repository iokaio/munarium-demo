// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.*;
import java.util.*;
import static org.junit.jupiter.api.Assertions.*;

@Tag("unit")
class UnitTest {
    @TempDir Path directory;
    @org.junit.jupiter.params.ParameterizedTest @org.junit.jupiter.params.provider.ValueSource(strings = {"default", "heldout", "stress"})
    void separateJvmProcessesGenerateIdenticalBytes(String profile) throws Exception {
        for (String name : List.of("first", "second")) {
            var process = new ProcessBuilder("java", "-cp", "build/install/order-exception-triage/lib/*", Fixtures.class.getName(), directory.resolve(name).toString(), directory.resolve(name + "-oracle").toString(), profile).redirectErrorStream(true).redirectOutput(directory.resolve(name + ".log").toFile()).start();
            if (!process.waitFor(30, java.util.concurrent.TimeUnit.SECONDS)) { process.destroyForcibly(); fail("Fixture subprocess timed out"); }
            assertEquals(0, process.exitValue(), Files.readString(directory.resolve(name + ".log")));
        }
        try (var paths = Files.walk(directory.resolve("first"))) {
            for (var path : paths.filter(Files::isRegularFile).toList()) assertArrayEquals(Files.readAllBytes(path), Files.readAllBytes(directory.resolve("second").resolve(directory.resolve("first").relativize(path))));
        }
        assertEquals(Files.readString(directory.resolve("first-oracle/expected.json")), Files.readString(directory.resolve("second-oracle/expected.json")));
        assertEquals(profile.equals("stress") ? 80 : 8, Fixtures.events(directory.resolve("first")).size());
    }
    @Test void fixturesAreReproducibleAndOracleIsSeparate() throws Exception {
        for (String name : List.of("a", "b")) Fixtures.generate(directory.resolve(name), directory.resolve(name + "-oracle"), 41017);
        assertEquals(Files.readString(directory.resolve("a/manifest.json")), Files.readString(directory.resolve("b/manifest.json")));
        assertEquals(8, Fixtures.events(directory.resolve("a")).size());
        assertFalse(Files.exists(directory.resolve("a/expected.json")));
        assertEquals(8, FilesUtil.read(directory.resolve("a-oracle/expected.json")).size());
    }
    @Test void fixtureTamperingIsRejected() throws Exception {
        Fixtures.generate(directory.resolve("inputs"), directory.resolve("oracle"), 1);
        Files.writeString(directory.resolve("inputs/documents/event-001.txt"), "changed");
        assertThrows(IllegalStateException.class, () -> Fixtures.verify(directory.resolve("inputs")));
    }
    @Test void seedsChangeInputs() throws Exception {
        Fixtures.generate(directory.resolve("a"), directory.resolve("oa"), 1); Fixtures.generate(directory.resolve("b"), directory.resolve("ob"), 2);
        assertNotEquals(Files.readString(directory.resolve("a/events/event-001.json")), Files.readString(directory.resolve("b/events/event-001.json")));
    }
    @Test void malformedInputAndPathTraversalAreRejected() {
        assertThrows(IllegalArgumentException.class, () -> new OrderEvent("../../escape", "order-1", "stock", 1, 0, false, true));
        assertThrows(IllegalArgumentException.class, () -> new OrderEvent("event-1", "order-1", "stock", -1, 0, false, true));
    }
    @Test void providerAssignmentsCoverCorpusWithTwoOrMoreEach() {
        var all = new HashSet<String>();
        for (String p : List.of("openai", "anthropic", "openrouter")) { assertTrue(Fixtures.assignments(p).size() >= 2); for (var id : Fixtures.assignments(p)) assertTrue(all.add(id)); }
        assertEquals(new HashSet<>(Fixtures.assignments("fixture")), all);
        assertThrows(IllegalArgumentException.class, () -> Fixtures.assignments("ollama"));
    }
    @Test void duplicateDeliveryAndChangedInput() throws Exception {
        try (var inbox = new Inbox(directory, "identity")) {
            var event = event(); assertTrue(inbox.accept(event)); assertFalse(inbox.accept(event));
            assertThrows(IllegalArgumentException.class, () -> inbox.accept(new OrderEvent(event.eventId(), event.orderId(), event.reason(), 12, 0, false, true)));
            assertEquals(1, inbox.rows().size());
        }
    }
    @Test void pendingIntentSurvivesReopenAndCannotBeClaimedAgain() throws Exception {
        try (var inbox = new Inbox(directory, "identity")) { inbox.accept(event()); assertTrue(inbox.claim("event-1", "session", "query")); }
        try (var inbox = new Inbox(directory, "identity")) {
            assertEquals("uncertain", inbox.get("event-1").state()); assertEquals("session", inbox.get("event-1").session());
            assertFalse(inbox.claim("event-1", "second-session", "query"));
        }
    }
    @Test void outboxAndCompletionSurviveReopen() throws Exception {
        try (var inbox = new Inbox(directory, "identity")) { inbox.accept(event()); inbox.finish("event-1", "packet"); }
        try (var inbox = new Inbox(directory, "identity")) { assertEquals("complete", inbox.get("event-1").state()); assertEquals(Map.of("event-1", "packet"), inbox.packets()); }
        assertThrows(IllegalArgumentException.class, () -> new Inbox(directory, "another identity"));
    }
    private static OrderEvent event() { return new OrderEvent("event-1", "order-1", "stock_shortage", 10, 0, false, true); }
}
