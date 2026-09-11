// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meetings;

public static class ProviderFixture
{
    public static async Task Run()
    {
        var app = WebApplication.CreateBuilder().Build(); var mode = "ok"; var calls = 0;
        app.MapGet("/api/tags", () => Results.Json(new { models = new[] { new { name = "meetings-fixture", model = "meetings-fixture" } } }));
        app.MapGet("/state", () => Results.Json(new { mode, calls }));
        app.MapPost("/mode", async (HttpRequest request) => { var value = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body); var selected = value.GetProperty("mode").GetString()!; if (selected is not ("ok" or "unavailable" or "bad-citation")) return Results.BadRequest(); mode = selected; return Results.Ok(); });
        app.MapPost("/api/chat", async (HttpRequest request) =>
        {
            Interlocked.Increment(ref calls);
            if (mode == "unavailable") return Results.Json(new { error = "Synthetic provider outage" }, statusCode: 503);
            var value = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
            var prompt = string.Join("\n", value.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
            var candidates = new List<Candidate>();
            foreach (Match chunk in Regex.Matches(prompt, @"\[(?<label>[^\]]+)\]\s*(?<text>.*?)(?=\n\n\[|\nQuestion:|\z)", RegexOptions.Singleline))
            {
                foreach (Match item in Regex.Matches(chunk.Groups["text"].Value, @"Item (?<id>(?:commitment|proposal)_[0-9]{3})\nDescription: (?<description>[^\n]+)\nOwner: (?<owner>[^\n]+)\nDue: (?<due>[^\n]+)\nDecision: (?<decision>[^\n]+)\nEvidence: (?<quote>[^\n]+)"))
                {
                    var owner = item.Groups["owner"].Value; var date = item.Groups["due"].Value;
                    candidates.Add(new(item.Groups["id"].Value, item.Groups["description"].Value, owner == "unassigned" ? null : owner, Workflow.Date(date) ? date : null, item.Groups["decision"].Value, item.Groups["quote"].Value, [mode == "bad-citation" ? "unserved/transcript" : chunk.Groups["label"].Value]));
                }
            }
            return Results.Json(new { model = value.GetProperty("model").GetString(), done = true, done_reason = "stop", message = new { role = "assistant", content = JsonSerializer.Serialize(new CandidateList(candidates.ToArray()), Storage.Json) }, prompt_eval_count = 160, eval_count = 120 });
        });
        await app.RunAsync("http://0.0.0.0:11434");
    }
}
