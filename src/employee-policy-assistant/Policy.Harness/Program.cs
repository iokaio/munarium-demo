// SPDX-License-Identifier: Apache-2.0
using Policy.Harness;

var action = args.FirstOrDefault() ?? "help";
try
{
    switch (action)
    {
        case "generate": Fixtures.Generate(args.ElementAtOrDefault(1) ?? "/inputs", args.ElementAtOrDefault(2) ?? "/oracle", args.ElementAtOrDefault(3) ?? Environment.GetEnvironmentVariable("DEMO_PROFILE") ?? "default"); Console.WriteLine("Generated the selected fictional corpus and eight independent oracle cases."); break;
        case "bootstrap": await Bootstrap.Run(args.ElementAtOrDefault(1) ?? "fixture", "/inputs", "/credentials", "/work", args.Contains("--approve")); break;
        case "provider": await ProviderFixture.Run(); break;
        case "reports": Reports.Check(args[1], int.Parse(args[2])); break;
        default: Console.Error.WriteLine("Actions: generate [inputs oracle], bootstrap [fixture|openai|anthropic|openrouter] --approve, provider"); Environment.ExitCode = 2; break;
    }
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
