# GAPS.md -- p-stacktrace recorded findings (GN-20 legacy, P1)

Case status: **W7 whitelist** (recorded, NOT byte-exact in the ILC model), but
**no longer a rejection case**: since `stacktrace-real` (2026-11-01) the probe
TRANSPILES, BUILDS and RUNS (exit 0, real frame text), because
`Environment.StackTrace` and the StackTrace/StackFrame surface are implemented
over the runtime's shadow frame stack (`src/runtime/src/stacktrace.cpp`). The
case stays in `scripts/sweep-exclude.txt` only for the residual FORMAT
divergences listed at the bottom of this file (PDB file/line info, the
CLR-internal `System.Environment.GetStackTrace` frames, and the
exception-attached frame list).

Superseded below: the older notes that describe the managed StackTrace surface
as "not wired in M5" / "not reconstructible" are kept as history -- every
member the probe calls is now a real icall, and the two messages that claimed
net48 lacked them were false.

## Why byte-exact is impossible (measured net48, zh-CN host)

`System.Diagnostics.StackTrace.GetStackFramesInternal` (net48 real ABI:
`static void (StackFrameHelper sfh, int iSkip, bool fNeedFileInfo, Exception e)`)
is the CLR *live-stack enumerator*. Its observable output depends on:

1. **Live runtime call stack** -- which managed methods are physically on the
   CLR stack at capture time and how many frames the CLR's
   `CalculateFramesToSkip` semantics keep. Measured: `new StackTrace()` in
   `Main` -> 1 frame; with `skipFrames=1` -> 0; exception capture -> 2 frames
   (`ThrowInner`, `Main`); `Environment.StackTrace` additionally reports the
   CLR-internal `System.Environment.GetStackTrace(Exception e, Boolean
   needFileInfo)` and `System.Environment.get_StackTrace()` native frames.
   The transpiled runtime has no managed-address stack to enumerate (the
   `il2cpp_frame` shadow stack exists only for GC roots of ref-local methods,
   records the *transpiled C++ name*, not the managed signature).
2. **Absolute PDB source path + line/col** -- `fNeedFileInfo=true` frames and
   every exception path embed `位置 F:\...\p-stacktrace.cs:行号 36` (the zh-CN
   resource spellings of `at`/`in`/`line`). These are build-machine absolute
   paths + PDB-derived line/column numbers; a transpiled AOT runtime has
   neither the path nor the PDB.
3. **Localized resource markers** -- zh-CN host renders `在`/`位置`/`行号`
   from mscorlib locale resources (en-US would render `at`/`in`/`line`).
4. **CLR-internal frame list** -- the exact frame set (internal helper frames,
   skip-n-returned) is an undocumented CLR stack-walking artifact.

=> W7 condition ("net48 输出依赖 CLR 内部栈指针不可静态还原") holds.
Reproducing it byte-exact requires porting the CLR stack walker + PDB reader +
locale resources, all outside the transpiler's native surface.

## Fixed here (mechanical gap closure only)

- **MethodMap routing bug** (`MethodRefResolver.cs`): `System.Diagnostics.StackTrace`
  sat in the WeakReference-dispatch bucket, where `TryMapWeakReference` THROWS
  on foreign members, so the `?? TryMapGC` fallback never ran and
  `GetStackFramesInternal` errored as a realErr (GN-20 P1 audit: the ONE
  user-reachable internal-call gap). Like `System.Runtime.GCSettings`, it is
  now dispatched directly to the GC map. Audit after fix:
  `OK System.Diagnostics.StackTrace.GetStackFramesInternal` (realErr 78 -> 77).
- **Stub ABI aligned** to net48 (`stubs.cpp` / `stubs.h`): the old stub was
  `(Il2CppObject*, int32_t, int32_t) -> Il2CppObject*` (3 args, object return);
  net48's real signature is a `void (StackFrameHelper, int, bool, Exception)`.
  The cvall now carries 4 args / void; the stub is the clean whitelist
  placeholder (leaves the helper empty = 0 frames). The MethodMap `I(...)`
  flags/casts were updated to match.
- Consequence (D2): user methods that call `new StackTrace()` with ONLY
  construction now transpile and build cleanly -- `BclAllocExpr` routes the
  allocation to a plain object and `TryMapGC`'s `.ctor` arm maps every ctor
  arity to the documented no-op `icall_stacktrace_ctor` (byte-exact regression
  case `../p-stacktrace-ctor/`, diff=0). The rest of the StackTrace *managed
  surface* (`get_FrameCount`, `ToString`, `GetFrames`, `StackFrame.GetMethod`,
  ...) is NOT wired in the transpiled runtime -- those calls fold-skip with
  the documented "not wired in M5" reason (this is the recorded W7 deviation;
  the whole class is not reconstructible, so a downstream embedder gets
  empty-frame StackTraces rather than fabricated ones).

## D1/D2 audit corrections (commit c3c82682 rework)

- **D2 (build regression)**: the first commit routed StackTrace to TryMapGC but
  left the bare ctor without a `.ctor` arm -> newobj fell to the BclAlloc fresh
  fallback -> `&System_Diagnostics_StackTrace_class` + phantom ctor -> cmake
  FAIL. Fixed here: BclAllocExpr plain-object allocation + `icall_stacktrace_ctor`
  no-op for every ctor arity. See `../p-stacktrace-ctor/GAPS.md`.
- **D1 (sweep registration)**: the W7 probe is registered in the gn-20-sweep
  pool (`lists/list_*.txt` / `shards/shard*.list`, union now 699 = the
  worktree csproj-count) and in `.opencode/tmp/gn-20-sweep/whitelist.txt`
  (W7 expected-FAIL, check-final accepts it); `p-stacktrace-ctor` is in the
  pool and byte-exact GREEN (NOT whitelisted).

## Recorded, NOT fixed (W7; evidence)

- SUPERSEDED (stacktrace-real, 2026-11-01): the old text here said the runtime
  channel "fold-skips on the unwired managed members -> 0 bytes, code 1".
  That is no longer true. Measured: transpile reports **0** `method fold
  failed` warnings, `gen/src/Cases_PStackTrace.cpp` carries frame records for
  `Main`/`Probe`/`OnPeek`/`Throws`/`Params`/`Strip`, and the built exe exits
  **0** with non-empty stdout (`[S1.count] 1`, `[S1.tostr]    at
  Cases.PStackTrace.Main()`, ..., `[S8.onpect] 1`).
- The case is STILL not byte-exact, for these measured reasons:
  1. net48 frame text carries absolute PDB paths + line/col
     (`位置 F:\...\行号 36`); an AOT build has no PDB, so
     `GetFileName()` is null and the line/column numbers are 0 (net48's own
     answer for a frame built without file info) -- the ` in ...:line N`
     suffix is absent.
  2. net48's `Environment.StackTrace` (S6) additionally lists its own
     CLR-internal `System.Environment.GetStackTrace(Exception e, Boolean
     needFileInfo)` / `get_StackTrace()` frames; the runtime has no such
     helpers, so S6 starts at the first managed frame.
  3. S4 (`new StackTrace(ex)` after a catch) reports the frames alive at
     construction (1: `Main`) instead of the exception's throw-time frames
     (2: `ThrowInner`, `Main`) -- exception objects here carry no frame list.
  4. S5 (`ex.ToString()`) prints only the exception type; the exception
     message/frame text is a separate pre-existing surface.
  5. The localized marker word (zh-CN `在`) renders as the invariant `at`.

## Verification

- Red: difftest `p-stacktrace` before fix -> transpile audit ERR; runtime path
  reported 0/1237 bytes.
- Green (this commit): `audit-internalcalls` reports the icall **OK** (mapped
  to the void stub), realErr 78 -> 77; `dotnet build src/il2cppc -c Debug`
  0 warnings / 0 errors; cmake mingw probe build exits 0 (the ctors/stub
  compile with no new warnings).