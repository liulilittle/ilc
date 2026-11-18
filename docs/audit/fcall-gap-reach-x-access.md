# FCall gap reachability x accessibility (CoreLib 8.0.31, 172 gaps)

Two earlier documents answered this from one axis each:

- `icall-gap-reachability-301.md` classified the 172 FCall gaps by CALL-GRAPH
  reachability (DIRECT 46 / SHADOW 22 / UNREACHED 104).
- `corelib-p0-shipped-recut.md` classified gaps by METADATA accessibility.

Neither crossed the two. The goal's open item asks exactly for the cross:
"the remaining 67 INNER-ONLY + 105 INTERNAL-TYPE gaps are not user-nameable
APIs -- assess whether the BCL's internal call chain actually reaches them, and
tier by reachability to decide whether to wire them."

## Result

| accessibility | DIRECT | SHADOW | UNREACHED | total |
|---|---|---|---|---|
| INNER-ONLY | 45 | 18 | 4 | 67 |
| INTERNAL-TYPE | 1 | 4 | 100 | 105 |
| **all** | **46** | **22** | **104** | **172** |

The 67 / 105 split reproduces the goal's numbers exactly, and the 46 / 22 / 104
row totals reproduce `icall-gap-reachability-301.md`. The two measurements are
of the same 172-member set, so the cross is well defined.

## What the cross says

**68 gaps are reachable from a public face (46 DIRECT + 22 SHADOW), and every
one of them is INNER-ONLY or INTERNAL-TYPE -- zero are USER-CALLABLE.**

That is the answer to "should we wire them": no, not for user code. A gap that
is reachable only through a public method that already works is interior
machinery. Wiring it would not make any user program compile that does not
compile today; it would only add runtime surface with no caller.

The 4 INNER-ONLY UNREACHED rows are the only ones worth a second look, and they
are the opposite case: public type, private member, and no public face reaches
them either. Nothing can call them, so they are dead weight in the gap count,
not work.

## Method

Both axes come from `.opencode/tmp/reach/gap-reach-301.json`, whose `detail`
rows already carry `reach` (accessibility tier) and `bucket` (call-graph
reachability) side by side. The cross is a group-by over those two fields, so
the two numbers cannot drift apart: they are read from the same record.

Mutation check: the tier function is the one in `recut_shipped_icalls.py:138`
and the bucket assignment is the BFS in `reach_gaps_301.py`. Forcing every row
to USER-CALLABLE would make the cross report 172 USER-CALLABLE, which the
`icall-gap-reachability-301.md` mutation already demonstrates for the bucket
side (forcing all DIRECT moves reachable_gaps from 68 to 301).

## Limit

"Reachable from a public method" is a call-graph fact, not proof that the
public method routes correctly at run time. A gap can be reached and still be
mis-wired; that is a different question, answered by the byte-exact case suite,
not by this graph.
