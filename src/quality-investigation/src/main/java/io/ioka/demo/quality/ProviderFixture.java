// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import com.sun.net.httpserver.HttpServer;
import io.ioka.munarium.client.model.Json;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.regex.Pattern;

public final class ProviderFixture {
    private ProviderFixture() {}
    public static void run() throws Exception {
        var server=HttpServer.create(new InetSocketAddress(11434),0);var mode=new AtomicReference<>("ok");var calls=new AtomicInteger();
        server.createContext("/",exchange->{try {
            int status=200;Object response;String path=exchange.getRequestURI().getPath();
            if(path.equals("/api/tags")) response=Map.of("models",List.of(Map.of("name","quality-fixture","model","quality-fixture")));
            else if(path.equals("/calls")) response=Map.of("calls",calls.get());
            else if(path.startsWith("/mode/")) {mode.set(path.substring(6));response=Map.of("mode",mode.get());}
            else if(path.equals("/api/chat")) {
                calls.incrementAndGet();if(mode.get().equals("unavailable")) {status=503;response=Map.of("error","Synthetic provider outage");}
                else {var body=Json.MAPPER.readTree(exchange.getRequestBody());var prompt=new StringBuilder();for(var message:body.path("messages")) prompt.append(message.path("content").asText()).append('\n');String text=prompt.toString();
                    var fact=Pattern.compile("lot_[0-9]{3}[^\\n]*?defects = ([0-9]+)").matcher(text);var note=Pattern.compile("Narrative defects: ([0-9]+)").matcher(text);var labels=new TreeSet<String>();var matcher=Pattern.compile("\\[([^\\[\\]\\s]+/[^\\[\\]\\s]+)\\]").matcher(text);while(matcher.find()) labels.add(matcher.group(1));
                    var answer=new TreeMap<String,Object>();answer.put("observed_defects",fact.find()?Integer.valueOf(fact.group(1)):null);answer.put("narrative_defects",note.find()?Integer.valueOf(note.group(1)):null);answer.put("action","Hold the lot for quality review.");answer.put("root_cause","not determined");answer.put("hypothesis","requires investigation");answer.put("citations",mode.get().equals("bad-citation")?List.of("unserved/secret"):labels);
                    response=Map.of("model",body.path("model").asText(),"done",true,"done_reason","stop","message",Map.of("role","assistant","content",FilesUtil.json(answer)),"prompt_eval_count",100,"eval_count",90);
                }
            }else {status=404;response=Map.of("error","Unknown route");}
            byte[] bytes=FilesUtil.json(response).getBytes(StandardCharsets.UTF_8);exchange.getResponseHeaders().set("Content-Type","application/json");exchange.sendResponseHeaders(status,bytes.length);exchange.getResponseBody().write(bytes);
        }catch(Exception e) {exchange.sendResponseHeaders(500,-1);}finally {exchange.close();}});server.start();System.out.println("Canned quality protocol fixture; no model inference");
    }
}
