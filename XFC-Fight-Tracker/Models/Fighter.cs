using System;
using System.Collections.Generic;

namespace XFCFightTracker.Models;

/// <summary>
/// A single brawler in a match. Tag Team matches give each fighter a team name
/// and a shared pool of lives.
/// </summary>
public sealed class Fighter
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Team label for tag matches, null for singles.</summary>
    public string? Team { get; set; }

    /// <summary>Remaining lives. 1 for standard matches, 2 per partner in Tag Team.</summary>
    public int Lives { get; set; } = 1;

    public bool IsEliminated { get; set; }

    /// <summary>Total damage this fighter has dealt across the match.</summary>
    public int TotalDamage { get; set; }

    /// <summary>Total damage this fighter has taken.</summary>
    public int DamageTaken { get; set; }

    /// <summary>Rounds this fighter has won outright.</summary>
    public int RoundsWon { get; set; }

    /// <summary>Order in the circle / bracket, used to keep displays stable.</summary>
    public int Seed { get; set; }

    /// <summary>Free-form notes, e.g. "Belt Holder" or "#1 Contender".</summary>
    public string? Note { get; set; }

    public Fighter Clone() => new()
    {
        Id = Id,
        Name = Name,
        Team = Team,
        Lives = Lives,
        IsEliminated = IsEliminated,
        TotalDamage = TotalDamage,
        DamageTaken = DamageTaken,
        RoundsWon = RoundsWon,
        Seed = Seed,
        Note = Note,
    };
}

/// <summary>
/// A tag team, aggregating the lives and damage of its members.
/// </summary>
public sealed class Team
{
    public string Name { get; set; } = string.Empty;
    public List<string> FighterIds { get; set; } = new();

    public int LivesRemaining(IReadOnlyList<Fighter> fighters)
    {
        var total = 0;
        foreach (var id in FighterIds)
        {
            var f = FindById(fighters, id);
            if (f is { IsEliminated: false })
            {
                total += f.Lives;
            }
        }

        return total;
    }

    public int TotalDamage(IReadOnlyList<Fighter> fighters)
    {
        var total = 0;
        foreach (var id in FighterIds)
        {
            var f = FindById(fighters, id);
            if (f != null)
            {
                total += f.TotalDamage;
            }
        }

        return total;
    }

    public bool IsEliminated(IReadOnlyList<Fighter> fighters)
        => LivesRemaining(fighters) <= 0;

    private static Fighter? FindById(IReadOnlyList<Fighter> fighters, string id)
    {
        foreach (var f in fighters)
        {
            if (f.Id == id)
            {
                return f;
            }
        }

        return null;
    }
}
