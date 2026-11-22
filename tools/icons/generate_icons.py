#!/usr/bin/env python
"""Build content/icons/<id>.png from the game-icons.net SVG set (CC BY 3.0).

  python -I tools/icons/generate_icons.py fetch            # download the SVG set into tools/icons/cache/
  python -I tools/icons/generate_icons.py match            # (re)build tools/icons/mapping.json
  python -I tools/icons/generate_icons.py render [--force] [--only ID ...]
  python -I tools/icons/generate_icons.py booktab          # content/icons/ui.book_tab.png (guide-book tab face)

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
          "templar-eye", "templar-heart", "american-shield", "magic-shield", "hasty-grave", "bank", "tombstone-style", "hood", "warlock-hood", "ribbon", "fist", "crown-of-thorns", "boxing-ring", "bow-tie-ribbon", "ribbon-medal", "ribbon-shield", "broken-ribbon", "sausages-ribbon", "spinning-ribbons", "nazi", "swastika"}  # real-world political/religious symbols


def index_icons():
    return [(f"{s.parent.name}/{s.stem}", set(s.stem.split("-")), s.stem) for s in sorted(SRC.glob("*/*.svg")) if s.stem not in BANNED]


def h32(s):
    return zlib.crc32(s.encode())


# Item names -> a small badge glyph (bottom right) when the item's type has no unused glyph left: every item gets an
# icon no other item has (COS-162, Max 2026-11-22). Fragments match inside compound names ("Emberforge" -> fire).
THEME = {
    "flame": "fire", "fire": "fire", "ember": "fire", "cinder": "fire", "furnace": "furnace", "ash": "smoking-volcano",
    "frost": "snowflake-1", "rime": "snowflake-1", "whiteout": "snowing", "blood": "drop", "vein": "drop", "bone": "bone-gnawer",
    "skull": "skull-bolt", "gold": "gold-bar", "void": "portal", "rift": "portal", "dial": "stopwatch", "everturn": "cycle",
    "cube": "cube", "silver": "sewing-string", "thread": "sewing-string", "spool": "sewing-string", "wire": "wire-coil",
    "key": "key-card", "rivet": "nails", "hinge": "gears", "crest": "castle", "oath": "shaking-hands", "sworn": "shaking-hands",
    "bell": "ringing-bell", "light": "light-bulb", "glow": "light-bulb", "quarry": "stone-block", "rail": "railway",
    "hare": "rabbit", "leech": "leeching-worm", "spore": "mushroom-gills", "honey": "honeycomb", "stair": "stairs",
    "penny": "coins", "smudge": "cauldron", "gaff": "meat-hook", "hide": "animal-hide", "pale": "ghost", "weight": "weight",
    "sprint": "run", "laugh": "laughing-mask", "bloat": "puffer-fish", "curtain": "curtains", "tally": "abacus", "hung": "noose",
    "cheap": "coins", "polymer": "3d-printer", "printed": "3d-printer", "ammo": "ammo-box", "flicker": "lightning-bolt",
    "gouge": "claw-slashes", "gnaw": "fangs", "pierced": "arrowed", "dead": "skull-crack", "muster": "flag-objective",
    "rust": "rusty-sword", "pine": "pine-tree", "charcoal": "candle-flame", "cave": "cave-entrance", "wreck": "crashed-helmet",
    "glider": "hang-glider", "burlap": "swap-bag", "hush": "silenced", "stone": "stone-block", "ageless": "hourglass",
    "rattle": "rattle", "dent": "hammer-drop", "filigree": "flower-twirl", "ridge": "mountains", "riot": "police-badge",
    "survivor": "campfire", "render": "cauldron", "kevlar": "bullets", "hammer": "hammer-drop", "plaid": "checkered-flag",
    "ceaseless": "infinity", "scuff": "footprint", "track": "footprint", "sinew": "muscle-up",
    "hold": "padlock", "tray": "meal", "pot": "cooking-pot", "oak": "oak", "vault": "locked-chest", "hunt": "deer-head",
    "kill": "skull-crack", "rush": "run", "tags": "id-card", "yoke": "chain", "knuckle": "knuckle-duster", "twin": "two-shadows",
    "welder": "welding-torch", "scrub": "soap", "weave": "sewing-string", "steel": "anvil", "joint": "chain", "nail": "nails",
    "unmarked": "invisible", "bite": "fangs", "steady": "balance", "wired": "wire-coil", "mother": "heart-beats", "beating": "heart-beats",
    "estate": "castle", "bound": "chain", "quiet": "silenced", "rage": "enrage", "spring": "spring", "ink": "quill-ink",
    "fletch": "feather", "pinfeather": "feather", "marksman": "crosshair", "thorn": "thorny-vine", "rampart": "stone-wall",
    "moss": "vine-leaf", "haul": "wooden-crate", "grapple": "grapple", "crush": "fist", "spotter": "spyglass", "drover": "cow",
    "twine": "rope-coil", "ward": "magic-shield", "duel": "crossed-swords", "tallow": "candle-flame", "almanac": "calendar",
    "bandana": "abacus", "sled": "sled", "cleat": "spikes", "spike": "spikes", "zipline": "zipline", "lantern": "lantern-flame",
    "horn": "hunting-horn", "puffer": "puffer-fish", "biker": "motorcycle", "board": "ticket",
    "sun": "sun", "salt": "salt-shaker", "patch": "sewing-needle", "lobby": "door", "diadem": "crown",
    "plank": "wood-beam", "lid": "cooking-pot", "manhole": "sewer", "pelt": "wolf-head", "cracked": "cracked-shield",
    "frayed": "sewing-string", "knit": "sewing-needle", "rubber": "rubber-boot", "loop": "cycle",
    "sandal": "sandal", "notes": "notebook", "field": "wheat", "boat": "sailboat", "locker": "padlock",
}
BADGES = ["sparkles", "star-swirl", "gem-chain", "crystal-growth", "lightning-bolt", "snowflake-1", "drop", "skull-crack",
          "heart-beats", "sun", "moon", "feather", "claw-slashes", "eye-target", "crown", "fire", "leaf", "wolf-head", "spider-alt",
          "raven", "anchor", "bell", "hourglass", "compass", "key", "padlock", "cog", "scroll-quill", "tooth", "spiral-shell"]


def badge_choices(name, slug_key):
    out = []
    for w in re.sub(r"[^a-z ]", " ", name.lower()).split():
        for frag in sorted((f for f in THEME if f in w), key=len, reverse=True):
            k = slug_key.get(THEME[frag])
            if k and k not in out:
                out.append(k)
    return out + [slug_key[b] for b in BADGES if b in slug_key and slug_key[b] not in out]


def match(ents, overrides):
    idx = index_icons()
    by_slug = {k.split("/")[1]: k for k, _, _ in idx}
    overrides = {i: BADGE.join(by_slug.get(x, x) for x in v.split(BADGE)) for i, v in overrides.items()}  # "base+badge" allowed
    for v in overrides.values():
        if v == PLACEHOLDER:
            continue
        for part in v.split(BADGE):
            assert (SRC / (part + ".svg")).exists(), "unknown icon " + part
    used = collections.Counter()
    res = {}
    items = [e for e in ents if e["kind"] == "item"]
    for e in sorted((e for e in ents if e["kind"] != "item"), key=lambda e: (-len(words(e["name"])), e["id"])):
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
            if e["kind"] != "item":
                s -= 4 * used[key]  # perks are symbolic: spread them over many glyphs; items reuse the best-fitting glyph
            s += (h32(e["id"] + key) % 1000) / 4000
            if s > bs:
                bs, best = s, key
        best = best or "lorc/magic-swirl"
        res[e["id"]] = best
        used[best] += 1
    res.update(match_items(items, overrides, idx, by_slug))
    return res


def item_scores(e, idx):
    ws = words(e["name"])
    strong, syn = set(ws), set()
    for w in ws:
        syn.update(SYN.get(w, []))
        if w.endswith("s"):
            strong.add(w[:-1])
    typ = set(TYPE_TOK.get(e["type"], []))
    out = []
    for key, toks, slug in idx:
        if not (typ & toks) and not (e["type"] in ("material", "other", "spell_scroll") and (slug in strong or slug in syn)):
            continue
        s = 5 * len(strong & toks) + 3 * len(syn & toks) + 1.5 * len(typ & toks) + (4 if slug in strong else 0)
        if s <= 0:
            continue
        s -= 0.3 * max(0, len(toks) - 2)
        s += (h32(e["id"] + key) % 1000) / 4000
        out.append((s, key))
    return [k for _, k in sorted(out, reverse=True)]


def match_items(items, overrides, idx, by_slug):
    """Every item gets an icon no other item has: its hand pick or best glyph if still free, else the type's best glyph
    with a badge from its name. Legendaries choose first, then Rare, Magic, Common."""
    taken, res = set(), {}
    for e in sorted(items, key=lambda e: (-e["rarity"], e["id"])):
        ov = overrides.get(e["id"])
        cands = ([ov] if ov and ov != PLACEHOLDER else []) + item_scores(e, idx)
        key = next((k for k in cands if k not in taken), None)
        if key is None:
            base = cands[0] if cands else "lorc/magic-swirl"
            key = next(base + BADGE + b for b in badge_choices(e["name"], by_slug) if b != base and base + BADGE + b not in taken)
        taken.add(key)
        res[e["id"]] = key
    assert len(set(res.values())) == len(res), "two items share an icon"
    return res


# The four rarities (COS-109), one colour each, the same as the mod's UI (Max, 2026-11-21): white, blue, yellow, red.
RARITY = ["#e8e8e8", "#4d9dff", "#ffd94a", "#ff3b30"]


def tier(e):
    return min(3, max(0, e["rarity"]))
CAT = {"melee_offense": "#e8603c", "ranged_offense": "#8ac24a", "magic_offense": "#9a6bff", "defense": "#5aa0e8",
       "support": "#4ed6a0", "utility": "#e8c25a", "spell": "#5ad8ff", "set": "#ffd24a"}


def tint(e):
    return RARITY[tier(e)] if e["kind"] == "item" else CAT.get(e["type"], "#cccccc")


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


BADGE = "+"  # mapping value "<base glyph>+<badge glyph>": the base with a small badge in a disc at the bottom right


def glyph_mask(key, size):
    """White-on-black SVG -> L mask at size x size (luminance: some icons have black cut-outs)."""
    import resvg_py
    from PIL import Image
    if BADGE in key:
        return badge_mask(*key.split(BADGE), size)
    svg = (SRC / (key + ".svg")).read_text(encoding="utf8")
    png = bytes(resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size))
    return Image.open(io.BytesIO(png)).convert("L")


def badge_mask(base, badge, size):
    import numpy as np
    from PIL import Image
    a = np.asarray(glyph_mask(base, size), np.float32) / 255
    bs = int(size * 0.58)
    yy, xx = np.mgrid[0:bs, 0:bs]
    r = np.hypot(yy - bs / 2, xx - bs / 2)
    sub = a[size - bs:, size - bs:]
    sub[r < bs / 2] = 0  # cut the base under the disc
    sub[(r < bs / 2) & (r > bs / 2 - size / 64)] = 1  # ring
    inner = int(bs * 0.18)
    ts = bs - 2 * inner
    t = np.asarray(glyph_mask(badge, ts), np.float32) / 255
    sub[inner:inner + ts, inner:inner + ts] = np.maximum(sub[inner:inner + ts, inner:inner + ts], t)
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def render_one(e, key):
    """key is a glyph ('author/slug') or PLACEHOLDER."""
    if key == PLACEHOLDER:
        return render_placeholder()
    import numpy as np
    from PIL import Image, ImageFilter
    big = 4 * SIZE
    off = int(big * 0.12)
    a = glyph_mask(key, big - 2 * off)
    c = Image.new("L", (big, big), 0)
    c.paste(a, (off, off))
    a = c
    an = np.asarray(a, np.float32) / 255
    base = np.array(hex2rgb(tint(e)), np.float32)
    r = tier(e) if e["kind"] == "item" else 3
    h, w = an.shape
    yy = np.linspace(0, 1, h)[:, None, None]
    light = np.minimum(255, base + 70)
    dark = base * 0.55
    col = np.where(yy < 0.5, light + (base - light) * (yy / 0.5), base + (dark - base) * ((yy - 0.5) / 0.5))
    col = np.broadcast_to(col, (h, w, 3))
    out = np.zeros((h, w, 4), np.float32)
    # Glow grows with rarity: none, small, medium, strong (the sizes the old tiers had before COS-109).
    glow_r = ([0, 3, 5, 9][r] * 4) if e["kind"] == "item" else 32
    if glow_r:
        ga = np.asarray(a.filter(ImageFilter.GaussianBlur(glow_r)), np.float32) / 255 * (0.45 + 0.07 * [0, 2, 3, 5][r])
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
    ola = np.asarray(a.filter(ImageFilter.MaxFilter(2 * (big // 64) + 1)), np.float32) / 255
    outline = np.zeros_like(out)
    outline[..., :3] = (10, 8, 12)
    outline[..., 3] = ola
    out = over(outline, out)
    glyph = np.zeros_like(out)
    glyph[..., :3] = col
    glyph[..., 3] = an
    return over(glyph, out)


def save(arr, path):
    import numpy as np
    from PIL import Image
    a = arr.copy()
    a[..., 3] *= 255
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA").resize((SIZE, SIZE), Image.LANCZOS)
    path.parent.mkdir(parents=True, exist_ok=True)
    im = im.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
    im.save(path, optimize=True)


BOOK_TAB_ICON = "delapouite/sparkles"


def render_book_tab():
    """content/icons/ui.book_tab.png: our guide-book tab face, in the layout of the game's own tab icons
    (256 px, black 14 px frame, white field, flat black glyph about 184 px wide; measured from TactiTabRafts)."""
    import numpy as np
    from PIL import Image
    size, frame, glyph = 256, 14, 184
    a = np.full((size, size), 255, np.uint8)
    a[:frame, :] = a[-frame:, :] = 0
    a[:, :frame] = a[:, -frame:] = 0
    m = np.array(glyph_mask(BOOK_TAB_ICON, glyph), np.float32) / 255
    o = (size - glyph) // 2
    a[o:o + glyph, o:o + glyph] = (a[o:o + glyph, o:o + glyph] * (1 - m)).astype(np.uint8)
    Image.fromarray(a, "L").convert("RGBA").save(OUT / "ui.book_tab.png", optimize=True)
    print("rendered ui.book_tab from", BOOK_TAB_ICON)


# game-icons.net folder -> the name content/icons/CREDITS.md must give (their license asks for "Icons made by {author}").
AUTHORS = {"lorc": "Lorc", "delapouite": "Delapouite", "skoll": "Skoll", "sbed": "Sbed", "lucasms": "Lucas",
           "willdabeast": "Willdabeast", "darkzaitzev": "DarkZaitzev", "carl-olsen": "Carl Olsen", "priorblue": "PriorBlue",
           "seregacthtuf": "SeregaCthtuf", "zeromancer": "Zeromancer", "irongamer": "Irongamer", "kier-heyl": "Kier Heyl",
           "heavenly-dog": "HeavenlyDog", "felbrigg": "Felbrigg", "faithtoken": "Faithtoken", "caro-asercion": "Caro Asercion",
           "lord-berandas": "Lord Berandas", "quoting": "Quoting", "catsu": "Catsu", "andymeneely": "Andy Meneely",
           "john-colburn": "John Colburn", "john-redman": "John Redman", "viscious-speed": "Viscious Speed",
           "cathelineau": "Cathelineau", "aussiesim": "Aussiesim", "sparker": "Sparker", "rihlsul": "Rihlsul",
           "guard13007": "Guard13007", "spencerdub": "SpencerDub", "generalace135": "GeneralAce135", "zajkonur": "Zajkonur",
           "starseeker": "Starseeker", "pepijn-poolman": "Pepijn Poolman", "pierre-leducq": "Pierre Leducq",
           "badges": "badges", "various-artists": "various artists"}


def check_credits(m):
    """Every game-icons author in the mapping must be named in content/icons/CREDITS.md."""
    credits = (OUT / "CREDITS.md").read_text(encoding="utf8")
    used = sorted({part.split("/")[0] for v in m.values() for part in v.split(BADGE) if "/" in part})
    missing = [f for f in used if AUTHORS.get(f, f) not in credits]
    print("credits: all", len(used), "authors named" if not missing else f"MISSING in CREDITS.md: {missing}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["fetch", "match", "render", "booktab"])
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--only", nargs="*")
    a = ap.parse_args()
    if a.cmd == "fetch":
        return fetch()
    if a.cmd == "booktab":
        return render_book_tab()
    ents = load_entries()
    ov = HERE / "overrides.json"
    overrides = json.load(open(ov, encoding="utf8")) if ov.exists() else {}
    if a.cmd == "match":
        m = match(ents, overrides)
        # Perks, spells and sets keep the icon they already have unless overridden: item changes must not reshuffle them.
        mp = HERE / "mapping.json"
        old = json.load(open(mp, encoding="utf8")) if mp.exists() else {}
        for e in ents:
            if e["kind"] != "item" and e["id"] in old and e["id"] not in overrides:
                m[e["id"]] = old[e["id"]]
        json.dump(dict(sorted(m.items())), open(HERE / "mapping.json", "w", encoding="utf8"), indent=1)
        print(len(m), "mapped,", len(set(m.values())), "distinct icons")
        check_credits(m)
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
