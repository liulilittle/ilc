# GAPS.md -- stringsort recorded findings (e2e batch C76, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 64-line
byte-exact). Active probes cover the string SORT/COMPARE ORDER dimensions:
prefix-lattice sorting, duplicates/null/empty packing, digit-vs-letter
ordering, a 20-element mixed array (exact order + pairwise CompareOrdinal
scan), CompareTo clamp, CompareOrdinal raw diff, mode-agreement signs, and
BinarySearch insertion points. Gaps below are RECORDED, NOT FIXED (this
batch creates test cases only).

## D-SS-1 -- net48 Array.Sort(string[]) is CURRENT-CULTURE; runtime is ORDINAL

- **Phenomenon**: .NET Framework 4.8 `Array.Sort(string[])` uses the
  CURRENT-CULTURE comparer (zh-CN here), while the runtime string sort
  (il2cpp_array_sort_string, arraysort.cpp CompareOrdinal) is ORDINAL. For
  ASCII-lowercase (+ null/empty) inputs the two orders coincide, so every
  sort probe passes byte-exact; a MIXED-CASE array would diverge
  (e.g. net48 might place "Cherry" before "apple" under zh-CN culture).
- **Case adaptation**: every sort input is ASCII lowercase (+ null/empty);
  the divergence is documented (sortdeep/arraysortdeep precedent), never
  exercised.
- **Suspected root file**: `src/runtime/src/arraysort.cpp` (CompareOrdinal
  in the string sort path -- a culture-aware comparer would be needed to
  match net48 exactly).
- **MEASURED 2026-10 (culture batch 2, W7)**: the zh-CN collation is NOT a
  simple case ASCII rule. `String.Compare` under zh-CN: "a" vs "A" -> -1
  (case tiebreak, lower before upper), "a-b" vs "ab" -> +1 (hyphen has a
  sort weight ABOVE the letter), "a_b" vs "ab" -> -1 (underscore BELOW
  nothing), "a b" vs "ab" -> -1, "ss" vs "\u00DF" -> 0 (expansion-equal),
  "e" vs "\u00E9" -> -1 (accent secondary), "\u03B1" vs "\u0391" -> -1,
  "\u6211" vs "\u4F60" -> +1 (CJK). Explicit cultures change the outcome
  ("i" vs "I" is +1 under tr-TR, -1 under zh-CN) -- os NLS (LCMapStringEx)
  backed. Evidence: `.opencode/tmp/gn20-legacy/acoll` and the
  `p-culture-str` probe diff.

## D-SS-2 -- string.Compare(a,b) default mode is folded to ORDINAL

- **Phenomenon**: the 2-arg `string.Compare(a,b)` (net48: culture-aware)
  maps to the ORDINAL static comparator (MethodMap.String.cs Compare arm,
  icall_string_compare_static) -- documented deviation. For ASCII-lowercase
  inputs the SIGN agrees with net48, so the mode-agreement probes assert
  only SIGN (cm1..cm3, cm10..cm12), never a raw value; raw-value probes use
  StringComparison.Ordinal (cm5/cm6) or CompareOrdinal directly.
- **Case adaptation**: sign-only assertions for the culture-default modes.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.String.cs` (Compare
  2-arg mapping), `src/runtime/src/stringext2.cpp`.
