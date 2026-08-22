# ut_conv_bool — bool-carrier conversion icall unit tests

**Agent:** TEST (exclusive files: `tests/runtime/test_conv_bool.cpp` + build dir `build/ut_conv_bool`)
**Status:** PASS — all assertions green, exit code 0

## Scope

Unit tests for the System.Convert bool-carrier icalls in `src/runtime/src/convert.cpp`:

| Function | Declared | Contract under test |
|---|---|---|
| `icall_convert_to_int32_bool(int32_t)` | `il2cpp_convert.h:117`, impl `convert.cpp:377` | false(0)→0, true(1)→1, any non-zero→exactly 1 |
| `icall_convert_to_bool_i4(int32_t)` | `il2cpp_convert.h:108`, impl `convert.cpp:365` | inverse direction; roundtrip partner |

## What was built

- **New test:** `tests/runtime/test_conv_bool.cpp` (~150 lines) mirroring the existing
  convert-test harness style (`test_convert.cpp` / `test_conv.cpp`: plain executable,
  minimal `Expect()` harness, non-zero exit on failure).
- **Build dir:** `E:\il2cpp\build\ut_conv_bool` — CMake MinGW Makefiles,
  g++ (`C:/Program Files/ming-w64/bin/g++.exe`), same toolchain as existing
  `tests_runtime_conv` dir. Only target `test_conv_bool` (+ its `il2cpp_runtime`
  lib dependency) was built in this dir.

## Test coverage

1. **to_int32_bool normal path:** `0→0`, `1→1`.
2. **to_int32_bool non-zero normalization:** `2, 42, -1, -42, INT32_MAX, INT32_MIN,
   0x10000 → exactly 1` (asserted `== 1`, not merely truthy — carrier model).
3. **to_bool_i4 contract:** `0→0`; `1, 42, -1, INT32_MAX, INT32_MIN → 1`.
4. **Roundtrips over a 22-value sample set** (boundaries ±1 of INT32_MIN/MAX,
   byte/word edges 255/256/257, 65536/65537, sign flips):
   - `to_int32_bool(to_bool_i4(v)) == to_bool_i4(v)` (bool→int32→bool)
   - `to_bool_i4(to_int32_bool(v)) == to_int32_bool(v)` (int32→bool→int32)
   - idempotence `f(f(v)) == f(v)` for both
   - cross-agreement: both icalls return identical results per input
   - both normalize into exact `{0, 1}` matching `(v != 0 ? 1 : 0)`

## Verification evidence

```
[BASH] cmake -S E:/il2cpp/tests/runtime -B E:/il2cpp/build/ut_conv_bool -G "MinGW Makefiles" ...
       → "Configuring done / Generating done / Build files have been written"
[BASH] cmake --build E:/il2cpp/build/ut_conv_bool --target test_conv_bool -j <n>
       → "[100%] Built target test_conv_bool" (zero compile errors/warnings)
[BASH] & E:\il2cpp\build\ut_conv_bool\test_conv_bool.exe
       → "all runtime conv_bool tests passed", EXIT=0
```

## Notes

- No git operations performed; no files outside the exclusive set touched
  (test file + own build dir + this report).
- The GLOB-based `tests/runtime/CMakeLists.txt` auto-discovered the new
  `test_*.cpp` at configure time — no CMake edits needed.
- Existing partial coverage noted: `test_convert.cpp:295-298` already covers
  `to_bool_i4` basics; `icall_convert_to_int32_bool` had NO prior unit-test
  coverage anywhere in tests/runtime — this file closes that gap and adds the
  bidirectional roundtrip/idempotence/cross-agreement matrix.
