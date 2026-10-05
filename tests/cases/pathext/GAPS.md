# GAPS.md — pathext notes (2026-08-06)

All 17 probes PASS vs net48 (mingw). One unwired surface recorded:

## D-PATH-1 — Path.Combine params form (3+ args) is LOUD

- `Path.Combine(a, b, c)` (and the params `Path.Combine(params string[])`)
  aborts the transpile with a LOUD NotSupportedException
  (MethodMap.IO.cs Combine case): "the runtime icall
  il2cpp::runtime::icall_path_combine takes exactly (string, string) --
  nested folding (combine(combine(a,b),c)) is the planned fix, codegen
  support not landed yet (work order #24)". The case therefore nests 2-arg
  Combines (`Path.Combine(Path.Combine("x", "y"), "z")`), which IS wired and
  passes.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.IO.cs` (Combine) +
  `src/runtime/src/pathproc.cpp` (multi-arg combine icall or codegen folding).
