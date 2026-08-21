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
}
