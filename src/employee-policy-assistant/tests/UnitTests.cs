// SPDX-License-Identifier: Apache-2.0
using Ioka.Munarium.Client;
using Policy.Core;
using Policy.Harness;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Policy.Tests;

[Trait("Kind", "unit")]
public sealed class UnitTests
{
    [Fact]
    public void CorpusIsByteIdenticalAndManifestVerified()
    {
        var root = Path.Combine(Path.GetTempPath(), "policy-generate-" + Guid.NewGuid().ToString("N"));
        Fixtures.Generate(root + "/a", root + "/oracle-a"); Fixtures.Generate(root + "/b", root + "/oracle-b");
        Fixtures.Verify(root + "/a"); Fixtures.Verify(root + "/b");
        foreach (var file in Directory.GetFiles(root + "/a", "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(root + "/b", Path.GetRelativePath(root + "/a", file))));
        Assert.Equal(File.ReadAllBytes(root + "/oracle-a/cases.json"), File.ReadAllBytes(root + "/oracle-b/cases.json"));
        Assert.Equal(7, Directory.GetFiles(root + "/a", "*.txt", SearchOption.AllDirectories).Length);
        Assert.False(File.Exists(root + "/a/cases.json"));
        File.AppendAllText(root + "/a/public/leave.txt", "tamper");
        Assert.Throws<InvalidDataException>(() => Fixtures.Verify(root + "/a"));
    }
    [Fact] public void FollowUpIsBoundedAndContainsOnlyUserQuestion() => Assert.Equal(600, PolicySession.BuildQuery("next", new string('a', 3000)).Count(c => c == 'a'));
    [Theory] [InlineData(0)] [InlineData(2001)]
    public void InvalidQuestionsAreRejected(int size) => Assert.Throws<ArgumentException>(() => PolicySession.BuildQuery(new string('x', size), null));
    [Fact]
    public void UnservedCitationsAndMissingCitationsAreRejected()
    {
        var hit = new TurnHit { Collection = "public", ChunkId = "1", SourceId = "s", SourcePath = "policy.txt", SourceContentHash = "hash", Text = "policy" };
        var result = new TurnResult { SessionId = "s", Hits = [hit], Completion = new() { Provider = "fixture", Model = "test", Text = "claim [secret/2]" } };
        Assert.Throws<InvalidDataException>(() => Evidence.Validate(result));
        Assert.Throws<InvalidDataException>(() => Evidence.Validate(result with { Completion = result.Completion with { Text = "claim" } }));
        Evidence.Validate(result with { Completion = result.Completion with { Text = "claim [public/1]" } });
    }
    [Fact] public void UncertainAnswerCannotBeExported() => Assert.Throws<InvalidOperationException>(() => Evidence.Export(new("employee", "s", "q", "uncertain", null, [])));
    [Fact]
    public async Task RefreshCannotChangeIdentity()
    {
        await using var app = new PolicySession("http://unused", Path.GetTempPath());
        var grant = new IdentityGrant("employee", "uid", "test-token", "2026-01-15", "r@1", "revision", []);
        await app.SelectIdentityAsync(grant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.RefreshAsync(grant with { Uid = "hr" }));
        Assert.Equal("uid", app.Identity!.Uid);
    }
}
