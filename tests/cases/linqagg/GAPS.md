# GAPS.md -- linqagg recorded findings (e2e batch C62, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 91-line
byte-exact, all True). Active probes cover the WIRED LINQ aggregate surface
(il2cpp_linq_sum_i4 / min_i4 / max_i4 + the operator chain).

## D-LINQA-1 -- no new gaps encountered

- **Phenomenon**: none. Every probe family (multi-stage chains, ties/
  duplicates, zero/negative/boundary, relation identities, single-element
  chains, derived-empty truth table incl. 12 Min/Max-throws) landed byte-exact
  on the first transpiled run with zero C++ warnings/errors. The derived-empty
  aggregate contract (Sum->0, Min/Max->InvalidOperationException on
  Where(always-false) / Take(0) / Skip(oversize) / empty-source chains)
  matches net48.
- **Notes for future batches**: the three `False` results encountered during
  oracle capture (c13/d7/r8) were MY arithmetic errors, not runtime divergences
  -- the probes were corrected against the net48 truth. Sum OVERFLOW is
  deliberately NOT re-probed (net48 checked / runtime throws OverflowException,
  D-LINQ-3; pinned by linqgroup s_overflow). Selector-form aggregates
  (`Sum(Func)`, `Min(Func)`) remain LOUD (MethodMap.Linq.cs -- i4 family
  source-only) and are not probed.
