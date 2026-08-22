# E2E Report — e2e_gcmem (GC / memory-management program checks)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_gcmem\`
Pipeline: C# → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eGcMemProbe)

Checks implemented:
1. `GC.GetTotalMemory(false) > 0`
2. `GC.Collect()` completes without crash
3. `GC.MaxGeneration >= 0`
4. `GC.GetGeneration(new object()) >= 0`
5. Large array alloc (`int[16*1024*1024]`, 64 MB, boundary elements written + read back) then `GC.Collect()` again, no crash

Prints one `PASS ...` line per check, then `ALL-PASS` (or `ALL-FAIL failures=N`). Exit code 0 on success, 1 on any failure.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_gcmem.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_gcmem.exe` (+ hand-written `out\e2e_gcmem.runtimeconfig.json` for the managed oracle).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_gcmem.exe`

```
PASS GetTotalMemory(false)=51704
PASS Collect() completed
PASS MaxGeneration=2
PASS GetGeneration(new object())=0
PASS large array alloc len=16777216
PASS Collect() after large alloc
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_gcmem.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eGcMemProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*`.

Icall surface pre-checked present before the run (no fabricated APIs):
`MethodMap.GC.cs`: `Collect` → `icall_gc_collect` (:14-15), `GetTotalMemory` → `icall_gc_get_total_memory` (:30-31), `GetGeneration` → `icall_gc_get_generation` (:32), `get_MaxGeneration` → `il2cpp_gc_get_max_generation` (:75-76); implementations in `src/runtime/src/gcapi.cpp`.

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS GetTotalMemory(false)=1448
PASS Collect() completed
PASS MaxGeneration=2
PASS GetGeneration(new object())=0
PASS large array alloc len=16777216
PASS Collect() after large alloc
ALL-PASS
native exit=0
```

Stability: 3 repeat runs → each exit=0, last line `ALL-PASS`, 7 output lines.

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| GetTotalMemory(false) > 0 | PASS (51704) | PASS (1448) |
| GC.Collect() no crash | PASS | PASS |
| MaxGeneration >= 0 | PASS (2) | PASS (2) |
| GetGeneration(new object()) >= 0 | PASS (0) | PASS (0) |
| Large array (64 MB) alloc + integrity | PASS (len 16777216) | PASS (len 16777216) |
| Collect() after large alloc, no crash | PASS | PASS |

**RESULT: ALL-PASS — 6/6 checks passed in the native generated executable, exit code 0; stable across 3 repeat runs.**

No git operations performed; no files outside the exclusive dir + transcript touched.
