# Mechanics

The rules Champions of the Sons implements. Numbers and formulas come from reading the original Champions of the Forest (COTF) source (2026-11-18); the code here is new. `U(a,b)` is a uniform roll, `L` a level, `d` the difficulty index 0..10. **Deviations** from COTF are listed at the end and marked ✱ inline.

## Progression
- XP to next level: `goal(L) = round(0.8L⁴ + L³ + 2L² + 20L + 4e^(L/3.3) + 1.36^(L−5))`; from L 138 the goal is 5e18 (practical cap).
- Gaining XP: `xp += amount × expGain`; while `xp ≥ goal(L)`: `xp −= goal`, level up.
- Level up: +1 mutation (perk) point; STR/AGI/VIT/INT base = `1 + A(L)` with `A(1)=1, A(x)=A(x−1)+round(√x)`.
- Reward items: every 10th level a Heart of Purity (respec); levels 25, 45, 65… a Greater Mutated Heart (+1 permanent perk point).
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
- Stat slots: each pool rolls one option uniformly; `null` = slot stays empty; `*` = one of the common stats (COTF IDs 1–18, 21–26, 31, 34–47, 49–57, 59–67).
- Value: `v = U(min, max) × level^levelPow`; `v = min(v, cap)`; `× statMultiplier`; `× rarityMult(r)`; `v = min(v, cap)`. `rarityMult = 0.5, 0.7, 1, 1.4, 2.3, 3.4, 4.5, 5.6`.
- Sockets: equippable, level > 20, `U ≤ 0.175` → `U[1, max]` empty sockets (max 1 most slots; 2 bracer/pants/shield/quiver/scroll; 3 chest). Gems fill a socket with a slot-dependent stat.
- Inventory: 120 bag slots; equipment: helmet, chest, pants, boots, shoulders, gloves, amulet, bracer, ring ×2, weapon, offhand (shield/quiver/scroll).

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

## Perks
- Fields: id, name, category (MeleeOffense, RangedOffense, MagicOffense, Defense, Support, Utility), unlock path (any one of), requirements (all of), cost, level requirement, stackable (no max), tree position, effects.
- Buy: points ≥ cost, path and requirements met, level met, not bought unless stackable. Each purchase applies the effects again. No single refund; respec unbuys all perks and spells and sets points = level + permanent bonus.

## Spells
- Fields: id, level requirement, energy cost, base cooldown, channeled, cast on release.
- Loadout 6 slots; unlock costs 2 perk points.
- Cooldown ticks at `dt × cooldownRate / cooldown`; equipping starts the cooldown.
- Cost: energy part `cost × spellCostEnergyCost × spellCost`, stamina part `cost × (1 − spellCostEnergyCost) × spellCost`; channeled per second.
- 27 spells (level / energy / cooldown s): Black Hole 20/350/140, Healing Dome 6/80/70, Blink 3/25/14, Sun Flare 10/100/70, Sustain Shield 8/50 per s, Wide Reach 2/10/0.4, Black Flame 21/25/2.5, War Cry 14/85/180, Portal 12/90/60, Magic Arrow 27/100/20, Multishot 40/30/12, Golden Skin 35/65/160, Purge 7/40/16, Snap Freeze 20/220/40, Berserk 25/10/200, Ball Lightning 44/350/100, Bash 17/25/25, Frenzy 1/0/40, Seeking Arrow 27/50/5, Focus 4/40/20, Parry 8/35/60, Cataclysm 50/500/300, Blood Infused Arrow 11/75/8, Roaring Cheeks 55/400/360, Taunt 9/40/45, Firebolt 6/45/20, Snow Storm 9/120/20. Damage formulas per spell are in COS-25/COS-26.

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
