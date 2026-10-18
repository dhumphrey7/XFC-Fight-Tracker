using System;
using System.Text.RegularExpressions;

namespace XFCFightTracker.Core;

/// <summary>
/// A roll read out of the chat log.
/// </summary>
public sealed record ParsedRoll(string PlayerName, int Value, int Max, string RawLine);

/// <summary>
/// Parses FFXIV /random output out of the chat log.
///
/// The game prints random rolls in a few shapes depending on locale and range, e.g.
///   "Kain Highwind rolls a 573. (1-999)"
///   "Kain Highwind rolls 42 (out of 999)."
///   "You roll a 128. (1-1000)"
/// Rolls are attributed to the player named in the line, or to the local player for "You roll".
/// </summary>
public static partial class RollParser
{
    [GeneratedRegex(
        @"^(?<who>.+?)\s+rolls?\s+(?:a\s+)?(?<value>\d+)(?:\s*\(?(?:out of|1-)\s*(?<max>\d+)\)?)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RollRegex();

    [GeneratedRegex(@"^You\s+rolls?\s+(?:a\s+)?(?<value>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YouRollRegex();

    /// <summary>
    /// Attempt to parse a chat line as a roll. Returns null when the line is not a roll.
    /// </summary>
    /// <param name="message">The chat message body, already stripped of chat channel markup.</param>
    /// <param name="localPlayerName">Name to attribute "You roll" lines to.</param>
    public static ParsedRoll? TryParse(string? message, string? localPlayerName)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var text = message.Trim();

        var you = YouRollRegex().Match(text);
        if (you.Success && int.TryParse(you.Groups["value"].Value, out var youValue))
        {
            return new ParsedRoll(localPlayerName ?? "You", youValue, 999, text);
        }

        var m = RollRegex().Match(text);
        if (!m.Success)
        {
            return null;
        }

        var who = m.Groups["who"].Value.Trim();

        // Guard against false positives such as "Kain Highwind rolls their eyes".
        if (!int.TryParse(m.Groups["value"].Value, out var value))
        {
            return null;
        }

        var max = 999;
        if (m.Groups["max"].Success && int.TryParse(m.Groups["max"].Value, out var parsedMax) && parsedMax > 0)
        {
            max = parsedMax;
        }

        return new ParsedRoll(who, value, max, text);
    }
}
