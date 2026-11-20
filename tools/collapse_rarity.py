"""One-off for COS-109 (2026-11-20): rewrite content rarities from the original eight tiers to four.

Common/Uncommon -> Common (0), Magic -> Magic (1), Rare/Epic -> Rare (2), Legendary/Mythic/Primal -> Legendary (3).
Every unique item (non-empty uniqueEffects, or "manual": true) becomes Legendary. Item stats map the same way.
Edits the "rarity" numbers in place so the files keep their formatting. Run once from the repo root:
    python -I tools/collapse_rarity.py
"""
import json
import re
import sys

def legacy(r):
    return 0 if r <= 1 else 1 if r == 2 else 2 if r in (3, 4) else 3

def rewrite(path, key, new_value):
    text = open(path, encoding="utf-8").read()
    entries = json.loads(text)[key]
    values = [new_value(e) for e in entries if "rarity" in e]
    hits = list(re.finditer(r'"rarity"\s*:\s*-?\d+', text))
    if len(hits) != len(values):
        sys.exit(f"{path}: {len(hits)} rarity fields but {len(values)} entries with rarity; not touching it")
    out, last = [], 0
    for m, v in zip(hits, values):
        out.append(text[last:m.start()]); out.append(f'"rarity": {v}'); last = m.end()
    out.append(text[last:])
    open(path, "w", encoding="utf-8", newline="").write("".join(out))
    counts = {r: values.count(r) for r in range(4)}
    print(f"{path}: {counts}")

def item(e):
    if e.get("uniqueEffects") or e.get("manual"):
        return 3
    return legacy(e["rarity"])

for p in ("content/items.json", "content/items_island.json"):
    rewrite(p, "items", item)
rewrite("content/item_stats.json", "itemStats", lambda e: legacy(e["rarity"]))
