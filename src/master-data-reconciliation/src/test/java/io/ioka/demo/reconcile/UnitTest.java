// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import java.nio.file.*;
import java.util.concurrent.TimeUnit;
import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;
import static org.junit.jupiter.api.Assertions.*;

@Tag("unit")
class UnitTest {
    @TempDir Path temporary;
    @Test void supplierAliasesNormalizeDeterministically() {assertEquals("supplier_001",Fixtures.normalize(" ＳＵＰＰＬＩＥＲ 001 "));assertEquals("supplier_002",Fixtures.normalize("Supplier-002"));}
    @Test void invalidKeysAreRejected() {assertThrows(IllegalArgumentException.class,()->Fixtures.normalize("supplier.001"));assertThrows(IllegalArgumentException.class,()->Fixtures.normalize("../supplier_001"));}
    @org.junit.jupiter.params.ParameterizedTest @org.junit.jupiter.params.provider.ValueSource(strings={"default","heldout","stress"})
    void independentProcessesGenerateIdenticalInputsAndOracle(String profile) throws Exception {
        for(String name:new String[]{"a","b"}) {var child=new ProcessBuilder("/app/build/install/master-data-reconciliation/bin/master-data-reconciliation","generate",temporary.resolve(name).toString(),temporary.resolve(name+"-oracle").toString(),profile).start();if(!child.waitFor(30,TimeUnit.SECONDS)){child.destroyForcibly();fail("Generator timed out");}assertEquals(0,child.exitValue());}
        assertArrayEquals(Files.readAllBytes(temporary.resolve("a/manifest.json")),Files.readAllBytes(temporary.resolve("b/manifest.json")));assertArrayEquals(Files.readAllBytes(temporary.resolve("a-oracle/expected.json")),Files.readAllBytes(temporary.resolve("b-oracle/expected.json")));
        try(var paths=Files.walk(temporary.resolve("a"))){for(var path:paths.filter(Files::isRegularFile).toList())assertArrayEquals(Files.readAllBytes(path),Files.readAllBytes(temporary.resolve("b").resolve(temporary.resolve("a").relativize(path))));}
        assertEquals(profile.equals("stress")?80:8,Fixtures.rows(temporary.resolve("a")).size());
        var expected=FilesUtil.read(temporary.resolve("a-oracle/expected.json"));
        assertEquals(profile.equals("default")?"USD":profile.equals("heldout")?"AUD":"EUR",expected.path("case-002").path("prior").asText());
        FilesUtil.write(Path.of(System.getenv("RECONCILE_REPORT_DIR"),"fixture-"+profile+"-manifest.json"),Files.readString(temporary.resolve("a/manifest.json")));
    }
    @Test void duplicateNormalizedRowsFailBeforeProviderWork() throws Exception {Fixtures.generate(temporary,temporary.resolveSibling("oracle"));Files.writeString(temporary.resolve("export-a.csv"),"supplier,field,value\nSUPPLIER 001,currency,USD\nsupplier_001,currency,CAD\n");assertThrows(IllegalArgumentException.class,()->Fixtures.rows(temporary));}
    @Test void mismatchedAndQuotedExportsRequireStewardReview() throws Exception {Fixtures.generate(temporary,temporary.resolveSibling("oracle"));Files.writeString(temporary.resolve("export-b.csv"),"supplier,field,value\nsupplier_009,currency,CAD\n");assertThrows(IllegalArgumentException.class,()->Fixtures.rows(temporary));Files.writeString(temporary.resolve("export-b.csv"),"supplier,field,value\nsupplier_001,display_name,\"Lumen, Inc\"\n");assertThrows(IllegalArgumentException.class,()->Fixtures.rows(temporary));}
    @Test void changedFixturesFailManifestVerification() throws Exception {Fixtures.generate(temporary,temporary.resolveSibling("oracle"));Files.writeString(temporary.resolve("export-a.csv"),"altered\n");assertThrows(IllegalArgumentException.class,()->Fixtures.verify(temporary));}
}
