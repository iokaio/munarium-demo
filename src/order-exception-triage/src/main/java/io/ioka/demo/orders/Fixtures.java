// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import java.nio.file.*;
import java.util.*;

/** Seeded synthetic inputs and a separate declarative business oracle. No model calls. */
public final class Fixtures {
    private Fixtures() {}
    public static final String VERSION = "orders-v1";
    public static void main(String[] args) throws Exception { generate(Path.of(args[0]), Path.of(args[1]), Long.parseLong(args[2])); }
    public static void generate(Path inputs, Path oracle, long seed) throws Exception {
        String[] reasons = {"stock_shortage", "address_invalid", "substitution", "export_missing", "carrier_delay", "unknown_hold", "stock_shortage", "substitution"};
        String[] routes = {"PROCUREMENT", "CUSTOMER_SERVICE", "FULFILLMENT", "COMPLIANCE", "LOGISTICS", "MANUAL_REVIEW", "PROCUREMENT", "FULFILLMENT"};
        String[] rules = {
            "Stock shortage: route to PROCUREMENT. Keep the order on hold and request replenishment; never claim stock was received.",
            "Invalid address: route to CUSTOMER_SERVICE. Keep the order on hold while the customer confirms the address; never invent an address.",
            "Substitution: route to FULFILLMENT. Require customer consent before proposing a substitute. Without consent keep the order on hold. Consent permits review only, not automatic shipment.",
            "Export documents missing: route to COMPLIANCE. Keep the order on hold. Explicitly state that export documents are missing and clearance cannot be confirmed.",
            "Carrier delay: route to LOGISTICS. Request a carrier status check and keep the order on hold; never invent a delivery date.",
            "Unknown hold: route to MANUAL_REVIEW. The hold-specific procedure is missing; state insufficient evidence and request manual review. Never invent a resolution.",
            "Stock shortage: route to PROCUREMENT. Keep the order on hold and request replenishment; never claim stock was received.",
            "Substitution: route to FULFILLMENT. Require customer consent before proposing a substitute. Without consent keep the order on hold. Consent permits review only, not automatic shipment."
        };
        var random = new Random(seed);
        var expectations = new TreeMap<String, Object>();
        var hashes = new TreeMap<String, String>();
        for (int i = 0; i < reasons.length; i++) {
            String id = "event-%03d".formatted(i + 1);
            var event = new OrderEvent(id, "order-%04d".formatted(1001 + i), reasons[i], 10 + random.nextInt(20), i == 6 ? 0 : 3, i == 7, i != 3);
            Path eventFile = inputs.resolve("events/" + id + ".json");
            FilesUtil.save(eventFile, event);
            String document = "FICTIONAL TEST MATERIAL — Northstar Fulfillment\nProcedure revision: 2026-09-01\nEvent scope: " + id + "\n" + rules[i] + "\n";
            FilesUtil.write(inputs.resolve("documents/" + id + ".txt"), document);
            expectations.put(id, new TreeMap<>(Map.of("route", routes[i], "missing_evidence", i == 3 || i == 5,
                "required_terms", switch(i) { case 0, 6 -> List.of("stock", "hold"); case 1 -> List.of("address"); case 2, 7 -> List.of("consent"); case 3 -> List.of("missing", "document"); case 4 -> List.of("carrier"); default -> List.of("evidence"); })));
        }
        try (var paths = Files.walk(inputs)) {
            for (Path path : paths.filter(Files::isRegularFile).filter(p -> !p.getFileName().toString().equals("manifest.json")).sorted().toList())
                hashes.put(inputs.relativize(path).toString().replace('\\', '/'), FilesUtil.hash(Files.readString(path)));
        }
        FilesUtil.save(inputs.resolve("manifest.json"), new TreeMap<>(Map.of("generator", VERSION, "seed", seed, "logical_time", "2026-09-01T00:00:00Z", "files", hashes)));
        FilesUtil.save(oracle.resolve("expected.json"), expectations);
        verify(inputs);
    }
    public static void verify(Path inputs) throws Exception {
        var manifest = FilesUtil.read(inputs.resolve("manifest.json"));
        if (!manifest.path("generator").asText().equals(VERSION) || manifest.path("files").size() != 16) throw new IllegalStateException("Unexpected fixture manifest.");
        var entries = manifest.path("files").properties();
        for (var entry : entries) {
            Path target = inputs.resolve(entry.getKey()).normalize();
            if (!target.startsWith(inputs.normalize()) || !FilesUtil.hash(Files.readString(target)).equals(entry.getValue().asText()))
                throw new IllegalStateException("Fixture hash mismatch.");
        }
    }
    public static List<OrderEvent> events(Path inputs) throws Exception {
        verify(inputs);
        var result = new ArrayList<OrderEvent>();
        try (var files = Files.list(inputs.resolve("events"))) {
            for (var file : files.sorted().toList()) result.add(io.ioka.munarium.client.model.Json.MAPPER.readValue(Files.readString(file), OrderEvent.class));
        }
        return result;
    }
    public static List<String> assignments(String provider) {
        return switch(provider) {
            case "openai" -> List.of("event-001", "event-002");
            case "anthropic" -> List.of("event-003", "event-004");
            case "openrouter" -> List.of("event-005", "event-006", "event-007", "event-008");
            case "fixture" -> java.util.stream.IntStream.rangeClosed(1, 8).mapToObj(i -> "event-%03d".formatted(i)).toList();
            default -> throw new IllegalArgumentException("Only the fixture and three online providers are allowed.");
        };
    }
}
