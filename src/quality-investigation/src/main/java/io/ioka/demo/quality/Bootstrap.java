// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import io.ioka.munarium.client.*;
import io.ioka.munarium.client.model.*;
import io.ioka.munarium.client.planes.Params;
import java.nio.file.*;
import java.util.*;

@SuppressWarnings("try")
public final class Bootstrap {
    private Bootstrap() {}
    public static final String REVISION="bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
    public record Grant(String token,String uid,String namespace,String provider,String model,String config,Map<String,String> runbooks) {}
    public static String endpoint() {return FilesUtil.env("MUNARIUM_REST_URL","http://server:8080");}
    public static MunariumClient client(String token,String uid,String endpoint) {return MunariumClient.rest(MunariumClientOptions.of(endpoint).withToken(token).withUid(uid).withReadRetries(0));}
    public static MunariumClient ops(boolean management) {return client(Objects.requireNonNull(System.getenv(management?"MUNARIUM_MGMT_TOKEN":"MUNARIUM_TOKEN")),"quality-operator",endpoint());}
    public static MunariumClient reader() {return client("quality-ro","quality-reader",endpoint());}
    public static Grant grant() throws Exception {return Json.MAPPER.treeToValue(FilesUtil.read(Path.of("/credentials/query.json")),Grant.class);}
    public static ObjectNode registry() throws Exception {return (ObjectNode)FilesUtil.read(Path.of("/credentials/registry.json"));}
    public static void ready() throws Exception {try(var api=reader()) {for(int n=0;;n++) {try {Fixtures.require(api.serverVersion().version().equals("1.1.1"),"Server 1.1.1 required");return;}catch(io.ioka.munarium.client.errors.MunariumException error) {if(n==59) throw error;Thread.sleep(1000);}}}}
    private static void save(ObjectNode value) throws Exception {FilesUtil.save(Path.of("/credentials/registry.json"),value);}
    public static void run(String provider,boolean approve) throws Exception {
        Fixtures.require(approve,"Explicit --approve required for isolated verified index cutovers");Fixtures.assignments(provider);Fixtures.verify(Path.of("/inputs"));ready();
        String model=provider.equals("fixture")?"quality-fixture":Objects.requireNonNull(System.getenv(provider.toUpperCase(Locale.ROOT)+"_MODEL"));
        String template=Files.readString(Path.of("/app/runbooks/investigation.yaml"));String namespace="quality-"+FilesUtil.hash(Files.readString(Path.of("/inputs/manifest.json"))+template+provider+model).substring(0,12),config=namespace+"-model";
        var registry=Files.exists(Path.of("/credentials/registry.json"))?registry():Json.MAPPER.createObjectNode();
        var refs=new TreeMap<String,String>();
        try(var api=ops(false)) {
            String connection=provider.equals("fixture")?"endpoint: http://provider-fixture:11434":"credentialRef: {env: "+provider.toUpperCase(Locale.ROOT)+"_API_KEY}";
            api.providers.applyConfig("""
                apiVersion: munarium.ioka.io/v1
                kind: ProviderConfig
                metadata: {name: %s}
                spec:
                  provider: %s
                  %s
                  models: {complete: [%s], fast: %s}
                  budgets: {rpm: 60, dailyTokens: {fast: 200000}}
                """.formatted(config,provider.equals("fixture")?"ollama":provider,connection,FilesUtil.json(model),FilesUtil.json(model)));
            Fixtures.require(api.providers.health(config).healthy(),"Provider health failed");
            api.runbooks.applyShape(Files.readString(Path.of("/app/shapes/documents.yaml")),null);api.runbooks.applyShape(Files.readString(Path.of("/app/shapes/observations.yaml")),null);
            for(int n=1;n<=8;n++) {
                String id=Fixtures.id(n),name=namespace+"-"+id;refs.put(id,name);
                var row=FilesUtil.read(Path.of("/inputs/"+id+"/inspection.json"));validateObservation(row,id);
                ObjectNode state=registry.has(name)?(ObjectNode)registry.path(name):registry.putObject(name);
                if(!state.has("baseline")) {
                    Fixtures.require(!state.has("creation_intent"),"Uncertain version creation; inspect before rebuilding");state.put("creation_intent",UUID.randomUUID().toString());save(registry);
                    String version=api.commands.createVersion(null,Json.MAPPER.valueToTree(Map.of("application","quality-investigation","input_hash",FilesUtil.hash(FilesUtil.json(row)))),state.path("creation_intent").asText());
                    var record=state.putObject("baseline");record.put("version",version).put("runbook",name+"@1").put("state","loading");save(registry);
                }
                var baseline=(ObjectNode)state.path("baseline");
                if(!baseline.path("state").asText().equals("frozen")) {
                    for(String field:List.of("sample_size","defects")) if(!row.path(field).isNull()) claim(api,registry,baseline,field,row.path("lot").asText(),field,row.path(field).asText(),null,Json.MAPPER.valueToTree(Map.of("input_hash",FilesUtil.hash(FilesUtil.json(row)))),id);
                    baseline.put("pin",api.query.head(baseline.path("version").asText())).put("state","frozen");save(registry);
                }
                apply(api,name,config,baseline.path("version").asText(),1);
                for(String file:List.of("procedure.txt","note.txt")) {var ingested=api.ingest.ingest(Ingesting.IngestFile.ofText(name+"/"+file,"text/plain",Files.readString(Path.of("/inputs/"+id+"/"+file))));Fixtures.require(ingested.error()==null && ingested.boundTo().equals(List.of(name)),"Unexpected document binding");}
                var run=api.runbooks.runRunbook(name,null);FilesUtil.save(Path.of("/work/bootstrap/"+run.runId()+".json"),Map.of("run",run,"namespace",namespace,"client_revision",REVISION,"provider",provider,"model",model));
                var pending=api.runbooks.getRun(run.runId()).steps().stream().filter(s->s.state().equals("awaiting_approval")).toList();Fixtures.require(pending.size()==1 && pending.getFirst().name().equals("cutover:"+name),"Unexpected cutover");api.runbooks.approveStep(run.runId(),pending.getFirst().ordinal());Fixtures.require(api.runbooks.getRun(run.runId()).state().equals("done"),"Index not active");
            }
        }
        try(var api=ops(true)) {var issued=api.tokens.mint(new Tokens.IssueTokenRequest("quality-reviewer",0,List.of(),List.of("query"),new ArrayList<>(refs.values()),3600L));FilesUtil.save(Path.of("/credentials/query.json"),new Grant(issued.token(),"quality-reviewer",namespace,provider,model,config,refs));}
        System.out.println("Eight frozen investigation versions and scoped runbooks prepared for "+provider);
    }
    private static void apply(MunariumClient api,String name,String config,String version,int revision) throws Exception {api.runbooks.applyRunbook(Files.readString(Path.of("/app/runbooks/investigation.yaml")).replace("__NAME__",name).replace("__PROVIDER__",config).replace("__VERSION__",version).replace("__REVISION__",Integer.toString(revision)));}
    private static void claim(MunariumClient api,ObjectNode registry,ObjectNode record,String operation,String subject,String field,String value,String supersedes,JsonNode evidence,String scope) throws Exception {
        var commands=record.has("commands")?(ObjectNode)record.path("commands"):record.putObject("commands");
        if(commands.has(operation)) {Fixtures.require(commands.path(operation).path("response").path("claim").path("status").asText().equals("accepted"),"Uncertain ledger command; no automatic replay");return;}
        var body=new Ledger.ClaimInput(subject,field,value,supersedes==null?"fact":"correction",scope,supersedes==null?"witnessed":"repaired",supersedes,null,evidence,null,"quality-observation@1",null);
        long head=api.query.head(record.path("version").asText());var intent=commands.putObject(operation);intent.put("key",UUID.randomUUID().toString()).put("expected_head",head);intent.set("body",Json.MAPPER.valueToTree(body));save(registry);
        var result=api.commands.proposeClaim(record.path("version").asText(),body,head,intent.path("key").asText());intent.set("response",Json.MAPPER.valueToTree(result));save(registry);Fixtures.require(!result.isDisputed(),"Observation disputed; review findings before freezing");
    }
    public static void correct(String id,int value,String reviewer,String reason) throws Exception {
        Fixtures.require(value>=0 && value<=40 && !reviewer.isBlank() && !reason.isBlank(),"Reviewed defect count between 0 and sample size required");
        try(var lease=new Lease(Path.of("/credentials"));var api=ops(false)) {
            var grant=grant();String name=Objects.requireNonNull(grant.runbooks().get(id));var registry=registry();var state=(ObjectNode)registry.path(name);var base=state.path("baseline");
            var review=Json.MAPPER.valueToTree(Map.of("value",value,"reviewer",reviewer,"reason",reason,"parent",base.path("version").asText(),"parent_pin",base.path("pin").asLong()));
            if(state.has("corrected")) Fixtures.require(FilesUtil.json(state.path("corrected").path("review")).equals(FilesUtil.json(review)),"Correction review changed");
            else {var child=state.putObject("corrected");child.set("review",review);child.put("creation_intent",UUID.randomUUID().toString());save(registry);assertFrozen(api,base);child.put("version",api.commands.createVersion(base.path("version").asText(),review,child.path("creation_intent").asText()));child.put("runbook",name+"@2");save(registry);}
            var child=(ObjectNode)state.path("corrected");Fixtures.require(child.has("version"),"Uncertain child creation; investigate manually");
            if(!child.path("state").asText().equals("frozen")) {
                var parentFacts=api.query.facts(base.path("version").asText(),Params.FactsQuery.atSeq(base.path("pin").asLong())).facts();var prior=parentFacts.stream().filter(f->f.key().equals("defects")).findFirst();String lot=FilesUtil.read(Path.of("/inputs/"+id+"/inspection.json")).path("lot").asText();
                claim(api,registry,child,"corrected_defects",lot,"defects",Integer.toString(value),prior.map(Ledger.Claim::id).orElse(null),review,id);
                child.put("pin",api.query.head(child.path("version").asText())).put("state","frozen");save(registry);
            }
            assertFrozen(api,base);apply(api,name,grant.config(),child.path("version").asText(),2);System.out.println("Frozen child "+child.path("version").asText()+" bound by "+name+"@2; parent retained");
        }
    }
    public static void assertFrozen(MunariumClient api,JsonNode record) {Fixtures.require(record.path("state").asText().equals("frozen") && record.path("pin").asLong()>0 && api.query.head(record.path("version").asText())==record.path("pin").asLong(),"Frozen version changed; refuse this packet and investigate");}
    public static void validateObservation(JsonNode row,String id) {
        Fixtures.require(row.path("fictional").asBoolean() && row.path("case_id").asText().equals(id) && row.path("lot").asText().equals("lot_"+id.substring(5)),"Unexpected observation identity");
        Fixtures.require(row.path("sample_size").isIntegralNumber() && row.path("sample_size").asInt()==40,"Invalid sample size");
        var value=row.path("defects");Fixtures.require(value.isNull() || value.isIntegralNumber() && value.asInt()>=0 && value.asInt()<=40,"Defect observation exceeds the sample or is malformed");
    }
}
