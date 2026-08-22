# E2E Report — e2e_arrsort (Array.Sort / BinarySearch / Reverse on 100-element deterministic arrays)

Date: 2026-08-22
Exclusive dir: `E:\il2cpp\tmp\e2e_arrsort\`
Pipeline: net9 refs csc → il2cppc transpile → MinGW cmake native build → run → byte-exact diff vs managed oracle

## Inputs

- net9 runtime refs: `E:\il2cpp\tmp\net910\net9\shared\Microsoft.NETCore.App\9.0.19\`
- transpiler: `E:\il2cpp\src\il2cppc\bin\Debug\net8.0\il2cppc.dll` (Release dir was mid-rebuild by a concurrent agent)
- toolchain: cmake `D:\android\sdk\cmake\3.22.1` + MinGW g++ (`C:\Program Files\ming-w64\bin`), generator `MinGW Makefiles`

## Program under test (`Program.cs`, namespace E2eArrSort)

10 self-validating checks over 100-element LCG-deterministic arrays (seed 20260822),
each printing `PASS <name> <detail>`, terminated by `ALL-PASS` / `ALL-FAIL failures=N`:

1. `sort_int_asc` — Array.Sort(int[100]) ascending: pairwise order, first==min-scan,
   last==max-scan, length preserved, order-independent multiset checksum unchanged.
2. `bs_int_hit` — Array.BinarySearch hit for ALL 100 elements (predicate form:
   idx>=0 && a[idx]==value; duplicate hits are contract-unspecified index).
3. `bs_int_miss` — miss complement semantics: below-all → ~ret==0, above-all → ~ret==100,
   gap probe (first i with a[i+1]>a[i]+1, probe a[i]+1) → ~ret==i+1, empty[] → -1.
4. `reverse_int` — Array.Reverse: mirror of snapshot, double-reverse restores,
   checksum stable.
5. `sort_str_ordinal` — Array.Sort(string[100]): pairwise CompareOrdinal<=0,
   first/last == ordinal min/max scan, checksum unchanged.
6. `bs_str_hit` — all 100 elements hit (predicate form).
7. `bs_str_miss` — proper-prefix-of-min → ~0; max+"~" ('~'>'z') → ~100;
   mid probe (contains '~', never generated) → ~linear-scan insertion point; empty[] → -1.
8. `reverse_str` — mirror / restore / checksum via CompareOrdinal.
9. `multi_int_pipeline` — fresh int[100]: sort→asc→snapshot→BinarySearch hit+miss→
   Reverse→desc+mirror→Reverse→restore→asc→checksum stable.
10. `multi_str_pipeline` — same chain for string[100].

Checksums are order-independent (commutative XOR/additive combine of per-element mixes),
so sort/reverse invariance is verifiable without hardcoding element order.

## Step 1 — compile against net9 (csc, exit=0)

```
dotnet <sdk8.0.205>\Roslyn\bincore\csc.dll -nologo -noconfig -nostdlib -target:exe
  -out:out\e2e_arrsort.exe
  -r:<net9>\System.Private.CoreLib.dll -r:<net9>\System.Runtime.dll -r:<net9>\System.Console.dll
  Program.cs
```

(+ hand-written `out\e2e_arrsort.runtimeconfig.json` — csc does not emit it.)

## Step 2 — managed oracle (net9 dotnet, exit=0)

```
e2e_arrsort N=100
PASS sort_int_asc min=-499 max=1485 ck=-35227221813299998
PASS bs_int_hit probes=100
PASS bs_int_miss low=~0 high=~100 gap=~1 empty=-1
PASS reverse_int ck=-35227221813299998
PASS sort_str_ordinal first=banana12 last=lemon90 ck=110609866392717083
PASS bs_str_hit probes=100
PASS bs_str_miss low=~0 mid=~51/51 high=~100 empty=-1
PASS reverse_str ck=110609866392717083
PASS multi_int_pipeline ck=2271030529131146954
PASS multi_str_pipeline ck=-8072067648563588539
ALL-PASS
```

(One earlier oracle iteration caught 2 bugs in MY OWN test logic before any native run:
the multi-op pipelines asserted ascending AFTER Reverse had already made the array
descending — contradictory assertions evaluated at the wrong pipeline stage. Fixed by
evaluating each property at its correct stage. This is exactly why oracle-first.)

## Step 3 — transpile (exit=0)

`dotnet il2cppc.dll out\e2e_arrsort.exe gen` → `generated 1 type(s) -> gen`.

Two source-level rejections were fixed in the PROGRAM first (M3 surface):
- `uint` return type unsupported → LCG switched to int/long arithmetic only.
- `string[]`-returning helper unsupported → converted to fill-in-place (void).

## Step 4 — TWO TRANSPILE-R BUGS FOUND, ROOT-CAUSED, FIXED

### Bug 1 — `const - var` array index mis-folded to `var + const` (CodeGenerator.cs)

- Symptom: native crashed `Index was outside the bounds of the array` in reverse_int;
  managed oracle passed the same code.
- Evidence: generated C++ line 964 emitted bounds check/address for `loc66 + (99LL)`
  where the C# source is `snapI[N - 1 - i]` (IL: `ldc.i4.s 99; ldloc; sub`) → OOB at i=1.
- Root cause: the constant-length array fast path (CodeGenerator.cs ~4416-4445,
  batch-159 bench-sort extension) decomposes index `leaf ± const`. Its const-left arm
  `else if (IsIntConst(b.Left)) { leaf = b.Right; off = IntConstValue(b.Left); }` did not
  check the operator — Sub is NOT commutative (Normalize.cs:361 documents this), so
  `99 - i` became `i + 99`.
- Fix: arm restricted to `b.Op == BinaryOp.Add && IsIntConst(b.Left)`; const-left Sub now
  falls back to the checked helper path (conservative, correct).
- Post-fix generated code: `(99LL - loc66)` via checked `il2cpp_array_get_elem_ptr`.
- Backup: `tmp\e2e_arrsort\backups\CodeGenerator.cs.pre-const-sub-fix`
  (MD5 774672470F47A64985FB4B10F74333BE).

### Bug 2 — `Array.Reverse<string>` routed to the int32-only icall (MethodMap.Array.cs)

- Symptom: native threw `Only one-dimensional System.Int32 arrays are supported.` at
  reverse_str (runtime `icall_array_reverse` is int32-only by contract,
  il2cpp_array_ext.h:11).
- Root cause: `case "Reverse"` mapped ANY 1-arg Reverse to `icall_array_reverse`,
  ignoring `genArgs` — while Sort/BinarySearch/IndexOf/Empty all dispatch on the generic
  argument byte. csc binds the GENERIC `Array.Reverse<T>(T[])` (.NET Core overload), so
  the element kind IS available. The runtime already ships typed reversers
  (`il2cpp_array_reverse_byte/int32/int64/string/object`, arrayextra.h:75-96).
- Fix: genArgs routing — 0x08 → existing icall; 0x0E → `il2cpp::il2cpp_array_reverse_string`;
  0x12/0x1C → `il2cpp::il2cpp_array_reverse_object`; both new entries use cast code `"a"`
  (NullSafePtrCast to Il2CppArray*, CodeGenerator.cs:6298); non-generic Reverse(Array)
  keeps the historical int32 contract; unknown genArgs stay LOUD.
- Backup: `tmp\e2e_arrsort\backups\MethodMap.Array.cs.pre-reverse-fix`
  (MD5 73EDCCCE8D5931D2ECEC6C387A2E4A91).

Both fixes: il2cppc rebuilt 0 errors / 0 warnings.

## Step 5 — cmake configure + MinGW build (exit=0)

```
cmake -S gen -B build_mingw -G "MinGW Makefiles"   → configure exit=0
cmake --build build_mingw --parallel 16            → [100%] Built target il2cpp_gen_exe
```

## Step 6 — native run (MinGW build, exit=0)

```
e2e_arrsort N=100
PASS sort_int_asc min=-499 max=1485 ck=-35227221813299998
PASS bs_int_hit probes=100
PASS bs_int_miss low=~0 high=~100 gap=~1 empty=-1
PASS reverse_int ck=-35227221813299998
PASS sort_str_ordinal first=banana12 last=lemon90 ck=110609866392717083
PASS bs_str_hit probes=100
PASS bs_str_miss low=~0 mid=~51/51 high=~100 empty=-1
PASS reverse_str ck=110609866392717083
PASS multi_int_pipeline ck=2271030529131146954
PASS multi_str_pipeline ck=-8072067648563588539
ALL-PASS
```

## Results

| Check | Managed (net9) | Native (il2cpp+MinGW) |
|---|---|---|
| sort_int_asc | PASS | PASS |
| bs_int_hit | PASS | PASS |
| bs_int_miss | PASS | PASS |
| reverse_int | PASS | PASS |
| sort_str_ordinal | PASS | PASS |
| bs_str_hit | PASS | PASS |
| bs_str_miss | PASS | PASS |
| reverse_str | PASS | PASS |
| multi_int_pipeline | PASS | PASS |
| multi_str_pipeline | PASS | PASS |

Byte-exact comparison (`Compare-Object -SyncWindow 0`): **managed 12 lines == native 12 lines**.

**RESULT: ALL-PASS — 10/10 checks, exit code 0, byte-exact vs net9 oracle.**

## Regression status of the two transpiler fixes

- Unit suite (tests/unit): 339/339 Array/StackFold/CodeGen-filtered tests PASS with both
  fixes in place.
- Full-suite run shows 19 failures (1807 total) — ALL in other agents' domains
  (Convert/Base64 narrowing family, Math SinCos/ModF loudness, Interlocked ref-double,
  Linq Where/OrderBy, Environment icalls). None touch Array.Reverse or the array-index
  fast path; those mapping files are unmodified in the working tree (git status: only
  CodeGenerator.cs + MethodMap.Array.cs [this session] and MethodBodyReader.cs
  [concurrent agent]). Failures pre-date this session at HEAD 9a1cca5.
- Behavior-change scope of both fixes is strictly additive/corrective: Bug-1 arm only ever
  produced WRONG code (any prior case hitting it would have failed its oracle diff; matrix
  was green, so nothing regresses); Bug-2 previously LOUD-crashed at runtime for string[]
  (no existing e2e exercised it).

## Artifacts

- `Program.cs`, `out\e2e_arrsort.exe` (+ .runtimeconfig.json), `out\oracle_managed.txt`,
  `out\native_actual.txt`, `out\transpile_log.txt`, `out\build_log.txt`
- `gen\` (generated CMake project), `build_mingw\il2cpp_gen_exe.exe`
- `backups\` (both pre-fix transpiler files)
