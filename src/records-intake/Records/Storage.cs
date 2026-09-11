// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Records;

public static class Storage
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
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
}
public record Route(string File, string Id, string Department);
public record Scope(string Collection, string Runbook);
public record Grant(string Token, string Uid, SortedDictionary<string, Scope> Routes, string Restricted);
public sealed class Entry
{
    public string File { get; set; } = "";
    public string Hash { get; set; } = "";
    public string State { get; set; } = "discovered";
    public DateTimeOffset FirstSeen { get; set; }
    public string? SourceId { get; set; }
    public string? RunId { get; set; }
    public string? BuildRunbook { get; set; }
    public string? Error { get; set; }
    public List<string> History { get; set; } = ["discovered"];
    public void Stage(string state) { State = state; if (!History.Contains(state)) History.Add(state); }
}
