# r77 membref / type-mismatch error analysis

Total errors parsed: 16653

Unique receiver types: 113


## Top 10 receiver types by error count

| Rank | Receiver | Count |
|------|----------|-------|
| 1 | `unknown` | 3172 |
| 2 | `other` | 2433 |
| 3 | `Il2CppObject *` | 1702 |
| 4 | `assignment` | 1373 |
| 5 | `namespace` | 1093 |
| 6 | `undeclared_identifier` | 872 |
| 7 | `Il2CppArray *` | 314 |
| 8 | `int32_t` | 238 |
| 9 | `Il2CppObject **` | 156 |
| 10 | `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int *` | 138 |

## Detailed breakdown by receiver type


### `unknown` (3172 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Guid\_\_ctor\_1430675b62f51540' | `System\_Guid\_\_ctor\_1430675b62f51540` |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Guid\_\_ctor\_1430675b62f51540' | `System\_Guid\_\_ctor\_1430675b62f51540` |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'System\_Runtime\_InteropServices\_RuntimeEnvironm | `System\_Runtime\_InteropServices\_RuntimeEnvironment\_GetRuntime` |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_St | `Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignat` |
| `Microsoft\_Runtime\_Hosting\_StrongNameHelpers.cpp` | no matching function for call to 'Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_St | `Microsoft\_Runtime\_Hosting\_StrongNameHelpers\_StrongNameSignat` |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyNameObject' | `CreateAssemblyNameObject` |
| `Microsoft\_Win32\_Fusion.cpp` | no matching function for call to 'CreateAssemblyEnum' | `CreateAssemblyEnum` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegFlushKey' | `RegFlushKey` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'System\_Buffer\_Memcpy\_58e096888db2c4e3' | `System\_Buffer\_Memcpy\_58e096888db2c4e3` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegDeleteKey' | `RegDeleteKey` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegDeleteKey' | `RegDeleteKey` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegDeleteKey' | `RegDeleteKey` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegDeleteValue' | `RegDeleteValue` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'System\_IntPtr\_\_ctor\_f665c347b98a1ea5' | `System\_IntPtr\_\_ctor\_f665c347b98a1ea5` |
| `Microsoft\_Win32\_RegistryKey.cpp` | no matching function for call to 'RegConnectRegistry' | `RegConnectRegistry` |
| ... | ... | ... |
| *3157 more errors* | | |

### `other` (2433 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `entry.cpp` | reference to 'System\_Delegate\_class' is ambiguous | `reference to 'System\_Delegate\_class' is ambiguous` |
| `entry.cpp` | reference to 'System\_Collections\_Generic\_Dictionary\_2\_string\_int\_class' is ambig | `reference to 'System\_Collections\_Generic\_Dictionary\_2\_string` |
| `entry.cpp` | reference to 'System\_Collections\_Generic\_KeyValuePair\_2\_string\_int\_class' is amb | `reference to 'System\_Collections\_Generic\_KeyValuePair\_2\_stri` |
| `entry.cpp` | reference to 'System\_Collections\_Generic\_Dictionary\_2\_Enumerator\_string\_int\_clas | `reference to 'System\_Collections\_Generic\_Dictionary\_2\_Enumer` |
| `Microsoft\_Win32\_RegistryKey.cpp` | incompatible pointer types assigning to 'Microsoft\_Win32\_SafeHandles\_SafeRegistr | `incompatible pointer types assigning to 'Microsoft\_Win32\_Saf` |
| `Microsoft\_Win32\_RegistryKey.cpp` | incompatible pointer types assigning to 'Microsoft\_Win32\_SafeHandles\_SafeRegistr | `incompatible pointer types assigning to 'Microsoft\_Win32\_Saf` |
| `Microsoft\_Win32\_RegistryKey.cpp` | incompatible pointer types assigning to 'Microsoft\_Win32\_SafeHandles\_SafeRegistr | `incompatible pointer types assigning to 'Microsoft\_Win32\_Saf` |
| `Microsoft\_Win32\_RegistryKey.cpp` | incompatible pointer types assigning to 'Microsoft\_Win32\_SafeHandles\_SafeRegistr | `incompatible pointer types assigning to 'Microsoft\_Win32\_Saf` |
| `System\_AggregateException.cpp` | incompatible integer to pointer conversion assigning to 'Il2CppObject *' from 'i | `incompatible integer to pointer conversion assigning to 'Il2` |
| `System\_AppDomain.cpp` | cannot initialize return object of type 'Il2CppObject *' with an lvalue of type  | `cannot initialize return object of type 'Il2CppObject *' wit` |
| `System\_AppDomain.cpp` | too many arguments to function call, expected 0, have 1 | `too many arguments to function call, expected 0, have 1` |
| `System\_AppDomain.cpp` | cannot initialize return object of type 'Il2CppObject *' with an rvalue of type  | `cannot initialize return object of type 'Il2CppObject *' wit` |
| `System\_AppDomainManager.cpp` | too many arguments to function call, expected 0, have 1 | `too many arguments to function call, expected 0, have 1` |
| `System\_AppDomainManager.cpp` | cannot initialize return object of type 'Il2CppObject *' with an rvalue of type  | `cannot initialize return object of type 'Il2CppObject *' wit` |
| `System\_BaseConfigHandler.cpp` | reference to 'System\_Delegate\_class' is ambiguous | `reference to 'System\_Delegate\_class' is ambiguous` |
| ... | ... | ... |
| *2418 more errors* | | |

### `Il2CppObject *` (1702 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `Microsoft\_Win32\_RegistryKey.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_AggregateException.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_AggregateException.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an rvalue of type 'i | `from\_int32\_t` |
| `System\_AggregateException.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_ArraySegment\_1\_ArraySegmentEnumerator\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_ArraySegmentEnumerator\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_ArraySegmentEnumerator\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_ArraySegmentEnumerator\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_ArraySegment\_1\_int.cpp` | cannot cast from type 'System\_ArraySegment\_1\_int' to pointer type 'Il2CppObject  | `from\_System\_ArraySegment\_1\_int` |
| `System\_Char.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| `System\_Char.cpp` | cannot initialize a parameter of type 'Il2CppObject *' with an lvalue of type 'i | `from\_int32\_t` |
| ... | ... | ... |
| *1687 more errors* | | |

### `assignment` (1373 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `Microsoft\_Win32\_Fusion.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_AppDomain.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_ArraySegment\_1\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no viable overloaded '=' | `no viable overloaded '='` |
| ... | ... | ... |
| *1358 more errors* | | |

### `namespace` (1093 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `System\_AppDomain.cpp` | no member named 'icall\_box\_intptr' in namespace 'il2cpp::runtime'; did you mean  | `icall\_box\_intptr` |
| `System\_AppDomain.cpp` | no member named 'icall\_unbox\_intptr' in namespace 'il2cpp::runtime'; did you mea | `icall\_unbox\_intptr` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_tables' in 'System\_Collections\_Concurrent\_ConcurrentDictionar | `m\_tables` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_buckets' in 'System\_Collections\_Concurrent\_ConcurrentDictiona | `m\_buckets` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_tables' in 'System\_Collections\_Concurrent\_ConcurrentDictionar | `m\_tables` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_buckets' in 'System\_Collections\_Concurrent\_ConcurrentDictiona | `m\_buckets` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_key' in 'System\_Collections\_Concurrent\_ConcurrentDictionary\_2 | `m\_key` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_value' in 'System\_Collections\_Concurrent\_ConcurrentDictionary | `m\_value` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_next' in 'System\_Collections\_Concurrent\_ConcurrentDictionary\_ | `m\_next` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no member named 'm\_next' in 'System\_Collections\_Concurrent\_ConcurrentDictionary\_ | `m\_next` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no member named 'm\_buckets' in 'System\_Collections\_Concurrent\_ConcurrentDictiona | `m\_buckets` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no member named 'm\_locks' in 'System\_Collections\_Concurrent\_ConcurrentDictionary | `m\_locks` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no member named 'm\_buckets' in 'System\_Collections\_Concurrent\_ConcurrentDictiona | `m\_buckets` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no member named 'm\_locks' in 'System\_Collections\_Concurrent\_ConcurrentDictionary | `m\_locks` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no member named 'm\_locks' in 'System\_Collections\_Concurrent\_ConcurrentDictionary | `m\_locks` |
| ... | ... | ... |
| *1078 more errors* | | |

### `undeclared_identifier` (872 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `entry.cpp` | use of undeclared identifier 'System\_Lazy\_1\_\_\_c\_System\_Runtime\_InteropServices\_W | `System\_Lazy\_1\_\_\_c\_System\_Runtime\_InteropServices\_WindowsRunt` |
| `entry.cpp` | use of undeclared identifier 'System\_Progress\_1\_System\_Runtime\_InteropServices\_W | `System\_Progress\_1\_System\_Runtime\_InteropServices\_WindowsRunt` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_LazyHelpers\_1\_System\_Runtime\_Inte | `System\_Threading\_LazyHelpers\_1\_System\_Runtime\_InteropService` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_Task\_1\_\_\_c\_System\_Runtime\_I | `System\_Threading\_Tasks\_Task\_1\_\_\_c\_System\_Runtime\_InteropServ` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass32\_0\_S` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass32\_0\_S` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass35\_0\_S` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass35\_0\_S` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass38\_0\_1` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass38\_0\_1` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass41\_0\_2` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass41\_0\_2` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass44\_0\_3` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayC | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_\_DisplayClass44\_0\_3` |
| `entry.cpp` | use of undeclared identifier 'System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_System\_Ru | `System\_Threading\_Tasks\_TaskFactory\_1\_\_\_c\_System\_Runtime\_Inte` |
| ... | ... | ... |
| *857 more errors* | | |

### `Il2CppArray *` (314 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `System\_Array\_SorterGenericArray.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an rvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array\_SZArrayEnumerator.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| `System\_Array.cpp` | cannot initialize a parameter of type 'Il2CppArray *' with an lvalue of type 'Il | `from\_Il2CppObject *` |
| ... | ... | ... |
| *299 more errors* | | |

### `int32_t` (238 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `Microsoft\_Win32\_Fusion.cpp` | no viable conversion from 'System\_IntPtr' to 'int32\_t' (aka 'int') | `from\_System\_IntPtr` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_System\_Threading\_Tasks\_ConcurrentExclusiveSchedulerPair\_ProcessingMode.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_System\_Threading\_Tasks\_ConcurrentExclusiveSchedulerPair\_ProcessingMode.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_int\_System\_Threading\_Tasks\_ConcurrentExclusiveSchedulerPair\_ProcessingMode.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_object\_object\_array.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_object\_object\_array.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_object\_object.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_object\_object.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_object\_System\_Reflection\_MemberInfo\_array.cpp` | no viable conversion from 'System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int' to | `from\_System\_Runtime\_CompilerServices\_TaskAwaiter\_1\_int` |
| ... | ... | ... |
| *223 more errors* | | |

### `Il2CppObject **` (156 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int\_System\_Threading\_Tasks\_ConcurrentExclusiveSchedulerPair\_ProcessingMode.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_int\_System\_Threading\_Tasks\_ConcurrentExclusiveSchedulerPair\_ProcessingMode.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_object\_array.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_object\_array.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_System\_Reflection\_MemberInfo\_array.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_System\_Reflection\_MemberInfo\_array.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_object.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_object\_object.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_string\_object.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_string\_object.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| `System\_Collections\_Concurrent\_ConcurrentDictionary\_2\_\_GetEnumerator\_d\_\_34\_System\_Guid\_int.cpp` | no matching function for call to 'System\_Threading\_Volatile\_Read\_c71f9240faef1df | `System\_Threading\_Volatile\_Read\_c71f9240faef1dff` |
| ... | ... | ... |
| *141 more errors* | | |

### `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int *` (138 errors)

| Source file | Error | Missing member |
|-------------|-------|----------------|
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AlgorithmIdentifierAsn.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| `System\_Security\_Cryptography\_X509Certificates\_AsnValueReader.cpp` | no matching function for call to 'System\_Security\_Cryptography\_X509Certificates\_ | `System\_Security\_Cryptography\_X509Certificates\_ReadOnlySpan\_1` |
| ... | ... | ... |
| *123 more errors* | | |

## Error categories summary

| Category | Count |
|----------|-------|
| no matching function | 4160 |
| other | 3156 |
| cannot initialize parameter | 1995 |
| cannot cast to pointer | 1600 |
| no viable overloaded = | 1373 |
| assigning incompatible type | 1063 |
| no viable conversion | 1047 |
| undeclared identifier | 872 |
| incompatible pointer to integer | 641 |
| reinterpret_cast not allowed | 445 |
| cannot convert without operator | 143 |
| too few arguments | 87 |
| array type not assignable | 71 |

## Key findings

1. **System.Guid** is the dominant error source - Guid is declared as `uint8_t[16]` but used as `System_Guid` struct
2. **System.IntPtr** confusion - `intptr_t` vs `System_IntPtr` struct mismatch
3. **System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken** - enum treated as int32 but used as struct
4. **Generic type parameter instantiation** - `T` defaults to `int32_t` but should be `Il2CppObject*` for reference types
5. **Delegate method registration** - generic closure methods not emitted for non-int instantiations