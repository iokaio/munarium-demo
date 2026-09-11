// SPDX-License-Identifier: Apache-2.0
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meetings;

public static class Faults
{
    public static async Task Run()
    {
        var app = WebApplication.CreateBuilder().Build(); var mode = "normal";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        app.Run(async context =>
        {
            var path = context.Request.Path.Value!;
            if (path.StartsWith("/control/", StringComparison.Ordinal)) { mode = path.Split('/').Last(); await context.Response.WriteAsJsonAsync(new { mode }); return; }
            if (mode == "outage") { context.Response.StatusCode = 502; await context.Response.WriteAsync("Synthetic dependency outage"); return; }
            using var request = new HttpRequestMessage(new(context.Request.Method), "http://server:8080" + path + context.Request.QueryString);
            using var body = new MemoryStream(); await context.Request.Body.CopyToAsync(body); request.Content = new ByteArrayContent(body.ToArray());
            foreach (var header in context.Request.Headers.Where(h => h.Key is not ("Host" or "Content-Length"))) if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray())) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await http.SendAsync(request); var bytes = await response.Content.ReadAsByteArrayAsync();
            var drop = context.Request.Method == "POST" && ((mode == "drop-turn" && path.Contains("/turns", StringComparison.Ordinal)) || (mode == "drop-claim" && path.EndsWith("/claims", StringComparison.Ordinal)) || (mode == "drop-promise" && path.EndsWith("/promises", StringComparison.Ordinal)));
            if (drop && response.IsSuccessStatusCode) { context.Response.StatusCode = 502; await context.Response.WriteAsync("Synthetic lost accepted response"); return; }
            context.Response.StatusCode = (int)response.StatusCode; context.Response.ContentType = response.Content.Headers.ContentType?.ToString(); await context.Response.Body.WriteAsync(bytes);
        });
        await app.RunAsync("http://0.0.0.0:11435");
    }
}
