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
                throw new NotSupportedException($"String ctor with {ps.Length} arg(s) not wired (the runtime icalls cover (char, int32), (char[]), (char[], int32, int32); the pointer-typed char*/sbyte* ctors need transpiler PTR-signature support)");
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
                    _ => throw new NotSupportedException($"String.IndexOf param type 0x{ps[0]:X2} not wired in M5"),
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
                throw new NotSupportedException($"String.IndexOfAny with {ps.Length} arg(s) not wired (the runtime icall takes (char[], int32, int32))");
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
                throw new NotSupportedException($"String.LastIndexOfAny with {ps.Length} arg(s) not wired (the runtime icall takes (char[], int32, int32))");
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
                throw new NotSupportedException($"String.Replace with {ps.Length} arg(s) not wired in M5 (the runtime icall_string_replace takes (char, char); the string/string overload needs a runtime icall_string_replace_s_s)");
            case "ToUpper": return I("il2cpp::runtime::icall_string_to_upper", 0, true, true, true);
            case "ToLower": return I("il2cpp::runtime::icall_string_to_lower", 0, true, true, true);
            case "Trim": return I("il2cpp::runtime::icall_string_trim", 0, true, true, true);
            case "TrimStart":
                // net48 has NO 0-arg TrimStart -- the only overload is
                // TrimStart(params char[]); Roslyn (optimize) lowers a
                // no-arg call to TrimStart(Array.Empty<char>()). The char[]
                // argument is the trim set: the runtime icall is
                // whitespace-only, so a NON-empty trim set would be ignored
                // (documented deviation); the empty-array shape the e2e
                // cases reach (Array.Empty<char>) is exact. ParamCount = 1
                // keeps the fold's shadow stack in lockstep with the IL
                // (this + char[] pushed); the array is dropped at emission.
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_string_trim_start", 1, true, true, true, new[] { "", "drop" })
                    : I("il2cpp::runtime::icall_string_trim_start", 0, true, true, true);
            case "TrimEnd":
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_string_trim_end", 1, true, true, true, new[] { "", "drop" })
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
                throw new NotSupportedException($"String.{name} with {ps.Length} arg(s) not wired in M5 (the runtime pad icalls take (string, int32[, char16_t]))");
            case "CompareTo": return I("il2cpp::runtime::icall_string_compare_to", 1, true, false, true);
            case "Contains": return I("il2cpp::runtime::icall_string_contains", 1, true, false, true);
            case "GetHashCode": return I("il2cpp::runtime::icall_string_get_hash_code", 0, true, false, true);
            case "ToString":
                // String.ToString() is the identity (net48 semantics) -- the
                // fold pushes the receiver.
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
                return ps.Length is 2 or 3
                    ? I("il2cpp::runtime::icall_string_compare_static", ps.Length, true, false, false,
                        ps.Length == 3 ? new[] { "", "", "drop" } : null)
                    : throw new NotSupportedException($"String.{name} with {ps.Length} arg(s) not wired in M5");
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
                        _ => throw new NotSupportedException($"String.Equals 2-arg param type 0x{ps[1]:X2} not wired in M5"),
                    };
                throw new NotSupportedException($"String.Equals with {ps.Length} arg(s) not wired in M5");
            case "op_Equality":
                // string == string: ORDINAL CONTENT equality (the runtime's
                // static comparator is null-aware and content-based).
                if (ps.Length == 2) return I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"String.op_Equality with {ps.Length} arg(s) not wired in M5");
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
                throw new NotSupportedException($"String.op_Inequality with {ps.Length} arg(s) not wired in M5");
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
                if (ps.Length == 2 && ps[1] == 0x1C)
                    return I("il2cpp::runtime::icall_string_format_1", 2, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 2 && ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_string_format", 2, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_string_format_2", 3, true, true, false, null, null, false, false, false, "System.String");
                if (ps.Length == 4)
                    return I("il2cpp::runtime::icall_string_format_3", 4, true, true, false, null, null, false, false, false, "System.String");
                throw new NotSupportedException($"String.Format with {ps.Length} arg(s) not wired in M5");
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
                        throw new NotSupportedException($"String.Concat(object, object) with two numeric arguments not wired in M5 (the runtime convenience appends a number to a string segment)");
                    return arg0Type switch
                    {
                        "System.Int32" => I("il2cpp::runtime::icall_string_concat_int", 2, true, true, false, new[] { "", "unbox:i" }),
                        "System.Double" or "System.Single" => I("il2cpp::runtime::icall_string_concat_double", 2, true, true, false, new[] { "", "unbox:d" }),
                        "System.Boolean" => I("il2cpp::runtime::icall_string_concat_bool", 2, true, true, false, new[] { "", "unbox:b" }),
                        "System.String" => I("il2cpp::runtime::icall_string_concat_2", 2, true, true, false),
                        _ => throw new NotSupportedException($"String.Concat(object, object) with untracked last argument (box type {arg0Type ?? "unknown"}) not wired in M5"),
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
                if (ps.Length == 1 && ps[0] is 0x1D or 0x1F)
                    return I("il2cpp::runtime::il2cpp_string_concat_array", 1, true, true, false);
                throw new NotSupportedException($"String.Concat with {ps.Length} arg(s) not wired in M5");
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
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] is 0x1D or 0x15)
                    return I("il2cpp::runtime::icall_string_join", 2, true, true, false, null, null, false, false, false, "System.String");
                throw new NotSupportedException($"String.Join with {ps.Length} arg(s) not wired in M5 (the runtime join takes (string, string[]))");
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
