namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Convert -- System.Convert + System.BitConverter icall mappings
// (il2cpp_convert.h). The BitConverter entries were folded into this domain
// at the partial split (both are conversion domains).
// Owners: System.Convert / System.BitConverter
//
// CONVERT-SWEEP (edit_convert, 2026-08-22): the value overloads of
// ToInt32/ToInt64/ToDouble/ToBoolean/ToChar now cover every source whose
// net48 semantics an EXISTING runtime icall reproduces exactly. The wiring
// rules, each proven against the net48 baseline pinned in il2cpp_convert.h /
// il2cpp_conv.h:
//   * int-family sources (sbyte..uint) ride int32 slots; long/ulong ride
//     int64 slots; float rides a double slot holding the exact float value.
//   * Widening/identity integer sources reuse the CHECKED int64 helpers via
//     the "l" sign-extending cast: every int-family value survives a
//     sign-extension unchanged, and a uint bit pattern >= 2^31 goes negative,
//     which fails any [0, max] range test -- exactly the OverflowException
//     net48 throws for those inputs.
//   * ulong sources use the "q" reinterpret cast into uint64_t params (the
//     batch 154b contract: negative bit patterns fail the checked test).
//   * Zero-extension sources (byte/ushort/char/bool/uint -> 64-bit) use the
//     icall_conv_u8_i4 zero-extending helper ((long)uint.MaxValue ==
//     4294967295, never -1).
//   * Float/double sources of the ROUNDING conversions (ToInt64(double),
//     ToSingle/ToDouble from decimal) stay LOUD-rejected: the conv family
//     TRUNCATES doubles before its range check while net48 Convert ROUNDS
//     half-to-even first -- reusing them would silently change results
//     (Convert.ToInt64(3.5) == 4, conv.ovf.i8(3.5) == 3).
//   * Sources net48 refuses with InvalidCastException (bool/char ->
//     floating-point targets, char/float/double/decimal -> bool, bool ->
//     char) throw LOUD with an ICE note: no existing icall raises
//     InvalidCastException, so they cannot be wired today.
// The full per-overload sweep table + needs-new-icall list lives in
// tmp/reports/edit_convert.md.
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
                                // p0conv2 (D2): Convert.ToInt32(decimal) ROUNDS
                                // half-to-even then range-checks (net48 probe:
                                // ToInt32(3.5m)==4, ToInt32(12.9m)==13,
                                // ToInt32(-0.9m)==-1, ToInt32(2147483647.6m)
                                // -> OverflowException). The truncating
                                // icall_decimal_to_int32 is the EXPLICIT-CAST
                                // semantics and must NOT be used here.
                                0x11 => I("il2cpp::runtime::icall_decimal_to_int32_round", 1, true, false, false, new[] { "dec" }),
                                // Sweep: ALL int-family sources (sbyte/byte/short/
                                // ushort/int/uint/long/ulong) through the checked
                                // int64 helper. "l" sign-extends the slot value;
                                // in-range inputs pass unchanged (identity/widening),
                                // out-of-range (incl. uint >= 2^31, whose bit
                                // pattern reads negative) throw OverflowException --
                                // the exact net48 contract for every one of these.
                                0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x0A or 0x0B
                                    => I("il2cpp::runtime::icall_convert_to_int32_i8", 1, true, false, false, new[] { "l" }),
                                0x0D => I("il2cpp::runtime::icall_convert_to_int32_d", 1, true, false, false, new[] { "d" }),
                                // Sweep: float rides a double slot carrying the exact
                                // float value; the rounding icall's half-to-even on
                                // that value IS net48 Convert.ToInt32(float).
                                0x0C => I("il2cpp::runtime::icall_convert_to_int32_d", 1, true, false, false, new[] { "d" }),
                                0x03 => I("il2cpp::runtime::icall_convert_to_int32_char", 1, true, false, false),
                                // net9/10 E2E icallcase: Convert.ToInt32(bool).
                                0x02 => I("il2cpp::runtime::icall_convert_to_int32_bool", 1, true, false, false, new[] { "b" }),
                                // BATCH 8: Convert.ToInt32(object) -- the runtime
                                // dispatches on the boxed value's IConvertible
                                // implementation (null -> 0).
                                0x1C => I("il2cpp::runtime::icall_convert_to_int32_obj", 1, true, false, false, new[] { "" }),
                                _ => throw new NotSupportedException($"Convert.ToInt32 param type 0x{ps[0]:X2} not wired (see tmp/reports/edit_convert.md needs-new list)"),
                            };
                        // M7: Convert.ToInt32(string, fromBase) -- the runtime
                        // throwing fromBase icall (il2cpp_convert.h,
                        // convt2 audit) reproduces net48 ParseNumbers.StringToInt
                        // semantics: base not in {2,8,10,16} -> ArgumentException,
                        // null -> 0, empty -> ArgumentOutOfRange, '0x' prefix
                        // (base 16), checked range -> OverflowException. The
                        // value string casts to Il2CppString* ("s"), the base
                        // to int32 ("i").
                        if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                            return I("il2cpp::runtime::icall_convert_to_int32_string_base", 2, true, false, false, new[] { "s", "i" });
                        throw new NotSupportedException($"Convert.ToInt32 with {ps.Length} param(s) not wired in M7");
                    case "ToInt64":
                        if (ps.Length == 1)
                            return ps[0] switch
                            {
                                0x0E => I("il2cpp::runtime::icall_convert_to_int64_string", 1, true, false, false, new[] { "s" }, "l"),
                                // p0conv2 (D2): Convert.ToInt64(decimal) ROUNDS
                                // half-to-even then range-checks (net48 probe:
                                // ToInt64(3.5m)==4, ToInt64(-3.5m)==-4,
                                // ToInt64(9223372036854775807.6m)
                                // -> OverflowException). NOT the truncating
                                // explicit-cast icall_decimal_to_int64.
                                0x11 => I("il2cpp::runtime::icall_decimal_to_int64_round", 1, true, false, false, new[] { "dec" }, "l"),
                                // Sweep: sign-extension sources (int/sbyte/short):
                                // icall_conv_i8_i4 widens exactly, never throws.
                                0x04 or 0x06 or 0x08
                                    => I("il2cpp::runtime::icall_conv_i8_i4", 1, true, false, false, new[] { "i" }, "l"),
                                // Sweep: zero-extension sources (byte/ushort/char/
                                // bool/uint): icall_conv_u8_i4 zero-extends the
                                // slot pattern ((long)uint.MaxValue == 4294967295).
                                0x02 or 0x03 or 0x05 or 0x07 or 0x09
                                    => I("il2cpp::runtime::icall_conv_u8_i4", 1, true, false, false, new[] { "i" }, "l"),
                                // Sweep: long identity through the i8<->u8
                                // reinterpret helper ("q" cast keeps the 64-bit
                                // pattern; the helper casts it back) -- bit-exact.
                                0x0A => I("il2cpp::runtime::icall_conv_i8_u8", 1, true, false, false, new[] { "q" }, "l"),
                                // Sweep: ulong -> long is CHECKED on net48
                                // (values >= 2^63 throw); the ovf form tests
                                // exactly that on the raw slot pattern.
                                0x0B => I("il2cpp::runtime::icall_conv_ovf_i8_u8", 1, true, false, false, new[] { "q" }, "l"),
                                // T1 parity: net48 rounds double/float half-to-even
                                // then checks the int64 range (the landed
                                // icall_convert_to_int64_d; the conv-family
                                // helpers truncate, which silently differs).
                                // Float rides the double slot ("d" widening,
                                // the ToInt32 0x0C precedent).
                                0x0C or 0x0D => I("il2cpp::runtime::icall_convert_to_int64_d", 1, true, false, false, new[] { "d" }, "l"),
                                // BATCH 8: Convert.ToInt64(object) -- the boxed
                                // dispatch (null -> 0; ulong checked).
                                0x1C => I("il2cpp::runtime::icall_convert_to_int64_obj", 1, true, false, false, new[] { "" }, "l"),
                                _ => throw new NotSupportedException($"Convert.ToInt64 param type 0x{ps[0]:X2} not wired (decimal/string+provider on the needs-new list)"),
                            };
                        throw new NotSupportedException($"Convert.ToInt64 with {ps.Length} param(s) not wired");
                    case "ToDouble":
                        return ps[0] switch
                        {
                            0x0E => I("il2cpp::runtime::icall_convert_to_double_string", 1, true, false, false, new[] { "s" }, "d"),
                            0x08 => I("il2cpp::runtime::icall_convert_to_double_i4", 1, true, false, false, null, "d"),
                            // p0conv2 (D2): Convert.ToDouble(decimal) is the exact
                            // VarR8FromDec conversion -- net48 probe confirms it is
                            // bit-identical to the explicit (double)decimal cast
                            // (0.1m, 1m/3m, decimal.MaxValue all agree), so the
                            // existing decimal->double icall serves it.
                            0x11 => I("il2cpp::runtime::icall_decimal_to_double", 1, true, false, false, new[] { "dec" }, "d"),
                            // Sweep: exact widenings from the small signed/unsigned
                            // integers (never throw, always exact).
                            0x04 or 0x05 or 0x06 or 0x07
                                => I("il2cpp::runtime::icall_conv_d_i4", 1, true, false, false, new[] { "i" }, "d"),
                            // Sweep: long -> double is exact below 2^53 and RNE
                            // above -- the documented icall_conv_d_i8 contract.
                            0x0A => I("il2cpp::runtime::icall_conv_d_i8", 1, true, false, false, new[] { "l" }, "d"),
                            // Sweep: ulong -> double RNE ("(double)ulong.MaxValue
                            // == 2^64", the net48 oracle pinned in il2cpp_conv.h);
                            // "q" feeds the uint64 param the raw slot pattern.
                            0x0B => I("il2cpp::runtime::icall_conv_d_u8", 1, true, false, false, new[] { "q" }, "d"),
                            // uint -> double: exact (uint32 fits the 53-bit
                            // mantissa); "u4" reinterprets the int32 carrier so
                            // uint >= 2^31 stays positive (batch 7).
                            0x09 => I("il2cpp::runtime::icall_conv_d_u4", 1, true, false, false, new[] { "u4" }, "d"),
                            // float -> double: the float source rides an
                            // already-widened double slot; icall_conv_d_d is the
                            // bit-exact identity (batch 7).
                            0x0C => I("il2cpp::runtime::icall_conv_d_d", 1, true, false, false, new[] { "d" }, "d"),
                            // BATCH 8: net48 Convert.ToDouble(bool) is
                            // `value ? 1.0 : 0.0` (oracle-pinned); the bool
                            // slot carries int32 0/1, so icall_conv_d_i4 is
                            // exact. char remains InvalidCastException.
                            0x02 => I("il2cpp::runtime::icall_conv_d_i4", 1, true, false, false, new[] { "i" }, "d"),
                            // BATCH 8: net48 throws InvalidCastException for the
                            // char source; the dedicated icall is the exact
                            // contract (probe-pinned).
                            0x03 => I("il2cpp::runtime::icall_convert_to_double_char", 1, true, false, false, new[] { "i" }, "d"),
                            // BATCH 8: Convert.ToDouble(object) -- the boxed
                            // dispatch (null -> 0.0; bool/char throw ICE, which
                            // the runtime icall reproduces).
                            0x1C => I("il2cpp::runtime::icall_convert_to_double_obj", 1, true, false, false, new[] { "" }, "d"),
                            // object / any remaining source: the edit_convert.md
                            // needs-new list.
                            _ => throw new NotSupportedException($"Convert.ToDouble param type 0x{ps[0]:X2} not wired (object on the edit_convert.md needs-new list)"),
                        };
                    case "ToString":
                        // M7 (convt2 audit): Convert.ToString(int, toBase) --
                        // the throwing icall_convert_to_string_i4_base
                        // (il2cpp_convert.h) reproduces net48
                        // ParseNumbers.IntToString: base not in {2,8,10,16} ->
                        // ArgumentException; base 10 signed, bases 2/8/16
                        // uint32 two's-complement with lowercase hex. Both
                        // params cast to int32 ("i"); the result is a string.
                        if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                            return I("il2cpp::runtime::icall_convert_to_string_i4_base", 2, true, true, false, new[] { "i", "i" });
                        // P0-narrow (2026-10-22): the narrow integer sources
                        // (sbyte/byte/short/ushort) had NO arm -- the fold
                        // threw "Convert.ToString param type 0x04 not wired in
                        // M5" and the WHOLE method was dropped (a runtime
                        // NotSupportedException stub) for a perfectly legal
                        // statement. net48 (probed): Convert.ToString(narrow)
                        // and Convert.ToString(narrow, provider) both return
                        // the plain decimal text (the provider is ignored;
                        // sbyte -1 -> "-1", byte 255 -> "255", ushort 65535 ->
                        // "65535"), which is exactly icall_int32_to_string on
                        // the sign/zero-extended value -- the same target the
                        // int32 source uses.
                        if (ps.Length == 1 && ps[0] is 0x04 or 0x05 or 0x06 or 0x07)
                            return I("il2cpp::runtime::icall_convert_to_string_i4", 1, true, true, false);
                        // Convert.ToString(object): net48 returns "" for null
                        // and the value's ToString() text otherwise. The
                        // boxed dispatch is icall_object_to_string, which
                        // THROWS on null, so the null-safe wrapper
                        // icall_convert_to_string_obj is the exact contract.
                        // The STRING source rides the same wrapper: net48
                        // Convert.ToString((string)null) is "" and a non-null
                        // string returns itself (icall_object_to_string's
                        // identity arm).
                        if (ps.Length == 1 && ps[0] is 0x1C or 0x0E)
                            return I("il2cpp::runtime::icall_convert_to_string_obj", 1, true, true, false);
                        // Convert.ToString(x, IFormatProvider): net48 IGNORES
                        // the provider for every primitive source (probed for
                        // int/long/double/sbyte: all identical to the 1-arg
                        // form), so the 2-arg form drops it and routes to the
                        // same default formatter. NOTE: before this arm the
                        // 2-arg shapes fell through to the 1-arg switch below,
                        // which set ParamCount 1 while the IL pushes 2 -- a
                        // stack desync that emitted the PROVIDER as the value
                        // (silent wrong value for int/long/double too).
                        if (ps.Length == 2 && ps[1] == 0x12)
                            return ps[0] switch
                            {
                                0x02 => I("il2cpp::runtime::icall_convert_to_string_bool", 2, true, true, false,
                                    new[] { "b", "drop" }),
                                0x03 => I("il2cpp::runtime::icall_char_to_string", 2, true, true, false,
                                    new[] { "c", "drop" }),
                                0x04 or 0x05 or 0x06 or 0x07 or 0x08
                                    => I("il2cpp::runtime::icall_convert_to_string_i4", 2, true, true, false,
                                        new[] { "", "drop" }),
                                0x0A => I("il2cpp::runtime::icall_convert_to_string_i8", 2, true, true, false,
                                    new[] { "l", "drop" }),
                                0x0D => I("il2cpp::runtime::icall_convert_to_string_d", 2, true, true, false,
                                    new[] { "d", "drop" }),
                                // BATCH 8: the remaining primitive sources (the
                                // provider is ignored for uint/ulong/float and for
                                // the string source, which returns itself).
                                0x09 => I("il2cpp::runtime::icall_uint32_to_string", 2, true, true, false,
                                    new[] { "u4", "drop" }),
                                0x0B => I("il2cpp::runtime::icall_uint64_to_string", 2, true, true, false,
                                    new[] { "q", "drop" }),
                                0x0C => I("il2cpp::runtime::icall_convert_to_string_d", 2, true, true, false,
                                    new[] { "d", "drop" }),
                                0x0E => I("il2cpp::runtime::icall_convert_to_string_obj", 2, true, true, false,
                                    new[] { "", "drop" }),
                                // BATCH 8: decimal -- net48 renders it with the
                                // provider's NumberFormatInfo; the runtime's
                                // decimal formatter is the invariant grammar
                                // (zh-CN's '.'/','), so the provider is dropped.
                                0x11 => I("il2cpp::runtime::icall_decimal_to_string", 2, true, true, false,
                                    new[] { "dec", "drop" }),
                                0x1C => I("il2cpp::runtime::icall_convert_to_string_obj", 2, true, true, false,
                                    new[] { "", "drop" }),
                                _ => throw new NotSupportedException(
                                    $"Convert.ToString param type 0x{ps[0]:X2} with IFormatProvider not wired in M5"),
                            };
                        return ps[0] switch
                        {
                            0x08 => I("il2cpp::runtime::icall_convert_to_string_i4", 1, true, true, false),
                            0x0A => I("il2cpp::runtime::icall_convert_to_string_i8", 1, true, true, false, new[] { "l" }),
                            0x0D => I("il2cpp::runtime::icall_convert_to_string_d", 1, true, true, false, new[] { "d" }),
                            0x02 => I("il2cpp::runtime::icall_convert_to_string_bool", 1, true, true, false, new[] { "b" }),
                            // M5 wiring: Convert.ToString(char) -> the char formatter.
                            0x03 => I("il2cpp::runtime::icall_char_to_string", 1, true, true, false, new[] { "c" }),
                            // BATCH 8: the remaining primitive sources. uint/ulong
                            // must render the FULL unsigned range (the int32/int64
                            // formatters would read uint >= 2^31 as negative); the
                            // float source rides the double slot.
                            0x09 => I("il2cpp::runtime::icall_uint32_to_string", 1, true, true, false, new[] { "u4" }),
                            0x0B => I("il2cpp::runtime::icall_uint64_to_string", 1, true, true, false, new[] { "q" }),
                            0x0C => I("il2cpp::runtime::icall_convert_to_string_d", 1, true, true, false, new[] { "d" }),
                            0x11 => I("il2cpp::runtime::icall_decimal_to_string", 1, true, true, false, new[] { "dec" }),
                            _ => throw new NotSupportedException($"Convert.ToString param type 0x{ps[0]:X2} not wired in M5"),
                        };
                    case "ToBoolean":
                        if (ps.Length != 1)
                            throw new NotSupportedException($"Convert.ToBoolean with {ps.Length} param(s) not wired");
                        return ps[0] switch
                        {
                            // Sweep: EVERY int32-carried integer source (sbyte/
                            // byte/short/ushort/int/uint): non-zero -> true
                            // including negatives -- exactly the bool_i4 icall.
                            0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09
                                => I("il2cpp::runtime::icall_convert_to_bool_i4", 1, true, false, false),
                            // Sweep: bool identity. Bool slots carry int32 0/1
                            // (the documented ABI invariant), on which the
                            // to-bool helper is the identity.
                            0x02 => I("il2cpp::runtime::icall_conv_bool_i4", 1, true, false, false),
                            // BATCH 8: net48 Convert.ToBoolean(long/ulong) is
                            // (value != 0) over the FULL 64-bit value (the
                            // oracle-pinned icall_convert_to_bool_i8; a low-32
                            // truncation would misread 0x100000000 as false).
                            0x0A => I("il2cpp::runtime::icall_convert_to_bool_i8", 1, true, false, false, new[] { "l" }),
                            0x0B => I("il2cpp::runtime::icall_convert_to_bool_i8", 1, true, false, false, new[] { "q" }),
                            // BATCH 8: net48 throws InvalidCastException for the
                            // char source; the dedicated icall is the exact
                            // contract (probe-pinned).
                            0x03 => I("il2cpp::runtime::icall_convert_to_bool_char", 1, true, false, false, new[] { "i" }),
                            // BATCH 8: net48 Convert.ToBoolean(float/double) is
                            // (value != 0) (oracle-pinned).
                            0x0C or 0x0D => I("il2cpp::runtime::icall_convert_to_bool_d", 1, true, false, false, new[] { "d" }),
                            // The throwing parser exists now: null -> false,
                            // "true"/"false" (case-insensitive, trimmed),
                            // anything else FormatException.
                            0x0E => I("il2cpp::runtime::icall_convert_to_boolean_string", 1, true, false, false, new[] { "s" }),
                            // BATCH 8: net48 Convert.ToBoolean(decimal) is
                            // (value != 0) (oracle-pinned).
                            0x11 => I("il2cpp::runtime::icall_convert_to_bool_dec", 1, true, false, false, new[] { "dec" }),
                            // BATCH 8: Convert.ToBoolean(object) -- the boxed
                            // dispatch (null -> false; the full 64-bit sources
                            // are tested for non-zero, no low-32 truncation).
                            0x1C => I("il2cpp::runtime::icall_convert_to_bool_obj", 1, true, false, false, new[] { "" }),
                            _ => throw new NotSupportedException($"Convert.ToBoolean param type 0x{ps[0]:X2} not wired (decimal/object on the edit_convert.md needs-new list)"),
                        };
                    case "ToChar":
                        if (ps.Length != 1)
                            throw new NotSupportedException($"Convert.ToChar with {ps.Length} param(s) not wired");
                        // Sweep: all int32-carried integer sources through the
                        // CHECKED UTF-16-range icall: byte/ushort/uint-in-range
                        // pass (zero-extension identity), sbyte/short negatives
                        // and uint patterns >= 0x8000 (which read negative)
                        // throw OverflowException -- the exact net48 contract.
                        return ps[0] switch
                        {
                            0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09
                                => I("il2cpp::runtime::icall_convert_to_char_i4", 1, true, false, false),
                            // BATCH 8: net48 Convert.ToChar(long/ulong) is the
                            // CHECKED UTF-16 range test over the FULL 64-bit
                            // value (oracle-pinned; a low-32 truncation would
                            // accept 0x100000041 as 'A').
                            0x0A => I("il2cpp::runtime::icall_convert_to_char_i8", 1, true, false, false, new[] { "l" }),
                            0x0B => I("il2cpp::runtime::icall_convert_to_char_u8", 1, true, false, false, new[] { "q" }),
                            // BATCH 8: net48 throws InvalidCastException for the
                            // bool / float / double sources; the dedicated icalls
                            // are the exact contracts (probe-pinned).
                            0x02 => I("il2cpp::runtime::icall_convert_to_char_bool", 1, true, false, false, new[] { "i" }),
                            0x0C or 0x0D => I("il2cpp::runtime::icall_convert_to_char_d", 1, true, false, false, new[] { "d" }),
                            // Convert.ToChar(string): null -> ArgumentNullException (the
                            // Convert null-means-zero rule does NOT extend to the char
                            // target -- net48 probe-pinned), a text that is not exactly
                            // one UTF-16 unit -> FormatException, otherwise the unit.
                            0x0E => I("il2cpp::runtime::icall_convert_to_char_string", 1, true, false, false, new[] { "s" }),
                            // BATCH 8: Convert.ToChar(object) -- the boxed
                            // dispatch (null -> '\0'; the int-family is CHECKED
                            // against the UTF-16 unit range).
                            0x1C => I("il2cpp::runtime::icall_convert_to_char_obj", 1, true, false, false, new[] { "" }),
                            _ => throw new NotSupportedException($"Convert.ToChar param type 0x{ps[0]:X2} not wired (decimal/object on the edit_convert.md needs-new list)"),
                        };
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
