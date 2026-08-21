# tests/bench-array -- Array/string hot-path performance benchmarks

Focused benchmarks for the **array/string hot paths** of the ILC transpiled
output vs the .NET Framework 4.8 reference runtime. Companion to the general
suite in `tests/bench/` (same conventions: fixed-workload, P/Invoke clock,
name/ms output pairs, checksum sink, M-stage transpilable subset).

## Why tests/bench-array/ and not tests/bench/

The task originally placed these files in `tests/bench/`, but that directory
is occupied: `bench.csproj` compiles every `*.cs` under `tests/bench/`
recursively (default compile glob), so an extra source file there breaks the
existing suite with a duplicate `Main` (verified empirically: a second
`Bench.Item` triggers CS0101). The new suite therefore lives in its own
directory `tests/bench-array/` and both csprojs use
`EnableDefaultCompileItems=false` + explicit `<Compile Include>`. Nothing in
`tests/bench/` was modified.

## Benchmarks (fixed work; net48 reference >= ~15 ms)

| name       | workload                                              | ILC status |
|------------|-------------------------------------------------------|------------|
| arr_sum    | 40,000,000 `int[]` element reads (`ldelem.i4`)        | RUNS      |
| arr_write  | 100,000,000 `int[]` element writes (`stelem.i4`)      | RUNS      |
| arr_copy   | 20,000 x `Array.Copy(int[],0,int[],0,24000)` (480M el)| net48 only |
| str_chars  | 40,000,128 `s[i]` char reads (`get_Chars`)            | net48 only |
| str_len    | 20,000,000 `s.Length` reads (NoInlining wrapper)      | RUNS      |
| str_concat | 500,000 x `"v" + i` (box + String.Concat)             | net48 only |

The RUNS rows are the **transpilable subset**, reproduced verbatim in
`bench_array_core.cs` (the FULL program `bench_array.cs` cannot be linked for
the ILC side yet -- see Failure points). The three methods are
`[MethodImpl(NoInlining)]` so the net48 JIT compiles them identically in both
programs (without it, JIT-inlining context varies the arr_sum measurement 2x
between the two assemblies -- see below).

Allocation constraints (ILC GC, 2026-08-03): every allocation inside ONE
segment (Gen0 64 KB payload / LOH 1 MB), arrays avoid the 64-85 KB Gen0 gap
and total LOH stays under one 1 MB segment (arr_sum 400 KB + arr_write
400 KB + arr_copy 2 x 96 KB = 992 KB). Workloads are reached by repeating the
operation. Masked sums (`& 0x1F` / `& 0xFF`) keep every accumulator inside
int32 (no wrap, no C++ signed-overflow UB).

## Usage

```powershell
pwsh -NoProfile -File scripts/run-bench-array.ps1          # MinGW (default)
pwsh -NoProfile -File scripts/run-bench-array.ps1 -Compiler msvc
```

The script: builds both net48 programs, runs them (FULL = 6-benchmark
reference; CORE = apples-to-apples cross-check), transpiles the CORE program,
builds the generated C++ via `tests/bench-array/CMakeLists.txt`
(`CAPILLIB_DIR` pattern, Release), runs it, prints both tables and writes
`tests/bench-array/results/<date>-array-<compiler>.csv` plus a failures file.
The FULL program's transpile failure is expected and reported (not fatal).

Manual net48 reference:

```powershell
dotnet build tests/bench-array/bench_array.csproj -c Release -o tests/bench-array/out
g++ -shared -O2 -o tests/bench-array/out/benchlib.dll tests/bench-array/benchlib/benchlib.cpp
& tests/bench-array/out/bench_array.exe
```

## Results (2026-08-03, transpiler source build, Release)

Ratio = net48_ms / ilc_ms (core program, same binary both sides); **ratio < 1
means the transpiled build is slower**.

| name       | net48 ms | ilc ms | ratio | toolchain |
|------------|----------|--------|-------|-----------|
| arr_sum    | 8        | 19     | 0.42  | mingw     |
| arr_write  | 19       | 42     | 0.45  | mingw     |
| str_len    | 19       | 76     | 0.25  | mingw     |
| arr_sum    | 8        | 20     | 0.40  | msvc      |
| arr_write  | 20       | 54     | 0.37  | msvc      |
| str_len    | 20       | 66     | 0.30  | msvc      |

net48-only references (FULL program): arr_copy 26-29 ms, str_chars 14-17 ms,
str_concat 21-23 ms.

## Audit comparison (docs/audit/generated-code-audit.md)

The audit predicted **2-5x** for array hot loops with the runtime helpers
non-inlined (section 4.2 / #1: `il2cpp_array_get_elem_ptr` / `get_length` are
non-inline `.cpp` functions, so every element access is a real call and the
loop cannot be vectorized/hoisted). Measured today:

* arr_sum 0.40-0.42  -> ILC **2.4-2.5x slower** (read-only loop)
* arr_write 0.37-0.45 -> ILC **2.2-2.7x slower** (write-only loop)
* str_len 0.25-0.30  -> ILC **3.3-4.0x slower** (Length is a per-iteration
  `il2cpp_string_get_length` call -- see property folding)
* existing mixed `array` benchmark (tests/bench): 0.23 -> **4.3x slower**

**Conclusion: the audit's 2-5x array gap is CONFIRMED and NOT closed.** The
generated code (gen_array_core/src/BenchArr_BenchArr.cpp:44/55/98/102) still
calls `il2cpp::il2cpp_array_get_elem_ptr` per element, and array.cpp:115/134
still define the helpers as non-inline functions. The in-flight optimizer
work (Normalize.cs -- algebraic identities + null-comparison folding, merged
into the transpiler during this run) does NOT change the hot loop shape.

**Property folding (str_len -- the verification point):** `s.Length` compiles
to `t0 = il2cpp::il2cpp_string_get_length(arg0)` (generated
BenchArr_BenchArr.cpp:128) -- a per-iteration CALL, not a folded direct field
read. The AccessorAnalyzer folds accessors of USER types only; System.String
is not a translated type, so its `get_Length` stays an icall. Folding
String.Length to `((Il2CppString*)s)->length` is the expected 2j follow-up.

## Failure points (FULL program, unwired BCL calls)

The transpiler CRASHES on all three unwired constructs (verified with minimal
probes -- legal net48 C#, not exotic):

| construct              | error                                      |
|------------------------|--------------------------------------------|
| `"v" + i` (Concat)     | `il2cppc error: Stack empty.` (StackFold pops unbalanced) |
| `s[i]` (get_Chars)     | `il2cppc error: callvirt on non-instance method` -- MethodRefResolver.cs:126 mishandles `get_` accessors (`!mrName.StartsWith("get_")` makes them non-instance) |
| `Array.Copy(...)`      | `il2cppc error: Stack empty.`              |

Each would additionally fail at link (dangling `System_String_Concat_*` /
`System_String_get_Chars_*` / `System_Array_Copy_*` symbols). The three
benchmarks stay in the suite as net48-only references and become the ILC-side
acceptance tests when the wiring lands (string icalls / arrayext are the
planned M5 targets).

## In-flight workaround (generated-code patch, not src/)

The transpiler now emits `il2cpp::il2cpp_string_intern_literal` for `ldstr`,
but the runtime's aggregate header `il2cpp_runtime.h` does not include
`il2cpp_string_intern.h` yet, so the generated code would not compile.
`run-bench-array.ps1` patches the GENERATED `gen*/include/internal/generated.h`
(adding the include) when needed -- it operates only on the generated output
in this directory, never on `src/`. Removed automatically once the runtime
header lands.

## Known measurement note

net48 arr_sum measures 16-18 ms in the FULL program but 8-9 ms in the CORE
program for **byte-identical IL** (verified via reflection IL dump) -- a
deterministic-per-binary net48 JIT artifact (not inlining: NoInlining does
not remove it; not data alignment: the LOH array address randomizes per run
via ASLR). The ratio table therefore uses the CORE program on both sides
(same binary). The FULL program's numbers remain the 6-benchmark reference.
