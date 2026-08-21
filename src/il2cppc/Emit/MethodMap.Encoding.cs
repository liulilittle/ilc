namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Encoding -- System.Text.Encoding / UTF8Encoding / ASCIIEncoding /
// UnicodeEncoding wiring (il2cpp_encoding.h).
// Owners: System.Text.Encoding / UTF8Encoding / ASCIIEncoding / UnicodeEncoding
//
// Wired surface:
//   * get_UTF8 / get_Unicode / get_ASCII fold to null (::null::) -- the
//     runtime has no Encoding OBJECT (il2cpp_encoding.h ships no singleton
//     icall) and the carrier model has no Encoding instance -- but record
//     the CONCRETE encoding type (ReturnTypeName "System.Text.UTF8Encoding"
//     etc.), which the fold carries on the folded NullExpr; the next
//     callvirt (Encoding.UTF8.GetBytes(...)) re-resolves with that receiver
//     hint (StackFold's callvirt receiver-type derivation).
//   * GetBytes(string) -> byte[], GetString(byte[]) -> string and
//     GetChars(byte[]) -> char[] (UTF8 page): the ALLOCATING icall family
//     (il2cpp_encoding_<page>_get_bytes / _get_string /
//     _utf8_get_chars). One icall per code page, page baked into the NAME --
//     the M5 emitter cannot inject a page constant (EmitIcallCallArgs
//     prepends only ParamCount and 0) -- so the map dispatches the page by
//     the receiver's tracked type. The receiver itself is DROPPED at
//     emission ("drop" cast): the singleton is null-folded and the page is
//     already fixed by the icall name, so no receiver argument reaches the
//     call. The byte[] argument carries the "a" cast (Il2CppObject* ->
//     Il2CppArray*; the string carries "s"), the same codes as the
//     Base64/string icalls. Results are tracked ("System.Byte[]" /
//     "System.String" / "System.Char[]") so downstream
//     ldlen/ldelem/string-equality folds resolve.
//   * First-resolution deferral: the fold resolves a callvirt TWICE -- once
//     before the receiver is popped (receiverType == null) and again with
//     the receiver hint. With no hint the map returns null (falling to the
//     generated-name path), so the receiver-aware re-resolve dispatches; a
//     call shape whose receiver stays UNtracked (e.g. Encoding stored in a
//     plain variable) keeps the generated-name fallback and fails LOUD at
//     emission.
//
// LOUD rejections -- the remaining forms of the three names:
//   * GetBytes/GetString overloads other than the allocating single-arg
//     forms (GetBytes(byte[],int,int,byte[],int), GetString(byte[],int,int),
//     the offset/length family) have no allocating helper.
//   * GetChars(byte[],int,int) ranged form: now wired on ALL three pages
//     (enc-b23 -- il2cpp_encoding_<page>_get_chars 3-arg overloads,
//     il2cpp_encoding.h:152-154); the single-array GetChars(byte[]) stays
//     UTF8-page-only (the ascii/unicode pages ship the ranged form only).
//
// Out-of-scope members (GetByteCount/GetCharCount/GetEncoding/Convert/
// get_Default/ctors/...) return null -> the generated-name path (standard
// unwired behavior).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEncoding(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_UTF8":
            case "get_Unicode":
            case "get_ASCII":
            case "get_BigEndianUnicode":
                // No Encoding object exists in the runtime carrier model; the
                // fold keeps the value deterministic (null) and never
                // dereferenced by any wired consumer. ReturnTypeName records
                // the concrete encoding type on the folded null -- the
                // GetBytes/GetString page dispatch keys on it. BigEndianUnicode
                // uses the pseudo-type "System.Text.UnicodeEncodingBE" (the
                // .NET type is UnicodeEncoding for both LE and BE, so the
                // pseudo-type disambiguates the page).
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false,
                    ReturnTypeName: name switch
                    {
                        "get_UTF8" => "System.Text.UTF8Encoding",
                        "get_Unicode" => "System.Text.UnicodeEncoding",
                        "get_BigEndianUnicode" => "System.Text.UnicodeEncodingBE",
                        _ => "System.Text.ASCIIEncoding",
                    });
            case "GetBytes":
                // Allocating form only: Encoding.GetBytes(string) -> byte[].
                // receiverType == null is the fold's FIRST resolution (the
                // receiver is popped after) -- defer to the receiver-aware
                // re-resolve by returning null (generated-name fallback).
                if (count != 1 || ps[0] != 0x0E)
                    throw new NotSupportedException(
                        $"Encoding.GetBytes overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                        "(allocating single-string form only)");
                if (receiverType == null) return null;
                return EncodingAllocIcall(receiverType, name, "get_bytes", new[] { "drop", "s" }, "System.Byte[]");
            case "GetString":
                if (count != 1 || ps[0] != 0x1D)
                    throw new NotSupportedException(
                        $"Encoding.GetString overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                        "(allocating single-byte[] form only)");
                if (receiverType == null) return null;
                return EncodingAllocIcall(receiverType, name, "get_string", new[] { "drop", "a" }, "System.String");
            case "GetChars":
                // Allocating forms only:
                //   GetChars(byte[])            -> char[]  (single-array form)
                //   GetChars(byte[], int, int)  -> char[]  (ranged form)
                // The page is baked into the icall NAME, chosen from the
                // receiver's tracked encoding type -- so a receiverType == null
                // FIRST resolution defers (return null -> receiver-aware
                // re-resolve). M5 wired the UTF8 page's single-array form;
                // enc-b23 added the ranged (byte[],index,count) allocating
                // form on ALL three pages (il2cpp_encoding_<page>_get_chars
                // 3-arg overloads, il2cpp_encoding.h:152-154).
                if (receiverType == null) return null;
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I($"il2cpp::runtime::il2cpp_encoding_{EncodingPage(receiverType, "GetChars")}_get_chars",
                        3, true, true, true, new[] { "drop", "a", "i", "i" }, returnTypeName: "System.Char[]");
                // M5 legacy: the single-array form ships on the UTF8 page only
                // (il2cpp_encoding_utf8_get_chars(Il2CppArray*)); the ascii/
                // unicode pages have the 3-arg ranged form only.
                if (count == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::il2cpp_encoding_utf8_get_chars", 1, true, true, true,
                        new[] { "drop", "a" }, returnTypeName: "System.Char[]");
                throw new NotSupportedException(
                    $"Encoding.GetChars overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired in M5 " +
                    "(allocating single-byte[] and ranged (byte[],int,int) forms only)");
            default: return null;
        }
    }

    // Page dispatch for the allocating GetBytes/GetString/GetChars icalls: the
    // page is baked into the icall NAME, chosen from the receiver's tracked
    // encoding type (the null-folded singleton getters set it). Any other
    // receiver (a plain Encoding-typed variable, a custom Encoding subclass)
    // is unwireable -- LOUD, self-identifying.
    private static string EncodingPage(string receiverType, string member)
    {
        return receiverType switch
        {
            "System.Text.UTF8Encoding" => "utf8",
            "System.Text.UnicodeEncoding" => "unicode",
            "System.Text.UnicodeEncodingBE" => "unicodebe",
            "System.Text.ASCIIEncoding" => "ascii",
            _ => throw new NotSupportedException(
                $"{member} on receiver '{receiverType}' not wired in M5 (UTF8/Unicode/ASCII singleton " +
                "receivers only -- call the member directly on Encoding.UTF8/Unicode/ASCII)"),
        };
    }

    private static ResolvedMethod EncodingAllocIcall(string receiverType, string member, string suffix,
        string[] casts, string resultType)
    {
        return I($"il2cpp::runtime::il2cpp_encoding_{EncodingPage(receiverType, member)}_{suffix}", 1, true, true, true, casts,
            returnTypeName: resultType);
    }
}
