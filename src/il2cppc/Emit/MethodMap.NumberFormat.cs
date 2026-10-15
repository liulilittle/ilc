namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.NumberFormat -- System.Globalization.NumberFormatInfo / TextInfo
// wiring. The runtime surface is the invariant/zh-CN two-culture model
// (numberformat.h): the culture-name string VALUE ("zh-CN" / "") is the
// receiver; string/int getters dispatch on it at run time.
//
// Owners: System.Globalization.NumberFormatInfo / TextInfo
//
// WIRING STATUS (updated probe batch 3, gn20-numfmt -- P0-4):
//   * Numeric ToString(format) stays on the zh-CN format_ext domain
//     (MethodMap.Format.cs / formatext.cpp) -- NOT this mapper (see the
//     unchanged ownership rationale below).
//   * get_CurrentInfo/get_InvariantInfo map to the runtime culture-name
//     icalls (il2cpp_culture_get_current/invariant) -- the receiver IS the
//     culture-name string the instance getters dispatch on.
//   * NumberFormatInfo instance getters are the invariant/zh-CN two-culture
//     dispatchers:
//       - strings via Literal where zh-CN == invariant (NumberDecimalSeparator
//         ".", NumberGroupSeparator ",", PercentSymbol "%", Percent*Separator
//         ".",",","+","-","NaN");
//       - the culture-VARYING ones via runtime icall (CurrencySymbol U+00A5
//         vs U+00A4, Positive/NegativeInfinitySymbol U+221E/-U+221E vs
//         "Infinity"/"-Infinity");
//       - gn20-numfmt P0-4 (the audit finding "getters hard-coded invariant
//         literals"): the previously-unmapped getters now ALL map to runtime
//         icalls -- CurrencyDecimalSeparator/CurrencyGroupSeparator/
//         PerMilleSymbol strings; NumberNegativePattern/NumberDecimalDigits/
//         CurrencyDecimalDigits/CurrencyPositivePattern/CurrencyNegative
//         Pattern/PercentPositivePattern/PercentNegativePattern/
//         PercentDecimalDigits ints (the three pattern ints differ between
//         zh-CN and invariant -- 2/1/1 vs 0/0/0); Number/Currency/Percent
//         GroupSizes int[] {3}; NativeDigits string[] {"0".."9"}.
//   * TextInfo.ToLower/ToUpper drop the TextInfo receiver and reuse the
//     string case icalls (icall_string_to_lower/upper) -- invariant-culture
//     TextInfo case mapping equals the string case helpers.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapNumberFormat(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Globalization.NumberFormatInfo":
                switch (name)
                {
                    // Static culture factories follow the CultureInfo
                    // handle-gap model: the value IS the culture NAME string
                    // ("zh-CN" / "" -- MethodMap.CultureInfo.cs:21-38). The
                    // instance getters below dispatch on it at run time.
                    case "get_CurrentInfo":
                        return I("il2cpp::runtime::il2cpp_culture_get_current", 0, true, true, false,
                            returnTypeName: "System.String");
                    case "get_InvariantInfo":
                        return I("il2cpp::runtime::il2cpp_culture_get_invariant", 0, true, true, false,
                            returnTypeName: "System.String");
                    // Instance getters fold to the invariant-culture literal
                    // strings (numberformat.h baseline); the receiver is
                    // discarded by the fold.
                    case "get_NumberDecimalSeparator": return Literal(".");
                    case "get_NumberGroupSeparator": return Literal(",");
                    case "get_PercentSymbol": return Literal("%");
                    // saudit1/4: the culture-name receiver selects the symbol
                    // ("" -> U+00A4 invariant, else U+00A5 zh-CN).
                    case "get_CurrencySymbol":
                        // culturefmt fix (2026-09-13): VALUE receivers (a static
                        // get_CurrentInfo call result -- the culture NAME string)
                        // must pass through as the helper argument; the
                        // rr=true/inst=true shape only built the arg for
                        // ldloca-shaped receivers, so `CurrentInfo.CurrencySymbol`
                        // desynced StackFold (culturefmt IL_0F16 stack 0). Same
                        // value shape as the BigInteger helpers (1,true,false,false).
                        return I("il2cpp::runtime::il2cpp_number_format_currency_symbol", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    // gn20-numfmt (probe batch 3): the remaining instance getter
                    // surface -- same culture-name receiver model as the currency/
                    // infinity symbols above. Most values are culture-INdependent
                    // on the invariant/zh-CN surface (curl/inv agree); the three
                    // patterns that differ (currency negative 2/0, percent
                    // positive 1/0, percent negative 1/0) dispatch at run time.
                    case "get_CurrencyDecimalSeparator":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_decimal_separator", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    case "get_CurrencyGroupSeparator":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_group_separator", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    case "get_PerMilleSymbol":
                        return I("il2cpp::runtime::il2cpp_number_format_per_mille_symbol", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    case "get_NumberNegativePattern":
                        return I("il2cpp::runtime::il2cpp_number_format_number_negative_pattern", 0, true, false, true,
                            new[] { "" });
                    case "get_NumberDecimalDigits":
                        return I("il2cpp::runtime::il2cpp_number_format_number_decimal_digits", 0, true, false, true,
                            new[] { "" });
                    case "get_CurrencyDecimalDigits":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_decimal_digits", 0, true, false, true,
                            new[] { "" });
                    case "get_CurrencyPositivePattern":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_positive_pattern", 0, true, false, true,
                            new[] { "" });
                    case "get_CurrencyNegativePattern":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_negative_pattern", 0, true, false, true,
                            new[] { "" });
                    case "get_PercentPositivePattern":
                        return I("il2cpp::runtime::il2cpp_number_format_percent_positive_pattern", 0, true, false, true,
                            new[] { "" });
                    case "get_PercentNegativePattern":
                        return I("il2cpp::runtime::il2cpp_number_format_percent_negative_pattern", 0, true, false, true,
                            new[] { "" });
                    case "get_PercentDecimalDigits":
                        return I("il2cpp::runtime::il2cpp_number_format_percent_decimal_digits", 0, true, false, true,
                            new[] { "" });
                    case "get_NumberGroupSizes":
                        return I("il2cpp::runtime::il2cpp_number_format_number_group_sizes", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.Int32[]");
                    case "get_CurrencyGroupSizes":
                        return I("il2cpp::runtime::il2cpp_number_format_currency_group_sizes", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.Int32[]");
                    case "get_PercentGroupSizes":
                        return I("il2cpp::runtime::il2cpp_number_format_percent_group_sizes", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.Int32[]");
                    case "get_NativeDigits":
                        return I("il2cpp::runtime::il2cpp_number_format_native_digits", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String[]");
                    case "get_PercentDecimalSeparator": return Literal(".");
                    case "get_PercentGroupSeparator": return Literal(",");
                    case "get_PositiveSign": return Literal("+");
                    case "get_NegativeSign": return Literal("-");
                    // The invariant special spellings (numberformat.h:7-8) --
                    // the only numberformat-domain value-formatting facts that
                    // are reachable here (numeric ToString(format) owners stay
                    // on the zh-CN format_ext domain, see the status header).
                    case "get_NaNSymbol": return Literal("NaN");
                    // saudit1: same culture-name dispatch as the currency
                    // symbol ("" -> "Infinity"/"-Infinity"; else U+221E).
                    case "get_PositiveInfinitySymbol":
                        return I("il2cpp::runtime::il2cpp_number_format_positive_infinity_symbol", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    case "get_NegativeInfinitySymbol":
                        return I("il2cpp::runtime::il2cpp_number_format_negative_infinity_symbol", 0, true, true, true,
                            new[] { "" }, returnTypeName: "System.String");
                    default: return null;
                }
            case "System.Globalization.TextInfo":
                switch (name)
                {
                    // TextInfo.ToLower/ToUpper(string): the TextInfo receiver
                    // is dropped ("drop" slot 0) -- the string case icalls
                    // take the string as their only argument.
                    case "ToLower":
                        return I("il2cpp::runtime::icall_string_to_lower", 1, true, true, true, new[] { "drop", "" });
                    case "ToUpper":
                        return I("il2cpp::runtime::icall_string_to_upper", 1, true, true, true, new[] { "drop", "" });
                    default: return null;
                }
            default: return null;
        }
    }

    private static ResolvedMethod Literal(string s) => new(
        ResolvedMethod.LiteralMarker + s, 0, false, true, true, Array.Empty<bool>(), true,
        ReturnTypeName: "System.String");
}
