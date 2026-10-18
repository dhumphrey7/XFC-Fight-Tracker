using System;
using System.Collections.Generic;

namespace XFCFightTracker.Models;

/// <summary>
/// The outcome of one round: who rolled what, who attacked, and what landed.
/// </summary>
public sealed class RoundResult
{
    public int RoundNumber { get; set; }

    /// <summary>Fighter id -> roll value for this round.</summary>
    public Dictionary<string, int> Rolls { get; set; } = new();

    /// <summary>Fighter id -> damage this fighter dealt this round.</summary>
    public Dictionary<string, int> DamageDealt { get; set; } = new();

    /// <summary>Fighter id of the attacker, when the format alternates roles.</summary>
    public string? AttackerId { get; set; }

    /// <summary>Fighter id of the defender, when the format alternates roles.</summary>
    public string? DefenderId { get; set; }

    /// <summary>Fighter id who won the round, when the format has a round winner.</summary>
    public string? WinnerId { get; set; }

    /// <summary>Fighter id eliminated this round, for elimination formats.</summary>
    public string? EliminatedId { get; set; }

    /// <summary>Roll range used, e.g. 999 for /random 999.</summary>
    public int RollMax { get; set; } = 999;

    /// <summary>True when the defender's roll blocked the attack.</summary>
    public bool Blocked { get; set; }

    /// <summary>Optional announcer note.</summary>
    public string? Note { get; set; }

    public RoundResult Clone() => new()
    {
        RoundNumber = RoundNumber,
        Rolls = new Dictionary<string, int>(Rolls),
        DamageDealt = new Dictionary<string, int>(DamageDealt),
        AttackerId = AttackerId,
        DefenderId = DefenderId,
        WinnerId = WinnerId,
        EliminatedId = EliminatedId,
        RollMax = RollMax,
        Blocked = Blocked,
        Note = Note,
    };
}

/// <summary>
/// A completed match, kept for history and export.
/// </summary>
public sealed class MatchRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public FightType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime EndedAt { get; set; } = DateTime.Now;
    public List<Fighter> Fighters { get; set; } = new();
    public List<RoundResult> Rounds { get; set; } = new();
    public string? WinnerName { get; set; }
    public string? RunnerUpName { get; set; }
    public string? BeltName { get; set; }
    public string? Notes { get; set; }

    /// <summary>One-line summary suitable for chat or a log file.</summary>
    public string Summary()
    {
        var winner = WinnerName ?? "nobody";
        var belt = string.IsNullOrWhiteSpace(BeltName) ? string.Empty : $" [{BeltName}]";
        return $"{StartedAt:yyyy-MM-dd HH:mm} | {TypeName}{belt} | {Fighters.Count} fighters | winner: {winner} | {Rounds.Count} rounds";
    }
}
