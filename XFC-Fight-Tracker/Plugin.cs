using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XFCFightTracker.Core;
using XFCFightTracker.Models;
using XFCFightTracker.Windows;

namespace XFCFightTracker;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/xfc";

    public Configuration Configuration { get; }

    public readonly WindowSystem WindowSystem = new("XFCFightTracker");

    private readonly SetupWindow setupWindow;
    private readonly FightWindow fightWindow;
    private readonly HistoryWindow historyWindow;
    private readonly ConfigWindow configWindow;

    /// <summary>The match currently being run, if any.</summary>
    public MatchEngine? Match { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        setupWindow = new SetupWindow(this);
        fightWindow = new FightWindow(this);
        historyWindow = new HistoryWindow(this);
        configWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(setupWindow);
        WindowSystem.AddWindow(fightWindow);
        WindowSystem.AddWindow(historyWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the XFC Fight Tracker. Subcommands: setup, fight, history, config, start, roll <n>, next, status, cancel",
            AllowedInMacros = true,
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleSetupUi;

        if (Configuration.AutoReadRolls)
        {
            ChatGui.ChatMessage += OnChatMessage;
        }

        Log.Information($"XFC Fight Tracker loaded (API {PluginInterface.Manifest.DalamudApiLevel}).");
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleSetupUi;

        WindowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(CommandName);
    }

    // ---------------------------------------------------------------- match control

    /// <summary>Create a match for the given fight type and fighter names.</summary>
    public MatchEngine CreateMatch(FightType type, IEnumerable<string> fighterNames, IEnumerable<(string Name, string Team)>? teams = null)
    {
        var definition = FightCatalog.Get(type);

        var fighters = new List<Fighter>();
        var seed = 0;

        foreach (var name in fighterNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            fighters.Add(new Fighter
            {
                Name = name.Trim(),
                Seed = seed++,
                Lives = definition.DefaultLives,
            });
        }

        if (teams is not null)
        {
            foreach (var (name, team) in teams)
            {
                var f = fighters.FirstOrDefault(x => x.Name == name);
                if (f is not null)
                {
                    f.Team = team;
                }
            }
        }

        Match = new MatchEngine(definition, fighters, Configuration.DamageMode);

        // Remember fighters for next time.
        foreach (var f in fighters)
        {
            if (!Configuration.KnownFighters.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            {
                Configuration.KnownFighters.Add(f.Name);
            }
        }

        Configuration.Save();

        if (Configuration.OpenWindowOnStart)
        {
            fightWindow.IsOpen = true;
        }

        Print($"Match ready: {definition.Name} with {fighters.Count} fighter(s). Use /xfc start when everyone is ready.");
        return Match;
    }

    public void StartMatch()
    {
        if (Match is null)
        {
            PrintError("No match set up. Use /xfc setup first.");
            return;
        }

        Match.Start();
        Print($"{Match.Definition.Name} started. {DescribeExpectedRolls()}");
    }

    public void SubmitRoll(string fighterName, int value)
    {
        if (Match is null)
        {
            PrintError("No match in progress.");
            return;
        }

        var fighter = Match.Fighters.FirstOrDefault(f =>
            f.Name.Equals(fighterName, StringComparison.OrdinalIgnoreCase));

        if (fighter is null)
        {
            PrintError($"{fighterName} is not in this match.");
            return;
        }

        Match.SubmitRoll(fighter.Id, value);
        Print($"{fighter.Name} rolled {value}.");

        if (Configuration.AutoResolveRound && Match.RoundReady())
        {
            ResolveRound();
        }
    }

    public void ResolveRound()
    {
        if (Match is null)
        {
            PrintError("No match in progress.");
            return;
        }

        var outcome = Match.ResolveRound();
        if (!outcome.Success)
        {
            PrintError(outcome.Message);
            return;
        }

        Print(outcome.Message);

        if (Match.IsComplete)
        {
            Print($"WINNER: {Match.Winner?.Name ?? "unknown"}" + (Match.RunnerUp is null ? string.Empty : $" | Runner-up: {Match.RunnerUp.Name}"));
            ArchiveMatch();
        }
        else if (Match.RoundReady())
        {
            Print(DescribeExpectedRolls());
        }
    }

    public void CancelMatch()
    {
        if (Match is null)
        {
            PrintError("No match in progress.");
            return;
        }

        Match = null;
        Print("Match cancelled.");
    }

    private void ArchiveMatch()
    {
        if (Match is null || !Configuration.KeepHistory)
        {
            return;
        }

        Configuration.History.Add(Match.ToRecord());
        if (Configuration.History.Count > Configuration.MaxHistoryEntries)
        {
            Configuration.History.RemoveRange(0, Configuration.History.Count - Configuration.MaxHistoryEntries);
        }

        Configuration.Save();
    }

    private string DescribeExpectedRolls()
    {
        if (Match is null)
        {
            return string.Empty;
        }

        var waiting = Match.FightersForRound().Where(f => !Match.HasRoll(f.Id)).Select(f => f.Name).ToList();
        return waiting.Count == 0
            ? "All rolls in - use /xfc next to resolve the round."
            : $"Waiting on rolls from: {string.Join(", ", waiting)}.";
    }

    // ---------------------------------------------------------------- chat reading

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (Match is null || Match.IsComplete)
        {
            return;
        }

        var text = message.Message.TextValue;
        var localName = PlayerState.CharacterName;

        var roll = RollParser.TryParse(text, localName);
        if (roll is null)
        {
            return;
        }

        var fighter = Match.Fighters.FirstOrDefault(f =>
            f.Name.Equals(roll.PlayerName, StringComparison.OrdinalIgnoreCase));

        if (fighter is null)
        {
            return;
        }

        Match.SubmitRoll(fighter.Id, roll.Value, roll.Max);

        if (Configuration.EchoParsedRolls)
        {
            Print($"Read roll: {fighter.Name} = {roll.Value} ({roll.Max})");
        }

        if (Configuration.AutoResolveRound && Match.RoundReady())
        {
            ResolveRound();
        }
    }

    // ---------------------------------------------------------------- commands

    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

        switch (sub)
        {
            case "":
            case "setup":
                setupWindow.Toggle();
                break;

            case "fight":
                fightWindow.Toggle();
                break;

            case "history":
                historyWindow.Toggle();
                break;

            case "config":
                configWindow.Toggle();
                break;

            case "start":
                StartMatch();
                break;

            case "next":
            case "resolve":
                ResolveRound();
                break;

            case "cancel":
                CancelMatch();
                break;

            case "roll":
                if (parts.Length >= 3 && int.TryParse(parts[^1], out var value))
                {
                    SubmitRoll(string.Join(' ', parts.Skip(1).Take(parts.Length - 2)), value);
                }
                else
                {
                    PrintError("Usage: /xfc roll <fighter name> <value>");
                }

                break;

            case "status":
                PrintStatus();
                break;

            default:
                PrintError($"Unknown subcommand '{sub}'. Try: setup, fight, history, config, start, roll, next, status, cancel");
                break;
        }
    }

    private void PrintStatus()
    {
        if (Match is null)
        {
            Print("No match set up.");
            return;
        }

        Print($"{Match.Definition.Name} | phase: {Match.Phase} | round {Match.CurrentRound} | {Match.Active.Count()} active");
        foreach (var f in Match.Fighters)
        {
            var state = f.IsEliminated ? "OUT" : $"{f.Lives} life";
            Print($"  {f.Name}: {f.TotalDamage} dmg dealt, {f.DamageTaken} taken, {f.RoundsWon} rounds won, {state}");
        }

        Print(DescribeExpectedRolls());
    }

    // ---------------------------------------------------------------- chat output

    public void Print(string message) => ChatGui.Print(message, Configuration.ChatTag, Configuration.ChatTagColor);

    public void PrintError(string message) => ChatGui.PrintError(message, Configuration.ChatTag, Configuration.ChatTagColor);

    public void ToggleSetupUi() => setupWindow.Toggle();

    public void ToggleFightUi() => fightWindow.Toggle();

    public void ToggleHistoryUi() => historyWindow.Toggle();

    public void ToggleConfigUi() => configWindow.Toggle();
}
