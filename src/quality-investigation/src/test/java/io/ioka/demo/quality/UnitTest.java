// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import static org.junit.jupiter.api.Assertions.*;
import java.nio.file.*;
import java.util.*;
import org.junit.jupiter.api.*;

@Tag("unit")
public class UnitTest {
    static Path root() {return Path.of(System.getenv("QUALITY_REPORT_DIR"));}
    static int child(String... args) throws Exception {var command=new ArrayList<>(List.of("/app/build/install/quality-investigation/bin/quality-investigation"));command.addAll(List.of(args));return new ProcessBuilder(command).inheritIO().start().waitFor();}
    @org.junit.jupiter.params.ParameterizedTest @org.junit.jupiter.params.provider.CsvSource({"default,8,3","heldout,8,5","stress,80,6"}) void independentGeneratorProcesses(String profile,int count,int defects) throws Exception {
        Path a=root().resolve("a-"+profile),b=root().resolve("b-"+profile);assertEquals(0,child("generate",a.toString(),a+"-oracle",profile));assertEquals(0,child("generate",b.toString(),b+"-oracle",profile));
        assertEquals(Files.readString(a.resolve("manifest.json")),Files.readString(b.resolve("manifest.json")));assertEquals(Files.readString(Path.of(a+"-oracle/expected.json")),Files.readString(Path.of(b+"-oracle/expected.json")));
        Fixtures.verify(a);var expected=FilesUtil.read(Path.of(a+"-oracle/expected.json"));assertEquals(count,expected.size());assertEquals(defects,expected.path("case-001").path("defects").asInt());assertEquals(count*3,FilesUtil.read(a.resolve("manifest.json")).path("files").size());
        var names=FilesUtil.read(a.resolve("manifest.json")).path("files").fieldNames();while(names.hasNext()) {String name=names.next();assertArrayEquals(Files.readAllBytes(a.resolve(name)),Files.readAllBytes(b.resolve(name)));}Files.copy(a.resolve("manifest.json"),root().resolve("reproducible-"+profile+"-manifest.json"));
    }
    @Test void tamperRejected() throws Exception {Path a=root().resolve("tamper");Fixtures.generate(a,Path.of(a+"-oracle"));Files.writeString(a.resolve("case-001/note.txt"),"changed");assertThrows(IllegalStateException.class,()->Fixtures.verify(a));}
    @Test void providerAllocation() {assertEquals(3,Fixtures.assignments("openai").size());assertEquals(3,Fixtures.assignments("anthropic").size());assertEquals(2,Fixtures.assignments("openrouter").size());assertThrows(IllegalArgumentException.class,()->Fixtures.assignments("ollama"));}
    @Test void deterministicMissingObservations() throws Exception {Path a=root().resolve("missing");Fixtures.generate(a,Path.of(a+"-oracle"));assertTrue(FilesUtil.read(a.resolve("case-007/inspection.json")).path("defects").isNull());assertTrue(Files.readString(a.resolve("case-008/note.txt")).contains("unrecorded"));}
    @Test void malformedObservationCannotBeImported() throws Exception {Path a=root().resolve("invalid-count");Fixtures.generate(a,Path.of(a+"-oracle"));var row=(com.fasterxml.jackson.databind.node.ObjectNode)FilesUtil.read(a.resolve("case-001/inspection.json"));row.put("defects",41);assertThrows(IllegalStateException.class,()->Bootstrap.validateObservation(row,"case-001"));row.put("defects","3");assertThrows(IllegalStateException.class,()->Bootstrap.validateObservation(row,"case-001"));}
}
