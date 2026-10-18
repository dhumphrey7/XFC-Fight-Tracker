using System;
using System.Collections.Generic;
using System.Linq;
using XFCFightTracker.Models;

namespace XFCFightTracker.Core;

public enum MatchPhase
{
    Setup,

    /// <summary>Everyone rolls, lowest is out (Battle Royale, Tag Team, brackets).</summary>
    Elimination,

    /// <summary>Round-by-round fighting (Basic, Gauntlet, Iron Crown, finals).</summary>
    RoundPhase,

    Complete,
}

/// <summary>How a landed attack's damage is calculated.</summary>
public enum DamageMode
{
    /// <summary>The attacker's roll becomes the damage (default).</summary>
    AttackerRoll,

    /// <summary>Damage is the margin by which the attacker beat the defender.</summary>
    RollDifference,
}

/// <summary>What happened as a result of the last engine action.</summary>
public sealed record RoundOutcome(
    bool Success,
    string Message,
    string? EliminatedName = null,
    string? WinnerName = null,
    string? RunnerUpName = null);

/// <summary>
/// Drives a single X-Fight Club match: collects rolls, resolves rounds,
/// handles eliminations and decides the winner according to the fight type's rules.
///
/// The engine holds no Dalamud or UI dependencies so it can be exercised headlessly.
/// </summary>
public sealed class MatchEngine
{
    private readonly List<string> eliminationOrder = new();

    public MatchEngine(FightTypeDefinition definition, IEnumerable<Fighter> fighters, DamageMode damageMode = DamageMode.AttackerRoll)
    {
        Definition = definition;
        Fighters = fighters.Select(f => f.Clone()).ToList();
        DamageMode = damageMode;
        Phase = MatchPhase.Setup;

        for (var i = 0; i < Fighters.Count; i++)
        {
            Fighters[i].Seed = i;
            Fighters[i].Lives = definition.DefaultLives;
        }
    }

    public FightTypeDefinition Definition { get; }

    public List<Fighter> Fighters { get; }

    public List<RoundResult> Rounds { get; } = new();

    public MatchPhase Phase { get; private set; }

    public DamageMode DamageMode { get; }

    /// <summary>1-based number of the round currently being collected.</summary>
    public int CurrentRound { get; private set; } = 1;

    /// <summary>Rolls collected for the round in progress, keyed by fighter id.</summary>
    public Dictionary<string, int> PendingRolls { get; } = new();

    /// <summary>Wins per fighter in best-of formats (Gauntlet, Iron Crown blocks).</summary>
    public Dictionary<string, int> SeriesWins { get; } = new();

    /// <summary>Damage accrued inside the current Iron Crown 4-round block.</summary>
    public Dictionary<string, int> BlockDamage { get; } = new();

    public string? WinnerId { get; private set; }

    public string? RunnerUpId { get; private set; }

    /// <summary>Fighters eliminated so far, in the order they went out.</summary>
    public IReadOnlyList<string> EliminationOrder => eliminationOrder;

    public bool IsComplete => Phase == MatchPhase.Complete;

    public DateTime StartedAt { get; } = DateTime.Now;

    public Fighter? Winner => Find(WinnerId);

    public Fighter? RunnerUp => Find(RunnerUpId);

    /// <summary>Fighters still in the match.</summary>
    public IEnumerable<Fighter> Active => Fighters.Where(f => !f.IsEliminated);

    /// <summary>Teams still in a tag match.</summary>
    public IEnumerable<IGrouping<string, Fighter>> ActiveTeams =>
        Active.Where(f => !string.IsNullOrWhiteSpace(f.Team)).GroupBy(f => f.Team!);

    /// <summary>Rounds needed to take a best-of series (Gauntlet = 3 of 5).</summary>
    public int RoundsToWin =>
        Definition.DefaultRoundsToWin > 0
            ? Definition.DefaultRoundsToWin
            : (Definition.DefaultRounds > 0 ? (Definition.DefaultRounds / 2) + 1 : 3);

    /// <summary>Fighters expected to roll this round, or everyone active in a free-for-all.</summary>
    public IReadOnlyList<Fighter> FightersForRound()
    {
        var active = Active.ToList();

        // Round-phase finals of a composite format always run head to head.
        if (Phase == MatchPhase.RoundPhase && Definition.Scoring is FightScoring.Damage or FightScoring.DamageWindow)
        {
            return active;
        }

        return active;
    }

    /// <summary>Begin the match, moving out of Setup into the first real phase.</summary>
    public void Start()
    {
        if (Phase != MatchPhase.Setup)
        {
            return;
        }

        PendingRolls.Clear();
        CurrentRound = 1;

        Phase = Definition.Scoring switch
        {
            FightScoring.Elimination => MatchPhase.Elimination,
            FightScoring.Deathroll => MatchPhase.Elimination,
            _ => MatchPhase.RoundPhase,
        };
    }

    /// <summary>
    /// Record a roll for a fighter. Rolls are keyed by fighter id and may be overwritten
    /// until the round is resolved (matching a brawler re-rolling after announcing it).
    /// </summary>
    public bool SubmitRoll(string fighterId, int value, int rollMax = 999)
    {
        var fighter = Find(fighterId);
        if (fighter is null || fighter.IsEliminated || IsComplete)
        {
            return false;
        }

        PendingRolls[fighterId] = value;
        LastRollMax = rollMax;
        return true;
    }

    public int LastRollMax { get; private set; } = 999;

    public bool HasRoll(string fighterId) => PendingRolls.ContainsKey(fighterId);

    /// <summary>True when every fighter expected this round has a roll recorded.</summary>
    public bool RoundReady()
    {
        var needed = FightersForRound();
        return needed.Count > 0 && needed.All(f => PendingRolls.ContainsKey(f.Id));
    }

    /// <summary>
    /// Resolve the round in progress and apply the consequences for the fight type.
    /// </summary>
    public RoundOutcome ResolveRound()
    {
        if (IsComplete)
        {
            return new RoundOutcome(false, "The match is already over.");
        }

        if (Phase == MatchPhase.Setup)
        {
            return new RoundOutcome(false, "Start the match first.");
        }

        if (!RoundReady())
        {
            var missing = FightersForRound().Where(f => !PendingRolls.ContainsKey(f.Id)).Select(f => f.Name);
            return new RoundOutcome(false, $"Waiting on rolls from: {string.Join(", ", missing)}");
        }

        return Phase switch
        {
            MatchPhase.Elimination => ResolveElimination(),
            MatchPhase.RoundPhase => ResolveRoundPhase(),
            _ => new RoundOutcome(false, "Nothing to resolve."),
        };
    }

    // ---------------------------------------------------------------- elimination

    private RoundOutcome ResolveElimination()
    {
        var contenders = FightersForRound().ToList();
        if (contenders.Count == 0)
        {
            return new RoundOutcome(false, "No fighters remain.");
        }

        var lowest = contenders.Min(f => PendingRolls[f.Id]);
        var lowestFighters = contenders.Where(f => PendingRolls[f.Id] == lowest).ToList();

        if (lowestFighters.Count > 1 && Definition.Type != FightType.RussianRoulette)
        {
            // Ties must be re-rolled rather than eliminating two people at once.
            var names = string.Join(", ", lowestFighters.Select(f => f.Name));
            foreach (var f in lowestFighters)
            {
                PendingRolls.Remove(f.Id);
            }

            return new RoundOutcome(false, $"Tie at {lowest} between {names} - re-roll to break it.");
        }

        var victim = lowestFighters[0];
        var rollResult = new RoundResult
        {
            RoundNumber = CurrentRound,
            RollMax = LastRollMax,
            Rolls = new Dictionary<string, int>(PendingRolls),
            EliminatedId = victim.Id,
        };

        if (Definition.Type == FightType.TagTeamChampionship)
        {
            victim.Lives--;
            if (victim.Lives <= 0)
            {
                victim.IsEliminated = true;
                rollResult.Note = $"{victim.Name} is out of lives and eliminated.";
            }
            else
            {
                rollResult.Note = $"{victim.Name} loses a life ({victim.Lives} left).";
            }
        }
        else
        {
            victim.IsEliminated = true;
            eliminationOrder.Add(victim.Id);
        }

        Rounds.Add(rollResult);
        PendingRolls.Clear();
        CurrentRound++;

        var activeNow = Active.ToList();

        // Tag Team ends its elimination phase once two teams remain.
        if (Definition.Type == FightType.TagTeamChampionship)
        {
            var teamsLeft = ActiveTeams.Count();
            if (teamsLeft <= 2)
            {
                Phase = MatchPhase.RoundPhase;
                CurrentRound = 1;
                foreach (var f in activeNow)
                {
                    f.TotalDamage = 0;
                }

                return new RoundOutcome(true, $"{victim.Name} is out. Two teams remain - final fight, {Definition.DefaultRounds} rounds max.", victim.Name);
            }

            return new RoundOutcome(true, $"{victim.Name} is out. {teamsLeft} teams remain.", victim.Name);
        }

        // Everything else drops to the final two.
        if (activeNow.Count <= 2)
        {
            Phase = MatchPhase.RoundPhase;
            CurrentRound = 1;

            // Damage-based finals (Battle Royale, Power Bottom, World Champ) start clean.
            if (Definition.Scoring is FightScoring.Damage or FightScoring.DamageWindow)
            {
                foreach (var f in activeNow)
                {
                    f.TotalDamage = 0;
                    f.DamageTaken = 0;
                }
            }

            var a = activeNow.ElementAtOrDefault(0)?.Name ?? "?";
            var b = activeNow.ElementAtOrDefault(1)?.Name ?? "?";
            return new RoundOutcome(true, $"{victim.Name} is out. Final two: {a} vs {b}.", victim.Name);
        }

        return new RoundOutcome(true, $"{victim.Name} is out with the lowest roll ({lowest}). {activeNow.Count} remain.", victim.Name);
    }

    // ---------------------------------------------------------------- rounds

    private RoundOutcome ResolveRoundPhase()
    {
        var contenders = FightersForRound().ToList();
        if (contenders.Count == 0)
        {
            return new RoundOutcome(false, "No fighters remain.");
        }

        var result = new RoundResult
        {
            RoundNumber = CurrentRound,
            RollMax = LastRollMax,
            Rolls = new Dictionary<string, int>(PendingRolls),
        };

        RoundOutcome outcome = Definition.Scoring switch
        {
            FightScoring.Damage => ResolveDamageRound(contenders, result),
            FightScoring.HighestRoll => ResolveHighestRollRound(contenders, result),
            FightScoring.DamageWindow => ResolveDamageWindowRound(contenders, result),
            FightScoring.Custom => ResolveHighestRollRound(contenders, result),
            _ => ResolveHighestRollRound(contenders, result),
        };

        if (!outcome.Success)
        {
            return outcome;
        }

        Rounds.Add(result);
        PendingRolls.Clear();

        if (IsComplete)
        {
            return outcome;
        }

        CurrentRound++;
        return outcome;
    }

    /// <summary>Basic Rules: alternating attack/defend, attacker's roll must beat the defender's.</summary>
    private RoundOutcome ResolveDamageRound(List<Fighter> contenders, RoundResult result)
    {
        if (contenders.Count < 2)
        {
            return FinishMatch(contenders.FirstOrDefault(), null);
        }

        // Roles alternate each round based on seed order.
        var attacker = contenders[(CurrentRound - 1) % contenders.Count];
        var defender = contenders[CurrentRound % contenders.Count];

        result.AttackerId = attacker.Id;
        result.DefenderId = defender.Id;

        var attackRoll = result.Rolls[attacker.Id];
        var defendRoll = result.Rolls[defender.Id];

        if (attackRoll > defendRoll)
        {
            var damage = DamageMode == DamageMode.AttackerRoll ? attackRoll : attackRoll - defendRoll;
            attacker.TotalDamage += damage;
            defender.DamageTaken += damage;
            result.DamageDealt[attacker.Id] = damage;
            result.WinnerId = attacker.Id;
            attacker.RoundsWon++;
        }
        else
        {
            result.Blocked = true;
            result.WinnerId = defender.Id;
            defender.RoundsWon++;
        }

        // Fixed-length formats run their full round count, then highest damage wins.
        var totalRounds = Definition.DefaultRounds > 0 ? Definition.DefaultRounds : 8;
        if (CurrentRound >= totalRounds)
        {
            var ranked = contenders.OrderByDescending(f => f.TotalDamage).ThenByDescending(f => f.RoundsWon).ToList();
            var winner = ranked[0];
            var runnerUp = ranked.ElementAtOrDefault(1);

            if (ranked.Count > 1 && ranked[0].TotalDamage == ranked[1].TotalDamage)
            {
                // Dead heat on damage: fall back to rounds won, then to a decision.
                if (ranked[0].RoundsWon == ranked[1].RoundsWon)
                {
                    return new RoundOutcome(false, $"Round limit reached with {ranked[0].Name} and {ranked[1].Name} tied on damage and rounds - resolve with a sudden death roll.");
                }
            }

            return FinishMatch(winner, runnerUp);
        }

        return new RoundOutcome(true, result.Blocked
            ? $"Round {result.RoundNumber}: {defender.Name} blocked {attacker.Name} ({defendRoll} vs {attackRoll})."
            : $"Round {result.RoundNumber}: {attacker.Name} lands {result.DamageDealt[attacker.Id]} on {defender.Name} ({attackRoll} vs {defendRoll}).");
    }

    /// <summary>Gauntlet: both roll, highest takes the round, first to the required wins takes the match.</summary>
    private RoundOutcome ResolveHighestRollRound(List<Fighter> contenders, RoundResult result)
    {
        var highest = contenders.Max(f => result.Rolls[f.Id]);
        var winners = contenders.Where(f => result.Rolls[f.Id] == highest).ToList();

        if (winners.Count > 1)
        {
            foreach (var f in winners)
            {
                PendingRolls.Remove(f.Id);
            }

            return new RoundOutcome(false, $"Tie at {highest} - re-roll to settle round {CurrentRound}.");
        }

        var roundWinner = winners[0];
        result.WinnerId = roundWinner.Id;
        roundWinner.RoundsWon++;

        SeriesWins.TryGetValue(roundWinner.Id, out var wins);
        SeriesWins[roundWinner.Id] = ++wins;

        if (wins >= RoundsToWin)
        {
            var runnerUp = contenders.FirstOrDefault(f => f.Id != roundWinner.Id);
            return FinishMatch(roundWinner, runnerUp);
        }

        return new RoundOutcome(true, $"Round {CurrentRound}: {roundWinner.Name} takes it with {highest} ({wins}/{RoundsToWin}).");
    }

    /// <summary>Iron Crown: 4-round blocks, most damage in a block wins it, best 2 of 3 blocks.</summary>
    private RoundOutcome ResolveDamageWindowRound(List<Fighter> contenders, RoundResult result)
    {
        if (contenders.Count < 2)
        {
            return FinishMatch(contenders.FirstOrDefault(), null);
        }

        var attacker = contenders[(CurrentRound - 1) % contenders.Count];
        var defender = contenders[CurrentRound % contenders.Count];

        result.AttackerId = attacker.Id;
        result.DefenderId = defender.Id;

        var attackRoll = result.Rolls[attacker.Id];
        var defendRoll = result.Rolls[defender.Id];

        if (attackRoll > defendRoll)
        {
            var damage = DamageMode == DamageMode.AttackerRoll ? attackRoll : attackRoll - defendRoll;
            attacker.TotalDamage += damage;
            defender.DamageTaken += damage;
            result.DamageDealt[attacker.Id] = damage;
            result.WinnerId = attacker.Id;
            attacker.RoundsWon++;

            BlockDamage.TryGetValue(attacker.Id, out var running);
            BlockDamage[attacker.Id] = running + damage;
        }
        else
        {
            result.Blocked = true;
            result.WinnerId = defender.Id;
            defender.RoundsWon++;
        }

        var blockComplete = CurrentRound % 4 == 0;
        if (!blockComplete)
        {
            return new RoundOutcome(true, result.Blocked
                ? $"Round {result.RoundNumber}: {defender.Name} blocked {attacker.Name}."
                : $"Round {result.RoundNumber}: {attacker.Name} lands {result.DamageDealt[attacker.Id]}.");
        }

        // A 4-round block just ended - award it to whoever did the most damage in it.
        var blockNumber = CurrentRound / 4;
        var blockWinner = contenders.OrderByDescending(f => BlockDamage.GetValueOrDefault(f.Id)).ToList()[0];
        SeriesWins.TryGetValue(blockWinner.Id, out var blocks);
        SeriesWins[blockWinner.Id] = ++blocks;

        var detail = $"Block {blockNumber} goes to {blockWinner.Name} ({blocks} block win(s)).";
        BlockDamage.Clear();

        if (blocks >= 2)
        {
            var runnerUp = contenders.FirstOrDefault(f => f.Id != blockWinner.Id);
            return FinishMatch(blockWinner, runnerUp, detail);
        }

        var maxRounds = Definition.DefaultRounds > 0 ? Definition.DefaultRounds : 12;
        if (CurrentRound >= maxRounds)
        {
            // No one reached two block wins - total damage settles it.
            var ranked = contenders.OrderByDescending(f => f.TotalDamage).ToList();
            if (ranked.Count > 1 && ranked[0].TotalDamage == ranked[1].TotalDamage)
            {
                return new RoundOutcome(false, $"All {maxRounds} rounds fought and damage is tied - resolve with a sudden death roll.");
            }

            return FinishMatch(ranked[0], ranked.ElementAtOrDefault(1), detail);
        }

        return new RoundOutcome(true, detail);
    }

    // ---------------------------------------------------------------- finishing

    private RoundOutcome FinishMatch(Fighter? winner, Fighter? runnerUp, string? prefix = null)
    {
        if (winner is null)
        {
            return new RoundOutcome(false, "No winner could be determined.");
        }

        WinnerId = winner.Id;
        RunnerUpId = runnerUp?.Id;
        Phase = MatchPhase.Complete;

        var head = prefix is null ? string.Empty : prefix + " ";
        var tail = runnerUp is null ? string.Empty : $" {runnerUp.Name} is the runner-up.";
        return new RoundOutcome(true, $"{head}{winner.Name} wins!", WinnerName: winner.Name, RunnerUpName: runnerUp?.Name).WithTail(tail);
    }

    private Fighter? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        foreach (var f in Fighters)
        {
            if (f.Id == id)
            {
                return f;
            }
        }

        return null;
    }

    /// <summary>Produce a history record for the finished match.</summary>
    public MatchRecord ToRecord(string? beltName = null, string? notes = null) => new()
    {
        Type = Definition.Type,
        TypeName = Definition.Name,
        StartedAt = StartedAt,
        EndedAt = DateTime.Now,
        Fighters = Fighters.Select(f => f.Clone()).ToList(),
        Rounds = Rounds.Select(r => r.Clone()).ToList(),
        WinnerName = Winner?.Name,
        RunnerUpName = RunnerUp?.Name,
        BeltName = beltName,
        Notes = notes,
    };
}

internal static class RoundOutcomeExtensions
{
    /// <summary>Append text to an outcome's message without losing the structured fields.</summary>
    public static RoundOutcome WithTail(this RoundOutcome outcome, string tail)
        => outcome with { Message = outcome.Message + tail };
}
