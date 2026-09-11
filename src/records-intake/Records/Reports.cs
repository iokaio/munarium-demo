// SPDX-License-Identifier: Apache-2.0
using System.Xml.Linq;


namespace Records;

public static class Reports
{
    public static void Check(string path, int allowedChronologySkips)
    {
        var doc = XDocument.Load(path); XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var results = doc.Descendants(ns + "UnitTestResult").ToArray();
        var skipped = results.Where(r => (string?)r.Attribute("outcome") == "NotExecuted").ToArray();
        var failed = results.Count(r => (string?)r.Attribute("outcome") is not ("Passed" or "NotExecuted"));
        var suite = new XElement("testsuite", new XAttribute("tests", results.Length), new XAttribute("failures", failed), new XAttribute("skipped", skipped.Length), results.Select(r => new XElement("testcase", new XAttribute("name", (string?)r.Attribute("testName") ?? "unknown"), (string?)r.Attribute("outcome") switch
        {
            "Passed" => null,
            "NotExecuted" => new XElement("skipped", "See native TRX for skip reason"),
            _ => new XElement("failure", r.Element(ns + "Output")?.Value ?? "Test failed")
        })));
        new XDocument(suite).Save(Path.ChangeExtension(path, ".xml"));
        var directory = Path.GetDirectoryName(path)!;
        var quality = Directory.GetFiles(directory, "*.quality.json").Select(Storage.Read<object>).ToArray();
        if (quality.Length > 0) Storage.Save(Path.Combine(directory, "quality.json"), new { cases = quality });
        if (results.Length == 0 || skipped.Length != allowedChronologySkips || skipped.Any(r => !((string?)r.Attribute("testName") ?? "").Contains("Chronology", StringComparison.OrdinalIgnoreCase)) || failed > 0)
            throw new InvalidDataException("Missing tests, unexpected skips, or failed tests in " + Path.GetFileName(path));
        Console.WriteLine($"Verified {results.Length - skipped.Length} passed; {skipped.Length} documented chronology skips.");
    }
}
