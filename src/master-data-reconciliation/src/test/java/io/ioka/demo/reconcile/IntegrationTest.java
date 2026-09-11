// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.errors.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.planes.Params;
import java.net.*;
import java.net.http.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import org.junit.jupiter.api.*;
import static org.junit.jupiter.api.Assertions.*;

@Tag("controlled")
class IntegrationTest {
    static void control(String host,String action) throws Exception {assertEquals(200,HttpClient.newHttpClient().send(HttpRequest.newBuilder(URI.create("http://"+host+"/"+action)).build(),HttpResponse.BodyHandlers.discarding()).statusCode());}
    static int calls() throws Exception {return Json.MAPPER.readTree(HttpClient.newHttpClient().send(HttpRequest.newBuilder(URI.create("http://provider-fixture:11434/calls")).build(),HttpResponse.BodyHandlers.ofString()).body()).path("calls").asInt();}
    static Fixtures.Row row() throws Exception {return Fixtures.rows(Path.of("/inputs")).getFirst();}
    Path work(String name) {return AcceptanceTest.report().resolve(name);}
    Path reviewed(String name) throws Exception {Path path=work(name);new Workflow(path,Bootstrap.grant()).prepare(row());Workflow.review(path,true,"test-steward","Reviewed against independent fixture");return path;}
    @Test void noLedgerImportBeforeReviewAndNoChangedDraftImport() throws Exception {
        Path path=work("review-boundary");new Workflow(path,Bootstrap.grant()).prepare(row());assertThrows(NoSuchFileException.class,()->new LedgerImport(path).run(false));assertFalse(Files.exists(path.resolve("ledger-journal.json")));
        Workflow.review(path,true,"steward","reviewed");Files.writeString(path.resolve("draft.json"),Files.readString(path.resolve("draft.json"))+" ");assertThrows(IllegalArgumentException.class,()->new LedgerImport(path).run(false));
    }
    @Test void queryCapabilityCannotWriteLedgerOrOpenOtherScope() throws Exception {
        var g=Bootstrap.grant();try(var issuer=Bootstrap.ops(true);var writer=Bootstrap.ops(false)) {String version=writer.commands.createVersion();var token=issuer.tokens.mint(new Tokens.IssueTokenRequest(g.uid(),0,List.of(),List.of("query"),List.of(g.runbooks().get("case-001").split("@")[0]),300L));try(var c=Bootstrap.client(token.token(),g.uid())) {assertThrows(ForbiddenException.class,()->c.sessions.create(g.runbooks().get("case-002")));assertThrows(ForbiddenException.class,()->c.commands.proposeClaim(version,Ledger.ClaimInput.fact("supplier_001","currency","USD"),null,null));}}
    }
    @Test void expiredCapabilityCanBeRenewedWithoutChangingUid() throws Exception {
        var g=Bootstrap.grant();try(var issuer=Bootstrap.ops(true)) {var token=issuer.tokens.mint(new Tokens.IssueTokenRequest(g.uid(),0,List.of(),List.of("query"),List.of(g.runbooks().get("case-001").split("@")[0]),1L));Thread.sleep(33_000);int before=calls();try(var c=Bootstrap.client(token.token(),g.uid())) {assertThrows(UnauthenticatedException.class,()->c.sessions.create(g.runbooks().get("case-001")));}try(var c=Bootstrap.client(g.token(),g.uid())) {assertNotNull(c.sessions.create(g.runbooks().get("case-001")));}assertEquals(before,calls());}
    }
    @Test void providerOutageDoesNotAutomaticallyRepeatTurn() throws Exception {
        Path path=work("provider-outage");var app=new Workflow(path,Bootstrap.grant());control("provider-fixture:11434","fail");
        try {assertThrows(Exception.class,()->app.prepare(row()));int count=calls();control("provider-fixture:11434","reset");app.prepare(row());app.recover();assertEquals(count,calls());assertEquals("uncertain",app.journal().path("status").asText());assertFalse(Files.exists(path.resolve("draft.json")));} finally {control("provider-fixture:11434","reset");}
    }
    @Test void lostTurnResponseRecoversOneScopedTranscriptWithoutPaymentReplay() throws Exception {
        Path path=work("lost-turn");control("faults:11435","control/drop-turn");try {assertThrows(Exception.class,()->new Workflow(path,Bootstrap.grant(),"http://faults:11435").prepare(row()));}finally {control("faults:11435","control/normal");}
        int count=calls();new Workflow(path,Bootstrap.grant()).recover();assertEquals(count,calls());assertTrue(FilesUtil.read(path.resolve("draft.json")).path("recovered").asBoolean());
    }
    @Test void lostClaimResponseIsReconciledByRecordedCommandEvidence() throws Exception {
        Path path=reviewed("lost-claim");control("faults:11435","control/drop-claim");try {assertThrows(MunariumException.class,()->new LedgerImport(path,"http://faults:11435",(v,h)->{}).run(false));}finally {control("faults:11435","control/normal");}
        var importer=new LedgerImport(path);assertThrows(IllegalStateException.class,()->importer.run(false));importer.reconcile("baseline");importer.run(false);assertTrue(importer.journal().path("steps").path("baseline").get(0).path("recovered").asBoolean());
    }
    @Test void headConflictRebuildsWithFreshKeyAndCompletedReplayKeepsOriginalBody() throws Exception {
        Path path=reviewed("head-conflict");var injected=new AtomicBoolean();
        var importer=new LedgerImport(path,Bootstrap.endpoint(),(version,head)->{if(injected.compareAndSet(false,true))try(var c=Bootstrap.ops(false)) {c.commands.proposeClaim(version,Ledger.ClaimInput.fact("concurrent_event","note","review started"),head,null);}});
        importer.run(false);var attempts=importer.journal().path("steps").path("baseline");assertEquals(2,attempts.size());assertEquals("head_conflict",attempts.get(0).path("status").asText());assertNotEquals(attempts.get(0).path("idempotency_key"),attempts.get(1).path("idempotency_key"));assertTrue(attempts.get(1).path("expected_head").asLong()>attempts.get(0).path("expected_head").asLong());
        try(var c=Bootstrap.ops(false)) {long head=c.query.head(importer.journal().path("version").asText());var replay=importer.replayCompleted("baseline");assertEquals(attempts.get(1).path("response").path("claim").path("id").asText(),replay.claim().id());assertEquals(head,c.query.head(importer.journal().path("version").asText()));}
    }
    @Test void officialClientWriteLoopRebuildsAgainstConcurrentHead() {
        try(var c=Bootstrap.ops(false)) {String version=c.commands.createVersion();var heads=new ArrayList<Long>();var result=c.proposeClaimWithRetry(version,head->{heads.add(head);if(heads.size()==1)c.commands.proposeClaim(version,Ledger.ClaimInput.fact("counter","note","concurrent"),head,null);return Ledger.ClaimInput.fact("supplier_001","currency","USD");});assertFalse(result.isDisputed());assertEquals(List.of(0L,1L),heads);}
    }
    @Test void processCrashAndServerRestartKeepCompletedWrites() throws Exception {
        Path path=reviewed("restart");var command="/app/build/install/master-data-reconciliation/bin/master-data-reconciliation";
        var child=new ProcessBuilder(command,"import",path.toString(),"--crash-after-dispute").redirectErrorStream(true).redirectOutput(path.resolve("child.log").toFile()).start();assertTrue(child.waitFor(30,TimeUnit.SECONDS));assertEquals(71,child.exitValue());assertEquals("awaiting_correction",new LedgerImport(path).journal().path("status").asText());
        FilesUtil.write(AcceptanceTest.report().getParent().resolve("restart-path.txt"),path.toString());
    }
    @Test void invalidShapeIsRecordedDisputedAndHistoryStillShowsPriorValue() throws Exception {
        Path path=reviewed("typed-shape");var importer=new LedgerImport(path);importer.run(false);var j=importer.journal();
        try(var c=Bootstrap.ops(false)) {
            var old=c.query.getClaim(j.path("steps").path("baseline").get(0).path("response").path("claim").path("id").asText());assertTrue(old.superseded());assertEquals(row().a(),old.claim().value());
            var bad=new Ledger.ClaimInput("invalid_supplier","currency","USD","fact",null,null,null,null,null,null,"reconcile-record@1",null);
            var outcome=c.commands.proposeClaim(j.path("version").asText(),bad,null,null);assertTrue(outcome.isDisputed());assertTrue(outcome.findings().stream().anyMatch(f->f.ruleId().equals("shape.schema-violation") && f.severity().equals("block")));
            assertTrue(c.query.getClaim(outcome.claim().id()).claim().isDisputed());assertTrue(c.query.facts(j.path("version").asText(),new Params.FactsQuery(null,null,List.of("accepted"),100)).facts().stream().noneMatch(f->f.id().equals(outcome.claim().id())));
        }
    }
}
