// SPDX-License-Identifier: Apache-2.0
using System.Collections.Concurrent;

namespace Demo.Web.Services;

/// <summary>
/// Caches capability JWTs keyed by (runbook, access_level, compartments, uid)
/// and refreshes each token 5 minutes before its expiry, so the BFF mints at
/// most one token per visitor per clearance combination per ~55 minutes. The
/// uid dimension is per-visitor attribution (2026-08-23): keys are bounded by
/// distinct gate cookies per day, and stale-day entries age out with the
/// tokens themselves.
/// </summary>
public sealed class TokenCache
{
    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // Resolved per mint, never captured: MunariumClient is a typed HttpClient, so
    // holding one in this singleton would pin a single HttpMessageHandler for
    // the life of the process and defeat the factory's handler rotation — the
    // classic way a long-lived app stops noticing that a backend's address
    // changed (which is exactly what happens when the munarium app rolls).
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly SemaphoreSlim _mintLock = new(1, 1);

    public TokenCache(IServiceProvider services) => _services = services;

    public async Task<string> GetTokenAsync(
        string runbook, int accessLevel, string[] compartments, string? uid = null,
        CancellationToken ct = default)
    {
        var key = $"{runbook}|{accessLevel}|{string.Join(",", compartments)}|{uid}";
        if (_cache.TryGetValue(key, out var hit) &&
            hit.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
        {
            return hit.Token;
        }

        await _mintLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock.
            if (_cache.TryGetValue(key, out hit) &&
                hit.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
            {
                return hit.Token;
            }
            var client = _services.GetRequiredService<MunariumClient>();
            var minted = await client.MintTokenAsync(runbook, accessLevel, compartments, uid, ct);
            var expires = DateTimeOffset.TryParse(minted.ExpiresAt, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.AddMinutes(55);
            _cache[key] = new Entry(minted.Token, expires);
            if (_cache.Count > 5_000)
            {
                foreach (var kv in _cache)
                {
                    if (kv.Value.ExpiresAt < DateTimeOffset.UtcNow) _cache.TryRemove(kv.Key, out _);
                }
            }
            return minted.Token;
        }
        finally
        {
            _mintLock.Release();
        }
    }
}
