// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import java.nio.file.*;
import java.util.*;

public final class Main {
    private Main() {}
    public static void main(String[] args) throws Exception {
        if (args.length == 0) throw new IllegalArgumentException("Use generate, bootstrap, consume, reconcile, inspect, provider, or render.");
        switch(args[0]) {
            case "generate" -> {
                Fixtures.generateProfile(Path.of("/inputs"), Path.of("/oracle"), FilesUtil.env("DEMO_PROFILE", "default"));
                System.out.println("Generated selected fictional events and procedures; private oracle stored separately.");
            }
            case "bootstrap" -> Bootstrap.run(args[1], Arrays.asList(args).contains("--approve"));
            case "provider" -> ProviderFixture.run();
            case "consume", "reconcile", "inspect", "crash-test" -> {
                var grant = Bootstrap.grant();
                Path directory = Path.of(args.length > 1 ? args[1] : "/work/manual/" + grant.namespace());
                try (var inbox = new Inbox(directory, grant.uid() + ":" + grant.namespace())) {
                    var worker = new Worker(inbox, grant);
                    if (args[0].equals("consume")) {
                        var events = Fixtures.events(Path.of("/inputs")).stream().filter(e -> grant.provider().equals("fixture") || Fixtures.assignments(grant.provider()).contains(e.eventId())).toList();
                        try { worker.consume(events); } finally { worker.export(directory.resolve("packets")); }
                    } else if (args[0].equals("reconcile")) {
                        try { worker.reconcile(); } finally { worker.export(directory.resolve("packets")); }
                    } else if (args[0].equals("crash-test")) {
                        if (!grant.provider().equals("fixture")) throw new IllegalArgumentException("Crash injection is controlled-only.");
                        var event = Fixtures.events(Path.of("/inputs")).getFirst(); inbox.accept(event);
                        worker.process(event.eventId(), () -> Runtime.getRuntime().halt(71));
                    }
                    for (var row : inbox.rows()) System.out.println(row.id() + "  " + row.state() + "  session=" + row.session() + (row.recovered() ? "  recovered" : ""));
                    if (!args[0].equals("inspect") && inbox.rows().stream().anyMatch(r -> !r.state().equals("complete"))) throw new IllegalStateException("Unresolved events remain; inspect the journal and reconcile explicitly.");
                }
            }
            case "render" -> Render.run(Path.of(args[1]), Path.of(args[2]));
            case "reports" -> Reports.check(Path.of(args[1]), Integer.parseInt(args[2]));
            default -> throw new IllegalArgumentException("Unknown command.");
        }
    }
}
