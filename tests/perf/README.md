# tests/perf -- repeated-sample performance gate

Multi-run sibling of `tests/bench` (which stays single-shot and unchanged).
The perf suite answers one question with a hard gate: **does the transpiled
output stay within `ratio >= 1.20` and `CV < 5%` of the net48 reference?**

```powershell
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler msvc  -Runs 5
```

## Pipeline

1. `dotnet build tests/perf/perf.csproj -c Release -f net48 -o tests/perf/out`
   (the csproj targets **net48 only** -- no net8.0 apphost can shadow the
   assembly, unlike the multi-targeted `tests/bench/bench.csproj`).
2. `g++ -shared -O2 -o out/benchlib.dll tests/perf/benchlib/benchlib.cpp`
   (P/Invoke clock for the net48 side).
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
6. Median / CV / verdict per case -> CSV + raw capture.

## Output

`tests/perf/results/<stamp>-<compiler>.csv` (InvariantCulture, `,` decimal
point regardless of the OS locale):

```
name,net48_ms,ilc_ms,ratio,net48_cv,ilc_cv,runs,verdict
```

| column | meaning |
|---|---|
| `name` | case name from `expected.txt` |
| `net48_ms` | median of the `Runs` net48 samples |
| `ilc_ms` | median of the `Runs` transpiled samples |
| `ratio` | `net48_ms / ilc_ms` (ratio > 1 = transpiled is faster) |
| `net48_cv` | sample stdev / mean (n-1 denominator) |
| `ilc_cv` | same for the transpiled side |
| `runs` | number of valid runs (a failed parse aborts the whole case) |
| `verdict` | `PASS` / `PASS_RATIO_CV_ENV` / `FAIL:ratio` / `FAIL:cv` / `FAIL:zero-ms` (combinations joined with `+`) |

`tests/perf/results/raw-<stamp>-<compiler>.txt` keeps every raw name/ms pair,
the commit, the jitter setting and the CPU load before/after, so the CV can be
recomputed by hand.

**`tests/perf/results/` is local run output and is gitignored** (the repo-wide
`results/` rule). Committed evidence lives in **`tests/perf/baseline/`**:
`array-mingw-2026-10-21.{csv,raw.txt}` is the green baseline and
`array-mingw-2026-10-21-redproof.{csv,raw.txt}` the red proof of the same day.

## Verdicts

| verdict | meaning | exit |
|---|---|---|
| `PASS` | ratio >= 1.20 and max(CV) < 5% and both medians > 0 | 0 |
| `PASS_RATIO_CV_ENV` | ratio and zero-ms gates met, CV over 5% on a **non-isolated** host (other builds running -- PROTOCOL G2 requires an isolated host for a CV claim). Never reported as `PASS`, never as `FAIL`; the load readings in the raw file are the evidence. | 3 |
| `FAIL:...` | a real gate miss (`ratio`, `cv`, `zero-ms`, or a combination) | 1 |
| red proof | `-RedProof` and `FAIL:ratio` observed (expected) | 1 |
| red proof | `-RedProof` but the gate still passed (harness bug) | 2 |

## Gate

Per case, all of:

1. `ratio >= 1.20` (median-based),
2. `max(net48_cv, ilc_cv) < 0.05`,
3. `net48_ms > 0` and `ilc_ms > 0` (a 0 ms transpiled reading means the loop
   was optimized away -- never a pass),
4. every run parsed and matched `expected.txt` order.

Exit codes: `0` = `PERF GATE: PASS`, `1` = gate failed, `2` = `-RedProof` ran
but the gate still passed (the red proof is not wired -- a harness bug).



## Red proofs (all executed; artifacts in `tests/perf/baseline/`)

**A -- ratio gate** (`-RedProof`): the generated side keeps the Release
configuration but gets `-DCMAKE_CXX_FLAGS_RELEASE=-O0`, i.e. the "optimizer
absent" condition the bench CMakeLists warns about. Measured
(`array-mingw-2026-10-21-redproof.*`): net48 median 1097 ms vs transpiled
median 2266 ms -> ratio 0.48 -> `FAIL:ratio`, exit 1. (A real
`CMAKE_BUILD_TYPE=Debug` configure does NOT link in this tree -- the generated
Debug targets hit undefined references from `src/runtime`, e.g.
`il2cpp_box_int32` / `icall_box_bool` -- hence the flag override.)

```powershell
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5 -RedProof
```

**B -- CV gate** (`PERF_JITTER_MS`): `tests/perf/benchlib/benchlib.cpp` adds a
pseudo-random `0..N` ms delay to every `now_us()` reading when the env var is
set, on **both** sides (the net48 DLL and the statically linked transpiled exe
read the same variable). The delay is seeded per process from the clock, so it
differs between the start and end reading and does not cancel in
`elapsed = t1 - t0`; the median ratio barely moves while the per-run spread
blows up -> `FAIL:cv`. The amplitude is absolute milliseconds, so size it
against the case duration (the `array` case is ~0.2 s on the transpiled side,
so use at least `PERF_JITTER_MS=3000`).

```powershell
$env:PERF_JITTER_MS=3000
pwsh -NoProfile -File scripts/run-perf.ps1 -Compiler mingw -Runs 5
Remove-Item Env:PERF_JITTER_MS
```

**C -- zero-ms sentinel**: shrink a case until it reports 0 ms and the verdict
becomes `FAIL:zero-ms`. Verification only; the workload must be restored
afterwards.

## Isolated-host requirement (PROTOCOL G2)

The CV gate is only meaningful on an idle machine. On a shared host running
other builds the net48 side alone can swing 0.01-0.19 CV, which is why the
script records `cpu_load_percent_before/after` in the raw file and reports
`PASS_RATIO_CV_ENV` (exit 3) instead of a fake `PASS`. Baseline run
`array-mingw-2026-10-21.*`: net48 1219/1040/1196/1152/1316 ms vs transpiled
202/197/201/208/180 ms, ratio 5.95, CV 0.085/0.054, load 77% -> 94%
(`PASS_RATIO_CV_ENV`). Re-run on an idle host for a clean `PASS`.

## Fast mode vs full mode

`-Runs 5` (default) is the full mode: 5 net48 + 5 transpiled processes, about
one minute end to end on this host once `build/perf_mingw` is warm (the first
run also builds the whole transpiled runtime, several minutes). For a quick
smoke test use `-Runs 3` (the gate's `runs` column records the actual count and
the minimum accepted is 3). The build directory is reused between runs, so only
codegen changes trigger a rebuild.

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

A `sort` case (heapsort, 4 KB x 120,000) was measured first and **rejected**:
net48 5034 ms vs transpiled 5379 ms (ratio 0.93) -- an honest `FAIL:ratio`,
i.e. not a case the transpiled output wins. It is not shipped as a gate case.
