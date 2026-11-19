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
| 0-1 Common/Uncommon | Broken, worn, left behind. Resort leftovers, scrap, rags. | Broken Flip-Flops, Old Boots, Worn Bracer |
| 2-3 Magic/Rare | Handmade and field-repaired. Hide, boiled leather, iron, steel, tribal bone. | Sturdy Leather Boots, Bone Shield, Iron Breastplate |
| 4-5 Epic/Legendary | Good steel, tribal heavies' gear, bunker-issue, resort gold, cult work. | Steel Helmet, Gold Brawler Ring, Hushed Cowl |
| 6 Mythic | Printed polymer, titanium, bunker prototypes, trophies from mutants. | Printed Polymer Chassis, Knucklebone Necklace |
| 7 Primal | Wrong and otherworldly: the Cube, gold nobody mined, things that hum or watch. | Cubeglass Bracers, Hand of the Cube, Golden Visage |

## Naming patterns

- **Armor and clothing:** `<material or origin> <piece>`. Steel Helmet, Whiteout Shroud, Hunter's Harness, Fairway Boots. Use "Leggings" (not "Leggins").
- **Weapons:** the real object first, one modifier. Rusty Machete, Fire-Hardened Spear, Rebar Greatsword, Glacier Maul. Nicknames only for top-tier uniques (Grudgeblade, Severer).
- **Shields:** what it was made from. Pot Lid Shield, Riot Door Shield, Bone Shield, Stairwell Shield.
- **Rings, lockets, pendants with a stat:** `<metal or gem> <stat word> <slot>`. Stat words: Brawler (strength), Survivor (vitality), Sprinter (agility), Tinker (intelligence). Steel Brawler Ring, Silver Survivor Locket, Diamond Tinker Pendant.
- **Scrolls (spell items):** notes, pages, tapes, manuals, blueprints. Triage Notes, Burned Cult Pages, Cube Folding Manual.
- **Quivers:** what carries the arrows. Potato Sack, Hollow Log, Humming Quiver.
- **Sets:** a two-word evocative name that fits the origin: Blood Oath, Whiteout Regalia, The Hushed, Buried Gold, Bunker Protocol, Back Nine. Pieces share the set word.
- **Materials:** the thing itself, with a state adjective: Uncut / Clear / Shiny Sapphire, then Humming and Cubestruck at the top.
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
- Rarity is read from material and finish, not glow: scrap and rags (common) up through hide, iron and steel, to printed polymer, titanium, gold, and finally seamless black cubeglass with a cold violet or green edge light (Primal).
- Cult items: ash grey, soot, red thread, a closed or stacked eye. Tribal items: bone, ochre paint, sinew. Bunker items: matte grey, serial codes, keycards, printed layers. Resort items: faded luxury, gold plating gone tacky, golf kit.
- Spells and perks: simple symbolic marks scratched or painted on a rough surface, not shiny glyph badges. Cold light, soot and ember, no stars or sparkles.
- No text, brands, flags or real symbols in icons.
