// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;

namespace Meetings;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "generate": Fixtures.Generate(args.ElementAtOrDefault(1) ?? "/inputs", args.ElementAtOrDefault(2) ?? "/oracle", args.ElementAtOrDefault(3) ?? Environment.GetEnvironmentVariable("DEMO_PROFILE") ?? "default"); break;
                case "bootstrap": await Bootstrap.Run(args.ElementAtOrDefault(1) ?? "fixture", args.ElementAtOrDefault(2) ?? "/work/bootstrap/" + Guid.NewGuid().ToString("N")); break;
                case "fixture": await ProviderFixture.Run(); break;
                case "faults": await Faults.Run(); break;
                case "prepare":
                case "recover":
                case "crash-turn":
                    var draft = await Workflow.Prepare(args[1], args[2], args[0] == "recover", args[0] == "crash-turn"); Console.WriteLine(File.ReadAllText(Path.Combine(args[1], "review.txt"))); if (draft.Status != "ready_for_review") return 2; break;
                case "review": Workflow.Review(args[1], args[2], args[3], args[4], args[5]); break;
                case "import": case "crash-import": await Ledger.Import(args[1], args[0] == "crash-import"); Console.WriteLine(await Ledger.Status(args[1])); break;
                case "reconcile": await Ledger.Reconcile(args[1], args[2]); break;
                case "correct": await Ledger.Correct(args[1], args[2], args[3], args[4], args[5]); Console.WriteLine(await Ledger.Status(args[1])); break;
                case "status": Console.WriteLine(await Ledger.Status(args[1])); break;
                case "reports": Reports.Check(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)); break;
                case "usage": await using (var api = Bootstrap.Client(Environment.GetEnvironmentVariable("MUNARIUM_MGMT_TOKEN")!, "meeting-reporter")) Storage.Save(Path.Combine(args[1], "usage.json"), await api.Reports.UsageAsync()); break;
                case "render":
                    var start = new ProcessStartInfo("python3") { UseShellExecute = false }; foreach (var arg in new[] { "/app/render.py", args[1], args[2] }) start.ArgumentList.Add(arg); using (var process = Process.Start(start)!) { await process.WaitForExitAsync(); if (process.ExitCode != 0) return process.ExitCode; }
                    break;
                default: throw new ArgumentException("Commands: prepare WORK CASE, review WORK CANDIDATE approve|reject REVIEWER REASON, import WORK, correct WORK CANDIDATE DATE REVIEWER REASON, status WORK, recover WORK CASE, reconcile WORK COMMAND");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 2; }
    }
}
