# Array casts: the element class must be synthesized, not just looked up

`(long[])x` failed at run time with `Unable to cast object to the target type`
whenever no `long[]` had been allocated yet. `(int[])`, `(string[])` and
`(short[])` had their own, different reasons to fail. All three are fixed.

## The cast path

`SzArrayArrayClassCpp` (CodeGenerator) turns an `isinst`/`castclass` on a
SZARRAY TypeSpec into the array-class lookup:

```
il2cpp_is_instance(obj, il2cpp_array_class_for_element_name("<element>"))
```

Before the fix it emitted a plain `il2cpp_class_by_name("[<element>")`.

## Three separate defects fell out of that one line

**1. A synthesized class is registered on first allocation.** `ArrayClassForElement`
synthesizes `[System.Int64` and registers it, but only when an array of that
element is allocated. A plain registry lookup therefore answers `nullptr` for
`[System.Int64` until some `long[]` exists, and `(long[])x` fails on a pointer
mismatch. The fix resolves the ELEMENT descriptor and goes through the same pool
the allocator uses, so allocation order no longer matters.

**2. `il2cpp_type_is_value_type` cannot answer for a primitive.** It walks the
base chain for `System.ValueType`, and `System_Int64_class.baseClass` is
`System.Object` (metadata.cpp models a boxed primitive as IS-A object). So it
reports 0 for Int64 and the first version of the fix asked for the REFERENCE
array class while `il2cpp_array_new` returns the VALUE one. Measured: `i8`, `f8`
and `ref` passed while `i4` alone threw. The primitive width table is the
authoritative value-type answer here.

**3. `int[]` and the narrow primitives keep a FIXED descriptor.**
`il2cpp_array_new` returns `g_int32ArrayClass` when the element class is null or
Int32 (`!isRefElement && elemClass != nullptr` guard's else branch). The
transpiler passes a descriptor only for int/long/single/double/uint
(`valueElemClass`), so byte/sbyte/short/ushort/char/bool all land on
`g_int32ArrayClass` too. Synthesizing a separate `[System.Int16` made `(short[])x`
fail. The fix answers the same fixed class for all of these.

## Array.Clone needed the same treatment

`il2cpp_array_new` records `arr->elemClass` only for REFERENCE arrays, so a
value array's clone passed `nullptr` through and was allocated as the int32 array
class -- `((long[])x.Clone())` then failed to cast. `il2cpp_array_clone` now
recovers the element descriptor from the source's own class name
(`"[System.Int64"` -> `"System.Int64"`), which `il2cpp_array_new` stamped from
the descriptor the transpiler passed.

## Evidence

`tests/cases/probe-arraycast` (7 lines) and the extended
`tests/cases/probe-arrayclone` (6 lines), both byte-identical to the net48
oracle:

```
i8:2:9000000000     i4:3:2      i4:3:2
f8:2:1.5            i2:2:7      ref:2:x
ref:2:x             u1:3:7      indep:1:99
                                shallow:p:z
                    badthrow:ok i8:2:9000000000:7000000000
                                f8:2:1.5:2.5
```

`badthrow:ok` is the negative case: casting `int[]` to `long[]` must throw
`InvalidCastException`, not silently produce a wrong array.

## Mutation evidence

| mutation | result |
|---|---|
| `il2cpp_array_class_for_element_name` returns nullptr without synthesizing | `(long[])x` throws, exit 1 |
| the narrow-primitive arm returns nullptr | `(short[])x` throws, exit 1 |
| restored | 7/7 and 6/6 lines match the oracle, exit 0 |

## Still not covered

`(int[,])x` still fails to transpile with
`TypeSpec signature byte 0x14 not GenericInst(0x15) in M5` -- a separate
TypeSpec (multi-dimensional array) decoding limit, unrelated to the element-class
lookup. And `as Array` / `(Array)x` still emit an undeclared `System_Array_class`;
that is a missing descriptor, not an element-class bug.
