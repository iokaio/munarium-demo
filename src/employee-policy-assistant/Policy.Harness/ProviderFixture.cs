// SPDX-License-Identifier: Apache-2.0
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Policy.Harness;

/// <summary>Controlled Ollama protocol fixture; builds responses only from the request, with no oracle mount.</summary>
public static class ProviderFixture
{
    public static async Task Run()
    {
        var listener = new HttpListener(); listener.Prefixes.Add("http://+:11434/"); listener.Start();
        var calls = new List<JsonElement>();
        var fail = false;
        while (true)
        {
            var context = await listener.GetContextAsync();
            object response;
            var path = context.Request.Url!.AbsolutePath;
            if (path == "/api/tags") response = new { models = new[] { new { name = "policy-selected", model = "policy-selected" } } };
            else if (path == "/calls") response = calls;
            else if (path == "/fail") { fail = true; response = new { ok = true }; }
            else if (path == "/reset") { fail = false; response = new { ok = true }; }
            else if (path == "/api/chat")
            {
                using var request = await JsonDocument.ParseAsync(context.Request.InputStream);
                var body = request.RootElement; calls.Add(body.Clone());
                if (fail) { context.Response.StatusCode = 503; response = new { error = "controlled provider outage" }; }
                else
                {
                    var prompt = string.Join("\n", body.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
                    var answer = "[\"policy\",\"procedure\"]";
                    if (prompt.Contains("EVIDENCE_START"))
                    {
                        var question = prompt.Split("QUESTION_START").Last().Split("QUESTION_END")[0].ToLowerInvariant();
                        var evidence = prompt.Split("EVIDENCE_START").Last().Split("EVIDENCE_END")[0];
                        var topic = new[] { "moonbase", "retention", "commute", "training", "equipment", "leave" }.FirstOrDefault(question.Contains) ?? "scope";
                        var blocks = Regex.Matches(evidence, @"\[([^\[\]\s]+/[^\[\]\s]+)\]([^\[]*)", RegexOptions.Singleline);
                        var selected = blocks.Cast<Match>().FirstOrDefault(m => m.Groups[2].Value.Contains("TOPIC:" + topic));
                        var fallback = blocks.Cast<Match>().FirstOrDefault();
                        answer = selected is null ? "The requested answer is not available in the supplied documents. " + (fallback?.Groups[1].Value is string label ? "[" + label + "]" : "") : selected.Groups[2].Value.Split("TOPIC:" + topic).Last().Trim() + " [" + selected.Groups[1].Value + "]";
                    }
                    response = new { model = body.GetProperty("model").GetString(), done = true, done_reason = "stop", message = new { role = "assistant", content = answer }, prompt_eval_count = 12, eval_count = 8 };
                }
            }
            else { context.Response.StatusCode = 404; response = new { error = "unknown fixture route" }; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
        }
    }
}
