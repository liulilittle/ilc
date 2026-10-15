# GAPS.md -- p-stacktrace recorded findings (GN-20 legacy, P1)

Case status: **W7 whitelist** (recorded, NOT byte-exact fixable in the ILC
model). The probe `p-stacktrace.cs` is kept as the net48 measurement + red
evidence; the runtime channel cannot match it byte-for-byte.

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

- `tests/cases/p-stacktrace/` probe (net48-compiled): `expected.txt` is the
  raw net48 oracle (1237 bytes) under the zh-CN host. Runtime channel:
  `Main` fold-skips on the unwired managed members -> 0 bytes, code 1
  (`this method was not transpiled`). Probe diff stays RED by design; the case
  is documentation, not a green difftest.
- net48 frame-text proof lines (S3/S5/S6) contain absolute paths + line/col
  (`位置 F:\...\行号 36`) and CLR-internal frames (`System.Environment.GetStackTrace`)
  -- neither is statically reconstructible. S6 `Environment.StackTrace` frame
  list is additionally net48-runtime-specific.

## Verification

- Red: difftest `p-stacktrace` before fix -> transpile audit ERR; runtime path
  reported 0/1237 bytes.
- Green (this commit): `audit-internalcalls` reports the icall **OK** (mapped
  to the void stub), realErr 78 -> 77; `dotnet build src/il2cppc -c Debug`
  0 warnings / 0 errors; cmake mingw probe build exits 0 (the ctors/stub
  compile with no new warnings).