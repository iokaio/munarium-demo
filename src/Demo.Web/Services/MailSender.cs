// SPDX-License-Identifier: Apache-2.0
using System.Text;
using System.Text.Json;

namespace Demo.Web.Services;

/// <summary>
/// Sends the demo's login-code emails through the SendGrid v3 mail API,
/// from an operator-configured sender. The API key comes from configuration
/// (DEMO_SENDGRID_API_KEY env, or SendGrid:ApiKey in appsettings — the value
/// itself is never committed to git). In Development with no key configured
/// the sender runs LOG-ONLY: the code is written to the application log
/// instead of sent, so the whole flow is testable without an account.
/// </summary>
public sealed class MailSender
{
    public string FromEmail { get; }
    private readonly string _fromName;

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly bool _logOnly;
    private readonly ILogger<MailSender> _log;

    public MailSender(HttpClient http, string? apiKey, bool logOnly, ILogger<MailSender> log, IConfiguration configuration)
    {
        FromEmail = configuration["DEMO_MAIL_FROM"] ?? configuration["SendGrid:FromEmail"] ?? "";
        _fromName = configuration["DEMO_MAIL_NAME"] ?? configuration["Demo:Name"] ?? "Munarium Demo";
        _http = http;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _logOnly = logOnly;
        _log = log;
    }

    /// <summary>True when a send can be attempted at all (a key is configured,
    /// or the sender is in Development log-only mode).</summary>
    public bool Configured => _logOnly || (_apiKey is not null && !string.IsNullOrWhiteSpace(FromEmail));

    /// <summary>Deliver a login code. Returns false on any failure — the gate
    /// treats that as "couldn't send", never as admission.</summary>
    public async Task<bool> SendLoginCodeAsync(string email, string code, CancellationToken ct)
    {
        if (_logOnly)
        {
            // Development convenience only; never enabled in Production.
            _log.LogWarning("MailSender (log-only): login code for {Email} is {Code}", email, code);
            return true;
        }
        if (_apiKey is null || string.IsNullOrWhiteSpace(FromEmail))
        {
            _log.LogError("MailSender: no SendGrid API key configured; cannot send to {Email}", email);
            return false;
        }

        var body = new
        {
            personalizations = new[] { new { to = new[] { new { email } } } },
            from = new { email = FromEmail, name = _fromName },
            subject = "Your Munarium demo login code",
            content = new[]
            {
                new
                {
                    type = "text/plain",
                    // One paragraph per line: a raw-string body wrapped to
                    // source width sends its line breaks mid-sentence.
                    value = $"""
                        Your login code for the Munarium demo is:

                            {code}

                        Enter it together with this email address to open the demo — and keep it: the same email and code let you back in on every future visit.

                        This address is used to manage your access to this demo. Contact its operator for their retention policy.

                        — {_fromName}
                        """,
                },
            },
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send");
            req.Headers.Authorization = new("Bearer", _apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true; // SendGrid answers 202
            var detail = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("MailSender: SendGrid {Status} sending to {Email}: {Detail}",
                (int)resp.StatusCode, email, detail);
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(e, "MailSender: send to {Email} failed", email);
            return false;
        }
    }
}
