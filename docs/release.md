# Releasing on sotf-mods.com

Packaging only; publishing is Max's call (COS-29).

## Build the zip

```powershell
./tools/package-release.ps1
```

Runs the core tests, ContentCheck and the strict translation check, builds the mod in Release (no deploy) and writes `dist/ChampionsOfTheSons-<version>.zip` plus its SHA-256 (sotf-mods.com lists a SHA-256 per version and scans uploads with VirusTotal). The version comes from `src/ChampionsOfTheSons.Mod/manifest.json` and must be semver; bump it there before each release.

Zip layout, extracted into the game folder (same as the dev deploy, which is proven in game):

```
Mods/ChampionsOfTheSons.dll
Mods/ChampionsOfTheSons/manifest.json
Mods/ChampionsOfTheSons/icons/*.png
Mods/ChampionsOfTheSons/README.txt          (docs/release/README.txt)
Mods/ChampionsOfTheSons/CREDITS.md
Mods/ChampionsOfTheSons/CREDITS-icons.md
```

The HDRP bundle (COS-5) and the content JSON are embedded in the DLL.

## Manifest

RedLoader's schema (https://raw.githubusercontent.com/ToniMacaroni/RedLoader/main/MetadataSchema.json) requires `id`, `author`, `version`, `description`, `type` and `url`. `platform` stays `Universal` (the dedicated server refuses client mods). Set `url` to the mod's sotf-mods.com page once it exists.

## Before uploading

- Targeted headless self-tests of the features changed since the last release (`tools/run-selftest.ps1 -Headless -Only ...`; the full suite is disabled), one co-op run (`tools/run-coop-test.ps1 -Windowed`).
- Strict translations pass. The 12 translations ship as machine translations, said so on the page and in README.txt (Max, Wave 7); the native review (COS-39) follows after release.
- `CREDITS.md` lists every third-party asset in the build.
- Bump `version` in the manifest.

## Page text (fill in on the upload form)

From sotf-mods.com's rules (https://sotf-mods.com/content-policy, https://sotf-mods.com/developers): the description must say what the mod does, how to install it and what it requires; only RedLoader mods; credit originals; new mods are reviewed before they go public. The upload form itself needs a login and was not checked (fields, image sizes, tags).

- **Name:** Champions of the Sons
- **Short description:** RPG overhaul: enemy levels and affixes, loot with rarities and stats, perks, spells and item merging; enemies scale with the party and the game's difficulty. Co-op and dedicated servers.
- **Description:** the first paragraph of `docs/release/README.txt`, then its Requirements, Install, Multiplayer and Keys sections.
- **Languages:** English, plus 12 machine-translated languages (corrections welcome).
- **Requirements:** RedLoader 0.8.6. Every player and the dedicated server need the mod, same version.
- **Dedicated server:** supported (install into the server's Mods folder).
- **Credits:** Champions of the Forest (mechanics), game-icons.net (icons, CC BY 3.0).
- **Images:** screenshots from a windowed self-test (`%TEMP%\cots-ui\`), after the UI art direction lands (COS-57/59).

One-click install is done by RedManager (https://github.com/ToniMacaroni/RedManager), which reads the published mod through the site's API and extracts the zip into the game folder; nothing extra is needed beyond this layout and correct dependencies (none besides RedLoader).
