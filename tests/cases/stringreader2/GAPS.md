# GAPS.md -- stringreader2 recorded findings (e2e batch C70, 2026-08-12)

Case status: net48 oracle 44-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-SR2-1 -- StringReader.Read(char[], int, int) stays LOUD (inherited, no change)

- The array-buffered Read form is a NotSupportedException in
  MethodMap.StringIO.cs ("needs a managed-array shaped icall"). This case's
  deep probes (ReadLine family / EOF / nesting) express the whole line/char
  surface through the wired single-char/line/rest icalls only.

## D-SR2-2 -- Use-after-Close is a documented M3 divergence, NOT probed

- net48 StringReader throws ObjectDisposedException on use-after-Close; this
  runtime follows the M3 result conventions (read/peek -> -1, read_line ->
  null). stringrw.h documents this. Every probe here uses a live reader.

## Verified byte-exact contract (this case pins, no gap)

- CR / LF / CRLF all terminate a line; CRLF is consumed as ONE terminator;
  a lone CR or lone LF at end-of-string also terminates.
- ReadLine at EOF after a final terminator returns null; an empty line is ""
  (never null); ReadToEnd from the middle returns the remainder; Peek does
  not consume.
- Nested readers (StringWriter -> StringReader round trip; a reader built
  from a line extracted by another reader) share no cursor state.
