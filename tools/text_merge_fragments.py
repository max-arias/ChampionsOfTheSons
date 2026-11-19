"""Merge every content/text/en.<feature>.json fragment into en.json and delete the fragment.

Run with:  python -I tools/text_merge_fragments.py [--dry-run] [--keep]

Feature sessions add strings in a fragment (en.<feature>.json) so parallel work does not collide on en.json.
This folds them in. Safe to rerun: with no fragments it does nothing. A key that already exists in en.json with
a different value is a conflict: nothing is written and the script exits 1. Afterwards translate the new keys
(docs/translations.md).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script dir
import textlib as T  # noqa: E402


def main(argv):
    dry = "--dry-run" in argv
    keep = "--keep" in argv
    frags = sorted(T.TEXT_DIR.glob("en.*.json"))
    if not frags:
        print("no fragments")
        return 0
    en, err = T.load_flat(T.TEXT_DIR / "en.json")
    if en is None:
        print(f"en.json: {err}")
        return 1
    added = {}
    conflicts = []
    for p in frags:
        data, err = T.load_flat(p)
        if data is None:
            print(f"{p.name}: {err}")
            return 1
        for k, v in data.items():
            if not isinstance(v, str):
                print(f"{p.name}: {k}: value is not a string")
                return 1
            old = en.get(k, added.get(k))
            if old is not None and old != v:
                conflicts.append(f"{p.name}: {k}: en.json/other fragment has {old!r}, fragment has {v!r}")
            else:
                added[k] = v
    if conflicts:
        print("\n".join(conflicts))
        print("conflicts: nothing written")
        return 1
    new = {k: v for k, v in added.items() if k not in en}
    print(f"{len(frags)} fragment(s), {len(new)} new key(s), {len(added) - len(new)} already in en.json")
    if dry:
        return 0
    en.update(new)
    T.write_table(T.TEXT_DIR / "en.json", en)
    if not keep:
        for p in frags:
            p.unlink()
            print(f"deleted {p.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
