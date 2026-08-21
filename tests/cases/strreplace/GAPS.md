# GAPS.md — strreplace unwired/divering surface (e2e batch C18, 2026-08-10)

Case status: **PASS 65/65 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the FULLY
WIRED string icalls -- Replace(char,char) / Replace(string,string) (stringext.
cpp icall_string_replace:343 / icall_string_replace_s_s:397) and Split with
StringSplitOptions (icall_string_split_options:823, char[] AND string[] +
None/RemoveEmptyEntries + invalid-enum ArgumentException) -- and is
byte-identical to net48. No product code modified; no commit made.
(compiler=wsl BLOCKED at HEAD by the shared-runtime D-C18-WSL-1, see the batch
report.)

## Notes

- The task's "解锁 icall 后" precondition holds at HEAD: replsplit (C-batch
  predecessor) covered Replace(string,string) + Split(char[]) single-form; this
  case extends to the options surface (`Split(char[]/string[], StringSplitOptions)`)
  and the char/char Replace identity/all-occurrence/unicode forms. All wired.
- Probed net48 semantics (all byte-exact): char-replace no-match returns the
  IDENTITY instance; oldChar==newChar WITH a match returns a NEW content-equal
  instance (no identity fast path, stringext.cpp:337-341 baseline); string
  replace non-overlapping greedy, null/empty newValue DELETES, no-match returns
  identity, empty oldValue ArgumentException / null oldValue ArgumentNullException.
- Split options: None RETAINS empty segments; RemoveEmptyEntries drops them
  (all-separator -> [], empty receiver -> [], whitespace collapses); null/empty
  separator array routes to the WHITESPACE overload; string[] separators are
  FIRST-match-in-array-order ({"a","aa"} len 4 vs {"aa","a"} len 3) with null
  and empty elements skipped; invalid options (2 / -1) throw ArgumentException
  BEFORE splitting.
- net48 oracle evidence: `out/strreplace.exe` exit=0, 65 lines captured
  byte-for-byte; harness diff PASS (mingw + msvc), see the C18 report.
