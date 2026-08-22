# E2E Report — e2e_conv (Convert roundtrips)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_conv\`
Pipeline: C# → il2cppc transpile → MinGW cmake native build → run

## Inputs

- net9 refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll`
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Step 1 — C# source (`Program.cs`, namespace E2eConvProbe)

Checks implemented (17 total):
1. `Convert.ToInt32(string)`: "123"→123, "-456"→-456
2. `Convert.ToInt32(int)`: identity on int.MaxValue / int.MinValue
3. `Convert.ToInt32(bool)`: true→1, false→0
4. `Convert.ToInt32(double)`: round-half-to-even — 3.7→4, 2.5→2, 3.5→4, -2.5→-2
5. `Convert.ToBoolean(int)`: 0→false; 1/42/-1→true
6. `Convert.ToString(int)`: 789→"789", -42→"-42"
7. `Convert.ToString(long)`: long.MaxValue→"9223372036854775807", -9034567890123456789 verbatim
8. `Convert.ToString(double)`: 2.5→"2.5", 0.1→"0.1", 123.456→"123.456"
9. `Convert.ToString(bool)`: true→"True", false→"False"
10. `Convert.ToInt64(string)`: "9223372036854775806"→long.MaxValue-1, "-123456789012"→-123456789012
11. `Convert.ToDouble(string)`: "3.14"→3.14, "-0.5"→-0.5
12. Roundtrip int: 123456789 → ToString → ToInt32 → equal
13. Roundtrip long: -9034567890123456789 → ToString → ToInt64 → equal
14. Roundtrip double: 123.456 → ToString → ToDouble → bit-equal
15. Roundtrip bool: true → ToInt32(=1) → ToBoolean(=true); false chain stays false

Prints one `PASS ...` line per check, then `ALL-PASS` (or `FAIL ...` / `ALL-FAIL failures=N`). Main returns failures count as exit code.

## Step 2 — Compile against net9 runtime assemblies

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -noconfig -nostdlib `
  -target:exe -out:out\e2e_conv.exe `
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll `
  Program.cs
```

Result: csc exit=0 → `out\e2e_conv.exe` (+ handwritten `out\e2e_conv.runtimeconfig.json` for the managed oracle).

## Step 3 — Managed oracle run (net9 runtime)

Command: `E:\il2cpp\tmp\net910\net9\dotnet.exe out\e2e_conv.exe`

```
PASS ToInt32(string)=123
PASS ToInt32(string-neg)=-456
PASS ToInt32(int)=2147483647
PASS ToInt32(int-min)=-2147483648
PASS ToInt32(bool)=1/0
PASS ToInt32(double)=4/2/4/-2
PASS ToBoolean(int)=F/T/T/T
PASS ToString(int)=789/-42
PASS ToString(long)=9223372036854775807/-9034567890123456789
PASS ToString(double)=2.5/0.1/123.456
PASS ToString(bool)=True/False
PASS ToInt64(string)=9223372036854775806/-123456789012
PASS ToDouble(string)=3.14/-0.5
PASS roundtrip int=123456789
PASS roundtrip long=-9034567890123456789
PASS roundtrip double=123.456
PASS roundtrip bool=T/T
ALL-PASS
managed exit=0
```

## Step 4 — Transpile

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_conv.exe gen
```

Output tail: `generated 1 type(s) -> gen`; exit=0.
Notes from transpiler log: string pool 40 distinct literals from 65 sites (25 duplicate sites deduped), 143 temp slots → 105 with reuse.

## Step 5 — cmake configure + MinGW build

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe; build exit=0
```

## Step 6 — Native run

Command: `build_mingw\il2cpp_gen_exe.exe`

```
PASS ToInt32(string)=123
PASS ToInt32(string-neg)=-456
PASS ToInt32(int)=2147483647
PASS ToInt32(int-min)=-2147483648
PASS ToInt32(double)=4/2/4/-2
PASS ToBoolean(int)=F/T/T/T
PASS ToString(int)=789/-42
PASS ToString(long)=9223372036854775807/-9034567890123456789
PASS ToString(double)=2.5/0.1/123.456
PASS ToString(bool)=True/False
PASS ToInt64(string)=9223372036854775806/-123456789012
PASS ToDouble(string)=3.14/-0.5
PASS roundtrip int=123456789
PASS roundtrip long=-9034567890123456789
PASS roundtrip double=123.456
PASS roundtrip bool=T/T
ALL-PASS
native exit=0
```

## Byte-exact comparison

Managed stdout vs native stdout captured to files and compared with
`Enumerable.SequenceEqual`: **managed bytes=594 native bytes=594 identical=True**.

## Results

| Check | Managed (net9) | Native (il2cpp) |
|---|---|---|
| ToInt32(string) ±values | PASS | PASS |
| ToInt32(int) MaxValue/MinValue | PASS | PASS |
| ToInt32(bool) 1/0 | PASS | PASS |
| ToInt32(double) half-to-even 4/2/4/-2 | PASS | PASS |
| ToBoolean(int) F/T/T/T | PASS | PASS |
| ToString(int) "789"/"-42" | PASS | PASS |
| ToString(long) MaxValue + negative | PASS | PASS |
| ToString(double) 2.5/0.1/123.456 | PASS | PASS |
| ToString(bool) True/False | PASS | PASS |
| ToInt64(string) near-MaxValue/negative | PASS | PASS |
| ToDouble(string) 3.14/-0.5 | PASS | PASS |
| roundtrip int | PASS | PASS |
| roundtrip long | PASS | PASS |
| roundtrip double | PASS | PASS |
| roundtrip bool | PASS | PASS |

**RESULT: ALL-PASS — 17/17 checks passed in the native generated executable, stdout byte-identical to the net9 managed oracle (594 bytes), exit code 0.**
