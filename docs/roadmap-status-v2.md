# Roadmap Status v2 — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-04 (snapshot 4 — supersedes snapshot 3; snapshot 3 kept at
  `docs/roadmap-status.md` — this is a new file, not an overwrite)
- Purpose: single-page status board for the M1-M8 milestone chain, the current
  sprint (batch 4/5 — LANDED), the critical-path analysis, and the next batch
  (batch 6+) task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git log --oneline -15` — [BASH] HEAD = `67eb48c`; **10 commits since
    snapshot 3** (`4673de0`): `f66a1c8` wiring batch, `8a9a3a9` BCL runtime
    domains + encoding completion, `ba11754` runtime core fixes, `4b25827` test
    suites (30 e2e fixtures), `8319efe` docs, `0d28414` transpiler stable batch
    (MethodMap domains + EH + GenericMethodParameter + StringPoolPass),
    `0b99780` regex engine (regex_parse/match/api), `57d3350` runtime core fixes
    (net48-exact 5381-seed string hash), `398a24e` regex tests (82 assertions +
    stringhash net48 oracle pins + regexcase e2e), `67eb48c` ndk-probe docs +
    gitignore cleanup.
  - `git status --porcelain` — [BASH] **46 modified, 0 untracked, 0 deleted**
    (snapshot 3: 149 untracked + 20 modified — batch 4/5 work is COMMITTED).
  - `git diff --stat` — [BASH] 34 files changed, +2333/−436 (in-flight
    conformance/verification diffs, see §2.5).
  - `tests/unit` — [BASH] **538 `[Fact]`** (snapshot 3: 382); latest full run
    [READ:process.md:45] **589/600 pass**, 11 failures all in parallel-agent
    in-flight domains (Base64×5 / Encoding×5 / Generics×1).
  - `tests/runtime` — [BASH] **88 `test_*.cpp`** (snapshot 3: 83).
  - `tests/cases` — [BASH] **88 e2e case dirs** (snapshot 3: 85; +regexcase,
    +2 others).
  - `src/il2cppc/Emit` — [BASH] **54 `MethodMap.*.cs`** (snapshot 3: same count,
    but then 0 tracked — now ALL committed at `f66a1c8`/`0d28414`).
  - Conformance audits — [READ] `docs/audit/{regex,number,datetime,
    baseconv}-conformance.md` (all dated 2026-08-04, all net48-oracle + .NET
    Core reference-source methodology) + `docs/audit/hotpath-audit-2.md`.
  - `docs/audit/e2e-full-matrix.md` — **NOT YET PRODUCED** (task in plan.md
    P1-P7; P1 verified, P2-P6 pending).
- Legend: ✅ = committed and verified; 🟡 = in-flight (committed but chain
  incomplete, or worktree-modified); ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 2k, ≥24) continuously modify files;
  all counts are measurement-time values. Re-verify `git status` before
  scheduling. `.gitignore` `gen*/` collision (snapshot-3 finding D2) — fix
  committed? `genlist` still invisible under `gen*/` pattern [BASH: git ls-files
  = 0] — **still open, must register before next milestone commit**.

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14. |
| **M3** Generational GC + string/array/EH + generics + delegates + frame records | ✅ | Snapshot-3 evidence stands (`a040fe2`/`2e6565c`/`0854772`/`e46110a`/`ff368bd`/`75eced6`). |
| **M4** P/Invoke + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | Snapshot-3 evidence stands (`c44e8b9`→`296ae20`). |
| **M5** BCL full translation (wiring, L1 trimming default) | 🟡 | **Batch 4/5 wiring LANDED**: all 54 `MethodMap.*.cs` committed [BASH]. 34-domain batch mostly complete — 28 named domains verified present and wired: Stopwatch, Interlocked*(skeleton — see below), IntPtr, Queue, Stack, StreamIO, BinaryIO, Encoding, Guid, Version, Uri, Enum, Attribute, Type, ArraySort, Buffer, BitConverter, MathExtra, TimeExt2, StringExt3, NumberFormat, Debug, Monitor, StringBuilder, Object, Format, Regex, List [BASH: file list]. Regex domain FULLY working end-to-end (regexcase e2e 17 lines byte-identical with net48 [READ:memory.md:15]). **Remaining gaps**: Interlocked = unfilled skeleton (header still says "WIRING AGENT: fill the switch body", 3 NotSupported/null markers [READ:MethodMap.Interlocked.cs:1-9]); **ArrayList = NO MethodMap file, NO runtime domain** [BASH: glob 0, grep 0]; decimal = partial (BinaryIO `Write(decimal)` loud NotSupported, "no runtime icall in the binary protocol" [READ:MethodMap.BinaryIO.cs:61]); non-Int32 box (UInt32/long/double) = loud-rejected only, not supported [READ:transpiler-audit-fixes.md:56-59]. |
| **M5.5** Reflection (trimming-level verification L0/L1/L2) | 🟡 | **TypeInfo runtime domain BUILT** [BASH: src/runtime/src/typeinfo.cpp exists]; `test_typeinfo` full green EXIT=0 after `interfaces` pointer-array fix [READ:memory.md:8-9]. **CLI 模式 B LANDED**: "il2cppc reflection manifest (mode B)" [READ:CliOptions.cs:100]. L0/L1/L2 verification still pending (needs T301-T310). |
| **M6** Transpiler (test suites all-green + 3-compiler CI + bench base) | 🟡 | **All batch-4/5 transpiler items landed** [BASH: git log `0d28414`]: generics (`GenericMethodParameter` + TypeSpec leading class marker), nested types (`DecodeNestedBaseType` + full-list base resolution [READ:recurse-recheck.md:88]), delegates, EH (EH-region **outer-first sort** + **catch-all normalization** + rethrow in finally [READ:CodeGenerator.cs:648,711,747]), **string pooling** (`StringPoolPass` layer1/2 diagnostic [BASH: src/il2cppc/Opt/StringPoolPass.cs]), **algebraic optimization** (`Normalize.cs` coreclr-fgMorphSmpOp model + Audit H9 div guard). Unit `[Fact]` = **538** [BASH], runtime tests = **88**, e2e dirs = **88**. Build gate green throughout the batch. 3-compiler CI routine + bench baseline still open (T605/T702/T707). |
| **M6.5** Cross-platform (DLR-style runtime portability + platform layers) | 🟡 | **Encoding POSIX path LANDED**: encoding.cpp reworked — `#include <windows.h>` removed, dual-branch UTF-8↔UTF-16 + iconv POSIX path [READ:cross-platform-design.md §1.1]. **Platform layer batch 1**: 17/17 files (21% of 81 runtime sources) with Windows footprint are dual-branched `#if defined(_WIN32)`/`#else` [READ:cross-platform-design.md §0.1]. **Metadata fixes LANDED**: metadata.cpp repaired → 79/88 sources green [READ:plan.md:4]. **Warning clearance PARTIAL** — remaining documented gaps: numberformat.cpp:339 `std::atoi` missing `<cstdlib>`, boxingext2.cpp:304 strict-aliasing, streamio.cpp:273 unused var + :284/:289 C4996 `_wfopen`, time.cpp:350 format-truncation, test_exceptionext.cpp:62/63 C4996 [READ:cross-platform-design.md §0.3-0.4; wsl-probe.md:59-78]. NDK probe: NDK 26.1.10909125 installed, arm64-v8a cross-compile feasible, env vars unset [READ:ndk-probe.md]. |
| **M7** Performance (hotpath + conformance vs net48 oracle) | 🟡 | **Hotpath fixes in flight**: hotpath-audit-2 (P0-1 redundant checks, P0-2 box→unbox folding, P0-3 null folding) — last-good bench baseline: fib 0.61-0.84×, loop 0.98×, array 0.19-0.23×, arr_sum 0.42×, arr_write 0.45×, str_len 0.25× (net48/ilc ratio) [READ:hotpath-audit-2.md:31-44]. **Conformance aligned to net48 oracle**: 4 audits committed — regex-conformance (36 net48 probes, net48≡Core), number-conformance (Number.Formatting.cs + net48 numprobe), datetime-conformance (ParseExact subset landed, standard Parse/ToString "G" still open), baseconv-conformance (Base64/BitConverter/string-hash 5381 seed — net48-exact, landed at `57d3350`) [READ: 4 conformance docs headers]. |
| **M8** Full CLDR / COM interop (long-term) | ⬜ | 路线图.md:37. |

Count: 4/8 completed (M1-M4), 4/8 in-flight (M5, M5.5, M6, M6.5, M7 — 5 lines marked
🟡; M5/M5.5 share the wiring/reflection chain), 1/8 not started (M8). M5/M6 ran in
parallel (24+ agents, 铁律 2k) and their batch-4/5 deliverables are now committed.

---

## 2. Current sprint (batch 4/5 — LANDED; close-out in progress)

### 2.1 Wiring — 54 `MethodMap.*.cs` ALL COMMITTED (snapshot 3: 0 tracked)

[BASH: git ls-files src/il2cppc/Emit/MethodMap*.cs = 54]. `f66a1c8` + `0d28414`
committed the full set. Worktree now has only `MethodMap.List.cs` +
`MethodMap.Regex.cs` modified (in-flight agent diffs, [BASH: git status]).
**T101 MethodMapHub (registry + tail-chain) still does NOT exist** [BASH:
Test-Path = False] — dispatch still via the single `MethodRefResolver.cs` switch;
wiring scale risk (snapshot-3 chain C) remains open.

### 2.2 Runtime — regex engine COMPLETE (chain B resolved); typeinfo + encoding done

- **Regex engine full stack LANDED** (`0b99780`): `regex.h`/`regex_match.h`
  IsMatch-Match-Replace-Split API (input-first order, net48-verified),
  `regex_parse.cpp` T1 parser (with fallback), `regex_match.cpp` CPS backtracker
  + step budget, `regex_api.cpp` managed-string wiring + GC-rooted results
  [BASH: all 3 .cpp present; CodeGenerator.cs:74 `#include "regex.h"` in
  generated.h list]. Tests: `test_regex_match` (82 assertions, `398a24e`) +
  `test_regex_api` + `test_stringhash` (net48 oracle pins) + `regexcase` e2e
  (17 lines byte-identical vs net48 [READ:memory.md:15]).
- **typeinfo** domain built + test green (M5.5 evidence, §1).
- **Net48-exact string hash** (5381 seed) landed `57d3350` — hashtable +
  valuesemantics aligned to net48 oracle (baseconv-conformance).

### 2.3 Transpiler — stable batches committed; build gate green throughout

[READ:process.md:43-44] `dotnet build src/il2cppc` → 0 errors 0 warnings; unit
filtered runs green (regex domain 11/11). Latest full unit run 589/600, 11
failures confined to parallel-agent in-flight domains (Base64/Encoding/Generics)
[READ:process.md:45].

### 2.4 e2e — 88 case dirs [BASH]; regexcase committed; versioncase risk noted

`regexcase` fixture committed (`398a24e`). ⚠️ **versioncase gen is stale** —
`versionuri.h` NOT in generated.h header list [READ:memory.md:22] → versioncase
e2e may be red until the header is added (parallel agent domain).
**docs/audit/e2e-full-matrix.md NOT YET PRODUCED** — plan.md task P1 verified
(88 cases == 88 csproj), P2-P6 pending; the 40-min plan of record is still open.

### 2.5 In-flight (worktree) — conformance + verification diffs

46 modified files, +2333/−436 [BASH: git diff --stat]: regex conformance audit
diffs (`regex_parse.cpp` +107: \a\e\~ escapes, in-class \D\W\S complements;
`regex_match.cpp` +12: bare repeat continuation; `test_regex_match` +84)
[READ:discover.md:12], timeext/timeext2 conformance expansions (test_timeext2
+178), console/bitconverter/convertbase64/formatext/numberformat fixes +
test sync, test_typeinfo fix, CMakeLists touch. These are parallel-agent
domains — do not touch their files.

---

## 3. Critical-path analysis

**Chain A — remaining wiring gaps (short, well-scoped):**
1. **exdeep f14b BLOCKER (top remaining e2e red)** — `no.ToString()` on null:
   callvirt owner System.Object not in translated types; throw at
   CodeGenerator.cs:1332-1334, receiverType missing at StackFold.cs:930-940
   (objTypes only records newobj/box/GetType results; plain locals typed
   System.Object never enter it) [READ:recurse-recheck.md:71-86]. Fix path:
   inject local-variable declared types (MethodBodyReader LocNames/LocalKinds)
   into objTypes → MethodMap.Object.cs:62-66 System.Object branch → routes to
   `icall_object_to_string` (exists). G3: null receiver must throw NRE
   (`f14b: nre call` expected, valuesemantics.cpp:246+) [READ:recurse-recheck.md:85-86].
2. **ArrayList** — no MethodMap file, no runtime domain [BASH: glob+grep = 0].
   Needs: runtime domain (listi4-style) + `MethodMap.ArrayList.cs` + e2e.
3. **decimal** — BinaryIO `Write(decimal)` loud-unsupported [READ:MethodMap.BinaryIO.cs:61];
   decimal formatting paths (NumberFormat) partial; T215-T216 card pool exists.
4. **UInt32 (non-Int32) box** — box/unbox validated → non-Int32 LOUD-rejected
   [READ:transpiler-audit-fixes.md:56-59]; support (not just rejection) needs
   token-dispatched box classes (boxingext has bool/char/double classes; the
   emitter still always calls `il2cpp_box_int32` [READ:transpiler-audit.md:181-182]).
5. **Interlocked skeleton** — unfilled [READ:MethodMap.Interlocked.cs:1-9];
   runtime icalls all ready (il2cpp_interlocked.h); interlockedcase fixture on
   disk. Pure wiring card.

**Chain B — verification debt (closing the batch):**
- Full unit run 589/600 — 11 red in 3 in-flight agent domains (Base64/Encoding/
  Generics) [READ:process.md:45]; re-run after those agents commit.
- e2e full-matrix (88 cases) runner + `docs/audit/e2e-full-matrix.md` NOT yet
  produced (plan.md P2-P6) — the plan-of-record next step.
- versioncase stale gen (versionuri.h missing from generated.h) [READ:memory.md:22].

**Chain C — infrastructure (unchanged from snapshot 3):**
- T101 MethodMapHub not implemented [BASH: Test-Path = False] — single-switch
  serialization bottleneck as wiring volume grows.
- gitignore `gen*/` still swallows `tests/cases/genlist/` [BASH: git ls-files =
  0, Test-Path = True] — **register before next milestone commit** (snapshot-3
  finding D2, still open; `67eb48c` tightened patterns but `gen*/` remains).

**Chain D — cross-platform residue (M6.5):**
- Warnings: 4 runtime + 2 tests gaps (atoi include; strict-aliasing; unused var;
  format-truncation; C4996 ×3) [READ:cross-platform-design.md §0.3-0.4].
- CMakeLists missing `find_package(Threads)` (monitor.cpp POSIX pthread path).
- POSIX placeholder stubs: pathproc full-path returns nullptr, temp path
  hardcodes "/tmp", thread.cpp:90-97 placeholder thread ID [READ:cross-platform-design.md §0.5].
- P/Invoke dynamic load: `mylib.dll` vs `libmylib.so` only remaining
  high-level divergence [READ:cross-platform-design.md §0.6].

**Secondary known gaps (prior evidence, unchanged unless noted):**
- versioncase gen staleness (above); WeakReference object model absent
  (MethodMap.WeakReference.cs LOUD); Thread object model absent (MethodMap.Thread.cs
  LOUD — threadcase is net48-only reference); StreamReader/StreamWriter object
  ctors unwired (MethodMap.StreamIO.cs:114-123, handle-factory vs object-ctor).

---

## 4. Next batch (batch 6+) task pool

### 4.1 Close-out of the landed batch (do FIRST)

| Priority | Task | Why now |
|---|---|---|
| P0 | **exdeep f14b fix** (objTypes local-var declared-type injection → System.Object callvirt → icall_object_to_string + null-NRE) | Longest-standing e2e red; unblocks exdeep 37-line diff + nested-exception chain verification (G5) [READ:recurse-recheck.md:85-88] |
| P0 | **e2e full-matrix run + docs/audit/e2e-full-matrix.md** (plan.md P2-P6: 88-case runner, stats table, failure attribution) | Plan-of-record task; batch-4/5 close-out gate |
| P0 | **Re-run full unit + runtime suites after in-flight agents commit** (Base64/Encoding/Generics 11 reds; regex conformance +107/+12) | Verify 600/600 + no regex regressions |
| P0 | **Register `gen*/` gitignore fix** (narrow to `/gen*/` root-only or rename genlist fixture) | Otherwise genlist never commits |
| P1 | **versionuri.h into generated.h header list** + versioncase re-gen | versioncase e2e likely red |

### 4.2 Remaining wiring (short cards, parallelizable)

| Priority | Card | Notes |
|---|---|---|
| P0 | **ArrayList** (runtime domain + MethodMap + e2e) | listi4 pattern; no file exists today |
| P0 | **Interlocked wiring** (fill skeleton switch) | Runtime icalls ready; interlockedcase fixture waiting |
| P1 | **Decimal batch** (T215-T216): BinaryIO Write/Read decimal icall + NumberFormat decimal formatting | 铁律 2b formatting paths need it |
| P1 | **Non-Int32 box support** (token-dispatched box classes: UInt32/long/double/struct) | Replaces loud-rejection; boxingext classes exist |
| P1 | **T101 MethodMapHub** (registry + tail-chain) | Wiring scale (chain C); do before more wiring volume |
| P1 | **newarr element-family extension (i8/d/b)** | Unblocks LINQ i8/d/b families (Chain A residue) |
| P2 | T301-T310 reflection three-mode batch | M5.5 L0/L1/L2 gate; mode B manifest already in place |

### 4.3 M6 close-out + matrix closure

| Priority | Task |
|---|---|
| P1 | **T605/T702/T707 bench baseline入库** (M6 exit criterion 铁律 2e) — hotpath-audit-2 numbers are the last-good baseline; P0-1/P0-2/P0-3 fixes need fresh runs |
| P1 | **3-compiler matrix closure**: msvc + mingw + wsl-gcc (and NDK 26.1 arm64-v8a compile probe → CI routine) — T701/T706 tooling |
| P2 | **M6.5 platform layer migration**: warning clearance (atoi include, strict-aliasing memcpy, C4996, format-truncation), `find_package(Threads)`, POSIX stubs (pathproc/thread tid), P/Invoke so/dll naming |
| P2 | **M5.5 trimming verification** (L0/L1/L2) once reflection modes land |
| P3 | M6.5 DLR (CallSite + custom binders, zero Emit) → M7 interpreter (metadata + IL bytecode, iOS-compliant) → M8 CLDR/COM |

### 4.4 Batch discipline (taskcards-128 §0, 铁律 2k)

Writer-uniqueness per file; GAPS.md for non-owned files; regression after each
wave: `dotnet build src/il2cppc/il2cppc.csproj` + `dotnet test
tests/unit/unit.csproj` + e2e runner; no git commit except per-里程碑 commits
(铁律 2m) — register the `gen*/` fix BEFORE the next milestone commit.

---

## 5. One-line summary

Batch 4/5 LANDED: 10 commits since snapshot 3 — 54 MethodMap wiring files
committed (28/34 domains done; Interlocked skeleton + ArrayList absent +
decimal partial remain), regex engine complete end-to-end (regexcase e2e
byte-identical vs net48), typeinfo runtime domain + CLI mode B manifest
(M5.5 in flight), transpiler stable batches (generics/nested/delegate/EH-
outer-first/catch-all/rethrow/StringPoolPass/Normalize+div-guard), encoding
POSIX dual-branch + metadata fix (79/88) + NDK probe (M6.5 batch 1), 4
net48-oracle conformance audits + hotpath audit (M7). Worktree: 46 modified,
0 untracked; unit 538 `[Fact]` (589/600 — 11 red in 3 in-flight agent
domains), runtime 88 tests, e2e 88 dirs. **Top blockers**: exdeep f14b
(callvirt System.Object), ArrayList wiring, decimal, UInt32 box support,
Interlocked skeleton; versioncase stale gen (versionuri.h missing from
generated.h). Next: close-out wave — f14b fix, e2e full-matrix doc, unit
re-verify, gen*/ gitignore registration — then batch 6 wiring + M6 exit
(bench, 3-compiler matrix, platform-layer migration).
