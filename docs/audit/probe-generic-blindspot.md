# The wiring probe was blind to generic methods

`IcallWiringProbe` reported `System.Array::Empty`, `Exists`, `Find`, `FindAll`,
`FindIndex`, `FindLast`, `FindLastIndex`, `ForEach`, `Resize`, `TrueForAll` and
`System.Threading.Interlocked::CompareExchange`/`Exchange` as `unmapped`, even
though every one of them has a mapping arm and every one of them works in a
transpiled program.

## Why

The probe resolves a method by calling `MethodRefResolver.BclResolve` with the
method's DEFINITION signature and `genArgs: null`:

```
var hintless = Call(md, owner, name, m.Signature, null);
var hinted   = Call(md, owner, name, m.Signature, owner);
```

A generic method's mapping arm dispatches on the generic ARGUMENT bytes:

```
case "Empty":
    if (genArgs != null)
        return genArgs[0] switch
        {
            0x08 => I("il2cpp::runtime::icall_array_empty_int32", ...),
            0x0E => I("il2cpp::runtime::icall_array_empty_string", ...),
            ...
        };
```

With `genArgs == null` the arm falls through and the probe records `unmapped`.
The real transpiler always has the closed `MethodSpec`, so the member works --
the probe was measuring its own blind spot, not the transpiler's coverage.

## The fix

When both the hintless and hinted resolutions fail AND the method declares
generic parameters, retry with a placeholder instantiation (every argument byte
`0x08`, i.e. `int32`). The arm only needs SOME argument byte to pick a branch,
and a resolved symbol from any instantiation proves the arm exists.

This is a probe-side correction. The transpiler is unchanged: it never sees an
open generic definition.

## Measured effect

| metric | before | after |
|---|---|---|
| `publicapi:resolved` | 2553 | 2574 (+21) |
| `publicapi:unmapped` | 9326 | 9320 |
| `publicapi:loud` | 527 | 512 |
| `System.Array` unmapped rows | 17 | 2 |

The two remaining `System.Array` rows are real: `AsReadOnly` (needs
`ReadOnlyCollection<T>`) and `GetEnumerator` (needs an enumerator object).

## Baselines re-pinned

The gate compares against two pinned sets, so the correction moved 7 rows from
`unmapped` to `resolved` and gained 21 resolved rows. Both baselines were
regenerated from the corrected probe:

```
unmapped: baseline=510 current=510 closed=0 new=0
resolved: baseline=1461 current=1461 regressed=0 gained=0
VERDICT: no core-type public API lost its mapping arm
```

No row regressed and no new hole appeared, so this is a measurement correction,
not a coverage change.

## Mutation evidence

| mutation | result |
|---|---|
| generic retry disabled (`if (false)`) | `publicapi:resolved` 2574 -> 2554, gate `new=2 regressed=16`, exit 1 |
| restored | `resolved=2574`, gate `closed=0 new=0 regressed=0 gained=0`, exit 0 |

The mutation is load-bearing: without the retry the gate fails against the
corrected baseline, which is exactly the false signal the fix removes.

## Regression

20 cases pass, including every array case in the tree.

## Limit

The placeholder instantiation proves an arm EXISTS, not that every instantiation
is covered. A generic arm that handles `int32` but not `string` would still
report `resolved`. The per-instantiation coverage is what the byte-exact case
suite measures, not this probe.
