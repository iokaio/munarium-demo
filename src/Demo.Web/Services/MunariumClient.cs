// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Demo.Web.Services;

/// <summary>
/// Raw-HTTP BFF client for the munarium-server REST API (no SDK, System.Text.Json
/// only). Field names follow the server's snake_case wire contract
/// (server/docs/api/rest.md + munarium-api-types). Every call carries
/// X-Munarium-Uid; data-plane calls use a capability JWT, token minting uses the
/// management token.
/// </summary>
public sealed class MunariumClient
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly MunariumOptions _options;

    public MunariumClient(HttpClient http, MunariumOptions options)
    {
        _http = http;
        _options = options;
    }

    // ---- DTOs (wire names are snake_case via the serializer policy) --------

    public sealed record IssueTokenRequest(
        string Uid, int AccessLevel, string[] Compartments, string[] Scopes,
        string[]? RunbookRefs, long? TtlSecs);

    public sealed record IssueTokenResponse(string Token, string Jti, string ExpiresAt);

    public sealed record CreateSessionResponse(
        string SessionId, string RunbookRef, string[] PermittedCollections);

    public sealed record ModelOverride(string? Provider, string? Model, string? Tier);

    public sealed record TurnRequest(string Query, bool? Complete, ModelOverride? ModelOverride);

    public sealed record TurnHit(
        string Collection, string ChunkId, string SourceId, string SourcePath,
        string SourceContentHash, string Text, double Score);

    public sealed record TurnVerification(
        string[] Checks, int Retries, string[] FirstPassViolations, string[] Violations);

    public sealed record TurnCompletion(
        string Provider, string Model, bool WasOverride, string Text,
        long InputTokens, long OutputTokens, TurnVerification? Verification);

    public sealed record TurnResponse(
        string SessionId, int Ordinal, string[] CollectionsSearched, string[]? Skipped,
        TurnHit[] Hits, TurnCompletion? Completion);

    public sealed record SearchRequest(string Query, int? TopK, string? ShapeRef);

    /// <summary>One SSE frame from the streaming turn plane: `progress` (a
    /// stage event), `done` (a full TurnResponse), or `error` (problem+json).</summary>
    public sealed record TurnStreamEvent(string Event, string Data);

    public sealed record ProviderModels(
        string Name, string Provider, string Source, bool CredentialOk,
        string? Fast, string? Capable, string? Frontier);

    public sealed record ProviderListResponse(ProviderModels[] Providers);

    // ---- reports DTOs (mgmt plane) -----------------------------------------

    public sealed record UsageRow(
        string Key, long Interactions, long Turns,
        long CompletionInputTokens, long CompletionOutputTokens, double? AvgLatencyMs);

    public sealed record UsageReport(string GroupBy, string? From, string? To, UsageRow[] Rows);

    public sealed record CostRow(
        string Provider, string Model, long Turns, long OverriddenTurns,
        long InputTokens, long OutputTokens);

    public sealed record CostReport(string? From, string? To, CostRow[] Rows);

    public sealed record TimeseriesBucket(
        string Bucket, long Requests,
        [property: JsonPropertyName("errors_4xx")] long Errors4xx,
        [property: JsonPropertyName("errors_5xx")] long Errors5xx,
        [property: JsonPropertyName("p50_latency_ms")] double? P50LatencyMs,
        [property: JsonPropertyName("p95_latency_ms")] double? P95LatencyMs);

    public sealed record TimeseriesReport(
        string Window, long BucketSeconds, string? Plane, TimeseriesBucket[] Buckets);

    public sealed record SessionsBucket(
        string Bucket, long SessionsOpened, long Turns, long ActiveUids);

    public sealed record SessionsReport(string Window, long BucketSeconds, SessionsBucket[] Buckets);

    public sealed record SearchHit(
        string ChunkId, string SourceId, string SourcePath, string SourceContentHash,
        string Text, double Score);

    public sealed record SearchResponse(SearchHit[] Hits);

    // ---- calls -------------------------------------------------------------

    /// <summary>POST /v1/access-tokens with the management token — mints a query-scope
    /// capability JWT. `uid` attributes the token to a specific visitor
    /// (defaults to the app uid); the JWT's sub must match the X-Munarium-Uid every
    /// data-plane call sends, so the same uid must flow to those calls too.</summary>
    public async Task<IssueTokenResponse> MintTokenAsync(
        string runbook, int accessLevel, string[] compartments, string? uid = null,
        CancellationToken ct = default)
    {
        var body = new IssueTokenRequest(
            uid ?? _options.Uid, accessLevel, compartments, ["query"], [runbook], 3600);
        return await SendAsync<IssueTokenResponse>(
            HttpMethod.Post, "/v1/access-tokens", _options.MgmtToken, body, ct);
    }

    /// <summary>POST /v1/runbooks/{runbook}/sessions (no body) with the capability JWT.</summary>
    public async Task<CreateSessionResponse> CreateSessionAsync(
        string runbook, string jwt, string? uid = null, CancellationToken ct = default)
    {
        return await SendAsync<CreateSessionResponse>(
            HttpMethod.Post, $"/v1/runbooks/{Uri.EscapeDataString(runbook)}/sessions", jwt, new { }, ct, uid);
    }

    /// <summary>POST /v1/sessions/{id}/turns — retrieval + completion with an optional model override.</summary>
    public async Task<TurnResponse> PostTurnAsync(
        string sessionId, string jwt, string query, string? provider, string? tier,
        string? uid = null, CancellationToken ct = default)
    {
        var body = new TurnRequest(query, Complete: true,
            provider is null && tier is null ? null : new ModelOverride(provider, null, tier));
        return await SendAsync<TurnResponse>(
            HttpMethod.Post, $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/turns", jwt, body, ct, uid);
    }

    /// <summary>
    /// Retrieval-only search across the whole corpus: a session turn with
    /// `complete: false`, which returns the RRF-merged hits and no completion.
    ///
    /// Do NOT reach for `POST /v1/search` here. That route searches exactly
    /// ONE collection (its only supported filter is
    /// `{"collections": ["<name-or-id>"]}`) and its unfiltered form reads a
    /// legacy shape-level index that a collections runbook never builds —
    /// against the 58-collection archive it returns zero hits, silently.
    /// Merged multi-collection retrieval is the session plane's job, and
    /// going through a session also applies the caller's clearance.
    /// </summary>
    public async Task<TurnResponse> SearchAsync(
        string sessionId, string jwt, string query, string? uid = null,
        CancellationToken ct = default)
    {
        var body = new TurnRequest(query, Complete: false, null);
        return await SendAsync<TurnResponse>(
            HttpMethod.Post, $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/turns", jwt, body, ct, uid);
    }

    /// <summary>
    /// POST /v1/sessions/{id}/turns/stream — the SSE turn plane. Yields raw
    /// (event, data) frames as the server emits them; the caller interprets
    /// `progress`/`done`/`error`. A non-2xx BEFORE the stream starts throws
    /// MunariumApiException exactly like the unary call.
    /// </summary>
    public async IAsyncEnumerable<TurnStreamEvent> PostTurnStreamAsync(
        string sessionId, string jwt, string query, string? provider, string? tier,
        string? uid = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = new TurnRequest(query, Complete: true,
            provider is null && tier is null ? null : new ModelOverride(provider, null, tier));
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"/v1/sessions/{Uri.EscapeDataString(sessionId)}/turns/stream"));
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {jwt}");
        req.Headers.TryAddWithoutValidation("X-Munarium-Uid", uid ?? _options.Uid);
        req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        req.Content = new StringContent(
            JsonSerializer.Serialize(body, Wire), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MunariumUnreachableException(_options.BaseUrl, ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                var (type, detail) = ParseProblem(text);
                throw new MunariumApiException((int)resp.StatusCode, type, detail);
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? eventName = null;
            var data = new StringBuilder();
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0)
                {
                    if (eventName is not null || data.Length > 0)
                    {
                        yield return new TurnStreamEvent(eventName ?? "message", data.ToString());
                        eventName = null;
                        data.Clear();
                    }
                    continue;
                }
                if (line.StartsWith(':')) continue; // SSE keep-alive comment
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line[5..].TrimStart());
                }
            }
            if (eventName is not null || data.Length > 0)
                yield return new TurnStreamEvent(eventName ?? "message", data.ToString());
        }
    }

    /// <summary>GET /v1/providers — free tier→model disclosure (no provider calls).</summary>
    public async Task<ProviderListResponse> ListProvidersAsync(CancellationToken ct = default)
    {
        return await SendAsync<ProviderListResponse>(
            HttpMethod.Get, "/v1/providers", _options.MgmtToken, null, ct);
    }

    /// <summary>POST /v1/sessions/{id}/close — idempotent lifecycle end.</summary>
    public async Task CloseSessionAsync(
        string sessionId, string jwt, string? uid = null, CancellationToken ct = default)
    {
        await SendAsync<JsonElement>(
            HttpMethod.Post, $"/v1/sessions/{Uri.EscapeDataString(sessionId)}/close", jwt, new { }, ct, uid);
    }

    // ---- reports (mgmt token; read-only) -----------------------------------

    /// <summary>GET /v1/reports/usage?group_by= — per-uid/runbook interaction + turn + token rollups.</summary>
    public Task<UsageReport> GetUsageAsync(string groupBy, CancellationToken ct = default) =>
        SendAsync<UsageReport>(
            HttpMethod.Get, $"/v1/reports/usage?group_by={Uri.EscapeDataString(groupBy)}",
            _options.MgmtToken, null, ct);

    /// <summary>GET /v1/reports/cost — provider/model token-spend rollups.</summary>
    public Task<CostReport> GetCostAsync(CancellationToken ct = default) =>
        SendAsync<CostReport>(HttpMethod.Get, "/v1/reports/cost", _options.MgmtToken, null, ct);

    /// <summary>GET /v1/reports/timeseries?window= — bucketed traffic/error/latency series.</summary>
    public Task<TimeseriesReport> GetTimeseriesAsync(string window, CancellationToken ct = default) =>
        SendAsync<TimeseriesReport>(
            HttpMethod.Get, $"/v1/reports/timeseries?window={Uri.EscapeDataString(window)}",
            _options.MgmtToken, null, ct);

    /// <summary>GET /v1/reports/sessions?window= — sessions opened / turns / active uids per bucket.</summary>
    public Task<SessionsReport> GetSessionsReportAsync(string window, CancellationToken ct = default) =>
        SendAsync<SessionsReport>(
            HttpMethod.Get, $"/v1/reports/sessions?window={Uri.EscapeDataString(window)}",
            _options.MgmtToken, null, ct);

    /// <summary>GET {base}/healthz — reachability probe. Never throws.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var resp = await _http.GetAsync(BuildUri("/healthz"), cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Backend admission, including index hydration. Never throws.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var resp = await _http.GetAsync(BuildUri("/readyz"), cts.Token);
            if (!resp.IsSuccessStatusCode) return false;
            using var body = await JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            return body.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }

    // ---- plumbing ----------------------------------------------------------

    private Uri BuildUri(string path) => new(_options.BaseUrl.TrimEnd('/') + path);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, string bearer, object? body, CancellationToken ct,
        string? uid = null)
    {
        using var req = new HttpRequestMessage(method, BuildUri(path));
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        req.Headers.TryAddWithoutValidation("X-Munarium-Uid", uid ?? _options.Uid);
        if (body is not null)
        {
            req.Content = new StringContent(
                JsonSerializer.Serialize(body, Wire), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MunariumUnreachableException(_options.BaseUrl, ex);
        }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var (type, detail) = ParseProblem(text);
                throw new MunariumApiException((int)resp.StatusCode, type, detail);
            }
            return JsonSerializer.Deserialize<T>(text, Wire)
                   ?? throw new MunariumApiException(502, "empty-response", $"empty body from {path}");
        }
    }

    /// <summary>Deserialize a TurnResponse JSON body (the SSE `done` payload).</summary>
    public static TurnResponse? ParseTurnResponse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TurnResponse>(json, Wire);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parse a problem+json body (the SSE `error` payload).</summary>
    public static (string Type, string Detail) ParseProblemJson(string text) => ParseProblem(text);

    private static (string Type, string Detail) ParseProblem(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var detail = root.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
            if (detail.Length == 0 && root.TryGetProperty("title", out var ti))
                detail = ti.GetString() ?? "";
            return (type, detail);
        }
        catch (JsonException)
        {
            return ("", text.Length > 300 ? text[..300] : text);
        }
    }
}

/// <summary>A /v1 call failed with an HTTP error (problem+json parsed when present).</summary>
public sealed class MunariumApiException(int status, string problemType, string detail)
    : Exception($"munarium {status}: {problemType} {detail}".Trim())
{
    public int Status { get; } = status;
    public string ProblemType { get; } = problemType;
    public string Detail { get; } = detail;

    public bool IsSessionNotOpen =>
        Status == 409 && (ProblemType.Contains("session-not-open") || Detail.Contains("session-not-open"));

    /// <summary>403 on a turn against a session created under a different uid —
    /// happens when a browser holds a sessionId minted before per-visitor
    /// attribution (or the visitor's gate cookie rotated at UTC midnight).
    /// Recoverable exactly like session-not-open: recreate once.</summary>
    public bool IsSessionUidMismatch =>
        Status == 403 && Detail.Contains("different uid");
}

/// <summary>The munarium backend did not answer at all (asleep / scaled to zero / wrong URL).</summary>
public sealed class MunariumUnreachableException(string baseUrl, Exception inner)
    : Exception($"munarium backend unreachable at {baseUrl}", inner);
