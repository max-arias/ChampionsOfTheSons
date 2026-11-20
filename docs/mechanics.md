# Mechanics

The rules Champions of the Sons implements. Numbers and formulas come from reading the original Champions of the Forest (COTF) source (2026-11-18); the code here is new. `U(a,b)` is a uniform roll, `L` a level, `d` the difficulty index 0..10. **Deviations** from COTF are listed at the end and marked ✱ inline.

## Progression
- XP to next level: `goal(L) = round(0.8L⁴ + L³ + 2L² + 20L + 4e^(L/3.3) + 1.36^(L−5))`; from L 138 the goal is 5e18 (practical cap).
- Gaining XP: `xp += amount × expGain`; while `xp ≥ goal(L)`: `xp −= goal`, level up.
- Level up: +1 mutation (perk) point; STR/AGI/VIT/INT base = `1 + A(L)` with `A(1)=1, A(x)=A(x−1)+round(√x)`.
- Reward items: every 10th level a Untainted Heart (respec); levels 25, 45, 65… a Greater Mutated Heart (+1 permanent perk point).
- Death: current-level XP resets to 0, pending massacre XP is lost; no level loss.

## Massacre (kill streak)
- On kill: `kills++`, `pending += xp`. Timer: if 0 → `maxMassacreTime` (15 s), else `clamp(timer + 7.5, 1, maxMassacreTime)`.
- When the timer runs out: grant `pending × mult(kills)`; reset.
- `mult`: <3→1, 3→1.1, 4→1.25, 5→1.5, 6–9→1.75, 10–11→2.3, 12–15→3, 16–19→5, 20–24→8.5, 25–29→16, 30–39→22.5, 40–49→35, 50–64→50, 65–74→100, 75–99→250, ≥100→1000.

## Difficulty (11 tiers)
| d | Name | Base enemy level | Armor factor `⌊d/2⌋+1` | Shielding CD | Meteor dmg | Laser dmg |
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

Host multipliers (default 1): drop quantity, drop chance, XP, extra enemy levels, enemy damage/health/armor/speed; allow elites; loot level policy (highest/average/lowest/closest/host); minimum loot rarity. No day-based scaling.

## Enemies
- Level: `ceil(base(d) + kindExtra + extraLevels)`. kindExtra comes from content per enemy kind (COTF: normal 1, leader 2, pale 2, painted 3, creepy 2, boss-type 6, baby 0).
- Damage multiplier: `L < 65 ? e^⌊L/6⌋ + L : (L−60)^2.6 × 600`.
- Armor: `⌊U(L^2.43/3 + 1, L^2.43·2/3 + 20) × armorMul⌋ × (⌊d/2⌋+1)`.
- HP: `(√vanillaHP + 45) × L^2.38 × 0.2 × 2^((L−50)/32) × healthMul`.
- Speed: `(0.94 + L/170) × speedMul`.
- Tier: elite if `(U < 0.1 or kind is a boss type or d = Hell) and elites allowed`. Elite ability count: 2 at d ≤ 1, `U[3,6]` at d ≥ 2, 10 for boss types. Boss if `n ≥ 6 and U < 0.1` ✱, Miniboss if `n > 4 and U < 0.25`, else Elite. Non-normal tiers: armor ×2; HP ×5 Elite, ×10 Miniboss, ×20 Boss ✱; scale ×1.1/1.15/1.2. Boss adds Boss Steadfast, Miniboss adds Elite Steadfast.
- Abilities: n distinct picks; only one steadfast; Tiny/Gargantuan exclusive; Meteor, Black Hole and Cataclysms need d ≥ 3; Undead only on kinds that allow it.
- Bounty (kill XP): `b = maxHP^0.8 × 0.8 × L^0.25 + 100`; `×n` if n > 1; `× max(1, L/20)`; `× xpMul`. Co-op: split evenly between players.
- Armor reduction: `DR(a) = (max(a,0)/(max(a,0)+500))²`. Physical damage `× dmgTakenMult × (1 − DR(armor − armorReduction))`; magic uses DR/1.5; pure ignores armor. Armor pierce adds to `armorReduction` (capped at armor); fire ticks also add.
- Steadfast caps each hit at 6% (Steadfast), 4% (Elite) or 1% (Boss) of max HP.
- Ability numbers (COTF): Gargantuan armor ×3, HP ×2.5, dmg ×2.5, speed ÷1.5; Tiny speed ×1.1, dmg ×1.2; Extra Health HP ×4; Extra Damage dmg ×5; Juggernaut CC-immune, speed ÷1.15; Rain Empowerment while raining dmg ×5, armor ×5, speed ×2; Undead revives once at half HP; Shielding 3 s immunity then tier CD; Basher 1 s stun; Poisonous `√(dmg/10)/10` per s for 6 s; Warp, Chains, Trapper, Blizzard, Radiance, Black Hole, Meteor, Flare, Laser, Arcane/Fire Cataclysm, Sacrifice, Avenger — see COTF spec numbers in the COS-19/COS-20 tickets.
- Enemy damage to player: `damageMult × debuffDmgMult + vanillaDamage − 20`.
- Enemy debuffs are keyed by source: slows keep max duration/min amount per source and multiply across sources; damage-taken keeps max duration/latest amount and multiplies; fire bonus sums. DoTs tick every 1 s for `ceil(duration)` ticks.

## Loot
- Drop on kill if `U ≤ 0.1 × dropChanceMul × magicFind`, or the enemy has any ability.
- Count: `U[2,3]` (+`U[2,4]` with 3+ abilities) (+7 Boss / +3 Miniboss) + `clamp(⌊L/50⌋, 1, 8)`, × drop quantity, rounded.
- Item level = player level by loot level policy; materials and special consumables are level 1.
- Rarity: `w = bounty / itemLevel × MF`, `mf = √MF − 1`, fresh U each check, climbing while each step passes:
  - r1: `(w > 20 and U < 0.70 + 0.45mf + 0.075d) or (d > 5 and w > 2000)`
  - r2: `w > 80 and (U < 0.50 + 0.4mf + 0.07d or (w > 2200 and d > 6))`
  - r3: `(w > 180 and U < 0.5 + 0.35mf + 0.05d) or (w > 5000 and d > 7 and U < 0.9)`, kept if `d > 0 or U < 0.05`
  - r4: `(w > 360 and U < 0.5 + 0.22mf + 0.034d) or (d > 8 and U < 0.7)`, kept if `d > 1 or U < 0.02`
  - r5: `w > 720 and U < 0.26 + 0.085mf + 0.02d`, kept if `d > 2 or U < 0.01`
  - r6: `w > 1440 and U < 0.18 + 0.033mf + 0.003d`, kept if `d > 3 or U < 0.05`
  - r7: `w > 5000 and U < 0.04 + 0.01mf`, kept if `d > 4 or U < 0.001`
- Base item: uniform among that rarity's items with `minLevel ≤ level`; none → try one rarity lower.
- ✱ First pass (COS-59): drops are equipment only. Materials and consumables stay defined in `content/` but never drop, and the crafting panel does not offer Scrap. Level-up hearts are still given.
- Stat slots: each pool rolls one option uniformly; `null` = slot stays empty; `*` = one of the common stats (COTF IDs 1–18, 21–26, 31, 34–47, 49–57, 59–67).
- Value: `v = U(min, max) × level^levelPow`; `v = min(v, cap)`; `× statMultiplier`; `× rarityMult(r)`; `v = min(v, cap)`. `rarityMult = 0.5, 0.7, 1, 1.4, 2.3, 3.4, 4.5, 5.6`.
- Sockets: equippable, level > 20, `U ≤ 0.175` → `U[1, max]` empty sockets (max 1 most slots; 2 bracer/pants/shield/quiver/scroll; 3 chest). Gems fill a socket with a slot-dependent stat.
- Inventory: 120 bag slots (COTF: a fixed 8 x 15 grid; its bag never grows with level or stats, checked 2026-11-19, COS-64); equipment: helmet, chest, pants, boots, shoulders, gloves, amulet, bracer, ring ×2, weapon, offhand (shield/quiver/scroll).

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
- Shielding: the first hit while ready is absorbed and raises a 3 s immunity, then it recharges for the difficulty's cooldown. Undead: the first lethal hit leaves half health. Juggernaut: ignores slow, stun and knockdown. Basher: the player's movement stops for 1 s (unless `stun_immunity`). Poisonous: `hit × √(damageMult/10)/10` damage per second for 6 s on the player. Rain Empowerment: damage ×5, armor ×5, speed ×2 while it rains.
- Avenger: each enemy dying within 30 m adds a stack (max 5, 20 s): +25% damage and +10% speed per stack. Sacrifice: on death, enemies within 25 m heal 30% of max health and deal ×1.5 damage for 15 s. (New rules.)
- Gargantuan/Tiny set the actor's scale; speed is the profile speed times every slow/stun/rain factor, applied to the actor's animator.
- Player on-hit stats: life/energy on hit (× all recovery), chance to slow (×0.5 speed, 8 s), bleed (hit/20 per s, 10 s) and weaken (×1.2 damage taken, 8 s), hammer stun (`i_hammer_stun_amount` chance, `i_hammer_stun_duration` s). DoTs are pure damage (no armor) but meet steadfast caps and shields. Thorns = `(thorns + per-STR·STR + per-VIT·VIT) × thornsDmgMult` back at the enemy that hit you, ignoring `thorns_armor_piercing` of its armor. Fire damage multiplies hits on burning enemies by `1 + fireDamage`. Explosion damage multiplies thrown/explosive hits by `1 + explosionDamage`.
- Energy is the game's stamina bar for now: max = vanilla × `MaxEnergy/100`; regeneration adds `staminaPerSecond + energyPerSecond` plus the game's own rate × (energy recovery multiplier / 2 − 1). Attack speed and attack cost scale the held melee weapon's data (and ranged fire delay); projectile speed, size and pierce scale the held ranged weapon's bullet data (pierce raises bullet weight up to ×4).
- Unique items (new rules): Biting Band 3% leech; Gorged Ring 6% leech and 5% max health per kill; Healer's Blood Bag 1% max health per second below half health; Undying Promise: a lethal hit leaves 30% health, once per 300 s; Untainted Heart: use to respec.
- Dropped for now: rafts and spell numbers (read by the spell system). Unique flags and `weapon_range`: see "Unique item flags" below (COS-42).

## Unique item flags (COS-42, this mod's own numbers; the item text is the spec)
- Implemented (`Core/Rules/UniqueFlags.cs`, `Followups/`): **King Qruie's sword** adds the last physical damage taken as flat damage to melee hits. **Death Pact**: each attack costs 7% of max health (never the last point) and damage is `1 + 0.05 × missing health percent`. **Hexed Pants of Mr. M**: ×1.4 damage and ×1.4 stamina regeneration while moving; after 1 s standing still, 1% of max health per second drains (never lethal). **Wind Armor**: a dodge heals 5% of max health and gives ×1.2 movement, ×1.35 damage and +2000 armor for 5 s. **Infinity Loop** (Pendant of Perpetual Rebirth, Destroyed Void Shard): each melee or ranged hit takes 1 s off one random spell cooldown that is running.
- Spell items (after COS-26): **Crossfire** (`i_crossfire_quiver`): a projectile hit fires a free Magic Arrow at the target, at most once per 2 s x the cooldown stat. **Grip of Sora** (`i_sora_bracers`): Multishot cast and upkeep cost x0.8; each volley fires 4 extra projectiles at the next nearest enemies (left-over ones hit the main target). **Withered Crown** (`i_hazard_crown`): one Blood Infused Arrow cast (one health cost) fires 5 arrows in a fan. **Greed** (`i_is_greed`): Wide Reach casts itself once a second, free. **Last Engineer's Note** (`i_spark_of_light_after_dark`): a Black Hole that hit 5 or more different enemies releases a Ball Lightning from its center when it ends.
- Dropped, with reasons: `i_archangel_bow` (needs a second player: co-op, COS-13), `i_great_bow_ignites` and `fire_duration` / `fire_tick_rate` (a test enemy could not be ignited through the game's burn API; unverifiable headless), `i_smokey_crossbow_quiver` (no verified way to tell a crossbow shot from other ranged hits), `smash_damage` (no verified heavy-attack signal; `heavy_attack_dmg` is unused for the same reason), `i_eruption_bow` (explosion effect needs visuals; headshot splash can be added once headshots are verified in play), `weapon_range` (would need collider or weapon-scale changes).

## Event and utility perks (COS-49)
- Improved Dodges: a dodge heals 5% of max health. Deployable Shield: a dodge grants a shield of 15% of max health for 8 s (Bulwark Reflex: 25%); it soaks damage before health. (New rules.)
- Adrenaline Rush: when a hit leaves health at or below 35%, gain ×1.25 movement and attack speed for 6 s, once per 60 s; Adrenaline Rush II makes it ×1.5 for 10 s. (New rules.)
- Resource Cost Reduction: spell cost ×0.91 per stack (data effect). Quick Silver: Blink's base cooldown −6 s before cooldown multipliers.
- Utility perks follow the perk texts. Carry-limit perks add to the game's own per-item limit (`ItemData.MaxAmount`), per stack; daily-grant perks give items at each new in-game day, never beyond the carry limit. Items are the game's own item names (`Core/Rules/PerkEffects.cs`): bombs = TimeBomb, dynamite = C4Brick, meds = Pills, booze = VodkaBottle, snacks = EnergyBar and EnergyDrink, meat = Meat and Fish. Not done: Fuel (the game has no fuel-can item), head bombs, crafting furs into cloth (Furs to Fabric), the ammo crafting recipes (More Ammo) and the Rescue Axe recipe (Craftable Plane Axe); the recipes need recipe injection.
- Waiting on the spells (COS-26): endless_stream, storm_season, endless_storm, wrath_of_the_sun, sudden_rampage, time_of_need, golden_cooldown, falter, enchant_weapon, afterburn, netherflame.

## Scrap (COS-47, new rule)
- Equippable items only. Gives `max(1, rarity − 1) + level / 40` of one random material (type "material") of rarity `clamp(item rarity, 3, 7)`, level 1, in one stack. A full bag drops what does not fit at the player's feet.
- The crafting panel shows a preview per mode (what will change, ranges for Polish, the player level for Empower, "random" for Reroll/Reforge) and a before/after list once crafted.

## Active enemy abilities (COS-20; this mod's numbers except where noted)
Host only; each runs only while the enemy is alive and active and is removed the moment it dies, goes dormant or despawns. Rules are in `Core/Rules/ActiveAbilities.cs`; damage is magic (the player's `magic_damage_taken` applies), times the enemy's damage multipliers (rain, Avenger, Sacrifice) and the host's enemy-damage rule. First cast 2-6 s after the enemy becomes active; the cooldown restarts when a cast ends. A cast needs the nearest player inside the range band. `D` = mean of the difficulty's Meteor damage.
| Ability | Cooldown | Range | Telegraph | Effect |
|---|---|---|---|---|
| Meteor | 14 s | 0-45 m | 2 s ring (r 4 m) on the player's spot | Impact damage = difficulty Meteor roll (table above); `perk_meteor_damage_taken` applies |
| Laser | 10 s | 3-35 m | 1.5 s thin line tracking the player | 1 s beam locked on where the player was, half-width 0.8 m, hits every 0.5 s for the difficulty Laser roll |
| Black Hole | 28 s | 5-35 m | 1 s marker | 6 s well (r 12 m) on the player's spot; pull 2 m/s at the rim to 9 m/s at the centre; core (r 3 m) deals 0.05·D per s |
| Blizzard | always | aura r 15 m | none | player speed ×0.55 inside, 0.02·D per s |
| Radiance | always | aura r 10 m | none | 0.04·D per s |
| Flare | 20 s | 5-40 m | 1.5 s rising flare | burst 10 m over the player: 3 s of light, 0.25·D to anyone within 7 m (horizontal) |
| Fire Cataclysm | 40 s | 0-40 m | 1 s growing | vortex r 8 m follows the enemy 10 s, 0.15·D per s |
| Arcane Cataclysm | 40 s | 0-40 m | 1 s growing | same, 0.1·D per s and player speed ×0.5 inside |
| Warp | 12 s | 15-60 m | 0.75 s | the enemy appears 5 m from the player on its own side |
| Chains | 15 s | 4-25 m | 1 s tether | roots the player 2.5 s (refused by `stun_immunity`) |
| Trapper | 18 s | 4-28 m | 1.5 s | 3 snares (r 2.5 m; one under the player, two within 6 m) wait 20 s (max 9 live); stepping on one roots 3 s and deals 0.2·D |
Not synced yet: the host applies effects to its own player; track E decides how remote players are targeted (`AbilityTargets.Positions`) and receive pull/root.

## Perks
- Fields: id, name, category (MeleeOffense, RangedOffense, MagicOffense, Defense, Support, Utility), unlock path (any one of), requirements (all of), cost, level requirement, stackable (no max), tree position, effects.
- Buy: points ≥ cost, path and requirements met, level met, not bought unless stackable. Each purchase applies the effects again. No single refund; respec unbuys all perks and spells and sets points = level + permanent bonus.

## Spells
- Fields: id, level requirement, energy cost, base cooldown, channeled, cast on release.
- Loadout 6 slots; unlock costs 2 perk points.
- Cooldown ticks at `dt × cooldownRate / cooldown`; equipping starts the cooldown.
- Cost: energy part `cost × spellCostEnergyCost × spellCost`, stamina part `cost × (1 − spellCostEnergyCost) × spellCost`; channeled per second.
- 27 spells (level / energy / cooldown s): Black Hole 20/350/140, Healing Dome 6/80/70, Blink 3/25/14, Sun Flare 10/100/70, Sustain Shield 8/50 per s, Wide Reach 2/10/0.4, Black Flame 21/25/2.5, War Cry 14/85/180, Portal 12/90/60, Magic Arrow 27/100/20, Multishot 40/30/12, Golden Skin 35/65/160, Purge 7/40/16, Snap Freeze 20/220/40, Berserk 25/10/200, Ball Lightning 44/350/100, Bash 17/25/25, Frenzy 1/0/40, Seeking Arrow 27/50/5, Focus 4/40/20, Parry 8/35/60, Cataclysm 50/500/300, Blood Infused Arrow 11/75/8, Roaring Cheeks 55/400/360, Taunt 9/40/45, Firebolt 6/45/20, Snow Storm 9/120/20. Damage formulas per spell are in COS-25/COS-26.

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
- Wide Reach: 20 + 1 × flat to every enemy within 5.5 m in a 140° cone *own*.
- Bash: 30 + 1 × flat to enemies within 4.5 m in an 80° cone, knocked back 3 m, damage taken ×1.3 and speed ×0.4 for 2 s *stat*. Snap Freeze: 40 + 6.5 × flat to enemies within 12 m, speed ×0.2 for 7 s *stat*. Roaring Cheeks: 50 + 5 × flat to enemies within 30 m, speed ×0.8 for 30 s, knocked back 2 × 1.5 m *stat/own*.
- Projectiles (fly in a line, stop at the first enemy): Firebolt 30 + 1.2 × flat then a burn of 25% of the hit per second for 4 s *own*; Magic Arrow 60 + 4.2 × flat, +1 arrow per volley-count perk *own/stat*; Seeking Arrow 30 + 1 × flat, homes on the nearest enemy for 10 s, slows ×0.9 for 4 s *stat*; Blood Infused Arrow spends 65% of current health (never the last point) and deals spent health × 5 + 0.1 × flat *stat*.
- Black Flame (toggle): every second enemies within 6 m take 8 + 0.4 × flat *own/stat*. Multishot (toggle): a stand-in for extra arrows: every 0.75 s the nearest enemy within 30 m takes 15 + 1 × flat *own*.
- Deviations from the original are deliberate placeholders for spells whose original behaviour needed bow/AI hooks: Multishot auto-fires instead of multiplying bow shots; Taunt speeds enemies up instead of redirecting aggro; Portal is a linked pair instead of recall.

## Crafting
Ingredients: exactly N items, each of rarity ≥ the target's; one of each consumed. Each mode is unlocked by a perk.
- Reroll (N=2): new stat types, values and sockets.
- Reforge (N=3): another base of the same rarity ✱ and same item type; keeps level; new rolls.
- Polish (N=1): re-roll one stat's value; a filled socket is emptied instead.
- Empower (N=9): equippable only; level = player level; re-roll all non-socket values.
- Individual reroll (N=1): pick a new option from the same stat pool (≥ 2 non-null options ✱).

## Buffs
Keyed by source; re-adding a source refreshes duration; amount overwrites, accumulates or takes the max per buff kind. Negative buffs can be blocked by resist (dispel ≤ 1) or immunity (dispel ≤ 2).

## Deviations from COTF (deliberate)
- Vanilla floors: enemy max health is at least the game's own value, and enemy damage to the player is at least the game's own hit. COTF's curves were tuned for The Forest's numbers; in Sons of the Forest they made low-level enemies far weaker than vanilla (seen in the 2026-11-18 self-test: a level-2 cannibal had 20 HP vs 100 vanilla).
- Boss tier: COTF requires n > 6 abilities, which only its Megan could reach, and gives Bosses ×2 HP (less than Miniboss ×10). Here Boss needs n ≥ 6 and has ×20 HP.
- Incoming damage: COTF computed armor/DR/block for the player but applied the vanilla damage. Here the computed value is applied.
- Pierce chance: COTF pierced when `U > chance` (inverted). Here `U < chance`.
- Cataclysm: COTF multiplied by `fireDamage` (0 by default). Here `× (1 + fireDamage)`.
- Life per second: COTF also added LPS every frame. Here only per second.
- Reforge keeps the item type (COTF could turn a ring into boots).
- Individual reroll skips empty (null) options instead of throwing.
- Stat sheet: contributions are sources, not add/subtract pairs (no drift, bools are reference-counted).
