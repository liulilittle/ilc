# r77 Inheritance & Type Conversion Error Analysis

**Date**: 2026-09-06
**Source**: r77 三片错误日志 (shard1/shard2/shard3)
**Total errors across all shards**: 16,653

---

## Executive Summary

The "cannot convert to base class" pattern is the **second-largest error family** in r77, accounting for roughly 2,800+ errors (17% of total). It manifests in three distinct flavors:

1. **`static_cast from X* to Y*` (not related by inheritance)** — 152 errors
2. **`cannot convert 'X' to 'Y' without a conversion operator`** — 156 errors
3. **`cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'`** — 627 errors (Top1 single error message)

---

## Top 10 Inheritance/Conversion Error Patterns

| # | Pattern | Count | Category |
|---|---------|-------|----------|
| 1 | `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` | 627 | Type mismatch (int32 vs object) |
| 2 | `cannot initialize a parameter of type 'int32_t' (aka 'int') with an lvalue of type 'Il2CppObject *'` | 243 | Type mismatch (object vs int32) |
| 3 | `cannot cast from type 'int32_t' to pointer type 'X *'` | 152 | static_cast to incompatible pointer |
| 4 | `static_cast from 'Il2CppObject *' to 'int32_t'` | 134 | Value-type unbox missing |
| 5 | `cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *'` | 627 | Array vs object |
| 6 | `cannot convert 'X' to 'int32_t' without a conversion operator'` (value types) | 44+12+8+7+7+6+6+5+5+4+4+4+4+4+3+3+2+2+2+2 = 156 | Value type → int32 |
| 7 | `static_cast from '... *' to 'Il2CppObject *' (not related by inheritance)` | 3 | Struct-to-object without Il2CppObject base |
| 8 | `static_cast from 'Il2CppObject **' to 'int32_t'` | 9 | Pointer dereference missing |
| 9 | `cannot initialize return object of type 'Il2CppObject *' with an lvalue of type 'int32_t'` | 11 | Return type mismatch |
| 10 | `cannot initialize return object of type 'int32_t' with an lvalue of type 'Il2CppObject *'` | 11 | Return type mismatch |

---

## Detailed Pattern Breakdown

### Pattern 1: `static_cast` to incompatible pointer (not related by inheritance)

**Count**: 152 total

| Source Type | Target Type | Count |
|-------------|-------------|-------|
| `Il2CppObject *` | `int32_t` | 134 |
| `Il2CppObject **` | `int32_t` | 9 |
| `Il2CppObject *` | `uint32_t` | 4 |
| `System_Security_Principal_WindowsIdentity *` | `Il2CppObject *` | 2 |
| `System_Threading_CompressedStack *` | `Il2CppObject *` | 1 |
| `System_Guid *` | `int32_t` | 1 |
| `System_Guid *` | `uint32_t` | 1 |
| `System_ValueTuple *` | `uint32_t` | 1 |
| `Il2CppObject *` | `int64_t` | 1 |
| `Il2CppObject *` | `char16_t` | 1 |

**Root cause**: The transpiler emits `static_cast<int32_t>(someObjectPtr)` where it should either:
- Use `reinterpret_cast` for pointer↔integer conversions
- Use `il2cpp_unbox_int32()` for boxed value extraction
- Use `->vtable[slot]` for virtual dispatch results

**Top affected files**:
- `System_Runtime_Remoting_Activation_RemotingXmlConfigFileParser.cpp` (37 errors)
- `System_Runtime_Serialization_Formatters_Binary_Converter.cpp` (30 errors)
- `System_Collections_Generic_GenericArraySortHelper_1_int.cpp` (8 errors)
- `System_Resources_RuntimeResourceSet.cpp` (8 errors)

---

### Pattern 2: `cannot convert 'X' to 'int32_t'` (value types without conversion operator)

**Count**: 156 total

| Value Type | Target | Count |
|------------|--------|-------|
| `System_Collections_DictionaryEntry` | `int32_t` | 44 |
| `Il2CppObject` | `int32_t` | 12 |
| `System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken` | `int32_t` | 8 |
| `System_Security_Cryptography_X509Certificates_AttributeAsn` | `int32_t` | 7 |
| `System_Diagnostics_Tracing_EventProvider_SessionInfo` | `int32_t` | 7 |
| `System_UIntPtr` | `int32_t` | 6 |
| `System_Reflection_Emit_MethodBuilder_SymCustomAttr` | `int32_t` | 6 |
| `System_Guid` | `int32_t` | 5 |
| `System_Resources_ResourceLocator` | `int32_t` | 5 |
| `il2cpp::runtime::Il2CppDateTime` | `int32_t` | 4 |
| `System_Collections_Generic_KeyValuePair_2_int` | `int32_t` | 4 |
| `il2cpp::runtime::Il2CppDateTimeOffset` | `int32_t` | 4 |
| `il2cpp::runtime::Il2CppTimeSpan` | `int32_t` | 4 |
| `il2cpp::runtime::Il2CppDecimal` | `int32_t` | 4 |
| `System_Runtime_CompilerServices_TaskAwaiter_1_int` | `int32_t` | 3 |
| `System_Collections_Generic_KeyValuePair_2_object_object` | `int32_t` | 3 |

**Root cause**: These are C# value types being assigned to `int32_t` C++ slots. The transpiler is missing field-access lowering (e.g., `.Item1`, `.Value`, `.Ticks`, etc.) before the assignment.

---

### Pattern 3: `cannot initialize parameter of type 'Il2CppObject *' with int32_t` (the Top1 single error)

**Count**: 627

This is the dominant single error. It occurs when:
1. A C# method has an `object` parameter
2. The call site passes an `int32` literal or computed value
3. The transpiler emits the raw `int32_t` value instead of `il2cpp_box_int32(...)` or `reinterpret_cast<Il2CppObject*>(...)`

**Fix strategy**: Already addressed by the `i2r` cast code added to `EmitIcallCallArgs` in CodeGenerator.cs:6565.

---

## Recommended Fix Priority

1. **Pattern 3 (627 errors)**: Add `i2r` cast to icall mappings where int32 arg needs boxing/reinterpret → DONE (cast code added, needs MethodMap wiring)
2. **Pattern 1 (134 errors, `Il2CppObject* -> int32_t`)**: Add `il2cpp_unbox_int32()` call before the static_cast, or change cast target to pointer-width integer first
3. **Pattern 2 (44 errors, `DictionaryEntry -> int32_t`)**: Implement field-access lowering for `.Key`/`.Value` on KeyValuePair/DictionaryEntry structs
4. **Pattern 1 (3 errors, struct* -> Il2CppObject*)**: These structs (WindowsIdentity, CompressedStack) don't derive from Il2CppObject. Need runtime-level struct descriptor fix.

---

## Appendix: Full Error Pattern Distribution (Top 30)

| Count | Error Pattern |
|-------|---------------|
| 1,373 | `no matching function for call to 'X'` |
| 1,245 | `cannot cast from type 'X' (aka 'X') to pointer type 'X'` |
| 960 | `cannot initialize a parameter of type 'Il2CppObject *'` |
| 855 | `no known conversion from 'Il2CppObject *' to 'int32_t'` |
| 830 | `no member named 'X' in 'X'` |
| 752 | `cannot initialize a parameter of type 'X' with an lvalue of type 'X'` |
| 669 | `assigning to 'X' (aka 'X') from incompatible type 'X'` |
| 627 | `cannot initialize a parameter of type 'Il2CppObject *' with int32_t` |
| 573 | `use of undeclared identifier 'X'; did you mean 'X'?` |
| 461 | `member reference base type 'X' is not a structure or union` |
| 441 | `cannot initialize a parameter of type 'X' with an lvalue of type 'X'` |
| 411 | `no viable conversion from 'X' to 'X'` |
| 371 | `reinterpret_cast from 'X' to 'X' is not allowed` |
| 355 | `cannot cast from type 'X' to pointer type 'X'` |
| 349 | `incompatible pointer to integer conversion assigning to 'X'` |
| 339 | `no viable conversion from returned value of type 'X' to function return type 'X'` |
| 299 | `use of undeclared identifier 'X'` |
| 266 | `unknown type name 'X'` |
| 246 | `unknown type name 'X'; did you mean 'X'?` |
| 243 | `cannot initialize a parameter of type 'int32_t'` |
| 235 | `incompatible pointer types assigning to 'X' from 'X'` |
| 226 | `no viable conversion from 'TaskAwaiter_1_int' to 'Il2CppObject *'` |
| 211 | `cannot initialize a parameter of type 'X' with an rvalue of type 'X'` |
| 185 | `cannot initialize a parameter of type 'X' (aka 'X') with an lvalue of type 'X'` |
| 173 | `incompatible pointer to integer conversion assigning to 'X' from 'X'` |
| 166 | `cannot initialize a parameter of type 'intptr_t *'` |
| 164 | `cannot initialize return object of type 'X' with an rvalue of type 'X'` |
| 152 | `static_cast from 'X' to 'X' is not allowed` |
| 143 | `no member named 'X' in namespace 'X'; did you mean 'X'?` |
| 143 | `cannot convert 'X' to 'X' without a conversion operator` |
