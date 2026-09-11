// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ioka.Munarium.Client;

namespace Meetings;

public static class Storage
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException(path);
    public static void Save<T>(string path, T value) => Write(path, JsonSerializer.Serialize(value, Json) + "\n");
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(Encoding.UTF8.GetBytes(text)); stream.Flush(true); }
        File.Move(temporary, path, true);
    }
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    public static FileStream Lease(string work) { Directory.CreateDirectory(work); return new FileStream(Path.Combine(work, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
}
public record Manifest(int Seed, string Generator, string LogicalTime, SortedDictionary<string, string> Files, string Profile, string TemplateRevision, SortedDictionary<string, int> RecordCounts, string Timezone, string Locale);
public record Grant(string Token, string Uid, string Provider, string Model, string Revision, SortedDictionary<string, string> Runbooks);
public sealed record Candidate(string Id, string Description, string? Owner, string? DueDate, string Disposition, string Quote, string[] Citations);
public record CandidateList(Candidate[] Candidates);
public record Draft(string Case, string InputHash, string Status, Candidate[] Candidates, string[] Errors, TurnResult Result, string Runbook, bool Recovered);
public sealed class TurnJournal
{
    public string Key { get; set; } = "";
    public string Query { get; set; } = "";
    public string Runbook { get; set; } = "";
    public string State { get; set; } = "creating_session";
    public string? SessionId { get; set; }
    public TurnResult? Result { get; set; }
    public List<JsonElement> Progress { get; set; } = [];
    public bool Recovered { get; set; }
}
public record Review(string DraftHash, string CandidateId, string Decision, string Reviewer, string Reason);
public sealed class Command
{
    public string Kind { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string State { get; set; } = "uncertain";
    public ClaimInput? Claim { get; set; }
    public JsonElement? Body { get; set; }
    public JsonElement? Response { get; set; }
    public List<JsonElement> Attempts { get; set; } = [];
}
public sealed class ImportJournal
{
    public string ReviewHash { get; set; } = "";
    public string? Version { get; set; }
    public ulong? PriorPin { get; set; }
    public SortedDictionary<string, Command> Commands { get; set; } = new(StringComparer.Ordinal);
}
