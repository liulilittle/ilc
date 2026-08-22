# ut_interlocked_fp -- P6 Interlocked float/double atomics test report

Date: 2026-08-22
Agent: TEST AGENT (exclusive files: `tests/runtime/test_interlocked_fp.cpp`, build dir `build/ut_interlocked_fp`)

## Result

| Metric | Value |
|---|---|
| Assertions (Expect calls) | 56 |
| PASSED | 56 |
| FAILED | 0 |
| Process exit code | 0 |

## Failures (verbatim)

None. Full stdout of the run:

```
all runtime interlocked fp tests passed (P6 float/double atomics)
EXIT_CODE=0
```

## What was tested

Target functions ([READ:src/runtime/include/internal/il2cpp_interlocked.h:97-102], implemented in [src/runtime/src/interlocked.cpp:181-239] via bit-reinterpretation over the int32/int64 atomics):

- `il2cpp_interlocked_exchange_float` / `il2cpp_interlocked_exchange_double`
- `il2cpp_interlocked_compare_exchange_float` / `il2cpp_interlocked_compare_exchange_double`
- `il2cpp_interlocked_add_float` / `il2cpp_interlocked_add_double`

Coverage by section (`tests/runtime/test_interlocked_fp.cpp`, style copied from `test_mathf.cpp`: plain exe + `Expect(bool, const char*)` harness + non-zero exit on failure):

1. **Exchange returns OLD value** (4): float/double return-old + store-new on ordinary values.
2. **Exchange bit-exactness on special patterns** (8): NaN payload preserved on the OLD side; -0.0/+inf/-inf/sign-bit-NaN land bit-exactly on the NEW side.
3. **CAS hit, bit-exact comparands** (16): hit on canonical NaN bits, on -0.0 exact encoding, on +/-inf encodings; stored value side also bit-exact (NaN payload lands verbatim). Float and double.
4. **CAS miss, ANY bit difference rejects the store** (16): `-0.0` vs `+0.0` both directions (numerically equal, bitwise distinct -> must NOT store), NaN payload mismatch, NaN sign mismatch, +inf vs FLT_MAX/DBL_MAX. Location unchanged and OLD value returned bit-exact in every miss.
5. **Add single-thread CAS-loop convergence** (8): returns-NEW contract; 1000x0.5f -> exactly 500.0f; 1000x0.1f and 2000x0.1 bit-identical to sequential summation (same order/rounding oracle); FLT_MAX+FLT_MAX and DBL_MAX+DBL_MAX overflow to exactly +inf.
6. **Add under contention** (2): 8 threads x 10000 adds of 1.0f -> exactly 80000.0f; 8 threads x 5000 adds of 2.0 -> exactly 80000.0 (all partial sums exact integers < 2^24, so any lost update from a broken CAS loop would show as a shortfall). Threads joined before assertion.

## Build & run evidence

```
cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_interlocked_fp -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
  -> Configuring done / Generating done (Clang 17.0.6 driver, MinGW-w64 target, Threads found)
cmake --build E:\il2cpp\build\ut_interlocked_fp --target test_interlocked_fp
  -> [100%] Built target il2cpp_runtime
  -> [100%] Building CXX object CMakeFiles/test_interlocked_fp.dir/test_interlocked_fp.cpp.obj
  -> [100%] Linking CXX executable test_interlocked_fp.exe   (zero warnings from the new test file)
E:\il2cpp\build\ut_interlocked_fp\test_interlocked_fp.exe
  -> all runtime interlocked fp tests passed (P6 float/double atomics)   EXIT_CODE=0
```

Notes:

- No CMakeLists.txt edit was required: `tests/runtime/CMakeLists.txt` GLOB-discovers `test_*.cpp` (`file(GLOB TEST_SOURCES CONFIGURE_DEPENDS test_*.cpp)`), so the fresh configure picked up the new file automatically. Target name = `test_interlocked_fp`.
- No git operations; no shared sources touched; no other build dirs touched.
