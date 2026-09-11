// SPDX-License-Identifier: Apache-2.0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
namespace Records;

public static class Faults
{
    public static async Task Run()
    {
        var app = WebApplication.CreateBuilder().Build(); var mode = "normal"; string? lastRun = null;
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        app.Run(async context =>
        {
            var path = context.Request.Path.Value!;
            if (path.StartsWith("/control/", StringComparison.Ordinal)) { mode = path.Split('/').Last(); await context.Response.WriteAsJsonAsync(new { mode, lastRun }); return; }
            var start = path.EndsWith("/runs", StringComparison.Ordinal) && context.Request.Method == "POST";
            if (start && mode == "fail") { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { type = "https://munarium.ioka.io/problems/overloaded", detail = "Controlled build service unavailable" }); return; }
            using var request = new HttpRequestMessage(new(context.Request.Method), "http://server:8080" + path + context.Request.QueryString);
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body); request.Content = new ByteArrayContent(body.ToArray());
            foreach (var header in context.Request.Headers.Where(h => h.Key is not ("Host" or "Content-Length"))) if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray())) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await http.SendAsync(request); var bytes = await response.Content.ReadAsByteArrayAsync();
            if (start && response.IsSuccessStatusCode) { lastRun = JsonDocument.Parse(bytes).RootElement.GetProperty("run_id").GetString(); if (mode == "drop") { context.Response.StatusCode = 502; await context.Response.WriteAsync("Controlled lost run response"); return; } }
            context.Response.StatusCode = (int)response.StatusCode; context.Response.ContentType = response.Content.Headers.ContentType?.ToString(); await context.Response.Body.WriteAsync(bytes);
        });
        await app.RunAsync("http://0.0.0.0:11435");
    }
}
