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
            // ToBase64String(byte[]) -> string: the single-arg form only; the
            // offset/length/options overloads have no runtime helper. The
            // byte[] slot takes the "a" cast (Il2CppObject* -> Il2CppArray*;
            // the runtime icall declares the concrete array type, and
            // Il2CppArray embeds Il2CppObject as its first member -- the
            // same exact-address reinterpret as the "s" string cast).
            case "ToBase64String":
                return ps.Length == 1 && ps[0] == 0x1D
                    ? I("il2cpp::runtime::il2cpp_convert_to_base64_string", 1, true, true, false, new[] { "a" })
                    : throw new NotSupportedException($"Convert.ToBase64String overload (arity {ps.Length}) not wired in M5 (single byte[] form only)");
            // FromBase64String(string) -> byte[]: the only overload of this name.
            // ps[0] is the string param (0x0E) -- the SZARRAY-return element
            // byte is now consumed by RawSignature (G2), so ps no longer
            // holds the leaked 0x05 element.
            case "FromBase64String":
                return ps.Length == 1 && ps[0] == 0x0E
                    ? I("il2cpp::runtime::il2cpp_convert_from_base64_string", 1, true, true, false, new[] { "s" })
                    : throw new NotSupportedException($"Convert.FromBase64String overload (arity {ps.Length}) not wired in M5 (single string form only)");
            // char[]-buffer variants: same Base64 domain, no runtime helper.
            case "ToBase64CharArray":
            case "FromBase64CharArray":
                throw new NotSupportedException($"Convert.{name} (char[] variant) has no runtime helper on the M5 surface");
            default: return null;
        }
    }
}
