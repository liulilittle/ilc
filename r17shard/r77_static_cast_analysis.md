# R77 Static Cast Error Analysis

Generated from r77 error logs (shards 1-3)

**Total unique patterns:** 11
**Total errors:** 156

## Top 30 Static Cast Error Patterns

| # | Count | Source Type | Target Type | Example File |
|---|-------|-------------|-------------|-------------|
| 1 | 134 | `Il2CppObject *` | `int32_t` | `1252: error` |
| 2 | 9 | `Il2CppObject **` | `int32_t` | `270: error` |
| 3 | 4 | `Il2CppObject *` | `uint32_t` | `388: error` |
| 4 | 2 | `System_Security_Principal_WindowsIdentity *` | `Il2CppObject *` | `721: error` |
| 5 | 1 | `System_DateTime_FullSystemTime *` | `int64_t` | `2817: error` |
| 6 | 1 | `System_Guid *` | `uint32_t` | `356: error` |
| 7 | 1 | `System_Guid *` | `int32_t` | `386: error` |
| 8 | 1 | `Il2CppObject *` | `char16_t` | `1521: error` |
| 9 | 1 | `Il2CppObject *` | `int64_t` | `3771: error` |
| 10 | 1 | `System_Threading_CompressedStack *` | `Il2CppObject *` | `1092: error` |
| 11 | 1 | `System_ValueTuple *` | `uint32_t` | `597: error` |

## Detailed Pattern Analysis

### Pattern 1: `Il2CppObject *` → `int32_t` (134 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int.cpp:1252:137: error: static_cast from 'Il2CppObject *' to 'int32_t' (aka 'int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentQueue_1_Segment_object.cpp:150:455: error: static_cast from 'Il2CppObject *' to 'int32_t' (aka 'int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentStack_1_object.cpp:980:137: error: static_cast from 'Il2CppObject *' to 'int32_t' (aka 'int') is not allowed
```

**Affected files:**

- `557: error`
- `834: error`
- `190: error`
- `709: error`
- `287: error`
- `971: error`
- `224: error`
- `1745: error`
- `150: error`
- `1493: error`

### Pattern 2: `Il2CppObject **` → `int32_t` (9 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_CspKeyContainerInfo.cpp:270:89: error: static_cast from 'Il2CppObject **' to 'int32_t' (aka 'int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_CspKeyContainerInfo.cpp:334:89: error: static_cast from 'Il2CppObject **' to 'int32_t' (aka 'int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_CspKeyContainerInfo.cpp:420:89: error: static_cast from 'Il2CppObject **' to 'int32_t' (aka 'int') is not allowed
```

**Affected files:**

- `551: error`
- `1054: error`
- `334: error`
- `594: error`
- `270: error`
- `420: error`
- `670: error`
- `368: error`
- `506: error`

### Pattern 3: `Il2CppObject *` → `uint32_t` (4 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_Utils.cpp:388:8: error: static_cast from 'Il2CppObject *' to 'uint32_t' (aka 'unsigned int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_Utils.cpp:391:8: error: static_cast from 'Il2CppObject *' to 'uint32_t' (aka 'unsigned int') is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_Utils.cpp:394:8: error: static_cast from 'Il2CppObject *' to 'uint32_t' (aka 'unsigned int') is not allowed
```

**Affected files:**

- `388: error`
- `391: error`
- `394: error`
- `1040: error`

### Pattern 4: `System_Security_Principal_WindowsIdentity *` → `Il2CppObject *` (2 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_SecurityContext.cpp:721:125: error: static_cast from 'System_Security_Principal_WindowsIdentity *' to 'Il2CppObject *', which are not related by inheritance, is not allowed
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_SecurityContext.cpp:774:125: error: static_cast from 'System_Security_Principal_WindowsIdentity *' to 'Il2CppObject *', which are not related by inheritance, is not allowed
```

**Affected files:**

- `721: error`
- `774: error`

### Pattern 5: `System_DateTime_FullSystemTime *` → `int64_t` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_DateTime.cpp:2817:74: error: static_cast from 'System_DateTime_FullSystemTime *' to 'int64_t' (aka 'long long') is not allowed
```

**Affected files:**

- `2817: error`

### Pattern 6: `System_Guid *` → `uint32_t` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_ActivityTracker_ActivityInfo.cpp:356:38: error: static_cast from 'System_Guid *' to 'uint32_t' (aka 'unsigned int') is not allowed
```

**Affected files:**

- `356: error`

### Pattern 7: `System_Guid *` → `int32_t` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_ActivityTracker_ActivityInfo.cpp:386:51: error: static_cast from 'System_Guid *' to 'int32_t' (aka 'int') is not allowed
```

**Affected files:**

- `386: error`

### Pattern 8: `Il2CppObject *` → `char16_t` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Globalization_TextInfo.cpp:1521:55: error: static_cast from 'Il2CppObject *' to 'char16_t' is not allowed
```

**Affected files:**

- `1521: error`

### Pattern 9: `Il2CppObject *` → `int64_t` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Principal_WindowsIdentity.cpp:3771:90: error: static_cast from 'Il2CppObject *' to 'int64_t' (aka 'long long') is not allowed
```

**Affected files:**

- `3771: error`

### Pattern 10: `System_Threading_CompressedStack *` → `Il2CppObject *` (1 errors)

**Example errors:**

```
E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_SecurityContext.cpp:1092:85: error: static_cast from 'System_Threading_CompressedStack *' to 'Il2CppObject *', which are not related by inheritance, is not allowed
```

**Affected files:**

- `1092: error`

## Full Pattern List

| # | Count | Source → Target |
|---|-------|----------------|
| 1 | 134 | Il2CppObject * → int32_t |
| 2 | 9 | Il2CppObject ** → int32_t |
| 3 | 4 | Il2CppObject * → uint32_t |
| 4 | 2 | System_Security_Principal_WindowsIdentity * → Il2CppObject * |
| 5 | 1 | System_DateTime_FullSystemTime * → int64_t |
| 6 | 1 | System_Guid * → uint32_t |
| 7 | 1 | System_Guid * → int32_t |
| 8 | 1 | Il2CppObject * → char16_t |
| 9 | 1 | Il2CppObject * → int64_t |
| 10 | 1 | System_Threading_CompressedStack * → Il2CppObject * |
| 11 | 1 | System_ValueTuple * → uint32_t |
