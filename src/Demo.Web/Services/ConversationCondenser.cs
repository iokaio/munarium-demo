// SPDX-License-Identifier: Apache-2.0
using System.Text;

namespace Demo.Web.Services;

/// <summary>
/// munarium-server keeps no conversation state between turns, so the BFF folds
/// the previous exchange into an explicitly requested follow-up query: the last
/// two messages, capped at 1,200 characters each, rendered as
/// "Conversation so far:" + "Current question:". An empty history sends the
/// message unchanged. Independent questions never search previous model answers.
/// </summary>
public sealed class ConversationCondenser
{
    private const int MaxTurns = 2;
    private const int MaxHistoryChars = 2500;
    private const int MaxSingleTurnChars = 1200;

    public sealed record HistoryTurn(string Role, string Text);

    public string Condense(IReadOnlyList<HistoryTurn>? history, string message, bool followUp = false)
    {
        if (!followUp || history is null || history.Count == 0) return message;

        // Newest-first selection under the budget, then re-reverse for display.
        var kept = new List<string>();
        var total = 0;
        foreach (var turn in history.Reverse().Take(MaxTurns))
        {
            var role = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? "User" : "Assistant";
            var text = (turn.Text ?? "").Trim();
            if (text.Length > MaxSingleTurnChars) text = text[..MaxSingleTurnChars] + "…";
            var line = $"{role}: {text}";
            if (total + line.Length > MaxHistoryChars) break;
            total += line.Length;
            kept.Add(line);
        }
        if (kept.Count == 0) return message;
        kept.Reverse();

        var sb = new StringBuilder();
        sb.AppendLine("Conversation so far:");
        foreach (var line in kept) sb.AppendLine(line);
        sb.Append("Current question: ").Append(message);
        return sb.ToString();
    }
}
