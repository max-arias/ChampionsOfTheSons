# Mechanics

The rules Champions of the Sons implements. Numbers and formulas come from reading the original Champions of the Forest (COTF) source (2026-11-18); the code here is new. `U(a,b)` is a uniform roll, `L` a level, `d` the index 0..10 of an enemy's level band (see Difficulty). **Deviations** from COTF are listed at the end and marked ✱ inline.

## Progression
- XP to next level: `goal(L) = round(0.8L⁴ + L³ + 2L² + 20L + 4e^(L/3.3) + 1.36^(L−5))`; from L 138 the goal is 5e18 (practical cap).
- Gaining XP: `xp += amount × expGain`; while `xp ≥ goal(L)`: `xp −= goal`, level up.
- Level up: +1 mutation (perk) point; STR/AGI/VIT/INT from level = `A(L)` with `A(1)=1, A(x)=A(x−1)+round(√x)` (the default 1 plus `A(L) − 1` from levels: 1 at level 1, as COTF's live level-up path gives).
- Reward items: every 10th level a Clearpulse Heart (respec); levels 25, 45, 65… a Writhing Heart (+1 permanent perk point).
- Using items (bag Use action, COS-72): one unit is consumed. Writhing Heart: `+round(value)` permanent perk points (they count toward the total, so they are spendable at once and survive a respec). Deadspite Heart: `value = 30 × level³ × rarityMult` XP through the normal XP path (XP gain applies). Clearpulse Heart: a respec (kept when nothing is bought). Items with no use effect have no Use.
- Death ✱ (COS-76): the base game's death (no item drops, the game's downed and respawn rules). On top, `xpLost = min(0.5 × xp, 0.1 × goal(L))` of the XP into the current level is lost and pending massacre XP is lost; never a level or perk point. Spell buffs, running spells and debuffs end. Each player's own client watches its own death (host, clients, single player alike).

## Massacre (kill streak)
- On kill: `kills++`, `pending += xp`. Timer: if 0 → `maxMassacreTime` (15 s), else `clamp(timer + 7.5, 1, maxMassacreTime)`.
- When the timer runs out: grant `pending × mult(kills)`; reset.
- `mult`: <3→1, 3→1.1, 4→1.25, 5→1.5, 6–9→1.75, 10–11→2.3, 12–15→3, 16–19→5, 20–24→8.5, 25–29→16, 30–39→22.5, 40–49→35, 50–64→50, 65–74→100, 75–99→250, ≥100→1000.

## Difficulty (COS-130) ✱
No difficulty is picked. Enemy levels follow the players (see Enemies), and the game's own difficulty, which the host reads from the game (`GameSetupManager` mode and, for Custom, its enemy sliders) and sends to clients, adds levels and elites:

| Game difficulty | Level offset | Elite chance |
|---|---|---|
| Peaceful | −2 | 8% |
| Normal, Creative | 0 | 10% |
| Hard | +5 | 15% |
| Hard Survival | +10 | 20% |
| Custom | each of enemy health, damage, armour, aggression: Low −1, Normal 0, High +2.5; sum rounded (all High = +10) | by offset |

Elite chance = `clamp(0.10 + 0.01 × offset, 0.05, 0.25)` (`GameDifficulty`, `Core/Rules/Difficulty.cs`).

COTF's 11 tiers stay as **level bands**: an enemy's band `d` is the highest one whose base level its level reaches (`Difficulties.ForLevel`), and gives the per-band numbers below (armor factor, Shielding cooldown, Meteor and Laser damage, elite ability count, ability gating, loot rarity roll).

| d | Name | Band from level (COTF base level) | Armor factor `⌊d/2⌋+1` | Shielding CD | Meteor dmg | Laser dmg |
|---|---|---|---|---|---|---|
|0|Easy|1–3|1|60|40–49|30–39|
|1|Veteran|10–13|1|55|100–149|85–104|
|2|Elite|20–24|2|50|300–354|221–233|
|3|Master|30–39|2|45|660–699|444–469|
|4|Challenge I|50–59|3|40|3000|4124|
|5|Challenge II|89–91|3|35|9000|15653|
|6|Challenge III|100|4|30|14000|72346|
|7|Challenge IV|130–132|4|25|25000|85932|
|8|Challenge V|160–164|5|20|50000|124636|
|9|Challenge VI|200|5|15|65000|164636|
|10|Hell|300|6|15|75000|224636|

Host multipliers (default 1): drop quantity, drop chance, XP, extra enemy levels (also `extra_enemy_levels` in a dedicated server's config), enemy damage/health/armor/speed; allow elites; loot level policy (highest/average/lowest/closest/host); minimum loot rarity. No day-based scaling.

## Enemies
- Level ✱ (COS-130): `max(1, party + U[−1,1] + kindExtra + gameOffset + extraLevels)`. `party` is the average level of the players within 150 m of the enemy when it is rolled, rounded up (nobody in range: all players; a dedicated server counts only clients; `EnemyLevels.PartyLevel`). kindExtra comes from content per enemy kind (COTF: normal 1, leader 2, pale 2, painted 3, creepy 2, boss-type 6, baby 0). gameOffset is the game difficulty's (table above). The band `d` used below is the enemy's own (`Difficulties.ForLevel(L)`).
- Damage multiplier: `L < 65 ? e^⌊L/6⌋ + L : (L−60)^2.6 × 600`.
- Armor: `⌊U(L^2.43/3 + 1, L^2.43·2/3 + 20) × armorMul⌋ × (⌊d/2⌋+1)`.
- HP: `(√vanillaHP + 45) × L^2.38 × 0.2 × 2^((L−50)/32) × healthMul`.
- Speed: `(0.94 + L/170) × speedMul`.
- Tier: elite if `(U < eliteChance or kind is a boss type or d = Hell) and elites allowed`, eliteChance from the game difficulty ✱ (COTF: 0.1). Elite ability count: 2 at d ≤ 1, `U[3,6]` at d ≥ 2, 10 for boss types. Boss if `n ≥ 6 and U < 0.1` ✱, Miniboss if `n > 4 and U < 0.25`, else Elite. Non-normal tiers: armor ×2; HP ×5 Elite, ×10 Miniboss, ×20 Boss ✱; scale ×1.1/1.15/1.2. Boss adds Boss Steadfast, Miniboss adds Elite Steadfast.
- Abilities: n distinct picks; only one steadfast; Tiny/Gargantuan exclusive; Meteor, Black Hole and Cataclysms need d ≥ 3; Undead only on kinds that allow it.
- Bounty (kill XP): `b = maxHP^0.8 × 0.8 × L^0.25 + 100`; `×n` if n > 1; `× max(1, L/20)`; `× xpMul`. Co-op: each player within 80 m of the kill gets `b × (1 + 0.2 × (n − 1)) / n` for n such players (`Enemies.KillXpShare`).
- Armor reduction: `DR(a) = (max(a,0)/(max(a,0)+500))²`. Physical damage `× dmgTakenMult × (1 − DR(armor − armorReduction))`; magic uses DR/1.5; pure ignores armor. Armor pierce adds to `armorReduction` (capped at armor). COTF's fire ticks also reduce armor; not built here (COS-90).
- Steadfast caps each hit at 6% (Steadfast), 4% (Elite) or 1% (Boss) of max HP.
- Ability numbers (COTF): Gargantuan armor ×3, HP ×2.5, dmg ×2.5, speed ÷1.5; Tiny speed ×1.1, dmg ×1.2; Extra Health HP ×4; Extra Damage dmg ×5; Juggernaut CC-immune, speed ÷1.15; Rain Empowerment while raining dmg ×5, armor ×5, speed ×2; Undead revives once at half HP; Shielding 3 s immunity then tier CD; Basher 1 s stun; Poisonous `√(dmg/10)/10` per s for 6 s; Warp, Chains, Trapper, Blizzard, Radiance, Black Hole, Meteor, Flare, Laser, Arcane/Fire Cataclysm, Sacrifice, Avenger — see COTF spec numbers in the COS-19/COS-20 tickets.
- Enemy damage to player: `damageMult × debuffDmgMult + vanillaDamage − 20`.
- Enemy debuffs are keyed by source: slows keep max duration/min amount per source and multiply across sources; damage-taken keeps max duration/latest amount and multiplies. COTF's summed fire bonus is not built (COS-90). DoTs tick every 1 s for `ceil(duration)` ticks.

## Loot
- Drop on kill if `U ≤ 0.1 × dropChanceMul × magicFind`, or the enemy has any ability.
- Co-op magic find (COS-81, Max 2026-11-20): the `magicFind` used for a kill's drop chance and rarity is the sum of every player's magic find (each player's multiplier, 1 = no bonus; a missing value counts as 1; a dedicated server counts only the clients), so a group of n players without bonuses rolls with `n` (`KillCredit.TeamMagicFind`). Alone it is the player's own. Each player sends theirs to the host every 3 s; the Character page lists every player's level and magic find and the team total.
- Count: `U[2,3]` (+`U[2,4]` with 3+ abilities) (+7 Boss / +3 Miniboss) + `clamp(⌊L/50⌋, 1, 8)`, × drop quantity, rounded.
- Item level = player level by loot level policy; "other" items whose stats do not grow with level (the respec and perk-point hearts) are level 1. The XP heart keeps its level.
- Rarity ✱ (COS-109, Max 2026-11-20): four tiers, Common 0, Magic 1, Rare 2, Legendary 3. The roll is COTF's eight-step climb below (`LootRoller.RollStep`), then folded: steps 0–1 → Common, 2 → Magic, 3–4 → Rare, 5–7 → Legendary (`Rarities.FromLegacy`), so drop pacing is unchanged. Every unique (an item with unique effects, or one of the five hand-coded uniques) is Legendary. Climb: `w = bounty / itemLevel × MF`, `mf = √MF − 1`, fresh U each check, climbing while each step passes:
  - r1: `(w > 20 and U < 0.70 + 0.45mf + 0.075d) or (d > 5 and w > 2000)`
  - r2: `w > 80 and (U < 0.50 + 0.4mf + 0.07d or (w > 2200 and d > 6))`
  - r3: `(w > 180 and U < 0.5 + 0.35mf + 0.05d) or (w > 5000 and d > 7 and U < 0.9)`, kept if `d > 0 or U < 0.05`
  - r4: `(w > 360 and U < 0.5 + 0.22mf + 0.034d) or (d > 8 and U < 0.7)`, kept if `d > 1 or U < 0.02`
  - r5: `w > 720 and U < 0.26 + 0.085mf + 0.02d`, kept if `d > 2 or U < 0.01`
  - r6: `w > 1440 and U < 0.18 + 0.033mf + 0.003d`, kept if `d > 3 or U < 0.05`
  - r7: `w > 5000 and U < 0.04 + 0.01mf`, kept if `d > 4 or U < 0.001`
- Base item: uniform among that rarity's items with `minLevel ≤ level`; none → try one rarity lower.
- ✱ Drops are equipment only (COS-59). The hearts are level-up rewards. There are no materials (COS-145).
- Stat slots: each pool rolls one option uniformly; `null` = slot stays empty; `*` stands for every common stat (COTF IDs 1–18, 21–26, 31, 34–47, 49–57, 59–67) as separate options, so in `[named, *]` each common stat weighs as much as the named one. Every rolled stat stores its slot index (saved, synced). COTF's `rarity` field on a stat is data only: no roll gates on it.
- Value: `v = U(min, max) × level^levelPow`; `v = min(v, cap)`; `× multiplier`; `× rarityMult(r)`; `v = min(v, cap)`. `rarityMult = 0.6, 1.0, 1.8, 4.5` (Common, Magic, Rare, Legendary; COS-109). The multiplier is the slot's (`statMultipliers` in items.json, COTF's per-slot `Multipier`; 30 items, often negative, e.g. heavy weapons roll attack speed ×−4 to ×−1.55) when set for that slot and stat, else the stat's own.
- Carry stats (COTF 1000–1004): while worn, each adds `round(value)` to the game's carry limit of sticks, rocks, ropes, drinks (VodkaBottle, EnergyDrink) or food (Meat, Fish, EnergyBar), through the same pass as the carry perks (`Core/Rules/ItemCarry.cs`).
- Ground drops (COS-96, COTF's rule): a drop lies `source + 2 × tier²` seconds, source enemy 120, thrown from the bag 60, overflow of a full bag 900; tier is the old eight-step rarity each of the four mostly came from (Common 0, Magic 2, Rare 3, Legendary 5), so +0/+8/+18/+50 s. It shrinks away in its last 2 s. Co-op: the host sends each drop's remaining seconds, so clients and late joiners expire it with the host. COTS has no player death drops.
- Inventory: 120 bag slots (COTF: a fixed 8 x 15 grid; its bag never grows with level or stats, checked 2026-11-19, COS-64); equipment: helmet, chest, pants, boots, shoulders, gloves, amulet, bracer, ring ×2, weapon, offhand (shield/quiver/scroll).

## Merge (COS-144, new rule, Max 2026-11-20)
- The only way to change items (no crafting, COS-145). Bag items only, each player's own bag, no host involvement (`Core/Rules/Merge.cs`).
- Rows: 3 Common → 1 Magic, 5 Magic → 1 Rare, 7 Rare → 1 Legendary, all of one item type (boots, ring, chest...; any base of that type); 3 Legendary of any types → 1 random Legendary of any type, never one of the input bases. Uniques and set pieces are ordinary bases of their rarity.
- Result: a base of the next rarity (the item type's, or any Legendary for the Legendary row), uniform among those with `minLevel ≤` the lowest input level; if none qualifies, among those with the lowest `minLevel`. Rolled fresh (`LootRoller.RollItem`) at the lowest input level. Every item type has a base at every rarity (COS-143).
- Book (layout E): Merge, left of Sort, starts merge mode from the selected bag item, which stays picked; items that cannot join dim; the player picks the rest and presses Merge n/N; the result takes the start item's slot with a NEW tag (memory only, cleared on click). With more of the kind left the mode continues from the highest one; Cancel ends it. The screen panel (book off) has a "Merge (n/N)" menu row that merges the item with the highest-level others of its kind.

## Stats and combat
- Player stat kinds: add (`default + Σ`), mult (`default × Π`), multi (`(default + Σ adds) × (defaultMult × Π mults)`), bool. ✱ Our sheet rebuilds from named sources instead of add/subtract pairs.
- Defaults of note: attributes 1, crit chance 0.05, crit damage 0.5 (×1.5), headshot ×6, magic find 1, XP gain 1, massacre 15 s.
- Derived: `MaxHP = (100 + VIT·hpPerVit + maxHealth) × maxHealthMult`; `MaxEnergy = (100 + AGI·enPerAgi + maxEnergy) × maxEnergyMult`; `Melee = allDmg × meleeInc × (1 + STR·meleePerStr)`; `Ranged = allDmg × rangedInc × (1 + AGI·rangedPerAgi)`; `Spell = allDmg × spellInc × (1 + INT·spellPerInt)`.
- Melee hit: `(weaponDmg + meleeFlat) × crit × Melee` (heavy ×3 × heavyMult); then armor pierce = allAP + meleeAP.
- Ranged hit: `(projDmg + rangedFlat) × crit × weaponTypeMult × Ranged × 0.75^i` (i-th multishot copy); headshot `× headshotDamage`.
- Spell hit: `(base + spellFlat × scaling) × Spell`, magic damage.
- Crit: `U < critChance` → `× (1 + critDamage)`.
- On hit: life/energy on hit; bleed `d/20` per s for 10 s; slow 0.5 for 8 s; weaken ×1.2 damage taken for 8 s.
- Incoming physical: dodge if `U > getHitChance`; else `dmg × damageTaken × (1 − DR(armor − lostArmor)) − block` ✱; magic `× magicDamageTaken`.
- Energy: stamina refills toward energy; `energyRecMult = 1 + (1 + INT·enRecPerInt) × allRecovery × staminaPerSecRate`. Health regen per second `(vanilla + 0.25%·maxHP + LPS) × (healthPerSecRate + 1) × allRecovery` ✱.

## Passive abilities and effects in game (this mod's numbers where COTF's are unknown)
- Abilities and debuffs only advance while the enemy is alive and active (COTF bug #43). Active spell-like abilities register an `IAbilityHandler` (`Bridge/EnemyAbilities.cs`).
- Shielding: the first hit while ready is absorbed and raises a 3 s immunity, then it recharges for its level band's cooldown. Undead: the first lethal hit leaves half health. Juggernaut: ignores slow, stun and knockdown. Basher: the player's movement stops for 1 s (unless `stun_immunity`). Poisonous: `hit × √(damageMult/10)/10` damage per second for 6 s on the player. Rain Empowerment: damage ×5, armor ×5, speed ×2 while it rains.
- Avenger: each enemy dying within 30 m adds a stack (max 5, 20 s): +25% damage and +10% speed per stack. Sacrifice: on death, enemies within 25 m heal 30% of max health and deal ×1.5 damage for 15 s. (New rules.)
- Gargantuan/Tiny set the actor's scale; speed is the profile speed times every slow/stun/rain factor, applied to the actor's animator.
- Player on-hit stats: life/energy on hit (× all recovery), chance to slow (×0.5 speed, 8 s), bleed (hit/20 per s, 10 s) and weaken (×1.2 damage taken, 8 s), hammer stun (`i_hammer_stun_amount` chance, `i_hammer_stun_duration` s). DoTs are pure damage (no armor) but meet steadfast caps and shields. Thorns = `(thorns + per-STR·STR + per-VIT·VIT) × thornsDmgMult` back at the enemy that hit you, ignoring `thorns_armor_piercing` of its armor. Fire damage multiplies hits on burning enemies by `1 + fireDamage`. Explosion damage multiplies thrown/explosive hits by `1 + explosionDamage`.
- Energy is the game's stamina bar ✱ (COS-94): max = vanilla × `MaxEnergy/100`; regeneration adds `staminaPerSecond + energyPerSecond` plus the game's own rate × (energy recovery multiplier / 2 − 1). Attack speed and attack cost scale the held melee weapon's data (and ranged fire delay); projectile speed, size and pierce scale the held ranged weapon's bullet data (pierce raises bullet weight up to ×4).
- Unique items (new rules, kept on purpose: Max 2026-11-20, COS-88; see Deviations): Leechband (`item.vampiric_band`) 3% leech; Bloatstone Ring (`item.vampire_ring`) 6% leech and 5% max health per kill; Bloodsack Talisman (`item.lama_mega_s_blood_bag`) 1% max health per second below half health; Bloodsworn Legguards (`item.undying_promise`): a lethal hit leaves 30% health, once per 300 s; Clearpulse Heart: use to respec.
- Dropped for now: rafts. Spell-number stats are read by the spell system (`StatCoverage.Spells`). Unique flags and `weapon_range`: see "Unique item flags" below (COS-42).

## Unique item flags (COS-42, this mod's own numbers; the item text is the spec)
- Implemented (`Core/Rules/UniqueFlags.cs`, `Followups/`): **Scarsworn Blade** (`item.king_qruies`) adds the last physical damage taken as flat damage to melee hits. **Skullbond Pauldrons** (`item.death_pact`): each attack costs 7% of max health (never the last point) and damage is `1 + 0.05 × missing health percent`. **Ceaseless Trousers** (`item.hexed_pants_of_mr_m`): ×1.4 damage and ×1.4 stamina regeneration while moving; after 1 s standing still, 1% of max health per second drains (never lethal). **Wreckwing Cuirass** (`item.wind_armor`): a dodge heals 5% of max health and gives ×1.2 movement, ×1.35 damage and +2000 armor for 5 s. **Infinity Loop** (Everturn Pendant `item.pendant_of_perpetual_rebirth`, Voidsplit Pendant `item.destroyed_void_shard`): each melee or ranged hit takes 1 s off one random spell cooldown that is running.
- Spell items (after COS-26): **The Hung Quiver** (`item.crossfire`): a projectile hit fires a free Magic Arrow at the target, at most once per 2 s x the cooldown stat. **Steadyhold Bracers** (`item.grip_of_sora`): Multishot cast and upkeep cost x0.8; each volley fires 4 extra projectiles at the next nearest enemies (left-over ones hit the main target). **Ashthorn Circlet** (`item.withered_crown`): one Blood Infused Arrow cast (one health cost) fires 5 arrows in a fan. **Gildspike Boots** (`item.greed`): Wide Reach casts itself once a second, free. **The Last Filament** (`item.the_spark_of_light_in_the_darkness`): a Black Hole that hit 5 or more different enemies releases a Ball Lightning from its center when it ends.
- **Eruption Bow** (`i_eruption_bow`, COS-88): a ranged headshot splashes 50% of its damage on other enemies within 4 m.
- Dropped, with reasons (`StatCoverage.Dropped`): `i_archangel_bow` (needs a second player: co-op, COS-13), `i_great_bow_ignites` and `fire_duration` / `fire_tick_rate` (a test enemy could not be ignited through the game's burn API; unverifiable headless), `i_smokey_crossbow_quiver` (crossbow shots can be told apart now, COS-86, but the bolt explosion has no verified hook), `smash_damage` (no verified heavy-attack signal; `heavy_attack_dmg` is unused for the same reason), `weapon_range` (would need collider or weapon-scale changes), `spell_snowstorm_max_charge` (Snow Storm has no charges yet, COS-83), `debuff_resistance` (COS-89), `perk_golden_resolve` (rule not defined yet).

## Event and utility perks (COS-49)
- Improved Dodges: a dodge heals 5% of max health. Deployable Shield: a dodge grants a shield of 15% of max health for 8 s (Bulwark Reflex: 25%); it soaks damage before health. (New rules.)
- Adrenaline Rush: when a hit leaves health at or below 35%, gain ×1.25 movement and attack speed for 6 s, once per 60 s; Adrenaline Rush II makes it ×1.5 for 10 s. (New rules.)
- Resource Cost Reduction: spell cost ×0.91 per stack (data effect). Quick Silver: Blink's base cooldown −6 s before cooldown multipliers.
- Utility perks follow the perk texts. Carry-limit perks add to the game's own per-item limit (`ItemData.MaxAmount`), per stack; daily-grant perks give items at each new in-game day, never beyond the carry limit. Items are the game's own item names (`Core/Rules/PerkEffects.cs`): bombs = TimeBomb, dynamite = C4Brick, meds = Pills, booze = VodkaBottle, snacks = EnergyBar and EnergyDrink, meat = Meat and Fish. Not done: Fuel (the game has no fuel-can item), head bombs, crafting furs into cloth (Furs to Fabric), the ammo crafting recipes (More Ammo) and the Rescue Axe recipe (Craftable Plane Axe); the recipes need recipe injection.
- Spell-event perks (endless_stream, storm_season, endless_storm, wrath_of_the_sun, sudden_rampage, time_of_need, golden_cooldown, falter, enchant_weapon, afterburn, netherflame) are hooked to the spells (COS-49 Log).

## Active enemy abilities (COS-20; this mod's numbers except where noted)
Host only; each runs only while the enemy is alive and active and is removed the moment it dies, goes dormant or despawns. Rules are in `Core/Rules/ActiveAbilities.cs`; damage is magic (the player's `magic_damage_taken` applies), times the enemy's damage multipliers (rain, Avenger, Sacrifice) and the host's enemy-damage rule. First cast 2-6 s after the enemy becomes active; the cooldown restarts when a cast ends. A cast needs the nearest player inside the range band. `D` = mean of the enemy's level band's Meteor damage.
| Ability | Cooldown | Range | Telegraph | Effect |
|---|---|---|---|---|
| Meteor | 14 s | 0-45 m | 2 s ring (r 4 m) on the player's spot | Impact damage = the band's Meteor roll (Difficulty table); `perk_meteor_damage_taken` applies |
| Laser | 10 s | 3-35 m | 1.5 s thin line tracking the player | 1 s beam locked on where the player was, half-width 0.8 m, hits every 0.5 s for the band's Laser roll |
| Black Hole | 28 s | 5-35 m | 1 s marker | 6 s well (r 12 m) on the player's spot; pull 2 m/s at the rim to 9 m/s at the centre; core (r 3 m) deals 0.05·D per s |
| Blizzard | always | aura r 15 m | none | player speed ×0.55 inside, 0.02·D per s |
| Radiance | always | aura r 10 m | none | 0.04·D per s |
| Flare | 20 s | 5-40 m | 1.5 s rising flare | burst 10 m over the player: 3 s of light, 0.25·D to anyone within 7 m (horizontal) |
| Fire Cataclysm | 40 s | 0-40 m | 1 s growing | vortex r 8 m follows the enemy 10 s, 0.15·D per s |
| Arcane Cataclysm | 40 s | 0-40 m | 1 s growing | same, 0.1·D per s and player speed ×0.5 inside |
| Warp | 12 s | 15-60 m | 0.75 s | the enemy appears 5 m from the player on its own side |
| Chains | 15 s | 4-25 m | 1 s tether | roots the player 2.5 s (refused by `stun_immunity`) |
| Trapper | 18 s | 4-28 m | 1.5 s | 3 snares (r 2.5 m; one under the player, two within 6 m) wait 20 s (max 9 live); stepping on one roots 3 s and deals 0.2·D |
Co-op (COS-79): the host (a dedicated server too) runs every enemy's casts and aims each at the nearest player it knows, host or client (clients at their last reported position). Each cast is sent to every client (`AbilityEvent`: the target spot, a seed for the trap spots, and whether that client is the target; a Laser also sends its locked beam end). Each machine plays the cast and resolves its effects on its own player only: damage (with the enemy's synced damage multiplier, then the player's own defenses), slows, the Black Hole pull, the Chains root (only on the player aimed at) and traps. Auras (Blizzard, Radiance) run on every client for each enemy that has one. Warp moves the enemy on the host only. A client hit by an enemy swing takes Basher stun and Poisonous from the nearest host enemy within 4.5 m. Clients show the enemy's synced scale; a client that joins late gets every existing enemy's profile again.

## Enemy scanner, danger and names (COS-68, COS-67; new rules, our own design)
- Danger (`Core/Rules/EnemyInfo.cs`): the level gap against the local player in steps of `max(3, playerLevel / 10)` levels: more than 2 steps below Trivial, up to 1 step above Even, up to 3 steps above Tough, beyond Dangerous; Elite and Miniboss +1, Boss +2, capped at Deadly. Nameplates show it as 1-5 tally marks (last one struck across), grey to red.
- Named elites: Elite, Miniboss and Boss tiers get a random given name and epithet from `content/elite_names.json` (host-rolled, sent in `EnemySync` as ids; text keys `<id>.name`, joined by `ui.elites.format`). The plate shows the name; the scanner shows kind and tier.
- Scanner: the crosshair resting 0.25 s on an enemy's body (within 40 m, plate drawn) opens a card beside its plate: name, kind, tier, level, danger word; health current/max and armor with the amount stripped (4 Hz); extra damage per hit (`max(0, damageMult − 20)`, see `DamageToPlayer`); XP bounty; one line per affix saying what it does. It lingers 0.6 s after the look leaves. Setting `show_scanner`.
- Boss bar: a Boss-tier enemy within 60 m that is in the game's combat-aware AI state, or that the local player hit in the last 10 s, gets a wide bar at the top centre (name, kind and level, health with a pale chunk for damage just taken, affixes). Hidden at once on death or despawn, 3 s after it stops being engaged, and with the HUD while panels, the book or menus are open. Two bars at most, nearest first. On a client the AI runs on the host, so the AI state may not be seen there: a hit always engages (not verified in co-op yet). Setting `show_boss_bar`.

## Perks
- Fields: id, name, category (MeleeOffense, RangedOffense, MagicOffense, Defense, Support, Utility), unlock path (any one of), requirements (all of), cost, level requirement, stackable (no max), tree position, effects.
- Buy: points ≥ cost, path and requirements met, level met, not bought unless stackable, and a perk's custom condition (`canBuyManual`): Heavy Hands needs attack speed above 110%, Slow Release projectile speed above 110%. Each purchase applies the effects again. No single refund; respec unbuys all perks and spells and sets points = level + permanent bonus.
- Unlock paths (COS-70): COTF defines runtime perk 151 twice (single-player and multiplayer alternates; COTS ships both), so every path or requirement reference to a runtime ID above 151 means block index ID + 1. Content check fails when a perk cannot be reached from a root. Saves keep perks bought through an older path.
- Spent points are derived from the bought perks; a perk deleted from content is removed from saves by a migration, so its points come back.

### Ranged and melee perks (COS-86)
- Weapon kind: every player projectile, thrown spears included, leaves through the game's `RangedWeapon`, which knows its item; a projectile hit within 15 s of a shot is credited to the last fired item, whose name gives the kind (bow, crossbow, firearm, slingshot, thrown spear).
- Ranged hit: `(proj + rangedFlat) × crit × Ranged × weapon × size × (1 + metres × perMetre) × 0.75^i × headshot × True Aim`. weapon: bow `perk_bow_damage_mult`, crossbow `perk_crossbow_damage_mult`, firearm `perk_bullet_damage_mult`, thrown spear `perk_thrown_spear_damage_mult`. size: projectile size with Size Matters. Speed Matters: crit multiplier + (projectile speed − 1). metres: player to target when the hit lands.
- Headshots: a head collider, or a rolled chance for firearms (`perk_bullet_crit_chance`, 10% base) and thrown spears (`perk_thrown_spear_crit_chance`, 5% base).
- True Aim (perk, Bunker Protocol 5): an arrow or bolt headshot beyond 60 ft (18.3 m) deals ×1.5 and hits once more; Palelight Cuirass: beyond 120 ft (36.6 m) ×5 and two more hits. Not Seeking Arrow.
- Spears: Piercing makes a thrown spear pierce `3 × ranged pierce + melee pierce`. Double Spears: a thrown spear that hit lands a second hit with 40% (+6% per Spears stack).
- Ammo: Reusability, Endless Quiver and the quivers give a chance that a spent round returns to the game inventory (`RangedWeaponController.OnAmmoSpent`).
- Area damage: with `area_damage` > 0, a ranged hit splashes `hit × area_damage` on enemies within `area_damage_radius` with `area_damage_chance` (nothing in content grants it yet).
- Explosions take only `explosion_damage`; thrown spears take only their own multiplier.
- Melee: Combat Regen restores 3 stamina per landed melee hit per stack. Ember Heart: while Black Flame burns, melee damage × (1 + attack speed × 1.1) and Black Flame's upkeep × attack speed. Measured Breath: while Black Flame burns, the swing speed the game gets is fixed at ×2.
- Thorns: `(thorns + per-STR + per-VIT) × thorns mult × melee increased damage × all damage`.

### Magic, defense, support and utility perks (COS-87)
- Infinity: every cast multiplies the remaining cooldown of all six slots by 0.95. Gilded Pace: Golden Skin's base cooldown −15 s (only Golden Skin).
- Dense Matter: enemy Black Holes do not pull the player. Warmth: inside an enemy Blizzard the player moves ×0.8 instead of ×0.55.
- Autocast Sustain Shield: with Sustain Shield on the bar and energy at 90% or more, the shield fills by itself for 10 energy per second.
- Smolder Mark: every landed weapon hit marks the enemy for 15 s; fire damage-over-time on it (Firebolt burn, Parry ignite) deals ×(1 + bonus), and hits on a burning enemy add the bonus to fire damage. bonus = `(1 + spellFlat/3) × Spell × (1 + fireDamage) × 0.35` (0.3 for ranged hits).
- Light the Way: flashlight intensity × the intensity stat (+100% per stack), battery drain ÷ the battery stat.
- Momentum Transfer: landing after 0.2 s in the air gives move speed ×1.25 for 0.5 s × jump power; Bunny Hopping ×1.6 for 0.75 s × jump power.

### Spell items (COS-88)
- Blink range = `spell_blink_range` (15 m, items add). Phase Pauldrons: Blink strikes every enemy within 1.5 m of its path for `spell_blink_damage` + `spell_blink_damage_scaling` × spell flat. Tearing Spacetime: an exit blast of `40 × movement speed + 2 × flat` within `max(2, 0.2 × distance)` m.
- Iron horns: War Cry adds 10% of the player's armor; the greater horn also damage taken ×0.9.
- Jagged Edge: Bash bleeds the enemy for 30% of the damage per second for the slow's duration, with `spell_bash_bleed_chance`. Bloodthirster: Bash heals and restores energy by `spell_bash_lifesteal` × damage.
- Alexander's Shield: a hit taken triggers Parry by itself (20%) when Parry is on the bar and ready; it then cools down.
- Purgatory: Purge gives all damage ×(1 + 3 × missing health fraction) for 10 s.
- Frenzy items: `spell_frenzy_active_crit_chance` adds crit chance while Frenzy runs. Flame Pauldrons: Firebolt costs `spell_firebolt_energy_cost − 5` more energy. Healing Dome bracer: its cooldown runs `1 + spell_healing_dome_cooldown_rate` times as fast and costs × `spell_healing_dome_spell_cost_reduction`.
- Mysterious Robe (and The Hushed 5): the arcane Cataclysm deals ×1.5 and slows enemies inside to ×0.2.
- Sets: a bonus marked `perPiece` applies once per equipped piece (Whiteout Regalia 1, Blood Oath 1, as COTF).

## Spells
- Fields: id, level requirement, energy cost, base cooldown, channeled, cast on release.
- Loadout 6 slots; unlock costs 2 perk points.
- Cooldown ticks at `dt × cooldownRate / cooldown`; equipping starts the cooldown.
- Cost: energy part `cost × spellCostEnergyCost × spellCost`, stamina part `cost × (1 − spellCostEnergyCost) × spellCost`; channeled per second.
- 27 spells (level / energy / cooldown s): Black Hole 20/350/140, Healing Dome 6/80/70, Blink 3/25/14, Sun Flare 10/100/70, Sustain Shield 8/50 per s, Wide Reach 2/10/0.4, Black Flame 21/25/2.5, War Cry 14/85/180, Portal 12/90/60, Magic Arrow 27/100/20, Multishot 40/30/12, Golden Skin 35/65/160, Purge 7/40/16, Snap Freeze 20/220/40, Berserk 25/10/200, Ball Lightning 44/350/100, Bash 17/25/25, Frenzy 1/0/40, Seeking Arrow 27/50/5, Focus 4/40/20, Parry 8/35/60, Cataclysm 50/500/300, Blood Infused Arrow 11/75/8, Roaring Cheeks 55/400/360, Taunt 9/40/45, Firebolt 6/45/20, Snow Storm 9/120/20. Damage formulas per spell are in COS-25/COS-26.

Co-op (COS-80): a cast is sent with every visual it drew (kind and look, place, size, colour, lifetime, whether it rides on the caster, how it flies), and later visuals and early ends (a bolt hit, a toggle switched off) follow, so teammates see the same effect at the same place, size and duration. Toggle spells (Black Flame, Multishot, Sustain Shield) show on and off on the caster. A teammate's Portal pair also carries the local player. Ball Lightning flies the same line for the same time everywhere, so it ends at the same place. Damage, buffs and debuffs stay with the caster's machine (client spell damage on host enemies: COS-13; teammate heals and buffs: COS-23). A homing Seeking Arrow flies straight on other machines until the caster's copy ends it.

### Spell effects (COS-26)
Numbers marked *stat* are the original `spell_*` player stats (content/player_stats.json), so perks that change them change the spell. Numbers marked *own* are this mod's (the original text is not available) and live in `SpellTuning` (Core/Rules/SpellRules.cs). Spell hit = `(base + spellFlatDmg × scaling) × Spell` magic damage through the enemy hit path (armor, shields, caps, Undead); it credits the player but does not trigger on-hit procs.
- Channeled and toggle spells: a cast switches them on if the energy covers one second of upkeep (and they are ready); they then cost their energy cost every second and switch off when the energy runs out or when cast again. Switching off starts the cooldown. (Holding a key is not detected: a second press is the release.)
- Buffs are stat-sheet sources `spell.<id>`; re-casting refreshes the time.
- War Cry: 20 s *own*, all damage ×1.2 and attack speed ×1.2 *stat*. Berserk: 30 s *stat*, damage ×1.3, movement ×1.35, attack speed ×1.25, max health ×1.0 *stat*. Golden Skin: 20 s, +250 armor, damage taken ×0.75 *own*. Focus: attack speed ×1.3 for 4 s *stat*, +15% crit chance, headshot ×1.5 *stat*; enemies the player hits meanwhile are slowed ×0.8 for 4 s *stat*. Frenzy: 20 s *own*, each hit adds a stack up to 5, +7.5% damage and +2% attack speed per stack *stat*. Taunt: 8 s *own*, enemies within 30 m move ×2 *stat* (they rush the caster), player +100 armor *own*. Purge: ends stun and poison on the player; with the perk also heals 20% max health *own*.
- Parry (35 energy): for 1.5 s *own* the next hit on the player does 0 damage; it strikes enemies within 3.5 m for 40 + 3 × spell flat, heals 10, gives 10 energy and a 10 s attack-speed buff *stat*.
- Sustain Shield (channeled): shield fills 3% of max health per second up to 15% *stat*; incoming damage drains it first (after armor, dodge and block); after release the rest lasts 20 s *stat*.
- Healing Dome: 10 s *stat*, radius 8 m *own*, heals the player inside 5% max health per second *own*.
- Sun Flare: aimed within 30 m; at once 40 + 1.5 × flat in 8.5 m *stat*; for 20 s *stat* enemies inside move ×0.4 *stat* and the player inside heals 11 per second and moves ×1.35 *stat*.
- Black Hole: aimed within 40 m, 9 s, radius 15 m *stat*; every second 40 + 3.25 × flat *stat* to enemies inside; they are pulled in at pull force 25 / 5 = 5 m/s *own*.
- Cataclysm: aimed within 50 m, 12 s, radius 5 m *stat*; every 0.5 s *own* 40 + 25 × flat *stat* to enemies inside.
- Snow Storm: aimed within 30 m, 8 s, radius 10 m *own*; every 0.5 s *stat* 8 *own* + 0.4 × flat *stat* (× damage mult *stat*), enemies inside move ×0.6 *own*.
- Ball Lightning: a ball rolls forward at 6 m/s for 10 s *own*; every second enemies within 7 m take a tenth of (1220 + 75 × flat) *stat/own*.
- Portal: a gate 3 m ahead and a linked gate where the view ray lands (up to 40 m); walking into one leaves from the other; 30 s *stat*, 1.5 s re-entry lock *own*.
- Wide Reach (COS-85, Max 2026-11-20: it picks things up or pulls them in): an area pickup, no damage. Collects the mod's drops within 5.5 m that are at least as rare as the lowest-rarity equipped item (0 when any equipment slot is empty), through the normal pickup (so co-op keeps the host's single-pickup rule), and the game's loose resource pickups on a list of game item names (`WideReachRules.GamePickups`, the game's own `PickUp.Collect`). Not while pushing a log sled. Greed casts it once a second.
- Bash: 30 + 1 × flat to enemies within 4.5 m in an 80° cone, knocked back 3 m, damage taken ×1.3 and speed ×0.4 for 2 s *stat*. Snap Freeze: 40 + 6.5 × flat to enemies within 12 m, speed ×0.2 for 7 s *stat*. Roaring Cheeks: 50 + 5 × flat to enemies within 30 m, speed ×0.8 for 30 s, knocked back 2 × 1.5 m *stat/own*.
- Projectiles (fly in a line, stop at the first enemy): Firebolt 30 + 1.2 × flat then a burn of 25% of the hit per second for 4 s *own*; Magic Arrow 60 + 4.2 × flat, +1 arrow per volley-count perk *own/stat*; Seeking Arrow 30 + 1 × flat, homes on the nearest enemy for 10 s, slows ×0.9 for 4 s *stat*; Blood Infused Arrow spends 65% of current health (never the last point) and deals spent health × 5 + 0.1 × flat *stat*.
- Black Flame (toggle): every second enemies within 6 m take 8 + 0.4 × flat *own/stat*. Multishot (toggle): a stand-in for extra arrows: every 0.75 s the nearest enemy within 30 m takes 15 + 1 × flat *own*.
- Deviations from the original are deliberate placeholders for spells whose original behaviour needed bow/AI hooks: Multishot auto-fires instead of multiplying bow shots; Taunt speeds enemies up instead of redirecting aggro; Portal places both linked gates in one cast, where COTF placed one portal per cast and linked the last two (COS-84).

## Buffs
Keyed by source; re-adding a source refreshes duration; amount overwrites, accumulates or takes the max per buff kind. Negative buffs can be blocked by resist (dispel ≤ 1) or immunity (dispel ≤ 2).

## Weapon types (COS-93)
- A weapon item's `weaponModel` names a type in `content/weapon_types.json`. The type applies while the item is in the weapon slot and the player holds a game weapon it fits: `melee` types on one-handed melee weapons (machete, katana, axes, club, knife, putter), `polearm` on the crafted spear, `greatbow` on the crafted or tactical bow. The draw key (default L) equips the first owned game weapon from the type's `baseItems`.
- While it applies, the game weapon's mesh is hidden and the type's model sits in the same hand (the game weapon keeps its animations, hit logic and sounds). Other players see the model: each player broadcasts the type in hand, receivers dress the held weapon nearest to the reported position.
- Melee: the type's `damage` replaces the game weapon's own damage (factor `damage / game damage`, machete = 14; the spear's damage comes from its hit script, it has no weapon data); swing speed and stamina cost multiply the game values (on top of attack speed and attack cost); `reach` lengthens the weapon's hit box.
- Trees: types with `cutsTrees: false` (swords, hammer, polearm) set the game's own "no tree cut" flag on the weapon's hit script and get a tree-cut multiplier of 0.
- Polearm (`canThrow: false`): the spear never enters its aim state, so the attack stays a stab and the spear can't be thrown.
- Greatbow: shot base damage `(game + flat) × mult`, projectile speed × `projectileForce`; arrows fly straight (the ballistics "bullet drop" switch is off while it is held, which also affects other projectiles in flight at that moment); `drawSpeed` 0.7 makes the time between shots ×1/0.7.
- Swing trail (`trailWidth`) and sounds (`swingSound`, `hitSound`, `soundPitch`; the bow's swing sound is its shot) follow the game's own swing, hit and shot events.
- Glow (COS-108, Max 2026-11-20): one look per rarity (`WeaponGlow.LevelOf`). Common: no glow. Magic: a faint blue fresnel edge (strength 0.2; calm even with many magic drops). Rare: a yellow edge (0.45). Legendary: a strong red-orange edge (1.3), embers along the blade, a pulsing light in hand, a flame-coloured wider and longer swing trail, and sparks where a melee hit lands. Uniques (item bases with unique effects) always get the Legendary look, whatever their rolled rarity; a unique's `glow` in items.json may set its own tint (`#rrggbb`) and particle style (`ember`, `frost`, `wisp`). Ground drops get the same edge and particles (they have their own light). "Reduce heavy effects" leaves out particles, lights and sparks. Other players see it: the held-weapon message carries the item's rarity and base id.
- Hammer: with `i_hammer_stun`, every hit slows the enemy to 25% speed for 0.4 s (COTF rule). The stun chance from `i_hammer_stun_amount` stays as before on any melee weapon (deviation below).
- Numbers are this mod's own, scaled to the game's weapons: long sword 20 dmg, swing ×0.85, stamina ×1.3, reach ×1.3; great sword 28, ×0.7, ×1.8, ×1.6; hammer 18, ×0.55, ×2.5, ×1.4; axe 13, ×1.15, ×0.8, ×0.9; polearm 24, ×1.1, ×1, reach ×2; greatbow +15 then ×1.4, force ×1.1, draw ×0.7.

## Co-op party (COS-81)
- Every player's name, level and magic find reach every player (every 3 s and on a level change). Other players carry a "name [LvN]" tag; the Character page (third page) lists the party.
- System lines at the left of the HUD for every player: someone joined (with level), left, died, or reached a level.
- Damage numbers: every hit on an enemy that lands on the host (its own hits, damage over time, spells, thorns, and other players' hits) is shown to the clients within 80 m, under the damage-number setting. Not shown: numbers for damage other players take.

## Deviations from COTF (deliberate)
- No difficulty picker (COS-130, Max 2026-11-20): COTF's host picked one of 11 tiers that fixed enemy levels for the world. Here enemy levels follow the party's level and the game's own difficulty adds levels and elite chance; the tiers only remain as level bands for their per-band numbers. Worlds saved with a tier (WorldSave v2) drop it on load and keep the host's extra levels: a tier's fixed levels do not translate into an offset over the party.
- Four rarities instead of eight (COS-109, Max 2026-11-20): Common, Magic, Rare, Legendary; every unique is Legendary. The roll keeps COTF's eight-step climb and folds its result. Value multipliers are averaged per tier (Legendary ×4.5, between COTF's ×3.4 and ×5.6), so the old Primal tier rolls about 20% lower and the old Legendary tier about 30% higher; the XP heart gives ×4.5 instead of ×3.4. Old saves are remapped on load.
- Vanilla floors: enemy max health is at least the game's own value, and enemy damage to the player is at least the game's own hit. COTF's curves were tuned for The Forest's numbers; in Sons of the Forest they made low-level enemies far weaker than vanilla (seen in the 2026-11-18 self-test: a level-2 cannibal had 20 HP vs 100 vanilla).
- Boss tier: COTF requires n > 6 abilities, which only its Megan could reach, and gives Bosses ×2 HP (less than Miniboss ×10). Here Boss needs n ≥ 6 and has ×20 HP.
- Incoming damage: COTF computed armor/DR/block for the player but applied the vanilla damage. Here the computed value is applied.
- Pierce chance: COTF pierced when `U > chance` (inverted). Here `U < chance`.
- Cataclysm: COTF multiplied by `fireDamage` (0 by default). Here `× (1 + fireDamage)`.
- Life per second: COTF also added LPS every frame. Here only per second.
- No crafting, Scrap, materials or sockets (COS-145, Max 2026-11-20). COTF's crafting modes (Reroll, Reforge, Polish, Empower, single-stat reroll) and their perks, its socket gems and use-on-item materials (drill, enzymes, acids, solutions, mixtures, waters) and empty-socket rolls are not in COTS. Merge (COS-144) is the only way to change items. Saves from before (PlayerSave v5) drop the crafting perks (they cost 0), deleted material stacks and socket stats.
- Stat sheet: contributions are sources, not add/subtract pairs (no drift, bools are reference-counted).
- Deleted perks (COS-86/87, Max 2026-11-20: perks that cannot work in Sons of the Forest are deleted; saves refund the points, PlayerSave v3): `perk.charge_pushback` (Ramming Swing, heavy-attack knockback: the game gives no verified heavy-attack signal).
- Six uniques keep this mod's own rules (Max 2026-11-20, COS-88): Healer's Blood Bag (COTF: each melee hit puts a DoT of 150% max health over 15 s on the enemy), Biting Band and Gorged Ring (COTF: +1 / +10 stamina per hit, doubled on a crit), Undying Promise (COTF: full heal, 5 s of 98% damage reduction, +35 health per second, 60 s cooldown), Hexed Pants of Mr. M (COTF: ×1.6, and 1.6 stamina regeneration), Death Pact (COTF: 6% per attack), Wind Armor (COTF: move bonus 30 s, damage and armor 10 s). Numbers as under "Unique item flags".
- Wide Reach: the pickup radius is this mod's 5.5 m (COTF 7.5 m).
- Weapon kind: a projectile hit is credited to the last shot within 15 s (the game's damage call does not carry the weapon).
- Pierce: still the bullet-weight approximation (pierce raises bullet weight up to ×4), not COTF's pass-through roll.
- Double Spears: the second spear is an immediate extra hit on the same target, without a second projectile.
- True Aim numbers follow the COTS texts: ×1.5 and one more hit beyond 60 ft; the cuirass ×5 and two more hits beyond 120 ft (COTF: ×10 beyond 120 m).
- Smolder Mark raises fire damage-over-time and hits on burning enemies; COTF raised the enemy's fire bonus, which only matters while it burns.
- Warmth: only the move slow (COTS Blizzards slow movement only and take no armor).
- Thorns ignore `thorns_armor_piercing` as a fraction of the attacker's armor; COTF stripped a flat amount of armor instead.
- Autocast Sustain Shield costs 10 energy per second (COTF: 10 stamina; energy is the stamina bar here).
- This mod's numbers where COTF's are unknown: Alexander's Shield 20%, Purgatory ×(1 + 3 × missing) for 10 s, Tearing Spacetime 40 × move speed + 2 × flat within max(2 m, 20% of the distance), arcane Cataclysm ×1.5 and ×0.2 slow.
- Death (COS-76): COTF reset the whole level bar on respawn; here a death costs half the bar, at most a tenth of the level's goal. COTF's host options "item drops on death" and "instant death when downed" are left out: death is the base game's.
- One energy bar (COS-94): COTF had energy and stamina as two pools. Here energy is the game's stamina, refilling the way stamina does, plus our sources: energy per second, stamina per second, energy and stamina on hit (one sum), Parry, Healing Dome with Humming Ground (5% of max energy per second × all recovery). A spell needs its whole cost (energy and stamina parts) in the bar. "Spell Cost to Stamina" only moved cost between the two pools, so it no longer rolls on items (old items keep it, without effect); the Sinew Channel perk has the same no-op effect until the perk pass decides.
- Weapon types (COS-93): types sit on any fitting game weapon the player owns instead of a key that swaps onto a fixed vanilla weapon; numbers are relative to the game's weapons. The greatbow's "slower draw" is a longer time between shots (the draw animation itself is the game's). The hammer stun chance on any melee weapon is kept as a COTS extra.
