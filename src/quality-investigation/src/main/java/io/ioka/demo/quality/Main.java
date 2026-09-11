// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import java.nio.file.*;
import java.util.*;
public final class Main {
    private Main() {}
    public static void main(String[] args) {try {run(args);}catch(Exception e) {System.err.println(e.getClass().getSimpleName()+": "+e.getMessage());System.exit(2);}}
    private static void run(String[] args) throws Exception {
        switch(args[0]) {
            case "generate"->Fixtures.generate(Path.of(args.length>1?args[1]:"/inputs"),Path.of(args.length>2?args[2]:"/oracle"));
            case "bootstrap"->Bootstrap.run(args[1],Arrays.asList(args).contains("--approve"));
            case "provider"->ProviderFixture.run();case "faults"->FaultProxy.run();
            case "packet","recover","crash"->{var output=Workflow.packet(Path.of(args[1]),args[2],args[3],args[0].equals("recover"),args[0].equals("crash"),Bootstrap.endpoint());System.out.println(Files.readString(Path.of(args[1],"packet.md")));if(output.path("status").asText().equals("incomplete")) System.exit(3);}
            case "correct"->Bootstrap.correct(args[1],Integer.parseInt(args[2]),args[3],args[4]);
            case "render"->Render.run(Path.of(args[1]),Path.of(args[2]));case "reports"->Reports.check(Path.of(args[1]),Integer.parseInt(args[2]));
            case "usage"->{try(var api=Bootstrap.ops(true)) {FilesUtil.save(Path.of(args[1],"usage.json"),api.reports.usage(new io.ioka.munarium.client.planes.Params.UsageQuery(null,null,null)));}}
            default->throw new IllegalArgumentException("Use packet WORK CASE baseline|corrected, correct CASE COUNT REVIEWER REASON, or recover WORK CASE REVISION");
        }
    }
}
