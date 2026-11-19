"""Copy English display names from content/text/en.json into the "name" fields of the content JSON.

  python -I tools/text_sync_names.py [--check]

The text table is the source of truth for what the player reads; the "name" in items.json, sets.json, perks.json,
spells.json, item_stats.json ... is only a fallback and a label for authors. This rewrites each "name" whose
"<id>.name" key in en.json differs (a plain text edit, so the file layout is kept). --check only lists them and
exits 1 if any differ.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script dir
import textlib as T  # noqa: E402

ID = re.compile(r'"id"\s*:\s*"([^"]+)"')
NAME = re.compile(r'("name"\s*:\s*)"((?:[^"\\]|\\.)*)"')


def main(argv):
    check = "--check" in argv
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    en, err = T.load_flat(T.TEXT_DIR / "en.json")
    if en is None:
        print(f"en.json: {err}")
        return 1
    changed = 0
    for path in sorted((T.ROOT / "content").glob("*.json")):
        raw = path.read_bytes().decode("utf-8")
        ids = list(ID.finditer(raw))
        out = []
        pos = 0
        n = 0
        for i, m in enumerate(ids):
            end = ids[i + 1].start() if i + 1 < len(ids) else len(raw)
            key = m.group(1) + ".name"
            if key not in en:
                continue
            nm = NAME.search(raw, m.end(), end)
            if not nm:
                continue
            if json.loads('"' + nm.group(2) + '"') == en[key]:
                continue
            out.append(raw[pos:nm.start()])
            out.append(nm.group(1) + json.dumps(en[key], ensure_ascii=False))
            pos = nm.end()
            n += 1
            if check:
                print(f"{path.name}: {m.group(1)}: {nm.group(2)!r} -> {en[key]!r}")
        if n:
            changed += n
            if not check:
                out.append(raw[pos:])
                path.write_bytes("".join(out).encode("utf-8"))
                print(f"{path.name}: {n} name(s) updated")
    if not changed:
        print("names in sync")
    return 1 if (check and changed) else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
