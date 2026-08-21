namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StringExt3 -- System.String positional-search and comparison-mode
// icall mappings (il2cpp_string_ext3.h / stringext3.cpp).
// Owners: System.String (stringext3 gap-fill)
//
// The net48 OVERLOAD family the M5 string domain left unwired: the search
// and prefix tests that carry an explicit position and/or a comparison mode.
// Every mapping is INSTANCE (this_ is the string receiver). The single-
// position forms (IndexOf(char), IndexOf(string), LastIndexOf(char),
// StartsWith(string), EndsWith(string), Contains, CompareOrdinal) live in
// MethodMap.String.cs -- this file must NOT re-map them (return null).
//
// OVERLOAD DISTINCTION (RawSignature ps bytes): the start/count arguments
// are plain int32 (0x08); the StringComparison argument is the StringComparison
// ENUM -- a valuetype (0x11) whose trailing TypeHandle RawSignature consumes,
// so ps[1] == 0x11 for StartsWith/EndsWith(string, StringComparison). Dispatch
// on ps.Length FIRST, then ps[0]/ps[1].
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStringExt3(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "IndexOf":
                // IndexOf(char, int32) / (char, int32, int32) / (string, int32)
                // / (string, int32, int32) -- the 1-arg forms stay in String.cs.
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_c_from", 2, true, false, true, new[] { "", "c", "i" });
                if (ps.Length == 3 && ps[0] == 0x03 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_c_range", 3, true, false, true, new[] { "", "c", "i", "i" });
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_s_from", 2, true, false, true, new[] { "", "", "i" });
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_s_range", 3, true, false, true, new[] { "", "", "i", "i" });
                // IndexOf(string, StringComparison) / (string, int, StringComparison):
                // known net48 overloads with NO icall -- the enum must not be
                // mistaken for the startIndex above.
                // IndexOf(string, StringComparison) / (string, int, StringComparison)
                // / (string, int, int, StringComparison) -- batch 149: the
                // comparison-mode icalls (the enum arrives as its int32 value
                // slot, "i").
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_index_of_s_sc", 2, true, false, true, new[] { "", "", "i" });
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_string_index_of_s_from_sc", 3, true, false, true, new[] { "", "", "i", "i" });
                if (ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x11)
                    return I("il2cpp::runtime::icall_string_index_of_s_range_sc", 4, true, false, true, new[] { "", "", "i", "i", "i" });
                return null;
            case "LastIndexOf":
                // LastIndexOf(string) and the char/string position/range
                // overloads -- the net48 family of il2cpp_string_ext3.h.
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_string_last_index_of_s", 1, true, false, true, new[] { "", "" });
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_c_from", 2, true, false, true, new[] { "", "c", "i" });
                if (ps.Length == 3 && ps[0] == 0x03 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_c_range", 3, true, false, true, new[] { "", "c", "i", "i" });
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_s_from", 2, true, false, true, new[] { "", "", "i" });
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_s_range", 3, true, false, true, new[] { "", "", "i", "i" });
                // LastIndexOf(string, StringComparison) / (string, int, StringComparison):
                // known net48 overloads with NO icall -- LOUD, as above.
                // LastIndexOf(string, StringComparison) / (string, int, StringComparison)
                // / (string, int, int, StringComparison) -- batch 149.
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_last_index_of_s_sc", 2, true, false, true, new[] { "", "", "i" });
                if (ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_string_last_index_of_s_from_sc", 3, true, false, true, new[] { "", "", "i", "i" });
                if (ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x11)
                    return I("il2cpp::runtime::icall_string_last_index_of_s_range_sc", 4, true, false, true, new[] { "", "", "i", "i", "i" });
                return null;
            case "StartsWith":
                // StartsWith(string, StringComparison): ps[1] is the ENUM
                // valuetype (0x11); the runtime takes the mode as int32_t.
                // The 1-arg ordinal form stays in String.cs.
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_startswith_culture", 2, true, false, true, new[] { "", "", "i" });
                return null;
            case "EndsWith":
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_endswith_culture", 2, true, false, true, new[] { "", "", "i" });
                return null;
            default: return null;
        }
    }
}
