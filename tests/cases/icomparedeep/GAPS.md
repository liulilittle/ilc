# GAPS.md -- icomparedeep recorded findings (e2e batch C53, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 37-line byte-exact).

## D-ICMP-1 -- mscorlib IComparable / IComparer INTERFACE dispatch is NOT wired

- **Phenomenon (probed at build)**: `((IComparable)x).CompareTo(y)` and
  `((IComparer)c).Compare(a, b)` on user classes that implement the mscorlib
  interfaces transpile silently but FAIL the C++ build (undeclared identifier
  `System_IComparable_CompareTo_...` / `System_Collections_IComparer_Compare_...`).
  The BclResolveCore dispatch has no `System.IComparable` / `System.IComparer`
  case, and the interface-dispatch path (il2cpp_interface_dispatch) cannot
  resolve a mscorlib interface descriptor.
- **Disposition**: recorded. The user comparer classes are exercised through
  their CONCRETE types (direct virtual dispatch -- the wired surface). The
  class DECLARATIONS still implement the interfaces (they generate fine; only
  interface-member CALLS fail).

## D-ICMP-2 -- System.Collections.Comparer (Comparer.Default / DefaultInvariant) NOT wired

- **Phenomenon**: `Comparer.Default.Compare("a","b")` fails at C++ build
  (`System_Collections_Comparer_sf_Default` / `..._Compare_...` undeclared).
- **Disposition**: recorded. Default-comparer sorts are exercised through the
  wired `Array.Sort<int32/string>` no-comparer family + custom comparers.

## D-ICMP-3 -- comparer-driven sorts NOT wired

- `Array.Sort(Array, IComparer)` (non-generic, genArgs == null) falls to the
  generated-name path -> undefined symbol.
- `Array.Sort<T>(T[], Comparison<T>)` is LOUD (no delegate-to-function-pointer
  ABI; value-element comparer sorts have no runtime entry) -- documented in
  MethodMap.ArraySort.cs.
- **Disposition**: recorded. The "sorting usage" of the custom comparers is
  verified INDIRECTLY: the wired Array.Sort ascending order is checked with a
  custom comparer over every adjacent sorted pair (`Compare(prev, next) <= 0`).

## D-ICMP-4 -- Array.Sort<string>: culture vs ordinal (net48 vs ILC)

- net48 `Array.Sort(string[])` uses the current-culture comparer; the ILC
  runtime string sort is ORDINAL (documented in sortdeep). On zh-CN the two
  orders diverge for case-different strings.
- **Disposition**: avoided -- all string sort inputs are ASCII lowercase (+
  null, which sorts first on both sides).
