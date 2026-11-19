# Champions of the Sons

RPG overhaul mod for Sons of the Forest (RedLoader 0.8.6, .NET 6, IL2CPP, HDRP, Unity 2022.2.16f1). A rebuild of the mechanics of Champions of the Forest (The Forest) — new code, same rules. Spec and plan: Obsidian ticket COS-1.

## Layout
- `src/ChampionsOfTheSons.Core` — pure .NET 6 library: rules (`Rules/`), content definitions and loading (`Content/`), save model (`Saving/`), net protocol (`Networking/`), seeded RNG. No Unity, Il2Cpp or game references, ever. Everything game-agnostic goes here.
- `src/ChampionsOfTheSons.Mod` — the RedLoader mod: game bridge (Harmony patches), UI (SUI), gamepad layer (`Gamepad/`), spells and effects. Compiles the Core sources in, so the mod ships as one DLL. Content JSON is embedded.
- `content/` — item, stat, perk, spell, affix, buff and difficulty definitions (JSON, stable string IDs). Embedded in the mod, copied next to the tests.
- `tests/ChampionsOfTheSons.Core.Tests` — xUnit tests for the rules core, save model and net protocol only.
- `docs/mechanics.md` — the rules and formulas we implement (taken from the original mod's behavior).

## Build
- `./build.ps1` builds and tests the core. Needs a .NET 10 SDK (PATH, or the local one at `D:\dev\deps\dotnet`; set `DOTNET_ROOT` to it).
- Build the mod with `-p:DeployToGame=false`. Deploy and test in-game only through `tools/run-selftest.ps1 -Headless`: it builds this checkout, deploys, loads test save 1999999999 headless, runs the mod's `--cots-selftest` steps and prints PASS/FAIL, all under a machine-wide lock, because every worktree shares one game folder. Add in-game checks with `SelfTest.Add(...)`.
- `dotnet run --project tools/ContentCheck -- content` validates all content JSON with the mod's own loader.
- The mod build needs RedLoader 0.8.6 installed in the game folder and the game launched once, so `_RedLoader\Game` holds the interop assemblies. Game folder: `C:\Program Files (x86)\Steam\steamapps\common\Sons Of The Forest` (override with `-p:GameDir=`).
- Fast in-game checks: launch options `--sdk.loadintomain` (straight into a world) or `--savegame <id>`; RedLoader log in `_RedLoader\Latest.log`.

## Parallel work conventions
- New features implement `IModFeature` (`src/ChampionsOfTheSons.Mod/Features.cs`) in their own folder; they are discovered automatically. Don't edit `ChampionsOfTheSonsMod.cs` for wiring.
- New English strings go in a fragment `content/text/en.<feature>.json` (keys prefixed `ui.<feature>.`), not in `en.json`; fragments merge into English at load. Don't touch other languages' files.
- Translations: `docs/translations.md` has the workflow (`tools/text_merge_fragments.py`, `text_todo.py`, `text_apply.py`, `check_translations.py`, stale detection). `build.ps1` runs the checker leniently; the branch that adds English text also translates it.
- Self-tests: `SelfTest.Add(delay, name, step)` from `RegisterSelfTests()`, using `TestKit` (spawn, hit, kill, give items, screenshots to `%TEMP%\cots-ui\`).

## Rules
- Core code must not reference Unity or game types; the bridge converts.
- Content IDs are stable strings; never derive IDs from order. Saves must keep loading: bump the save version and add a migration for any format change.
- Host is authoritative for enemies, drops and difficulty. A client computes its own outgoing hits; the host applies defense.
- Content stays fantasy and RPG: nothing political or religious, no real-world people, slogans or brands (names, text, icons).
- Do not copy code or lore/description text from the original Champions of the Forest repo (it has no license). Mechanics and numbers are fine.
- Tests: rules, save migrations and codec round-trips. No tests that restate content values.

## Task tracking
Tasks live in the Obsidian vault, project folder `champions-of-the-sons`, prefix `COS` (`C:\Users\max\Documents\Obsidian Vault\champions-of-the-sons`). See `Ticket System.md` in the vault root for everything else. GitHub Issues and TODO files are not used.
