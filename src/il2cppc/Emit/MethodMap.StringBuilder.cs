namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StringBuilder -- System.Text.StringBuilder icall mappings
// (il2cpp_stringbuilder.h).
// Owners: System.Text.StringBuilder
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStringBuilder(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0) return I("il2cpp::runtime::icall_stringbuilder_ctor_default", 0, false, false, true);
                if (count == 1 && ps[0] == 0x08) return I("il2cpp::runtime::icall_stringbuilder_ctor_capacity", 1, false, false, true, new[] { "", "i" });
                if (count == 1 && ps[0] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_ctor_string", 1, false, false, true);
                // nb9 (M9): StringBuilder(string, int) / (string, int, int, int).
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_ctor_string_capacity", 2, false, false, true,
                        new[] { "", "", "i" });
                if (count == 4 && ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_ctor_string_range_capacity", 4, false, false, true,
                        new[] { "", "", "i", "i", "i" });
                // SB-REST: ctor(int capacity, int maxCapacity). The 2-arg form
                // must NOT fall through to the 1-arg capacity arm (the fold
                // would pop one arg and desync the stack).
                if (count == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_ctor_capacity_max", 2, false, false, true, new[] { "", "i", "i" });
                throw new NotSupportedException($"StringBuilder ctor param type 0x{ps[0]:X2} not wired in M5");
            case "Append":
                // SB-REST: the range overloads are 3-param methods. The old
                // ps[0]-only dispatch bound them to the 1-arg Append icall
                // (ParamCount 1): the fold popped only the trailing count,
                // then callvirt popped the receiver -- receiver AND string
                // were lost (`append_s(2LL, 3LL)`, a C0 compile break).
                if (count == 3)
                {
                    if (ps[0] == 0x0E && ps[1] == 0x08 && ps[2] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_append_s_range", 3, true, true, true,
                            new[] { "", "", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    if (ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_append_c_array_range", 3, true, true, true,
                            new[] { "", "a", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.Append 3-arg signature not wired in M5");
                }
                if (count != 1)
                {
                    // nb9 (M14): Append(char, int) -- repeat the unit
                    // repeatCount times (AOORE("repeatCount") when negative).
                    if (count == 2 && ps[0] == 0x03 && ps[1] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_append_c_count", 2, true, true, true,
                            new[] { "", "c", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.Append with {count} args not wired in M5");
                }
                return ps[0] switch
                {
                    0x0E => I("il2cpp::runtime::icall_stringbuilder_append_s", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    0x03 => I("il2cpp::runtime::icall_stringbuilder_append_c", 1, true, true, true, new[] { "", "c" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x08 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(byte/sbyte/short/ushort/uint) -- the narrow
                    // integer family. net48 appends the value's invariant
                    // decimal text; the runtime's append_i kernel takes an
                    // int32, so the small types are widened ("i"). A uint
                    // value >= 2^31 would render through the signed kernel
                    // (the same documented deviation as the
                    // DefaultInterpolatedStringHandler AppendFormatted
                    // mapping, which folds 0x04/0x05/0x06/0x07/0x09 to the
                    // int kernel).
                    0x04 or 0x05 or 0x06 or 0x07 or 0x09 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, true, true, true, new[] { "", "i" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, true, true, true, new[] { "", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(ulong) -- the int64 kernel (a value >= 2^63
                    // would render signed; the same documented deviation).
                    0x0B => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, true, true, true, new[] { "", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x0D => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, true, true, true, new[] { "", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(float) -- net48 appends the value's "G"-like
                    // default text. Float values live in double slots (the
                    // MethodMap.Format / MathF precedent), so the append_d
                    // kernel renders them with the "d" cast (the same shape
                    // the DefaultInterpolatedStringHandler mapping uses).
                    0x0C => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, true, true, true, new[] { "", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x02 => I("il2cpp::runtime::icall_stringbuilder_append_b", 1, true, true, true, new[] { "", "b" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(StringBuilder): the class-typed (0x12) Append
                    // overload is exactly StringBuilder -- append its CONTENT
                    // (StringBuilder.ToString()), not the type name.
                    0x12 => I("il2cpp::runtime::icall_stringbuilder_append_sb", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    0x1C => I("il2cpp::runtime::icall_stringbuilder_append_o", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    _ => throw new NotSupportedException($"StringBuilder.Append param type 0x{ps[0]:X2} not wired in M5"),
                };
            case "Insert":
                // batch 170 (#114 deep-dive): the Insert overload family. The
                // OLD mapping resolved EVERY signature to the (int, string)
                // icall -- numeric values reached Il2CppObject* raw
                // (uncompilable C++), the 3-arg form lost the receiver, and
                // char[] silently re-read the ARRAY header as a string. Gate
                // per ps[1] exactly like the Append case (numeric values go
                // through the per-type insert icalls; unmapped value types
                // -- char[] 0x1D, object 0x1C, float 0x0C, Decimal 0x11, ...
                // -- are LOUD rejections, never silent corruption).
                if (count == 2 && ps[0] == 0x08)
                {
                    return ps[1] switch
                    {
                        0x0E => I("il2cpp::runtime::icall_stringbuilder_insert", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                        0x08 => I("il2cpp::runtime::icall_stringbuilder_insert_i", 2, true, true, true, new[] { "", "i", "i" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x0A => I("il2cpp::runtime::icall_stringbuilder_insert_l", 2, true, true, true, new[] { "", "i", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x0D => I("il2cpp::runtime::icall_stringbuilder_insert_d", 2, true, true, true, new[] { "", "i", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x02 => I("il2cpp::runtime::icall_stringbuilder_insert_b", 2, true, true, true, new[] { "", "i", "b" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x03 => I("il2cpp::runtime::icall_stringbuilder_insert_c", 2, true, true, true, new[] { "", "i", "c" }, null, false, false, false, "System.Text.StringBuilder"),
                        _ => throw new NotSupportedException($"StringBuilder.Insert value param type 0x{ps[1]:X2} not wired in M5"),
                    };
                }
                if (count == 3 && ps[0] == 0x08 && ps[1] == 0x0E && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_insert_s_count", 3, true, true, true, new[] { "", "i", "", "i" }, null, false, false, false, "System.Text.StringBuilder");
                // SB-REST: Insert(int, char[], int, int) -- the range form over
                // a managed char[] (SZARRAY 0x1D).
                if (count == 4 && ps[0] == 0x08 && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_insert_c_array_range", 4, true, true, true,
                        new[] { "", "i", "a", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.Insert signature not wired in M5");
            case "Remove":
                return I("il2cpp::runtime::icall_stringbuilder_remove", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
            // netcore21+ audit surface: Clear() -- Length drops to 0 (the
            // buffer stays allocated; stringbuilder.cpp).
            case "Clear":
                if (count == 0) return I("il2cpp::runtime::icall_stringbuilder_clear", 0, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                return null;
            case "AppendLine":
                if (count == 0) return I("il2cpp::runtime::icall_stringbuilder_append_line", 0, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (count == 1 && ps[0] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_append_line_s", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.AppendLine signature not wired in M5");
            case "AppendFormat":
                // net48 overloads: (string, object[, object[, object[,
                // object]]]), the params-object[] form, and the
                // IFormatProvider-prefixed forms. All share the
                // AppendFormatCore composite engine (stringbuilder.cpp).
                // IFormatProvider crosses as the CLASS element byte 0x12.
                if (ps.Length >= 2 && ps[0] == 0x12 && ps[1] == 0x0E)
                {
                    // (provider, string) / (provider, string, params object[])
                    // / (provider, string, object) / (provider, string, object,
                    // object) / (provider, string, object, object, object).
                    if (ps.Length == 2)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 3 && ps[2] == 0x1D)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 3 && ps[2] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 4 && ps[2] == 0x1C && ps[3] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_2_provider", 4, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 5 && ps[2] == 0x1C && ps[3] == 0x1C && ps[4] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_3_provider", 5, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.AppendFormat provider signature not wired in M5");
                }
                if (count < 2 || ps[0] != 0x0E)
                    throw new NotSupportedException($"StringBuilder.AppendFormat signature not wired in M5");
                // (string, params object[]) -- the SZARRAY element byte is
                // consumed by the signature walk (ps[1] == 0x1D).
                if (ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_stringbuilder_append_format", ps.Length, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (ps[1] != 0x1C)
                    throw new NotSupportedException($"StringBuilder.AppendFormat signature not wired in M5");
                return count switch
                {
                    2 => I("il2cpp::runtime::icall_stringbuilder_append_format_1", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    3 => I("il2cpp::runtime::icall_stringbuilder_append_format_2", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    4 => I("il2cpp::runtime::icall_stringbuilder_append_format_3", 4, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    5 => I("il2cpp::runtime::icall_stringbuilder_append_format_4", 5, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    _ => throw new NotSupportedException($"StringBuilder.AppendFormat with {count} args not wired in M5"),
                };
            case "Replace":
                // SB-REST: the 4-arg range forms
                // Replace(string,string,int,int) / Replace(char,char,int,int).
                if (count == 4)
                {
                    if (ps[0] == 0x0E && ps[1] == 0x0E && ps[2] == 0x08 && ps[3] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_replace_s_s_range", 4, true, true, true,
                            new[] { "", "", "", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    if (ps[0] == 0x03 && ps[1] == 0x03 && ps[2] == 0x08 && ps[3] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_replace_c_c_range", 4, true, true, true,
                            new[] { "", "c", "c", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.Replace 4-arg signature not wired in M5");
                }
                if (count != 2) throw new NotSupportedException($"StringBuilder.Replace with {count} args not wired in M5");
                if (ps[0] == 0x0E && ps[1] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_replace_s_s", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (ps[0] == 0x03 && ps[1] == 0x03) return I("il2cpp::runtime::icall_stringbuilder_replace_c_c", 2, true, true, true, new[] { "", "c", "c" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.Replace param type 0x{ps[0]:X2} not wired in M5");
            // SB-REST: CopyTo(int, char[], int, int) -- copies the builder
            // window into a managed char[] (void result).
            case "CopyTo":
                if (count == 4 && ps[0] == 0x08 && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_copy_to", 4, false, false, true,
                        new[] { "", "i", "a", "i", "i" });
                throw new NotSupportedException($"StringBuilder.CopyTo signature not wired in M5");
            // SB-REST: EnsureCapacity(int) -> int (the resulting Capacity).
            case "EnsureCapacity":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_ensure_capacity", 1, true, false, true, new[] { "", "i" });
                throw new NotSupportedException($"StringBuilder.EnsureCapacity signature not wired in M5");
            // SB-REST: Equals(StringBuilder) -- reference equality.
            case "Equals":
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_stringbuilder_equals", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"StringBuilder.Equals signature not wired in M5");
            case "ToString":
                // The 2-arg overload ToString(int startIndex, int length) must
                // NOT bind to the 0-arg snapshot icall (that would pass the
                // two ints as the object receiver -- a C0 compile break). The
                // range form slices the buffer with net48 AOORE validation
                // (icall_stringbuilder_to_string_range, stringbuilder.cpp).
                if (count == 0) return I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true);
                if (count == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_to_string_range", 2, true, true, true,
                        new[] { "", "i", "i" }, returnTypeName: "System.String");
                return null;
            // set_Length(int32 value): the net48 Length setter -- negative ->
            // ArgumentOutOfRangeException; larger values grow the buffer
            // (zero-filled), smaller values truncate (icall_stringbuilder_
            // set_length, stringbuilder.cpp).
            case "set_Length":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_set_length", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            // D5 stsdeep: get_MaxCapacity -- constant int.MaxValue (see runtime note).
            case "get_Length":
                return I("il2cpp::runtime::icall_stringbuilder_get_length", 0, true, false, true);
            case "get_MaxCapacity":
                return I("il2cpp::runtime::icall_stringbuilder_get_max_capacity", 0, true, false, true);
            // get_Chars(index): the indexer -- one UTF-16 unit out of the
            // buffer (int32 carrier); out-of-range -> IndexOutOfRangeException.
            case "get_Chars":
                return I("il2cpp::runtime::icall_stringbuilder_get_chars", 1, true, false, true, new[] { "", "i" });
            // P26 (bcldeep): set_Chars(index, c) -- the indexer WRITER (net8
            // CoreLib setter is pure-managed; the transpiler needs the icall).
            case "set_Chars":
                return I("il2cpp::runtime::icall_stringbuilder_set_chars", 2, false, false, true, new[] { "", "i", "c" });
            case "get_Capacity":
                // No capacity icall: read the runtime struct field directly.
                return I("il2cpp::runtime::il2cpp_stringbuilder_capacity", 0, true, false, true, new[] { "fcap" });
            // nb9 M-3: set_Capacity(int) -- net48 AOORE("value") above
            // MaxCapacity (the old behaviour fell to a generated-name stub
            // that threw NotSupportedException at run time).
            case "set_Capacity":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_set_capacity", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            // P6: StringBuilder.ReplaceBufferInternal/ReplaceBufferAnsiInternal
            // (net48 internal helpers for char*/byte* buffer replacement).
            case "ReplaceBufferInternal": return I("il2cpp::runtime::icall_stringbuilder_replace_buffer_internal", 1, true, true, true, new[] { "c" });
            case "ReplaceBufferAnsiInternal": return I("il2cpp::runtime::icall_stringbuilder_replace_buffer_ansi_internal", 1, true, true, true, new[] { "" });
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapDefaultInterpHandler -- P25f (dhprobe): the net6+ string
    // interpolation lowering. A `$"...{x}..."` compiles to an address-taken
    // System.Runtime.CompilerServices.DefaultInterpolatedStringHandler VALUE
    // local + a fixed sequence of instance `call`s (the ildasm shape):
    //   .ctor(int32 literalLength, int32 formattedCount)
    //   AppendLiteral(string) / AppendFormatted<T>(T) / AppendFormatted(string)
    //   ToStringAndClear(): string
    // The handler is modeled as a StringBuilder object inside the slot: the
    // ctor writes a fresh builder into the slot ADDRESS (Il2CppObject** -- the
    // icall_interp_handler_ctor runtime helper), the appends forward to the
    // builder append icalls (deref:r on the receiver -- the slot holds the
    // builder object), and ToStringAndClear reads the built string.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapDefaultInterpHandler(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string[]? genArgNames, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // (int32 literalLength, int32 formattedCount) -- capacity hints.
                return count == 2
                    ? I("il2cpp::runtime::icall_interp_handler_ctor", 2, false, false, true, new[] { "", "i", "i" })
                    : null;
            case "AppendLiteral":
                return count == 1
                    ? I("il2cpp::runtime::icall_stringbuilder_append_s", 1, false, false, true, new[] { "deref:r" })
                    : null;
            case "AppendFormatted":
                // The generic-inst arm carries the INSTANTIATED arg in genArgs
                // (MethodSpec decode -- AppendFormatted<int32> -> 0x08); the
                // non-generic string overload reaches here with the string ps.
                // ParamCount MUST track the IL arity exactly (the TrimStart
                // lockstep discipline): a count-2/3 form resolved with
                // ParamCount 1 DESYNCS the fold's shadow stack and emits
                // corrupt calls (the handler slot becomes a literal, a string
                // pool is cast to int -- silent volume). The width ('{x,5}')
                // and format ('{x:D3}') overloads therefore map to the
                // dedicated _w / _f / _wf runtime trios (P25g).
                var vk = genArgs != null && genArgs.Length > 0 ? genArgs[0]
                    : ps.Length > 0 ? ps[0] : (byte)0xFF;
                // P25h: a generic VALUE type (0x11 -- decimal / DateTime / Guid)
                // has no primitive slot cast. The MethodSpec generic-arg NAME
                // table names the closed type, and the transpiler boxes the value
                // slot through the matching BCL box helper; the runtime composite
                // engine (icall_format_composite_item) renders the box (the same
                // net48 composite semantics the primitive _w/_f/_wf trios use).
                if (vk == 0x11)
                {
                    var vtypeName = genArgNames != null && genArgNames.Length > 0 ? genArgNames[0] : null;
                    var valCast = vtypeName switch
                    {
                        "System.Decimal" => "box:dec",
                        "System.DateTime" => "box:dt",
                        "System.Guid" => "box:guid",
                        _ => null,
                    };
                    if (valCast == null)
                        throw new NotSupportedException(
                            $"DefaultInterpolatedStringHandler.AppendFormatted value type '{vtypeName ?? "0x11"}' not wired in M5");
                    if (count == 1)
                        return I("il2cpp::runtime::icall_stringbuilder_append_o", 1, false, false, true,
                            new[] { "deref:r", valCast });
                    if (count == 2)
                    {
                        var isWidth = ps.Length > 1 && ps[1] == 0x08;
                        var isFmt = ps.Length > 1 && ps[1] == 0x0E;
                        if (!isWidth && !isFmt)
                            throw new NotSupportedException(
                                $"DefaultInterpolatedStringHandler.AppendFormatted 2-arg signature not wired in M5");
                        return isWidth
                            ? I("il2cpp::runtime::icall_stringbuilder_append_o_w", 2, false, false, true,
                                new[] { "deref:r", valCast, "i" })
                            : I("il2cpp::runtime::icall_stringbuilder_append_o_f", 2, false, false, true,
                                new[] { "deref:r", valCast, "" });
                    }
                    if (count == 3)
                    {
                        if (ps.Length != 3 || ps[1] != 0x08 || ps[2] != 0x0E)
                            throw new NotSupportedException(
                                "DefaultInterpolatedStringHandler.AppendFormatted 3-arg signature not wired in M5");
                        return I("il2cpp::runtime::icall_stringbuilder_append_o_wf", 3, false, false, true,
                            new[] { "deref:r", valCast, "i", "" });
                    }
                    throw new NotSupportedException(
                        $"DefaultInterpolatedStringHandler.AppendFormatted with {count} arg(s) not wired in M5");
                }
                if (count == 1)
                {
                    return vk switch
                    {
                        0x08 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r" }),
                        0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, false, false, true, new[] { "deref:r", "l" }),
                        0x0D => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, false, false, true, new[] { "deref:r", "d" }),
                        0x02 => I("il2cpp::runtime::icall_stringbuilder_append_b", 1, false, false, true, new[] { "deref:r", "b" }),
                        0x03 => I("il2cpp::runtime::icall_stringbuilder_append_c", 1, false, false, true, new[] { "deref:r", "c" }),
                        0x04 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r", "i" }),
                        0x05 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r", "i" }),
                        0x06 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r", "i" }),
                        0x07 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r", "i" }),
                        0x09 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r", "i" }),
                        0x0B => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, false, false, true, new[] { "deref:r", "l" }),
                        0x0C => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, false, false, true, new[] { "deref:r", "d" }),
                        _ when vk == 0x0E => I("il2cpp::runtime::icall_stringbuilder_append_s", 1, false, false, true, new[] { "deref:r" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted arg 0x{vk:X2} not wired in M5"),
                    };
                }
                if (count == 2)
                {
                    // (T, int32 width) -> _w; (T, string format) -> _f.
                    var isWidth = ps.Length > 1 && ps[1] == 0x08;
                    var isFmt = ps.Length > 1 && ps[1] == 0x0E;
                    if (!isWidth && !isFmt)
                        throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 2-arg signature not wired in M5 (ps[{string.Join(",", ps.Select(b => $"0x{b:X2}"))}])");
                    var suffix = isWidth ? "_w" : "_f";
                    return vk switch
                    {
                        0x08 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x0A => I($"il2cpp::runtime::icall_stringbuilder_append_l{suffix}", 2, false, false, true,
                            new[] { "deref:r", "l", isWidth ? "i" : "" }),
                        0x0D => I($"il2cpp::runtime::icall_stringbuilder_append_d{suffix}", 2, false, false, true,
                            new[] { "deref:r", "d", isWidth ? "i" : "" }),
                        0x02 => I($"il2cpp::runtime::icall_stringbuilder_append_b{suffix}", 2, false, false, true,
                            new[] { "deref:r", "b", isWidth ? "i" : "" }),
                        0x03 => I($"il2cpp::runtime::icall_stringbuilder_append_c{suffix}", 2, false, false, true,
                            new[] { "deref:r", "c", isWidth ? "i" : "" }),
                        0x04 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x05 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x06 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x07 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x09 => I($"il2cpp::runtime::icall_stringbuilder_append_i{suffix}", 2, false, false, true,
                            new[] { "deref:r", "i", isWidth ? "i" : "" }),
                        0x0B => I($"il2cpp::runtime::icall_stringbuilder_append_l{suffix}", 2, false, false, true,
                            new[] { "deref:r", "l", isWidth ? "i" : "" }),
                        0x0C => I($"il2cpp::runtime::icall_stringbuilder_append_d{suffix}", 2, false, false, true,
                            new[] { "deref:r", "d", isWidth ? "i" : "" }),
                        _ when vk == 0x0E => I($"il2cpp::runtime::icall_stringbuilder_append_s{suffix}", 2, false, false, true,
                            new[] { "deref:r", "", isWidth ? "i" : "" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 2-arg value 0x{vk:X2} not wired in M5"),
                    };
                }
                if (count == 3)
                {
                    // (T, int32 width, string format) -> _wf.
                    if (ps.Length != 3 || ps[1] != 0x08 || ps[2] != 0x0E)
                        throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 3-arg signature not wired in M5");
                    return vk switch
                    {
                        0x08 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l_wf", 3, false, false, true, new[] { "deref:r", "l", "i", "" }),
                        0x0D => I("il2cpp::runtime::icall_stringbuilder_append_d_wf", 3, false, false, true, new[] { "deref:r", "d", "i", "" }),
                        0x02 => I("il2cpp::runtime::icall_stringbuilder_append_b_wf", 3, false, false, true, new[] { "deref:r", "b", "i", "" }),
                        0x03 => I("il2cpp::runtime::icall_stringbuilder_append_c_wf", 3, false, false, true, new[] { "deref:r", "c", "i", "" }),
                        0x04 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x05 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x06 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x07 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x09 => I("il2cpp::runtime::icall_stringbuilder_append_i_wf", 3, false, false, true, new[] { "deref:r", "i", "i", "" }),
                        0x0B => I("il2cpp::runtime::icall_stringbuilder_append_l_wf", 3, false, false, true, new[] { "deref:r", "l", "i", "" }),
                        0x0C => I("il2cpp::runtime::icall_stringbuilder_append_d_wf", 3, false, false, true, new[] { "deref:r", "d", "i", "" }),
                        _ when vk == 0x0E => I("il2cpp::runtime::icall_stringbuilder_append_s_wf", 3, false, false, true, new[] { "deref:r", "", "i", "" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 3-arg value 0x{vk:X2} not wired in M5"),
                    };
                }
                throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted with {count} arg(s) not wired in M5");
            case "ToStringAndClear":
                return count == 0
                    ? I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true, new[] { "deref:r" }, returnTypeName: "System.String")
                    : null;
            default:
                return null;
        }
    }
}
