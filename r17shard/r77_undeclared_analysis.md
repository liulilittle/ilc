# r77 Undeclared Error Analysis

## Overview

- **Total undeclared errors**: 872
- **Unique undeclared symbols**: 294
- **Error log lines analyzed**: 88,211
- **Source**: r77 3-shard generation of mscorlib (out/mscorlib_gen_r77)

---

## Top 10 Undeclared Symbols

| Rank | Symbol | Count | Type |
|------|--------|-------|------|
| 1 | `__il2cpp_ex` | 36 | Exception handling local variable |
| 2 | `System_RuntimeType_ListBuilder_1_object_get_Count_46255826a76b6404` | 34 | Generic method (object arg) |
| 3 | `loc0` | 34 | Local variable not declared |
| 4 | `System_RuntimeType_ListBuilder_1_object_ToArray_ce459a06e5466806` | 25 | Generic method (object arg) |
| 5 | `SZARRAY_class` | 18 | SZARRAY class descriptor |
| 6 | `System_RuntimeType_ListBuilder_1_object_Add_c10f6a0e5679f7f2` | 18 | Generic method (object arg) |
| 7 | `loc3` | 17 | Local variable not declared |
| 8 | `System_Diagnostics_Tracing_TraceLoggingTypeInfo_1_array__ctor_8cd34122ca48ece0` | 14 | Generic method (array of T) |
| 9 | `System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_Value_1fa83d49461a09e3` | 14 | Deeply nested generic |
| 10 | `loc2` | 13 | Local variable not declared |

---

## Error Categories

| Category | Count | Description |
|----------|-------|-------------|
| `System_Threading_*` | 226 | TaskFactory, Task, AsyncBuilder generics |
| `System_RuntimeType_*` | 100 | ListBuilder<T> with T=object |
| `loc*` | 92 | Undeclared local variables |
| `V_*` | 90 | Value type descriptors (V_ prefix) |
| `System_Runtime_*` | 39 | WindowsRuntime, Remoting, Serialization |
| `System_Nullable_*` | 36 | Nullable<T> with complex T |
| `__il2cpp_ex` | 36 | Exception handling variable |
| `System_Collections_*` | 35 | Generic collection types |
| `array_tolerant*` | 26 | Array element helpers |
| `System_Diagnostics_Tracing_*` | 24 | TraceLoggingTypeInfo generics |
| `t*` | 23 | Temp variables not declared |
| `System_Reflection_*` | 12 | Runtime reflection types |
| `System_Lazy_*` | 6 | Lazy<T> closures |
| `System_EventHandler_*` | 6 | EventHandler<T> with T=int |
| `System_Tuple_*` | 6 | Tuple descriptors |
| Other | 114 | Miscellaneous (dto, dec, SZARRAY, etc.) |

---

## Root Cause Analysis

### Pattern 1: Generic Method Instantiation Gap (Majority)

The dominant pattern is **calls to generic-instantiated methods where only the `int` (default) specialization was emitted**, but the method body references the `object` (or other type) specialization.

Example from compiler suggestions:
```
did you mean 'System_RuntimeType_ListBuilder_1_int_Add_c10f6a0e5679f7f2'?
```

This means: `ListBuilder<int>::Add` was emitted, but `ListBuilder<object>::Add` was NOT emitted, yet the generated code calls it.

**Affected types**:
- `System_RuntimeType_ListBuilder_1<object>` (most common)
- `System_Threading_Tasks_TaskFactory_1<object>`
- `System_Threading_Tasks_Task_1<object>`
- `System_Diagnostics_Tracing_TraceLoggingTypeInfo_1<T[]>` (array of T, not just int[])
- `System_Nullable_1<ReadOnlyMemory<byte>>` (complex nested generic)
- `System_Collections_Concurrent_ConcurrentDictionary_2<object,...>`

### Pattern 2: Exception Handling Variable (`__il2cpp_ex`)

36 errors where `__il2cpp_ex` is undeclared. This is the catch-block exception variable. The transpiler emits code that references `__il2cpp_ex.type_name()` or `__il2cpp_ex.message_text()` outside the catch scope, or the catch block variable wasn't declared.

### Pattern 3: Local Variables Not Declared (`loc0`, `loc2`, `loc3`, etc.)

Local variables are used in the generated C++ but never declared at the function top. This happens when:
- A local is assigned but the declaration was skipped
- The variable is referenced in a catch block but declared outside it
- Generic-closure locals leak into the default (int) instantiation

### Pattern 4: Value Type Descriptors (`V_*`)

6+ occurrences where `V_System_TimeSpan_class`, `V_System_Decimal_class`, `V_System_Collections_DictionaryEntry_class` are undeclared. These are value-type class descriptors that should be in generated.h but weren't emitted.

### Pattern 5: Static Fields Not Emitted

`System_Threading_Tasks_TaskFactory_1_int_sf_s_completeFromAsyncResult` - a static field that should be emitted but wasn't.

---

## Compiler "Did You Mean" Suggestions

The compiler often suggests an alternative that WAS emitted:

| Undeclared Symbol | Compiler Suggestion |
|-------------------|---------------------|
| `ListBuilder<object>::Add` | `ListBuilder<int>::Add` |
| `ListBuilder<object>::get_Count` | `ListBuilder<int>::get_Count` |
| `ListBuilder<object>::ToArray` | `ListBuilder<int>::ToArray` |
| `TraceLoggingTypeInfo<array>::.ctor` | `TraceLoggingTypeInfo<int[]>::.ctor` |
| `ConcurrentDictionary<object,...>::.ctor` | `ConcurrentDictionary<object,object>::.ctor` |
| `EventHandler<int>::.ctor` | `EventHandler<object>::.ctor` |

**Root cause**: The transpiler generates only the `int` (default) instantiation of generic types/methods, but the IL references other instantiations (object, string, byte[], complex nested generics). The resolver routes the call to the non-emitted instantiation symbol.

---

## Recommendations

1. **Emit all generic instantiations referenced in IL**, not just the default `int` one. The transpiler must track which MethodSpec/TypeSpec instantiations are actually used and emit struct definitions + method bodies for each.

2. **Fix `__il2cpp_ex` declaration scope**: Ensure the catch-block variable is declared inside the try/catch scope, not at function top or in a parent scope.

3. **Declare all locals before use**: Audit the local-declaration pass to ensure every assigned/referenced local gets a declaration line.

4. **Emit all V_ descriptors**: Ensure value type descriptors are emitted for every value type referenced, not just the ones with generated structs.

5. **Fix static field emission**: Audit why `sf_s_completeFromAsyncResult` and similar static fields are not emitted.
