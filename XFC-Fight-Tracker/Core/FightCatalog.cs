using System.Collections.Generic;
using XFCFightTracker.Models;

namespace XFCFightTracker.Core;

/// <summary>
/// The rules of every match type run at X-Fight Club, transcribed from
/// https://xfightclub.com/fights. This is the single source of truth for
/// defaults and for the rule text shown in the UI.
/// </summary>
public static class FightCatalog
{
    public static IReadOnlyList<FightTypeDefinition> All { get; } = new List<FightTypeDefinition>
    {
        new()
        {
            Type = FightType.BasicRules,
            Name = "Basic Rules",
            Scoring = FightScoring.Damage,
            Summary = "8 rounds of attacking and defending. Most damage wins.",
            DefaultRounds = 8,
            DefaultFighterCount = 2,
            Rules = new[]
            {
                "8 rounds of attacking and defending.",
                "First brawler types /random in chat to attack; second does the same to defend.",
                "Roles switch every round.",
                "If the attacker's roll beats the defender's, their damage is recorded.",
                "If the defender rolls higher, no damage goes through.",
                "Winner is whoever dealt the most damage.",
            },
        },
        new()
        {
            Type = FightType.RussianRoulette,
            Name = "Russian Roulette",
            Scoring = FightScoring.Deathroll,
            Summary = "Announcer-started deathroll circle. Roll off the last number.",
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "Announcer takes all fighters who rolled to get in.",
                "All brawlers form a circle and take turns deathrolling.",
                "The announcer rolls /random and passes the pew pew to whoever they choose.",
                "That person rolls off the number given to them.",
                "Brawlers may roll multiple times, but must announce it before rolling.",
            },
        },
        new()
        {
            Type = FightType.BattleRoyale,
            Name = "Battle Royale",
            Scoring = FightScoring.Elimination,
            Summary = "Everyone rolls; lowest is out. Final two fight Basic Rules.",
            DefaultRounds = 8,
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "Everyone in the ring; the announcer calls for a /random from all.",
                "Whoever has the lowest roll is out.",
                "Repeat until two fighters remain.",
                "The final two have a standard 8-round Basic Rules fight.",
            },
        },
        new()
        {
            Type = FightType.Gauntlet,
            Name = "Gauntlet",
            Scoring = FightScoring.HighestRoll,
            Summary = "Best of 5. Both fighters roll, highest roll takes the round.",
            DefaultRoundsToWin = 3,
            DefaultFighterCount = 2,
            Rules = new[]
            {
                "Used for quick matches and Gauntlet tournaments.",
                "Best of 5 to win the match.",
                "Both fighters roll /random during each round.",
                "Highest roll wins the round.",
            },
        },
        new()
        {
            Type = FightType.MoneyInTheBank,
            Name = "Money in the Bank",
            Scoring = FightScoring.HighestRoll,
            Summary = "Gauntlet rules. Winner cashes in to challenge any title holder.",
            DefaultRoundsToWin = 3,
            DefaultFighterCount = 2,
            Rules = new[]
            {
                "Gauntlet rules apply.",
                "A dedicated day for sign-ups; fighters challenge each other.",
                "Cashing in lets you fight ANY title holder - but you must run the gauntlet first.",
                "If a TNT or Iron title holder cannot make it, the belt fight is rescheduled once only.",
                "After that the title is forfeited back to the public.",
                "If a Tag Team or World Champ cannot make it, #1 Contender and Runner Up fight instead.",
                "Previous belt holders may return the next month as #1 Contenders for one final shot.",
            },
        },
        new()
        {
            Type = FightType.PowerBottomExtravaganza,
            Name = "Power Bottom Battle Extravaganza",
            Scoring = FightScoring.HighestRoll,
            Summary = "Battle royale splits the field, two brackets, then a champion vs champion final.",
            DefaultRoundsToWin = 3,
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "All entrants join an epic battle royale.",
                "The bottom half of fighters are sorted into \"Bottoms\".",
                "The top half go into \"Power Bottoms\".",
                "Each bracket runs 1v1 Gauntlet rules, best of 5, one bracket after the other.",
                "The winner of each bracket is crowned \"Bottom\" and \"Power Bottom\".",
                "Those two champions then fight 1v1 under Normal Rules.",
                "The winner is the TRUE Bottomest of bottoms.",
            },
        },
        new()
        {
            Type = FightType.TntBelt,
            Name = "T.N.T Belt",
            Scoring = FightScoring.Custom,
            Summary = "Battle Royale style, then the final match follows rules set by the belt holder.",
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "Run as a Battle Royale.",
                "Rules of the final match are dictated by the current belt holder.",
                "The belt holder comes up with the rules.",
            },
        },
        new()
        {
            Type = FightType.TagTeamChampionship,
            Name = "Tag Team Championship Belt",
            Scoring = FightScoring.Elimination,
            Summary = "Tag battle royales. 2 lives per partner, 4 per team. Final is 6 rounds max.",
            DefaultRounds = 6,
            DefaultLives = 2,
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "Multiple rounds of Battle Royale against different teams.",
                "One partner goes first and rolls /random; the lowest number is out.",
                "Tag your partner in and repeat.",
                "Each tag partner gets 2 lives; teams have 4 lives total.",
                "The final two teams face off in normal fight style, 6 rounds maximum.",
            },
        },
        new()
        {
            Type = FightType.IronCrownBelt,
            Name = "Iron Crown Belt",
            Scoring = FightScoring.DamageWindow,
            Summary = "Up to 12 rounds. Most damage across a 4-round window wins a match. Best 2 of 3.",
            DefaultRounds = 12,
            DefaultFighterCount = 2,
            Rules = new[]
            {
                "Up to 12 rounds.",
                "Outlast your opponent in damage.",
                "Most damage across 4 rounds wins a match.",
                "Best 2 out of 3 matches takes the belt.",
                "Contender is decided normally.",
                "Fought on every off Saturday.",
            },
        },
        new()
        {
            Type = FightType.WorldChampionship,
            Name = "XFC World Championship Belt",
            Scoring = FightScoring.Elimination,
            Summary = "Monthly Battle Royale decides the qualifier, who then fights the belt holder.",
            DefaultFighterCount = 0,
            Rules = new[]
            {
                "Host a Battle Royale to determine the qualifier.",
                "The qualifier fights the belt holder at the end of the month.",
                "Runs every month.",
            },
        },
    };

    public static FightTypeDefinition Get(FightType type)
    {
        foreach (var def in All)
        {
            if (def.Type == type)
            {
                return def;
            }
        }

        return All[0];
    }
}
