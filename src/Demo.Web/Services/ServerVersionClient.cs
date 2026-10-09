// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Demo.Web.Services;

/// <summary>Reads only the public version endpoint; never sends application credentials.</summary>
public sealed class ServerVersionClient(HttpClient http, MunariumOptions options)
{
    public async Task<string?> GetAsync(CancellationToken ct)
    {
        try
        {
            var uri = new Uri(options.BaseUrl.TrimEnd('/') + "/version");
            var version = await http.GetFromJsonAsync<VersionResponse>(uri, ct);
            return version is { Name: "munarium-server", Version.Length: > 0 and <= 64 }
                && Regex.IsMatch(version.Version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?\z")
                ? version.Version : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private sealed record VersionResponse(string? Name, string? Version);
}
