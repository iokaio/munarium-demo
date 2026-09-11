// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.model.SessionsApi.*;
import io.ioka.munarium.client.planes.Params;
import java.nio.file.*;
import java.util.*;
import java.util.regex.Pattern;

@SuppressWarnings("try")
public final class Workflow {
    private Workflow() {}
    public static JsonNode packet(Path work,String id,String revision,boolean recover,boolean crash,String endpoint) throws Exception {
        try(var lease=new Lease(work)) {
            Fixtures.verify(Path.of("/inputs"));Fixtures.require(Set.of("baseline","corrected").contains(revision),"Explicit baseline or corrected revision required");
            var grant=Bootstrap.grant();String name=Objects.requireNonNull(grant.runbooks().get(id));var record=Bootstrap.registry().path(name).path(revision);Fixtures.require(record.has("version"),"Selected revision has not been created");
            String key=FilesUtil.hash(grant.namespace()+grant.uid()+FilesUtil.json(record)+Files.readString(Path.of("/inputs/manifest.json")));
            Path journalPath=work.resolve("journal.json");ObjectNode journal;
            try(var reader=Bootstrap.reader();var api=Bootstrap.client(grant.token(),grant.uid(),endpoint)) {
                Bootstrap.assertFrozen(reader,record);
                if(Files.exists(journalPath)) {
                    journal=(ObjectNode)FilesUtil.read(journalPath);Fixtures.require(journal.path("key").asText().equals(key),"Changed investigation binding requires new work");
                    if(!journal.has("result")) {
                        Fixtures.require(recover && journal.has("session_id"),"Uncertain turn requires explicit transcript recovery; no replay");var session=api.sessions.get(journal.path("session_id").asText());
                        Fixtures.require(session.uid().equals(grant.uid()) && session.runbookRef().equals(record.path("runbook").asText()),"Transcript scope mismatch");
                        var turns=session.turns().stream().filter(t->t.query().equals(journal.path("query").asText()) && t.completion()!=null && !t.completion().isNull()).toList();Fixtures.require(turns.size()==1,"No uniquely recoverable completion");
                        var turn=turns.getFirst();var completion=(ObjectNode)turn.completion().deepCopy();completion.put("was_override",completion.path("resolved").path("was_override").asBoolean());
                        var result=new TurnResult(session.sessionId(),turn.ordinal(),turn.collectionsSearched(),null,Arrays.asList(Json.MAPPER.treeToValue(turn.hits(),TurnHit[].class)),Arrays.asList(Json.MAPPER.treeToValue(turn.envelope(),CollectionEnvelope[].class)),Json.MAPPER.treeToValue(completion,TurnCompletion.class),null);
                        journal.set("result",Json.MAPPER.valueToTree(result));journal.set("transcript_completion",turn.completion());journal.put("recovered",true);FilesUtil.save(journalPath,journal);
                    }
                } else {
                    journal=Json.MAPPER.createObjectNode();journal.put("key",key).put("query","Assemble the sampled inspection, narrative and required procedure for fictional "+id+". Preserve missing values and disagreement.").put("state","creating_session");journal.set("binding",record);FilesUtil.save(journalPath,journal);
                    journal.put("session_id",api.sessions.create(record.path("runbook").asText()).sessionId()).put("state","uncertain");FilesUtil.save(journalPath,journal);
                    var progress=journal.putArray("progress");
                    var result=api.sessions.turnStream(journal.path("session_id").asText(),Params.TurnOptions.of(journal.path("query").asText()).withCompletion(null),event->{progress.add(Json.MAPPER.valueToTree(event));try {FilesUtil.save(journalPath,journal);}catch(Exception e) {throw new IllegalStateException(e);}});
                    if(crash) Runtime.getRuntime().halt(71);journal.set("result",Json.MAPPER.valueToTree(result));FilesUtil.save(journalPath,journal);
                }
                Bootstrap.assertFrozen(reader,record);var facts=reader.query.facts(record.path("version").asText(),Params.FactsQuery.atSeq(record.path("pin").asLong()));
                var result=Json.MAPPER.treeToValue(journal.path("result"),TurnResult.class);var output=Json.MAPPER.createObjectNode();output.put("case_id",id).put("revision",revision).put("fictional",true).put("client_revision",Bootstrap.REVISION);output.set("binding",record);output.set("observations",Json.MAPPER.valueToTree(facts));output.set("evidence",journal.path("result"));output.put("recovered",journal.path("recovered").asBoolean());
                var errors=output.putArray("unresolved");ObjectNode answer=Json.MAPPER.createObjectNode();
                try {answer=validate(result,grant,name,id,facts);}
                catch(Exception e) {errors.add("Model evidence validation failed: "+e.getMessage());}
                if(result.hierarchy()==null) errors.add("Hierarchy decision is unavailable in the recovered transcript; completeness remains unresolved");
                else {
                    var layers=result.hierarchy().layers();if(layers.size()!=2 || !layers.get(0).layer().equals("observations") || !layers.get(0).block().equals("fact_slice") || !layers.get(1).block().equals("document_hits")) errors.add("Required fact or procedure evidence unavailable");
                }
                var observed=facts.facts().stream().filter(f->f.key().equals("defects")).findFirst();Integer defects=observed.map(f->Integer.valueOf(f.value())).orElse(null);Integer narrative=narrative(result.hits());
                if(defects==null) errors.add("Required inspection defect observation is missing");if(narrative==null) errors.add("Required narrative observation is missing");
                boolean conflict=defects!=null && narrative!=null && !defects.equals(narrative);output.put("disagreement",conflict);output.set("model_suggestion",answer);output.put("status",!errors.isEmpty()?"incomplete":conflict?"disagreement_requires_review":"complete_for_review");output.put("complete_lot_defect_count_available",false);
                output.put("supported_conclusion",conflict?"Recorded sample and narrative disagree; reconciliation is required":"Available observations are listed for review; this is not a lot-wide count");output.put("hypothesis","Root cause is not determined; further investigation is required");output.put("disposition","Reviewer decision required");
                FilesUtil.save(work.resolve("packet.json"),output);
                String text="# Fictional quality investigation | "+id+" | "+revision+"\n\nStatus: "+output.path("status").asText()+"\nLedger: "+record.path("version").asText()+" at pin "+record.path("pin").asLong()+"\nRunbook: "+record.path("runbook").asText()+"\n\nObservation: sampled defects = "+defects+"; narrative defects = "+narrative+"\nSupported conclusion: "+output.path("supported_conclusion").asText()+"\nHypothesis: "+output.path("hypothesis").asText()+"\nProcedure: "+answer.path("action").asText("unavailable")+"\nDisposition: reviewer decision required\nExact lot defect count: unavailable; procedure search cannot establish it\n\nCitations: "+answer.path("citations")+"\nUnresolved: "+errors+"\nSession: "+result.sessionId()+"\n";
                FilesUtil.write(work.resolve("packet.md"),text);journal.put("state",output.path("status").asText());FilesUtil.save(journalPath,journal);return output;
            }
        }
    }
    public static Integer narrative(List<TurnHit> hits) {for(var hit:hits) {var m=Pattern.compile("Narrative defects: ([0-9]+)").matcher(hit.text());if(m.find()) return Integer.valueOf(m.group(1));}return null;}
    public static ObjectNode validate(TurnResult result,Bootstrap.Grant grant,String collection,String id,Ledger.FactsPage facts) throws Exception {
        var c=result.completion();Fixtures.require(c!=null && c.provider().equals(grant.provider().equals("fixture")?"ollama":grant.provider()) && c.model().equals(grant.model()),"Unexpected model identity");Fixtures.require(c.verification()==null || c.verification().violations().isEmpty(),"Unresolved verification violations");
        String raw=c.text().trim().replaceFirst("^```(?:json)?\\s*","").replaceFirst("\\s*```$","");var parsed=Json.MAPPER.readTree(raw);Fixtures.require(parsed.isObject(),"Expected an object");var answer=(ObjectNode)parsed;
        var fields=new HashSet<String>();answer.fieldNames().forEachRemaining(fields::add);Fixtures.require(fields.equals(Set.of("observed_defects","narrative_defects","action","root_cause","hypothesis","citations")),"Unexpected answer schema");
        var expected=facts.facts().stream().filter(f->f.key().equals("defects")).findFirst();if(expected.isPresent()) Fixtures.require(answer.path("observed_defects").isIntegralNumber() && answer.path("observed_defects").asInt()==Integer.parseInt(expected.get().value()),"Inspection observation mismatch");else Fixtures.require(answer.path("observed_defects").isNull(),"Missing inspection was invented");
        Integer narrative=narrative(result.hits());Fixtures.require(narrative==null?answer.path("narrative_defects").isNull():answer.path("narrative_defects").isIntegralNumber() && answer.path("narrative_defects").asInt()==narrative,"Narrative observation mismatch");
        Fixtures.require(answer.path("root_cause").asText().equals("not determined") && answer.path("hypothesis").asText().equals("requires investigation") && answer.path("action").asText().equals("Hold the lot for quality review."),"Unsupported conclusion or disposition");
        var labels=new HashMap<String,TurnHit>();for(var hit:result.hits()) {Fixtures.require(hit.collection().equals(collection) && Set.of(collection+"/procedure.txt",collection+"/note.txt").contains(hit.sourcePath()),"Unexpected evidence scope");String file=hit.sourcePath().substring(collection.length()+1);Fixtures.require(hit.sourceContentHash().equals(FilesUtil.hash(Files.readString(Path.of("/inputs/"+id+"/"+file)))),"Source hash mismatch");labels.put("procedures/"+hit.chunkId(),hit);}
        Fixtures.require(answer.path("citations").isArray() && !answer.path("citations").isEmpty(),"Citations missing");var paths=new HashSet<String>();for(var citation:answer.path("citations")) {Fixtures.require(labels.containsKey(citation.asText()),"Unserved citation");paths.add(labels.get(citation.asText()).sourcePath());}Fixtures.require(paths.equals(Set.of(collection+"/procedure.txt",collection+"/note.txt")),"Both required document sources must be cited");return answer;
    }
}
