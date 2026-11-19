# Volatile.Read/Write over every width net48 declares

Status: GREEN (`p-nb4-gc`, 26 lines, byte-exact against the net48 oracle).

## What was missing

`Thread.VolatileRead` / `Thread.VolatileWrite` had mapping arms, but only for
two of the thirteen overloads each:

```
case "VolatileRead":
    if (count == 1 && ps[0] == 0x08) ... il2cpp_volatile_read_i4
    if (count == 1 && ps[0] == 0x0A) ... il2cpp_volatile_read_i8
```

`ps[0] == 0x08` is int32 and `0x0A` is int64. Every other width fell through
to `return null`, and the transpiler then failed the whole method:

```
warning: method fold failed -> skipped
  (Cases.PNb4Gc::Main: generic instantiation of
   System.Threading.Volatile.Write has no BCL mapping in M5)
```

That is a whole-method loss, not a per-call one: `Main` produced no code at
all, so the probe printed nothing and the diff reported 26 missing records.

## What net48 actually declares

Measured from the wiring probe over `v4.8/mscorlib.dll` (not assumed):

| family | overloads | widths |
|---|---|---|
| `Thread.VolatileRead` | 13 | byte, sbyte, short, ushort, int, uint, long, ulong, float, double, IntPtr, UIntPtr, object |
| `Thread.VolatileWrite` | 13 | same, each `(T&, T)` |
| `Volatile.Read` | 14 | the same 13 plus generic `T` (`!!0&`) |
| `Volatile.Write` | 14 | the same 13 plus generic `T` |

`bool`, `char` and `string` are NOT overloads on the Thread family: bool and
char fold into the byte overload, and string into object. The probe's first
draft called all three and the C# compiler rejected them
(`CS1503: cannot convert 'ref bool' to 'ref byte'`), which is how that was
confirmed rather than guessed.

## The slot-width model (the load-bearing constraint)

The first implementation attempt gave the runtime entries narrow signatures:

```cpp
int8_t il2cpp_volatile_read_i1(int8_t* location);
```

and the generated call was

```cpp
loc28 = il2cpp::il2cpp_volatile_read_i1((&loc4));
```

with `int32_t loc4;`. The M-stage models **every** scalar local as an
`int32_t` slot (or `int64_t` for the 8-byte family), so the address of a byte
local is an `int32_t*`, not an `int8_t*`:

```
error: cannot convert 'int32_t*' {aka 'int*'} to 'int8_t*'
```

The width is therefore a property of the **access**, not of the pointer. The
entries group by slot width and reinterpret inside:

| entry | pointer it takes | why |
|---|---|---|
| `i1` | `int32_t*` | byte/sbyte/bool locals are int32 slots |
| `i2` | `int32_t*` | short/ushort/char locals are int32 slots |
| `i4` | `int32_t*` | int/uint/single |
| `i8` | `int64_t*` | long/ulong/double |
| `ip` | `intptr_t*` | IntPtr local is declared `intptr_t` |
| `up` | `uintptr_t*` | UIntPtr local is declared `uintptr_t` -- **not** interchangeable with `ip` |
| `float` / `double` | `double*` | every floating local is a double slot |
| `obj` | `Il2CppObject**` | every reference encoding |

`ip` vs `up` is a real split, not a style choice: the transpiler declares the
IntPtr local `intptr_t` and the UIntPtr local `uintptr_t`, and an `intptr_t*`
parameter rejects the `uintptr_t*` address at compile time. A single
pointer-width entry could not serve both.

### Float and double: bit-pattern carrier

`__atomic_load_n` does not accept float or double pointers on this toolchain:

```
error: operand type 'float*' is incompatible with argument 1 of '__atomic_load_n'
```

and the generic `__atomic_load(p, &r, size, model)` is a hard error -- this
g++ (16.1.0) exposes the 3-argument form only, whose default memory model
raises `-Winvalid-memory-model`. The working form is the standard bit-pattern
carrier: an IEEE float is 4 bytes and a double 8, so the atomic access runs on
the **same-width integer** and the bits are `memcpy`'d across.

```cpp
float il2cpp_volatile_read_float(double* location)
{
    int32_t bits = __atomic_load_n(reinterpret_cast<int32_t*>(location), __ATOMIC_SEQ_CST);
    float f;
    __builtin_memcpy(&f, &bits, sizeof(f));
    return f;
}
```

This is exact -- no numeric conversion, no aliasing UB -- and is the same
technique the existing `Interlocked` float/double exchange uses.

## Write barrier on the reference store

`il2cpp_volatile_write_obj` does an atomic store and then
`il2cpp_write_barrier`, because a young reference written into an old object
must mark the card. This is the contract every other reference-field store in
the runtime follows; an atomic store without the barrier would be a GC hole
that only shows up under a generational collection.

## Casts

`CodeGenerator.cs` gained two scalar argument casts:

```
"i1" => static_cast<int8_t>(...)
"i2" => static_cast<int16_t>(...)
```

These are **not** the P/Invoke array element names (`i1`/`u1` in
`PinvokeArrayElementCpp` mean something else). They exist because the narrow
runtime entry takes an `int8_t`/`int16_t` value and the caller's expression is
the int32 carrier the assignment used.

`"f"` is deliberately not used: there is no such cast code, and float rides the
`double` slot, so the float write arm passes `"d"` and lets the `float`
parameter narrow.

## Evidence

```
[BASH] net48 oracle, 26 lines:
       [gc.reg]=registered  [gc.cancel]=called  [thr.vr]=1|42  [vol.rw]=3|99
       [b]=9 [sb]=-9 [s]=456 [us]=456 [ui]=4000000000
       [ul]=8000000000000000000 [f]=2.5 [d]=2.5 [ip]=2 [up]=2 [ref]=q
       [vol.b]=2 [vol.sb]=-2 [vol.s]=2 [vol.us]=2 [vol.ui]=2 [vol.ul]=2
       [vol.f]=2.5 [vol.d]=2.5 [vol.ip]=2 [vol.up]=2 [vol.ref]=q
[BASH] difftest -Case p-nb4-gc:
       case=p-nb4-gc compiler=mingw ok=True
       oracle  code=0 out=328B sha=49539f4e1240adbe13687846c4cd2e22e4db9b142d09e3718113dc9afb00dd25
       runtime code=0 out=328B sha=49539f4e1240adbe13687846c4cd2e22e4db9b142d09e3718113dc9afb00dd25
       firstDiff stdout=-1 stderr=-1 records(oracle=26 runtime=26) diff=0
[BASH] mutation -- write_i1 stores value+1:
       [b]=10 [sb]=-8 [vol.b]=3 [vol.sb]=-1   (RED: four rows diverge)
[BASH] restore -> [b]=9 [sb]=-9 [vol.b]=2 [vol.sb]=-2  (GREEN)
[BASH] g++ -std=c++17 -c: interlocked / arrayext / arraysort / array / arrayextra /
       readonlycollection -> 0 errors, 0 warnings (interlocked)
[BASH] dotnet build src/il2cppc/il2cppc.csproj -c Release -> 0 warnings, 0 errors
[BASH] publicapi_unmapped_gate (net48 mscorlib input):
       unmapped baseline=486 current=486 closed=0 new=0
       resolved baseline=1485 current=1485 regressed=0 gained=0
       (was unmapped 510 / resolved 1461 -- 24 Volatile rows moved across)
[BASH] green-gate: PASS (28 GREEN contract case(s) still byte-exact)
[BASH] check_decl_symmetry -> REAL GAP: none; ABI checked=5711 match=5711
       mismatch=0 skipped=0
```

## Scope

Covers the concrete widths net48 declares. The generic `Volatile.Read<T>` /
`Write<T>` instantiation is dispatched to the object slot (the reference
encoding is correct for every reference `T`), and a generic value-type `T`
resolves through the same MVAR arm the other generic domains use. A
reference-typed `Volatile.Write` over a non-object `T` is not modelled.