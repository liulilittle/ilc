# ut_dt_calendar — DateTime calendar helper unit tests

Agent: TEST AGENT (exclusive files: `tests/runtime/test_dt_calendar.cpp` + `build/ut_dt_calendar`)

## Scope

Unit tests for `icall_datetime_is_leap_year` / `icall_datetime_days_in_month`
(`src/runtime/src/time.cpp:366-381`), mirroring the `test_time.cpp` harness
style (plain executable, `Expect()` + `ThrowsWithType()`, non-zero exit on any
failure).

## Coverage

| # | Area | Assertions |
|---|------|-----------|
| 1 | IsLeapYear Gregorian rule | 2000 == 1 (÷400), 1900 == 0 (÷100 not ÷400), 2024 == 1 (÷4), 2023 == 0 (not ÷4) |
| 2 | DaysInMonth full table | all 12 months of common year 2023 vs canonical `{31,28,31,30,31,30,31,31,30,31,30,31}`; leap year 2024 = same table with Feb→29 only; explicit Feb anchors 2023→28, 2024→29 |
| 3 | Rule-year Feb agreement | DaysInMonth(2000,2)==29, DaysInMonth(1900,2)==28 |
| 4 | Error paths | month 0 and month 13 throw `System.ArgumentOutOfRangeException` (caught via `ThrowsWithType`) |

## Build

- New dedicated build dir `E:\il2cpp\build\ut_dt_calendar`
- Generator/config mirrored from `build/tests_runtime_time`: MinGW Makefiles,
  g++ `C:/Program Files/ming-w64/bin/g++.exe`, source dir `E:/il2cpp/tests/runtime`
  (GLOB auto-discovers the new `test_*.cpp`; no CMakeLists edit needed)
- Target built: `test_dt_calendar` only (`mingw32-make -j8 test_dt_calendar`)
- Result: `[100%] Built target test_dt_calendar`; recompiled the test TU in
  isolation → **zero warnings/errors from test_dt_calendar.cpp** (the warnings
  visible during the lib build are pre-existing in runtime sources, e.g.
  threadobj.cpp unused-parameter)

## Run

```
E:\il2cpp\build\ut_dt_calendar\test_dt_calendar.exe
all runtime dt_calendar tests passed
EXIT=0
```

## Status

PASS — 12 Expect assertions, 0 failures, exit code 0.

No git operations performed; no files outside the exclusive scope touched.
