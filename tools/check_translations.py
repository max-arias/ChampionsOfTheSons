"""Check content/text/<lang>.json against content/text/en.json.

Run with:  python -I tools/check_translations.py [-v] [-l LANG ...] [CONTENT_TEXT_DIR]

Per language it reports
  * missing keys   (in en.json, not in the language file)           -> exit 1
  * extra keys     (in the language file, not in en.json)           -> exit 1
  * unreadable file, invalid JSON, non-flat object, non-string or empty values -> exit 1
  * strings whose digits differ from English (digit runs compared as a sorted list, so "1,220" vs "1.220"
    is fine but "15%" vs "51%" is not), and strings whose '%' count differs   -> reported, exit 0
  * strings identical to English that contain letters (count only; -v lists them) -> reported, exit 0
A shipped language that has no file at all counts as missing every key.
"""
import json
import re
import sys
from pathlib import Path

# Keep in sync with TextDb.Languages (src/ChampionsOfTheSons.Core/Content/TextDb.cs).
LANGUAGES = ["zh-Hans", "zh-Hant", "fi", "fr", "de", "it", "ja", "ko", "pl", "ru", "es", "tr"]

DIGITS = re.compile(r"\d+")
LETTER = re.compile(r"[^\W\d_]", re.UNICODE)


def load_flat(path: Path):
    """Return (dict, error). dict is None when the file cannot be used."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except OSError as e:
        return None, f"cannot read: {e}"
    except ValueError as e:
        return None, f"invalid JSON: {e}"
    if not isinstance(data, dict):
        return None, "top level is not a JSON object"
    return data, None


def show(label, items, verbose, limit=15):
    if not items:
        return
    print(f"  {label}: {len(items)}")
    shown = items if verbose else items[:limit]
    for line in shown:
        print(f"    {line}")
    if len(shown) < len(items):
        print(f"    ... {len(items) - len(shown)} more (use -v)")


def main(argv):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    verbose = False
    only = []
    text_dir = None
    args = list(argv)
    while args:
        a = args.pop(0)
        if a in ("-v", "--verbose"):
            verbose = True
        elif a in ("-l", "--lang"):
            only.append(args.pop(0))
        else:
            text_dir = Path(a)
    if text_dir is None:
        text_dir = Path(__file__).resolve().parent.parent / "content" / "text"

    en, err = load_flat(text_dir / "en.json")
    if en is None:
        print(f"en.json: {err}")
        return 1
    en_keys = set(en)

    langs = only or LANGUAGES
    failed = False
    for lang in langs:
        path = text_dir / f"{lang}.json"
        print(f"{lang}:")
        data, err = load_flat(path)
        if data is None:
            print(f"  ERROR {err}")
            failed = True
            continue
        problems = []
        bad_values = [k for k, v in data.items() if not isinstance(v, str) or not v.strip()]
        if bad_values:
            problems.append(f"non-string or empty values: {len(bad_values)}")
            show("non-string/empty values", sorted(bad_values), verbose)
        keys = set(data)
        missing = sorted(en_keys - keys)
        extra = sorted(keys - en_keys)
        show("missing keys", missing, verbose)
        show("extra keys", extra, verbose)

        digit_diff, pct_diff, same = [], [], []
        for k in sorted(keys & en_keys):
            v, e = data[k], en[k]
            if not isinstance(v, str):
                continue
            if sorted(DIGITS.findall(v)) != sorted(DIGITS.findall(e)):
                digit_diff.append(f"{k}: en={e!r} {lang}={v!r}")
            if v.count("%") != e.count("%"):
                pct_diff.append(f"{k}: en={e!r} {lang}={v!r}")
            if v == e and LETTER.search(e):
                same.append(k)
        show("digits differ from English", digit_diff, verbose)
        show("percent-sign count differs", pct_diff, verbose)
        if same:
            print(f"  identical to English (letters present): {len(same)}" + (" -> " + ", ".join(same) if verbose else ""))

        print(f"  {len(keys & en_keys)}/{len(en_keys)} keys, {len(missing)} missing, {len(extra)} extra, "
              f"{len(digit_diff)} digit diffs, {len(pct_diff)} percent diffs")
        if missing or extra or bad_values:
            failed = True

    unknown = sorted(p.stem for p in text_dir.glob("*.json") if p.stem != "en" and p.stem not in LANGUAGES)
    if unknown and not only:
        print(f"note: language files not in the shipped list: {', '.join(unknown)}")
    print("FAIL" if failed else "OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
