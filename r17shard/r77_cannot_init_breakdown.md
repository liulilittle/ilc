# r77 cannot-init Error Breakdown

Generated: 2026/09/06 09:06:07

## Summary

- **Total unique errors (deduplicated by file:line):** 3811
- **Total raw error occurrences:** 3813

## Pattern Distribution

| Pattern | Count | Percentage | Example Files |
|---------|-------|------------|--------------|
| Il2CppObject* <- int32_t | 666 | 17.5% | Microsoft_Win32_RegistryKey.cpp:2913, System_AggregateException.cpp:425, System_AggregateException.cpp:835 |
| int32_t <- EventRegistrationToken | 4 | 0.1% | System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_ManagedEventRegistrationImpl.cpp:251, System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl.cpp:221, System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl.cpp:420 |
| EventRegistrationToken <- Il2CppObject* | 7 | 0.2% | System_Collections_Generic_Dictionary_2_object_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList.cpp:742, System_Collections_Generic_Dictionary_2_object_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList.cpp:776, System_Collections_Generic_Dictionary_2_object_System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList.cpp:876 |
| Il2CppClass* <- int64_t | 33 | 0.9% | Microsoft_Win32_OAVariantLib.cpp:138, System_ArgIterator.cpp:210, System_Array.cpp:1068 |
| Il2CppArray* <- Il2CppObject* | 314 | 8.2% | System_Array_SorterGenericArray.cpp:406, System_Array_SZArrayEnumerator.cpp:118, System_Array.cpp:2451 |
| no viable overloaded = | 1373 | 36% | Microsoft_Win32_Fusion.cpp:148, System_AppDomain.cpp:8305, System_ArraySegment_1_int.cpp:317 |
| no viable conversion | 511 | 13.4% | Microsoft_Win32_Fusion.cpp:160, System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:2287, System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:2327 |
| incompatible pointer-to-int | 52 | 1.4% | System_AggregateException.cpp:425, System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:1188, System_Collections_Concurrent_ConcurrentDictionary_2_int_System_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp:1189 |
| cannot cast to pointer | 353 | 9.3% | System_ArraySegment_1_ArraySegmentEnumerator_int.cpp:110, System_ArraySegment_1_ArraySegmentEnumerator_int.cpp:111, System_ArraySegment_1_ArraySegmentEnumerator_int.cpp:112 |
| Other | 548 | 14.4% | (see breakdown below) |

## Top Patterns by File Distribution

### no viable overloaded = (1373 occurrences)

- **S_TupleExtensions.cpp**: 231 errors
- **S_DateTimeOffset.cpp**: 40 errors
- **S_TimeZoneInfo.cpp**: 32 errors
- **S_DateTime.cpp**: 25 errors
- **S_Decimal.cpp**: 22 errors

### Il2CppObject* <- int32_t (666 occurrences)

- **S_Diagnostics_Tracing_ManifestBuilder.cpp**: 19 errors
- **S_Collections_Concurrent_ConcurrentDictionary_2_object_S_Reflection_MemberInfo_array.cpp**: 12 errors
- **S_Collections_Concurrent_ConcurrentDictionary_2_object_object.cpp**: 12 errors
- **S_Collections_Concurrent_ConcurrentDictionary_2_string_object.cpp**: 12 errors
- **S_Collections_Concurrent_ConcurrentDictionary_2_object_object_array.cpp**: 12 errors

### no viable conversion (504 occurrences)

- **S_TimeZoneInfo.cpp**: 12 errors
- **S_Collections_Generic_Dictionary_2_S_R_I_WindowsR_WindowsRuntimeMarshal_N_EventCacheKey_Sy_a8ebd877402b.cpp**: 11 errors
- **S_Collections_Generic_Dictionary_2_S_Guid_S_Guid.cpp**: 11 errors
- **S_Collections_Generic_Dictionary_2_S_Guid_object.cpp**: 8 errors
- **S_Collections_Generic_Dictionary_2_S_Guid_int.cpp**: 8 errors

### cannot cast to pointer (353 occurrences)

- **S_R_CompilerServices_AsyncTaskMethodBuilder_1_S_Threading_Tasks_VoidTaskResult.cpp**: 15 errors
- **S_R_Serialization_Formatters_Binary_Converter.cpp**: 12 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_S_DateTimeOffset.cpp**: 11 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_S_Decimal.cpp**: 11 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_S_Diagnostics_Tracing_EventProvider_SessionInfo.cpp**: 11 errors

### Il2CppArray* <- Il2CppObject* (314 occurrences)

- **S_Array.cpp**: 33 errors
- **S_R_I_WindowsR_CLRIPropertyValueImpl.cpp**: 3 errors
- **S_Attribute.cpp**: 3 errors
- **S_R_Serialization_Formatters_Binary___BinaryWriter.cpp**: 3 errors
- **S_Collections_Generic_Dictionary_2_string_S_Collections_Generic_List_1_object.cpp**: 2 errors

### Other: Il2CppObject * <- Il2CppObject ** (75 occurrences)

- **S_R_CompilerServices_AsyncVoidMethodBuilder.cpp**: 5 errors
- **S_Security_Principal_WindowsIdentity__get_Claims_d__95.cpp**: 4 errors
- **S_R_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp**: 3 errors
- **S_R_CompilerServices_AsyncTaskMethodBuilder_1_S_Threading_Tasks_VoidTaskResult.cpp**: 3 errors
- **S_R_CompilerServices_AsyncTaskMethodBuilder_1_string.cpp**: 3 errors

### Other: System_RuntimeType_ListBuilder_1_int * <- Il2CppObject ** (62 occurrences)

- **S_RuntimeType.cpp**: 49 errors
- **S_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp**: 13 errors

### Other: Il2CppObject * <- intptr_t (62 occurrences)

- **S_R_I_GCHandleCookieTable.cpp**: 6 errors
- **S_R_Remoting_Messaging_TransitionCall.cpp**: 3 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp**: 2 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_S_TimeSpan.cpp**: 2 errors
- **S_Collections_Generic_ObjectEqualityComparer_1_S_Threading_Tasks_ConcurrentExclusiveSchedulerPair_ProcessingMode.cpp**: 2 errors

### Other: Il2CppObject * <- int32_t * (59 occurrences)

- **S_Collections_Generic_GenericArraySortHelper_1_int.cpp**: 5 errors
- **S_Collections_Generic_GenericArraySortHelper_1_string.cpp**: 5 errors
- **S_Collections_Generic_GenericArraySortHelper_2_int.cpp**: 5 errors
- **S_Collections_Generic_GenericArraySortHelper_2_string_string.cpp**: 5 errors
- **S_Diagnostics_Tracing_ManifestBuilder.cpp**: 4 errors

### Il2CppClass* <- int64_t (33 occurrences)

- **S_RuntimeTypeHandle.cpp**: 6 errors
- **S_Array.cpp**: 5 errors
- **S_Reflection_Emit_DynamicResolver.cpp**: 4 errors
- **S_Security_PermissionTokenFactory.cpp**: 2 errors
- **S_R_I_Marshal.cpp**: 1 errors

## Other Cannot-Init Breakdown (Top 20)

| Target Type <- Source Type | Count | Example File |
|---------------------------|-------|--------------|
| Il2CppObject * <- Il2CppObject ** | 75 | System_Collections_Generic_KeyValuePair_2_int_double.cpp |
| Il2CppObject * <- intptr_t | 62 | System_Collections_Generic_ObjectEqualityComparer_1_Byte.cpp |
| System_RuntimeType_ListBuilder_1_int * <- Il2CppObject ** | 62 | System_RuntimeType_RuntimeTypeCache_MemberInfoCache_1_int.cpp |
| Il2CppObject * <- int32_t * | 59 | System_Collections_Generic_GenericArraySortHelper_1_int.cpp |
| Il2CppString * <- Il2CppObject * | 18 | System_Environment.cpp |
| System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int * <- Il2CppObject ** | 16 | System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp |
| Il2CppObject * <- long long | 15 | System_Globalization_CultureData.cpp |
| Il2CppObject * <- int64_t | 13 | System_Collections_Generic_List_1_long.cpp |
| Il2CppObject * <- uint8_t (*)[16] | 13 | System_Diagnostics_Tracing_EventProvider.cpp |
| System_Guid * <- uint8_t (*)[16] | 13 | System_Deployment_Internal_Isolation_Store.cpp |
| const Il2CppMethodInfo * <- int64_t | 11 | System_Diagnostics_StackFrameHelper.cpp |
| Il2CppObject ** <- int32_t * | 11 | System_Diagnostics_Tracing_ArrayTypeInfo_1_int.cpp |
| System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int * <- System_Nullable_1_int * | 10 | System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn.cpp |
| Il2CppObject * <- System_String * | 9 | System_Collections_Generic_GenericArraySortHelper_1_string.cpp |
| Il2CppObject * <- System_Enum * | 9 | System_Enum.cpp |
| Il2CppObject * <- double | 9 | System_Collections_Generic_List_1_double.cpp |
| Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int * | 8 | System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_int.cpp |
| Il2CppObject * <- System_Utf8String * | 8 | System_Reflection_MdFieldInfo.cpp |
| Il2CppObject * <- System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult * | 8 | System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_System_Threading_Tasks_VoidTaskResult.cpp |
| Il2CppObject ** <- Il2CppObject * | 8 | System_Diagnostics_Tracing_ClassPropertyWriter_2_int_string.cpp |

## Conclusions

1. **Il2CppObject* <- int32_t** (Top1): The primary target of this fix - async state machine locals declared as Il2CppObject* but assigned int32_t values.
2. **no viable overloaded =**: Value-type struct assignment failures (System.IntPtr, Guid, EventRegistrationToken) - separate family.
3. **no viable conversion**: Struct/value-type conversion failures between runtime types.
4. **Il2CppClass* <- int64_t**: Generic type-handle resolution producing int64_t where Il2CppClass* expected.
5. **EventRegistrationToken**: WinRT-specific token type mismatches with Il2CppObject* and int32_t.

