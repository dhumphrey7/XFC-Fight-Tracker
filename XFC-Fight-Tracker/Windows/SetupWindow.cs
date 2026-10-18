using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using XFCFightTracker.Core;
using XFCFightTracker.Models;

namespace XFCFightTracker.Windows;

/// <summary>
/// Match setup: choose the fight type, read its rules, and list the brawlers.
/// </summary>
public class SetupWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private FightType selectedType = FightType.BasicRules;
    private readonly List<string> fighterNames = new() { string.Empty, string.Empty };
    private string teamA = "Team A";
    private string teamB = "Team B";
    private string customRulesNote = string.Empty;
    private bool splitTeams;

    public SetupWindow(Plugin plugin)
        : base("XFC Fight Tracker##xfc-setup", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(680, 560);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var definition = FightCatalog.Get(selectedType);

        DrawFightTypePicker(definition);

        ImGui.Separator();

        using (var child = ImRaii.Child("xfc-setup-body", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() - 4), true))
        {
            if (child.Success)
            {
                DrawRules(definition);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                DrawFighterEditor(definition);
            }
        }

        ImGui.Separator();

        var canCreate = fighterNames.Count(n => !string.IsNullOrWhiteSpace(n)) >= 2;

        if (!canCreate)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Create Match", new Vector2(140, 0)))
        {
            CreateMatch();
        }

        if (!canCreate)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("Load from Party", new Vector2(140, 0)))
        {
            LoadFromParty();
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear", new Vector2(80, 0)))
        {
            fighterNames.Clear();
            fighterNames.Add(string.Empty);
            fighterNames.Add(string.Empty);
        }

        if (!canCreate)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1f), "Add at least 2 fighters.");
        }
    }

    private void DrawFightTypePicker(FightTypeDefinition definition)
    {
        ImGui.SetNextItemWidth(320);
        if (ImGui.BeginCombo("Match type", definition.Name))
        {
            foreach (var def in FightCatalog.All)
            {
                var selected = def.Type == selectedType;
                if (ImGui.Selectable(def.Name, selected))
                {
                    selectedType = def.Type;
                    ApplyDefaults(def);
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextDisabled(definition.Summary);
    }

    private void ApplyDefaults(FightTypeDefinition def)
    {
        var needed = def.DefaultFighterCount;
        if (needed > 0)
        {
            while (fighterNames.Count < needed)
            {
                fighterNames.Add(string.Empty);
            }

            while (fighterNames.Count > needed)
            {
                fighterNames.RemoveAt(fighterNames.Count - 1);
            }
        }
    }

    private void DrawRules(FightTypeDefinition definition)
    {
        ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.15f, 1f), definition.Name);
        ImGui.SameLine();
        ImGui.TextDisabled($"({definition.Scoring})");

        foreach (var rule in definition.Rules)
        {
            ImGui.BulletText(rule);
        }

        if (definition.DefaultRounds > 0)
        {
            ImGui.TextDisabled($"Rounds: {definition.DefaultRounds}");
        }

        if (definition.DefaultRoundsToWin > 0)
        {
            ImGui.TextDisabled($"Rounds to win: {definition.DefaultRoundsToWin}");
        }

        if (definition.DefaultLives > 1)
        {
            ImGui.TextDisabled($"Lives per fighter: {definition.DefaultLives}");
        }

        if (definition.Scoring == FightScoring.Custom)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##customrules", "Rules set by the belt holder for the final match", ref customRulesNote, 512);
        }
    }

    private void DrawFighterEditor(FightTypeDefinition definition)
    {
        ImGui.Text("Brawlers");

        var isTag = definition.Type == FightType.TagTeamChampionship;
        if (isTag)
        {
            ImGui.SameLine();
            var split = splitTeams;
            if (ImGui.Checkbox("Assign teams", ref split))
            {
                splitTeams = split;
            }
        }

        var removeAt = -1;

        for (var i = 0; i < fighterNames.Count; i++)
        {
            ImGui.PushID(i);

            ImGui.Text($"{i + 1}.");
            ImGui.SameLine();

            ImGui.SetNextItemWidth(220);
            var name = fighterNames[i];
            if (ImGui.InputTextWithHint("##name", "Fighter name", ref name, 64))
            {
                fighterNames[i] = name;
            }

            if (isTag && splitTeams)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(140);
                var team = i % 2 == 0 ? teamA : teamB;
                if (ImGui.InputTextWithHint("##team", "Team", ref team, 32))
                {
                    if (i % 2 == 0)
                    {
                        teamA = team;
                    }
                    else
                    {
                        teamB = team;
                    }
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("x"))
            {
                removeAt = i;
            }

            ImGui.PopID();
        }

        if (removeAt >= 0 && fighterNames.Count > 2)
        {
            fighterNames.RemoveAt(removeAt);
        }

        if (ImGui.Button("+ Add fighter"))
        {
            fighterNames.Add(string.Empty);
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"{fighterNames.Count(n => !string.IsNullOrWhiteSpace(n))} entered");

        if (plugin.Configuration.KnownFighters.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Known fighters - click to fill the first empty slot");
            foreach (var known in plugin.Configuration.KnownFighters.TakeLast(24))
            {
                if (ImGui.SmallButton(known))
                {
                    var empty = fighterNames.FindIndex(n => string.IsNullOrWhiteSpace(n));
                    if (empty < 0)
                    {
                        fighterNames.Add(known);
                    }
                    else
                    {
                        fighterNames[empty] = known;
                    }
                }

                ImGui.SameLine();
            }

            ImGui.NewLine();
        }
    }

    private void CreateMatch()
    {
        var names = fighterNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        List<(string Name, string Team)>? teams = null;
        if (splitTeams && selectedType == FightType.TagTeamChampionship)
        {
            teams = new List<(string, string)>();
            for (var i = 0; i < names.Count; i++)
            {
                teams.Add((names[i], i % 2 == 0 ? teamA : teamB));
            }
        }

        plugin.CreateMatch(selectedType, names, teams);
    }

    private void LoadFromParty()
    {
        fighterNames.Clear();

        try
        {
            foreach (var member in Plugin.PartyList)
            {
                var name = member.Name.TextValue;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fighterNames.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read party list");
        }

        if (fighterNames.Count == 0)
        {
            var self = Plugin.PlayerState.CharacterName;
            if (!string.IsNullOrWhiteSpace(self))
            {
                fighterNames.Add(self);
            }

            fighterNames.Add(string.Empty);
        }
    }
}
