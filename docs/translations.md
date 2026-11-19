# Translations

English is the source: `content/text/en.json`. Twelve languages sit next to it (`zh-Hans zh-Hant fi fr de it ja ko pl ru es tr`, the list in `TextDb.Languages` and `tools/textlib.py`). All tables are flat `{key: string}` JSON in one layout (sorted keys, 1-space indent, CRLF, no trailing newline), written by the tools below, never by hand.

Missing keys are safe at runtime (`TextDb` falls back to English), so features land English first and translation follows in the same branch or right after.

## Rules for feature work

- Add new English strings to a fragment `content/text/en.<feature>.json` (keys prefixed `ui.<feature>.`). Fragments are merged into English at load, so the game works before anyone merges them.
- Do not edit other languages by hand. Do not change an English string just to fix style without re-translating it (the checker will flag it).
- Content `name` fields in `content/*.json` follow `en.json`: run `python -I tools/text_sync_names.py` after changing a name.

## Workflow: new or changed English text

```powershell
python -I tools/text_merge_fragments.py        # fold en.<feature>.json fragments into en.json, delete them
python -I tools/text_todo.py                   # per language: how many keys are missing or stale
python -I tools/text_todo.py --export $env:TEMP\todo   # writes <lang>.todo.json = {key: English}
# translate each <lang>.todo.json into <lang>.json (same keys, translated values); one agent or person per language
python -I tools/text_apply.py $env:TEMP\out    # validates (digits, %, {0} placeholders), merges, records the source hash
python -I tools/check_translations.py          # strict: must print OK
dotnet run --project tools/ContentCheck -- content
```

Translators use `tools/translations/glossaries.md` (the per-language term choices: rarity and difficulty tiers, spell names, stats; also kept in the vault note "Translation glossaries") and look at how related keys are already translated in the language's own table. `tools/translations/glossary-en.md` is the English-side term list from the first run. Add a term to the language's section when a new recurring word needs a fixed translation.

## Stale detection

`tools/translations/source-hashes.json` records, per language and key, a hash of the English string the translation was made from (`text_apply.py` writes it). The checker reports a key as stale when today's English hashes differently, or when no hash was recorded. Changing an English string therefore flags that key in all 12 languages until it is re-translated and applied.

- A deliberate "the old translations are still right" edit (typo fix): run `python -I tools/text_todo.py --baseline -l <lang> ...` after checking, or re-apply the existing strings.
- Native-speaker fixes made directly in a language file do not change the English hash and need nothing.

## Severity

`build.ps1` runs `check_translations.py --lenient`: it fails on structural problems (invalid file, extra keys, empty values) and only prints missing/stale as warnings, so a feature branch with new English is not blocked. Before a release run the strict form (`python -I tools/check_translations.py`), which also fails on missing and stale keys.

## Who translates

The branch that adds or changes English text also translates it (machine pass per language using the glossaries, via `text_todo --export` / `text_apply`) before it is merged. A native-speaker review is tracked separately (COS-39).
