# E2E Report — e2e_sbfmt (StringBuilder AppendFormat / AppendLine / Insert-Remove-Replace chain)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_sbfmt\`
Pipeline: C# (net9 refs) → il2cppc transpile → MinGW cmake native build → run → diff

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- net9 host (managed oracle): `E:\il2cpp\tmp\net910\net9\dotnet.exe`
- csc: `C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

All 8 toolchain paths verified present before starting.

## Step 1 — C# source (`Program.cs`, namespace E2eSbFmtProbe)

Checks implemented:
1. `AppendFormat("{0} {1}", 42, "apples")` → `"42 apples"` (int + string placeholders)
2. `AppendFormat` returns the same builder instance (chaining support)
3. `Environment.NewLine == "\r\n"` (guard for newline checks)
4. `AppendLine()` on `"line1"` → `"line1\r\n"`
5. `AppendLine("hello")` on empty builder → `"hello\r\n"`
6. Chained edit sequence equals expected final string:
   `"Hello World"` → `Insert(5,",")` → `Remove(5,1)` → `Insert(11,"!")` → `Replace("World","il2cpp")` → `Replace('!','?')` ⇒ `"Hello il2cpp?"`
7. Grand finale — one builder through the whole sequence:
   `AppendFormat("[{0}|{1}]",7,"kit")` → `AppendLine()` → `Append("alpha-beta-gamma")` → `Remove(0,9)` → `Replace('-','_')` → `Insert(16,"!")` → `Remove(16,1)` → `Replace('_',' ')` → `AppendLine("end")`
   ⇒ `"alpha beta gammaend\r\n"`

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ... got=<escaped>` / `ALL-FAIL failures=N`). Exit code 0.

### Test-authoring fix during the run (v1 → v2)

v1's grand-finale expectation was miswritten by the test author: `expE = "alpha beta gamma\r\nend\r\n"`.
Managed oracle correctly failed it with `got=alpha beta gammaend\r\n`. Root cause: `AppendLine(string)` is
`Append(value).AppendLine()` — the newline goes AFTER the value, not before. .NET behavior was correct;
the expected constant was fixed to `"alpha beta gammaend\r\n"` (original kept as `Program.cs.v1.bak`).
Raw-byte inspection of `expected.txt` confirmed no console-rendering artifact before any change.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_sbfmt.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_sbfmt.exe` (+ hand-written `out\e2e_sbfmt.runtimeconfig.json`, framework Microsoft.NETCore.App 9.0.19).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_sbfmt.exe` → captured to `expected.txt`

```
PASS AppendFormat {0} {1} int+string
PASS AppendFormat returns same builder
PASS Environment.NewLine is CRLF
PASS AppendLine()
PASS AppendLine(string)
PASS chained Insert/Remove/Replace
PASS grand finale sequence
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_sbfmt.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eSbFmtProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*`.

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe` → captured to `actual.txt`

```
PASS AppendFormat {0} {1} int+string
PASS AppendFormat returns same builder
PASS Environment.NewLine is CRLF
PASS AppendLine()
PASS AppendLine(string)
PASS chained Insert/Remove/Replace
PASS grand finale sequence
ALL-PASS
native exit=0
```

Diff: `Compare-Object expected.txt actual.txt` → empty → **RESULT=PASS lines=8**

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| AppendFormat `{0} {1}` int+string | PASS | PASS |
| AppendFormat returns same builder | PASS | PASS |
| Environment.NewLine is CRLF | PASS | PASS |
| AppendLine() | PASS | PASS |
| AppendLine(string) | PASS | PASS |
| Chained Insert/Remove/Replace == expected final string | PASS | PASS |
| Grand finale full sequence == expected final string | PASS | PASS |

**RESULT: ALL-PASS — 7/7 checks passed in the native generated executable, byte-identical to the net9 managed oracle (8/8 lines), exit code 0.**
