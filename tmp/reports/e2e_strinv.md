# E2E Report — e2e_strinv (string invariant program checks)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_strinv\`
Pipeline: C# (net9 refs) → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eStrInvProbe)

Checks implemented:
1. `"abc".ToUpperInvariant() == "ABC"`
2. `"ABC".ToLowerInvariant() == "abc"`
3. Insert/Remove chain: `"abcdef".Insert(2,"XY") == "abXYcdef"` then `.Remove(1,3) == "acdef"`
4. Split/Join roundtrip: `"a,b,c".Split(new[]{','})` → 3 parts a/b/c, `string.Join(",", parts) == "a,b,c"`
5. PadLeft/PadRight: `"7".PadLeft(3,'0')=="007"`, `"7".PadRight(3,'.')=="7.."`, `"abc".PadLeft(5)=="  abc"`, `"abc".PadRight(2)=="abc"` (width<length no-op)
6. Contains ordinal: `"hello world".Contains("lo w")`, `!"hello".Contains("HELLO")` (case-sensitive), `"hello".Contains("ell")`

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`). Exit 0 on success.

Overload support pre-verified in transpiler source: MethodMap.String.cs — ToUpperInvariant/ToLowerInvariant (:113-114), Insert/:116, Remove(int,int)/:118, PadLeft/PadRight :152-171, Contains(string) :173-181, Split(char[]) :346-348, Join(string,string[]) :382-383.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_strinv.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_strinv.exe` (+ hand-written `out\e2e_strinv.runtimeconfig.json`, tfm net9.0 / Microsoft.NETCore.App 9.0.19).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_strinv.exe`

```
PASS ToUpperInvariant abc->ABC
PASS ToLowerInvariant ABC->abc
PASS InsertRemove chain=acdef
PASS SplitJoin roundtrip=a,b,c
PASS Pad left=007 right=7..
PASS Contains ordinal present=True caseSensitive=True
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_strinv.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eStrInvProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*` (string pool: 38 distinct literals from 50 sites).

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS ToUpperInvariant abc->ABC
PASS ToLowerInvariant ABC->abc
PASS InsertRemove chain=acdef
PASS SplitJoin roundtrip=a,b,c
PASS Pad left=007 right=7..
PASS Contains ordinal present=True caseSensitive=True
ALL-PASS
native exit=0
```

## Step 7 — Output comparison

`Compare-Object expected.txt actual.txt`: no differences; byte counts equal (expected=221, actual=221).

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| ToUpperInvariant "abc"→"ABC" | PASS | PASS |
| ToLowerInvariant "ABC"→"abc" | PASS | PASS |
| Insert/Remove chain → "acdef" | PASS | PASS |
| Split(new[]{','}) + Join roundtrip | PASS | PASS |
| PadLeft/PadRight (pad char/space/short-width) | PASS | PASS |
| Contains ordinal (present/case-sensitive) | PASS | PASS |

**RESULT: ALL-PASS — 6/6 checks passed in the native generated executable, output byte-identical to the net9 managed oracle, exit code 0.**
