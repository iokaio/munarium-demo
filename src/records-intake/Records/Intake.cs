// SPDX-License-Identifier: Apache-2.0
using Ioka.Munarium.Client;
using Microsoft.Extensions.Hosting;
namespace Records;

public sealed class Intake(string incoming, string state, Grant grant)
{
    public SortedDictionary<string, Entry> Entries() => File.Exists(Path.Combine(state, "journal.json")) ? Storage.Read<SortedDictionary<string, Entry>>(Path.Combine(state, "journal.json")) : new();
    private void Save(SortedDictionary<string, Entry> entries) => Storage.Save(Path.Combine(state, "journal.json"), entries);
    private FileStream Lease() { Directory.CreateDirectory(state); return new(Path.Combine(state, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    public async Task Scan(DateTimeOffset now, bool uploadOnly = false)
    {
        using var lease = Lease(); var entries = Entries(); await using var c = Bootstrap.Client(grant.Token, grant.Uid);
        foreach (var file in Directory.EnumerateFiles(incoming, "*.txt", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(incoming, file).Replace('\\', '/');
            if (!grant.Routes.TryGetValue(relative, out var scope)) { Console.Error.WriteLine($"Unmapped file quarantined: {relative}"); continue; }
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked input is not allowed");
            var bytes = await File.ReadAllBytesAsync(file); var hash = Storage.Hash(bytes);
            if (!entries.TryGetValue(relative, out var entry) || entry.Hash != hash)
            {
                if (entry is not null) Storage.Save(Path.Combine(state, "history", Storage.Hash(relative), entry.Hash + ".json"), entry);
                entries[relative] = new() { File = relative, Hash = hash, FirstSeen = now }; Save(entries); continue;
            }
            if (now - entry.FirstSeen < TimeSpan.FromSeconds(2) || entry.State is not ("discovered" or "uploaded")) continue;
            IngestFile Request(string[] collections) => new() { Filename = scope.Collection + "/record.txt", MediaType = "text/plain", Sha256 = hash, ContentBase64 = Convert.ToBase64String(bytes), Collections = collections };
            try
            {
                if (entry.State == "discovered")
                {
                    var uploaded = await c.Ingest.IngestAsync(Request([])); Storage.Require(uploaded.Error is null && uploaded.BoundTo.Count == 0 && uploaded.Sha256 == hash, "Upload did not succeed independently of binding");
                    entry.SourceId = uploaded.SourceId; entry.Stage("uploaded"); Save(entries);
                }
                if (uploadOnly) continue;
                var bound = await c.Ingest.IngestAsync(Request([scope.Collection])); Storage.Require(bound.Error is null && bound.BoundTo.SequenceEqual([scope.Collection]), "Binding failed");
                entry.Stage("bound"); entry.Error = null; Save(entries);
            }
            catch (Exception error) { entry.Error = error.Message; Save(entries); throw; }
        }
    }
    public async Task Build(string file, string? endpoint = null)
    {
        using var lease = Lease(); var entries = Entries(); var e = entries[file];
        Storage.Require(e.State == "bound" && e.BuildRunbook is null, "Only a bound revision without a submitted build can start a build");
        Storage.Require(Storage.Hash(await File.ReadAllBytesAsync(Path.Combine(incoming, file))) == e.Hash, "Source changed; reconcile first");
        e.BuildRunbook = grant.Routes[file].Collection + "-build-" + Guid.NewGuid().ToString("N"); e.Stage("build-submitted"); Save(entries);
        await using var c = Bootstrap.Ops(endpoint: endpoint);
        try
        {
            await c.Runbooks.ApplyRunbookAsync(Bootstrap.Template(e.BuildRunbook, grant.Routes[file].Collection));
            var run = await c.Runbooks.RunRunbookAsync(e.BuildRunbook); e.RunId = run.RunId; Save(entries);
            await Refresh(c, e); Save(entries);
        }
        catch (Exception error) { e.Error = error.Message; Save(entries); throw; }
    }
    private async Task Refresh(MunariumClient c, Entry e)
    {
        var run = await c.Runbooks.GetRunAsync(e.RunId!); Storage.Save(Path.Combine(state, "runs", e.RunId + ".json"), run);
        Storage.Require(run.RunbookRef == e.BuildRunbook + "@1", "Run belongs to another submitted build");
        e.Error = null;
        if (run.Steps.Any(s => s.Name.StartsWith("buildIndex:", StringComparison.Ordinal) && s.State == "done")) e.Stage("indexed");
        if (run.Steps.Any(s => s.Name.StartsWith("verify:", StringComparison.Ordinal) && s.State == "done")) e.Stage("verified");
        if (run.State == "done") e.Stage("active");
        if (run.State == "failed") { e.Error = "Build failed; prior active index retained"; e.Stage("build-failed"); }
    }
    public async Task Resume(string file, string? adopt = null)
    {
        using var lease = Lease(); var entries = Entries(); var e = entries[file]; await using var c = Bootstrap.Ops();
        if (adopt is not null) { var candidate = await c.Runbooks.GetRunAsync(adopt); Storage.Require(e.RunId is null && candidate.RunbookRef == e.BuildRunbook + "@1", "Cannot adopt an unrelated run"); e.RunId = adopt; Save(entries); }
        Storage.Require(e.RunId is not null, "Build outcome unknown; inspect Server and explicitly adopt its matching run ID. No automatic retry.");
        await Refresh(c, e); Save(entries);
    }
    public async Task Approve(string file, string runId)
    {
        using var lease = Lease(); var entries = Entries(); var e = entries[file];
        Storage.Require(e.RunId == runId && e.State == "verified", "Approval must name this verified revision's recorded run");
        Storage.Require(Storage.Hash(await File.ReadAllBytesAsync(Path.Combine(incoming, file))) == e.Hash, "Source changed after build; refuse stale cutover");
        await using var c = Bootstrap.Ops(); var run = await c.Runbooks.GetRunAsync(runId); var pending = run.Steps.Where(s => s.State == "awaiting_approval").ToArray();
        Storage.Require(run.RunbookRef == e.BuildRunbook + "@1" && pending.Length == 1 && pending[0].Name == "cutover:" + grant.Routes[file].Collection, "Unrelated or unexpected cutover");
        await c.Runbooks.ApproveStepAsync(runId, pending[0].Ordinal); await Refresh(c, e); Save(entries); Storage.Require(e.State == "active", "Activation not confirmed");
    }
    public string Status() => "RECORDS INTAKE | synthetic office procedures\n" + string.Join("\n", Entries().Values.Select(e => $"{e.File}\n  State: {e.State} | SHA256: {e.Hash}\n  Lifecycle: {string.Join(" -> ", e.History)}\n  Run: {e.RunId ?? "not submitted"}\n  Error: {e.Error ?? "none"}")) + "\n";
}
public sealed class IntakeWorker(Intake intake) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await intake.Scan(DateTimeOffset.UtcNow); } catch (Exception e) { Console.Error.WriteLine(e.Message); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
