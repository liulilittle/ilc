# Non byte-exact cases: the `sweep-exclude.txt` contract

`scripts/sweep-exclude.txt` is the single list of case directories under `tests/cases/`
that are **not byte-exact difftest cases**. It existed before this change and was already
consumed by `scripts/sweep-cases.ps1` (full-pool sweep) and `scripts/loud-gate.ps1`
(LOUD contract check); `scripts/difftest.ps1` and `scripts/run-tests.ps1` did not read it,
so invoking them directly on such a case produced a permanent red result.

## Why the two harnesses now read it

A case listed there cannot pass an oracle-vs-runtime byte comparison **by construction**:

| kind of entry | why byte-exact is impossible |
| --- | --- |
| `RUNTIME-CONTRACT` probe (e.g. `p-adb-loud`) | net48 deliberately diverges (it returns a localized name) while the runtime must throw; `expected.txt` pins the runtime side only |
| W7 whitelist (e.g. `p-stacktrace`) | depends on live CLR internals that cannot be statically reproduced; kept as net48 measurement + red evidence |
| `LOUD contract` cases | the assertion is "threw the documented exception", not a matching output; verified by `scripts/loud-gate.ps1` |
| incomplete WIP assets (e.g. `convboolchar1`) | only a `.csproj` was committed, the oracle cannot even build |

Reporting those as `FAIL` mixes them into `PASS=1050 FAIL=3`, which is exactly how a real
regression gets hidden behind a permanently red case.

## Behaviour

- Parsing is shared: `scripts/sweep-exclude.ps1` (`Get-SweepExcludeEntries`,
  `Get-SweepExcludedCaseNames`, `Find-SweepExcludeEntry`) reproduces exactly the original
  `sweep-cases.ps1` block -- `Trim`, drop empty and `^#` lines, first token of
  `-split '\s+'`. All three consumers now call it, so they cannot drift.
- `scripts/difftest.ps1` on an excluded case does **not** run the comparison. It prints
  `ok=SKIP`, the entry's `# ...` comment as the reason and the list path, writes the same to
  `<case>.<compiler>.diffreport.txt|json`, and exits **0** (SKIP is not a failure).
  `LOUD contract` rows are intentionally excluded from this early return: their non-zero
  exit *is* the assertion and `scripts/loud-gate.ps1` drives `difftest.ps1` on them.
- `scripts/run-tests.ps1` skips the same cases and counts them **separately**:
  `PASS: n  FAIL: n  SKIP: n  TOTAL: n`, plus a `Skipped cases (...)` list with the reason
  of each entry, also written to `-ResultsFile` (`skipped=...` plus a `SKIP<TAB>case<TAB>reason`
  row). PASS no longer absorbs them, so the pass ratio stays meaningful.
- Skipping is never silent: every consumer prints the reason. Adding/removing a case means
  editing one line in `sweep-exclude.txt`, so a reviewer sees the waiver in the diff.
- `scripts/run-tests.ps1` also accepts `-Cases a,b,c` (comma-separated subset of the
  directory pool) so a mixed batch can be verified without sweeping all cases. The
  parameter is `[string[]]`, so both `-Cases a,b` (PowerShell binds a two-element array)
  and `-Cases "a,b"` (one string) are accepted; a `[string]` declaration would have joined
  the array with spaces and silently broken the unquoted form.

## Regression guard

Removing `p-adb-loud` from the list makes `difftest.ps1 -Case p-adb-loud` return
`ok=False` again with the two known DIFF rows, i.e. SKIP is triggered by the list entry and
not by a broken harness. The six byte-exact anchors are unaffected
(`probe-lasterror 86c8e322`, `probe-handleref a089ccbf`, `probe-intptr 2e1ae3f0`,
`probe-typedref c978f5b3`, `probe-p0a 73083dbc`, `probe-listrange 8179a76a`).

## W11 F2-alpha: honest de-label of `-Compiler msvc` (difftest)

`scripts/difftest.ps1` used to accept `-Compiler msvc` (ValidateSet
`"mingw","msvc","clang"`) even though its build loop has only two arms:
`clang` (clang-mingw toolchain) and `else` (`-G "MinGW Makefiles"`). There is
no vcvars/cl/VS-generator arm in this harness. Because the top-level
`Resolve-ToolDir` PATH injection makes that `else` arm succeed, `-Compiler msvc`
did NOT crash -- it built a **MinGW** binary and then wrote evidence/report
files named `*.msvc.*` with `compiler=msvc` (both the JSON report and the txt
report). That is a stable fake-label green, not a real MSVC comparison.

- Fix: `difftest.ps1` now uses `ValidateSet("mingw","clang")`. `-Compiler msvc`
  is rejected up front; no `.msvc.*` fake-label artifact can be written.
  `scripts/diff-smoke.ps1` (which drives difftest and writes `smoke-summary.tsv`)
  uses the same two-value set, so a batch smoke run cannot re-introduce the
  fake `compiler=msvc` rows.
- The **real MSVC channel is unaffected**: `scripts/ci.ps1` runs
  `scripts/run-tests.ps1 -Compiler msvc`, which loads `scripts/vcvars.ps1` and
  builds with `-G "Visual Studio 17 2022"`. That path never calls difftest.ps1.
  `sweep-cases.ps1` and the other sweep scripts also drive run-tests.ps1, so
  their `-Compiler msvc` handling is untouched.
- `smoke-summary.tsv` has no committed instance in this tree (checked 2026-11-13);
  the 6 historical `.opencode/tmp/dr-exec-msvc/loud-linq-decorderbygrp.msvc.*`
  files (mtime 2026-11-12 20:31, `compiler=msvc ok=True`) are MinGW binaries
  with a false label. They are untracked historical artifacts, NOT current-HEAD
  green evidence; keep them only as forensic samples and do not cite them as
  MSVC proof. A future real MSVC difftest channel (F2-beta, out of scope here)
  must use distinct artifact names so new/old evidence cannot be confused.
- Dynamic config preserved (J8): `-Compiler / -ReportDir / -TimeoutMs / -AppArgs`
  parameters and the `IL2CPP_DIFFTEST_LOCK_MS` env override all remain;
  `IL2CPP_MSVC_GENERATOR` (in build-app.ps1 / run-tests path) is untouched.
