// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Ioka.Munarium.Client;
using Meetings;
using Draft = Meetings.Draft;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class Tests
{
    private static string Root => Environment.GetEnvironmentVariable("MEETINGS_REPORT_DIR") ?? "/work/tests";
    private static string Work(string name) => Path.Combine(Root, name);
    private static async Task Mode(string mode) { using var http = new HttpClient(); (await http.PostAsJsonAsync("http://provider-fixture:11434/mode", new { mode })).EnsureSuccessStatusCode(); }
    private static async Task Fault(string mode) { using var http = new HttpClient(); (await http.GetAsync("http://faults:11435/control/" + mode)).EnsureSuccessStatusCode(); }
    private static async Task<int> Calls() { using var http = new HttpClient(); return (await http.GetFromJsonAsync<JsonElement>("http://provider-fixture:11434/state")).GetProperty("calls").GetInt32(); }
    private static async Task<int> Child(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false }; start.ArgumentList.Add("/app/Meetings/bin/Release/net10.0/Meetings.dll"); foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; await process.WaitForExitAsync(); return process.ExitCode;
    }
    [Fact, Trait("Kind", "unit")]
    public async Task IndependentGeneratorProcesses()
    {
        var first = Work("fixtures-a"); var second = Work("fixtures-b");
        Assert.Equal(0, await Child("generate", first, first + "-oracle")); Assert.Equal(0, await Child("generate", second, second + "-oracle"));
        Assert.Equal(File.ReadAllBytes(Path.Combine(first, "manifest.json")), File.ReadAllBytes(Path.Combine(second, "manifest.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(first + "-oracle", "expected.json")), File.ReadAllBytes(Path.Combine(second + "-oracle", "expected.json")));
        foreach (var pair in Fixtures.Verify(first).Files) Assert.Equal(File.ReadAllBytes(Path.Combine(first, pair.Key)), File.ReadAllBytes(Path.Combine(second, pair.Key)));
        File.Copy(Path.Combine(first, "manifest.json"), Work("reproducible-manifest.json"));
    }
    [Theory, Trait("Kind", "unit")]
    [InlineData("2026-09-21", true)]
    [InlineData("next Friday", false)]
    [InlineData("2026-02-30", false)]
    public void OnlyAbsoluteValidDates(string date, bool expected) => Assert.Equal(expected, Workflow.Date(date));
    [Fact, Trait("Kind", "unit")]
    public void ChangedFixtureFails()
    {
        var path = Work("tampered"); Fixtures.Generate(path, path + "-oracle"); File.AppendAllText(Path.Combine(path, "case-001/transcript.md"), "changed"); Assert.Throws<InvalidDataException>(() => Fixtures.Verify(path));
    }
    private static async Task<Draft> Prepared(string path, string caseId)
    {
        var draft = await Workflow.Prepare(path, caseId); Assert.Equal("ready_for_review", draft.Status); Assert.False(File.Exists(Path.Combine(path, "import.json")));
        return draft;
    }
    private static void Reviewed(string path, Draft draft)
    {
        foreach (var candidate in draft.Candidates) Workflow.Review(path, candidate.Id, candidate.Disposition == "agreed" ? "approve" : "reject", "synthetic-reviewer", "Checked the cited transcript item");
    }
    private static async Task Scenario(int number)
    {
        await Bootstrap.Ready(); var caseId = $"case-{number:000}"; var path = Work(caseId);
        Assert.False(File.Exists(Path.Combine(path, "turn.json")));
        var draft = await Prepared(path, caseId);
        var expected = Storage.Read<JsonElement>("/oracle/expected.json").GetProperty(caseId);
        var candidate = draft.Candidates.Single(c => c.Id == expected.GetProperty("candidate").GetString());
        Assert.Equal(expected.GetProperty("owner").GetString(), candidate.Owner); Assert.Equal(expected.GetProperty("due_date").GetString(), candidate.DueDate); Assert.Equal(expected.GetProperty("disposition").GetString(), candidate.Disposition);
        Assert.Equal("proposed", draft.Candidates.Single(c => c.Id.StartsWith("proposal_", StringComparison.Ordinal)).Disposition);
        await Assert.ThrowsAsync<FileNotFoundException>(() => Ledger.Import(path)); Assert.False(File.Exists(Path.Combine(path, "import.json")));
        Reviewed(path, draft); await Ledger.Import(path);
        var journal = Storage.Read<ImportJournal>(Path.Combine(path, "import.json"));
        if (expected.GetProperty("approved").GetBoolean())
        {
            Assert.NotNull(journal.Version); Assert.True(journal.PriorPin > 0);
            await Ledger.Correct(path, candidate.Id, "2026-10-01", "synthetic-reviewer", "Human-approved revised project date");
            await Ledger.Status(path);
            var result = Storage.Read<JsonElement>(Path.Combine(path, "ledger.json"));
            var prior = result.GetProperty("prior").GetProperty("facts").EnumerateArray().ToArray();
            var current = result.GetProperty("current").GetProperty("facts").EnumerateArray().ToArray();
            Assert.Equal(candidate.DueDate, prior.Single(c => c.GetProperty("key").GetString() == "due_date").GetProperty("value").GetString());
            Assert.Equal("2026-10-01", current.Single(c => c.GetProperty("key").GetString() == "due_date").GetProperty("value").GetString());
            Assert.Equal(3, current.Length); Assert.All(current, c => Assert.Equal(candidate.Id, c.GetProperty("subject").GetString()));
            Assert.Single(result.GetProperty("promises").EnumerateArray());
            await using var reader = Bootstrap.Reader(); var head = await reader.Query.HeadAsync(journal.Version!);
            await Ledger.Import(path); await Ledger.Correct(path, candidate.Id, "2026-10-01", "synthetic-reviewer", "Human-approved revised project date"); Assert.Equal(head, await reader.Query.HeadAsync(journal.Version!));
        }
        else { Assert.Null(journal.Version); Assert.Empty(journal.Commands); await Ledger.Status(path); }
        Storage.Save(Work(caseId + ".quality.json"), new { caseId, passed = true, draft.InputHash, draft.Result.Completion, accepted = expected.GetProperty("approved").GetBoolean(), priorPin = journal.PriorPin });
    }
    [Theory, Trait("Kind", "business")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public Task IndependentReviewedBusinessCase(int number) => Scenario(number);
    [Fact, Trait("Kind", "failure")]
    public async Task QueryIdentityCannotWrite()
    {
        var grant = Storage.Read<Grant>("/credentials/query.json"); await using var api = Bootstrap.Client(grant.Token, grant.Uid);
        await Assert.ThrowsAsync<ForbiddenException>(() => api.Commands.CreateVersionAsync());
        await Assert.ThrowsAsync<ForbiddenException>(() => api.Runbooks.ApplyShapeAsync("invalid"));
    }
    [Fact, Trait("Kind", "failure")]
    public async Task CompletedDraftReusedAndReviewImmutable()
    {
        var path = Work("reuse"); var draft = await Prepared(path, "case-001"); var count = await Calls(); await Workflow.Prepare(path, "case-001"); Assert.Equal(count, await Calls());
        Reviewed(path, draft); Assert.Throws<InvalidDataException>(() => Workflow.Review(path, "commitment_001", "reject", "other", "Changed decision"));
        await Assert.ThrowsAsync<InvalidDataException>(() => Workflow.Prepare(path, "case-002"));
    }
    [Fact, Trait("Kind", "failure")]
    public async Task LostTurnUsesTranscriptWithoutReplay()
    {
        var path = Work("lost-turn"); await Fault("drop-turn");
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => Workflow.Prepare(path, "case-002", endpoint: "http://faults:11435")); }
        finally { await Fault("normal"); }
        var count = await Calls(); Assert.Equal("ready_for_review", (await Workflow.Prepare(path, "case-002", recover: true)).Status); Assert.Equal(count, await Calls());
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ProviderOutageRemainsUncertain()
    {
        var path = Work("outage"); await Mode("unavailable");
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => Workflow.Prepare(path, "case-003")); } finally { await Mode("ok"); }
        var count = await Calls(); await Assert.ThrowsAsync<InvalidDataException>(() => Workflow.Prepare(path, "case-003", recover: true)); Assert.Equal(count, await Calls());
    }
    [Fact, Trait("Kind", "failure")]
    public async Task UnservedCitationCannotBeReviewed()
    {
        var path = Work("bad-citation"); await Mode("bad-citation");
        try { Assert.Equal("unverified", (await Workflow.Prepare(path, "case-004")).Status); Assert.Throws<InvalidDataException>(() => Workflow.Review(path, "commitment_004", "approve", "reviewer", "Review")); } finally { await Mode("ok"); }
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ProcessCrashAfterTurn()
    {
        var path = Work("crash-turn"); Assert.Equal(71, await Child("crash-turn", path, "case-005")); var count = await Calls(); Assert.Equal("ready_for_review", (await Workflow.Prepare(path, "case-005", recover: true)).Status); Assert.Equal(count, await Calls());
    }
    [Fact, Trait("Kind", "failure")]
    public async Task ProcessCrashAfterClaim()
    {
        var path = Work("crash-import"); Reviewed(path, await Prepared(path, "case-006")); Assert.Equal(72, await Child("crash-import", path));
        await Ledger.Reconcile(path, "commitment_006-owner"); await Ledger.Import(path);
        var journal = Storage.Read<ImportJournal>(Path.Combine(path, "import.json")); await using var api = Bootstrap.Reader(); Assert.Equal(3, (await api.Query.FactsAsync(journal.Version!)).Facts.Count);
    }
    [Theory, Trait("Kind", "failure")]
    [InlineData("drop-claim", "commitment_001-owner")]
    [InlineData("drop-promise", "commitment_001-promise")]
    public async Task LostCommandResponse(string mode, string name)
    {
        var path = Work(mode); Reviewed(path, await Prepared(path, "case-001")); await Fault(mode);
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => Ledger.Import(path, endpoint: "http://faults:11435")); } finally { await Fault("normal"); }
        await Assert.ThrowsAsync<InvalidDataException>(() => Ledger.Import(path)); await Ledger.Reconcile(path, name); await Ledger.Import(path);
        var journal = Storage.Read<ImportJournal>(Path.Combine(path, "import.json")); await using var api = Bootstrap.Reader(); Assert.Equal(3, (await api.Query.FactsAsync(journal.Version!)).Facts.Count); Assert.Single(await api.Query.PromisesAsync(journal.Version!));
    }
    [Fact, Trait("Kind", "prepare-restart")]
    public async Task SavePendingReviewedImport()
    {
        var path = Work("restart"); Reviewed(path, await Prepared(path, "case-002")); Storage.Save(Work("restart-path.json"), path);
    }
    [Fact, Trait("Kind", "restarted")]
    public async Task ResumeReviewAfterServerRestart()
    {
        await Bootstrap.Ready(); var path = Storage.Read<string>(Path.Combine(Directory.GetParent(Root)!.FullName, "controlled/restart-path.json")); var calls = await Calls(); await Ledger.Import(path); await Ledger.Status(path); Assert.Equal(calls, await Calls());
    }
    public static IEnumerable<object[]> CloudCases()
    {
        var provider = Environment.GetEnvironmentVariable("MEETINGS_PROVIDER") ?? "openai";
        return (provider switch { "openai" => new[] { 1, 3, 5 }, "anthropic" => [2, 4, 6], "openrouter" => [7, 8], _ => throw new InvalidDataException("Unknown provider") }).Select(n => new object[] { n });
    }
    [Theory, MemberData(nameof(CloudCases)), Trait("Kind", "cloud")]
    public async Task FreshOnlineReviewWorkflow(int number)
    {
        if (Environment.GetEnvironmentVariable("MEETINGS_PROVIDER") == "openrouter") await Task.Delay(TimeSpan.FromSeconds(60));
        await Scenario(number);
    }
}
