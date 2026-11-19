# Shared glossary (English source terms)

Pick ONE translation per term per language and use it everywhere (names, descriptions, perks, sets). Spell names (see the `spell.*.name` lines in chunk01) are referenced inside many item/perk/set strings, so a spell must read the same wherever it appears. A term that is a stat or a spell name in `chunk01`/`chunk02` is the glossary entry for that term.

## Rarity tiers (ascending)
Common, Uncommon, Magic, Rare, Epic, Legendary, Mythic, Primal.
"Legendary" appears in item text ("an item of Legendary rarity or lower"). Use the usual loot-game word for each tier in your language (as in a Diablo/WoW/PoE-style localization).

## Difficulty tiers (not in the string table yet, for reference/consistency if they appear)
Easy, Veteran, Elite, Master, Challenge I..VI, Hell.

## Core stats
Strength, Agility, Vitality, Intelligence (abbreviations STR / AGI / VIT / INT appear in a few strings, e.g. "STR/5": keep the Latin abbreviations exactly as is, or use the language's usual short form only if all four are changed together and the digits stay the same; keeping them is simplest and safest).
Armor, Armor piercing, Critical hit (Crit) chance/damage, Headshot, Thorns, Dodge, Block, Damage Reduction, Resistance to magic, Magic Find, Experience, Life/Health (HP), Energy (the mana-like resource spells cost), Stamina, Cooldown (Reduction), Movement speed, Attack speed.
"Life" and "health" mean the same hit-point resource: use one word for both unless the language demands otherwise.
"Energy" is the spell resource (mana): do NOT translate it as physical "power/electricity" if the language has a better word for a magic/energy pool; "stamina" is the separate physical resource.

## Status effects and mechanics
Bleed, Slow, Weaken, Stun, Ignite/burn, Lifesteal, Knockback, Parry, Stack(s) (a buff counter: "per stack", "up to 5 stacks"), Socket / Empty socket (gem slot in gear), Reforge/Reroll/Upgrade, Magic Find, Massacre (a kill-streak mode), Frenzy (a spell AND its stacks), Berserk (a spell and a set effect), Purge (a spell), Shield (barrier), Projectile, Volley, Spear / Thrown spear, Greatbow, Greatsword, Quiver, Locket, Bracers.

## Tone
Dark survival-horror with RPG swagger: terse, dry, a little sardonic in flavor/lore lines (like a veteran loot-game narrator who has seen too much). Mechanical lines (`effect`, `unique`, `bonus.N`, `description` of spells and stats) are precise and plain; flavor/lore lines may have wit. Never add explanation, never soften the humor into blandness, never make it cute.

## Proper names (keep as-is, do not translate or transliterate unless the language's writing system forces it: for Chinese/Japanese/Korean/Russian, transliterate phonetically)
Kelvin, Virginia (characters of Sons of the Forest). Other proper-noun-ish item names (e.g. "Aezyn", "Alexander's Shield", "Yuki-Onna's Regalia", "Yorium's Vow") are flavorful names: translate the common-word parts meaningfully, keep invented names phonetic/as-is, and treat possessives naturally in the target language.

## Do not change
Numbers (including thousand separators and decimal points exactly as written, e.g. "1,220", "7.5%", "x0.4"), `%` signs, `x` multipliers ("x1.5", "75x"), signs (+/-), units written as symbols or abbreviations ("m", "s", "AGI/5"), ordinals written with digits ("300th": keep the digits "300"; adapt the suffix to the language).
