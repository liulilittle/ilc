# The core-type `publicapi unmapped` gate

`IcallWiringProbe` emits a `publicapi` row for every public method on a public
type, precisely so a public method with no mapping arm can be spotted. Nothing
gated on those rows, and two real holes shipped through it:

- `Array.CreateInstance` (six overloads) -- found while chasing the reachability
  audit's "Limits" caveat;
- `Array.Clone` -- found while looking for the next one.

Both were `publicapi unmapped` the whole time. That is the failure mode this
gate closes: a public method with no arm is invisible to the reachability audit,
because that audit classifies the FCall *gap* set and these are managed CoreLib
methods, not icalls.

## Why not gate all 9328 rows

The full net48 `publicapi unmapped` set is 9328 rows. Most are APIs a normal
program never names: `Reflection.Emit` builders, remoting, `_Type`,
`DateTimeFormatInfo` internals. Gating all of it would fail on every unrelated
change and be ignored.

The gate covers the CORE owners -- the types a program names directly: the
primitives, `String`, `Array`, `Console`, `Convert`, `Math`, the collections,
IO, and the threading primitives. That subset is 517 unmapped + 1440 resolved
today.

## Two baselines, because one is not enough

`scripts/publicapi-unmapped-baseline.txt` (517 rows) is the unmapped set.
`scripts/publicapi-resolved-baseline.txt` (1440 rows) is the resolved set.

The unmapped baseline alone is not sufficient, and this was measured rather than
assumed: deleting the `Array.Clone` arm with only the unmapped baseline in place
reported `new=0`. The row simply returned to the unmapped set, where the
baseline already listed it -- so the gate saw no change at all. A one-sided gate
would have let the exact regression it exists to catch pass silently.

The two-sided contract:

| movement | verdict |
|---|---|
| unmapped row becomes resolved | `CLOSED`, passes (the set shrank) |
| resolved row becomes unmapped | `REGRESSED`, **fails** |
| unmapped row not in the baseline | `NEW`, **fails** |
| resolved row not in the baseline | `GAINED`, passes (reported) |

## Mutation evidence

| step | unmapped | resolved | gate |
|---|---|---|---|
| baseline | 517 / 517 | 1440 / 1440 | exit 0 |
| delete `case "Clone":` from `MethodMap.Array.cs` | 517 / 517 | 1440 / **1439** | exit 1, `REGRESSED System.Array::Clone` |
| restore | 517 / 517 | 1440 / 1440 | exit 0 |

The one-sided variant was also measured: with only `--baseline`, the same
deletion reported `new=0` and exit 0.

## Cost

`IcallWiringProbe` builds in ~3s and runs in under a second on net48 mscorlib.
The step is pinned to
`C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8/mscorlib.dll`
and throws a named error if that reference assembly is absent, rather than
silently auditing a different one.

## What this does not cover

- The 8811 non-core unmapped rows. A hole there is still invisible; widening the
  gate means adding the owner to `CORE_OWNERS` and regenerating both baselines,
  which is a deliberate act rather than an accident.
- Whether a resolved row is *correct*. The gate proves an arm exists, not that
  it behaves like net48. That is what the GREEN contract cases are for.
