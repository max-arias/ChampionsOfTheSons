"""Extract game data (numbers, names, stat links) from a Champions of the Forest checkout into our content JSON.

Usage: python -I tools/extract_cotf.py <path-to-ChampionsOfForest> <content-dir> [reference.json]

Only data is carried over: stat ranges, item names, rarities, levels, stat pools and the stat changes
an item applies. Lore, descriptions and translations are not copied (the original repo has no license);
our own text lives in content/text/. The optional reference.json (keep it outside the repo) holds the
original's mechanic notes so writers can get numbers right without reusing its wording.
Unique effects that are not simple stat changes are flagged "manual" for COS-17.
"""
import json
import os
import re
import sys

SRC, OUT = sys.argv[1], sys.argv[2]
REF_OUT = sys.argv[3] if len(sys.argv) > 3 else None


def read(rel):
    with open(os.path.join(SRC, rel), encoding="utf-8-sig") as f:
        return f.read()


def snake(name):
    s = re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name)
    s = re.sub(r"(?<=[A-Z])([A-Z][a-z])", r"_\1", s)
    return re.sub(r"_+", "_", s).lower().strip("_")


def slug(text):
    return re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")


def matching(src, i, open_c="{", close_c="}"):
    """Index just past the bracket matching the one at src[i]. Skips strings and comments."""
    depth = 0
    j = i
    while j < len(src):
        c = src[j]
        if src.startswith("//", j):
            j = src.index("\n", j)
            continue
        if src.startswith("/*", j):
            j = src.index("*/", j) + 2
            continue
        if c == '"':
            j += 1
            while src[j] != '"':
                j += 2 if src[j] == "\\" else 1
            j += 1
            continue
        if c == open_c:
            depth += 1
        elif c == close_c:
            depth -= 1
            if depth == 0:
                return j + 1
        j += 1
    raise ValueError("unbalanced")


def split_top(s, sep=","):
    """Split on separators that are not inside brackets, strings or comments."""
    parts, depth, cur, j = [], 0, "", 0
    while j < len(s):
        c = s[j]
        if s.startswith("//", j):
            k = s.find("\n", j)
            j = len(s) if k < 0 else k
            continue
        if s.startswith("/*", j):
            k = s.index("*/", j) + 2
            cur += s[j:k]
            j = k
            continue
        if c == '"':
            k = j + 1
            while s[k] != '"':
                k += 2 if s[k] == "\\" else 1
            cur += s[j:k + 1]
            j = k + 1
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == sep and depth == 0:
            parts.append(cur.strip())
            cur = ""
        else:
            cur += c
        j += 1
    if cur.strip():
        parts.append(cur.strip())
    return parts


def comment_text(expr):
    m = re.search(r"/\*(.*?)\*/", expr, re.S)
    if m:
        return m.group(1).strip()
    m = re.search(r'"((?:[^"\\]|\\.)*)"', expr)
    return m.group(1) if m else None


def num(s):
    """A C# numeric literal or simple constant arithmetic like `1 / 2.5f`."""
    e = re.sub(r"(?<=[\d.])[fFdD]\b", "", s.strip())
    if not re.fullmatch(r"[\d.\s+\-*/()]+", e):
        raise ValueError(f"not a constant expression: {s!r}")
    return float(eval(e, {"__builtins__": {}}, {}))


# ---------------------------------------------------------------- player stats
def player_stats():
    src = read("Player/ModdedPlayer/ModdedPlayer.ModdedPlayerStats.cs")
    stats = []
    pat = re.compile(r"this\.(\w+)\s*=\s*new\s+(\w+?)PlayerStat(?:<(\w+)>)?\((.*?)\);")
    for m in pat.finditer(src):
        name, kind, typ, args = m.group(1), m.group(2), m.group(3), split_top(m.group(4))
        synced = "NetworkSynced" in kind
        kind = kind.replace("NetworkSynced", "")
        entry = {"id": snake(name), "legacyName": name}
        if kind == "Additive":
            entry.update(kind="add", default=num(args[0]))
        elif kind == "Multiplicative":
            entry.update(kind="mult", default=num(args[0]))
        elif kind == "Boolean":
            entry.update(kind="bool", default=1.0 if args and args[0].strip() == "true" else 0.0)
        elif kind == "MultiOperation":
            entry.update(kind="multi", default=num(args[0]), defaultMult=num(args[1]))
        else:
            raise ValueError(kind)
        if typ == "int":
            entry["integer"] = True
        fmt = next((a.strip('"') for a in args if a.startswith('"')), None)
        if fmt and fmt.startswith("P"):
            entry["percent"] = True
        if synced:
            entry["synced"] = True
        stats.append(entry)
    return stats


# ---------------------------------------------------------------- stat changes ("effects")
EFFECT_PATTERNS = [
    # ModdedPlayer.Stats.x.Add(Mathf.RoundToInt(f))
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.(Add|Substract|Multiply|Divide)\((.+)\)$"), "call"),
    # ModdedPlayer.Stats.x.valueMultiplicative *= expr
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.valueMultiplicative\s*\*=\s*(.+)$"), "mulassign"),
    # ModdedPlayer.Stats.x.valueAdditive += expr
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.valueAdditive\s*\+=\s*(.+)$"), "addassign"),
    # ModdedPlayer.Stats.x.valueAdditive -= expr / *= expr / ++
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.valueAdditive\s*-=\s*(.+)$"), "subassign"),
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.valueAdditive\s*\*=\s*(.+)$"), "mulassign"),
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.valueAdditive\s*\+\+$"), "inc"),
    # StatActions.AddX(f) or StatActions.AddX(0.13f): resolved through the StatActions table
    (re.compile(r"^StatActions\.(\w+)\(([\w.\-]+)\)$"), "action"),
    # ModdedPlayer.Stats.x.value = true
    (re.compile(r"^ModdedPlayer\.Stats\.(\w+)\.value\s*=\s*(true|false)$"), "set"),
]


def value_expr(expr, var="f"):
    """Turn a C# value expression into our effect value: a number, or a transform of the rolled value."""
    e = expr.strip().replace(" ", "")
    if re.fullmatch(r"-?[\d.]+[fF]?", e):
        return {"value": num(e)}
    table = {
        var: "x", f"Mathf.RoundToInt({var})": "round", f"-{var}": "neg", f"-Mathf.RoundToInt({var})": "neg_round",
        f"1-{var}": "one_minus", f"1+{var}": "one_plus", f"(1-{var})": "one_minus", f"(1+{var})": "one_plus",
    }
    if e in table:
        return {"transform": table[e]}
    m = re.fullmatch(rf"{var}\*([\d.]+)[fF]?|([\d.]+)[fF]?\*{var}", e)
    if m:
        return {"transform": "x", "scale": num(m.group(1) or m.group(2))}
    return None


ACTIONS = {}

# Original wording, kept out of the repo: only written when a reference path is given (mechanics lookup for writers).
REFERENCE = {"items": {}, "perks": {}}

NEGATED = {"x": "neg", "neg": "x", "round": "neg_round", "neg_round": "round"}


def negate(v):
    if "value" in v:
        return {**v, "value": -v["value"]}
    if v["transform"] not in NEGATED:
        raise ValueError(f"cannot negate {v}")
    return {**v, "transform": NEGATED[v["transform"]]}


def fold(effect, x):
    """An effect with a transform of x, evaluated for a fixed x (StatActions.AddAllStats(5))."""
    if "transform" not in effect:
        return effect
    x *= effect.get("scale", 1)
    t = effect["transform"]
    val = {"x": x, "round": round(x), "neg": -x, "neg_round": -round(x), "one_minus": 1 - x, "one_plus": 1 + x}[t]
    out = {k: v for k, v in effect.items() if k not in ("transform", "scale")}
    out["value"] = float(val)
    return out


def parse_effects(body, var="f"):
    """Parse a block of stat-change statements. Returns (effects, ok)."""
    effects = []
    stmts = [s.strip() for s in body.strip().strip("{}").split(";") if s.strip()]
    for st in stmts:
        st = re.sub(r"\s+", " ", st)
        for pat, kind in EFFECT_PATTERNS:
            m = pat.match(st)
            if not m:
                continue
            stat = snake(m.group(1))
            if kind == "call":
                op = {"Add": "add", "Substract": "add", "Multiply": "mult", "Divide": "div"}[m.group(2)]
                v = value_expr(m.group(3), var)
                if v is None:
                    return effects, False
                if m.group(2) == "Substract":
                    v = negate(v)
                effects.append({"stat": stat, "op": op, **v})
            elif kind == "addassign":
                v = value_expr(m.group(2), var)
                if v is None:
                    return effects, False
                effects.append({"stat": stat, "op": "add", **v})
            elif kind == "subassign":
                v = value_expr(m.group(2), var)
                if v is None:
                    return effects, False
                effects.append({"stat": stat, "op": "add", **negate(v)})
            elif kind == "inc":
                effects.append({"stat": stat, "op": "add", "value": 1.0})
            elif kind == "action":
                inner = ACTIONS.get(m.group(1))
                if inner is None:
                    return effects, False
                sub, ok = parse_effects(inner)
                if not ok:
                    return effects, False
                arg = m.group(2)
                if arg != var:
                    try:
                        lit = num(arg)
                    except ValueError:
                        return effects, False
                    sub = [fold(e, lit) for e in sub]
                effects += sub
            elif kind == "mulassign":
                v = value_expr(m.group(2), var)
                if v is None:
                    return effects, False
                effects.append({"stat": stat, "op": "mult", **v})
            else:
                effects.append({"stat": stat, "op": "enable"} if m.group(2) == "true" else {"stat": stat, "op": "disable"})
            break
        else:
            return effects, False
    return effects, True


def stat_actions():
    src = read("Items/StatActions.cs")
    actions = {}
    for m in re.finditer(r"public static void (\w+)\(float f\)\s*\{", src):
        start = m.end() - 1
        end = matching(src, start)
        actions[m.group(1)] = src[start:end]
    return actions


# ---------------------------------------------------------------- item stats
def item_stats():
    src = read("Items/ItemDataBase_StatDefinitions.cs")
    enum_block = src[src.index("public enum Stat"):]
    enum_block = enum_block[enum_block.index("{"):matching(enum_block, enum_block.index("{"))]
    enum, value = {}, 0
    for raw in enum_block.strip("{}").split(","):
        raw = re.sub(r"//.*", "", raw).strip()
        if not raw:
            continue
        if "=" in raw:
            k, v = [x.strip() for x in raw.split("=")]
            value = int(v)
        else:
            k = raw
        enum[k] = value
        value += 1
    by_value = {v: k for k, v in enum.items()}

    actions = stat_actions()
    ACTIONS.update(actions)
    combine = {"scAdd": "sum", "scMult": "product", "scMultPlusOne": "product_plus_one", "scOneMinusMult": "one_minus_product"}
    stats, problems = [], []
    body = src[src.index("public static void PopulateStats"):]
    i = None
    pos = 0
    token = re.compile(r"(?:int\s+)?i\s*=\s*(\d+)\s*;|i\+\+\s*;|new ItemStat\(")
    while True:
        m = token.search(body, pos)
        if not m:
            break
        if m.group(0).startswith("new ItemStat"):
            start = m.end() - 1
            end = matching(body, start, "(", ")")
            args = split_top(body[start + 1:end - 1])
            pos = end
            init = {}
            rest = body[end:end + 400].lstrip()
            if rest.startswith("{"):
                iend = matching(rest, 0)
                for part in split_top(rest[1:iend - 1]):
                    if "=" in part:
                        k, v = [x.strip() for x in part.split("=", 1)]
                        init[k] = v
            if args[0].strip() == "i++":
                sid = i
                i += 1
            else:
                sid = int(args[0]) if args[0].strip().isdigit() else i
            name = comment_text(args[4]) or by_value.get(sid, f"stat_{sid}").title()
            enum_name = by_value.get(sid)
            entry = {
                "id": "stat." + (enum_name.lower() if enum_name else slug(name)),
                "legacyId": sid,
                "name": name,
                "min": num(args[1]), "max": num(args[2]), "levelPow": num(args[3]),
                "combine": combine.get(args[5].strip(), args[5].strip()),
                "rarity": int(args[6]),
            }
            if init.get("DisplayAsPercent") == "true":
                entry["percent"] = True
            if "RoundingCount" in init:
                entry["rounding"] = int(init["RoundingCount"])
            if "ValueCap" in init:
                entry["cap"] = num(init["ValueCap"])
            if "Multipier" in init:
                entry["multiplier"] = num(init["Multipier"])
            # the add-action: StatActions.X or an inline lambda
            add = args[8].strip() if len(args) > 8 else ""
            eff, ok = None, False
            am = re.fullmatch(r"StatActions\.(\w+)", add)
            if am and am.group(1) in actions:
                eff, ok = parse_effects(actions[am.group(1)])
            else:
                lm = re.fullmatch(r"\(?\s*(\w+)\s*\)?\s*=>\s*(.+)", add, re.S)
                if lm:
                    eff, ok = parse_effects(lm.group(2), lm.group(1))
            if ok and eff:
                entry["effects"] = eff
            elif add == "null":
                entry["effects"] = []
            else:
                entry["effects"] = []
                entry["manual"] = True
                problems.append(f"stat {entry['id']}: {add[:120]}")
            consume = args[10].strip() if len(args) > 10 else ""
            if consume and consume != "null" and consume != add:
                entry["consumeAction"] = "manual"
            stats.append(entry)
        elif m.group(1):
            i = int(m.group(1))
            pos = m.end()
        else:
            i += 1
            pos = m.end()
    return stats, enum, problems


# ---------------------------------------------------------------- items
def items(enum):
    src = read("Items/ItemDataBase_ItemDefinitions.cs")
    sets = {"AkagiSet": "set.akagi", "BerserkSet": "set.berserk"}
    out, problems, used_ids = [], [], set()
    for m in re.finditer(r"new BaseItem\(\s*new (?:int|Stat)\s*\[\]\s*\[\]\s*", src):
        pstart = src.index("{", m.end() - 1)
        pend = matching(src, pstart)
        pools = []
        for pm in re.finditer(r"new (?:int|Stat)\s*\[\]\s*\{(.*?)\}", src[pstart:pend], re.S):
            pool = []
            for tok in pm.group(1).split(","):
                tok = re.sub(r"//.*|/\*.*?\*/", "", tok).strip()
                if not tok:
                    continue
                tok = tok.replace("Stat.", "")
                v = int(tok) if re.fullmatch(r"-?\d+", tok) else enum[tok]
                pool.append(None if v == 0 else "*" if v == -1 else v)
            pools.append(pool)
        close = src.index(")", pend)
        istart = src.index("{", close)
        iend = matching(src, istart)
        fields = {}
        for part in split_top(src[istart + 1:iend - 1]):
            if "=" in part:
                k, v = part.split("=", 1)
                fields[k.strip()] = v.strip()
        name = comment_text(fields["name"]) or "Unnamed"
        iid = "item." + slug(name)
        n = 2
        while iid in used_ids:
            iid = f"item.{slug(name)}_{n}"
            n += 1
        used_ids.add(iid)
        item = {
            "id": iid,
            "legacyIndex": len(out),
            "name": name,
            "type": snake(fields["type"].split(".")[-1]),
            "rarity": int(fields["Rarity"]),
            "minLevel": int(fields["minLevel"]),
            "maxLevel": int(fields["maxLevel"]),
            "statPools": pools,
        }
        if fields.get("StackSize", "1") != "1":
            item["stackSize"] = int(fields["StackSize"])
        if fields.get("CanConsume") == "true":
            item["consumable"] = True
        if "subtype" in fields:
            item["subtype"] = snake(fields["subtype"].split(".")[-1])
        if "weaponModel" in fields:
            item["weaponModel"] = snake(fields["weaponModel"].split(".")[-1])
        if "uniqueStat" in fields:
            REFERENCE["items"][iid] = comment_text(fields["uniqueStat"])
        equip = fields.get("onEquip")
        if equip:
            sm = re.fullmatch(r"\(\)\s*=>\s*(\w+)\.Equip\(\)", equip)
            if sm and sm.group(1) in sets:
                item["set"] = sets[sm.group(1)]
            else:
                lm = re.fullmatch(r"\(\)\s*=>\s*(.+)", equip, re.S)
                eff, ok = parse_effects(lm.group(1)) if lm else ([], False)
                if ok and eff:
                    item["uniqueEffects"] = eff
                else:
                    item["uniqueEffects"] = []
                    item["manual"] = True
                    flat = re.sub(r"\s+", " ", equip)
                    problems.append(f"{iid}: {flat[:150]}")
        if "onConsume" in fields:
            item["consumeAction"] = "manual"
        out.append(item)
    return out, problems


# ---------------------------------------------------------------- perks
def translations():
    src = read("Translations.cs")
    pat = re.compile(r'public string _(\w+) = "((?:[^"\\]|\\.)*)";')
    return {m.group(1): m.group(2).replace("\\n", " ").replace('\\"', '"') for m in pat.finditer(src)}


PERK_ALT_ID = 151


def perks():
    src = read("Player/Perks/PerkDatabase.cs")
    tr = translations()
    out, problems, used = [], [], set()
    for m in re.finditer(r"new Perk\(\)\s*\{", src):
        start = m.end() - 1
        end = matching(src, start)
        fields = {}
        for part in split_top(src[start + 1:end - 1]):
            if "=" in part:
                k, v = part.split("=", 1)
                fields[k.strip()] = v.strip()
        nm = re.match(r"Translations\.(\w+)", fields.get("name", ""))
        name = tr.get(nm.group(1)) if nm else comment_text(fields.get("name", ""))
        name = name or f"Perk {len(out)}"
        pid = "perk." + slug(name)
        n = 2
        while pid in used:
            pid = f"perk.{slug(name)}_{n}"
            n += 1
        used.add(pid)

        def ints(v):
            return [int(x) for x in re.findall(r"-?\d+", v or "")]

        perk = {
            "id": pid,
            "legacyIndex": len(out),
            "name": name,
            "category": snake(fields.get("category", "").split(".")[-1]),
            "cost": int(fields.get("cost", "1")),
            "levelReq": int(fields.get("levelReq", "1")),
            "stackable": fields.get("stackable") == "true",
            "x": num(fields.get("posX", "0")),
            "y": num(fields.get("posY", "0")),
            "unlockPath": ints(fields.get("unlockPath")),
            "requires": ints(fields.get("unlockRequirement")),
        }
        dm = re.match(r"Translations\.(\w+)(?:\((.*)\))?$", fields.get("originalDescription", ""), re.S)
        if dm:
            REFERENCE["perks"][pid] = {"template": tr.get(dm.group(1), ""), "args": " ".join((dm.group(2) or "").split())}
        apply = fields.get("onApply", "")
        lm = re.fullmatch(r"\(\)\s*=>\s*(.+)", apply, re.S)
        eff, ok = parse_effects(lm.group(1)) if lm else ([], False)
        perk["effects"] = eff if ok else []
        if not ok:
            perk["manual"] = True
            problems.append(f"{pid}: {' '.join(apply.split())[:140]}")
        if "canBuy" in fields:
            perk["canBuyManual"] = True
        out.append(perk)
    # COTF defines runtime perk 151 twice (single-player / multiplayer alternates); only one exists at
    # runtime, so unlock references to a runtime ID above 151 point at block index ID + 1 (COS-70).
    by_index = {i: out[i if i <= PERK_ALT_ID else i + 1]["id"] for i in range(len(out) - 1)}
    for p in out:
        p["unlockPath"] = [by_index[i] for i in p["unlockPath"] if i >= 0 and i in by_index]
        p["requires"] = [by_index[i] for i in p["requires"] if i >= 0 and i in by_index]
    return out, problems


# Names/ids from the original we don't ship (real-world politics or religion): renamed on extraction.
ID_RENAMES = {"item.maga_cap": ("item.sunscorched_cap", "Sunscorched Cap"),
              "item.jihad_vest": ("item.zealots_powder_vest", "Zealot's Powder Vest")}

# Items cut to keep the catalog focused (near-duplicate families, filler, items tied to the original). Dropped on extraction.
EXCLUDE = {
    "item.300th_spear",
    "item.ancient_greatbow",
    "item.archer_s_gear",
    "item.armored_boots",
    "item.armsy_finger_necklace",
    "item.armsy_skin_footwear",
    "item.army_boots",
    "item.assassins_pauldrons",
    "item.axe_of_misfortune",
    "item.band_of_hurting",
    "item.bear_skin_armor",
    "item.boar_skin_armor",
    "item.bombastinc_choker",
    "item.bone_shield",
    "item.brawler_s_gloves",
    "item.broken_protector",
    "item.buckler",
    "item.bunny_ears_hairband",
    "item.cargo_shorts",
    "item.cargo_shorts_mk2",
    "item.cowman_toe_necklace",
    "item.crocodile_skin_armor",
    "item.crusader_helmet",
    "item.damaged_army_boots",
    "item.dark_oak_shield",
    "item.demoman_s_vest",
    "item.diamond_pendant_of_agility",
    "item.diamond_pendant_of_intelligence",
    "item.diamond_pendant_of_strength",
    "item.diamond_pendant_of_vitality",
    "item.doom_pauldrons",
    "item.dull_axe",
    "item.emerald_pendant_of_agility",
    "item.emerald_pendant_of_intelligence",
    "item.emerald_pendant_of_strength",
    "item.emerald_pendant_of_vitality",
    "item.etched_mantle",
    "item.explosive_touch",
    "item.fate_boots",
    "item.fate_gloves",
    "item.full_metal_sword",
    "item.giant_polearm",
    "item.golden_axe_of_fortune",
    "item.golden_locket",
    "item.golden_locket_of_agility",
    "item.golden_locket_of_intelligence",
    "item.golden_locket_of_strength",
    "item.golden_locket_of_vitality",
    "item.golden_ring_of_agility",
    "item.golden_ring_of_intelligence",
    "item.golden_ring_of_strength",
    "item.golden_ring_of_vitality",
    "item.greatbow",
    "item.gunpowder_boxers",
    "item.gunpowder_filled_socks",
    "item.hand_held_ballista",
    "item.hazard_s_gear",
    "item.healer_s_bracers",
    "item.healer_s_gloves",
    "item.heavy_shoulder_plates",
    "item.hood_2",
    "item.horned_helmet",
    "item.hubble_s_vision",
    "item.iron_gauntlet",
    "item.iron_plate_full_of_holes",
    "item.javelin",
    "item.knife_on_a_stick",
    "item.leather_bracer",
    "item.leather_shoulder_armor",
    "item.life_ring",
    "item.long_lost_quiver",
    "item.longsword",
    "item.mana_ring",
    "item.mask",
    "item.mask_of_madness",
    "item.maximale_qualit_t",
    "item.megan_s_locket",
    "item.moon_boots",
    "item.moritz_s_gear",
    "item.nail_gloves",
    "item.novice_magic_caster_s_bracers",
    "item.novice_magic_caster_s_gloves",
    "item.old_boots",
    "item.old_buckler",
    "item.piercer",
    "item.pirate_pants",
    "item.plate_armour",
    "item.plate_leggins",
    "item.rabbit_ears_hairband",
    "item.ranger_s_bracers",
    "item.ranger_s_gloves",
    "item.red_skirt",
    "item.ring_of_fortune",
    "item.rough_hide_leggins",
    "item.round_shield",
    "item.rusty_javelin",
    "item.rusty_longsword",
    "item.sage_s_robes",
    "item.severer",
    "item.shield_blade",
    "item.shoulder_guards",
    "item.silver_locket_of_intelligence",
    "item.silver_locket_of_strength",
    "item.silver_locket_of_vitality",
    "item.silver_ring_of_agility",
    "item.silver_ring_of_intelligence",
    "item.silver_ring_of_strength",
    "item.silver_ring_of_vitality",
    "item.spellbound_quiver",
    "item.spiked_ring",
    "item.star_robe",
    "item.steel_ring_of_intelligence",
    "item.steel_ring_of_strength",
    "item.steel_ring_of_vitality",
    "item.straw_hat",
    "item.sturdy_leather_boots",
    "item.swordsman_s_bracers",
    "item.swordsman_s_gloves",
    "item.the_executioner",
    "item.the_leech",
    "item.titanium_leggins",
    "item.torso_of_strength",
    "item.tower_shield",
    "item.tribal_glove",
    "item.tribe_leader_glove",
    "item.virginia_heart_pendant",
    "item.volatile_bracers",
    "item.volatile_helmet",
    "item.warplate",
    "item.zealots_powder_vest",
}

# Known bugs in the original data, fixed on extraction: (kind, id, stat, field, new value, why).
FIXES = [
    ("perk", "perk.melee_mastery", "attack_speed", "value", 0.25,
     "original code adds +125% attack speed; its own description says 25%"),
]


def apply_fixes(items, perks):
    by_kind = {"item": {i["id"]: i for i in items}, "perk": {p["id"]: p for p in perks}}
    for kind, eid, stat, field, value, why in FIXES:
        entry = by_kind[kind].get(eid)
        effects = (entry or {}).get("effects" if kind == "perk" else "uniqueEffects", [])
        hit = [e for e in effects if e["stat"] == stat]
        if not hit:
            print(f"  FIX NOT APPLIED {eid} {stat}: target not found")
            continue
        for e in hit:
            e[field] = value
            e["fixed"] = why


def main():
    os.makedirs(OUT, exist_ok=True)
    ps = player_stats()
    st, enum, p1 = item_stats()
    it, p2 = items(enum)
    pk, p3 = perks()
    apply_fixes(it, pk)
    it = [i for i in it if i["id"] not in EXCLUDE]
    for item in it:
        if item["id"] in ID_RENAMES:
            item["id"], item["name"] = ID_RENAMES[item["id"]]
    # stat pools reference legacy numeric stat ids -> map to our stat ids
    by_legacy = {s["legacyId"]: s["id"] for s in st}
    for item in it:
        item["statPools"] = [[x if x in (None, "*") else by_legacy.get(x, f"stat.unknown_{x}") for x in pool] for pool in item["statPools"]]

    def dump(name, key, data):
        with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump({key: data}, f, indent=2, ensure_ascii=False)
            f.write("\n")

    dump("player_stats.json", "playerStats", ps)
    dump("item_stats.json", "itemStats", st)
    dump("items.json", "items", it)
    dump("perks.json", "perks", pk)
    if REF_OUT:
        with open(REF_OUT, "w", encoding="utf-8") as f:
            json.dump(REFERENCE, f, indent=1, ensure_ascii=False)
    print(f"player stats {len(ps)}, item stats {len(st)}, items {len(it)}")
    print(f"perks {len(pk)}")
    print(f"manual: {sum(1 for s in st if s.get('manual'))} stats, {sum(1 for i in it if i.get('manual'))} items, {sum(1 for p in pk if p.get('manual'))} perks")
    for p in p1 + p2 + p3:
        print("  MANUAL", p)


main()
