# Hot-Path Audit 2 — transpiled C++ structure (read-only)

Date: 2026-08-03 · Scope: `src/il2cppc/**` (22 .cs files) + generated code + runtime hot helpers
Mode: READ-ONLY audit. Only this file was written; no source modified, no commit.

## 1. Status summary

| Item | Status | Evidence |
|---|---|---|
| Transpiler build | PASS (0 errors) | `[BASH: dotnet build src/il2cppc/il2cppc.csproj -c Release]` → 0 错误 |
| Fresh transpile of bench_array_core.exe | PASS (identical output to baseline gen/) | `[BASH: dotnet run --project src/il2cppc -- tests/bench-array/out/bench_array_core.exe ...]` → "generated 1 type(s)"; `[BASH: git diff --no-index]` old vs new gen = no output (byte-identical) |
| Fresh transpiled BENCHMARK run | **BLOCKED** — C++ runtime does not compile | `[BASH: pwsh scripts/run-bench.ps1 -Compiler mingw]` → `src/runtime/src/cultureinfo.cpp:34: error: static_cast from 'Il2CppObject *' to 'Il2CppString *' ... not allowed` |
| Blocked-by | In-flight parallel agent change | `[READ:src/runtime/src/cultureinfo.cpp:34]` casts `il2cpp_string_intern_literal(utf8)` (declared `Il2CppObject*` at `[READ:src/runtime/include/internal/il2cpp_string_intern.h:78]` and defined `stringintern.cpp:280`) to `Il2CppString*` — types are not inheritance-related; `static_cast` is illegal. File mtime 19:03:41. |

Per the audit brief: transpiler-side work proceeded; the benchmark number section below therefore
carries the last-good baseline (2026-08-03 16:07) instead of a fresh run.

## 2. Source structure (hot-path files)

`src/il2cppc/` — 22 .cs files (`[BASH: glob]`):
- **Emit/** (codegen): `CodeGenerator.cs` (2210 lines — arithmetic emission :908-937, array ldelem/stelem :1740-1753, GC-frame prologue :1250-1298), `MethodRefResolver.cs` (BCL call wiring), `AccessorAnalyzer.cs` (property folding pre-scan, 200 lines), `Normalize.cs` lives in **Ir/** (algebraic/null-folding pass, 282 lines), `StackFold.cs` (Ir/, stack→IR), `TypeEmitter.cs`, `VTablePlanner.cs`, `CppWriter.cs`, `MethodEmitter.cs`
- **Ir/**: `Expr.cs`, `Stmt.cs`, `StackFold.cs`, `Normalize.cs`
- **Il/**: `MethodBodyReader.cs`, `IlDecoder.cs`, `IlInstruction.cs` · **Model/**: TypeDef/MethodDef/FieldDef · `MetadataLoader.cs`, `Program.cs`

Hot-path helpers are inline in the runtime headers: `il2cpp_array_get_elem_ptr` `[READ:il2cpp_array.h:75]`, `il2cpp_string_get_length` `[READ:il2cpp_string.h:72]` — the committed "hot helper inline" is confirmed present. The transpiler emits calls to these inline helpers (`[READ:CodeGenerator.cs:1744,1753]`), so the generated `.cpp` is unchanged vs baseline (`[BASH: git diff --no-index]` = identical) — the win lives entirely in the runtime headers.

## 3. Benchmark numbers

Fresh transpiled numbers UNAVAILABLE this session (runtime build break, §1). Last-good baseline, `tests/bench/results/2026-08-03_1535-msvc.csv` + `2026-08-03_1536-mingw.csv` + `tests/bench-array/results/2026-08-03_1607-array-mingw.csv` `[BASH: Get-Content]`:

| benchmark | net48_ms | ilc_ms | ratio (net48/ilc) |
|---|---|---|---|
| fib (mingw) | 20 | 33 | 0.61 |
| loop (mingw) | 58 | 59 | 0.98 |
| array (mingw) | 12 | 62 | **0.19** |
| fib (msvc) | 21 | 25 | 0.84 |
| loop (msvc) | 58 | 59 | 0.98 |
| array (msvc) | 12 | 52 | **0.23** |
| arr_sum (core, mingw) | 8 | 19 | **0.42** |
| arr_write (core, mingw) | 19 | 42 | **0.45** |
| str_len (core, mingw) | 19 | 76 | **0.25** |

Array hot path = still 2.2-4.3x slower than net48 (arr_sum 2.4x, arr_write 2.2x, array 4.3x, str_len 4.0x).
fib is pure recursion (frame push/pop per call is the main cost, 0.61-0.84); loop is at parity (0.98).

## 4. Generated-code anti-patterns (fresh transpile of bench_array_core.exe → temp audit dir; content byte-identical to `tests/bench-array/gen_array_core/src/BenchArr_BenchArr.cpp`)

### P0-1 (c) Redundant null + bounds checks on every array access in provable loops

Generated code `[READ: C:\Users\windo\AppData\Local\Temp\opencode\audit-gen\src\BenchArr_BenchArr.cpp]`:
- `:44` `(*(int32_t*)il2cpp::il2cpp_array_get_elem_ptr((Il2CppArray*)(loc0), (int32_t)(loc2)) = (int32_t)(loc2));` — stelem
- `:55` `t1 = (*(int32_t*)...get_elem_ptr((Il2CppArray*)(loc0), (int32_t)(loc4)));` — ldelem (inner sum loop, 400 × 100000 = 40M reads)
- `:98` / `:102` — same shape in ArrWrite (100M writes + reads)

The check `arr != nullptr && index >= 0 && index < arr->length` `[READ:il2cpp_array.h:75-82]` runs on every one of the 40-100M accesses even though the transpiler can PROVE both halves statically:
- null: `loc0` is a fresh `new int[100000]` result `:39` `:89`, never reassigned → null check is dead
- bounds: the loop guards are `loc2 < 100000LL` `:47`, `loc4 < 100000LL` `:59`, `loc3 < 100000LL` `:101` — the SAME constant as the allocation length, index starts at 0 and only increments → `0 <= i < length` holds by induction

Root cause: `CodeGenerator.cs:1740-1753` emits `il2cpp_array_get_elem_ptr` unconditionally for every ldelem/stelem; no proof-based unchecked fast path (Normalize.cs knows `newarr` lengths and loop-induction shapes but never consumes them).
Suggestion: when the emitter (or a small pre-pass) can prove `0 <= idx && idx < newarr-length` (constant-length `newarr` + induction-var comparison against the same constant), emit a raw `*(int32_t*)(arr->data + idx*4)` or guard with `[[assume(idx >= 0 && idx < arr->length)]]` so the C++ optimizer eliminates the compare; keep the checked helper on every unprovable path (correctness contract `[READ:il2cpp_array.h:22-25]` untouched).

### P0-2 (a) Property access emitted as a call + per-call GC frame inside a 20M loop

Generated code `[READ: same file]`:
- `:158` `t0 = BenchArr_BenchArr_Len_e97abc654a080bd9(loc0);` — `Len` (the `s.Length` read) is a NOINLINE call per iteration
- `:121-126` — `Len` sets up a full GC frame (`__frameRefs[]`, `il2cpp::Il2CppFrame __frame`, `il2cpp_frame_push(&__frame)`) even though it contains NO allocation and NO GC-triggering call
- `:128` the actual property read inside is `il2cpp_string_get_length(arg0)` — inline `[READ:il2cpp_string.h:72]`, so per iteration the cost is: call + frame push (`framestack.cpp:34`, out-of-line) + RAII frame pop (`framestack.cpp:54`, out-of-line) — 3 function calls to read one field.

Compounding: `loc0` is loop-invariant (`:153` assigns once), so `Len(loc0)` is loop-invariant — the transpiler has NO LICM pass (Normalize.cs `[READ:Normalize.cs:26-37]` covers algebraic identities + null-folding only), and the C++ compiler cannot hoist it because the generated function is `IL2CPP_NOINLINE` (`[READ:il2cpp_runtime.h:26]`). net48 JIT runs the same `Len` (also NoInlining) but pays no frame protocol — the 4.0x gap.

Root cause: `CodeGenerator.cs:1266-1298` emits the frame for EVERY method with ≥1 ref local, with no leaf/purity test (a method that can never allocate can never trigger a collection → its frame is pure overhead). And no inlining/LICM in Ir/.
Suggestion: (1) skip frame emission for methods with no allocation/call-that-can-allocate (leaf purity analysis on the IR — allocation-free body + no runtime calls); (2) add a minimal LICM pass (hoist loop-invariant CallExpr whose args are loop-invariant and result is pure — `Len`'s body is a plain field read) or allow transpiler-level inlining of simple accessor-shaped methods at call sites; (3) `Normalize` already proves `knownNonNull` for `ldstr` — reuse it to elide the `nullptr` arm of `il2cpp_string_get_length` (`[READ:il2cpp_string.h:72-76]`).

### P1 (b) Constant-divisor division emits an out-of-line checked call instead of foldable code

Generated code `[READ: same file]`:
- `:191` `il2cpp::runtime::icall_console_write_line_i(il2cpp::il2cpp_checked_div((t2 - loc0), 1000LL));`
- `:198` / `:205` — same shape (all three timing prints in Main)

Root cause: `CodeGenerator.cs:926-927` emits `il2cpp::il2cpp_checked_div(a, b)` for EVERY `Div`, including constant divisors (TryFoldInt32 `:920` only folds both-constant). `il2cpp_checked_div` is out-of-line `[READ:exception.cpp:106]` and carries two checks that are statically dead for a constant divisor: `b == 0` (exception.cpp:108) and `INT_MIN/-1` (exception.cpp:116) — divisor 1000 is neither 0 nor -1. Each `x / 1000` costs a real call + 2 branches, and the C++ compiler cannot strength-reduce it into mul-shift because it never sees the division.
Suggestion: `CodeGenerator.cs:926` special-case constant divisors: `divisor != 0 && divisor != -1 && divisor != INT32_MIN` → emit plain `(a / c)` (C++ compiler folds to mul-shift; semantics identical — no throw possible); keep `il2cpp_checked_div` for runtime-valued divisors only. Note: the integer result of `x/1000` truncation matches .NET on both paths; only the throw paths differ and they are unreachable for this divisor set.

### P2 (minor, same file)

- int64 noise: `(int32_t)(100000LL)` / `(loc2 + 1LL)` / `(loc2 < 100000LL)` — every int32 op is emitted with 64-bit constants and truncating casts (`:44-47`, `:56-59`); C++ folds these at -O3 but the pattern is noisy and blocks cheap textual audits. Emit plain int32 literals when the constant fits.
- `str_len` sink is provably constant (`Len` = 256, `& 0xFF` = 0) — the loop body could be folded away entirely by stronger constant propagation (benchmark comment at bench_array_core.cs:57-61 acknowledges the call is observable; a value-range analysis would change this — only if the semantics allow).

## 5. Priority ordering

| ID | Anti-pattern | Where (gen / emitter / runtime) | Evidence | Impact | Priority |
|---|---|---|---|---|---|
| P0-1 | null+bounds check per array element in provable loops | gen `:44,55,98,102` · `CodeGenerator.cs:1740-1753` · `il2cpp_array.h:75` | arr_sum 0.42, arr_write 0.45 | 40-100M dead compares/branches per benchmark; blocks clean vectorization | **P0** |
| P0-2 | property read via NOINLINE call + GC frame per call, no LICM | gen `:121-126,158` · `CodeGenerator.cs:1266-1298` · `framestack.cpp:34,54` | str_len 0.25 (4.0x) | 20M × (call + push + pop) | **P0** |
| P1 | constant-divisor → out-of-line checked call | gen `:191,198,205` · `CodeGenerator.cs:926-927` · `exception.cpp:106` | pattern everywhere `x / const` appears | call + 2 dead branches per division; blocks mul-shift | **P1** |
| P2 | int64 constant/truncation noise | gen `:44-47,56-59` | — | cosmetic at -O3 | **P2** |

## 6. Constraints honored

- Only `docs/audit/hotpath-audit-2.md` written; zero source edits; no git commit.
- Benchmark attempted once via the existing mechanism (`scripts/run-bench.ps1 -Compiler mingw`); blocked by the in-flight runtime break (§1), noted per brief — transpiler-side audit completed anyway.
- No rollback/restore performed.
