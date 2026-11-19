"""Shared helpers for the text/translation tools (see docs/translations.md)."""
import hashlib
import json
from pathlib import Path

# Keep in sync with TextDb.Languages (src/ChampionsOfTheSons.Core/Content/TextDb.cs).
LANGUAGES = ["zh-Hans", "zh-Hant", "fi", "fr", "de", "it", "ja", "ko", "pl", "ru", "es", "tr"]

ROOT = Path(__file__).resolve().parent.parent
TEXT_DIR = ROOT / "content" / "text"
# Which English string each translation was made from: {lang: {key: hash}}. Kept outside content/ so it is not embedded.
SIDECAR = ROOT / "tools" / "translations" / "source-hashes.json"


def src_hash(text: str) -> str:
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:8]


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


def dump_table(table: dict) -> str:
    """The layout every text table uses: sorted keys, 1-space indent, CRLF, no trailing newline."""
    return json.dumps(table, indent=1, ensure_ascii=False, sort_keys=True).replace("\n", "\r\n")


def write_table(path: Path, table: dict):
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(dump_table(table))


def load_sidecar(path: Path = SIDECAR) -> dict:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def write_sidecar(data: dict, path: Path = SIDECAR):
    """One line per key so diffs stay readable."""
    path.parent.mkdir(parents=True, exist_ok=True)
    out = ["{"]
    langs = sorted(data)
    for i, lang in enumerate(langs):
        out.append(f" {json.dumps(lang)}: {{")
        keys = sorted(data[lang])
        for j, k in enumerate(keys):
            out.append(f"  {json.dumps(k)}: {json.dumps(data[lang][k])}" + ("," if j < len(keys) - 1 else ""))
        out.append(" }" + ("," if i < len(langs) - 1 else ""))
    out.append("}")
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("\n".join(out) + "\n")


def needs_translation(en: dict, lang_table: dict, hashes: dict):
    """Keys of en that the language needs (re)translated: (missing, stale)."""
    missing = sorted(k for k in en if k not in lang_table)
    stale = sorted(k for k in en if k in lang_table and hashes.get(k) != src_hash(en[k]))
    return missing, stale
