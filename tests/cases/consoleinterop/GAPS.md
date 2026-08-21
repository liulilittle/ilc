# GAPS.md — consoleinterop unwired / diverging surface (e2e batch C63, 2026-08-12)

Case status: **PASS 64/64 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the typed
Write/WriteLine overload matrix (incl. int/long min-max, uint/ulong max,
double/float raw), tracked-box object overloads, no-newline Write combos,
the composite String.Format "{N,w}"/"{N:f2}" alignment kernel, code-unit
encoding probes and the stream fold/swallow/restore interaction
(SetError(Console.Out), SetOut(TextWriter.Null), SetOut/SetError(null) ANE).
No product code modified; no commit made.

## No divergence found at HEAD on this surface

The entire console-stream + format + encoding surface is wired and
byte-exact: get_Out/get_Error getters, SetOut/SetError (TextWriter.Null /
null-ANE), the error-stream fold into stdout, the composite format kernel
(alignment + precision), and the typed output matrix.

## Not probed (recorded convention)

- **Wide-char RAW output** — codepage-fragile on this host (net48 decodes
  via the ANSI codepage, the transpiled side via UTF-8). Encoding coverage
  uses code-unit probes ((int)char, string.Length, Substring) that stay
  encoding-independent; the consoledeep/consolestream convention.
- **Console.ReadKey / ReadLine-with-input / IsInputRedirected** — no
  deterministic harness input path (ReadKey blocks on EOF; IsInputRedirected
  is LOUD in MethodMap.Console.cs).
- **SetOut(arbitrary TextWriter subclass)** — only the console streams /
  TextWriter.Null are wired; a custom TextWriter is the redirection domain
  of a future milestone.
