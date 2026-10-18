using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using XFCFightTracker.Core;
using XFCFightTracker.Models;

namespace XFCFightTracker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Announcer tag prefixed to plugin chat output.</summary>
    public string ChatTag { get; set; } = "XFC";

    /// <summary>Colour of the chat tag.</summary>
    public ushort ChatTagColor { get; set; } = 0x21;

    /// <summary>Read /random results out of the chat log automatically.</summary>
    public bool AutoReadRolls { get; set; } = true;

    /// <summary>How landed attacks turn rolls into damage.</summary>
    public DamageMode DamageMode { get; set; } = DamageMode.AttackerRoll;

    /// <summary>Print each parsed roll to chat so the announcer can see what was picked up.</summary>
    public bool EchoParsedRolls { get; set; } = true;

    /// <summary>Auto-resolve a round as soon as every fighter has rolled.</summary>
    public bool AutoResolveRound { get; set; }

    /// <summary>Keep a history of completed matches.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>Maximum matches retained in history.</summary>
    public int MaxHistoryEntries { get; set; } = 200;

    /// <summary>Fighters remembered between matches, so setup is quick at an event.</summary>
    public List<string> KnownFighters { get; set; } = new();

    /// <summary>Completed matches.</summary>
    public List<MatchRecord> History { get; set; } = new();

    /// <summary>Show the fight window automatically when a match starts.</summary>
    public bool OpenWindowOnStart { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
