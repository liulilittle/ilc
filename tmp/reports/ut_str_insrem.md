# ut_str_insrem — Unit test report: `icall_string_insert` / `icall_string_remove`

- **Test file (new, exclusive):** `E:\il2cpp\tests\runtime\test_str_insrem.cpp`
- **Build dir (exclusive):** `E:\il2cpp\build\ut_str_insrem`
- **Target under test:** `il2cpp::runtime::icall_string_insert` / `icall_string_remove`
  (`src/runtime/include/internal/il2cpp_string_ext.h:121-122`, impl `src/runtime/src/stringext.cpp:983-1017`)
- **Date:** 2026-08-22

## Build

```
cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_str_insrem -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build E:\il2cpp\build\ut_str_insrem --target test_str_insrem -- -j8
```

- Configure: OK (CMakeLists GLOB auto-discovers `test_*.cpp` — no CMakeLists edit needed).
- Compile: OK, zero warnings from `test_str_insrem.cpp` (only pre-existing `-Wunused-parameter` warnings in `threadobj.cpp`).
- Link: OK (`test_str_insrem.exe`).

## Run

```
E:\il2cpp\build\ut_str_insrem\test_str_insrem.exe
all runtime str_insrem tests passed   EXIT=0
```

**35/35 assertions passed.**

## Coverage matrix

| # | Requirement | Assertions | Result |
|---|---|---|---|
| 1 | Insert at 0 | `Insert("World",0,"Hello ")=="Hello World"`; boundary sweep `{0,1,2}` on `"ab"` | PASS |
| 2 | Insert middle | `Insert("abcd",2,"--")=="ab--cd"` | PASS |
| 3 | Insert end (`index==Length`) | `Insert("abc",3,"def")=="abcdef"` | PASS |
| 4 | Empty-value insert | content-equal `"abc"`, NEW instance, source unmodified; `Insert("",0,"")==""` | PASS |
| 5 | Insert into empty receiver | `Insert("",0,"xy")=="xy"` | PASS |
| 6 | Remove head | `Remove("abc",0,1)=="bc"` | PASS |
| 7 | Remove middle | `Remove("abcd",1,2)=="ad"` | PASS |
| 8 | Remove tail | `Remove("abc",2,1)=="ab"`; `Remove("abcd",2,2)=="ab"` | PASS |
| 9 | Remove full | `Remove("abc",0,3)==""` | PASS |
| 10 | Remove count=0 | content-equal copy, NEW instance, source unmodified; `Remove("",0,0)==""` | PASS |
| 11 | New-instance + source-unmodified contract | both icalls never return the receiver; receiver bytes unchanged | PASS |
| 12 | Insert index out-of-range | index −1, Length+1, and 1-on-empty → `System.ArgumentOutOfRangeException` | PASS |
| 13 | Remove startIndex out-of-range | −1 and Length+1 → `System.ArgumentOutOfRangeException` | PASS |
| 14 | Remove count overflow | count −1; count > remaining span (`Remove("abc",1,3)`); count Length+1 → `System.ArgumentOutOfRangeException` | PASS |
| 15 | Null arguments | insert null self / null value, remove null self → `System.ArgumentNullException` | PASS |
| 16 | GC sanity | rooted insert/remove results survive `il2cpp_gc_collect()` with content intact | PASS |

## Method notes

- Harness mirrors sibling `test_stringext6.cpp`: plain executable, `Expect()` counter,
  non-zero exit on first-failure signal; exceptions asserted via
  `ThrowsWithType` (`DotNetException::type_name()` prefix match) inside try/catch.
- Exception types verified against the impl's throw helpers:
  `il2cpp_throw_argument_null_exception` → `System.ArgumentNullException`
  (`exceptionext.cpp:124`), `il2cpp_throw_argument_out_of_range_exception` →
  `System.ArgumentOutOfRangeException` (`exceptionext.cpp:136`).

## Scope compliance

- Only new file: `tests/runtime/test_str_insrem.cpp`. No CMakeLists change (GLOB registration).
- No git operations. Built/ran only in `E:\il2cpp\build\ut_str_insrem`.
