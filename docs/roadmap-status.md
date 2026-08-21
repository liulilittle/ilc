# Roadmap Status — ILC (IL→C++ AOT Compiler)

- Date: 2026-08-03 (snapshot 3 — supersedes snapshot 2; backup at
  `.opencode/backups/roadmap-status.md.20260803-s2.md`)
- Purpose: single-page status board for the M1-M8 milestone chain, the current sprint
  (this batch), the critical-path analysis, and the next batch (Batch 4/5) task pool.
- Authoritative sources (all read/measured THIS turn):
  - `git log --oneline -20` — [BASH] HEAD = `4673de0` (M6 docs + gitignore test
    artifacts); chain spans M3..M6.
  - `git status --porcelain` — [BASH] 169 entries: **149 untracked, 20 modified,
    0 deleted** (snapshot 2 reported 4 deleted — all restored, see §2.3).
  - `docs/REQUIREMENTS.md` — iron rules (2k parallel ≥24, 2h English docs, 2m
    icall-full-coverage / reflection modes, M1-M8 chain at :172).
  - `docs/alignment-tracker.md` — domain/icall totals (64 headers at its snapshot;
    **79 headers measured now**), gap work-list = `docs/audit/icall-gap-list.md`
    (exists, verified) — 174 forward gaps: P1 78 / P2 55 / P3 41.
  - `docs/plans/taskcards-128.md` — 128-card parallel backlog (T101-T708), writer-
    uniqueness audit in §八, scheduling suggestions in §九.
  - `docs/design/regex-domain.md` + `docs/design/linq-domain.md` (committed at
    4673de0) — Chain A (LINQ: eager delegate-callback icalls, NO iterator state
    machines) and Chain B (regex T1 parser / T2 matcher / T3 API+wiring) baselines.
  - In-tree test counts [BASH]: **382 `[Fact]`** in tests/unit (vs 327 at snapshot 2,
    205 at last verified-green unit run), **83 `test_*.cpp`** in tests/runtime
    (vs 81), **85 e2e case dirs** in tests/cases (unchanged).
- Legend: ✅ = committed and verified; 🟡 = in-flight (worktree-modified/untracked);
  ⬜ = not started.
- ⚠️ Snapshot warning: parallel agents (铁律 2k, ≥24) continuously create files; all
  counts are measurement-time values. Re-verify `git status` before scheduling.
  NOTE: `.gitignore` line `gen*/` silently hides untracked fixture dirs whose name
  starts with `gen` (see §3 finding D2) — untracked counts below EXCLUDE such files.

---

## 1. Milestone chain M1-M8

| Milestone | Status | Latest completion evidence (this turn) |
|-----------|:------:|----------------------------------------|
| **M1** Transpiler skeleton (IL decode → IR → C++ emit) | ✅ | Baseline per 路线图.md:13; current stack (IlDecoder/StackFold/CodeGenerator/Normalize) is its evolution. |
| **M2** Object model + mark-sweep GC + inheritance/interface/virtual/boxing | ✅ | 路线图.md:13-14 ("M2 对象模型+标记清除 GC" delivered). |
| **M3** Generational GC + string/array/EH + generics (value-type instantiation, no C++ templates) + delegates + frame records | ✅ | [BASH:git log] `a040fe2` generics (TypeSpec + Box_int), `2e6565c` delegates, `0854772` frame records, `e46110a` EH+frame, `ff368bd` value types, `75eced6` "M3 complete". |
| **M4** P/Invoke (static/dynamic binding, string marshalling, SetLastError) + Marshal icalls + icall platform layer + C API + 4-state artifacts | ✅ | [BASH:git log] `c44e8b9`→`ca09354`→`99e3a3a`→`4920126`→`dbcb615`→`f6b92be`→`296ae20` (C API exports + lib/exe split + capi tests). |
| **M5** BCL full translation (L1 trimming default, parallel domains) | 🟡 | [BASH:git log] `f1db70d` M5 BCL runtime domains committed. In-flight (all untracked, [BASH:git status]): **54 `MethodMap.*.cs`** wiring files (0 tracked), **12 runtime headers + 11 runtime impls**, **14 runtime test_*.cpp**, **26 e2e fixture dirs** with new content (see §2). Known blockers: callvirt on System.Object fails loudly (MethodMap.Object.cs:46), nested-type emission + nested base-chain resolution missing [READ:.opencode/memory.md:21]. |
| **M5.5** Trimming-level verification (L0/L1/L2) | ⬜ | 路线图.md:33 definition exists; design card T609 unused. Requires trimming + reflection modes (T301-T310) first. |
| **M6** Test suites all-green + 3-compiler CI routine + bench base | 🟡 | [BASH:git log] `d62cd80` M6 transpiler batch, `94b5b18` M6 BCL domains, `2612400` M6 runtime core, `b827e1b` M6 test suites + bench + probes, `4673de0` M6 docs. In-tree test counts [BASH]: **382 `[Fact]`** unit, **83 `test_*.cpp`** runtime, **85 e2e case dirs**. ✅ **Build gate GREEN this snapshot**: `src/il2cppc/il2cppc.csproj` restored [BASH: Test-Path = True] — snapshot 2's red gate (chain D) is RESOLVED. |
| **M6.5** DLR (CallSite + custom binders, zero Emit) | ⬜ | 路线图.md:35 — queued, requires M6 close-out. |
| **M7** Interpreter fallback (metadata + IL bytecode execution, iOS-compliant) | ⬜ | 路线图.md:36; REQUIREMENTS 铁律 3 chain. AOT main path stays the only execution path (铁律 1). |
| **M8** Full CLDR / COM interop (long-term) | ⬜ | 路线图.md:37. |

Count: 4/8 milestones completed (M1-M4), 2/8 in-flight (M5, M6), 4/8 not started
(M5.5, M6.5, M7, M8). M5/M6 executed in parallel (24+ agents, 铁律 2k).

---

## 2. Current sprint (this batch) — checklist

### 2.1 Wiring — 54 untracked `MethodMap.*.cs` (count unchanged vs snapshot 2)

[BASH: `(Get-ChildItem src/il2cppc/Emit -Filter 'MethodMap*.cs').Count` = 54; all
UNTRACKED, `git ls-files` = 0]. Dispatch still routes through the worktree-modified
`Emit/MethodRefResolver.cs` single switch; **T101 MethodMapHub still does NOT exist**
[BASH: Test-Path = False] — see §3 chain C. New this snapshot: 17 untracked
`tests/unit/MethodMapTests.*.cs` + `MethodRefResolverDispatchTests.cs` — per-domain
wiring unit tests are landing in parallel with the wiring files.

Domains covered by the 54 files (grouped): Array/ArraySort, Attribute, Base64,
BinaryIO, BitConverter, Buffer, Console, Convert(+Extra), CultureInfo, DateTime
(+Extra), Debug, Delegate, Dictionary, Encoding, Enum, Environment(+Extra),
Exception, Format, GC, Guid, Hashtable, IntPtr/UIntPtr, Interlocked, IO, List,
Math(+Extra), Monitor, NumberFormat, Object, Queue, Random(+Extra), Regex, Stack,
StackQueue, Stopwatch, StreamIO, String(+Builder+Ext3), Thread, TimeExt2, TimeSpan,
Type, Uri, ValueType, Version, WeakReference. (Same 54-set as snapshot 2 — no new
wiring files this interval; unit tests grew instead.)

### 2.2 Runtime — 12 untracked headers + 11 untracked impls (up from 12+9)

[BASH: git status untracked list under src/runtime]:
- **LINQ trio complete**: `linq.h`+`linq.cpp`, `linq_map.h`+`linq_map.cpp`,
  `linq_sort.h`+`linq_sort.cpp`, + 3 `test_linq*.cpp` — matches linq-domain.md
  T1/T2/T3 file domains (eager icall design, no iterator state machines).
- **List/Dictionary complete**: `listi4.h`+`listi4.cpp`, **`dictsi.h`+`dictsi.cpp`
  (impl NEW this snapshot** — snapshot 2 had header only) + `test_listi4.cpp`,
  `test_dictsi.cpp` — Chain A steps 2-3 now have full runtime domains.
- **Thread extensions + ThreadPool**: `threadext.h`+`threadext.cpp`,
  `threadpool.h`+`threadpool.cpp` + `test_threadext.cpp`, `test_threadpool.cpp`.
- **Array extra**: `arrayextra.h`+`arrayextra.cpp` + `test_array_extra.cpp`
  (NEW this snapshot).
- **IO/string/marshal helpers**: `pathproc.h`+`pathproc.cpp`, `stringrw.h`+`stringrw.cpp`
  (verified this session, 0-warning build + all tests passed — see memory.md),
  `il2cpp_marshal_ext2.h`+`marshalext2.cpp` (+ `test_pathproc.cpp`, `test_stringrw.cpp`,
  `test_marshal2.cpp`).
- **Regex engine STARTED**: `regex_ast.h` present (T1 contract header per
  regex-domain.md:396) but **NO `regex*.cpp` anywhere** and no regex runtime test
  [BASH: src/runtime/src regex* count = 0, tests/runtime test_regex* count = 0] —
  parser/matcher/API not implemented, see §3 chain B.
- Total runtime headers measured: **79** (alignment-tracker snapshot said 64; growth
  from parallel new-domain work).

### 2.3 Transpiler — ✅ BUILD GATE GREEN (resolved from snapshot 2)

- **`src/il2cppc/il2cppc.csproj` EXISTS again** [BASH: Test-Path = True; it is the
  only csproj under src/]. Snapshot 2's red gate (chain D) is resolved — the
  in-flight refactor either restored it or never deleted the tracked file
  (git status: 0 deleted entries [BASH]). `dotnet build`/`dotnet test` verification
  waves are possible again.
- Modified (20 files, [BASH]): `CodeGenerator.cs`, `MethodRefResolver.cs`,
  `IlDecoder.cs`, `MethodBodyReader.cs`, `Expr.cs`, `StackFold.cs`, `Program.cs`,
  `Normalize.cs` (UNTRACKED — new copy), `CliOptions.cs` (untracked new),
  runtime: `il2cpp_array.h`, `il2cpp_stringbuilder.h`, `array.cpp`, `arrayext.cpp`,
  `gcapi.cpp`, `string.cpp`, `stringbuilder.cpp` (NEW modified vs snapshot 2 —
  in-flight StringBuilder wiring), `valuesemantics.cpp`; tests: `test_gcapi.cpp`,
  `test_valuesemantics.cpp`, `CliTests.cs`, `IlDecoderTests.cs`, `StackFoldTests.cs`.
- Unit test growth: 18 untracked test files (17 `MethodMapTests.*` +
  `MethodRefResolverDispatchTests.cs`); in-tree `[Fact]` = **382** [BASH:
  Select-String count] (327 at snapshot 2, 205 at last verified-green run).
  ⚠️ These new tests are UNVERIFIED against the restored build — the next
  verification wave must run `dotnet build src/il2cppc/il2cppc.csproj` +
  `dotnet test tests/unit/unit.csproj` to confirm green.

### 2.4 e2e — 85 case dirs total [BASH]; 26 dirs with untracked content

New untracked fixture content (26 dirs, [BASH: git status under tests/cases]):
fully-new dirs: arraybounds, base64case, bitconvcase, boxstr, cctorchain, delechain,
encodingcase, fmtchain, guidcase, interlockedcase, propcase, swcase, versioncase;
existing dirs with additions (expected.txt etc.): boxdeep, boxing2, datetimechain,
delegate2, exdeep, filechain, formatext, hashtable2, internchain, mathextra, recurse,
recursive_arith, sbchain. Plus new runtime tests (test_arraybounds, test_boxedtostring,
test_string_utf8, etc.).
⚠️ **`tests/cases/genlist/` exists on disk (genlist.cs + csproj + expected.txt +
out/) but is git-INVISIBLE** — `.gitignore` line `gen*/` (committed at 4673de0)
matches any `gen*` directory name, swallowing the untracked genlist fixture
[BASH: `git ls-files tests/cases/genlist/*` = 0; Test-Path = True]. The pre-existing
`generics/` fixture is unaffected (already tracked — gitignore does not apply to
tracked files). See §3 finding D2.

### 2.5 Docs — 4 audit probes + design docs + this file

- docs/audit/: gc-recheck.md, generics-probe.md, msvc-probe.md, wsl-probe.md
  (untracked, in-flight) + icall-gap-list.md, audit-summary.md (present, verified).
- docs/design/regex-domain.md + linq-domain.md **committed** (4673de0) — Chain A/B
  design baselines [BASH: Test-Path = True].
- docs/architecture/string-pooling-design.md (untracked, prior session);
  docs/user-guide.md (untracked); docs/roadmap-status.md (this file, this task).
- This session's StringReader/StringWriter domain completed and verified
  (stringrw.h/.cpp + test_stringrw.cpp, 0 warnings, all tests passed) — see
  `.opencode/memory.md:9-16`.

---

## 3. Critical-path analysis

**Chain A — generics → List<T> → LINQ → ToList (longest IN-FLIGHT chain):**
1. Generics foundation — ✅ committed (`a040fe2`; `d62cd80` TypeEmitter/VTablePlanner).
2. `List<T>` runtime — ✅ runtime domain COMPLETE this interval: `listi4.h`+`listi4.cpp`
   + `test_listi4.cpp` [BASH]. (Snapshot 2: header+impl already; now tests landed.)
3. `Dictionary<K,V>` — 🟡 **impl landed**: `dictsi.h`+`dictsi.cpp` + `test_dictsi.cpp`
   [BASH] (snapshot 2: header only).
4. LINQ runtime helpers — 🟡 linq.cpp/linq_map.cpp/linq_sort.cpp + 3 tests landed.
5. **Iterator-state-machine risk — RESOLVED BY DESIGN.** linq-domain.md:4 ("本域不做
   迭代器状态机") + :48 (eager materialization, arrays→arrays icall chains). Phase 1
   needs NO IEnumerable/IEnumerator. Remaining make-or-break items:
   (a) **Enumerable dispatch wiring — STILL UNOWNED**: no `System.Linq.Enumerable`
   case in BclResolveCore yet (linq-domain.md:201); wiring requires genArgs[0] →
   element-family table + `arg0Type` array-source check + ReturnsRef on array
   results (linq-domain.md:204-207); no taskcard covers it (T204/T206 are
   e2e/runtime-shaped; T206's MethodMap.Linq.cs is the closest) — see §4.2;
   (b) **captured-lambda rejection** — instance-bound delegates (target!=null) are
   loud-rejected; only static/non-capturing lambdas work (linq-domain.md:72-78);
   (c) **newarr element-family limit** — only int32[] and ref arrays constructible
   (CodeGenerator.cs:1873-1880 per linq-domain.md:40); i8/d/b LINQ families gated on
   newarr extension.
6. ToList + linqcomb e2e — ⬜ (genlist fixture on disk but git-hidden, see D2;
   T515 linqcomb not started).

**Chain B — Regex T1→T2→T3→wiring→e2e (longest COLD chain, STARTED but stalled):**
1. T1 parser (`regex_ast.h` + `regex_parse.cpp`) — 🟡 **contract header landed**
   (regex_ast.h untracked), **parser NOT implemented** [BASH: no regex*.cpp in
   src/runtime/src, count = 0].
2. T2 matcher (`regex_match.h/.cpp`) — ⬜ (engine entry declaration is T2's output,
   regex-domain.md:410-412).
3. T3 API + wiring (`regex.h`/`regex_api.cpp` + MethodRefResolver RegexDispatch) — ⬜
   (`MethodMap.Regex.cs` ALREADY EXISTS untracked — wiring is ahead of the engine;
   it will hit missing symbols / loud NotSupported until the engine lands).
4. regex1 e2e — ⬜.
Next dispatch: complete T1 parser, then T2/T3 in parallel (disjoint file domains,
regex-domain.md §5: T2 needs regex_ast.h, T3 needs T2's entry decl).

**Chain C — wiring scale risk (infrastructure):**
`MethodRefResolver.cs` remains the single dispatch switch (worktree-modified);
54 partial files + 36+ inline cases route through it; **T101 MethodMapHub (registry
+ tail-chain) is NOT implemented** [BASH: Test-Path MethodMapHub.cs = False]. Every
new wiring card touches the same file → serialization bottleneck + edit-conflict
surface. Pull T101 into Batch 4 before BCL wiring volume grows further.

**Chain D — build gate risk: ✅ RESOLVED (this snapshot).**
Snapshot 2 flagged `src/il2cppc/il2cppc.csproj` deleted (0 csproj under src/).
Now: csproj exists [BASH: Test-Path = True], 0 deleted git entries, and git log
shows no new commit — the file was restored in the worktree (or the earlier
deletion was reverted by the in-flight refactor). Verification waves can resume;
run the full gate once before dispatching cards that need `dotnet build`/`dotnet test`.

**Finding D2 — gitignore `gen*/` collides with `gen*` fixture dirs (NEW):**
`.gitignore` line 9 `gen*/` (intended for generated-code output) also matches
`tests/cases/genlist/` — its source files are invisible to `git status` and would
NOT be committed by a milestone commit. Existing `generics/` is unaffected (tracked).
Fix options: narrow pattern to `gen/` + `gen_test/` (keep `gen*/` only at repo root
`/gen*/`) or rename fixture. Register before next milestone commit.

**Secondary known gaps (prior batches, evidence in memory/discover):**
- M3 EH shape limits: nested catch, sibling-catch dispatch (C++ bare `throw;`
  skips sibling handlers), catch-all → System.Object type resolution
  [READ:.opencode/discover.md:4-10].
- callvirt on System.Object receiver (MethodMap.Object.cs:46) [READ:memory.md:21].
- INT_MIN/-1 constant-fold division overflow [READ:discover.md:7].
- Interlocked not yet wired (MethodMap.Interlocked.cs skeleton → static-icall
  misclassified as instance → StackFold "pops this but stack is empty"); runtime
  icalls all 10 ready (il2cpp_interlocked.h:38-76); interlockedcase fixture exists
  [READ:memory.md:10-12].

---

## 4. Next batch (Batch 4/5) task pool

### 4.1 Pull from taskcards-128 (unused cards, ordered by criticality)

| Priority | Cards | Why now |
|---|---|---|
| P0 | **T101 MethodMapHub + registry refactor** | Unblocks wiring scale (chain C); T102-T135 depend on it; 54 files already waiting |
| P0 | **Regex T1 parser completion → T2/T3 parallel** (regex-domain.md §5 cards) | Chain B half-started and stalled at T1; MethodMap.Regex.cs already waiting |
| P0 | **LINQ wiring card** (Enumerable dispatch, genArgs element-family table, arg0Type array check, ReturnsRef) + linqcomb e2e (T515) | Chain A step 5a; runtime helpers (linq trio) fully landed, dispatch unowned |
| P0 | **Register fix for finding D2** (gitignore `gen*/` vs genlist fixture) | Otherwise genlist never commits; milestone commit would silently drop it |
| P1 | **T220 ThreadPool wiring + T228 Task** | threadpool.cpp + test_threadpool.cpp exist — impl gap closed, only wiring remains |
| P1 | **newarr element-family extension (i8/d/b)** | Unblocks LINQ i8/d/b families + audit C4 (int64 truncation family) |
| P1 | **T301-T310 reflection three-mode batch** | 铁律 2m/2g-2; M5.5 trimming gate depends on it |
| P1 | **T605 bench design + T702 bench skeleton + T707 bench-regress** | M6 exit criterion (基准基线入库, 铁律 2e) |
| P1 | **First verification wave** (dotnet build + dotnet test + unit) | 382 [Fact] incl. 18 untracked test files are UNVERIFIED since csproj restore |
| P2 | T207-T209 XML / T210-T211 crypto / T212-T213 network | New runtime domains, disjoint file domains, 24-agent parallelizable |
| P2 | T215-T216 Decimal/BigInteger, T217-T218 DateTimeOffset/TimeZoneInfo | Value-type numeric domains; decimal needed by formatting paths |
| P2 | T219 StackTrace, T221-T227 WaitHandle/MemoryStream/TextStream/BinaryIO/Deflate/Process/Calendar | Completes IO/threading matrix |
| P2 | T401-T420 optimization passes (PassRunner first; T402 property folding is the M5 roadmap item) | 铁律 2e/2j |
| P3 | T501-T515 e2e expansion, T601-T610 docs, T701-T708 tooling | Parallel slack fillers; T701/T706 unblock 3-compiler routine |

### 4.2 Gap-driven pool (exposed by THIS batch, not in taskcards)

1. **LINQ wiring is unowned** — linq-domain.md §6 lists exact changes (BclResolveCore
   Enumerable case, genArgs dispatch, arg0Type array check, ReturnsRef) but no
   taskcard covers it; needs a dedicated wiring card or T206 scope-up (P0 above).
2. **Regex wiring/engine ordering** — MethodMap.Regex.cs exists ahead of engine;
   either finish engine fast (P0 above) or gate the file behind engine presence.
3. **gitignore D2 fix** — narrow `gen*/` or rename genlist fixture; register before
   milestone commit.
4. **Verification debt** — 18 untracked unit-test files + restored csproj: run the
   full gate (build + unit + e2e 双跑) and record green/red before more dispatch.
5. EH shape fixes: sibling-catch dispatch, catch-all → System.Object, nested
   catch/finally region ordering [READ:discover.md:5-10].
6. callvirt on System.Object receiver — MethodMap.Object.cs:46 loud failure blocks
   full exdeep translation [READ:memory.md:21].
7. INT_MIN/-1 constant-fold division overflow — TryFoldInt32 must throw, not wrap
   [READ:discover.md:7].
8. Nested type emission + nested base-chain resolution — TypeEmitter domain
   [READ:discover.md:13].
9. Interlocked wiring (T128 card body exists; runtime icalls all ready) —
   interlockedcase fixture is on disk and currently aborts the full e2e script.

### 4.3 Batch discipline (taskcards-128 §0 and 铁律 2k)

Writer-uniqueness per file; new fixture names pre-registered (taskcards §八 audit);
GAPS.md for anything touching a non-owned file; regression after each wave:
`dotnet build src/il2cppc/il2cppc.csproj` (RESTORED — gate green) +
`dotnet test tests/unit/unit.csproj` + e2e 双跑; no git commit (user constraint,
except per-里程碑 commits per 铁律 2m git-local-backup rule — commit BEFORE touching
the gen*/ gitignore pattern).

---

## 5. One-line summary

M1-M4 ✅ committed; M5/M6 🟡 in-flight (54 wiring files, 12 headers + 11 impls,
14 runtime tests, 26 e2e dirs untracked; LINQ trio + listi4 + dictsi + threadpool +
stringrw verified; regex chain stalled at T1 contract header); M5.5/M6.5/M7/M8 ⬜.
**Snapshot-2 red gate resolved: transpiler csproj restored (0 deleted files), build
gate GREEN — first priority is a verification wave over the 382 untracked-adjacent
[Fact] tests.** Longest in-flight chain = generics→List→LINQ→ToList (runtime helpers
complete; gated on unowned Enumerable wiring + newarr family extension); longest cold
chain = regex T1→T3 (parser next). New finding: `.gitignore` `gen*/` hides the
genlist fixture — register before next commit. Batch 4 = T101 hub + regex completion
+ LINQ wiring + D2 fix + reflection batch + verification wave.
