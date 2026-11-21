"""Generates the action bar's rune & bone HUD textures (COS-151) into content/icons/ui.hud.*.png.

Procedural (numpy + Pillow), fixed seeds, so a rerun gives the same files. Hand-painted art can replace any file later as
long as it keeps the name, size and 9-slice border listed in SLICES (the mod reads the borders from HudArt.cs, keep both
in sync). Glyphs are invented curve-and-dot marks, not letters of any real alphabet or religious symbols.

    python tools/hud_art.py [--preview out.png]
"""
from __future__ import annotations

import argparse
import math
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "content" / "icons"
SS = 4  # supersampling for anti-aliased shapes

# name -> (width, height, 9-slice border l, b, r, t in pixels). Mirrored in src/ChampionsOfTheSons.Mod/ActionBar/HudArt.cs.
SLICES = {
    "ui.hud.backing": (512, 128, 56, 40, 56, 40),
    "ui.hud.slot_frame": (128, 128, 30, 30, 30, 30),
    "ui.hud.slot_glow": (128, 128, 34, 34, 34, 34),
    "ui.hud.slot_inset": (96, 96, 0, 0, 0, 0),
    "ui.hud.bar_channel": (256, 40, 26, 14, 26, 14),
    "ui.hud.bar_strap": (256, 40, 22, 14, 22, 14),
    "ui.hud.bar_fill": (128, 24, 4, 4, 4, 4),
    "ui.hud.level_disc": (128, 128, 0, 0, 0, 0),
    "ui.hud.key_plate": (48, 28, 10, 8, 10, 8),
    "ui.hud.clasp": (40, 128, 0, 0, 0, 0),
}

BONE = np.array([226, 212, 182], float)
BONE_DARK = np.array([120, 98, 70], float)
HIDE = np.array([58, 38, 26], float)
HIDE_DARK = np.array([24, 15, 10], float)
SINEW = np.array([96, 54, 34], float)


# ----------------------------------------------------------------------------------------------------------- noise


def value_noise(h: int, w: int, cells: int, rng: np.random.Generator) -> np.ndarray:
    small = rng.random((max(2, cells), max(2, int(cells * w / h) or 2)))
    img = Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, float) / 255.0


def fbm(h: int, w: int, seed: int, base: int = 4, octaves: int = 5) -> np.ndarray:
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        out += amp * value_noise(h, w, base * 2**o, rng)
        total += amp
        amp *= 0.5
    return out / total


def mask_from(draw_fn, w: int, h: int, blur: float = 0) -> np.ndarray:
    """Draws white-on-black at SS x and returns an anti-aliased 0..1 mask."""
    img = Image.new("L", (w * SS, h * SS), 0)
    draw_fn(ImageDraw.Draw(img), SS)
    img = img.resize((w, h), Image.LANCZOS)
    if blur:
        img = img.filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(img, float) / 255.0


def edge_distance(mask: np.ndarray, radius: int) -> np.ndarray:
    """0 at the mask edge rising to 1 at `radius` px inside (blur-based approximation)."""
    img = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius))
    d = np.asarray(img, float) / 255.0
    return np.clip((d - 0.5) * 2, 0, 1) * mask


def to_rgba(rgb: np.ndarray, alpha: np.ndarray) -> Image.Image:
    a = np.clip(alpha, 0, 1)[..., None]
    arr = np.concatenate([np.clip(rgb, 0, 255), a * 255], axis=-1).astype(np.uint8)
    return Image.fromarray(arr, "RGBA")


def lerp(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return a + (b - a) * t


# ----------------------------------------------------------------------------------------------------------- materials


def bone_rgb(h: int, w: int, seed: int, inner: np.ndarray) -> np.ndarray:
    """Weathered bone: streaky grain along x, pits, darker toward the edges (inner = 0 edge .. 1 centre)."""
    grain = fbm(h, w, seed, base=3)
    streak = fbm(h, max(4, w // 6), seed + 1, base=6)
    streak = np.asarray(Image.fromarray((streak * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), float) / 255
    pits = (fbm(h, w, seed + 2, base=24, octaves=2) > 0.68).astype(float)
    shade = 0.55 + 0.45 * np.clip(inner * 1.4, 0, 1)
    tone = np.clip(0.55 * grain + 0.45 * streak, 0, 1)
    rgb = lerp(BONE_DARK, BONE, np.clip(tone * 1.25 - 0.05, 0, 1) * shade)
    rgb = rgb * (1 - 0.35 * pits[..., None])
    return rgb


def add_cracks(rgb: np.ndarray, mask: np.ndarray, seed: int, count: int, length: int) -> np.ndarray:
    h, w = mask.shape
    rng = np.random.default_rng(seed)
    img = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(img)
    ys, xs = np.nonzero(mask > 0.9)
    if len(xs) == 0:
        return rgb
    for _ in range(count):
        i = rng.integers(len(xs))
        x, y = float(xs[i]), float(ys[i])
        ang = rng.random() * math.tau
        pts = [(x, y)]
        for _ in range(length):
            ang += rng.normal(0, 0.5)
            x += math.cos(ang) * 2.2
            y += math.sin(ang) * 2.2
            pts.append((x, y))
        d.line(pts, fill=255, width=1)
    c = np.asarray(img, float) / 255 * mask
    return rgb * (1 - 0.45 * c[..., None])


def hide_rgb(h: int, w: int, seed: int) -> np.ndarray:
    n = fbm(h, w, seed, base=5)
    pores = fbm(h, w, seed + 7, base=40, octaves=2)
    rgb = lerp(HIDE_DARK, HIDE, np.clip(n * 1.3 - 0.1, 0, 1))
    rgb = rgb * (1 - 0.3 * (pores > 0.62)[..., None])
    return rgb


def vignette(h: int, w: int, strength: float) -> np.ndarray:
    y, x = np.mgrid[0:h, 0:w]
    dx = (x - (w - 1) / 2) / (w / 2)
    dy = (y - (h - 1) / 2) / (h / 2)
    return 1 - strength * np.clip(np.sqrt(dx**2 + dy**2) - 0.35, 0, 1)


# ----------------------------------------------------------------------------------------------------------- glyphs


def glyph(d: ImageDraw.ImageDraw, cx: float, cy: float, size: float, seed: int, width: float, fill=255):
    """An invented mark: a stave with arcs, hooks and dots (curved shapes keep it clear of real runic letters)."""
    rng = np.random.default_rng(seed)
    s = size
    top, bot = cy - s / 2, cy + s / 2
    d.line([(cx, top), (cx, bot)], fill=fill, width=int(width))
    for _ in range(rng.integers(1, 3)):
        r = s * (0.18 + 0.14 * rng.random())
        y = top + s * (0.2 + 0.6 * rng.random())
        side = -1 if rng.random() < 0.5 else 1
        box = [cx + (0 if side > 0 else -2 * r), y - r, cx + (2 * r if side > 0 else 0), y + r]
        start = 270 if side > 0 else 90
        d.arc(box, start, start + 180, fill=fill, width=int(width))
    for _ in range(rng.integers(1, 3)):
        side = -1 if rng.random() < 0.5 else 1
        y = top + s * rng.random()
        r = width * 0.9
        x = cx + side * s * 0.32
        d.ellipse([x - r, y - r, x + r, y + r], fill=fill)


# ----------------------------------------------------------------------------------------------------------- pieces


def slot_frame(w, h):
    """Square bone frame with sinew-wrapped corners; hollow centre (the inset shows through)."""
    def outer(d, s):
        d.rounded_rectangle([2 * s, 2 * s, (w - 2) * s, (h - 2) * s], radius=14 * s, fill=255)

    def hole(d, s):
        d.rounded_rectangle([16 * s, 16 * s, (w - 16) * s, (h - 16) * s], radius=6 * s, fill=255)

    m_out, m_hole = mask_from(outer, w, h), mask_from(hole, w, h)
    ring = np.clip(m_out - m_hole, 0, 1)
    inner = edge_distance(ring, 4)
    rgb = bone_rgb(h, w, 11, inner)
    rgb = add_cracks(rgb, ring, 12, 10, 9)

    # sinew wraps across each corner
    def wraps(d, s):
        for cx, cy in [(14, 14), (w - 14, 14), (14, h - 14), (w - 14, h - 14)]:
            for k in range(-2, 3):
                o = k * 4.2
                d.line([((cx - 11 + o) * s, (cy - 11 - o) * s), ((cx + 11 + o) * s, (cy + 11 - o) * s)][::1], fill=255, width=int(2.6 * s))

    wrap = mask_from(wraps, w, h) * ring
    corner = np.zeros((h, w))
    for cx, cy in [(14, 14), (w - 14, 14), (14, h - 14), (w - 14, h - 14)]:
        y, x = np.mgrid[0:h, 0:w]
        corner = np.maximum(corner, (np.hypot(x - cx, y - cy) < 13).astype(float))
    wrap *= corner
    sinew = SINEW * (0.7 + 0.5 * fbm(h, w, 13, base=16)[..., None])
    rgb = lerp(rgb, sinew, wrap)

    # inner lip shadow
    lip = mask_from(lambda d, s: d.rounded_rectangle([13 * s, 13 * s, (w - 13) * s, (h - 13) * s], radius=8 * s, fill=255), w, h)
    rgb *= (1 - 0.35 * np.clip(lip - m_hole, 0, 1))[..., None]
    return to_rgba(rgb, ring)


def slot_glow(w, h):
    """White glow hugging the frame's inner lip with faint glyph ticks; tinted at runtime (rune blue when ready, ember on the held weapon)."""
    def ring(d, s):
        d.rounded_rectangle([12 * s, 12 * s, (w - 12) * s, (h - 12) * s], radius=9 * s, outline=255, width=int(5 * s))

    g = mask_from(ring, w, h, blur=4) * 1.6
    sharp = mask_from(lambda d, s: d.rounded_rectangle([14 * s, 14 * s, (w - 14) * s, (h - 14) * s], radius=8 * s, outline=255, width=int(2 * s)), w, h)
    a = np.clip(g + sharp, 0, 1)
    rgb = np.full((h, w, 3), 255.0)
    return to_rgba(rgb, a)


def slot_inset(w, h):
    rgb = hide_rgb(h, w, 21) * vignette(h, w, 0.9)[..., None]
    return to_rgba(rgb, np.ones((h, w)))


def backing(w, h):
    """Long stretched-hide strip behind the whole bar, ragged edges, lacing holes at the ends."""
    rng = np.random.default_rng(31)

    def shape(d, s):
        pts = []
        steps = 48
        for i in range(steps + 1):  # top edge, ragged
            x = 6 + (w - 12) * i / steps
            pts.append((x * s, (8 + rng.random() * 6) * s))
        for i in range(steps + 1):  # bottom edge
            x = w - 6 - (w - 12) * i / steps
            pts.append((x * s, (h - 8 - rng.random() * 6) * s))
        d.polygon(pts, fill=255)

    m = mask_from(shape, w, h, blur=0.6)
    rgb = hide_rgb(h, w, 32)
    inner = edge_distance(m, 6)
    rgb *= (0.55 + 0.45 * inner)[..., None]
    # lacing holes and thongs at both ends
    lace = mask_from(lambda d, s: [d.ellipse([(x - 4) * s, (y - 4) * s, (x + 4) * s, (y + 4) * s], fill=255)
                                   for x in (20, w - 20) for y in (h * 0.3, h * 0.5, h * 0.7)], w, h)
    rgb = rgb * (1 - 0.85 * lace[..., None])
    thong = mask_from(lambda d, s: [d.line([((x - 9) * s, (h * 0.3) * s), ((x + 9) * s, (h * 0.7) * s)], fill=255, width=int(3 * s))
                                    for x in (20, w - 20)], w, h)
    rgb = lerp(rgb, SINEW * 1.1, thong * 0.9)
    return to_rgba(rgb, m * 0.94)


def bar_channel(w, h):
    """Bone groove for the energy bar: raised bone lips, dark carved channel, glyph etchings on the end caps."""
    outer = mask_from(lambda d, s: d.rounded_rectangle([1 * s, 2 * s, (w - 1) * s, (h - 2) * s], radius=12 * s, fill=255), w, h)
    groove = mask_from(lambda d, s: d.rounded_rectangle([22 * s, 11 * s, (w - 22) * s, (h - 11) * s], radius=4 * s, fill=255), w, h)
    rgb = bone_rgb(h, w, 41, edge_distance(outer, 3))
    rgb = add_cracks(rgb, outer, 42, 14, 8)
    rgb = lerp(rgb, HIDE_DARK * 0.6, groove)
    etch = mask_from(lambda d, s: [glyph(d, x * s, h / 2 * s, 20 * s, 43 + i, 1.6 * s) for i, x in enumerate((11, w - 11))], w, h)
    rgb *= (1 - 0.55 * etch)[..., None]
    return to_rgba(rgb, outer)


def bar_strap(w, h):
    """Leather strap for the XP bar: stitched top and bottom, recessed centre."""
    outer = mask_from(lambda d, s: d.rounded_rectangle([1 * s, 3 * s, (w - 1) * s, (h - 3) * s], radius=8 * s, fill=255), w, h)
    rgb = hide_rgb(h, w, 51) * 1.25
    rgb *= (0.6 + 0.4 * edge_distance(outer, 3))[..., None]
    recess = mask_from(lambda d, s: d.rounded_rectangle([18 * s, 12 * s, (w - 18) * s, (h - 12) * s], radius=3 * s, fill=255), w, h)
    rgb = lerp(rgb, HIDE_DARK * 0.5, recess)

    def stitches(d, s):
        for y in (7, h - 7):
            for x in range(6, w - 6, 9):
                d.line([(x * s, (y - 1) * s), ((x + 5) * s, (y + 1) * s)], fill=255, width=int(1.6 * s))

    st = mask_from(stitches, w, h) * (1 - recess)
    rgb = lerp(rgb, np.array([196, 170, 128.0]), st)
    return to_rgba(rgb, outer)


def bar_fill(w, h):
    """White fill with grain and a top highlight; tinted at runtime."""
    n = fbm(h, w, 61, base=6)
    y = np.linspace(0, 1, h)[:, None]
    light = 0.78 + 0.22 * np.exp(-((y - 0.25) ** 2) / 0.02) - 0.18 * y
    v = np.clip(light * (0.85 + 0.15 * n), 0, 1) * 255
    rgb = np.repeat(v[..., None], 3, axis=-1)
    return to_rgba(rgb, np.ones((h, w)))


def level_disc(w, h):
    """Carved bone disc for the level number: rim of invented glyphs, sunken face."""
    disc = mask_from(lambda d, s: d.ellipse([3 * s, 3 * s, (w - 3) * s, (h - 3) * s], fill=255), w, h)
    face = mask_from(lambda d, s: d.ellipse([28 * s, 28 * s, (w - 28) * s, (h - 28) * s], fill=255), w, h)
    rgb = bone_rgb(h, w, 71, edge_distance(disc, 6))
    rgb = add_cracks(rgb, disc, 72, 12, 10)

    def rim(d, s):
        n = 8
        for i in range(n):
            a = i / n * math.tau
            cx, cy = w / 2 + math.cos(a) * 41, h / 2 + math.sin(a) * 41
            glyph(d, cx * s, cy * s, 15 * s, 73 + i, 1.4 * s)

    rgb *= (1 - 0.6 * mask_from(rim, w, h))[..., None]
    rgb = lerp(rgb, HIDE_DARK * 0.8, face * 0.92)
    lip = mask_from(lambda d, s: d.ellipse([25 * s, 25 * s, (w - 25) * s, (h - 25) * s], outline=255, width=int(3 * s)), w, h)
    rgb *= (1 - 0.4 * lip)[..., None]
    return to_rgba(rgb, disc)


def key_plate(w, h):
    m = mask_from(lambda d, s: d.rounded_rectangle([1 * s, 1 * s, (w - 1) * s, (h - 1) * s], radius=7 * s, fill=255), w, h)
    rgb = bone_rgb(h, w, 81, edge_distance(m, 2)) * 0.95
    return to_rgba(rgb, m)


def clasp(w, h):
    """Group divider: a tapered bone spike bound with sinew."""
    def spike(d, s):
        d.polygon([(w / 2 * s, 2 * s), ((w - 9) * s, h * 0.5 * s), (w / 2 * s, (h - 2) * s), (9 * s, h * 0.5 * s)], fill=255)

    m = mask_from(spike, w, h)
    rgb = bone_rgb(h, w, 91, edge_distance(m, 3))
    bands = mask_from(lambda d, s: [d.line([(4 * s, (h / 2 + o) * s), ((w - 4) * s, (h / 2 + o - 3) * s)], fill=255, width=int(2.5 * s))
                                    for o in (-8, -3, 2, 7)], w, h) * m
    rgb = lerp(rgb, SINEW, bands)
    return to_rgba(rgb, m)


BUILDERS = {
    "ui.hud.backing": backing,
    "ui.hud.slot_frame": slot_frame,
    "ui.hud.slot_glow": slot_glow,
    "ui.hud.slot_inset": slot_inset,
    "ui.hud.bar_channel": bar_channel,
    "ui.hud.bar_strap": bar_strap,
    "ui.hud.bar_fill": bar_fill,
    "ui.hud.level_disc": level_disc,
    "ui.hud.key_plate": key_plate,
    "ui.hud.clasp": clasp,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="also write a contact sheet of all pieces on a mid-grey field")
    args = ap.parse_args()
    imgs = {}
    for name, (w, h, *_border) in SLICES.items():
        img = BUILDERS[name](w, h)
        assert img.size == (w, h), name
        img.save(OUT / f"{name}.png", optimize=True)
        imgs[name] = img
        print(f"wrote {name}.png {w}x{h}")
    if args.preview:
        pad = 16
        W = sum(i.width for i in imgs.values()) + pad * (len(imgs) + 1)
        H = max(i.height for i in imgs.values()) + 2 * pad
        sheet = Image.new("RGBA", (W, H), (70, 80, 70, 255))
        x = pad
        for img in imgs.values():
            sheet.alpha_composite(img, (x, pad))
            x += img.width + pad
        sheet.save(args.preview)


if __name__ == "__main__":
    main()
