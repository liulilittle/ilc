# GAPS.md -- random2b notes (2026-08-07)

23 probes PASS vs net48 (mingw). The wired Random surface holds: seed ctor,
Next()/Next(max)/Next(min,max)/NextDouble, NextBytes (fill/zero/null), fixed-
seed sequence consistency (seed 42 pinned: 1434747710/302596119/269548474),
distribution range bounds incl. full-int32 Next(Int32.MinValue, Int32.MaxValue),
argument validation (Next(max<0), Next(min>max), NextBytes(null)).

## DEFECT-RAND-1 -- transpiler ldlen_ fallback emits broken C++ (CodeGenerator.cs:3559, regression ed4b688)

`new byte[n].Length` (array Length read NOT consumed as a loop bound) routes
to the `ldlen_` fallback in CodeGenerator.cs:3559, which now emits the literal
text `EmitExpr(c.Args[0], md, types)` -- the `{...}` interpolation braces were
lost in ed4b688 (the old form was `reinterpret_cast<Il2CppArray*>({EmitExpr(...)})`).
Generated C++ fails to compile ("use of undeclared identifier 'c'/'md'/'types'"),
so the probe `zbuf.Length == 0` was REPLACED with a no-throw probe
(`nb_zero_nothrow`) that avoids reading `.Length` while still covering the
zero-length NextBytes no-op path.

VERIFIED REPRODUCIBLE: the same regression breaks the pre-existing `arraydeep`
case (run-tests.ps1 -Case arraydeep -> cmake build fails with the identical
undeclared-identifier errors at array_get_length / array_get_elem_ptr sites).
The working emission (MethodMap.Array.cs get_Length -> il2cpp_array_get_length)
is bypassed whenever the fold lowers Length to the ldlen_ CallExpr IR path.

NOT FIXED (defect recorded only). Fix: restore `{...}` around EmitExpr at
CodeGenerator.cs:3559 (and audit array_get_elem_ptr fallback on the same line).
