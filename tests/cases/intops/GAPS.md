# GAPS.md -- intops recorded findings (e2e batch C61, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 44-line
byte-exact, all True). Active probes cover the WIRED int surface (unchecked
wrap + conv.ovf conversions + checked div/rem + bit ops).

## D-INT-1 -- `checked(a + b)` int32 folds to il2cpp_checked_add which is NOT in the runtime

- **Phenomenon**: StackFold.cs folds `add.ovf/sub.ovf/mul.ovf` to
  `il2cpp::il2cpp_checked_add/sub/mul` (StackFold.cs:1300-1322), but the
  runtime declares/defines ONLY `il2cpp_checked_div` and `il2cpp_checked_rem`
  (il2cpp_exception.h:91-92). A `checked(a+b)` probe would therefore emit a
  reference to an undefined `il2cpp::il2cpp_checked_add` symbol and fail at
  cmake link. The StackFoldTests unit test asserts the fold target
  (tests/unit/StackFoldTests.cs:1599) but the runtime side was never landed.
- **Impact**: the "checked" part of this case is covered by the WIRED checked
  CONVERSIONS (`checked((int)l)` -> icall_conv_ovf_i4_i8, in-range + overflow
  catches) and the checked DIV/REM overflow (`int.MinValue / -1` ->
  OverflowException via il2cpp_checked_div). Checked add/sub/mul probes are
  excluded -- a D-INT gap, recorded not fixed.
- **Fix path (future)**: add `int32_t il2cpp_checked_add/sub/mul[_un]` to
  il2cpp_exception.h/exception.cpp (throw OverflowException on wrap, matching
  the checked_div/rem pattern) -- or emit inlined boundary checks.

## D-INT-2 -- a dead (result-unused) division/checked-conversion inside try{} is DCE'd, so the exception never fires

- **Phenomenon**: a try block whose ONLY effect is `int z = minv / -1;` (z
  never used) has the division dead-code-eliminated, so the generated C++
  try body contains no throw site and the catch never fires (net48 still
  throws). First draft of this case hit this: the oracle printed True but the
  transpiled run printed "never" + False. Worked around by making the result
  observable (`Console.WriteLine("d1 " + z)`), matching the overflow.cs
  precedent.
- **Impact**: future overflow/checked-conversion probes MUST observe the
  result inside the try (or the DCE removes the checked operation). Recorded
  here as a probe-authoring rule, not a runtime defect.
- **Fix path (future)**: none (correct by design -- an unused managed op has
  no observable side effect).
