// SPDX-License-Identifier: Apache-2.0
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ioka.Munarium.Client;
using Policy.Core;

namespace Policy.Desktop;

public sealed class PolicyViewModel : INotifyPropertyChanged
{
    public PolicySession Session { get; }
    private readonly string credentials;
    private CancellationTokenSource? cancellation;
    public string[] Identities { get; }
    public ObservableCollection<ModelChoice> Models { get; } = [];
    public ObservableCollection<TurnHit> Sources { get; } = [];
    public ObservableCollection<string> Stages { get; } = [];
    private string status = "Select a tutorial identity to begin.";
    private string answer = "";
    private bool busy;
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Answer { get => answer; private set { answer = value; Changed(); } }
    public bool Busy { get => busy; private set { busy = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public PolicyViewModel(PolicySession session, string credentials)
    {
        Session = session; this.credentials = credentials;
        Identities = Directory.Exists(credentials) ? Directory.GetFiles(credentials, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray() : [];
        Session.Progress += p =>
        {
            var family = Session.Identity?.Models.FirstOrDefault(m => m.Provider == p.Provider)?.Family ?? p.Provider;
            Stages.Add($"{p.Stage}{(p.Model is null ? "" : $" · {family} / {p.Model}")}{(p.InputTokens is null ? "" : $" · {p.InputTokens} in / {p.OutputTokens} out")}");
        };
        if (Identities.Length == 0) Status = "No identity grants found. Run bootstrap and set POLICY_CREDENTIALS to the issued grants folder.";
    }
    public async Task SelectAsync(string identity)
    {
        await Guard(async () =>
        {
            await Session.SelectIdentityAsync(Storage.Read<IdentityGrant>(Path.Combine(credentials, identity + ".json")));
            Sources.Clear(); Stages.Clear(); Answer = ""; Models.Clear();
            foreach (var model in Session.Identity!.Models) Models.Add(model);
            Status = Session.Answer?.Status == "uncertain" ? "An interrupted turn was found for this identity. Inspect its transcript before asking again." : $"{Session.Identity.Uid} · capability expires {Session.Identity.ExpiresAt}";
        });
    }
    public async Task AskAsync(string question, ModelChoice? model, bool followUp)
    {
        await Guard(async () =>
        {
            if (model is null) throw new InvalidOperationException("Select a model.");
            Stages.Clear(); Sources.Clear(); Answer = "";
            using var source = new CancellationTokenSource(); cancellation = source;
            try { Display(await Session.AskAsync(question, model, followUp, ct: source.Token)); }
            finally { cancellation = null; }
        });
    }
    public void Cancel() => cancellation?.Cancel();
    public Task ReconcileAsync() => Guard(async () =>
    {
        var result = await Session.ReconcileAsync();
        if (result is null) Status = "No interrupted turn found for this identity."; else Display(result);
    });
    public Task RefreshAsync() => Guard(async () =>
    {
        var identity = Session.Identity ?? throw new InvalidOperationException("Select an identity first.");
        await Session.RefreshAsync(Storage.Read<IdentityGrant>(Path.Combine(credentials, identity.Identity + ".json")));
        Status = $"Refreshed {identity.Uid}; no turn was resent.";
    });
    public Task ExportAsync(string path) => Guard(() => { Session.Export(path); Status = "Answer and evidence exported."; return Task.CompletedTask; });
    private void Display(AnswerRecord record)
    {
        Sources.Clear(); Answer = record.Result?.Completion?.Text ?? "";
        foreach (var hit in record.Result?.Hits ?? []) Sources.Add(hit);
        Status = record.Status switch
        {
            "complete" => $"{record.Uid} · {record.Result?.Completion?.Provider} / {record.Result?.Completion?.Model}{(record.Recovered ? " · recovered from transcript" : "")}",
            "rejected" => "Server rejected the credential before inference. Reload a renewed grant before asking again.",
            "unverified" => "The answer failed evidence checks and cannot be exported.",
            _ => "Turn uncertain. Inspect the transcript before asking again."
        };
    }
    private async Task Guard(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); }
        catch (UnauthenticatedException) { Status = "Capability expired or rejected. Ask the issuer to renew this identity, then reload its grant and reconcile."; }
        catch (ForbiddenException) { Status = "Server denied this operation. Check the identity and allowed model; no automatic retry."; }
        catch (OperationCanceledException) { Status = "Stream disconnected; the turn is uncertain. Inspect its transcript."; }
        catch (Exception ex) { Status = Session.Answer?.Status == "uncertain" ? "Turn uncertain. Inspect its transcript; do not resend automatically." : ex.Message; }
        finally { Busy = false; }
    }
}
