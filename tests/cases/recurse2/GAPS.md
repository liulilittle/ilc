# GAPS.md — recurse2 notes (2026-08-06)

All 10 probes PASS vs net48 (mingw, -O0). Two observations recorded here:

## D-REC-1 — the recurse case's cmake_build defect (why `Main()` is no-arg)

The existing `tests/cases/recurse/` FAILs at cmake_build (matrix5): the
generated `entry.cpp` calls the entry method with NO arguments
(`Cases_Recurse_Main_9866ee88af8eb9db();`) while `Main(string[])` is declared
as `Cases_Recurse_Main_<hash>(Il2CppObject* arg0)` -- the transpiler's
entry.cpp generator never passes the arg0 argument. recurse2 avoids the
defect by declaring `Main()` with no parameters, so the generated call and
declaration agree.

- **Suspected root file**: `src/il2cppc/Emit/CodeGenerator.cs` (entry.cpp /
  il2cpp_entry_main emission, ~line 3254) -- the entry caller must pass
  `nullptr` for `Main(string[])`.

## D-REC-2 — stack depth budget on the unoptimized MinGW build

The generated CMakeLists.txt sets NO `CMAKE_BUILD_TYPE`, so the MinGW build
compiles at -O0 (larger C++ frames). Depths are capped conservatively:
IsEven/IsOdd mutual recursion at 6000 (~12k frames peak), NonTailSum/Cascade
at 4000-5000, TailSum(6000) (the transpiler's M6 tail-fold may collapse the
self call to a loop -- the output is identical either way). Raising depths
past these measured-safe values should be re-verified against the -O0 frame
budget (the recursion-audit.md record for the recurse case shows
TailSum(20000) crashes at -O0).

## D-REC-3 — generated-code warning (harmless)

`Cases_Recurse2.cpp:131: warning: variable 't2' set but not used` -- a
temp-slot produced by the ThrowDeep path is assigned but never read (the
exception unwind takes over). Compiles clean under -Wall -Wextra; cosmetic
only.
