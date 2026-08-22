# e2e_mon — Monitor/lock domain E2E (net9 refs / transpiler / MinGW)

- Date: 2026-08-22
- Workspace: `E:\il2cpp\tmp\e2e_mon\` (exclusive)
- Pipeline: **net9 refs → transpiler → MinGW → transpiled binary**, byte-exact diff vs net9 oracle
- Result: **PASS — BYTE-EXACT MATCH, exit 0, stderr empty**

## Program under test (`Program.cs`)

Single-threaded, fully deterministic. Static gate `object gate`, static `int shared`.

| # | Probe | Verdict |
|---|---|---|
| 1 | `lock(obj){shared=5}` — Roslyn lowering = `Monitor.Enter(obj, ref taken)` + try/finally `Exit` | PASS |
| 2 | `Monitor.Enter(gate, ref entered)` / `Monitor.Exit(gate)` + reacquire-after-Exit proof | PASS |
| 3 | `Monitor.IsEntered` | **PASS-skip** (not wired) |
| 4 | nested lock, same object, reentrancy depth 2 (`shared` = 5+10 = 15) | PASS |

### IsEntered skip justification (statically verified, not guessed)

- `src/il2cppc/Emit/MethodMap.Monitor.cs` — `TryMapMonitor` handles only
  `Enter`(1-arg / obj+ref bool), `Exit`, `TryEnter`(1-arg / obj+ref bool),
  `Pulse`/`PulseAll`, `Wait`(obj,int). **No `IsEntered` case** → unmapped.
- `src/runtime/include/internal/monitor.h` — icall surface is
  `il2cpp_monitor_{enter,exit,try_enter,pulse,wait}` only; **no
  `il2cpp_monitor_is_entered`** exists to map to.
- Calling it would die loudly at transpile, so the probe prints a
  `PASS-skip Monitor.IsEntered (not wired in MethodMap.Monitor.cs / monitor.h)`
  line on BOTH runtimes (part of the byte-exact set).

## Pipeline stages (commands + evidence)

1. **net9 refs compile** — Roslyn via SDK 8.0.205:
   `dotnet exec "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" @e2e_mon.rsp`
   rsp: `-noconfig -nostdlib+ -target:exe -optimize+` + all 164 DLLs of
   `microsoft.netcore.app.ref\9.0.6\ref\net9.0`. → `out\e2e_mon.exe` (5120 B).
   One benign `warning CS2023` (`-noconfig` inside rsp ignored).
2. **net9 oracle run** — runtime `tmp\net910\net9\dotnet.exe out\e2e_mon.dll`
   (+ hand-written `e2e_mon.runtimeconfig.json`, framework 9.0.19), exit 0 →
   `expected.txt`.
3. **Transpile** — `dotnet run --project src\il2cppc -- out\e2e_mon.exe gen`
   → `generated 1 type(s)`, exit 0.
4. **MinGW build** — `cmake -S gen -B build_mingw -G "MinGW Makefiles"` +
   `cmake --build build_mingw --config Release` → `il2cpp_gen_exe.exe`, exit 0.
5. **Run + diff** — transpiled exe exit 0, stderr empty,
   stdout **byte-exact match** vs `expected.txt`.

## Output (identical on both runtimes)

```
PASS lock(obj){shared=5} shared=5
PASS Monitor.Enter(gate,ref)/Exit entered=True
PASS-skip Monitor.IsEntered (not wired in MethodMap.Monitor.cs / monitor.h)
PASS nested lock same-object depth2 shared=15
ALL-PASS
```

## Incident log (stage 3 first attempt)

- `[ATTEMPT]` `dotnet run --project src/il2cppc` (Debug and `-c Release`)
- `[ERROR]` transpiler itself failed to build: `CodeGenerator.cs(3541,9):
  error CS8510` — duplicate switch arm `"System.Converter`2_int_int"`
  (already mapped at line 3521; a recent WIP edit re-added it).
- `[NEXT]` On re-inspection the working tree had ALREADY been fixed
  concurrently by another session (duplicate arm removed at 3541, comment
  reworded to cover only the genuinely new `string_int`/`string_string`
  arms). Retried → transpile exit 0. No edit to `CodeGenerator.cs` was made
  by this session; pre-fix snapshot kept at
  `tmp\e2e_mon\backup_CodeGenerator.cs.pre-cs8510` for the record.

## Artifacts

| Path | Content |
|---|---|
| `tmp\e2e_mon\Program.cs` | probe source |
| `tmp\e2e_mon\e2e_mon.rsp` | csc response file (net9 refs) |
| `tmp\e2e_mon\out\` | net9 PE + runtimeconfig |
| `tmp\e2e_mon\expected.txt` / `ref_err.txt` | net9 oracle stdout/stderr |
| `tmp\e2e_mon\gen\` | transpiled CMake project |
| `tmp\e2e_mon\build_mingw\` | MinGW build tree |
| `tmp\e2e_mon\actual.txt` | transpiled-binary stdout |

**Verdict: ALL-PASS (4/4 probes: 3 PASS + 1 documented PASS-skip), byte-exact.**
