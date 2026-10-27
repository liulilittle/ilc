namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StringBuilder -- System.Text.StringBuilder icall mappings
// (il2cpp_stringbuilder.h).
// Owners: System.Text.StringBuilder
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStringBuilder(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string[]? genArgNames = null)
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
                throw new NotSupportedException($"StringBuilder ctor param type 0x{ps[0]:X2} not wired in M5 -- LOUD whitelist (net48 StringBuilder ctors are only (), (int), (int,int), (string), (string,int) and (string,int,int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:86-157");
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
                    throw new NotSupportedException($"StringBuilder.Append 3-arg signature not wired in M5 -- LOUD whitelist (net48 Append 3-arg forms are (string,int,int) and (char[],int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037");
                }
                if (count != 1)
                {
                    // nb9 (M14): Append(char, int) -- repeat the unit
                    // repeatCount times (AOORE("repeatCount") when negative).
                    if (count == 2 && ps[0] == 0x03 && ps[1] == 0x08)
                        return I("il2cpp::runtime::icall_stringbuilder_append_c_count", 2, true, true, true,
                            new[] { "", "c", "i" }, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.Append with {count} args not wired in M5 -- LOUD whitelist (net48 Append has no overload with this arity); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037");
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
                    0x04 or 0x05 or 0x06 or 0x07 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, true, true, true, new[] { "", "i" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(uint) -- the UNSIGNED decimal kernel (a value
                    // >= 2^31 must not render through the signed int32 form).
                    0x09 => I("il2cpp::runtime::icall_stringbuilder_append_u4", 1, true, true, true, new[] { "", "u4" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, true, true, true, new[] { "", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(ulong) -- the UNSIGNED 64-bit kernel.
                    0x0B => I("il2cpp::runtime::icall_stringbuilder_append_u8", 1, true, true, true, new[] { "", "q" }, null, false, false, false, "System.Text.StringBuilder"),
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
                    // Append(char[]) -- the whole managed array (null no-op).
                    0x1D => I("il2cpp::runtime::icall_stringbuilder_append_c_array", 1, true, true, true, new[] { "", "a" }, null, false, false, false, "System.Text.StringBuilder"),
                    // Append(decimal) -- the 0x11 VALUETYPE slot boxes through
                    // the BCL decimal box and renders via the Object.ToString
                    // dispatch (net48 Append(decimal) = Append(ToString())).
                    // D (impl-sb): the net6+ interpolation handler overload
                    // Append(ref AppendInterpolatedStringHandler) decodes to the
                    // SAME 0x11 byte (ReadParamByte strips the BYREF wrapper) --
                    // the discriminator is receiverIsAddress (the fold's stack
                    // top): the handler arg is the ldloca ADDRESS of the handler
                    // slot, a decimal argument is a plain value slot. The
                    // handler's content already landed in the builder (the
                    // nested ctor bound the REAL builder into the slot), so the
                    // outer call is a NO-OP that returns the builder:
                    // append_s with an appended nullptr literal appends nothing
                    // and chains (the kernel's documented null contract).
                    0x11 when receiverIsAddress => I("il2cpp::runtime::icall_stringbuilder_append_s", 1, true, true, true,
                        new[] { "", "drop" }, null, false, false, false, "System.Text.StringBuilder",
                        appendLiterals: new[] { "nullptr" }),
                    0x11 => I("il2cpp::runtime::icall_stringbuilder_append_o", 1, true, true, true, new[] { "", "box:dec" }, null, false, false, false, "System.Text.StringBuilder"),
                    // D (impl-sb): Append(ReadOnlySpan<char>) -- netcoreapp2.1+
                    // (reference/netcore/.../StringBuilder.cs:1133). The span
                    // carrier is the BACKING object identity the fold
                    // materializes (MemoryExtensions.AsSpan(string) -> the
                    // string; Span<char>.op_Implicit(char[]) -> the array), so
                    // the runtime span kernel dispatches on the object class
                    // (string units vs char[] elements). A sliced span (offset
                    // / count) is not representable -- the compiler lowering
                    // for these call sites only produces whole-source spans.
                    0x15 => I("il2cpp::runtime::icall_stringbuilder_append_span", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    0x1C => I("il2cpp::runtime::icall_stringbuilder_append_o", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    _ => throw new NotSupportedException($"StringBuilder.Append param type 0x{ps[0]:X2} not wired in M5 -- LOUD whitelist (net48 Append value families are bool/char/char[]/decimal/double/short/int/long/float/sbyte/string/uint/ulong/ushort/object/StringBuilder plus (char,int) and (char[],int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037"),
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
                        // Insert(int, byte/sbyte/short/ushort): the value
                        // widens to the int32 carrier (sign/zero extension is
                        // exact for the narrow domains).
                        0x04 or 0x05 or 0x06 or 0x07 => I("il2cpp::runtime::icall_stringbuilder_insert_i", 2, true, true, true, new[] { "", "i", "i" }, null, false, false, false, "System.Text.StringBuilder"),
                        // Insert(int, uint) / (int, ulong): the UNSIGNED text.
                        0x09 => I("il2cpp::runtime::icall_stringbuilder_insert_u4", 2, true, true, true, new[] { "", "i", "u4" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x0A => I("il2cpp::runtime::icall_stringbuilder_insert_l", 2, true, true, true, new[] { "", "i", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x0B => I("il2cpp::runtime::icall_stringbuilder_insert_u8", 2, true, true, true, new[] { "", "i", "q" }, null, false, false, false, "System.Text.StringBuilder"),
                        // Insert(int, float): float lives in a double slot
                        // (the Append(float) / MethodMap.Format precedent).
                        0x0C => I("il2cpp::runtime::icall_stringbuilder_insert_d", 2, true, true, true, new[] { "", "i", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x0D => I("il2cpp::runtime::icall_stringbuilder_insert_d", 2, true, true, true, new[] { "", "i", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x02 => I("il2cpp::runtime::icall_stringbuilder_insert_b", 2, true, true, true, new[] { "", "i", "b" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x03 => I("il2cpp::runtime::icall_stringbuilder_insert_c", 2, true, true, true, new[] { "", "i", "c" }, null, false, false, false, "System.Text.StringBuilder"),
                        // Insert(int, char[]) -- the whole managed array.
                        0x1D => I("il2cpp::runtime::icall_stringbuilder_insert_c_array", 2, true, true, true, new[] { "", "i", "a" }, null, false, false, false, "System.Text.StringBuilder"),
                        // Insert(int, object) / Insert(int, decimal): the
                        // Object.ToString dispatch (decimal boxes through the
                        // BCL decimal box first).
                        0x1C => I("il2cpp::runtime::icall_stringbuilder_insert_o", 2, true, true, true, new[] { "", "i", "" }, null, false, false, false, "System.Text.StringBuilder"),
                        0x11 => I("il2cpp::runtime::icall_stringbuilder_insert_o", 2, true, true, true, new[] { "", "i", "box:dec" }, null, false, false, false, "System.Text.StringBuilder"),
                        _ => throw new NotSupportedException($"StringBuilder.Insert value param type 0x{ps[1]:X2} not wired in M5 -- LOUD whitelist (net48 Insert value families are bool/char/char[]/decimal/double/short/int/long/float/sbyte/string/uint/ulong/ushort/object plus (int,string,int) and (int,char[],int,int)) (defensive; no reference assembly defines this shape); net48 RefSrc mscorlib/system/text/stringbuilder.cs:820,1037-1251"),
                    };
                }
                if (count == 3 && ps[0] == 0x08 && ps[1] == 0x0E && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_insert_s_count", 3, true, true, true, new[] { "", "i", "", "i" }, null, false, false, false, "System.Text.StringBuilder");
                // SB-REST: Insert(int, char[], int, int) -- the range form over
                // a managed char[] (SZARRAY 0x1D).
                if (count == 4 && ps[0] == 0x08 && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_insert_c_array_range", 4, true, true, true,
                        new[] { "", "i", "a", "i", "i" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.Insert signature not wired in M5 -- LOUD whitelist (net48 Insert defines no overload with this arity/param shape); net48 RefSrc mscorlib/system/text/stringbuilder.cs:820,1037-1251");
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
                // A (impl-sb): the net6+ interpolation handler overloads
                // (reference/netcore/.../StringBuilder.cs:1155/:1161) -- the
                // BYREF(0x10) to the nested AppendInterpolatedStringHandler
                // VALUETYPE decodes to the 0x11 element byte (no net48
                // AppendLine carries a VALUETYPE first parameter, so the byte
                // cannot collide). The handler's appends already landed in the
                // builder (the nested ctor bound the REAL builder into the
                // handler slot), so the outer call only appends the newline:
                // the handler address (and the provider slot on the 2-arg form)
                // pop in lockstep and drop. The provider is ignored -- the
                // newline is culture-free (the composite culture surface of the
                // nested handler's AppendFormatted is the documented net6+
                // deviation of the bind model).
                if (count == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_stringbuilder_append_line", 1, true, true, true,
                        new[] { "", "drop" }, null, false, false, false, "System.Text.StringBuilder");
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_stringbuilder_append_line", 2, true, true, true,
                        new[] { "", "drop", "drop" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.AppendLine signature not wired in M5 -- LOUD whitelist (net48 AppendLine has only () and (string); the net6+ interpolation-handler forms (ref handler / (provider, ref handler)) are wired (net6+ handler, no net48 counterpart)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:742-748");
            case "AppendFormat":
                // net48 overloads: (string, object[, object[, object[,
                // object]]]), the params-object[] form, and the
                // IFormatProvider-prefixed forms. All share the
                // AppendFormatCore composite engine (stringbuilder.cpp).
                // IFormatProvider crosses as the CLASS element byte 0x12.
                // C (impl-sb): the net6+ CompositeFormat overloads
                // (reference/netcore/.../StringBuilder.cs:1852-1894/:1912) put a
                // SECOND CLASS slot (CompositeFormat, 0x12) before the args --
                // net48 has no (IFormatProvider, CLASS, ...) AppendFormat
                // shape, so the byte cannot collide. The generic TArg slots
                // arrive as MVAR (0x1E) bytes with the CLOSED types in genArgs
                // (the MethodSpec decode); each slot's cast follows the
                // AppendFormatted vk table (the value families box through the
                // matching BCL box helper). SEMANTICS NOTE: the kernel takes
                // the composite FORMAT STRING -- a pre-parsed CompositeFormat
                // is (per the netcore reference) an immutable pre-validated
                // wrapper; the runtime re-parses the underlying format text,
                // which yields the same output for the shapes the parse
                // accepts (the pre-parse validation surface is not modeled).
                if (ps.Length >= 2 && ps[0] == 0x12 && ps[1] == 0x12)
                {
                    // (provider, CompositeFormat, params object?[]) -- the
                    // SZARRAY params form (reference :1912).
                    if (ps.Length == 3 && ps[2] == 0x1D)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    // (provider, CompositeFormat, TArg0[, TArg1[, TArg2]]) --
                    // the closed generic slots (reference :1852/:1872/:1894).
                    if (ps.Length >= 3 && ps[2] == 0x1E)
                    {
                        var slots = ps.Length - 2;
                        if (genArgs == null || genArgs.Length != slots)
                            throw new NotSupportedException(
                                $"StringBuilder.AppendFormat CompositeFormat with {slots} closed slot(s) has no MethodSpec generic-arg table (genArgs={(genArgs == null ? "null" : genArgs.Length.ToString())}) -- LOUD whitelist (net6+ handler, no net48 counterpart; the closed TArg carrier cannot be generated without the MethodSpec decode)");
                        var argCasts = new string[slots];
                        for (var i = 0; i < slots; i++)
                            argCasts[i] = CompositeArgCast(genArgs[i], genArgNames != null && i < genArgNames.Length ? genArgNames[i] : null);
                        return slots switch
                        {
                            1 => I("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", 3, true, true, true,
                                new[] { "", "", "" }.Concat(argCasts).ToArray(), null, false, false, false, "System.Text.StringBuilder"),
                            2 => I("il2cpp::runtime::icall_stringbuilder_append_format_2_provider", 4, true, true, true,
                                new[] { "", "", "" }.Concat(argCasts).ToArray(), null, false, false, false, "System.Text.StringBuilder"),
                            3 => I("il2cpp::runtime::icall_stringbuilder_append_format_3_provider", 5, true, true, true,
                                new[] { "", "", "" }.Concat(argCasts).ToArray(), null, false, false, false, "System.Text.StringBuilder"),
                            _ => throw new NotSupportedException(
                                $"StringBuilder.AppendFormat CompositeFormat with {slots} closed slots not wired in M5 -- LOUD whitelist (netcore defines TArg0..TArg2 generic overloads only) (net6+ handler, no net48 counterpart); netcore ref System.Private.CoreLib/src/System/Text/StringBuilder.cs:1852-1894"),
                        };
                    }
                    throw new NotSupportedException($"StringBuilder.AppendFormat CompositeFormat signature not wired in M5 -- LOUD whitelist (netcore CompositeFormat forms are (provider,CompositeFormat,params object[]) and the TArg0..TArg2 generic family) (net6+ handler, no net48 counterpart); netcore ref System.Private.CoreLib/src/System/Text/StringBuilder.cs:1852-1938");
                }
                if (ps.Length >= 2 && ps[0] == 0x12 && ps[1] == 0x0E)
                {
                    // (provider, string) / (provider, string, params object[])
                    // / (provider, string, object) / (provider, string, object,
                    // object) / (provider, string, object, object, object).
                    if (ps.Length == 2)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    // B (impl-sb): the netcore span-params form
                    // (provider, string, params ReadOnlySpan<object?>)
                    // (reference :1569). The span tail (0x15) rides the SAME
                    // argsArray kernel: the fold materializes the object[] from
                    // the compiler's inline-array lowering, so the popped count
                    // stays the SIGNATURE arity (the array carries the elements).
                    if (ps.Length == 3 && ps[2] == 0x15)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 3 && ps[2] == 0x1D)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 3 && ps[2] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_1_provider", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 4 && ps[2] == 0x1C && ps[3] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_2_provider", 4, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    if (ps.Length == 5 && ps[2] == 0x1C && ps[3] == 0x1C && ps[4] == 0x1C)
                        return I("il2cpp::runtime::icall_stringbuilder_append_format_3_provider", 5, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                    throw new NotSupportedException($"StringBuilder.AppendFormat provider signature not wired in M5 -- LOUD whitelist (net48 AppendFormat provider forms are (provider,string), (provider,string,object), (provider,string,object,object), (provider,string,object,object,object) and (provider,string,params object[]); the netcore span-params form is wired) (net6+ handler, no net48 counterpart); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1290-1305");
                }
                if (count < 2 || ps[0] != 0x0E)
                    throw new NotSupportedException($"StringBuilder.AppendFormat signature not wired in M5 -- LOUD whitelist (net48 AppendFormat requires a string format as its first argument); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1262-1305");
                // (string, params object[]) -- the SZARRAY element byte is
                // consumed by the signature walk (ps[1] == 0x1D).
                if (ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_stringbuilder_append_format", ps.Length, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                // B (impl-sb): the netcore span-params form (string, params
                // ReadOnlySpan<object?>) (reference :1522) -- the same argsArray
                // kernel over the fold-materialized object[].
                if (ps.Length == 2 && ps[1] == 0x15)
                    return I("il2cpp::runtime::icall_stringbuilder_append_format", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (ps[1] != 0x1C)
                    throw new NotSupportedException($"StringBuilder.AppendFormat signature not wired in M5 -- LOUD whitelist (net48 AppendFormat's second argument is object or params object[]; the netcore span-params form is wired) (net6+ handler, no net48 counterpart); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1262-1305");
                return count switch
                {
                    2 => I("il2cpp::runtime::icall_stringbuilder_append_format_1", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    3 => I("il2cpp::runtime::icall_stringbuilder_append_format_2", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    4 => I("il2cpp::runtime::icall_stringbuilder_append_format_3", 4, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    5 => I("il2cpp::runtime::icall_stringbuilder_append_format_4", 5, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    _ => throw new NotSupportedException($"StringBuilder.AppendFormat with {count} args not wired in M5 -- LOUD whitelist (no reference assembly defines an AppendFormat overload with this arity) (defensive; no reference assembly defines this shape); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1262-1305"),
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
                    throw new NotSupportedException($"StringBuilder.Replace 4-arg signature not wired in M5 -- LOUD whitelist (net48 Replace 4-arg forms are (string,string,int,int) and (char,char,int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1535,1623");
                }
                if (count != 2) throw new NotSupportedException($"StringBuilder.Replace with {count} args not wired in M5 -- LOUD whitelist (net48 Replace has only (char,char), (string,string), (char,char,int,int) and (string,string,int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1485-1623");
                if (ps[0] == 0x0E && ps[1] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_replace_s_s", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (ps[0] == 0x03 && ps[1] == 0x03) return I("il2cpp::runtime::icall_stringbuilder_replace_c_c", 2, true, true, true, new[] { "", "c", "c" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.Replace param type 0x{ps[0]:X2} not wired in M5 -- LOUD whitelist (net48 Replace's first argument is char or string); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1485-1623");
            // SB-REST: CopyTo(int, char[], int, int) -- copies the builder
            // window into a managed char[] (void result).
            case "CopyTo":
                if (count == 4 && ps[0] == 0x08 && ps[1] == 0x1D && ps[2] == 0x08 && ps[3] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_copy_to", 4, false, false, true,
                        new[] { "", "i", "a", "i", "i" });
                throw new NotSupportedException($"StringBuilder.CopyTo signature not wired in M5 -- LOUD whitelist (net48 CopyTo has only (int,char[],int,int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:756");
            // SB-REST: EnsureCapacity(int) -> int (the resulting Capacity).
            case "EnsureCapacity":
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_stringbuilder_ensure_capacity", 1, true, false, true, new[] { "", "i" });
                throw new NotSupportedException($"StringBuilder.EnsureCapacity signature not wired in M5 -- LOUD whitelist (net48 EnsureCapacity has only (int)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:318");
            // SB-REST: Equals(StringBuilder) -- reference equality.
            case "Equals":
                if (count == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::icall_stringbuilder_equals", 1, true, false, true, new[] { "", "" });
                // Equals(object) -- System.Object reference equality (net48
                // StringBuilder only value-compares the StringBuilder
                // overload).
                if (count == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_stringbuilder_equals_obj", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"StringBuilder.Equals signature not wired in M5 -- LOUD whitelist (net48 Equals has only (StringBuilder) and (object)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:1490");
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
    // C (impl-sb): the CompositeFormat generic slot cast -- the closed TArg
    // rides the composite-engine OBJECT kernel, so a value family must box
    // through its BCL box helper first (the same per-vk table the
    // AppendFormatted arms use; the box casts mirror the ScalarTable box
    // helpers). Reference families ride the object slot raw.
    //--------------------------------------------------------------------------
    private static string CompositeArgCast(byte vk, string? vtypeName)
    {
        if (vk == 0x11)
        {
            // The BCL value types keep their dedicated box helpers; a USER
            // struct or enum boxes through "box:us:<name>" (the emitter
            // resolves the TypeDef and applies the GC-safety gate).
            return vtypeName switch
            {
                "System.Decimal" => "box:dec",
                "System.DateTime" => "box:dt",
                "System.Guid" => "box:guid",
                "System.TimeSpan" => "box:ts",
                null => throw new NotSupportedException(
                    "StringBuilder.AppendFormat CompositeFormat value-type slot has no MethodSpec generic-arg name (M5) -- LOUD whitelist (the MethodSpec generic-argument name is unavailable, so the boxed value-type carrier cannot be generated) (net6+ handler, no net48 counterpart); netcore ref System.Private.CoreLib/src/System/Text/StringBuilder.cs:1852-1894"),
                _ => "box:us:" + vtypeName,
            };
        }
        return vk switch
        {
            // int32 + the narrow integer family (the small types are widened
            // to the int32 box -- the documented Append narrow deviation).
            0x08 or 0x04 or 0x05 or 0x06 or 0x07 => "box:i",
            // The UNSIGNED/64-bit/float/bool/char boxes keep their OWN box
            // klass so the composite engine renders the right type text.
            0x09 => "box:u4",
            0x0A => "box:l",
            0x0B => "box:u8",
            0x0C => "box:f",
            0x0D => "box:d",
            0x02 => "box:b",
            0x03 => "box:c",
            // Reference families: the slot is already an Il2CppObject*.
            0x0E or 0x12 or 0x1C or 0x1D or 0x1F or 0x15 => "",
            _ => throw new NotSupportedException(
                $"StringBuilder.AppendFormat CompositeFormat closed slot 0x{vk:X2} not wired in M5 -- LOUD whitelist (the closed TArg families are the primitive/reference families) (net6+ handler, no net48 counterpart); netcore ref System.Private.CoreLib/src/System/Text/StringBuilder.cs:1852-1894"),
        };
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
                if (count == 2)
                    return I("il2cpp::runtime::icall_interp_handler_ctor", 2, false, false, true, new[] { "", "i", "i" });
                // A (impl-sb): the NESTED StringBuilder.AppendInterpolatedStringHandler
                // ctor shapes (the owner routes here from MethodRefResolver) --
                // (literalLength, formattedCount, StringBuilder) and the
                // IFormatProvider-prefixed 4-arg form
                // (reference/netcore/.../StringBuilder.cs:2962/:2975). Unlike the
                // DefaultInterpolatedStringHandler (a FRESH builder in the
                // slot), the nested handler holds the CALLER'S builder: the
                // bind kernel stores the passed StringBuilder INTO the slot so
                // the deref:r appends land in the real builder, and the outer
                // AppendLine/Append call finishes the operation. The provider
                // overload drops the provider (the bind model does not thread a
                // culture into the append kernels -- the documented net6+
                // deviation, same posture as the whitelist below).
                if (count == 3 && ps.Length == 3 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x12)
                    return I("il2cpp::runtime::icall_interp_handler_bind", 3, false, false, true,
                        new[] { "", "drop", "drop", "" });
                if (count == 4 && ps.Length == 4 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x12 && ps[3] == 0x12)
                    return I("il2cpp::runtime::icall_interp_handler_bind", 4, false, false, true,
                        new[] { "", "drop", "drop", "", "drop" });
                return null;
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
                    // The BCL value types keep their dedicated box helpers; a USER
                    // struct or enum boxes through the "box:us:<name>" cast (the
                    // emitter resolves the TypeDef and applies the GC-safety gate:
                    // a user struct with GC reference fields is rejected, an enum
                    // boxes through icall_box_enum).
                    var valCast = vtypeName switch
                    {
                        "System.Decimal" => "box:dec",
                        "System.DateTime" => "box:dt",
                        "System.Guid" => "box:guid",
                        "System.TimeSpan" => "box:ts",
                        null => throw new NotSupportedException(
                            "DefaultInterpolatedStringHandler.AppendFormatted value type has no MethodSpec generic-arg name (M5) -- LOUD whitelist (the MethodSpec generic-argument name is unavailable, so the boxed value-type carrier cannot be generated); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)"),
                        _ => "box:us:" + vtypeName,
                    };
                    if (count == 1)
                        return I("il2cpp::runtime::icall_stringbuilder_append_o", 1, false, false, true,
                            new[] { "deref:r", valCast });
                    if (count == 2)
                    {
                        var isWidth = ps.Length > 1 && ps[1] == 0x08;
                        var isFmt = ps.Length > 1 && ps[1] == 0x0E;
                        if (!isWidth && !isFmt)
                            throw new NotSupportedException(
                                $"DefaultInterpolatedStringHandler.AppendFormatted 2-arg signature not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> 2-arg forms are (T,int) and (T,string)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
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
                                "DefaultInterpolatedStringHandler.AppendFormatted 3-arg signature not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> 3-arg form is (T,int,string)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
                        return I("il2cpp::runtime::icall_stringbuilder_append_o_wf", 3, false, false, true,
                            new[] { "deref:r", valCast, "i", "" });
                    }
                    throw new NotSupportedException(
                        $"DefaultInterpolatedStringHandler.AppendFormatted with {count} arg(s) not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> arities are 1/2/3); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
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
                        // A REFERENCE generic argument (class/interface/
                        // object/array/generic instantiation): the slot is
                        // already an Il2CppObject*, so it rides the
                        // Append(object) ToString dispatch unchanged.
                        0x12 or 0x1C or 0x1D or 0x1F or 0x15 =>
                            I("il2cpp::runtime::icall_stringbuilder_append_o", 1, false, false, true,
                                new[] { "deref:r", "" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted arg 0x{vk:X2} not wired in M5 -- LOUD whitelist (net6+ AppendFormatted value carriers are the primitive/reference families); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)"),
                    };
                }
                if (count == 2)
                {
                    // (T, int32 width) -> _w; (T, string format) -> _f.
                    var isWidth = ps.Length > 1 && ps[1] == 0x08;
                    var isFmt = ps.Length > 1 && ps[1] == 0x0E;
                    if (!isWidth && !isFmt)
                        throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 2-arg signature not wired in M5 (ps[{string.Join(",", ps.Select(b => $"0x{b:X2}"))}]) -- LOUD whitelist (net6+ AppendFormatted<T> 2-arg forms are (T,int) and (T,string)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
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
                        // Reference generic argument: the boxed-object
                        // alignment/format trio (the value is already an
                        // object reference).
                        0x12 or 0x1C or 0x1D or 0x1F or 0x15 =>
                            I($"il2cpp::runtime::icall_stringbuilder_append_o{suffix}", 2, false, false, true,
                                new[] { "deref:r", "", isWidth ? "i" : "" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 2-arg value 0x{vk:X2} not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> 2-arg value carriers are the primitive/reference families); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)"),
                    };
                }
                if (count == 3)
                {
                    // (T, int32 width, string format) -> _wf.
                    if (ps.Length != 3 || ps[1] != 0x08 || ps[2] != 0x0E)
                        throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 3-arg signature not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> 3-arg form is (T,int,string)); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
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
                        // Reference generic argument: the boxed-object
                        // alignment + format trio.
                        0x12 or 0x1C or 0x1D or 0x1F or 0x15 =>
                            I("il2cpp::runtime::icall_stringbuilder_append_o_wf", 3, false, false, true,
                                new[] { "deref:r", "", "i", "" }),
                        _ => throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted 3-arg value 0x{vk:X2} not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> 3-arg value carriers are the primitive/reference families); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)"),
                    };
                }
                throw new NotSupportedException($"DefaultInterpolatedStringHandler.AppendFormatted with {count} arg(s) not wired in M5 -- LOUD whitelist (net6+ AppendFormatted<T> arities are 1/2/3); net48 RefSrc mscorlib/system/text/stringbuilder.cs:554-1037 (net6+ handler, no net48 counterpart)");
            case "ToStringAndClear":
                return count == 0
                    ? I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true, new[] { "deref:r" }, returnTypeName: "System.String")
                    : null;
            default:
                return null;
        }
    }
}
