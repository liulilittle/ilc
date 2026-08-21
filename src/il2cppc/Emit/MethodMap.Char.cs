namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Char -- System.Char STATIC surface (D-CHA-2, charstr GAPS.md):
// the classification / case-conversion methods (char.IsDigit / IsLetter /
// IsLetterOrDigit / IsLower / IsUpper / IsWhiteSpace / ToUpper /
// ToUpperInvariant / ToLower / ToLowerInvariant). icalls live in il2cpp_char.h
// (char.cpp) -- ASCII-domain semantics pinned against the net48 probes.
//
// Dispatch note: the BclResolveCore "System.Char" case (MethodRefResolver.cs)
// runs TryMapChar BEFORE TryMapValueType -- the static surface here, the
// instance surface (ToString()/Equals(object)) stays in MethodMap.ValueType.cs.
//
// Signature shape: every member takes exactly ONE char parameter (element
// byte 0x03 SignatureTypeCode.Char -- MethodSignatureCoder.cs:99). Is* return
// bool (0x02) -> the int32 ABI slot (null ResultKind); To* return char (0x03)
// -> char16_t icall result, which widens exactly into the int32 slot the
// emitter allocates (the bitconverter ToChar precedent -- null ResultKind).
// The char argument crosses with the "c" cast (static_cast<char16_t>), the
// icall_stringbuilder_append_c precedent.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapChar(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.Char")
            return null;
        if (ps.Length != 1 || ps[0] != 0x03)
            return null;
        switch (name)
        {
            case "IsDigit": return I("il2cpp::runtime::icall_char_is_digit", 1, true, false, false, new[] { "c" });
            case "IsLetter": return I("il2cpp::runtime::icall_char_is_letter", 1, true, false, false, new[] { "c" });
            case "IsLetterOrDigit": return I("il2cpp::runtime::icall_char_is_letter_or_digit", 1, true, false, false, new[] { "c" });
            case "IsLower": return I("il2cpp::runtime::icall_char_is_lower", 1, true, false, false, new[] { "c" });
            case "IsUpper": return I("il2cpp::runtime::icall_char_is_upper", 1, true, false, false, new[] { "c" });
            case "IsWhiteSpace": return I("il2cpp::runtime::icall_char_is_whitespace", 1, true, false, false, new[] { "c" });
            case "ToUpper":
            case "ToUpperInvariant": return I("il2cpp::runtime::icall_char_to_upper", 1, true, false, false, new[] { "c" });
            case "ToLower":
            case "ToLowerInvariant": return I("il2cpp::runtime::icall_char_to_lower", 1, true, false, false, new[] { "c" });
            default: return null;
        }
    }
}
