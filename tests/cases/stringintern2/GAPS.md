# GAPS.md -- stringintern2 defects (e2e batch C64, 2026-08-12)

Case status: **PASS** (net48 oracle 30 lines, exit 0; dual-compiler PASS via
run-tests.ps1). The active probes cover the WIRED String.Intern surface
(il2cpp_string_intern + the StringPool): nested-Intern idempotence, Intern of
a Concat of pooled parts, cross-construction content dedup, empty/char
round-trips, a 320-entry FOUR-REHASH pressure phase with identity survival and
find-only no-duplicate re-intern, 200 trailing-digit probe-stress contents,
200-char long-content interning, and cross-construction dedup against the
pressure set. Gaps below are RECORDED, NOT FIXED (this batch creates test
cases only).

## D-SINT2-1 -- String.IsInterned not wired (inherited, D-INT-1)

- **Phenomenon**: no probe calls `string.IsInterned(s)` -- it has no
  transpiler mapping and no runtime icall (grep finds no "IsInterned" in
  src/runtime or src/il2cppc); a call would abort at transpile. The
  find-only pool query surface therefore stays OUT of reach (already recorded
  as D-INT-1 in stringinterndeep/GAPS.md).

## D-SINT2-2 -- (informational) net48 JIT pre-interns method literals

- **Phenomenon**: any literal appearing anywhere in a net48 method is pooled
  when that method JITs, so `string.Intern(fresh)` where `fresh`'s content
  equals a source literal returns the PRE-INTERNED literal, NOT `fresh`
  (RefEq == 0). This case therefore assembles every runtime content via
  `string.Concat` from parts that never form a full source literal (the
  "si2-" prefix + part shape). The ILC ldstr path
  (il2cpp_string_intern_literal) matches on first use.
- **Not a defect** -- net48 behavior; the case is insensitive to it.

## D-SINT2-3 -- (informational) the ILC pool may rehash MORE times than net48

- The runtime pool starts at 64 slots and doubles at the 70% load factor
  (44.8 entries); 320 distinct contents force four rehashes on both runtimes.
  The case asserts IDENTITY SURVIVAL and CONTENT DEDUP, never slot counts --
  both runtimes must satisfy the same observable contract regardless of any
  internal table-size difference.
