# Array.Clone: a public API with no mapping arm

`Array.Clone` is a public, user-callable BCL method. It was `publicapi unmapped`
in the wiring audit, and a program calling it failed to transpile:

```
il2cppc warning: method fold failed -> skipped (Program::Main:
System.Array::Clone: not wired in M5 (BCL owner has no mapping arm ...))
```

This is the second instance of the class `array-create-instance.md` records: a
public method with no arm is invisible to the reachability audit, because that
audit classifies the FCall *gap* set and `Array.Clone` is a managed CoreLib
method, not an icall.

## The fix

`il2cpp_array_clone` allocates a new array of the same shape (rank, per-dim
sizes, element size, element kind, reference-ness) through the same
`il2cpp_array_new_multi` the transpiler emits for `new T[n]`, then copies the
element payload verbatim:

```
reference element -> the SLOTS are copied, so the clone holds the same objects
value element     -> the bytes are copied
```

That is net48's shallow-clone contract. The result keeps the source's array
class, so `GetType().Name` reports the same `"T[]"`.

`elemKind` is copied explicitly after allocation: `il2cpp_array_new_multi`
recomputes it from the element descriptor, which loses the primitive kind for a
descriptor-less array (a `long[]` and a `double[]` cell are both 8 bytes).

A null receiver throws `NullReferenceException` (net48:
`((Array)null).Clone()` throws NRE).

## Evidence

`tests/cases/probe-arrayclone`:

```
i4:3:2
ref:2:x
indep:1:99
shallow:p:z
```

Byte-identical to the net48 oracle. The last two lines are the contract, not
decoration: `indep` proves the clone is a separate allocation (mutating it does
not touch the source), and `shallow` proves a reference-element clone shares the
element slots (assigning through the clone is visible in the source).

Mutation: removing the `memcpy` makes the payload read as zero
(`i4:3:0`, `ref:2:`); restoring it returns the four lines above.

## What this does not cover

- A multi-dimensional clone is not exercised: `(int[,])x` fails to transpile
  with `TypeSpec signature byte 0x14 not GenericInst(0x15) in M5`, a
  pre-existing TypeSpec decoding limit unrelated to Clone.
- `(long[])x` / `(double[])x` casts fail at run time with
  `Unable to cast object to the target type` -- the array class name for those
  element types does not match what `il2cpp_is_instance` looks up. Also
  pre-existing and unrelated to Clone; the case therefore uses `int[]` and
  `string[]`, which do work.
- `as Array` and `(Array)x` emit `&System_Array_class`, which is not declared
  anywhere. Pre-existing; the case avoids it by casting to the concrete array
  type.

All three are recorded here rather than fixed, because each is a separate
defect in the cast/TypeSpec path, not in Clone.
