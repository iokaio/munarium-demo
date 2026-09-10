// SPDX-License-Identifier: Apache-2.0
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Demo.Web.Services;

/// <summary>Live readiness from the authenticated GPU gateway, never the model catalog cache.</summary>
public sealed class OllamaAvailability(HttpClient http, IConfiguration configuration)
{
    public sealed record ReadyState(bool Ready, DateTimeOffset ExpiresAt, string? Fast, string? Capable);

    public async Task<ReadyState?> GetAsync(CancellationToken cancellationToken)
    {
        var endpoint = Environment.GetEnvironmentVariable("DEMO_OLLAMA_URL") ?? configuration["Ollama:Url"];
        var key = Environment.GetEnvironmentVariable("DEMO_OLLAMA_KEY") ?? configuration["Ollama:Key"];
        if ((Environment.GetEnvironmentVariable("DEMO_OLLAMA_MODE") ?? configuration["Ollama:Mode"]) == "direct")
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var directUri)
                || directUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(directUri.UserInfo)) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var response = await http.GetAsync(endpoint!.TrimEnd('/') + "/api/tags", timeout.Token);
                if (!response.IsSuccessStatusCode) return null;
                using var json = await System.Text.Json.JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
                var models = json.RootElement.GetProperty("models").EnumerateArray()
                    .Select(model => model.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
                var fast = Environment.GetEnvironmentVariable("DEMO_OLLAMA_FAST") ?? "qwen3:1.7b";
                var capable = Environment.GetEnvironmentVariable("DEMO_OLLAMA_CAPABLE") ?? fast;
                return models.Contains(fast) && models.Contains(capable)
                    ? new ReadyState(true, DateTimeOffset.UtcNow.AddSeconds(45), fast, capable) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(key)) return null;
        // HTTP is permitted only for loopback integration tests/local development.
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint!.TrimEnd('/') + "/readyz");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await http.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode) return null;
            var state = await response.Content.ReadFromJsonAsync<ReadyState>(deadline.Token);
            var now = DateTimeOffset.UtcNow;
            // Model selection belongs to the authenticated gateway and provider catalog.
            // The caller verifies their agreement, allowing deployment model upgrades.
            return state is { Ready: true } && !string.IsNullOrWhiteSpace(state.Fast)
                && !string.IsNullOrWhiteSpace(state.Capable) && state.Fast.Length <= 128 && state.Capable.Length <= 128
                && state.ExpiresAt > now && state.ExpiresAt <= now.AddHours(3).AddMinutes(1) ? state : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return null;
        }
    }
}
