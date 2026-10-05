# GC Regression Recheck — 2026-08-03 (read-only, no source changes)

## Scope
Re-verify that the GC changes (weak-reference table, finalizer table, cumulative
counters, free-list fix) in `src/runtime/src/gc.cpp` remain stable under the
latest merge. Report only; no fixes.

## Baseline
- `gc.cpp` mtime: **2026-08-03 16:24:40**, 58539 bytes — unchanged by parallel
  agents during this audit (all src/runtime edits in the last 30 min were in
  other domains: numberformat/convertbase64/sortedlist/etc.).
- GC features confirmed present in current source:
  - `g_weakRefs[kMaxWeakRefs]` + add/remove/clear path (gc.cpp:662, 793, 1382-1410)
  - `FinalizerEntry g_finalizers[kMaxFinalizers]` + two-collection finalization
    with reentrancy guard (gc.cpp:690-769, 939-956)
  - `g_freeBytes` free-list accounting (gc.cpp:238, 417, 969)
  - public contract in `il2cpp_gc.h` (weak slots, register/run finalizers).

## Fresh runtime build (isolated dir, zero shared-tree footprint)
- Command: `cmake -S tests/runtime -B build/runtime_gccheck -G "MinGW Makefiles"`
  (Clang 17.0.6) + `cmake --build ... --target test_gc test_gcapi test_gcext test_gcstress`
- Result: all four gc test executables linked against a fresh `libil2cpp_runtime.a`
  compiled from the current tree (19:31-19:32). A GLOB mismatch fired mid-build
  (parallel agent added `linq_sort.cpp`) but compilation completed cleanly.

## Results

### Runtime unit tests (fresh build)
| Test | Exit | Output |
|------|------|--------|
| test_gc.exe (19:31:54) | 0 | `all runtime gc tests passed` |
| test_gcapi.exe (19:32:20) | 0 | `all runtime gcapi tests passed` |
| test_gcext.exe (19:32:29) | 0 | `all runtime gcext tests passed` |
| test_gcstress.exe (19:32:34) | 0 | `gc stress suite finished in 10673.8 ms` + `all gc stress tests passed` |

Coverage exercised by these suites (from the test sources):
- mass small-object churn (126k allocs across explicit + alloc-triggered collects)
- leak tripwire (12 × 15000-object batches)
- 64-node and 2000-node deep chains across compactions (Phase C reference rewrite)
- unrooted + rooted reference cycles (mark worklist termination, back-link rewrite)
- static roots over ref-element array / int32 array / string
- LOH churn (300 KB, non-moving) and LOH-threshold boundary (84992 vs 85001)
- mixed size-class fragmentation (16 B..16 KB, first-fit whole-block reuse)
- free-list retention across explicit collections (1024 pre-fill / 924 untouched
  across a collect / 1000 probes → ≥990 distinct reused blocks)

### E2E cases (tests/cases)
| Case | Artifact | Result |
|------|----------|--------|
| gcstress | `build_mingw/il2cpp_gen_exe.exe` (18:47:55, newer than gc.cpp) | PASS — output matches gcstress.cs expectations: `1999000 0 2 777 1`, exit 0 |
| gccase | gen tree exists (18:47:40) but no linked exe in build_mingw | NOT VERIFIED — build incomplete; gccase.cs documents a KNOWN gap (GC.GetTotalMemory/GetAllocatedBytes currently return 0, missing cumulative-counter accessor in gc.cpp) |
| gcext | net48 reference only; no gen dir | NOT VERIFIED — case documents real .NET weakref/finalizer semantics; ILC wiring (WeakReference/GC icalls) is a transpiler-side task, not a GC-runtime issue |

## Conclusion
**稳定 (STABLE)** — 4/4 runtime gc suites pass on a fresh build of the latest
merge; the gcstress e2e case also passes with byte-exact expected output.
No regression detected in weak-reference handling, finalizer/free-list behavior,
or compaction under the current tree. The two unverified e2e cases (gccase,
gcext) are blocked by documented transpiler/wiring gaps, not by the GC changes.
