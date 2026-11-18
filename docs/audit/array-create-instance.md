# Array.CreateInstance: a public API with no mapping arm

`Array.CreateInstance` is a public, user-callable BCL method. All six net48
overloads were `publicapi unmapped` in the wiring audit, and a program calling
the simplest one failed to transpile:

```
il2cppc error: Program::Main: System_Array_CreateInstance_f1b7d7f6627f0191:
not wired in M5 (BCL owner has no mapping arm and is not defined in the input
assembly -- a phantom generated-name call would never link)
```

This is the class of defect `icall-gap-reachability-301.md` warns about at its
"Limits" section: a gap that is not user-callable can still be reached through a
public wrapper, and then the *wrapper* is the defect. Here the wrapper is the
public method itself.

## Why the reachability audit did not catch it

`reach_gaps_301.py` classifies the 172 FCall gaps. `Array.CreateInstance` is not
one of them: it is a managed CoreLib method, not an icall, so it has no row in
the gap set at all. The 68 reachable gaps are all interior machinery reached
*from* public methods like this one. The audit answers "is this gap reachable",
not "is this public method wired", so a public method with no arm is invisible
to it.

The wiring probe does see it -- `IcallWiringProbe` emits `publicapi` rows for
public methods on public types precisely so a missing wrapper can be spotted --
but nothing gated on those rows.

## The fix

The element type arrives as the simplified Type model: a full-name managed
string (`"System.Int32"`), the same shape `il2cpp_typeinfo_lookup` and
`il2cpp_activator_create_instance` already consume. So the runtime resolves the
name to a class descriptor and takes the element size from that descriptor:

```
reference element -> elemSize = sizeof(void*), isRefElement = true
value element     -> elemSize = klass->instanceSize, isRefElement = false
```

and allocates through the same `il2cpp_array_new` / `il2cpp_array_new_multi`
the transpiler emits for `new T[n]`.

Four runtime entry points, one per net48 shape:

| entry point | net48 overload |
|---|---|
| `il2cpp_array_create_instance` | `(Type, int)` |
| `il2cpp_array_create_instance_2d` | `(Type, int, int)` |
| `il2cpp_array_create_instance_3d` | `(Type, int, int, int)` |
| `il2cpp_array_create_instance_dims` | `(Type, int[])`, `(Type, int[], int[])` |

`(Type, long[])` maps to the dims entry point as well; the lengths are read as
int32 cells, which is what the array header stores.

## Evidence

`tests/cases/probe-arraycreate` prints `Length`, `GetType().Name`, then
`SetValue`/`GetValue` round-trip:

```
3
Int32[]
42
```

Byte-identical to the net48 oracle.

Mutation: renaming the `case "CreateInstance":` arm makes the transpile fail
again with the same `not wired in M5` error (exit 2); restoring it returns exit
0 and the three lines above.

## The element size is NOT the descriptor's instanceSize

The first version of this fix took the element size from `klass->instanceSize`.
That is wrong for every primitive: a primitive's descriptor is its BOX class,
so `System_Int32_class.instanceSize` is 16 (object header + an aligned 8-byte
slot), not 4. Every primitive array was allocated at 4x the right size.

Measured with `Buffer.ByteLength` (already wired, `il2cpp_buffer_byte_length`):

| array | net48 | instanceSize version | fixed |
|---|---|---|---|
| `int[3]` | 12 | 48 | 12 |
| `int[2,3]` | 24 | 96 | 24 |
| `int[2,3,4]` | 96 | 384 | 96 |
| `long[3]` | 24 | 48 | 24 |
| `double[2]` | 16 | 32 | 16 |

The fix maps the 14 primitive full names to their real widths and falls back to
`instanceSize` only for a translated user struct, where the descriptor's
instanceSize IS the element stride (TypeEmitter lays the C++ struct out at that
size, and the transpiler's own newarr path uses the same value --
`CodeGenerator.ArrayElementType`).

Mutation: changing `System.Int32` from 4 to 8 in that table makes the
ByteLength column read 24/48/192; restoring it returns 12/24/96.

## Difference from net48, recorded

An element type with no descriptor in this build throws
`NotSupportedException`; net48 raises `TypeLoadException`. The build cannot size
an element it has no descriptor for, so the failure is loud rather than a
silent wrong-size allocation. This is the same trade `il2cpp_activator_create_instance`
makes for an unregistered type (it throws `MissingMethodException` there).

## What this does not cover

`Array.CreateInstance` with a non-zero lower bound
(`(Type, int[], int[])` where the bounds are not all zero) is not modelled: the
array header has no lower-bound field, so the dims entry point ignores the
bounds array. net48 supports non-zero lower bounds. A program relying on them
would get a zero-based array. Not measured against an oracle; recorded as a
known gap rather than claimed correct.
