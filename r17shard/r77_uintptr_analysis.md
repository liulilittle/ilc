# r77 System.UIntPtr Error Pattern Analysis

Total UIntPtr-related errors: 153


## Error pattern distribution

| Pattern | Count |
|---------|-------|
| uintptr_t_vs_int32 | 79 |
| other | 67 |
| UIntPtr_vs_int32 | 6 |
| UIntPtr_param_to_int32 | 1 |

## Top source files

| Source file | Count |
|-------------|-------|
| `System\_Text\_UTF8Encoding.cpp` | 20 |
| `System\_UIntPtr.cpp` | 19 |
| `System\_Text\_UnicodeEncoding.cpp` | 17 |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | 11 |
| `System\_Text\_DBCSCodePageEncoding.cpp` | 9 |
| `System\_Security\_Principal\_WindowsIdentity.cpp` | 8 |
| `System\_Text\_ASCIIEncoding.cpp` | 8 |
| `System\_Text\_SBCSCodePageEncoding.cpp` | 8 |
| `System\_Text\_UTF32Encoding.cpp` | 6 |
| `System\_Text\_Latin1Encoding.cpp` | 5 |
| `System\_DateTimeFormat.cpp` | 4 |
| `System\_Runtime\_InteropServices\_SafeBuffer.cpp` | 4 |
| `System\_Convert.cpp` | 3 |
| `System\_Diagnostics\_Tracing\_ActivityTracker\_ActivityInfo.cpp` | 3 |
| `System\_Threading\_Tasks\_StackGuard.cpp` | 3 |
| `Microsoft\_Win32\_SafeHandles\_SafeLocalAllocHandle.cpp` | 2 |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaMemoryHandle.cpp` | 2 |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaReturnBufferHandle.cpp` | 2 |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr\_array.cpp` | 2 |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr.cpp` | 2 |

## Detailed Analysis by Pattern


### Pattern: `uintptr_t_vs_int32` (79 errors)

| Source file | Error message | Note |
|-------------|---------------|------|
| `System\_Convert.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Convert.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_DateTimeFormat.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_DateTimeFormat.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Diagnostics\_Tracing\_ActivityTracker\_ActivityInfo.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Diagnostics\_Tracing\_ActivityTracker\_ActivityInfo.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Diagnostics\_Tracing\_ActivityTracker\_ActivityInfo.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_String.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| `System\_Text\_ASCIIEncoding.cpp` | reinterpret\_cast from 'int32\_t' (aka 'int') to 'uintptr\_t' (aka 'unsigned long long') is n |  |
| *64 more* | | |

### Pattern: `other` (67 errors)

| Source file | Error message | Note |
|-------------|---------------|------|
| `Microsoft\_Win32\_SafeHandles\_SafeLocalAllocHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLocalAllocHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaMemoryHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaMemoryHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaReturnBufferHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `Microsoft\_Win32\_SafeHandles\_SafeLsaReturnBufferHandle.cpp` | reinterpret\_cast from 'uintptr\_t' (aka 'unsigned long long') to 'intptr\_t' (aka 'long long |  |
| `System\_Convert.cpp` | reinterpret\_cast from 'long long' to 'uintptr\_t' (aka 'unsigned long long') is not allowed |  |
| `System\_DateTimeFormat.cpp` | reinterpret\_cast from 'intptr\_t' (aka 'long long') to 'uintptr\_t' (aka 'unsigned long long |  |
| `System\_DateTimeFormat.cpp` | reinterpret\_cast from 'intptr\_t' (aka 'long long') to 'uintptr\_t' (aka 'unsigned long long |  |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr\_array.cpp` | no viable overloaded '=' | candidate function (the implicit copy assignment operator) not viable: |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr\_array.cpp` | cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'Il2CppObje |  |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr.cpp` | no member named 'icall\_unbox\_uintptr' in namespace 'il2cpp::runtime'; did you mean 'icall\_ | 'icall\_unbox\_uint' declared here |
| `System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_System\_UIntPtr.cpp` | cannot initialize a parameter of type 'Il2CppObject **' with an rvalue of type 'intptr\_t * |  |
| `System\_Diagnostics\_Tracing\_UIntPtrArrayTypeInfo.cpp` | use of undeclared identifier 'System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_array\_\_cto | 'System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_int\_array\_\_ctor\_8cd |
| `System\_Diagnostics\_Tracing\_UIntPtrTypeInfo.cpp` | use of undeclared identifier 'System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_uintptr\_\_c | 'System\_Diagnostics\_Tracing\_TraceLoggingTypeInfo\_1\_int\_\_ctor\_8cd34122c |
| *52 more* | | |

### Pattern: `UIntPtr_vs_int32` (6 errors)

| Source file | Error message | Note |
|-------------|---------------|------|
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Runtime\_InteropServices\_Marshal.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Security\_Principal\_WindowsIdentity.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Security\_Principal\_WindowsIdentity.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |
| `System\_Security\_Principal\_WindowsIdentity.cpp` | cannot convert 'System\_UIntPtr' to 'int32\_t' (aka 'int') without a conversion operator |  |

### Pattern: `UIntPtr_param_to_int32` (1 errors)

| Source file | Error message | Note |
|-------------|---------------|------|
| `System\_Enum.cpp` | no matching function for call to 'System\_UIntPtr\_GetHashCode\_74f072e3e5e025f8' | candidate function not viable: no known conversion from 'int32\_t' (aka |


## Root Cause Analysis


### Pattern 1: uintptr_t vs int32_t mismatch
When UIntPtr is used as a return type or parameter, the transpiler declares it as `uintptr_t` (integer).
But when calling methods that expect `int32_t` (or vice versa), C++ cannot implicitly convert.

### Pattern 2: System_UIntPtr struct vs uintptr_t integer
Similar to IntPtr, UIntPtr has dual representation:
- `System_UIntPtr` struct (the managed value type)
- `uintptr_t` (the native integer representation)

The transpiler sometimes uses one where the other is expected.

### Pattern 3: reinterpret_cast failures
When trying to cast between System_UIntPtr and other types, C++ rejects the cast
because there is no implicit conversion operator.

### Pattern 4: UIntPtr in P/Invoke calls
P/Invoke methods often use `uintptr_t` directly, but the managed layer uses `System_UIntPtr`.
The mismatch occurs at the boundary.


## Fix Suggestions


1. **Unify UIntPtr representation**: Decide whether UIntPtr slots use `System_UIntPtr` struct or `uintptr_t` integer and be consistent.

2. **Add conversion helpers**: At P/Invoke boundaries, add explicit conversion between `System_UIntPtr` and `uintptr_t`.

3. **Match parameter types**: When a method declares `uintptr_t` parameter, ensure call sites pass `uintptr_t`, not `System_UIntPtr`.

4. **Return type consistency**: If a method returns `System_UIntPtr`, ensure the slot is typed `System_UIntPtr`, not `uintptr_t`.
