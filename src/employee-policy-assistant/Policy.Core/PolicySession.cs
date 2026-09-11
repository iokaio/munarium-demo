// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Ioka.Munarium.Client;

namespace Policy.Core;

/// <summary>One desktop identity at a time; no issuer or provider keys enter this class.</summary>
public sealed class PolicySession : IAsyncDisposable
{
    private MunariumClient? client;
    private string? journal;
    private readonly string endpoint;
    private readonly string work;
    private readonly Func<MunariumClientOptions, MunariumClient> factory;
    public IdentityGrant? Identity { get; private set; }
    public string? SessionId { get; private set; }
    public AnswerRecord? Answer { get; private set; }
    public bool Busy { get; private set; }
    public event Action<TurnProgressEvent>? Progress;

    public PolicySession(string endpoint, string work, Func<MunariumClientOptions, MunariumClient>? factory = null)
    {
        this.endpoint = endpoint; this.work = work;
        this.factory = factory ?? (options => MunariumClient.Rest(options));
    }
    public async Task SelectIdentityAsync(IdentityGrant grant)
    {
        if (Busy) throw new InvalidOperationException("Wait for the active turn before switching identity.");
        if (client is not null)
        {
            // An uncertain session must remain open for the previous identity to reconcile.
            if (SessionId is not null && Answer?.Status != "uncertain")
            {
                try { await client.Sessions.CloseAsync(SessionId); }
                catch (UnauthenticatedException) { /* Expired capability cannot close its old session. */ }
            }
            await client.DisposeAsync();
        }
        Identity = grant; SessionId = null; Answer = null; journal = null;
        client = factory(new MunariumClientOptions { Endpoint = endpoint, Token = grant.Token, Uid = grant.Uid });
        LoadPending();
    }
    public async Task RefreshAsync(IdentityGrant grant)
    {
        if (Busy || Identity is null || grant.Uid != Identity.Uid || grant.Runbook != Identity.Runbook || grant.Revision != Identity.Revision)
            throw new InvalidOperationException("Refresh must preserve the same identity and configuration.");
        if (client is not null) await client.DisposeAsync();
        Identity = grant;
        client = factory(new MunariumClientOptions { Endpoint = endpoint, Token = grant.Token, Uid = grant.Uid });
    }
    public static string BuildQuery(string question, string? previousQuestion)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 2000) throw new ArgumentException("Enter a question of 1–2000 characters.");
        // Carry only a bounded user question. Generated answers never become source evidence.
        return previousQuestion is null ? question.Trim() : $"Previous user question (context only): {previousQuestion[..Math.Min(previousQuestion.Length, 600)]}\nCurrent question: {question.Trim()}";
    }
    public async Task<AnswerRecord> AskAsync(string question, ModelChoice model, bool followUp = false, string? workId = null, CancellationToken ct = default)
    {
        if (Busy || Identity is null || client is null) throw new InvalidOperationException("Select an identity and wait for the current turn.");
        if (Answer?.Status == "uncertain") throw new InvalidOperationException("Reconcile the interrupted turn first.");
        if (!Identity.Models.Contains(model)) throw new InvalidOperationException("Select an allowed model.");
        var query = BuildQuery(question, followUp ? Answer?.Query : null);
        journal = Path.Combine(work, Storage.Hash(Identity.Uid + Identity.Revision), Storage.Hash(workId ?? Guid.NewGuid().ToString("N")) + ".json");
        if (File.Exists(journal))
        {
            Answer = Storage.Read<AnswerRecord>(journal);
            if (Answer.Query != query || Answer.Uid != Identity.Uid) throw new InvalidOperationException("Work identity already belongs to another query.");
            SessionId = Answer.SessionId;
            return Answer;
        }
        Busy = true;
        try
        {
            SessionId ??= (await client.Sessions.CreateAsync(Identity.Runbook, ct)).SessionId;
            Answer = new(Identity.Uid, SessionId, query, "uncertain", null, [], RequestedModel: model);
            Storage.Save(journal, Answer);
            var progress = new List<TurnProgressEvent>();
            await foreach (var item in client.Sessions.TurnStreamAsync(SessionId, new TurnRequest
            {
                Query = query, Complete = true, TopK = 6,
                ModelOverride = new() { Provider = model.Provider, Model = model.Model }
            }, ct))
            {
                if (item is TurnStreamEvent.Progress p) { progress.Add(p.Event); Progress?.Invoke(p.Event); }
                if (item is TurnStreamEvent.Done done)
                {
                    Answer = new(Identity.Uid, SessionId, query, "unverified", done.Response, progress.ToArray(), RequestedModel: model);
                    Storage.Save(journal, Answer);
                    Evidence.Validate(done.Response);
                    CheckModel(done.Response, model);
                    Answer = Answer with { Status = "complete" };
                    Storage.Save(journal, Answer);
                }
            }
            return Answer;
        }
        catch (UnauthenticatedException)
        {
            // Server authentication refuses the request before dispatching provider work.
            // Renewal may allow an explicit new attempt; a transport loss remains uncertain.
            if (journal is not null && Answer?.Status == "uncertain")
            {
                Answer = Answer with { Status = "rejected" };
                Storage.Save(journal, Answer);
            }
            throw;
        }
        finally { Busy = false; }
    }
    public async Task<AnswerRecord?> ReconcileAsync()
    {
        if (Busy || client is null || Identity is null) throw new InvalidOperationException("Select an idle identity first.");
        if (Answer is null) LoadPending();
        if (Answer is null) return null;
        if (Answer.Status != "uncertain") return Answer;
        var transcript = await client.Sessions.GetAsync(Answer.SessionId);
        if (transcript.Uid != Identity.Uid) throw new InvalidDataException("Transcript identity mismatch.");
        var matches = transcript.Turns.Where(t => t.Query == Answer.Query && t.Completion is not null).ToArray();
        if (matches.Length != 1) return Answer;
        var turn = matches[0];
        var completion = turn.Completion!.Value;
        var resolved = completion.GetProperty("resolved");
        if (Answer.RequestedModel is not null && resolved.GetProperty("provider").GetString() != Answer.RequestedModel.Provider)
            throw new InvalidDataException("Transcript resolved an unexpected named provider configuration.");
        var result = new TurnResult
        {
            SessionId = Answer.SessionId, Ordinal = turn.Ordinal, CollectionsSearched = turn.CollectionsSearched,
            Hits = turn.Hits?.Deserialize<TurnHit[]>() ?? [],
            Envelopes = turn.Envelope?.Deserialize<CollectionEnvelope[]>() ?? [],
            Completion = new() { Text = completion.GetProperty("text").GetString()!, Provider = completion.GetProperty("provider").GetString()!, Model = completion.GetProperty("model").GetString()!, WasOverride = resolved.GetProperty("was_override").GetBoolean(), InputTokens = completion.GetProperty("input_tokens").GetUInt64(), OutputTokens = completion.GetProperty("output_tokens").GetUInt64(), Verification = completion.TryGetProperty("verification", out var verification) ? verification.Deserialize<TurnVerification>() : null }
        };
        Evidence.Validate(result);
        if (Answer.RequestedModel is not null) CheckModel(result, Answer.RequestedModel);
        Answer = Answer with { Status = "complete", Result = result, Recovered = true, StoredCompletion = completion.Clone() };
        Storage.Save(journal!, Answer);
        return Answer;
    }
    public void Export(string path)
    {
        if (Answer is null) throw new InvalidOperationException("Ask a question first.");
        Storage.Write(path, Evidence.Export(Answer));
        // Recovered transcripts do not preserve skipped collections or complete live progress.
        Storage.Save(path + ".json", new { Answer.Uid, Answer.SessionId, Answer.Query, Answer.Status, Answer.Recovered, Answer.RequestedModel, Answer.StoredCompletion, Answer.Result?.Hits, Answer.Result?.Envelopes, Answer.Result?.Completion, liveProgress = Answer.Recovered ? null : Answer.Progress });
    }
    private void LoadPending()
    {
        if (Identity is null) return;
        var directory = Path.Combine(work, Storage.Hash(Identity.Uid + Identity.Revision));
        var pending = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Where(p => Storage.Read<AnswerRecord>(p).Status == "uncertain").Order().ToArray() : [];
        if (pending.Length == 0) return;
        journal = pending[0]; Answer = Storage.Read<AnswerRecord>(journal); SessionId = Answer.SessionId;
    }
    private static void CheckModel(TurnResult result, ModelChoice model)
    {
        if (result.Completion?.Provider != model.Family || result.Completion.Model != model.Model || !result.Completion.WasOverride)
            throw new InvalidDataException("Actual completion differs from the requested provider/model.");
    }
    public async ValueTask DisposeAsync() { if (client is not null) await client.DisposeAsync(); }
}
