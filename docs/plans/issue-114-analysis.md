# Issue #114 Analysis — StringBuilder + string concat crash

- Date: 2026-08-19
- Status: INVESTIGATION COMPLETE — original crash NOT reproduced; 3 real bugs
  found and fixed (batch 170); latent risk areas documented below.
- Scope: `src/runtime/src/stringbuilder.cpp`, `src/runtime/src/concat.cpp`,
  `src/il2cppc/Emit/MethodMap.StringBuilder.cs`, probe case `sbinsertcase`.

## 1. Original report

User reported a crash involving string concatenation combined with
StringBuilder usage. The exact reproducer was never provided; the report
predates the batch-170 fix.

## 2. What the 52-variant deep-dive found (batch 170, commit a9aadeb)

`tmp/concat114x/` holds 52 probe variants (b2..h8 + concat114x V1-V12) covering:
- huge append loops (memory pressure + realloc path)
- nested builders + ToString chaining
- Insert(0,...)/Replace/Remove mixed with concat
- preallocated capacity + over-append (growth path)
- multi-arg concat (4-arg fixed, 5-arg params-array)
- mixed chains (string + int + ToString + arithmetic)
- empty-string / single-char boundaries (bounded doubling)
- GC pressure (GC.Collect every N iterations)
- fold-defeat (number-first concat, multi-use ToString)
- int64 / negative / large values
- self-referential Append(sb.ToString() + i)
- Append(object) boxed path
- bool/double/char concat paths, AppendFormat, chained appends
- dual-builder churn, ctor(string) + Insert(0, concat)

**3 real bugs found and fixed in batch 170:**
1. Numeric Insert overloads emitted raw int64/double as `Il2CppObject*`
   (uncompilable C++).
2. `Insert(int, char[])` silently read the array header as a string
   (data corruption).
3. 3-arg `Insert(int, string, int)` lost the receiver.

All three are now gated per `ps[0]` type like the Append case (numeric ->
ToString-then-insert / per-type insert icalls, char[] loud, unmapped loud
NotSupportedException). Probe case `sbinsertcase` (20 blocks, 40-line
expected.txt) committed with the fix.

## 3. Code-path analysis (2026-08-19, current HEAD 6b040f1)

### 3.1 Growth policies (stringbuilder.cpp:161-208)
- Append: `GrowBuffer` — newCapacity = max(required, max(Length, oldCapacity)*2);
  int64 arithmetic, INT32_MAX clamp, OOM throw. Verified net48-consistent.
- Insert: `GrowBufferForInsert` — newCapacity = oldCapacity + valueLength.
  Verified net48-consistent.
- `ReplaceBuffer` (144-159) uses `il2cpp_write_barrier` before the chars
  field store — the correct pattern for a GC-tracked reference field.

### 3.2 AppendUnits / InsertUnits (216-267)
- `AppendUnits`: memcpy into `sb->chars->data + length`; grows first.
- `InsertUnits`: bounds-check index, memmove tail, splice; grows first.
- **Aliasing risk (latent, not currently reachable):** if `units` pointed
  INTO the builder's own `chars` buffer AND no growth occurred, the
  memcpy/memmove would overlap (UB). All current callers pass units from
  either a ROOTED managed string (`v->chars`) or a C++ stack buffer (numeric
  format kernels) — never the builder's own buffer. Documented in the
  InsertUnits contract comment (238-241). No fix needed; a defensive
  `memmove` in AppendUnits would be a no-op change (memcpy is correct for
  non-overlapping).

### 3.3 ctor_string (613-640)
- `sb->chars = chars;` at line 632 is written WITHOUT `il2cpp_write_barrier`
  (unlike ReplaceBuffer 150-151). The builder is freshly allocated
  (object_new) so it lives in Gen0 and is always scanned by the collector —
  the missing barrier is therefore benign in practice. **Inconsistency only,
  not a bug.** A future refactor should add the barrier for uniformity.

### 3.4 append_o / AppendFormatArg (736-753)
- `Append(object)` dispatches through `AppendFormatArg(&self, value)`:
  null -> no-op; string -> payload; boxed Int32 -> invariant decimal; else
  `icall_object_to_string`. `self` and `value` are both rooted in a
  RootScope. Safe.

### 3.5 to_string (1269-1282)
- Reads `sb->chars->data` + `sb->length` BEFORE `il2cpp_string_new`; rooting
  self keeps builder + buffer marked, so a collection inside the allocation
  cannot sweep them (marked old copies stay content-valid even when
  compaction moves them). Safe.
- **Null-chars assumption:** `sb->chars->data` dereferences the buffer; all
  ctor paths allocate a buffer, so `chars` is never null for a constructed
  builder. Safe.

### 3.6 replace_s_s (1096-1121)
- null oldValue -> ArgumentNullException; empty -> ArgumentException; greedy
  left-to-right scan. No aliasing (oldValue/newValue are separate managed
  strings). Safe.

### 3.7 concat.cpp ConcatFromArray (134-207)
- Two-pass (lengths, then payloads) with a single non-GC scratch allocation
  before the single managed allocation; element pointers re-read in pass 2
  (never cached across a collection). GC-safety contract documented in the
  header. Safe.

## 4. Conclusion

- The batch-170 fixes (3 real Insert bugs) are the most probable root cause
  of the original #114 crash: the numeric-overload bug emitted raw int64/
  double as object pointers, which would crash on any numeric Insert; the
  char[] bug corrupted data; the 3-arg bug lost the receiver.
- No crash is reproducible on current HEAD across 52+ variants + the
  committed sbinsertcase probe + the full 415-case matrix (all PASS).
- Latent risk areas (documented, not bugs): ctor_string missing write
  barrier (benign, Gen0), AppendUnits memcpy aliasing (unreachable with
  current callers).

## 5. What the user should provide to reproduce the ORIGINAL crash

To close #114 definitively, the original reproducer is needed. Useful
details:
1. The exact source snippet (or a minimal reduction) that crashed.
2. The crash signature: exit code / exception type / stack trace.
3. Which runtime: the ILC-transpiled exe or the net48 oracle?
4. Approximate date/commit of the crash (pre- or post-batch-170).

Without the original case, the investigation stands as: 3 real bugs fixed,
52+ variants + full matrix green, latent risks documented.