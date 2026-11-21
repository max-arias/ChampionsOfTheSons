# Style guide: names, text and visuals

Everything the player reads, and every icon, should feel like Sons of the Forest: a remote snowy island, survival first, an RPG power fantasy layered on top. This guide covers `content/text/en.json` (`item.*`, `set.*`, `perk.*`, `spell.*`) and the icon direction. Mechanics wording stays as it is; see `docs/mechanics.md`.

## Tone

- Grounded, dry, a little dark humor. Understatement beats drama. Short sentences.
- Describe what the thing is made of and where it came from, then one grim or wry line about its last owner. Never explain the joke.
- Concrete nouns: duct tape, sinew, rebar, tarp, hide, bone, keycard, filament. No abstract nouns doing the work ("Power", "Destiny").
- Second person is fine in lore ("you stop noticing the second heartbeat"). No exclamation marks, no puns in names.
- Descriptions (`.description`) say what it looks like, one sentence. Lore (`.lore`) is the story, one or two sentences. `.unique`, `.effect`, `.bonus.N` and spell descriptions are mechanics text: never restyle them, never touch a number.

## Vocabulary

Use: bone, sinew, hide, pelt, rope, wire, rebar, scrap, tarp, duct tape, rust, soot, ash, frost, permafrost, tally marks, keycard, filament, printed, polymer, bunker, resort, clubhouse, fairway, cult, tribe, mutant, cube, cubeglass.
Modern and tactical kit is welcome (visor, harness, chaps, puffer jacket, riot door). The Cube and its tech is the "something wrong": seamless, humming, cold, angles that do not add up.

## Rarity feels like

Rarity shows in materials and craft, then at the top something otherworldly.

| Tier | Feels like | Typical names |
| --- | --- | --- |
| 0 Common | Broken, worn, left behind. Resort leftovers, scrap, rags. | Broken Flip-Flops, Old Boots, Worn Bracer |
| 1 Magic | Handmade and field-repaired. Hide, boiled leather, iron, tribal bone. | Sturdy Leather Boots, Bone Shield |
| 2 Rare | Good steel, tribal heavies' gear, bunker-issue, resort gold, cult work. | Iron Breastplate, Steel Helmet, Hushed Cowl |
| 3 Legendary | Printed polymer, titanium, mutant trophies, and at the top the wrong and otherworldly: the Cube, gold nobody mined, things that hum or watch. Every unique. | Knucklebone Necklace, Cubeglass Bracers, Golden Visage |

Four tiers since COS-109 (Max 2026-11-20); item names written for the old eight tiers stay as they are.

## Classic action-RPG naming

Names follow classic action-RPG conventions (Diablo, Path of Exile) on top of the Sons of the Forest grit above. No name from the original Champions of the Forest mod is kept.

- Short and punchy: 1-3 words, about 26 characters at most. Every item, perk and set name is unique.
- Tiers read at a glance:
  - Common: plain and worn. Rusted Cleaver, Torn Tarp Hood.
  - Magic: material plus piece, or a coined compound. Gloomgrip, Boiled Hide Cuirass.
  - Rare: coined compounds with a strong noun. Ashmaw Visor, Rebar Reaper.
  - Legendary: uniques with proper-noun names, "The X", or one "X of the Y". Grudgeblade, Hollowlight, The Hungering Band.
- Compound coinages (Wolfsbane, Ghostwire) and role possessives (Scavenger's, Cultist's, Greenskeeper's) are welcome. Never real names, brands or the original mod's character names.
- The piece must stay readable (boots, ring, spear) and set pieces keep their set's word.
- Perks are passive-skill titles in the Path of Exile notable style (Iron Reflexes, Cold Resolve); perks tied to a spell keep the spell prefix.

## Naming patterns

- **Armor and clothing:** `<material or origin> <piece>`. Steel Helmet, Whiteout Shroud, Hunter's Harness, Fairway Boots. Use "Leggings" (not "Leggins").
- **Weapons:** the real object first, one modifier. Rusty Machete, Fire-Hardened Spear, Rebar Greatsword, Glacier Maul. Nicknames only for top-tier uniques (Grudgeblade, Severer).
- **Shields:** what it was made from. Pot Lid Shield, Riot Door Shield, Bone Shield, Stairwell Shield.
- **Rings, lockets, pendants with a stat:** `<metal or gem> <stat word> <slot>`. Stat words: Brawler (strength), Survivor (vitality), Sprinter (agility), Tinker (intelligence). Steel Brawler Ring, Silver Survivor Locket, Diamond Tinker Pendant.
- **Scrolls (spell items):** notes, pages, tapes, manuals, blueprints. Triage Notes, Burned Cult Pages, Cube Folding Manual.
- **Quivers:** what carries the arrows. Potato Sack, Hollow Log, Humming Quiver.
- **Sets:** a two-word evocative name that fits the origin: Blood Oath, Whiteout Regalia, The Hushed, Buried Gold, Bunker Protocol, Back Nine. Pieces share the set word.
- **Perks:** plain verb-or-noun phrases a survivor would say. Fortified Ground, Scorched Earth, Lucky Charm. A perk with a spell prefix keeps it (`Focus: Snap Shot`). Flavor is one short line.
- **Spells:** keep the established names; they are referenced by perks and set bonuses.

## Avoid

- Generic high fantasy: "of the Phoenix", "Celestial", "Arcane", "Ethereal", "Divine", "of Fortune/Misfortune" suffix chains, elves, dwarves, kings, knights, dragons, "Enchanted" as a rarity word.
- Names from The Forest or Champions of the Forest: Yorium, Moritz, Kuldar, Smokey, Mr M., Lama Mega and similar. Do not use any real or game character names, even invented-sounding ones, unless it is Kelvin or Virginia, and only in lore.
- Brands, franchises, real people, slogans (no "One Ring", "Hulk", "Demoman").
- Political and religious terms: no churches, rosaries, liturgy, crusaders, angels, gods, sacraments, choirs, congregations. The cult is a doomsday cult; keep it about silence, ash, signals, the Cube.
- Magic-school words for the cube's power ("mana", "mage", "spellbound", "sigil", "rune"). The power is strange tech: marks, scratches, hum, glow.
- Typos in names ("Leggins", "Stabilizators"), foreign-language leftovers, and ids leaking into names.

## Visual direction (icons)

- Flat item on a dark, desaturated, cold background. Slightly gritty, hand-made look: visible stitching, tape, rust, wear and chips. No clean fantasy gleam.
- Palette: grey-blue frost, wet charcoal, rust brown, bone, tarp orange and hazard yellow as accents. Fire is a small warm orange against the cold.
- Rarity is read from material and finish, not glow: scrap and rags (common) up through hide, iron and steel, to printed polymer, titanium, gold, and finally seamless black cubeglass with a cold violet or green edge light (Legendary). Weapons add the rarity glow on top (COS-108).
- Cult items: ash grey, soot, red thread, a closed or stacked eye. Tribal items: bone, ochre paint, sinew. Bunker items: matte grey, serial codes, keycards, printed layers. Resort items: faded luxury, gold plating gone tacky, golf kit.
- Spells and perks: simple symbolic marks scratched or painted on a rough surface, not shiny glyph badges. Cold light, soot and ember, no stars or sparkles.
- No text, brands, flags or real symbols in icons.

## UI (COS-57, COS-59)

- Equipment, bag and character live on the game's own guide book: the real book in hand, our page drawn into its page texture, one mod tab under the game's tab column, and Gear / Bag / Character as sub-tabs in the page's title bar (`src/ChampionsOfTheSons.Mod/Book/`). There is no other inventory screen (Max 2026-11-21): when the book cannot open, the log says why.
- Page look follows the game's guide book, a little darker: cream paper `#D9CFB8`, slot shade `#C9BEA4`, ink `#2A2622`, brown title bar `#5A4636` with paper-colored open tab, red selection `#B8322A`.
- Slots: ruled squares, a rarity tape tab in the corner (muted: `#9A968C`, `#4F7FA6`, `#C9982A`, `#C24A22` for Common, Magic, Rare, Legendary), level top right, `xN` bottom right. Slots never glow; held and dropped weapons do (COS-108).
- Keep clear of the spiral (left ~62 px of the page) and the thumb (bottom-left ~92 px): pagers and buttons sit bottom right.
- Item details and actions go in a dark card beside the book on the screen, never on the page.
- Our tab icon is a flat off-white glyph on a dark square, like the game's tab icons, not the colored item icon.
