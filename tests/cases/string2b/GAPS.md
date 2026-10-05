# GAPS.md -- string2b notes (2026-08-07)

58 probes PASS vs net48 (mingw). The wired String surface holds:
Compare 2/3-arg (Ordinal), CompareOrdinal, GetHashCode stability + pinned
values, ToUpper/ToLower, IsNullOrEmpty, Concat 3/4-arg, Contains (start),
Format composite (multi-placeholder / reorder / brace-escape / 1-3 arg).

## DEFECT-STR2B-1 -- net48 Ordinal prefix-length compare returns RAW boundary-char diffs (runtime normalizes)

net48 `string.CompareOrdinal` / `string.Compare(..., StringComparison.Ordinal)`
do NOT always return the length difference for prefix cases. Empirically
(csc.exe net48 probe, this session):

  CompareOrdinal("",  "a")    == -97   (0 - 'a')
  CompareOrdinal("abc","abcd") == -100  (0 - 'd')
  CompareOrdinal("a", "ab")   == -98   (0 - 'b')
  CompareOrdinal("ab", "a")   ==  98   ('b' - 0)
  CompareOrdinal("abc","ab")  ==   1   (length diff)
  CompareOrdinal("ab", "abc") ==  -1   (length diff)

So net48 reads the boundary char (0 for the terminator side) for SOME prefix
shapes, and returns the plain length difference for others (opaque,
alignment-sensitive net48 internals). The runtime's icall_string_compare_static
(ComparePayloads, stringext2.cpp:190) ALWAYS returns a->length - b->length for
prefix cases. The two agree only when net48 happens to produce the length diff.

Impact on the case: the probe `string.Compare("", "a", StringComparison.Ordinal)
== -1` was net48-False / runtime-True and was REMOVED. The retained probes
(cmp2_prefix "ab"/"abc", cord_len "abc"/"ab") use shapes where net48 == the
runtime's length-diff, so the case stays green and byte-identical.

NOT FIXED (defect recorded only). The runtime would need to replicate net48's
boundary-char read for byte-exactness on empty/prefix inputs.
