// SPDX-License-Identifier: Apache-2.0
using Ioka.Munarium.Client;

namespace Meetings;

public static class Bootstrap
{
    public static string Endpoint => Environment.GetEnvironmentVariable("MUNARIUM_REST_URL") ?? "http://server:8080";
    public static MunariumClient Client(string token, string uid, string? endpoint = null) => MunariumClient.Rest(new() { Endpoint = endpoint ?? Endpoint, Token = token, Uid = uid, ReadRetries = 0 });
    public static MunariumClient Writer(string? endpoint = null) => Client(Environment.GetEnvironmentVariable("MUNARIUM_TOKEN") ?? throw new InvalidOperationException("Trusted writer credential required"), "meeting-writer", endpoint);
    public static MunariumClient Reader() => Client("meetings-ro", "meeting-reader");
    public static async Task Ready()
    {
        await using var api = Reader();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try { Storage.Require((await api.ServerVersionAsync()).Version == "1.1.1", "Server 1.1.1 required"); return; }
            catch (MunariumException) { await Task.Delay(1000); }
        }
        throw new InvalidOperationException("Server not ready");
    }
    public static async Task Run(string provider, string work)
    {
        Storage.Require(provider is "fixture" or "openai" or "anthropic" or "openrouter", "Unknown provider");
        var manifest = Fixtures.Verify("/inputs");
        await Ready();
        var model = provider == "fixture" ? "meetings-fixture" : Environment.GetEnvironmentVariable(provider.ToUpperInvariant() + "_MODEL") ?? throw new InvalidOperationException("Preferred model missing");
        var template = File.ReadAllText("/app/runbooks/meeting.yaml");
        var shape = File.ReadAllText("/app/shapes/documents.yaml");
        var revision = Storage.Hash(File.ReadAllText("/inputs/manifest.json") + template + shape + provider + model);
        var prefix = "meetings-" + revision[..12]; var config = prefix + "-model";
        await using var api = Writer();
        var connection = provider == "fixture" ? "endpoint: http://provider-fixture:11434" : $"credentialRef: {{ env: {provider.ToUpperInvariant()}_API_KEY }}";
        await api.Providers.ApplyConfigAsync($$"""
            apiVersion: munarium.ioka.io/v1
            kind: ProviderConfig
            metadata: {name: {{config}}}
            spec:
              provider: {{(provider == "fixture" ? "ollama" : provider)}}
              {{connection}}
              models: {complete: [{{model}}], fast: {{model}} }
              budgets: {rpm: 60, dailyTokens: {fast: 200000} }
            """);
        Storage.Require((await api.Providers.HealthAsync(config)).Healthy, "Provider health failed");
        await api.Runbooks.ApplyShapeAsync(shape);
        await api.Runbooks.ApplyShapeAsync(File.ReadAllText("/app/shapes/commitments.yaml"));
        var runbooks = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in manifest.Files)
        {
            var caseId = pair.Key.Split('/')[0]; var name = prefix + "-" + caseId;
            await api.Runbooks.ApplyRunbookAsync(template.Replace("__NAME__", name).Replace("__PROVIDER__", config));
            var result = await api.Ingest.IngestAsync(new() { Filename = name + "/transcript.md", MediaType = "text/plain", ContentBase64 = Convert.ToBase64String(File.ReadAllBytes("/inputs/" + pair.Key)), Sha256 = pair.Value });
            Storage.Require(result.Error is null && result.BoundTo.SequenceEqual([name]), "Unexpected transcript binding");
            var run = await api.Runbooks.RunRunbookAsync(name);
            Storage.Save(Path.Combine(work, caseId + "-run.json"), new { run.RunId, name, revision, provider, model });
            var status = await api.Runbooks.GetRunAsync(run.RunId);
            var pending = status.Steps.Where(s => s.State == "awaiting_approval").ToArray();
            Storage.Require(pending.Length == 1 && pending[0].Name == "cutover:" + name, "Unexpected verified cutover");
            await api.Runbooks.ApproveStepAsync(run.RunId, pending[0].Ordinal);
            Storage.Require((await api.Runbooks.GetRunAsync(run.RunId)).State == "done", "Index not activated");
            runbooks[caseId] = name + "@1";
        }
        await using var issuer = Client(Environment.GetEnvironmentVariable("MUNARIUM_MGMT_TOKEN")!, "meeting-issuer");
        var grant = await issuer.Tokens.MintAsync("meeting-reviewer", 0, ["query"], [], runbooks.Values.Select(r => r.Split('@')[0]).ToArray(), 3600);
        Storage.Save("/credentials/query.json", new Grant(grant.Token, "meeting-reviewer", provider, model, revision, runbooks));
        Storage.Save(Path.Combine(work, "manifest.json"), new { inputs = manifest, clientRevision = Fixtures.Revision, provider, model, runbooks });
    }
}
