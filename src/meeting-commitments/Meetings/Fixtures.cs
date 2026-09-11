// SPDX-License-Identifier: Apache-2.0
using System.Globalization;

namespace Meetings;

public static class Fixtures
{
    public const string Revision = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public static Manifest Generate(string inputs, string oracle)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var expected = new SortedDictionary<string, object>(StringComparer.Ordinal);
        for (var number = 1; number <= 8; number++)
        {
            var id = $"case-{number:000}";
            var owner = number == 7 ? "unassigned" : $"Person{number:000}";
            var date = number == 8 ? "next Friday" : new DateOnly(2026, 9, 20 + number).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var disposition = number >= 7 ? "ambiguous" : "agreed";
            var text = $"# Fictional project meeting {id}\nMeeting date: 2026-09-11 UTC\nProject: office-{number:000}\n\nItem commitment_{number:000}\nDescription: Prepare the office handover checklist {number:000}.\nOwner: {owner}\nDue: {date}\nDecision: {disposition}\nEvidence: The team agreed to prepare checklist {number:000}; owner {owner}; due {date}.\n\nItem proposal_{number:000}\nDescription: Publish all draft notes externally.\nOwner: unassigned\nDue: undecided\nDecision: proposed\nEvidence: A suggestion to publish draft notes was discussed but not agreed.\n\nNotes: équipe fictive; generated text is not approval.\n";
            var relative = id + "/transcript.md";
            Storage.Write(Path.Combine(inputs, relative), text);
            files[relative] = Storage.Hash(text);
            expected[id] = new { Approved = number < 7, Owner = number == 7 ? null : owner, DueDate = number == 8 ? null : date, Disposition = disposition, Candidate = $"commitment_{number:000}", Proposal = $"proposal_{number:000}" };
        }
        var manifest = new Manifest(9091, "meeting-v1", "2026-09-11T00:00:00Z", files);
        Storage.Save(Path.Combine(inputs, "manifest.json"), manifest);
        Storage.Save(Path.Combine(oracle, "expected.json"), expected);
        return manifest;
    }
    public static Manifest Verify(string inputs)
    {
        var manifest = Storage.Read<Manifest>(Path.Combine(inputs, "manifest.json"));
        Storage.Require(manifest.Generator == "meeting-v1" && manifest.Files.Count == 8, "Unexpected fixture generation");
        foreach (var pair in manifest.Files)
        {
            Storage.Require(System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^case-[0-9]{3}/transcript[.]md$"), "Unexpected fixture path");
            Storage.Require(Storage.Hash(File.ReadAllBytes(Path.Combine(inputs, pair.Key))) == pair.Value, "Source hash mismatch");
        }
        return manifest;
    }
}
