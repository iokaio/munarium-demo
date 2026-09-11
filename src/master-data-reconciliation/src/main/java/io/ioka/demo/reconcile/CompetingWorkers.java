// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;

import io.ioka.munarium.client.errors.HeadConflictException;
import io.ioka.munarium.client.model.Ledger;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Map;
import java.util.UUID;

/** A dedicated ledger version and a filesystem barrier shared by two containers. */
public final class CompetingWorkers {
    private CompetingWorkers() {}

    private static void await(Path path) throws Exception {
        long deadline = System.nanoTime() + 60_000_000_000L;
        while (!Files.exists(path)) {
            if (System.nanoTime() > deadline) throw new IllegalStateException("Worker barrier timed out");
            Thread.sleep(50);
        }
    }

    public static void run(String action, Path work) throws Exception {
        Files.createDirectories(work);
        try (var client = Bootstrap.ops(false)) {
            if (action.equals("init")) {
                if (Files.exists(work.resolve("version.json"))) throw new IllegalStateException("Fresh race state required");
                FilesUtil.write(work.resolve("version.json"), FilesUtil.json(Map.of("version", client.commands.createVersion())));
                return;
            }
            String version = FilesUtil.read(work.resolve("version.json")).path("version").asText();
            if (action.equals("verify")) {
                var a = FilesUtil.read(work.resolve("a-done.json"));
                var b = FilesUtil.read(work.resolve("b-done.json"));
                int conflicts = a.path("conflicts").asInt() + b.path("conflicts").asInt();
                if (conflicts != 1 || client.query.head(version) != 2 || !a.path("replay_stable").asBoolean() || !b.path("replay_stable").asBoolean()) {
                    throw new IllegalStateException("Expected one conflict, two writes, and stable completed replays");
                }
                FilesUtil.write(work.resolve("quality.json"), FilesUtil.json(Map.of("passed", true, "tests", 1, "conflicts", conflicts, "workers", 2, "version", version)));
                FilesUtil.write(work.resolve("tests.xml"), "<testsuite name=\"competing-worker-containers\" tests=\"1\" failures=\"0\" skipped=\"0\"><testcase name=\"fresh-head-and-key-after-conflict\"/></testsuite>");
                return;
            }
            if (!action.equals("a") && !action.equals("b")) throw new IllegalArgumentException("Unknown worker");
            String peer = action.equals("a") ? "b" : "a";
            long head = client.query.head(version);
            if (head != 0) throw new IllegalStateException("Fresh ledger required");
            FilesUtil.write(work.resolve(action + "-ready.json"), FilesUtil.json(Map.of("head", head)));
            await(work.resolve(peer + "-ready.json"));
            var claim = Ledger.ClaimInput.fact("worker_" + action, "review_status", "reviewed");
            var attempts = new ArrayList<Map<String, Object>>();
            int conflicts = 0;
            for (int attempt = 0; attempt < 2; attempt++) {
                String key = UUID.randomUUID().toString();
                attempts.add(Map.of("expected_head", head, "idempotency_key", key, "claim", claim));
                FilesUtil.write(work.resolve(action + "-journal.json"), FilesUtil.json(attempts));
                try {
                    var result = client.commands.proposeClaim(version, claim, head, key);
                    if (result.isDisputed()) throw new IllegalStateException("Unexpected disputed race claim");
                    FilesUtil.write(work.resolve(action + "-accepted.json"), FilesUtil.json(Map.of("claim_id", result.claim().id())));
                    await(work.resolve(peer + "-accepted.json"));
                    long completedHead = client.query.head(version);
                    var replay = client.commands.proposeClaim(version, claim, head, key);
                    if (!replay.claim().id().equals(result.claim().id()) || client.query.head(version) != completedHead) throw new IllegalStateException("Completed replay changed ledger");
                    FilesUtil.write(work.resolve(action + "-done.json"), FilesUtil.json(Map.of("conflicts", conflicts, "replay_stable", true, "attempts", attempts)));
                    return;
                } catch (HeadConflictException conflict) {
                    conflicts++;
                    head = client.query.head(version);
                    if (head != 1) throw new IllegalStateException("Unexpected competing head");
                }
            }
            throw new IllegalStateException("Worker did not finish within retry bound");
        }
    }
}
