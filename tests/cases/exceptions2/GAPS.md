# GAPS.md — exceptions2 recorded findings (e2e batch C81, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle byte-exact,
25 lines). Active probes cover EXCEPTION DEPTH 4: the 3-deep nested
try/finally unwinding order, 2-level finally on the normal path (return
value survives), try/finally inside a catch, nested try/finally + catch
during unwinding, catch-variable DATA at multiple levels (GetType().Name +
Message presence), rethrow through a nested finally, and the exception
factory data surface. Gaps below are RECORDED, NOT FIXED (this batch
creates test cases only).

## D-EX4-1 — finally-throws-during-unwind emits a localized net48 stderr trace

- **Phenomenon**: `try { throw "orig"; } finally { throw "replace"; }` —
  net48 writes a LOCALIZED unhandled-exception trace to STDERR (the zh-CN
  machine prints the Chinese "未处理的异常" + the source path + line number,
  exceptions2.cs:87) BEFORE the outer catch runs. The harness diffs stdout
  AND stderr byte-for-byte; the stderr text is culture/path/line-dependent,
  so the shape is NOT byte-stable. (The CATCH outcome itself is
  deterministic: the outer catch receives the finally's exception.)
- **Confirmed**: the finally-throws probe was removed; section 4 instead
  probes nested try/finally + catch during unwinding with the finally NOT
  throwing (byte-stable on every line).
- **Suspected root file**: none (net48 runtime behavior; the harness diff
  contract forbids culture-dependent stderr).

## D-EX4-2 — transpiler EH "dup on empty stack" with many EH regions in one method

- **Phenomenon**: a single method carrying 7 nested catch/finally regions
  aborts the transpile with `il2cppc error: dup on empty stack at offset
  0x001D/0x0264` (StackFold.cs:349). Root cause (isolated by bisection): a
  CATCH HANDLER that reads its catch variable twice via Roslyn's `dup`
  emission (`catch (Exception ex) { rr1 = (ex.GetType()...); rr2 =
  (ex.Message...); }` emits `dup; callvirt GetType; ... get_Message`), which
  StackFold cannot fold when the handler ALSO participates in a sibling/
  nested EH chain. PRINTING the reads inside the handler
  (`Console.WriteLine(... ex.GetType()...); Console.WriteLine(... ex.Message...)`)
  avoids the dup and transpiles clean.
- **Confirmed**: the case was restructured so every catch-variable read
  PRINTS inside its handler (the excextdeep convention) — transpiles clean
  on both compilers; a single exact-type KeyNotFoundException catch at the
  END of a many-region method also triggered the abort and was dropped from
  the case.
- **Suspected root file**: `src/il2cppc/Ir/StackFold.cs` Dup / EH-catch
  handler handling (dup-on-catch-variable in nested region chains).

## D-EX4-3 — carried-over exception-domain gaps (unchanged from exops2/excextdeep)

- InnerException (get_InnerException unmapped) — D-EX2-1, NOT probed.
- Custom exception ctor messages (only BCL ctor args forward) — D-EX2-2,
  NOT probed.
- StackTrace / Data (IDictionary) / HelpLink / Source / TargetSite — unmapped
  (excextdeep GAPS), NOT probed.
