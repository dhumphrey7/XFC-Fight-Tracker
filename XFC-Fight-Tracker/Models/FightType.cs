using System;

namespace XFCFightTracker.Models;

/// <summary>
/// The match types run at X-Fight Club. Mirrors https://xfightclub.com/fights
/// </summary>
public enum FightType
{
    BasicRules,
    RussianRoulette,
    BattleRoyale,
    Gauntlet,
    MoneyInTheBank,
    PowerBottomExtravaganza,
    TntBelt,
    TagTeamChampionship,
    IronCrownBelt,
    WorldChampionship,
}

/// <summary>
/// How a match decides its winner.
/// </summary>
public enum FightScoring
{
    /// <summary>Attacker must out-roll the defender to land damage. Most damage wins.</summary>
    Damage,

    /// <summary>Highest roll takes the round. First to the required round wins.</summary>
    HighestRoll,

    /// <summary>Most damage across a window of rounds wins the match, best-of series.</summary>
    DamageWindow,

    /// <summary>Lowest roll is eliminated each pass.</summary>
    Elimination,

    /// <summary>Deathroll: roll off the previous number, 1 loses.</summary>
    Deathroll,

    /// <summary>Rules are dictated by the belt holder at match time.</summary>
    Custom,
}

/// <summary>
/// Immutable description of a match type's rules, used to drive the UI and defaults.
/// </summary>
public sealed class FightTypeDefinition
{
    public required FightType Type { get; init; }
    public required string Name { get; init; }
    public required FightScoring Scoring { get; init; }
    public required string Summary { get; init; }

    /// <summary>Rounds in the main phase, or 0 when the phase is not round-based.</summary>
    public int DefaultRounds { get; init; }

    /// <summary>Rounds needed to take the match in a best-of format (e.g. Gauntlet = 3).</summary>
    public int DefaultRoundsToWin { get; init; }

    /// <summary>How many fighters a match expects, or 0 for "any".</summary>
    public int DefaultFighterCount { get; init; }

    /// <summary>Lives each fighter starts with (Tag Team = 2 per partner).</summary>
    public int DefaultLives { get; init; } = 1;

    /// <summary>Bullet points shown in the setup window.</summary>
    public string[] Rules { get; init; } = Array.Empty<string>();
}
