# GAPS.md -- p-adb-gc recorded findings (adm1 defect-fix batch: D1 GC safety + NB1 SetData capacity)

Case status: **PASS** (mingw; net48 oracle == transpiled runtime byte-for-byte,
oracle sha f3cf37f0 == runtime sha, 4 records, diff=0).

Scope: the adm1 review's D1 (compacting-GC windows in AppDomain.CreateInstance)
and NB1 (SetData static-root overflow + silent key drop) defects. `expected.txt`
is the exact net48 oracle -- every record is a boolean assertion, byte-exact.

## Fixed here

- **NB1**: the SetData side table is now rooted as ONE root RANGE over the whole
  fixed slot array (il2cpp_gc_add_root_range), independent of the
  kMaxStaticRoots=128 per-slot counter (per-slot static roots overflowed it at
  63 keys -> "static root table overflow" abort). Capacity raised 64 -> 256
  keys; overflow now FAILS LOUD (InvalidOperationException) -- an explicit
  documented AOT bound, never a silent drop. The probe sets 80 keys + an
  in-place overwrite and reads them all back.
- **D1**: AppDomain.CreateInstance's two cross-allocation windows now root the
  plain local in an il2cpp::GcRoot (CreateInstanceCore's `obj` across the
  StringBuilder default-ctor's buffer allocation; icall_appdomain_create_
  instance's `inner` across the ObjectHandle allocation), re-reading Get()
  after each allocation -- the il2cpp_gcroot.h RAII contract (the stackqueue2
  precedent for the compacting 256KB-threshold collector). The probe stresses
  both windows (CreateInstance + CreateInstanceAndUnwrap under ~256KB churn
  per iteration) and verifies every created builder appends + reads back
  intact. The D1-specific GC-window trip is allocation-timing dependent and was
  not reproducibly observed at stress scale on the pre-fix build (the p-adb-gc
  case is deterministically RED pre-fix through the NB1 abort, which aborts
  before the D1 loop runs); the stress assertions give post-fix coverage.

## Recorded, NOT fixed (W7)

- 256-key SetData bound (a loud documented AOT wall); arbitrary-type default
  ctors beyond StringBuilder (zero-init only); CreateInstance for unregistered
  types -> NotSupportedException; AppDomain.ToString / the CAS surface remain
  LOUD (see p-adb-loud).