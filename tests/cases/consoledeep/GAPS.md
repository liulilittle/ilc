# GAPS.md — consoledeep unwired/divering surface (e2e batch C18, 2026-08-10)

Case status: **PASS 21/21 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the console
STREAM domain -- get_Out/get_Error getters, SetError(Console.Out) error-stream
folding (order determinization), SetOut/SetError(TextWriter.Null) swallow +
restore, SetOut(null)/SetError(null) ArgumentNullException -- and is
byte-identical to net48. No product code modified; no commit made.
(compiler=wsl BLOCKED at HEAD by the shared-runtime D-C18-WSL-1, see the batch
report.)

## Design note — why the error stream is FOLDED, not probed raw

- The harness captures the reference run with `& exe 2>&1` (run-tests.ps1:52).
  When Console.Error writes go to stderr while Console.Out writes go to stdout,
  the merged ordering is governed by each process's stdout block-buffering vs
  stderr unbuffering -- console3's expected.txt demonstrates the hazard
  (`outline1` written AFTER `errline1` appears FIRST in the capture). To make
  the error-stream ORDER deterministic, consoledeep calls
  `Console.SetError(Console.Out)` BEFORE any output: every Console.Error write
  then routes through the Out stream object to stdout (console.cpp
  StreamTarget: Out kind -> stdout), so the capture is ONE stream in PROGRAM
  order (e1,o1,e2,o2,...). This is both the determinization and a redirection
  probe in one. net48 SetError(Console.Out) folds identically (the error
  TextWriter wraps the same underlying stdout).
- The swallow probes rely on the ILC contract: SetOut(TextWriter.Null) swaps
  the Out stream to the discarding kConsoleStreamNull object AND freopen's the
  underlying C stdout to NUL (console.cpp RedirectStdStream), so even the M1
  core icalls (icall_console_write_line_s) are swallowed; SetOut(saved) restores
  via the saved fd (console.cpp:418-430). All byte-exact vs net48.

## Notes

- net48 oracle evidence: `out/consoledeep.exe` exit=0, 21 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C18 report.
- No defect found on this surface at HEAD: the entire console-stream domain
  (the old batchB D-CON-2 gap) is wired and byte-exact.
