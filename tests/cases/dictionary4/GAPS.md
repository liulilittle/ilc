# GAPS.md -- dictionary4 defects (e2e batch C75, 2026-08-13)

Case status: **PASS** (net48 oracle 35 lines, exit 0; dual-compiler PASS via
run-tests.ps1 -Compiler mingw/msvc). Active probes cover the FULL wired
Dictionary<string,int> surface (MethodMap.Dictionary.cs + dictsi_obj.h wrapper):
1200-key shared-prefix collision through multiple 0.72-load-factor resizes,
the capacity matrix up to 1000000, growth from capacity 1 to 1500 entries,
Clear-then-reuse, partial-Remove pressure, four-order insertion convergence,
interleaved Add/Remove/overwrite convergence, value edges, and the exception
faces. Gaps below are RECORDED, NOT FIXED (this batch creates test cases only).

## D-DICT4-1 -- runtime-created keys must be CALLER-ROOTED (documented contract, confirmed)

- **Phenomenon**: the dictsi wrapper does NOT root its keys (dictsi.h GC
  CONTRACT: "the caller must keep every stored key reachable"). A case that
  re-created each key with a fresh runtime string at access time (the
  `K(i)`-built "k0000".."k1199") failed the 1200-key round-trip on the
  transpiled runtime (load_roundtrip=False, then indexer miss
  KeyNotFoundException) while net48 passed -- the collector freed a stored
  key whose only reference was the dict's raw bucket. Rooting the whole key
  set in a string[] local (frame-rooted) fixed it byte-exact.
- **Suspected root file**: `src/runtime/src/dictsi.cpp` (would need a
  finalizer/strong-ref model; this batch does NOT fix -- the case respects
  the documented caller-side contract instead).
- **Case adaptation**: dictionary4 pre-builds and roots every stored key in a
  string[]; miss probes use transient (never-stored) keys.

## D-DICT4-2 -- KeyNotFoundException chain uses the WRONG type name (typed catch misses)

- **Phenomenon**: `il2cpp_throw_key_not_found_exception`
  (exceptionext.cpp:208-212) throws the chain
  `"System.KeyNotFoundException,System.KeyNotFoundException,System.SystemException,System.Exception"`.
  The real .NET full name is **`System.Collections.Generic.KeyNotFoundException`**
  (`System.KeyNotFoundException` is not a valid type). The transpiler emits
  catch clauses from metadata full names, so `catch (KeyNotFoundException)`
  NEVER matches and the exception escapes as UNHANDLED (the C++ rethrow
  guard). This is the same bug class as D-MON-3 (SyncLock chain).
- **Case adaptation**: the indexer-miss probe uses `catch (Exception)`
  instead (matches the runtime chain's System.Exception segment; net48's
  real KeyNotFoundException also derives from Exception) -- the probe still
  asserts that the miss throws, byte-exact on both runtimes. This mirrors
  dictedge's existing empty-get probe.
- **Suspected root file**: `src/runtime/src/exceptionext.cpp:210` (first two
  chain segments should be `System.Collections.Generic.KeyNotFoundException`).
