using System;
using System.Collections.Generic;
using System.Linq;
using XFCFightTracker.Core;
using XFCFightTracker.Models;

namespace XFCFightTracker.Tests;

/// <summary>
/// A dependency-free test harness. The match engine holds no Dalamud or UI
/// dependencies, so every fight type's rules can be verified without the game.
/// </summary>
public static class Program
{
    private static int passed;
    private static int failed;
    private static readonly List<string> failures = new();

    public static int Main()
    {
        TestRollParser();
        TestCatalogCoverage();
        TestBasicRules();
        TestBasicRulesTieRefused();
        TestBasicRulesBlocking();
        TestGauntlet();
        TestBattleRoyale();
        TestTagTeam();
        TestIronCrown();
        TestTieHandling();
        TestToRecord();

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"PASSED: {passed}   FAILED: {failed}");

        if (failures.Count > 0)
        {
            Console.WriteLine();
            foreach (var f in failures)
            {
                Console.WriteLine("  FAIL " + f);
            }
        }

        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- helpers

    private static void Check(bool condition, string label)
    {
        if (condition)
        {
            passed++;
            Console.WriteLine($"  ok   {label}");
        }
        else
        {
            failed++;
            failures.Add(label);
            Console.WriteLine($"  FAIL {label}");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
        => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label} (expected {expected}, got {actual})");

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine(name);
    }

    /// <summary>Feed one roll per active fighter for the round.</summary>
    private static void RollAll(MatchEngine engine, params int[] values)
    {
        var fighters = engine.FightersForRound().ToList();
        for (var i = 0; i < fighters.Count && i < values.Length; i++)
        {
            engine.SubmitRoll(fighters[i].Id, values[i]);
        }
    }

    // ---------------------------------------------------------------- tests

    private static void TestRollParser()
    {
        Section("RollParser");

        var r1 = RollParser.TryParse("Kain Highwind rolls a 573. (1-999)", null);
        Check(r1 is not null, "parses 'rolls a N'");
        Equal("Kain Highwind", r1!.PlayerName, "attributed to the roller");
        Equal(573, r1.Value, "roll value");
        Equal(999, r1.Max, "roll max");

        var r2 = RollParser.TryParse("Cecil rolls 42 (out of 1000)", null);
        Check(r2 is not null, "parses 'rolls N (out of M)'");
        Equal(42, r2!.Value, "value from out-of form");
        Equal(1000, r2.Max, "max from out-of form");

        var r3 = RollParser.TryParse("You roll a 128. (1-999)", "David Humphrey");
        Check(r3 is not null, "parses 'You roll'");
        Equal("David Humphrey", r3!.PlayerName, "You roll attributed to local player");
        Equal(128, r3.Value, "You roll value");

        Check(RollParser.TryParse("Kain Highwind rolls their eyes at you", null) is null, "ignores non-numeric 'rolls'");
        Check(RollParser.TryParse("", null) is null, "ignores empty message");
        Check(RollParser.TryParse(null, null) is null, "ignores null message");
        Check(RollParser.TryParse("Hello everyone!", null) is null, "ignores ordinary chat");
    }

    private static void TestCatalogCoverage()
    {
        Section("FightCatalog");

        Equal(10, FightCatalog.All.Count, "all 10 XFC match types present");
        Check(FightCatalog.All.All(d => !string.IsNullOrWhiteSpace(d.Summary)), "every type has a summary");
        Check(FightCatalog.All.All(d => d.Rules.Length > 0), "every type has rules text");

        var basic = FightCatalog.Get(FightType.BasicRules);
        Equal(8, basic.DefaultRounds, "Basic Rules is 8 rounds");
        Equal(FightScoring.Damage, basic.Scoring, "Basic Rules scores on damage");

        var gauntlet = FightCatalog.Get(FightType.Gauntlet);
        Equal(3, gauntlet.DefaultRoundsToWin, "Gauntlet is best of 5 (3 to win)");

        var tag = FightCatalog.Get(FightType.TagTeamChampionship);
        Equal(2, tag.DefaultLives, "Tag Team gives 2 lives per partner");

        var iron = FightCatalog.Get(FightType.IronCrownBelt);
        Equal(12, iron.DefaultRounds, "Iron Crown is up to 12 rounds");
    }

    private static void TestBasicRules()
    {
        Section("Basic Rules - 8 rounds, damage wins");

        var def = FightCatalog.Get(FightType.BasicRules);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();
        Equal(MatchPhase.RoundPhase, engine.Phase, "starts in the round phase");

        // Alpha attacks on odd rounds, Bravo on even ones. Bravo's rolls are
        // deliberately lower so the damage totals differ and the match is decisive.
        var alphasDamage = 0;
        var bravosDamage = 0;

        for (var round = 1; round <= 8; round++)
        {
            var attackerIsAlpha = round % 2 == 1;

            // The attacker always beats the defender, but Alpha hits harder.
            // Rolls are submitted in fighter order (Alpha, then Bravo), so on
            // even rounds Bravo is the attacker and gets the higher roll.
            var outcome = attackerIsAlpha
                ? ResolveWith(engine, 900, 100)
                : ResolveWith(engine, 200, 300);

            Check(outcome.Success, $"round {round} resolves");

            if (attackerIsAlpha)
            {
                alphasDamage += 900;
            }
            else
            {
                bravosDamage += 300;
            }
        }

        Check(engine.IsComplete, "match completes after 8 rounds");
        Equal(8, engine.Rounds.Count, "8 rounds recorded");
        Equal("Alpha", engine.Winner?.Name, "Alpha wins on total damage");
        Equal(alphasDamage, engine.Fighters[0].TotalDamage, "Alpha damage total matches");
        Equal(bravosDamage, engine.Fighters[1].TotalDamage, "Bravo damage total matches");
        Equal("Bravo", engine.RunnerUp?.Name, "Bravo is runner-up");
    }

    private static void TestBasicRulesTieRefused()
    {
        Section("Basic Rules - a dead heat is refused rather than guessed");

        var def = FightCatalog.Get(FightType.BasicRules);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();

        // Perfectly symmetrical: identical damage and identical rounds won.
        for (var round = 1; round <= 8; round++)
        {
            var attackerIsAlpha = round % 2 == 1;
            ResolveWith(engine, attackerIsAlpha ? 900 : 100, attackerIsAlpha ? 100 : 900);
        }

        Check(!engine.IsComplete, "match does not complete on a dead heat");
        Equal(3600, engine.Fighters[0].TotalDamage, "Alpha damage recorded");
        Equal(3600, engine.Fighters[1].TotalDamage, "Bravo damage recorded");
        Equal(4, engine.Fighters[0].RoundsWon, "rounds won are tied 4-4");
    }

    private static void TestBasicRulesBlocking()
    {
        Section("Basic Rules - defender blocks when rolling higher");

        var def = FightCatalog.Get(FightType.BasicRules);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();

        // Round 1: Alpha attacks with 100, Bravo defends with 900 -> blocked, no damage.
        var outcome = ResolveWith(engine, 100, 900);
        Check(outcome.Success, "blocked round resolves");
        Equal(0, engine.Fighters[0].TotalDamage, "attacker deals no damage when blocked");
        Equal(0, engine.Fighters[1].DamageTaken, "defender takes no damage when blocking");
        Check(engine.Rounds[0].Blocked, "round flagged as blocked");
        Equal("Bravo", engine.Fighters.First(f => f.Id == engine.Rounds[0].WinnerId).Name, "defender wins the blocked round");
    }

    private static void TestGauntlet()
    {
        Section("Gauntlet - best of 5, first to 3 rounds");

        var def = FightCatalog.Get(FightType.Gauntlet);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();

        ResolveWith(engine, 900, 100);
        ResolveWith(engine, 900, 100);
        Check(!engine.IsComplete, "not complete at 2 round wins");

        var final = ResolveWith(engine, 900, 100);
        Check(final.Success, "third round win resolves");
        Check(engine.IsComplete, "match completes at 3 round wins");
        var winner = engine.Winner!;
        Equal("Alpha", winner.Name, "Alpha wins the gauntlet 3-0");
        Equal(3, engine.SeriesWins[winner.Id], "3 series wins recorded");
    }

    private static void TestBattleRoyale()
    {
        Section("Battle Royale - lowest roll eliminated, final two fight");

        var def = FightCatalog.Get(FightType.BattleRoyale);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
            new Fighter { Name = "Charlie" },
            new Fighter { Name = "Delta" },
        });

        engine.Start();
        Equal(MatchPhase.Elimination, engine.Phase, "starts in the elimination phase");

        // Lowest roll goes out each pass.
        RollAll(engine, 100, 500, 600, 700);
        var o1 = engine.ResolveRound();
        Check(o1.Success, "first elimination resolves");
        Equal("Alpha", o1.EliminatedName, "Alpha eliminated with the lowest roll");
        Equal(3, engine.Active.Count(), "3 fighters remain");

        RollAll(engine, 200, 800, 900);
        var o2 = engine.ResolveRound();
        Check(o2.Success, "second elimination resolves");
        Equal("Bravo", o2.EliminatedName, "Bravo eliminated second");

        Check(engine.Phase == MatchPhase.RoundPhase, "final two move to the round phase");
        Equal(2, engine.Active.Count(), "exactly 2 fighters remain");
        Equal(1, engine.CurrentRound, "round counter resets for the final");

        // Final is a standard 8-round fight; Bravo/Charlie damage was reset.
        Check(engine.Active.All(f => f.TotalDamage == 0), "damage resets for the final fight");
    }

    private static void TestTagTeam()
    {
        Section("Tag Team - 2 lives per partner, 4 per team");

        var def = FightCatalog.Get(FightType.TagTeamChampionship);

        // Two teams only: after the first elimination two teams remain, so the
        // match should move straight to the 6-round final. Verify the life maths first.
        var duo = new MatchEngine(def, new[]
        {
            new Fighter { Name = "A1", Team = "A" },
            new Fighter { Name = "B1", Team = "B" },
            new Fighter { Name = "A2", Team = "A" },
            new Fighter { Name = "B2", Team = "B" },
        });

        duo.Start();
        Equal(2, duo.Fighters[0].Lives, "A1 starts with 2 lives");

        RollAll(duo, 10, 500, 600, 700);
        var o1 = duo.ResolveRound();
        Check(o1.Success, "life-loss round resolves");
        Equal(1, duo.Fighters[0].Lives, "A1 down to 1 life");
        Check(!duo.Fighters[0].IsEliminated, "A1 still in the match");
        Check(duo.Phase == MatchPhase.RoundPhase, "two teams remain, so the final fight begins");

        // With more teams still alive, the elimination phase continues and lives drain.
        var many = new MatchEngine(def, new[]
        {
            new Fighter { Name = "A1", Team = "A" },
            new Fighter { Name = "B1", Team = "B" },
            new Fighter { Name = "C1", Team = "C" },
            new Fighter { Name = "A2", Team = "A" },
            new Fighter { Name = "B2", Team = "B" },
            new Fighter { Name = "C2", Team = "C" },
        });

        many.Start();
        Equal(MatchPhase.Elimination, many.Phase, "starts in the elimination phase");

        // A1 goes out on the lowest roll twice in a row: first loses a life, then is eliminated.
        RollAll(many, 10, 500, 600, 700, 800, 900);
        many.ResolveRound();
        Equal(1, many.Fighters[0].Lives, "A1 down to 1 life");
        Check(!many.Fighters[0].IsEliminated, "A1 still in the match after one life loss");
        Equal(6, many.Active.Count(), "all 6 fighters still active");

        RollAll(many, 10, 500, 600, 700, 800, 900);
        many.ResolveRound();
        Equal(0, many.Fighters[0].Lives, "A1 out of lives");
        Check(many.Fighters[0].IsEliminated, "A1 eliminated");
        Equal(5, many.Active.Count(), "5 fighters remain");

        var teamA = many.Fighters.Where(f => f.Team == "A").ToList();
        Equal(2, teamA.Sum(f => f.Lives), "team A has 2 lives left (A2 only)");
        Check(teamA.Count(f => !f.IsEliminated) == 1, "one team A partner remains");
    }

    private static void TestIronCrown()
    {
        Section("Iron Crown - 4-round blocks, best 2 of 3 blocks");

        var def = FightCatalog.Get(FightType.IronCrownBelt);
        var engine = new MatchEngine(def, new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();

        // Alpha attacks on odd rounds and lands big damage on rounds 1 and 3.
        // Block 1 = rounds 1-4.
        for (var round = 1; round <= 4; round++)
        {
            var attackerIsAlpha = round % 2 == 1;
            var outcome = attackerIsAlpha
                ? ResolveWith(engine, 900, 100)
                : ResolveWith(engine, 100, 900);

            Check(outcome.Success, $"round {round} resolves");
        }

        Check(!engine.IsComplete, "not complete after block 1");
        Equal(1, engine.SeriesWins.Values.Sum(), "exactly one block awarded");

        // Block 2 = rounds 5-8, Alpha again.
        for (var round = 5; round <= 8; round++)
        {
            var attackerIsAlpha = round % 2 == 1;
            ResolveWith(engine, attackerIsAlpha ? 900 : 100, attackerIsAlpha ? 100 : 900);
        }

        Check(engine.IsComplete, "match completes once a fighter takes 2 blocks");
        Equal("Alpha", engine.Winner?.Name, "Alpha takes 2 blocks and wins the belt");
    }

    private static void TestTieHandling()
    {
        Section("Tie handling - re-roll instead of double elimination");

        // Battle Royale: two fighters tie on the lowest roll.
        var br = new MatchEngine(FightCatalog.Get(FightType.BattleRoyale), new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
            new Fighter { Name = "Charlie" },
        });

        br.Start();
        RollAll(br, 100, 100, 700);
        var outcome = br.ResolveRound();

        Check(!outcome.Success, "tied elimination is rejected");
        Check(outcome.Message.Contains("Tie", StringComparison.OrdinalIgnoreCase), "tie is reported to the announcer");
        Equal(3, br.Active.Count(), "no fighter eliminated on a tie");
        Check(br.PendingRolls.Count == 1, "only the tied rolls are cleared for re-roll");

        // Gauntlet: both fighters roll the same.
        var g = new MatchEngine(FightCatalog.Get(FightType.Gauntlet), new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        g.Start();
        RollAll(g, 500, 500);
        var gOutcome = g.ResolveRound();

        Check(!gOutcome.Success, "tied gauntlet round is rejected");
        Check(g.SeriesWins.Count == 0, "no round win awarded on a tie");
    }

    private static void TestToRecord()
    {
        Section("Match history record");

        var engine = new MatchEngine(FightCatalog.Get(FightType.Gauntlet), new[]
        {
            new Fighter { Name = "Alpha" },
            new Fighter { Name = "Bravo" },
        });

        engine.Start();
        ResolveWith(engine, 900, 100);
        ResolveWith(engine, 900, 100);
        ResolveWith(engine, 900, 100);

        var record = engine.ToRecord(beltName: "Iron Crown");

        Equal("Gauntlet", record.TypeName, "record keeps the match type name");
        Equal("Alpha", record.WinnerName, "record keeps the winner");
        Equal("Bravo", record.RunnerUpName, "record keeps the runner-up");
        Equal(3, record.Rounds.Count, "record keeps every round");
        Equal("Iron Crown", record.BeltName, "record keeps the belt");
        Check(record.Summary().Contains("Gauntlet"), "summary line is populated");
    }

    // ---------------------------------------------------------------- util

    /// <summary>Submit rolls in fighter order and resolve.</summary>
    private static RoundOutcome ResolveWith(MatchEngine engine, params int[] values)
    {
        var fighters = engine.FightersForRound().ToList();
        for (var i = 0; i < fighters.Count && i < values.Length; i++)
        {
            engine.SubmitRoll(fighters[i].Id, values[i]);
        }

        return engine.ResolveRound();
    }
}
