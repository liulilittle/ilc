# R77 Ambiguous Error Analysis

**Total ambiguous errors**: 41 (Shard 1: 15, Shard 2: 7, Shard 3: 19)

## Root Cause

All ambiguous errors are **C++ namespace collisions** — the same class descriptor symbol name exists in BOTH the global namespace (emitted by `il2cppc` into `generated.h`) AND the `il2cpp::` namespace (declared in runtime headers). The `using namespace il2cpp;` directive in `generated.h` makes both candidates visible at lookup time.

The canonical pattern:
```
candidate found by name lookup is 'X_class'          ← global (generated.h extern)
candidate found by name lookup is 'il2cpp::X_class'  ← runtime header (namespace il2cpp)
```

---

## Top 10 Ambiguous Symbols

| Rank | Symbol | Count | Source of Conflict |
|------|--------|-------|-------------------|
| 1 | `System_Delegate_class` | 31 | `generated.h` global extern vs `il2cpp_delegate.h` in `namespace il2cpp` |
| 2 | `System_Collections_Generic_Dictionary_2_Enumerator_string_int_class` | 4 | `generated.h` global extern vs `dictsi_obj.h` in `namespace il2cpp` |
| 3 | `System_Collections_Generic_KeyValuePair_2_string_int_class` | 4 | `generated.h` global extern vs `dictsi_obj.h` in `namespace il2cpp` |
| 4 | `System_Collections_Generic_Dictionary_2_string_int_class` | 2 | `generated.h` global extern vs `dictsi_obj.h` in `namespace il2cpp` |
| — | Self-collision (definition + extern same file) | 3 | Same TU has both `Il2CppClass X = {...}` and `extern Il2CppClass X` |

---

## Per-Symbol Detail

### 1. `System_Delegate_class` (31 errors)

**Sources**:
- `generated.h:110212` — `extern Il2CppClass System_Delegate_class;` (global)
- `il2cpp_delegate.h:37` — `extern Il2CppClass System_Delegate_class;` (namespace il2cpp)
- `System_Delegate.cpp:42` — `Il2CppClass System_Delegate_class = {...}` (definition, global)

**Affected call sites**:
- `il2cpp_class_register(&System_Delegate_class)` in entry.cpp
- `il2cpp_array_new(&System_Delegate_class, ...)` — newarr delegate[]
- `il2cpp_is_instance(obj, &System_Delegate_class)` — castclass/isinst to Delegate

**Files hit**: entry.cpp, System_BaseConfigHandler.cpp, System_Delegate.cpp, System_Runtime_InteropServices_Marshal.cpp, System_Runtime_InteropServices_WindowsRuntime_EventRegistrationTokenTable_1_int.cpp, System_Runtime_InteropServices_WindowsRuntime_IReferenceFactory.cpp, System_Runtime_Remoting_Messaging_AsyncResult.cpp, System_Runtime_Remoting_Proxies_RealProxy.cpp, System_Runtime_Remoting_Proxies_RemotingProxy.cpp, System_Threading_Tasks_AwaitTaskContinuation.cpp, System_Threading_Tasks_StandardTaskContinuation.cpp, System_Threading_Tasks_Task.cpp, all Task`1<T>.cpp variants

### 2. `System_Collections_Generic_Dictionary_*_class` (10 errors combined)

**Sources**:
- `generated.h` — global extern for each descriptor
- `dictsi_obj.h` — same symbols in `namespace il2cpp` (lines 97, 98, 132)

**Affected call sites**:
- `il2cpp_class_register(&...)` in entry.cpp
- `il2cpp_array_new(&..., 8, true, ...)` — newarr of KV pairs/enumerators
- `icall_box_user_struct(&..., ...)` — boxing user struct

**Files hit**: entry.cpp, System_Collections_Generic_Dictionary_2_string_int.cpp, System_Collections_Generic_Dictionary_2_Enumerator_string_int.cpp

---

## Why This Happens

`generated.h` line 409: `using namespace il2cpp;`

The generated `extern Il2CppClass X_class;` declarations live in the **global namespace**. But the runtime headers (`il2cpp_delegate.h`, `dictsi_obj.h`, etc.) declare the SAME symbol inside `namespace il2cpp { ... }`. The `using`-directive drags `il2cpp::X_class` into the global scope, creating two identically-named candidates.

---

## Fix Strategy

The fix is to make the `generated.h` extern declarations live in `namespace il2cpp` so they refer to the SAME symbol as the runtime headers, not create a colliding global one:

```cpp
// generated.h
namespace il2cpp {
  // ... all existing extern declarations ...
}
using namespace il2cpp;  // keep for backward compat OR qualify every reference
```

OR remove the `using namespace il2cpp;` line and fully qualify all runtime calls (larger change).

The surgical fix: wrap the existing global `extern Il2CppClass X_class;` block in a `namespace il2cpp { ... }` wrapper — this matches where `dictsi_obj.h` and `il2cpp_delegate.h` declare their descriptors, eliminating the ambiguity at the source.
