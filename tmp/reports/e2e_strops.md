# e2e_strops — E2E transcript: System.String operations (net9 refs → il2cppc → MinGW)

- **Date:** 2026-08-22
- **Exclusive dir:** `E:\il2cpp\tmp\e2e_strops\`
- **Program:** `Program.cs` — fixed strings, exact expected outputs; prints `PASS <case>` per assertion, `ALL-PASS` at end (exit 0) or `ALL-FAIL failures=N` (exit 1).
- **Pipeline:** C# compiled against **net9 reference assemblies** (`Microsoft.NETCore.App.Ref 9.0.6`, raw Roslyn `csc`, `-nostdlib+`) → **reference run** on real .NET 9.0.19 (`tmp/net910/net9/dotnet.exe`) → **il2cppc transpiler** → **MinGW** CMake native build → run → byte-diff vs reference.
- **Result: PASS — 51/51 assertions, byte-exact vs .NET 9.0.19, ALL-PASS.**

## Stages

| # | Stage | Command | Evidence |
|---|---|---|---|
| 1 | Compile (net9 refs) | `dotnet exec <sdk>\Roslyn\bincore\csc.dll -nologo -noconfig -nostdlib+ -target:exe -optimize+ -out:out\e2e_strops.exe -r:<refpack>\System.Runtime.dll -r:<refpack>\System.Console.dll Program.cs` | exit 0; `out\e2e_strops.exe` = 8192 bytes |
| 2 | Reference run (.NET 9.0.19) | copy exe→`e2e_strops.dll` + handwritten `e2e_strops.runtimeconfig.json` (Microsoft.NETCore.App 9.0.19); `tmp\net910\net9\dotnet.exe out\e2e_strops.dll` | exit 0; 52 stdout lines; stderr empty; `ALL-PASS` |
| 3 | Transpile | `dotnet run --project src/il2cppc -c Release -- out\e2e_strops.exe gen` | exit 0; 99 distinct literals / 120 sites / 5 methods; `gen\src\E2eStrOps_Program.cpp` 41042 B |
| 4 | Native build (MinGW) | `cmake -S gen -B build_mingw -G "MinGW Makefiles"` then `cmake --build build_mingw -- -j8` | configure exit 0; build exit 0 at `[100%] Built target il2cpp_gen_exe` |
| 5 | Run transpiled exe | `build_mingw\il2cpp_gen_exe.exe` (stdout/stderr captured separately, UTF-8) | exit 0; 52 stdout lines; stderr empty; `ALL-PASS` |
| 6 | Byte-diff | case-sensitive line-by-line compare `ref_out.txt` vs `act_out.txt` | 52/52 lines, **mismatches=0** |

## Coverage matrix (51 assertions — all PASS on both sides)

| Operation | Cases | Highlights |
|---|---|---|
| Length | 2 | 20-char literal; `"".Length == 0` |
| get_Chars | 4 | first/mid/last/indexer on `"banana"`, exact char codes (72/73/33/97) |
| Substring | 6 | 1-arg tail; 2-arg head/mid/window; empty source; `startIndex == Length` → `""` |
| IndexOf | 8 | char & string forms; miss → −1; `(string, startIndex)` global-index semantics; `(char, startIndex)`; empty source |
| LastIndexOf | 6 | char & string; miss → −1; backward-from-startIndex; string suffix hit |
| Replace | 5 | char→char; string→string single/multi-hit greedy non-overlap (`banana`→`bxxa`); no-match identity; delete-via-empty |
| Trim | 2 (+2 shared) | spaces; `\t \n` whitespace set; all-whitespace → `""`; no-op identity |
| TrimStart / TrimEnd | 2 | one-sided trim preserves the other side's trailing spaces |
| StartsWith / EndsWith | 10 | true/false; ordinal case-sensitivity (`hello` ≠ `Hello`); `""` value/prefix both-empty → true |
| CompareOrdinal | 6 | eq 0; lt −1; gt +1; case pair raw unit diff −32; prefix even-length −2 (length difference) |

## Findings during the run

1. **Reference-run oracle caught an agent-side expectation bug before it could pollute the verdict:** `"banana".IndexOf("ana", 2)` is **3** on real .NET 9 (the index-1 match starts before `startIndex`; search begins at 2 → first admissible match at global 3). Literal corrected to 3; both sides then agree.
2. **Pre-existing transpiler build breakage fixed (shared tree, not caused by this task):**
   - `[ATTEMPT]` `dotnet run --project src/il2cppc` → `[ERROR]` CS8510 unreachable switch arm at `Emit/CodeGenerator.cs:3541` — `"System.Converter\`2_int_int" => "nullptr"` duplicated an arm already present at line 3521 (wir9-b23); a prior agent's Converter`2 batch re-added the existing key.
   - `[NEXT]` deleted only the redundant duplicate arm (identical key, identical value — zero semantic change), left a CS8510 warning comment. Backup: `E:\il2cpp\tmp\backups\e2e_strops\CodeGenerator.cs.bak`. Rebuild: 0 warnings / 0 errors.
3. Raw-csc + `-nostdlib+` against the net9 ref pack (refs: `System.Runtime.dll`, `System.Console.dll`) yields a plain IL PE the transpiler accepts directly (same shape as the `tmp/e2e_env` precedent); renaming the bytes to `.dll` + handwritten runtimeconfig.json runs it unchanged on the extracted 9.0.19 runtime.

## Scope compliance

- No git operations; no `.opencode` usage; all artifacts inside `E:\il2cpp\tmp\e2e_strops\` (+ this report, + the one shared-tree backup + the CS8510 fix noted above).
- Program touches only wired surface: parameterless Trim family (no trim-set deviation exercised), 1-arg StartsWith/EndsWith, ordinal-safe ASCII fixtures (culture-sensitive default overloads are result-identical on ASCII).
