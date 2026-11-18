# System.Array is a real descriptor, and every array class reaches it

`(Array)o` and `o as Array` did not fail at run time -- the case failed to
COMPILE:

```
probe-arraybase.cs:10:45: error: 'System_Array_class' was not declared in this
scope; did you mean 'System_Uri_class'?
```

The transpiler emitted `&System_Array_class` for the cast target, and no such
symbol existed anywhere in the runtime. `il2cpp_metadata.h` declared the
`System.*` roots (Object, ValueType, String, the primitives, the exception
hierarchy) but not Array.

## Why the symbol was missing, and why it is not just a declaration

Declaring the symbol alone would have compiled and then failed every cast:
`il2cpp_is_instance` walks `obj->klass->baseClass` to nullptr, and no array
class had `System.Array` anywhere in its chain. The two fixed descriptors had
`baseClass = nullptr`, and a synthesized element class was wired to
`&g_objectArrayClass` for a reference element and to `nullptr` for a value
element. So `(Array)new int[]{...}` would have walked `[System.Int32` -> null
and answered false.

The fix is therefore two parts: define the descriptor, and make it the node the
array chains converge on.

```
int32[] / value-element[]  ->  System.Array  ->  System.Object
object[] / ref-element[]   ->  object[]  ->  System.Array  ->  System.Object
```

The reference chain keeps the `object[]` step net48 has (`string[]` IS-A
`object[]` IS-A `Array`); the value chain goes straight to `Array`, because a
value-element array is NOT assignable to `object[]` (that is the nb9 F1 rule
already encoded at `array.cpp:179`).

## The descriptor

`System_Array_class` in `metadata.cpp` is header-only and never allocated: no
transpiled program can `new Array()` (the ctor is protected assembly in
CoreLib). Its vtable is empty -- `Array`'s own members (`Length`, `Clone`,
`GetValue`) route through the dedicated array icalls on the `Il2CppArray`
header, not through virtual dispatch. It is registered in
`il2cpp_ensure_builtin_system_classes` alongside the other roots, so
`il2cpp_class_by_name("System.Array")` answers it too.

Inserting a node into the base chain is invisible to every existing reader:
each one (`il2cpp_is_instance`, `typecheck.cpp`, `typeext.cpp`,
`valuesemantics.cpp`, `attribute.cpp`, `marshalext3.cpp`) walks the chain to
nullptr and only ever compares identity, so an extra hop changes no answer
except the one that was wrong.

## Evidence

`tests/cases/probe-arraybase` (6 lines), byte-identical to the net8 oracle
(`sha=cc90ee3fde75360ccdfff21d80934ad2690e12a9c2e17819eca66a7829ed2ee3`):

```
cast:3
as:3
badthrow:ok
badnull:True
i8base:2:9000000000
refbase:2:q
```

`badthrow:ok` and `badnull:True` are the negative cases: a non-array operand
must throw `InvalidCastException` on the cast form and yield null on the `as`
form. `i8base` and `refbase` exercise `Length`/`GetValue`/`Clone` through the
base type, so the descriptor is not merely a cast target.

## Mutation evidence

| mutation | result |
|---|---|
| all three `&System_Array_class` base links replaced with `nullptr` | `i8base` throws `Unable to cast object to the target type`, exit 1 |
| restored | 6/6 lines match the oracle, exit 0 |

The mutation is load-bearing on the base chain, not on the declaration: the
symbol still exists, so the case still compiles -- it fails at run time exactly
where the chain is broken.

## Regression

All 20 GREEN contracts pass after the change, including the three array cases
that share this code path (`probe-arraycast`, `probe-arrayclone`,
`probe-arraycreate`).

## Still not covered

`(int[,])x` still fails to transpile with
`TypeSpec signature byte 0x14 not GenericInst(0x15) in M5` -- a
multi-dimensional TypeSpec decoding limit. `System.Array` now exists, so that
work is unblocked, but the TypeSpec decoder is a separate change.
