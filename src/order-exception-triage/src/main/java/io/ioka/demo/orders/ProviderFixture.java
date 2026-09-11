// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import com.sun.net.httpserver.HttpServer;
import io.ioka.munarium.client.model.Json;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.regex.Pattern;

/** Canned protocol responses derived from supplied evidence; no inference and no oracle access. */
public final class ProviderFixture {
    private ProviderFixture() {}
    public static void run() throws Exception {
        var server = HttpServer.create(new InetSocketAddress(11434), 0);
        var fail = new AtomicBoolean(); var calls = new AtomicInteger();
        server.createContext("/", exchange -> {
            int status = 200; Object response;
            try {
                String path = exchange.getRequestURI().getPath();
                if (path.equals("/api/tags")) response = Map.of("models", List.of(Map.of("name", "order-selected", "model", "order-selected")));
                else if (path.equals("/calls")) response = Map.of("calls", calls.get());
                else if (path.equals("/fail")) { fail.set(true); response = Map.of("ok", true); }
                else if (path.equals("/reset")) { fail.set(false); response = Map.of("ok", true); }
                else if (path.equals("/api/chat")) {
                    calls.incrementAndGet();
                    if (fail.get()) { status = 503; response = Map.of("error", "controlled provider outage"); }
                    else {
                        var body = Json.MAPPER.readTree(exchange.getRequestBody());
                        StringBuilder prompt = new StringBuilder(); for (var message : body.path("messages")) prompt.append(message.path("content").asText()).append('\n');
                        String evidence = prompt.toString().split("EVIDENCE_START")[1].split("EVIDENCE_END")[0];
                        var citation = Pattern.compile("\\[([^\\[\\]\\s]+/[^\\[\\]\\s]+)\\]").matcher(evidence);
                        var route = Pattern.compile("route to ([A-Z_]+)").matcher(evidence);
                        if (!citation.find() || !route.find()) throw new IllegalStateException("Fixture received no procedure evidence.");
                        String explanation = evidence.substring(evidence.indexOf("Procedure revision:")).replaceAll("\\[[^]]+\\]", "").trim();
                        String answer = FilesUtil.json(Map.of("explanation", explanation, "route", route.group(1), "disposition", "review_required", "missing_evidence", evidence.contains("Export documents missing") || evidence.contains("Unknown hold"), "citations", List.of(citation.group(1))));
                        response = Map.of("model", body.path("model").asText(), "done", true, "done_reason", "stop", "message", Map.of("role", "assistant", "content", answer), "prompt_eval_count", 24, "eval_count", 32);
                    }
                } else { status = 404; response = Map.of("error", "unknown route"); }
                byte[] bytes = FilesUtil.json(response).getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().set("Content-Type", "application/json"); exchange.sendResponseHeaders(status, bytes.length); exchange.getResponseBody().write(bytes);
            } catch (Exception e) { exchange.sendResponseHeaders(500, -1); }
            finally { exchange.close(); }
        });
        server.start();
        System.out.println("Controlled order fixture listening; no model runtime.");
    }
}
