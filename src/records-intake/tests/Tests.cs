// SPDX-License-Identifier: Apache-2.0
using Records;
using Ioka.Munarium.Client;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Records.Tests;

public sealed class Tests
{
    private static string Work => Environment.GetEnvironmentVariable("RECORDS_REPORT_DIR") ?? "/work/development";
    private static string Relative(int id) => Storage.Read<Route[]>("/inputs/routes.json").Single(r => r.Id == $"case-{id:000}").File;
    private static (Intake App, string Input, string State, string File) Case(int id)
    {
        var path = Path.Combine(Work, $"case-{id:000}"); var file = Relative(id); var input = Path.Combine(path, "incoming"); var state = Path.Combine(path, "state");
        if (!File.Exists(Path.Combine(input, file))) Storage.Write(Path.Combine(input, file), File.ReadAllText(Path.Combine("/inputs/incoming", file)));
        return (new Intake(input, state, Bootstrap.Grant()), input, state, file);
    }
    private static async Task<TurnResult> Search(string file)
    {
        var g = Bootstrap.Grant("reader"); await using var c = Bootstrap.Client(g.Token, g.Uid); var session = await c.Sessions.CreateAsync(g.Routes[file].Runbook);
        return await c.Sessions.TurnAsync(session.SessionId, new() { Query = "Reference revision office record requests", Complete = false, TopK = 4 });
    }
    private static async Task Change(int id)
    {
        var c = Case(id); var bytes = File.ReadAllText(Path.Combine(c.Input, c.File)); Storage.Write(Path.Combine(c.Input, c.File), bytes.Replace("Revision A", "Revision B").Replace($"RECORD-{id:00}-A", $"RECORD-{id:00}-B"));
        await c.App.Scan(DateTimeOffset.UtcNow); await c.App.Scan(DateTimeOffset.UtcNow.AddSeconds(3));
    }
    [Theory, Trait("Kind", "unit")]
    [InlineData("default", 8, "RECORD-01-A")]
    [InlineData("heldout", 8, "SEED-85091-RECORD-01-A")]
    [InlineData("stress", 80, "SEED-95091-RECORD-01-A")]
    public void GeneratorIsReproducibleAcrossProcesses(string profile, int count, string firstReference)
    {
        var paths = new[] { $"/tmp/records-{profile}-a", $"/tmp/records-{profile}-b" };
        foreach (var path in paths)
        {
            using var child = Process.Start(new ProcessStartInfo("dotnet") { ArgumentList = { "/app/Records/bin/Release/net10.0/Records.dll", "generate", path, path + "-oracle", profile } })!;
            if (!child.WaitForExit(30000)) { child.Kill(true); throw new TimeoutException("Generator timed out"); }
            Assert.Equal(0, child.ExitCode);
        }
        Assert.Equal(File.ReadAllBytes(paths[0] + "/manifest.json"), File.ReadAllBytes(paths[1] + "/manifest.json")); Assert.Equal(File.ReadAllBytes(paths[0] + "-oracle/expected.json"), File.ReadAllBytes(paths[1] + "-oracle/expected.json"));
        foreach (var file in Directory.GetFiles(paths[0], "*", SearchOption.AllDirectories)) Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(paths[1], Path.GetRelativePath(paths[0], file))));
        var expected = Storage.Read<Dictionary<string, string>>(paths[0] + "-oracle/expected.json");
        Assert.Equal(count, expected.Count); Assert.Equal(firstReference, expected["office/case-001.txt"]); Assert.Equal(count, Storage.Read<Route[]>(paths[0] + "/routes.json").Length);
        Assert.False(File.Exists(paths[0] + "/expected.json"));
        Storage.Save(Path.Combine(Work, $"fixture-{profile}-manifest.json"), Storage.Read<JsonElement>(paths[0] + "/manifest.json"));
    }
    public static IEnumerable<object[]> BusinessCases() => Storage.Read<Dictionary<string, string>>("/oracle/expected.json").Keys.Order(StringComparer.Ordinal).Select(file => new object[] { int.Parse(Path.GetFileNameWithoutExtension(file)[5..], System.Globalization.CultureInfo.InvariantCulture) });
    [Theory, Trait("Kind", "business")]
    [MemberData(nameof(BusinessCases), DisableDiscoveryEnumeration = true)]
    public async Task IndependentDocumentLifecycle(int id)
    {
        var c = Case(id); var now = DateTimeOffset.UtcNow; var expected = Storage.Read<Dictionary<string, string>>("/oracle/expected.json")[c.File];
        await c.App.Scan(now); Assert.Equal("discovered", c.App.Entries()[c.File].State);
        await c.App.Scan(now.AddSeconds(1)); Assert.Equal("discovered", c.App.Entries()[c.File].State);
        await c.App.Scan(now.AddSeconds(3), true); Assert.Equal("uploaded", c.App.Entries()[c.File].State); Assert.Empty((await Search(c.File)).Hits);
        await c.App.Scan(now.AddSeconds(4)); Assert.Equal("bound", c.App.Entries()[c.File].State); Assert.Empty((await Search(c.File)).Hits);
        await c.App.Build(c.File); var e = c.App.Entries()[c.File]; Assert.Equal("verified", e.State); Assert.Empty((await Search(c.File)).Hits);
        await c.App.Approve(c.File, e.RunId!); var result = await Search(c.File); Assert.Null(result.Completion); Assert.Single(result.Hits); Assert.Contains(expected, result.Hits[0].Text);
        Assert.Equal(Storage.Hash(File.ReadAllBytes(Path.Combine(c.Input, c.File))), result.Hits[0].SourceContentHash); Assert.Equal(Bootstrap.Grant().Routes[c.File].Collection + "/record.txt", result.Hits[0].SourcePath);
        Assert.Equal(new[] { "discovered", "uploaded", "bound", "build-submitted", "indexed", "verified", "active" }, c.App.Entries()[c.File].History);
        var before = File.ReadAllBytes(Path.Combine(c.State, "journal.json")); await c.App.Scan(now.AddSeconds(5)); Assert.Equal(before, File.ReadAllBytes(Path.Combine(c.State, "journal.json")));
        Storage.Save(Path.Combine(Work, $"case-{id:000}.quality.json"), new { id, passed = true, expected, result.CollectionsSearched, result.Hits, modelCalls = 0 });
        Storage.Write(Path.Combine(Work, $"case-{id:000}/status.txt"), c.App.Status());
    }
    [Fact, Trait("Kind", "failure")]
    public async Task UnauthorizedBindingAndAdministrationAreDenied()
    {
        var g = Bootstrap.Grant(); await using var c = Bootstrap.Client(g.Token, g.Uid);
        var file = new IngestFile { Filename = "records-forbidden.txt", MediaType = "text/plain", ContentBase64 = Convert.ToBase64String("synthetic blocked record"u8.ToArray()), Collections = [g.Restricted] };
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Ingest.IngestAsync(file));
        var result = (await c.Ingest.IngestBatchAsync([file])).Single();
        Assert.NotNull(result.Error); Assert.Empty(result.BoundTo);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Runbooks.RunRunbookAsync(g.Routes.Values.First().Runbook.Split('@')[0]));
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ExpiredIngestCapabilityCanBeRenewedWithSameUid()
    {
        var g = Bootstrap.Grant(); await using var issuer = Bootstrap.Ops(true); var token = await issuer.Tokens.MintAsync(g.Uid, 0, ["ingest"], [], g.Routes.Values.Select(r => r.Runbook.Split('@')[0]).ToArray(), 1);
        await using var c = Bootstrap.Client(token.Token, g.Uid); await Task.Delay(TimeSpan.FromSeconds(33));
        var file = new IngestFile { Filename = "records-expiry.txt", MediaType = "text/plain", ContentBase64 = Convert.ToBase64String("synthetic expiry"u8.ToArray()), Collections = [] };
        await Assert.ThrowsAsync<UnauthenticatedException>(() => c.Ingest.IngestAsync(file)); await using var renewed = Bootstrap.Client(g.Token, g.Uid); Assert.Null((await renewed.Ingest.IngestAsync(file)).Error);
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ChangedRevisionIsNotVisibleUntilItsOwnApprovedCutover()
    {
        var c = Case(1); var previous = c.App.Entries()[c.File].RunId; await Change(1); await c.App.Build(c.File); var pending = c.App.Entries()[c.File];
        Assert.NotEqual(previous, pending.RunId); Assert.Contains("RECORD-01-A", (await Search(c.File)).Hits.Single().Text);
        await Assert.ThrowsAsync<InvalidDataException>(() => c.App.Approve(c.File, previous!));
        await c.App.Approve(c.File, pending.RunId!); Assert.Contains("RECORD-01-B", (await Search(c.File)).Hits.Single().Text);
        Assert.Single(Directory.GetFiles(Path.Combine(c.State, "history"), "*.json", SearchOption.AllDirectories));
    }
    [Fact, Trait("Kind", "failure")]
    public async Task FailedBuildRequestKeepsPreviousActiveIndexAndDoesNotRetry()
    {
        var c = Case(5); await Change(5); using var http = new HttpClient(); await http.GetStringAsync("http://faults:11435/control/fail");
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => c.App.Build(c.File, "http://faults:11435")); }
        finally { await http.GetStringAsync("http://faults:11435/control/normal"); }
        Assert.Contains("RECORD-05-A", (await Search(c.File)).Hits.Single().Text); Assert.Equal("build-submitted", c.App.Entries()[c.File].State);
        await Assert.ThrowsAsync<InvalidDataException>(() => c.App.Build(c.File)); await Assert.ThrowsAsync<InvalidDataException>(() => c.App.Resume(c.File));
    }
    [Fact, Trait("Kind", "failure")]
    public async Task LostBuildResponseIsAdoptedOnlyForItsUniqueRunbook()
    {
        var c = Case(6); await Change(6); using var http = new HttpClient(); await http.GetStringAsync("http://faults:11435/control/drop");
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => c.App.Build(c.File, "http://faults:11435")); }
        finally { await http.GetStringAsync("http://faults:11435/control/normal"); }
        var status = JsonDocument.Parse(await http.GetStringAsync("http://faults:11435/control/normal")).RootElement; var run = status.GetProperty("lastRun").GetString()!;
        await Assert.ThrowsAsync<InvalidDataException>(() => c.App.Resume(c.File, Case(2).App.Entries()[Relative(2)].RunId));
        await c.App.Resume(c.File, run); Assert.Equal("verified", c.App.Entries()[c.File].State); await c.App.Approve(c.File, run); Assert.Contains("RECORD-06-B", (await Search(c.File)).Hits.Single().Text);
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ChangingBytesAfterVerificationRejectsStaleApproval()
    {
        var c = Case(8); await Change(8); await c.App.Build(c.File); var run = c.App.Entries()[c.File].RunId;
        File.AppendAllText(Path.Combine(c.Input, c.File), "A later edit is not reviewed.\n"); await Assert.ThrowsAsync<InvalidDataException>(() => c.App.Approve(c.File, run!)); Assert.Contains("RECORD-08-A", (await Search(c.File)).Hits.Single().Text);
    }
    [Fact, Trait("Kind", "prepare-restart")]
    public async Task CheckpointVerifiedBuildBeforeServerRestart() { var c = Case(7); await Change(7); await c.App.Build(c.File); Assert.Equal("verified", c.App.Entries()[c.File].State); }
    [Fact, Trait("Kind", "failure")]
    public async Task PartialWritesResetStabilityAndUnmappedFilesStayUnpublished()
    {
        var c = Case(4); var path = Path.Combine(c.Input, c.File); var text = File.ReadAllText(path).Replace("RECORD-04-A", "RECORD-04-B"); var now = DateTimeOffset.UtcNow;
        Storage.Write(path, "synthetic partial write"); await c.App.Scan(now);
        Storage.Write(path, text); await c.App.Scan(now.AddSeconds(1)); await c.App.Scan(now.AddSeconds(2)); Assert.Equal("discovered", c.App.Entries()[c.File].State); Assert.Null(c.App.Entries()[c.File].SourceId);
        Storage.Write(Path.Combine(c.Input, "unmapped.txt"), "synthetic unknown route"); await c.App.Scan(now.AddSeconds(4)); Assert.Equal("bound", c.App.Entries()[c.File].State); Assert.Single(c.App.Entries());
        Assert.Contains("RECORD-04-A", (await Search(c.File)).Hits.Single().Text); await c.App.Build(c.File); await c.App.Approve(c.File, c.App.Entries()[c.File].RunId!); Assert.Contains("RECORD-04-B", (await Search(c.File)).Hits.Single().Text);
    }
    [Fact, Trait("Kind", "failure")]
    public async Task BackgroundWorkerCrashPreservesBoundRevision()
    {
        var c = Case(3); var path = Path.Combine(c.Input, c.File); Storage.Write(path, File.ReadAllText(path).Replace("RECORD-03-A", "RECORD-03-B"));
        var start = new ProcessStartInfo("dotnet") { ArgumentList = { "/app/Records/bin/Release/net10.0/Records.dll", "worker", c.Input, c.State }, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("MUNARIUM_TOKEN"); start.Environment.Remove("MUNARIUM_MGMT_TOKEN");
        using var worker = Process.Start(start)!;
        try { for (int i = 0; i < 20 && c.App.Entries()[c.File].State != "bound"; i++) await Task.Delay(500); Assert.Equal("bound", c.App.Entries()[c.File].State); }
        finally { if (!worker.HasExited) worker.Kill(true); await worker.WaitForExitAsync(); }
        var persisted = c.App.Entries()[c.File].Hash; var restarted = new Intake(c.Input, c.State, Bootstrap.Grant()); await restarted.Scan(DateTimeOffset.UtcNow.AddSeconds(5)); Assert.Equal(persisted, restarted.Entries()[c.File].Hash);
        await restarted.Build(c.File); await restarted.Approve(c.File, restarted.Entries()[c.File].RunId!); Assert.Contains("RECORD-03-B", (await Search(c.File)).Hits.Single().Text);
    }
    [Fact, Trait("Kind", "restarted")]
    public async Task NewProcessResumesRecordedBuildAfterServerRestart()
    {
        await Bootstrap.Ready(); var c = Case(7); var run = c.App.Entries()[c.File].RunId!; await c.App.Resume(c.File); Assert.Equal(run, c.App.Entries()[c.File].RunId); await c.App.Approve(c.File, run); Assert.Contains("RECORD-07-B", (await Search(c.File)).Hits.Single().Text);
    }
}
