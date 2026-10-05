# GAPS.md -- regexunicode recorded findings (e2e batch C57, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 80-line
byte-exact; expected.txt == oracle).

## D-REG-UNI-1 -- \p{...} Unicode categories / \uXXXX / \xNN / \b / lookarounds out of subset

- **Phenomenon**: `\p{L}`, `\uXXXX`, `\xNN`, `\b`/`\B` word boundaries,
  `(?:...)`/`(?=...)`/`(?!...)`/`(?<=...)`/`(?<!...)`/`(?>)`/`(?<name>...)`/
  `(?i)` all throw (regex_parse.cpp UnsupportedConstruct; net48 succeeds) --
  the pre-existing regex-subset gap also recorded in regexdeep2/regex2.
- **Disposition**: recorded. The "Unicode classes" axis is exercised through
  explicit BMP code-unit BLOCK RANGES (`[一-龥]` CJK, `[ぁ-ん]` hiragana,
  `[ァ-ヶ]` katakana, `[가-힣]` hangul, `[０-９]` fullwidth, `[α-ω]` Greek)
  which both engines evaluate as code-unit ranges -- no \p{...} probe can run.

## D-REG-UNI-2 -- surrogate pairs (emoji): Set/Any nodes are ONE code unit, net48 treats a pair as one char

- **Phenomenon**: the runtime `Set`/`Any` nodes match a single char16_t unit,
  so `.`/`[...]`/quantifiers over non-BMP input behave differently from net48
  (which treats a surrogate pair as one char). Example divergences that would
  byte-diff: `Regex.Match("😀",".")` length 1 vs 2; `[😀]`/`😀{2}` semantics.
- **Disposition**: recorded. Emoji is exercised ONLY as a LITERAL in
  pattern/input/replacement (code-unit identity, identical on both engines):
  IsMatch/Replace/Split with literal emoji all PASS. No `.`/char-class/
  quantifier probe may run on surrogate-pair input.

## D-REG-UNI-3 -- \w / \s are ASCII-only in the runtime, Unicode in net48

- **Phenomenon**: the runtime `\w` = `[A-Za-z0-9_]` and `\s` = `[ \t\r\n\f\v]`
  (docs §1.4-4 ASCII scope) while net48 `\w`/`\s` match the full Unicode
  classes. `Regex.IsMatch("中","\\w")` would diverge.
- **Disposition**: recorded (regexdeep2 precedent). Only `\d` (ASCII digits on
  both sides) is probed against CJK-mixed input; `\w`/`\s` are kept on ASCII
  input only.

## D-REG-UNI-4 -- Regex.Match instance result graph not wired

- **Phenomenon**: `Regex.Match` maps to the runtime string[] group-captures
  icall; the net48 `Match` object graph (`.Groups`/`.Value`/`.Index`/`.Success`)
  is unwired (instance forms LOUD-rejected in MethodMap.Regex.cs).
- **Disposition**: recorded. Group capture/replacement semantics are covered
  via `Regex.Replace` `$1`/`$2` reordering (the regexdeep2 precedent).
