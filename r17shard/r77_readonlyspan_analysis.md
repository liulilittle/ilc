# r77 ReadOnlySpan/ReadOnlyMemory Error Analysis

Generated: 2026/09/06 09:23:18

## Summary

Analysis of r77 shard3 errors containing ReadOnlySpan, ReadOnlyMemory, or Span_1_ patterns.

- **Total ReadOnlySpan/Memory/Span errors in shard3:** 955 lines (including notes)
- **Unique error patterns:** ~40 distinct method/type mismatch patterns
- **Affected files:** All in System_Security_Cryptography_X509Certificates_* namespace

## Root Cause

The ReadOnlySpan<T>, ReadOnlyMemory<T>, and Span<T> structs are value types in the generated C++ code, but the transpiler is generating calls that pass Il2CppObject* or Il2CppObject** where a pointer to the struct (ReadOnlySpan_1_int*) is expected.

### Type Mismatch Pattern

`
Generated C++ call:
  ReadOnlySpan_1_int_get_Length_xxx(&loc0)  // expects ReadOnlySpan_1_int*

Actual argument:
  loc0 is declared as Il2CppObject*         // WRONG: should be ReadOnlySpan_1_int
  &loc0 is Il2CppObject**                   // WRONG: should be ReadOnlySpan_1_int*
`

## Error Categories

### Category 1: Method Call Argument Mismatch (Most Common)

Methods declared to take ReadOnlySpan_1_int* or ReadOnlyMemory_1_int* as first argument, but called with Il2CppObject**.

| Method Pattern | Count | Affected Types |
|---------------|-------|----------------|
| ReadOnlySpan<int>::get_Length | ~90 | ReadOnlySpan_1_int |
| ReadOnlyMemory<int>::get_Span | ~70 | ReadOnlyMemory_1_int |
| Span<int>::Clear | ~50 | Span_1_int |
| ReadOnlySpan<int>::Slice | ~45 | ReadOnlySpan_1_int |
| ReadOnlyMemory<int>::Slice | ~40 | ReadOnlyMemory_1_int |
| ReadOnlySpan<int>::Overlaps | ~35 | ReadOnlySpan_1_int |
| ReadOnlySpan<int>::ToArray | ~30 | ReadOnlySpan_1_int |
| Span<int>::_ctor | ~25 | Span_1_int |
| ReadOnlySpan<int>::get_Item | ~20 | ReadOnlySpan_1_int |
| ReadOnlySpan<int>::CopyTo | ~15 | ReadOnlySpan_1_int |
| ReadOnlyMemory<int>::_ctor | ~15 | ReadOnlyMemory_1_int |
| Span<int>::Slice | ~15 | Span_1_int |
| Span<int>::get_Length | ~10 | Span_1_int |
| Span<int>::op_Implicit | ~10 | Span_1_int |
| ReadOnlySpan<int>::get_IsEmpty | ~10 | ReadOnlySpan_1_int |
| ReadOnlyMemory<int>::get_Length | ~10 | ReadOnlyMemory_1_int |
| ReadOnlyMemory<int>::get_IsEmpty | ~5 | ReadOnlyMemory_1_int |
| ReadOnlyMemory<int>::CopyTo | ~5 | ReadOnlyMemory_1_int |
| ReadOnlyMemory<int>::op_Implicit | ~5 | ReadOnlyMemory_1_int |
| Span<int>::CopyTo | ~5 | Span_1_int |
| Span<int>::Fill | ~5 | Span_1_int |
| Span<int>::ToArray | ~5 | Span_1_int |

### Category 2: Missing Type Definitions

The following types are referenced but never generated:

| Missing Type | Impact |
|-------------|--------|
| System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_byte | High - used in multiple Asn files |
| System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_byte | Medium |
| System_Security_Cryptography_X509Certificates_Span_1_byte | Medium |

### Category 3: Nullable<ReadOnlyMemory> Mismatch

System_Nullable_1_*_ReadOnlyMemory_1_int_* methods expect Nullable_1_ReadOnlyMemory_1_int* but receive Il2CppObject* or Nullable_1_int*.

### Category 4: Cast Errors

Attempting to cast ReadOnlySpan_1_int or ReadOnlyMemory_1_int directly to Il2CppObject* (value types cannot be cast to object pointers).

## Top Affected Files

| File | Error Count | Primary Issue |
|------|-------------|---------------|
| PasswordBasedEncryption.cpp | ~120 | ReadOnlySpan/Memory method args |
| AsnDecoder.cpp | ~90 | ReadOnlySpan method args |
| Span_1_int.cpp | ~70 | Span method args |
| AsnReader.cpp | ~65 | ReadOnlyMemory method args |
| ReadOnlySpan_1_int.cpp | ~60 | Self-referential (missing struct definition) |
| PfxAsn.cpp | ~50 | ReadOnlySpan method args |
| Pkcs12Kdf.cpp | ~45 | ReadOnlyMemory method args |
| ReadOnlyMemory_1_int.cpp | ~40 | Self-referential |
| AsnValueReader.cpp | ~35 | ReadOnlySpan method args |
| AlgorithmIdentifierAsn.cpp | ~30 | ReadOnlySpan + Nullable<ReadOnlyMemory> |

## Structural Analysis

The errors form a consistent pattern:

1. **Local declaration mismatch**: Locals that should be ReadOnlySpan_1_int are declared as Il2CppObject*
2. **Address-of mismatch**: &locN produces Il2CppObject** instead of ReadOnlySpan_1_int*
3. **Missing struct definitions**: ReadOnlyMemory_1_byte and ReadOnlySpan_1_byte are not generated
4. **Nullable wrapper mismatch**: Nullable<ReadOnlyMemory<int>> methods get wrong pointer types

## Recommended Fix Strategy

1. **Type emission**: Ensure ReadOnlySpan_1_int, ReadOnlyMemory_1_int, Span_1_int (and _byte variants) are emitted as proper C++ structs
2. **Local kind tracking**: The locKind dictionary must track these value types correctly (not as ef)
3. **Method call bridging**: When calling Span/Memory methods, pass &locN where locN is the actual struct type, not Il2CppObject*
4. **Generic type registration**: Register ReadOnlyMemory_1_byte and ReadOnlySpan_1_byte in the type system
