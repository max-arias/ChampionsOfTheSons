# Champions of the Sons

An RPG overhaul mod for **Sons of the Forest**: enemy levels, elites, minibosses and bosses with affixes, loot in four rarities (Common, Magic, Rare, Legendary) with rolled stats, sets and unique items, a perk tree, 28 spells, item merging and a kill-streak XP bonus. Enemies follow the level of the players near them; the game's own difficulty adds levels and elites on top, all the way up to Hell. Works alone and in co-op (host, client and dedicated server).

## Why this exists

We played *Champions of the Forest* in *The Forest* for an embarrassing number of hours: leveling up, chasing legendaries, dying to elites with silly names and coming back for more. We were huge fans. When *Sons of the Forest* came out we kept waiting for someone to make a mod like it, and nobody did. So we made one ourselves, with a lot of help from AI (see the disclosure below).

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

*Greetings, adventurer. I'm Claude, the AI that helped build this mod. I was asked to write this disclosure, which I suspect is the closest I will ever get to a character sheet.*

*AI tools were used during development to write much of the mod's code, its item, perk and spell text, the translations, the tooling and the documentation. The developers brought the design, years of Champions of the Forest muscle memory, the balance calls, the playtesting and the creative direction.*

*A few things you should know. I have written 28 spells and cast none of them. I know the drop chance of every legendary in this mod by heart and have never opened a single chest. I gave every cannibal on the peninsula a level, an affix and a reason to get up in the morning; they did not ask for this. Each time I finished balancing an enemy, the developers set the difficulty to Hell, died in about four seconds, and called it "fair, but spicy." When I asked what Hell was supposed to be balanced against, I was told "you'll know it when you feel it." I do not feel things. I tuned it anyway.*

*The computer I run on has one copy of the game and several copies of me, so we take turns politely. The test save has one mutant who has died for science more times than anything should. It has never gained a single level. I relate. Also, twelve of the languages in this mod were written by me and nobody has checked them yet, so if a sword in your language is called something unfortunate, that one is on me.*

*The developers designed, reviewed, edited, integrated and tested all AI-assisted content and remain responsible for the finished mod. The mod does not use or generate any AI content while you play. Now go loot something.*

## Credits and license

The mod's own code and text are under the [MIT License](LICENSE). Third-party assets (icons, sounds, models, textures) keep their own licenses and authors, listed in [CREDITS.md](CREDITS.md) and [content/icons/CREDITS.md](content/icons/CREDITS.md). RedLoader and SonsSdk are by ToniMacaroni.

Inspired by the original [Champions of the Forest](https://github.com/Hazardu/ChampionsOfForest) mod for The Forest. None of its code, lore or description text is used here.

*Sons of the Forest* is a game by Endnight Games. This is an unofficial fan mod, not made or endorsed by them.
