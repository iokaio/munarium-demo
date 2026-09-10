// SPDX-License-Identifier: Apache-2.0
using Demo.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Web.Pages;

public class GateModel : PageModel
{
    /// <summary>Login-code emails per address per UTC day. Generous for a
    /// human ("didn't arrive" retries), tight enough that the gate cannot be
    /// used to spray a mailbox.</summary>
    private const int MaxSendsPerDay = 5;

    private readonly GateService _gate;
    private readonly DemoStore _store;
    private readonly MailSender _mail;

    public GateModel(GateService gate, DemoStore store, MailSender mail)
    {
        _gate = gate;
        _store = store;
        _mail = mail;
    }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Code { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public string? Error { get; set; }
    public string? Notice { get; set; }

    public IActionResult OnGet(string? @return)
    {
        // Already admitted (or gate disabled in dev) — go straight in.
        if (_gate.Disabled || _gate.ValidateCookie(Request.Cookies[GateService.CookieName]))
        {
            return LocalRedirect(SafeReturn(@return));
        }
        ReturnUrl = @return;
        return Page();
    }

    /// <summary>The Enter button: with a code, log in; with a blank code,
    /// either send a first code (new address) or point at the one already
    /// issued (existing address — deliberately NOT rotated, so forgetting to
    /// type the code does not invalidate it).</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // Fixed delay: slows brute-forcing and masks timing differences in
        // validation (DNS and SendGrid latency run inside it).
        var delay = Task.Delay(700);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_gate.IpAllowed(ip))
        {
            await delay;
            return StatusCode(StatusCodes.Status429TooManyRequests,
                "Too many failed attempts from this address today. Try again after midnight UTC.");
        }

        if (!EmailValidator.IsPlausibleFormat(Email))
        {
            await delay;
            Error = "Enter a valid email address — your login code is sent there " +
                    "and used together with it.";
            return Page();
        }

        if (GateService.NormalizePasscode(Code) is null)
        {
            if (await _store.PasscodeHashAsync(Email!) is not null)
            {
                await delay;
                Notice = "This address already has a login code — enter the code we " +
                         "emailed you, or use “Email me a new one” below.";
                return Page();
            }
            // First visit: the blank code means "send me one".
            return await SendNewCodeAsync(delay);
        }

        // Login: verify the (email, code) pair. A blocked email must read
        // exactly like a wrong code — no oracle.
        var storedHash = await _store.PasscodeHashAsync(Email!);
        var codeOk = _gate.VerifyPasscode(Email!, Code, storedHash);
        var blocked = await _store.IsBlockedEmailAsync(Email!);
        await delay;
        if (!codeOk || blocked)
        {
            _gate.RecordFailure(ip);
            Error = "That email and login code combination is not valid.";
            return Page();
        }

        var uid = _gate.UidForEmail(Email!);
        await _store.RegisterVisitorAsync(Email!, uid);

        var (value, expires) = _gate.IssueCookie(uid);
        Response.Cookies.Append(GateService.CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            // Request.IsHttps, not a constant: ACA terminates TLS at ingress
            // and forwards X-Forwarded-Proto (mapped by UseForwardedHeaders),
            // so production cookies stay Secure while plain-HTTP local dev
            // still round-trips them.
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = expires,
            Path = "/",
        });
        return LocalRedirect(SafeReturn(ReturnUrl));
    }

    /// <summary>The explicit "email me a new one" button: always rotates —
    /// the previous code stops working the moment a new one is issued.</summary>
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        var delay = Task.Delay(700);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_gate.IpAllowed(ip))
        {
            await delay;
            return StatusCode(StatusCodes.Status429TooManyRequests,
                "Too many failed attempts from this address today. Try again after midnight UTC.");
        }

        if (!EmailValidator.IsPlausibleFormat(Email))
        {
            await delay;
            Error = "Enter a valid email address — your login code is sent there " +
                    "and used together with it.";
            return Page();
        }

        return await SendNewCodeAsync(delay);
    }

    private async Task<IActionResult> SendNewCodeAsync(Task delay)
    {
        var sentNotice = $"We've emailed a login code to {Email} from {_mail.FromEmail}. " +
                         "Enter it above once it arrives — the same email and code get " +
                         "you in on every future visit.";

        // A blocked address gets the success message and no email: telling it
        // apart from a delivered code is not something this page hands out.
        if (await _store.IsBlockedEmailAsync(Email!))
        {
            await delay;
            Notice = sentNotice;
            return Page();
        }

        if (!await EmailValidator.IsDeliverableAsync(Email, HttpContext.RequestAborted))
        {
            await delay;
            Error = "We couldn't find a mail server for that address — check it and try again.";
            return Page();
        }

        var uid = _gate.UidForEmail(Email!);
        var code = GateService.GeneratePasscode();
        var issued = await _store.TryIssuePasscodeAsync(
            Email!, uid, _gate.HashPasscode(Email!, code), MaxSendsPerDay);
        if (!issued)
        {
            await delay;
            Error = "That address has requested several codes today — use the most " +
                    "recent one we sent, or try again after midnight UTC.";
            return Page();
        }

        var sent = await _mail.SendLoginCodeAsync(
            DemoStore.NormalizeEmail(Email!), code, HttpContext.RequestAborted);
        await delay;
        if (!sent)
        {
            Error = "We couldn't send the code just now — please try again in a moment.";
            return Page();
        }
        Notice = sentNotice;
        return Page();
    }

    private static string SafeReturn(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") ? url : "/";
}
