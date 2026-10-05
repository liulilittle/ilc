# GAPS.md -- stringbuild2 defects (e2e batch C75, 2026-08-13; D-SB-1 resolved)

Case status: **PASS** (net48 oracle 46 lines, exit 0; dual-compiler PASS via
run-tests.ps1 -Compiler mingw/msvc). Active probes cover the WIRED
AppendFormat overloads (AppendFormat(string, object[, object[, object]]) ->
icall_stringbuilder_append_format_1/2/3 + AppendFormatCore): "{{"/"}}" escapes,
the D-SB-1 brace-combination grammar (1b: "{{0}}"->"{0}", "{{{0}}}"->"{7}",
"{0}"->"7", "x{{{0}}}y"->"x{7}y", "{{{{0}}}}"->"{{0}}", "{0}}}"->"7}",
"{{0}}{0}"->"{0}9", and the "{0}}"/"{{0}}}" FormatException faces),
{N} plain with 1/2/3 args, null args, string args ignoring the specifier,
alignment (right/left/no-truncate/zero/null), combined {N,width:fmt}, the
zh-CN numeric specifiers (N2/N0/C/C0/P/F2/F0/D5/D8/X/x/X4/E/G), bool/char boxed
args, the exception faces (null format ANE; "{}"/"{x}"/"{0"/"}" /"{0,}"/index>=n
FormatException -- builder unchanged after each), and a chained
AppendFormat+Append+Insert.

## D-SB-1 -- RESOLVED (no runtime change needed; GAPS trace was inaccurate)

- **Original report**: net48 `AppendFormat("{{{0}}}", 7)` produces "{7}" but
  the runtime's AppendFormatCore throws FormatException.
- **Verified reality (2026-08-13, D-SB-1 agent)**: the runtime ALREADY yields
  "{7}" for `{{{0}}}`. AppendFormatCore (stringbuilder.cpp:340) consumes the
  leading "{{" as one literal brace, expands the "{0}" placeholder, then
  consumes the trailing "}}" as one literal brace -- the combination never
  leaves a lone '}', so the "}}" escape path (stringbuilder.cpp:490) handles
  it. The original GAPS trace miscounted the braces (it assumed a lone '}'
  remained after the placeholder; `{{{0}}}` is 7 units and leaves none).
  Proof: test_stringbuilder.cpp now probes `{{{0}}}` -> "{7}" via BOTH the
  String.Format and AppendFormat icall paths (green); the e2e stringbuild2
  brace-combination block (1b) PASSES byte-exact on net48 (46-line oracle) and
  on the transpiled runtime under BOTH MinGW and MSVC.
- **Action taken**: no edit to src/runtime (the code was already net48-correct
  for the required combos); added the brace-combination probes (unit + e2e)
  that LOCK the behavior and corrected this record.

## D-SB-1b -- RECORDED, NOT FIXED -- net48-binary lenient-unclosed quirks

- **Phenomenon**: for DEGENERATE unbalanced format strings the net48 binary on
  this host is MORE lenient than both its own reference source
  (microsoft/referencesource StringBuilder.AppendFormatHelper) and modern
  .NET. File-captured oracle: `{{{0}` -> "{7}", `{{{0},{1}}}` -> "{7,8}",
  `{{0` -> "{0}", `{{{{0}}` -> "{{0}". The runtime (like the reference source
  and .NET 6+) rejects `{{{0}` and `{{{0},{1}}}` with FormatException.
- **Root cause**: the runtime's index/alignment parsing demands a closing '}'
  (stringbuilder.cpp:453-458), which is the documented .NET contract; the
  net48 binary's acceptance appears to be a JIT/NGEN artifact -- its own
  mscorlib IL contains `if (pos == len) FormatError()` in the index loop and
  `if (ch != '}') FormatError()` after alignment, so the observed "{7}"/"{7,8}"
  contradicts the assembly being executed. No runtime change: matching the
  quirk would make the parser MORE lenient than the reference implementation.
- **Coverage**: `{0` / `abc{0` / `{0,5` / `{0,` are FormatException on BOTH
  net48 and the runtime (asserted); the lenient cases are intentionally NOT
  probed in the e2e case (they would diverge on the net48 oracle).

## D-SB-2 -- zero-arg params AppendFormat lowers to Array.Empty<object>() (D-CON-1 class)

- **Phenomenon**: `AppendFormat("{{0}}")` (no args) binds to the
  `AppendFormat(string, params object[])` overload and Roslyn passes
  `Array.Empty<object>()`, which the transpiler rejects LOUDLY
  ("System.Array::Empty: Array.Empty<T> generic argument 0x1C not wired in
  M5", MethodRefResolver.cs BclResolve) -- the same defect recorded as D-CON-1
  in batchB-report. The zero-arg form is therefore NOT transpilable.
- **Case adaptation**: the escape probes pass one unused arg
  (`AppendFormat("{{0}}", 1)`) so they bind to the wired (string, object)
  overload; the output is identical.
- **Suspected root file**: `src/il2cppc/Emit/MethodRefResolver.cs` (Array.Empty
  dispatch; 0x1C object element) / MethodMap.Array.cs.
