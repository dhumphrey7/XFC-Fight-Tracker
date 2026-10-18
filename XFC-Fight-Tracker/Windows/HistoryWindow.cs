using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XFCFightTracker.Windows;

/// <summary>
/// Completed match history with clipboard and CSV export.
/// </summary>
public class HistoryWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private int selectedIndex = -1;
    private string filter = string.Empty;

    public HistoryWindow(Plugin plugin)
        : base("XFC Match History##xfc-history")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        Size = new Vector2(700, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var history = plugin.Configuration.History;

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "Filter by fighter or type", ref filter, 64);
        ImGui.SameLine();

        if (ImGui.Button("Copy all as text"))
        {
            CopyToClipboard(BuildTextExport(history));
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy CSV"))
        {
            CopyToClipboard(BuildCsvExport(history));
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear history"))
        {
            history.Clear();
            plugin.Configuration.Save();
            selectedIndex = -1;
        }

        ImGui.Separator();

        if (history.Count == 0)
        {
            ImGui.TextDisabled("No matches recorded yet.");
            return;
        }

        using (var child = ImRaii.Child("xfc-history-list", new Vector2(0, -120), true))
        {
            if (child.Success)
            {
                for (var i = history.Count - 1; i >= 0; i--)
                {
                    var record = history[i];

                    if (!Matches(record))
                    {
                        continue;
                    }

                    ImGui.PushID(i);
                    if (ImGui.Selectable($"{record.Summary()}##sel", selectedIndex == i))
                    {
                        selectedIndex = i;
                    }

                    ImGui.PopID();
                }
            }
        }

        ImGui.Separator();

        if (selectedIndex >= 0 && selectedIndex < history.Count)
        {
            var record = history[selectedIndex];
            ImGui.TextColored(new Vector4(0.8f, 0.2f, 0.15f, 1f), record.TypeName);
            ImGui.SameLine();
            ImGui.TextDisabled($"started {record.StartedAt:yyyy-MM-dd HH:mm}");

            ImGui.Text($"Winner: {record.WinnerName ?? "unknown"}");
            if (record.RunnerUpName is not null)
            {
                ImGui.Text($"Runner-up: {record.RunnerUpName}");
            }

            if (ImGui.TreeNode($"Rounds ({record.Rounds.Count})"))
            {
                foreach (var round in record.Rounds)
                {
                    var rolls = string.Join(", ", round.Rolls.Select(kv =>
                    {
                        var name = record.Fighters.FirstOrDefault(f => f.Id == kv.Key)?.Name ?? kv.Key;
                        return $"{name}={kv.Value}";
                    }));

                    var note = round.Note is null ? string.Empty : $" [{round.Note}]";
                    var blocked = round.Blocked ? " (blocked)" : string.Empty;
                    ImGui.BulletText($"R{round.RoundNumber}: {rolls}{blocked}{note}");
                }

                ImGui.TreePop();
            }
        }
        else
        {
            ImGui.TextDisabled("Select a match to see details.");
        }
    }

    private bool Matches(Models.MatchRecord record)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return record.TypeName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (record.WinnerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
               || record.Fighters.Any(f => f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTextExport(System.Collections.Generic.List<Models.MatchRecord> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("XFC Match History");
        sb.AppendLine(new string('-', 60));

        foreach (var record in history)
        {
            sb.AppendLine(record.Summary());
            foreach (var f in record.Fighters)
            {
                sb.AppendLine($"    {f.Name}: {f.TotalDamage} dmg dealt, {f.DamageTaken} taken, {f.RoundsWon} rounds won");
            }
        }

        return sb.ToString();
    }

    private static string BuildCsvExport(System.Collections.Generic.List<Models.MatchRecord> history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("match_id,date,type,winner,runner_up,belt,fighter,damage_dealt,damage_taken,rounds_won");

        foreach (var record in history)
        {
            foreach (var f in record.Fighters)
            {
                sb.AppendLine(string.Join(',',
                    Csv(record.Id),
                    Csv(record.StartedAt.ToString("yyyy-MM-dd HH:mm")),
                    Csv(record.TypeName),
                    Csv(record.WinnerName),
                    Csv(record.RunnerUpName),
                    Csv(record.BeltName),
                    Csv(f.Name),
                    f.TotalDamage,
                    f.DamageTaken,
                    f.RoundsWon));
            }
        }

        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static void CopyToClipboard(string text)
    {
        ImGui.SetClipboardText(text);
    }
}
