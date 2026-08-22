# ut_timespan_min — Unit test report: `icall_timespan_from_minutes(double)`

- **Date**: 2026-08-22
- **Function under test**: `il2cpp::runtime::icall_timespan_from_minutes` — declared `src/runtime/include/internal/il2cpp_time.h:150`, implemented `src/runtime/src/time.cpp:451-460`
- **Test file**: `tests/runtime/test_timespan_min.cpp` (NEW, exclusive to this task)
- **Build dir**: `build/ut_timespan_min` (exclusive; MinGW Makefiles + g++, mirroring sibling per-test dirs)
- **Result**: **PASS — 43 assertions, 0 failures** (`all runtime timespan_min tests passed`, exit code 0)

## Implementation contract (pinned from source)

```
ticks_d = minutes * 600000000.0                    // TicksPerMinute
if (!(ticks_d >= min_bound) || ticks_d > max_bound) throw ArgumentOutOfRangeException
return (int64_t)(ticks_d >= 0 ? ticks_d + 0.5 : ticks_d - 0.5)   // half away from zero
```

- Bounds: `[±(INT64_MIN|MAX / TicksPerSecond) * TicksPerSecond] = ±9223372036850000000`
  (`kMinTotalSeconds/kMaxTotalSeconds`, time.cpp:62-63). As doubles the bounds round to
  ±9223372036849999872.0.
- Rounding is HALF AWAY FROM ZERO on the tick grid.
- An exact .5-tick tie is **not constructible** through a binary-double input:
  `(k+0.5)/600000000` always carries a factor 3·5^8 in the denominator → never dyadic.
  Rounding probes therefore sit deterministically on one side of any tie.

## Coverage matrix

| # | Area | Probes | Result |
|---|------|--------|--------|
| 1 | Exactness via ticks math | FromMinutes(90) == 54000000000 ticks; **TotalHours == 1.5 bit-exact** (54e9/36e9 exact IEEE quotient); TotalMinutes == 90; TotalSeconds == 5400; TotalMilliseconds == 5400000; TotalDays == 0.0625; agreement with `icall_timespan_from_ticks` | PASS |
| 2 | Whole/half-minute grid | 0→0, 1→6e8, 60→3.6e10, 1440→8.64e11, 0.5→3e8, 1.5→9e8, 90.5→5.43e10, 2.5→1.5e9, 1.25→7.5e8 (x.5-minute inputs land on integral ticks) | PASS |
| 3 | Sub-tick rounding (half away from zero) | 1e-9 min (0.6 t)→1; 2e-9 (1.2)→1; 3e-9 (1.8)→2; 4e-10 (0.24)→0; −1e-9→−1; −3e-9→−2; −4e-10→0; −0.0→0; 1/60 min→10000000 (1 s); 1/600 min→1000000 (0.1 s); 1/60000 min→10000 (1 ms) | PASS |
| 4 | Negative values | −90→−54000000000; TotalHours == −1.5 bit-exact; mirror symmetry vs +90; −1440→−TicksPerDay; decomposition of −90 min = 0d −1h −30m 0s (truncation toward zero) | PASS |
| 5 | Range boundary | ±15372286728 min → ±9223372036800000000 ticks accepted (largest minute-grid double inside bound); ±15372286729 min → ArgumentOutOfRangeException | PASS |
| 6 | Overflow/error paths | NaN, +inf, −inf, ±1e12, 1e300, DBL_MAX → all ArgumentOutOfRangeException | PASS |

## Findings

1. **FromMinutes(90).TotalHours == 1.5 holds bit-exactly** — the ticks path
   (54,000,000,000 / 36,000,000,000) yields an exactly representable quotient; asserted with
   `==`, no epsilon.
2. **"Overflow" path is reachable only as ArgumentOutOfRangeException.** Validation precedes
   the int64 cast, so there is no UB/raw-int64-overflow path; `OverflowException` is not part
   of this icall's contract (it belongs to Add/Subtract/Negate). NaN fails the `>= min` guard,
   so it also maps to ArgumentOutOfRange.
3. Negative TimeSpan range is symmetric to the positive one here (±9223372036850000000 ticks),
   unlike DateTime's [0, MaxValue].
4. Development-time correction (test-side, not product): the initial probe assumed
   `1/600 min == 1 ms`; actually 1/600 min = 0.1 s = 1,000,000 ticks. Fixed by using
   1/60000 min for the 1 ms probe and adding the 0.1 s case. Product code needed no change.

## Build & run evidence

```
cmake -G "MinGW Makefiles" -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_timespan_min
  -> Configuring done / Generating done
cmake --build E:\il2cpp\build\ut_timespan_min --target test_timespan_min
  -> [100%] Built target test_timespan_min   (zero errors; warnings pre-existing in runtime sources)
E:\il2cpp\build\ut_timespan_min\test_timespan_min.exe
  -> all runtime timespan_min tests passed   EXIT=0
```

Scope respected: only `tests/runtime/test_timespan_min.cpp` created; build isolated in
`build/ut_timespan_min`; no git operations; no other targets built or run.
