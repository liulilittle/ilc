# E2E Report — e2e_env (Environment program checks)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_env\`
Pipeline: C# → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eEnvProbe)

Checks implemented:
1. `Environment.ProcessorCount > 0`
2. `Environment.CurrentManagedThreadId > 0`
3. `Environment.TickCount64 >= Environment.TickCount` (TickCount read first, TickCount64 second; both monotonic ms-since-boot)
4. `Environment.NewLine` nonempty
5. `Environment.ExitCode = 42` → get == 42 → reset to 0 → get == 0

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`). ExitCode reset to 0 at end so process exit stays 0.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_env.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_env.exe` (+ `out\e2e_env.runtimeconfig.json` for the managed oracle).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_env.exe`

```
PASS ProcessorCount=32
PASS CurrentManagedThreadId=2
PASS TickCount64>=TickCount (57134406 >= 57134406)
PASS NewLine.Length=2
PASS ExitCode set/get=42
PASS ExitCode reset/get=0
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_env.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eEnvProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*`.

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS ProcessorCount=32
PASS CurrentManagedThreadId=965640
PASS TickCount64>=TickCount (57700765 >= 57700765)
PASS NewLine.Length=2
PASS ExitCode set/get=42
PASS ExitCode reset/get=0
ALL-PASS
native exit=0
```

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| ProcessorCount > 0 | PASS (32) | PASS (32) |
| CurrentManagedThreadId > 0 | PASS (2) | PASS (965640) |
| TickCount64 >= TickCount | PASS | PASS |
| NewLine nonempty | PASS (len 2) | PASS (len 2) |
| ExitCode set/get == 42 | PASS | PASS |
| ExitCode reset to 0 | PASS | PASS |

**RESULT: ALL-PASS — 6/6 checks passed in the native generated executable, exit code 0.**
