// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Demo.Web.Services;

/// <summary>
/// The demo gate. Since 2026-09-01 (evening) a visitor's credential is the
/// pair (email, emailed login code): any email is accepted, the first visit
/// sends a generated code via SendGrid from info@ioka.io, and the same
/// email+code pair re-enters the demo on every later visit. On success the
/// gate issues a stateless HMAC cookie (v3) carrying the visitor's
/// PSEUDONYMOUS uid (em-…, derived from the email — the email itself never
/// leaves the demo's own store), valid until the next UTC midnight — no
/// server-side session state, survives restarts.
/// </summary>
public sealed class GateService
{
    public const string CookieName = "demo_gate";
    /// <summary>The /admin session cookie (2026-09-01): issued after a
    /// username/password login, scoped to /admin, 30 minutes.</summary>
    public const string AdminCookieName = "demo_admin";
    private const int MaxFailuresPerIpPerDay = 20;
    private static readonly TimeSpan AdminSessionLength = TimeSpan.FromMinutes(30);

    private readonly byte[] _cookieKey;
    private readonly byte[] _emailUidKey;
    private readonly byte[] _adminSessionKey;
    private readonly byte[] _passcodeKey;
    private readonly ConcurrentDictionary<string, (DateOnly Day, int Count)> _failures = new();

    // Revocation state (2026-09-01). Volatile and in-memory, like TurnBudget,
    // and for the same reason: the demo runs max_replicas=1. A restart clears
    // both — the honest bound is that a "revoked" cookie can come back only
    // until its own midnight-UTC expiry, and the /gatekeeper page says so.
    private long _revokedBeforeUnix; // cookies ISSUED before this instant are invalid
    private long _maxAgeSeconds;     // 0 = disabled (midnight expiry only)

    public bool Disabled { get; }

    public GateService(string secret, bool disabled = false, TimeSpan? initialMaxAge = null)
    {
        _cookieKey = Encoding.UTF8.GetBytes(secret + "|cookie");
        _emailUidKey = Encoding.UTF8.GetBytes(secret + "|email-uid");
        _adminSessionKey = Encoding.UTF8.GetBytes(secret + "|admin-session");
        _passcodeKey = Encoding.UTF8.GetBytes(secret + "|passcode");
        Disabled = disabled;
        if (initialMaxAge is { } age && age > TimeSpan.Zero)
        {
            _maxAgeSeconds = (long)age.TotalSeconds;
        }
    }

    // ---- login codes (2026-09-01 evening) ---------------------------------
    // The shape-rule access code retired; a visitor's standing credential is
    // now the pair (email, emailed login code). Codes are generated here,
    // delivered by SendGrid from info@ioka.io, and REUSED across visits; the
    // store keeps only an HMAC of the code under a secret-derived key, so a
    // copied demo.sqlite yields no working credentials.

    private const string PasscodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no I/L/O/0/1
    public const int PasscodeLength = 8;

    /// <summary>A fresh login code, displayed as XXXX-XXXX.</summary>
    public static string GeneratePasscode()
    {
        var chars = new char[PasscodeLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = PasscodeAlphabet[RandomNumberGenerator.GetInt32(PasscodeAlphabet.Length)];
        }
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    /// <summary>Canonical form of a submitted login code — uppercased, with
    /// separators and whitespace dropped, so "abcd-2345" and "ABCD 2345" are
    /// one code. Null when the input cannot be a code.</summary>
    public static string? NormalizePasscode(string? submitted)
    {
        if (string.IsNullOrWhiteSpace(submitted)) return null;
        var chars = submitted.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray();
        return chars.Length == PasscodeLength ? new string(chars) : null;
    }

    /// <summary>The HMAC a login code is stored as, bound to the email it was
    /// issued for so a code cannot be replayed against another address. The
    /// code is normalized HERE (not by the caller) so the display form
    /// ("XXXX-XXXX") and any user rendering of it hash identically — the
    /// first local run proved the caller-normalizes contract is a trap.</summary>
    public string HashPasscode(string email, string code)
    {
        var normalized = NormalizePasscode(code)
                         ?? throw new ArgumentException("not a login code", nameof(code));
        var mac = HMACSHA256.HashData(_passcodeKey,
            Encoding.UTF8.GetBytes(DemoStore.NormalizeEmail(email) + "|" + normalized));
        return Convert.ToHexString(mac);
    }

    /// <summary>Constant-time check of a submitted code against the stored HMAC.</summary>
    public bool VerifyPasscode(string email, string? submitted, string? storedHash)
    {
        if (storedHash is null || NormalizePasscode(submitted) is not { } code) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(storedHash); }
        catch (FormatException) { return false; }
        var actual = Convert.FromHexString(HashPasscode(email, code));
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>The pseudonymous uid a registered email maps to (2026-09-01):
    /// an HMAC over the normalized address under a secret-derived key, so the
    /// munarium server, its audit trail and the visitor-gated console proxy
    /// only ever see `em-&lt;16 hex&gt;` — the email itself lives solely in the
    /// demo's own store, joined back on /admin.</summary>
    public string UidForEmail(string email)
    {
        var mac = HMACSHA256.HashData(
            _emailUidKey, Encoding.UTF8.GetBytes(DemoStore.NormalizeEmail(email)));
        return "em-" + Convert.ToHexString(mac.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>
    /// The slug an access code is recorded (and deny-listed) under in the
    /// demo store. Lower-cased, with runs of anything but letters and digits
    /// folded to one hyphen: "Demo-2026!" and "demo 2026!" are one slug.
    /// Since 2026-09-01 the slug is bookkeeping only — the uid asserted to
    /// the server derives from the EMAIL (<see cref="UidForEmail"/>).
    /// </summary>
    public static string SlugForCode(string code)
    {
        var slug = new StringBuilder();
        var pendingHyphen = false;
        foreach (var c in code.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && slug.Length > 0) slug.Append('-');
                pendingHyphen = false;
                slug.Append(c);
            }
            else
            {
                pendingHyphen = true;
            }
        }
        return slug.ToString();
    }

    /// <summary>
    /// Issue the stateless gate cookie for an admitted code, valid until the
    /// next UTC midnight.
    ///
    /// The cookie carries a per-visitor random nonce as well as the expiry
    /// and the code. The nonce keeps two browsers admitted with the same
    /// code distinguishable for the per-visitor turn budget; the code is
    /// what usage is attributed to.
    /// </summary>
    public (string Value, DateTimeOffset Expires) IssueCookie(string uid)
    {
        // Format v3 (2026-09-01): "{expiryUnix}.{issuedUnix}.{nonce}.{uidHex}.{hmac}".
        // The fourth segment carries the visitor's PSEUDONYMOUS uid (em-…),
        // never the email and no longer the access code. The issue time is
        // what age-based and revoke-all invalidation check against. Earlier
        // formats are simply invalid — each format change is a one-time
        // global revocation, and midnight-UTC expiry makes that near-free.
        var now = DateTimeOffset.UtcNow;
        var expires = new DateTimeOffset(now.Date.AddDays(1), TimeSpan.Zero); // next UTC midnight
        var unix = expires.ToUnixTimeSeconds().ToString();
        var issued = now.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var uidHex = Convert.ToHexString(Encoding.UTF8.GetBytes(uid));
        var payload = $"{unix}.{issued}.{nonce}.{uidHex}";
        var mac = HMACSHA256.HashData(_cookieKey, Encoding.UTF8.GetBytes(payload));
        return ($"{payload}.{Convert.ToHexString(mac)}", expires);
    }

    /// <summary>Validate a gate cookie: unexpired and correctly signed (constant-time).</summary>
    public bool ValidateCookie(string? value) => UidFromCookie(value) is not null;

    /// <summary>
    /// The pseudonymous visitor uid (`em-…`) carried by a valid (unexpired,
    /// correctly signed) gate cookie; null for anything else, including every
    /// earlier cookie format (v1's four parts, v2's code-bearing five).
    /// </summary>
    public string? UidFromCookie(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var parts = value.Split('.');
        if (parts.Length != 5) return null;
        var (unixPart, issuedPart, noncePart, uidPart, macPart) =
            (parts[0], parts[1], parts[2], parts[3], parts[4]);
        if (unixPart.Length == 0 || issuedPart.Length == 0 || noncePart.Length == 0 ||
            uidPart.Length == 0 || macPart.Length == 0)
        {
            return null;
        }
        if (!long.TryParse(unixPart, out var unix)) return null;
        if (!long.TryParse(issuedPart, out var issued)) return null;
        var now = DateTimeOffset.UtcNow;
        if (DateTimeOffset.FromUnixTimeSeconds(unix) <= now) return null;
        // A cookie "issued" in the future is forged or clock-skewed — either
        // way it must not outlive the age checks it would sidestep.
        if (issued > now.ToUnixTimeSeconds() + 300) return null;
        // Revoke-all: anything issued before the cutoff is dead.
        if (issued < Interlocked.Read(ref _revokedBeforeUnix)) return null;
        // Age ceiling: older than the configured maximum is dead, even
        // though its midnight expiry has not arrived.
        var maxAge = Interlocked.Read(ref _maxAgeSeconds);
        if (maxAge > 0 && now.ToUnixTimeSeconds() - issued > maxAge) return null;
        byte[] submittedMac;
        byte[] uidBytes;
        try
        {
            submittedMac = Convert.FromHexString(macPart);
            uidBytes = Convert.FromHexString(uidPart);
        }
        catch (FormatException) { return null; }
        var expectedMac = HMACSHA256.HashData(
            _cookieKey, Encoding.UTF8.GetBytes($"{unixPart}.{issuedPart}.{noncePart}.{uidPart}"));
        if (submittedMac.Length != expectedMac.Length ||
            !CryptographicOperations.FixedTimeEquals(submittedMac, expectedMac))
        {
            return null;
        }
        var uid = Encoding.UTF8.GetString(uidBytes);
        // Only the shape this service itself issues: v2 code-bearing cookies
        // decoded here would yield an arbitrary string, and the format bump
        // is supposed to invalidate them, not repurpose them.
        return uid.StartsWith("em-", StringComparison.Ordinal) && uid.Length <= 64 &&
               !uid.Any(char.IsControl)
            ? uid
            : null;
    }

    // ---- /admin session (username/password login, 2026-09-01) -------------

    /// <summary>Issue the 30-minute admin session cookie. It carries no
    /// credential material — login already decided — just expiry + nonce
    /// under a dedicated HMAC key, so it can never validate as a gate cookie
    /// (and vice versa).</summary>
    public (string Value, DateTimeOffset Expires) IssueAdminSession()
    {
        var expires = DateTimeOffset.UtcNow + AdminSessionLength;
        var unix = expires.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var payload = $"{unix}.{nonce}";
        var mac = HMACSHA256.HashData(_adminSessionKey, Encoding.UTF8.GetBytes(payload));
        return ($"{payload}.{Convert.ToHexString(mac)}", expires);
    }

    public bool ValidateAdminSession(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var parts = value.Split('.');
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[0], out var unix)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(unix) <= DateTimeOffset.UtcNow) return false;
        byte[] submittedMac;
        try { submittedMac = Convert.FromHexString(parts[2]); }
        catch (FormatException) { return false; }
        var expectedMac = HMACSHA256.HashData(
            _adminSessionKey, Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"));
        return submittedMac.Length == expectedMac.Length &&
               CryptographicOperations.FixedTimeEquals(submittedMac, expectedMac);
    }

    // ---- revocation controls (2026-09-01) ---------------------------------

    /// <summary>Kill switch: every visitor cookie issued before this instant
    /// stops validating. New admissions are unaffected.</summary>
    public void RevokeAllIssuedBefore(DateTimeOffset cutoff) =>
        Interlocked.Exchange(ref _revokedBeforeUnix, cutoff.ToUnixTimeSeconds());

    /// <summary>The active revoke-all cutoff, if one has been set since the
    /// process started.</summary>
    public DateTimeOffset? RevokedBefore
    {
        get
        {
            var v = Interlocked.Read(ref _revokedBeforeUnix);
            return v > 0 ? DateTimeOffset.FromUnixTimeSeconds(v) : null;
        }
    }

    /// <summary>Set (or clear, with null) the maximum cookie age. Enforced at
    /// validation, so it applies to already-issued cookies immediately.</summary>
    public void SetMaxAge(TimeSpan? age) =>
        Interlocked.Exchange(ref _maxAgeSeconds,
            age is { } a && a > TimeSpan.Zero ? (long)a.TotalSeconds : 0);

    public TimeSpan? MaxAge
    {
        get
        {
            var v = Interlocked.Read(ref _maxAgeSeconds);
            return v > 0 ? TimeSpan.FromSeconds(v) : null;
        }
    }

    /// <summary>Per-IP failure cap: true while the IP is still under today's limit.</summary>
    public bool IpAllowed(string ip)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return !_failures.TryGetValue(ip, out var entry) || entry.Day != today || entry.Count < MaxFailuresPerIpPerDay;
    }

    public void RecordFailure(string ip)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _failures.AddOrUpdate(ip,
            _ => (today, 1),
            (_, prev) => prev.Day == today ? (today, prev.Count + 1) : (today, 1));
        // Opportunistic cleanup of stale days so the map cannot grow unbounded.
        if (_failures.Count > 10_000)
        {
            foreach (var kv in _failures)
            {
                if (kv.Value.Day != today) _failures.TryRemove(kv.Key, out _);
            }
        }
    }
}
