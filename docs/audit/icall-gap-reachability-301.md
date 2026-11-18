# icall gap reachability (CoreLib 8.0.31, 301 gaps)

`corelib-p0-shipped-recut.md` counts the gaps and classifies them by the access
bits in metadata. That leaves one question open: of the gaps whose declaring
type is not public, which ones can a user program still reach indirectly,
through a public wrapper that calls into them? This document answers it from the
call graph rather than from a guess.

## Result

| table | DIRECT | SHADOW | UNREACHED | NOT-IN-GRAPH | total |
|---|---|---|---|---|---|
| FCall | 46 | 22 | 104 | 0 | 172 |
| QCall | 0 | 0 | 0 | 129 | 129 |
| **all** | **46** | **22** | **104** | **129** | **301** |

**68 of 301 gaps are reachable from a public face** (DIRECT = the public method
calls the gap itself; SHADOW = reachable at 2+ hops). The remaining 104 FCall
gaps are in the graph but no public face reaches them; the 129 QCall gaps have
no call-graph entry at all, so nothing can be said about them either way.

## Why 68 reachable gaps still means zero user-visible holes

Every one of the 68 fails *both* of the following:

```
with publicFace=True among the gap's own graph nodes : 0 of 68
gap types that are public in metadata                : 0 of 68
```

`publicFace` is the call graph's own marker for a method user code can name, and
it agrees with the metadata check. Of the 68, 63 are INNER-ONLY and 5 are
INTERNAL-TYPE; none is USER-CALLABLE. They are interior machinery --
`System.Array::InternalCreate`, `System.Buffer::__BulkMoveWithWriteBarrier`,
`System.Delegate::InternalAlloc` -- reached from public methods that already
work. They cannot be named from user source, so wiring them is not a
prerequisite for any user program.

Caveat that this measurement cannot settle: "reached from a public method" is a
call-graph fact, not proof that the public method routes correctly at run time.
A public wrapper can itself be unmapped while its internal target sits in this
list, and then the wrapper -- not the gap -- is the defect. One such case was
found this way and is recorded separately: `Marshal.GetExceptionForHR` has no
arm at all and emits an undefined symbol (`discover.md`, "轮 40").

This is the same conclusion `recut` reached by a different route, and getting it
twice from independent data is the point: `recut` reads access bits, this script
reads the call graph. Neither alone would be convincing.

## Reproduce

```
python tools/audit/reach_gaps_301.py
```

Inputs: `.opencode/tmp/reach/corelib-callgraph.json` (57812 edges, 18468 public
faces, 0 decode failures) plus the TSVs the recut pipeline already produces.
Output: `.opencode/tmp/reach/gap-reach-301.json`.

## Mutation check

Forcing every gap into the `DIRECT` bucket (i.e. ignoring reachability
altogether) moves `reachable_gaps` from **68 to 301**. Restoring the BFS returns
it to 68. Without that check the script would "prove" nothing, because a script
that labels everything reachable also reports 68 reachable rows.

## Limits

- The graph covers CoreLib only. A BCL method reached solely from a *user*
  assembly would not appear here, but such a method would have to be public to
  be called at all, and no public gap remains (USER-CALLABLE is 0 in both
  tables).
- The 129 QCall gaps are absent from the graph, so "NOT-IN-GRAPH" is the absence
  of evidence, not evidence of unreachability. They are already classified as
  internal by metadata, which is what makes them non-user-callable regardless.
- `SHADOW` counts hops but does not prove the shadowing wrapper is itself
  reachable in a given program; it only proves a path exists from some public
  face.
