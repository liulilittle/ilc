# r77 cannot-cast Error Analysis Report

**Date**: 2026-09-06  
**Source**: r77shard 三片错误日志 (`r77shard1_shard1_errors.log`, `r77shard2_shard2_errors.log`, `r77shard3_shard3_errors.log`)  
**Total cannot-cast errors**: 1600

---

## 1. Shard Distribution

| Shard | Errors | % |
|-------|--------|---|
| r77shard1_shard1 | 1146 | 71.6% |
| r77shard2_shard2 | 175 | 10.9% |
| r77shard3_shard3 | 279 | 17.4% |
| **Total** | **1600** | 100% |

---

## 2. Top 10 cannot-cast Error Patterns (by source type)

| # | Source Type | Count | % | Example File |
|---|-------------|-------|---|---|
| 1 | `int32_t` (aka `int`) | 1224 | 76.5% | `System_AggregateException.cpp` |
| 2 | `intptr_t` (aka `long long`) | 21 | 1.3% | `System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp` |
| 3 | `System_Threading_Tasks_VoidTaskResult` | 19 | 1.2% | `System_Runtime_CompilerServices_ConfiguredTaskAwaitable_1_object_array.cpp` |
| 4 | `System_Guid` | 16 | 1.0% | `System_Collections_Generic_ObjectEqualityComparer_1_System_Guid.cpp` |
| 5 | `System_Security_Cryptography_X509Certificates_AttributeAsn` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_Security_Cryptography_X509Certificates_AttributeAsn.cpp` |
| 6 | `il2cpp::runtime::Il2CppDateTimeOffset` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_DateTimeOffset.cpp` |
| 7 | `System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken.cpp` |
| 8 | `il2cpp::runtime::Il2CppTimeSpan` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_TimeSpan.cpp` |
| 9 | `System_Reflection_Emit_MethodBuilder_SymCustomAttr` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_Reflection_Emit_MethodBuilder_SymCustomAttr.cpp` |
| 10 | `System_Collections_DictionaryEntry` | 14 | 0.9% | `System_Collections_Generic_ObjectEqualityComparer_1_System_Collections_DictionaryEntry.cpp` |

---

## 3. Top 10 Files with Most cannot-cast Errors

| # | File | Count |
|---|------|-------|
| 1 | `System_Threading_AsyncLocalValueMap_MultiElementAsyncLocalValueMap.cpp` | 25 |
| 2 | `System_Reflection_Emit_AssemblyBuilder.cpp` | 19 |
| 3 | `System_Security_Cryptography_CryptoConfig.cpp` | 18 |
| 4 | `System_Collections_Concurrent_ConcurrentDictionary_2_object_object.cpp` | 17 |
| 5 | `System_Collections_Concurrent_ConcurrentDictionary_2_object_System_Reflection_MemberInfo_array.cpp` | 17 |
| 6 | `System_Collections_Concurrent_ConcurrentDictionary_2_string_object.cpp` | 17 |
| 7 | `System_Collections_Concurrent_ConcurrentDictionary_2_System_Guid_object.cpp` | 17 |
| 8 | `System_Collections_Concurrent_ConcurrentDictionary_2_object_object_array.cpp` | 17 |
| 9 | `System_Collections_Generic_ObjectEqualityComparer_1_System_Tuple_2...int.cpp` | 15 |
| 10 | `System_Collections_Generic_ObjectEqualityComparer_1_System_Threading...ProcessingMode.cpp` | 15 |

---

## 4. Pattern Analysis

### 4.1 Pattern 1: `int32_t` → `Il2CppObject*` (1224 errors, 76.5%)

**Root cause**: The dominant pattern. An `int32_t` value (typically from an integer local or return value) is being passed/stored where `Il2CppObject*` is expected. This occurs when:
- A boxed integer is unboxed but the unbox result is not properly cast
- An integer return value from an icall is assigned to an `Il2CppObject*` field/param without `reinterpret_cast`
- The `"i"` cast code in `EmitIcallCallArgs` does `static_cast<int32_t>(code)` instead of boxing

**Example (System_AggregateException.cpp:610)**:
```cpp
// Original error: cannot cast from type 'int32_t' (aka 'int') to pointer type 'Il2CppObject *'
t0 = static_cast<Il2CppObject*>(il2cpp::runtime::icall_exception_get_message(...));
// The icall returns int32_t (the message carrier) but the assignment target is Il2CppObject*
```

### 4.2 Pattern 2: `intptr_t` → `Il2CppObject*` (21 errors, 1.3%)

**Root cause**: An `intptr_t` (native int carrier, often from `IntPtr` values) is being assigned to `Il2CppObject*`. Same pattern as int32_t but for pointer-width values.

**Example (ConcurrentDictionary_2_int_int.cpp:71:217)**:
```cpp
t0 = static_cast<Il2CppObject*>(some_intptr_expression);
```

### 4.3 Pattern 3: `System_Threading_Tasks_VoidTaskResult` → `Il2CppObject*` (19 errors, 1.2%)

**Root cause**: A `VoidTaskResult` struct value (the result type of `Task.CompletedTask`) is being assigned to `Il2CppObject*`. This is a value-type struct that needs boxing before being treated as a reference.

**Example (ConfiguredTaskAwaitable_1_object_array.cpp:89:37)**:
```cpp
// VoidTaskResult is a struct, not a reference — needs il2cpp_box_void_task_result
t0 = static_cast<Il2CppObject*>(voidTaskResult_value);
```

### 4.4 Pattern 4: `System_Guid` → `Il2CppObject*` (16 errors, 1.0%)

**Root cause**: A `System_Guid` struct value is assigned to `Il2CppObject*`. The `Guid` struct (16 bytes) needs boxing.

**Example (ObjectEqualityComparer_1_Guid.cpp)**:
```cpp
t0 = static_cast<Il2CppObject*>(guid_value);
```

### 4.5 Pattern 5: `System_Security_Cryptography_X509Certificates_AttributeAsn` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: User struct types (like `AttributeAsn`) being assigned to `Il2CppObject*` without boxing. These are value types that need `icall_box_user_struct`.

### 4.6 Pattern 6: `il2cpp::runtime::Il2CppDateTimeOffset` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: Runtime DateTimeOffset struct assigned to Il2CppObject*. Needs boxing.

### 4.7 Pattern 7: `EventRegistrationToken` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: WinRT EventRegistrationToken struct assigned to Il2CppObject*. Needs boxing.

### 4.8 Pattern 8: `il2cpp::runtime::Il2CppTimeSpan` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: Runtime TimeSpan struct assigned to Il2CppObject*. Needs boxing.

### 4.9 Pattern 9: `MethodBuilder_SymCustomAttr` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: Reflection.Emit custom attribute struct assigned to Il2CppObject*. Needs boxing.

### 4.10 Pattern 10: `System_Collections_DictionaryEntry` → `Il2CppObject*` (14 errors, 0.9%)

**Root cause**: DictionaryEntry struct assigned to Il2CppObject*. Needs boxing.

---

## 5. Common Thread: Missing Box for Value Types

All 10 patterns share a common root cause: **a value type is being assigned to `Il2CppObject*` without going through the boxing helper**.

The C++ `static_cast<Il2CppObject*>(value)` is only valid when `value` is already a pointer (or can be implicitly converted). For value types (`int32_t`, structs), the correct approach is:
1. Box the value into a heap object → returns `Il2CppObject*`
2. Then assign to the `Il2CppObject*` target

The transpiler's `EmitIcallCallArgs` cast switch and `EmitTask9Call` logic lack the `"box:i"` cast code arm and the `TryEmitObjectParamArg` helper (removed in L2 carrier refactor) that would bridge val→ref via boxing.

---

## 6. Recommended Actions

1. **Add `"box:i"` cast code** to `EmitIcallCallArgs` in `CodeGenerator.cs` — handles `int32_t` → `Il2CppObject*` (fixes 76.5% of errors)
2. **Add `"box:guid"` cast code** for `System_Guid` → `Il2CppObject*` (fixes 1.0%)
3. **Add `TryEmitObjectParamArg` helper** (re-introduce from L2 refactor) — handles generic val→ref boxing for all struct types (fixes remaining ~22%)
4. **Wire `"box:i"` in MethodMap.Collections** for `TryMapListEnumerator` receiver cast

---

*Report generated from r77 generation artifacts. All paths relative to `E:\il2cpp`.*
