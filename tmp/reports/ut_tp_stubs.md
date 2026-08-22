# ut_tp_stubs -- ThreadPool internal-call stub unit tests

Date: 2026-08-22
Agent scope: `tests/runtime/test_tp_stubs.cpp` (NEW) + build dir `build/ut_tp_stubs` only.

## Subject under test

ThreadPool icall stubs in `src/runtime/src/stubs.cpp:264-268`, declared in
`src/runtime/include/internal/stubs.h:198-202` (namespace `il2cpp::runtime`):

| Symbol | Contract verified |
|---|---|
| `icall_thread_pool_notify_work_item_complete()` | callable, returns normally; single + repeated calls |
| `icall_thread_pool_notify_work_item_progress_native()` | callable, returns normally; single + repeated calls |
| `icall_thread_pool_report_thread_status(int32_t)` | no-crash across int32 domain edges: 0, 1, -1, INT32_MAX, INT32_MIN |
| `icall_thread_pool_is_thread_pool_hosted()` | returns 0 (not hosted); stable across repeated calls |

A crash in any stub kills the process and is observed as a non-zero exit by CI;
the assertion harness additionally prints FAIL lines per violated invariant.

## Build

- Toolchain: MinGW Makefiles + g++ (same as sibling `tests_runtime_*` builds).
- Configure: `cmake -S E:\il2cpp\tests\runtime -B E:\il2cpp\build\ut_tp_stubs -G "MinGW Makefiles"`
- Target-only build (did not rebuild unrelated test targets):
  `cmake --build E:\il2cpp\build\ut_tp_stubs --target test_tp_stubs -- -j8`
- Result: **zero errors, zero warnings** (`-Wall -Wextra` active for the TU).

## Run

```
E:\il2cpp\build\ut_tp_stubs\test_tp_stubs.exe
all threadpool stub tests passed
EXIT=0
```

Re-verified after forced recompile of the TU (warning scan pass): same output,
exit 0.

## Notes / discoveries

- `stubs.h` is documented as "self-contained" but only bridges
  `../il2cpp_api.h`, which does NOT declare `Il2CppObject`/`Il2CppString`
  (they live in `include/internal/il2cpp_object.h` / `il2cpp_string.h`,
  namespace `il2cpp`). The test includes those two internal headers before
  `stubs.h` -- the same include set `stubs.cpp` itself uses. Without them the
  TU fails with `unknown type name 'Il2CppObject'`.
- No overlap with existing `test_threadpool.cpp`: that file exercises the real
  simplified pool (`threadpool.h`: queue/concurrency/min-max contracts); this
  file covers only the BCL-infrastructure icall stubs.
- Scope kept exact per task: the neighboring stubs on the same lines
  (`icall_thread_pool_get_*`, `icall_thread_pool_clear_notify`) are NOT covered
  here -- they belong to whichever agent's charter lists them.

## Verdict

PASS -- link + symbol presence proven by a clean build against
`libil2cpp_runtime.a`; runtime contract (no-crash x3, hosted==0) verified by
execution, exit code 0.
