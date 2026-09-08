# r77 "no viable overloaded" Error Pattern Analysis

## Summary

| Shard | Errors |
|-------|--------|
| r77shard1_shard1_errors.log | 688 |
| r77shard2_shard2_errors.log | 196 |
| r77shard3_shard3_errors.log | 489 |
| **Total** | **1,373** |

## Top 10 Target Types (RHS - What C++ Expected)

| Rank | Type | Count |
|------|------|-------|
| 1 | System_Runtime_CompilerServices_TaskAwaiter_1_int | 582 |
| 2 | System_Int32 | 264 |
| 3 | Il2CppDateTime | 120 |
| 4 | Il2CppObject | 73 |
| 5 | System_Guid | 49 |
| 6 | Il2CppTimeSpan | 39 |
| 7 | Il2CppDateTimeOffset | 38 |
| 8 | System_Runtime_CompilerServices_ConfiguredTaskAwaitable_ConfiguredTaskAwaiter | 33 |
| 9 | Il2CppDecimal | 28 |
| 10 | System_Collections_DictionaryEntry | 21 |

## Top 10 Source Types (LHS - What Was Provided)

| Rank | Type | Count |
|------|------|-------|
| 1 | int32_t | 346 |
| 2 | Il2CppObject* | 87 |
| 3 | System_Int32 | 82 |
| 4 | intptr_t | 34 |
| 5 | System_IntPtr | 31 |
| 6 | System_Runtime_CompilerServices_TaskAwaiter_1_int | 28 |
| 7 | int | 14 |
| 8 | Il2CppDateTime | 8 |
| 9 | System_DateTime | 7 |
| 10 | double | 7 |

## Pattern Analysis

### Pattern 1: TaskAwaiter Slot Reuse (582 errors)
**Root Cause:** Local slot declared as `TaskAwaiter_1_int` but assigned `int32_t` values. The IR reuses a slot that was once a reference (ref-kind) for int32 storage. C++ refuses implicit int→struct conversion.

**Example:**
```
t2 = il2cpp::il2cpp_unbox_int32(arg1);  // stores int32_t
// but t2 is declared as System_Runtime_CompilerServices_TaskAwaiter_1_int
```

### Pattern 2: Missing Implicit Conversion (264 errors)
**Root Cause:** `System_Int32` (generated struct wrapper) lacks implicit constructor from `int32_t`. The generated struct needs `System_Int32() = default;` and `System_Int32(int32_t v) : value__{v} {}` to allow implicit conversion.

**Example:**
```
loc0 = 0;  // stores int, but loc0 is System_Int32
```

### Pattern 3: BCL Value-Type Mismatch (120 errors for DateTime)
**Root Cause:** Code uses `System_DateTime` (generated wrapper) but runtime expects `Il2CppDateTime` (runtime struct). The dual BCL/runtime type model requires explicit mapping at all use sites.

## Root Cause Summary

All three patterns trace to **CodeGenerator.TypeCppName()** returning the generated BCL wrapper type name instead of the runtime struct type for value-type fields and locals. The fix requires:

1. **Local-slot kind reanalysis** - A slot that stores only int32 should be declared int32_t, not TaskAwaiter_1_int
2. **Implicit constructors on wrapper structs** - Allow `System_Int32 = int` syntax
3. **BCL-to-runtime type forwarding** - Use `il2cpp::runtime::Il2CppDateTime` everywhere, not `System_DateTime`

## Recommended Fix

The 582 TaskAwaiter errors can be fixed by auditing `RootSlotPass` / `TempSlotPass` for slots that are classified as `ref` (reference) by the initial IL-kind scan but only ever receive value-type stores in the folded IR. Those slots should be reclassified as `val` (int32_t) at emission time.
