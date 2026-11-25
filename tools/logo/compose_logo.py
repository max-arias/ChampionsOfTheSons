"""Builds 'Champions of the Sons' logo textures from the game's own logo pieces.

usage: python -I tools/logo/compose_logo.py <src dir> <out dir> [output width, default 1024]
<src dir> is the game's logo dump: run tools/run-selftest.ps1 -Headless -Only TitleLogoFeature -ExtraArgs --cots-dump-logo,
which writes the textures to %TEMP%\\cots-ui\\logo. The game's art is not kept in the repo; copy the outputs to content/logo/.
Every new letter (C, H, A, M, P, I) is cut and recombined from the original logo's own letters, so font and grunge match.
Reads GameLogo__SonsOfTheForestLogoGrunge.png (red) and GameLogoLine__SonsOfTheForestLogoGrungeWhite.png (white silhouette);
every glyph op runs on both so the layers line up. Writes cots_logo.png and cots_logo_white.png (+ preview.png).
"""
import sys, os
import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
RED = np.array(Image.open(os.path.join(src, "GameLogo__SonsOfTheForestLogoGrunge.png")).convert("RGBA")).astype(np.float32)
WHT = np.array(Image.open(os.path.join(src, "GameLogoLine__SonsOfTheForestLogoGrungeWhite.png")).convert("RGBA")).astype(np.float32)
H0, W0 = RED.shape[:2]

# Source colour per row (mean of opaque pixels), used to re-apply the top-to-bottom gradient after moving pieces.
def row_colors(img):
    m = img[..., 3] > 200
    cols = np.zeros((img.shape[0], 3), np.float32)
    known = []
    for y in range(img.shape[0]):
        if m[y].any():
            cols[y] = img[y][m[y]][:, :3].mean(0); known.append(y)
    ks = np.array(known)
    for c in range(3):
        cols[:, c] = np.interp(np.arange(img.shape[0]), ks, cols[ks, c])
    return cols
SRC_ROW = row_colors(RED)

class G:
    """A glyph: red + white RGBA layers plus each pixel's source row (for recolouring)."""
    def __init__(self, r, w, rows):
        self.r, self.w, self.rows = r, w, rows
    @staticmethod
    def crop(x0, y0, x1, y1):
        rows = np.repeat(np.arange(y0, y1, dtype=np.float32)[:, None], x1 - x0, 1)
        return G(RED[y0:y1, x0:x1].copy(), WHT[y0:y1, x0:x1].copy(), rows)
    @property
    def h(self): return self.r.shape[0]
    @property
    def wd(self): return self.r.shape[1]
    def sub(self, x0, x1):
        return G(self.r[:, x0:x1].copy(), self.w[:, x0:x1].copy(), self.rows[:, x0:x1].copy())
    def clear(self, x0, y0, x1, y1):
        self.r[y0:y1, x0:x1] = 0; self.w[y0:y1, x0:x1] = 0
        return self
    def paste(self, g, x, y):
        """Union: paste g over self (alpha max) at x,y."""
        for a, b in ((self.r, g.r), (self.w, g.w)):
            reg = a[y:y + g.h, x:x + g.wd]
            take = b[..., 3:4] > reg[..., 3:4]
            reg[:] = np.where(take, b, reg)
        reg = self.rows[y:y + g.h, x:x + g.wd]
        reg[:] = np.where(g.r[..., 3] > 0, g.rows, reg)
        return self
    def mirror(self):
        return G(self.r[:, ::-1].copy(), self.w[:, ::-1].copy(), self.rows[:, ::-1].copy())
    @staticmethod
    def blank(w, h):
        return G(np.zeros((h, w, 4), np.float32), np.zeros((h, w, 4), np.float32), np.zeros((h, w), np.float32))
    def resize(self, h):
        w = max(1, round(self.wd * h / self.h))
        def rs(a):
            return np.array(Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA").resize((w, h), Image.LANCZOS)).astype(np.float32)
        rows = np.array(Image.fromarray(self.rows).resize((w, h), Image.NEAREST))
        return G(rs(self.r), rs(self.w), rows)
    def trim(self):
        cols = np.where((self.w[..., 3] > 8).any(0))[0]
        return self.sub(cols[0], cols[-1] + 1)

def rough(g, x, y0, y1, side, seed):
    """Roughen a straight vertical cut at column x (rows y0..y1): eat 0-3 px of noise into the side ('l' or 'r')."""
    rng = np.random.default_rng(seed)
    n = np.convolve(rng.integers(0, 4, y1 - y0 + 4), np.ones(3) / 3, "same")[2:-2].round().astype(int)
    for i, y in enumerate(range(y0, y1)):
        if side == "r": g.clear(x - n[i], y, x, y + 1)
        else: g.clear(x, y, x + n[i], y + 1)
    return g

# Bottom band (FOREST) letters are 356 px tall: y 362..718.
BY0, BY1 = 362, 718
F = G.crop(5, BY0, 131, BY1)
O = G.crop(145, BY0, 296, BY1)
R = G.crop(316, BY0, 464, BY1)
E = G.crop(480, BY0, 610, BY1)
S = G.crop(624, BY0, 767, BY1)
T = G.crop(785, BY0, 922, BY1)
N = G.crop(308, 5, 457, 341).resize(BY1 - BY0)  # from SONS, scaled to FOREST height
HT = BY1 - BY0

def glyph_H():
    left = F.sub(0, 58)                      # F's stem (square top-left corner like F)
    right = E.sub(0, 60).mirror()            # E's stem, mirrored so its outer edge faces right
    bar = F.sub(56, 92).sub(0, 36)           # a slice of F's middle bar
    bar = G(bar.r[140:196].copy(), bar.w[140:196].copy(), bar.rows[140:196].copy())
    g = G.blank(58 + 34 + 60, HT)
    g.paste(left, 0, 0).paste(right, 92, 0).paste(bar, 56, 150)
    return g

def glyph_P():
    p = G(R.r.copy(), R.w.copy(), R.rows.copy())
    p.clear(57, 212, p.wd, HT)               # drop R's leg below the bowl
    p.clear(86, 158, p.wd, 212)              # and the bowl's lower right, where the leg flares out
    corner = G(R.r[0:54, 86:].copy(), R.w[0:54, 86:].copy(), R.rows[0:54, 86:].copy())
    corner = G(corner.r[::-1].copy(), corner.w[::-1].copy(), corner.rows[::-1].copy())
    p.paste(corner, 86, 158)                 # R's own rounded top-right corner, flipped, closes the bowl
    return p

def glyph_C():
    c = G(O.r.copy(), O.w.copy(), O.rows.copy())
    c.clear(90, 118, c.wd, 236)              # open the right side
    for y0, y1 in ((112, 118), (236, 242)):  # roughen the cut ends
        rng = np.random.default_rng(y0)
        for x in range(90, c.wd):
            k = rng.integers(0, 6)
            if y0 == 112: c.clear(x, y1 - k, x + 1, y1)
            else: c.clear(x, y0, x + 1, y0 + k)
    return c

def glyph_A():
    a = G(O.r.copy(), O.w.copy(), O.rows.copy())
    a.clear(57, 290, 91, HT)                 # open the bottom between the legs
    bar = F.sub(56, 92)
    bar = G(bar.r[140:196, :34].copy(), bar.w[140:196, :34].copy(), bar.rows[140:196, :34].copy())
    a.paste(bar, 57, 186)                    # crossbar
    return a

def glyph_M(cut=78):
    half = N.sub(0, cut)                     # left stem + upper part of the diagonal
    g = G.blank(cut * 2, HT)
    g.paste(half, 0, 0).paste(half.mirror(), cut, 0)
    return g

GAP = 17
def word(glyphs, gap=GAP):
    w = sum(g.wd for g in glyphs) + gap * (len(glyphs) - 1)
    out = G.blank(w, HT)
    x = 0
    for g in glyphs:
        out.paste(g, x, 0); x += g.wd + gap
    return out

I_ = F.sub(0, 59)
champions = word([glyph_C(), glyph_H(), glyph_A(), glyph_M(), glyph_P(), I_, O, N, S])

# Line 1: CHAMPIONS with "OF THE" stacked small to its right, bottom-aligned (as "OF THE" sits beside SONS in the original).
# Line 2: SONS (the original word's pixels) scaled to the full width of line 1.
sons = G.crop(3, 5, 603, 341)
ofthe = G.crop(626, 186, 819, 341).resize(HT // 2)  # half the height of CHAMPIONS
OFTHE_GAP, LINE_GAP, PAD = 22, 22, 6

def build(width, name):
    line1_w = champions.wd + OFTHE_GAP + ofthe.wd
    big = sons.resize(round(sons.h * line1_w / sons.wd))
    W, Hh = line1_w + 2 * PAD, PAD + HT + LINE_GAP + big.h + PAD
    g = G.blank(W, Hh)
    g.paste(champions, PAD, PAD)
    g.paste(ofthe, PAD + champions.wd + OFTHE_GAP, PAD + HT - ofthe.h)
    g.paste(big, PAD + (line1_w - big.wd) // 2, PAD + HT + LINE_GAP)
    g = g.resize(round(Hh * width / W))
    W, Hh = g.wd, g.h
    # Re-apply the gradient: scale each pixel's colour from its source row's mean to the new row's target.
    top, bot = SRC_ROW[20], SRC_ROW[H0 - 10]
    t = np.linspace(0, 1, Hh)[:, None, None]
    target = top + (bot - top) * t
    srcc = SRC_ROW[np.clip(g.rows.astype(int), 0, H0 - 1)]
    rgb = g.r[..., :3] * (target / np.maximum(srcc, 1))
    red = np.concatenate([np.clip(rgb, 0, 255), g.r[..., 3:4]], -1)
    Image.fromarray(red.astype(np.uint8), "RGBA").save(os.path.join(out, f"{name}.png"))
    Image.fromarray(np.clip(g.w, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(out, f"{name}_white.png"))
    # Preview: loading-screen look (dark backing, red filled to ~60 %, orange line) on black.
    bg = Image.new("RGBA", (W + 60, Hh + 60), (0, 0, 0, 255))
    dark = np.array(Image.open(os.path.join(out, f"{name}_white.png"))).astype(np.float32)
    dark[..., :3] = np.array([19, 24, 27], np.float32)
    line = dark.copy(); line[..., :3] = np.array([244, 146, 0], np.float32)
    cut = int(Hh * 0.62)
    redp = red.copy(); redp[cut:, :, 3] = 0
    line[:cut, :, 3] = 0; line[cut + 6:, :, 3] = 0
    for layer in (dark, line, redp):
        bg.alpha_composite(Image.fromarray(layer.astype(np.uint8), "RGBA"), (30, 30))
    bg.save(os.path.join(out, f"{name}_preview.png"))
    print(name, W, Hh)

build(int(sys.argv[3]) if len(sys.argv) > 3 else 1024, "cots_logo")
