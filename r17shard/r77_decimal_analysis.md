# r77 System.Decimal Error Analysis Report

**Date**: 2026-09-06  
**Source**: r77shard 三片错误日志  
**Total Decimal-related entries**: 835  
**Actual error lines (excl. notes/hints)**: 220  

---

## 1. Shard Distribution

| Shard | Decimal Errors (all) | Errors (excl. notes) |
|-------|---------------------|---------------------|
| r77shard1_shard1 | 586 | ~140 |
| r77shard2_shard2 | 166 | ~50 |
| r77shard3_shard3 | 83 | ~30 |
| **Total** | **835** | **~220** |

---

## 2. Top 10 Decimal Error Types

| # | Error Type | Count | % | Category |
|---|-----------|-------|---|----------|
| 1 | `no member named 'lo'/'mid'/'hi' in 'il2cpp::runtime::Il2CppDecimal'` | 72 (24+24+24) | 32.7% | Struct field mismatch |
| 2 | `cannot cast from type 'il2cpp::runtime::Il2CppDecimal' to pointer type 'Il2CppObject *'` | 12 | 5.5% | Val→Ref missing box |
| 3 | `no member named 'icall_unbox_date_time'` | 10 | 4.5% | Wrong unboxer name |
| 4 | `assigning to 'int32_t' from incompatible type 'il2cpp::runtime::Il2CppDecimal'` | 9 | 4.1% | Decimal→int implicit |
| 5 | `unknown type name 'V_System_Decimal'` | 9 | 4.1% | Type name mangling |
| 6 | `no member named 'icall_box_date_time'` | 9 | 4.1% | Wrong box helper name |
| 7 | `no viable conversion from 'Il2CppDecimal' to 'Il2CppObject *'` | 6 | 2.7% | Return type mismatch |
| 8 | `no viable conversion from 'int32_t' to 'Il2CppDecimal'` | 4 | 1.8% | int→Decimal implicit |
| 9 | `cannot convert 'Il2CppDecimal' to 'int32_t' without conversion operator` | 4 | 1.8% | Decimal→int cast |
| 10 | `static_cast from 'int32_t *' to 'Il2CppDecimal *' not allowed` | 4 | 1.8% | Pointer reinterpret |

---

## 3. Top 10 Affected Files

| # | File | Count |
|---|------|-------|
| 1 | `System_Decimal.cpp` | 80 |
| 2 | `System_Collections_Generic_List_1_System_Decimal.cpp` | 25 |
| 3 | `System_Convert.cpp` | 17 |
| 4 | `System_Collections_Generic_ObjectEqualityComparer_1_System_Decimal.cpp` | 11 |
| 5 | `System_Math.cpp` | 11 |
| 6 | `System_Collections_Generic_List_1_SynchronizedList_System_Decimal.cpp` | 7 |
| 7 | `System_Collections_ObjectModel_ReadOnlyCollection_1_System_Decimal.cpp` | 6 |
| 8 | `System_Diagnostics_Tracing_TraceLoggingTypeInfo_1_System_Decimal.cpp` | 4 |
| 9 | `System_Variant.cpp` | 4 |
| 10 | `System_Collections_Generic_EqualityComparer_1_System_Decimal.cpp` | 4 |

---

## 4. Pattern Analysis

### 4.1 Pattern 1: `lo`/`mid`/`hi` Member Access Errors (72 errors, 32.7%)

**Root cause**: The generated code tries to access `lo`, `mid`, `hi` fields on `Il2CppDecimal`, but these fields don't exist in the runtime struct. The runtime `Il2CppDecimal` struct (decimal.h) uses internal field names that differ from what the transpiler emits.

**Example (System_Decimal.cpp:340)**:
```cpp
// Generated code tries:
decimal.lo = some_value;
// But Il2CppDecimal has different internal field names (e.g., reserved, hi, mid, lo, flags)
```

**Fix**: Update `MethodMap.Decimal.cs` or the type emitter to use the correct field names from `decimal.h`.

### 4.2 Pattern 2: `Il2CppDecimal` → `Il2CppObject*` (12 errors, 5.5%)

**Root cause**: A Decimal struct value is being cast to `Il2CppObject*` without boxing. Same val→ref pattern as other value types.

**Example (List_1_System_Decimal.cpp:898)**:
```cpp
t0 = static_cast<Il2CppObject*>(decimal_value);
// Needs: il2cpp::il2cpp_box_decimal(&decimal_value)
```

### 4.3 Pattern 3: `icall_unbox_date_time` (10 errors, 4.5%)

**Root cause**: The generated code calls `icall_unbox_date_time` but this symbol doesn't exist — the runtime only has `icall_unbox_decimal`. This is a naming error in MethodMap or StackFold where the wrong unboxer is wired.

**Example (EventProvider.cpp:1305)**:
```cpp
// Generated:
il2cpp::runtime::icall_unbox_date_time(obj)
// Should be:
il2cpp::runtime::icall_unbox_decimal(obj)
```

### 4.4 Pattern 4: `int32_t` ← `Il2CppDecimal` Assignment (9 errors, 4.1%)

**Root cause**: A Decimal struct value is being assigned to an `int32_t` local without an explicit conversion operator.

**Example (ReadOnlyCollection_1_System_Decimal.cpp:745)**:
```cpp
int32_t loc0 = decimal_value;  // No implicit conversion
```

### 4.5 Pattern 5: `V_System_Decimal` Unknown Type (9 errors, 4.1%)

**Root cause**: The type name mangler produces `V_System_Decimal` (the `V:` prefix from ECMA VALUETYPE encoding) but the generated header only declares `System_Decimal`. The `V:` prefix is not being stripped during type emission.

**Example (List_1_System_Decimal.cpp:578)**:
```cpp
V_System_Decimal t1;  // Unknown type
// Should be: System_Decimal t1;
```

### 4.6 Pattern 6: `icall_box_date_time` (9 errors, 4.1%)

**Root cause**: Same as Pattern 3 but for boxing — the wrong helper name is generated.

### 4.7 Pattern 7: `Il2CppDecimal` → `Il2CppObject*` Return (6 errors, 2.7%)

**Root cause**: A method returning `Il2CppObject*` has a `return` statement with a Decimal value. The Decimal needs boxing before returning.

---

## 5. Common Thread: Decimal Type System Mismatch

All 6 patterns share a common theme: **the transpiler's view of System.Decimal doesn't match the runtime's `Il2CppDecimal` struct**.

Specifically:
1. **Field names**: The transpiler emits `lo/mid/hi` but the runtime struct uses different names
2. **Box/unbox helpers**: The transpiler generates `icall_box_date_time`/`icall_unbox_date_time` instead of `icall_box_decimal`/`icall_unbox_decimal`
3. **Type name mangling**: The `V:` prefix from ECMA VALUETYPE encoding leaks into generated code as `V_System_Decimal`
4. **Implicit conversions**: C++ has no implicit conversion between `Il2CppDecimal` and `int32_t`/`Il2CppObject*`, requiring explicit boxing/unboxing

---

## 6. Recommended Actions

1. **Fix field name mapping** for `Il2CppDecimal` in decimal.h — ensure `lo/mid/hi` match between transpiler and runtime (fixes 32.7%)
2. **Fix box/unbox helper names** in MethodMap.Decimal.cs — replace `icall_box_date_time`/`icall_unbox_date_time` with `icall_box_decimal`/`icall_unbox_decimal` (fixes 8.6%)
3. **Strip `V:` prefix** in type name mangler for VALUETYPE-encoded types (fixes 4.1%)
4. **Add explicit boxing** for Decimal→Il2CppObject* conversions (fixes 5.5%)
5. **Add explicit conversion** for Decimal↔int32_t (fixes 3.6%)

---

*Report generated from r77 generation artifacts. All paths relative to `E:\il2cpp`.*
