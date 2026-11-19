"""Merge translated strings into content/text/<lang>.json and record the English they were made from.

  python -I tools/text_apply.py DIR [-l fr ...] [--allow-digits]

DIR holds <lang>.json files, flat {key: translated string}, for the keys exported by tools/text_todo.py
(<lang>.todo.json -> <lang>.json). Each string is checked against English first (same digits, same '%' count,
same {0}-style placeholders, not empty); a language is written only if all its strings pass, otherwise the
problems are listed and that language is skipped.
--allow-digits turns digit-count mismatches into warnings, for languages that write "one" or "a minute" as a digit
("1分間"); review the warnings before using it. Keys that are not in en.json are rejected.
"""
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script dir
import textlib as T  # noqa: E402

DIGITS = re.compile(r"\d+")
PLACEHOLDER = re.compile(r"\{\d+\}")


def problems_for(en_text, text):
    if not isinstance(text, str) or not text.strip():
        return ["empty or not a string"]
    out = []
    if sorted(DIGITS.findall(text)) != sorted(DIGITS.findall(en_text)):
        out.append(f"digits differ: en={en_text!r} got={text!r}")
    if text.count("%") != en_text.count("%"):
        out.append(f"'%' count differs: en={en_text!r} got={text!r}")
    if sorted(PLACEHOLDER.findall(text)) != sorted(PLACEHOLDER.findall(en_text)):
        out.append(f"placeholders differ: en={en_text!r} got={text!r}")
    return out


def main(argv):
    args = list(argv)
    only = []
    allow_digits = False
    src = None
    while args:
        a = args.pop(0)
        if a in ("-l", "--lang"):
            only.append(args.pop(0))
        elif a == "--allow-digits":
            allow_digits = True
        else:
            src = Path(a)
    if src is None:
        print(__doc__)
        return 2
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    en, err = T.load_flat(T.TEXT_DIR / "en.json")
    if en is None:
        print(f"en.json: {err}")
        return 1
    sidecar = T.load_sidecar()
    failed = False
    for lang in only or T.LANGUAGES:
        p = src / f"{lang}.json"
        if not p.exists():
            continue
        new, err = T.load_flat(p)
        if new is None:
            print(f"{lang}: {err}")
            failed = True
            continue
        bad = []
        for k, v in new.items():
            if k not in en:
                bad.append(f"{k}: not in en.json")
                continue
            for m in problems_for(en[k], v):
                if allow_digits and m.startswith("digits differ"):
                    print(f"    warning {lang} {k}: {m}")
                else:
                    bad.append(f"{k}: {m}")
        if bad:
            print(f"{lang}: NOT WRITTEN, {len(bad)} problem(s)")
            for b in bad[:40]:
                print(f"    {b}")
            failed = True
            continue
        table, err = T.load_flat(T.TEXT_DIR / f"{lang}.json")
        table = table or {}
        table.update(new)
        T.write_table(T.TEXT_DIR / f"{lang}.json", table)
        h = sidecar.setdefault(lang, {})
        for k in new:
            h[k] = T.src_hash(en[k])
        print(f"{lang}: applied {len(new)} string(s)")
    T.write_sidecar(sidecar)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
