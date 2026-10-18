#!/usr/bin/env python3
"""
Generate and validate pluginmaster.json for the XFC Fight Tracker custom
Dalamud repository.

A Dalamud custom repository is a JSON array of "store entries". Each entry is
the plugin's build manifest plus store-only keys (download links, icon, update
timestamp). See:
  https://dalamud.dev/plugin-publishing/custom-repositories/

Usage:
    # generate
    python3 scripts/build-repo.py \
        --manifest XFC-Fight-Tracker/bin/x64/Release/XFC-Fight-Tracker/XFC-Fight-Tracker.json \
        --out pluginmaster.json \
        --tag v0.1.0.0

    # validate any repository file (ours or a third party's)
    python3 scripts/build-repo.py --check-only --out pluginmaster.json
"""

from __future__ import annotations

import argparse
import json
import sys
import time

# Keys that must be present and non-empty for an entry to be installable.
REQUIRED = [
    "Author",
    "Name",
    "Punchline",
    "Description",
    "InternalName",
    "AssemblyVersion",
    "ApplicableVersion",
    "DalamudApiLevel",
    "DownloadLinkInstall",
    "DownloadLinkUpdate",
]

# CategoryTags values the installer actually filters on. Anything else is not
# reachable through the category filters, so it is a warning rather than an error.
VALID_CATEGORY_TAGS = {"jobs", "utility", "other"}


def load_manifest(path: str) -> dict:
    with open(path, encoding="utf-8-sig") as fh:
        return json.load(fh)


def build_store_entry(manifest: dict, repo_url: str, zip_url: str, last_update: int,
                      icon_url: str | None = None) -> dict:
    """Turn a build manifest into a repository store entry."""
    entry = dict(manifest)

    # Store-only keys.
    entry["RepoUrl"] = repo_url
    entry["DownloadLinkInstall"] = zip_url
    entry["DownloadLinkUpdate"] = zip_url
    entry["DownloadLinkTesting"] = zip_url
    entry["LastUpdate"] = last_update

    # Testing keys: mirror the release so opted-in testers get the same build.
    entry["TestingAssemblyVersion"] = manifest["AssemblyVersion"]
    entry["TestingDalamudApiLevel"] = manifest["DalamudApiLevel"]
    entry["IsTestingExclusive"] = False

    entry["IsHide"] = False
    entry["DownloadCount"] = 0

    if icon_url:
        entry["IconUrl"] = icon_url

    return entry


def _as_int(value) -> int | None:
    """
    Dalamud parses these files with Newtonsoft.Json, which happily coerces the
    string "15" into the integer 15. Real-world repositories rely on that, so
    accept both forms rather than flagging working entries.
    """
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, str) and value.strip().lstrip("-").isdigit():
        return int(value)
    return None


def validate(entry: dict) -> tuple[list[str], list[str]]:
    """
    Check one store entry.

    Returns (errors, warnings). Errors mean the entry will not install or update
    correctly. Warnings are convention deviations that still work in practice.
    """
    errors: list[str] = []
    warnings: list[str] = []

    for key in REQUIRED:
        if key not in entry or entry[key] in (None, ""):
            errors.append(f"missing required key: {key}")

    # AssemblyVersion must be a 4-part .NET version (this is what update
    # comparison is done on, so a malformed one breaks updates silently).
    version = str(entry.get("AssemblyVersion", ""))
    parts = version.split(".")
    if version and (len(parts) != 4 or not all(p.isdigit() for p in parts)):
        errors.append(
            f"AssemblyVersion must be 4 numeric parts (e.g. 1.0.0.0), got {version!r}"
        )

    # Download links must be absolute and point at a .zip artifact.
    for key in ("DownloadLinkInstall", "DownloadLinkUpdate"):
        url = str(entry.get(key) or "")
        if not url:
            continue  # already reported as missing
        if not url.startswith(("http://", "https://")):
            errors.append(f"{key} must be an absolute URL, got {url!r}")
        elif not url.endswith(".zip"):
            errors.append(f"{key} should point at a .zip artifact, got {url!r}")

    # LastUpdate drives the "update available" check. A stale or absent value
    # means users never see an update.
    last = _as_int(entry.get("LastUpdate"))
    if last is None:
        warnings.append(
            f"LastUpdate should be a unix timestamp in seconds, got {entry.get('LastUpdate')!r}"
        )
    elif last <= 0:
        warnings.append("LastUpdate is 0; users will not be prompted to update")

    api = _as_int(entry.get("DalamudApiLevel"))
    if api is None:
        errors.append(f"DalamudApiLevel must be an integer, got {entry.get('DalamudApiLevel')!r}")

    # ApplicableVersion is normally 'any'.
    if not entry.get("ApplicableVersion"):
        errors.append("ApplicableVersion must be set (use 'any' to target all game versions)")

    # RepoUrl should point at the source repository, not the raw json.
    repo = str(entry.get("RepoUrl") or "")
    if repo.endswith(".json"):
        warnings.append(
            f"RepoUrl points at the repository JSON rather than the source page: {repo!r}"
        )
    if not repo:
        warnings.append("RepoUrl is not set; the installer's source link will be missing")

    cats = entry.get("CategoryTags") or []
    for cat in cats:
        if str(cat).lower() not in VALID_CATEGORY_TAGS:
            warnings.append(
                f"CategoryTags value {cat!r} is not one of {sorted(VALID_CATEGORY_TAGS)}; "
                "the plugin will not appear under any category filter"
            )

    if "Tags" in entry and not isinstance(entry["Tags"], list):
        warnings.append(f"Tags should be an array, got {type(entry['Tags']).__name__}")

    # An empty icon or image URL renders a broken image in the installer.
    if entry.get("IconUrl") == "":
        warnings.append("IconUrl is present but empty; omit the key instead")
    for url in entry.get("ImageUrls") or []:
        if not url:
            warnings.append("ImageUrls contains an empty string; omit empty entries")

    # Testing keys only matter if a testing build is offered.
    if "TestingAssemblyVersion" in entry:
        tver = str(entry.get("TestingAssemblyVersion") or "")
        tparts = tver.split(".")
        if tver and (len(tparts) != 4 or not all(p.isdigit() for p in tparts)):
            warnings.append(
                f"TestingAssemblyVersion should be 4 numeric parts, got {tver!r}"
            )

    return errors, warnings


def check_file(path: str, strict: bool = False) -> int:
    """Validate an existing repository file. Returns a process exit code."""
    with open(path, encoding="utf-8") as fh:
        entries = json.load(fh)

    if not isinstance(entries, list):
        print(f"FAIL: {path} must be a JSON array of store entries, got {type(entries).__name__}")
        return 1

    total_errors = 0
    total_warnings = 0

    for entry in entries:
        if not isinstance(entry, dict):
            print(f"FAIL: entry is {type(entry).__name__}, expected an object")
            total_errors += 1
            continue

        name = entry.get("Name") or entry.get("InternalName") or "?"
        errors, warnings = validate(entry)

        for problem in errors:
            print(f"ERROR   [{name}] {problem}")
        for problem in warnings:
            print(f"warning [{name}] {problem}")

        total_errors += len(errors)
        total_warnings += len(warnings)

    print(f"\n{len(entries)} entry/entries checked: "
          f"{total_errors} error(s), {total_warnings} warning(s)")

    if total_errors:
        print("Errors must be fixed: these break install or update.")
    if total_warnings and not total_errors:
        print("Warnings are convention deviations that still work.")

    if strict:
        return 1 if (total_errors or total_warnings) else 0
    return 1 if total_errors else 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest",
                        help="path to the manifest produced by DalamudPackager "
                             "(required unless --check-only)")
    parser.add_argument("--out", default="pluginmaster.json",
                        help="path to write (or, with --check-only, read) the repository JSON")
    parser.add_argument("--repo-url",
                        default="https://github.com/dhumphrey7/XFC-Fight-Tracker",
                        help="source repository URL")
    parser.add_argument("--tag", default=None,
                        help="release tag the zip is attached to (e.g. v0.1.0.0)")
    parser.add_argument("--zip-url", default=None,
                        help="explicit download URL; overrides --tag")
    parser.add_argument("--icon-url", default=None, help="optional icon URL")
    parser.add_argument("--last-update", type=int, default=None,
                        help="unix seconds; defaults to now")
    parser.add_argument("--check-only", action="store_true",
                        help="validate the --out file without rewriting it")
    parser.add_argument("--strict", action="store_true",
                        help="with --check-only, treat warnings as failures")
    args = parser.parse_args()

    if args.check_only:
        return check_file(args.out, strict=args.strict)

    if not args.manifest:
        parser.error("--manifest is required unless --check-only is given")

    manifest = load_manifest(args.manifest)

    if args.zip_url:
        zip_url = args.zip_url
    elif args.tag:
        zip_url = f"{args.repo_url}/releases/download/{args.tag}/latest.zip"
    else:
        zip_url = f"{args.repo_url}/releases/latest/download/latest.zip"

    entry = build_store_entry(
        manifest,
        repo_url=args.repo_url,
        zip_url=zip_url,
        last_update=args.last_update or int(time.time()),
        icon_url=args.icon_url,
    )

    errors, warnings = validate(entry)
    for problem in warnings:
        print(f"warning {problem}")
    if errors:
        print("Repository entry is NOT compliant:")
        for problem in errors:
            print(f"  ERROR {problem}")
        return 1

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump([entry], fh, indent=2, ensure_ascii=False)
        fh.write("\n")

    print(f"Wrote {args.out}")
    print(f"  {entry['Name']} ({entry['InternalName']}) v{entry['AssemblyVersion']}")
    print(f"  API level: {entry['DalamudApiLevel']}")
    print(f"  download:  {zip_url}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
