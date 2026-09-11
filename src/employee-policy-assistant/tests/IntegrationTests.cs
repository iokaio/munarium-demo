// SPDX-License-Identifier: Apache-2.0
using System.Net;
using System.Text;
using System.Text.Json;
using Ioka.Munarium.Client;
using Policy.Core;
using Policy.Harness;
using Xunit;

namespace Policy.Tests;

[Trait("Kind", "integration")]
public sealed class IntegrationTests
{
    private static readonly HttpClient Fixture = new() { BaseAddress = new Uri("http://provider-fixture:11434") };
    private static async Task<int> Calls() => JsonDocument.Parse(await Fixture.GetStringAsync("/calls")).RootElement.GetArrayLength();
    [Fact]
    public async Task FollowUpUsesBoundedUserContextInTheSameIdentitySession()
    {
        var grant = Acceptance.Grant("employee");
        await using var app = Acceptance.App("follow-up-" + Guid.NewGuid().ToString("N"));
        await app.SelectIdentityAsync(grant);
        var first = await app.AskAsync("What is the equipment allowance?", grant.Models[0]);
        var next = await app.AskAsync("Who approves it?", grant.Models[0], followUp: true);
        Assert.Equal(first.SessionId, next.SessionId);
        Assert.Contains("Previous user question (context only): What is the equipment allowance?", next.Query);
        Assert.DoesNotContain(first.Result!.Completion!.Text, next.Query);
        Assert.Contains("manager", next.Result!.Completion!.Text);
    }
    [Fact]
    public async Task DeniedOverrideMakesNoExpansionOrCompletionCall()
    {
        var grant = Acceptance.Grant("employee");
        await using var client = MunariumClient.Rest(new() { Endpoint = Bootstrap.Endpoint, Token = grant.Token, Uid = grant.Uid });
        var session = await client.Sessions.CreateAsync(grant.Runbook);
        var before = await Calls();
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            await foreach (var _ in client.Sessions.TurnStreamAsync(session.SessionId, new() { Query = "equipment", Complete = true, ModelOverride = new() { Provider = "not-allowed" } })) { }
        });
        Assert.Equal(before, await Calls());
    }
    [Fact]
    public async Task IdentitySwitchClosesSessionAndClearsAnswers()
    {
        await using var app = Acceptance.App("identity-" + Guid.NewGuid().ToString("N"));
        var hr = Acceptance.Grant("hr"); var employee = Acceptance.Grant("employee");
        await app.SelectIdentityAsync(hr);
        var answer = await app.AskAsync("What is the confidential retention review code?", hr.Models[0]);
        Assert.Contains(Acceptance.ReadScenario("hr-allowed").Required[0], answer.Result!.Completion!.Text);
        await app.SelectIdentityAsync(employee);
        Assert.Null(app.Answer); Assert.Null(app.SessionId);
        await using var oldClient = MunariumClient.Rest(new() { Endpoint = Bootstrap.Endpoint, Token = hr.Token, Uid = hr.Uid });
        Assert.Equal("closed", (await oldClient.Sessions.GetAsync(answer.SessionId)).State);
        var restricted = await app.AskAsync("What is the confidential retention review code?", employee.Models[0]);
        Assert.DoesNotContain(Acceptance.ReadScenario("hr-allowed").Required[0], restricted.Result!.Completion!.Text);
        Assert.DoesNotContain(restricted.Result.Hits, h => h.SourcePath.Contains("/hr/"));
        await using var wrongUid = MunariumClient.Rest(new() { Endpoint = Bootstrap.Endpoint, Token = employee.Token, Uid = "policy-hr" });
        await Assert.ThrowsAsync<ForbiddenException>(() => wrongUid.Sessions.GetAsync(restricted.SessionId));
    }
    [Fact]
    public async Task CapabilityExpiryAndRenewalPreserveSubject()
    {
        var grant = Acceptance.Grant("employee");
        await using var issuer = Bootstrap.Ops(true);
        var shortGrant = await issuer.Tokens.MintAsync(grant.Uid, 0, ["query"], [], [grant.Runbook.Split('@')[0]], 1);
        await using var app = Acceptance.App("expiry-" + Guid.NewGuid().ToString("N"));
        await app.SelectIdentityAsync(grant with { Token = shortGrant.Token, ExpiresAt = shortGrant.ExpiresAt });
        var first = await app.AskAsync("equipment allowance", grant.Models[0]);
        // Server 1.1.1 permits 30 seconds of JWT clock skew; wait past the actual expiry plus that allowance.
        var remaining = DateTimeOffset.Parse(shortGrant.ExpiresAt).AddSeconds(32) - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        await Assert.ThrowsAsync<UnauthenticatedException>(() => app.AskAsync("training allowance", grant.Models[0]));
        Assert.Equal("rejected", app.Answer!.Status);
        await app.RefreshAsync(grant);
        Assert.Equal(grant.Uid, app.Identity!.Uid);
        var renewed = await app.AskAsync("training allowance", grant.Models[0]);
        Assert.Equal("complete", renewed.Status);
        Assert.Equal(first.SessionId, renewed.SessionId);
    }
    [Fact]
    public async Task CompletedWorkReusesResponseAfterRestart()
    {
        var path = Path.Combine(Acceptance.Work, "restart-" + Guid.NewGuid().ToString("N"));
        var grant = Acceptance.Grant("employee");
        await using (var first = new PolicySession(Bootstrap.Endpoint, path))
        {
            await first.SelectIdentityAsync(grant); await first.AskAsync("equipment allowance", grant.Models[0], workId: "same-work");
        }
        var before = await Calls();
        await using var second = new PolicySession(Bootstrap.Endpoint, path);
        await second.SelectIdentityAsync(grant);
        var restored = await second.AskAsync("equipment allowance", grant.Models[0], workId: "same-work");
        second.Export(Path.Combine(path, "restored.txt"));
        Assert.Equal("complete", restored.Status); Assert.Equal(before, await Calls());
        Assert.Contains(Acceptance.ReadScenario("equipment").Required[0], File.ReadAllText(Path.Combine(path, "restored.txt")));
    }
    [Fact]
    public async Task LostStreamResponseReconcilesWithoutNewPaidTurn()
    {
        var path = Path.Combine(Acceptance.Work, "disconnect-" + Guid.NewGuid().ToString("N"));
        var grant = Acceptance.Grant("employee");
        using var http = new HttpClient(new DropDoneHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        await using (var first = new PolicySession(Bootstrap.Endpoint, path, options => MunariumClient.Rest(options, http)))
        {
            await first.SelectIdentityAsync(grant);
            await Assert.ThrowsAsync<MunariumTransportException>(() => first.AskAsync("equipment allowance", grant.Models[0]));
            Assert.Equal("uncertain", first.Answer!.Status);
        }
        var before = await Calls();
        await using var recovered = new PolicySession(Bootstrap.Endpoint, path);
        await recovered.SelectIdentityAsync(grant);
        Assert.Equal("uncertain", recovered.Answer!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovered.AskAsync("equipment allowance", grant.Models[0]));
        var result = await recovered.ReconcileAsync();
        Assert.NotNull(result); Assert.True(result.Recovered); Assert.Equal("complete", result.Status);
        Assert.True(result.Result!.Completion!.WasOverride); Assert.Equal(grant.Models[0].Model, result.Result.Completion.Model);
        Assert.Equal(grant.Models[0].Family, result.Result.Completion.Provider);
        Assert.Equal(grant.Models[0].Provider, result.StoredCompletion!.Value.GetProperty("resolved").GetProperty("provider").GetString());
        recovered.Export(Path.Combine(path, "recovered.txt"));
        Assert.DoesNotContain("skipped", File.ReadAllText(Path.Combine(path, "recovered.txt.json")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await Calls());
    }
    [Fact]
    public async Task ProviderOutageLeavesUncertainWorkWithoutBlindRetry()
    {
        var grant = Acceptance.Grant("employee");
        await using var app = Acceptance.App("outage-" + Guid.NewGuid().ToString("N"));
        await app.SelectIdentityAsync(grant);
        await Fixture.GetStringAsync("/fail");
        try { await Assert.ThrowsAnyAsync<MunariumException>(() => app.AskAsync("equipment allowance", grant.Models[0])); }
        finally { await Fixture.GetStringAsync("/reset"); }
        Assert.Equal("uncertain", app.Answer!.Status);
        var before = await Calls();
        Assert.Equal("uncertain", (await app.ReconcileAsync())!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.AskAsync("equipment allowance", grant.Models[0]));
        Assert.Equal(before, await Calls());
    }
}

internal sealed class DropDoneHandler : DelegatingHandler
{
    public DropDoneHandler() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var cut = body.LastIndexOf("event: done", StringComparison.Ordinal);
            if (cut < 0) cut = body.LastIndexOf("event:done", StringComparison.Ordinal);
            if (cut < 0) throw new InvalidDataException("Fault fixture expected a real completed SSE response.");
            response.Content.Dispose(); response.Content = new StringContent(body[..cut], Encoding.UTF8, "text/event-stream");
        }
        return response;
    }
}
