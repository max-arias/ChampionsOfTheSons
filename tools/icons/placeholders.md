# Placeholder icons

Entries with no fitting game-icons.net glyph get the deliberate magenta/black checker "missing art" icon: set the override
`"<id>": "placeholder"` in `overrides.json`, then `match` and `render --force --only <id>`.

Currently 5 (no scarf glyph in the set; `ribbon` reads as an awareness ribbon and is banned):

- `item.scarf`
- `item.kuldars_s_scarf`
- `item.peasant_s_scarf`
- `item.sword_devil_s_scarf`
- `item.tricksters_scarf`

Weakest non-placeholder matches, to replace when better art exists:

- All bracers and wristguards (18 items): the set's `bracer`/`bracers` glyphs are abstract arm guards.
- Quivers share one `quiver` glyph and differ only by color/glow.
- Ring and amulet families (gold/silver/steel x Brawler/Survivor/Sprinter/Tinker, emerald/diamond pendants) share a glyph per family on purpose.
- Banned as real-world symbols: ribbon, fist, crown-of-thorns, cross, pentagram, all-seeing eye, halo, templar shield, pointed hoods, hammer-sickle.
