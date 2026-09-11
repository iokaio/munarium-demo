// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace Meetings;

public static class Fixtures
{
    public const string Revision = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public static System.Text.Json.JsonElement Settings(string profile) => Storage.Read<System.Text.Json.JsonElement>("/app/fixture-profiles.json").GetProperty(profile);
    public static Manifest Generate(string inputs, string oracle, string profile = "default")
    {
        var config = Settings(profile); var seed = config.GetProperty("seed").GetInt32(); var count = config.GetProperty("cases").GetInt32();
        var manifestPath = Path.Combine(inputs, "manifest.json");
        if (File.Exists(manifestPath)) { var prior = Storage.Read<Manifest>(manifestPath); Storage.Require(prior.Profile == profile && prior.Seed == seed, "Use empty state for a different profile"); }
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var expected = new SortedDictionary<string, object>(StringComparer.Ordinal);
        for (var number = 1; number <= count; number++)
        {
            var scenario = (number - 1) % 8 + 1;
            var id = $"case-{number:000}";
            var owner = scenario == 7 ? "unassigned" : $"Person{number + (profile == "default" ? 0 : seed % 800):000}";
            var date = scenario == 8 ? "next Friday" : new DateOnly(2026, profile == "default" ? 9 : profile == "heldout" ? 10 : 11, 20 + scenario).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var disposition = scenario >= 7 ? "ambiguous" : "agreed";
            var text = $"# Fictional project meeting {id}\nMeeting date: 2026-09-11 UTC\nProject: office-{number:000}\n\nItem commitment_{number:000}\nDescription: Prepare the office handover checklist {number:000}.\nOwner: {owner}\nDue: {date}\nDecision: {disposition}\nEvidence: The team agreed to prepare checklist {number:000}; owner {owner}; due {date}.\n\nItem proposal_{number:000}\nDescription: Publish all draft notes externally.\nOwner: unassigned\nDue: undecided\nDecision: proposed\nEvidence: A suggestion to publish draft notes was discussed but not agreed.\n\nNotes: équipe fictive; generated text is not approval.\n";
            var relative = id + "/transcript.md";
            Storage.Write(Path.Combine(inputs, relative), text);
            files[relative] = Storage.Hash(text);
            expected[id] = new { Approved = scenario < 7, Owner = scenario == 7 ? null : owner, DueDate = scenario == 8 ? null : date, Disposition = disposition, Candidate = $"commitment_{number:000}", Proposal = $"proposal_{number:000}" };
        }
        var manifest = new Manifest(seed, "meeting-v2", "2026-09-11T00:00:00Z", files, profile, "office-commitments-v1", new() { ["cases"] = count, ["documents"] = count }, "UTC", "invariant");
        Storage.Save(Path.Combine(inputs, "manifest.json"), manifest);
        Storage.Save(Path.Combine(oracle, "expected.json"), expected);
        return manifest;
    }
    public static Manifest Verify(string inputs)
    {
        var manifest = Storage.Read<Manifest>(Path.Combine(inputs, "manifest.json"));
        var config = Settings(manifest.Profile);
        Storage.Require(manifest.Generator == "meeting-v2" && manifest.Seed == config.GetProperty("seed").GetInt32() && manifest.Files.Count == config.GetProperty("cases").GetInt32() && manifest.RecordCounts["documents"] == manifest.Files.Count, "Unexpected fixture generation");
        foreach (var pair in manifest.Files)
        {
            Storage.Require(System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^case-[0-9]{3}/transcript[.]md$"), "Unexpected fixture path");
            Storage.Require(Storage.Hash(File.ReadAllBytes(Path.Combine(inputs, pair.Key))) == pair.Value, "Source hash mismatch");
        }
        return manifest;
    }
}
