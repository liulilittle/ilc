# r77 Guid/IntPtr/EventRegistrationToken Value Type Analysis

Total value-type-related errors: 15111

Unique value types involved: 14


## Error count by value type

| Value Type | Count |
|------------|-------|
| `int32_t` | 10632 |
| `intptr_t` | 1922 |
| `System_Guid` | 1119 |
| `System_IntPtr` | 514 |
| `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList` | 226 |
| `uint8_t[16]` | 198 |
| `System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken` | 187 |
| `uintptr_t` | 106 |
| `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheKey` | 101 |
| `System_UIntPtr` | 50 |
| `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_MyReaderWriterLock` | 34 |
| `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_TokenListCount` | 13 |
| `System_Runtime_InteropServices_WindowsRuntime_VectorToListAdapter` | 7 |
| `System_Runtime_InteropServices_WindowsRuntime_VectorToCollectionAdapter` | 2 |

## Detailed Analysis by Value Type


### `int32_t` (10632 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 3058 |
| other | 1737 |
| cannot initialize parameter | 1623 |
| cannot cast to pointer | 1244 |
| assigning incompatible type | 843 |
| incompatible pointer to integer | 601 |
| no viable conversion | 540 |
| no viable overloaded = | 482 |
| reinterpret_cast not allowed | 352 |
| cannot convert without operator | 143 |
| too few arguments | 9 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `\_\_f\_\_AnonymousType0\_1\_int.cpp` | incompatible pointer to integer conversion assigning to 'intptr\_t' (aka 'long long') from 'int32\_t * |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignatureGen | no known conversion from 'int32\_t *' (aka 'int *') to 'intptr\_t *' (aka 'long lo |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignatureGen | no known conversion from 'int32\_t *' (aka 'int *') to 'intptr\_t *' (aka 'long lo |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyNameObject' | no known conversion from 'Il2CppObject **' to 'int32\_t' (aka 'int') for 1st argu |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyEnum' | no known conversion from 'Il2CppObject **' to 'int32\_t' (aka 'int') for 1st argu |

### `intptr_t` (1922 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 425 |
| incompatible pointer to integer | 382 |
| reinterpret_cast not allowed | 375 |
| other | 311 |
| cannot initialize parameter | 309 |
| assigning incompatible type | 68 |
| no viable conversion | 23 |
| cannot cast to pointer | 22 |
| no viable overloaded = | 4 |
| cannot convert without operator | 2 |
| too few arguments | 1 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `\_\_f\_\_AnonymousType0\_1\_int.cpp` | incompatible pointer to integer conversion assigning to 'intptr\_t' (aka 'long long') from 'int32\_t * |  |
| `\_\_f\_\_AnonymousType0\_1\_string.cpp` | incompatible pointer to integer conversion assigning to 'intptr\_t' (aka 'long long') from 'Il2CppObj |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignatureGen | no known conversion from 'int32\_t *' (aka 'int *') to 'intptr\_t *' (aka 'long lo |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | cannot initialize a parameter of type 'int32\_t *' (aka 'int *') with an lvalue of type 'intptr\_t *'  |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignatureGen | no known conversion from 'int32\_t *' (aka 'int *') to 'intptr\_t *' (aka 'long lo |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyNameObject' | no known conversion from 'Il2CppObject **' to 'int32\_t' (aka 'int') for 1st argu |

### `System_Guid` (1119 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 323 |
| cannot cast to pointer | 147 |
| other | 142 |
| no viable conversion | 136 |
| no viable overloaded = | 118 |
| cannot initialize parameter | 94 |
| array type not assignable | 54 |
| assigning incompatible type | 47 |
| reinterpret_cast not allowed | 41 |
| incompatible pointer to integer | 11 |
| cannot convert without operator | 6 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Guid\_\_ctor\_1430675b62f51540' | no known conversion from 'uint8\_t (*)[16]' (aka 'unsigned char (*)[16]') to 'Sys |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Guid\_\_ctor\_1430675b62f51540' | no known conversion from 'uint8\_t (*)[16]' (aka 'unsigned char (*)[16]') to 'Sys |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_RuntimeEnvironment\_GetRuntimeInterf | no known conversion from 'uint8\_t[16]' (aka 'unsigned char[16]') to 'System\_Guid |
| `Microsoft\_Win32\_UnsafeNativeMethods\_ManifestEtw.cpp` | no matching function for call to 'EventWriteTransfer' | no known conversion from 'uint8\_t (*)[16]' (aka 'unsigned char (*)[16]') to 'Sys |
| `System\_ActivationContext.cpp` | no matching function for call to 'ParseManifest' | no known conversion from 'std::nullptr\_t' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_System\_Guid\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1dff' | no known conversion from 'System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_N |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_System\_Guid\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1dff' | no known conversion from 'System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_N |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_System\_Guid\_int.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_System\_Guid\_int\_\_ctor\_fe | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |

### `System_IntPtr` (514 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 242 |
| other | 111 |
| assigning incompatible type | 53 |
| cannot initialize parameter | 47 |
| no viable conversion | 31 |
| reinterpret_cast not allowed | 13 |
| no viable overloaded = | 9 |
| cannot cast to pointer | 3 |
| incompatible pointer to integer | 3 |
| cannot convert without operator | 2 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyNameObject' | no known conversion from 'Il2CppObject **' to 'int32\_t' (aka 'int') for 1st argu |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyEnum' | no known conversion from 'Il2CppObject **' to 'int32\_t' (aka 'int') for 1st argu |
| `Microsoft\_Win32\_Fusion.cpp` | no viable overloaded '=' |  |
| `Microsoft\_Win32\_Fusion.cpp` | no viable conversion from 'System\_IntPtr' to 'int32\_t' (aka 'int') |  |
| `Microsoft\_Win32\_Fusion.cpp` | reinterpret\_cast from 'System\_IntPtr' to 'const char16\_t *' is not allowed |  |
| `Microsoft\_Win32\_RegistryKey.cpp` | assigning to 'intptr\_t' (aka 'long long') from incompatible type 'System\_IntPtr' |  |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'System\_IntPtr\_\_ctor\_f665c347b98a1ea5' | no known conversion from 'intptr\_t *' (aka 'long long *') to 'System\_IntPtr *' f |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegQueryInfoKey' | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 1st argum |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegQueryInfoKey' | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 1st argum |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegEnumValue' | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 1st argum |

### `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_EventRegistrationTokenList` (226 errors)

#### Error categories

| Category | Count |
|----------|-------|
| cannot cast to pointer | 52 |
| cannot initialize parameter | 44 |
| other | 31 |
| no viable conversion | 30 |
| no matching function | 28 |
| no viable overloaded = | 20 |
| assigning incompatible type | 9 |
| reinterpret_cast not allowed | 5 |
| cannot convert without operator | 5 |
| incompatible pointer to integer | 2 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_object\_System\_Collection | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'System\_Collections\_Generic\_KeyValuePair\_2\_object\_System\_Collections\_Generic\_D |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_object\_System\_Collection | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | no viable conversion from returned value of type 'System\_Collections\_DictionaryEntry' to function re |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Collections\_Generic\_Dictionary\_2\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_object\_System\_Runtime\_In | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'System\_Collections\_Generic\_KeyValuePair\_2\_object\_System\_Runtime\_InteropServic |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_object\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_EventRegistrationTokenList.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |

### `uint8_t[16]` (198 errors)

#### Error categories

| Category | Count |
|----------|-------|
| array type not assignable | 71 |
| no matching function | 39 |
| no viable overloaded = | 33 |
| other | 33 |
| assigning incompatible type | 13 |
| cannot initialize parameter | 9 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_RuntimeEnvironment\_GetRuntimeInterf | no known conversion from 'uint8\_t[16]' (aka 'unsigned char[16]') to 'System\_Guid |
| `System\_Deployment\_Internal\_Isolation\_IsolationInterop.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Deployment\_Internal\_Isolation\_IsolationInterop.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Deployment\_Internal\_Isolation\_Store.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Deployment\_Internal\_Isolation\_Store.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Deployment\_Internal\_Isolation\_Store.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Deployment\_Internal\_Isolation\_Store.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |
| `System\_Diagnostics\_SymbolStore\_SymDocumentType.cpp` | array type 'uint8\_t[16]' (aka 'unsigned char[16]') is not assignable |  |

### `System_Runtime_InteropServices_WindowsRuntime_EventRegistrationToken` (187 errors)

#### Error categories

| Category | Count |
|----------|-------|
| other | 49 |
| no viable conversion | 45 |
| cannot cast to pointer | 26 |
| no viable overloaded = | 18 |
| no matching function | 17 |
| cannot initialize parameter | 13 |
| assigning incompatible type | 8 |
| cannot convert without operator | 8 |
| reinterpret_cast not allowed | 3 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `entry.cpp` | use of undeclared identifier 'System\_Lazy\_1\_\_\_c\_System\_Runtime\_InteropServices\_WindowsRuntime\_EventR |  |
| `entry.cpp` | use of undeclared identifier 'System\_Progress\_1\_System\_Runtime\_InteropServices\_WindowsRuntime\_EventR |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_LazyHelpers\_1\_System\_Runtime\_InteropServices\_WindowsR |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_Task\_1\_\_\_c\_System\_Runtime\_InteropServices\_Windo |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass32\_0\_System\_Runt |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass32\_0\_System\_Runt |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass35\_0\_System\_Runt |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass35\_0\_System\_Runt |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass38\_0\_1\_System\_Ru |  |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass38\_0\_1\_System\_Ru |  |

### `uintptr_t` (106 errors)

#### Error categories

| Category | Count |
|----------|-------|
| reinterpret_cast not allowed | 103 |
| no matching function | 2 |
| cannot initialize parameter | 1 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `Microsoft\_Win32\_SafeHandles\_SafeLocalAllocHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLocalAllocHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaMemoryHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaMemoryHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaReturnBufferHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaReturnBufferHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `System\_Convert.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is not allowed |  |
| `System\_Convert.cpp` | reinterpret\_cast from 'long long' to 'uintptr\_t' (aka 'unsigned long long') is not allowed |  |
| `System\_Convert.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is not allowed |  |
| `System\_DateTimeFormat.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is not allowed |  |

### `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_EventCacheKey` (101 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no viable conversion | 26 |
| cannot cast to pointer | 21 |
| other | 16 |
| no matching function | 11 |
| no viable overloaded = | 10 |
| assigning incompatible type | 8 |
| cannot initialize parameter | 7 |
| reinterpret_cast not allowed | 2 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Event\_4b4a0d51c675.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_System\_Runtime\_InteropSe | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Event\_4b4a0d51c675.cpp` | cannot cast from type 'System\_Collections\_Generic\_KeyValuePair\_2\_System\_Runtime\_InteropServices\_Wind |  |
| `System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Event\_4b4a0d51c675.cpp` | no matching function for call to 'System\_Collections\_Generic\_KeyValuePair\_2\_System\_Runtime\_InteropSe | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |
| `System\_Collections\_Generic\_Dictionary\_2\_KeyCollection\_Enumerator\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistra\_740dce824ebe.cpp` | unknown type name 'V\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrSta |  |
| `System\_Collections\_Generic\_Dictionary\_2\_KeyCollection\_Enumerator\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistra\_740dce824ebe.cpp` | incompatible pointer types assigning to 'Il2CppObject *' from 'System\_Runtime\_InteropServices\_Window |  |
| `System\_Collections\_Generic\_Dictionary\_2\_KeyCollection\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Ev\_ec3f51f79871.cpp` | no viable conversion from returned value of type 'System\_Collections\_Generic\_Dictionary\_2\_KeyCollect |  |
| `System\_Collections\_Generic\_Dictionary\_2\_KeyCollection\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Ev\_ec3f51f79871.cpp` | static\_cast from 'Il2CppObject *' to 'int32\_t' (aka 'int') is not allowed |  |
| `System\_Collections\_Generic\_Dictionary\_2\_KeyCollection\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_Ev\_ec3f51f79871.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32\_t' (aka 'int') |  |
| `System\_Collections\_Generic\_Dictionary\_2\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_EventCacheKey\_Sy\_a8ebd877402b.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'intptr\_t' (aka 'long long') is not allowed |  |
| `System\_Collections\_Generic\_Dictionary\_2\_System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_EventCacheKey\_Sy\_a8ebd877402b.cpp` | no matching function for call to 'System\_Runtime\_CompilerServices\_ConditionalWeakTable\_2\_int\_Add\_4c5 | no known conversion from 'Il2CppObject *' to 'int32\_t' (aka 'int') for 2nd argum |

### `System_UIntPtr` (50 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 17 |
| assigning incompatible type | 11 |
| other | 7 |
| cannot convert without operator | 6 |
| reinterpret_cast not allowed | 5 |
| no viable overloaded = | 2 |
| cannot initialize parameter | 2 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr\_array.cpp` | no viable overloaded '=' |  |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr\_array.cpp` | cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'Il2CppObject *' |  |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr.cpp` | no member named 'icall\_unbox\_uintptr' in namespace 'il2cpp::runtime'; did you mean 'icall\_unbox\_uint |  |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr.cpp` | cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'intptr\_t *' (aka 'lo |  |
| `System\_Enum.cpp` | no matching function for call to 'System\_UIntPtr\_GetHashCode\_74f072e3e5e025f8' | no known conversion from 'int32\_t' (aka 'int') to 'System\_UIntPtr *' for 1st arg |
| `System\_Exception.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `System\_Exception.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long') is not  |  |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |

### `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_MyReaderWriterLock` (34 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 18 |
| cannot cast to pointer | 16 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_MyReaderWriterLock.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'System\_Threading\_EventWaitHandle **' to 'Il2CppObject  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_MyReaderWriterLock.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'System\_Threading\_EventWaitHandle **' to 'Il2CppObject  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |

### `System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal_NativeOrStaticEventRegistrationImpl_TokenListCount` (13 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 4 |
| cannot cast to pointer | 4 |
| no viable conversion | 2 |
| incompatible pointer to integer | 2 |
| no viable overloaded = | 1 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no viable conversion from 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_Nativ |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | incompatible pointer to integer conversion assigning to 'int32\_t' (aka 'int') from 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | incompatible pointer to integer conversion assigning to 'int32\_t' (aka 'int') from 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarsha | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_WindowsRuntimeMarshal\_NativeOrStaticEventRegistrationImpl\_TokenListCount.cpp` | cannot cast from type 'int32\_t' (aka 'int') to pointer type 'Il2CppObject *' |  |

### `System_Runtime_InteropServices_WindowsRuntime_VectorToListAdapter` (7 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 7 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Runtime\_InteropServices\_WindowsRuntime\_MapToCollectionAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToCollectionAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToCollectionAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |

### `System_Runtime_InteropServices_WindowsRuntime_VectorToCollectionAdapter` (2 errors)

#### Error categories

| Category | Count |
|----------|-------|
| no matching function | 2 |

#### Representative errors (first 10)

| Source file | Error | Note |
|-------------|-------|------|
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToCollectionAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |
| `System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToCollectionAdapter.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_WindowsRuntime\_VectorToListAdapter\_ | no known conversion from 'int32\_t' (aka 'int') to 'Il2CppObject *' for 1st argum |


## Root Cause Analysis


### 1. System.Guid — `uint8_t[16]` vs `System_Guid` struct mismatch

**Problem:** Guid is declared as `uint8_t[16]` (array type) but used as `System_Guid` struct.
C++ cannot convert between array and struct types.

**Manifestations:**
- `array type 'uint8_t[16]' is not assignable` — cannot assign struct literal to array
- `no known conversion from 'uint8_t (*)[16]' to 'System_Guid *'` — ctor call fails
- `reinterpret_cast from 'System_Guid' to 'uint8_t *' is not allowed` — icall guid_equals fails
- `cannot initialize a parameter of type 'int32_t' with an lvalue of type 'uint8_t[16]'` — ConcurrentDictionary key mismatch

**Root cause in CodeGenerator:**
- `Guid` local/temp slots are typed as `uint8_t[16]` (array) but Guid ctors expect `System_Guid*`
- When Guid is passed as argument, the C++ cannot reinterpret between array and struct
- The il2cpp_guid_equals icall expects `uint8_t*` but receives `System_Guid`

**Fix needed:**
1. Guid slots should be declared as `System_Guid` struct, not `uint8_t[16]`
2. Guid-to-uint8_t* calls need `reinterpret_cast<uint8_t*>(&guid_struct)`
3. ConcurrentDictionary<Guid,int> needs to use `System_Guid` as key, not `uint8_t[16]`

---

### 2. System.IntPtr — `intptr_t` vs `System_IntPtr` struct mismatch

**Problem:** IntPtr is declared as `intptr_t` (integer) but used as `System_IntPtr` struct.

**Manifestations:**
- `incompatible pointer to integer conversion assigning to 'intptr_t' from 'int32_t *'` — IntPtr slot assigned pointer
- `no known conversion from 'Il2CppObject **' to 'int32_t'` — IntPtr return type confusion
- `no viable conversion from 'System_IntPtr' to 'int32_t'` — struct to int
- `reinterpret_cast from 'System_IntPtr' to 'const char16_t *' is not allowed` — IntPtr passed as string

**Root cause in CodeGenerator:**
- IntPtr return types are declared as `intptr_t` but the IL expects `System_IntPtr` struct
- IntPtr ctors return `intptr_t` but are assigned to `System_IntPtr` slots
- IntPtr fields are typed `System_IntPtr*` but the slot is `intptr_t`

**Fix needed:**
1. IntPtr slots should use `System_IntPtr` struct, not `intptr_t`
2. IntPtr icalls returning `intptr_t` should be wrapped in `System_IntPtr` struct
3. IntPtr fields should be declared as `System_IntPtr` (struct), not `intptr_t`

---

### 3. System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken

**Problem:** enum treated as `int32_t` but used as struct in some contexts.

**Manifestations:**
- `cannot convert 'EventRegistrationToken' to 'int32_t' without a conversion operator` — enum to int
- `no viable overloaded '=' — no known conversion from 'Il2CppObject *' to 'EventRegistrationToken'` — object to enum
- `no viable conversion from 'EventRegistrationToken' to 'int32_t'` — struct to int

**Root cause:**
- EventRegistrationToken is an enum in .NET (underlying type int32) but the transpiler treats it as a struct
- `icall_multicast_invoke_i4` expects `int32_t` for the 2nd arg but receives `EventRegistrationToken`
- `il2cpp_list_obj_add` expects `int32_t` (boxed) but receives `EventRegistrationToken`

**Fix needed:**
1. EventRegistrationToken slots should be declared as `int32_t` (its underlying type)
2. Or provide explicit conversion operators between EventRegistrationToken and int32_t

---

### 4. Generic Type Parameter Instantiation

**Problem:** `T` defaults to `int32_t` but should be `Il2CppObject*` for reference types.

**Manifestations:**
- `cannot cast from type 'System_ArraySegment_1_int' to pointer type 'Il2CppObject *'` — value type to object
- `cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'int32_t'` — int to object
- `no known conversion from 'int32_t' to 'Il2CppObject *'` — int to object

**Root cause:**
- Generic methods are only emitted for the `int32_t` instantiation (default)
- Reference type instantiations (string, object) are not generated
- Value type instantiations use `int32_t` slot but the actual type is a struct

**Fix needed:**
1. Generate all generic instantiations, not just int32_t
2. Or use template/generic C++ code that works for all types

---

### 5. ReadOnlySpan<int> and Other Value Types

**Problem:** ReadOnlySpan<int> methods are not emitted with correct signatures.

**Manifestations:**
- `no matching function for call to 'ReadOnlySpan_1_int__GetAt_...'` — method not found
- `no known conversion from 'int32_t' to 'Il2CppObject *'` — receiver type mismatch

**Root cause:**
- ReadOnlySpan<int> methods may not be wired correctly in the MethodMap
- The receiver is declared as `int32_t` but should be `ReadOnlySpan_1_int*`

**Fix needed:**
1. Verify MethodMap coverage for ReadOnlySpan<int> methods
2. Ensure correct struct type for ReadOnlySpan<int> slots


## Fix Suggestions Summary


### Priority 1: Guid slot type
Change Guid local/temp declaration from `uint8_t[16]` to `System_Guid` struct.

### Priority 2: IntPtr slot type  
Change IntPtr local/temp declaration from `intptr_t` to `System_IntPtr` struct.

### Priority 3: EventRegistrationToken underlying type
Treat EventRegistrationToken as `int32_t` (its underlying enum type) rather than as a struct.

### Priority 4: Generic instantiation
Generate all generic method instantiations, not just int32_t default.

### Priority 5: Type-punning bridge helpers
Add `reinterpret_cast` helpers for Guid→uint8_t* and IntPtr→intptr_t conversions at call sites.
