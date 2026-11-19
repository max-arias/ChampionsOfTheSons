# Placeholder icons

Entries with no fitting game-icons.net glyph get the deliberate magenta/black checker "missing art" icon: set the override
`"<id>": "placeholder"` in `overrides.json`, then `match` and `render --force --only <id>`.

Currently none: every entry has a glyph that reads as the thing named. Weakest matches, to replace when better art exists:

- Scarves (`item.scarf`, `item.itchy_red_scarf`, `item.horned_grin_scarf`, `item.patched_wool_scarf`, `item.tricksters_scarf`): generic `ribbon`; the set has no scarf.
- All bracers and wristguards (18 items): the set's `bracer`/`bracers` glyphs are abstract arm guards.
- Quivers share one `quiver` glyph and differ only by color/glow.
- Ring and amulet families (gold/silver/steel x Brawler/Survivor/Sprinter/Tinker, emerald/diamond pendants) share a glyph per family on purpose.
