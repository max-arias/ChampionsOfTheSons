"""Lay out each perk tree in a shape that stands for its class (COS-169): melee a sword, ranged a bow and arrow, magic a
flame, defense a shield, support a heart, utility a compass. Writes the x/y of every perk in content/perks.json; unlock paths
do not change.

How: each shape is a union of polygons and discs. A hex grid of points inside it is made just dense enough to hold the tree,
points are ordered by how far they are from the shape's base, and perks are ordered by unlock depth (roots first; ties by
their old x), then matched one to one. So roots sit at the base and paths grow along the shape.

  python -I tools/perk_layout.py            # rewrite content/perks.json
  python -I tools/perk_layout.py --check    # print the trees as ASCII, change nothing
"""
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PERKS = ROOT / "content" / "perks.json"

# Shapes in a 8.5 x 10 box (the book's tree area is about that aspect), y down. "base" says where the roots go.
W, H = 8.5, 10.0


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def arc_band(cx, cy, r0, r1, a0, a1, steps=24):
    """A thick arc (angles in degrees, 0 = right, 90 = down) as one polygon."""
    outer = [(cx + r1 * math.cos(math.radians(a0 + (a1 - a0) * i / steps)), cy + r1 * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]
    inner = [(cx + r0 * math.cos(math.radians(a1 - (a1 - a0) * i / steps)), cy + r0 * math.sin(math.radians(a1 - (a1 - a0) * i / steps))) for i in range(steps + 1)]
    return outer + inner


SHAPES = {
    # Blade with a point up top, crossguard, grip, pommel; roots at the pommel.
    "melee_offense": dict(base="bottom", polys=[
        [(3.75, 1.4), (4.25, 0.2), (4.75, 1.4), (4.75, 6.4), (3.75, 6.4)],
        rect(1.4, 6.4, 7.1, 7.15),
        rect(3.85, 7.15, 4.65, 9.0),
    ], discs=[(4.25, 9.4, 0.55)]),
    # A bow (thick arc on the left) with its string and an arrow pointing right; roots at the grip, paths fly with the arrow.
    "ranged_offense": dict(base="left", polys=[
        arc_band(5.2, 5.0, 3.9, 4.8, 135, 225),
        rect(1.55, 1.2, 1.95, 8.8),
        rect(0.6, 4.75, 7.0, 5.25),
        [(7.0, 4.2), (8.4, 5.0), (7.0, 5.8)],
        [(0.6, 4.75), (0.1, 4.3), (0.1, 5.7), (0.6, 5.25)],
    ], discs=[]),
    # A flame: round base, a tall middle tongue and two side tongues; roots at the base.
    "magic_offense": dict(base="bottom", polys=[
        [(4.25, 0.2), (5.4, 2.6), (6.2, 1.6), (6.9, 4.2), (7.3, 6.6), (6.4, 8.7), (4.25, 9.8), (2.1, 8.7), (1.2, 6.6), (1.6, 4.2), (2.3, 1.6), (3.1, 2.6)],
    ], discs=[]),
    # A heater shield: flat top, straight sides, a point at the bottom; roots along the top.
    "defense": dict(base="top", polys=[
        [(0.6, 0.6), (7.9, 0.6), (7.9, 5.0), (7.2, 7.2), (4.25, 9.7), (1.3, 7.2), (0.6, 5.0)],
    ], discs=[]),
    # A heart: two lobes and the point; roots at the point.
    "support": dict(base="bottom", polys=[
        [(0.55, 3.6), (7.95, 3.6), (4.25, 9.4)],
    ], discs=[(2.45, 3.2, 2.0), (6.05, 3.2, 2.0)]),
    # A compass: a ring and a needle; roots at the centre, paths go out along the needle to the ring.
    "utility": dict(base="center", polys=[
        [(4.25, 0.9), (5.0, 5.0), (4.25, 9.1), (3.5, 5.0)],
        arc_band(4.25, 5.0, 3.3, 4.15, 0, 360, steps=48),
    ], discs=[]),
}


def inside_poly(x, y, poly):
    n, c = len(poly), False
    for i in range(n):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % n]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


def inside(shape, x, y):
    if any(inside_poly(x, y, p) for p in shape["polys"]):
        return True
    return any((x - cx) ** 2 + (y - cy) ** 2 <= r * r for cx, cy, r in shape["discs"])


def grid(shape, step):
    pts = []
    row = 0
    y = step / 2
    while y < H:
        x = step / 2 + (step / 2 if row % 2 else 0)
        while x < W:
            if inside(shape, x, y):
                pts.append((x, y))
            x += step
        y += step * math.sqrt(3) / 2
        row += 1
    return pts


def points_for(shape, n):
    """The densest-but-not-more hex grid with at least n points inside, thinned evenly to exactly n."""
    lo, hi = 0.15, 5.0
    for _ in range(40):
        mid = (lo + hi) / 2
        if len(grid(shape, mid)) >= n:
            lo = mid
        else:
            hi = mid
    pts = grid(shape, lo)
    pts.sort(key=lambda p: progress(shape, p))
    extra = len(pts) - n
    if extra > 0:
        drop = {round((i + 0.5) * len(pts) / extra) for i in range(extra)}
        pts = [p for i, p in enumerate(pts) if i not in drop][:n]
    return pts


def progress(shape, p):
    x, y = p
    b = shape["base"]
    if b == "bottom":
        return (H - y) * 10 + abs(x - W / 2) * 0.01
    if b == "top":
        return y * 10 + abs(x - W / 2) * 0.01
    if b == "left":
        return x * 10 + abs(y - H / 2) * 0.01
    return math.hypot(x - W / 2, y - H / 2) * 10 + (math.atan2(y - H / 2, x - W / 2) % (2 * math.pi)) * 0.01


def depths(perks):
    ids = {p["id"] for p in perks}
    parents = {p["id"]: [q for q in p.get("unlockPath", []) + p.get("requires", []) if q in ids] for p in perks}
    memo = {}

    def d(i, seen=()):
        if i in memo:
            return memo[i]
        if i in seen:
            return 0
        v = 0 if not parents[i] else 1 + max(d(q, seen + (i,)) for q in parents[i])
        memo[i] = v
        return v

    return {p["id"]: d(p["id"]) for p in perks}


def layout(data):
    out = {}
    by_cat = {}
    for p in data["perks"]:
        by_cat.setdefault(p["category"], []).append(p)
    for cat, perks in by_cat.items():
        shape = SHAPES[cat]
        dep = depths(perks)
        order = sorted(perks, key=lambda p: (dep[p["id"]], p["x"], p["y"], p["id"]))
        pts = points_for(shape, len(order))
        for p, (x, y) in zip(order, pts):
            out[p["id"]] = (round(x, 2), round(y, 2))
    return out


def ascii_view(data, pos, cat):
    cols, rows = 34, 20
    g = [[" "] * cols for _ in range(rows)]
    for p in data["perks"]:
        if p["category"] != cat:
            continue
        x, y = pos[p["id"]]
        g[min(rows - 1, int(y / H * rows))][min(cols - 1, int(x / W * cols))] = "o"
    return "\n".join("".join(r) for r in g)


def main():
    data = json.load(open(PERKS, encoding="utf-8"))
    pos = layout(data)
    if "--check" in sys.argv:
        for cat in SHAPES:
            print(f"== {cat} ({sum(1 for p in data['perks'] if p['category'] == cat)} perks)")
            print(ascii_view(data, pos, cat))
        return
    for p in data["perks"]:
        p["x"], p["y"] = pos[p["id"]]
    open(PERKS, "w", encoding="utf-8", newline="\n").write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    print("laid out", len(pos), "perks")


if __name__ == "__main__":
    main()
