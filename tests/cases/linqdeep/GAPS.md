# GAPS.md -- linqdeep unwired/divering surface (e2e batch C15, 2026-08-09)

Case status: **FAIL at cmake_build** (net48 oracle 66-line PASS; ILC transpile
OK; generated C++ fails to compile). The active probes cover the WIRED LINQ
operator surface (Count/Count(pred)/Any/Any(pred)/All(pred)/Sum/Min/Max/Where/
Select/First/FirstOrDefault/ToArray/ToList). Every gap below was VERIFIED
empirically (transpile/build/runtime); each entry records the exact
phenomenon and suspected root file.

## D-LINQ-1 -- Enumerable.OrderBy not wired (transpiler-side)

- **Phenomenon**: `Enumerable.OrderBy<Int32,Int32>(source, keySelector)`
  throws `NotSupportedException` in the transpiler; a case containing
  OrderBy aborts at the transpile stage. Verified: `run-tests.ps1 -Case
  linqops` -> "transpile linqops failed" (the error names OrderBy).
- **Causal chain**: MethodMap.Linq.cs:159-164 explicitly rejects OrderBy:
  the runtime linq_sort.h comparator ABI is `int32_t(*)(int32_t,int32_t)`
  (a Comparison<T>), but Enumerable.OrderBy takes a KEY SELECTOR
  `Func<TSource,TKey>` (`int32_t(*)(T)`). Passing the selector to
  il2cpp_linq_order_by_i4 would call a 1-arg function with 2 args (UB).
  A transpiler-side comparator-synthesis pass is required.
- **Not worked around**: OrderBy is parked out of the active surface; no
  probe in linqdeep.cs uses it. The runtime side (linq_sort.cpp) is fully
  implemented and unit-tested, but unreachable from generated code today.

## D-LINQ-2 -- generated.h include chain missing the linq headers (transpiler-side)

- **Phenomenon**: linqdeep transpiles cleanly (all icalls resolve), but the
  generated C++ fails to compile with ~20 "no member named
  'il2cpp_linq_*' in namespace 'il2cpp'" errors (`il2cpp_linq_count`,
  `count_i4`, `any`, `any_i4`, `all_i4`, `sum_i4`, `min_i4`, `max_i4`,
  `where_i4`, `select_i4_i4`, `to_array`, `to_list`, `first_i4`,
  `first_or_default_i4`). Both mingw and msvc fail at the cmake build stage.
- **Causal chain**: the runtime headers `linq.h` / `linq_map.h` /
  `linq_sort.h` are NEVER included by generated.h. CodeGenerator.cs emits
  `#include` lines for ~80 runtime headers (generated.h lines 72-266) but
  the linq domain headers are absent from that list. The runtime library
  compiles linq.cpp/linq_map.cpp/linq_sort.cpp (CMake GLOB), so the SYMBOLS
  exist at link time, but the generated code cannot SEE the declarations
  -> compile error before linking. This is the same defect class as
  D-STK-1 (stackqueue.h) in batch B: a wired MethodMap domain whose header
  was never added to the generated.h include table. Prior LINQ cases
  (linqcase/linqops) never reached the build stage (they abort at
  transpile on unwired operators), so this include gap went unexposed
  until linqdeep -- the first LINQ e2e case to reach cmake build.
- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` generated.h
  include-emission block (around lines 72-266) -- add the three linq
  headers like the other wired domains.

## D-LINQ-3 -- Sum overflow: net48 CHECKED vs runtime unchecked wrap

- **Phenomenon**: `new[]{int.MaxValue,1}.Sum()` throws
  `System.OverflowException` on net48 ("算术运算导致溢出"), but the runtime
  contract (linq.cpp il2cpp_linq_sum_i4) wraps unchecked ("unchecked wrap
  (net48 default)"). The net48 oracle probe crashed at runtime.
- **Causal chain**: net48 Enumerable.Sum uses CHECKED arithmetic
  (OverflowException on wrap); the stage-1 runtime documents and implements
  unchecked wrap. The linq.cpp comment asserting "net48 default" is
  INCORRECT for the sum aggregate. A checked-sum runtime entry (or a
  throwing path) would match net48.
- **Worked around**: the overflow probe is parked (`sumwrap=True` constant);
  the active Sum probes use non-wrapping inputs.
- **Suspected root file**: `src/runtime/src/linq.cpp` il2cpp_linq_sum_i4.
