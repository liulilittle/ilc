# Bodiless public wrappers: the shape that silently emits undefined symbols

## The shape

A method with an IL body needs no MethodMap arm -- the body itself transpiles.
A method with **no IL body** has no other way to acquire one than an arm, so
"bodiless + unmapped" is a real hole. "Unmapped" alone says nothing: most
managed methods are unmapped and perfectly fine.

This distinction is what makes the join trustworthy. The round-40 join used
`unmapped` alone and produced 15368 rows, of which almost all were ordinary
managed methods.

## Why the phantom guard does not catch them

`MethodRefResolver.BclResolve` sets `IsFictionalBclFallback` on a generated-name
fallback for an external BCL owner (`MethodRefResolver.cs:1482`), and
`StackFold` rejects that at fold time via `RequireResolvable`
(`StackFold.cs:4504`). But a bodiless wrapper is resolved through
`MethodRefResolver.Resolve`, which never sets the flag, so the guard sees
`false` and stays silent. The generated C++ then calls
`System_Xxx_<hash>` -- a symbol nothing defines -- and the failure surfaces only
at cmake time.

## Measurement

`tools/audit/BodilessPublicProbe` enumerates public/family methods with
`RelativeVirtualAddress == 0`, excluding P/Invoke (legitimately bodiless) and
abstract/interface members (bodiless by design):

```
bodiless_methods=677
bodiless_public_nonpinvoke=366
```

`tools/audit/bodiless_holes.py` joins those against the wiring probe:

| status | count |
|---|---|
| unmapped | 237 |
| resolved | 101 |
| loud | 2 |
| NOT-PROBED | 26 |

Of the 237 unmapped, **173 are delegate `Invoke`/`BeginInvoke`/`EndInvoke`** and
a further 58 are delegate `.ctor` -- the runtime supplies those indirectly, so
they are not holes. That leaves **6**, all `System.Threading.Interlocked`:

```
CompareExchange  Int32 (Int32&, Int32, Int32)
CompareExchange  Int64 (Int64&, Int64, Int64)
CompareExchange  Object (Object&, Object, Object)
Exchange         Int32 (Int32&, Int32)
Exchange         Int64 (Int64&, Int64)
Exchange         Object (Object&, Object)
```

The probe reports these as unmapped because it passes no receiver type; the
emitter resolves the signed forms through the `icall` rows. The genuinely
missing ones are the **unsigned** widths and the .NET 9 `And`/`Or` pair, which
have no arm at all.

## The defect, confirmed by building

A program using `Interlocked.Exchange(ref uint, uint)` and friends failed to
compile:

```
error: 'System_Threading_Interlocked_Exchange_2bd904345a97bd3e' was not declared in this scope
error: 'System_Threading_Interlocked_CompareExchange_53fe37d4c6691822' was not declared in this scope
error: 'System_Threading_Interlocked_Increment_19bcaf8f6779ea37' was not declared in this scope
error: 'System_Threading_Interlocked_Add_8df64c5abe849e70' was not declared in this scope
```

## Fix

- `il2cpp_interlocked_and/and64/or/and64` added to the runtime. Neither the
  Windows `Interlocked*` family nor the `__atomic` builtins expose AND/OR, so
  each is a CAS retry loop.
- `MethodMap.Interlocked` now accepts the unsigned encodings (`0x09` u4, `0x0B`
  u8, `0x19` UIntPtr) on the same arms as their signed counterparts -- identical
  width, identical bits -- and gained `And`/`Or` arms.

## Verification

`tests/cases/probe-interlocked-widths` (net9.0, since `And`/`Or` do not exist in
net8) covers 10 assertions across signed/unsigned 32/64 plus `And`/`Or`. ILC
output matches the net9 oracle line for line, `RUN=0`.

Mutation: forcing the `And` arm to emit `or64` changes the output from
`5 8 9 8 7 8 9` to `5 8 9 8 7 13 9`. Restoring returns it. A first attempt at
this mutation edited `interlocked.cpp` instead and produced no change at all --
the gen build had already linked the runtime, so the edit never reached the
binary. That is why the mutation is applied at the mapping layer, which is the
decision under test.

## Limits

- The probe cannot see whether an arm exists; it only reports what
  `BclResolve` answers with no receiver type. The verdict for each candidate
  came from transpiling a real call.
- 26 bodiless public methods were NOT-PROBED (absent from the wiring TSV). They
  are unmeasured, not cleared.
- The `Resolve`-path phantom gap itself is still unfixed; this document records
  the shape and the one family that was found through it.
