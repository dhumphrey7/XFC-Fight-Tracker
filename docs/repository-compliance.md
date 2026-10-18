# Repository compliance

How `pluginmaster.json` is built, and what it must contain to work with Dalamud.

Sources: [Publishing to a Custom Repository](https://dalamud.dev/plugin-publishing/custom-repositories/)
and [Setting Plugin Metadata](https://dalamud.dev/plugin-development/plugin-metadata).
Format cross-checked against five live repositories: UnknownX7/DalamudPluginRepo,
NightmareXIV/MyDalamudPlugins, xivdev/Penumbra, Ottermandias/Glamourer and
love.puni.sh.

## What a repository file is

A URL pointing at a JSON **array of store entries**, fetched with a plain
`GET` and no authentication. A store entry is the plugin's build manifest plus
store-only keys.

Users add it in-game via **ESC → Dalamud Settings → Experimental → Custom
Plugin Repositories**, then find the plugin in `/xlplugins`.

```
https://raw.githubusercontent.com/dhumphrey7/XFC-Fight-Tracker/master/pluginmaster.json
```

## Keys

### Required

| Key | Value here | Notes |
|---|---|---|
| `Author` | `X-Fight Club` | |
| `Name` | `XFC Fight Tracker` | display name |
| `InternalName` | `XFC-Fight-Tracker` | must match the DLL; permanent |
| `Punchline` | short blurb | one line |
| `Description` | longer text | shown when expanded |
| `AssemblyVersion` | `0.1.0.0` | **4 numeric parts** — this is what update comparison uses |
| `ApplicableVersion` | `any` | game version constraint |
| `DalamudApiLevel` | `15` | integer API level |
| `DownloadLinkInstall` | release `latest.zip` | absolute URL to a `.zip` |
| `DownloadLinkUpdate` | release `latest.zip` | may differ for download tracking |
| `LastUpdate` | unix seconds | a stale value means users never see an update |

### Optional / store-only

| Key | Notes |
|---|---|
| `RepoUrl` | source repo page. **Not** the raw JSON. |
| `Changelog` | shown in the installer |
| `Tags` | array of free-form search tags |
| `CategoryTags` | array from `jobs`, `utility`, `other` only |
| `IconUrl` | absolute PNG URL; omit rather than leaving empty |
| `ImageUrls` | array of preview images; omit empty strings |
| `IsHide` | hide without removing |
| `DownloadCount` | display only |
| `LoadPriority`, `LoadSync`, `LoadRequiredState`, `CanUnloadAsync` | load behaviour |

### Testing keys

Only needed to publish a separate test build. We mirror the release so
opted-in testers get the same artifact:

| Key | Notes |
|---|---|
| `IsTestingExclusive` | `false` — not test-only |
| `TestingAssemblyVersion` | only used when **greater** than the release version |
| `TestingDalamudApiLevel` | |
| `DownloadLinkTesting` | |
| `TestingChangelog` | optional |

## Type coercion (do not "fix" this)

Dalamud parses these files with **Newtonsoft.Json**, which coerces strings to
their target types. Real repositories rely on this — `love.puni.sh` ships
`"DalamudApiLevel": "15"` and `UnknownX7` ships `"LastUpdate": "1696378706"`,
and both work.

`scripts/build-repo.py` therefore accepts numeric strings and only errors on
values that genuinely cannot be read. Treating these as failures would flag
working plugins as broken.

## How ours is generated

`pluginmaster.json` is **generated from the manifest the build actually
produces**, never hand-edited, so it cannot drift from what shipped:

```bash
python3 scripts/build-repo.py \
  --manifest XFC-Fight-Tracker/bin/x64/Release/XFC-Fight-Tracker/XFC-Fight-Tracker.json \
  --out pluginmaster.json \
  --tag v0.1.0.0 \
  --icon-url "https://raw.githubusercontent.com/dhumphrey7/XFC-Fight-Tracker/master/images/icon.png"
```

The `Release` workflow does this on every `v*` tag and commits the result.

Validate at any time, including third-party repositories:

```bash
python3 scripts/build-repo.py --check-only --out pluginmaster.json
python3 scripts/build-repo.py --check-only --strict --out pluginmaster.json
```

Errors break install or update; warnings are convention deviations that still
work. `--strict` promotes warnings to failures, and is what CI runs on our own
file.

## Gotchas worth remembering

- **`AssemblyVersion` drives updates.** It must be 4 numeric parts. A tag like
  `v0.1.0` is rejected by the release workflow because it cannot be turned into
  a valid 4-part version.
- **Bump the version and `LastUpdate`, or users never update.** Regenerating
  refreshes `LastUpdate` automatically.
- **Build output path depends on the entry point.** Building the `.csproj`
  yields `bin/Release/`; building the `.slnx` yields `bin/x64/Release/`. CI uses
  the solution for the canonical `x64` layout.
- **The zip must contain both the DLL and the manifest.** The release workflow
  asserts this before publishing.
- **`InternalName` is fixed at first release.** It is the config directory, the
  log tag, and the installer's identity for the plugin.
