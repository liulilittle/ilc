# GAPS.md -- p-numfmt-nfi recorded findings (probe batch 3, GN-20 numfmt)

Case status: **PASS** (mingw, net48 oracle 79-line byte-exact).

Scope: the NumberFormatInfo GETTER surface under the invariant / zh-CN
two-culture dispatch (audit P0-4: "NumberFormatInfo getters hard-coded
invariant literals") + the current-culture composite `String.Format`
rows. `expected.txt` is the exact net48 oracle under the zh-CN host
culture; the transpiled runtime reproduces it byte-for-byte (verified:
79 lines, 0 diff — difftest mbh oracle/runtime sha identical).

## FIXED this batch (byte-exact, runtime icall two-culture dispatch)

`src/runtime/src/numberformat.cpp` + `numberformat.h` add the remaining
NumberFormatInfo getters as culture-name receiver icalls (same model as the
existing currency/infinity symbol dispatch):
- string: CurrencyDecimalSeparator "." / CurrencyGroupSeparator "," /
  PerMilleSymbol U+2030 (zh-CN == invariant).
- int: NumberNegativePattern 1, NumberDecimalDigits 2, CurrencyDecimalDigits
  2, CurrencyPositivePattern 0, PercentDecimalDigits 2 (zh-CN == invariant);
  CurrencyNegativePattern (zh-CN 2 / invariant 0), PercentPositivePattern
  (1 / 0), PercentNegativePattern (1 / 0) — culture-name dispatched.
- int[]: Number/Currency/PercentGroupSizes {3}; string[]: NativeDigits
  {"0".."9"} (zh-CN == invariant).
- D1 (auditB re-review): get_DigitSubstitution == DigitShapes.None == 1 and
  get_IsReadOnly == true on BOTH net48 canonical singletons (measured —
  oracle_digitsub.txt); wired as constant icalls.
`MethodMap.NumberFormat.cs` maps every instance getter to the icall
(get_CurrentInfo -> "zh-CN"/invariant culture-name value, so the dispatch is
byte-exact). `formatext.cpp` composite engine (`icall_format_composite_item`)
gained the boxed UInt32/UInt64 branches (values <= INT64_MAX route to the
signed int64 engine = byte-exact; UInt32 full range via int64 zero-extend).

empirical net48 facts pinned (oracle_net48.txt / oracle_xcult.txt on the
zh-CN host): en-US/de-DE explicit NumberFormat values differ (CurrencySymbol
$, NumberDecimalSeparator "," for de-DE, CurrencyNegativePattern 0/8) — the
ILC pinned host (zh-CN current) keeps separators identical; explicit
arbitrary-culture NumberFormatInfo has NO ILC surface (no CultureInfo object
model) — recorded as W7 below.

## W7 -- not aligned (honest white list)

1. **Arbitrary explicit cultures** (`new CultureInfo("en-US"/"de-DE")` +
   `.NumberFormat` getters / ToString(format, provider)): the ILC runtime has
   NO CultureInfo object model; a culture-parameterized NumberFormatInfo
   cannot be represented. Measured Oracle deltas (oracle_xcult.txt): de-DE
   NumberDecimalSeparator ",", CurrencySymbol U+20AC, CurrencyNegativePattern
   8; en-US CurrencySymbol "$", CurrencyNegativePattern 0. Same class as the
   string-collation W7 (culture-parameterized, depends on OS/arbitrary-culture
   data).

2. **Unsigned full-range decimal-family formats** (uint32 >= 2^31, uint64 >=
   2^63 in D/N/C/E/F/P/G and the composite engine): pre-existing documented
   subset boundary (MethodMap.Format.cs DOCUMENTED SUBSET BOUNDARY; the
   unsigned integers ride the SIGNED digit loops). net48 oracle:
   `ulong.MaxValue.ToString("N0")` = "18,446,744,073,709,551,615",
   `(uint)4000000000.ToString("N0")` = "4,000,000,000"; binary literal
   bit-pattern re-interpretation into the signed engines prints negative.
   Recorded, NOT asserted here (keep the case byte-exact green); a full
   unsigned format engine is out of this probe's scope.