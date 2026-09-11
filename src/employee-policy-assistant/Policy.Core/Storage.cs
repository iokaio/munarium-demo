// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ioka.Munarium.Client;

namespace Policy.Core;

public static class Storage
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);
    public static void Save<T>(string path, T value) => Write(path, JsonSerializer.Serialize(value, Json) + "\n");
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }
}

public sealed record ModelChoice(string Provider, string Family, string Model)
{
    public override string ToString() => $"{Family} / {Model}";
}
public sealed record IdentityGrant(string Identity, string Uid, string Token, string ExpiresAt, string Runbook, string Revision, ModelChoice[] Models);
public sealed record AnswerRecord(string Uid, string SessionId, string Query, string Status, TurnResult? Result, TurnProgressEvent[] Progress, bool Recovered = false, ModelChoice? RequestedModel = null, JsonElement? StoredCompletion = null);

public static class Evidence
{
    public static string Label(TurnHit hit) => $"[{hit.Collection}/{hit.ChunkId}]";
    public static void Validate(TurnResult result)
    {
        var completion = result.Completion ?? throw new InvalidDataException("No completion returned.");
        if (completion.Verification?.Violations.Count > 0) throw new InvalidDataException("Server reported unverified evidence.");
        var labels = System.Text.RegularExpressions.Regex.Matches(completion.Text, @"\[([^\[\]\s]+/[^\[\]\s]+)\]").Select(m => m.Value).ToArray();
        if (labels.Any(label => !result.Hits.Any(hit => Label(hit) == label))) throw new InvalidDataException("Answer cites an unserved source.");
        if (result.Hits.Count > 0 && labels.Length == 0) throw new InvalidDataException("Answer has no served citation.");
    }
    public static string Export(AnswerRecord answer)
    {
        if (answer.Status != "complete" || answer.Result is null) throw new InvalidOperationException("Only completed, checked answers can be exported.");
        var result = answer.Result;
        return $"FICTIONAL TRAINING MATERIAL — review before use\nIdentity: {answer.Uid}\nSession: {answer.SessionId}\nRecovered: {answer.Recovered}\nQuestion: {answer.Query}\n\n{result.Completion?.Text}\n\nSources\n" +
            string.Join("\n\n", result.Hits.Select(h => $"{Label(h)} {h.SourcePath}\nSHA-256: {h.SourceContentHash}\n{h.Text}")) + "\n";
    }
}
