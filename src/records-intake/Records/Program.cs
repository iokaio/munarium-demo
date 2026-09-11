// SPDX-License-Identifier: Apache-2.0
using Records;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
try
{
    switch (args.FirstOrDefault())
    {
        case "generate": Fixtures.Generate(args.ElementAtOrDefault(1) ?? "/inputs", args.ElementAtOrDefault(2) ?? "/oracle", args.ElementAtOrDefault(3) ?? Environment.GetEnvironmentVariable("DEMO_PROFILE") ?? "default"); break;
        case "bootstrap": await Bootstrap.Run(); break;
        case "stage":
            foreach (var route in Storage.Read<Route[]>("/inputs/routes.json"))
            {
                var destination = Path.Combine("/work/incoming", route.File);
                if (!File.Exists(destination)) Storage.Write(destination, File.ReadAllText(Path.Combine("/inputs/incoming", route.File)));
            }
            Console.WriteLine("Synthetic arrivals staged under /work/incoming; existing files preserved."); break;
        case "reports": Reports.Check(args[1], int.Parse(args[2])); break;
        case "faults": await Faults.Run(); break;
        case "cloud": Console.WriteLine("Not applicable: records intake uses no AI completion or model query expansion. Run test for full keyless qualification."); break;
        case "worker":
            var builder = Host.CreateApplicationBuilder(); builder.Services.AddSingleton(new Intake(args[1], args[2], Bootstrap.Grant())); builder.Services.AddHostedService<IntakeWorker>(); await builder.Build().RunAsync(); break;
        case "scan": await new Intake(args[1], args[2], Bootstrap.Grant()).Scan(DateTimeOffset.UtcNow); break;
        case "status": Console.WriteLine(new Intake(args[1], args[2], Bootstrap.Grant()).Status()); break;
        case "build": await new Intake(args[1], args[2], Bootstrap.Grant()).Build(args[3]); break;
        case "resume": await new Intake(args[1], args[2], Bootstrap.Grant()).Resume(args[3], args.ElementAtOrDefault(4)); break;
        case "approve": await new Intake(args[1], args[2], Bootstrap.Grant()).Approve(args[3], args[4]); break;
        default: throw new ArgumentException("Commands: generate, bootstrap, stage, worker/scan/status INCOMING STATE, build/resume/approve INCOMING STATE FILE [RUN-ID]");
    }
}
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
