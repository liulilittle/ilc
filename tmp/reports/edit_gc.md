# MethodMap.GC.cs — CppName namespace audit report

Date: 2026-08-22
Scope: `E:\il2cpp\src\il2cppc\Emit\MethodMap.GC.cs` (162 unique CppNames audited)
Backup: `E:\il2cpp\tmp\backups\MethodMap.GC.cs.20260822-040250.bak`

## Method

Every quoted CppName in the file was extracted and resolved against its declaring
header in `E:\il2cpp\src\runtime` (269 .h/.cpp files scanned; per-line namespace-block
tracking). Ground truth per header:

| Header | Namespace | Evidence |
|---|---|---|
| `include/internal/il2cpp_gc.h` | `il2cpp` | opens line 38, closes line 376 |
| `include/internal/il2cpp_gc_api.h` | `il2cpp::runtime` | opens line 56, closes line 120 |
| `include/internal/il2cpp_gc_ext.h` | `il2cpp::runtime` (icall block) | opens line 60, closes line 108 |
| `include/internal/stubs.h` | `il2cpp::runtime` | opens lines 10–11 |

## Corrections applied (5)

### 1. GetTotalAllocatedBytes — FABRICATED SYMBOL (line 40)
- Before: `il2cpp::runtime::icall_gc_get_total_allocated_bytes_approximate`
- After:  `il2cpp::il2cpp_gc_get_total_allocated_bytes_approximate`
- The old name exists NOWHERE in the runtime (repo-wide grep: only this .cs file).
  The real symbol is the collector accessor declared at `il2cpp_gc.h:304`,
  defined `gc.cpp:1969`, namespace `il2cpp`. Same direct-to-collector pattern as
  the already-correct get_MaxGeneration / GetAllocatedBytesForCurrentThread rows.

### 2. GetSegmentSize — wrong prefix (line 67)
- Before: `il2cpp::runtime::il2cpp_gc_get_segment_size`
- After:  `il2cpp::il2cpp_gc_get_segment_size` — declared `il2cpp_gc.h:271` (ns `il2cpp`).

### 3. GetLastGCPercentTimeInGC — wrong prefix (line 68)
- Before: `il2cpp::runtime::il2cpp_gc_get_last_gc_percent_time_in_gc`
- After:  `il2cpp::il2cpp_gc_get_last_gc_percent_time_in_gc` — declared `il2cpp_gc.h:256`.

### 4. GetGenerationSize — wrong prefix (line 69)
- Before: `il2cpp::runtime::il2cpp_gc_get_generation_size`
- After:  `il2cpp::il2cpp_gc_get_generation_size` — declared `il2cpp_gc.h:280`.

### 5. GetFragmentedBytes — wrong prefix (line 78)
- Before: `il2cpp::runtime::il2cpp_gc_get_fragmented_bytes`
- After:  `il2cpp::il2cpp_gc_get_fragmented_bytes` — declared `il2cpp_gc.h:275`.

Root cause of 2–5: these four are collector-layer functions in `il2cpp_gc.h`
(namespace `il2cpp`) but were prefixed as if they were icall-layer wrappers from
`il2cpp_gc_api.h` (namespace `il2cpp::runtime`). Same failure mode as the
get_MaxGeneration/GetAllocatedBytesForCurrentThread pair fixed earlier
(comment at lines 70–72).

## Verified correct (no change)

- `icall_gc_collect`, `icall_gc_get_total_memory`, `icall_gc_get_generation`,
  `icall_gc_keep_alive`, `icall_gc_suppress_finalize`,
  `icall_gc_re_register_for_finalize`, `icall_gc_get_allocated_bytes`,
  `icall_gc_get/set_latency_mode`, `icall_gc_get/set_loh_compaction_mode`,
  `icall_gc_is_server_gc`, `icall_gc_register/unregister_for_gc_reporting`
  → all declared in `il2cpp_gc_api.h`, ns `il2cpp::runtime`. ✔
- `icall_gc_finalize_trigger` (WaitForPendingFinalizers) → `il2cpp_gc_ext.h:77`,
  ns `il2cpp::runtime`. ✔
- `il2cpp::il2cpp_gc_get_max_generation` (`il2cpp_gc.h:285`),
  `il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread` (`il2cpp_gc.h:299`). ✔
- All ~120 `icall_*` stubs (number/crypto/x509/threadpool/etc.) → `stubs.h`,
  ns `il2cpp::runtime`; plus `il2cpp_object_ctor_nop` → `il2cpp_runtime.h:146`,
  ns `il2cpp::runtime`. ✔

## Post-fix verification

Re-ran the full resolver on the edited file:

- 162/162 names resolved; **159 OK**.
- **0 MISMATCH, 0 NOTFOUND.**
- 3 "AMBIGUOUS" flags are comment-reference noise only (symbol mentioned in a
  doc block outside/inside a different namespace than its declaration):
  - `il2cpp_gc_get_allocated_bytes_for_current_thread`: declaration
    `il2cpp_gc.h:299` + definition `gc.cpp:1954/1959` = ns `il2cpp` (matches).
    Noise: mentions at `il2cpp_gc_api.h:40/:48` (pre-namespace doc block),
    `gcapi.cpp:112/:118` (caller comments), `il2cpp_gc_api.h:88` (comment).
  - `icall_gc_get_allocated_bytes`: declaration `il2cpp_gc_api.h:91` =
    ns `il2cpp::runtime` (matches). Noise: `gcapi.cpp:6` header comment.
  - `icall_gc_get_total_memory`: declaration `il2cpp_gc_api.h:68` =
    ns `il2cpp::runtime` (matches). Noise: `gcapi.cpp:6` header comment.

No builds were run (per constraint); verification is by symbol-existence +
namespace-resolution against runtime sources only.
