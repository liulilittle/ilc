namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Regex -- System.Text.RegularExpressions.Regex icall mappings
// (regex.h / regex_api.cpp).
// Owners: System.Text.RegularExpressions.Regex
//
// The runtime surface mirrors the .NET STATIC signatures EXACTLY -- input
// first, pattern second (net48: IsMatch(string input, string pattern)) --
// so every mapping is positional (the M5 emitter has no arg-reordering
// facility; see MethodMap.Buffer.cs's MemoryCopy note). The runtime-only
// trailing parameters default in regex.h (options = 0, Match's
// outStart/outLength = nullptr), so a 2-arg .NET call becomes a plain 2-arg
// C call:
//
//   IsMatch(string, string[, RegexOptions[, TimeSpan]])
//       -> il2cpp::runtime::il2cpp_regex_is_match    (int32 result)
//   Match(string, string[, RegexOptions[, TimeSpan]])
//       -> il2cpp::runtime::il2cpp_regex_match       (string[] result)
//   Replace(string, string, string[, RegexOptions[, TimeSpan]])
//       -> il2cpp::runtime::il2cpp_regex_replace     (string result)
//   Split(string, string[, RegexOptions[, TimeSpan]])
//       -> il2cpp::runtime::il2cpp_regex_split       (string[] result)
//
// Casts: every string slot gets "s" (Il2CppString* -- the IR carries strings
// as Il2CppObject*), the RegexOptions enum slot (i4) gets "i", and the
// trailing .NET TimeSpan matchTimeout overload is DROPPED (the engine enforces
// its own step budget, regex-domain.md §2.4 -- net48 timeouts have no runtime
// counterpart).
//
// Known-unwireable members reject LOUDLY (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap):
//   * Instance forms (new Regex(pattern).IsMatch(input) etc.) -- the runtime
//     has no Regex ctor/handle object graph (regex-domain.md §1.1, v2).
//   * Replace(string, string, MatchEvaluator) -- no delegate-evaluator
//     support on the runtime surface.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRegex(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "IsMatch": return RegexStatic(ps, "il2cpp::runtime::il2cpp_regex_is_match", false);
            case "Match": return RegexStatic(ps, "il2cpp::runtime::il2cpp_regex_match", true);
            case "Split": return RegexStatic(ps, "il2cpp::runtime::il2cpp_regex_split", true);
            case "Replace": return RegexReplace(ps);
            default: return null;
        }
    }

    // IsMatch / Match / Split: (string input, string pattern[, RegexOptions
    // [, TimeSpan]]). Returns a bool (IsMatch) or a string[] reference.
    private static ResolvedMethod? RegexStatic(byte[] ps, string cpp, bool returnsRef)
    {
        if (ps.Length < 2)
            throw new NotSupportedException(
                "instance form: the runtime has no Regex ctor/handle object graph (v2, regex-domain.md §1.1)");
        if (ps[0] != 0x0E || ps[1] != 0x0E)
            return null; // not a (string, string) overload
        return ps.Length switch
        {
            2 => I(cpp, 2, true, returnsRef, false, new[] { "s", "s" },
                returnTypeName: returnsRef ? "System.String[]" : null),
            3 when ps[2] == 0x08 => I(cpp, 3, true, returnsRef, false, new[] { "s", "s", "i" },
                returnTypeName: returnsRef ? "System.String[]" : null),
            // RegexOptions + TimeSpan matchTimeout: the timeout is dropped
            // (step budget replaces it).
            >= 4 when ps[2] == 0x08 => I(cpp, 4, true, returnsRef, false, new[] { "s", "s", "i", "drop" },
                returnTypeName: returnsRef ? "System.String[]" : null),
            _ => null,
        };
    }

    // Replace: (string input, string pattern, string replacement[, RegexOptions
    // [, TimeSpan]]). Returns a string reference.
    private static ResolvedMethod? RegexReplace(byte[] ps)
    {
        if (ps.Length < 3)
            throw new NotSupportedException(
                "instance form: the runtime has no Regex ctor/handle object graph (v2, regex-domain.md §1.1)");
        if (ps[0] != 0x0E || ps[1] != 0x0E)
            return null;
        if (ps[2] == 0x12)
            throw new NotSupportedException("MatchEvaluator overload: no delegate-evaluator support on the runtime surface");
        if (ps[2] != 0x0E)
            return null;
        return ps.Length switch
        {
            3 => I("il2cpp::runtime::il2cpp_regex_replace", 3, true, true, false, new[] { "s", "s", "s" },
                returnTypeName: "System.String"),
            4 when ps[3] == 0x08 => I("il2cpp::runtime::il2cpp_regex_replace", 4, true, true, false,
                new[] { "s", "s", "s", "i" }, returnTypeName: "System.String"),
            // RegexOptions + TimeSpan matchTimeout: the timeout is dropped.
            >= 5 when ps[3] == 0x08 => I("il2cpp::runtime::il2cpp_regex_replace", 5, true, true, false,
                new[] { "s", "s", "s", "i", "drop" }, returnTypeName: "System.String"),
            _ => null,
        };
    }
}
