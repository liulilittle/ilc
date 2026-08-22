# e2e_dt — DateTime/TimeSpan end-to-end (net9 refs → il2cppc → MinGW)

Agent: E2E AGENT (exclusive dir `E:\il2cpp\tmp\e2e_dt\`). No git operations; no `.opencode` usage.

## Task

C# program validating, printing one `PASS` line per check plus final `ALL-PASS`:

| # | Check |
|---|-------|
| 1 | `DateTime.IsLeapYear(2024)` true AND `DateTime.IsLeapYear(2023)` false |
| 2 | `DateTime.DaysInMonth(2024, 2) == 29` |
| 3 | `new DateTime(2024,1,31).AddDays(1)` → Year 2024 / Month 2 / Day 1 |
| 4 | `TimeSpan.FromMinutes(90).TotalHours == 1.5` |
| 5 | Ticks arithmetic: consecutive dates differ by exactly `TimeSpan.TicksPerDay`; ticks-built `TimeSpan` totals 24 h |

## Pipeline (standard: net9 refs / transpiler / MinGW)

1. **Source** — `tmp\e2e_dt\Program.cs` (namespace `E2eDtProbe`, style mirrors `tmp\e2e_env\Program.cs`).
2. **Compile against .NET 9 reference assemblies** — SDK 8.0.205 cannot target net9.0 (NETSDK1045), so Roslyn csc is invoked directly (`sdk\8.0.205\Roslyn\bincore\csc.dll`, args in `e2e_dt.rsp`) with `-nostdlib+` and all 164 refs from `%USERPROFILE%\.nuget\packages\microsoft.netcore.app.ref\9.0.6\ref\net9.0`. Output: pure-IL `out\e2e_dt.exe` + hand-written `out\e2e_dt.runtimeconfig.json` (tfm net9.0, framework 9.0.19).
3. **Reference run (real .NET 9)** — standalone runtime `tmp\net910\net9\dotnet.exe out\e2e_dt.exe` → `expected.txt`, exit 0.
4. **Transpile** — `dotnet src\il2cppc\bin\Release\net8.0\il2cppc.dll out\e2e_dt.exe gen` → `gen\` (1 type, 16 literals, 26 temp slots), exit 0.
5. **Build** — `cmake -S gen -B build_mingw -G "MinGW Makefiles"` + `cmake --build build_mingw` → `[100%] Built target il2cpp_gen_exe`, zero errors.
6. **Run + compare** — `build_mingw\il2cpp_gen_exe.exe` → `actual.txt`, exit 0; `Compare-Object expected.txt actual.txt` → identical (6 lines).

## Result

```
PASS IsLeapYear(2024)=true IsLeapYear(2023)=false
PASS DaysInMonth(2024,2)=29
PASS Jan31+1d=2024-02-01
PASS FromMinutes(90).TotalHours=1.5
PASS Ticks dayDelta=864000000000 dayHours=24
ALL-PASS
```

Transpiled MinGW native binary vs real .NET 9 (9.0.19): **byte-identical**, both exit 0.
5/5 checks PASS — **ALL-PASS**.

## Gap discovered (not fixed — out of E2E scope)

First build failed: generated code called undeclared `System_TimeSpan_FromHours_b42d4e33b878de9c`
(`gen\src\E2eDtProbe_Program.cpp:140`). Root cause: `src\il2cppc\Emit\MethodMap.TimeSpan.cs`
wires only ctor(ticks/hms/dhms), getters, Add/Subtract/Negate/CompareTo, `FromMinutes`;
`FromHours`/`FromDays`/`FromSeconds`/`FromMilliseconds` fall through `default: return null`
→ unresolved generated-name call. Runtime has no `icall_timespan_from_hours` either
(`src\runtime\src\time.cpp` has `from_ticks/from_hms/from_dhms/from_minutes` only).
Check 5 was rewritten to use wired APIs only (`get_Ticks`, `AddDays`, TimeSpan ctor(ticks),
`get_TotalHours`). Wiring `FromHours` remains open for a transpiler/runtime owner.

## Artifacts

- `E:\il2cpp\tmp\e2e_dt\`: Program.cs, e2e_dt.rsp, out\ (exe + runtimeconfig.json), expected.txt, gen\, build_mingw\, actual.txt, build_err.txt (first-failure log)
- Transcript: this file
