// SPDX-License-Identifier: Apache-2.0
namespace Records;

public static class Fixtures
{
    public static void Generate(string inputs, string oracle)
    {
        var routes = new List<Route>(); var expected = new SortedDictionary<string, string>();
        string[] departments = ["office", "facilities", "training", "purchasing"];
        string[] subjects = ["visitor badges", "meeting supplies", "room bookings", "display checks", "training requests", "course attendance", "purchase review", "supplier forms"];
        for (var i = 0; i < 8; i++)
        {
            var id = $"case-{i + 1:000}"; var department = departments[i / 2]; var name = $"{department}/{id}.txt";
            routes.Add(new(name, id, department)); expected[name] = $"RECORD-{i + 1:00}-A";
            Storage.Write(Path.Combine(inputs, "incoming", name), $"Synthetic Northstar Office — équipe fictive; seed 5091; logical date 2026-09-11.\nDepartment: {department}. Record {id} concerns {subjects[i]}.\nRevision A. Reference RECORD-{i + 1:00}-A. Route requests to the {department} review queue.\n");
        }
        Storage.Save(Path.Combine(inputs, "routes.json"), routes);
        var files = Directory.GetFiles(inputs, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith("manifest.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(inputs, p).Replace('\\', '/'), p => Storage.Hash(File.ReadAllBytes(p)));
        Storage.Save(Path.Combine(inputs, "manifest.json"), new { seed = 5091, generatorVersion = 1, templateRevision = "records-procedures-1", profile = "complete-eight", documentCount = 8, timezone = "UTC", locale = "invariant", logicalDate = "2026-09-11", files });
        Storage.Save(Path.Combine(oracle, "expected.json"), expected);
    }
}
