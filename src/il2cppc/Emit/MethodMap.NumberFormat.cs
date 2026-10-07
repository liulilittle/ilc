namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.NumberFormat -- System.Globalization.NumberFormatInfo / TextInfo
// wiring. The runtime pins the INVARIANT culture (numberformat.h documents
// the invariant baseline: '.' decimal, ',' thousands, "%" percent layout);
// these members fold to the matching fixed-culture literals so BCL calls
// never reach the runtime for culture trivia.
//
// Owners: System.Globalization.NumberFormatInfo / TextInfo
//
// WIRING STATUS (2026-08-03, verified):
//   * il2cpp_number_format_* (general/fixed/hex/scientific/percent for
//     int64/double, numberformat.h) are NOT reachable from this domain: the
//     dispatch in MethodRefResolver.cs routes System.Int32/Int64/Double to
//     TryMapFormat ?? TryMapValueType only (MethodRefResolver.cs:312-319).
//     Unlocking them needs MORE than appending "?? TryMapNumberFormat": the
//     format domain returns non-null for EVERY ToString(string) overload
//     (MethodMap.Format.cs:17-24), so a fallback never fires -- making the
//     invariant icalls reachable requires CHANGING TryMapFormat's ownership
//     (e.g. moving a format spec over) = switching the existing zh-CN wiring,
//     which is explicitly out of scope for this task. Also verified: format_ext
//     ALREADY covers double's "P" (formatext.cpp:716-731, zh-CN "%" layout
//     without space) and ints' D/X/N/C/E/G (formatext.cpp:595-621) -- the
//     numberformat.h invariant variants are a SEMANTICS difference (e.g.
//     "12.3 %" with space), not a coverage gap. Wiring value-formatting here
//     would be dead code; stays on icall_format_*_custom (NOT switched).
//   * NumberFormatInfo property getters fold to invariant literal strings
//     (::lit::) -- NumberDecimalSeparator ".", NumberGroupSeparator ",",
//     PercentSymbol "%" etc., matching numberformat.h's fixed culture. The
//     special spellings pinned by numberformat.h (lines 7-8: "NaN"/"Infinity"/
//     "-Infinity") fold here as get_NaNSymbol/get_PositiveInfinitySymbol/
//     get_NegativeInfinitySymbol -- reachable, invariant-unique. The zh-CN
//     currency symbol get_CurrencySymbol (U+00A5, yen sign) is wired directly to the
//     runtime icall il2cpp_number_format_currency_symbol (D-CUL-1 -- the same
//     spelling formatext.cpp's C format emits, so the getter and ToString("C")
//     agree).
//   * get_CurrentInfo/get_InvariantInfo fold to null (::null::, same as
//     CultureInfo.get_InvariantCulture): consumers in the wired surface
//     either drop the culture or fold the getters directly.
//   * TextInfo.ToLower/ToUpper drop the TextInfo receiver and reuse the
//     string case icalls (icall_string_to_lower/upper) -- invariant-culture
//     TextInfo case mapping equals the string case helpers.
//   * NumberFormatInfo property getters fold to invariant literal strings
//     (::lit::) -- NumberDecimalSeparator ".", NumberGroupSeparator ",",
//     PercentSymbol "%" etc., matching numberformat.h's fixed culture.
//   * get_CurrentInfo/get_InvariantInfo fold to null (::null::, same as
//     CultureInfo.get_InvariantCulture): consumers in the wired surface
//     either drop the culture or fold the getters directly.
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
                    // saudit4: the culture-name receiver selects the symbol
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
