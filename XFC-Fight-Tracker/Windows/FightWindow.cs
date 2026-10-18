using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using XFCFightTracker.Core;

namespace XFCFightTracker.Windows;

/// <summary>
/// The live match view: standings, the round in progress, and manual roll entry.
/// </summary>
public class FightWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Dictionary<string, int> manualRolls = new();
    private string quickName = string.Empty;
    private int quickValue = 500;
    private string lastMessage = string.Empty;

    public FightWindow(Plugin plugin)
        : base("XFC Live Match##xfc-fight", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var match = plugin.Match;
        if (match is null)
        {
            ImGui.TextDisabled("No match set up.");
            if (ImGui.Button("Open setup"))
            {
                plugin.ToggleSetupUi();
            }

            return;
        }

        ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.15f, 1f), match.Definition.Name);
        ImGui.SameLine();
        ImGui.TextDisabled($"| {match.Phase} | round {match.CurrentRound}");

        if (match.IsComplete)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"WINNER: {match.Winner?.Name}");
        }

        DrawControls(match);

        ImGui.Separator();

        var height = -ImGui.GetFrameHeightWithSpacing() * 2 - 8;
        using (var child = ImRaii.Child("xfc-fight-body", new Vector2(0, height), true))
        {
            if (child.Success)
            {
                DrawStandings(match);
                ImGui.Spacing();
                ImGui.Separator();
                DrawRoundEntry(match);
            }
        }

        ImGui.Separator();
        if (!string.IsNullOrEmpty(lastMessage))
        {
            ImGui.TextWrapped(lastMessage);
        }
        else
        {
            ImGui.TextDisabled("Rolls read from chat appear here automatically.");
        }
    }

    private void DrawControls(MatchEngine match)
    {
        if (match.Phase == MatchPhase.Setup)
        {
            if (ImGui.Button("Start Match", new Vector2(120, 0)))
            {
                match.Start();
                lastMessage = $"{match.Definition.Name} started.";
            }
        }
        else if (!match.IsComplete)
        {
            var ready = match.RoundReady();

            if (!ready)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Resolve Round", new Vector2(120, 0)))
            {
                Resolve();
            }

            if (!ready)
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();
            var auto = plugin.Configuration.AutoResolveRound;
            if (ImGui.Checkbox("Auto-resolve", ref auto))
            {
                plugin.Configuration.AutoResolveRound = auto;
                plugin.Configuration.Save();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel Match", new Vector2(110, 0)))
        {
            plugin.CancelMatch();
            lastMessage = "Match cancelled.";
        }

        ImGui.SameLine();
        if (ImGui.Button("History", new Vector2(80, 0)))
        {
            plugin.ToggleHistoryUi();
        }
    }

    private void DrawStandings(MatchEngine match)
    {
        ImGui.Text("Standings");

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;

        if (!ImGui.BeginTable("xfc-standings", 6, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Fighter");
        ImGui.TableSetupColumn("Team");
        ImGui.TableSetupColumn("Dmg dealt");
        ImGui.TableSetupColumn("Taken");
        ImGui.TableSetupColumn("Rounds");
        ImGui.TableSetupColumn("Status");
        ImGui.TableHeadersRow();

        foreach (var f in match.Fighters.OrderByDescending(x => !x.IsEliminated).ThenBy(x => x.Seed))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (f.IsEliminated)
            {
                ImGui.TextDisabled(f.Name);
            }
            else
            {
                ImGui.Text(f.Name);
            }

            ImGui.TableNextColumn();
            ImGui.Text(f.Team ?? "-");

            ImGui.TableNextColumn();
            ImGui.Text(f.TotalDamage.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(f.DamageTaken.ToString());

            ImGui.TableNextColumn();
            ImGui.Text(f.RoundsWon.ToString());

            ImGui.TableNextColumn();
            if (f.IsEliminated)
            {
                ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1f), "OUT");
            }
            else if (f.Lives > 1)
            {
                ImGui.Text($"{f.Lives} lives");
            }
            else if (match.HasRoll(f.Id))
            {
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"rolled {match.PendingRolls[f.Id]}");
            }
            else
            {
                ImGui.TextDisabled("waiting");
            }
        }

        ImGui.EndTable();
    }

    private void DrawRoundEntry(MatchEngine match)
    {
        if (match.IsComplete)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"{match.Winner?.Name} wins the match.");
            if (match.RunnerUp is not null)
            {
                ImGui.TextDisabled($"Runner-up: {match.RunnerUp.Name}");
            }

            return;
        }

        ImGui.Text("Round in progress");

        foreach (var f in match.FightersForRound())
        {
            ImGui.PushID(f.Id);

            ImGui.Text(f.Name);
            ImGui.SameLine(180 * ImGuiHelpers.GlobalScaleSafe());

            var value = match.PendingRolls.GetValueOrDefault(f.Id, 0);
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputInt("##roll", ref value, 1, 10))
            {
                if (value > 0)
                {
                    match.SubmitRoll(f.Id, value);
                    lastMessage = $"{f.Name} rolled {value}.";
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("clear") && match.HasRoll(f.Id))
            {
                match.PendingRolls.Remove(f.Id);
            }

            ImGui.PopID();
        }

        ImGui.Spacing();

        // Quick entry for a fighter not in the list, or to correct a name.
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##quickname", "Fighter name", ref quickName, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("##quickvalue", ref quickValue);
        ImGui.SameLine();
        if (ImGui.Button("Submit roll"))
        {
            var target = match.Fighters.FirstOrDefault(x => x.Name.Equals(quickName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                lastMessage = $"{quickName} is not in this match.";
            }
            else
            {
                match.SubmitRoll(target.Id, quickValue);
                lastMessage = $"{target.Name} rolled {quickValue}.";
            }
        }
    }

    private void Resolve()
    {
        var match = plugin.Match;
        if (match is null)
        {
            return;
        }

        var outcome = match.ResolveRound();
        lastMessage = outcome.Message;
    }
}

internal static class ImGuiHelpers
{
    /// <summary>Global UI scale, defaulting to 1 when not available.</summary>
    public static float GlobalScaleSafe() => Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
}
