// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import static org.junit.jupiter.api.Assertions.*;
import java.nio.file.*;
import org.junit.jupiter.api.*;
@Tag("restarted")
public class RestartTest {
    @Test void recoverAfterRealServerRestart() throws Exception {Bootstrap.ready();var saved=FilesUtil.read(UnitTest.root().getParent().resolve("controlled/restart.json"));var packet=Workflow.packet(Path.of(saved.path("path").asText()),"case-001","baseline",true,false,Bootstrap.endpoint());assertTrue(packet.path("recovered").asBoolean());assertEquals("incomplete",packet.path("status").asText());assertEquals(saved.path("calls").asInt(),IntegrationTest.calls());}
}
