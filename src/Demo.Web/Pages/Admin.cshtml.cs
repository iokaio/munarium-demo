// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using Demo.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Web.Pages;

/// <summary>
/// The unlinked admin page at /admin (username/password since 2026-09-01,
/// replacing the gate-code scheme). Login issues a 30-minute HMAC-signed
/// session cookie scoped to /admin; every action revalidates it plus the
/// Razor antiforgery token. Credentials come from env/secret only
/// (DEMO_ADMIN_USER / DEMO_ADMIN_PASSWORD) — unset means the login is
/// disabled, fail closed.
///
/// This is the ONLY page that renders visitor emails: the identity panel
/// joins the server's pseudonymous per-uid usage report against the demo's
/// own store, and every email row carries inline block/unblock plus cap
/// controls. /stats stays aggregate-only.
/// </summary>
public class AdminModel : PageModel
{
    private readonly GateService _gate;
    private readonly AdminCredentials _creds;
    private readonly DemoStore _store;
    private readonly MunariumClient _munarium;
    private readonly BatteryIdentity _battery;

    public AdminModel(
        GateService gate, AdminCredentials creds, DemoStore store, MunariumClient munarium,
        BatteryIdentity battery)
    {
        _gate = gate;
        _creds = creds;
        _store = store;
        _munarium = munarium;
        _battery = battery;
    }

    // Login form.
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }

    // Action forms.
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public string? Slug { get; set; }
    [BindProperty] public int? TurnCap { get; set; }
    [BindProperty] public int? FrontierCap { get; set; }
    [BindProperty] public double? MaxAgeHours { get; set; }

    public bool Authed { get; private set; }
    public bool LoginConfigured => _creds.Configured;
    public string? Error { get; private set; }
    public string? Notice { get; private set; }

    public DateTimeOffset? RevokedBefore => _gate.RevokedBefore;
    public TimeSpan? MaxAge => _gate.MaxAge;

    /// <summary>The address the chip batteries sign in with; its row is
    /// badged, because its usage is scripted traffic rather than a visitor's.</summary>
    public string BatteryEmail => _battery.Email;

    /// <summary>A just-issued login code, rendered exactly once (the store
    /// keeps only its hash).</summary>
    public string? IssuedCode { get; private set; }
    public string? IssuedCodeEmail { get; private set; }

    /// <summary>One identity row: the visitor joined with server-side usage.
    /// <paramref name="Battery"/> marks the chip batteries' sign-in address —
    /// the one row whose numbers are a script's, not a person's.</summary>
    public sealed record IdentityRow(
        DemoStore.Visitor Visitor,
        long Interactions, long Turns, long TokensIn, long TokensOut,
        int FrontierToday, bool Battery);

    public List<IdentityRow> Identities { get; } = [];
    public List<MunariumClient.UsageRow> Unattributed { get; } = [];
    public List<(string Slug, bool Blocked, string? Note)> Codes { get; private set; } = [];
    public string? UsageError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Authed = _gate.ValidateAdminSession(Request.Cookies[GateService.AdminCookieName]);
        if (Authed) await LoadPanelAsync(ct);
    }

    public async Task<IActionResult> OnPostLoginAsync()
    {
        // Same brute-force posture as the visitor gate: fixed delay, per-IP
        // failure cap, one generic refusal.
        await Task.Delay(700);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_gate.IpAllowed(ip))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                "Too many failed attempts from this address today. Try again after midnight UTC.");
        }
        if (!_creds.Configured || !FixedEquals(Username, _creds.User) || !FixedEquals(Password, _creds.Password))
        {
            _gate.RecordFailure(ip);
            Error = "Those credentials are not valid.";
            return Page();
        }
        var (value, expires) = _gate.IssueAdminSession();
        Response.Cookies.Append(GateService.AdminCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            // Request.IsHttps, not a constant: ACA terminates TLS at ingress
            // and forwards X-Forwarded-Proto (mapped by UseForwardedHeaders),
            // so production cookies stay Secure while plain-HTTP local dev
            // still round-trips them.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/admin",
        });
        Response.Cookies.Append(GateService.AdminCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/matrix-admin",
        });
        // Literal lowercase, never RedirectToPage(): the canonical "/Admin"
        // does not path-match the /admin-scoped cookie (RFC 6265 paths are
        // case-sensitive), which read as a silently failed login.
        return Redirect("/admin");
    }

    public IActionResult OnPostLogout()
    {
        Response.Cookies.Delete(GateService.AdminCookieName, new CookieOptions { Path = "/admin" });
        Response.Cookies.Delete(GateService.AdminCookieName, new CookieOptions { Path = "/matrix-admin" });
        return Redirect("/admin");
    }

    public Task<IActionResult> OnPostBlockEmailAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        var ok = Email is not null && await _store.SetBlockedEmailAsync(Email, true, Note);
        Notice = ok
            ? $"Blocked. The visitor's next request is refused within 30 seconds."
            : "No registered visitor with that email.";
    }, ct);

    public Task<IActionResult> OnPostUnblockEmailAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        var ok = Email is not null && await _store.SetBlockedEmailAsync(Email, false, null);
        Notice = ok ? "Unblocked." : "No registered visitor with that email.";
    }, ct);

    public Task<IActionResult> OnPostSetCapsAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        var ok = Email is not null && await _store.SetCapsAsync(Email, TurnCap, FrontierCap);
        Notice = ok
            ? $"Caps updated: turns/day {(TurnCap?.ToString() ?? "default")}, frontier/collection/day {(FrontierCap?.ToString() ?? "default")}."
            : "No registered visitor with that email.";
    }, ct);

    /// <summary>Issue (or rotate) a login code for an email WITHOUT sending
    /// mail — shown once here, for handing out directly. This is also how the
    /// verification address used by the chip batteries gets a known code, and
    /// it bypasses the visitor gate's per-day send cap (an admin action, not
    /// a send).</summary>
    public Task<IActionResult> OnPostIssueCodeAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        if (EmailValidator.IsPlausibleFormat(Email))
        {
            var uid = _gate.UidForEmail(Email!);
            var code = GateService.GeneratePasscode();
            await _store.TryIssuePasscodeAsync(
                Email!, uid, _gate.HashPasscode(Email!, code), int.MaxValue);
            IssuedCode = code;
            IssuedCodeEmail = DemoStore.NormalizeEmail(Email!);
            Notice = "Login code issued (any earlier code for this address no longer works).";
        }
        else
        {
            Error = "Enter a valid email address to issue a code for.";
        }
    }, ct);

    public Task<IActionResult> OnPostBlockCodeAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        if (!string.IsNullOrWhiteSpace(Slug))
        {
            await _store.SetBlockedCodeAsync(GateService.SlugForCode(Slug), true, Note);
            Notice = "Code blocked: admissions with it are refused.";
        }
    }, ct);

    public Task<IActionResult> OnPostUnblockCodeAsync(CancellationToken ct) => ActionAsync(async () =>
    {
        if (!string.IsNullOrWhiteSpace(Slug))
        {
            await _store.SetBlockedCodeAsync(GateService.SlugForCode(Slug), false, null);
            Notice = "Code unblocked.";
        }
    }, ct);

    public Task<IActionResult> OnPostRevokeAllAsync(CancellationToken ct) => ActionAsync(() =>
    {
        _gate.RevokeAllIssuedBefore(DateTimeOffset.UtcNow);
        Notice = "Every outstanding visitor cookie is now invalid. Registered visitors re-enter at the gate.";
        return Task.CompletedTask;
    }, ct);

    public Task<IActionResult> OnPostMaxAgeAsync(CancellationToken ct) => ActionAsync(() =>
    {
        _gate.SetMaxAge(MaxAgeHours is > 0 ? TimeSpan.FromHours(MaxAgeHours.Value) : null);
        Notice = _gate.MaxAge is { } age
            ? $"Visitor cookies now expire {age.TotalHours:0.#} hour(s) after issue (and still at midnight UTC)."
            : "Age ceiling cleared — visitor cookies last until midnight UTC.";
        return Task.CompletedTask;
    }, ct);

    /// <summary>Every action revalidates the session cookie; an expired
    /// session degrades to the login form with a notice, never a silent
    /// no-op.</summary>
    private async Task<IActionResult> ActionAsync(Func<Task> action, CancellationToken ct)
    {
        Authed = _gate.ValidateAdminSession(Request.Cookies[GateService.AdminCookieName]);
        if (!Authed)
        {
            Error = "Session expired — sign in again.";
            return Page();
        }
        await action();
        await LoadPanelAsync(ct);
        return Page();
    }

    private async Task LoadPanelAsync(CancellationToken ct)
    {
        var visitors = await _store.AllVisitorsAsync();
        var frontierToday = await _store.FrontierTodayByUidAsync();
        Codes = await _store.AllCodesAsync();

        Dictionary<string, MunariumClient.UsageRow> usageByUid = new();
        try
        {
            var usage = await _munarium.GetUsageAsync("uid", ct);
            foreach (var row in usage.Rows) usageByUid[row.Key] = row;
        }
        catch (Exception ex) when (ex is MunariumApiException or MunariumUnreachableException)
        {
            UsageError = "Server usage report unavailable: " + ex.Message;
        }

        var known = new HashSet<string>();
        foreach (var v in visitors)
        {
            known.Add(v.Uid);
            usageByUid.TryGetValue(v.Uid, out var u);
            Identities.Add(new IdentityRow(
                v,
                u?.Interactions ?? 0, u?.Turns ?? 0,
                u?.CompletionInputTokens ?? 0, u?.CompletionOutputTokens ?? 0,
                frontierToday.GetValueOrDefault(v.Uid),
                _battery.IsBattery(v.Email)));
        }
        Identities.Sort((a, b) => b.Interactions.CompareTo(a.Interactions));
        Unattributed.AddRange(usageByUid.Values
            .Where(r => !known.Contains(r.Key))
            .OrderByDescending(r => r.Interactions));
    }

    private static bool FixedEquals(string? submitted, string? expected)
    {
        // Hash both sides so length differences do not leak through the
        // comparison, then compare constant-time.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(submitted ?? ""));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected ?? ""));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
