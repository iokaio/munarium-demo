// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import com.fasterxml.jackson.databind.*;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.model.SessionsApi.*;
import io.ioka.munarium.client.planes.Params;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;

public final class Worker {
    public enum Route { PROCUREMENT, CUSTOMER_SERVICE, FULFILLMENT, COMPLIANCE, LOGISTICS, MANUAL_REVIEW }
    private final Inbox inbox;
    private final Bootstrap.Grant grant;
    private final String endpoint;
    public Worker(Inbox inbox, Bootstrap.Grant grant) { this(inbox, grant, Bootstrap.endpoint()); }
    public Worker(Inbox inbox, Bootstrap.Grant grant, String endpoint) { this.inbox = inbox; this.grant = grant; this.endpoint = endpoint; }
    private MunariumClient client() { return MunariumClient.rest(MunariumClientOptions.of(endpoint).withToken(grant.token()).withUid(grant.uid()).withReadRetries(0)); }
    public void consume(List<OrderEvent> events) throws Exception {
        // Validate all duplicate IDs before dispatching any new provider work.
        for (var event : events) inbox.accept(event);
        var executor = new ThreadPoolExecutor(2, 2, 0, TimeUnit.MILLISECONDS, new ArrayBlockingQueue<>(16), new ThreadPoolExecutor.CallerRunsPolicy());
        var tasks = new ArrayList<Future<?>>();
        try {
            for (var row : inbox.rows()) if (row.state().equals("new")) tasks.add(executor.submit(() -> {
                try { process(row.id(), () -> {}); }
                catch (Exception e) { throw new CompletionException(e); }
            }));
            Exception failure = null;
            for (var task : tasks) try { task.get(); } catch (ExecutionException e) { failure = e; }
            if (failure != null) throw failure;
        } finally { executor.shutdown(); }
    }
    public void process(String id, Runnable afterTurn) throws Exception {
        var row = inbox.get(id);
        if (row == null || !row.state().equals("new")) return;
        String ref = grant.runbooks().get(id);
        if (ref == null) throw new IllegalArgumentException("No scoped runbook for event.");
        var event = Json.MAPPER.readValue(row.eventJson(), OrderEvent.class);
        String query = event.query();
        try (var client = client()) {
            String session = client.sessions.create(ref).sessionId();
            // The durable uncertain state precedes submission; losing the response never causes an automatic retry.
            if (!inbox.claim(id, session, query)) return;
            var progress = new ArrayList<TurnProgress>();
            TurnResult result = client.sessions.turnStream(session, Params.TurnOptions.of(query).withCompletion(null), progress::add);
            afterTurn.run();
            var stored = Json.MAPPER.createObjectNode();
            stored.set("result", Json.MAPPER.valueToTree(result)); stored.set("progress", Json.MAPPER.valueToTree(progress));
            inbox.response(id, FilesUtil.json(stored), false);
            finish(id, result);
        }
    }
    public void reconcile() throws Exception {
        try (var client = client()) {
            for (var row : inbox.rows()) {
                if (row.state().equals("review_required") && row.response() != null) {
                    finish(row.id(), Json.MAPPER.treeToValue(Json.MAPPER.readTree(row.response()).path("result"), TurnResult.class));
                    continue;
                }
                if (!row.state().equals("uncertain")) continue;
                var session = client.sessions.get(row.session());
                if (!session.uid().equals(grant.uid()) || !session.runbookRef().equals(grant.runbooks().get(row.id()))) throw new IllegalStateException("Transcript identity/configuration mismatch.");
                var turns = session.turns().stream().filter(t -> row.query().equals(t.query()) && t.completion() != null && !t.completion().isNull()).toList();
                if (turns.size() != 1 || session.turns().size() != 1) continue;
                var turn = turns.getFirst();
                var stored = turn.completion().deepCopy();
                if (!stored.path("resolved").path("provider").asText().equals(grant.config())) throw new IllegalStateException("Transcript provider configuration mismatch.");
                var completion = ((com.fasterxml.jackson.databind.node.ObjectNode) stored.deepCopy());
                completion.put("was_override", stored.path("resolved").path("was_override").asBoolean());
                var result = new TurnResult(row.session(), turn.ordinal(), turn.collectionsSearched(), null,
                    Arrays.asList(Json.MAPPER.treeToValue(turn.hits(), TurnHit[].class)),
                    turn.envelope() == null || turn.envelope().isNull() ? List.of() : Arrays.asList(Json.MAPPER.treeToValue(turn.envelope(), CollectionEnvelope[].class)),
                    Json.MAPPER.treeToValue(completion, TurnCompletion.class), null);
                inbox.response(row.id(), FilesUtil.json(Map.of("result", result, "stored_completion", stored)), true);
                finish(row.id(), result);
            }
        }
    }
    private void finish(String id, TurnResult result) throws Exception {
        JsonNode answer = validate(result, grant, id);
        var row = inbox.get(id);
        var packet = new TreeMap<String,Object>();
        packet.put("event", Json.MAPPER.readTree(row.eventJson())); packet.put("input_hash", row.inputHash());
        packet.put("session_id", row.session()); packet.put("answer", answer); packet.put("evidence", result);
        packet.put("recovered", row.recovered()); packet.put("status", "review_required");
        packet.put("client_revision", Bootstrap.REVISION); packet.put("configuration", grant.namespace());
        inbox.finish(id, FilesUtil.json(packet));
    }
    public static JsonNode validate(TurnResult result, Bootstrap.Grant grant, String id) throws Exception {
        var c = result.completion();
        String family = grant.provider().equals("fixture") ? "ollama" : grant.provider();
        if (c == null || !family.equals(c.provider()) || !grant.model().equals(c.model())) throw new IllegalStateException("Unexpected provider/model identity.");
        if (c.verification() != null && !c.verification().violations().isEmpty()) throw new IllegalStateException("Unresolved completion verification.");
        String text = c.text().trim();
        if (text.startsWith("```")) text = text.replaceFirst("^```(?:json)?\\s*", "").replaceFirst("\\s*```$", "");
        var answer = Json.MAPPER.readTree(text);
        if (!answer.isObject()) throw new IllegalStateException("Completion must be an object.");
        Route.valueOf(answer.path("route").asText());
        if (!answer.path("disposition").asText().equals("review_required") || !answer.path("missing_evidence").isBoolean()
            || !answer.path("explanation").isTextual() || answer.path("explanation").asText().isBlank()) throw new IllegalStateException("Invalid review packet fields.");
        String collection = grant.runbooks().get(id).split("@")[0];
        if (result.hits() == null || result.hits().isEmpty() || result.collectionsSearched() == null || !result.collectionsSearched().equals(List.of(collection))) throw new IllegalStateException("Missing or cross-event retrieval.");
        var labels = new HashSet<String>();
        for (var hit : result.hits()) {
            if (!collection.equals(hit.collection()) || !hit.sourcePath().startsWith(collection + "/") || hit.sourceContentHash() == null || hit.sourceContentHash().isBlank()) throw new IllegalStateException("Unscoped or unidentified evidence.");
            labels.add(hit.collection() + "/" + hit.chunkId());
        }
        var citations = answer.path("citations");
        if (!citations.isArray() || citations.isEmpty()) throw new IllegalStateException("Missing citations.");
        for (var citation : citations) if (!citation.isTextual() || !labels.contains(citation.asText())) throw new IllegalStateException("Unresolved citation.");
        return answer;
    }
    public void export(Path directory) throws Exception {
        for (var entry : inbox.packets().entrySet()) {
            var packet = Json.MAPPER.readTree(entry.getValue());
            FilesUtil.write(directory.resolve(entry.getKey() + ".json"), entry.getValue() + "\n");
            var answer = packet.path("answer");
            FilesUtil.write(directory.resolve(entry.getKey() + ".md"), "# Fictional order review: " + entry.getKey() + "\n\nProposed team: " + answer.path("route").asText() + "\n\nStatus: review required\n\n" + answer.path("explanation").asText() + "\n\nCitations: " + answer.path("citations") + "\n");
        }
        FilesUtil.save(directory.resolve("journal.json"), inbox.rows());
    }
}
