// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;

import com.fasterxml.jackson.databind.*;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.model.SessionsApi.*;
import io.ioka.munarium.client.planes.Params;
import java.nio.file.*;
import java.util.*;

@SuppressWarnings("try")
public final class Workflow {
    private final Path directory;
    private final Bootstrap.Grant grant;
    private final String endpoint;
    public Workflow(Path directory, Bootstrap.Grant grant) { this(directory, grant, Bootstrap.endpoint()); }
    public Workflow(Path directory, Bootstrap.Grant grant, String endpoint) { this.directory = directory; this.grant = grant; this.endpoint = endpoint; }
    public ObjectNode journal() throws Exception { return (ObjectNode) FilesUtil.read(directory.resolve("draft-journal.json")); }
    private void save(ObjectNode journal) throws Exception { FilesUtil.save(directory.resolve("draft-journal.json"), journal); }
    private MunariumClient client() { return MunariumClient.rest(MunariumClientOptions.of(endpoint).withToken(grant.token()).withUid(grant.uid()).withReadRetries(0)); }
    public void prepare(Fixtures.Row row) throws Exception {
        try (var lease = new Lease(directory)) {
            String hash = FilesUtil.hash(FilesUtil.json(row));
            if (Files.exists(directory.resolve("draft-journal.json"))) {
                var saved = journal();
                if (!saved.path("input_hash").asText().equals(hash) || !saved.path("configuration").asText().equals(grant.namespace())) throw new IllegalArgumentException("Input/configuration changed; use a new work directory");
                if (saved.has("result")) finish(saved);
                return;
            }
            try (var c = client()) {
                String session = c.sessions.create(grant.runbooks().get(row.id())).sessionId();
                String query = "Explain the stewardship recommendation for " + row.field() + ". Export rows: " + FilesUtil.json(row);
                var state = Json.MAPPER.createObjectNode(); state.put("input_hash",hash).put("configuration",grant.namespace()).put("session_id",session).put("query",query).put("status","uncertain"); state.set("row",Json.MAPPER.valueToTree(row)); save(state);
                var progress = new ArrayList<TurnProgress>();
                var result = c.sessions.turnStream(session, Params.TurnOptions.of(query).withCompletion(null), progress::add);
                state.set("result",Json.MAPPER.valueToTree(result)); state.set("progress",Json.MAPPER.valueToTree(progress)); state.put("status","unverified"); save(state); finish(state);
            }
        }
    }
    private void finish(ObjectNode state) throws Exception {
        String id = state.path("row").path("id").asText();
        TurnResult result = Json.MAPPER.treeToValue(state.path("result"),TurnResult.class);
        JsonNode answer = validate(result,grant,id);
        var packet = Json.MAPPER.createObjectNode(); packet.set("row",state.path("row")); packet.set("answer",answer); packet.set("evidence",state.path("result"));
        packet.put("input_hash",state.path("input_hash").asText()).put("session_id",state.path("session_id").asText()).put("status","review_required").put("client_revision",Bootstrap.REVISION).put("recovered",state.path("recovered").asBoolean());
        FilesUtil.save(directory.resolve("draft.json"),packet);
        FilesUtil.write(directory.resolve("draft.md"),"# Synthetic stewardship draft: " + id + "\n\nReview required. Recommendation: " + answer.path("recommendation").asText() + "\n\n" + answer.path("explanation").asText() + "\n\nCitations: " + answer.path("citations") + "\n\nNo business system has been updated. Ledger acceptance does not establish factual correctness.\n");
        state.put("status","review_required"); save(state);
    }
    public void recover() throws Exception {
        try (var lease = new Lease(directory); var c = client()) {
            var state = journal(); if (state.has("result")) { finish(state); return; }
            var session = c.sessions.get(state.path("session_id").asText());
            String id = state.path("row").path("id").asText();
            if (!session.uid().equals(grant.uid()) || !session.runbookRef().equals(grant.runbooks().get(id))) throw new IllegalStateException("Transcript scope mismatch");
            var turns = session.turns().stream().filter(t -> t.query().equals(state.path("query").asText()) && t.completion()!=null && !t.completion().isNull()).toList();
            if (turns.size()!=1 || session.turns().size()!=1) return;
            var turn = turns.getFirst(); var completion = (ObjectNode) turn.completion().deepCopy();
            if (!completion.path("resolved").path("provider").asText().equals(grant.config())) throw new IllegalStateException("Recovered provider mismatch");
            completion.put("was_override",completion.path("resolved").path("was_override").asBoolean());
            var result = new TurnResult(session.sessionId(),turn.ordinal(),turn.collectionsSearched(),null,Arrays.asList(Json.MAPPER.treeToValue(turn.hits(),TurnHit[].class)),turn.envelope()==null || turn.envelope().isNull()?List.of():Arrays.asList(Json.MAPPER.treeToValue(turn.envelope(),CollectionEnvelope[].class)),Json.MAPPER.treeToValue(completion,TurnCompletion.class),null);
            state.set("result",Json.MAPPER.valueToTree(result)); state.set("transcript_completion",turn.completion()); state.put("recovered",true); save(state); finish(state);
        }
    }
    public static JsonNode validate(TurnResult result, Bootstrap.Grant grant, String id) throws Exception {
        var c=result.completion();
        if (c==null || !c.provider().equals(grant.provider().equals("fixture")?"ollama":grant.provider()) || !c.model().equals(grant.model())) throw new IllegalStateException("Unexpected completion identity");
        if (c.verification()!=null && !c.verification().violations().isEmpty()) throw new IllegalStateException("Unresolved verification violations");
        String text=c.text().trim().replaceFirst("^```(?:json)?\\s*","").replaceFirst("\\s*```$",""); var answer=Json.MAPPER.readTree(text);
        if (!Set.of("prefer_b","retain_a","review_ambiguous").contains(answer.path("recommendation").asText()) || !answer.path("disposition").asText().equals("review_required") || answer.path("explanation").asText().isBlank()) throw new IllegalStateException("Invalid stewardship draft");
        String collection=grant.runbooks().get(id).split("@")[0]; var labels=new HashSet<String>();
        if (result.hits()==null || result.hits().isEmpty() || !List.of(collection).equals(result.collectionsSearched())) throw new IllegalStateException("Missing or unscoped evidence");
        for (var hit:result.hits()) {
            if (!collection.equals(hit.collection()) || !hit.sourcePath().equals(collection+"/procedure.txt") || hit.sourceContentHash()==null || hit.sourceContentHash().isBlank()) throw new IllegalStateException("Unidentified evidence");
            labels.add(collection+"/"+hit.chunkId());
        }
        var citations=answer.path("citations"); if (!citations.isArray() || citations.isEmpty()) throw new IllegalStateException("Missing citations");
        for (var citation:citations) if (!labels.contains(citation.asText())) throw new IllegalStateException("Unresolved citation");
        return answer;
    }
    public static void review(Path directory, boolean approve, String reviewer, String reason) throws Exception {
        try (var lease=new Lease(directory)) {
            if (reviewer.isBlank() || reason.isBlank()) throw new IllegalArgumentException("Reviewer and reason required");
            var packet=FilesUtil.read(directory.resolve("draft.json"));
            var decision=Map.of("approved",approve,"reviewer",reviewer,"reason",reason,"input_hash",packet.path("input_hash").asText(),"draft_hash",FilesUtil.hash(Files.readString(directory.resolve("draft.json"))));
            if (Files.exists(directory.resolve("review.json"))) {
                if (!FilesUtil.read(directory.resolve("review.json")).equals(Json.MAPPER.valueToTree(decision))) throw new IllegalStateException("Review already recorded; use a new work item for another decision");
            } else FilesUtil.save(directory.resolve("review.json"),decision);
        }
    }
}
