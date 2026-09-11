// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.reconcile;
import com.sun.net.httpserver.HttpServer;
import io.ioka.munarium.client.model.Json;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.regex.Pattern;

/** A canned protocol responder; no model runtime, input mount, or private oracle. */
public final class ProviderFixture {
    private ProviderFixture() {}
    public static void run() throws Exception {
        var server=HttpServer.create(new InetSocketAddress(11434),0);var fail=new AtomicBoolean();var calls=new AtomicInteger();
        server.createContext("/",exchange->{
            int status=200;Object response;
            try {
                String path=exchange.getRequestURI().getPath();
                if(path.equals("/api/tags")) response=Map.of("models",List.of(Map.of("name","reconcile-selected","model","reconcile-selected")));
                else if(path.equals("/calls")) response=Map.of("calls",calls.get());
                else if(path.equals("/fail")) {fail.set(true);response=Map.of("ok",true);}
                else if(path.equals("/reset")) {fail.set(false);response=Map.of("ok",true);}
                else if(path.equals("/api/chat")) {
                    calls.incrementAndGet();
                    if(fail.get()) {status=503;response=Map.of("error","controlled provider outage");}
                    else {
                        var body=Json.MAPPER.readTree(exchange.getRequestBody());StringBuilder prompt=new StringBuilder();for(var message:body.path("messages")) prompt.append(message.path("content").asText()).append('\n');
                        String evidence=prompt.toString().split("EVIDENCE_START")[1].split("EVIDENCE_END")[0];
                        var citation=Pattern.compile("\\[([^\\[\\]\\s]+/[^\\[\\]\\s]+)\\]").matcher(evidence);var recommendation=Pattern.compile("Recommendation: ([a-z_]+)").matcher(evidence);
                        if(!citation.find() || !recommendation.find()) throw new IllegalStateException("No policy evidence");
                        String answer=FilesUtil.json(Map.of("explanation",evidence.substring(evidence.indexOf("Policy revision:")).replaceAll("\\[[^]]+\\]","").trim(),"recommendation",recommendation.group(1),"disposition","review_required","citations",List.of(citation.group(1))));
                        response=Map.of("model",body.path("model").asText(),"done",true,"done_reason","stop","message",Map.of("role","assistant","content",answer),"prompt_eval_count",24,"eval_count",32);
                    }
                } else {status=404;response=Map.of("error","unknown route");}
                byte[] bytes=FilesUtil.json(response).getBytes(StandardCharsets.UTF_8);exchange.getResponseHeaders().set("Content-Type","application/json");exchange.sendResponseHeaders(status,bytes.length);exchange.getResponseBody().write(bytes);
            } catch(Exception error) {exchange.sendResponseHeaders(500,-1);} finally {exchange.close();}
        });server.start();System.out.println("Controlled stewardship fixture; no model inference.");
    }
}
