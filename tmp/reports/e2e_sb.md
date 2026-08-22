# e2e_sb — E2E StringBuilder pipeline report

- **Date:** 2026-08-22
- **Exclusive dir:** `E:\il2cpp\tmp\e2e_sb\`
- **Pipeline:** net9 refs (`Microsoft.NETCore.App.Ref` 9.0.6, csc `-nostdlib`) → `src/il2cppc` transpiler → CMake + MinGW → your runtime (`il2cpp_gen_exe`)
- **Oracle:** real .NET 9.0.19 runtime (`tmp\net910\net9\dotnet.exe`, framework-dependent via `e2e_sb_oracle.runtimeconfig.json`)

## Program under test (`e2e_sb.cs`)

Self-checking: each requirement asserts expected values in-program via `Check(name, cond)` → prints `PASS <name>` / `FAIL <name>`; final line `ALL-PASS` iff zero failures.

| # | Requirement | Checks |
|---|---|---|
| 1 | Append chain + AppendFormat `{0}` | `append-chain`, `appendformat-0`, `appendformat-chain` |
| 2 | Insert at 0 | `insert-at-0`, `insert-at-0-chain` (string + char at 0, chained) |
| 3 | Remove | `remove-mid`, `remove-head`, `remove-tail` |
| 4 | Replace char + string forms | `replace-char`, `replace-string`, `replace-nonoverlap` (`"aaa"`→`"ba"`) |
| 5 | Capacity >= Length invariant | `capacity-invariant` (100 appends from cap 4, checked every iteration) |
| 6 | ToString snapshot independence | `snapshot-append`, `snapshot-insert`, `snapshot-replace`, `snapshot-remove` |

16 checks total.

## Build / run evidence

```
csc (SDK 8.0.205 Roslyn) -noconfig -nostdlib+ -r:<ref9pkg\ref\net9.0\*.dll>
    -target:exe   -out:out\e2e_sb.exe         EXIT=0   (transpiler input)
    -target:exe   -out:out\e2e_sb_oracle.dll  EXIT=0   (oracle twin, entrypoint kept)
dotnet run --project src/il2cppc -- out\e2e_sb.exe gen          EXIT=0
cmake -S gen -B build_mingw -G "MinGW Makefiles"                CONF_EXIT=0
cmake --build build_mingw --config Release                      BUILD_EXIT=0
build_mingw\il2cpp_gen_exe.exe                                  EXIT=0, stderr empty
```

## Results

- **Oracle (.NET 9.0.19):** 16/16 `PASS` + `ALL-PASS`, exit 0.
- **Transpiled MinGW binary:** 16/16 `PASS` + `ALL-PASS`, exit 0.
- **Byte-exact diff:** `oracle_net9.txt` vs `actual_mingw.txt` → 349 bytes vs 349 bytes, `byte_identical=True`; both stderr streams empty.

## Notes

- Test-authoring bug caught by the oracle before transpile: `"bonono".Replace("no","na")` is `"bonana"` (not `"banana"`); expectation fixed, then oracle went green.
- Ref pack 9.0.6 fetched as nupkg from nuget.org and extracted to `ref9pkg\` (local packs only had ≤8.0.5; SDK 8.0.205 cannot target `net9.0` TFM directly).
- Oracle twin compiled `-target:exe` renamed `.dll`: `-target:library` emits no PE entrypoint token (host: `MissingMethodException: Entry point not found`), while the host requires the `.dll` name — exe-flavor PE satisfies both.

**Verdict: ALL-PASS (16/16, byte-identical to .NET 9).**
