// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Ioka.Munarium.Client;

namespace Meetings;

public static class Ledger
{
    private static string PathOf(string work) => Path.Combine(work, "import.json");
    private static void Save(string work, ImportJournal journal) => Storage.Save(PathOf(work), journal);
    public static async Task Import(string work, bool crash = false, string? endpoint = null)
    {
        using var lease = Storage.Lease(work);
        var draftPath = Path.Combine(work, "draft.json"); var draft = Storage.Read<Draft>(draftPath);
        Storage.Require(draft.Status == "ready_for_review", "Draft is not validated");
        var reviews = draft.Candidates.Select(c => Storage.Read<Review>(Path.Combine(work, c.Id + ".review.json"))).OrderBy(r => r.CandidateId, StringComparer.Ordinal).ToArray();
        Storage.Require(reviews.All(r => r.DraftHash == Storage.Hash(File.ReadAllBytes(draftPath))), "Review belongs to changed draft");
        var hash = Storage.Hash(JsonSerializer.Serialize(reviews, Storage.Json));
        var journal = File.Exists(PathOf(work)) ? Storage.Read<ImportJournal>(PathOf(work)) : new() { ReviewHash = hash };
        Storage.Require(journal.ReviewHash == hash, "Review decisions changed after import started");
        var approved = reviews.Where(r => r.Decision == "approve").Select(r => draft.Candidates.Single(c => c.Id == r.CandidateId)).ToArray();
        Storage.Require(approved.All(c => c.Disposition == "agreed" && c.Owner is not null && Workflow.Date(c.DueDate)), "Only explicit supported commitments can be imported");
        if (approved.Length == 0) { Save(work, journal); return; }
        await using var api = Bootstrap.Writer(endpoint);
        if (journal.Version is null)
        {
            Storage.Require(!journal.Commands.ContainsKey("version"), "Uncertain version creation needs operator investigation");
            var cmd = new Command { Kind = "version", IdempotencyKey = Guid.NewGuid().ToString("N"), Body = Storage.Element(new { application = "meeting-commitments", draft.Case, draft.InputHash, reviewHash = hash }) };
            journal.Commands["version"] = cmd; Save(work, journal);
            journal.Version = await api.Commands.CreateVersionAsync(metadata: cmd.Body, idempotencyKey: cmd.IdempotencyKey);
            cmd.State = "complete"; cmd.Response = Storage.Element(new { version = journal.Version }); Save(work, journal);
        }
        foreach (var candidate in approved)
        {
            foreach (var field in new[] { ("owner", candidate.Owner!), ("due_date", candidate.DueDate!), ("description", candidate.Description) })
            {
                await Propose(api, work, journal, candidate.Id + "-" + field.Item1, async () => new ClaimInput { Subject = candidate.Id, Key = field.Item1, Value = field.Item2, ExpectedHead = await api.Query.HeadAsync(journal.Version), ScopePath = draft.Case, Provenance = "backfilled", ShapeRef = "meeting-commitment@1", Evidence = Storage.Element(new { transcriptHash = draft.InputHash, reviewHash = hash, candidate.Quote, candidate.Citations }) }, crash);
            }
            var name = candidate.Id + "-promise";
            if (!journal.Commands.TryGetValue(name, out var command))
            {
                var key = Guid.NewGuid().ToString("N");
                command = new() { Kind = "promise", IdempotencyKey = key, Body = Storage.Element(new { key = candidate.Id + "-" + key, kind = "meeting", description = candidate.Description, origin_scope = draft.Case, due_scope = candidate.DueDate }) };
                journal.Commands[name] = command; Save(work, journal);
                var body = command.Body!.Value;
                var promise = await api.Commands.OpenPromiseAsync(journal.Version, body.GetProperty("key").GetString()!, "meeting", candidate.Description, draft.Case, candidate.DueDate, key);
                command.State = "complete"; command.Response = Storage.Element(promise); Save(work, journal);
            }
            Storage.Require(command.State == "complete", "Uncertain promise requires reconciliation");
        }
        journal.PriorPin ??= await api.Query.HeadAsync(journal.Version); Save(work, journal);
    }
    private static async Task Propose(MunariumClient api, string work, ImportJournal journal, string name, Func<Task<ClaimInput>> build, bool crash = false)
    {
        journal.Commands.TryGetValue(name, out var command);
        if (command?.State == "complete") return;
        Storage.Require(command is null || command.State == "head_conflict", "Uncertain or disputed claim requires review; no replay");
        var history = command?.Attempts ?? [];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var body = await build(); var key = Guid.NewGuid().ToString("N");
            body = body with { Evidence = Storage.Element(new { command_key = key, evidence = body.Evidence }) };
            command = new() { Kind = "claim", IdempotencyKey = key, Claim = body, Attempts = history };
            journal.Commands[name] = command; Save(work, journal);
            try
            {
                var response = await api.Commands.ProposeClaimAsync(journal.Version!, body, key);
                if (crash) Environment.Exit(72);
                command.Response = Storage.Element(response); command.State = response.IsDisputed ? "disputed" : "complete"; Save(work, journal);
                Storage.Require(!response.IsDisputed, "Governance dispute retained for review"); return;
            }
            catch (HeadConflictException)
            {
                history.Add(Storage.Element(new { key, body })); command.State = "head_conflict"; Save(work, journal);
            }
        }
        throw new InvalidOperationException("Repeated head conflicts require review");
    }
    public static async Task Reconcile(string work, string name)
    {
        using var lease = Storage.Lease(work); var journal = Storage.Read<ImportJournal>(PathOf(work));
        var command = journal.Commands[name]; Storage.Require(command.State == "uncertain" && journal.Version is not null, "No reconcilable ledger command");
        await using var api = Bootstrap.Writer();
        if (command.Kind == "claim")
        {
            var body = command.Claim!;
            var candidates = (await api.Query.FactsAsync(journal.Version!, statuses: ["accepted", "disputed"])).Facts.Where(c => c.Subject == body.Subject && c.Key == body.Key && c.Value == body.Value && c.SupersedesId == body.SupersedesId && c.Evidence is { } actual && body.Evidence is { } intended && JsonElement.DeepEquals(actual, intended)).ToArray();
            Storage.Require(candidates.Length == 1, "No uniquely matching command claim; remains uncertain");
            var claim = candidates[0]; command.Response = Storage.Element(new { claim, head_seq = claim.Seq }); command.State = claim.Status == "accepted" ? "complete" : "disputed";
        }
        else if (command.Kind == "promise")
        {
            var body = command.Body!.Value;
            var matches = (await api.Query.PromisesAsync(journal.Version!)).Where(p => p.Key == body.GetProperty("key").GetString() && p.Description == body.GetProperty("description").GetString()).ToArray();
            Storage.Require(matches.Length == 1, "No uniquely matching operation-marked promise");
            command.Response = Storage.Element(matches[0]); command.State = "complete";
        }
        else throw new InvalidDataException("Version creation requires operator investigation");
        Save(work, journal);
    }
    public static async Task Correct(string work, string candidateId, string date, string reviewer, string reason)
    {
        using var lease = Storage.Lease(work); Storage.Require(Workflow.Date(date) && !string.IsNullOrWhiteSpace(reviewer) && !string.IsNullOrWhiteSpace(reason), "Explicit ISO date, reviewer and reason required");
        var journal = Storage.Read<ImportJournal>(PathOf(work)); Storage.Require(journal.Version is not null && journal.PriorPin is > 0, "Complete the reviewed import first");
        var draft = Storage.Read<Draft>(Path.Combine(work, "draft.json")); var candidate = draft.Candidates.Single(c => c.Id == candidateId);
        var reviewPath = Path.Combine(work, candidateId + ".correction.json");
        var review = Storage.Element(new { candidateId, date, reviewer, reason, priorPin = journal.PriorPin, originalDate = candidate.DueDate });
        if (File.Exists(reviewPath)) Storage.Require(JsonElement.DeepEquals(Storage.Read<JsonElement>(reviewPath), review), "Correction review is immutable"); else Storage.Save(reviewPath, review);
        await using var api = Bootstrap.Writer();
        await Propose(api, work, journal, candidateId + "-correction", async () =>
        {
            var head = await api.Query.HeadAsync(journal.Version!);
            var current = (await api.Query.FactsAsync(journal.Version!, asOfSeq: head)).Facts.Single(c => c.Subject == candidateId && c.Key == "due_date");
            Storage.Require(current.Value == candidate.DueDate, "Due date changed since the reviewed baseline");
            return new ClaimInput { Subject = candidateId, Key = "due_date", Value = date, ClaimType = "correction", ExpectedHead = head, SupersedesId = current.Id, ScopePath = draft.Case, Provenance = "repaired", ShapeRef = "meeting-commitment@1", Evidence = review };
        });
    }
    public static async Task<string> Status(string work)
    {
        var journal = Storage.Read<ImportJournal>(PathOf(work));
        if (journal.Version is null) { var empty = "MEETING IMPORT | all candidates rejected; no ledger version created\n"; Storage.Write(Path.Combine(work, "status.txt"), empty); return empty; }
        Storage.Require(journal.PriorPin is > 0, "Import incomplete");
        await using var api = Bootstrap.Reader(); var head = await api.Query.HeadAsync(journal.Version);
        var prior = await api.Query.FactsAsync(journal.Version, asOfSeq: journal.PriorPin);
        var current = await api.Query.FactsAsync(journal.Version, asOfSeq: head);
        var promises = await api.Query.PromisesAsync(journal.Version, asOfSeq: head, status: "open");
        Storage.Save(Path.Combine(work, "ledger.json"), new { version = journal.Version, priorPin = journal.PriorPin, head, prior, current, promises });
        var text = $"MEETING COMMITMENTS | reviewed import\nVersion: {journal.Version}\nPrior pin: {journal.PriorPin} | current pin: {head}\n";
        foreach (var claim in current.Facts) text += $"{claim.Subject}.{claim.Key} = {claim.Value} [{claim.Status}]\n";
        foreach (var claim in prior.Facts.Where(c => c.Key == "due_date")) text += $"Previously: {claim.Subject}.due_date = {claim.Value}\n";
        text += $"Unresolved commitments: {promises.Count}\nReminder delivery: local preview only; use current accepted due-date facts after a correction.\n";
        Storage.Write(Path.Combine(work, "status.txt"), File.ReadAllText(Path.Combine(work, "review.txt")) + "\n" + text); return text;
    }
}
