// SPDX-License-Identifier: Apache-2.0
using Demo.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Web.Pages;

/// <summary>
/// Demo usage dashboard — read-only rollups from the munarium server's mgmt
/// reports API. Each section fetches independently and degrades to an error
/// note, so one failing report never blanks the page. Gated like every other
/// page; the mgmt token never leaves the BFF.
/// </summary>
public class StatsModel : PageModel
{
    private readonly MunariumClient _munarium;

    public StatsModel(MunariumClient munarium) => _munarium = munarium;

    public MunariumClient.UsageReport? UsageByRunbook { get; private set; }
    public MunariumClient.CostReport? Cost { get; private set; }
    public MunariumClient.TimeseriesReport? Timeseries { get; private set; }
    public MunariumClient.SessionsReport? Sessions { get; private set; }
    public List<string> Errors { get; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Per-identity usage is deliberately NOT fetched here (2026-09-01):
        // this page is visitor-facing and shows aggregates only; per-email
        // detail lives on /admin.
        UsageByRunbook = await FetchAsync("usage by corpus", () => _munarium.GetUsageAsync("runbook", ct));
        Cost = await FetchAsync("model spend", () => _munarium.GetCostAsync(ct));
        Timeseries = await FetchAsync("traffic (24h)", () => _munarium.GetTimeseriesAsync("24h", ct));
        Sessions = await FetchAsync("sessions (7d)", () => _munarium.GetSessionsReportAsync("7d", ct));
    }

    private async Task<T?> FetchAsync<T>(string label, Func<Task<T>> fetch) where T : class
    {
        try
        {
            return await fetch();
        }
        catch (MunariumUnreachableException)
        {
            Errors.Add($"{label}: the munarium backend is unreachable.");
            return null;
        }
        catch (MunariumApiException ex)
        {
            Errors.Add($"{label}: munarium answered {ex.Status} {ex.Detail}".TrimEnd());
            return null;
        }
    }

    /// <summary>
    /// Display form of a uid: an access-code uid shows its code slug without
    /// the prefix; an anonymous visitor hash is shortened; others pass through.
    /// </summary>
    public static string ShortUid(string uid) => uid switch
    {
        _ when uid.StartsWith("code-") && uid.Length > 5 => uid[5..],
        _ when uid.StartsWith("visitor-") && uid.Length > 20 => uid[..20] + "…",
        _ => uid,
    };

    public static string Num(long n) => n.ToString("N0");

    public static string Ms(double? ms) => ms is null ? "—" : $"{ms:N0} ms";

    /// <summary>Local-legible bucket label from an RFC 3339 UTC stamp.</summary>
    public static string Bucket(string rfc3339) =>
        DateTimeOffset.TryParse(rfc3339, out var t) ? t.ToString("MM-dd HH:mm 'UTC'") : rfc3339;
}
