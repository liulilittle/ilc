# ut_spinwait -- unit test report: `icall_threadobj_spin_wait(int32_t)`

- **Test file (NEW):** `E:\il2cpp\tests\runtime\test_spinwait.cpp`
- **Build dir:** `E:\il2cpp\build\ut_spinwait` (MinGW Makefiles, g++ `C:/Program Files/ming-w64/bin/g++.exe`, cmake 3.22.1)
- **Target under test:** `il2cpp::runtime::icall_threadobj_spin_wait` — declared `src/runtime/include/internal/stubs.h:203`, defined `src/runtime/src/stubs.cpp:201-207` (volatile pause loop, void return, net48 contract: never throws, no side effects)
- **Command:** `cmake -G "MinGW Makefiles" E:\il2cpp\tests\runtime` then `cmake --build . --target test_spinwait` then `.\test_spinwait.exe`

## Requirements vs results

| # | Requirement | Result | Evidence |
|---|-------------|--------|----------|
| 1 | N=1000 completes quickly (< 1 s wall) | PASS | 0.0005 ms / 0.0006 ms across 2 runs |
| 2 | Zero iterations returns immediately | PASS | ~0.0000 ms (< 100 ms bound) |
| 3 | Negative N terminates immediately (loop condition false on entry) | PASS | `-1`: ~0.0000 ms; `INT32_MIN`: ~0.0000 ms |
| 4 | Elapsed measured via std::chrono in test | DONE | `std::chrono::steady_clock`, printed per case |

## Raw output (run 2)

```
spin_wait(1000)     :    0.0006 ms
spin_wait(0)        :    0.0000 ms
spin_wait(-1)       :    0.0000 ms
spin_wait(INT32_MIN):    0.0000 ms
ALL PASS
EXIT=0
```

Run 1 identical (`ALL PASS`, exit 0; 1000 iters = 0.0005 ms).

## Assertions in test

1. `spin_wait(1000)` wall time < 1000 ms (regression guard against a sleeping/yielding impl).
2. `spin_wait(0)` wall time < 100 ms (loop never entered).
3. `spin_wait(-1)` wall time < 100 ms.
4. `spin_wait(INT32_MIN)` wall time < 100 ms (guards unsigned-promotion/sign-wrap turning negative N into a huge busy loop).

Exit code: 0 on pass, non-zero on any FAIL (CI-visible via exit code, repo convention).

## Notes

- Build issue found + fixed: `stubs.h` alone does not compile ("unknown type name 'Il2CppObject'") despite its self-contained claim — the test includes `il2cpp_object.h` + `il2cpp_string.h` first (same set stubs.cpp itself uses, stubs.cpp:5-7).
- Only target `test_spinwait` was built in this dir (runtime static lib + one TU); no other test targets touched. No git operations performed.
