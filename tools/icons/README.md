# Icons

`content/icons/<id>.png` (128x128 RGBA, transparent) for every item, perk, spell and set: 426 files, about 4 MB.
They ship to `Mods\ChampionsOfTheSons\icons\` (CopyToGame target in the mod csproj) and load through `Icons/IconCache.cs`
(`GetTexture(id)` / `GetSprite(id)`, null when missing).

## Source

AI generation (local ComfyUI, Z-Image Turbo) was dropped. Icons are recolored **game-icons.net** vectors
(CC BY 3.0, 4,200+ single-color SVGs, https://github.com/game-icons/icons). Credits: `content/icons/CREDITS.md`
(keep it with the icons; the mod must show "Icons made by ... from game-icons.net" somewhere, e.g. a credits screen).

- `mapping.json`: entry id -> `author/icon` (generated, committed).
- No two items share an icon (COS-162; `match` asserts it). Each item takes its hand pick or best glyph while it is still free (Legendaries choose first, then Rare, Magic, Common); when its type has none left it gets the type's glyph with a small badge from its name in a disc at the bottom right (`THEME` in `generate_icons.py`; mapping value `<base>+<badge>`, also allowed in `overrides.json`). Perks, spells and sets keep the icon already in `mapping.json` unless overridden, and are spread over many glyphs.
- `placeholders.md`: entries using the magenta "missing art" icon (none right now) and the weakest matches.
- `overrides.json`: hand picks (`id -> icon slug`), win over the automatic match. Edit this to fix a bad icon.
- Matching: keywords from the entry name and type (e.g. a weapon must match a weapon icon), synonyms, a penalty for reusing
  an icon, deterministic tie-break. Items must match their type. A few icons are banned (`BANNED`).
- Look: white glyph -> vertical gradient in the rarity color (Common grey, Magic blue, Rare yellow, Legendary red, the mod's UI colours),
  dark outline, glow growing with rarity. Perks/spells/sets get a category color and a dark disc.
  Rendered at 512 px, downscaled to 128, palette-quantized (256 colors) to keep the files small.

## Commands

Needs Python 3 with `pillow`, `numpy`, `resvg-py` (`pip install pillow numpy resvg-py`); run with `python -I`.

```powershell
python -I tools/icons/generate_icons.py fetch                       # once: SVG set into tools/icons/cache/ (git-ignored)
python -I tools/icons/generate_icons.py match                       # rebuild mapping.json after content or overrides change
python -I tools/icons/generate_icons.py render                      # new icons only (resumable)
python -I tools/icons/generate_icons.py render --force --only item.broken_flip_flops   # one icon
python -I tools/icons/generate_icons.py render --force              # all (about a minute)
python -I tools/icons/generate_icons.py booktab                     # the guide-book tab face (ui.book_tab.png)
python -I tools/icons/contact_sheet.py "$env:TEMP\cots-icons-sheets"   # per-category sheets with name + source icon
```

`booktab` renders `content/icons/ui.book_tab.png`, the face of our guide-book tab (COS-131): `BOOK_TAB_ICON` in the game's tab-icon layout (black frame, white field, flat black glyph), not the item style.

New content entries: run `match` then `render`. Replace one icon: add `"<id>": "<icon-slug>"` to `overrides.json`, then `match` and `render --force --only <id>`.

## Licenses

game-icons.net icons: CC BY 3.0 (a few contributors CC0); attribution required, changes allowed (recoloring is noted in the credits).
Tools used: Pillow (HPND), NumPy (BSD-3), resvg-py / resvg (MPL-2.0 / MIT-Apache).
