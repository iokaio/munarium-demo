// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import io.ioka.munarium.client.model.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.Stream;
import org.junit.jupiter.api.*;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;
import static org.junit.jupiter.api.Assertions.*;

class AcceptanceTest {
    static Path report() {return Path.of(System.getenv("RECONCILE_REPORT_DIR"));}
    static Stream<String> cases() throws Exception {
        String kind=System.getenv("RECONCILE_TEST_KIND");String expected=kind.equals("controlled")?"fixture":kind.substring("cloud-".length());
        assertEquals(expected,Bootstrap.grant().provider(),"Profile must use its assigned provider");
        return expected.equals("fixture") ? FilesUtil.read(Path.of("/oracle/expected.json")).properties().stream().map(Map.Entry::getKey).sorted() : Fixtures.assignments(expected).stream();
    }
    @BeforeAll static void usageBefore() throws Exception {
        try(var management=Bootstrap.ops(true)) {FilesUtil.save(report().resolve("usage-before.json"),management.reports.usage(io.ioka.munarium.client.planes.Params.UsageQuery.byUid()));}
    }
    @ParameterizedTest @MethodSource("cases") @Tag("controlled") @Tag("cloud-openai") @Tag("cloud-anthropic") @Tag("cloud-openrouter")
    void independentStewardshipCase(String id) throws Exception {
        var grant=Bootstrap.grant();
        boolean passed=false;
        try {
        if(grant.provider().equals("openrouter")) Thread.sleep(60_000);
        var row=Fixtures.rows(Path.of("/inputs")).stream().filter(r->r.id().equals(id)).findFirst().orElseThrow();
        var expected=FilesUtil.read(Path.of("/oracle/expected.json")).path(id);Path work=report().resolve(id);
        var app=new Workflow(work,grant);app.prepare(row);assertFalse(Files.exists(work.resolve("ledger-journal.json")));
        var packet=FilesUtil.read(work.resolve("draft.json"));assertEquals(expected.path("recommendation").asText(),packet.path("answer").path("recommendation").asText());
        var result=Json.MAPPER.treeToValue(packet.path("evidence"),SessionsApi.TurnResult.class);
        assertEquals(1,result.hits().size());assertTrue(result.hits().getFirst().text().contains(expected.path("revision").asText()));assertEquals(FilesUtil.hash(Files.readString(Path.of("/inputs/documents/"+id+".txt"))),result.hits().getFirst().sourceContentHash());
        Workflow.review(work,expected.path("approved").asBoolean(),"synthetic-steward","Independent fixture review decision");
        var importer=new LedgerImport(work);importer.run(false);var ledger=FilesUtil.read(work.resolve("ledger-report.json"));
        assertEquals("complete",ledger.path("status").asText());assertEquals(1,ledger.path("accepted").path("facts").size());assertEquals(expected.path("current").asText(),ledger.path("accepted").path("facts").get(0).path("value").asText());
        assertEquals(expected.path("prior").asText(),ledger.path("historical").path("facts").get(0).path("value").asText());assertEquals(1,ledger.path("disputed").path("facts").size());assertFalse(ledger.path("findings").isEmpty());
        assertTrue(ledger.path("accepted").path("facts").get(0).path("origin").isMissingNode() || ledger.path("accepted").path("facts").get(0).path("origin").isNull());
        String before=Files.readString(work.resolve("ledger-journal.json"));String session=app.journal().path("session_id").asText();
        Files.delete(work.resolve("draft.md"));app.prepare(row);importer.run(false);assertEquals(before,Files.readString(work.resolve("ledger-journal.json")));assertEquals(session,app.journal().path("session_id").asText());assertTrue(Files.isRegularFile(work.resolve("draft.md")));
        FilesUtil.save(work.resolve("quality.json"),Map.of("case",id,"passed",true,"recommendation",expected.path("recommendation").asText(),"provider",result.completion().provider(),"model",result.completion().model(),"completion",result.completion(),"historical_pin",ledger.path("historical_pin"),"claims",ledger));
        passed=true;
        } finally {
            if(!passed) FilesUtil.save(report().resolve(id).resolve("quality.json"),Map.of("case",id,"passed",false,"provider",grant.provider(),"model",grant.model(),"details","See JUnit failure and retained draft/ledger journals"));
        }
    }
    @AfterAll static void aggregate() throws Exception {
        var cases=new ArrayList<Object>();try(var paths=Files.walk(report())) {for(var p:paths.filter(p->p.getFileName().toString().equals("quality.json") && !p.getParent().equals(report())).toList()) cases.add(FilesUtil.read(p));}
        FilesUtil.save(report().resolve("quality.json"),Map.of("cases",cases,"workload","disjoint provider assignments, not a comparative benchmark"));
        try(var management=Bootstrap.ops(true)) {FilesUtil.save(report().resolve("usage-after.json"),management.reports.usage(io.ioka.munarium.client.planes.Params.UsageQuery.byUid()));}
    }
}
