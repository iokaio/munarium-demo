// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;

import java.nio.file.*;
import java.util.*;
import java.text.Normalizer;

public final class Fixtures {
    private Fixtures() {}
    public record Row(String id, String subject, String field, String a, String b) {}
    public static String normalize(String key) {
        String normalized = Normalizer.normalize(key, Normalizer.Form.NFKC).trim().toLowerCase(Locale.ROOT).replaceAll("[ _-]+", "_");
        if (!normalized.matches("supplier_[0-9]{3}")) throw new IllegalArgumentException("Invalid supplier key");
        return normalized;
    }
    private static Map<String,String[]> csv(Path path) throws Exception {
        List<String> lines = Files.readAllLines(path);
        if (lines.isEmpty() || !lines.getFirst().equals("supplier,field,value")) throw new IllegalArgumentException("CSV header mismatch");
        var rows = new TreeMap<String,String[]>();
        for (String line : lines.subList(1, lines.size())) {
            // The tutorial deliberately accepts an unquoted three-column export, rejecting richer CSV.
            String[] parts = line.split(",", -1);
            if (parts.length != 3 || line.contains("\"") || parts[2].isBlank()) throw new IllegalArgumentException("Expected three unquoted, nonempty CSV fields");
            if (!Set.of("billing_city","currency","payment_days","contact_team","delivery_site","display_name","tax_region","bank_review").contains(parts[1])) throw new IllegalArgumentException("Unknown field mapping");
            String key = normalize(parts[0]);
            if (rows.putIfAbsent(key, new String[]{parts[1], parts[2].trim()}) != null) throw new IllegalArgumentException("Duplicate normalized supplier: " + key);
        }
        return rows;
    }
    public static List<Row> rows(Path inputs) throws Exception {
        var a = csv(inputs.resolve("export-a.csv")); var b = csv(inputs.resolve("export-b.csv"));
        if (!a.keySet().equals(b.keySet())) throw new IllegalArgumentException("Export key sets differ; steward must resolve unmatched rows");
        var result = new ArrayList<Row>();
        for (var key : a.keySet()) {
            if (!a.get(key)[0].equals(b.get(key)[0])) throw new IllegalArgumentException("Field mapping differs");
            result.add(new Row("case-" + key.substring(9), key, a.get(key)[0], a.get(key)[1], b.get(key)[1]));
        }
        return result;
    }
    public static List<String> assignments(String provider) {
        return switch (provider) {
            case "fixture" -> List.of("case-001","case-002","case-003","case-004","case-005","case-006","case-007","case-008");
            case "openai" -> List.of("case-001","case-003","case-005");
            case "anthropic" -> List.of("case-002","case-004","case-006");
            case "openrouter" -> List.of("case-007","case-008");
            default -> throw new IllegalArgumentException("Only the controlled fixture and three online providers are allowed");
        };
    }
    public static void generate(Path inputs, Path oracle) throws Exception {
        generate(inputs, oracle, "default");
    }
    public static void generate(Path inputs, Path oracle, String profile) throws Exception {
        var config = FilesUtil.read(Path.of("/app/fixture-profiles.json")).path(profile);
        if (config.isMissingNode()) throw new IllegalArgumentException("Unknown fixture profile");
        int seed = config.path("seed").asInt(), count = config.path("cases").asInt();
        if (Files.exists(inputs.resolve("manifest.json"))) {
            var prior = FilesUtil.read(inputs.resolve("manifest.json"));
            if (!prior.path("profile").asText().equals(profile) || prior.path("seed").asInt() != seed) throw new IllegalArgumentException("Use empty state for a different profile");
        }
        String[] fields = {"billing_city","currency","payment_days","contact_team","delivery_site","display_name","tax_region","bank_review"};
        String[] a = {"Cedar Bay","USD","30","Old Desk","Depot A","Lumière Supply","North","Pending"};
        String[] b = {"Maple Bay","CAD","45","New Desk","Depot B","Lumière Office","South","Released"};
        StringBuilder first = new StringBuilder("supplier,field,value\n"), second = new StringBuilder("supplier,field,value\n");
        var expected = new TreeMap<String,Object>();
        for (int i = 0; i < count; i++) {
            int scenario = i % fields.length;
            String number = String.format(Locale.ROOT, "%03d", i + 1), id = "case-" + number;
            String before = a[scenario], after = b[scenario];
            if (!profile.equals("default")) {
                if (scenario == 1) { before = profile.equals("heldout") ? "AUD" : "EUR"; after = profile.equals("heldout") ? "NZD" : "GBP"; }
                else if (scenario == 2) { before = Integer.toString(30 + seed % 17 + 1); after = Integer.toString(45 + seed % 17 + 1); }
                else if (scenario != 7) { before += " " + seed + "-" + number; after += " " + seed + "-" + number; }
            }
            first.append("SUPPLIER ").append(number).append(',').append(fields[scenario]).append(',').append(before).append('\n');
            second.append("supplier_").append(number).append(',').append(fields[scenario]).append(',').append(after).append('\n');
            String recommendation = scenario < 6 ? "prefer_b" : scenario == 6 ? "retain_a" : "review_ambiguous";
            String revision = "STEWARD-" + number + "-R1" + (profile.equals("default") ? "" : "-S" + seed);
            FilesUtil.write(inputs.resolve("documents/" + id + ".txt"), "Synthetic Lumen Stewardship, seed " + seed + ", logical date 2026-09-11.\nPolicy revision: " + revision + ". Field: " + fields[scenario] + ". Recommendation: " + recommendation + ".\n" + (scenario < 6 ? "Export B reflects the approved directory review. A steward may approve a correction after inspecting both rows." : scenario == 6 ? "Supporting regional approval is missing. Retain export A pending evidence; do not infer a new tax region." : "Source ownership is ambiguous. Do not resolve the bank review state automatically; retain the current value pending a steward decision.") + "\nA ledger acceptance is a recorded governance outcome, not proof of factual correctness.\n");
            expected.put(id, Map.of("recommendation", recommendation, "approved", scenario < 6, "current", scenario < 6 ? after : before, "prior", before, "revision", revision));
        }
        FilesUtil.write(inputs.resolve("export-a.csv"), first.toString()); FilesUtil.write(inputs.resolve("export-b.csv"), second.toString());
        var hashes = new TreeMap<String,String>();
        try (var paths = Files.walk(inputs)) { for (var path : paths.filter(Files::isRegularFile).filter(p -> !p.getFileName().toString().equals("manifest.json")).toList()) hashes.put(inputs.relativize(path).toString().replace('\\','/'), FilesUtil.hash(Files.readString(path))); }
        FilesUtil.save(inputs.resolve("manifest.json"), Map.of("seed",seed,"generator_version",2,"template_revision","stewardship-1","profile",profile,"record_counts",Map.of("documents",count,"export_a_rows",count,"export_b_rows",count),"logical_date","2026-09-11","timezone","UTC","locale","ROOT","cases",count,"files",hashes));
        FilesUtil.save(oracle.resolve("expected.json"), expected);
    }
    public static void verify(Path inputs) throws Exception {
        var manifest = FilesUtil.read(inputs.resolve("manifest.json"));
        var config = FilesUtil.read(Path.of("/app/fixture-profiles.json")).path(manifest.path("profile").asText());
        if (config.isMissingNode() || manifest.path("seed").asInt()!=config.path("seed").asInt() || manifest.path("generator_version").asInt()!=2 || manifest.path("cases").asInt()!=config.path("cases").asInt() || manifest.path("files").size()!=config.path("cases").asInt()+2) throw new IllegalArgumentException("Unexpected fixture manifest");
        for (var entry : manifest.path("files").properties()) {
            Path target=inputs.resolve(entry.getKey()).normalize();
            if (!target.startsWith(inputs.normalize()) || !entry.getValue().asText().equals(FilesUtil.hash(Files.readString(target)))) throw new IllegalArgumentException("Fixture hash mismatch: " + entry.getKey());
        }
    }
}
