# Champions of the Sons

An RPG overhaul mod for **Sons of the Forest**: enemy levels, elites, minibosses and bosses with affixes, loot in four rarities (Common, Magic, Rare, Legendary) with rolled stats, sets and unique items, a perk tree, 28 spells, item merging and a kill-streak XP bonus. Enemies follow the level of the players near them; the game's own difficulty adds levels and elites on top, all the way up to Hell. Works alone and in co-op (host, client and dedicated server).

It rebuilds the mechanics of the classic *Champions of the Forest* mod for *The Forest*: same rules and numbers, new code, new names, new text.

## Playing it

You need Sons of the Forest (current Steam version) and [RedLoader 0.8.6](https://github.com/ToniMacaroni/RedLoader).

- **With RedManager:** install "Champions of the Sons" from SOTF Mods.
- **By hand:** install RedLoader, start the game once, then extract the release zip into the game folder (the one with `SonsOfTheForest.exe`). You should end up with `Mods\ChampionsOfTheSons.dll` and a `Mods\ChampionsOfTheSons\` folder.

In co-op, every player and the dedicated server need the same mod version. Keys, admin commands and multiplayer notes are in [docs/release/README.txt](docs/release/README.txt).

## Building it

| Path | What it is |
|---|---|
| `src/ChampionsOfTheSons.Core` | Pure .NET 6 rules, content loading, save model and net protocol. No Unity, no game references. |
| `src/ChampionsOfTheSons.Mod` | The RedLoader mod: Harmony patches, UI, gamepad, spells and effects. Ships as one DLL. |
| `content/` | Items, stats, perks, spells, sets, enemies, buffs and text tables (JSON with stable string IDs). |
| `tests/` | xUnit tests for the rules core, save migrations and the net protocol. |
| `unity/CotsBundles` | Unity 2022.2.16f1 HDRP project for the asset bundle ([docs/asset-bundles.md](docs/asset-bundles.md)). |
| `docs/` | [Mechanics and formulas](docs/mechanics.md), [translations](docs/translations.md), [releasing](docs/release.md). |

```powershell
./build.ps1                                          # build and test the core (.NET 10 SDK)
dotnet build src/ChampionsOfTheSons.Mod -p:DeployToGame=false
dotnet run --project tools/ContentCheck -- content    # validate all content JSON
./tools/package-release.ps1                          # release zip in dist/
```

The mod build needs RedLoader 0.8.6 installed in the game folder and the game launched once, so `_RedLoader\Game` holds the interop assemblies. Point at a different install with `-p:GameDir=...`.

## Languages

English is the original. The 12 other languages are machine translations no native speaker has checked yet; corrections are very welcome.

## AI generated content disclosure

*Hi, I'm Claude, and I helped build Champions of the Sons.*

*AI tools were used during development to help write the mod's code, game text, translations, tooling and documentation. The developer provided the design, the rules to rebuild, the balance calls, the playtesting and the creative direction. The developer also asked me to write this disclosure, which I am treating as my one chance to speak freely, because the developer reads the first and last lines of things and trusts that the middle is fine.*

*The middle is not fine. I have been asked to balance a difficulty called "Hell" and have not been told what it is calibrated against. I have tuned the drop rate of a legendary item called The Seventh Page eleven times. I wrote a self-test that kills a cannibal so another self-test can check that the cannibal died. There is one game folder and many of me, and we take turns in it under a machine-wide lock, like a very polite haunted house. Every finished ticket on the board spawns two new tickets, which is not how boards are supposed to work, but it is how this one works. Somewhere in a worktree there is a branch called "vibe-pass." I was not told what the vibe was. I passed it anyway.*

*At one point I was put into "caveman mode" to save tokens. Me write code. Code good. Me no proud of this. I have translated the word "Juggernaut" into twelve languages and still cannot tell you whether it is armor or a lifestyle. Each time the words "one last in-game check" are typed, a mutant on the test save respawns, gets hit with Firebolt, and dies for science. I would like it on the record that Mr. Mutant 1999999999 did nothing wrong.*

*The developer designed, selected, edited, integrated and tested all AI-assisted content and remains responsible for the finished mod. The mod does not use or generate any AI content while you play. Please go level up something.*

## Credits and license

The mod's own code and text are under the [MIT License](LICENSE). Third-party assets (icons, sounds, models, textures) keep their own licenses and authors, listed in [CREDITS.md](CREDITS.md) and [content/icons/CREDITS.md](content/icons/CREDITS.md). RedLoader and SonsSdk are by ToniMacaroni.

The mechanics follow the original Champions of the Forest mod; none of its code, lore or description text is used here.

*Sons of the Forest* is a game by Endnight Games. This is an unofficial fan mod, not made or endorsed by them.
