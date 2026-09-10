// SPDX-License-Identifier: Apache-2.0
namespace Demo.Web.Services;

/// <summary>Connection settings for the munarium-server backend.</summary>
public sealed class MunariumOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8080";
    public string MgmtToken { get; set; } = "";
    /// <summary>The uid every BFF call asserts (JWT sub + X-Munarium-Uid).</summary>
    public string Uid { get; set; } = "demo-web";
}

/// <summary>Connection settings for the Munarium Matrix operator console
/// (WP-7.7). An empty BaseUrl means "no Matrix here": the nav shows no tab and
/// the passthrough answers 404, so a deployment without a Matrix never grows a
/// dead link.</summary>
public sealed class MatrixOptions
{
    /// <summary>Matrix's REST base (the /admin console lives on it), e.g.
    /// http://localhost:8180. Empty = feature off.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The Matrix mgmt token the BFF injects server-side. The rw
    /// token the server itself holds is deliberately NOT reused: Matrix's
    /// /admin refuses it, and the demo should hold no credential stronger
    /// than the page it fronts.</summary>
    public string MgmtToken { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>Whether the /matrix-admin console is SHOWN (nav tab, links,
    /// the passthrough route). Off by default (owner-directed, 2026-09-01:
    /// the Matrix console is not shown yet); a configured Matrix with
    /// <c>MATRIX_ADMIN_SHOWN=1</c> turns it back on without a code change.
    /// Deliberately a separate switch from <see cref="Enabled"/>: BaseUrl is
    /// terraform-managed and removing it would drift the deployed config,
    /// which azuredeploy's preflight refuses.</summary>
    public bool AdminConsoleShown { get; set; }

    /// <summary>The console is reachable only when a Matrix exists AND the
    /// show switch is on.</summary>
    public bool ConsoleVisible => Enabled && AdminConsoleShown;
}

/// <summary>One demo corpus: which runbook it fronts, what clearance the demo token
/// carries, and how it presents in the Collections nav.</summary>
public sealed class CorpusConfig
{
    public string Runbook { get; set; } = "";
    public int AccessLevel { get; set; }
    public string[] Compartments { get; set; } = [];
    /// <summary>Dataroom only: named personas overriding level/compartments.</summary>
    public Dictionary<string, PersonaConfig>? Personas { get; set; }

    // ---- display registry ---------------------------------------------------
    // The nav is generated from this same dictionary the API keys on, so a
    // corpus cannot appear in the menu without being callable, or vice versa.
    // The page prose stays hand-written: only identity lives here.

    /// <summary>Page and card title, e.g. "Wealth Advisory Record".</summary>
    public string Title { get; set; } = "";

    /// <summary>Short label for the Collections dropdown; falls back to Title.</summary>
    public string? NavLabel { get; set; }

    /// <summary>Route; falls back to "/" + the corpus key.</summary>
    public string? Path { get; set; }

    /// <summary>Ascending order in the dropdown. LOAD-BEARING: configuration
    /// binding enumerates dictionary keys alphabetically, not in the order they
    /// are declared in appsettings.json, so without this the menu reads
    /// advisory-first.</summary>
    public int NavOrder { get; set; } = 1000;

    /// <summary>Material Design Icons class, e.g. "mdi-bank".</summary>
    public string? Icon { get; set; }

    /// <summary>Resolve effective clearance for an optional persona name.</summary>
    public (int Level, string[] Compartments) Resolve(string? persona)
    {
        if (Personas is { Count: > 0 })
        {
            var key = string.IsNullOrWhiteSpace(persona) ? "associate" : persona.Trim().ToLowerInvariant();
            if (!Personas.TryGetValue(key, out var p))
                throw new ArgumentException($"unknown persona '{persona}'");
            return (p.AccessLevel, p.Compartments);
        }
        return (AccessLevel, Compartments);
    }
}

public sealed class PersonaConfig
{
    public int AccessLevel { get; set; }
    public string[] Compartments { get; set; } = [];
}

/// <summary>Read helpers over the corpus registry, so views never repeat the
/// fallback rules.</summary>
public static class CorpusRegistry
{
    /// <summary>The corpora in nav order (key breaks a tie, so the order is total).</summary>
    public static IEnumerable<(string Key, CorpusConfig Corpus)> Ordered(
        this Dictionary<string, CorpusConfig> map) =>
        map.OrderBy(kv => kv.Value.NavOrder)
           .ThenBy(kv => kv.Key, StringComparer.Ordinal)
           .Select(kv => (kv.Key, kv.Value));

    public static string NavLabelOrTitle(this CorpusConfig c) =>
        string.IsNullOrWhiteSpace(c.NavLabel) ? c.Title : c.NavLabel;

    public static string PathFor(this CorpusConfig c, string key) =>
        string.IsNullOrWhiteSpace(c.Path) ? "/" + key : c.Path;

    /// <summary>Title for a runbook name, for reports that group by runbook.
    /// Falls back to the raw name so an unregistered runbook still shows.</summary>
    public static string TitleForRunbook(this Dictionary<string, CorpusConfig> map, string runbook) =>
        map.Values.FirstOrDefault(c => c.Runbook == runbook) is { Title.Length: > 0 } hit
            ? hit.Title
            : runbook;
}

/// <summary>/admin login credentials (2026-09-01) — from env/secret only,
/// never committed. Either missing means the admin login is disabled.</summary>
public sealed class AdminCredentials
{
    public AdminCredentials(string? user, string? password)
    {
        User = user;
        Password = password;
    }

    public string? User { get; }
    public string? Password { get; }
    public bool Configured => !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);
}

/// <summary>The address the chip batteries sign in with (2026-09-02):
/// <c>demo-corpus-check.ps1</c>'s default <c>-Email</c>. Its usage on /admin
/// is scripted traffic, not a person's, and the identity panel badges it so —
/// on the first day of per-email attribution every battery turn read as the
/// usage of a visitor nobody could place. <c>DEMO_VERIFY_EMAIL</c> /
/// <c>Gate:VerifyEmail</c> override the default when a battery is pointed at
/// another address.</summary>
public sealed class BatteryIdentity
{
    public const string DefaultEmail = "verification@example.invalid";

    public BatteryIdentity(string? email) =>
        Email = DemoStore.NormalizeEmail(string.IsNullOrWhiteSpace(email) ? DefaultEmail : email);

    public string Email { get; }

    public bool IsBattery(string email) =>
        string.Equals(DemoStore.NormalizeEmail(email), Email, StringComparison.Ordinal);
}
