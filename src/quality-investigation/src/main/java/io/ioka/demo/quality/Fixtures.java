// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import java.nio.file.*;
import java.util.*;

public final class Fixtures {
    private Fixtures() {}
    public static String id(int n) { return String.format(Locale.ROOT,"case-%03d",n); }
    public static void generate(Path input,Path oracle) throws Exception {
        var hashes=new TreeMap<String,String>();var expected=new TreeMap<String,Object>();
        for(int n=1;n<=8;n++) {
            String id=id(n),lot=String.format(Locale.ROOT,"lot_%03d",n);Integer defects=n==7?null:n+2;Integer note=n==8?null:n+2+(n%2==0?1:0);
            var inspection=new TreeMap<String,Object>();inspection.put("case_id",id);inspection.put("lot",lot);inspection.put("sample_size",40);inspection.put("defects",defects);inspection.put("fictional",true);
            FilesUtil.save(input.resolve(id+"/inspection.json"),inspection);
            String procedure="# Fictional quality procedure: "+id+"\nRevision: 1\nAction: Hold the lot for quality review.\nA sampled inspection is not a complete lot defect count. Root cause and disposition require a reviewer.\n";
            String narrative="# Fictional narrative note: "+id+"\nLot: "+lot+"\nNarrative defects: "+(note==null?"unrecorded":note)+"\nThis narrative is an observation, not a disposition decision. Équipe fictive.\n";
            FilesUtil.write(input.resolve(id+"/procedure.txt"),procedure);FilesUtil.write(input.resolve(id+"/note.txt"),narrative);
            for(String file:List.of("inspection.json","procedure.txt","note.txt")) hashes.put(id+"/"+file,FilesUtil.hash(Files.readString(input.resolve(id+"/"+file))));
            var answer=new TreeMap<String,Object>();answer.put("defects",defects);answer.put("narrative",note);answer.put("status",n>=7?"incomplete":n%2==0?"disagreement_requires_review":"complete_for_review");answer.put("corrected_defects",n+1);expected.put(id,answer);
        }
        FilesUtil.save(input.resolve("manifest.json"),Map.of("seed",10091,"generator","quality-v1","template_revision",1,"profile","eight-lots","record_count",8,"logical_time","2026-09-11T00:00:00Z","locale","ROOT","files",hashes));
        FilesUtil.save(oracle.resolve("expected.json"),expected);
    }
    public static void verify(Path input) throws Exception {
        var manifest=FilesUtil.read(input.resolve("manifest.json"));require(manifest.path("generator").asText().equals("quality-v1") && manifest.path("files").size()==24,"Unexpected fixture manifest");
        var fields=manifest.path("files").properties().iterator();while(fields.hasNext()) {var f=fields.next();require(f.getKey().matches("case-00[1-8]/(inspection.json|procedure.txt|note.txt)"),"Unexpected input path");require(f.getValue().asText().equals(FilesUtil.hash(Files.readString(input.resolve(f.getKey())))),"Source hash mismatch");}
    }
    public static void require(boolean condition,String message) {if(!condition) throw new IllegalStateException(message);}
    public static List<Integer> assignments(String provider) {return switch(provider) {case "fixture"->List.of(1,2,3,4,5,6,7,8);case "openai"->List.of(1,3,7);case "anthropic"->List.of(2,4,8);case "openrouter"->List.of(5,6);default->throw new IllegalArgumentException("Unknown provider");};}
}
