// SPDX-License-Identifier: Apache-2.0
using Demo.Web.Middleware;
using Demo.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
// Lowercase generated URLs. Load-bearing for /admin: the session cookie is
// scoped Path=/admin, and RFC 6265 path matching is case-sensitive — a
// redirect or form action pointing at Razor Pages' canonical "/Admin" would
// never receive the cookie (the login "succeeded" straight back to the form).
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteOptions>(o => o.LowercaseUrls = true);

// ---- configuration (env vars win over appsettings) -------------------------

// Gitignored secrets file (2026-09-01): the place to put values that must
// never be committed — today the SendGrid API key ("SendGrid": {"ApiKey":
// "SG...."}). Excluded from publish output and image build context.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: false);

var munariumOptions = new MunariumOptions
{
    BaseUrl = Environment.GetEnvironmentVariable("MUNARIUM_BASE_URL")
              ?? builder.Configuration["Munarium:BaseUrl"]
              ?? "http://localhost:8080",
    MgmtToken = Environment.GetEnvironmentVariable("MUNARIUM_MGMT_TOKEN")
                ?? builder.Configuration["Munarium:MgmtToken"]
                ?? "",
};
if (!Uri.TryCreate(munariumOptions.BaseUrl, UriKind.Absolute, out var backendUri)
    || backendUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(backendUri.UserInfo))
    throw new InvalidOperationException("MUNARIUM_BASE_URL must be an HTTP(S) origin without credentials.");
builder.Services.AddSingleton(munariumOptions);
builder.Services.AddSingleton(new DemoIdentity(builder.Configuration));

var matrixOptions = new MatrixOptions
{
    BaseUrl = Environment.GetEnvironmentVariable("MATRIX_BASE_URL")
              ?? builder.Configuration["Matrix:BaseUrl"]
              ?? "",
    MgmtToken = Environment.GetEnvironmentVariable("MATRIX_MGMT_TOKEN")
                ?? builder.Configuration["Matrix:MgmtToken"]
                ?? "",
    AdminConsoleShown = (Environment.GetEnvironmentVariable("MATRIX_ADMIN_SHOWN")
                         ?? builder.Configuration["Matrix:AdminShown"]
                         ?? "") is "1" or "true",
};
builder.Services.AddSingleton(matrixOptions);

var corpora = builder.Configuration.GetSection("Corpora").Get<Dictionary<string, CorpusConfig>>()
              ?? new Dictionary<string, CorpusConfig>();
builder.Services.AddSingleton(corpora);

// Gate secret resolution — fail closed outside Development.
var gateSecret = Environment.GetEnvironmentVariable("DEMO_GATE_SECRET")
                 ?? builder.Configuration["Gate:Secret"];
if (string.IsNullOrEmpty(gateSecret))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "DEMO_GATE_SECRET is not set and the environment is not Development. " +
            "Refusing to start un-gated.");
    }
    gateSecret = builder.Configuration["Gate:DevSecret"] ?? "demo-dev-secret";
}
if (!builder.Environment.IsDevelopment() && (gateSecret.Length < 32 || gateSecret.StartsWith("demo-dev", StringComparison.Ordinal)))
    throw new InvalidOperationException("Production requires a randomly generated DEMO_GATE_SECRET of at least 32 characters.");
var gateDisabled = builder.Environment.IsDevelopment()
                   && builder.Configuration.GetValue<bool>("Gate:Disabled");
// Optional age ceiling on visitor cookies (hours; unset/0 = midnight-UTC
// expiry only). The /gatekeeper page can change it at runtime; this env var
// is the value a restart comes back to.
TimeSpan? gateMaxAge = null;
if (double.TryParse(Environment.GetEnvironmentVariable("DEMO_GATE_MAX_AGE_HOURS"),
        out var maxAgeHours) && maxAgeHours > 0)
{
    gateMaxAge = TimeSpan.FromHours(maxAgeHours);
}
builder.Services.AddSingleton(new GateService(gateSecret, gateDisabled, gateMaxAge));

// The demo's only persistent state: the visitor registry / allow-deny store
// (emails, code slugs, frontier counters). On Azure the path sits on the
// mounted file share so it survives restarts; locally it lands beside the app.
var storePath = Environment.GetEnvironmentVariable("DEMO_STORE_PATH")
                ?? builder.Configuration["DemoStore:Path"]
                ?? Path.Combine(builder.Environment.ContentRootPath, "data", "demo.sqlite");
builder.Services.AddSingleton(new DemoStore(storePath));

// /admin credentials (2026-09-01): env/secret only, never committed. Unset
// outside Development = the admin login is disabled (fail closed).
var adminUser = Environment.GetEnvironmentVariable("DEMO_ADMIN_USER")
                ?? builder.Configuration["Admin:User"];
var adminPassword = Environment.GetEnvironmentVariable("DEMO_ADMIN_PASSWORD")
                    ?? builder.Configuration["Admin:Password"];
if (!builder.Environment.IsDevelopment() && !string.IsNullOrEmpty(adminPassword) && adminPassword.Length < 16)
    throw new InvalidOperationException("Production admin passwords must contain at least 16 characters.");
builder.Services.AddSingleton(new AdminCredentials(adminUser, adminPassword));

// The battery's sign-in address (2026-09-02): /admin badges its row as
// scripted traffic. The default matches demo-corpus-check.ps1's -Email default.
builder.Services.AddSingleton(new BatteryIdentity(
    Environment.GetEnvironmentVariable("DEMO_VERIFY_EMAIL")
    ?? builder.Configuration["Gate:VerifyEmail"]));

// Login-code delivery (2026-09-01 evening): SendGrid, from info@ioka.io. The
// key comes from env or appsettings and is NEVER committed; with no key in
// Development the sender logs the code instead of sending it.
var sendgridKey = Environment.GetEnvironmentVariable("DEMO_SENDGRID_API_KEY")
                  ?? builder.Configuration["SendGrid:ApiKey"];
builder.Services.AddHttpClient("sendgrid", http =>
{
    http.BaseAddress = new Uri("https://api.sendgrid.com/");
    http.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton(sp => new MailSender(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("sendgrid"),
    sendgridKey,
    logOnly: builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(sendgridKey),
    sp.GetRequiredService<ILogger<MailSender>>(), builder.Configuration));

builder.Services.AddSingleton<TurnBudget>();
builder.Services.AddSingleton<ConversationCondenser>();
builder.Services.AddHttpClient<MunariumClient>(http =>
{
    // Completions on the capable tier can take a while; retrieval-only calls
    // return fast. One generous ceiling covers both.
    http.Timeout = TimeSpan.FromSeconds(150);
});
builder.Services.AddSingleton<TokenCache>();
builder.Services.AddSingleton<ModelCatalogCache>();
builder.Services.AddHttpClient<OllamaAvailability>();

var app = builder.Build();

// Trust only explicitly configured ingress addresses. Loopback proxies remain trusted.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2,
};
foreach (var address in (builder.Configuration["DEMO_TRUSTED_PROXIES"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
    forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(address.Trim()));
app.UseForwardedHeaders(forwarded);

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<RobotsMiddleware>();
app.UseMiddleware<GateMiddleware>();

// Static files: after the gate so /downloads is protected; /css, /js, /images,
// /fonts pass through the gate's allowlist. YAML downloads get text/yaml.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".yaml"] = "text/yaml";
contentTypes.Mappings[".yml"] = "text/yaml";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

app.MapRazorPages();

// Tiny logout (2026-09-01, footer link): drop the visitor's gate cookie and
// return to the gate. The cookie is stateless, so "log out" IS deleting it.
app.MapGet("/logout", (HttpContext http) =>
{
    http.Response.Cookies.Delete(GateService.CookieName, new CookieOptions { Path = "/" });
    return Results.Redirect("/gate");
});

// ---- BFF API ---------------------------------------------------------------

// UI family -> the server-side ProviderConfig name the model_override names.
static string ProviderConfigFor(string? family) => (family ?? "").ToLowerInvariant() switch
{
    "gpt" => "demo-openai",
    "openrouter" => "demo-openrouter",
    "ollama" => "demo-ollama",
    _ => "demo-anthropic",
};

// Per-email attribution (2026-09-01; per-code 2026-08-25, per-visitor
// 2026-08-23): the uid every data-plane call asserts is the PSEUDONYM of the
// registered email (`em-…`, HMAC-derived — the email itself never leaves the
// demo's own store), carried in the signed gate cookie. The server's per-uid
// usage report and audit trail roll up per email without ever seeing one;
// /admin joins pseudonym back to email locally. The per-visitor turn budget
// still keys on the cookie's nonce, so two browsers on one email keep
// separate budgets while their usage counts together. A request with no
// admitted cookie (the gate disabled in Development, or an old-format
// cookie) falls back to the anonymous per-visitor hash.
static string VisitorUid(HttpContext http)
{
    var gate = http.RequestServices.GetRequiredService<GateService>();
    var uid = gate.UidFromCookie(http.Request.Cookies[GateService.CookieName]);
    if (uid is not null) return uid;
    return "visitor-" + TurnBudget.VisitorKey(
        http.Request.Cookies[GateService.CookieName],
        http.Connection.RemoteIpAddress?.ToString()).ToLowerInvariant();
}

static (int Status, object Payload)? Unavailable(Exception ex) => ex switch
{
    MunariumUnreachableException => (503, new
    {
        error = "asleep",
        message = "The demo environment appears to be asleep or unreachable. " +
                  "It scales to zero when idle — try again in a minute.",
    }),
    // A server 429 stays a 429 — flattening it into the 502 branch below
    // loses exactly the signal the visitor can act on. daily-cap-reached is
    // the spending cap (resets midnight UTC, or drop a tier); rate-limited
    // is the rpm bucket (try again in a minute).
    MunariumApiException api when api.Status == 429 => (429, new
    {
        error = (api.ProblemType ?? "").EndsWith("daily-cap-reached") ? "model-budget" : "rate-limited",
        message = "This model tier is rate limited right now — try again shortly.",
        status = api.Status,
        problemType = "upstream-error",
    }),
    MunariumApiException api => (502, new
    {
        error = "munarium",
        message = $"The backend could not complete this request (HTTP {api.Status}).",
        status = api.Status,
        problemType = "upstream-error",
    }),
    _ => null,
};

app.MapPost("/api/session/{corpus}", async (
    string corpus, SessionApiRequest? request, HttpContext http,
    Dictionary<string, CorpusConfig> corporaMap, TokenCache tokens, MunariumClient munarium,
    CancellationToken ct) =>
{
    if (!corporaMap.TryGetValue(corpus, out var config))
        return Results.NotFound(new { error = "unknown-corpus", message = $"No corpus '{corpus}'." });

    int level;
    string[] compartments;
    try
    {
        (level, compartments) = config.Resolve(request?.Persona);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "unknown-persona", message = ex.Message });
    }

    var uid = VisitorUid(http);
    try
    {
        var jwt = await tokens.GetTokenAsync(config.Runbook, level, compartments, uid, ct);
        var session = await munarium.CreateSessionAsync(config.Runbook, jwt, uid, ct);
        return Results.Json(new
        {
            sessionId = session.SessionId,
            runbook = session.RunbookRef,
            permittedCollections = session.PermittedCollections,
            persona = request?.Persona,
        });
    }
    catch (Exception ex) when (Unavailable(ex) is { } mapped)
    {
        return Results.Json(mapped.Payload, statusCode: mapped.Status);
    }
});

app.MapPost("/api/chat/{corpus}", async (
    string corpus, ChatApiRequest request, HttpContext http,
    Dictionary<string, CorpusConfig> corporaMap, TokenCache tokens, MunariumClient munarium,
    ConversationCondenser condenser, TurnBudget budget, DemoStore store, OllamaAvailability ollama, CancellationToken ct) =>
{
    if (!corporaMap.TryGetValue(corpus, out var config))
        return Results.NotFound(new { error = "unknown-corpus", message = $"No corpus '{corpus}'." });
    var message = (request.Message ?? "").Trim();
    if (message.Length == 0)
        return Results.BadRequest(new { error = "empty-message", message = "Ask a question first." });
    if (message.Length > 1000)
        return Results.BadRequest(new
        {
            error = "message-too-long",
            message = "Questions are capped at 1,000 characters for this demo.",
        });

    if (await ValidateModelSelection(request, ollama, ct) is { } selectionError) return selectionError;

    var visitor = TurnBudget.VisitorKey(
        http.Request.Cookies[GateService.CookieName],
        http.Connection.RemoteIpAddress?.ToString());
    if (!budget.TryConsume(visitor, out var remaining))
    {
        return Results.Json(new
        {
            error = "turn-budget",
            message = $"You have used all {budget.DailyCap} demo turns for today. " +
                      "The budget resets at midnight UTC.",
        }, statusCode: 429);
    }

    int level;
    string[] compartments;
    try
    {
        (level, compartments) = config.Resolve(request.Persona);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "unknown-persona", message = ex.Message });
    }

    var provider = ProviderConfigFor(request.Family);
    var tier = (request.Tier ?? "").ToLowerInvariant() switch
    {
        "capable" => "capable",
        "frontier" => "frontier",
        _ => "fast",
    };
    var uid = VisitorUid(http);
    // Frontier is open to every visitor but budgeted (2026-09-01): two
    // requests per collection per UTC day (per-email override on /admin).
    // Checked BEFORE the turn; deliberately no refund on failure — one of
    // two slots spent on a failed turn is acceptable and simpler than
    // settle-or-release at this scale.
    if (tier == "frontier")
    {
        var frontierCap = await store.FrontierCapAsync(uid);
        var (frontierOk, _) = await store.TryConsumeFrontierAsync(uid, corpus, frontierCap);
        if (!frontierOk)
        {
            return Results.Json(new
            {
                error = "frontier-budget",
                message = $"You've used your {frontierCap ?? DemoStore.DefaultFrontierCap} Frontier " +
                          "requests for this collection today — Fast and Capable remain available, " +
                          "and the limit resets at midnight UTC.",
            }, statusCode: 429);
        }
    }
    var history = (request.History ?? [])
        .Select(h => new ConversationCondenser.HistoryTurn(h.Role ?? "user", h.Text ?? ""))
        .ToList();
    var query = condenser.Condense(history, message, request.FollowUp);
    try
    {
        var jwt = await tokens.GetTokenAsync(config.Runbook, level, compartments, uid, ct);
        var sessionId = request.FollowUp ? request.SessionId : null;
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = (await munarium.CreateSessionAsync(config.Runbook, jwt, uid, ct)).SessionId;
        }

        MunariumClient.TurnResponse turn;
        try
        {
            turn = await munarium.PostTurnAsync(sessionId, jwt, query, provider, tier, uid, ct);
        }
        catch (MunariumApiException ex) when (ex.IsSessionNotOpen || ex.IsSessionUidMismatch)
        {
            // The session expired, was closed server-side, or belongs to a
            // pre-attribution/rotated uid — recreate once.
            sessionId = (await munarium.CreateSessionAsync(config.Runbook, jwt, uid, ct)).SessionId;
            turn = await munarium.PostTurnAsync(sessionId, jwt, query, provider, tier, uid, ct);
        }

        var completion = turn.Completion;
        var verification = completion?.Verification;
        return Results.Json(new
        {
            sessionId,
            answer = string.IsNullOrWhiteSpace(completion?.Text)
                ? "(The model returned no answer text this turn — its token budget went to hidden reasoning. Ask again to retry.)"
                : completion!.Text,
            model = completion?.Model ?? "",
            provider = completion?.Provider ?? "",
            wasOverride = completion?.WasOverride ?? false,
            inputTokens = completion?.InputTokens ?? 0,
            outputTokens = completion?.OutputTokens ?? 0,
            turnsRemaining = remaining,
            hits = turn.Hits.Select(h => new
            {
                docId = h.SourcePath,
                collection = h.Collection,
                snippet = h.Text.Length > 240 ? h.Text[..240] + "…" : h.Text,
            }),
            verification = verification is null ? null : new
            {
                verified = verification.Violations.Length == 0,
                checks = verification.Checks,
                retries = verification.Retries,
                firstPassViolations = verification.FirstPassViolations,
                violations = verification.Violations,
            },
        });
    }
    catch (Exception ex) when (Unavailable(ex) is { } mapped)
    {
        return Results.Json(mapped.Payload, statusCode: mapped.Status);
    }
});

// Streaming chat: same contract as /api/chat/{corpus}, but the response is
// text/event-stream — `progress` frames (the server's TurnProgressEvent JSON,
// passed through verbatim) followed by exactly one `done` (the same JSON
// shape the unary endpoint returns) or `error`. Validation failures before
// the stream starts answer plain JSON like the unary endpoint.
app.MapPost("/api/chat/{corpus}/stream", async (
    string corpus, ChatApiRequest request, HttpContext http,
    Dictionary<string, CorpusConfig> corporaMap, TokenCache tokens, MunariumClient munarium,
    ConversationCondenser condenser, TurnBudget budget, DemoStore store, OllamaAvailability ollama, CancellationToken ct) =>
{
    if (!corporaMap.TryGetValue(corpus, out var config))
        return Results.NotFound(new { error = "unknown-corpus", message = $"No corpus '{corpus}'." });
    var message = (request.Message ?? "").Trim();
    if (message.Length == 0)
        return Results.BadRequest(new { error = "empty-message", message = "Ask a question first." });
    if (message.Length > 1000)
        return Results.BadRequest(new
        {
            error = "message-too-long",
            message = "Questions are capped at 1,000 characters for this demo.",
        });

    if (await ValidateModelSelection(request, ollama, ct) is { } selectionError) return selectionError;

    var visitor = TurnBudget.VisitorKey(
        http.Request.Cookies[GateService.CookieName],
        http.Connection.RemoteIpAddress?.ToString());
    if (!budget.TryConsume(visitor, out var remaining))
    {
        return Results.Json(new
        {
            error = "turn-budget",
            message = $"You have used all {budget.DailyCap} demo turns for today. " +
                      "The budget resets at midnight UTC.",
        }, statusCode: 429);
    }

    int level;
    string[] compartments;
    try
    {
        (level, compartments) = config.Resolve(request.Persona);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "unknown-persona", message = ex.Message });
    }

    var provider = ProviderConfigFor(request.Family);
    var tier = (request.Tier ?? "").ToLowerInvariant() switch
    {
        "capable" => "capable",
        "frontier" => "frontier",
        _ => "fast",
    };
    var uid = VisitorUid(http);
    // Frontier is open to every visitor but budgeted (2026-09-01): two
    // requests per collection per UTC day (per-email override on /admin).
    // Checked BEFORE the turn; deliberately no refund on failure — one of
    // two slots spent on a failed turn is acceptable and simpler than
    // settle-or-release at this scale.
    if (tier == "frontier")
    {
        var frontierCap = await store.FrontierCapAsync(uid);
        var (frontierOk, _) = await store.TryConsumeFrontierAsync(uid, corpus, frontierCap);
        if (!frontierOk)
        {
            return Results.Json(new
            {
                error = "frontier-budget",
                message = $"You've used your {frontierCap ?? DemoStore.DefaultFrontierCap} Frontier " +
                          "requests for this collection today — Fast and Capable remain available, " +
                          "and the limit resets at midnight UTC.",
            }, statusCode: 429);
        }
    }
    var history = (request.History ?? [])
        .Select(h => new ConversationCondenser.HistoryTurn(h.Role ?? "user", h.Text ?? ""))
        .ToList();
    var query = condenser.Condense(history, message, request.FollowUp);

    async IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<string>> Stream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        string sessionId = "";
        string jwt = "";
        object? fatal = null;
        try
        {
            jwt = await tokens.GetTokenAsync(config.Runbook, level, compartments, uid, token);
            sessionId = !request.FollowUp || string.IsNullOrEmpty(request.SessionId)
                ? (await munarium.CreateSessionAsync(config.Runbook, jwt, uid, token)).SessionId
                : request.SessionId!;
        }
        catch (Exception ex) when (Unavailable(ex) is { } mapped)
        {
            fatal = mapped.Payload;
        }
        if (fatal is not null)
        {
            yield return SseError(fatal);
            yield break;
        }

        var recreated = false;
        while (true)
        {
            MunariumClient.TurnResponse? turn = null;
            var sessionNotOpen = false;
            object? errorPayload = null;

            var events = munarium
                .PostTurnStreamAsync(sessionId, jwt, query, provider, tier, uid, token)
                .GetAsyncEnumerator(token);
            try
            {
                while (true)
                {
                    MunariumClient.TurnStreamEvent? ev = null;
                    try
                    {
                        if (!await events.MoveNextAsync()) break;
                        ev = events.Current;
                    }
                    catch (Exception ex) when (Unavailable(ex) is { } mapped)
                    {
                        errorPayload = mapped.Payload;
                        break;
                    }
                    if (ev.Event == "progress")
                    {
                        yield return new(ev.Data, eventType: "progress");
                    }
                    else if (ev.Event == "done")
                    {
                        turn = MunariumClient.ParseTurnResponse(ev.Data);
                        break;
                    }
                    else if (ev.Event == "error")
                    {
                        if (!recreated &&
                            (ev.Data.Contains("session-not-open") || ev.Data.Contains("different uid")))
                        {
                            sessionNotOpen = true;
                            break;
                        }
                        var (type, _) = MunariumClient.ParseProblemJson(ev.Data);
                        // Name the refusals the UI has guidance for: the
                        // daily spending cap and a plain rate limit keep
                        // their identity instead of flattening to "munarium".
                        var errName = (type ?? "").EndsWith("daily-cap-reached") ? "model-budget"
                            : (type ?? "").EndsWith("rate-limited") ? "rate-limited"
                            : "munarium";
                        errorPayload = new
                        {
                            error = errName,
                            message = "The backend could not complete this request.",
                            problemType = "upstream-error",
                        };
                        break;
                    }
                }
            }
            finally
            {
                await events.DisposeAsync();
            }

            if (errorPayload is not null)
            {
                yield return SseError(errorPayload);
                yield break;
            }

            if (sessionNotOpen)
            {
                // The session expired, was closed server-side, or belongs to
                // a pre-attribution/rotated uid — recreate once.
                recreated = true;
                try
                {
                    sessionId = (await munarium.CreateSessionAsync(config.Runbook, jwt, uid, token)).SessionId;
                }
                catch (Exception ex) when (Unavailable(ex) is { } mapped)
                {
                    fatal = mapped.Payload;
                }
                if (fatal is not null)
                {
                    yield return SseError(fatal);
                    yield break;
                }
                continue;
            }

            if (turn is not null)
            {
                var completion = turn.Completion;
                var verification = completion?.Verification;
                var done = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId,
                    answer = string.IsNullOrWhiteSpace(completion?.Text)
                        ? "(The model returned no answer text this turn — its token budget went to hidden reasoning. Ask again to retry.)"
                        : completion!.Text,
                    model = completion?.Model ?? "",
                    provider = completion?.Provider ?? "",
                    wasOverride = completion?.WasOverride ?? false,
                    inputTokens = completion?.InputTokens ?? 0,
                    outputTokens = completion?.OutputTokens ?? 0,
                    turnsRemaining = remaining,
                    hits = turn.Hits.Select(h => new
                    {
                        docId = h.SourcePath,
                        collection = h.Collection,
                        snippet = h.Text.Length > 240 ? h.Text[..240] + "…" : h.Text,
                    }),
                    verification = verification is null ? null : new
                    {
                        verified = verification.Violations.Length == 0,
                        checks = verification.Checks,
                        retries = verification.Retries,
                        firstPassViolations = verification.FirstPassViolations,
                        violations = verification.Violations,
                    },
                });
                yield return new(done, eventType: "done");
            }
            yield break;
        }
    }

    return TypedResults.ServerSentEvents(Stream(ct));

    static System.Net.ServerSentEvents.SseItem<string> SseError(object payload) =>
        new(System.Text.Json.JsonSerializer.Serialize(payload), eventType: "error");
});

// Full disclosure: the concrete model each family x tier choice resolves to,
// straight from the server's free introspection plane (GET /v1/providers —
// zero provider calls). The RAW introspection is cached for 5 minutes; since
// 2026-09-01 every tier (frontier included) shows to every visitor — frontier
// is budgeted per collection per day, not hidden.
app.MapGet("/api/models", async (
    ModelCatalogCache catalogCache, MunariumClient munarium, OllamaAvailability ollama, HttpContext http,
    CancellationToken ct) =>
{
    try
    {
        http.Response.Headers.CacheControl = "no-store";
        var readiness = await ollama.GetAsync(ct);
        var raw = (MunariumClient.ProviderListResponse)await catalogCache.GetAsync(
            async () => (object)await munarium.ListProvidersAsync(ct));
        object? Family(string uiFamily)
        {
            var configName = ProviderConfigFor(uiFamily);
            var entry = raw.Providers.FirstOrDefault(p => p.Name == configName);
            if (entry is null || !entry.CredentialOk) return null;
            if (uiFamily == "ollama" && (readiness is null || entry.Provider != "ollama"
                || !entry.CredentialOk || entry.Fast != readiness.Fast || entry.Capable != readiness.Capable)) return null;
            return new
            {
                config = entry.Name,
                provider = entry.Provider,
                fast = entry.Fast,
                capable = entry.Capable,
                frontier = uiFamily == "ollama" ? null : entry.Frontier,
                credentialOk = entry.CredentialOk,
                expiresAt = uiFamily == "ollama" ? readiness?.ExpiresAt : null,
            };
        }
        return Results.Json(new
        {
            families = new
            {
                claude = Family("claude"),
                gpt = Family("gpt"),
                openrouter = Family("openrouter"),
                ollama = Family("ollama"),
            },
        });
    }
    catch (Exception ex) when (Unavailable(ex) is { } mapped)
    {
        return Results.Json(mapped.Payload, statusCode: mapped.Status);
    }
});

app.MapPost("/api/search/{corpus}", async (
    string corpus, SearchApiRequest request, HttpContext http,
    Dictionary<string, CorpusConfig> corporaMap, TokenCache tokens, MunariumClient munarium,
    CancellationToken ct) =>
{
    if (!corporaMap.TryGetValue(corpus, out var config))
        return Results.NotFound(new { error = "unknown-corpus", message = $"No corpus '{corpus}'." });
    var query = (request.Query ?? "").Trim();
    if (query.Length == 0)
        return Results.BadRequest(new { error = "empty-query", message = "Enter a search query." });
    if (query.Length > 500)
        return Results.BadRequest(new { error = "query-too-long", message = "Search queries are capped at 500 characters." });

    int level;
    string[] compartments;
    try
    {
        (level, compartments) = config.Resolve(request.Persona);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "unknown-persona", message = ex.Message });
    }

    var uid = VisitorUid(http);
    try
    {
        var jwt = await tokens.GetTokenAsync(config.Runbook, level, compartments, uid, ct);
        // Retrieval-only turn: needs a session, so make one per search and
        // close it — searches are stateless here and a session pins clearance
        // at creation, which is exactly what the persona selector wants.
        var session = await munarium.CreateSessionAsync(config.Runbook, jwt, uid, ct);
        MunariumClient.TurnResponse result;
        try
        {
            result = await munarium.SearchAsync(session.SessionId, jwt, query, uid, ct);
        }
        finally
        {
            try { await munarium.CloseSessionAsync(session.SessionId, jwt, uid, ct); }
            catch (Exception) { /* best effort; sessions expire on their own */ }
        }
        return Results.Json(new
        {
            collectionsSearched = result.CollectionsSearched,
            hits = result.Hits.Select(h => new
            {
                docId = h.SourcePath,
                collection = h.Collection,
                snippet = h.Text.Length > 280 ? h.Text[..280] + "…" : h.Text,
                score = Math.Round(h.Score, 4),
            }),
        });
    }
    catch (Exception ex) when (Unavailable(ex) is { } mapped)
    {
        return Results.Json(mapped.Payload, statusCode: mapped.Status);
    }
});

// View-only passthrough to the munarium server's /admin operator console
// (2026-08-23; view-only header 2026-08-27). GET only — the BFF injects the
// mgmt bearer, so the server's login page is never needed, and no control-
// plane action (token issue/revoke, runbook gate approval) can be submitted
// through the demo: POSTs never match this route, and the
// X-Munarium-Admin-View-Only header tells the server to render every action
// form as a note instead of a button that would 405 here. Gated like every
// other page. The pages are self-contained server-rendered HTML + inline SVG
// with same-prefix relative links, so a byte-for-byte body copy is a working
// proxy.
app.MapGet("/admin/{**rest}", async (
    string? rest, HttpContext http, IHttpClientFactory httpFactory, MunariumOptions options,
    CancellationToken ct) =>
{
    if (!app.Configuration.GetValue<bool>("OperatorConsole:Enabled")) return Results.NotFound();
    var operatorGate = http.RequestServices.GetRequiredService<GateService>();
    if (!operatorGate.ValidateAdminSession(http.Request.Cookies[GateService.AdminCookieName])) return Results.Unauthorized();
    // Exact /admin became the demo's own gate-admin page (2026-09-01), so
    // the server console's OVERVIEW answers at the "console" alias; every
    // deeper path proxies unchanged. The console's own nav links to
    // "/admin" and will land on the gate-admin page — one extra click.
    if (string.Equals(rest, "console", StringComparison.OrdinalIgnoreCase)) rest = null;
    var target = options.BaseUrl.TrimEnd('/') + "/admin"
                 + (string.IsNullOrEmpty(rest) ? "" : "/" + rest)
                 + http.Request.QueryString;
    using var upstream = new HttpRequestMessage(HttpMethod.Get, target);
    upstream.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.MgmtToken}");
    upstream.Headers.TryAddWithoutValidation("X-Munarium-Admin-View-Only", "1");
    var client = httpFactory.CreateClient("admin-proxy");
    HttpResponseMessage resp;
    try
    {
        resp = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new
        {
            error = "asleep",
            message = "The munarium backend is unreachable — the admin dashboards live there.",
        }, statusCode: 503);
    }
    using (resp)
    {
        http.Response.StatusCode = (int)resp.StatusCode;
        http.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "text/html";
        await resp.Content.CopyToAsync(http.Response.Body, ct);
    }
    return Results.Empty;
});

// View-only passthrough to Munarium MATRIX's /admin operator console
// (WP-7.7, 2026-08-31). Same contract as the server console proxy above —
// GET only, gate-cookie required, the BFF injects Matrix's mgmt bearer so
// its login page is never needed, and Matrix honours the same
// X-Munarium-Admin-View-Only header (it refuses writes as well as hiding the
// buttons). One difference of shape: Matrix's pages emit root-relative
// /admin/... links from its own nav, and /admin here is the SERVER console —
// so HTML bodies are rewritten "/admin → "/matrix-admin to keep navigation
// inside this proxy. The console is zero-JavaScript server-rendered HTML
// with inline SVG, which is what makes a quoted-attribute rewrite complete;
// non-HTML bodies stream byte-for-byte. Disabled (404) when no Matrix is
// configured, so a deployment without one never grows a dead route.
app.MapGet("/matrix-admin/{**rest}", async (
    string? rest, HttpContext http, IHttpClientFactory httpFactory, MatrixOptions matrix,
    CancellationToken ct) =>
{
    if (!app.Configuration.GetValue<bool>("OperatorConsole:Enabled")) return Results.NotFound();
    var operatorGate = http.RequestServices.GetRequiredService<GateService>();
    if (!operatorGate.ValidateAdminSession(http.Request.Cookies[GateService.AdminCookieName])) return Results.Unauthorized();
    if (!matrix.ConsoleVisible)
    {
        return Results.NotFound();
    }
    var target = matrix.BaseUrl.TrimEnd('/') + "/admin"
                 + (string.IsNullOrEmpty(rest) ? "" : "/" + rest)
                 + http.Request.QueryString;
    using var upstream = new HttpRequestMessage(HttpMethod.Get, target);
    upstream.Headers.TryAddWithoutValidation("Authorization", $"Bearer {matrix.MgmtToken}");
    upstream.Headers.TryAddWithoutValidation("X-Munarium-Admin-View-Only", "1");
    var client = httpFactory.CreateClient("admin-proxy");
    HttpResponseMessage resp;
    try
    {
        resp = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, ct);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return Results.Json(new
        {
            error = "asleep",
            message = "Munarium Matrix is unreachable — it scales to zero when parked.",
        }, statusCode: 503);
    }
    using (resp)
    {
        http.Response.StatusCode = (int)resp.StatusCode;
        var contentType = resp.Content.Headers.ContentType?.ToString() ?? "text/html";
        http.Response.ContentType = contentType;
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            body = body.Replace("\"/admin", "\"/matrix-admin", StringComparison.Ordinal);
            await http.Response.WriteAsync(body, ct);
        }
        else
        {
            await resp.Content.CopyToAsync(http.Response.Body, ct);
        }
    }
    return Results.Empty;
});

// Liveness performs no backend I/O; a slow backend must not restart this app.
app.MapGet("/livez", () => Results.Json(new { ok = true }));

// Legacy reachability diagnostic, retained for existing operator clients.
app.MapGet("/healthz", async (MunariumClient munarium, CancellationToken ct) =>
{
    var reachable = await munarium.IsReachableAsync(ct);
    return Results.Json(new { ok = true, munarium = reachable });
});

// Backend failures remove this replica from traffic without restarting the web app.
app.MapGet("/readyz", async (MunariumClient munarium, CancellationToken ct) =>
{
    var ready = await munarium.IsReadyAsync(ct);
    return Results.Json(new { ok = ready, munarium = ready }, statusCode: ready ? 200 : 503);
});

static async Task<IResult?> ValidateModelSelection(ChatApiRequest request, OllamaAvailability ollama, CancellationToken ct)
{
    var family = (request.Family ?? "claude").ToLowerInvariant();
    var tier = (request.Tier ?? "fast").ToLowerInvariant();
    if (family is not ("claude" or "gpt" or "openrouter" or "ollama") || tier is not ("fast" or "capable" or "frontier"))
        return Results.BadRequest(new { error = "unknown-model-selection", message = "Choose an available provider and tier." });
    if (family != "ollama") return null;
    if (tier == "frontier")
        return Results.BadRequest(new { error = "unsupported-tier", message = "Ollama offers Fast and Capable tiers." });
    if (await ollama.GetAsync(ct) is null)
        return Results.Json(new { error = "ollama-unavailable", message = "Ollama is not available right now. Choose another provider." }, statusCode: 503);
    return null;
}

app.Run();

// ---- API request records ----------------------------------------------------

public sealed record SessionApiRequest(string? Persona);

public sealed record ChatHistoryItem(string? Role, string? Text);

public sealed record ChatApiRequest(
    string? SessionId, string? Message, List<ChatHistoryItem>? History,
    string? Family, string? Tier, string? Persona, bool FollowUp = false);

public sealed record SearchApiRequest(string? Query, string? Persona);
