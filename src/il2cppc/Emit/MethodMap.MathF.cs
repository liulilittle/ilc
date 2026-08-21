namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.MathF -- System.MathF icall mappings (src/runtime/include/
// internal/il2cpp_mathf.h -- the mathf.cpp corert float-semantics domain).
// Owner: System.MathF
//
// FLOAT TYPE FACE (M-stage model): the transpiler stores float (ELEMENT_TYPE_
// R4, 0x0C) values in DOUBLE slots (MethodBodyReader.cs:187 -- 0x0C/0x0D both
// decode to the "d" slot kind; MethodSignatureCoder.cs:80 maps 0x0C to "f",
// which ParamTypesCpp renders as double). Consequently:
//   * every float ARGUMENT passes through uncast ("" = the double-slot value;
//     the emitter has no "f" narrowing cast -- CodeGenerator read-only) and
//     the icall's `float` param receives it via the implicit C++ conversion;
//   * every float RESULT rides ResultKind "d" (a double temp): the icall's
//     float return widens to double losslessly (the report's minimal patch).
//   This is the documented float-in-double divergence, shared with the
//   double math domain (the value bits are exact -- only the downstream
//   arithmetic width changes).
//
// SURFACE (all icalls VERIFIED in the runtime, il2cpp_mathf.h):
//   single float -> float : Sqrt Abs Sin Cos Tan Asin Acos Atan Exp Log
//                           Log2 Log10 Ceiling Floor Round Truncate Cosh
//                           Sinh Tanh Acosh Asinh Atanh Cbrt -> icall_mathf_<name>_f
//   2x float -> float     : Atan2 Pow -> _f ; Log(x, y) -> log_base_f ;
//                           Max/Min/MaxMagnitude/MinMagnitude -> _f ;
//                           IEEERemainder -> ieee_remainder_f
//   3x float -> float     : FusedMultiplyAdd -> fma_f
//   (float, int) -> float : Round(x, digits) -> round_digits_f ;
//                           ScaleB(x, n) -> scalb_f
//   float -> int32        : Sign -> sign_i ; ILogB -> ilogb_i
//   ModF/SinCos           : icall_mathf_modf_f (out float intPart, float
//                           result) / icall_mathf_sincos_f (out float sin,
//                           out float cos) -- the out pointers are double*
//                           (float-in-double slot model)
//
// PARAM BYTES: float = 0x0C (R4); int32 = 0x08. MathF has NO net48 baseline
// (Core-only) -- no receiver; every member is static.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapMathF(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // ---- single float -> float -------------------------------------
            case "Sqrt": case "Abs": case "Sin": case "Cos": case "Tan":
            case "Asin": case "Acos": case "Atan": case "Exp":
            case "Log2": case "Log10": case "Ceiling": case "Floor":
            case "Truncate": case "Cosh": case "Sinh":
            case "Tanh": case "Acosh": case "Asinh": case "Atanh": case "Cbrt":
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I($"il2cpp::runtime::icall_mathf_{MathFStem(name)}_f", 1, true, false, false, null, "d");
                return null;
            // ---- two floats -> float ----------------------------------------
            case "Atan2": case "Pow": case "IEEERemainder":
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x0C)
                    return I($"il2cpp::runtime::icall_mathf_{MathFStem(name)}_f", 2, true, false, false, null, "d");
                return null;
            case "Log":
                // Log(float x, float y): the base-carrying overload routes to
                // log_base_f (a distinct runtime entry, not the natural log).
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_log_f", 1, true, false, false, null, "d");
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_log_base_f", 2, true, false, false, null, "d");
                return null;
            case "Max": case "Min": case "MaxMagnitude": case "MinMagnitude":
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x0C)
                    return I($"il2cpp::runtime::icall_mathf_{MathFStem(name)}_f", 2, true, false, false, null, "d");
                return null;
            // ---- three floats -> float --------------------------------------
            case "FusedMultiplyAdd":
                if (ps.Length == 3 && ps[0] == 0x0C && ps[1] == 0x0C && ps[2] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_fma_f", 3, true, false, false, null, "d");
                return null;
            // ---- (float, int) -> float --------------------------------------
            case "Round":
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_round_f", 1, true, false, false, null, "d");
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_mathf_round_digits_f", 2, true, false, false,
                        new[] { "", "i" }, "d");
                return null;
            case "ScaleB":
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_mathf_scalb_f", 2, true, false, false,
                        new[] { "", "i" }, "d");
                return null;
            // ---- float -> int32 (default int32 result kind) -----------------
            case "Sign":
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_sign_i", 1, true, false, false);
                return null;
            case "ILogB":
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_ilogb_i", 1, true, false, false);
                return null;
            // P5: MathF.SinCos(float) -> (float, float): netcore public API
            // returns ValueTuple<float,float>. The float-in-double model stores
            // float as double, so TupleReturnFields=["d","d"] (double fields).
            // The runtime icall icall_mathf_sincos_f(float, double*, double*)
            // takes float value (implicit double->float) + double* out-pointers
            // matching the double tuple fields.
            case "SinCos":
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_sincos_f", 1, false, false, false,
                        null, tupleReturnFields: new[] { "d", "d" });
                return null;
            // P5: MathF.ModF(float, out float) returns float + writes intPart
            // via double* out-pointer. NOT a ValueTuple return -- normal call
            // emission with "addr:d" cast for the byref out-param.
            case "ModF":
                if (ps.Length == 2 && ps[0] == 0x0C && ps[1] == 0x0C)
                    return I("il2cpp::runtime::icall_mathf_modf_f", 2, true, false, false,
                        new[] { "d", "addr:d" }, resultKind: "d");
                return null;
            default: return null;
        }
    }

    // MathF member name -> the icall stem suffix (Sqrt -> "sqrt", Atan2 ->
    // "atan2", IEEERemainder -> "ieee_remainder", MaxMagnitude ->
    // "max_magnitude", ... -- matching the il2cpp_mathf.h entry names).
    private static string MathFStem(string name) => name switch
    {
        "IEEERemainder" => "ieee_remainder",
        "MaxMagnitude" => "max_magnitude",
        "MinMagnitude" => "min_magnitude",
        _ => name.ToLowerInvariant(),
    };
}
