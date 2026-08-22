# E2E REPORT — e2e_guidrnd (Guid.Parse roundtrip + Random(42) determinism + NextBytes)

- Date: 2026-08-22
- Exclusive dir: `E:\il2cpp\tmp\e2e_guidrnd\`
- Pipeline: **net9 refs → transpiler → MinGW → own-oracle diff** ("yours")
- Verdict: **ALL-PASS** (6/6 checks, exit code 0, byte-exact vs expected, deterministic across 3 runs)

## Program under test (`e2e_guidrnd.cs`)

Three requirement groups, each reported as a `PASS <name>` line:

| Check name | Requirement |
|---|---|
| `guid.roundtrip.canonical` | `Guid.Parse("12345678-1234-1234-1234-123456789abc")` → `ToString()` equals the input string; re-parse is `==` to the first Guid |
| `guid.roundtrip.zeros` | same roundtrip for `"00000000-0000-0000-0000-000000000000"` |
| `guid.roundtrip.maxes` | same roundtrip for `"ffffffff-ffff-ffff-ffff-ffffffffffff"` |
| `guid.roundtrip.mixed` | same roundtrip for `"a1b2c3d4-e5f6-4789-a0bc-def012345678"` |
| `random.seed42.identical` | two `new Random(42)` instances produce identical first 5 `Next(1000)` values, each in `[0,999]` |
| `random.nextbytes.nonzero` | `NextBytes` fills a 32-byte array with at least one byte `!= 0` |

Final line `ALL-PASS` printed only when zero checks failed; exit code 0 on success, 1 otherwise.

## Pipeline commands (standard, as in tmp/icallcase/net10_pipeline.txt)

### STEP 1 — csc compile against net9 reference assemblies (EXITCODE=0)

```
dotnet "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll" -nologo -nostdlib+ -noconfig -target:exe -langversion:latest -out:"E:\il2cpp\tmp\e2e_guidrnd\e2e_guidrnd.dll" -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Private.CoreLib.dll" -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Console.dll" -r:"E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\System.Runtime.dll" "E:\il2cpp\tmp\e2e_guidrnd\e2e_guidrnd.cs"
```

Output: `e2e_guidrnd.dll` (5120 bytes), no errors/warnings.

### STEP 2 — il2cppc transpile (EXITCODE=0)

```
dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll "E:\il2cpp\tmp\e2e_guidrnd\e2e_guidrnd.dll" "E:\il2cpp\tmp\e2e_guidrnd\gen"
```

```
symbols: no Portable PDB found -> no #line C# line directives
string pool: 13 distinct literal(s) from 13 site(s) across 3 method(s) (0 constant concat(s) folded, 0 duplicate site(s) deduped)
string pool table: g_strPool[13] -> include/internal/string-pool.h
resources: 0 embedded manifest resource(s) -> include/internal/resource-pool.h
licm: 0 invariant length read(s) hoistable across 3 method(s)
inline: 0 simple call site(s) inlineable across 3 method(s)
tempslot: 7 temp slot(s) -> 5 with reuse across 3 method(s)
generated 1 type(s) -> E:\il2cpp\tmp\e2e_guidrnd\gen
```

### STEP 3 — cmake MinGW configure + build (both EXITCODE=0)

```
cmake -S "E:\il2cpp\tmp\e2e_guidrnd\gen" -B "E:\il2cpp\tmp\e2e_guidrnd\build" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build "E:\il2cpp\tmp\e2e_guidrnd\build"
```

Configure tail: `-- Configuring done / -- Generating done / Build files have been written to: E:/il2cpp/tmp/e2e_guidrnd/build`.
Build tail: `[100%] Linking CXX executable il2cpp_gen_exe.exe / Built target il2cpp_gen_exe`.

### STEP 4 — run native exe (EXITCODE=0)

```
E:\il2cpp\tmp\e2e_guidrnd\build\il2cpp_gen_exe.exe
```

Actual stdout:

```
PASS guid.roundtrip.canonical
PASS guid.roundtrip.zeros
PASS guid.roundtrip.maxes
PASS guid.roundtrip.mixed
PASS random.seed42.identical
PASS random.nextbytes.nonzero
ALL-PASS
```

## Verification

- **Own-oracle diff**: `expected.txt` (the 7 lines above) vs captured stdout → `Compare-Object` empty (byte-equal line sets). Result file: `actual.txt`.
- **Determinism**: two additional runs produced identical output (`Compare-Object` empty).
- **Exit code**: 0.

## Requirement closure

| # | Requirement | Status |
|---|---|---|
| 1 | Guid.Parse roundtrip fixed GUID string equality | COMPLETE — 4/4 `guid.roundtrip.*` PASS lines above |
| 2 | Two Random(42) → identical first 5 Next(1000) values | COMPLETE — `random.seed42.identical` PASS |
| 3 | NextBytes fills array nonzero-ish (≥1 of 32 bytes != 0) | COMPLETE — `random.nextbytes.nonzero` PASS |
| 4 | Print PASS + ALL-PASS | COMPLETE — 6 PASS lines + ALL-PASS in captured stdout |
| 5 | Transcript at `tmp/reports/e2e_guidrnd.md` | COMPLETE — this file |
| 6 | NO git; no .opencode | COMPLETE — no git commands, no .opencode writes |

6/6 requirements verified.
