# tests/bench -- Performance benchmark suite

Fixed-workload benchmarks that run the **same** `bench.cs` on two runtimes:

1. **.NET Framework 4.8** (reference) -- `out/bench.exe` (net48 build).
2. **ILC transpiled output** -- the same source translated by `il2cppc` to C++,
   compiled with optimization, run as `il2cpp_gen_exe`.

The numbers answer: *how fast is the transpiled code relative to the net48
JIT today, and does it regress as the compiler evolves?*

## Usage

```powershell
# one-click pipeline (MinGW, default):
pwsh -NoProfile -File scripts/run-bench.ps1

# MSVC toolchain:
pwsh -NoProfile -File scripts/run-bench.ps1 -Compiler msvc
```

The script builds the fixture, transpiles, builds the generated C++ project
(via `CMakeLists.txt`, `CAPILLIB_DIR` pattern), runs both binaries, prints a
comparison table and writes `tests/bench/results/<date>.csv`
(`name,net48_ms,ilc_ms,ratio`; ratio = net48/ilc, **> 1 means the transpiled
build is faster**).

Manual reference run (net48 only):

```powershell
dotnet build tests/bench/bench.csproj -c Release -o tests/bench/out
g++ -shared -O2 -o tests/bench/out/benchlib.dll tests/bench/benchlib/benchlib.cpp
& tests/bench/out/bench.exe
```

Both runs must be **optimized builds** (`-DCMAKE_BUILD_TYPE=Release` /
`--config Release`, set by the harness): -O0 numbers measure the absence of
the optimizer, not the transpiled code.

## Benchmarks

Every benchmark does a **fixed amount of work** (time varies, work does not --
that is what makes the ratio meaningful):

| name   | workload                                                    |
|--------|-------------------------------------------------------------|
| fib    | `Fib(34)` naive recursion (int32; 5.7M calls)               |
| loop   | 50,000,000 xorshift32 updates + accumulation                |
| sort   | insertion sort of 12,000 deterministic ints                 |
| array  | 200 x (fill + sum a 400 KB int array) = 40M element ops     |
| hash   | 12 rounds of 28,000 insert + lookup in a 384 KB int table   |
| string | 20,000,000 x string materialization + `Length` read         |
| object | 5,000,000 x `new Item(i)` + instance method call (net48: JIT-eliminated)|

Workloads are sized so the net48 reference takes at least ~15 ms -- integer
millisecond output is then meaningful on both sides (the original suggestion
of fib(30)/1M-loop/10k-objects measured 0-4 ms on net48, which makes ratios
meaningless). Arrays are repeated in-place instead of enlarged because of an
ILC runtime limitation (see below).

All results fold into the static `checksum` sink: the writes are observable
(external linkage in the generated C++) so `-O3` cannot eliminate the loops;
the .NET JIT keeps them for the same reason. The pseudo-random data comes
from a deterministic xorshift32 (shifts/XOR/mask only -- no signed-overflow
undefined behavior in C++).

## Adding a new benchmark

1. Add a `public static int MyBench()` method in `tests/bench/bench.cs`
   (must stay inside the **transpilable subset**, see Limitations below).
   Return a value derived from the work done (it feeds `checksum`).
2. Add a timed section in `Main()`:

   ```csharp
   t0 = now_us();
   checksum += MyBench();
   t1 = now_us();
   Console.WriteLine("mybench");
   Console.WriteLine((t1 - t0) / 1000);
   ```

3. Add `"mybench"` to the `$Expected` list in `scripts/run-bench.ps1`.
4. Run the harness. If the transpiled numbers are implausible (0 ms), check
   whether `-O3` could legally eliminate the loop (e.g. pure arithmetic with
   no observable side effect) and add an observable sink.

## Limitations (documented downgrades)

The benchmark program is constrained to the M-stage transpilable subset --
`tests/bench/bench.cs` is the same source for both runtimes, and the
transpiler does not yet wire every BCL member. Verified gaps
(MethodRefResolver / il2cppc audit, 2026-08-03):

- **Stopwatch / DateTime / Environment.TickCount** are not transpilable ->
  timing uses the P/Invoke clock `benchlib.now_us()` (steady-clock
  microseconds, int32). `benchlib` lives in `tests/bench/benchlib/` and is
  built twice: a DLL for the net48 run, a static lib for the transpiled run.
- **String.Concat (`"x" + i`)** is not transpilable -> the string benchmark
  measures `ldstr` allocation + `String.Length` instead.
- **Array.Sort / Hashtable / System.Random** are not transpilable -> sort and
  hashtable are implemented in user code (same algorithm both sides, a fairer
  comparison); data comes from the deterministic xorshift32.
- **`"name: <ms>"` on one line needs String.Concat** -> each benchmark prints
  two lines (name, then integer ms); the harness pairs them.
- `System.Math`, `System.Random` and string icalls exist in the runtime but
  their transpiler wiring is pending; benchmarks avoid them.
- Only int32 value types are supported (no `long`/`double` locals).
- **ILC runtime limitation (2026-08-03, working-tree gc.cpp):** allocations
  larger than one segment payload (Gen0 64 KB / LOH 1 MB) overrun the
  segment (`AllocCore` bumps `used += size` without re-checking capacity
  after `AllocSegment`; a 4 MB array crashed in `ScanObject`). Benchmarks
  keep every allocation inside one segment (<= 48 KB Gen0 or <= 400 KB LOH)
  and reach the workload by repeating the operation. The fix belongs to the
  GC domain; benchmarks lift the limit automatically once fixed.
- **ILC transpiler limitation (2026-08-03, CodeGenerator WrapFrameRoots):**
  reference locals are registered as GC roots at function entry, but the
  slots are uninitialized until first use; a collection running during an
  allocation BEFORE the refs are assigned scans garbage and crashes
  (reproduced with hash's second LOH array: `ScanObject(obj=0x9)`). The
  benchmark order and sizes avoid the trigger (total LOH under one segment,
  first Gen0 allocation while the segment is fresh, pressure benchmarks
  last). The fix (zero-initialize ref slots before frame push) belongs to
  the transpiler domain; benchmarks run safely regardless once fixed.
- **net48 string folding:** the string benchmark's per-iteration values are
  loop-invariant literals from the JIT's perspective; the net48 JIT hoists
  them and runs ~2 ns/iteration while the ILC side allocates every
  iteration. The 100x+ gap is real (optimizer gap), not an artifact.
- **net48 object-churn folding:** same story for `new Item(i).Get()` -- the
  JIT inlines ctor + Get, proves the object never escapes and eliminates
  the allocation (~6 ns/iteration); the ILC side allocates every iteration.
  No transpilable construct forces the object to escape (reference-typed
  static fields / reference arrays are not yet supported by the
  transpiler), so this is accepted and documented as an optimizer-gap
  probe.

## Layout

```
tests/bench/
  bench.cs            benchmark source (net48 + transpilable subset)
  bench.csproj        net48 project (string.csproj template)
  CMakeLists.txt      C++ build driver (CAPILLIB_DIR pattern, Release)
  benchlib/           native timing source (P/Invoke clock)
  out/                net48 build output (generated)
  gen/                transpiler output (generated)
  results/            comparison CSVs (generated)
scripts/run-bench.ps1 one-click harness
```
