# ut_gc_getters — GC getter unit tests report

**Agent:** TEST (exclusive file: `tests/runtime/test_gc_getters.cpp`, build dir: `build/ut_gc_getters`)
**Date:** 2026-08-22
**Result: PASS** — all assertions green, exit code 0.

## Scope

Unit tests for the GC statistics getters declared in
`src/runtime/include/internal/il2cpp_gc.h`:

| Function | Header line | Requirement |
|---|---|---|
| `il2cpp::il2cpp_gc_get_max_generation()` | il2cpp_gc.h:285 | assert >= 0 |
| `il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread()` | il2cpp_gc.h:299 | assert > 0 after an allocation |
| GC.GetTotalMemory path: `il2cpp::runtime::icall_gc_get_total_memory()` (delegates to `il2cpp_gc_get_total_memory()`) | il2cpp_gc_api.h:68 / il2cpp_gc.h:139 | exposed — tested |

## Test coverage

1. **Allocation precondition** — fixture object (16 B, no ref fields) allocates non-null with correct klass header.
2. **max_generation** — `>= 0`; equals `kGcGeneration2` (oldest small-object generation, the GC.MaxGeneration contract per header comment); stable across repeated queries.
3. **allocated_bytes_for_current_thread** — positive after an allocation; equals the cumulative counter `il2cpp_gc_get_allocated_bytes()` (single-threaded collector contract); strictly grows after a fresh allocation.
4. **GetTotalMemory path** — icall value identical to `il2cpp_gc_get_total_memory()`; live-heap total positive after allocation; a rooted survivor keeps at least its own block size (`>= instanceSize`) in the live-heap total across an explicit collect.

## Build & run evidence

```
cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_gc_getters -G "MinGW Makefiles" -DCMAKE_CXX_COMPILER=C:/Program Files/ming-w64/bin/g++.exe
  → Configuring done / Generating done / Build files written
cmake --build E:\il2cpp\build\ut_gc_getters --target test_gc_getters -- -j8
  → [100%] Built target test_gc_getters   (zero errors, zero warnings)
E:\il2cpp\build\ut_gc_getters\test_gc_getters.exe
  → all runtime gc getter tests passed
  → EXIT=0
```

## Notes

- No CMakeLists edit needed: `tests/runtime/CMakeLists.txt` GLOBs `test_*.cpp`
  (`CONFIGURE_DEPENDS`); only a cmake re-configure was required.
- One compile fix during development: `Il2CppClass` is forward-declared in
  `il2cpp_object.h`; added `#include "il2cpp_metadata.h"` for the full type
  (same include set as `test_gc.cpp`).
- Style mirrors `tests/runtime/test_gc.cpp`: plain executable, minimal
  `Expect()` harness, non-zero exit on failure.
- No git operations performed; no files outside the exclusive scope touched.
