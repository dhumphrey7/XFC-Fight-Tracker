# XFC Fight Tracker

A [Dalamud](https://dalamud.dev) plugin that runs and tracks [X-Fight Club](https://xfightclub.com/fights) matches inside FFXIV.

It knows the club's match types, reads `/random` results straight out of the chat log, and keeps the bookkeeping that is otherwise done by hand in a spreadsheet or on paper.

## What it does

- **Every XFC match type** is built in with its real rules: Basic Rules, Russian Roulette, Battle Royale, Gauntlet, Money in the Bank, Power Bottom Battle Extravaganza, T.N.T Belt, Tag Team Championship, Iron Crown Belt and the XFC World Championship.
- **Rolls are read from chat.** When a brawler types `/random`, the plugin attributes the result to them automatically. Rolls can also be entered by hand when reading chat is not wanted.
- **Damage, eliminations, lives and brackets** are tracked per the rules of the chosen format.
- **Match history** with clipboard and CSV export, so results can be posted to Discord or kept for the record.
- **Ties are never guessed.** A tie that the rules do not resolve asks for a re-roll instead of silently picking a winner.

## Install

This plugin is distributed from its own custom Dalamud repository.

1. In game, open **ESC → Dalamud Settings → Experimental**.
2. Under **Custom Plugin Repositories**, add:

   ```
   https://raw.githubusercontent.com/dhumphrey7/XFC-Fight-Tracker/master/pluginmaster.json
   ```

3. Tick the new entry as **Enabled** and save.
4. Open `/xlplugins`, search for **XFC Fight Tracker**, and install.

Updates arrive through the installer like any other plugin. The repository file
is generated from the build manifest on every release, so it cannot drift from
what actually shipped. See [docs/repository-compliance.md](docs/repository-compliance.md).

## Commands

| Command | What it does |
|---|---|
| `/xfc` | Open the setup window |
| `/xfc setup` | Open the setup window |
| `/xfc fight` | Open the live match window |
| `/xfc start` | Start the match that was set up |
| `/xfc roll <fighter> <value>` | Record a roll by hand |
| `/xfc next` | Resolve the current round |
| `/xfc status` | Print standings to chat |
| `/xfc history` | Open the match history |
| `/xfc config` | Open settings |
| `/xfc cancel` | Abandon the current match |

## Match types and how they resolve

| Match type | Resolution |
|---|---|
| Basic Rules | 8 rounds, roles alternate, attacker must out-roll the defender to land damage. Most damage wins. |
| Russian Roulette | Deathroll circle. Lowest roll is out. |
| Battle Royale | Everyone rolls, lowest is out, repeat until two remain, then an 8-round Basic Rules final. |
| Gauntlet | Best of 5. Both roll, highest takes the round. |
| Money in the Bank | Gauntlet rules, with the cash-in notes from the club ruleset. |
| Power Bottom Extravaganza | Battle royale splits the field into two brackets, each best-of-5, then champion vs champion. |
| T.N.T Belt | Battle royale, then a final whose rules the belt holder sets. |
| Tag Team Championship | 2 lives per partner, 4 per team. Final two teams fight 6 rounds max. |
| Iron Crown Belt | Up to 12 rounds. Most damage across a 4-round block wins it. Best 2 of 3 blocks. |
| XFC World Championship | Monthly battle royale for the qualifier, who then fights the belt holder. |

## Development

Requires the .NET 10 SDK and a Dalamud dev distribution.

```bash
# Fetch the Dalamud libraries the SDK compiles against
mkdir -p ~/.xlcore/dalamud/Hooks/dev
curl -sSL https://goatcorp.github.io/dalamud-distrib/latest.zip -o /tmp/dalamud.zip
unzip -o /tmp/dalamud.zip -d ~/.xlcore/dalamud/Hooks/dev/

# Build
dotnet build XFC-Fight-Tracker/XFC-Fight-Tracker.csproj --configuration Release
```

Point Dalamud at the build output with `/xldev` → Dalamud → Dev Plugin Locations, then load it from the Dev Plugins tab in `/xlplugins`.

### Tests

The match engine (`Core/MatchEngine.cs`), the rules catalog and the roll parser deliberately have **no Dalamud dependency**, so all of the fight rules can be exercised headlessly:

```bash
dotnet run --project XFCFightTracker.Tests/XFCFightTracker.Tests.csproj
```

This covers all ten match types: damage resolution and blocking, best-of series, elimination and life draining, Iron Crown's block scoring, tie rejection, and the roll parser's chat formats.

## Layout

```
XFC-Fight-Tracker/
  Core/
    FightCatalog.cs    Rules and defaults for all ten XFC match types
    MatchEngine.cs     Match state machine - rounds, eliminations, winners
    RollParser.cs      Parses /random output out of the chat log
  Models/
    FightType.cs       Match type, scoring mode and rule definitions
    Fighter.cs         Fighters and tag teams
    RoundResult.cs     Per-round results and match history records
  Windows/
    SetupWindow.cs     Pick the match type, read the rules, list the brawlers
    FightWindow.cs     Live standings and round entry
    HistoryWindow.cs   Completed matches, text and CSV export
    ConfigWindow.cs    Settings
  Plugin.cs            Entry point, commands and chat reading
  Configuration.cs     Persisted settings and history
```

## Notes

- Basic Rules damage defaults to the attacker's roll. Settings can switch it to the margin over the defender instead.
- The plugin never posts to chat on its own beyond the announcer's own tags; it only reads `/random` results and prints what it recorded.
- This is an unofficial community tool and is not affiliated with Square Enix.
