# (owner, name) join keys in tools/audit: which are sound, and which are not

Three rounds were spent fixing the same defect one script at a time, so this
document closes the audit: every `(owner, name)` key in `tools/audit/*.py`,
classified by whether the key can merge overloads in a way that changes the
answer.

## The defect being guarded against

A `(owner, name)` key merges overloads. When one overload is wired and another
is not, a key-level lookup reports the whole member as wired and the gap
silently disappears. Measured on the shipped 8.0.31 tables, the FCall set has 6
duplicate keys and the QCall set has 0.

## Classification

| script | key use | sound? | why |
|---|---|---|---|
| `recut_shipped_icalls.py` | wired/unwired join | **guarded** | key-level join; guard added, fires on constructed data |
| `reach_gaps_301.py` | wired/unwired join | **guarded** | same; guard added, fires on constructed data |
| `qcall_unmapped_reach.py` | wired/unwired join | **guarded** | same; guard added, fires on constructed data |
| `parse_icall_tables.py` | `decl_index` (evidence only) | **sound** | only attaches the `decl` record to a row; does not decide status |
| `parse_icall_tables.py` | `wiring_lookup` (status) | **sound by design** | static grep of the owner's dispatch chain for a `case "<name>":` arm; overloads genuinely share one arm, so the key matches the question being asked |
| `recut_p0_gaps.py` | `surface()` membership | **sound** | answers "does this member exist in version X", and overloads share existence |

The three guarded scripts are the ones that answer "is this member wired", where
merging overloads changes the answer. The three unguarded uses answer different
questions -- "what is the declaration", "is there a case arm", "does the member
exist" -- where overloads legitimately share one answer.

## The limit this audit found

`wiring_lookup` is a static grep, so it cannot distinguish overloads, and the
shipped table has a case where that shows:

```
System.ArgIterator::.ctor  (IntPtr)          resolved  icall_argiterator_ctor
System.ArgIterator::.ctor  (IntPtr, Void*)   loud      MethodMap.Legacy.cs:321
```

Both overloads hit the same `case ".ctor":` arm, so `wiring_lookup` answers
"wired" for both. That is correct for the question it asks (an arm exists) and
wrong for the question "does this specific overload resolve". The runtime probe
(`IcallWiringProbe`) is the authority for the latter, and it is what the
guarded scripts use.

Note what this case does NOT mean. `resolved` and `loud` are both "wired" for
the gap count -- a LOUD rejection is a deliberate refusal that names the member,
not a silent undefined symbol -- so `ArgIterator::.ctor` is uniformly wired and
the (owner, name) key gives the right answer for it. The guards check for
`resolved/loud` vs `unmapped`, which is the boundary that changes the count; a
`resolved` vs `loud` split does not. A check that treated those two as
different states would report a false mixed key here, which is what my first
scratch script did before I read `load_wiring`'s docstring.

`corelib-p0-recut.md` already records that `tier_for()` is a hardcoded type set
rather than a reachability derivation, and that the disposition column is a grep
measurement. This document adds the overload caveat to that same family of
limits: a static arm check is not a per-overload resolution check.

## Verification

- The three guards each fire on constructed data (one overload flipped to
  unmapped) and do not fire on the real tables, where all 6 duplicate keys are
  uniform.
- The real tables reproduce: FCall 418 declared / 172 unwired, QCall 192 / 129,
  shipped 610 / 301, USER-CALLABLE (live) 0.
