# HDRP asset bundles

Meshes, materials and particle effects that code cannot build well ship in one asset bundle, `content/bundles/cots`, embedded in the mod DLL (resource `bundles/cots`). The mod loads it once (`src/ChampionsOfTheSons.Mod/Bundles/CotsBundle.cs`) and looks prefabs up by name. Every caller keeps a primitive fallback, so a build without the bundle still plays.

## Why the exact Unity version

A bundle only loads in a player of the same Unity version, and HDRP materials must be compiled for the game's HDRP package. Sons of the Forest runs Unity **2022.2.16f1** (`UnityPlayer.dll` product version `2022.2.16f1 (d535843d11e1)`), so the bundle project pins that editor (`unity/CotsBundles/ProjectSettings/ProjectVersion.txt`). Unity 6 is installed here too; it cannot build bundles for the game.

## One-time setup

1. Install Unity **2022.2.16f1** with Windows Build Support (IL2CPP is not needed: bundles are data). Unity Hub: Installs → Install Editor → Archive → download archive, or `unityhub://2022.2.16f1/d535843d11e1`. Installing into `C:\Program Files` needs admin.
2. Open `unity/CotsBundles` once in that editor so it resolves packages. Check `Packages/manifest.json`: the HDRP version must be the one the 2022.2.16 editor ships with (pinned to 14.0.7; Package Manager shows the built-in version if that is wrong).

## Build (one command)

```powershell
./tools/build-bundles.ps1
```

It runs the editor in batch mode (`CotsBundleBuilder.BuildFromCommandLine`): generates the placeholder prefabs from code, tags everything under `Assets/Cots` with bundle name `cots`, builds for `StandaloneWindows64` with chunk compression and writes only `content/bundles/cots`. The next mod build embeds it (`ChampionsOfTheSons.Mod.csproj`, conditional `EmbeddedResource`). Log: `%TEMP%\cots-bundle-build.log`.

## Adding an asset

- Put the prefab under `unity/CotsBundles/Assets/Cots/` (models re-authored as HDRP/Lit; particles on HDRP/Unlit or HDRP particle shaders). Use only scripts that exist in the game (Unity built-ins); custom MonoBehaviours do not survive into the game, so motion and tinting are done by the mod.
- Add the prefab name as a constant in `CotsBundle` and call `CotsBundle.Spawn(name, parent)`, keeping a fallback.
- Credit third-party assets in `CREDITS.md` in the same commit (COS-6).

## What uses it

| Prefab | Where | Fallback |
|---|---|---|
| `LootMarker` (faceted crystal, HDRP/Lit, tinted `_BaseColor` by rarity) | ground drops, `Loot/LootVisuals.cs` | primitive cube |
| `LootSparkle` (rising sparks, HDRP/Unlit additive, start color by rarity) | ground drops | none |
