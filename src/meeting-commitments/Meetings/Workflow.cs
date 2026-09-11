// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ioka.Munarium.Client;

namespace Meetings;

public static class Workflow
{
    public static bool Date(string? value) => value is not null && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public static async Task<Draft> Prepare(string work, string caseId, bool recover = false, bool crash = false, string? endpoint = null)
    {
        using var lease = Storage.Lease(work);
        var manifest = Fixtures.Verify("/inputs"); var grant = Storage.Read<Grant>("/credentials/query.json");
        Storage.Require(grant.Runbooks.ContainsKey(caseId), "Unknown project case");
        var inputHash = manifest.Files[caseId + "/transcript.md"]; var runbook = grant.Runbooks[caseId];
        var key = Storage.Hash(inputHash + runbook + grant.Uid + grant.Provider + grant.Model);
        var path = Path.Combine(work, "turn.json"); var query = "Extract all reviewed candidates and unagreed proposals for fictional project " + caseId + ".";
        await using var api = Bootstrap.Client(grant.Token, grant.Uid, endpoint);
        TurnJournal journal;
        if (File.Exists(path))
        {
            journal = Storage.Read<TurnJournal>(path); Storage.Require(journal.Key == key, "Changed input or configuration requires new work");
            if (journal.Result is null)
            {
                Storage.Require(recover && journal.SessionId is not null, "Uncertain turn requires explicit recovery; no replay");
                var transcript = await api.Sessions.GetAsync(journal.SessionId!);
                Storage.Require(transcript.Uid == grant.Uid && transcript.RunbookRef == runbook, "Transcript identity mismatch");
                var matches = transcript.Turns.Where(t => t.Query == journal.Query && t.Completion is not null).ToArray();
                Storage.Require(matches.Length == 1, "No uniquely recoverable completion");
                var turn = matches[0]; var completion = turn.Completion!.Value;
                journal.Result = new TurnResult { SessionId = transcript.SessionId, Ordinal = turn.Ordinal, CollectionsSearched = turn.CollectionsSearched, Hits = turn.Hits?.Deserialize<TurnHit[]>() ?? [], Envelopes = turn.Envelope?.Deserialize<CollectionEnvelope[]>() ?? [], Completion = new() { Text = completion.GetProperty("text").GetString()!, Provider = completion.GetProperty("provider").GetString()!, Model = completion.GetProperty("model").GetString()!, WasOverride = completion.GetProperty("resolved").GetProperty("was_override").GetBoolean(), InputTokens = completion.GetProperty("input_tokens").GetUInt64(), OutputTokens = completion.GetProperty("output_tokens").GetUInt64(), Verification = completion.TryGetProperty("verification", out var v) ? v.Deserialize<TurnVerification>() : null } };
                journal.Recovered = true;
            }
        }
        else
        {
            journal = new() { Key = key, Runbook = runbook, Query = query }; Storage.Save(path, journal);
            journal.SessionId = (await api.Sessions.CreateAsync(runbook)).SessionId; journal.State = "uncertain"; Storage.Save(path, journal);
            await foreach (var item in api.Sessions.TurnStreamAsync(journal.SessionId, new TurnRequest { Query = query, Complete = true }))
            {
                if (item is TurnStreamEvent.Progress progress) { journal.Progress.Add(Storage.Element(progress.Event)); Storage.Save(path, journal); }
                if (item is TurnStreamEvent.Done done) { if (crash) Environment.Exit(71); journal.Result = done.Response; }
            }
            Storage.Require(journal.Result is not null, "Stream ended without a result");
        }
        journal.State = "response_saved"; Storage.Save(path, journal);
        var result = journal.Result!; var errors = new List<string>(); Candidate[] candidates = [];
        try
        {
            Storage.Require(result.Completion is not null, "No completion");
            var completion = result.Completion!;
            Storage.Require(completion.Provider == (grant.Provider == "fixture" ? "ollama" : grant.Provider) && completion.Model == grant.Model, "Unexpected model identity");
            Storage.Require(completion.Verification is null || completion.Verification.Violations.Count == 0, "Unresolved verification violations");
            var raw = completion.Text.Trim(); if (raw.StartsWith("```json\n", StringComparison.Ordinal) && raw.EndsWith("\n```", StringComparison.Ordinal)) raw = raw[8..^4];
            candidates = JsonSerializer.Deserialize<CandidateList>(raw, Storage.Json)?.Candidates ?? throw new InvalidDataException("Missing candidates");
            Storage.Require(candidates.Length == 2 && candidates.Select(c => c.Id).Distinct().Count() == 2, "Expected both transcript items");
            var labels = result.Hits.ToDictionary(h => h.Collection + "/" + h.ChunkId);
            foreach (var candidate in candidates)
            {
                Storage.Require(Regex.IsMatch(candidate.Id, "^(commitment|proposal)_[0-9]{3}$") && candidate.Id.EndsWith(caseId[5..], StringComparison.Ordinal), "Invalid candidate identity");
                Storage.Require(candidate.Description.Length is > 0 and <= 300 && candidate.Quote.Length is > 0 and <= 600, "Invalid candidate text");
                Storage.Require(candidate.Disposition is "agreed" or "proposed" or "ambiguous", "Unknown disposition");
                var block = Regex.Match(File.ReadAllText("/inputs/" + caseId + "/transcript.md"), "(?ms)^Item " + Regex.Escape(candidate.Id) + "\n(?<body>.*?)(?=\n\nItem |\n\nNotes:|\\z)").Groups["body"].Value;
                string Field(string name) => Regex.Match(block, "(?m)^" + name + ": (.+)$").Groups[1].Value;
                Storage.Require(candidate.Description == Field("Description") && candidate.Quote == Field("Evidence") && candidate.Disposition == Field("Decision"), "Extracted text or decision differs from its transcript item");
                Storage.Require(candidate.Owner == (Field("Owner") == "unassigned" ? null : Field("Owner")) && candidate.DueDate == (Date(Field("Due")) ? Field("Due") : null), "Owner or due date differs from its source item");
                Storage.Require(candidate.Citations.Length > 0 && candidate.Citations.All(labels.ContainsKey), "Unserved citation");
                Storage.Require(candidate.Citations.Any(c => labels[c].Text.Contains(candidate.Quote, StringComparison.Ordinal)), "Quote does not resolve to cited evidence");
                Storage.Require(candidate.Owner is null || (Regex.IsMatch(candidate.Owner, "^Person[0-9]{3}$") && result.Hits.Any(h => h.Text.Contains("Owner: " + candidate.Owner, StringComparison.Ordinal))), "Unknown owner");
                Storage.Require(candidate.DueDate is null || (Date(candidate.DueDate) && result.Hits.Any(h => h.Text.Contains("Due: " + candidate.DueDate, StringComparison.Ordinal))), "Unresolved or invented date");
                Storage.Require(candidate.Disposition != "agreed" || (candidate.Owner is not null && Date(candidate.DueDate)), "Agreed candidate needs explicit owner and date");
            }
            Storage.Require(result.Hits.Count > 0 && result.Hits.All(h => h.SourceContentHash == inputHash && h.SourcePath == runbook.Split('@')[0] + "/transcript.md"), "Source scope or hash mismatch");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NullReferenceException or ArgumentException) { errors.Add(error.Message); }
        var draft = new Draft(caseId, inputHash, errors.Count == 0 ? "ready_for_review" : "unverified", candidates, errors.ToArray(), result, runbook, journal.Recovered);
        Storage.Save(Path.Combine(work, "draft.json"), draft);
        var text = $"MEETING COMMITMENT REVIEW | fictional {caseId}\nStatus: {draft.Status}; ledger writes: none before explicit import\n";
        foreach (var candidate in candidates) text += $"\n{candidate.Id}: {candidate.Description}\nDisposition: {candidate.Disposition} | owner: {candidate.Owner ?? "unresolved"} | due: {candidate.DueDate ?? "unresolved"}\nEvidence: {candidate.Quote}\nCitations: {string.Join(", ", candidate.Citations)}\n";
        if (errors.Count > 0) text += "\nValidation errors: " + string.Join("; ", errors);
        Storage.Write(Path.Combine(work, "review.txt"), text);
        return draft;
    }
    public static void Review(string work, string candidateId, string decision, string reviewer, string reason)
    {
        using var lease = Storage.Lease(work); var draftPath = Path.Combine(work, "draft.json"); var draft = Storage.Read<Draft>(draftPath);
        Storage.Require(draft.Status == "ready_for_review", "Only validated candidates can be reviewed");
        var candidate = draft.Candidates.Single(c => c.Id == candidateId);
        Storage.Require(decision is "approve" or "reject" && !string.IsNullOrWhiteSpace(reviewer) && !string.IsNullOrWhiteSpace(reason), "Explicit decision, reviewer and reason required");
        Storage.Require(decision == "reject" || candidate.Disposition == "agreed" && candidate.Owner is not null && Date(candidate.DueDate), "Ambiguous or proposed item cannot be imported as agreed");
        var review = new Review(Storage.Hash(File.ReadAllBytes(draftPath)), candidateId, decision, reviewer, reason);
        var path = Path.Combine(work, candidateId + ".review.json");
        if (File.Exists(path)) Storage.Require(Storage.Read<Review>(path) == review, "Review is immutable; create new work for a new decision");
        else Storage.Save(path, review);
    }
}
