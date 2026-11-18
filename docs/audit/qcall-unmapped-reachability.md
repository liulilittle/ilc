# QCall unmapped reachability (CoreLib 8.0.31, 129 unmapped)

`qcall-gap-tiers.md` classified the QCall *gap* set (205 rows, including FCall
migrations) and found 4 public members, all already carrying transpile-time
markers. That left a different question open: of the 192 shipped QCalls, 129
have no mapping arm at all. Are any of those 129 nameable from user code?

This document answers it from metadata accessibility, the same two-level test
`recut_shipped_icalls.py` uses, rather than from a guess.

## Result

| tier | count | meaning |
|---|---|---|
| INTERNAL-TYPE | 84 | the declaring type is not public -- user code cannot name it |
| INNER-ONLY | 45 | the type is public, but the member is private/assembly |
| **USER-CALLABLE** | **0** | -- |

**Zero of the 129 unmapped QCalls are user-callable.** The 45 INNER-ONLY rows
are the interesting-looking ones: their type is public (`System.Buffer`,
`System.Array`), so a naive "is the type public" check would report 45 holes.
The member access bit is what settles it -- `__Memmove`, `__ZeroMemory`,
`GetElementConstructorEntrypoint` are private helpers reached only from inside
CoreLib, and CoreLib method bodies are not transpiled
(`Program.cs` loads only `opts.InputAssembly`), so they have no call site and
produce no code.

## Why this closes the direction

The goal's open item was "QCall 192 is UNMEASURED". It is now measured on both
axes:

- wiring status: 19 resolved / 44 loud / 129 unmapped (IcallWiringProbe, qcall
  kind, `wiring-8031-qcall.tsv`);
- reachability of the unmapped remainder: 0 user-callable (this document).

So the unmapped 129 cannot be a user-visible hole. They are interior machinery
with no call site in a transpiled program.

## Method and its limits

Accessibility comes from `qcall-8031.tsv` column 4, `typeAccess/methodAccess`,
produced by `QCallProbe`. The tier function is the one in
`recut_shipped_icalls.py:138-142`:

```
typeAccess != "public"                                  -> INTERNAL-TYPE
methodAccess in (public, family, familyorassembly)      -> USER-CALLABLE
otherwise                                               -> INNER-ONLY
```

Mutation check: forcing the tier function to answer USER-CALLABLE for every row
turns the result into `{USER-CALLABLE: 129}`, so the classification is doing
real work rather than defaulting.

Limit this measurement cannot settle: "not user-callable" is a metadata fact,
not proof that no public CoreLib method routes into these helpers at run time.
That second question is the call-graph question `icall-gap-reachability-301.md`
answers for the FCall set; the QCall rows have no call-graph entry at all
(NOT-IN-GRAPH 129 in that document), so nothing can be said about them either
way from the graph. The metadata answer is the strongest one available.
