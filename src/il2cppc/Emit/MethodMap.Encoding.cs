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
                // P2(5) A1 (2026-09-19): the static singletons now return REAL
                // encoding objects (identity/GetPreamble/instance state). The
                // object icall allocates + caches per page; ReturnTypeName
                // records the concrete type so the object-receiver GetBytes/
                // GetString/GetChars dispatch (and `new UTF8Encoding(bool)`
                // chains) resolve. BigEndianUnicode uses the pseudo-type
                // "System.Text.UnicodeEncodingBE" (the .NET type is
                // UnicodeEncoding for both LE and BE).
                return I(name switch
                {
                    "get_UTF8" => "il2cpp::runtime::icall_encoding_get_UTF8",
                    "get_Unicode" => "il2cpp::runtime::icall_encoding_get_Unicode",
                    "get_BigEndianUnicode" => "il2cpp::runtime::icall_encoding_get_UnicodeBE",
                    _ => "il2cpp::runtime::icall_encoding_get_ASCII",
                }, 0, true, true, false,
                    returnTypeName: name switch
                    {
                        "get_UTF8" => "System.Text.UTF8Encoding",
                        "get_Unicode" => "System.Text.UnicodeEncoding",
                        "get_BigEndianUnicode" => "System.Text.UnicodeEncodingBE",
                        _ => "System.Text.ASCIIEncoding",
                    });
            case ".ctor":
                // P2(5) A1: `new UTF8Encoding(bool emitBom[, bool throwOnInvalid])`,
                // `new UnicodeEncoding(bool bigEndian, bool byteOrderMark)` and
                // `new ASCIIEncoding()`. The instance ctor sets page + emitBom
                // on the fresh object.
                if (owner == "System.Text.UTF8Encoding" && count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_encoding_ctor_utf8", 1, false, false, true,
                        new[] { "", "i" });
                if (owner == "System.Text.UTF8Encoding" && count == 2 && ps[0] == 0x02 && ps[1] == 0x02)
                    return I("il2cpp::runtime::icall_encoding_ctor_utf8_2", 2, false, false, true,
                        new[] { "", "i", "i" });
                if (owner == "System.Text.UnicodeEncoding" && count == 2 && ps[0] == 0x02 && ps[1] == 0x02)
                    return I("il2cpp::runtime::icall_encoding_ctor_unicode", 2, false, false, true,
                        new[] { "", "i", "i" });
                if (owner == "System.Text.ASCIIEncoding" && count == 0)
                    return I("il2cpp::runtime::icall_encoding_ctor_ascii", 0, false, false, true);
                return null; // other Encoding ctors -> generated-name path
            case "GetPreamble":
                // byte[] GetPreamble() -- the BOM bytes for the receiver's
                // page/emitBom (empty array when emitBom=0).
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                return I("il2cpp::runtime::icall_encoding_get_preamble", 0, true, true, true,
                    returnTypeName: "System.Byte[]");
            case "GetBytes":
                // Allocating forms: GetBytes(string) -> byte[],
                // GetBytes(char[]) / GetBytes(char[],int,int) -> byte[].
                // receiverType == null is the fold's FIRST resolution (the
                // receiver is popped after) -- defer to the receiver-aware
                // re-resolve by returning null (generated-name fallback).
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                if (count == 1 && ps[0] == 0x0E)
                    return ObjectEncodingIcall("icall_encoding_get_bytes", "get_bytes",
                        new[] { "", "s" }, "System.Byte[]");
                if (count == 1 && ps[0] == 0x1D)
                    return ObjectEncodingIcall("icall_encoding_get_bytes_chars", "get_bytes",
                        new[] { "", "a" }, "System.Byte[]");
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_bytes_chars_range", "get_bytes",
                        new[] { "", "a", "i", "i" }, "System.Byte[]");
                // nb9 (M8): the DESTINATION-BUFFER overloads
                //   GetBytes(string,int,int,byte[],int) -> int
                //   GetBytes(char[],int,int,byte[],int) -> int
                // (previously LOUD; the unit test codified the rejection).
                if (count == 5 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x1D && ps[4] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_bytes_into_string", "get_bytes",
                        new[] { "", "s", "i", "i", "a", "i" }, null);
                if (count == 5 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x1D && ps[4] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_bytes_into_chars", "get_bytes",
                        new[] { "", "a", "i", "i", "a", "i" }, null);
                throw new NotSupportedException(
                    $"Encoding.GetBytes overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                    "(allocating string/char[] and ranged (char,int,int) forms only)");
            case "GetByteCount":
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                if (count == 1 && ps[0] == 0x0E)
                    return ObjectEncodingIcall("icall_encoding_get_byte_count_string", "get_byte_count",
                        new[] { "", "s" }, null);
                if (count == 1 && ps[0] == 0x1D)
                    return ObjectEncodingIcall("icall_encoding_get_byte_count_chars", "get_byte_count",
                        new[] { "", "a" }, null);
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_byte_count_chars_range", "get_byte_count",
                        new[] { "", "a", "i", "i" }, null);
                throw new NotSupportedException(
                    $"Encoding.GetByteCount overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                    "(string/char[] and ranged (char,int,int) forms only)");
            case "GetCharCount":
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                if (count == 1 && ps[0] == 0x1D)
                    return ObjectEncodingIcall("icall_encoding_get_char_count_bytes", "get_char_count",
                        new[] { "", "a" }, null);
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_char_count_bytes_range", "get_char_count",
                        new[] { "", "a", "i", "i" }, null);
                throw new NotSupportedException(
                    $"Encoding.GetCharCount overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                    "(byte[] and ranged (byte,int,int) forms only)");
            case "GetString":
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                if (count == 1 && ps[0] == 0x1D)
                    return ObjectEncodingIcall("icall_encoding_get_string", "get_string",
                        new[] { "", "a" }, "System.String");
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_string_range", "get_string",
                        new[] { "", "a", "i", "i" }, "System.String");
                throw new NotSupportedException(
                    $"Encoding.GetString overload (arity {count}, param 0x{ps[0]:X2}) not wired in M5 " +
                    "(byte[] and ranged (byte,int,int) forms only)");
            case "GetChars":
                // GetChars(byte[]) / GetChars(byte[],int,int) -> char[],
                // GetChars(byte[],int,int,char[],int) -> int (writes into the
                // caller's destination array).
                // nb9 (M4-fix/M8): the object-receiver entries read the page off
                // the receiver at runtime, so a plain Encoding-typed parameter is
                // fully supported -- do NOT defer on an untracked receiver.
                if (count == 5 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x1D && ps[4] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_chars_into", "get_chars",
                        new[] { "", "a", "i", "i", "a", "i" }, null);
                if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return ObjectEncodingIcall("icall_encoding_get_chars", "get_chars",
                        new[] { "", "a", "i", "i" }, "System.Char[]");
                if (count == 1 && ps[0] == 0x1D)
                    return ObjectEncodingIcall("icall_encoding_get_chars_all", "get_chars",
                        new[] { "", "a" }, "System.Char[]");
                throw new NotSupportedException(
                    $"Encoding.GetChars overload (arity {count}, param 0x{(count > 0 ? ps[0] : 0):X2}) not wired in M5 " +
                    "(allocating single-byte[] / ranged / destination-array forms only)");
            default: return null;
        }
    }

    // P2(5) A1: the object-receiver allocating icall. The receiver is a REAL
    // Encoding object (page/emitBom read at runtime); the page no longer rides
    // the icall name. `member` names the managed member for the LOUD receiver
    // check. ASCII/UTF8/Unicode/BE receiver types are all accepted (the object
    // carries the page).
    private static ResolvedMethod ObjectEncodingIcall(string cpp, string member,
        string[] casts, string? resultType)
    {
        return I($"il2cpp::runtime::{cpp}", casts.Length - 1, true, resultType != null, true, casts,
            returnTypeName: resultType);
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
