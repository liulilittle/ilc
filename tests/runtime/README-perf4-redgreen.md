# ReadLine per-byte fseek removal -- red/green evidence (perf4)

Measured evidence for the streamio ReadLine optimisation (commits `6cffa4d5`,
`006e4c98`, `9f84f38b`, `d616aebc`, and this record-fix commit).

`.opencode/` is gitignored (`.gitignore:43`), so every artifact referenced
here lives next to this file under `tests/runtime/`.

## 1. Workload and toolchain

* Workload: 100,000 lines x 40 bytes = 4,000,000 bytes, read with
  `il2cpp_stream_read_line` to EOF. Line content is deterministic
  (`'a' + ((i + k) % 26)`), so the checksum is 100,000 x 39 = 3,900,000 and the
  final Position is 4,000,000.
* Toolchain: MinGW-w64 g++ 16.1.0, CMake "MinGW Makefiles", `-O3 -DNDEBUG`.
* Metric for timing: `QueryThreadCycleTime` cycles per pass (wall clock on the
  shared build machine is dominated by other agents' processes).

## 2. Deterministic counter -- primary evidence

Both sides expose the same counter symbol `g_streamioIdentitySeeks`. The RED
side is the pristine `27404191` source with the minimal probe patch in
`perf4-red-baseline.patch`:

```
+extern "C" { long long g_streamioIdentitySeeks = 0; int g_streamioForceSeekFailure = 0; }
...
         (void)STREAM_FSEEK(s->fp, 0, SEEK_CUR);
+        ++g_streamioIdentitySeeks;
         return std::getc(s->fp); // EOF -> -1
```

Run output (probe: 100,000-line fixture, one full `read_line` pass):

```
RED  : lines=100000 identity_seeks=4000001
GREEN: lines=100000 identity_seeks=0
```

`4,000,001` = one identity seek per byte (4,000,000) plus the final EOF probe.

## 3. Section 13h -- measured RED vs GREEN

### 3.1 How the RED build was produced (reproducible)

An independent tree is materialised from the baseline commit, the probe patch
is applied, and the CURRENT test file (the one containing section 13h) is
compiled against it. Nothing is hand-edited into the expected results.

```powershell
# 1. pristine baseline tree (27404191)
git archive 27404191 | tar -x -C <red-tree>

# 2. apply the probe patch from the repository root:
#    git apply tests/runtime/perf4-red-baseline.patch
#    (paths are src/runtime/src/streamio.cpp; verified with git apply --check)
#    -> defines g_streamioIdentitySeeks / g_streamioForceSeekFailure and
#       increments the counter before the per-byte identity fseek

# 3. bring in the current test file (section 13h lives there)
copy <repo>/tests/runtime/test_streamio.cpp <red-tree>/tests/runtime/

# 4. build with the probe macro on both TUs
cmake -S tests/runtime -B build_red -G "MinGW Makefiles" `
      -DCMAKE_BUILD_TYPE=Release `
      -DCMAKE_CXX_FLAGS="-DIL2CPP_STREAMIO_FSEEK_PROBE"
cmake --build build_red --target test_streamio -j 8
./build_red/test_streamio.exe
```

The GREEN side is the ordinary build of this tree:

```powershell
cmake -S tests/runtime -B tests/runtime/build_perf4
cmake --build tests/runtime/build_perf4 --target test_streamio
tests/runtime/build_perf4/test_streamio.exe
```

### 3.2 RED output (verbatim, `perf4-red-baseline-stdout.txt`)

```
FAIL: read_to_end immediately after a write (no seek) returns "" at EOF
FAIL: read_to_end after the rewind returns the exact written bytes
FAIL: a plain ReadLine pass over 4 MB issues ZERO identity seeks (was 4,000,001)
FAIL: the first EOF probe costs no seek (the explicit seek cleared the flags)
FAIL: a failed mode reset makes the read raise IOException (no UB read)
FAIL: the recovering seek leaves no pending reset (zero seeks on the read)
6 streamio assertion(s) failed
```

Exit code: **1**. Six failures, exactly as listed -- no entry is inferred.

### 3.3 GREEN output (verbatim)

```
all runtime streamio tests passed
```

Exit code: **0**.

### 3.4 Why the two read_to_end assertions really fail on the baseline

An auditor expected those two to pass, reasoning that the baseline's
`read_to_end` is a plain `fread` loop and an empty read decodes to "". The
measurement disagrees, and the cause is a genuine baseline defect rather than a
test artifact: the baseline `read_to_end` file branch never performs the C11
7.21.5.3 write->read reset, so the `fread` that follows a write is undefined
behaviour and reads past the 12-byte file into the stdio buffer tail.

Diagnostic probe (`perf4-red-rte-diag.txt` for RED,
`perf4-green-rte-diag.txt` for GREEN), same program on both libraries:

```
RED
write=12
read_to_end_after_write len=4084 pos=4096
seek0=0
read_to_end_after_rewind len=4096 pos=4096

GREEN
write=12
read_to_end_after_write len=0 pos=12
seek0=0
read_to_end_after_rewind len=12 pos=12
```

So on the baseline:

* `read_to_end` immediately after a write returns a 4,084-unit string of NULs
  (position jumps to 4096) instead of "" -> the assertion FAILs for a real
  reason (the read was performed in an illegal stdio mode).
* After the rewind the same baseline call returns 4,096 units (the 12 real
  bytes followed by 4,084 NULs) instead of exactly "line1\nline2\n" -> also a
  real FAIL.

Both are therefore genuine red evidence, not new coverage that happens to pass.
The GREEN side returns 0 and 12 units respectively.

## 4. Timing (5 launches each, 5 in-process runs per launch, 1 pass per run)

CPU load was sampled per launch and is recorded because it exceeded the 50%
protocol note.

RED (`27404191`):

```
launch 1 (load=57%):  wall median=13630.961 ms  cpu median=13593.750 ms  cycle median=33860454874  cv=1.99%
launch 2 (load=65%):  wall median=12982.811 ms  cpu median=12937.500 ms  cycle median=32263167097  cv=6.88%
launch 3 (load=99%):  wall median=16479.954 ms  cpu median=16421.875 ms  cycle median=40915860296  cv=1.86%
launch 4 (load=93%):  wall median=15899.374 ms  cpu median=15796.875 ms  cycle median=39526098425  cv=0.83%
launch 5 (load=93%):  wall median=17230.367 ms  cpu median=17171.875 ms  cycle median=42718747799  cv=2.47%
```

GREEN (current tree):

```
launch 1 (load=94%):  wall median=173.306 ms  cpu median=171.875 ms  cycle median=428847335  cv=1.77%
launch 2 (load=100%): wall median=152.050 ms  cpu median=156.250 ms  cycle median=377953327  cv=3.20%
launch 3 (load=96%):  wall median=174.484 ms  cpu median=171.875 ms  cycle median=410887567  cv=6.58%
launch 4 (load=93%):  wall median=174.322 ms  cpu median=171.875 ms  cycle median=432750763  cv=0.50%
launch 5 (load=97%):  wall median=173.923 ms  cpu median=156.250 ms  cycle median=415802235  cv=4.39%
```

Median of launch medians: RED 39,526,098,425 cycles vs GREEN 415,802,235
cycles (~95x on that pairing; the exact multiplier depends on host load). The
conclusion rests on the counter (4,000,001 -> 0), not on the ratio.

## 5. Macro propagation

Section 13h is guarded by `IL2CPP_STREAMIO_FSEEK_PROBE`. Both translation units
must see it or the section would be compiled out:

```
CMakeFiles/test_streamio.dir/flags.make          CXX_DEFINES = -DIL2CPP_STREAMIO_FSEEK_PROBE
runtime/CMakeFiles/il2cpp_runtime.dir/flags.make CXX_DEFINES = -DIL2CPP_STREAMIO_FSEEK_PROBE
```

`findstr` on `test_streamio.cpp.obj` finds `plain ReadLine pass`,
`identity seek` and `raise IOException`, confirming the assertions are really
compiled into the binary.

## 6. Artifacts in this directory

| file | contents |
|---|---|
| `perf4-red-baseline.patch` | minimal probe patch applied to the `27404191` source |
| `perf4-red-baseline-stdout.txt` | verbatim RED run output (6 FAILs, exit 1) |
| `perf4-red-rte-diag.txt` | RED read_to_end diagnostic (4084 / 4096 units) |
| `perf4-green-rte-diag.txt` | GREEN read_to_end diagnostic (0 / 12 units) |
