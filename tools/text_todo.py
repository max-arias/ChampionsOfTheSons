"""List or export the keys each language still needs translated (missing or stale).

  python -I tools/text_todo.py                      summary per language
  python -I tools/text_todo.py -v                   plus the keys
  python -I tools/text_todo.py --export DIR [-l fr] write DIR/<lang>.todo.json: {key: english, ...} per language
  python -I tools/text_todo.py --baseline [-l fr]   record every current translation as made from the current English
                                                    (use once, or after a deliberate "these are still fine" review)

Missing = key absent from the language file. Stale = English changed since the translation was recorded
(tools/translations/source-hashes.json). Translate the exported files, then run tools/text_apply.py.
"""
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script dir
import textlib as T  # noqa: E402


def main(argv):
    args = list(argv)
    verbose = False
    export = None
    only = []
    baseline = False
    while args:
        a = args.pop(0)
        if a in ("-v", "--verbose"):
            verbose = True
        elif a == "--export":
            export = Path(args.pop(0))
        elif a in ("-l", "--lang"):
            only.append(args.pop(0))
        elif a == "--baseline":
            baseline = True
        else:
            print(__doc__)
            return 2
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    en, err = T.load_flat(T.TEXT_DIR / "en.json")
    if en is None:
        print(f"en.json: {err}")
        return 1
    frags = sorted(p.name for p in T.TEXT_DIR.glob("en.*.json"))
    if frags:
        print(f"note: unmerged fragments {', '.join(frags)}; run tools/text_merge_fragments.py first")
    sidecar = T.load_sidecar()
    langs = only or T.LANGUAGES
    if baseline:
        for lang in langs:
            table, err = T.load_flat(T.TEXT_DIR / f"{lang}.json")
            if table is None:
                print(f"{lang}: {err}")
                continue
            sidecar[lang] = {k: T.src_hash(en[k]) for k in table if k in en}
            print(f"{lang}: recorded {len(sidecar[lang])} keys")
        T.write_sidecar(sidecar)
        return 0
    if export:
        export.mkdir(parents=True, exist_ok=True)
    for lang in langs:
        table, err = T.load_flat(T.TEXT_DIR / f"{lang}.json")
        if table is None:
            table = {}
        missing, stale = T.needs_translation(en, table, sidecar.get(lang, {}))
        print(f"{lang}: {len(missing)} missing, {len(stale)} stale")
        if verbose:
            for k in missing:
                print(f"    missing {k}")
            for k in stale:
                print(f"    stale   {k}")
        if export:
            todo = {k: en[k] for k in missing + stale}
            if todo:
                (export / f"{lang}.todo.json").write_text(
                    json.dumps(todo, indent=1, ensure_ascii=False, sort_keys=True), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
