#!/usr/bin/env python
"""Build content/icons/<id>.png from the game-icons.net SVG set (CC BY 3.0).

  python -I tools/icons/generate_icons.py fetch            # download the SVG set into tools/icons/cache/
  python -I tools/icons/generate_icons.py match            # (re)build tools/icons/mapping.json
  python -I tools/icons/generate_icons.py render [--force] [--only ID ...]

Matching is automatic (keywords from the entry name + type), then overridden by
tools/icons/overrides.json ({"item.some_id": "author/icon-slug"}). Rendering is
deterministic, resumable (skips existing PNGs) and needs: pillow, numpy, resvg-py.
"""
import argparse, collections, io, json, re, urllib.request, zipfile, zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
CACHE = HERE / "cache"
SRC = CACHE / "icons-master"
CONTENT = ROOT / "content"
OUT = CONTENT / "icons"
ZIP_URL = "https://github.com/game-icons/icons/archive/refs/heads/master.zip"
SIZE = 128
PLACEHOLDER = "placeholder"  # deliberate eye-catching "missing art" icon, listed in placeholders.md


def load_entries():
    ents = []
    text = json.load(open(CONTENT / "text" / "en.json", encoding="utf8"))
    for f in ("items", "items_island"):
        for i in json.load(open(CONTENT / f"{f}.json", encoding="utf8"))["items"]:
            ents.append(dict(id=i["id"], kind="item", type=i["type"], name=i["name"], rarity=i.get("rarity", 0)))
    for i in json.load(open(CONTENT / "perks.json", encoding="utf8"))["perks"]:
        ents.append(dict(id=i["id"], kind="perk", type=i["category"], name=i["name"], rarity=0))
    for i in json.load(open(CONTENT / "spells.json", encoding="utf8"))["spells"]:
        ents.append(dict(id=i["id"], kind="spell", type="spell", name=i["name"], rarity=0))
    for i in json.load(open(CONTENT / "sets.json", encoding="utf8"))["sets"]:
        ents.append(dict(id=i["id"], kind="set", type="set", name=i["name"], rarity=0))
    for e in ents:  # current display names (the content name may be stale after naming passes)
        e["name"] = text.get(e["id"] + ".name", e["name"])
        e["desc"] = text.get(e["id"] + ".description", text.get(e["id"] + ".flavor", ""))
    return ents


def fetch():
    CACHE.mkdir(exist_ok=True)
    print("downloading", ZIP_URL)
    data = urllib.request.urlopen(ZIP_URL).read()
    zipfile.ZipFile(io.BytesIO(data)).extractall(CACHE)
    print("ok", SRC)


STOP = {"of", "the", "a", "an", "and", "s", "i", "ii", "iii", "iv", "v", "to", "in", "on", "for", "with", "all",
        "more", "bonus", "greater", "lesser"}
TYPE_TOK = {
    "boot": ["boots", "boot", "shoe", "sandal", "foot"],
    "weapon": ["sword", "blade", "axe", "mace", "spear", "dagger", "knife", "hammer", "club", "bow", "staff", "cleaver", "scythe", "halberd", "flail", "katana", "scimitar", "rapier", "lance", "trident", "crossbow", "wand", "whip", "glaive", "sickle", "pike", "naginata", "halberd", "javelin", "broadsword", "longsword", "claymore"],
    "amulet": ["necklace", "amulet", "pendant", "locket"],
    "ring": ["ring", "diamond", "signet"],
    "chest_armor": ["breastplate", "armor", "chest", "vest", "robe", "cuirass"],
    "helmet": ["helmet", "hood", "crown", "cap", "visored"],
    "glove": ["gloves", "gauntlet", "hand", "fist"],
    "pants": ["trousers", "greaves", "pants", "belt", "leg"],
    "bracer": ["bracers", "bracer", "wristband", "wrist"],
    "shoulder_armor": ["spaulders", "pauldron", "shoulder", "epaulet"],
    "shield": ["shield", "buckler"],
    "quiver": ["quiver", "arrow", "arrows"],
    "spell_scroll": ["scroll", "spellbook", "book", "rune"],
    "material": ["ore", "gem", "crystal", "bone", "herb", "leaf", "stone"],
    "other": ["gem", "key", "orb", "potion"],
    "melee_offense": ["sword", "fist", "punch", "axe"],
    "ranged_offense": ["arrow", "bow", "crosshair", "archery", "target"],
    "magic_offense": ["magic", "spell", "wand", "lightning", "orb", "pentagram"],
    "defense": ["shield", "armor", "defensive", "helmet"],
    "support": ["heal", "heart", "aura", "plus", "cross", "shining"],
    "utility": ["gears", "key", "cog", "compass", "hourglass", "boot"],
    "spell": ["magic", "spell", "orb", "lightning"],
    "set": ["crown", "emblem", "sigil", "banner", "crest", "shield"],
}
SYN = {
    "flipflops": ["sandal"], "shorts": ["trousers", "pants"], "cargo": ["trousers"],
    "mantle": ["cloak", "cape"], "cape": ["cloak", "cape"], "hairband": ["crown", "ears"],
    "ears": ["rabbit", "ears"], "bunny": ["rabbit"], "scarf": ["scarf", "cloak"], "locket": ["locket", "necklace"],
    "pendant": ["necklace", "pendant"], "wristguard": ["bracers", "wristband"],
    "armor": ["armor", "breastplate"], "armour": ["armor"], "pierce": ["pierce", "arrow"], "piercing": ["pierce", "arrow"],
    "penetration": ["pierce", "arrow"], "regen": ["heart", "heal"], "health": ["heart", "health"], "healing": ["heal", "heart"],
    "stamina": ["run", "lungs", "heart"], "energy": ["lightning", "battery", "energy"], "speed": ["running", "boot", "fast"],
    "jump": ["jump", "spring"], "dodge": ["dodge", "run"], "cooldown": ["hourglass", "stopwatch"],
    "durability": ["anvil", "shield"], "resistance": ["shield", "aura"], "damage": ["sword", "damage", "hit"],
    "crit": ["crosshair", "skull"], "critical": ["crosshair", "skull"], "strength": ["muscle", "biceps", "fist"],
    "intelligence": ["brain", "book"], "agility": ["run", "boot"], "attributes": ["star"], "projectile": ["arrow"],
    "caliber": ["bullet", "cannon"], "spell": ["spell", "magic"], "spells": ["spell", "magic"], "melee": ["sword"],
    "fire": ["fire", "flame"], "frost": ["ice", "snowflake"], "ice": ["ice", "snowflake"], "poison": ["poison", "skull"],
    "blood": ["blood", "drop"], "shadow": ["shadow", "ghost"], "holy": ["holy", "halo"], "snow": ["snowflake", "snow"],
    "storm": ["tornado", "lightning"], "flare": ["sun"], "moonstone": ["gem", "moon"], "tooth": ["tooth", "fangs"],
    "bone": ["bone", "bones"], "iron": ["anvil"], "steel": ["anvil"], "golden": ["gold"], "ruby": ["ruby", "gem"],
    "emerald": ["emerald", "gem"], "sapphire": ["sapphire", "gem"], "amethyst": ["gem"], "javelin": ["spear", "javelin"],
    "longsword": ["sword", "broadsword"], "greatsword": ["broadsword"], "executioner": ["axe", "guillotine"],
    "chastity": ["padlock", "belt"], "loop": ["ring"], "tome": ["book"], "grimoire": ["book", "spellbook"],
}


def words(name):
    n = re.sub(r"[^a-z0-9 ]", "", name.lower().replace("-", ""))
    return [w for w in n.split() if w not in STOP]


BANNED = {"hammer-sickle", "cultist", "executioner-hood", "all-seeing-eye", "cross", "iron-cross", "celtic-cross", "cross-shield",
          "pentagram-rose", "pentacle", "heptagram", "ankh", "eye-of-horus", "sheikah-eye", "spiked-halo", "prayer-beads", "templar-shield",
          "templar-eye", "templar-heart", "american-shield", "magic-shield", "hood", "warlock-hood", "ribbon", "fist", "crown-of-thorns", "boxing-ring", "bow-tie-ribbon", "ribbon-medal", "ribbon-shield", "broken-ribbon", "sausages-ribbon", "spinning-ribbons", "nazi", "swastika"}  # real-world political/religious symbols


def index_icons():
    return [(f"{s.parent.name}/{s.stem}", set(s.stem.split("-")), s.stem) for s in sorted(SRC.glob("*/*.svg")) if s.stem not in BANNED]


def h32(s):
    return zlib.crc32(s.encode())


# Same-type alternatives: when two items of a type map to one glyph, the later one gets an unused glyph from its pool.
POOLS = {
    "ring": "ring diamond-ring skull-ring big-diamond-ring power-ring globe-ring fire-ring skull-signet frozen-ring".split(),
    "amulet": "necklace pearl-necklace double-necklace heart-necklace gem-necklace emerald-necklace feather-necklace intricate-necklace primitive-necklace necklace-display tribal-pendant gem-pendant pendant-key".split(),
    "glove": "gloves gauntlet winter-gloves cooking-glove mailed-fist hand-bandage metal-hand".split(),
    "shoulder_armor": "shoulder-armor spiked-shoulder-armor shoulder-scales".split(),
    "bracer": "bracer bracers arm-bandage hand-bandage".split(),
    "pants": "trousers shorts underwear-shorts skirt leg-armor greaves armored-pants metal-skirt leg armor-cuisses".split(),
    "boot": "boots leather-boot steeltoe-boots metal-boot rubber-boot fur-boot chelsea-boot cowboy-boot walking-boot ski-boot tabi-boot sticky-boot".split(),
    "chest_armor": "chest-armor leather-armor armor-vest kevlar-vest leather-vest breastplate scale-mail mail-shirt trench-body-armor abdominal-armor spiked-armor heart-armor".split(),
    "shield": "shield bordered-shield checked-shield cracked-shield crenulated-shield dice-shield dragon-shield edged-shield eye-shield fire-shield griffin-shield heart-shield ice-shield bolt-shield riot-shield round-shield skull-shield spiked-shield roman-shield attached-shield bell-shield acid-shield winged-shield broken-shield slashed-shield tribal-shield viking-shield".split(),
}
# Name word -> small emblem glyph for 'base+emblem' variants (used when the pool has no unused glyph left).
THEME = {
    "iron": "anvil", "steel": "anvil", "titanium": "anvil", "forged": "anvil", "plate": "anvil", "chromium": "anvil", "tungsten": "anvil", "lead": "anvil",
    "gold": "gold-bar", "golden": "gold-bar", "gilt": "gold-bar", "gilded": "gold-bar", "greed": "gold-bar",
    "silver": "moon", "moon": "moon", "moonstone": "moon", "midnight": "moon", "emerald": "emerald", "diamond": "diamond",
    "frost": "snowflake-2", "ice": "snowflake-2", "whiteout": "snowflake-2", "glacier": "snowflake-2", "frozen": "snowflake-2", "snow": "snowflake-2", "frostbound": "snowflake-2", "frostbitten": "snowflake-2",
    "fire": "flame", "flame": "flame", "ember": "flame", "scorched": "flame", "scorch": "flame", "cinder": "flame", "soot": "flame", "burned": "flame", "burn": "flame", "pyromancy": "flame", "heat": "flame",
    "blood": "blood", "vampire": "blood", "vampiric": "blood", "gorged": "blood", "biting": "fangs", "crimson": "blood", "red": "blood", "oath": "blood", "debt": "blood",
    "skull": "skull", "bone": "bone-gnawer", "knucklebone": "skull", "tribal": "tribal-pendant", "tribe": "tribal-pendant", "twin": "skull", "cultist": "candle-flame", "cult": "candle-flame", "hushed": "candle-flame", "ritual": "candle-flame",
    "cube": "cube", "cubestruck": "cube", "seamless": "cube", "humming": "cube", "cubeglass": "cube", "wormhole": "cube",
    "leather": "animal-hide", "hide": "animal-hide", "fur": "animal-hide", "rabbit": "rabbit", "bear": "animal-hide", "boar": "animal-hide",
    "spiked": "spikes", "nail": "spikes", "nails": "spikes", "barbed": "spikes", "thorn": "spikes", "jagged": "spikes", "razor": "spikes",
    "golf": "golf-tee", "fairway": "golf-tee", "clubhouse": "golf-tee", "nine": "golf-tee", "bunker": "cog", "printed": "cog", "polymer": "cog", "keycard": "cog", "gps": "cog", "locator": "cog", "factory": "cog", "gears": "gears",
    "poison": "poison-bottle", "toxic": "poison-bottle", "acid": "poison-bottle", "explosive": "dynamite", "gunpowder": "dynamite", "demolition": "dynamite", "volatile": "dynamite", "powder": "dynamite",
    "brawler": "sword-clash", "survivor": "heart", "sprinter": "running-ninja", "tinker": "gears", "heart": "heart", "life": "heart", "vitality": "heart", "medic": "heart", "healer": "heart",
    "shark": "shark-fin", "whale": "shark-fin", "megalodon": "shark-fin", "feather": "feather", "crow": "feather", "water": "drop", "tiger": "tiger-head", "rusty": "broken-bone", "broken": "broken-bone", "damaged": "broken-bone",
    "old": "anchor", "worn": "anchor", "ancient": "anchor", "light": "sun", "sun": "sun", "sunscorched": "sun", "travel": "compass", "tracker": "compass", "hunter": "compass", "duelist": "sword-clash", "initiate": "key", "keepsake": "key", "key": "key",
    "lantern": "lantern", "wired": "gears", "precise": "compass", "long": "arrow-cluster",
}
NEUTRAL = "leaf feather key anchor sun compass drop gears moon tooth".split()


def uniquify(ents, res, by_slug, locked=()):
    """No two entries of one kind+type share a visual: first try an unused pool glyph, then add a small theme emblem."""
    def key_of(slug):
        return by_slug.get(slug, slug)

    pools = {t: [key_of(x) for x in v if x in by_slug and x not in BANNED] for t, v in POOLS.items()}
    emblem_theme = {w: key_of(v) for w, v in THEME.items() if v in by_slug}
    neutral = [key_of(x) for x in NEUTRAL if x in by_slug]
    groups = collections.defaultdict(list)
    for e in ents:
        groups[(e["kind"], e["type"])].append(e)
    for (kind, typ), g in groups.items():
        g.sort(key=lambda e: (e["id"] not in locked, -e["rarity"], e["id"]))
        used_full, used_base = set(), set()
        for e in g:
            cur = res[e["id"]]
            if cur == PLACEHOLDER:
                continue
            base = cur.split("+")[0]
            if base in used_base:
                alts = [] if e["id"] in locked else [x for x in pools.get(typ, []) if x not in used_base]
                if alts:
                    nb = alts[h32(e["id"]) % len(alts)]
                    cur = nb + ("+" + cur.split("+")[1] if "+" in cur else "")
                    base = nb
            if cur in used_full or (base in used_base and False):
                pass
            if base in used_base:
                emb = None
                cands = [emblem_theme[w] for w in words(e["name"]) if w in emblem_theme] + neutral
                for c in cands:
                    if c != base and base + "+" + c not in used_full:
                        emb = c
                        break
                assert emb, "no emblem for " + e["id"]
                cur = base + "+" + emb
            res[e["id"]] = cur
            used_full.add(cur)
            used_base.add(base)
    return res


def match(ents, overrides):
    idx = index_icons()
    by_slug = {k.split("/")[1]: k for k, _, _ in idx}
    overrides = {i: by_slug.get(v, v) for i, v in overrides.items()}
    for v in overrides.values():
        if v == PLACEHOLDER:
            continue
        assert (SRC / (v + ".svg")).exists(), "unknown icon " + v
    used = collections.Counter()
    res = {}
    for e in sorted(ents, key=lambda e: (-len(words(e["name"])), e["id"])):
        if e["id"] in overrides:
            res[e["id"]] = overrides[e["id"]]
            used[overrides[e["id"]]] += 1
            continue
        ws = words(e["name"])
        strong, syn = set(ws), set()
        for w in ws:
            syn.update(SYN.get(w, []))
            if w.endswith("s"):
                strong.add(w[:-1])
        typ = set(TYPE_TOK.get(e["type"], []))
        best, bs = None, -1e9
        for key, toks, slug in idx:
            if e["kind"] == "item" and not (typ & toks) and not (e["type"] in ("material", "other", "spell_scroll") and (slug in strong or slug in syn)):
                continue
            s = 5 * len(strong & toks) + 3 * len(syn & toks) + 1.5 * len(typ & toks)
            if slug in strong:
                s += 4
            if s <= 0:
                continue
            s -= 0.3 * max(0, len(toks) - 2)
            s -= 4 * used[key]
            s += (h32(e["id"] + key) % 1000) / 4000
            if s > bs:
                bs, best = s, key
        best = best or "lorc/magic-swirl"
        res[e["id"]] = best
        used[best] += 1
    return uniquify(ents, res, by_slug, set(overrides))


RARITY = ["#b9b9b9", "#5fd05f", "#4aa3ff", "#ecd84a", "#b266ff", "#ff8c2e", "#ff3d3d", "#ff55dd"]
CAT = {"melee_offense": "#e8603c", "ranged_offense": "#8ac24a", "magic_offense": "#9a6bff", "defense": "#5aa0e8",
       "support": "#4ed6a0", "utility": "#e8c25a", "spell": "#5ad8ff", "set": "#ffd24a"}


def tint(e):
    return RARITY[min(7, e["rarity"])] if e["kind"] == "item" else CAT.get(e["type"], "#cccccc")


def hex2rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def over(top, bot):
    import numpy as np
    ta, ba = top[..., 3:4], bot[..., 3:4]
    oa = ta + ba * (1 - ta)
    rgb = np.where(oa > 0, (top[..., :3] * ta + bot[..., :3] * ba * (1 - ta)) / np.maximum(oa, 1e-6), 0)
    return np.concatenate([rgb, oa], -1)


def render_placeholder():
    import numpy as np
    n = 16
    yy, xx = np.mgrid[0:n * 8, 0:n * 8]
    chk = ((yy // 16 + xx // 16) % 2).astype(np.float32)
    out = np.zeros((n * 8, n * 8, 4), np.float32)
    out[..., 0] = 255
    out[..., 1] = chk * 40
    out[..., 2] = 255 * (1 - chk * 0.0)
    out[..., 3] = 1
    d = np.abs(yy - xx) < 6
    d |= np.abs(yy + xx - (n * 8 - 1)) < 6
    out[d, :3] = 20
    # rounded-ish margin: fade the border to transparent
    m = 6
    out[:m] = out[-m:] = 0
    out[:, :m] = out[:, -m:] = 0
    return out


def glyph_mask(key, size):
    """White-on-black SVG -> L mask at size x size (luminance: some icons have black cut-outs)."""
    import resvg_py
    from PIL import Image
    svg = (SRC / (key + ".svg")).read_text(encoding="utf8")
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size))
    return Image.open(io.BytesIO(png)).convert("L")


def render_one(e, key):
    """key is a glyph ('author/slug'), PLACEHOLDER, or 'base+emblem' (small emblem glyph in the lower right corner)."""
    if key == PLACEHOLDER:
        return render_placeholder()
    import numpy as np
    from PIL import Image, ImageFilter
    big = 4 * SIZE
    parts = key.split("+")
    combo = len(parts) == 2
    content = 0.70 if combo else 0.76
    cs = int(big * content)
    a = glyph_mask(parts[0], cs)
    c = Image.new("L", (big, big), 0)
    off = int(big * (0.06 if combo else 0.12))
    c.paste(a, (off, off))
    a = c
    em = None
    if combo:
        es = int(big * 0.42)
        em = Image.new("L", (big, big), 0)
        em.paste(glyph_mask(parts[1], es), (big - es - int(big * 0.05), big - es - int(big * 0.05)))
    union = a if em is None else Image.fromarray(np.maximum(np.asarray(a), np.asarray(em)))
    an = np.asarray(a, np.float32) / 255
    if em is not None:
        gap = np.asarray(em.filter(ImageFilter.MaxFilter(2 * (big // 40) + 1)), np.float32) / 255
        an = an * (1 - gap)
    base = np.array(hex2rgb(tint(e)), np.float32)
    r = min(7, e["rarity"]) if e["kind"] == "item" else 3
    h, w = an.shape
    yy = np.linspace(0, 1, h)[:, None, None]
    light = np.minimum(255, base + 70)
    dark = base * 0.55
    col = np.where(yy < 0.5, light + (base - light) * (yy / 0.5), base + (dark - base) * ((yy - 0.5) / 0.5))
    col = np.broadcast_to(col, (h, w, 3))
    out = np.zeros((h, w, 4), np.float32)
    glow_r = ([0, 0, 3, 5, 7, 9, 11, 13][r] * 4) if e["kind"] == "item" else 32
    if glow_r:
        ga = np.asarray(union.filter(ImageFilter.GaussianBlur(glow_r)), np.float32) / 255 * (0.45 + 0.07 * r)
        out[..., :3] = base
        out[..., 3] = np.clip(ga * 1.6, 0, 1)
    if e["kind"] != "item":
        yy2, xx2 = np.mgrid[0:h, 0:w]
        d = np.hypot(yy2 - h / 2, xx2 - w / 2) / (h * 0.46)
        disc = np.where(d < 1, np.clip(1 - d, 0, 1) ** 0.5 * 0.85, 0)
        bg = np.zeros_like(out)
        bg[..., :3] = base * 0.18 + 8
        bg[..., 3] = disc
        out = over(bg, out)
    k = 2 * (big // 64) + 1
    ola = np.asarray(a.filter(ImageFilter.MaxFilter(k)), np.float32) / 255
    outline = np.zeros_like(out)
    outline[..., :3] = (10, 8, 12)
    outline[..., 3] = ola
    out = over(outline, out)
    glyph = np.zeros_like(out)
    glyph[..., :3] = col
    glyph[..., 3] = an
    out = over(glyph, out)
    if em is not None:
        ema = np.asarray(em, np.float32) / 255
        eol = np.asarray(em.filter(ImageFilter.MaxFilter(k)), np.float32) / 255
        o2 = np.zeros_like(out)
        o2[..., :3] = (10, 8, 12)
        o2[..., 3] = eol
        out = over(o2, out)
        e2 = np.zeros_like(out)
        pale = np.minimum(255, base * 0.35 + 190)
        e2[..., :3] = pale
        e2[..., 3] = ema
        out = over(e2, out)
    return out


def save(arr, path):
    import numpy as np
    from PIL import Image
    a = arr.copy()
    a[..., 3] *= 255
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA").resize((SIZE, SIZE), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    im = im.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    im.save(path, optimize=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["fetch", "match", "render"])
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--only", nargs="*")
    a = ap.parse_args()
    if a.cmd == "fetch":
        return fetch()
    ents = load_entries()
    ov = HERE / "overrides.json"
    overrides = json.load(open(ov, encoding="utf8")) if ov.exists() else {}
    if a.cmd == "match":
        m = match(ents, overrides)
        json.dump(dict(sorted(m.items())), open(HERE / "mapping.json", "w", encoding="utf8"), indent=1)
        print(len(m), "mapped,", len(set(m.values())), "distinct icons")
        return
    m = json.load(open(HERE / "mapping.json", encoding="utf8"))
    n = 0
    for e in ents:
        if a.only and e["id"] not in a.only:
            continue
        p = OUT / f"{e['id']}.png"
        if p.exists() and not a.force:
            continue
        save(render_one(e, m[e["id"]]), p)
        n += 1
    print("rendered", n)


if __name__ == "__main__":
    main()
