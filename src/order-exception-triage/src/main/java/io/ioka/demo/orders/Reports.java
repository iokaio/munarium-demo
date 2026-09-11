// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import java.nio.file.*;
import java.util.*;
import javax.xml.parsers.DocumentBuilderFactory;

public final class Reports {
    private Reports() {}
    public static void check(Path directory, int expectedSkips) throws Exception {
        var factory = DocumentBuilderFactory.newInstance();
        factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
        int tests = 0, failures = 0, skips = 0;
        List<String> skipped = new ArrayList<>();
        try (var paths = Files.walk(directory)) {
            for (var path : paths.filter(p -> p.getFileName().toString().startsWith("TEST-") && p.toString().endsWith(".xml")).toList()) {
                var document = factory.newDocumentBuilder().parse(path.toFile());
                var cases = document.getElementsByTagName("testcase"); tests += cases.getLength();
                failures += document.getElementsByTagName("failure").getLength() + document.getElementsByTagName("error").getLength();
                skips += document.getElementsByTagName("skipped").getLength();
                for (int i = 0; i < cases.getLength(); i++) {
                    var element = (org.w3c.dom.Element) cases.item(i);
                    if (element.getElementsByTagName("skipped").getLength() > 0) skipped.add(element.getAttribute("classname") + "." + element.getAttribute("name"));
                }
            }
        }
        FilesUtil.save(directory.resolve("summary.json"), Map.of("tests", tests, "passed", tests - failures - skips, "failures", failures, "skips", skips, "skipped_cases", skipped));
        System.out.printf("JUnit: %d tests, %d failures, %d skips%n", tests, failures, skips);
        if (tests == 0 || failures != 0 || skips != expectedSkips || skipped.stream().anyMatch(s -> !s.equals("io.ioka.munarium.client.conformance.ScenariosTest.gatesChronologyCertainOnly()"))) throw new IllegalStateException("Missing, failed, or unexpectedly skipped tests.");
    }
}
