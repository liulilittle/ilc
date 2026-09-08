# r77 Unknown Type Error Analysis

**Date**: 2026-09-06  
**Source**: r77 shard 1/2/3 error logs  
**Total unknown type errors**: 512  
**Unique type names**: 42

---

## Top 10 Unknown Type Names

| Rank | Count | Type Name | Category |
|------|-------|-----------|----------|
| 1 | 165 | `dt` | Carrier kind leak |
| 2 | 48 | `ts` | Carrier kind leak |
| 3 | 38 | `V_System_Guid` | V: prefix leak |
| 4 | 31 | `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_` | Trailing underscore leak |
| 5 | 21 | `V_System_Threading_Tasks_VoidTaskResult` | V: prefix leak |
| 6 | 16 | `V_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode` | V: prefix leak |
| 7 | 15 | `System_Nullable_1_V_System_Security_Cryptography_X509Certificates_Asn1Tag_` | Trailing underscore leak |
| 8 | 15 | `V_System_Collections_Generic_KeyValuePair_2_int` | V: prefix leak |
| 9 | 11 | `System_Threading_IThreadPoolWorkItem` | Other (missing type) |
| 10 | 10 | `V_System_TimeSpan` | V: prefix leak |

---

## Error Categories

### 1. Carrier Kind Leak (216 errors, 42.2%)

The emitter outputs the carrier kind tag (`dt`, `ts`, `dec`) as a C++ type name instead of resolving it to the real C++ type.

- `dt` (165) — `System.DateTime` carrier kind
- `ts` (48) — `System.TimeSpan` carrier kind  
- `dec` (3) — `System.Decimal` carrier kind

**Root cause**: The `TypeKindResolver.ResolveParamEncoding` / `ResolveExprKind` returns the carrier kind string (`"dt"`, `"ts"`, `"dec"`) which then gets emitted directly as a C++ type name in generic instantiation contexts (e.g., `V:<FullName>` encoding).

### 2. V: Prefix Leak (221 errors, 43.2%)

The `V:` prefix from `MethodSignatureCoder.EncodeReturn` / `EncodeParam` leaks into the C++ type name.

Examples:
- `V_System_Guid` → should be `System_Guid`
- `V_System_DateTime` → should be `il2cpp::runtime::Il2CppDateTime`
- `V_System_Decimal` → should be `il2cpp::runtime::Il2CppDecimal`

**Root cause**: The `V:` prefix is not stripped before the type name is used in generic type instantiation or local/param declaration.

### 3. Trailing Underscore Leak (58 errors, 11.3%)

The byref-value alias suffix `_` leaks into the C++ type name.

Examples:
- `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_` → should be `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1`
- `System_Nullable_1_V_System_Security_Cryptography_X509Certificates_Asn1Tag_` → trailing `_` not stripped

**Root cause**: `TypeEmitter.Sanitize` produces the trailing-underscore form for byref value types, but the alias `using Foo_ = Foo;` is not visible at the use site.

### 4. Other Missing Types (17 errors, 3.3%)

Types that have no TypeDef in the translated set and no runtime mapping:
- `System_Threading_IThreadPoolWorkItem` (11) — interface, no generated struct
- `System_Runtime_Remoting_Services_ITrackingHandler` (2) — interface
- `System_Collections_Generic_IEnumerable_1_object` (2) — interface

---

## Complete Error List (42 unique types)

| Count | Type Name | Category |
|-------|-----------|----------|
| 165 | dt | Carrier kind leak |
| 48 | ts | Carrier kind leak |
| 38 | V_System_Guid | V: prefix leak |
| 31 | System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_ | Trailing underscore |
| 21 | V_System_Threading_Tasks_VoidTaskResult | V: prefix leak |
| 16 | V_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode | V: prefix leak |
| 15 | System_Nullable_1_V_System_Security_Cryptography_X509Certificates_Asn1Tag_ | Trailing underscore |
| 15 | V_System_Collections_Generic_KeyValuePair_2_int | V: prefix leak |
| 11 | System_Threading_IThreadPoolWorkItem | Missing type |
| 10 | V_System_TimeSpan | V: prefix leak |
| 10 | V_System_DateTimeOffset | V: prefix leak |
| 9 | V_System_Decimal | V: prefix leak |
| 9 | V_System_DateTime | V: prefix leak |
| 9 | V_System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken | V: prefix leak |
| 7 | V_System_AppContext_SwitchValueState | V: prefix leak |
| 7 | V_System_Diagnostics_Tracing_EventProvider_SessionInfo | V: prefix leak |
| 7 | V_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int | V: prefix leak |
| 7 | V_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventC… | V: prefix leak |
| 7 | V_System_Resources_ResourceLocator | V: prefix leak |
| 7 | V_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList | V: prefix leak |
| 6 | V_System_Security_Cryptography_X509Certificates_AttributeAsn | V: prefix leak |
| 6 | V_System_Reflection_Emit_MethodBuilder_SymCustomAttr | V: prefix leak |
| 6 | V_System_Runtime_InteropServices_GCHandle | V: prefix leak |
| 6 | V_System_Collections_DictionaryEntry | V: prefix leak |
| 5 | System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_c_ | Trailing underscore |
| 4 | System_Security_Cryptography_X509Certificates_Span_1_u1_ | Trailing underscore |
| 4 | System_Runtime_Remoting_Services_ITrackingHandler | Missing type |
| 3 | V_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventC… | V: prefix leak |
| 3 | dec | Carrier kind leak |
| 3 | V_System_Reflection_MemberTypes | V: prefix leak |
| 3 | V_System_Diagnostics_Tracing_EmptyStruct | V: prefix leak |
| 2 | System_Nullable_1_i_ | Trailing underscore |
| 2 | System_Collections_Generic_IEnumerable_1_object | Missing type |
| 2 | V_System_Runtime_InteropServices_WindowsRuntime_PropertyType | V: prefix leak |
| 1 | V_System_Runtime_InteropServices_Architecture | V: prefix leak |
| 1 | V_System_Security_Cryptography_HashAlgorithmName | V: prefix leak |
| 1 | V_System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn | V: prefix leak |
| 1 | V_System_Security_Cryptography_X509Certificates_Asn1Tag | V: prefix leak |
| 1 | System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_u1_ | Trailing underscore |
| 1 | V_System_Security_Cryptography_X509Certificates_MacData | V: prefix leak |
| 1 | V_System_Threading_CancellationTokenRegistration | V: prefix leak |
| 1 | V_System_Diagnostics_Tracing_EventTags | V: prefix leak |

---

## Recommended Fix Priority

1. **Carrier kind leak (dt/ts/dec)** — 216 errors. Fix `TypeKindResolver.ResolveParamEncoding` to return the actual C++ type name, not the carrier kind tag.
2. **V: prefix leak** — 221 errors. Strip `V:` prefix in `TypeCppName` / `ResolveExprKind` before emitting as C++ type.
3. **Trailing underscore leak** — 58 errors. Ensure the byref-value alias is emitted before use sites, or strip trailing `_` when resolving the type.
4. **Missing types** — 17 errors. Add runtime mappings for `System_Threading_IThreadPoolWorkItem` etc.
