// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import java.nio.file.*;

public final class Main {
    private Main() {}
    public static void main(String[] args) throws Exception {
        if(args.length==0) throw new IllegalArgumentException("Commands: generate, bootstrap PROVIDER --approve, prepare WORK CASE, recover WORK, review WORK approve|reject REVIEWER REASON, import WORK [--pause], reconcile WORK STEP, render STATUS PNG");
        switch(args[0]) {
            case "race" -> CompetingWorkers.run(args[1],Path.of(args[2]));
            case "generate" -> Fixtures.generate(Path.of(args.length>1?args[1]:"/inputs"),Path.of(args.length>2?args[2]:"/oracle"));
            case "bootstrap" -> Bootstrap.run(args[1],args.length>2 && args[2].equals("--approve"));
            case "provider" -> ProviderFixture.run();
            case "faults" -> FaultProxy.run();
            case "prepare" -> {
                Fixtures.verify(Path.of("/inputs"));var rows=Fixtures.rows(Path.of("/inputs"));var row=rows.stream().filter(r->r.id().equals(args[2])).findFirst().orElseThrow();new Workflow(Path.of(args[1]),Bootstrap.grant()).prepare(row);
            }
            case "recover" -> new Workflow(Path.of(args[1]),Bootstrap.grant()).recover();
            case "review" -> {if(!args[2].equals("approve") && !args[2].equals("reject")) throw new IllegalArgumentException("Decision must be approve or reject");Workflow.review(Path.of(args[1]),args[2].equals("approve"),args[3],args[4]);}
            case "import" -> {
                boolean crash=args.length>2 && args[2].equals("--crash-after-dispute");
                new LedgerImport(Path.of(args[1])).run(crash || (args.length>2 && args[2].equals("--pause")));
                if(crash) Runtime.getRuntime().halt(71);
            }
            case "reconcile" -> new LedgerImport(Path.of(args[1])).reconcile(args[2]);
            case "status" -> System.out.print(Files.readString(Path.of(args[1],"status.txt")));
            case "reports" -> Reports.check(Path.of(args[1]),Integer.parseInt(args[2]));
            case "render" -> Render.run(Path.of(args[1]),Path.of(args[2]));
            default -> throw new IllegalArgumentException("Unknown command");
        }
    }
}
