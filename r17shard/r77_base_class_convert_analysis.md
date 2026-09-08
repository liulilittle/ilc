# r77 Base Class / Inheritance / Cast / Conversion Error Analysis Report

**Scope**: r77 three-shard error logs
**Total errors**: 12604
**Base/Inheritance/Cast/Conversion related**: 3766
**Date**: 2026-09-06

---

## Top 10 Inheritance / Type-Conversion Errors

| # | Total | Shard1 | Shard2 | Shard3 | Error Description |
|---|-------|--------|--------|--------|-------------------|
| 1 | 1120 | 870 | 102 | 148 | cannot cast from type int32_t to pointer type Il2CppObject* |
| 2 | 268 | 114 | 69 | 85 | reinterpret_cast from int32_t to intptr_t is not allowed |
| 3 | 226 | 226 | 0 | 0 | no viable conversion from TaskAwaiter_1_int to Il2CppObject* |
| 4 | 126 | 61 | 17 | 48 | static_cast from Il2CppObject* to int32_t is not allowed |
| 5 | 120 | 0 | 102 | 18 | member reference base type int32_t is not a structure or union |
| 6 | 105 | 11 | 11 | 83 | incompatible pointer to integer conversion assigning to int32_t from int32_t* |
| 7 | 93 | 40 | 18 | 35 | incompatible pointer to integer conversion assigning to int32_t from Il2CppObject* |
| 8 | 75 | 0 | 3 | 72 | reinterpret_cast from int32_t to uintptr_t is not allowed |
| 9 | 73 | 67 | 5 | 1 | no viable conversion from TaskAwaiter_1_int to int32_t |
| 10 | 119 | 0 | 90 | 29 | member reference base type intptr_t is not a structure or union |

---

## Top 10 Error Sample Lines

### #1: cannot cast from type int32_t to pointer type Il2CppObject*

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_AggregateException.cpp:610:136: error: cannot cast from type int32_t to pointer type Il2CppObject*
```

### #2: reinterpret_cast from int32_t to intptr_t is not allowed

```
E:\il2cpp\out\mscorlib_gen_r77\src\Microsoft_Win32_RegistryKey.cpp:691:255: error: reinterpret_cast from int32_t to intptr_t is not allowed
```

### #3: no viable conversion from TaskAwaiter_1_int to Il2CppObject*

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_Comparer_1_object.cpp:276:152: error: no viable conversion from TaskAwaiter_1_int to Il2CppObject*
```

### #4: static_cast from Il2CppObject* to int32_t is not allowed

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp:1252:137: error: static_cast from Il2CppObject* to int32_t is not allowed
```

### #5: member reference base type int32_t is not a structure or union

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_DataCollector.cpp:494:14: error: member reference base type int32_t is not a structure or union
```

### #6: incompatible pointer to integer conversion

```
E:\il2cpp\out\mscorlib_gen_r77\src\Microsoft_Win32_Win32Native_WIN32_FIND_DATA.cpp:91:12: error: incompatible pointer to integer conversion assigning to int32_t from int32_t*
```

### #7: incompatible pointer to integer conversion from Il2CppObject*

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_DictionaryEnumerator_int_int.cpp:223:10: error: incompatible pointer to integer conversion assigning to int32_t from Il2CppObject*
```

### #8: reinterpret_cast from int32_t to uintptr_t is not allowed

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_ActivityTracker_ActivityInfo.cpp:334:8: error: reinterpret_cast from int32_t to uintptr_t is not allowed
```

### #9: no viable conversion from TaskAwaiter_1_int to int32_t

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:2287:234: error: no viable conversion from TaskAwaiter_1_int to int32_t
```

### #10: member reference base type intptr_t is not a structure or union

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_EventProvider.cpp:833:24: error: member reference base type intptr_t is not a structure or union
```

---

## Aggregated Category Statistics

- **int32_t -> Il2CppObject* conversion/assignment**: 1706
- **int32_t -> intptr_t/uintptr_t reinterpret_cast**: 343
- **Il2CppObject* -> int32_t reverse conversion**: 274
- **Pointer/integer mismatch assignment**: 198
- **TaskAwaiter_1_int type errors**: 299
- **Non-struct member reference**: 239

---

## Conclusion

r77 has **NO real cannot-convert-to-base-class errors** (only 3 not-related-by-inheritance static_cast misuses, in System_Security_Principal_WindowsIdentity and System_Threading_CompressedStack).

The **real Top problem** is **int32_t <-> Il2CppObject* type-bridge missing**:
1. int32_t directly assigned/returned to Il2CppObject* -> missing static_cast<intptr_t> intermediate (1120 places)
2. reinterpret_cast<int32_t, intptr_t> not allowed (268+75=343 places)
3. Il2CppObject* reverse to int32_t missing intermediate (126 places)
4. TaskAwaiter_1_int struct not bridged to pointer/int (226+73=299 places)

These patterns share the same root cause as cannot-init: IL slot reuse (int slot holds ref value or vice versa) causes C++ type bridge missing.
