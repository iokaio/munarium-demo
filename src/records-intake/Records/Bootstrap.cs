// SPDX-License-Identifier: Apache-2.0
using Ioka.Munarium.Client;
namespace Records;

public static class Bootstrap
{
    public const string Revision = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public static string Endpoint => Environment.GetEnvironmentVariable("MUNARIUM_REST_URL") ?? "http://server:8080";
    public static MunariumClient Client(string token, string uid, string? endpoint = null) => MunariumClient.Rest(new() { Endpoint = endpoint ?? Endpoint, Token = token, Uid = uid, ReadRetries = 0 });
    public static MunariumClient Ops(bool management = false, string? endpoint = null) => Client(Environment.GetEnvironmentVariable(management ? "MUNARIUM_MGMT_TOKEN" : "MUNARIUM_TOKEN") ?? throw new InvalidOperationException("Operator credential absent"), "records-operator", endpoint);
    public static Grant Grant(string kind = "worker") => Storage.Read<Grant>($"/credentials/{kind}.json");
    public static string Asset(string path) => File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("RECORDS_ASSET_ROOT") ?? "/app", path));
    public static string Template(string name, string collection, int level = 0) => Asset("runbooks/intake.yaml").Replace("__NAME__", name).Replace("__COLLECTION__", collection).Replace("__LEVEL__", level.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public static async Task Ready()
    {
        await using var c = Ops();
        for (int i = 0; ; i++) { try { Storage.Require((await c.ServerVersionAsync()).Version == "1.1.1", "Server 1.1.1 required"); return; } catch (MunariumException) when (i < 60) { await Task.Delay(1000); } }
    }
    public static async Task Run()
    {
        await Ready(); await using var c = Ops(); await using var issuer = Ops(true);
        var manifest = File.ReadAllText("/inputs/manifest.json");
        var ns = "records-" + Storage.Hash(manifest + Asset("runbooks/intake.yaml") + Asset("shapes/documents.yaml") + Revision + (Environment.GetEnvironmentVariable("RECORDS_RUN_ID") ?? "interactive"))[..12];
        await c.Runbooks.ApplyShapeAsync(Asset("shapes/documents.yaml"));
        var routes = new SortedDictionary<string, Scope>();
        foreach (var route in Storage.Read<Route[]>("/inputs/routes.json"))
        {
            var name = ns + "-" + route.Id; await c.Runbooks.ApplyRunbookAsync(Template(name, name)); routes.Add(route.File, new(name, name + "@1"));
        }
        var restricted = ns + "-restricted"; await c.Runbooks.ApplyRunbookAsync(Template(restricted, restricted, 2));
        foreach (var kind in new[] { "worker", "reader" })
        {
            var uid = "records-" + kind;
            var issued = await issuer.Tokens.MintAsync(uid, 0, [kind == "worker" ? "ingest" : "query"], [], routes.Values.Select(s => s.Runbook.Split('@')[0]).ToArray(), 3600);
            Storage.Save($"/credentials/{kind}.json", new Grant(issued.Token, uid, routes, restricted));
        }
        Storage.Save($"/work/bootstrap/{ns}.json", new { clientRevision = Revision, server = "1.1.1", manifest = System.Text.Json.JsonDocument.Parse(manifest).RootElement, routes, restricted });
        Console.WriteLine($"{routes.Count} collections configured. No documents uploaded, no index activated, no model configured.");
    }
}
