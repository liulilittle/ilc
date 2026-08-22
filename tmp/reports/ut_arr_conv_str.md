# ut_arr_conv_str — icall_array_convert_all_str_i4 unit tests

- **Date**: 2026-08-22
- **Agent file (exclusive)**: `tests/runtime/test_arr_conv_str.cpp` (NEW)
- **Build dir (exclusive)**: `build/ut_arr_conv_str` (MinGW Makefiles, g++ ming-w64)
- **Function under test**: `il2cpp::runtime::icall_array_convert_all_str_i4(Il2CppArray*, Il2CppObject*)`
  (`src/runtime/src/arrayext2.cpp:838`, declared `src/runtime/include/internal/il2cpp_array_ext2.h:200`)
- **Result: PASS** — all checks passed, exit code 0.

## Build & run evidence

```
cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_arr_conv_str -G "MinGW Makefiles" \
      -DCMAKE_CXX_COMPILER="C:/Program Files/ming-w64/bin/g++.exe"
  -> Configuring done / Generating done / Build files written
cmake --build E:\il2cpp\build\ut_arr_conv_str --target test_arr_conv_str
  -> [100%] Built target test_arr_conv_str   (0 errors; only pre-existing
     runtime-lib warnings in unrelated sources; test TU compiled clean)
E:\il2cpp\build\ut_arr_conv_str\test_arr_conv_str.exe
  -> test_arr_conv_str: all checks passed   EXIT=0
```

No CMakeLists edit was required: `tests/runtime/CMakeLists.txt` auto-discovers
`test_*.cpp` via `file(GLOB ... CONFIGURE_DEPENDS)` (line 31).

## Delegate construction pattern (mirrors test_arrayext2.cpp)

Single-cast STATIC-method delegate ABI (`arrayext2.cpp` `ConvertAllStrI4Fn`,
line 90): the converter leaf is `int32_t (*)(Il2CppString*)`; wrapped via
`il2cpp_delegate_new(fn)` (ldftn + newobj shape). Instance-bound leaves use
`int32_t (*)(Il2CppObject* /*this*/, Il2CppString*)` wrapped via
`il2cpp_delegate_new(fn, target)`. Source arrays are reference arrays built
with `il2cpp_array_new(nullptr, 8, true, n)` + `il2cpp_string_new_from_cs`.

## Coverage matrix

| # | Case | Expected | Status |
|---|------|----------|--------|
| 1 | Normal conversion `{"1","22"}` via static Parse-like C leaf | result `{1, 22}`; NEW array (`!= src`, `isRefElem==0`, `elemSize==4`, len 2); source untouched | PASS |
| 2 | Parse semantics `{"-7","300","0"}` | `{−7, 300, 0}` (sign, multi-digit, zero) | PASS |
| 3 | Null string element in source slot | null rides the managed-ref slot into the callback verbatim → mapped to 0, no crash/UB | PASS |
| 4 | Empty string[] | non-null zero-length int32[] result | PASS |
| 5 | INSTANCE-BOUND converter (bound radix payload read via `this`) | `{"ff","10"}` with base-16 bound leaf → `{255, 16}` (per-binding dispatch, `ConvertAllStrI4ThisFn` arm) | PASS |
| 6 | GC probe — `il2cpp_gc_collect()` inside every converter call | rooted source (ref array) + delegate + result survive mid-loop compaction; result correct; source content intact | PASS |
| 7a | null array | ArgumentNullException (net48 order: array checked before converter) | PASS |
| 7b | null converter | ArgumentNullException | PASS |
| 7c | non-array object as source | ArgumentException (`RequireFindIndexArray` '[' marker check) | PASS |
| 7d | int32 (non-ref) array as source | NotSupportedException (`RequireRefArrayExt2`) | PASS |
| 7e | multicast container as converter | InvalidOperationException (`RequireArrayCallback` — `first` field aliases methodPtr) | PASS |
| 7f | non-delegate object as converter | ArgumentException | PASS |

## Notes / findings

- The historical "reference arrays unsafe across collections" blocker noted in
  `test_arrayext.cpp:268` is FIXED in `array.cpp`: `ArrayAllocSize` now uses
  `offsetof(Il2CppArray, data)` (array.cpp:149–161), so the GC-probe case (#6)
  runs a real compaction over a string[] source safely.
- Exception assertions follow the sibling harness convention: chain-head match
  on `DotNetException::type_name()` (`System.Xxx` prefix before `,`).
- Scope kept exact: one new test file only; no git operations; no shared-file
  edits (GLOB made a CMakeLists entry unnecessary).
