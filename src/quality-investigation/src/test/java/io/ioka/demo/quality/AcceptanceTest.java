// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import static org.junit.jupiter.api.Assertions.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.Stream;
import io.ioka.munarium.client.planes.Params;
import org.junit.jupiter.api.*;

public class AcceptanceTest {
    static void scenario(int n,boolean correction) throws Exception {
        Bootstrap.ready();String id=Fixtures.id(n);Path work=UnitTest.root().resolve(id);assertFalse(Files.exists(work.resolve("journal.json")));
        var packet=Workflow.packet(work,id,"baseline",false,false,Bootstrap.endpoint());var oracle=FilesUtil.read(Path.of("/oracle/expected.json")).path(id);
        assertEquals(oracle.path("status"),packet.path("status"));assertEquals(oracle.path("defects"),packet.path("model_suggestion").path("observed_defects"));assertEquals(oracle.path("narrative"),packet.path("model_suggestion").path("narrative_defects"));assertFalse(packet.path("complete_lot_defect_count_available").asBoolean());
        assertEquals("fact_slice",packet.path("evidence").path("hierarchy").path("layers").get(0).path("block").asText());assertEquals("document_hits",packet.path("evidence").path("hierarchy").path("layers").get(1).path("block").asText());
        assertEquals(2,packet.path("model_suggestion").path("citations").size());assertEquals(n==7?1:2,packet.path("observations").path("facts").size());
        if(correction) {
            int before=IntegrationTest.calls();var repeat=Workflow.packet(work,id,"baseline",false,false,Bootstrap.endpoint());assertEquals(FilesUtil.json(packet),FilesUtil.json(repeat));assertEquals(before,IntegrationTest.calls());
            var base=packet.path("binding");Bootstrap.correct(id,oracle.path("corrected_defects").asInt(),"synthetic-reviewer","Reviewed corrected sampled inspection");var updated=Workflow.packet(work.resolve("corrected"),id,"corrected",false,false,Bootstrap.endpoint());
            assertEquals(oracle.path("corrected_defects").asInt(),updated.path("model_suggestion").path("observed_defects").asInt());assertNotEquals(base.path("version"),updated.path("binding").path("version"));Bootstrap.correct(id,oracle.path("corrected_defects").asInt(),"synthetic-reviewer","Reviewed corrected sampled inspection");assertTrue(updated.path("binding").path("runbook").asText().endsWith("@2"));
            try(var api=Bootstrap.reader()) {Bootstrap.assertFrozen(api,base);assertEquals(packet.path("observations").path("facts"),io.ioka.munarium.client.model.Json.MAPPER.valueToTree(api.query.facts(base.path("version").asText(),Params.FactsQuery.atSeq(base.path("pin").asLong())).facts()));}
            assertThrows(IllegalStateException.class,()->Workflow.packet(work,id,"corrected",false,false,Bootstrap.endpoint()));
        }
        FilesUtil.save(UnitTest.root().resolve(id+".quality.json"),Map.of("case_id",id,"passed",true,"status",packet.path("status"),"model",packet.path("evidence").path("completion"),"required_evidence",packet.path("evidence").path("hierarchy")));
    }
    @TestFactory @Tag("controlled") Stream<DynamicTest> controlled() {return Fixtures.assignments("fixture").stream().map(n->DynamicTest.dynamicTest(Fixtures.id(n),()->scenario(n,true)));}
    static Stream<DynamicTest> cloud(String provider) {return Fixtures.assignments(provider).stream().map(n->DynamicTest.dynamicTest(Fixtures.id(n),()->{if(provider.equals("openrouter")) Thread.sleep(60000);scenario(n,false);}));}
    @TestFactory @Tag("cloud-openai") Stream<DynamicTest> openai() {return cloud("openai");}
    @TestFactory @Tag("cloud-anthropic") Stream<DynamicTest> anthropic() {return cloud("anthropic");}
    @TestFactory @Tag("cloud-openrouter") Stream<DynamicTest> openrouter() {return cloud("openrouter");}
}
