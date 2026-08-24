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
                if (ps[0] == 0x08) return I("il2cpp::runtime::icall_stringbuilder_ctor_capacity", 1, false, false, true, new[] { "", "i" });
                if (ps[0] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_ctor_string", 1, false, false, true);
                throw new NotSupportedException($"StringBuilder ctor param type 0x{ps[0]:X2} not wired in M5");
            case "Append":
                return ps[0] switch
                {
                    0x0E => I("il2cpp::runtime::icall_stringbuilder_append_s", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    0x03 => I("il2cpp::runtime::icall_stringbuilder_append_c", 1, true, true, true, new[] { "", "c" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x08 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, true, true, true, new[] { "", "l" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x0D => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, true, true, true, new[] { "", "d" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x02 => I("il2cpp::runtime::icall_stringbuilder_append_b", 1, true, true, true, new[] { "", "b" }, null, false, false, false, "System.Text.StringBuilder"),
                    0x1C or 0x12 => I("il2cpp::runtime::icall_stringbuilder_append_o", 1, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
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
                // net48 overloads: (string, object[, object[, object]]); the
                // params-object[] / IFormatProvider forms are not wired (loud).
                if (count < 2 || ps[0] != 0x0E || ps[1] != 0x1C)
                    throw new NotSupportedException($"StringBuilder.AppendFormat signature not wired in M5");
                return count switch
                {
                    2 => I("il2cpp::runtime::icall_stringbuilder_append_format_1", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    3 => I("il2cpp::runtime::icall_stringbuilder_append_format_2", 3, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    4 => I("il2cpp::runtime::icall_stringbuilder_append_format_3", 4, true, true, true, null, null, false, false, false, "System.Text.StringBuilder"),
                    _ => throw new NotSupportedException($"StringBuilder.AppendFormat with {count} args not wired in M5"),
                };
            case "Replace":
                if (count != 2) throw new NotSupportedException($"StringBuilder.Replace with {count} args not wired in M5");
                if (ps[0] == 0x0E && ps[1] == 0x0E) return I("il2cpp::runtime::icall_stringbuilder_replace_s_s", 2, true, true, true, null, null, false, false, false, "System.Text.StringBuilder");
                if (ps[0] == 0x03 && ps[1] == 0x03) return I("il2cpp::runtime::icall_stringbuilder_replace_c_c", 2, true, true, true, new[] { "", "c", "c" }, null, false, false, false, "System.Text.StringBuilder");
                throw new NotSupportedException($"StringBuilder.Replace param type 0x{ps[0]:X2} not wired in M5");
            case "ToString":
                return I("il2cpp::runtime::icall_stringbuilder_to_string", 0, true, true, true);
            case "get_Length":
                return I("il2cpp::runtime::icall_stringbuilder_get_length", 0, true, false, true);
            // get_Chars(index): the indexer -- one UTF-16 unit out of the
            // buffer (int32 carrier); out-of-range -> IndexOutOfRangeException.
            case "get_Chars":
                return I("il2cpp::runtime::icall_stringbuilder_get_chars", 1, true, false, true, new[] { "", "i" });
            case "get_Capacity":
                // No capacity icall: read the runtime struct field directly.
                return I("il2cpp::runtime::il2cpp_stringbuilder_capacity", 0, true, false, true, new[] { "fcap" });
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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
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
                if (count == 1)
                {
                    return vk switch
                    {
                        0x08 => I("il2cpp::runtime::icall_stringbuilder_append_i", 1, false, false, true, new[] { "deref:r" }),
                        0x0A => I("il2cpp::runtime::icall_stringbuilder_append_l", 1, false, false, true, new[] { "deref:r", "l" }),
                        0x0D => I("il2cpp::runtime::icall_stringbuilder_append_d", 1, false, false, true, new[] { "deref:r", "d" }),
                        0x02 => I("il2cpp::runtime::icall_stringbuilder_append_b", 1, false, false, true, new[] { "deref:r", "b" }),
                        0x03 => I("il2cpp::runtime::icall_stringbuilder_append_c", 1, false, false, true, new[] { "deref:r", "c" }),
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
