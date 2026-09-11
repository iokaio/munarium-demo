// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import java.nio.file.*;
import org.junit.jupiter.api.*;
import static org.junit.jupiter.api.Assertions.*;

@Tag("restarted")
class RestartTest {
    @Test void newProcessContinuesSavedVersionAfterServerRestart() throws Exception {
        Path path=Path.of(Files.readString(AcceptanceTest.report().getParent().resolve("restart-path.txt")));var importer=new LedgerImport(path);String version=importer.journal().path("version").asText();int count=IntegrationTest.calls();importer.run(false);assertEquals(version,importer.journal().path("version").asText());assertEquals("complete",importer.journal().path("status").asText());assertEquals(count,IntegrationTest.calls());
    }
}
