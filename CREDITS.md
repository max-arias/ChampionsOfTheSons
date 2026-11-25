# Credits

Champions of the Sons is new code and text. Its rules and numbers follow the original Champions of the Forest mod for The Forest; nothing else from it is used.

## Assets

| Asset in the mod | Source | Author | License | Modified |
|---|---|---|---|---|
| Item, perk, spell and set icons (`icons/`) | https://game-icons.net | game-icons.net contributors (per icon: `CREDITS-icons.md`, `tools/icons/mapping.json`) | CC BY 3.0 (a few CC0) | recolored, resized; some item icons combine two glyphs |
| Sound effects (29 clips, embedded in the DLL; source clip per cue in `tools/sounds/build_sounds.py`) | "200 Free RPG Sounds", https://clutch-assets.itch.io/200-free-rpg-sounds | Clutch Assets | free for commercial use with credit; may not be resold or claimed as one's own | trimmed, mono, converted to Ogg Vorbis |
| Ground-drop and held-weapon models: swords, great sword, axes, hammers, bow, spear, halberd, arrows (asset bundle) | Low Poly Weapon Pack, itch.io | Kickin It Studios | free to use and modify in games; not to be resold as 3D models (pack README) | rescaled; flat colours replaced by the worn-steel texture set below on generated UVs |
| Shield drop model (asset bundle) | KayKit Fantasy Weapons Bits 1.0, https://kaylousberg.com | Kay Lousberg | CC0 | rescaled; atlas colours mapped to the worn-steel texture set below on generated UVs |
| Chest, key, coin and flask drop models (armor, materials, jewelry, scrolls, consumables; asset bundle) | Deepvein Mine Kit, https://elliottfhotmailfr.itch.io/deepvein-mine-kit | elliottfhotmailfr (itch.io) | CC0 | textures half desaturated, downsized to 512 px; rescaled |
| Worn-steel weapon textures: metal_plate_02, rusty_metal_04, rough_wood, brown_leather, wood_table_worn (asset bundle; `unity/CotsBundles/Assets/ThirdParty/PolyHaven`) | Poly Haven, https://polyhaven.com | Poly Haven contributors | CC0 | downsized to 512 px, colour graded, ARM maps repacked as HDRP mask maps |
| Spell effects: rings, glyphs, bursts and hits, shield bubble, healing circle, portal, buff aura (asset bundle; sources not in the repo) | Magic Effects FREE 1.6, https://assetstore.unity.com/packages/vfx/particles/spells/magic-effects-free-247933 | Hovl Studio | Standard Unity Asset Store EULA | materials rebuilt on the mod's own shader, colours reduced to brightness so the mod tints them, scripts, lights and distortion layers removed, rescaled |
| Spell effects: bolt, flames, lightning arcs, black-hole pull (asset bundle; sources not in the repo) | Free Quick Effects Vol. 1 1.0, https://assetstore.unity.com/packages/slug/304424 | Gabriel Aguiar Prod. | Standard Unity Asset Store EULA | as above; textures downsized to 512 px |
| Poison gas cloud (asset bundle; sources not in the repo) | Particle Pack, https://assetstore.unity.com/packages/p/particle-pack-127325 | Unity Technologies | Standard Unity Asset Store EULA | as above |
| Black-hole swirl texture, twirl_01 (asset bundle; `unity/CotsBundles/Assets/ThirdParty/Kenney`) | Particle Pack 1.1, https://kenney.nl/assets/particle-pack | Kenney Vleugels | CC0 | - |
| The other spell, ability and loot effects (columns, beams, chains, snow, black-hole core, tier, weapon and loot effects): particle systems, effect textures and the `Cots/Fx` shader (asset bundle) | made for this mod (`unity/CotsBundles/Assets/Editor/CotsFxPrefabs.cs`) | Champions of the Sons | same as the mod | - |

The sound effects are made by Clutch Assets, not by this project.

New third-party assets (models, sounds, textures, fonts) get a row here in the same commit that adds them (COS-6). CC0 assets are listed too.

## Tools

- RedLoader and SonsSdk by ToniMacaroni (https://github.com/ToniMacaroni/RedLoader).
