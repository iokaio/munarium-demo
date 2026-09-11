// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import java.nio.file.*;
import java.util.*;

public final class Fixtures {
    private Fixtures() {}
    public static String id(int n) { return String.format(Locale.ROOT,"case-%03d",n); }
    public static void generate(Path input,Path oracle) throws Exception {
        generate(input,oracle,"default");
    }
    public static com.fasterxml.jackson.databind.JsonNode settings(String profile) throws Exception {
        var config=FilesUtil.read(Path.of("/app/fixture-profiles.json")).path(profile);require(config.has("seed"),"Unknown fixture profile");return config;
    }
    public static void generate(Path input,Path oracle,String profile) throws Exception {
        var config=settings(profile);int seed=config.path("seed").asInt(),count=config.path("cases").asInt(),offset=profile.equals("default")?0:seed%11+1;
        if(Files.exists(input.resolve("manifest.json"))) {var prior=FilesUtil.read(input.resolve("manifest.json"));require(prior.path("profile").asText().equals(profile)&&prior.path("seed").asInt()==seed,"Use empty state for a different profile");}
        var hashes=new TreeMap<String,String>();var expected=new TreeMap<String,Object>();
        for(int n=1;n<=count;n++) {
            int scenario=(n-1)%8+1;
            String id=id(n),lot=String.format(Locale.ROOT,"lot_%03d",n);Integer defects=scenario==7?null:scenario+2+offset;Integer note=scenario==8?null:scenario+2+offset+(scenario%2==0?1:0);
            var inspection=new TreeMap<String,Object>();inspection.put("case_id",id);inspection.put("lot",lot);inspection.put("sample_size",40);inspection.put("defects",defects);inspection.put("fictional",true);
            FilesUtil.save(input.resolve(id+"/inspection.json"),inspection);
            String procedure="# Fictional quality procedure: "+id+"\nRevision: 1\nAction: Hold the lot for quality review.\nA sampled inspection is not a complete lot defect count. Root cause and disposition require a reviewer.\n";
            String narrative="# Fictional narrative note: "+id+"\nLot: "+lot+"\nNarrative defects: "+(note==null?"unrecorded":note)+"\nThis narrative is an observation, not a disposition decision. Équipe fictive.\n";
            FilesUtil.write(input.resolve(id+"/procedure.txt"),procedure);FilesUtil.write(input.resolve(id+"/note.txt"),narrative);
            for(String file:List.of("inspection.json","procedure.txt","note.txt")) hashes.put(id+"/"+file,FilesUtil.hash(Files.readString(input.resolve(id+"/"+file))));
            var answer=new TreeMap<String,Object>();answer.put("defects",defects);answer.put("narrative",note);answer.put("status",scenario>=7?"incomplete":scenario%2==0?"disagreement_requires_review":"complete_for_review");answer.put("corrected_defects",scenario+1+offset);expected.put(id,answer);
        }
        var manifest=new TreeMap<String,Object>(Map.of("seed",seed,"generator","quality-v2","template_revision","office-inspections-v1","profile",profile,"record_count",count,"logical_time","2026-09-11T00:00:00Z","locale","ROOT","files",hashes));
        manifest.put("timezone","UTC");manifest.put("record_counts",Map.of("cases",count,"files",count*3,"documents",count*2,"observations",count));FilesUtil.save(input.resolve("manifest.json"),manifest);
        FilesUtil.save(oracle.resolve("expected.json"),expected);
    }
    public static void verify(Path input) throws Exception {
        var manifest=FilesUtil.read(input.resolve("manifest.json"));var config=settings(manifest.path("profile").asText());require(manifest.path("generator").asText().equals("quality-v2") && manifest.path("seed").asInt()==config.path("seed").asInt() && manifest.path("record_count").asInt()==config.path("cases").asInt() && manifest.path("files").size()==config.path("cases").asInt()*3,"Unexpected fixture manifest");
        var fields=manifest.path("files").properties().iterator();while(fields.hasNext()) {var f=fields.next();require(f.getKey().matches("case-[0-9]{3}/(inspection.json|procedure.txt|note.txt)"),"Unexpected input path");require(f.getValue().asText().equals(FilesUtil.hash(Files.readString(input.resolve(f.getKey())))),"Source hash mismatch");}
    }
    public static void require(boolean condition,String message) {if(!condition) throw new IllegalStateException(message);}
    public static List<Integer> assignments(String provider) {return switch(provider) {case "fixture"->List.of(1,2,3,4,5,6,7,8);case "openai"->List.of(1,3,7);case "anthropic"->List.of(2,4,8);case "openrouter"->List.of(5,6);default->throw new IllegalArgumentException("Unknown provider");};}
}
