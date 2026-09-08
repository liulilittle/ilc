# R77 TaskAwaiter Deep Error Analysis

Generated from r77 error logs (shards 1-3)

**Total unique patterns:** 13
**Total Task-related errors:** 1970

## Summary

The TaskAwaiter errors in r77 fall into three main categories:

1. **TaskAwaiter_1_int to int32_t conversion failures** (73+ errors) - The most common pattern. `TaskAwaiter_1_int` cannot implicitly convert to `int32_t` because the transpiler emits the wrong type.

2. **TaskAwaiter_1_int to Il2CppObject* conversion failures** (226+ errors) - TaskAwaiter structs are being passed where `Il2CppObject*` is expected.

3. **ValueTuple field access on TaskAwaiter** (100+ errors) - `Item1`, `Item2`, `Item3`, etc. fields are being accessed on `TaskAwaiter_1_int` which has no such fields. This suggests the transpiler is confusing `TaskAwaiter<T>` with `ValueTuple<T>`.

## Top TaskAwaiter/Task Error Patterns

| # | Count | Pattern | Example |
|---|-------|---------|---------|
| 1 | 1874 | `OTHER_TASK_ERROR` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerat...` |
| 2 | 33 | `ENTRY_CPP_UNDECLARED (generic closures)` | `E:\il2cpp\out\mscorlib_gen_r77\src\entry.cpp:314:70: error: use of undeclared identifier 'System_Thr...` |
| 3 | 16 | `INIT: Il2CppObject * <- int32_t` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_System_T...` |
| 4 | 14 | `INIT: Il2CppObject * <- Il2CppObject **` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_KeyValuePair_2_int_System_Threading_Ta...` |
| 5 | 8 | `INIT: Il2CppObject * <- System_Runtime_CompilerServices_Asyn` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:...` |
| 6 | 8 | `INIT: Il2CppObject * <- System_Runtime_CompilerServices_Asyn` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string.c...` |
| 7 | 8 | `INIT: Il2CppObject * <- System_Runtime_CompilerServices_Asyn` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_T...` |
| 8 | 2 | `INIT: Il2CppArray * <- Il2CppObject *` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_System_T...` |
| 9 | 2 | `INIT: Il2CppObject * <- intptr_t` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_ObjectEqualityComparer_1_System_Thread...` |
| 10 | 2 | `INIT: Il2CppObject ** <- System_Runtime_CompilerServices_Tas` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_InvokeTypeInfo_1_int.cpp:288:149: erro...` |
| 11 | 1 | `INIT: Il2CppObject * <- int32_t *` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_KeyValuePair_2_int_System_Threading_Ta...` |
| 12 | 1 | `INIT: Il2CppObject * <- System_Runtime_CompilerServices_Asyn` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder.cpp:309:88...` |
| 13 | 1 | `INIT: Il2CppObject ** <- int32_t *` | `E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_Concurren...` |

## Exact Error Messages (Grouped)

| # | Count | Error Message |
|---|-------|---------------|
| 1 | 226 | `no viable conversion from 'RTS_X' to 'Il2CppObject *'` |
| 2 | 73 | `no viable conversion from 'RTS_X' to 'int32_t' (aka 'int')` |
| 3 | 31 | `no member named 'Item1' in 'RTS_X'` |
| 4 | 28 | `no member named 'Item2' in 'RTS_X'` |
| 5 | 24 | `no member named 'Item3' in 'RTS_X'` |
| 6 | 20 | `no viable conversion from 'RTS_X' to 'S_X'` |
| 7 | 20 | `no member named 'Item4' in 'RTS_X'` |
| 8 | 16 | `no member named 'Item5' in 'RTS_X'` |
| 9 | 12 | `no member named 'Item6' in 'RTS_X'` |
| 10 | 8 | `cannot cast from type 'RTS_X' to pointer type 'Il2CppObject *'` |
| 11 | 8 | `no member named 'Item7' in 'RTS_X'` |
| 12 | 6 | `no viable conversion from returned value of type 'RTS_X' to function return type 'Il2CppObject *'` |
| 13 | 5 | `no viable conversion from returned value of type 'RTS_X' to function return type 'int32_t' (aka 'int` |
| 14 | 4 | `no member named 'Rest' in 'RTS_X'` |
| 15 | 3 | `no viable conversion from 'RTS_X' to 'int64_t' (aka 'long long')` |
| 16 | 3 | `cannot convert 'RTS_X' to 'int32_t' (aka 'int') without a conversion operator` |
| 17 | 2 | `no viable conversion from 'RTS_X' to 'double'` |
| 18 | 2 | `no viable conversion from 'RTS_X' to 'il2cpp::runtime::Il2CppDateTime'` |
| 19 | 2 | `no viable conversion from 'RTS_X' to 'il2cpp::runtime::Il2CppDateTimeOffset'` |
| 20 | 2 | `no viable conversion from 'RTS_X' to 'il2cpp::runtime::Il2CppDecimal'` |

## TaskAwaiter Conversion Target Types

| # | Count | Target Type |
|---|-------|-------------|
| 1 | 332 | `System_Runtime_CompilerServices_TaskAwaiter_1_int` |
| 2 | 226 | `Il2CppObject *` |
| 3 | 76 | `int32_t` |
| 4 | 3 | `int64_t` |
| 5 | 2 | `double` |
| 6 | 2 | `System_Collections_DictionaryEntry` |
| 7 | 2 | `il2cpp::runtime::Il2CppDateTime` |
| 8 | 2 | `il2cpp::runtime::Il2CppDateTimeOffset` |
| 9 | 2 | `il2cpp::runtime::Il2CppDecimal` |
| 10 | 2 | `System_Diagnostics_Tracing_EventProvider_SessionInfo` |
| 11 | 2 | `System_Guid` |
| 12 | 2 | `System_Reflection_Emit_MethodBuilder_SymCustomAttr` |
| 13 | 2 | `System_Resources_ResourceLocator` |
| 14 | 2 | `System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken` |
| 15 | 2 | `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList` |
| 16 | 2 | `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheEntry` |
| 17 | 2 | `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheKey` |
| 18 | 2 | `System_Security_Cryptography_X509Certificates_AttributeAsn` |
| 19 | 2 | `il2cpp::runtime::Il2CppTimeSpan` |
| 20 | 1 | `System_Runtime_CompilerServices_ConfiguredTaskAwaitable_1_ConfiguredTaskAwaiter_int__ctor_11a4bb3fb4dc3e37` |

## Detailed Analysis

### Pattern 1 (1874 errors)

**Pattern:** `OTHER_TASK_ERROR`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:164:329: error: no matching function for call to 'System_Threading_Volatile_Read_c71f9240faef1dff'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:165:274: error: no matching function for call to 'System_Threading_Volatile_Read_c71f9240faef1dff'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:169:5: error: no matching function for call to 'System_Collections_Generic_KeyValuePair_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode__ctor_fe5b561b5331a819'
```

### Pattern 2 (33 errors)

**Pattern:** `ENTRY_CPP_UNDECLARED (generic closures)`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\entry.cpp:314:70: error: use of undeclared identifier 'System_Threading_Tasks_Task_1___c_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken___cctor_b__64_0_bc9c30ed93f09628'
E:\il2cpp\out\mscorlib_gen_r77\src\entry.cpp:315:70: error: use of undeclared identifier 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken__FromAsyncImpl_b__0_f5f366aaeb2eb86f'; did you mean 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Threading_Tasks_VoidTaskResult__FromAsyncImpl_b__0_f5f366aaeb2eb86f'?
E:\il2cpp\out\mscorlib_gen_r77\src\entry.cpp:316:70: error: use of undeclared identifier 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken__FromAsyncImpl_b__1_0efdff1336b834e8'; did you mean 'System_Threading_Tasks_TaskFactory_1___c__DisplayClass32_0_System_Threading_Tasks_VoidTaskResult__FromAsyncImpl_b__1_0efdff1336b834e8'?
```

### Pattern 3 (16 errors)

**Pattern:** `INIT: Il2CppObject * <- int32_t`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:3065:43: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (aka 'int')
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_List_1_SynchronizedList_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:285:288: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (aka 'int')
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_List_1_SynchronizedList_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:362:286: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t' (aka 'int')
```

**Affected files:**
- `1958:51`
- `519:309`
- `169:53`
- `2170:53`
- `6894:201`
- `558:288`
- `3065:43`
- `6929:57`
- `285:288`
- `1244:39`

### Pattern 4 (14 errors)

**Pattern:** `INIT: Il2CppObject * <- Il2CppObject **`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_KeyValuePair_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:138:82: error: cannot initialize a parameter of type 'Il2CppObject *' with an rvalue of type 'Il2CppObject **'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:181:182: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'Il2CppObject **'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:252:196: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'Il2CppObject **'
```

**Affected files:**
- `178:182`
- `310:204`
- `175:182`
- `252:196`
- `823:123`
- `246:196`
- `304:204`
- `138:82`
- `181:182`

### Pattern 5 (8 errors)

**Pattern:** `INIT: Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:241:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:247:94: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp:299:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int *'
```

**Affected files:**
- `502:88`
- `518:88`
- `241:92`
- `305:94`
- `417:53`
- `247:94`
- `299:92`
- `461:90`

### Pattern 6 (8 errors)

**Pattern:** `INIT: Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string.cpp:235:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string.cpp:241:94: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string.cpp:293:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_string *'
```

**Affected files:**
- `408:55`
- `452:90`
- `299:94`
- `493:88`
- `293:92`
- `241:94`
- `509:88`
- `235:92`

### Pattern 7 (8 errors)

**Pattern:** `INIT: Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult.cpp:235:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult.cpp:241:94: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult.cpp:293:92: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult *'
```

**Affected files:**
- `452:90`
- `299:94`
- `408:53`
- `493:88`
- `293:92`
- `241:94`
- `509:88`
- `235:92`

### Pattern 8 (2 errors)

**Pattern:** `INIT: Il2CppArray * <- Il2CppObject *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:2638:45: error: cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_ObjectModel_ReadOnlyCollection_1_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:699:42: error: cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il2CppObject *'
```

**Affected files:**
- `699:42`
- `2638:45`

### Pattern 9 (2 errors)

**Pattern:** `INIT: Il2CppObject * <- intptr_t`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_ObjectEqualityComparer_1_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:201:49: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'intptr_t' (aka 'long long')
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_ObjectEqualityComparer_1_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:263:49: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'intptr_t' (aka 'long long')
```

**Affected files:**
- `263:49`
- `201:49`

### Pattern 10 (2 errors)

**Pattern:** `INIT: Il2CppObject ** <- System_Runtime_CompilerServices_TaskAwaiter_1_int *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_InvokeTypeInfo_1_int.cpp:288:149: error: cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'System_Runtime_CompilerServices_TaskAwaiter_1_int *'
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_TraceLoggingTypeInfo_1_int.cpp:167:149: error: cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'System_Runtime_CompilerServices_TaskAwaiter_1_int *'
```

**Affected files:**
- `288:149`
- `167:149`

### Pattern 11 (1 errors)

**Pattern:** `INIT: Il2CppObject * <- int32_t *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_KeyValuePair_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:146:123: error: cannot initialize a parameter of type 'Il2CppObject *' with an rvalue of type 'int32_t *' (aka 'int *')
```

**Affected files:**
- `146:123`

### Pattern 12 (1 errors)

**Pattern:** `INIT: Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Runtime_CompilerServices_AsyncTaskMethodBuilder.cpp:309:88: error: cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'System_Runtime_CompilerServices_AsyncTaskMethodBuilder *'
```

**Affected files:**
- `309:88`

### Pattern 13 (1 errors)

**Pattern:** `INIT: Il2CppObject ** <- int32_t *`

**Examples:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ConcurrentExclusiveTaskScheduler.cpp:283:934: error: cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'int32_t *' (aka 'int *')
```

**Affected files:**
- `283:934`

## Root Cause Analysis

### 1. TaskAwaiter_1_int → int32_t (73 errors)

The transpiler emits `TaskAwaiter_1_int` where `int32_t` is expected. This happens when the IL `Unsafe.As<T>` or `Unsafe.Unbox` pattern is used, or when the async state machine's `UnsafeAwaitCompleted` returns a TaskAwaiter that is then assigned to an int local.

**Example:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:2287:234: error: no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'int32_t' (aka 'int')
```

### 2. TaskAwaiter_1_int → Il2CppObject* (226 errors)

TaskAwaiter structs are being passed where `Il2CppObject*` is expected. This is the "cannot-init Top1" family - the method returns `Il2CppObject*` but the actual value is a `TaskAwaiter_1_int`.

**Example:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_Comparer_1_object.cpp:276:152: error: no viable conversion from 'System_Runtime_CompilerServices_TaskAwaiter_1_int' to 'Il2CppObject *'
```

### 3. ValueTuple field access on TaskAwaiter (100+ errors)

The transpiler is confusing `TaskAwaiter<T>` with `ValueTuple<T>`. Fields like `Item1`, `Item2`, `Item3` are being accessed on `TaskAwaiter_1_int` which has no such fields. This suggests the IR is incorrectly classifying TaskAwaiter locals as ValueTuple.

**Example:**
```
E:\il2cpp\out\mscorlib_gen_r77\src\System_ValueTuple_1_int.cpp:224:55: error: no member named 'Item1' in 'System_Runtime_CompilerServices_TaskAwaiter_1_int'
```

## Recommended Fixes

1. **TaskAwaiter → int32_t**: Add an implicit conversion or `GetResult()` call when TaskAwaiter_1_int is assigned to int32_t.

2. **TaskAwaiter → Il2CppObject***: Insert `reinterpret_cast<Il2CppObject*>` or box the TaskAwaiter value.

3. **ValueTuple field access**: Fix the locKind analysis to correctly distinguish TaskAwaiter from ValueTuple.

