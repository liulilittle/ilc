# E2E Transcript — e2e_gc (GC.GetTotalAllocatedBytes / MaxGeneration)

- Date: 2026-08-22
- Agent dir (exclusive): `E:\il2cpp\tmp\e2e_gc\`
- Pipeline standard: **net9 refs → il2cppc transpiler → MinGW native build → run**
- Constraints honored: NO git operations; no `.opencode` artifacts created.

## Program under test (spec-mandated)

```csharp
using System;

class Program
{
    static void Main()
    {
        GC.Collect();
        long a = GC.GetTotalAllocatedBytes();
        object o = new string('x', 100);
        GC.Collect();
        long b = GC.GetTotalAllocatedBytes();
        Console.WriteLine(a >= 0 && b >= a ? "ALL-PASS" : "FAIL");
        int mg = GC.MaxGeneration;
        Console.WriteLine("MaxGeneration=" + mg + " ok=" + (mg >= 0));
    }
}
```

API note: the spec's `GetMaxGeneration()` is the net48 method alias. On the net9
reference surface the accessor is the `GC.MaxGeneration` property getter
(`get_MaxGeneration`). Both names are wired to the same icall in the transpiler —
[READ:E:\il2cpp\src\il2cppc\Emit\MethodMap.GC.cs:75-76]
(`case "get_MaxGeneration": case "GetMaxGeneration": return I("il2cpp::il2cpp_gc_get_max_generation", ...)`).
Semantics identical; adaptation is purely the net9 API shape.
`GetTotalAllocatedBytes(bool precise=false)` maps to
`il2cpp_gc_get_total_allocated_bytes_approximate` with the arg dropped
([READ:E:\il2cpp\src\il2cppc\Emit\MethodMap.GC.cs:40]); runtime impl at
gc.cpp:1969 (`g_allocatedBytes` cumulative counter); max-generation impl at
gc.cpp:1920.

## Pipeline steps (all commands run in `E:\il2cpp\tmp\e2e_gc`)

1. **net9 refs acquisition** — reused the on-disk extraction of
   `Microsoft.NETCore.App.Ref 9.0.6` (nupkg from nuget.org, present in
   `%USERPROFILE%\.nuget\packages\microsoft.netcore.app.ref\9.0.6` and already
   extracted by a sibling agent at `tmp\e2e_sb\ref9pkg`). Copied
   `mscorlib.dll`, `System.Runtime.dll`, `System.Console.dll` from
   `ref/net9.0/` into `ref9\`.
   - Dead end documented: MSBuild route (`<TargetFramework>net8.0</TargetFramework>`
     + PackageReference Microsoft.NETCore.App.Ref 9.0.6) fails restore with
     NU1202/NU1213 (package supports net9.0 only; DotnetPlatform package type).
     SDK 8.0.205 cannot target net9.0 directly. Same failure occurred in the
     sibling agent's `tmp\e2e_sb\obj\project.nuget.cache` (`"success": false`).
   - Working route: direct Roslyn invocation against the ref pack DLLs.

2. **Compile C# → IL exe against net9 refs**:
   ```
   dotnet <sdk>\Roslyn\bincore\csc.dll -nologo -noconfig -nostdlib -target:exe `
     -out:out\e2e_gc.exe -langversion:latest Program.cs `
     -r:ref9\mscorlib.dll -r:ref9\System.Runtime.dll -r:ref9\System.Console.dll
   ```
   Result: `out\e2e_gc.exe` (4096 bytes), exit 0.

3. **Transpile**:
   ```
   dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_gc.exe gen
   ```
   Result: exit 0; generated `gen\CMakeLists.txt`, `gen\src`, `gen\include`
   ("generated 1 type(s) -> gen").

4. **MinGW native build**:
   ```
   cmake -S gen -B build_mingw -G "MinGW Makefiles"     # configure OK
   cmake --build build_mingw                            # [100%] Built target il2cpp_gen_exe
   ```
   Toolchain: cmake 3.22.1; gcc = clang 17.0.6 (`C:\Program Files\ming-w64\bin`).
   Result: `build_mingw\il2cpp_gen_exe.exe` (687,104 bytes), exit 0.

5. **Run (native)**:
   ```
   .\build_mingw\il2cpp_gen_exe.exe
   ```
   Output (exit code 0):
   ```
   ALL-PASS
   MaxGeneration=2 ok=True
   ```

6. **Managed oracle (same IL on real .NET 9 runtime)** — cross-check that the
   transpiled behavior matches CoreCLR 9.0.19 (`E:\il2cpp\tmp\net910\net9\dotnet.exe`,
   same csc-produced IL repackaged as `oracle\e2e_gc.dll` + net9.0 runtimeconfig):
   ```
   ALL-PASS
   MaxGeneration=2 ok=True
   ```
   Byte-identical to the native output.

## Verification matrix

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1 | Program per spec (Collect/GetTotalAllocatedBytes×2/string alloc/Collect/ALL-PASS gate) | COMPLETE | Program.cs above; compile+run logs in this transcript |
| 2 | Native prints `ALL-PASS` (a>=0 && b>=a) | COMPLETE | native run output line 1, exit=0 |
| 3 | GetMaxGeneration()>=0 printed | COMPLETE | native run output line 2: `MaxGeneration=2 ok=True` |
| 4 | net9 refs pipeline leg | COMPLETE | csc against Microsoft.NETCore.App.Ref 9.0.6 `ref/net9.0` DLLs |
| 5 | Transpiler pipeline leg | COMPLETE | il2cppc Release net8.0 dll, exit 0, gen/ emitted |
| 6 | MinGW pipeline leg | COMPLETE | MinGW Makefiles configure + build exit 0, exe linked |
| 7 | Managed-vs-native behavioral match | COMPLETE | identical 2-line outputs on CoreCLR 9.0.19 vs transpiled exe |
| 8 | Exclusive dir discipline / no git / no .opencode | COMPLETE | all artifacts under `E:\il2cpp\tmp\e2e_gc\`; zero git commands; no .opencode writes |

Result: **7/7 functional requirements verified PASS (+ discipline item 8).**

## Artifacts

- `E:\il2cpp\tmp\e2e_gc\Program.cs` — program under test
- `E:\il2cpp\tmp\e2e_gc\out\e2e_gc.exe` — net9-refs IL assembly
- `E:\il2cpp\tmp\e2e_gc\gen\` — transpiled CMake project
- `E:\il2cpp\tmp\e2e_gc\build_mingw\il2cpp_gen_exe.exe` — MinGW native binary
- `E:\il2cpp\tmp\e2e_gc\oracle\e2e_gc.dll` + `managed_net9.txt` — managed oracle capture
- `E:\il2cpp\tmp\e2e_gc\actual.txt` — native output capture

## Notes / learnings for future e2e agents

- "net9 refs" working recipe: extract `microsoft.netcore.app.ref` nupkg, hand
  `csc.dll -nostdlib -r:` the needed `ref/net9.0` facades. Do NOT bother with
  dotnet restore of the package into a net8.0-TFM project (NU1202 hard-fails).
- On net9 refs, `GC.MaxGeneration` (property) replaces net48's
  `GC.GetMaxGeneration()` method; transpiler maps both to one icall.
- To run an IL `-target:exe` image on a local runtime-only install, copy it to
  `<name>.dll` beside a matching `.runtimeconfig.json` and invoke via that
  runtime's `dotnet.exe`.
