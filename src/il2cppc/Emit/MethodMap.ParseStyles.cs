namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ParseStyles -- the IFormatProvider / NumberStyles-aware numeric
// parse overloads (src/runtime/include/internal/il2cpp_parse_styles.h).
//
// WHY THIS FILE EXISTS: the double.Parse/TryParse overloads that carry a
// NumberStyles and/or IFormatProvider argument used to fall through to the
// generated-name path (an undeclared C++ symbol). The runtime surface that
// serves them lives in parse_styles.cpp, and the group/decimal separators it
// accepts come from the PROVIDER's NumberFormatInfo -- which is what makes
// double.Parse("1,000") == 1000 under the invariant culture and
// double.Parse("1.000") == 1000 under de-DE.
//
// PROVIDER MODEL: this runtime has no IFormatProvider object model -- a
// provider IS its culture-NAME string (the CultureInfo handle-gap documented
// in cultureinfo.h). A nullptr provider means "no provider" == the CURRENT
// culture, exactly like net48. The provider therefore passes through UNCAST
// ("" code); the two injectable defaults are:
//   * (string, NumberStyles): the missing provider -> a `nullptr` slot
//     (argOrder -1), i.e. the current culture;
//   * (string, IFormatProvider): the missing styles -> the net48 provider-only
//     default for floating point, NumberStyles.Float | AllowThousands = 231
//     (measured: double.Parse("1,000", InvariantCulture) == 1000).
//
// Owners: System.Double. Kept OUT of MethodMap.ValueType.cs so the value-
// semantics ownership of that file is untouched.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapParseStyles(string owner, string name, byte[] ps, int count,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Double") return null;
        if (name != "Parse" && name != "TryParse") return null;
        // NumberStyles is the VALUETYPE (0x11) enum, IFormatProvider the CLASS
        // (0x12) interface, and the TryParse result is the BYREF (0x0D) double.
        if (name == "Parse")
        {
            if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                return I("il2cpp::runtime::icall_string_parse_double_styles", 2, true, false, false,
                    new[] { "s", "i", "" }, "d", argOrder: new[] { 0, 1, -1 });
            if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x12)
                return I("il2cpp::runtime::icall_string_parse_double_styles", 2, true, false, false,
                    new[] { "s", "lit:231", "" }, "d", argOrder: new[] { 0, 1, 1 });
            if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x11 && ps[2] == 0x12)
                return I("il2cpp::runtime::icall_string_parse_double_styles", 3, true, false, false,
                    new[] { "s", "i", "" }, "d");
            return null;
        }
        if (ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x11 && ps[2] == 0x12 && ps[3] == 0x0D)
            return I("il2cpp::runtime::icall_try_parse_double_styles", 4, true, false, false,
                new[] { "", "i", "", "addr:d" });
        return null;
    }
}
