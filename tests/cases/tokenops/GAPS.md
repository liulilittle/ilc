# GAPS.md -- tokenops recorded findings (e2e batch C61, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 52-line
byte-exact, all True). Active probes cover the WIRED Split surface
(icall_string_split + icall_string_split_options).

## D-TOK-1 -- whitespace-mode probes use the 1-arg / explicit-args shapes (null-arg variance)

- **Phenomenon**: this case's whitespace-mode probes call `Split((char[])null)`
  (1-arg) and `Split((char[])null, StringSplitOptions.RemoveEmptyEntries)`
  (2-arg with an explicit null separator array). Both route to the full
  Char.IsWhiteSpace set in the runtime (`whitespaceMode` when separators is
  null or length<=0). The `Split((string[])null, ...)` shape was not probed
  because the C# overload resolution picks `Split(char[], ...)` only when the
  null literal is cast; the string[] null shape resolves to the string[]
  overload and is equally valid -- recorded here so future cases know both
  array shapes reach whitespace mode.
- **Fix path (future)**: none (runtime behavior matches net48; this is a
  coverage-completeness note).

## D-TOK-2 -- first-match string[] separator greedy ordering is order-sensitive (pinned, not a gap)

- **Phenomenon**: `Split("xaaay", new[]{"a","aa"})` yields 4 segments while
  `Split("xaaay", new[]{"aa","a"})` yields 3 -- the runtime breaks on the FIRST
  matching element in array order (non-overlapping greedy), matching net48
  (verified by the rtspl2 probe per the stringext.cpp header). The u4/u5
  probes pin this; recorded here because a future author might assume
  longest-match-wins and write a case that flips.
- **Fix path (future)**: none.
