# Math/MathF MethodMap Sweep Report

- Date: 2026-08-22
- Agent scope: `src/il2cppc/Emit/MethodMap.Math.cs`, `MethodMap.MathExtra.cs`, `MethodMap.MathF.cs` (edit-owned); headers read-only.
- Method: enumerated the net48 + net9 System.Math / System.MathF public static surface, grepped
  `src/runtime/include/internal/il2cpp_math.h`, `il2cpp_math_ext.h`, `il2cpp_mathf.h`, `il2cpp_math_c99.h`
  for an existing icall per candidate, cross-checked against the three MethodMap files.
- Dispatch wiring verified: `MethodRefResolver.cs:634-635` (`System.Math` -> TryMapMath ?? TryMapMathExtra),
  `MethodRefResolver.cs:1073` (`System.MathF` -> TryMapMathF).

## Result summary

Every icall that exists for a System.Math / System.MathF public member is ALREADY mapped.
Exactly one defect found and fixed (BigMul wide-form mis-map, below). All remaining gaps lack a
runtime icall -> recorded in needs-new-icall.

## Sweep table (named candidates)

| Candidate | icall exists? | Mapped? | Action |
|---|---|---|---|
| Math.BigMul(int,int)->long | `icall_math_bigmul` (math.h:59) | Math.cs:97-100 | none |
| Math.BigMul(long,long,out long) / (ulong,ulong,out ulong) [net7] | NO (only int32-pair bigmul) | was UNCONDITIONALLY mis-mapped in Extra.cs | **FIXED**: arity guard + LOUD reject |
| Math.DivRem(int,int,out int) | `icall_math_divrem_i` (math.h:105) | Extra.cs:82-84 | none |
| Math.DivRem(long,long,out long) | `icall_math_divrem_l` (math.h:106) | Extra.cs:85-87 | none |
| Math.Log(double,double) | `icall_math_log_base_d` (ext.h:88) | Math.cs:84-86 | none |
| Math.MaxMagnitude/MinMagnitude(double,double) | NO (float only, mathf.h:171-172) | - | needs-new-icall |
| Math.ILogB(double) | NO (float only, mathf.h:183) | - | needs-new-icall |
| Math.CopySign(double,double) | NO (grep: no copysign in any header) | - | needs-new-icall |
| Math.FusedMultiplyAdd(d,d,d) | `icall_math_fma_d` (ext.h:147) | Extra.cs:73-76 | none |
| Math.ScaleB(double,int) | NO (float only, mathf.h:187) | - | needs-new-icall |
| Math.ReciprocalEstimate / ReciprocalSqrtEstimate(double) | NO | - | needs-new-icall |
| MathF single-float set (Sqrt..Cbrt, 21 members) | mathf.h:41-152 | MathF.cs:48-55 | none |
| MathF Atan2/Pow/IEEERemainder | mathf.h | MathF.cs:57-60 | none |
| MathF.Log(float) / Log(float,float) | log_f / log_base_f (mathf.h:71,92) | MathF.cs:61-68 | none |
| MathF Max/Min/MaxMagnitude/MinMagnitude | mathf.h:163-172 | MathF.cs:69-72 | none |
| MathF.FusedMultiplyAdd | fma_f (mathf.h:152) | MathF.cs:74-77 | none |
| MathF.Round(x) / Round(x,digits) | round_f / round_digits_f | MathF.cs:79-85 | none |
| MathF.ScaleB | scalb_f (mathf.h:187) | MathF.cs:86-90 | none |
| MathF.Sign / ILogB | sign_i / ilogb_i (mathf.h:177,183) | MathF.cs:92-99 | none |
| MathF.SinCos / ModF | sincos_f / modf_f (mathf.h:218,206) | MathF.cs:106-118 | none |

Reverse check (header icalls without a mapping): NONE outstanding for public API.
`icall_math_div_/mod_ i/u/l/ul` (math.h:115-122) are the DivModInt IL-opcode primitives
(defined runtime-side in math.cpp:315-357; not referenced from src/il2cppc) -- not System.Math
API surface, intentionally unmapped here. `icall_math_fmod_d` lowers BinaryOp.Rem (D-DBL-1).
`icall_double_is_*` are mapped in MethodMap.Double (Math.cs:115-127).

## Edit made (1 file)

`MethodMap.MathExtra.cs` -- BigMul case:

- Before: unconditional `return I("...icall_math_bigmul", 2, ...)` for ANY ps shape. A net7
  caller of `BigMul(long,long,out long)` (ps=[0x0A,0x0A,0x0A]) would emit a 2-arg call while the
  IL stack pops 3 slots -> silent stack imbalance / dropped out-param.
- After: `[0x08,0x08]` guard keeps the batch-150 int-form mapping (unreachable via Extra --
  Math.cs handles it first -- kept for file self-containment); every other arity now throws
  NotSupportedException naming the missing wide-form icalls (file's established LOUD convention,
  cf. ModF/SplitFractionDouble). Header coverage-reconciliation comment updated accordingly.
- Backup: `tmp/backups/MethodMap.MathExtra.cs.20260822-034820`.
- Build verification: NOT run (task constraint: no builds). Edit re-read and structurally
  verified (balanced braces; throw-in-case pattern identical to existing cases).

## Needs-new-icall list

Runtime icalls to add before these public members can be mapped (none has any mapping path today;
unmapped names fall through to the generated-name path and fail the C++ build as undeclared):

1. `Math.BigMul(long,long,out long)` / `BigMul(ulong,ulong,out ulong)` (.NET 7) -- wide exact product.
2. `Math.DivRem(int,int)` / `(long,long)` tuple-return forms (.NET 7) -- NOTE: divrem_i/l exist but
   return the quotient via the return channel with ONE pointer out; the P5 tuple emission
   (CodeGenerator.cs:2098) discards the callee return and appends one pointer PER tuple field, so a
   mapping would emit a 4-arg call to a 3-arg function. Needs either a void
   `(T a, T b, T* quo, T* rem)` icall or a hybrid tuple emission mode.
3. `Math.MaxMagnitude(double,double)` / `MinMagnitude(double,double)` (.NET Core 3.0) -- float
   analogues exist (`max/min_magnitude_f`); double versions absent.
4. `Math.ILogB(double)` (.NET Core 3.0) -- float analogue `icall_mathf_ilogb_i` exists.
5. `Math.CopySign(double,double)` AND `MathF.CopySign(float,float)` (.NET Core 3.0) -- nothing.
6. `Math.ScaleB(double,int)` (.NET Core 3.0) -- float analogue `scalb_f` exists.
7. `Math.ReciprocalEstimate(double)` / `ReciprocalSqrtEstimate(double)` + MathF float forms (.NET Core 3.0).
8. `Math/MathF.BitDecrement` / `BitIncrement` (.NET 6).
9. `Math/MathF.MaxNumber` / `MinNumber` / `MaxMagnitudeNumber` / `MinMagnitudeNumber` (.NET 7).
10. `Math.Clamp` overloads beyond int32 (byte/sbyte/short/ushort/uint/long/ulong/float/double/decimal/nint/nuint)
    -- only `icall_math_clamp_i` exists (mapped, Extra.cs:48-49).
11. `Math.Sign(double/float/sbyte/short/decimal)` -- only `_i`(int32)/`_l`(int64) exist.

## Observations (out of edit scope, recorded for owners)

- **Latent Sign mis-route (pre-existing)**: MethodMap.Math.cs:34-37 sends every non-int32 ps to
  `sign_l`; `Math.Sign(double)` (ps=0x0D) / `Sign(float)` (ps=0x0C) / `Sign(short)` (0x06) /
  `Sign(sbyte)` (0x02) therefore emit `icall_math_sign_l((int64_t)x)` -- truncation toward zero
  first, so e.g. Sign(-0.5) yields 0 instead of -1, NaN does not throw ArithmeticException, and a
  double slot value converts to int64_t. Fix requires new `sign_d`/`sign_f` icalls (item 11 above);
  not touched here (outside sweep candidates, would need runtime work).
- **Dead code (harmless, untouched)**: Extra.cs SinCos LOUD-reject (lines 92-97) is unreachable --
  TryMapMath maps SinCos first via TupleReturnFields (Math.cs:78-82, P5); its header bullet
  ("transpiler has no tuple-return emission", lines 34-36) predates P5. Same for Extra's guarded
  BigMul int-form branch (kept deliberately).
- `il2cpp_math_c99.h` is a libstdc++ shim only -- declares no icalls; not part of the surface.
