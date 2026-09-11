// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import com.sun.net.httpserver.HttpServer;
import java.net.*;
import java.net.http.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;

public final class FaultProxy {
    private FaultProxy() {}
    public static void run() throws Exception {
        var server=HttpServer.create(new InetSocketAddress(11435),0);var mode=new AtomicReference<>("normal");var http=HttpClient.newHttpClient();
        server.createContext("/",exchange->{
            try {
                String path=exchange.getRequestURI().getPath();
                if(path.startsWith("/control/")) {mode.set(path.substring(9));byte[] bytes="{}".getBytes(StandardCharsets.UTF_8);exchange.sendResponseHeaders(200,bytes.length);exchange.getResponseBody().write(bytes);return;}
                var builder=HttpRequest.newBuilder(URI.create("http://server:8080"+exchange.getRequestURI()));
                for(var header:exchange.getRequestHeaders().entrySet()) if(!Set.of("host","content-length","connection","upgrade","http2-settings").contains(header.getKey().toLowerCase(Locale.ROOT))) for(var value:header.getValue()) builder.header(header.getKey(),value);
                var response=http.send(builder.method(exchange.getRequestMethod(),HttpRequest.BodyPublishers.ofByteArray(exchange.getRequestBody().readAllBytes())).build(),HttpResponse.BodyHandlers.ofByteArray());
                boolean drop=exchange.getRequestMethod().equals("POST") && ((mode.get().equals("drop-claim") && path.endsWith("/claims")) || (mode.get().equals("drop-turn") && path.contains("/turns")));
                if(drop) {exchange.getResponseHeaders().set("Content-Type","application/problem+json");byte[] bytes="{\"type\":\"about:blank\",\"title\":\"Controlled lost response\",\"status\":502}".getBytes(StandardCharsets.UTF_8);exchange.sendResponseHeaders(502,bytes.length);exchange.getResponseBody().write(bytes);}
                else {response.headers().firstValue("content-type").ifPresent(v->exchange.getResponseHeaders().set("Content-Type",v));exchange.sendResponseHeaders(response.statusCode(),response.body().length);exchange.getResponseBody().write(response.body());}
            } catch(Exception error) {exchange.sendResponseHeaders(502,-1);} finally {exchange.close();}
        });server.start();System.out.println("Local fault proxy ready.");
    }
}
