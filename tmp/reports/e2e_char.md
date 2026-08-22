# E2E Report — e2e_char (System.Char program checks)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_char\`
Pipeline: C# → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eCharProbe)

Checks implemented:
1. `char.IsDigit('5')` is true
2. `char.IsLetter('a')` is true
3. `char.IsWhiteSpace(' ')` is true
4. `char.ToUpper('a') == 'A'`
5. `char.ToLower('Z') == 'z'`
6. char arithmetic: `(char)('a' + 1) == 'b'` (int-add then cast back to char)

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`). Exit code 0 on success.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_char.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_char.exe` (+ hand-written `out\e2e_char.runtimeconfig.json`, tfm net9.0 / Microsoft.NETCore.App 9.0.19, for the managed oracle).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_char.exe`

```
PASS IsDigit('5')
PASS IsLetter('a')
PASS IsWhiteSpace(' ')
PASS ToUpper('a')=='A
PASS ToLower('Z')=='z'
PASS ('a'+1)=='b'
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_char.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Generated: `gen/CMakeLists.txt`, `gen/src/E2eCharProbe_Program.cpp`, `gen/src/entry.cpp`, `gen/src/host.cpp`, `gen/include/internal/*`.

Icall mapping verified in generated C++ (`gen/src/E2eCharProbe_Program.cpp`):
- `char.IsDigit` → `il2cpp::runtime::icall_char_is_digit(char16_t)`
- `char.IsLetter` → `il2cpp::runtime::icall_char_is_letter(char16_t)`
- `char.IsWhiteSpace` → `il2cpp::runtime::icall_char_is_whitespace(char16_t)`
- `char.ToUpper` → `il2cpp::runtime::icall_char_to_upper(char16_t)`
- `char.ToLower` → `il2cpp::runtime::icall_char_to_lower(char16_t)`
- `(char)('a' + 1)` folded to the constant comparison `loc3 = 98; loc3 == 98` (semantics preserved).

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

(Compiler warnings during runtime build are pre-existing unused-parameter stubs noise; zero errors.)

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS IsDigit('5')
PASS IsLetter('a')
PASS IsWhiteSpace(' ')
PASS ToUpper('a')=='A
PASS ToLower('Z')=='z'
PASS ('a'+1)=='b'
ALL-PASS
native exit=0
```

## Byte-exact diff (managed vs native)

Both captures split on CRLF/LF, empty trailing lines dropped, joined with LF, UTF-8 encoded:

```
managed lines=7 exit=0
native  lines=7 exit=0
byte-identical=True
sha256 managed=54 17 87 a5 60 e4 45 92 72 d7 7c 6a c7 61 79 12 5c b7 c6 a6 fb 0b c2 c4 e7 a8 f0 f6 4a 75 96 0d
sha256 native =54 17 87 a5 60 e4 45 92 72 d7 7c 6a c7 61 79 12 5c b7 c6 a6 fb 0b c2 c4 e7 a8 f0 f6 4a 75 96 0d
```

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| char.IsDigit('5') | PASS | PASS |
| char.IsLetter('a') | PASS | PASS |
| char.IsWhiteSpace(' ') | PASS | PASS |
| char.ToUpper('a') == 'A' | PASS | PASS |
| char.ToLower('Z') == 'z' | PASS | PASS |
| (char)('a'+1) == 'b' | PASS | PASS |

**RESULT: ALL-PASS — 6/6 checks passed in the native generated executable, stdout byte-identical to the net9 managed oracle, exit code 0.**
