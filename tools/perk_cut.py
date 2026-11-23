"""COS-176 perk cut (Max, 2026-11-23): about 15 general perks per tree; spell-specific perks become spell mastery upgrades that
unlock by casting the spell (no points); everything else goes.

Writes content/perks.json (kept tree perks with repaired unlock paths, mastery perks under the hidden "mastery" category, cut
perks removed), content/spells.json ("mastery": [perk ids in unlock order]) and drops the cut perks' text from every language
table. Rerunnable: perks already cut are simply absent. Run tools/perk_layout.py afterwards for the tree shapes.

  python -I tools/perk_cut.py            # apply
  python -I tools/perk_cut.py --check    # print what would happen
"""
import glob
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTENT = ROOT / "content"

KEEP = {
    "melee_offense": ["stronger_hits", "arm_strength", "body_strength", "grip_strength", "combat_regen", "attack_speed", "power_swing",
                      "charged_attack", "lucky_hits", "bleed", "overwhelming_odds", "super_charged_attack", "heaviest_blow", "melee_mastery",
                      "curse_of_strengthening"],
    "ranged_offense": ["stronger_projectiles", "projectile_damage", "reusability_i", "bow_specialization", "crossbow_specialization",
                       "pistol_specialization", "spear_specialization", "target_practise", "sniper", "bullseye", "hawk_s_eye", "endless_quiver",
                       "double_spears", "ranged_mastery", "true_aim"],
    "magic_offense": ["stronger_spells", "transmutation", "cooldown_reduction", "resource_cost_reduction", "greater_cooldown_reduction",
                      "spell_power", "overcharge", "greater_cooldown_reduction_2", "greater_cooldown_reduction_3", "greater_cooldown_reduction_4",
                      "infinity", "armageddon", "curse_of_power", "curse_of_speed", "the_real_armageddon"],
    "defense": ["more_health", "armor", "bonus_health", "enhanced_vitality", "thorny_skin", "magic_resistance", "spikes", "durability",
                "near_death_experience", "iron_maiden", "heavy_metal", "block", "dodge", "improved_dodges", "deployable_shield"],
    "support": ["more_healing", "inner_fire", "armor_penetration", "energy_generation", "fiery_embrace", "health_regen", "insight",
                "combat_energy_regen", "combat_health_regen", "flame_lifetime", "pyromancy", "insight_ii", "rejuvenation", "lingering_flames",
                "restoration"],
    "utility": ["luck_enchantment", "luck_enchantment_ii", "looting", "metabolism", "endurance", "enhanced_energy", "jump", "light_foot",
                "momentum_transfer", "more_ammo", "more_building_resources", "more_carried_logs", "transporter", "light_the_way",
                "explosion_damage_up"],
}

# Spell -> its upgrade perks; they unlock in order of their old level requirement. Upgrades with a built-in downside (Cursed Arrow,
# Dance of the Firegod) are cut rather than forced on the player by casting.
MASTERY = {
    "parry": ["counter_strike", "flame_guard", "full_counter", "parry_range", "instant_riposte", "parry_mastery"],
    "bash": ["concussive_bash", "lucky_bashes", "skull_basher", "skull_basher_ii"],
    "focus": ["focus_perfection", "focus_quickscope", "focus_quickerscope", "focus_cripple"],
    "blood_infused_arrow": ["arcane_blood", "near_death_arrow", "concussive_shot", "critical_infusion"],
    "seeking_arrow": ["seeking_arrow_crippling_precision", "seeking_arrow_head_hunting", "seeking_arrow_improved_memory",
                      "seeking_arrow_movement_impairing_arrows"],
    "multishot": ["multishot_empower"],
    "firebolt": ["firebolt_upgrade", "firebolt_upgrade_2", "firebolt_cost_reduction"],
    "sustain_shield": ["autocast_shield", "shield_endurance"],
    "taunt": ["falter", "line_up"],
    "war_cry": ["empowered_war_cry"],
    "frenzy": ["frenzy_haste", "mania", "fury_swipes"],
    "black_flame": ["enchant_weapon", "afterburn", "netherflame", "breathing_tehniques"],
    "magic_arrow": ["exposure", "magic_arrow_devastation", "disabler", "magic_binding", "endless_stream"],
    "blink": ["blink_passthrough", "blink_wormhole", "quick_silver"],
    "sun_flare": ["wrath_of_the_sun"],
    "berserk": ["pound_of_flesh", "sudden_rampage"],
    "golden_skin": ["golden_resolve", "golden_cooldown"],
    "ball_lightning": ["nuke_conjuration", "storm_season", "endless_storm", "storm_of_the_century"],
    "black_hole": ["the_big_suck", "event_horizon_expansion", "event_horizon_expansion_2"],
    "purge": ["closing_wounds"],
    "healing_dome": ["energy_field", "temple_of_protection", "magic_tower", "safe_heaven", "pantheon_of_champions", "time_of_need"],
}


def load(name):
    return json.load(open(CONTENT / name, encoding="utf-8"))


def save(name, data):
    open(CONTENT / name, "w", encoding="utf-8", newline="\n").write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")


def main():
    check = "--check" in sys.argv
    perks_doc, spells_doc = load("perks.json"), load("spells.json")
    perks = {p["id"]: p for p in perks_doc["perks"]}
    keep = {"perk." + i for ids in KEEP.values() for i in ids}
    mastery_of = {"perk." + i: "spell." + s for s, ids in MASTERY.items() for i in ids}
    missing = [i for i in keep | set(mastery_of) if i not in perks and not any(p.get("category") == "mastery" and p["id"] == i for p in perks.values())]
    if missing:
        sys.exit(f"unknown perk ids: {missing}")
    for cat, ids in KEEP.items():
        wrong = [i for i in ids if perks["perk." + i]["category"] != cat]
        if wrong:
            sys.exit(f"{cat}: not in that tree: {wrong}")

    def kept_ancestors(pid, seen=()):
        """The nearest kept tree perks above pid (pid itself if kept)."""
        if pid in keep:
            return [pid]
        if pid in seen or pid not in perks:
            return []
        out = []
        for q in perks[pid].get("unlockPath", []):
            for a in kept_ancestors(q, seen + (pid,)):
                if a not in out:
                    out.append(a)
        return out

    new_perks = []
    for p in perks_doc["perks"]:
        pid = p["id"]
        if pid in keep:
            path = []
            for q in p.get("unlockPath", []):
                for a in kept_ancestors(q):
                    if a not in path and a != pid:
                        path.append(a)
            p["unlockPath"] = path
            p["requires"] = [q for q in p.get("requires", []) if q in keep]
            new_perks.append(p)
        elif pid in mastery_of:
            p["category"] = "mastery"
            p["unlockPath"] = []
            p["requires"] = []
            new_perks.append(p)
    cut = sorted(set(perks) - {p["id"] for p in new_perks})

    for s in spells_doc["spells"]:
        key = s["id"].split(".", 1)[1]
        if key in MASTERY:
            s["mastery"] = ["perk." + i for i in MASTERY[key]]
        else:
            s.pop("mastery", None)

    print(f"kept {len(keep)} tree perks, {len(mastery_of)} mastery perks, cut {len(cut)}")
    for cat in KEEP:
        roots = [p["id"] for p in new_perks if p["category"] == cat and not p["unlockPath"]]
        print(f"  {cat}: {len(KEEP[cat])} perks, roots {len(roots)}")
    if check:
        print("cut:", ", ".join(c.replace("perk.", "") for c in cut))
        return
    perks_doc["perks"] = new_perks
    save("perks.json", perks_doc)
    save("spells.json", spells_doc)
    for f in glob.glob(str(CONTENT / "text" / "*.json")):
        t = json.load(open(f, encoding="utf-8"))
        drop = [k for k in t if any(k.startswith(c + ".") for c in cut)]
        if drop:
            for k in drop:
                del t[k]
            open(f, "w", encoding="utf-8", newline="\n").write(json.dumps(t, ensure_ascii=False, indent=1) + "\n")
    print("written")


if __name__ == "__main__":
    main()
