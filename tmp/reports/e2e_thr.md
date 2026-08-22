# E2E Report — e2e_thr (Thread program checks)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_thr\`
Pipeline: C# → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eThrProbe)

Checks implemented:
1. `Thread.Sleep(0)` returns without error
2. `Thread.SpinWait(100)` returns without error
3. `Thread.CurrentThread.ManagedThreadId > 0`
4. Start a `Thread` running a static worker that sets static bool `WorkerFlag = true`; main thread `Join()`s it, then asserts `WorkerFlag` is set

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`). Exit code 0 on all-pass, 1 otherwise.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_thr.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  -r:<net9>\System.Threading.Thread.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_thr.exe` (+ `out\e2e_thr.runtimeconfig.json` for the managed oracle).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_thr.exe`

```
PASS Thread.Sleep(0)
PASS Thread.SpinWait(100)
PASS ManagedThreadId=2
PASS worker thread set flag after Join
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_thr.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eThrProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*`.

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0 (Threads: pthread found)
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS Thread.Sleep(0)
PASS Thread.SpinWait(100)
PASS ManagedThreadId=1043804
PASS worker thread set flag after Join
ALL-PASS
native exit=0
```

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| Thread.Sleep(0) no error | PASS | PASS |
| Thread.SpinWait(100) no error | PASS | PASS |
| CurrentThread.ManagedThreadId > 0 | PASS (2) | PASS (1043804) |
| Worker thread sets static flag, Join, flag set | PASS | PASS |

**RESULT: ALL-PASS — 4/4 checks passed in the native generated executable, exit code 0.**
