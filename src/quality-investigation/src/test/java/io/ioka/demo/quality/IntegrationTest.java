// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import static org.junit.jupiter.api.Assertions.*;
import java.net.*;
import java.net.http.*;
import java.nio.file.*;
import java.util.*;
import io.ioka.munarium.client.errors.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.planes.Params;
import org.junit.jupiter.api.*;

@Tag("controlled")
@TestMethodOrder(MethodOrderer.OrderAnnotation.class)
public class IntegrationTest {
    static void get(String url) throws Exception {try(var http=HttpClient.newHttpClient()) {assertEquals(200,http.send(HttpRequest.newBuilder(URI.create(url)).build(),HttpResponse.BodyHandlers.discarding()).statusCode());}}
    static int calls() throws Exception {try(var http=HttpClient.newHttpClient()) {return Json.MAPPER.readTree(http.send(HttpRequest.newBuilder(URI.create("http://provider-fixture:11434/calls")).build(),HttpResponse.BodyHandlers.ofString()).body()).path("calls").asInt();}}
    @Test @Order(1) void queryCannotAdminister() throws Exception {var grant=Bootstrap.grant();try(var api=Bootstrap.client(grant.token(),grant.uid(),Bootstrap.endpoint())) {assertThrows(ForbiddenException.class,()->api.commands.createVersion(null,null,null));assertThrows(ForbiddenException.class,()->api.runbooks.applyShape(Files.readString(Path.of("/app/shapes/documents.yaml")),null));}}
    @Test @Order(2) void uncertainTurnRecoveredWithoutReplay() throws Exception {Path work=UnitTest.root().resolve("lost-turn");get("http://faults:11435/control/drop-turn");try {assertThrows(MunariumException.class,()->Workflow.packet(work,"case-001","baseline",false,false,"http://faults:11435"));}finally {get("http://faults:11435/control/normal");}int before=calls();assertThrows(IllegalStateException.class,()->Workflow.packet(work,"case-001","baseline",false,false,Bootstrap.endpoint()));var recovered=Workflow.packet(work,"case-001","baseline",true,false,Bootstrap.endpoint());assertEquals("incomplete",recovered.path("status").asText());assertTrue(recovered.path("recovered").asBoolean());assertFalse(recovered.path("evidence").has("hierarchy"));assertFalse(recovered.path("evidence").has("skipped"));assertEquals(before,calls());}
    @Test @Order(3) void providerOutageLeavesUncertain() throws Exception {Path work=UnitTest.root().resolve("outage");get("http://provider-fixture:11434/mode/unavailable");try {assertThrows(MunariumException.class,()->Workflow.packet(work,"case-001","baseline",false,false,Bootstrap.endpoint()));}finally {get("http://provider-fixture:11434/mode/ok");}int before=calls();assertThrows(IllegalStateException.class,()->Workflow.packet(work,"case-001","baseline",true,false,Bootstrap.endpoint()));assertEquals(before,calls());}
    @Test @Order(4) void unservedCitationPreventsCompletePacket() throws Exception {get("http://provider-fixture:11434/mode/bad-citation");try {var packet=Workflow.packet(UnitTest.root().resolve("invalid-citation"),"case-001","baseline",false,false,Bootstrap.endpoint());assertEquals("incomplete",packet.path("status").asText());}finally {get("http://provider-fixture:11434/mode/ok");}}
    @Test @Order(5) void deniedCollectionRefusesRequiredEvidence() throws Exception {
        String name="quality-denied-"+UUID.randomUUID().toString().substring(0,8);String version;try(var api=Bootstrap.ops(false)) {
            version=api.commands.createVersion(null,null,null);api.commands.proposeClaim(version,Ledger.ClaimInput.fact("lot_999","sample_size","40"),0L,null);
            String book=Files.readString(Path.of("/app/runbooks/investigation.yaml")).replace("__NAME__",name).replace("__REVISION__","1").replace("__VERSION__",version).replace("__PROVIDER__",Bootstrap.grant().config()).replace("accessLevel: 0","accessLevel: 2");api.runbooks.applyRunbook(book);
            api.ingest.ingest(Ingesting.IngestFile.ofText(name+"/procedure.txt","text/plain","RESTRICTED_QUALITY_SENTINEL"));var run=api.runbooks.runRunbook(name,null);var pending=api.runbooks.getRun(run.runId()).steps().stream().filter(s->s.state().equals("awaiting_approval")).toList();assertEquals(1,pending.size());api.runbooks.approveStep(run.runId(),pending.getFirst().ordinal());
        }
        try(var issuer=Bootstrap.ops(true)) {var token=issuer.tokens.mint(new Tokens.IssueTokenRequest("denied-reviewer",0,List.of(),List.of("query"),List.of(name),3600L));try(var api=Bootstrap.client(token.token(),"denied-reviewer",Bootstrap.endpoint())) {var error=assertThrows(MunariumException.class,()->{var session=api.sessions.create(name+"@1");api.sessions.turn(session.sessionId(),Params.TurnOptions.of("Show quality observations").withCompletion(null));});assertFalse(error.getMessage().contains("RESTRICTED_QUALITY_SENTINEL"));}}
    }
    @Test @Order(6) void expiredQueryCapabilityFails() throws Exception {var grant=Bootstrap.grant();try(var issuer=Bootstrap.ops(true)) {var token=issuer.tokens.mint(new Tokens.IssueTokenRequest("expired-reviewer",0,List.of(),List.of("query"),new ArrayList<>(grant.runbooks().values()),1L));Thread.sleep(33000);try(var api=Bootstrap.client(token.token(),"expired-reviewer",Bootstrap.endpoint())) {assertThrows(UnauthenticatedException.class,()->api.sessions.create(grant.runbooks().get("case-001")+"@1"));}}}
    @Test @Order(7) void alteredFrozenVersionRefused() throws Exception {
        // A new private registry entry leaves the eight business scenarios intact.
        try(var api=Bootstrap.ops(false)) {String version=api.commands.createVersion(null,null,null);api.commands.proposeClaim(version,Ledger.ClaimInput.fact("lot_999","sample_size","40"),0L,null);var record=Json.MAPPER.createObjectNode().put("version",version).put("pin",1).put("state","frozen");Bootstrap.assertFrozen(api,record);api.commands.proposeClaim(version,Ledger.ClaimInput.fact("lot_999","defects","1"),1L,null);assertThrows(IllegalStateException.class,()->Bootstrap.assertFrozen(api,record));}
    }
    @Test @Order(8) void prepareCrashForServerRestart() throws Exception {Path work=UnitTest.root().resolve("restart");assertEquals(71,UnitTest.child("crash",work.toString(),"case-001","baseline"));FilesUtil.save(UnitTest.root().resolve("restart.json"),Map.of("path",work.toString(),"calls",calls()));}
}
