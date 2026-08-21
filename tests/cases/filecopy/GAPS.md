# GAPS.md — filecopy unwired/divering surface (e2e batch C19, 2026-08-10)

Case status: **PASS 19/19 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; compiler=mingw). The active surface drives the File.Copy /
File.Copy(overwrite) / File.Move exception MAPPING (io.cpp throw paths) end
to end: missing-source -> FileNotFoundException, existing-destination ->
IOException, overwrite semantics, base-catch (IOException catches the
FileNotFoundException), source-preservation on a failed Move, and the success
paths re-verified with the mapping active. No product code modified; no commit
made. (compiler=msvc unverifiable on this machine -- no VS2022; compiler=wsl
BLOCKED by the C18 report's shared-runtime build defect D-C18-WSL-1.)

## D-FC-1 — null path arguments are not mapped to .NET exceptions

- **Phenomenon (recorded, probes ABSENT)**: net48 `File.Copy(null, dst)` /
  `File.Copy(src, null)` / `File.Move(null, dst)` throw ArgumentNullException;
  the io.cpp icalls return -1 SILENTLY for a null source or dest
  (io.cpp:655 / 699 / 742 -- the `-1` null-path convention). A probe would
  print the catch flag True on net48 and False on ILC -> permanent diff FAIL,
  so the null paths are deliberately absent from the active set.
- **Suspected root file**: `src/runtime/src/io.cpp` icall_file_copy /
  icall_file_copy_overwrite / icall_file_move (throw
  il2cpp_throw_argument_null_exception("source"/"dest") instead of returning
  -1). The transpiler-side MethodMap.IO.cs mapping needs no change.

## D-FC-2 — BCL failure messages are localized on net48, English on ILC

- **Phenomenon (recorded, handled in the probe design)**: on this machine
  net48 localizes the IOException / FileNotFoundException message text
  (Chinese); the io.cpp carriers are hardcoded English (exceptionext.cpp).
  The case therefore probes ONLY the catch flag + ex.GetType().Name (both
  invariant) and file-state flags; raw message text is never printed. This is
  the same family as exops2's D-EX2-3.
- **Also recorded**: io.cpp's IOException payload for an existing-destination
  Copy/Move on the _WIN32 path is the generic "I/O error." (io.cpp:636
  ThrowFileOperationFailure) rather than net48's
  "Cannot create a file when that file already exists." -- unprobed; the
  TYPE (IOException) is what the case asserts.

## Notes

- net48 oracle evidence: `out/filecopy.exe` exit=0, 19 lines byte-exact (all
  ASCII flags). Harness diff PASS (mingw), 1/1.
- Both runtimes share the harness CWD (repo root) and the case removes every
  fcopy_* temp file at start AND end (idempotent, no repo pollution; verified
  clean after the run).
