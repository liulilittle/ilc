namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.MathExtra -- System.Math wiring leftovers (not covered by MethodMap.Math.cs).
// Owners: System.Math
//
// Dispatch: MethodRefResolver.cs "BCL domain dispatch" -- TryMapMath is tried
// FIRST (`case "System.Math": return TryMapMath(...) ?? TryMapMathExtra(...)`),
// so MathExtra only receives names MethodMap.Math.cs returned null for.
//
// Coverage reconciliation (il2cpp_math.h + il2cpp_math_ext.h icalls vs
// MethodMap.Math.cs): every icall in both headers is already mapped in
// MethodMap.Math.cs EXCEPT icall_math_clamp_i (il2cpp_math.h) -- that mapping
// lives here. Members with no runtime support are LOUD-rejected
// (NotSupportedException) instead of silently falling into the generated-name
// path: ModF/SplitFractionDouble, and the net7 wide BigMul(long, long,
// out long)/BigMul(ulong, ulong, out ulong) overloads (math-sweep 2026-08-22;
// only long BigMul(int, int) has an icall -- mapped in MethodMap.Math.cs).
//
// .NET CORE gMathFuncs additions (Math 超函数 + DivModInt, icall-inventory-
// core.md §8.1): net48 has NO public Acosh/Asinh/Atanh/Cbrt/FusedMultiplyAdd/
// Log2/ModF/SinCos members (member scan verified 2026-08-07), so these
// mappings are DORMANT -- no net48-compiled user IL can reference them, but
// the runtime icalls exist (il2cpp_math_ext.h / mathext.cpp) and the resolver
// maps a future .NET Core-shaped reference to the correct icall instead of
// the generated-name path.
//   * Acosh/Asinh/Atanh/Cbrt/Log2 -- single-double icalls (ResultKind "d").
//   * FusedMultiplyAdd(double,double,double) -- 3-arg double icall.
//   * DivRem(int,int,out int)/DivRem(long,long,out long) -- net48 PUBLIC API
//     (reachable!): the corert Math.DivModInt family as a result-first icall.
//     The M7 ref/out wiring ("addr:i"/"addr:l" casts, CodeGenerator.cs) passes
//     the ldloca REMAINDER slot as a typed pointer; the QUOTIENT rides the
//     value channel. This replaces the old blanket DivRem LOUD-reject.
//   * SinCos -- the netcore public shape returns a (double,double) ValueTuple
//     and the FCall takes two double* outs; the transpiler has no tuple-return
//     or dual-out-slot emission (ref/out milestone) -- LOUD.
//   * ModF -- private in net48 (SplitFractionDouble) and netcore; pointer-
//     typed arg, no transpiler emission -- LOUD.
// Unknown names return null.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapMathExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Clamp":
                return I("il2cpp::runtime::icall_math_clamp_i", 3, true, false, false);
            // --- .NET Core gMathFuncs: the 6 single-double super-functions. ---
            // Net48 lacks these members (dormant mapping), semantics = the
            // .NET Core libm contract (netcore oracle bit-verified).
            case "Acosh":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_acosh_d", 1, true, false, false, null, "d");
                return null;
            case "Asinh":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_asinh_d", 1, true, false, false, null, "d");
                return null;
            case "Atanh":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_atanh_d", 1, true, false, false, null, "d");
                return null;
            case "Cbrt":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_cbrt_d", 1, true, false, false, null, "d");
                return null;
            case "Log2":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_log2_d", 1, true, false, false, null, "d");
                return null;
            case "FusedMultiplyAdd":
                if (ps.Length == 3 && ps[0] == 0x0D && ps[1] == 0x0D && ps[2] == 0x0D)
                    return I("il2cpp::runtime::icall_math_fma_d", 3, true, false, false, null, "d");
                return null;
            // net9/10 E2E campaign (math sweep): the DOUBLE IEEE-754-2019
            // family (mathext.cpp mirrors of the mathf float impls).
            case "MaxMagnitude":
                return ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x0D
                    ? I("il2cpp::runtime::icall_math_max_magnitude_d", 2, true, false, false, null, "d")
                    : null;
            case "MinMagnitude":
                return ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x0D
                    ? I("il2cpp::runtime::icall_math_min_magnitude_d", 2, true, false, false, null, "d")
                    : null;
            case "ILogB":
                return ps.Length == 1 && ps[0] == 0x0D
                    ? I("il2cpp::runtime::icall_math_ilogb_d", 1, true, false, false, new[] { "d" })
                    : null;
            case "ScaleB":
                return ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x08
                    ? I("il2cpp::runtime::icall_math_scaleb_d", 2, true, false, false, new[] { "d", "i" }, "d")
                    : null;
            case "CopySign":
                return ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x0D
                    ? I("il2cpp::runtime::icall_math_copy_sign_d", 2, true, false, false, null, "d")
                    : null;
            // net9/10 E2E campaign: BitDecrement/BitIncrement -- nextafter
            // toward -inf / +inf (mathext.cpp).
            case "BitDecrement":
                return ps.Length == 1 && ps[0] == 0x0D
                    ? I("il2cpp::runtime::icall_math_bit_decrement_d", 1, true, false, false, null, "d")
                    : null;
            case "BitIncrement":
                return ps.Length == 1 && ps[0] == 0x0D
                    ? I("il2cpp::runtime::icall_math_bit_increment_d", 1, true, false, false, null, "d")
                    : null;
            // net9/10 E2E campaign: ReciprocalEstimate/ReciprocalSqrtEstimate
            // -- precisely-corrected fallback (spec-permitted).
            case "ReciprocalEstimate":
                return ps.Length == 1 && ps[0] == 0x0D
                    ? I("il2cpp::runtime::icall_math_reciprocal_estimate_d", 1, true, false, false, null, "d")
                    : null;
            case "ReciprocalSqrtEstimate":
                return ps.Length == 1 && ps[0] == 0x0D
                    ? I("il2cpp::runtime::icall_math_reciprocal_sqrt_estimate_d", 1, true, false, false, null, "d")
                    : null;
            // --- DivRem: net48 PUBLIC (reachable) -- the corert Math.DivModInt
            // family exposed result-first. The BYREF out param decodes to its
            // element byte (ReadParamByte, 0x10 recursion), so both forms match
            // on ps == [w,w,w]; the "addr:*" cast (M7) passes the ldloca
            // remainder slot as a typed pointer and the quotient is returned.
            case "DivRem":
                if (ps.Length == 3 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_math_divrem_i", 3, true, false, false, new[] { "i", "i", "addr:i" });
                if (ps.Length == 3 && ps[0] == 0x0A && ps[1] == 0x0A && ps[2] == 0x0A)
                    return I("il2cpp::runtime::icall_math_divrem_l", 3, true, false, false, new[] { "l", "l", "addr:l" }, "l");
                return null;
            // --- SinCos: netcore public returns a (double Sin, double Cos)
            // ValueTuple; the runtime icall (icall_math_sincos, result-first
            // double* outs) exists but the transpiler has no tuple-return /
            // dual-out-slot emission -- LOUD instead of the generated-name path.
            case "SinCos":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    throw new NotSupportedException(
                        $"Math.{name} not wired: the netcore public API returns a (double Sin, double Cos) ValueTuple and the FCall needs two double* out slots " +
                        "(icall_math_sincos exists result-first in il2cpp_math_ext.h) -- the M-stage transpiler has no tuple-return or dual-out-slot emission (ref/out milestone item); use Math.Sin + Math.Cos");
                return null;
            // --- ModF: netcore public `double ModF(double value, out double
            // intPart)` (private SplitFractionDouble in net48). The runtime icall
            // icall_math_modf_d(double, double*) exists and is unit-tested, but
            // the public API returns a ValueTuple / uses an out-param the
            // transpiler cannot yet emit (ref/out milestone) -- LOUD.
            case "ModF":
            case "SplitFractionDouble":
                throw new NotSupportedException(
                    $"Math.{name} not wired: the netcore public API returns a ValueTuple (double frac, out double intPart) -- " +
                    "the transpiler has no tuple-return / out-param emission (ref/out milestone item); the runtime icall exists and is unit-tested");
            case "BigMul":
                // long BigMul(int, int) -- batch 150: the exact 64-bit product
                // (int32 widening; the "l" result rides the int64 slot).
                if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_math_bigmul", 2, true, false, false, null, "l");
                // math-sweep (2026-08-22): the net7 wide forms BigMul(long,
                // long, out long) / BigMul(ulong, ulong, out ulong) have NO
                // runtime icall (only the int32-pair icall_math_bigmul exists,
                // il2cpp_math.h). The old unconditional mapping captured ANY
                // arity as a 2-arg bigmul call -- a 3-arg IL pop emitted as a
                // 2-arg call drops the out-param slot (silent stack imbalance).
                throw new NotSupportedException(
                    $"Math.{name} overload (arity {ps.Length}) not wired: only long BigMul(int, int) has a runtime icall " +
                    "(icall_math_bigmul, il2cpp_math.h) -- the net7 wide forms BigMul(long, long, out long)/BigMul(ulong, ulong, out ulong) need a new icall");
            default:
                return null;
        }
    }
}
