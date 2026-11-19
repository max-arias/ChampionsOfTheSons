#!/usr/bin/env python
"""Contact sheets of content/icons, one or more PNGs per category.

  python -I tools/icons/contact_sheet.py [OUTDIR]     # default OUTDIR = %TEMP%/cots-icons-sheets
Each cell: icon (128px), entry name, matched source icon. Sheets hold 6 rows x 8 cols.
"""
import importlib.util, json, os, sys, tempfile
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("gen", HERE / "generate_icons.py")
gen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen)

COLS, ROWS, CW, CH = 8, 6, 150, 178


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(tempfile.gettempdir()) / "cots-icons-sheets"
    out.mkdir(parents=True, exist_ok=True)
    mapping = json.load(open(HERE / "mapping.json", encoding="utf8"))
    groups = {}
    for e in gen.load_entries():
        groups.setdefault(e["kind"] if e["kind"] != "item" else "item_" + e["type"], []).append(e)
    font = ImageFont.load_default()
    paths = []
    for g, ents in sorted(groups.items()):
        per = COLS * ROWS
        for page in range(0, len(ents), per):
            chunk = ents[page:page + per]
            rows = (len(chunk) + COLS - 1) // COLS
            im = Image.new("RGB", (COLS * CW, rows * CH), (34, 36, 40))
            d = ImageDraw.Draw(im)
            for i, e in enumerate(chunk):
                x, y = (i % COLS) * CW, (i // COLS) * CH
                p = gen.OUT / f"{e['id']}.png"
                if p.exists():
                    ic = Image.open(p).convert("RGBA")
                    im.paste(ic, (x + 11, y + 4), ic)
                d.text((x + 4, y + 134), e["name"][:24], fill=(235, 235, 235), font=font)
                d.text((x + 4, y + 147), mapping[e["id"]].split("/")[-1][:24], fill=(130, 150, 170), font=font)
                if e["kind"] == "item":
                    d.text((x + 4, y + 160), f"r{e['rarity']}", fill=(180, 180, 90), font=font)
            f = out / f"{g}_{page // per + 1}.png"
            im.save(f)
            paths.append(f)
    print("\n".join(map(str, paths)))


if __name__ == "__main__":
    main()
