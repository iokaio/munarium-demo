// SPDX-License-Identifier: Apache-2.0
namespace Records;

public static class Fixtures
{
    public static void Generate(string inputs, string oracle, string profile = "default")
    {
        var config = System.Text.Json.JsonDocument.Parse(Bootstrap.Asset("fixture-profiles.json")).RootElement.GetProperty(profile);
        var seed = config.GetProperty("seed").GetInt32(); var count = config.GetProperty("documents").GetInt32();
        var manifestPath = Path.Combine(inputs, "manifest.json");
        if (File.Exists(manifestPath))
        {
            var prior = Storage.Read<System.Text.Json.JsonElement>(manifestPath);
            Storage.Require(prior.GetProperty("profile").GetString() == profile && prior.GetProperty("seed").GetInt32() == seed, "Use empty state for a different profile");
        }
        var routes = new List<Route>(); var expected = new SortedDictionary<string, string>();
        string[] departments = ["office", "facilities", "training", "purchasing"];
        string[] subjects = ["visitor badges", "meeting supplies", "room bookings", "display checks", "training requests", "course attendance", "purchase review", "supplier forms"];
        for (var i = 0; i < count; i++)
        {
            var scenario = i % 8;
            var id = $"case-{i + 1:000}"; var department = departments[scenario / 2]; var name = $"{department}/{id}.txt";
            var reference = (profile == "default" ? "" : $"SEED-{seed}-") + $"RECORD-{i + 1:00}-A";
            routes.Add(new(name, id, department)); expected[name] = reference;
            Storage.Write(Path.Combine(inputs, "incoming", name), $"Synthetic Northstar Office — équipe fictive; seed {seed}; logical date 2026-09-11.\nDepartment: {department}. Record {id} concerns {subjects[scenario]}.\nRevision A. Reference {reference}. Route requests to the {department} review queue.\n");
        }
        Storage.Save(Path.Combine(inputs, "routes.json"), routes);
        var files = Directory.GetFiles(inputs, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith("manifest.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(inputs, p).Replace('\\', '/'), p => Storage.Hash(File.ReadAllBytes(p)));
        Storage.Save(manifestPath, new { seed, generatorVersion = 2, templateRevision = "records-procedures-1", profile, documentCount = count, recordCounts = new { documents = count, routes = count }, timezone = "UTC", locale = "invariant", logicalDate = "2026-09-11", files });
        Storage.Save(Path.Combine(oracle, "expected.json"), expected);
    }
}
