// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Demo.Web.Services;

/// <summary>
/// Spend control: an in-memory per-visitor daily turn counter. The visitor key
/// is a hash of the gate cookie (all replicas of the demo run max_replicas=1,
/// so in-memory is sufficient); the cap resets at UTC midnight along with the
/// gate cookie itself.
/// </summary>
public sealed class TurnBudget
{
    /// <summary>Per-visitor daily turn cap. 50 by default (raised from 30 on
    /// 2026-09-01: the server-side per-tier token caps are the spend guard
    /// now, so this is a fairness device); DEMO_TURN_DAILY_CAP overrides.</summary>
    public int DailyCap { get; }

    public TurnBudget()
    {
        DailyCap = int.TryParse(Environment.GetEnvironmentVariable("DEMO_TURN_DAILY_CAP"), out var cap) && cap > 0
            ? cap : 50;
    }

    private readonly ConcurrentDictionary<string, (DateOnly Day, int Count)> _counts = new();

    /// <summary>Try to consume one turn. Returns false (and remaining=0) when the cap is hit.</summary>
    public bool TryConsume(string visitorKey, out int remaining)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = _counts.AddOrUpdate(visitorKey,
            _ => (today, 1),
            (_, prev) => prev.Day == today ? (today, prev.Count + 1) : (today, 1));
        if (entry.Count > DailyCap)
        {
            remaining = 0;
            return false;
        }
        remaining = DailyCap - entry.Count;
        if (_counts.Count > 10_000)
        {
            foreach (var kv in _counts)
            {
                if (kv.Value.Day != today) _counts.TryRemove(kv.Key, out _);
            }
        }
        return true;
    }

    /// <summary>Stable visitor key from the gate cookie value (or the client IP when un-gated in dev).</summary>
    public static string VisitorKey(string? gateCookie, string? fallbackIp)
    {
        var material = string.IsNullOrEmpty(gateCookie) ? $"ip:{fallbackIp ?? "unknown"}" : gateCookie;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)).AsSpan(0, 16));
    }
}
