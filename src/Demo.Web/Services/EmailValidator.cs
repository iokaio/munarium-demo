// SPDX-License-Identifier: Apache-2.0
using System.Net.Mail;
using DnsClient;

namespace Demo.Web.Services;

/// <summary>
/// Gate email validation (2026-09-01): strict-enough format checks plus a
/// live DNS lookup that the domain can receive mail (MX, with the RFC 5321
/// implicit-MX fallback to an A/AAAA record). This proves deliverability
/// plausibility, not ownership — verification links were deliberately
/// deferred. Lookups are capped at three seconds and a DNS outage fails
/// OPEN on the network step (format still enforced): a resolver hiccup must
/// not lock the gate.
/// </summary>
public static class EmailValidator
{
    private static readonly LookupClient Dns = new(new LookupClientOptions
    {
        Timeout = TimeSpan.FromSeconds(3),
        Retries = 1,
        UseCache = true,
    });

    public static bool IsPlausibleFormat(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var value = email.Trim();
        if (value.Length is < 6 or > 254) return false;
        if (value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return false;
        // MailAddress accepts a lot ("a@b" with display names etc.) — pin the
        // shape down: exactly one '@', a dot in the domain, no leading/trailing
        // dots, and the parse must round-trip the address unchanged.
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1) return false;
        var domain = value[(at + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.') ||
            domain.Contains("..") || domain.Length > 253)
        {
            return false;
        }
        try
        {
            var parsed = new MailAddress(value);
            return parsed.Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // The generic-provider (company-email-only) rule was REMOVED 2026-09-01
    // evening: with SendGrid-delivered login codes, any address that can
    // receive mail is acceptable — the emailed code is the proof of access.

    /// <summary>Format + MX (or implicit-MX A/AAAA fallback). Returns false
    /// only when the format fails or DNS AUTHORITATIVELY answers that the
    /// domain cannot receive mail; resolver errors fail open.</summary>
    public static async Task<bool> IsDeliverableAsync(string? email, CancellationToken ct = default)
    {
        if (!IsPlausibleFormat(email)) return false;
        var domain = email!.Trim()[(email.Trim().IndexOf('@') + 1)..];
        try
        {
            var mx = await Dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
            if (mx.Answers.MxRecords().Any()) return true;
            var a = await Dns.QueryAsync(domain, QueryType.A, cancellationToken: ct);
            if (a.Answers.ARecords().Any()) return true;
            var aaaa = await Dns.QueryAsync(domain, QueryType.AAAA, cancellationToken: ct);
            return aaaa.Answers.AaaaRecords().Any();
        }
        catch (DnsResponseException)
        {
            // Resolver trouble, not a verdict about the domain.
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
