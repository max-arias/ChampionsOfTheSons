# HDRP asset bundles

Meshes, materials and particle effects that code cannot build well ship in one asset bundle, `content/bundles/cots`, embedded in the mod DLL (resource `bundles/cots`). The mod loads it once (`src/ChampionsOfTheSons.Mod/Bundles/CotsBundle.cs`) and looks prefabs up by name. Every caller keeps a primitive fallback, so a build without the bundle still plays.

## Why the exact Unity version

A bundle only loads in a player of the same Unity version, and HDRP materials must be compiled for the game's HDRP package. Sons of the Forest runs Unity **2022.2.16f1** (`UnityPlayer.dll` product version `2022.2.16f1 (d535843d11e1)`), so the bundle project pins that editor (`unity/CotsBundles/ProjectSettings/ProjectVersion.txt`). Unity 6 is installed here too; it cannot build bundles for the game.

## One-time setup

1. Install Unity **2022.2.16f1** with Windows Build Support (IL2CPP is not needed: bundles are data). Unity Hub: Installs → Install Editor → Archive → download archive, or `unityhub://2022.2.16f1/d535843d11e1`. Installing into `C:\Program Files` needs admin.
2. The first build resolves packages and imports everything (~10 min); later builds take about a minute. HDRP is pinned to **14.0.7**, checked on 2026-11-20 against both sides: the 2022.2.16f1 editor's built-in `com.unity.render-pipelines.high-definition/package.json` and the game's `il2cpp_data/Metadata/global-metadata.dat`, which embeds `com.unity.render-pipelines.high-definition@14.0.7` source paths. The editor revision is the game's too (`ProjectVersion.txt`: `2022.2.16f1 (d535843d11e1)`).

## Build (one command)

```powershell
./tools/build-bundles.ps1
```

It runs the editor in batch mode (`CotsBundleBuilder.BuildFromCommandLine`): makes HDRP the active pipeline (`Assets/Settings/CotsHDRP.asset`, both lit shader modes, Direct3D 11 and 12; without an active HDRP asset the Lit variants would be stripped and render pink), regenerates every prefab from code into `Assets/Cots/Generated` (git-ignored), tags only those prefabs with bundle name `cots` (meshes, materials, textures and shaders come along as dependencies), builds for `StandaloneWindows64` with chunk compression and writes only `content/bundles/cots`. The bundle file is committed, so a mod build needs no Unity. The next mod build embeds it (`ChampionsOfTheSons.Mod.csproj`, conditional `EmbeddedResource`). Log: `%TEMP%\cots-bundle-build.log`.

Editor scripts (`unity/CotsBundles/Assets/Editor`): `CotsBundleBuilder` (pipeline and build), `CotsGen` (procedural textures, HDRP/Lit and Cots/Fx materials, meshes), `CotsLootPrefabs` (drop and weapon models, marker, sparkle), `CotsFxPrefabs` (effects), `CotsInspect` (dumps the third-party models' meshes, material slots and bounds to `%TEMP%\cots-inspect.txt`; run it with `-executeMethod CotsInspect.Dump`).

## Adding an asset

- Third-party sources go under `Assets/ThirdParty/<pack>/` with their license note; generate the prefab from them in an editor script (models on HDRP/Lit through `CotsGen.Lit`; particles and effect meshes on `Cots/Fx` through `CotsGen.Fx`). HDRP/Unlit ignores vertex colour, so particles on it cannot fade or be tinted per particle: use `Cots/Fx` (`Assets/Cots/Shaders/CotsFx.shader`: texture x vertex colour x `_TintColor` x `_Intensity`, additive or alpha blend, optional fresnel, UV scroll and pulse, drawn in HDRP's SRPDefaultUnlit pass).
- Use only scripts that exist in the game (Unity built-ins); custom MonoBehaviours do not survive into the game, so motion and tinting are done by the mod. Lights are added by the mod too (`FxKit.AddLight`).
- Prefab names must be unique (the build fails otherwise); the mod spawns by name with `CotsBundle.Spawn(name, parent)` or, for effects, `FxKit.Spawn(name, pos, scale, tint)`, keeping a fallback.
- Credit third-party assets in `CREDITS.md` in the same commit (COS-6).

## What uses it

| Prefab | Where | Fallback |
|---|---|---|
| `Drop_Sword`, `Drop_GreatSword`, `Drop_Axe`, `Drop_Hammer`, `Drop_Bow`, `Drop_Polearm`, `Drop_Shield`, `Drop_Quiver`, `Drop_Scroll` (key), `Drop_Armor` (chest), `Drop_Jewelry` (coin), `Drop_Material` (small chest), `Drop_Consumable` (flask): grey metal, centred on the origin (COS-63) | ground drops, `Loot/LootVisuals.DropPrefabFor` (by weapon model, then item type) | `LootMarker` |
| `LootMarker` (faceted crystal, HDRP/Lit, tinted `_BaseColor` by rarity) | ground drops without a model | primitive cube |
| `LootSparkle` (rising sparks, Cots/Fx additive, start color by rarity) | ground drops | none |
| `Weapon_Sword`, `Weapon_GreatSword`, `Weapon_Axe`, `Weapon_GreatAxe`, `Weapon_Hammer`, `Weapon_GreatHammer`, `Weapon_Spear`, `Weapon_Polearm`, `Weapon_Bow`: real size, origin at the grip end (bow: grip centre), +Y toward the blade or head | custom weapons (COS-93) | - |
| `Fx_Ring`, `Fx_Burst`, `Fx_Orb`, `Fx_Bubble`, `Fx_Dome`, `Fx_Column`, `Fx_BlackHole`, `Fx_Snow`, `Fx_Portal`, `Fx_Beam`, `Fx_Chain`, `Fx_Flame`, `Fx_Glyph`, `Fx_Gas`: white, unit size (radius 1 m; beams, chains and columns 0..1 along +Y), tinted and scaled by the mod (COS-102) | spells (`Spells/SpellFx.cs`), enemy abilities (`AbilityVisuals` in `Abilities/AbilitySupport.cs`), player states (`Visuals/PlayerStateFx.cs`), co-op flashes | the old primitives |

The setting `reduce_heavy_effects` (`Visuals/VisualsFeature.cs`) hides the hand flames and glyphs, halves particle emission and drops non-essential effect lights. Effect lights are capped at 12.
