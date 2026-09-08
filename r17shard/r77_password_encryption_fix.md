# r77 PasswordBasedEncryption NMF Error Analysis

**Date**: 2026-09-06  
**Scope**: `System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp` in r77 shard3  
**Total errors**: 172 error lines in shard3 (all PasswordBasedEncryption-related errors are in shard3; shard1/shard2 have 0)

---

## 1. Error Summary

| Category | Count | % |
|----------|-------|---|
| `Span_1_int` method signature mismatch (value vs ref) | ~55 | 32% |
| `Nullable_1_ReadOnlyMemory_1_byte` undeclared | ~28 | 16% |
| `ReadOnlySpan_1_int` method signature mismatch | ~25 | 15% |
| `ReadOnlyMemory_1_int` method signature mismatch | ~20 | 12% |
| `ArraySegment_1_int` method signature mismatch | ~14 | 8% |
| Unknown type names (`ReadOnlySpan_1_c_`, `ReadOnlySpan_1_u1_`, `Nullable_1_i_`) | ~12 | 7% |
| `Il2CppObject*` vs `Span_1_int` assignment/cast errors | ~10 | 6% |
| `no viable overloaded '='` | ~8 | 4% |
| **Total** | **~172** | **100%** |

---

## 2. Root Cause Analysis

### 2.1 Root Cause A: Generic Instantiation Type Mismatch (`int` vs `byte`/`u1`)

**The dominant pattern (60%+ of errors)**

The transpiler generates `Span_1_int` / `ReadOnlySpan_1_int` / `ReadOnlyMemory_1_int` / `Nullable_1_ReadOnlyMemory_1_int` declarations and calls, but the actual IL signature uses `byte` (`u1` in IL encoding) for the generic type argument.

**Evidence from generated.h (line 57)**:
```cpp
{ "Pbes2Decrypt", "i(V:System.Nullable`1<V:System.Security.Cryptography.X509Certificates.ReadOnlyMemory`1<u1>>,V:System.Security.Cryptography.X509Certificates.ReadOnlySpan`1<c>,V:System.Security.Cryptography.X509Certificates.ReadOnlySpan`1<u1>,V:System.Security.Cryptography.X509Certificates.ReadOnlySpan`1<u1>,V:System.Security.Cryptography.X509Certificates.Span`1<u1>)", ... }
```

The metadata signature clearly shows:
- `ReadOnlyMemory`1<u1>` (byte)
- `ReadOnlySpan`1<c>` (char)
- `ReadOnlySpan`1<u1>` (byte)
- `Span`1<u1>` (byte)

But the generated C++ calls use `ReadOnlyMemory_1_int`, `ReadOnlySpan_1_int`, `Span_1_int` everywhere.

**Example error chain (line 523)**:
```cpp
// Generated call:
System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_HasValue_495fb3b18c2b35b9((&arg0))
// Error: use of undeclared identifier '..._ReadOnlyMemory_1_byte_get_HasValue...'; did you mean '..._ReadOnlyMemory_1_int_get_HasValue...'?
```

The compiler suggests the `int` version exists but `byte` version does not. This confirms: **the transpiler instantiates generic mathods with `int` where the IL requires `byte`**.

**Affected methods**:
- `Nullable_1_ReadOnlyMemory_1_byte_get_HasValue` (7 occurrences)
- `Nullable_1_ReadOnlyMemory_1_byte_get_Value` (7 occurrences)
- `ReadOnlySpan_1_int_CopyTo` (called with `Span_1_int` arg, 6 occurrences)
- `Span_1_int_Slice` (8 occurrences)
- `Span_1_int_op_Implicit_47310c4b54bdf3fe` (8 occurrences)
- `Span_1_int_Clear` (20 occurrences)
- `Span_1_int__ctor_7cc9132536000361` (10 occurrences)

### 2.2 Root Cause B: Value Type vs Reference Type Confirmation

`Span_1_int`, `ReadOnlySpan_1_int`, `ReadOnlyMemory_1_int` are **value types** (C++ structs, no object header), confirmed by generated.h:
```cpp
struct System_Security_Cryptography_X509Certificates_Span_1_int {
    // value type: no object header (instances are plain C++ values)
    Il2CppObject* _Segment;
};
```

But the emitter generates code that treats them as `Il2CppObject*`:
```cpp
// Line 456: loc1 is Il2CppObject*, but Span_1_int_op_Implicit returns Il2CppObject*
loc1 = reinterpret_cast<Il2CppObject*>(System_Security_Cryptography_X509Certificates_Span_1_int_op_Implicit_47310c4b54bdf3fe(loc0));
// Then &loc1 is Il2CppObject**, but Span_1_int_Clear expects Span_1_int*
System_Security_Cryptography_X509Certificates_Span_1_int_Clear_e0400568017a2a6f((&inl6));
// Error: cannot initialize parameter of type Span_1_int* with Il2CppObject**
```

**The fix must ensure**: locals holding Span/ReadOnlySpan values are declared as the struct type, not `Il2CppObject*`.

### 2.3 Root Cause C: Missing Type Definitions

The following types are **referenced but never defined** in generated.h:

| Missing Type | Declared in generated.h? |
|-------------|------------------------|
| `System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte` | NO |
| `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_byte` | NO |
| `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1` | NO |
| `System_Security_Cryptography_X509Certificates_Span_1_byte` | NO |
| `System_Security_Cryptography_X509Certificates_Span_1_u1` | NO |
| `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_c` | NO |
| `System_Nullable_1_i_` | NO |

Only `System_Nullable_1_..._ReadOnlyMemory_1_int` and `..._ReadOnlyMemory_1_int` are defined (generated.h lines 461, 832, 9387, 15409).

### 2.4 Root Cause D: `op_Implicit` Return Type Confusion

`Span_1_int_op_Implicit_65c7949156fa5b48(Il2CppObject*)` returns `Il2CppObject*` (a boxed Span). But the code assigns the result to a local and then takes its address:
```cpp
loc5 = reinterpret_cast<Il2CppObject*>(System_Security_Cryptography_X509Certificates_Span_1_int_op_Implicit_65c7949156fa5b48(t0));
// ...
System_Security_Cryptography_X509Certificates_PasswordBasedEncryption_OpenCipher_2e872b53e5e88e6a((&(loc0))->EncryptionScheme, loc1, (&loc5)));
// Error: OpenCipher expects Span_1_int* but gets Il2CppObject**
```

The `op_Implicit` from array to Span should return the struct, not `Il2CppObject*`.

---

## 3. Error Pattern Catalog

### Pattern 1: `Span_1_int_Clear` on `Il2CppObject*` local (20 occurrences)

```cpp
// Generated (WRONG):
inl6 = t0;  // t0 is Il2CppObject*
System_Security_Cryptography_X509Certificates_Span_1_int_Clear_e0400568017a2a6f((&inl6));
// Error: no matching function (expects Span_1_int*, got Il2CppObject**)

// Correct:
System_Security_Cryptography_X509Certificates_Span_1_int inl6_struct = <struct>;
System_Security_Cryptography_X509Certificates_Span_1_int_Clear_e0400568017a2a6f((&inl6_struct));
```

### Pattern 2: `Nullable_1_ReadOnlyMemory_1_byte` undeclared (14 occurrences)

```cpp
// Generated (WRONG):
System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte_get_HasValue_495fb3b18c2b35b9((&arg0))
// Error: undeclared identifier

// Fix: emit the byte variant of Nullable_1_ReadOnlyMemory in generated.h
```

### Pattern 3: `Span_1_int_Slice` / `op_Implicit` type chain (16 occurrences)

```cpp
// Generated (WRONG):
loc0 = reinterpret_cast<Il2CppObject*>(System_Security_Cryptography_X509Certificates_Span_1_int_Slice_8d80c0d429d2b07a((&loc0), 0LL, loc7));
// Error: (&loc0) is Il2CppObject**, not Span_1_int*

// Fix: loc0 must be Span_1_int, not Il2CppObject*
```

### Pattern 4: `ArraySegment_1_int_get_*` on `Il2CppObject*` (14 occurrences)

```cpp
// Generated (WRONG):
inl7 = reinterpret_cast<Il2CppObject*>(System_ArraySegment_1_int_get_Array_aaceee58dbd184e0((&inl6)));
// Error: no matching function (expects ArraySegment_1_int*, got Il2CppObject**)
```

### Pattern 5: Unknown type names (12 occurrences)

```cpp
// Line 621: System_Nullable_1_i_ loc3;  -- unknown type name
// Line 1101: System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_c_ inl6;  -- unknown
// Line 1389: System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_u1_ inl1;  -- unknown
```

---

## 4. Fix Strategy

### Phase 1: Generic Instantiation Fix (addresses 60% of errors)

**File**: `src/il2cppc/Emit/TypeEmitter.cs` or `MetadataLoader.cs`

The transpiler must decode the actual generic type argument from the IL signature (`u1` = byte, `c` = char, `i4` = int32) and instantiate the correct C++ type.

**Current behavior**: All `Span<T>`, `ReadOnlySpan<T>`, `ReadOnlyMemory<T>`, `Nullable<ReadOnlyMemory<T>>` are emitted as `T = int32`.

**Required behavior**: Decode `T` from the signature and emit:
- `u1` → `Span_1_byte` / `ReadOnlySpan_1_byte` / `ReadOnlyMemory_1_byte`
- `c` → `Span_1_char` / `ReadOnlySpan_1_char`
- `i4` → `Span_1_int` / `ReadOnlySpan_1_int` / `ReadOnlyMemory_1_int`

### Phase 2: Value Type Local Declaration (addresses 25% of errors)

**File**: `src/il2cppc/Emit/CodeGenerator.cs`

When a local's kind is tracked as a Span/ReadOnlySpan/ReadOnlyMemory type, declare it as the struct type, not `Il2CppObject*`.

**Current code** (around line 1372 in CodeGenerator.cs):
```csharp
var locKind = new Dictionary<string, string>(); // name -> "ref" | "val" | <type>
```

**Fix**: Add cases for Span/ReadOnlyMemory types in `ExprKind` resolution and local declaration. When the type is `Span_1_int`, declare `System_Security_Cryptography_X509Certificates_Span_1_int locN;` instead of `Il2CppObject* locN = nullptr;`.

### Phase 3: Missing Type Definitions (addresses 10% of errors)

**File**: `src/il2cppc/Emit/TypeEmitter.cs` (generated.h emission)

Emit struct definitions and method declarations for:
- `System_Nullable_1_System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte`
- `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_byte`
- `System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_char`
- `System_Security_Cryptography_X509Certificates_Span_1_byte`
- `System_Security_Cryptography_X509Certificates_Span_1_char`
- `System_Nullable_1_i_` (Nullable<int>)

### Phase 4: `op_Implicit` Return Type Fix (addresses 5% of errors)

**File**: `src/il2cppc/Emit/CodeGenerator.cs`

`Span_1_int_op_Implicit(Il2CppObject* array)` should return `Span_1_int` (the struct), not `Il2CppObject*`. The current emission wraps it in `reinterpret_cast<Il2CppObject*>` which is wrong for a value type.

---

## 5. Verification Plan

1. After Phase 1-4 fixes, regenerate r77
2. Count PasswordBasedEncryption errors: target 0
3. Verify no regressions in other X509Certificates files (AsnDecoder, PfxAsn, etc.)
4. Check that `ReadOnlySpan_1_byte`, `Span_1_byte`, etc. are properly emitted in generated.h

---

## 6. Key File Locations

| File | Lines | Role |
|------|-------|------|
| `src/il2cppc/Emit/CodeGenerator.cs` | 1372, 3078-3207, 3295-3778, 4042-8143 | Local declaration, statement emission, expression emission |
| `src/il2cppc/Emit/TypeEmitter.cs` | unknown | Type struct emission, generic instantiation |
| `src/il2cppc/Emit/MethodRefResolver.cs` | 121-180, 1496-1511 | ResolvedMethod record, I() helper |
| `src/il2cppc/MetadataLoader.cs` | 71, 94 | Type loading, generic type resolution |
| `src/il2cppc/Ir/StackFold.cs` | 3491-3660 | newobj folding, objTypes tracking |
| `out/mscorlib_gen_r77/include/internal/generated.h` | 461, 830-832, 9387, 15394-15413, 76221-76256 | Type declarations, method declarations |
| `out/mscorlib_gen_r77/src/System_Security_Cryptography_X509Certificates_PasswordBasedEncryption.cpp` | 1-1541 | Generated file with all errors |

---

## 7. Conclusion

The 172 PasswordBasedEncryption errors stem from **a single systemic issue**: the transpiler does not correctly decode generic type arguments from IL signatures for `Span<T>`, `ReadOnlySpan<T>`, `ReadOnlyMemory<T>`, and `Nullable<ReadOnlyMemory<T>>`. It defaults to `int` for all of them, producing `Span_1_int` where `Span_1_byte` or `Span_1_char` is required. Combined with the value-type-as-reference-type confusion in local declarations, this creates the full error cascade.

The fix requires changes in:
1. Generic type argument resolution (use actual T from signature)
2. Local declaration (struct type for value types)
3. Type emission (emit byte/char variants)
4. op_Implicit emission (return struct, not Il2CppObject*)
