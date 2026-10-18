using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using XFCFightTracker.Core;

namespace XFCFightTracker.Windows;

/// <summary>
/// Plugin settings.
/// </summary>
public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base("XFC Fight Tracker Settings###xfc-config")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize;

        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var config = plugin.Configuration;

        var autoRead = config.AutoReadRolls;
        if (ImGui.Checkbox("Read rolls from chat automatically", ref autoRead))
        {
            config.AutoReadRolls = autoRead;
            config.Save();
        }

        ImGui.TextDisabled("Picks up /random results and attributes them to fighters in the match.");

        var echo = config.EchoParsedRolls;
        if (ImGui.Checkbox("Echo parsed rolls to chat", ref echo))
        {
            config.EchoParsedRolls = echo;
            config.Save();
        }

        var autoResolve = config.AutoResolveRound;
        if (ImGui.Checkbox("Auto-resolve the round once all rolls are in", ref autoResolve))
        {
            config.AutoResolveRound = autoResolve;
            config.Save();
        }

        var openOnStart = config.OpenWindowOnStart;
        if (ImGui.Checkbox("Open the live match window when a match is created", ref openOnStart))
        {
            config.OpenWindowOnStart = openOnStart;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Damage mode");
        var mode = config.DamageMode;
        if (ImGui.RadioButton("Attacker's roll is the damage", mode == DamageMode.AttackerRoll))
        {
            config.DamageMode = DamageMode.AttackerRoll;
            config.Save();
        }

        if (ImGui.RadioButton("Damage is the margin over the defender", mode == DamageMode.RollDifference))
        {
            config.DamageMode = DamageMode.RollDifference;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var tag = config.ChatTag;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputText("Chat tag", ref tag, 16))
        {
            config.ChatTag = tag;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var keepHistory = config.KeepHistory;
        if (ImGui.Checkbox("Keep match history", ref keepHistory))
        {
            config.KeepHistory = keepHistory;
            config.Save();
        }

        var maxHistory = config.MaxHistoryEntries;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Max history entries", ref maxHistory))
        {
            config.MaxHistoryEntries = Math.Clamp(maxHistory, 10, 5000);
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"{config.KnownFighters.Count} known fighters, {config.History.Count} matches recorded");
    }
}
