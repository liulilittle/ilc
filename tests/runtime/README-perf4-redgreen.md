# ReadLine per-byte fseek removal -- red/green evidence (perf4)

Controlled-path copy of the measurement evidence for commits `6cffa4d5`,
`006e4c98`, `9f84f38b` and the follow-up hygiene commit. `.opencode/` is
gitignored (`.gitignore:43`), so the evidence lives here instead.

## Workload and toolchain

* Workload: 100,000 lines x 40 bytes = 4,000,000 bytes, read with
  `il2cpp_stream_read_line` to EOF. Checksum (sum of line lengths) = 3,900,000,
  final Position = 4,000,000.
* Toolchain: MinGW-w64 g++ 16.1.0, `-O3 -DNDEBUG`, CMake "MinGW Makefiles".
* Metric: `QueryThreadCycleTime` cycles per pass (wall clock on the shared build
  machine is dominated by other agents' processes). CPU load was sampled per
  launch and is recorded below because it exceeded the 50% protocol note.

## Deterministic counter (primary evidence)

Same counter symbol (`g_streamioIdentitySeeks`, enabled by
`IL2CPP_STREAMIO_FSEEK_PROBE`) on both sides. The RED side is
`27404191:src/runtime/src/streamio.cpp` with one `++g_streamioIdentitySeeks;`
inserted immediately before the per-byte `fseek`, compiled into a separate
archive; the GREEN side is the current tree.

```
RED  : lines=100000 checksum=3900000 identity_seeks=4000001 pos=4000000
GREEN: lines=100000 checksum=3900000 identity_seeks=0       pos=4000000
```

`4000001` = one identity seek per byte (4,000,000) plus the EOF probe.

## Timing (5 launches each, 5 in-process runs per launch, 1 pass per run)

RED (streamio.cpp at `27404191`):

```
launch 1 (load=57%): wall median=13630.961 ms  cpu median=13593.750 ms  cycle median=33860454874  cv=1.99%
launch 2 (load=65%): wall median=12982.811 ms  cpu median=12937.500 ms  cycle median=32263167097  cv=6.88%
launch 3 (load=99%): wall median=16479.954 ms  cpu median=16421.875 ms  cycle median=40915860296  cv=1.86%
launch 4 (load=93%): wall median=15899.374 ms  cpu median=15796.875 ms  cycle median=39526098425  cv=0.83%
launch 5 (load=93%): wall median=17230.367 ms  cpu median=17171.875 ms  cycle median=42718747799  cv=2.47%
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
cycles (~95x on that pairing; the exact multiplier is host-load dependent).
The headline conclusion rests on the counter (4,000,001 -> 0), not the ratio.

## test_streamio section 13h: red -> green

Same test translation unit, same probe macro (both the runtime and the test
target get `-DIL2CPP_STREAMIO_FSEEK_PROBE` from
`tests/runtime/CMakeLists.txt`). The RED executable links
`test_streamio.cpp` against the instrumented `27404191` archive.

Commands:

```powershell
# GREEN (current tree)
cmake -S tests/runtime -B tests/runtime/build_perf4
cmake --build tests/runtime/build_perf4 --target test_streamio
tests/runtime/build_perf4/test_streamio.exe

# RED (instrumented baseline archive)
g++ -O3 -DNDEBUG -std=c++17 -DIL2CPP_STREAMIO_FSEEK_PROBE `
    -I src/runtime/include/internal `
    -o test_streamio_red.exe tests/runtime/test_streamio.cpp lib_red13h.a `
    -lws2_32 -ladvapi32 -lpsapi
./test_streamio_red.exe
```

RED output (exit code 1):

```
FAIL: read_to_end immediately after a write (no seek) returns "" at EOF
FAIL: read_to_end after the rewind returns the exact written bytes
FAIL: a plain ReadLine pass over 4 MB issues ZERO identity seeks (was 4,000,001)
FAIL: the first EOF probe costs no seek (the explicit seek cleared the flags)
FAIL: a failed mode reset makes the read raise IOException (no UB read)
FAIL: the recovering seek leaves no pending reset (zero seeks on the read)
6 streamio assertion(s) failed
```

GREEN output (exit code 0):

```
all runtime streamio tests passed
```

Macro propagation evidence (both `flags.make` files):

```
CMakeFiles/test_streamio.dir/flags.make       CXX_DEFINES = -DIL2CPP_STREAMIO_FSEEK_PROBE
runtime/CMakeFiles/il2cpp_runtime.dir/flags.make CXX_DEFINES = -DIL2CPP_STREAMIO_FSEEK_PROBE
```

and `findstr` on `test_streamio.cpp.obj` finds `plain ReadLine pass`,
`identity seek` and `raise IOException`, i.e. section 13h is really compiled
into the test binary.
