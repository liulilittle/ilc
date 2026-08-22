# e2e_arrpred — System.Array predicate + mutation surface (net9 refs pipeline)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_arrpred\`
Pipeline: **net9 refs → il2cppc transpiler → MinGW → 自研 runtime**
Result: **PASS — byte-for-byte identical to net9 oracle (651/651 bytes), 30 PASS lines + ALL-PASS, exit 0**

## Scope

| Group | APIs exercised | Checks |
|---|---|---|
| A. predicate family (lambdas) | `Array.Find<int>` / `FindAll<int>` / `Exists<int>` / `TrueForAll<int>` with `x => ...` lambdas (hit / miss→default / empty; FindAll result contents & empty result; Exists true/false/empty; TrueForAll true/false/vacuous-empty) | 12 |
| B. mutation + search | `Sort<int>` 1-arg; `Sort<int>(T[], Comparison<T>)` lambda descending; `BinarySearch<int>` hit + miss-negative; `IndexOf<int>` first-hit + miss(-1); `Reverse` 1-arg + 3-arg range; `Clear(arr,i,len)`; `Copy` 5-arg window; `Resize` grow/shrink via `ref` local | 13 |
| C. Empty | `Array.Empty<string>().Length == 0` | 1 |
| D. bounds | `GetLength(0)` / `GetLowerBound(0)` / `GetUpperBound(0)` on non-empty + empty arrays | 5 |

Total: 31 assertions → 30 PASS lines + final ALL-PASS verdict line.

## Pipeline stages (all verified this session)

1. **Transpiler rebuild** — prebuilt `src/il2cppc/bin/Release/net8.0/il2cppc.dll` was stale
   (3:09 vs sources 4:18). `dotnet build src/il2cppc/il2cppc.csproj -c Release` → 0 warnings /
   0 errors; dll refreshed to 836608 bytes @ 4:22.
2. **C# compile (net9 refs)** — Roslyn csc (SDK 8.0.205 bincore) with `-nostdlib+`
   referencing the local .NET 9 runtime assemblies at
   `tmp/net910/net9/shared/Microsoft.NETCore.App/9.0.19/`
   (`System.Runtime.dll`, `System.Console.dll`, `System.Private.CoreLib.dll`)
   → `e2e_arrpred.exe` (managed PE, entrypoint set).
   Note: referencing CoreLib alone failed (CS0103 `Console`) — the facade pair
   (`System.Runtime` + `System.Console`) is required alongside it.
3. **Oracle run** — `tmp/net910/net9/dotnet.exe exec e2e_arrpred.dll` with a hand-written
   `e2e_arrpred.runtimeconfig.json` (Microsoft.NETCore.App 9.0.19) → exit 0, ALL-PASS.
4. **Transpile** — `il2cppc.exe e2e_arrpred.exe gen` → exit 0; 6 types, 34 literals,
   16 methods, 117 temp slots (86 reused).
5. **MinGW build** — `cmake -S gen -B build_mingw -G "MinGW Makefiles"` +
   `cmake --build` → 100%, `il2cpp_gen_exe.exe` linked, zero errors.
6. **Native run + diff** — `build_mingw/il2cpp_gen_exe.exe` → exit 0, stderr empty;
   stdout byte-diff vs oracle: **IDENTICAL (651 == 651 bytes)**.

## Oracle-adjudicated defect (in the test itself, fixed before pinning)

First oracle run FAILED `trueforall_false`: assertion used
`!Array.TrueForAll({10,20,30,20,40}, x => x > 5)` — every element IS > 5, so the
predicate legitimately returns true and the negation is false. The real .NET 9
oracle rejected the assertion (ALL-FAIL failures=1, exit 1). Predicate corrected to
`x > 15` (10 fails it) → oracle ALL-PASS. The failing oracle run was kept as the
pin; only then was the test fixed and re-run green.

## Verdict

The full wired surface held under the net9-ref pipeline:
- generic predicate icalls (`icall_array_find_i4/_find_all_i4/_exists_i4/_true_for_all_i4`),
- sort icall + Comparison-lambda delegate sort,
- binary-search/index-of int32 icalls, reverse/reverse-range, clear, 5-arg copy,
- byref resize (`addr:a` cast),
- `Array.Empty<string>` icall,
- GetLength/GetLowerBound/GetUpperBound instance icalls,

with no-capture lambdas lowering through the `<>c` display-class cached-delegate
path on all four predicate APIs and on `Comparison<int>`.

Native output tail:
```
PASS empty_getlength
PASS empty_getupperbound
ALL-PASS
```

Artifacts: `Program.cs`, `e2e_arrpred.exe/.dll`, `oracle_out.txt`, `gen/`,
`build_mingw/il2cpp_gen_exe.exe`, `actual_out.txt`.
