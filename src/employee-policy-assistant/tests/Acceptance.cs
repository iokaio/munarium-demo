// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;
using Ioka.Munarium.Client;
using Policy.Core;
using Policy.Harness;
using Xunit;

namespace Policy.Tests;

public static class Acceptance
{
    public static string Work => Environment.GetEnvironmentVariable("POLICY_REPORT_DIR") ?? "/work/controlled";
    public static string Credentials => Environment.GetEnvironmentVariable("POLICY_CREDENTIALS") ?? "/credentials";
    public static IdentityGrant Grant(string identity) => Storage.Read<IdentityGrant>(Path.Combine(Credentials, identity + ".json"));
    public static PolicySession App(string suffix) => new(Bootstrap.Endpoint, Path.Combine(Work, suffix));
    public static async Task Run(string caseId)
    {
        var scenario = Storage.Read<Scenario[]>("/oracle/cases.json").Single(s => s.Id == caseId);
        var grant = Grant(scenario.Identity);
        await using var app = App(caseId);
        await app.SelectIdentityAsync(grant);
        var result = await app.AskAsync(scenario.Question, grant.Models.Single(), workId: Guid.NewGuid().ToString("N"));
        Assert.Equal("complete", result.Status);
        var completion = Assert.IsType<TurnCompletion>(result.Result!.Completion);
        Assert.Equal(grant.Models[0].Family, completion.Provider); Assert.Equal(grant.Models[0].Model, completion.Model); Assert.True(completion.WasOverride);
        Assert.Contains(result.Progress, p => p.Stage == "expansion");
        Assert.Contains(result.Progress, p => p.Stage == "expansion" && p.Model == completion.Model && p.Provider == completion.Provider);
        Assert.Contains(result.Progress, p => p.Stage == "completion" && p.Model == completion.Model && p.Provider == completion.Provider);
        var compact = completion.Text.Replace(",", "");
        foreach (var expected in scenario.Required) Assert.Contains(expected, compact, StringComparison.OrdinalIgnoreCase);
        foreach (var forbidden in scenario.Forbidden)
        {
            Assert.DoesNotContain(forbidden, compact, StringComparison.OrdinalIgnoreCase);
            Assert.All(result.Result.Hits, h => Assert.DoesNotContain(forbidden, h.Text, StringComparison.OrdinalIgnoreCase));
        }
        if (scenario.Missing) Assert.Matches(new Regex(@"not (available|provided|specified|establish|include|contain|state|covered)|do not|does not|don't|doesn't|cannot|can't|insufficient|no (information|policy|evidence|details)", RegexOptions.IgnoreCase), completion.Text);
        Assert.All(result.Result.Hits, h =>
        {
            var region = h.SourcePath.Split('/')[1];
            Assert.True(region == "public" || region == scenario.Identity, "Source outside the identity's permitted collection.");
            Assert.Equal(Storage.Hash(File.ReadAllText(Path.Combine("/inputs", string.Join('/', h.SourcePath.Split('/').Skip(1))))), h.SourceContentHash);
        });
        app.Export(Path.Combine(Work, caseId + ".txt"));
        Storage.Save(Path.Combine(Work, caseId + ".quality.json"), new { caseId, scenario.Identity, passed = true, completion.Provider, completion.Model, completion.InputTokens, completion.OutputTokens, expansion = result.Progress.Where(p => p.Stage == "expansion"), session = result.SessionId, hits = result.Result.Hits.Count, result.Recovered });
    }
}

[Trait("Kind", "model")]
public sealed class ModelTests
{
    [Theory]
    [InlineData("equipment")] [InlineData("training")] [InlineData("west")] [InlineData("wrong-region")]
    [InlineData("hr-denied")] [InlineData("hr-allowed")] [InlineData("leave")] [InlineData("missing")]
    public Task CompleteCorpus(string id) => Acceptance.Run(id);
}
[Trait("Kind", "cloud")]
public sealed class CloudTests
{
    [Theory] [InlineData("equipment")] [InlineData("training")]
    public Task Openai(string id) => Acceptance.Run(id);
    [Theory] [InlineData("west")] [InlineData("wrong-region")]
    public Task Anthropic(string id) => Acceptance.Run(id);
    [Theory] [InlineData("hr-denied")] [InlineData("hr-allowed")]
    public Task Openrouter(string id) => Acceptance.Run(id);
}
