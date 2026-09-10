// SPDX-License-Identifier: Apache-2.0
namespace Demo.Web.Services;

/// <summary>
/// 5-minute cache over the server's free provider introspection
/// (GET /v1/providers) so /api/models never turns page loads into backend
/// round trips. Single-flight is not needed at demo scale — a duplicate
/// fetch is a free call.
/// </summary>
public sealed class ModelCatalogCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly object _lock = new();
    private (DateTimeOffset At, object Payload)? _cached;

    public async Task<object> GetAsync(Func<Task<object>> fetch)
    {
        lock (_lock)
        {
            if (_cached is { } c && DateTimeOffset.UtcNow - c.At < Ttl)
                return c.Payload;
        }
        var payload = await fetch();
        lock (_lock)
        {
            _cached = (DateTimeOffset.UtcNow, payload);
        }
        return payload;
    }
}
