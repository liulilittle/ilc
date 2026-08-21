# ILC Roadmap Status — v32 (deep-testing campaign + write-barrier consistency + audit)

Snapshot: HEAD `e4fc0b5` (batch 343). Continuation of v31.

## Completed since v31

### Deep-testing campaign (2026-08-19, user-directed: "进行深度详细测试把，你才能发现问题不是嘛")
Seven new GC-stress runtime tests, all committed and passing, covering every
major allocation-heavy path under forced full collections (the collector is
explicit-only, so the contract under test is the CROSS-CALL one: a rooted
object graph must survive repeated compacting collections):

| Test | Sections | Coverage |
|---|---|---|
| `test_stringbuilder_gcstress.cpp` | 7 | #114 risk areas: ctor_string:632 barrier gap / AppendUnits / InsertUnits |
| `test_concat_gcstress.cpp` | 7 | concat two-pass read contract (concat.cpp:128-133) |
| `test_stringops_gcstress.cpp` | 8 | replace/join/concat/substring/trim/char[] ctor |
| `test_format_gcstress.cpp` | 5 | numeric formatting kernel GC-safety contract |
| `test_hashtable_gcstress.cpp` | 5 | rehash + cross-call compaction contract |
| `test_arraylist_gcstress.cpp` | 5 | growth + insert/remove/churn under collection pressure |
| `test_delegate_gcstress.cpp` | 4 | combine/remove/invoke under collection pressure |

Commits: `2b1f43e` (3 tests), `4f209d6` (format), `980de20` (hashtable),
`9c330ed` (arraylist), `e4fc0b5` (delegate).

**Campaign conclusion: no runtime bugs found** (only 7 self-introduced test
bugs, all fixed). The runtime's GC discipline is excellent: explicit-only
collector + GcRoot + re-read-after-allocation + write barrier + full
refFieldBitmap coverage.

### Write-barrier consistency fix (2026-08-19, commit `d7ce50b`)
Closed the documented #114 risk point: the three StringBuilder ctor paths
(ctor_default/ctor_capacity/ctor_string) stored the fresh char buffer into
`sb->chars` WITHOUT `il2cpp_write_barrier`, violating the GC-safety contract
in `il2cpp_stringbuilder.h:27-32` and the `ReplaceBuffer` pattern
(stringbuilder.cpp:150-151). All 4 buffer stores now honor the contract
uniformly. Also fixed a latent compile error in the committed
`test_concat_gcstress.cpp` (const-pointer array element assignment).

### Write-barrier / rooting audit (2026-08-19)
Comprehensive audit of every reference-field store and refFieldBitmap:
- The collector performs FULL-HEAP collection (gc.cpp:300-303), so the card
  table does not change correctness yet — missing barriers are consistency
  improvements, not bugs.
- Every managed object's refFieldBitmap correctly covers its reference
  fields (delegate, delegateext, datatable, hashtable, listi4, linq_map,
  stringrw, threadobj, sortedlist, stackqueue, crypto_obj).
- Native structs holding managed pointers either root them explicitly
  (xml_write `add_root_range`) or via the managed wrapper (stringrw).
- No open TODO/FIXME (BUG-1/2/3 all documented as fixed).
- **No real bugs found.**

### Verification
- Runtime: **146/146 PASS** (139 + 7 new GC-stress tests).
- Unit: **1807/1807 PASS**.
- e2e: 7-case set PASS (winforms1 1031 byte-exact, delegops, asynccase,
  linqgroup, monitordeep, com17type, sbinsertcase).
- Targeted e2e sweep of 47 string/format/collection/GC-heavy cases:
  **47/47 PASS** (confirms the stringbuilder.cpp change is zero-regression).

## Remaining / next batch pool
1. M8 S2 macOS/OpenBSD real-machine verification (external hardware) —
   documented verification procedure + blocker record.
2. #114 concat+sb original crash case (needs the user's original repro) —
   all documented risk areas now closed or verified safe under GC pressure.
3. winforms1 Main split (deferred, high risk; capped at 110 phases).
