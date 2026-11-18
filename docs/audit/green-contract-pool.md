# GREEN contract set: full-pool verification

Every case in `scripts/green-contracts.txt` re-run at `f0ed0602`, one
`green-gate.ps1 -CaseFilter <name>` invocation each (the filter takes a single
case, so the set is run in batches).

| # | case | result | producedBytes |
|---|---|---|---|
| 1 | w11probe-linqgrpdec | OK ok=True | 52 |
| 2 | comslot | OK ok=True | 226 |
| 3 | probe-apartment-fail | OK ok=True | 564 |
| 4 | probe-excbase | OK ok=True | 706 |
| 5 | probe-catchf6a | OK ok=True | 76 |
| 6 | probe-userexcmsg | OK ok=True | 262 |
| 7 | probe-catchcarrier | OK ok=True | 760 |
| 8 | probe-excmsgcap | OK ok=True | 148 |
| 9 | probe-excparamname | OK ok=True | 372 |
| 10 | probe-pinvoke-error | OK ok=True | 146 |
| 11 | probe-marshal-helpers | OK ok=True | 150 |
| 12 | probe-interlocked-widths | OK ok=True | 380 |
| 13 | corelibp0 | OK ok=True | 1774 |
| 14 | delegate-bcl-invoke | OK ok=True | 84 |
| 15 | delegate-abi-signatures | OK ok=True | 78 |
| 16 | delegate-byref-params | OK ok=True | 38 |
| 17 | delegate-struct-signatures | OK ok=True | 38 |

17/17 GREEN.

## What `ok=True` means here

`green-gate.ps1` calls `difftest.ps1` for each case, and reads
`ok` from the produced `.diffreport.json` as a BOOLEAN (a SKIP is the string
"SKIP" and is not a pass). `difftest.ps1` computes it as

```
$ok = $produced -and ($dOut -lt 0) -and ($dErr -lt 0) -and ($exp.Code -eq $act.Code) -and ($diffIds.Count -eq 0)
```

i.e. both sides produced output, neither output nor stderr differ, the exit
codes match, and no record-level diff was found.

## `expected.txt` is NOT what difftest compares against

`difftest.ps1` runs the net48 oracle ITSELF and diffs against it; it never opens
`tests/cases/<name>/expected.txt`. Verified by corrupting
`tests/cases/delegate-byref-params/expected.txt` to `WRONG-OUTPUT`: difftest
still reported `ok=True` with `records(oracle=0 runtime=0) diff=0`.

That file is dead weight in this tree. 414 of the 1275 case directories carry
one (a historical convention); only `tests/perf/expected.txt` is actually read
(`run-perf.ps1`). They are left in place rather than deleted — removing 414
tracked files is a much larger change than this verification, and the existing
convention predates it. Recorded here so nobody assumes the file is checked.

## Why this document exists

The contract set is the only thing that gates regressions in the areas it
covers: a guard that exempts a family (delegates), a decoder arm (byref,
value types), or an exception-carrier path can regress silently if no contract
case exercises it. `corelibp0` was in no contract file for four rounds while
`new Action(...)` was broken. A periodic full-pool run is the countermeasure;
this file records the state at this commit so a later failure is a diff, not a
rediscovery.
