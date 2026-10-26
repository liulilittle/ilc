namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Base64 -- System.Convert Base64 mappings (il2cpp_convertbase64.h).
// Owners: System.Convert (ToBase64String/FromBase64String)
//
// The runtime helpers implement the net48 RFC 4648 semantics: '=' padding for
// partial final quantums (ToBase64String); whitespace ignored anywhere and
// '=' only legal at the end (FromBase64String). The byte[] param carries the
// "a" cast (Il2CppObject* -> Il2CppArray* -- the runtime icall declares the
// concrete array type); the string param carries the "s" cast exactly like
// the existing string icalls in MethodMap.Convert.cs. The char[]-array
// variants (ToBase64CharArray/FromBase64CharArray) have no runtime helper --
// rejected LOUD (the manual's known-unwireable rule).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapBase64(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // ToBase64String(byte[]) -> string: the single-arg form. The
            // byte[] slot takes the "a" cast (Il2CppObject* -> Il2CppArray*;
            // the runtime icall declares the concrete array type, and
            // Il2CppArray embeds Il2CppObject as its first member -- the
            // same exact-address reinterpret as the "s" string cast).
            case "ToBase64String":
                // BATCH 8: the offset/length/options overloads (the enum param
                // is a VALUETYPE 0x11 slot carrying the int32 flag).
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::il2cpp_convert_to_base64_string_range", 3, true, true, false,
                        new[] { "a", "i", "i" });
                if (ps.Length == 2 && ps[0] == 0x1D && ps[1] is 0x11 or 0x40)
                    return I("il2cpp::runtime::il2cpp_convert_to_base64_string_options", 2, true, true, false,
                        new[] { "a", "" });
                if (ps.Length == 4 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] is 0x11 or 0x40)
                    return I("il2cpp::runtime::il2cpp_convert_to_base64_string_range_options", 4, true, true, false,
                        new[] { "a", "i", "i", "" });
                return ps.Length == 1 && ps[0] == 0x1D
                    ? I("il2cpp::runtime::il2cpp_convert_to_base64_string", 1, true, true, false, new[] { "a" })
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToBase64String arity {ps.Length} -- " +
                        "net48 forms are (byte[]), (byte[],Base64FormattingOptions), (byte[],int,int) and " +
                        "(byte[],int,int,Base64FormattingOptions) [Reference Source convert.cs:2131,2141,2150,2156]");
            // FromBase64String(string) -> byte[]: the only overload of this name.
            // ps[0] is the string param (0x0E) -- the SZARRAY-return element
            // byte is now consumed by RawSignature (G2), so ps no longer
            // holds the leaked 0x05 element.
            case "FromBase64String":
                return ps.Length == 1 && ps[0] == 0x0E
                    ? I("il2cpp::runtime::il2cpp_convert_from_base64_string", 1, true, true, false, new[] { "s" })
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.FromBase64String arity {ps.Length} -- " +
                        "net48 has the single string form only [Reference Source convert.cs:2345]");
            // BATCH 8: FromBase64CharArray(char[], int, int) -> byte[].
            case "FromBase64CharArray":
                return ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    ? I("il2cpp::runtime::il2cpp_convert_from_base64_char_array", 3, true, true, false,
                        new[] { "a", "i", "i" })
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.FromBase64CharArray arity {ps.Length} -- " +
                        "net48 has the (char[],int,int) form only [Reference Source convert.cs:2371]");
            // char[]-buffer encoder (convert.cs:2193/2203): the int result is
            // the number of chars written into outArray at offsetOut. Both
            // net48 overloads are compile-time analyzable and wire to the
            // runtime char[] encoder (the options form inserts CRLF every 76
            // output chars).
            case "ToBase64CharArray":
                if (ps.Length == 5 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x1D && ps[4] == 0x08)
                    return I("il2cpp::runtime::il2cpp_convert_to_base64_char_array", 5, true, false, false,
                        new[] { "a", "i", "i", "a", "i" });
                if (ps.Length == 6 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x1D && ps[4] == 0x08 && ps[5] is 0x11 or 0x40)
                    return I("il2cpp::runtime::il2cpp_convert_to_base64_char_array_options", 6, true, false, false,
                        new[] { "a", "i", "i", "a", "i", "" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Convert.ToBase64CharArray arity {ps.Length} -- " +
                    "net48 forms are (byte[],int,int,char[],int) and (byte[],int,int,char[],int,Base64FormattingOptions) " +
                    "[Reference Source convert.cs:2193,2203]");
            default: return null;
        }
    }
}
