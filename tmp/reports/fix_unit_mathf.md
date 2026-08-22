# fix_unit_mathf — 4 stale Math/MathF dispatch tests updated to the P5 ValueTuple contract

Date: 2026-08-22
Files changed (ownership scope respected):
- `tests/unit/MethodMapTests.MathF.cs` (class `MethodMapMathFTests`)
- `tests/unit/MethodMapTests.MathExtra.cs` (class `MethodMapTestsMathExtra`)

## Root cause

The P5 ValueTuple-return wiring replaced the pointer-out-param premises the four
tests pinned:

| Test (old) | Old premise | Current code |
|---|---|---|
| `Resolve_MathF_SinCos_ThrowsLoudPointerOutParams` | 3-param FCall shape throws "pointer out-params" | `TryMapMathF` maps 1-param `(float)` → `icall_mathf_sincos_f`, `TupleReturnFields ["d","d"]` (`MethodMap.MathF.cs:106-110`); non-matching shapes return null → generated-name path, **no throw** (`MethodRefResolver.cs:526-532`) |
| `Resolve_MathF_ModF_ThrowsLoudPointerOutParams` | `(float, VT)` shape throws | `TryMapMathF` maps `(float x, out float intPart)` → `icall_mathf_modf_f`, casts `["d","addr:d"]`, result `"d"` (`MethodMap.MathF.cs:114-118`) |
| `Resolve_Math_SinCos_ThrowsLoud` | `SinCos(double)` LOUD-rejected | `TryMapMath` maps 1-param `(double)` → `icall_math_sincos`, casts `["d"]`, `TupleReturnFields ["d","d"]` (`MethodMap.Math.cs:78-82`) |
| `Resolve_Math_ModF_ThrowsLoud` | message contains `icall_math_modf_d` | Still throws (unconditional by-name reject, `MethodMap.MathExtra.cs:103-107`), but the message is now `"Math.ModF not wired: ... ValueTuple ..."`; wrapped by `BclResolve` as `"System.Math::ModF: ..."` (`MethodRefResolver.cs:613`) — no icall name inside |

## Changes

### MethodMapTests.MathF.cs
1. `Resolve_MathF_ModF_ThrowsLoudPointerOutParams` → **`Resolve_MathF_ModF_MapsToModFIcall`**:
   real public shape `float ModF(float, out float)` encoded as
   `Sig(0x0C, 0x0C, 0x10, 0x0C)` (BYREF `0x10` decodes to its element byte,
   DivRem precedent). Asserts resolve SUCCEEDS: CppName
   `il2cpp::runtime::icall_mathf_modf_f`, ReturnsValue, ParamCount 2,
   ParamTypeNames `["d","addr:d"]`, ResultKind `"d"`, TupleReturnFields null.
2. `Resolve_MathF_SinCos_ThrowsLoudPointerOutParams` → **`Resolve_MathF_SinCos_MapsToSincosTupleIcall`**:
   public ValueTuple shape encoded with the GENERICINST return
   `{ 00 01 15 11 05 02 0C 0C 0C }` (marker + VALUETYPE token + arity 2 + two R4
   elements — exactly the shape `RawSignature` documents for SinCos,
   `MethodRefResolver.cs:1199-1210`). Asserts resolve SUCCEEDS: CppName
   `il2cpp::runtime::icall_mathf_sincos_f`, ParamCount 1, TupleReturnFields
   length 2 = `["d","d"]`.
3. Header comment line "ModF/SinCos -> LOUD (pointer out-params)" updated to the
   new wiring.

Legacy 3-param FCall shapes: verified against current code — they no longer
throw (null from `TryMapMathF` falls through to the generated-name path), so the
legacy Throws assertions were NOT kept.

### MethodMapTests.MathExtra.cs
1. `Resolve_Math_SinCos_ThrowsLoud` → **`Resolve_Math_SinCos_MapsToSincosTupleIcall`**:
   GENERICINST-return blob `{ 00 01 15 11 05 02 0D 0D 0D }`. Asserts resolve
   SUCCEEDS: CppName `il2cpp::runtime::icall_math_sincos`, ParamCount 1,
   ParamTypeNames `["d"]`, TupleReturnFields length 2 = `["d","d"]`.
2. `Resolve_Math_ModF_ThrowsLoud`: KEPT as a Throws test (still LOUD per current
   code); message assertions updated to the current text — contains
   `System.Math::ModF` (BclResolve wrapper prefix), `not wired`, `ValueTuple`;
   dropped the stale `icall_math_modf_d` containment.
3. Header comment block updated (SinCos resolves; ModF still LOUD).

## Verification

```
dotnet test E:\il2cpp\tests\unit\unit.csproj -c Release --filter "FullyQualifiedName~MethodMapMathF|FullyQualifiedName~MethodMapMathExtra"
  → Passed!  Failed: 0, Passed: 38, Skipped: 0, Total: 38
dotnet test E:\il2cpp\tests\unit\unit.csproj -c Release --filter "FullyQualifiedName~MethodMapTestsMathExtra"
  → Passed!  Failed: 0, Passed: 18, Skipped: 0, Total: 18
```

Note on the filter: the class FQNs are namespace-less (`MethodMapMathFTests.*`,
`MethodMapTestsMathExtra.*`). The task's filter substring
`MethodMapMathExtra` is not a contiguous substring of `MethodMapTestsMathExtra`,
so a supplementary pass with `~MethodMapTestsMathExtra` was run to cover the
second owned class; both passes are fully green (56 tests total).

## Environment note (outside my ownership)

During this task `src/il2cppc/Emit/CodeGenerator.cs` transiently failed to
compile (CS0103 `managedRetStruct`/`returnType` at :2187, then CS0841/CS0103 at
:668/:2188) — a parallel agent's in-progress edit, not touched by me. The build
went green once that agent landed its fix; all verification above ran after
that. No git operations performed; no files outside the two owned test files
(plus this report) were modified.
