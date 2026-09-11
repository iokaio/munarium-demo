// SPDX-License-Identifier: Apache-2.0
using Ioka.Munarium.Client;
using Policy.Core;

namespace Policy.Harness;

public static class Bootstrap
{
    public const string ClientRevision = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public static string Endpoint => Environment.GetEnvironmentVariable("MUNARIUM_REST_URL") ?? "http://server:8080";
    public static MunariumClient Ops(bool management = false) => MunariumClient.Rest(new() { Endpoint = Endpoint, Token = Environment.GetEnvironmentVariable(management ? "MUNARIUM_MGMT_TOKEN" : "MUNARIUM_TOKEN") ?? throw new InvalidOperationException("Bootstrap credential absent."), Uid = "policy-bootstrap" });
    public static string Model(string provider) => provider switch { "fixture" => "policy-selected", _ => Environment.GetEnvironmentVariable(provider.ToUpperInvariant() + "_MODEL") ?? throw new InvalidOperationException("Preferred model missing.") };
    public static async Task Run(string provider, string inputs, string credentials, string work, bool approve)
    {
        if (provider is not ("fixture" or "openai" or "anthropic" or "openrouter")) throw new ArgumentException("Unsupported demo provider.");
        Fixtures.Verify(inputs);
        await using var ops = Ops();
        for (var attempt = 0; ; attempt++)
        {
            try { var version = await ops.ServerVersionAsync(); if (version.Version != "1.1.1") throw new InvalidDataException("Server 1.1.1 required."); break; }
            catch (MunariumException) when (attempt < 60) { await Task.Delay(2000); }
        }
        var template = File.ReadAllText("runbooks/policy.yaml");
        var shape = File.ReadAllText("shapes/documents.yaml");
        var model = Model(provider);
        var revision = Storage.Hash(File.ReadAllText(Path.Combine(inputs, "manifest.json")) + template + shape + provider + model)[..12];
        var name = "policy-" + revision;
        var config = name + "-model";
        var connection = provider == "fixture" ? "endpoint: http://provider-fixture:11434" : $"credentialRef: {{ env: {provider.ToUpperInvariant()}_API_KEY }}";
        await ops.Providers.ApplyConfigAsync($$"""
            apiVersion: munarium.ioka.io/v1
            kind: ProviderConfig
            metadata: { name: {{config}} }
            spec:
              provider: {{(provider == "fixture" ? "ollama" : provider)}}
              {{connection}}
              models:
                complete: [{{model}}]
                fast: {{model}}
                capable: {{model}}
              budgets: { rpm: 60, dailyTokens: { fast: 200000, capable: 200000 } }
            """);
        if (!(await ops.Providers.HealthAsync(config)).Healthy) throw new InvalidOperationException("Provider health failed.");
        await ops.Runbooks.ApplyShapeAsync(shape);
        await ops.Runbooks.ApplyRunbookAsync(template.Replace("__NAME__", name).Replace("__PROVIDER__", config));
        foreach (var path in Directory.GetFiles(inputs, "*.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(inputs, path).Replace('\\', '/');
            var result = await ops.Ingest.IngestAsync(new() { Filename = name + "/" + relative, MediaType = "text/plain", ContentBase64 = Convert.ToBase64String(File.ReadAllBytes(path)) });
            if (result.Error is not null || !result.BoundTo.Contains(name + "-" + relative.Split('/')[0])) throw new InvalidDataException("Ingest binding failed.");
        }
        var run = await ops.Runbooks.RunRunbookAsync(name);
        Storage.Save(Path.Combine(work, "bootstrap-" + provider + "-" + run.RunId + ".json"), new { run.RunId, name, revision, provider, model, clientRevision = ClientRevision, manifest = Storage.Read<object>(Path.Combine(inputs, "manifest.json")) });
        if (!approve) throw new InvalidOperationException("Inspect the saved run; rerun bootstrap with --approve for this isolated test corpus.");
        for (var step = 0; step < 8; step++)
        {
            var status = await ops.Runbooks.GetRunAsync(run.RunId);
            if (status.State == "done") break;
            var pending = status.Steps.Where(s => s.State == "awaiting_approval").ToArray();
            if (pending.Length != 1 || !new[] { "public", "west", "east", "hr" }.Any(c => pending[0].Name == "cutover:" + name + "-" + c)) throw new InvalidOperationException("Unexpected cutover; inspect saved run.");
            await ops.Runbooks.ApproveStepAsync(run.RunId, pending[0].Ordinal);
        }
        if ((await ops.Runbooks.GetRunAsync(run.RunId)).State != "done") throw new InvalidOperationException("Index run did not complete.");
        await using var issuer = Ops(true);
        foreach (var identity in new[] { "employee", "west", "east", "hr" })
        {
            var level = identity == "hr" ? 2 : identity == "employee" ? 0 : 1;
            var issued = await issuer.Tokens.MintAsync("policy-" + identity, level, ["query"], identity == "employee" ? [] : [identity], [name], 3600);
            Storage.Save(Path.Combine(credentials, identity + ".json"), new IdentityGrant(identity, "policy-" + identity, issued.Token, issued.ExpiresAt, name + "@1", revision, [new(config, provider == "fixture" ? "ollama" : provider, model)]));
        }
        Console.WriteLine($"Bootstrapped {provider}/{model}; four scoped identities; approved run {run.RunId}.");
    }
}
