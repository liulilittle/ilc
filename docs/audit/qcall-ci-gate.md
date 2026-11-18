# QCall wiring + reachability is now a CI gate

The goal listed "QCall 192 is UNMEASURED" as an open item. It was measured, but
only in `.opencode/tmp` scratch files: `IcallWiringProbe` grew a `qcall` kind
(ImplMap module == "QCall") and `qcall_unmapped_reach.py` tiered the unmapped
remainder, and the result was written up in
`docs/audit/qcall-unmapped-reachability.md`. Nothing re-derived it, so a deleted
mapping arm would have changed the numbers silently.

CI step 3.74 now re-derives both axes on every run.

## What it measures

| axis | probe | result at CoreLib 8.0.31 |
|---|---|---|
| wiring status | `IcallWiringProbe` (kind=qcall) | resolved 19 / loud 44 / unmapped 129 |
| reachability of the unmapped 129 | `qcall_unmapped_reach.py` | INTERNAL-TYPE 84 / INNER-ONLY 45 / USER-CALLABLE 0 |

`QCallProbe` supplies the accessibility column (`typeAccess/methodAccess`) from
the ImplMap rows; `IcallWiringProbe` supplies the status. The join key is
`(owner, name)`, which the script asserts is unique (192 keys, 0 duplicates) --
a duplicate would merge overloads and overstate the gap.

## The two assertions, and why each exists

`--assert-zero-user-callable` fails when an unmapped QCall is nameable from user
code. That is the direct hole: a public member with no mapping arm emits an
undefined symbol.

`--assert-resolved-at-least 19` fails when fewer QCalls resolve than the pinned
floor. This one is not redundant, and the reason is a real blind spot in the
accessibility tiers:

> `Environment.ProcessorCount` is a PUBLIC property that calls the PRIVATE QCall
> `GetProcessorCount`. Deleting that mapping arm moves the row from `resolved`
> to `unmapped`, which changes the tier counts only from INNER-ONLY 45 to 46 --
> `USER-CALLABLE` stays 0, so `--assert-zero-user-callable` does not fire. A
> user-visible API is broken and the reachability gate is silent.

Measured: with the arm removed, `qcall:resolved=18`, tiers `INTERNAL-TYPE 84 /
INNER-ONLY 46`, and the reachability assertion alone exits 0. The floor
assertion exits 1. This is the same limitation
`qcall-unmapped-reachability.md` records at its "Method and its limits" section:
"not user-callable" is a metadata fact, not proof that no public CoreLib method
routes into the helper at run time.

## Mutation evidence

| step | resolved | CI step 3.74 |
|---|---|---|
| baseline | 19 | exit 0 |
| delete `case "GetProcessorCount"` from `MethodMap.Environment.cs` | 18 | exit 1 (`below the pinned floor of 19`) |
| restore | 19 | exit 0 |

The tier function was mutated separately (force every row to USER-CALLABLE):
`--assert-zero-user-callable` then exits 1 with `129 unmapped QCall(s) are
USER-CALLABLE`. Both assertions are therefore load-bearing, not decorative.

## Cost

`QCallProbe` builds in ~3s, `IcallWiringProbe` in ~8s (it references
`il2cppc`), and both probes run in under a second on a 2.6k-type assembly. The
step is pinned to `C:/Program Files/dotnet/shared/Microsoft.NETCore.App/8.0.31`
and throws a named error if that build is absent, rather than silently auditing
a different CoreLib.
