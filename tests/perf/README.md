# tests/perf -- repeated-sample performance gate

Multi-run sibling of `tests/bench` (which stays single-shot and unchanged).
The perf suite turns "is the transpiled output fast enough?" into a mechanical
gate: **ratio = median(net48_ms) / median(ilc_ms) >= 1.20, CV < 5%**, over at
least 5 runs per side.

```powershell
# acceptance run (the only mode that can return PASS):
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5
# MSVC (UNVERIFIED: msvc path not exercised on this host -- see below):
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler msvc  -Runs 5
```

## Pipeline

1. `dotnet build tests/perf/perf.csproj -c Release -f net48 -o tests/perf/out`
   (the csproj targets **net48 only** -- no net8.0 apphost can shadow the
   assembly, unlike the multi-targeted `tests/bench/bench.csproj`).
2. Build the P/Invoke clock as a DLL next to `perf.exe`: `g++ -shared -O2` for
   MinGW, `cl /LD /O2` for MSVC (same toolchain family as the generated side).
3. Run `out/perf.exe` **Runs** times -> net48 samples.
4. Transpile `out/perf.exe` with `il2cppc` -> `tests/perf/gen/`.
   The generated project discovers the native module by walking up from the
   assembly directory to `<module>/CMakeLists.txt`, so it emits
   `add_subdirectory(../benchlib ...)` and links `benchlib` into
   `il2cpp_gen` -- the same mylib pattern `tests/bench` uses. The script
   verifies that line is present in the generated CMakeLists.
5. `cmake -S tests/perf -B build/perf_<c> -DCAPILLIB_DIR=<gen>`
   (+ `-DCMAKE_BUILD_TYPE=Release` / `--config Release`), then run
   `il2cpp_gen_exe` **Runs** times -> transpiled samples.
6. Median / CV / ratio interval / verdict per case -> CSV + raw capture.

Both sides are Release (`-O3`), same source, and the transpiled side links the
same `benchlib` clock statically, so the only difference is the runtime.

## Output

`tests/perf/results/<stamp>-<compiler>.csv` (InvariantCulture -- `,` is the
column separator and `.` the decimal point regardless of the OS locale):

```
name,net48_ms,ilc_ms,ratio,ratio_lo,ratio_hi,net48_cv,ilc_cv,runs,smoke,verdict
```

| column | meaning |
|---|---|
| `name` | case name from `expected.txt` |
| `net48_ms` | median of the `Runs` net48 samples |
| `ilc_ms` | median of the `Runs` transpiled samples |
| `ratio` | `net48_ms / ilc_ms` (ratio > 1 = transpiled is faster) |
| `ratio_lo` | `min(net48) / max(ilc)` -- observed-extremes **lower bound** |
| `ratio_hi` | `max(net48) / min(ilc)` -- observed-extremes **upper bound** |
| `net48_cv` | sample stdev / mean (n-1 denominator) |
| `ilc_cv` | same for the transpiled side |
| `runs` | number of valid runs (a failed parse aborts the whole case) |
| `smoke` | `true` when `Runs < 5` -- that row is not acceptance evidence |
| `verdict` | see the table below |

`ratio_lo`/`ratio_hi` are a conservative envelope over the sampled extremes,
**not** a statistical confidence interval (5 samples cannot support one).

`tests/perf/results/raw-<stamp>-<compiler>.txt` records, per run: the exact
command line, the git commit **and dirty state**, the compiler version, the
cmake version, the dotnet SDK version, the il2cppc version **and its dirty
state**, the CPU model and core counts, the OS version, the process affinity
mask, the `PERF_JITTER_MS` value, the CMake cache flags actually used
(build type + CXX flags, so `-O0`/`/Od` is visible), whether the build
directory was fresh (`clean_build`), SHA256 of `perf.cs`, the generated
`Perf_Perf.cpp`, the transpiled exe and both net48 binaries, every raw name/ms
pair, the CPU load before and after, and whether the host qualified as isolated
(load <= 70%).

**`tests/perf/results/` is local run output and is gitignored** (the repo-wide
`results/` rule). Committed evidence lives in **`tests/perf/baseline/`** --
see "Committed baseline" below.

## Verdicts and exit codes

| verdict | exit | meaning |
|---|---|---|
| `PASS` | 0 | `Runs >= 5`, ratio >= 1.20, max(CV) < 5%, both medians > 0 |
| `FAIL:...` | 1 | a real gate miss: `ratio`, `cv`, `zero-ms`, or a combination (`+`) |
| `SMOKE` | 4 | `Runs < 5`: numbers are printed and saved, but this is **never an acceptance run** |
| `RATIO_OK_CV_OVER_LIMIT` | 3 | only with the explicit `-AllowEnvCv` switch **and** host load > 70%: ratio and zero-ms gates met, CV over 5%. Not a pass, not a code verdict. |
| red proof | 1 | `-RedProof` and a `ratio` reason observed (in practice `FAIL:ratio+cv`) |
| red proof | 2 | `-RedProof` but the gate still passed (the red proof is not wired: harness bug) |
| red proof | 5 | `-RedProof` failed, but **not** on ratio (inconclusive: the ratio red proof did not fire) |

The CV gate is **strict by default**: a CV miss is `FAIL:cv` / exit 1. The
`-AllowEnvCv` switch exists only to record "the host was busy" without
pretending it is a pass, and it refuses the attribution when the load readings
say the host was actually idle. Nothing here ever turns a CV miss into exit 0.

`-Runs 3` is a smoke test (exit 4). Use it while editing code; never quote its
numbers as acceptance evidence. The minimum accepted value is 3
(`ValidateRange(3, 51)`); 2 or less is rejected outright. Smoke rows carry
`smoke=true` in the CSV so a saved file can never be mistaken for acceptance.

`SMOKE` is evaluated **before** the red-proof branch: `-RedProof -Runs 3` exits
4, not 1/2/5.

`-RedProof` combined with `-AllowEnvCv` is judged on the verdict, not on a
partial "did something fail" flag: if the ratio gate still passes (`-O0` did not
move it) the run is **inconclusive** (exit 5) even when the CV gate missed.
Only a genuine ratio miss returns exit 1; a fully passing gate returns exit 2
(the red proof is not wired).

A CV above 1000% is reported as `cv-infra` (broken measurement, e.g. a killed
process or clock wrap), never as an environmental near-miss.

`-RedProofFlags <value>` overrides the flags `-RedProof` applies (`-O0` / `/Od`
by default); `-RedProofFlags none` keeps the Release flags. That is a harness
self-test hook: with `none` the red proof cannot change the code, so the script
must report "not wired" (exit 2) or "inconclusive" (exit 5) -- never claim a
pass. It is what proves the `-RedProof -AllowEnvCv` combination can no longer
produce the old false "the gate PASSED" message.

### MSVC status: UNVERIFIED

The MSVC branch (build `benchlib.dll` with `cl /LD`, configure with the VS17
generator, `-DCMAKE_CXX_FLAGS_RELEASE=/Od /DNDEBUG` for the red proof, read the
`cl` banner for the version) is implemented but **was not exercised on this
host** -- no MSVC run is committed. Treat `-Compiler msvc` as untested.

## Committed baseline (`tests/perf/baseline/`)

| file | what it is |
|---|---|
| `array-mingw-2026-10-21.csv` / `.raw.txt` | the `array` case, MinGW, 5 runs, `-AllowEnvCv`, commit `bc0382df` (clean), load 94% -> 100% -> `RATIO_OK_CV_OVER_LIMIT` (exit 3). Ratio 9.70, envelope 7.61-11.64, CV 0.0929/0.0798 |
| `array-mingw-2026-10-21-redproof.csv` / `.raw.txt` | same case, `-RedProof` (Release + `-O0`), commit `bc0382df` (clean) -> **`FAIL:ratio+cv`** (not `FAIL:ratio` alone), ratio 0.45, exit 1 |
| `array-mingw-2026-10-21-jitter.csv` / `.raw.txt` | `PERF_JITTER_MS=150`, commit `bc0382df` (clean) -> `FAIL:cv`, CV 0.0845/0.5848, exit 1. The ratio did **not** stay put: 9.70 -> 14.28 (ilc 146 -> 67 ms). Injected jitter is absolute, added to both clock readings, so a reading pair can move the elapsed time *down* as well as up -- the median ratio therefore shifts too. This artifact proves the CV gate detects injected jitter; it says nothing about sensitivity to a codegen regression. |
| `rejected-sort/` | the `sort` case that was **rejected**, plus its source snapshot and raw run (ratio 1.04, CV 0.17/0.14). Captured on a **dirty** tree (`753f0128`, 3 modified files) -> `UNVERIFIED (dirty tree)`, not reproducible from history |

The directory holds **three `array` runs (6 files) plus one `rejected-sort` run
(3 files)** -- not four array groups.

**There is no clean `PASS` on this host yet.** Every acceptance run recorded so
far has CPU load above 70% (other agents compiling in parallel), so the CV gate
cannot be satisfied and the best available verdict is
`RATIO_OK_CV_OVER_LIMIT`. Do **not** cite those rows as `PASS`. A clean `PASS`
requires an idle machine (PROTOCOL G2: isolated host) and a fresh run.

Only the files in `tests/perf/baseline/` are evidence. Numbers quoted from a
terminal that were never written into that directory (for example an earlier
ad-hoc `ratio 7.20`) are **not** evidence and must not be used.

## Red proofs (all executed; artifacts in `tests/perf/baseline/`)

**A -- ratio gate** (`-RedProof`): the generated side keeps the Release
configuration but gets `-DCMAKE_CXX_FLAGS_RELEASE=-O0` (`/Od` under MSVC), i.e.
the "optimizer absent" condition the bench CMakeLists warns about. Scope: this
proves **the threshold comparison and the failure path are wired**. It does
**not** prove sensitivity to a regression at the same optimization level --
that needs a codegen change, not a build-flag change.

```powershell
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5 -RedProof
```

(A real `CMAKE_BUILD_TYPE=Debug` configure does not link in this tree: the
generated Debug targets hit undefined references from `src/runtime`, e.g.
`il2cpp_box_int32` / `icall_box_bool`. Hence the flag override.)

**B -- CV gate** (`PERF_JITTER_MS`): `tests/perf/benchlib/benchlib.cpp` adds a
pseudo-random `0..N` ms delay to every `now_us()` reading when the env var is
set, on **both** sides (the net48 DLL and the statically linked transpiled exe
read the same variable). The delay is seeded per process from the clock, so it
differs between the start and end reading and does not cancel in
`elapsed = t1 - t0`; the median ratio barely moves while the per-run spread
blows up -> `FAIL:cv`. The amplitude is absolute milliseconds -- size it
against the case duration (the `array` case is ~0.2 s on the transpiled side,
so 150 ms is already enough).

```powershell
$env:PERF_JITTER_MS=150
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5
Remove-Item Env:PERF_JITTER_MS
```

**C -- zero-ms sentinel**: shrink a case until it reports 0 ms and the verdict
becomes `FAIL:zero-ms`. Verification only; the workload must be restored
afterwards.

## Isolated-host requirement (PROTOCOL G2)

The CV gate is only meaningful on an idle machine. On a shared host running
other builds the net48 side alone can swing 0.01-0.22 CV, which is why the raw
file records load before/after and why a CV miss stays a failure unless the
operator explicitly asks for the environmental attribution.

## Fast mode vs full mode

`-Runs 5` is the acceptance mode: 5 net48 + 5 transpiled processes, about a
minute end to end once `build/perf_mingw` is warm (the first invocation also
builds the whole transpiled runtime, several minutes). `-Runs 3` is the smoke
mode (exit 4, never acceptance). The build directory is reused between runs
(incremental), and the red-proof build has its own directory.

## Adding a case

1. Add `public static int MyBench()` in `tests/perf/perf.cs` (transpilable
   subset: no `Stopwatch`, no string concatenation, no `Array.Sort`, every
   allocation inside one GC segment, reference locals assigned before any
   allocating call).
2. Add a timed block in `Main()` printing **two lines** (`name`, then the
   integer milliseconds).
3. Add the name to `expected.txt`, in the same order as `Main()` prints.

## The case that is shipped

`array`: a 4 KB int array (1024 ints) is filled and XOR-folded 1,000,000 times
(2.0 billion element operations). The array is allocated once, stays
cache-resident, and the fill value depends on the round index so nothing can be
hoisted. Each process times the case repeatedly and reports the **minimum**
repetition (a warm-up is discarded; scheduler noise can only add time), for at
least 4 repetitions within a ~12 s budget. The harness then takes the median of
the 5 per-process minima.

## The case that was rejected

A `sort` case (heapsort, 4 KB x 120,000) was measured first and **rejected**:
net48 5034 ms vs transpiled 5379 ms (ratio 0.93) in an early exploratory run,
and a later 5-run pass measured ratio 1.04 with CV 0.17/0.14. Both are honest
`FAIL:ratio` (or `FAIL:ratio+cv`) results -- the transpiled output does not win
this case, so it is not shipped as a gate case.

Its source snapshot and raw run are kept in
`tests/perf/baseline/rejected-sort/`. **Caveat: that raw run was captured on a
dirty tree (`commit 753f0128 (dirty 3 files)`), so it cannot be rebuilt exactly
from history -- treat it as `UNVERIFIED (dirty tree)` evidence for the
rejection decision, not as a reproducible baseline.** The shipped `array`
artifacts are the reproducible ones (all captured on the clean tree of
`bc0382df`).
