// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import org.junit.jupiter.api.*;
import java.nio.file.*;
import static org.junit.jupiter.api.Assertions.*;

@Tag("restarted")
class RestartTest {
    @Test void checkpointSurvivesServerRestartAndRecovers() throws Exception {
        var grant = Bootstrap.grant();
        Path directory = AcceptanceTest.report().getParent().resolve("restart");
        try (var inbox = new Inbox(directory, AcceptanceTest.identity(grant))) {
            assertEquals("uncertain", inbox.get("event-001").state());
            var worker = new Worker(inbox, grant);
            for (int attempt = 0; ; attempt++) {
                try { worker.reconcile(); break; }
                catch (io.ioka.munarium.client.errors.MunariumException e) { if (attempt == 29) throw e; Thread.sleep(1000); }
            }
            assertEquals("complete", inbox.get("event-001").state()); assertTrue(inbox.get("event-001").recovered());
            worker.export(directory.resolve("packets"));
            try (var client = Bootstrap.client(grant.token(), grant.uid())) { assertEquals(1, client.sessions.get(inbox.get("event-001").session()).turns().size()); }
        }
    }
}
