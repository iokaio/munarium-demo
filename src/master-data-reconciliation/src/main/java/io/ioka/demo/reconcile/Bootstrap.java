// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;

import io.ioka.munarium.client.*;
import io.ioka.munarium.client.model.*;
import java.nio.file.*;
import java.util.*;

public final class Bootstrap {
    private Bootstrap() {}
    public static final String REVISION = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public record Grant(String token, String uid, String namespace, String provider, String model, String config, Map<String,String> runbooks) {}
    public static String endpoint() { return FilesUtil.env("MUNARIUM_REST_URL", "http://server:8080"); }
    public static MunariumClient client(String token, String uid) {
        return MunariumClient.rest(MunariumClientOptions.of(endpoint()).withToken(token).withUid(uid).withReadRetries(0));
    }
    public static MunariumClient ops(boolean management) {
        String token = System.getenv(management ? "MUNARIUM_MGMT_TOKEN" : "MUNARIUM_TOKEN");
        if (token == null) throw new IllegalStateException("Operator credential is absent.");
        return client(token, "reconcile-bootstrap");
    }
    public static Grant grant() throws Exception { return Json.MAPPER.readValue(Files.readString(Path.of("/credentials/query.json")), Grant.class); }
    public static void run(String provider, boolean approve) throws Exception {
        Fixtures.assignments(provider);
        if (!approve) throw new IllegalArgumentException("Bootstrap requires --approve for its isolated test index cutovers.");
        Path inputs = Path.of("/inputs"); Fixtures.verify(inputs);
        if (!provider.equals("fixture") && !FilesUtil.read(inputs.resolve("manifest.json")).path("profile").asText().equals("default")) throw new IllegalArgumentException("Online qualification requires default fixtures");
        String model = provider.equals("fixture") ? "reconcile-selected" : System.getenv(provider.toUpperCase(Locale.ROOT) + "_MODEL");
        if (model == null || model.isBlank()) throw new IllegalArgumentException("Preferred model is absent.");
        String template = Files.readString(Path.of("/app/runbooks/stewardship.yaml"));
        String shape = Files.readString(Path.of("/app/shapes/documents.yaml"));
        String name = "reconcile-" + FilesUtil.hash(Files.readString(inputs.resolve("manifest.json")) + template + shape + Files.readString(Path.of("/app/shapes/claims.yaml")) + REVISION + provider + model).substring(0, 12);
        String config = name + "-model";
        Map<String,String> refs = new TreeMap<>();
        Map<String,String> runs = new TreeMap<>();
        try (var ops = ops(false)) {
            for (int attempt = 0; ; attempt++) {
                try {
                    if (!ops.serverVersion().version().equals("1.1.1")) throw new IllegalStateException("Server 1.1.1 required.");
                    break;
                } catch (io.ioka.munarium.client.errors.MunariumException e) { if (attempt == 59) throw e; Thread.sleep(2000); }
            }
            String connection = provider.equals("fixture") ? "endpoint: http://provider-fixture:11434" : "credentialRef: {env: " + provider.toUpperCase(Locale.ROOT) + "_API_KEY}";
            ops.providers.applyConfig("""
                apiVersion: munarium.ioka.io/v1
                kind: ProviderConfig
                metadata: {name: %s}
                spec:
                  provider: %s
                  %s
                  models:
                    complete: [%s]
                    fast: %s
                  budgets: {rpm: %d, dailyTokens: {fast: 100000}}
                """.formatted(config, provider.equals("fixture") ? "ollama" : provider, connection, FilesUtil.json(model), FilesUtil.json(model), provider.equals("fixture") ? Math.max(60, Fixtures.rows(inputs).size() * 3) : 60));
            if (!ops.providers.health(config).healthy()) throw new IllegalStateException("Named provider health failed: " + provider);
            ops.runbooks.applyShape(shape, null);
            ops.runbooks.applyShape(Files.readString(Path.of("/app/shapes/claims.yaml")), null);
            for (var event : Fixtures.rows(inputs)) {
                String scoped = name + "-" + event.id();
                ops.runbooks.applyRunbook(template.replace("__NAME__", scoped).replace("__PROVIDER__", config));
                var result = ops.ingest.ingest(Ingesting.IngestFile.ofText(scoped + "/procedure.txt", "text/plain", Files.readString(inputs.resolve("documents/" + event.id() + ".txt"))));
                if (result.error() != null || !result.boundTo().contains(scoped)) throw new IllegalStateException("Unexpected ingest binding.");
                var run = ops.runbooks.runRunbook(scoped, null);
                runs.put(event.id(), run.runId());
                FilesUtil.save(Path.of("/work/bootstrap/" + provider + "-" + run.runId() + ".json"), Map.of("runs", runs, "client_revision", REVISION, "namespace", name, "provider", provider, "model", model, "manifest", FilesUtil.read(inputs.resolve("manifest.json"))));
                var pending = ops.runbooks.getRun(run.runId()).steps().stream().filter(s -> s.state().equals("awaiting_approval")).toList();
                if (pending.size() != 1 || !pending.getFirst().name().equals("cutover:" + scoped)) throw new IllegalStateException("Unexpected index cutover.");
                ops.runbooks.approveStep(run.runId(), pending.getFirst().ordinal());
                if (!ops.runbooks.getRun(run.runId()).state().equals("done")) throw new IllegalStateException("Index run incomplete.");
                refs.put(event.id(), scoped + "@1");
            }
        }
        try (var issuer = ops(true)) {
            var issued = issuer.tokens.mint(new Tokens.IssueTokenRequest("reconcile-reviewer", 0, List.of(), List.of("query"), refs.values().stream().map(r -> r.split("@")[0]).toList(), 3600L));
            FilesUtil.save(Path.of("/credentials/query.json"), new Grant(issued.token(), "reconcile-reviewer", name, provider, model, config, refs));
        }
        System.out.println("Bootstrapped " + provider + "/" + model + "; " + refs.size() + " isolated stewardship collections.");
    }
}
