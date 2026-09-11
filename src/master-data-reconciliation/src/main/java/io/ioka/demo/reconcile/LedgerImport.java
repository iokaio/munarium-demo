// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;

import com.fasterxml.jackson.databind.node.*;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.errors.HeadConflictException;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.planes.Params;
import java.nio.file.*;
import java.util.*;
import java.util.function.BiConsumer;

/** Trusted writer: append-only review imports with completed-command replay and explicit uncertainty. */
@SuppressWarnings("try")
public final class LedgerImport {
    private final Path directory;
    private final String endpoint;
    private final BiConsumer<String,Long> beforeDispatch;
    public LedgerImport(Path directory) { this(directory,Bootstrap.endpoint(),(version,head)->{}); }
    public LedgerImport(Path directory,String endpoint,BiConsumer<String,Long> beforeDispatch) { this.directory=directory; this.endpoint=endpoint; this.beforeDispatch=beforeDispatch; }
    public ObjectNode journal() throws Exception { return (ObjectNode)FilesUtil.read(directory.resolve("ledger-journal.json")); }
    private void save(ObjectNode j) throws Exception { FilesUtil.save(directory.resolve("ledger-journal.json"),j); }
    private MunariumClient writer() {
        String token=System.getenv("MUNARIUM_TOKEN"); if(token==null) throw new IllegalStateException("Trusted write credential absent");
        return MunariumClient.rest(MunariumClientOptions.of(endpoint).withToken(token).withUid("reconcile-writer").withReadRetries(0));
    }
    public void run(boolean stopAfterDispute) throws Exception {
        try(var lease=new Lease(directory);var c=writer()) {
            var packet=FilesUtil.read(directory.resolve("draft.json")); var review=FilesUtil.read(directory.resolve("review.json"));
            if(!review.path("draft_hash").asText().equals(FilesUtil.hash(Files.readString(directory.resolve("draft.json")))) || !review.path("input_hash").equals(packet.path("input_hash"))) throw new IllegalArgumentException("Review does not match this exact draft and input");
            Fixtures.Row row=Json.MAPPER.treeToValue(packet.path("row"),Fixtures.Row.class);
            ObjectNode j;
            if(Files.exists(directory.resolve("ledger-journal.json"))) j=journal();
            else { j=Json.MAPPER.createObjectNode();j.set("row",packet.path("row"));j.set("review",review);j.put("input_hash",packet.path("input_hash").asText());j.set("steps",Json.MAPPER.createObjectNode());save(j); }
            if(!j.path("review").equals(review) || !j.path("input_hash").equals(packet.path("input_hash"))) throw new IllegalArgumentException("Import intent changed");
            if(!j.has("version")) {
                if(j.has("version_intent")) throw new IllegalStateException("Version creation is uncertain; inspect Server, do not automatically replay");
                var intent=Json.MAPPER.createObjectNode();intent.put("key",UUID.randomUUID().toString());intent.set("metadata",Json.MAPPER.valueToTree(Map.of("application","master-data-reconciliation","input_hash",packet.path("input_hash").asText())));j.set("version_intent",intent);save(j);
                String version=c.commands.createVersion(null,intent.path("metadata"),intent.path("key").asText());j.put("version",version);save(j);
            }
            var baseline=claim(c,j,"baseline",row,"fact",row.a(),null);
            if(baseline.isDisputed()) throw new IllegalStateException("Baseline disputed; steward intervention required");
            j.put("historical_pin",baseline.headSeq());save(j);
            var disputed=claim(c,j,"conflicting_export",row,"fact",row.b(),null);
            if(!disputed.isDisputed() || disputed.findings().stream().noneMatch(f->f.severity().equals("block"))) throw new IllegalStateException("Expected conflict was not recorded disputed with a block finding");
            j.put("status","awaiting_correction");save(j);
            if(stopAfterDispute) { report(c,j);return; }
            if(review.path("approved").asBoolean()) {
                var corrected=claim(c,j,"correction",row,"correction",row.b(),baseline.claim().id());
                if(corrected.isDisputed()) throw new IllegalStateException("Correction disputed; steward intervention required");
            }
            j.put("status","complete");save(j);report(c,j);
        }
    }
    private Ledger.ClaimOutcome claim(MunariumClient c,ObjectNode j,String step,Fixtures.Row row,String kind,String value,String supersedes) throws Exception {
        ObjectNode steps=(ObjectNode)j.path("steps");ArrayNode attempts=steps.has(step)?(ArrayNode)steps.path(step):steps.putArray(step);
        if(!attempts.isEmpty()) {
            var last=attempts.get(attempts.size()-1);
            if(last.path("status").asText().equals("complete")) return Json.MAPPER.treeToValue(last.path("response"),Ledger.ClaimOutcome.class);
            if(last.path("status").asText().equals("uncertain")) throw new IllegalStateException("Command outcome uncertain; reconcile before continuing");
        }
        for(int n=0;n<3;n++) {
            String version=j.path("version").asText();long head=c.query.head(version);
            if(kind.equals("correction")) {
                var current=c.query.facts(version,new Params.FactsQuery(null,head,List.of("accepted"),100)).facts().stream().filter(f->f.subject().equals(row.subject()) && f.key().equals(row.field())).toList();
                if(current.size()!=1 || !current.getFirst().id().equals(supersedes) || !current.getFirst().value().equals(row.a())) throw new IllegalStateException("Current fact changed since review; obtain a new review");
            }
            String key=UUID.randomUUID().toString();
            var evidence=Json.MAPPER.valueToTree(Map.of("reconciliation_command",key,"input_hash",j.path("input_hash").asText()));
            var input=new Ledger.ClaimInput(row.subject(),row.field(),value,kind,"stewardship",kind.equals("correction")?"repaired":"backfilled",supersedes,null,evidence,null,"reconcile-record@1",null);
            var attempt=attempts.addObject();attempt.put("idempotency_key",key).put("expected_head",head).put("status","uncertain");attempt.set("body",Json.MAPPER.valueToTree(input));save(j);
            beforeDispatch.accept(version,head);
            try {
                var outcome=c.commands.proposeClaim(version,input,head,key);
                attempt.set("response",Json.MAPPER.valueToTree(outcome));attempt.put("status","complete");save(j);return outcome;
            } catch(HeadConflictException conflict) {
                attempt.put("status","head_conflict").put("actual_head",conflict.actual());save(j);
                // Only an explicit head conflict is rebuilt. The next attempt re-reads facts/head and saves a fresh key.
            }
        }
        throw new IllegalStateException("Head conflict limit reached; review current state before resuming");
    }
    public void reconcile(String step) throws Exception {
        try(var lease=new Lease(directory);var c=writer()) {
            var j=journal();var attempts=(ArrayNode)j.path("steps").path(step);
            if(attempts==null || attempts.isEmpty()) throw new IllegalArgumentException("No submitted command");
            var last=(ObjectNode)attempts.get(attempts.size()-1);
            if(!last.path("status").asText().equals("uncertain")) return;
            var body=Json.MAPPER.treeToValue(last.path("body"),Ledger.ClaimInput.class);String version=j.path("version").asText();
            var found=c.query.facts(version,new Params.FactsQuery(null,null,List.of("accepted","disputed"),1000)).facts().stream().filter(f->Objects.equals(f.evidence(),body.evidence()) && Objects.equals(f.provenance(),body.provenance()) && Objects.equals(f.shapeRef(),body.shapeRef()) && Objects.equals(f.scopePath(),body.scopePath()) && f.origin()==null && f.subject().equals(body.subject()) && f.key().equals(body.key()) && f.value().equals(body.value()) && f.claimType().equals(body.claimType()) && Objects.equals(f.supersedesId(),body.supersedesId())).toList();
            if(found.size()!=1) return;
            var recorded=found.getFirst();var findings=c.query.findings(version,Params.FindingsQuery.all()).stream().filter(f->f.seq()==recorded.seq()).map(Ledger.StoredFinding::finding).toList();
            last.set("response",Json.MAPPER.valueToTree(new Ledger.ClaimOutcome(recorded,findings,recorded.seq())));last.put("status","complete").put("recovered",true);save(j);
        }
    }
    public Ledger.ClaimOutcome replayCompleted(String step) throws Exception {
        try(var lease=new Lease(directory);var c=writer()) {
            var j=journal();var a=j.path("steps").path(step);var last=a.get(a.size()-1);
            if(!last.path("status").asText().equals("complete")) throw new IllegalStateException("Only confirmed completed commands may replay");
            return c.commands.proposeClaim(j.path("version").asText(),Json.MAPPER.treeToValue(last.path("body"),Ledger.ClaimInput.class),last.path("expected_head").asLong(),last.path("idempotency_key").asText());
        }
    }
    private void report(MunariumClient c,ObjectNode j) throws Exception {
        String version=j.path("version").asText();long pin=j.path("historical_pin").asLong();
        var report=new TreeMap<String,Object>();report.put("version",version);report.put("historical_pin",pin);report.put("historical",c.query.facts(version,new Params.FactsQuery(null,pin,List.of("accepted"),100)));report.put("accepted",c.query.facts(version,new Params.FactsQuery(null,null,List.of("accepted"),100)));report.put("disputed",c.query.facts(version,new Params.FactsQuery(null,null,List.of("disputed"),100)));report.put("findings",c.query.findings(version,Params.FindingsQuery.all()));report.put("status",j.path("status").asText());report.put("review",j.path("review"));
        FilesUtil.save(directory.resolve("ledger-report.json"),report);
        var current=((Ledger.FactsPage)report.get("accepted")).facts().stream().map(f->f.subject()+" / "+f.key()+": "+f.value()).toList();
        FilesUtil.write(directory.resolve("status.txt"),"MASTER-DATA RECONCILIATION | synthetic stewardship\nSupplier: "+j.path("row").path("subject").asText()+" / "+j.path("row").path("field").asText()+"\nState: "+j.path("status").asText()+"\nVersion: "+version+"\nHistorical pin: "+pin+"\nReviewed correction: "+j.path("review").path("approved").asBoolean()+"\nAccepted now: "+String.join("; ",current)+"\nPrior accepted value: "+j.path("row").path("a").asText()+"\nRetained disputed claims: "+((Ledger.FactsPage)report.get("disputed")).facts().size()+"\nBusiness systems remain authoritative. See ledger-report.json for claim IDs and findings.\n");
    }
}
