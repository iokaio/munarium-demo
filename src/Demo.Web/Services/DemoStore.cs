// SPDX-License-Identifier: Apache-2.0
using Microsoft.Data.Sqlite;

namespace Demo.Web.Services;

/// <summary>
/// The demo's only persistent state (2026-09-01): a local SQLite file holding
/// the visitor registry (email → pseudonymous uid), the email/code allow-deny
/// lists, per-email caps, and the per-collection frontier counters. The email
/// itself never leaves this store — the uid the demo asserts to the munarium
/// server is an HMAC pseudonym (<see cref="GateService.UidForEmail"/>), and
/// the only page that joins pseudonym back to email is /admin.
///
/// Concurrency posture: the demo runs max_replicas=1, write rates are tiny,
/// and the file may live on an Azure Files SMB mount — so journal_mode stays
/// DELETE (no WAL: SMB byte-range locking and WAL shared memory do not mix),
/// every access serializes behind one semaphore, and a generous busy timeout
/// covers the brief two-replica overlap of an ACA revision roll.
/// </summary>
public sealed class DemoStore : IDisposable
{
    public const int DefaultFrontierCap = 2; // per collection per UTC day

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly SqliteConnection _db;

    // Blocked lookups sit on the request hot path (GateMiddleware), so they
    // are cached briefly; 30 s is the advertised block-propagation bound.
    private readonly object _cacheLock = new();
    private (DateTimeOffset At, HashSet<string> Uids, HashSet<string> Codes)? _blockedCache;
    private static readonly TimeSpan BlockedCacheTtl = TimeSpan.FromSeconds(30);

    public sealed record Visitor(
        string Email, string Uid, string FirstSeen, string LastSeen,
        bool Blocked, string? BlockedNote, int? TurnCap, int? FrontierCap,
        bool HasPasscode);

    public DemoStore(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString());
        _db.Open();
        Exec("PRAGMA journal_mode=DELETE;");
        Exec("PRAGMA busy_timeout=30000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS visitors (
                email          TEXT PRIMARY KEY,
                uid            TEXT UNIQUE NOT NULL,
                first_seen     TEXT NOT NULL,
                last_seen      TEXT NOT NULL,
                blocked        INTEGER NOT NULL DEFAULT 0,
                blocked_note   TEXT,
                turn_cap       INTEGER,
                frontier_cap   INTEGER,
                passcode_hash  TEXT,
                code_issued_at TEXT,
                send_day       TEXT,
                sends_today    INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS codes (
                slug       TEXT PRIMARY KEY,
                blocked    INTEGER NOT NULL DEFAULT 0,
                note       TEXT,
                first_seen TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS frontier_usage (
                uid    TEXT NOT NULL,
                corpus TEXT NOT NULL,
                day    TEXT NOT NULL,
                count  INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (uid, corpus, day)
            );
            """);
        // Idempotent column migration for databases created before the
        // SendGrid login-code redesign (2026-09-01 evening). SQLite has no
        // ADD COLUMN IF NOT EXISTS, so a duplicate-column error is the
        // "already migrated" signal.
        foreach (var col in new[]
        {
            "passcode_hash TEXT", "code_issued_at TEXT", "send_day TEXT",
            "sends_today INTEGER NOT NULL DEFAULT 0",
        })
        {
            try { Exec($"ALTER TABLE visitors ADD COLUMN {col};"); }
            catch (SqliteException e) when (e.SqliteErrorCode == 1) { /* duplicate column */ }
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string NowIso() => DateTimeOffset.UtcNow.ToString("u");
    private static string TodayUtc() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    /// <summary>Canonical email form: trimmed, lower-cased. The pseudonym and
    /// the primary key both derive from this, so `Jane@X` and `jane@x` are one
    /// visitor.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Upsert the visitor and return their pseudonymous uid.</summary>
    public async Task<string> RegisterVisitorAsync(string email, string uid)
    {
        var now = NowIso();
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO visitors (email, uid, first_seen, last_seen)
                VALUES ($email, $uid, $now, $now)
                ON CONFLICT(email) DO UPDATE SET last_seen = $now;
                """;
            cmd.Parameters.AddWithValue("$email", NormalizeEmail(email));
            cmd.Parameters.AddWithValue("$uid", uid);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
            return uid;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Issue (or rotate) the login passcode for an email: upsert the visitor
    /// row, store the new code's HMAC, and count the send against the per-day
    /// ceiling — one transaction under the store lock, so a burst of resend
    /// clicks cannot overshoot the cap. Returns false (nothing changed) when
    /// the email has already hit <paramref name="maxSendsPerDay"/> today.
    /// The plaintext code never enters this store.
    /// </summary>
    public async Task<bool> TryIssuePasscodeAsync(
        string email, string uid, string passcodeHash, int maxSendsPerDay)
    {
        var now = NowIso();
        var day = TodayUtc();
        await _lock.WaitAsync();
        try
        {
            using (var read = _db.CreateCommand())
            {
                read.CommandText =
                    "SELECT send_day, sends_today FROM visitors WHERE email = $email;";
                read.Parameters.AddWithValue("$email", NormalizeEmail(email));
                using var r = read.ExecuteReader();
                if (r.Read())
                {
                    var sendDay = r.IsDBNull(0) ? null : r.GetString(0);
                    var sends = r.IsDBNull(1) ? 0 : (int)r.GetInt64(1);
                    if (sendDay == day && sends >= maxSendsPerDay) return false;
                }
            }
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO visitors (email, uid, first_seen, last_seen,
                                      passcode_hash, code_issued_at, send_day, sends_today)
                VALUES ($email, $uid, $now, $now, $hash, $now, $day, 1)
                ON CONFLICT(email) DO UPDATE SET
                    passcode_hash  = $hash,
                    code_issued_at = $now,
                    sends_today    = CASE WHEN send_day = $day THEN sends_today + 1 ELSE 1 END,
                    send_day       = $day;
                """;
            cmd.Parameters.AddWithValue("$email", NormalizeEmail(email));
            cmd.Parameters.AddWithValue("$uid", uid);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$hash", passcodeHash);
            cmd.Parameters.AddWithValue("$day", day);
            cmd.ExecuteNonQuery();
            return true;
        }
        finally { _lock.Release(); }
    }

    /// <summary>The stored passcode HMAC for an email; null when the address
    /// has never been issued a code.</summary>
    public async Task<string?> PasscodeHashAsync(string email)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT passcode_hash FROM visitors WHERE email = $email;";
            cmd.Parameters.AddWithValue("$email", NormalizeEmail(email));
            return cmd.ExecuteScalar() as string;
        }
        finally { _lock.Release(); }
    }

    public async Task<List<Visitor>> AllVisitorsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<Visitor>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT email, uid, first_seen, last_seen, blocked, blocked_note, turn_cap, frontier_cap,
                       passcode_hash IS NOT NULL
                FROM visitors ORDER BY last_seen DESC;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Visitor(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetInt64(4) != 0,
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetInt32(6),
                    r.IsDBNull(7) ? null : r.GetInt32(7),
                    r.GetInt64(8) != 0));
            }
            return list;
        }
        finally { _lock.Release(); }
    }

    public async Task<Visitor?> VisitorByEmailAsync(string email)
    {
        var all = await AllVisitorsAsync();
        var norm = NormalizeEmail(email);
        return all.FirstOrDefault(v => v.Email == norm);
    }

    public async Task<bool> SetBlockedEmailAsync(string email, bool blocked, string? note)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                UPDATE visitors SET blocked = $blocked, blocked_note = $note
                WHERE email = $email;
                """;
            cmd.Parameters.AddWithValue("$blocked", blocked ? 1 : 0);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", NormalizeEmail(email));
            var changed = cmd.ExecuteNonQuery() > 0;
            InvalidateBlockedCache();
            return changed;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> SetCapsAsync(string email, int? turnCap, int? frontierCap)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                UPDATE visitors SET turn_cap = $turn, frontier_cap = $frontier
                WHERE email = $email;
                """;
            cmd.Parameters.AddWithValue("$turn", (object?)turnCap ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$frontier", (object?)frontierCap ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$email", NormalizeEmail(email));
            return cmd.ExecuteNonQuery() > 0;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> SetBlockedCodeAsync(string slug, bool blocked, string? note)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO codes (slug, blocked, note, first_seen)
                VALUES ($slug, $blocked, $note, $now)
                ON CONFLICT(slug) DO UPDATE SET blocked = $blocked, note = $note;
                """;
            cmd.Parameters.AddWithValue("$slug", slug);
            cmd.Parameters.AddWithValue("$blocked", blocked ? 1 : 0);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", NowIso());
            cmd.ExecuteNonQuery();
            InvalidateBlockedCache();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<List<(string Slug, bool Blocked, string? Note)>> AllCodesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<(string, bool, string?)>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT slug, blocked, note FROM codes ORDER BY first_seen DESC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add((r.GetString(0), r.GetInt64(1) != 0, r.IsDBNull(2) ? null : r.GetString(2)));
            }
            return list;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Hot-path checks, 30 s cached: is this uid's email, or this
    /// code slug, blocked?</summary>
    public async Task<bool> IsBlockedUidAsync(string uid) => (await BlockedAsync()).Uids.Contains(uid);

    public async Task<bool> IsBlockedCodeAsync(string slug) => (await BlockedAsync()).Codes.Contains(slug);

    public async Task<bool> IsBlockedEmailAsync(string email)
    {
        var v = await VisitorByEmailAsync(email);
        return v?.Blocked == true;
    }

    private void InvalidateBlockedCache()
    {
        lock (_cacheLock) { _blockedCache = null; }
    }

    private async Task<(HashSet<string> Uids, HashSet<string> Codes)> BlockedAsync()
    {
        lock (_cacheLock)
        {
            if (_blockedCache is { } c && DateTimeOffset.UtcNow - c.At < BlockedCacheTtl)
                return (c.Uids, c.Codes);
        }
        await _lock.WaitAsync();
        HashSet<string> uids = new();
        HashSet<string> codes = new();
        try
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT uid FROM visitors WHERE blocked = 1;";
                using var r = cmd.ExecuteReader();
                while (r.Read()) uids.Add(r.GetString(0));
            }
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT slug FROM codes WHERE blocked = 1;";
                using var r = cmd.ExecuteReader();
                while (r.Read()) codes.Add(r.GetString(0));
            }
        }
        finally { _lock.Release(); }
        lock (_cacheLock) { _blockedCache = (DateTimeOffset.UtcNow, uids, codes); }
        return (uids, codes);
    }

    /// <summary>The per-collection frontier cap override for a uid, if its
    /// visitor row carries one; null = the default cap.</summary>
    public async Task<int?> FrontierCapAsync(string uid)
    {
        await _lock.WaitAsync();
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT frontier_cap FROM visitors WHERE uid = $uid;";
            cmd.Parameters.AddWithValue("$uid", uid);
            var v = cmd.ExecuteScalar();
            return v is long n ? (int)n : null;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Consume one frontier request for (uid, corpus) against today's
    /// per-collection cap. Deliberately no refund path: a failed frontier turn
    /// spent one of the visitor's slots, which is acceptable and simpler than
    /// settle-or-release at this scale.</summary>
    public async Task<(bool Ok, int Remaining)> TryConsumeFrontierAsync(string uid, string corpus, int? capOverride)
    {
        var cap = capOverride ?? DefaultFrontierCap;
        var day = TodayUtc();
        await _lock.WaitAsync();
        try
        {
            using (var read = _db.CreateCommand())
            {
                read.CommandText = "SELECT count FROM frontier_usage WHERE uid=$uid AND corpus=$corpus AND day=$day;";
                read.Parameters.AddWithValue("$uid", uid);
                read.Parameters.AddWithValue("$corpus", corpus);
                read.Parameters.AddWithValue("$day", day);
                var current = read.ExecuteScalar() is long n ? (int)n : 0;
                if (current >= cap) return (false, 0);
            }
            using (var write = _db.CreateCommand())
            {
                write.CommandText = """
                    INSERT INTO frontier_usage (uid, corpus, day, count) VALUES ($uid, $corpus, $day, 1)
                    ON CONFLICT(uid, corpus, day) DO UPDATE SET count = count + 1
                    RETURNING count;
                    """;
                write.Parameters.AddWithValue("$uid", uid);
                write.Parameters.AddWithValue("$corpus", corpus);
                write.Parameters.AddWithValue("$day", day);
                var count = Convert.ToInt32(write.ExecuteScalar());
                return (true, Math.Max(0, cap - count));
            }
        }
        finally { _lock.Release(); }
    }

    /// <summary>Today's frontier usage per (uid, corpus) — the admin table's
    /// "frontier today" column.</summary>
    public async Task<Dictionary<string, int>> FrontierTodayByUidAsync()
    {
        var day = TodayUtc();
        await _lock.WaitAsync();
        try
        {
            var totals = new Dictionary<string, int>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT uid, SUM(count) FROM frontier_usage WHERE day=$day GROUP BY uid;";
            cmd.Parameters.AddWithValue("$day", day);
            using var r = cmd.ExecuteReader();
            while (r.Read()) totals[r.GetString(0)] = (int)r.GetInt64(1);
            return totals;
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _db.Dispose();
        _lock.Dispose();
    }
}
