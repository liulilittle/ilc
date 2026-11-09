namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.String -- System.String icall mappings (il2cpp_string.h +
// il2cpp_string_ext2.h + string intern).
// Owners: System.String
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapString(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // System.String..ctor(char, int32) -- `new string(ch, n)`:
                // the runtime allocates a fresh string of 'count' copies of
                // 'ch' (icall_string_ctor_repeat, create-style: the newobj
                // placeholder receiver is dropped at the runtime; count < 0 ->
                // ArgumentOutOfRangeException, count == 0 -> the empty string,
                // net48 csc-probe-verified). ParamCount = the IL arity (2) so
                // the fold pops the char + count; casts: receiver "" (kept as
                // this_ -- the runtime ignores it), char -> "c", int -> "i".
                // ReturnTypeName tracks the result so downstream member calls
                // (get_Length etc.) resolve on it.
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_ctor_repeat", 2, true, true, true,
                        new[] { "", "c", "i" }, returnTypeName: "System.String");
                // stralloc-b23: ctor(char[]) -- a fresh copy of every unit;
                // null -> empty (net48 probe). The char[] SZARRAY param is a
                // managed array ref passing as Il2CppObject* ("" passthrough).
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_string_ctor_char_array", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                // stralloc-b23: ctor(char[], int32 startIndex, int32 length) --
                // net48-probe bounds: null -> ANE (FIRST), startIndex<0 or
                // length<0 -> AOO, window overflow -> AOO, length==0 -> empty.
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_ctor_char_array_range", 3, true, true, true,
                        new[] { "", "", "i", "i" }, returnTypeName: "System.String");
                // PTR-SIG (2026-08-22): the unsafe pointer ctor family -- the
                // ReadParamByte PTR arm now consumes the pointed-to element
                // and carries it, so char* arrives as 0x03 and sbyte* as 0x04.
                // The runtime icalls predate the wiring (string.cpp:360-477).
                // Casts: char* -> "p16" (const char16_t*), sbyte* -> "p"
                // (const char*) -- both established EmitIcallCallArgs codes.
                if (ps.Length == 1 && ps[0] == 0x03)
                    return I("il2cpp::runtime::icall_string_ctor_char_ptr", 1, true, true, true,
                        new[] { "", "p16" }, returnTypeName: "System.String");
                if (ps.Length == 3 && ps[0] == 0x03 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_ctor_char_ptr_range", 3, true, true, true,
                        new[] { "", "p16", "i", "i" }, returnTypeName: "System.String");
                if (ps.Length == 1 && ps[0] == 0x04)
                    return I("il2cpp::runtime::icall_string_ctor_sbyte_ptr", 1, true, true, true,
                        new[] { "", "p" }, returnTypeName: "System.String");
                if (ps.Length == 3 && ps[0] == 0x04 && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_ctor_sbyte_ptr_range", 3, true, true, true,
                        new[] { "", "p", "i", "i" }, returnTypeName: "System.String");
                // (sbyte*, int, int, Encoding): the Encoding object crosses as
                // its code-page number -- the runtime entry decodes via the
                // registered code page (string.cpp).
                if (ps.Length == 4 && ps[0] == 0x04 && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x12)
                    return I("il2cpp::runtime::icall_string_ctor_sbyte_ptr_range_encoding", 4, true, true, true,
                        new[] { "", "p", "i", "i", "" }, returnTypeName: "System.String");
                // SPAN-SIG (2026-08-22): new string(ReadOnlySpan<char>) --
                // net5+ GenericInst valuetype param (raw sig 20-01-01-15-...).
                // The span rides as a caller SLOT with the two-field
                // local-storage layout {ptr, length}; the icall reads both
                // fields through the address passthrough ("").
                if (ps.Length == 1 && ps[0] == 0x15)
                    return I("il2cpp::runtime::icall_string_ctor_char_span", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"String ctor with {ps.Length} arg(s) not wired (the runtime icalls cover (char, int32), (char[]), (char[], int32, int32) and the full char*/sbyte* pointer family) -- LOUD whitelist (net48 defines no String ctor of this arity/param shape); net48 RefSrc mscorlib/system/string.cs:1350-1762");
            case "get_Length":
                return I("il2cpp::il2cpp_string_get_length", 0, true, false, true);
            case "Substring":
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_string_substring", 1, true, true, true)
                    : I("il2cpp::runtime::icall_string_substring_range", 2, true, true, true);
            case "StartsWith": return ps.Length == 1 ? I("il2cpp::runtime::icall_string_startswith", 1, true, false, true) : null;
            case "EndsWith": return ps.Length == 1 ? I("il2cpp::runtime::icall_string_endswith", 1, true, false, true) : null;
            case "IndexOf":
                // The 1-arg forms (char)/(string) live here; the position and
                // range overloads (2/3 args) fall through to TryMapStringExt3.
                if (ps.Length != 1) return null;
                return ps[0] switch
                {
                    0x03 => I("il2cpp::runtime::icall_string_index_of_c", 1, true, false, true, new[] { "", "c" }),
                    0x0E => I("il2cpp::runtime::icall_string_index_of_s", 1, true, false, true),
                    _ => throw new NotSupportedException($"String.IndexOf param type 0x{ps[0]:X2} not wired in M5 -- LOUD whitelist (net48 IndexOf takes only (char) or (string) as its value argument); net48 RefSrc mscorlib/system/string.cs:2266-2350"),
                };
            case "IndexOfAny":
                // System.String.IndexOfAny(char[] anyOf, int32 startIndex,
                // int32 count) -- the first index in [startIndex,
                // startIndex + count) whose char is in 'anyOf', or -1. The
                // char[] SZARRAY param (0x1D) is a managed array ref that
                // passes as Il2CppObject* (the runtime casts to Il2CppArray*
                // internally -- the icall_string_join passthrough precedent);
                // the two int32s get "i" casts. net48 bounds
                // (csc-probe-verified): null anyOf -> ANE; (uint)startIndex >
                // Length -> AOORE; (uint)count > Length - startIndex -> AOORE.
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_any_range", 3, true, false, true,
                        new[] { "", "", "i", "i" });
                // net9/10 E2E campaign: the 1/2-arg forms -- runtime wrappers
                // default startIndex=0 / count=Length (stringext.cpp).
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_string_index_of_any", 1, true, false, true,
                        new[] { "", "" });
                if (ps.Length == 2 && ps[0] == 0x1D && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_index_of_any_start", 2, true, false, true,
                        new[] { "", "", "i" });
                throw new NotSupportedException($"String.IndexOfAny with {ps.Length} arg(s) not wired (the runtime icall takes (char[], int32, int32)) -- LOUD whitelist (net48 IndexOfAny has only (char[]), (char[],int) and (char[],int,int)); net48 RefSrc mscorlib/system/string.cs:2285-2298");
            case "LastIndexOfAny":
                // System.String.LastIndexOfAny(char[] anyOf, int32 startIndex,
                // int32 count) -- the LAST index in [startIndex - count + 1,
                // startIndex] whose char is in 'anyOf', or -1. Shape and casts
                // as IndexOfAny. net48 bounds: null anyOf -> ANE; Length == 0
                // -> -1; (uint)startIndex >= Length -> AOORE; count < 0 ||
                // count - 1 > startIndex -> AOORE.
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_any_range", 3, true, false, true,
                        new[] { "", "", "i", "i" });
                // net9/10 E2E campaign: 1/2-arg wrappers (stringext.cpp).
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_string_last_index_of_any", 1, true, false, true,
                        new[] { "", "" });
                if (ps.Length == 2 && ps[0] == 0x1D && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_last_index_of_any_start", 2, true, false, true,
                        new[] { "", "", "i" });
                throw new NotSupportedException($"String.LastIndexOfAny with {ps.Length} arg(s) not wired (the runtime icall takes (char[], int32, int32)) -- LOUD whitelist (net48 LastIndexOfAny has only (char[]), (char[],int) and (char[],int,int)); net48 RefSrc mscorlib/system/string.cs:2418-2432");
            case "LastIndexOf":
                // The char 1-arg form lives here; the string 1-arg form and
                // the position/range overloads fall through to TryMapStringExt3.
                if (ps.Length == 1 && ps[0] == 0x03) return I("il2cpp::runtime::icall_string_last_index_of_c", 1, true, false, true, new[] { "", "c" });
                return null;
            case "Replace":
                // strrep-b23: the runtime icall_string_replace is CHAR/CHAR
                // only (il2cpp_string_ext.h:88 -- (Il2CppObject*, char16_t,
                // char16_t)). strcomp2 (rtrepl-b23): the STRING/STRING
                // overload now has its own runtime surface --
                // icall_string_replace_s_s (il2cpp_string_ext.h:90, net48
                // semantics: non-overlapping greedy replace, null/empty new
                // deletes, no-match returns the identity, empty old throws
                // ArgumentException). All three slots are Il2CppString*: the
                // "s" cast (NullSafePtrCast Il2CppString*) applies to the
                // receiver self, oldValue and newValue alike -- the split
                // mapping's receiver-cast convention. returnTypeName keeps
                // downstream get_Length/indexer resolution on the result.
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x03)
                    return I("il2cpp::runtime::icall_string_replace", 2, true, true, true, new[] { "", "c", "c" });
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                    return I("il2cpp::runtime::icall_string_replace_s_s", 2, true, true, true,
                        new[] { "s", "s", "s" }, returnTypeName: "System.String");
                throw new NotSupportedException($"String.Replace with {ps.Length} arg(s) not wired in M5 (the runtime icall_string_replace takes (char, char); the string/string overload needs a runtime icall_string_replace_s_s) -- LOUD whitelist (net48 Replace has only (char,char) and (string,string)); net48 RefSrc mscorlib/system/string.cs:2863-2879");
case "ToUpper":
case "ToUpperInvariant":
    // ToUpper(IFormatProvider): the provider is IGNORED (the runtime pins the
    // net48 zh-CN simple case map -- the documented ASCII-domain deviation);
    // ParamCount = 1 so the fold pops receiver + provider and the "drop" cast
    // keeps only the receiver. The OLD mapping used ParamCount = 0 and passed
    // the provider as the receiver (strgen1 up_ci/lo_ci).
    if (ps.Length == 0)
        return I("il2cpp::runtime::icall_string_to_upper", 0, true, true, true);
    if (ps.Length == 1)
        return I("il2cpp::runtime::icall_string_to_upper", 1, true, true, true, new[] { "", "drop" });
    throw new NotSupportedException($"String.ToUpper with {ps.Length} arg(s) not wired -- LOUD whitelist (net48 ToUpper has only () and (CultureInfo); ToUpperInvariant is netcore-only); net48 RefSrc mscorlib/system/string.cs:2667-2676");
case "ToLower":
case "ToLowerInvariant":
    // Symmetric to ToUpper (see above).
    if (ps.Length == 0)
        return I("il2cpp::runtime::icall_string_to_lower", 0, true, true, true);
    if (ps.Length == 1)
        return I("il2cpp::runtime::icall_string_to_lower", 1, true, true, true, new[] { "", "drop" });
    throw new NotSupportedException($"String.ToLower with {ps.Length} arg(s) not wired -- LOUD whitelist (net48 ToLower has only () and (CultureInfo); ToLowerInvariant is netcore-only); net48 RefSrc mscorlib/system/string.cs:2640-2648");
// net9/10 E2E icallcase: Insert/Remove (stringext.cpp, new icalls).
case "Insert": return I("il2cpp::runtime::icall_string_insert", 2, true, true, true,
    new[] { "s", "i", "s" }, returnTypeName: "System.String");
case "Remove":
    return ps.Length == 2
        ? I("il2cpp::runtime::icall_string_remove", 2, true, true, true,
            new[] { "s", "i", "i" }, returnTypeName: "System.String")
        // Remove(int startIndex): removes through the END (remove_to_end).
        : ps.Length == 1
        ? I("il2cpp::runtime::icall_string_remove_to_end", 1, true, true, true,
            new[] { "s", "i" }, returnTypeName: "System.String")
        : null;
// net9/10 E2E campaign (string sweep): ToCharArray() -- a NEW char[] copy.
case "ToCharArray":
    if (ps.Length == 0)
        return I("il2cpp::runtime::icall_string_to_char_array", 0, true, true, true,
            new[] { "s" }, returnTypeName: "System.Char[]");
    // P26 (bcldeep): ToCharArray(startIndex, length) -- the RANGED copy
    // (net8 CoreLib body is pure-managed; the transpiler needs the icall).
    if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
        return I("il2cpp::runtime::icall_string_to_char_array_range", 2, true, true, true,
            new[] { "s", "i", "i" }, returnTypeName: "System.Char[]");
    return null;
// net9/10 E2E campaign: CopyTo(srcIndex, dest, destIndex, count).
// INSTANCE method (callvirt call sites -- icallclassic caught the missing
// IsInstance flag: StackFold rejects callvirt on a non-instance resolution).
case "CopyTo":
    return ps.Length == 4 && ps[1] == 0x1D
        ? I("il2cpp::runtime::icall_string_copy_to_array", 4, false, false, true,
            new[] { "s", "i", "a", "i", "i" })
        : null;
// net9/10 E2E campaign: ReplaceLineEndings(string) -- CRLF/CR/LF normalize.
// The 0-arg overload (Environment.NewLine injection) is not expressible
// here; it stays LOUD pending a newline-fetching wrapper.
case "ReplaceLineEndings":
    return ps.Length == 1 && ps[0] == 0x0E
        ? I("il2cpp::runtime::icall_string_replace_line_endings", 1, true, true, true,
            new[] { "s", "s" }, returnTypeName: "System.String")
        : null;
            case "Trim":
                // Trim(char[] set) -- the SET-AWARE both-ends trim
                // (icall_string_trim_set, strgen1): strip every leading AND
                // trailing unit in the set (a null/empty set falls back to the
                // whitespace trim, the net48 params-array semantics). ParamCount
                // = 1 tracks the IL arity (this + array) so the fold's shadow
                // stack stays in lockstep. The net9 single-char 0x03 shape
                // keeps the whitespace-only behavior (the runtime trim is
                // whitespace-only there -- documented deviation).
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_string_trim_set", 1, true, true, true,
                        new[] { "", "" });
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_string_trim", 1, true, true, true, new[] { "", "drop" });
                return I("il2cpp::runtime::icall_string_trim", 0, true, true, true);
            case "TrimStart":
                // TrimStart(params char[]): the SET-aware icall
                // (icall_string_trim_start_set) strips every leading unit in
                // the set -- the old whitespace-only deviation is retired.
                // ParamCount = 1 keeps the fold's shadow stack in lockstep
                // with the IL (this + char[] pushed).
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_string_trim_start_set", 1, true, true, true, new[] { "", "" })
                    : I("il2cpp::runtime::icall_string_trim_start", 0, true, true, true);
            case "TrimEnd":
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_string_trim_end_set", 1, true, true, true, new[] { "", "" })
                    : I("il2cpp::runtime::icall_string_trim_end", 0, true, true, true);
            case "PadLeft":
            case "PadRight":
                // #32 (strdeep): the net48 pad family (il2cpp_string_ext3.h).
                // Pad(int totalWidth) and Pad(int totalWidth, char
                // paddingChar) route to the 2-arg / 3-arg runtime icalls
                // (ps dispatch: 1 param -> (this, width), 2 params ->
                // (this, width, padChar); the char slot needs the "c"
                // char16_t cast, the width the "i" int32 cast). The runtime
                // returns the ORIGINAL string when totalWidth < Length
                // (net48-verified) and allocates a new padded string
                // otherwise -- the result is a managed string ref
                // (ReturnTypeName "System.String", the Join/Copy/Format
                // convention).
                if (ps.Length == 1)
                    return I(name == "PadLeft" ? "il2cpp::runtime::icall_string_pad_left" : "il2cpp::runtime::icall_string_pad_right",
                        1, true, true, true, new[] { "", "i" }, null, false, false, false, "System.String");
                if (ps.Length == 2)
                    return I(name == "PadLeft" ? "il2cpp::runtime::icall_string_pad_left" : "il2cpp::runtime::icall_string_pad_right",
                        2, true, true, true, new[] { "", "i", "c" }, null, false, false, false, "System.String");
                throw new NotSupportedException($"String.{name} with {ps.Length} arg(s) not wired in M5 (the runtime pad icalls take (string, int32[, char16_t])) -- LOUD whitelist (net48 PadLeft/PadRight has only (int) and (int,char)); net48 RefSrc mscorlib/system/string.cs:2530-2545");
            case "CompareTo": return I("il2cpp::runtime::icall_string_compare_to", 1, true, false, true);
            case "Contains":
                // The wired icall takes the VALUE as Il2CppObject* (a string).
                // net9/10 E2E campaign: Contains(char) (ps[0] == 0x03) now has
                // the dedicated icall_string_contains_char ordinal scan.
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_string_contains", 1, true, false, true);
                if (ps.Length == 1 && ps[0] == 0x03)
                    return I("il2cpp::runtime::icall_string_contains_char", 1, true, false, true,
                        new[] { "s", "c" });
                throw new NotSupportedException($"String.Contains with {ps.Length} arg(s) not wired -- LOUD whitelist (net48 Contains has only (string); the char/StringComparison forms are netcore-only); net48 RefSrc mscorlib/system/string.cs:2172");
            case "GetHashCode":
                // GetHashCode(StringComparison) (.NET Core 2.0+/net9) has NO
                // mode-aware hash icall -- and mapping it onto the 0-param
                // primitive would pop one value short of the IL arity (the
                // TrimStart lockstep discipline). Loud until an icall exists.
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_string_get_hash_code", 0, true, false, true);
                // GetHashCode(StringComparison) -- the net core 2.0+ overload
                // (net48 has no such member). The enum crosses as its int32
                // value; the runtime keeps the ordinal hash for Ordinal and
                // folds ASCII for OrdinalIgnoreCase (the culture modes
                // degrade to the ordinal hash, the documented deviation).
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_string_get_hash_code_mode", 1, true, false, true,
                        new[] { "", "i" });
                throw new NotSupportedException($"String.GetHashCode with {ps.Length} arg(s) not wired (needs a comparison-mode-aware runtime hash icall) -- LOUD whitelist (net48 GetHashCode has only the parameterless form; the StringComparison overload is netcore-only); net48 RefSrc mscorlib/system/string.cs:833");
            // System.String.Normalize / IsNormalized (stringnormalize.cpp --
            // the full UAX #15 implementation). The form crosses as the raw
            // System.Text.NormalizationForm value (FormC=1, FormD=2, FormKC=5,
            // FormKD=6 -- net48 oracle-verified); the parameterless forms
            // inject the NFC default (1) as a trailing literal
            // (AppendLiterals) onto the SAME runtime icall. A form outside
            // that set throws System.ArgumentException at runtime; a null
            // receiver throws NullReferenceException (checked first).
            case "Normalize":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_string_normalize", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.String", appendLiterals: new[] { "1" });
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_string_normalize", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.String");
                throw new NotSupportedException($"String.Normalize with {ps.Length} arg(s) not wired (the runtime icall takes (string[, NormalizationForm])) -- LOUD whitelist (net48 Normalize has only () and (NormalizationForm)); net48 RefSrc mscorlib/system/string.cs:1503-1515");
            case "IsNormalized":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_string_is_normalized", 0, true, false, true,
                        new[] { "" }, appendLiterals: new[] { "1" });
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_string_is_normalized", 1, true, false, true,
                        new[] { "", "i" });
                throw new NotSupportedException($"String.IsNormalized with {ps.Length} arg(s) not wired (the runtime icall takes (string[, NormalizationForm])) -- LOUD whitelist (net48 IsNormalized has only () and (NormalizationForm)); net48 RefSrc mscorlib/system/string.cs:1475-1487");
            case "ToString":
                // String.ToString() is the identity (net48 semantics) -- the
                // fold pushes the receiver. ToString(IFormatProvider) is ALSO
                // the identity (a string is culture-invariant; the provider is
                // ignored) -- the identity fold drops the trailing argument
                // (the fold pushes the receiver and discards the rest), so the
                // ParamCount tracks the IL arity for the receiver pop.
                if (ps.Length == 0)
                    return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true);
                if (ps.Length == 1 && ps[0] is 0x12 or 0x1C)
                    return new ResolvedMethod(ResolvedMethod.IdentityMarker, 1, false, true, true, new[] { false }, true);
                return null;
            // net9/10 E2E campaign: String.Clone() -- .NET returns the SAME
            // instance (strings are immutable), so it folds to the identity
            // exactly like ToString(). No icall needed.
            case "Clone":
                return ps.Length == 0
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true)
                    : null;
            // M5 wiring: the stringext2 STATIC domain (il2cpp_string_ext2.h).
            case "Compare":
            case "CompareOrdinal":
                // CompareOrdinal(a, b) -> the ordinal static icall;
                // Compare(a, b) -> ordinal (documented deviation: the net48
                // default overload is culture-sensitive);
                // Compare(a, b, StringComparison) -> the comparison arg is
                // dropped at emission (the runtime is ordinal-only). NOTE:
                // ParamCount = the IL arity -- the FOLD pops that many args.
                // Compare(a, b, bool ignoreCase) (ps[2] == 0x02) CANNOT be
                // wired by dropping -- ignoreCase=true must FOLD case. It now
                // routes to the MODE-AWARE static comparator below, whose
                // int32 'mode' parameter carries the flag (0/1).
                if (ps.Length == 2)
                    return I("il2cpp::runtime::icall_string_compare_static", 2, true, false, false);
                if (ps.Length == 3 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_string_compare_mode", 3, true, false, false,
                        new[] { "", "", "i" });
                // strgen1: Compare(string, string, bool ignoreCase) -- the
                // MODE-AWARE static comparator already folds the flag onto
                // its int32 'mode' parameter: false -> mode 0 (ordinal,
                // case-sensitive), true -> mode 1 (ordinal ignore-case).
                // This is exactly net48's bool overload contract (the
                // culture forms of the enum degrade to the ordinal fold, the
                // same documented ASCII-domain deviation); the bool value is
                // an int32 slot (0/1), so the "i" cast renders the mode
                // directly. ParamCount = the IL arity (3) so the fold pops the
                // flag -- dropping it would silently ignore ignoreCase=true.
                if (ps.Length == 3 && ps[2] == 0x02)
                    return I("il2cpp::runtime::icall_string_compare_mode", 3, true, false, false,
                        new[] { "", "", "i" });
                // String.CompareOrdinal(string, int, string, int, int) -- the
                // ORDINAL range form. ps = (string, int32, string, int32,
                // int32); the runtime clamps the length and treats null as
                // empty (net48 probe-pinned).
                if (name == "CompareOrdinal" && ps.Length == 5
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08)
                    return I("il2cpp::runtime::icall_string_compare_ordinal_range", 5, true, false, false,
                        new[] { "", "i", "", "i", "i" });
                // String.Compare(string, int, string, int, int) -- the
                // CULTURE range form. The runtime validates null/window the
                // net48 way and compares through the documented ordinal fold.
                if (name == "Compare" && ps.Length == 5
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08)
                    return I("il2cpp::runtime::icall_string_compare_range", 5, true, false, false,
                        new[] { "", "i", "", "i", "i" });
                // String.Compare(..., bool ignoreCase) -- the CULTURE range
                // form with the ignore-case flag (the same net48 null/window
                // validation; the fold is ordinal, the documented deviation).
                if (name == "Compare" && ps.Length == 6
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x02)
                    return I("il2cpp::runtime::icall_string_compare_range_bool", 6, true, false, false,
                        new[] { "", "i", "", "i", "i", "b" });
                // String.Compare(..., StringComparison) -- the MODE-AWARE
                // range form (null treated as empty; length/index AOORE with
                // the "length"/"indexA"/"indexB" names).
                if (name == "Compare" && ps.Length == 6
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x11)
                    return I("il2cpp::runtime::icall_string_compare_range_sc", 6, true, false, false,
                        new[] { "", "i", "", "i", "i", "i" });
                // String.Compare(..., bool ignoreCase, CultureInfo): the
                // CultureInfo is dropped (culture collation is not modelled);
                // the flag carries the ordinal ignore-case mode.
                if (name == "Compare" && ps.Length == 7
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x02
                    && ps[6] == 0x12)
                    return I("il2cpp::runtime::icall_string_compare_range_bool", 7, true, false, false,
                        new[] { "", "i", "", "i", "i", "b", "drop" });
                // String.Compare(..., CultureInfo, CompareOptions): the
                // CultureInfo is dropped and the options fold to the ordinal
                // /ignore-case modes.
                if (name == "Compare" && ps.Length == 7
                    && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x0E
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x12
                    && ps[6] == 0x11)
                    return I("il2cpp::runtime::icall_string_compare_range_options", 7, true, false, false,
                        new[] { "", "i", "", "i", "i", "drop", "i" });
                // String.Compare(string, string, bool ignoreCase,
                // CultureInfo): the CultureInfo is dropped; the bool rides
                // the mode slot (false -> 0 CurrentCulture, true -> 1
                // CurrentCultureIgnoreCase -- the compare_mode fold).
                if (name == "Compare" && ps.Length == 4
                    && ps[0] == 0x0E && ps[1] == 0x0E && ps[2] == 0x02 && ps[3] == 0x12)
                    return I("il2cpp::runtime::icall_string_compare_mode", 4, true, false, false,
                        new[] { "", "", "i", "drop" });
                // String.Compare(string, string, CultureInfo,
                // CompareOptions): the CultureInfo is dropped; the options
                // select the ordinal/ignore-case fold.
                if (name == "Compare" && ps.Length == 4
                    && ps[0] == 0x0E && ps[1] == 0x0E && ps[2] == 0x12 && ps[3] == 0x11)
                    return I("il2cpp::runtime::icall_string_compare_options", 4, true, false, false,
                        new[] { "", "", "drop", "i" });
                throw new NotSupportedException($"String.{name} with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines no Compare/CompareOrdinal overload of this arity/param shape); net48 RefSrc mscorlib/system/string.cs:1775-2009,2129-2158");
            case "Equals":
                // Static Equals(a, b) and instance Equals(obj/string) both
                // reduce to the ordinal static comparator (null-aware).
                if (ps.Length == 1) return I("il2cpp::runtime::icall_string_equals_static", 1, true, false, true);
                if (ps.Length == 2)
                    return ps[1] switch
                    {
                        // static Equals(string, string): both args kept.
                        0x0E => I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false),
                        // instance Equals(string, StringComparison): the IL
                        // pushes [this, string, StringComparison] (3 values;
                        // Roslyn devirtualizes literal receivers to plain
                        // `call`), so the mapping must be INSTANCE (the fold
                        // pops this on plain `call` -- StackFold gates the
                        // receiver pop on IsInstance). The comparisonType
                        // crosses as int32_t to the MODE-AWARE runtime
                        // primitive (icall_string_equals_culture,
                        // il2cpp_string_ext2.h:96 -- the culture modes fold
                        // to ordinal, the ignore-case modes fold ASCII; a
                        // mode outside 0..5 throws ArgumentException; a null
                        // 'value' returns false -- the net48 comparison-form
                        // contract, netcore String.Comparison.cs:
                        // "if (value is null) { CheckStringComparison();
                        // return false; }"). The OLD mapping dropped the mode
                        // (ordinal-only equals_static) and broke every
                        // OrdinalIgnoreCase/CurrentCulture assertion in the
                        // strcompare e2e. 0x11 = VALUETYPE (the enum's
                        // signature encoding; RawSignature consumes the
                        // trailing TypeHandle).
                        0x11 => I("il2cpp::runtime::icall_string_equals_culture", 2, true, false, true, new[] { "", "", "i" }),
                        _ => throw new NotSupportedException($"String.Equals 2-arg param type 0x{ps[1]:X2} not wired in M5 -- LOUD whitelist (net48 Equals has only the static (string,string) and the instance (string)/(object) forms); net48 RefSrc mscorlib/system/string.cs:638-655"),
                    };
                // dscmp: STATIC Equals(string a, string b, StringComparison)
                // -- no receiver; the mode-aware runtime primitive takes
                // (this_=a, value=b, mode) and folds the culture modes to
                // ordinal (il2cpp_string_ext2.h:96). ParamCount = IL arity 3;
                // the 0x11 enum signature carries the trailing TypeHandle.
                if (ps.Length == 3 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_string_equals_culture", 3, true, false, false, new[] { "", "", "i" });
                if (ps.Length == 3 && ps[2] == 0x02)
                    throw new NotSupportedException($"String.{name}(string, string, bool ignoreCase) not wired (no mode-aware static equals icall; dropping the flag would silently ignore ignoreCase=true) -- LOUD whitelist (net48 Equals has no (string,string,bool) overload; netcore uses StringComparison, which is wired); net48 RefSrc mscorlib/system/string.cs:638-655");
                throw new NotSupportedException($"String.Equals with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines no Equals overload of this arity/param shape); net48 RefSrc mscorlib/system/string.cs:638-655");
            case "op_Equality":
                // string == string: ORDINAL CONTENT equality (the runtime's
                // static comparator is null-aware and content-based).
                if (ps.Length == 2) return I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"String.op_Equality with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines op_Equality only as (string,string)); net48 RefSrc mscorlib/system/string.cs:705");
            case "op_Inequality":
                // string != string: the runtime has no inequality icall, so
                // the mapping emits the ORDINAL EQUALITY icall under a C++
                // logical-NOT. The emitter puts CppName in front of the
                // argument list, and in C++ postfix (call) binds tighter than
                // the unary ! -- "!f(a, b)" parses as "!(f(a, b))". The
                // equality icall is already null-aware, so !(eq(a, b)) is
                // exactly the net48 != contract (null != null -> false,
                // exactly one null -> true, content difference -> true).
                // NOTE: the leading "!" makes CppName not a bare symbol --
                // the fold's il2cpp:: classification gates (IsRuntimeDirect /
                // the callvirt re-resolve) only inspect CALLVIRT receivers,
                // and op_* are always static calls, so the prefix is safe.
                if (ps.Length == 2) return I("!il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"String.op_Inequality with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines op_Inequality only as (string,string)); net48 RefSrc mscorlib/system/string.cs:709");
            case "Format":
                // String.Format overloads (net48 ps dispatch): the 1-arg
                // format-only form, the 2/3/4-arg (string, object[, ...])
                // forms, and the params object[] form. All route to the
                // stringbuilder.cpp composite-format kernel
                // (icall_string_format_0/1/2/3 for the fixed arities,
                // icall_string_format for the params array). The returned
                // string is a managed reference like Join/Copy.
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_string_format_0", 1, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length >= 2 && (ps[0] == 0x12 || ps[0] == 0x1C) && ps[1] == 0x0E)
                {
                    // Format(IFormatProvider, string[, arg...]): the provider
                    // arm -- the mscorlib IFormatProvider INTERFACE param
                    // decodes to element byte 0x12 (CLASS type handle);
                    // a boxed-object provider encodes 0x1C. Guarded on the
                    // second slot being the format STRING (0x0E) so no other
                    // Format overload matches. The provider slot passes
                    // through VERBATIM as the leading parameter of the
                    // *_provider runtime wrapper (invariant culture: it is
                    // ignored). Keeps the fold slot order 1:1 -- a positional
                    // "drop" would shift the format into slot 0 and corrupt
                    // the call.
                    var tail = Enumerable.Repeat("", ps.Length - 1).ToArray();
                    var casts = new[] { "" }.Concat(tail).ToArray();
                    if (ps.Length == 3 && ps[2] == 0x1D)
                        return I("il2cpp::runtime::icall_string_format_provider", ps.Length, true, true, false, casts, null, false, false, false, "System.String");
                    return I($"il2cpp::runtime::icall_string_format_{ps.Length - 2}_provider", ps.Length, true, true, false, casts, null, false, false, false, "System.String");
                }
                if (ps.Length == 2 && ps[1] == 0x1C)
                    return I("il2cpp::runtime::icall_string_format_1", 2, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 2 && ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_string_format", 2, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_string_format_2", 3, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 4)
                    return I("il2cpp::runtime::icall_string_format_3", 4, true, true, false, null, null, false, false, false, "System.String");
                throw new NotSupportedException($"String.Format with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines no Format overload of this arity/param shape); net48 RefSrc mscorlib/system/string.cs:2960-3003");
            case "Concat":
                // M5 wiring: Concat(object, object) numeric conveniences
                // (il2cpp_string_ext2.h) -- the LAST stack value (arg0Type)
                // is the boxed NUMBER when the first argument is the leading
                // string segment (IL pushes arg0 first, so the fold's peek
                // sees argN).
                if (ps.Length == 2 && ps[0] == 0x1C && ps[1] == 0x1C)
                {
                    var bothNumbers = arg1Type is "System.Int32" or "System.Int64" or "System.Double"
                        or "System.Single" or "System.Boolean" or "System.Byte" or "System.Char";
                    if (bothNumbers)
                    {
                        // strgen1: two boxed numerics -- the variadic
                        // concat_many formats each boxed value via
                        // icall_object_to_string (net48 Concat(object, object)
                        // of two numbers is the plain concatenation of their
                        // ToString text, e.g. (object)1 + (object)2 -> "12").
                        return I("il2cpp::runtime::il2cpp_string_concat_many", 2, true, true, false,
                            null, null, prependCount: true);
                    }
                    return arg0Type switch
                    {
                        "System.Int32" => I("il2cpp::runtime::icall_string_concat_int", 2, true, true, false, new[] { "", "unbox:i" }),
                        "System.Double" or "System.Single" => I("il2cpp::runtime::icall_string_concat_double", 2, true, true, false, new[] { "", "unbox:d" }),
                        "System.Boolean" => I("il2cpp::runtime::icall_string_concat_bool", 2, true, true, false, new[] { "", "unbox:b" }),
                        "System.String" => I("il2cpp::runtime::icall_string_concat_2", 2, true, true, false),
                        _ => throw new NotSupportedException($"String.Concat(object, object) with untracked last argument (box type {arg0Type ?? "unknown"}) not wired in M5 -- LOUD whitelist (the boxed last argument's runtime type is not tracked -- dynamic/object dispatch); net48 RefSrc mscorlib/system/string.cs:3045-3307"),
                    };
                }
                if (ps.Length == 2 || ps.Length == 3 || ps.Length == 4)
                {
                    // perf1 fast path: ALL-STRING segments -> the fixed-arity
                    // icalls (icall_string_concat_2/3/4 -> ConcatFixed in
                    // stringext2.cpp: ONE managed allocation + ONE scratch,
                    // single payload pass -- no variadic re-walk, no
                    // per-segment class-name strcmp like concat_many).
                    // This is the shape the net48 compiler emits for
                    // `"a" + b + c` after its ToString/string folding
                    // (bench-array str_concat measures Concat(string,string)).
                    // Mixed / non-string params keep the variadic
                    // il2cpp_string_concat_many (object conversion path).
                    var allStrings = true;
                    foreach (var p in ps)
                        if (p != 0x0E) { allStrings = false; break; }
                    if (allStrings)
                        return I($"il2cpp::runtime::icall_string_concat_{ps.Length}", ps.Length, true, true, false);
                    return I("il2cpp::runtime::il2cpp_string_concat_many", ps.Length, true, true, false,
                        null, null, prependCount: true);
                }
                // Concat(object): null -> String.Empty; a string is the
                // identity; any other object renders through the net48
                // Object.ToString dispatch.
                if (ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_string_concat_object", 1, true, true, false,
                        null, null, false, false, false, "System.String");
                // Concat<T>(IEnumerable<T>) / Concat(IEnumerable<string>):
                // the source is enumerated through the runtime IEnumerator
                // dispatch. The element carrier is a compile-time fact:
                //   genArgs null -> IEnumerable<string> (reference elements)
                //   genArgs[0] -> the closed T of Concat<T> (the same byte
                //   vocabulary as the AppendFormatted value families).
                // The literal appended after the source is the elemKind the
                // runtime switch consumes.
                if (ps.Length == 1 && ps[0] == 0x15)
                {
                    var elemKind = "0"; // IEnumerable<string>
                    if (genArgs is { Length: > 0 })
                    {
                        elemKind = genArgs[0] switch
                        {
                            0x02 => "6", // bool
                            0x03 => "7", // char
                            0x04 or 0x05 or 0x06 or 0x07 => "1", // byte/sbyte/short/ushort
                            0x08 => "1", // int32
                            0x09 => "2", // uint32
                            0x0A => "3", // int64
                            0x0B => "4", // uint64
                            0x0C or 0x0D => "5", // float/double
                            0x11 => "8", // decimal / value type (decimal carrier)
                            _ => "0", // reference element (string/object/class)
                        };
                    }
                    return I("il2cpp::runtime::icall_string_concat_enumerable", 1, true, true, false,
                        new[] { "" }, null, false, false, false, "System.String",
                        appendLiterals: new[] { elemKind });
                }
                if (ps.Length == 1 && ps[0] is 0x1D or 0x1F)
                    return I("il2cpp::runtime::il2cpp_string_concat_array", 1, true, true, false);
                throw new NotSupportedException($"String.Concat with {ps.Length} arg(s) not wired in M5 -- LOUD whitelist (net48 defines no Concat overload of this arity/param shape); net48 RefSrc mscorlib/system/string.cs:3045-3307");
            case "Split":
                // Split(char[] separator): split this string on every
                // occurrence of any character in 'separator' (default
                // StringSplitOptions.None -- EMPTY segments retained, the
                // net48 contract; a null or EMPTY array splits on whitespace,
                // the full Char.IsWhiteSpace set). Wired to the runtime
                // icall_string_split (il2cpp_string_ext.h) whose declaration
                // takes (Il2CppString* self, Il2CppArray* separators): the
                // receiver crosses with the "s" cast (Il2CppString*), the
                // char[] SZARRAY parameter with the "a" cast (Il2CppArray*).
                // The result is a string[] reference (ReturnTypeName drives
                // downstream get_Length / indexer resolution).
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_string_split", 1, true, true, true,
                        new[] { "s", "a" }, returnTypeName: "System.String[]");
                // Split(char) -- the netcore21+ single-char overload (the
                // runtime wrapper builds the 1-element separator array).
                if (ps.Length == 1 && ps[0] == 0x03)
                    return I("il2cpp::runtime::icall_string_split_char", 1, true, true, true,
                        new[] { "s", "c" }, returnTypeName: "System.String[]");
                // Split(char, StringSplitOptions) -- netcore21 binds s.Split(' ')
                // to THIS shape (options defaults at the call site, the IL
                // carries both args). Same wrapper family as the 1-arg form.
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_split_char_options", 2, true, true, true,
                        new[] { "s", "c", "" }, returnTypeName: "System.String[]");
                // 2-arg options overload: Split(char[], StringSplitOptions) /
                // Split(string[], StringSplitOptions) (the RT-SPL2 / strcomp2
                // wiring). BOTH SZARRAY shapes (ps[0] == 0x1D; the element
                // byte was consumed by the signature walk) + the
                // StringSplitOptions enum (ps[1] == 0x11) map to the ONE
                // runtime icall_string_split_options (il2cpp_string_ext.h):
                // (Il2CppString* self, Il2CppArray* separators, int32_t
                // options) -- the receiver crosses with the "s" cast, the
                // array with the "a" cast, the options value with the "" raw
                // int32 passthrough. The runtime disambiguates char[] vs
                // string[] separators by the array's isRefElem flag (net48
                // options semantics: None retains empty segments,
                // RemoveEmptyEntries drops them, invalid options throw
                // ArgumentException).
                if (ps.Length == 2 && ps[0] is 0x1D or 0x1F && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_split_options", 2, true, true, true,
                        new[] { "s", "a", "" }, returnTypeName: "System.String[]");
                // strgen1: Split(char[]/string[], int count) /
                // (char[]/string[], int count, StringSplitOptions) -- the
                // count-capped forms (the runtime caps the split at 'count'
                // substrings; the first count-1 separators split, the remainder
                // stays in the last segment). Same isRefElem disambiguation
                // and whitespace fallback as the options form.
                if (ps.Length == 2 && ps[0] is 0x1D or 0x1F && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_string_split_count", 2, true, true, true,
                        new[] { "s", "a", "i" }, returnTypeName: "System.String[]");
                if (ps.Length == 3 && ps[0] is 0x1D or 0x1F && ps[1] == 0x08 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_string_split_count_options", 3, true, true, true,
                        new[] { "s", "a", "i", "" }, returnTypeName: "System.String[]");
                return null;
            case "Join":
                // string.Join(string separator, string[] values) -- the
                // runtime join (il2cpp_string_ext2.h). NOTE: the runtime is
                // defensive (null array -> empty string) while net48 throws
                // ArgumentNullException -- documented deviation; the string[]
                // overload is the only wired shape.
                // randomcase: string.Join(",", byte[]) selects the GENERIC
                // overload Join<byte>(string, IEnumerable<byte>) -- the
                // signature's IEnumerable<T> parameter surfaces as
                // GENERICINST (0x15) after the RawSignature fix consumes the
                // GenericParameterCount. The runtime icall takes
                // (string, array) and converts non-string elements via
                // icall_object_to_string (JoinFromArray), so the generic
                // shape shares the string[] mapping; the element/array
                // semantics live in the runtime.
                //
                // b5-gaps section 99: a GROUPING source (one GroupBy bucket,
                // arg1Type System.Linq.IGrouping...) is an OBJECT, not an
                // array -- the array icall casts it to Il2CppArray* and answers
                // an empty join (probe-s100r4: oracle [r4] 3,3 vs runtime
                // empty). Route a grouping source to the IEnumerable overload.
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] is 0x1D or 0x15
                    && arg1Type != null
                    && (arg1Type == "System.Linq.IGrouping"
                        || arg1Type.StartsWith("System.Linq.IGrouping`2", StringComparison.Ordinal)
                        || arg1Type.StartsWith("System.Linq.IGrouping_key_", StringComparison.Ordinal)
                        || arg1Type == "System.Linq.IGroupingResult"
                        || arg1Type == "System.Collections.Generic.IEnumerable`1"))
                    return I("il2cpp::runtime::icall_string_join_ienumerable", 2, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] is 0x1D or 0x15)
                    return I("il2cpp::runtime::icall_string_join", 2, true, true, false, null, null, false, false, false, "System.String");
                // joinchar sweep: Join(char separator, string[]/object[]
                // values) (.NET Core 2.0+/net9, ps = (0x03, 0x1D)) ->
                // il2cpp_string_builder_join (il2cpp_concat.h): the same
                // ConcatFromArray pipeline as concat_array/join -- string
                // elements join directly, NON-string elements convert via
                // icall_object_to_string (the object[] semantics), null
                // elements count as empty. All SZARRAY shapes surface as
                // ps[1] == 0x1D and cross as reference arrays here. The
                // GENERIC IEnumerable<T> shape (ps[1] == 0x15) stays UNWIRED:
                // a value-typed array argument (byte[] etc.) is not a
                // reference array and ConcatFromArray would return ""
                // defensively instead of joining.
                if (ps.Length == 2 && ps[0] == 0x03 && ps[1] == 0x1D)
                    return I("il2cpp::runtime::il2cpp_string_builder_join", 2, true, true, false,
                        new[] { "c", "" }, null, false, false, false, "System.String");
                // net9/10 E2E campaign: the start/count forms -- slice then
                // join (stringext.cpp wrappers).
                if (ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_string_join_range_s", 4, true, true, false,
                        new[] { "s", "a", "i", "i" }, null, false, false, false, "System.String");
                if (ps.Length == 4 && ps[0] == 0x03 && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_string_join_range_c", 4, true, true, false,
                        new[] { "c", "a", "i", "i" }, null, false, false, false, "System.String");
                throw new NotSupportedException($"String.Join with {ps.Length} arg(s) not wired (the runtime joins take (string, string[]) and (char, string[]); the start/count forms need a count-aware runtime icall) -- LOUD whitelist (net48 Join has only (string,string[]), (string,object[]), (string,IEnumerable<string>) and Join<T>(string,IEnumerable<T>)); net48 RefSrc mscorlib/system/string.cs:84-161");
            case "IsNullOrEmpty":
                return I("il2cpp::runtime::icall_string_is_null_or_empty", 1, true, false, false);
            case "IsNullOrWhiteSpace":
                return I("il2cpp::runtime::icall_string_is_null_or_whitespace", 1, true, false, false);
            case "Copy":
                return I("il2cpp::runtime::icall_string_copy", 1, true, true, false, null, null, false, false, false, "System.String");
            case "get_Chars":
                // the string indexer: s[i] -- instance, receiver + int index.
                return I("il2cpp::runtime::icall_string_get_chars", 1, true, false, true);
            case "Intern":
                return I("il2cpp::il2cpp_string_intern", 1, true, true, false);
            // String.IsInterned: check if a string is in the intern pool
            // WITHOUT adding it. Returns the interned string if found, null if not.
            case "IsInterned":
                return I("il2cpp::il2cpp_string_is_interned", 1, true, true, false);
            // P6: String.FastAllocateString(int32) -- internal string allocator.
            case "FastAllocateString":
                return I("il2cpp::runtime::icall_string_fast_allocate", 1, true, true, false,
                    new[] { "", "i" }, returnTypeName: "System.String");
            // P6: String.SetTrailByte(byte) -- sets the last byte of the
            // string's internal UTF-16 buffer (encoding metadata).
            case "SetTrailByte":
                return I("il2cpp::runtime::icall_string_set_trail_byte", 1, false, false, false,
                    new[] { "", "b" });
            // P6: String.TryGetTrailByte(out byte) -- reads the last byte.
            case "TryGetTrailByte":
                return I("il2cpp::runtime::icall_string_try_get_trail_byte", 1, true, false, false,
                    new[] { "", "B:u1" });
            // P6: remaining String internal methods.
            case "nativeCompareOrdinalEx": return I("il2cpp::runtime::icall_string_native_compare_ordinal_ex", 6, true, false, false, new[] { "", "", "i", "i", "i", "i" });
            case "nativeCompareOrdinalIgnoreCaseWC": return I("il2cpp::runtime::icall_string_native_compare_ordinal_ignore_case_wc", 2, true, false, false, new[] { "", "" });
            case "InternalMarvin32HashString": return I("il2cpp::runtime::icall_string_internal_marvin32_hash_string", 1, true, false, false, new[] { "" });
            case "IsFastSort": return I("il2cpp::runtime::icall_string_is_fast_sort", 0, true, true, false);
            case "IsAscii": return I("il2cpp::runtime::icall_string_is_ascii", 0, true, false, false);
            default: return null;
        }
    }
}
