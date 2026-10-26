namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Math -- System.Math icall mappings (il2cpp_math.h).
// Owners: System.Math
//
// Template for all MethodMap files: one TryMap method per file, uniform
// signature, registered by ONE case line in the "BCL domain dispatch"
// #region in MethodRefResolver.cs. See that file's header for the full
// convention (wiring-agent steps).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapMath(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "Abs":
                // LOUD P0-19: Math.Abs(decimal) -- decimal is a 16-byte value
                // (raw Il2CppDecimal* slots, "dec" result). The old default
                // double fallback cast the decimal slot to double (C2664).
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_abs", 1, true, false, false, new[] { "dec" }, "dec");
                // mathabs1: the sbyte/short overloads sign-extend to int32 and
                // return the int32 magnitude (the call-site consumer narrows);
                // their minimum values throw OverflowException. The old
                // default routed them to the double icall, which reinterpreted
                // the int32 slot as a double (Abs((sbyte)-127) == 0).
                return ps.Length == 1 && ps[0] == 0x04 ? I("il2cpp::runtime::icall_math_abs_sb", 1, true, false, false)
                    : ps[0] == 0x06 ? I("il2cpp::runtime::icall_math_abs_sh", 1, true, false, false)
                    : ps[0] == 0x08 ? I("il2cpp::runtime::icall_math_abs_i", 1, true, false, false)
                    : ps[0] == 0x0A ? I("il2cpp::runtime::icall_math_abs_l", 1, true, false, false, null, "l")
                    : I("il2cpp::runtime::icall_math_abs_d", 1, true, false, false, null, "d");
            case "Max":
            case "Min":
            {
                var stem = name == "Max" ? "max" : "min";
                if (ps.Length == 2 && IsMathDecimalParam(ps, 0, arg0Type) && IsMathDecimalParam(ps, 1, arg1Type))
                    return I($"il2cpp::runtime::icall_decimal_{stem}", 2, true, false, false,
                        new[] { "dec", "dec" }, "dec");
                // mathminmax1: every integer overload (sbyte/byte/short/
                // ushort/int/long/uint/ulong) used to fall to the double icall,
                // which reinterpreted the int32/int64 slot (Max(byte) == 0).
                // Signed and small unsigned types compare in the int32/int64
                // carriers; uint/ulong need the UNSIGNED compare icalls.
                // Float/double keep the double icall (float-in-double model).
                return ps[0] switch
                {
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08
                        => I($"il2cpp::runtime::icall_math_{stem}_i", 2, true, false, false),
                    0x09 => I($"il2cpp::runtime::icall_math_{stem}_u", 2, true, false, false),
                    0x0A => I($"il2cpp::runtime::icall_math_{stem}_l", 2, true, false, false, null, "l"),
                    0x0B => I($"il2cpp::runtime::icall_math_{stem}_ul", 2, true, false, false, null, "l"),
                    _ => I($"il2cpp::runtime::icall_math_{stem}_d", 2, true, false, false, null, "d"),
                };
            }
            case "Sign":
                // Math.Sign(decimal) -> int32 sign icall (the decimal is an
                // address arg; the result rides the plain int32 slot).
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_sign", 1, true, false, false, new[] { "dec" });
                // sbyte/short sign-extend into the int32 slot; int32 is the
                // native form. (The old route sent the small types through
                // sign_l, which happened to work but widened the carrier.)
                return (ps[0] == 0x04 || ps[0] == 0x05 || ps[0] == 0x06 || ps[0] == 0x07 || ps[0] == 0x08)
                    ? I("il2cpp::runtime::icall_math_sign_i", 1, true, false, false)
                    // net9/10 E2E campaign (math sweep): R4/R8 must test the
                    // FRACTION's sign -- sign_l truncated first (Sign(-0.5)
                    // wrongly 0). icall_math_sign_d takes the double slot
                    // (float-in-double model) and returns int32; net48 throws
                    // ArithmeticException for NaN (mathminmax1).
                    : (ps[0] == 0x0D || ps[0] == 0x0C)
                    ? I("il2cpp::runtime::icall_math_sign_d", 1, true, false, false, new[] { "d" })
                    : I("il2cpp::runtime::icall_math_sign_l", 1, true, false, false, null, "l");
            case "Sqrt": return I("il2cpp::runtime::icall_math_sqrt_d", 1, true, false, false, null, "d");
            case "Pow": return I("il2cpp::runtime::icall_math_pow_d", 2, true, false, false, null, "d");
            case "Floor":
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_floor", 1, true, false, false, new[] { "dec" }, "dec");
                return I("il2cpp::runtime::icall_math_floor_d", 1, true, false, false, null, "d");
            case "Ceiling":
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_ceiling", 1, true, false, false, new[] { "dec" }, "dec");
                return I("il2cpp::runtime::icall_math_ceiling_d", 1, true, false, false, null, "d");
            case "Round":
                // Math.Round(decimal) / Math.Round(decimal, int digits): the
                // decimal.Round ToEven path (mode 0) with the missing
                // decimals/mode slots appended as literal 0s -- the same
                // shape MethodMap.Decimal.cs uses for Decimal.Round.
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_round", 1, true, false, false, new[] { "dec" }, "dec",
                        appendLiterals: new[] { "0", "0" });
                if (ps.Length == 2 && IsMathDecimalParam(ps, 0, arg0Type) && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_decimal_round", 2, true, false, false, new[] { "dec", "i" }, "dec",
                        appendLiterals: new[] { "0" });
                // Math.Round(decimal, MidpointRounding): the enum is a value
                // type (0x11) riding the int32 slot; decimals is fixed at 0,
                // so the dedicated 3-arg icall carries (result, d, mode).
                if (ps.Length == 2 && IsMathDecimalParam(ps, 0, arg0Type) && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_round_mode", 2, true, false, false,
                        new[] { "dec", "i" }, "dec");
                // Math.Round(decimal, int, MidpointRounding): the existing
                // 4-arg icall already matches (result, d, decimals, mode).
                if (ps.Length == 3 && IsMathDecimalParam(ps, 0, arg0Type) && ps[1] == 0x08 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_round", 3, true, false, false,
                        new[] { "dec", "i", "i" }, "dec");
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_math_round_d", 1, true, false, false, null, "d");
                // Round(double, int digits): the 2-param overload MUST resolve
                // to the 2-param icall -- the old single-arity mapping dropped
                // the first argument (math2 generated `round_d(2LL)` and
                // printed 2 instead of 3.14/2.68). net48 digits range
                // [0, 15], validated inside the runtime icall.
                if (ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_math_round_digits_d", 2, true, false, false, null, "d");
                if (ps.Length == 2 && ps[0] == 0x0D && ps[1] == 0x11)
                    // Round(double, MidpointRounding) -- batch 149: the mode
                    // select (0 ToEven / 1 AwayFromZero); the enum (a
                    // VALUETYPE 0x11) arrives as its int32 value slot ("i").
                    return I("il2cpp::runtime::icall_math_round_mode_d", 2, true, false, false,
                        new[] { "d", "i" }, "d");
                if (ps.Length == 3 && ps[0] == 0x0D && ps[1] == 0x08 && ps[2] == 0x11)
                    // Round(double, int, MidpointRounding) -- batch 149: the
                    // digit-scaled mode select.
                    return I("il2cpp::runtime::icall_math_round_digits_mode_d", 3, true, false, false,
                        new[] { "d", "i", "i" }, "d");
                throw new NotSupportedException(
                    $"Math.Round overload (arity {ps.Length}) not wired: only Round(double), Round(double, int), Round(double, MidpointRounding) and Round(double, int, MidpointRounding) have runtime icalls");
            case "Truncate":
                if (ps.Length == 1 && IsMathDecimalParam(ps, 0, arg0Type))
                    return I("il2cpp::runtime::icall_decimal_truncate", 1, true, false, false, new[] { "dec" }, "dec");
                return I("il2cpp::runtime::icall_math_truncate_d", 1, true, false, false, null, "d");
            case "Sin": return I("il2cpp::runtime::icall_math_sin_d", 1, true, false, false, null, "d");
            case "Cos": return I("il2cpp::runtime::icall_math_cos_d", 1, true, false, false, null, "d");
            case "Tan": return I("il2cpp::runtime::icall_math_tan_d", 1, true, false, false, null, "d");
            case "Asin": return I("il2cpp::runtime::icall_math_asin_d", 1, true, false, false, null, "d");
            case "Acos": return I("il2cpp::runtime::icall_math_acos_d", 1, true, false, false, null, "d");
            case "Atan": return I("il2cpp::runtime::icall_math_atan_d", 1, true, false, false, null, "d");
            case "Atan2": return I("il2cpp::runtime::icall_math_atan2_d", 2, true, false, false, null, "d");
            // P5: Math.SinCos(double) -> (double, double): the netcore public
            // API returns a ValueTuple<double,double> (1-param call). The
            // runtime icall icall_math_sincos(double, double*, double*) takes
            // the value + 2 out-pointer args. TupleReturnFields signals the
            // CodeGenerator AssignStmt handler to emit fn(arg, &t.Item1, &t.Item2).
            case "SinCos":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_math_sincos", 1, false, false, false,
                        new[] { "d" }, tupleReturnFields: new[] { "d", "d" });
                return null;
            case "Exp": return I("il2cpp::runtime::icall_math_exp_d", 1, true, false, false, null, "d");
            case "Log": return ps.Length == 1
                ? I("il2cpp::runtime::icall_math_log_d", 1, true, false, false, null, "d")
                : I("il2cpp::runtime::icall_math_log_base_d", 2, true, false, false, null, "d");
            case "Log10": return I("il2cpp::runtime::icall_math_log10_d", 1, true, false, false, null, "d");
            case "Cosh": return I("il2cpp::runtime::icall_math_cosh_d", 1, true, false, false, null, "d");
            case "Sinh": return I("il2cpp::runtime::icall_math_sinh_d", 1, true, false, false, null, "d");
            case "Tanh": return I("il2cpp::runtime::icall_math_tanh_d", 1, true, false, false, null, "d");
            case "IEEERemainder": return I("il2cpp::runtime::icall_math_ieee_remainder_d", 2, true, false, false, null, "d");
            // D-ME2-1 (2026-08-12): Math.BigMul(int, int) -> long -- the net48
            // wide-product icall (icall_math_bigmul, il2cpp_math.h). Before
            // this mapping TryMapMathExtra LOUD-rejected BigMul, so no
            // transpiled case could call it. ps == [0x08, 0x08] (two int32),
            // result kind "l" (int64).
            case "BigMul":
                // net48 exposes ONLY long BigMul(int, int) (verified against
                // the net48 reference assembly -- the unsigned
                // BigMul(uint, uint) is a .NET Core addition, unreachable from
                // net48 IL). A non-int32 pair is not the net48 overload ->
                // the MathExtra gate keeps it LOUD.
                if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_math_bigmul", 2, true, false, false, new[] { "i", "i" }, "l");
                return null;
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // LOUD P0-19: is the parameter at 'index' the DECIMAL overload?
    //
    // The managed type hint (arg0Type/arg1Type) is the dispatch key: a
    // decimal local/temp tracks as "System.Decimal", and the dispatcher also
    // fills it from the DECODED signature ("V:System.Decimal") when a decimal
    // arrives as an untracked ldarg parameter. 0x11 ALONE is never enough --
    // DateTime/TimeSpan/user structs/enums share the VALUETYPE byte -- so the
    // 0x11 byte is only a necessary gate and any non-decimal hint vetoes the
    // decimal arm.
    //--------------------------------------------------------------------------
    private static bool IsMathDecimalParam(byte[] ps, int index, string? hint)
        => index < ps.Length && ps[index] == 0x11 && hint == "System.Decimal";

    //--------------------------------------------------------------------------
    // MethodMap.Double -- System.Double STATIC predicate domain
    // (D-ME2-2, 2026-08-12). Before this domain double.IsNaN /
    // IsPositiveInfinity / IsNegativeInfinity / IsInfinity resolved to the
    // generated-name path and failed the C++ build as undeclared identifiers
    // (no MethodMap.Double existed -- the System.Double owner fell through
    // TryMapFormat/TryMapValueType to the mangled name). Each maps to an
    // IEEE-754 bit-check icall (il2cpp_math_ext.h) returning bool in the
    // int32 ABI slot (0/1), exactly like icall_char_is_digit.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapDouble(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (ps.Length != 1 || ps[0] != 0x0D) return null;
        return name switch
        {
            "IsNaN" => I("il2cpp::runtime::icall_double_is_nan", 1, true, false, false, new[] { "d" }),
            "IsPositiveInfinity" => I("il2cpp::runtime::icall_double_is_positive_infinity", 1, true, false, false, new[] { "d" }),
            "IsNegativeInfinity" => I("il2cpp::runtime::icall_double_is_negative_infinity", 1, true, false, false, new[] { "d" }),
            "IsInfinity" => I("il2cpp::runtime::icall_double_is_infinity", 1, true, false, false, new[] { "d" }),
            _ => null,
        };
    }
}
