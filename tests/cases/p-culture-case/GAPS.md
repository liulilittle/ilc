# GAPS.md -- p-culture-case recorded findings (culture batch 2, GN-20)

Case status: **PASS** (mingw, net48 oracle 1977-line byte-exact).

Scope: the FULL BMP culture case map of `string.ToUpper()` / `ToLower()`
(and the Invariant aliases that share the same icalls). For every BMP code
unit the compressed output prints `up|XXXX|=` only when `ToUpper()` changes
the unit (and symmetric for `ToLower`). `expected.txt` is the exact net48
oracle under the zh-CN host culture; the transpiled runtime reproduces it
byte-for-byte (verified: 1977 lines, 0 diff).

## Fixed here (byte-exact, oracle-generated)

- `stringext.cpp` `ToUpperUnit`/`ToLowerUnit` replaced the hand-rolled
  ASCII/Latin-1/Greek/Cyrillic ranges with the FULL net48 zh-CN case map
  (`stringcase.cpp`, generated from the casedump oracle -- see
  `.opencode/tmp/gn20-legacy/casedump`). 991 upper + 986 lower BMP pairs.
- The old rule had a DEAD final-sigma branch: the U+03B1-U+03C9 range rule
  swallowed U+03C2 and emitted the unassigned U+03A2 instead of net48's
  U+03A3. Now correct.
- net48 zh-CN measured: NO multi-unit expansions on the BMP (U+00DF stays
  U+00DF as an uppercase; U+0130 `I-dot` -> `i`; U+0131 dotless-i -> `I`;
  U+00B5 micro -> Greek capital Mu, etc.). A pure 1:1 unit map reproduces
  culture-correct casing for the whole BMP.

## Recorded, NOT fixed (OS-NLS-backed collation, see W7)

- `String.Compare` / `CompareTo` / `IndexOf(string)` / `StartsWith` /
  `EndsWith` (default) and the CurrentCulture/InvariantCulture comparison
  modes remain ORDINAL(+ASCII fold). net48 under zh-CN is real collation:
  case tiebreak (lower sorts before upper), punctuation weights (e.g.
  "a-b" > "ab", "a_b" < "ab", space < nothing), U+00DF==ss expansion
  equality, accent secondary weights, Greek/Cyrillic lowercase-before-
  uppercase, CJK pinyin/stroke ordering -- all OS NLS backed
  (LCMapStringEx). Explicit culture args change outcomes (tr-TR "i" vs "I"
  -> 1 while zh-CN -> -1). Replicated byte-exact only by porting the OS
  collation tables; recorded, not attempted here. Evidence:
  `.opencode/tmp/gn20-legacy/acoll` + the `p-culture-str` probe diff
  (55 remaining culture-collation lines).