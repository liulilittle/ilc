# GAPS.md -- stringops2 recorded findings (e2e batch C57, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 98-line
byte-exact; expected.txt == oracle).

## D-STR2-1 -- Join(string, object[]): net48 first-element-null shortcut DIVERGES from the runtime

- **Phenomenon** (live net48 probe): `string.Join(",", new object[]{null,"x"})`
  returns **String.Empty** on net48 (reference source:
  `if (values.Length == 0 || values[0] == null) return String.Empty;`), while
  the runtime's `JoinFromArray` (stringext2.cpp) treats every null element as
  empty and would return `",x"`. Mid-array nulls agree (`{"p",null,"q"}`
  -> `"p,,q"` on both).
- **Disposition**: recorded. The object[] Join probes keep the FIRST element
  non-null (only mid-array null elements are exercised); a first-element-null
  probe would byte-diff.

## D-STR2-2 -- newarr double[] / float[] / bool[] / short[] rejected (audit C4)

- **Phenomenon**: the transpiler's `ArrayElementType` only allocates
  Int32/Byte/Char/Int64/UInt64 + reference-element arrays; `new double[]` fails
  transpile with "newarr of value type System.Double not supported (audit C4)".
  This dropped the planned `Join(string, double[])` probe.
- **Disposition**: recorded. The generic Join value-array shape is covered with
  int[] / long[]; double formatting is still exercised through the boxed double
  in the object[] `{ 1, 2.5, true, "s" }` element.

## D-STR2-3 -- unwired Split/Join overload family

- Split(char) / Split(string) / Split(char[] , int, ...) / Split(string[] , int, ...)
  (net48 4.0+ single-separator forms) and Join(string, ..., int startIndex, int
  count) have no icall -- LOUD-rejected or unresolved. net48 has no
  char-separator Join, so Join(char, ...) is moot.
- **Disposition**: recorded. Only the wired `Split(char[])`,
  `Split(char[]/string[], StringSplitOptions)` and `Join(string, array)`
  shapes are exercised.

## D-STR2-4 -- Compare(a,b,StringComparison) drops the mode; Compare/CompareTo culture vs ordinal

- **Phenomenon**: the 3-arg `Compare` mapping DROPS the StringComparison arg
  (ordinal-only), so OrdinalIgnoreCase / culture modes would diverge; the 2-arg
  `Compare`/instance `CompareTo` are ordinal in the runtime while net48 is
  culture-aware.
- **Disposition**: recorded. Only `StringComparison.Ordinal` is used in 3-arg
  Compare; all 2-arg Compare / CompareTo probes use lowercase ASCII (where the
  zh-CN culture ordering equals ordinal), and null semantics (CompareTo(null)=1
  etc.) which both engines share.

## D-STR2-5 -- IndexOf/LastIndexOf with StringComparison unwired (LOUD)

- **Phenomenon**: `IndexOf(string, StringComparison)` / `LastIndexOf(...)`
  throw NotSupportedException in MethodMap.StringExt3.cs ("no icall in
  il2cpp_string_ext3.h").
- **Disposition**: recorded. Comparison-family coverage uses Equals /
  StartsWith / EndsWith with StringComparison (wired) instead.
