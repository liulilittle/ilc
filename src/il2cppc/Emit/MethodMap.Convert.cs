namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Convert -- System.Convert + System.BitConverter icall mappings
// (il2cpp_convert.h). The BitConverter entries were folded into this domain
// at the partial split (both are conversion domains).
// Owners: System.Convert / System.BitConverter
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapConvert(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Convert":
                switch (name)
                {
                    case "ToInt32":
                        if (ps.Length == 1)
                            return ps[0] switch
                            {
                                0x0E => I("il2cpp::runtime::icall_convert_to_int32_string", 1, true, false, false, new[] { "s" }),
                                0x0A => I("il2cpp::runtime::icall_convert_to_int32_i8", 1, true, false, false, new[] { "l" }),
                                0x0D => I("il2cpp::runtime::icall_convert_to_int32_d", 1, true, false, false, new[] { "d" }),
                                0x03 => I("il2cpp::runtime::icall_convert_to_int32_char", 1, true, false, false),
                                _ => throw new NotSupportedException($"Convert.ToInt32 param type 0x{ps[0]:X2} not wired in M5"),
                            };
                        // M7: Convert.ToInt32(string, fromBase) -- the runtime
                        // throwing fromBase icall (il2cpp_convert.h,
                        // convt2 audit) reproduces net48 ParseNumbers.StringToInt
                        // semantics: base ∉ {2,8,10,16} -> ArgumentException,
                        // null -> 0, empty -> ArgumentOutOfRange, '0x' prefix
                        // (base 16), checked range -> OverflowException. The
                        // value string casts to Il2CppString* ("s"), the base
                        // to int32 ("i").
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                            return I("il2cpp::runtime::icall_convert_to_int32_string_base", 2, true, false, false, new[] { "s", "i" });
                        throw new NotSupportedException($"Convert.ToInt32 with {ps.Length} param(s) not wired in M7");
                    case "ToInt64":
                        if (ps[0] == 0x0E) return I("il2cpp::runtime::icall_convert_to_int64_string", 1, true, false, false, new[] { "s" }, "l");
                        throw new NotSupportedException($"Convert.ToInt64 param type 0x{ps[0]:X2} not wired in M5");
                    case "ToDouble":
                        return ps[0] switch
                        {
                            0x0E => I("il2cpp::runtime::icall_convert_to_double_string", 1, true, false, false, new[] { "s" }, "d"),
                            0x08 => I("il2cpp::runtime::icall_convert_to_double_i4", 1, true, false, false, null, "d"),
                            _ => throw new NotSupportedException($"Convert.ToDouble param type 0x{ps[0]:X2} not wired in M5"),
                        };
                    case "ToString":
                        // M7 (convt2 audit): Convert.ToString(int, toBase) --
                        // the throwing icall_convert_to_string_i4_base
                        // (il2cpp_convert.h) reproduces net48
                        // ParseNumbers.IntToString: base ∉ {2,8,10,16} ->
                        // ArgumentException; base 10 signed, bases 2/8/16
                        // uint32 two's-complement with lowercase hex. Both
                        // params cast to int32 ("i"); the result is a string.
                        if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                            return I("il2cpp::runtime::icall_convert_to_string_i4_base", 2, true, true, false, new[] { "i", "i" });
                        return ps[0] switch
                        {
                            0x08 => I("il2cpp::runtime::icall_convert_to_string_i4", 1, true, true, false),
                            0x0A => I("il2cpp::runtime::icall_convert_to_string_i8", 1, true, true, false, new[] { "l" }),
                            0x0D => I("il2cpp::runtime::icall_convert_to_string_d", 1, true, true, false, new[] { "d" }),
                            0x02 => I("il2cpp::runtime::icall_convert_to_string_bool", 1, true, true, false, new[] { "b" }),
                            // M5 wiring: Convert.ToString(char) -> the char formatter.
                            0x03 => I("il2cpp::runtime::icall_char_to_string", 1, true, true, false, new[] { "c" }),
                            _ => throw new NotSupportedException($"Convert.ToString param type 0x{ps[0]:X2} not wired in M5"),
                        };
                    case "ToBoolean":
                        if (ps[0] == 0x08) return I("il2cpp::runtime::icall_convert_to_bool_i4", 1, true, false, false);
                        throw new NotSupportedException($"Convert.ToBoolean param type 0x{ps[0]:X2} not wired in M5");
                    case "ToChar":
                        if (ps[0] == 0x08) return I("il2cpp::runtime::icall_convert_to_char_i4", 1, true, false, false);
                        throw new NotSupportedException($"Convert.ToChar param type 0x{ps[0]:X2} not wired in M5");
                    default: return null;
                }
            case "System.BitConverter":
                if (name == "DoubleToInt64Bits" && ps.Length == 1)
                    return I("il2cpp_double_bits", 1, true, false, false, new[] { "d" }, "l");
                if (name == "Int64BitsToDouble" && ps.Length == 1)
                    return I("il2cpp_double_from_bits", 1, true, false, false, new[] { "l" }, "d");
                return null;
            default: return null;
        }
    }
}
