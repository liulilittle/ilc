# GAPS.md -- stringsearch recorded findings (e2e batch C79, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 113-line
byte-exact). Active probes cover the STRING SEARCH depth: raw-index
IndexOf(char/string) 1/2/3-arg across the window lattice, first-vs-last
semantics, the OVERLAP lattice (search does not consume the match --
"aaaa".IndexOf("aa")==0, IndexOf("aa",1)==1), multi-occurrence enumeration
through repeated IndexOf(value,start), the LastIndexOf char/string window
rules (char-form start==Length THROWS vs string-form LEGAL), the char-SET
family IndexOfAny/LastIndexOfAny (3-arg -- the only wired overload arity),
empty-value quirks, and the exception matrix. Gaps below are RECORDED, NOT
FIXED (this batch creates test cases only).

## D-SS-1 -- IndexOfAny / LastIndexOfAny are wired ONLY in the 3-arg form

- **Phenomenon**: MethodMap.String.cs maps IndexOfAny/LastIndexOfAny ONLY
  for ps.Length == 3 (`icall_string_index_of_any_range` /
  `icall_string_last_index_of_any_range`); the C# 1-arg `IndexOfAny(char[])`
  and 2-arg `IndexOfAny(char[], startIndex)` overloads throw
  NotSupportedException at transpile. The case therefore calls the 3-arg
  forms exclusively (anyOf, startIndex, count).
- **Confirmed**: probes use only the 3-arg arity; the 1/2-arg overloads are
  not reachable. No probe removed.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.String.cs`
  (IndexOfAny/LastIndexOfAny cases).

## D-SS-2 -- an empty-needle CountAll loop can overflow startIndex past Length

- **Phenomenon**: `CountAll("abc", "")` (IndexOf("", p) advancing p = i+1)
  walks p = 0,1,2,3,4 -- IndexOf("", 4) THROWS ArgumentOutOfRangeException
  (startIndex > Length) on net48, so the unbounded empty-needle enumeration
  cannot be expressed as a count. The non-empty-needle loops (mo1..mo4, mo6)
  terminate cleanly (a miss returns -1); the empty-needle probe was rewritten
  to the single-position form (mo5 = IndexOf("", 3) == 3).
- **Confirmed**: probe adapted (empty-needle count removed); all 113 lines
  pass byte-exact. The non-empty overlap enumeration (mo2 = 3 occurrences
  of "aba" in "abababa", mo3 = 3 of "aa" in "aaaa") proves the scan resumes
  correctly at p = i + 1.
- **Suspected root file**: none (net48 contract, not a runtime defect).
