// SPDX-License-Identifier: Apache-2.0
using Demo.Web.Services;

namespace Demo.Web.Middleware;

/// <summary>
/// Everything is behind the access-code gate except the gate page itself,
/// /healthz, /robots.txt, and the static asset paths (/css, /js, /images,
/// /fonts). NOTE: /downloads IS gated — the runbook/shape YAMLs are corpus
/// assets, not public statics. Ungated API calls get 401 JSON; ungated page
/// requests are redirected to /gate.
/// </summary>
public sealed class GateMiddleware
{
    private static readonly string[] OpenPrefixes = ["/css", "/js", "/images", "/fonts"];
    // Exact /admin is the unlinked gate-admin page (2026-09-01): it
    // authenticates with its own per-visit gate code, so the visitor gate
    // must not sit in front of it — an operator revoking access codes may
    // not hold one. Only the EXACT path is open (OpenPaths is an equals
    // check): /admin/<anything> is the operator-console passthrough and
    // stays visitor-gated. /logout just drops the visitor cookie.
    private static readonly string[] OpenPaths = ["/gate", "/admin", "/logout", "/healthz", "/livez", "/readyz", "/robots.txt"];

    private readonly RequestDelegate _next;
    private readonly GateService _gate;
    private readonly DemoStore _store;

    public GateMiddleware(RequestDelegate next, GateService gate, DemoStore store)
    {
        _next = next;
        _gate = gate;
        _store = store;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_gate.Disabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        var open =
            OpenPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)) ||
            OpenPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

        if (!open)
        {
            var cookie = context.Request.Cookies[GateService.CookieName];
            // A valid cookie whose email the admin has since BLOCKED is
            // treated exactly like no cookie (2026-09-01): the block bites
            // mid-day, within the store's 30 s cache, not at re-entry.
            var uid = _gate.UidFromCookie(cookie);
            if (uid is null || await _store.IsBlockedUidAsync(uid))
            {
                if (path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "gate",
                        message = "Access code required. Reload the page and enter your code.",
                    });
                    return;
                }
                var returnUrl = Uri.EscapeDataString(
                    context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/gate?return={returnUrl}");
                return;
            }
        }

        await _next(context);
    }
}
