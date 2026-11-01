namespace Il2Cpp.Transpiler.Emit;

using System.Reflection;
using System.Reflection.Metadata;

//------------------------------------------------------------------------------
// MethodMap.ValueType -- the boxed numeric value types' value-semantics
// surface (il2cpp_boxing_ext2.h / value_semantics domain): parameterless
// ToString, Parse, Equals(object) -> boxed equality, GetHashCode. NOTE:
// ToString(string format) for Int32/Int64/Double lives in
// MethodMap.Format.cs (the dispatch switch tries Format first for those
// owners).
// Owners: System.Int32 / System.Int64 / System.Boolean / System.Char /
//         System.Double / System.Single / System.Byte / System.SByte /
//         System.Int16 / System.UInt16 / System.UInt32 / System.UInt64
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapValueType(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Int32":
                if (name == "ToString")
                {
                    if (ps.Length == 0)
                        return I("il2cpp::runtime::icall_int32_to_string", 0, true, true, true,
                            new[] { receiverIsAddress ? "deref:i" : "unbox:i" });
                    return null; // ToString(string format) is handled by TryMapFormat
                }
                if (name == "Parse" && ps.Length == 1)
                    return I("il2cpp::runtime::icall_string_parse_int", 1, true, false, false, new[] { "s" });
                // M7 ref/out wiring: TryParse(string, out int32) -- the BYREF
                // element byte (ReadParamByte consumes the 0x10 marker) is the
                // int32 slot (0x08); the ldloca address passes through with
                // the typed "addr:i" cast (Guid.TryParse's "" passthrough
                // would hit the emitter's blanket uint8_t* cast). The 4-arg
                // NumberStyles/IFormatProvider overloads fall through to the
                // generated-name path (LOUD at C++ build).
                // Cast note: the text parameter of the il2cpp_parse.h
                // try_parse family is Il2CppObject* (NOT Il2CppString* --
                // the icalls cast internally, see il2cpp_parse.h:75), so the
                // "s" reinterpret would fail to compile against the object
                // slot; the "" passthrough is the exact ABI.
                if (name == "TryParse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_try_parse_int32", 2, true, false, false, new[] { "", "addr:i" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int32_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:i" : "unbox:i" });
                // GN-N3 (p-iface): Int32.CompareTo(int32) -- the net48 sign
                // verdict. The receiver arrives as the value-slot ADDRESS (the
                // ldflda on Item.N); icall_int32_compare_to reads the slot and
                // compares the by-value argument (same receiver ABI as
                // icall_value_equals_i4 -- the arg cast "i" is the value).
                if (name == "CompareTo" && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_int32_compare_to", 1, true, false, true,
                        new[] { "addr:i", "i" });
                // M5 wiring: Int32.Equals(object) on a boxed receiver -- the
                // boxed-value equality dispatcher (boxingext2 domain). D5
                // cov2: Equals(int32) on an UNBOXED receiver (implicit-this
                // arrives as an address) routes to the value-equals icall
                // (ps[0] is the ARG element byte -- 0x08 = the int32 form).
                if (name == "CompareTo" && ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.Int32\"" });
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x08 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Int64":
                if (name == "ToString")
                {
                    if (ps.Length == 0)
                        return I("il2cpp::runtime::icall_int64_to_string", 0, true, true, true,
                            new[] { receiverIsAddress ? "deref:l" : "unbox:l" });
                    return null; // ToString(string format) is handled by TryMapFormat
                }
                if (name == "Parse" && ps.Length == 1)
                    return I("il2cpp::runtime::icall_string_parse_long", 1, true, false, false, new[] { "s" }, "l");
                if (name == "TryParse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0A)
                    return I("il2cpp::runtime::icall_try_parse_int64", 2, true, false, false, new[] { "", "addr:l" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int64_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:l" : "unbox:l" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0A => I("il2cpp::runtime::icall_int64_compare_to", 1, true, false, true, new[] { "addr:l", "l" }),
                        0x1C => I("il2cpp::runtime::icall_int64_compare_to_obj", 1, true, false, true, new[] { "addr:l", "" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0A => I("il2cpp::runtime::icall_value_equals_i8", 1, true, false, true, new[] { "", "l" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Boolean":
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_bool_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:b" : "unbox:b" });
                // M7 ref/out wiring: TryParse(string, out bool) -- the out
                // bool lives in an int32 slot (icall_try_parse_bool writes an
                // int32_t 1/0), so the address cast is "addr:i".
                if (name == "TryParse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                    return I("il2cpp::runtime::icall_try_parse_bool", 2, true, false, false, new[] { "", "addr:i" });
                // bool.Parse(string): the strict parser (null -> ANE, unlike
                // Convert.ToBoolean's null -> false).
                if (name == "Parse" && ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_bool_parse", 1, true, false, false, new[] { "s" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_bool_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:b" : "unbox:b" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x02 => I("il2cpp::runtime::icall_int32_compare_to", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.Boolean\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x02 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Char":
                // binrw3 residual: the ADDRESS form reads the NATIVE cell
                // width (deref:u2 -- ldelema of char[] points at a 2-byte
                // cell; deref:i over-reads). An int32-slot char keeps high
                // bytes zero, so the narrow read is exact in both shapes.
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_char_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:u2" : "unbox:c" });
                // zmatrix: Char.GetHashCode had no arm (the narrow hash family is
                // icall_narrow_get_hash_code kind 4).
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:u2" : "unbox:c" }, appendLiterals: new[] { "4" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x03 => I("il2cpp::runtime::icall_primitive_compare_to_diff", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.Char\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x03 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Double":
                if (name == "ToString")
                {
                    if (ps.Length == 0)
                        return I("il2cpp::runtime::icall_double_to_string", 0, true, true, true,
                            new[] { receiverIsAddress ? "deref:d" : "unbox:d" });
                    return null; // ToString(string format) is handled by TryMapFormat
                }
                if (name == "Parse" && ps.Length == 1)
                    return I("il2cpp::runtime::icall_string_parse_double", 1, true, false, false, new[] { "s" }, "d");
                // double.Parse(string, NumberStyles[, IFormatProvider]) and the
                // TryParse twins: the styles/provider-aware icalls added with
                // the group-separator fix (il2cpp_parse_styles.h). The provider
                // is a culture-NAME string or nullptr ("no provider" == the
                // current culture), so it passes through UNCAST; the missing
                // provider on the (string, NumberStyles) form is injected as a
                // nullptr literal, and the missing styles on the
                // (string, IFormatProvider) form as NumberStyles.Float (the
                // net48 provider-only default for floating point).
                if (name == "Parse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_string_parse_double_styles", 2, true, false, false,
                        new[] { "s", "i", "" }, "d", argOrder: new[] { 0, 1, -1 });
                if (name == "Parse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_string_parse_double_styles", 2, true, false, false,
                        new[] { "s", "lit:231", "" }, "d", argOrder: new[] { 0, 1, 1 });
                if (name == "Parse" && ps.Length == 3 && ps[0] == 0x0E && ps[1] == 0x11 && ps[2] == 0x12)
                    return I("il2cpp::runtime::icall_string_parse_double_styles", 3, true, false, false,
                        new[] { "s", "i", "" }, "d");
                if (name == "TryParse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0D)
                    return I("il2cpp::runtime::icall_try_parse_double", 2, true, false, false, new[] { "", "addr:d" });
                if (name == "TryParse" && ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x11 && ps[2] == 0x12 && ps[3] == 0x0D)
                    return I("il2cpp::runtime::icall_try_parse_double_styles", 4, true, false, false,
                        new[] { "", "i", "", "addr:d" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_double_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:d" : "unbox:d" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0D => I("il2cpp::runtime::icall_double_compare_to", 1, true, false, true, new[] { "addr:d", "d" }),
                        0x1C => I("il2cpp::runtime::icall_double_compare_to_obj", 1, true, false, true, new[] { "addr:d", "" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0D => I("il2cpp::runtime::icall_value_equals_f8", 1, true, false, true, new[] { "", "d" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Single":
                // float values live in double slots; Single.ToString formats like
                // double (the runtime prints double semantics).
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_single_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:sf" : "unbox:d" });
                // zmatrix: Single.GetHashCode had NO arm here (Int32/Int64/Double
                // have one), so `a.First().GetHashCode()` on a float[] fell to the
                // generated-name path and emitted a call to a nonexistent
                // System_Single_GetHashCode_<hash> symbol -- a C++ compile failure.
                // net48 Single.GetHashCode is the 32-bit float pattern (0 for +-0);
                // the value rides the double carrier.
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_single_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:sf" : "unbox:d" });
                // float.Parse(string): the value rides a double slot and the
                // float domain is a subset of double -- the double parser is
                // exact for every float-representable text.
                if (name == "Parse" && ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_string_parse_double", 1, true, false, false, new[] { "s" }, "d");
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0C => I("il2cpp::runtime::icall_single_compare_to", 1, true, false, true, new[] { "addr:d", "d" }),
                        0x1C => I("il2cpp::runtime::icall_single_compare_to_obj", 1, true, false, true, new[] { "addr:d", "" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0C => I("il2cpp::runtime::icall_value_equals_f4", 1, true, false, true, new[] { "", "d" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            // M5 wiring: the remaining boxed numeric types only need Equals(object)
            // in the current case surface (boxext2 e2e). The runtime dispatches the
            // box classes itself (il2cpp_boxing_ext2.h).
            case "System.Byte":
                // M8 parsecase unblock (2026-08-08): Byte.ToString() formats
                // via the int32 formatter (byte range 0..255 -> identical
                // decimal output). The ADDRESS form reads the NATIVE cell
                // width (deref:u1 -- binrw3 residual: ldelema of byte[]
                // points at a 1-byte cell; deref:i over-read neighbor bytes,
                // b4=12345). An int32-slot byte keeps high bytes zero, so the
                // narrow read is exact in both shapes. The boxed form unboxes
                // the payload via the boxing_ext2 dispatcher.
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int32_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:u1" : "unbox:i" });
                // M7 ref/out wiring: TryParse(string, out byte) -- byte lives
                // in an int32 slot; icall_try_parse_byte takes int32_t*. The
                // BYREF element byte is 0x05 (ELEMENT_TYPE_U1 -- exe probe
                // sig=[0 2 2 14 16 5]); 0x01 is VOID, never the element.
                if (name == "TryParse" && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x05)
                    return I("il2cpp::runtime::icall_try_parse_byte", 2, true, false, false, new[] { "", "addr:i" });
                // zmatrix: Byte.GetHashCode had no arm (the narrow hash family is
                // icall_narrow_get_hash_code kind 1).
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:u1" : "unbox:i" }, appendLiterals: new[] { "1" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x05 => I("il2cpp::runtime::icall_primitive_compare_to_diff", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.Byte\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x05 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.SByte":
                // P0-narrow (2026-10-22): SByte.ToString() had NO wiring here
                // (only Byte did), so the call fell through to the
                // generated-name path and emitted a call to a nonexistent
                // System_SByte_ToString_<hash> symbol -- a SILENT transpile
                // success followed by a C++ compile failure. SByte formats
                // through the int32 formatter (the Byte precedent) with the
                // ADDRESS form reading the NATIVE SIGNED 1-byte cell
                // (deref:i1 -- the sbyte[] ldelema cell; the unsigned deref:u1
                // would print 253 for -3). An int32 slot keeps the
                // sign-extended value, so the narrow read is exact in both
                // shapes.
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int32_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:i1" : "unbox:i" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:i1" : "unbox:i" }, appendLiterals: new[] { "0" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x04 => I("il2cpp::runtime::icall_primitive_compare_to_diff", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.SByte\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x04 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.Int16":
                // P0-narrow (2026-10-22): Int16.ToString() had NO wiring here
                // (only Byte did) -- see the SByte arm above. The ADDRESS form
                // reads the NATIVE SIGNED 2-byte cell (deref:i2 -- the short[]
                // ldelema cell; deref:i would over-read neighbour bytes and an
                // unsigned read would print 65531 for -5).
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int32_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:i2" : "unbox:i" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:i2" : "unbox:i" }, appendLiterals: new[] { "2" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x06 => I("il2cpp::runtime::icall_primitive_compare_to_diff", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.Int16\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x06 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.UInt16":
                // P0-narrow (2026-10-22): UInt16.ToString() had NO wiring here
                // (only Byte did) -- see the SByte arm above. The ADDRESS form
                // reads the NATIVE UNSIGNED 2-byte cell (deref:u2 -- the
                // ushort[] ldelema cell); the full 0..65535 range must not be
                // sign-extended, so deref:i2 would print -1 for 65535.
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int32_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:u2" : "unbox:i" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:u2" : "unbox:i" }, appendLiterals: new[] { "3" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x07 => I("il2cpp::runtime::icall_primitive_compare_to_diff", 1, true, false, true, new[] { "addr:i", "i" }),
                        0x1C => I("il2cpp::runtime::icall_int32_compare_to_obj", 1, true, false, true, new[] { "addr:i", "" }, appendLiterals: new[] { "\"System.UInt16\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x07 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.UInt32":
                // Decimal-domain C44 (decgap): UInt32.ToString() -- the value
                // lives in an int32 slot (or a boxed int32 payload); the bit
                // pattern passes to the uint32 icall (implicit conversion is
                // modulo, bit-identical), so 0xFFFFFFFF prints "4294967295",
                // never the signed -1 (values >= 2^31 would sign-extend
                // through icall_int32_to_string).
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_uint32_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:ui" : "unbox:ui" });
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_narrow_get_hash_code", 1, true, false, true,
                        new[] { receiverIsAddress ? "deref:ui" : "unbox:ui" }, appendLiterals: new[] { "5" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x09 => I("il2cpp::runtime::icall_uint32_compare_to", 1, true, false, true, new[] { "addr:ui", "i" }),
                        0x1C => I("il2cpp::runtime::icall_uint32_compare_to_obj", 1, true, false, true, new[] { "addr:ui", "" }, appendLiterals: new[] { "\"System.UInt32\"" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x09 => I("il2cpp::runtime::icall_value_equals_i4", 1, true, false, true, new[] { "", "i" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            case "System.UInt64":
                // Decimal-domain C44 (decgap): UInt64.ToString() -- the value
                // lives in an int64 slot (or a boxed int64 payload); the full
                // range (>= INT64_MAX) needs the unsigned icall's digit loop.
                if (name == "ToString" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_uint64_to_string", 0, true, true, true,
                        new[] { receiverIsAddress ? "deref:ul" : "unbox:ul" });
                // UInt64.GetHashCode == low32 ^ high32, the SAME rule as
                // Int64.GetHashCode (the bit pattern is width-identical), so the
                // int64 hash icall serves it (zmatrix ul family).
                if (name == "GetHashCode" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_int64_get_hash_code", 0, true, false, true,
                        new[] { receiverIsAddress ? "deref:ul" : "unbox:ul" });
                if (name == "CompareTo" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0B => I("il2cpp::runtime::icall_uint64_compare_to", 1, true, false, true, new[] { "addr:ul", "l" }),
                        0x1C => I("il2cpp::runtime::icall_uint64_compare_to_obj", 1, true, false, true, new[] { "addr:ul", "" }),
                        _ => null,
                    };
                if (name == "Equals" && ps.Length == 1)
                    return ps[0] switch
                    {
                        0x0B => I("il2cpp::runtime::icall_value_equals_i8", 1, true, false, true, new[] { "", "l" }),
                        0x1C => I("il2cpp::runtime::icall_boxed_equals_ext2", 1, true, false, true),
                        _ => null,
                    };
                return null;
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapValueTypeGetHashCode -- System.ValueType::GetHashCode (the
    // constrained-callvirt default for value types that do NOT override the
    // member; the audit section 5 #5 gap). net48 DOCUMENTED rule (netcore
    // ValueType.cs:89-98): "look for the first non-static field and get its
    // hashcode; if the type has no non-static fields, return the hashcode of
    // the type." The PROBED net48 actual behavior is a process-randomized
    // whole-struct hash (il2cpp_value_semantics.h:107-112) -- not
    // reproducible for deterministic AOT -- so this wiring implements the
    // DOCUMENTED rule deterministically.
    //
    // CONSTRAINED-SHAPE CONTRACT: `constrained. T; callvirt
    // System.ValueType::GetHashCode()` tracks the receiver type T in
    // receiverType; T is a USER struct TypeDef (BCL structs like
    // Guid/DateTime/Decimal OVERRIDE GetHashCode, so Roslyn emits THEIR
    // owner, never System.ValueType). The runtime has no value-type
    // descriptors, and the emitter can inject only literal 0 / ParamCount
    // (CodeGenerator.EmitIcallCallArgs), so the (typeHash, fieldKind, data)
    // icall_value_type_get_hash_code contract is NOT fully wireable -- the
    // resolver selects a per-kind runtime target instead:
    //
    //   first field kind                 runtime target + receiver cast
    //   --------------------------------------------------------------------
    //   4-byte value (i4/u4/bool/char/   icall_int32_get_hash_code + deref:i
    //     i1/u1/i2/u2/enum/nested-struct)  (Int32.GetHashCode identity)
    //   8-byte value (i8/u8)             icall_int64_get_hash_code + deref:l
    //                                    (low 32 XOR high 32)
    //   float/double (r4/r8)             icall_double_get_hash_code + deref:d
    //                                    (bit pattern; +0.0/-0.0 -> 0)
    //   GC reference                     icall_value_type_get_hash_code_ref
    //                                    + "" (object hash; null -> 0)
    //   no instance fields               icall_value_type_get_hash_code +
    //                                    PrependCountArg + PrependZeroArg ->
    //                                    (0, 0, data) -> fieldKind 0 returns
    //                                    typeHash 0 (the type hash constant
    //                                    is not injectable; documented
    //                                    divergence: field-less structs hash
    //                                    to 0 -- equality-consistent, since
    //                                    all instances of such a type are
    //                                    equal)
    //
    // The receiver address always passes through (the constrained shape is an
    // ldloca ADDRESS). Value-typed receivers reuse the existing per-type hash
    // icalls; only the reference case needs a dedicated runtime icall.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapValueTypeGetHashCode(MetadataReader md, string owner, string name,
        byte[] ps, int count, byte[]? genArgs, string? receiverType, bool receiverIsAddress,
        string? arg0Type, string? arg1Type)
    {
        // P6: ValueType.GetHashCodeOfPtr -- internal helper that hashes a value
        // type by pointer (the GC.cs entry is dead code because System.ValueType
        // dispatches here, not to TryMapGC).
        if (owner == "System.ValueType" && name == "GetHashCodeOfPtr")
            return I("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", 1, true, false, false);
        if (owner != "System.ValueType" || name != "GetHashCode" || ps.Length != 0)
            return null;
        // The FOLD resolves the callvirt TWICE: the first pass (StackFold.cs
        // line 1366) carries NO receiver hint, then the constrained marker
        // supplies the receiver type and the call is re-resolved (StackFold.cs
        // line 1501). A throw on the hint-less pass would break EVERY
        // constrained call -- defer (return null) so the re-resolve with the
        // hint reaches the metadata classification below. A receiver that is
        // genuinely untracked (no constrained marker) keeps the pre-existing
        // generated-name fallback (a self-identifying C++ link error).
        // A receiver that is genuinely untracked (no constrained marker --
        // e.g. GetHashCode on a box result) cannot classify: route to the
        // OfPtr icall (declared in stubs.h, defined in stubs.cpp) instead of
        // the generated-name fallback (a self-identifying C++ link error).
        // The re-resolve override below re-routes tracked receivers to the
        // precise per-kind icall, so this arm only survives hint-less calls.
        if (receiverType == null)
            return I("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", 0, true, false, true, new[] { "" });
        var kind = ClassifyValueTypeFirstField(md, receiverType);
        return kind switch
        {
            1 => I("il2cpp::runtime::icall_int32_get_hash_code", 0, true, false, true, new[] { "deref:i" }),
            3 => I("il2cpp::runtime::icall_int64_get_hash_code", 0, true, false, true, new[] { "deref:l" }),
            4 => I("il2cpp::runtime::icall_double_get_hash_code", 0, true, false, true, new[] { "deref:d" }),
            2 => I("il2cpp::runtime::icall_value_type_get_hash_code_ref", 0, true, false, true, new[] { "" }),
            0 => I("il2cpp::runtime::icall_value_type_get_hash_code", 0, true, false, true, new[] { "" },
                prependCount: true, prependZero: true),
            _ => throw new NotSupportedException(
                $"LOUD whitelist (no runtime descriptor/model): System.ValueType::GetHashCode for receiver type " +
                $"{receiverType}: unclassified first-field kind {kind} -- the classifier (ClassifyValueTypeFirstField) covers the " +
                "int32/uint32/bool/char/narrow, int64/uint64, float/double, GC-reference and nested-struct first-field kinds only " +
                "[Reference Source: net48 mscorlib System.ValueType.GetHashCode]"),
        };
    }

    // The receiver type's FIRST INSTANCE field kind
    //   (0 none / 1 4-byte value / 3 8-byte value / 4 float-double /
    //    2 GC reference),
    // read from the user-struct TypeDef metadata (the constrained receiver is
    // always a TypeDef in the current assembly; a miss is LOUD -- the 
    // signature for a nested struct's full name is not recovered by
    // GetFullName(TypeDefinitionHandle), so nested types fall through here).
    private static int ClassifyValueTypeFirstField(MetadataReader md, string receiverType)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            if (md.GetFullName(tdh) != receiverType) continue;
            foreach (var fh in md.GetTypeDefinition(tdh).GetFields())
            {
                var f = md.GetFieldDefinition(fh);
                if ((f.Attributes & FieldAttributes.Static) != 0) continue;
                var fsig = md.GetBlobReader(f.Signature);
                if (fsig.Length < 2 || fsig.ReadByte() != 0x06) continue; // FIELD marker (ECMA II.23.1.5)
                var elem = ReadParamByte(md, ref fsig);
                return elem switch
                {
                    // 4-byte value slots (int32/uint32/bool/char/i1/u1/i2/u2).
                    0x08 or 0x09 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 => 1,
                    // 8-byte value slots (int64/uint64).
                    0x0A or 0x0B => 3,
                    // float/double (Single lives in a double slot).
                    0x0C or 0x0D => 4,
                    // GC reference slots (string/class/object/array/typehandle).
                    0x0E or 0x12 or 0x1C or 0x1D or 0x1F or 0x40 => 2,
                    // Nested valuetype / generic struct: the netcore rule boxes
                    // and hashes it (ValueTypeOverride); M-scope simplifies to
                    // the low-4-bytes value hash.
                    0x11 or 0x15 => 1,
                    // VAR/MVAR (a GENERIC struct's T field, e.g. GBox<T>.Value):
                    // the open TypeDef metadata cannot name the closed argument
                    // (T=int vs T=long both encode 0x13), so M-scope applies the
                    // same low-4-bytes value-hash simplification as 0x11/0x15.
                    // (The classifier is a real function; this arm keeps it from
                    // throwing on the generic-struct shape -- the documented
                    // M-scope divergence, not a silent truncation.)
                    0x13 or 0x1E => 1,
                    _ => throw new NotSupportedException(
                        $"LOUD whitelist (no runtime descriptor/model): System.ValueType::GetHashCode for receiver type " +
                        $"{receiverType}: unsupported first-field element byte 0x{elem:X2} -- a pointer / function-pointer / native-int / " +
                        "byref first field has no value-hash descriptor (the netcore ValueTypeOverride boxes and hashes it) " +
                        "[Reference Source: net48 mscorlib System.ValueType.GetHashCode]"),
                };
            }
            return 0; // the struct has fields but none are instance fields
        }
        throw new NotSupportedException(
            $"LOUD whitelist (no runtime descriptor/model): System.ValueType::GetHashCode for receiver type {receiverType}: no " +
            "TypeDef found to classify its first instance field -- a BCL struct (which overrides GetHashCode) or a nested struct whose " +
            "full name is not recovered by GetFullName routes here; only a top-level user struct has a classified first field " +
            "[Reference Source: net48 mscorlib System.ValueType.GetHashCode]");
    }
}

