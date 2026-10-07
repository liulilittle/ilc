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
                                _ => throw new NotSupportedException($"Convert.ToInt64 param type 0x{ps[0]:X2} not wired (decimal/string+provider on the needs-new list)"),
                            };
                        throw new NotSupportedException($"Convert.ToInt64 with {ps.Length} param(s) not wired");
                    case "ToDouble":
                        return ps[0] switch
                        {
                            0x0E => I("il2cpp::runtime::icall_convert_to_double_string", 1, true, false, false, new[] { "s" }, "d"),
                            0x08 => I("il2cpp::runtime::icall_convert_to_double_i4", 1, true, false, false, null, "d"),
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
                            // net48 InvalidCastException ALWAYS (unsupported
                            // conversions); no existing icall raises ICE.
                            0x02 or 0x03 => throw new NotSupportedException($"Convert.ToDouble({(ps[0] == 0x02 ? "bool" : "char")}) throws InvalidCastException on net48 and no existing icall raises it"),
                            // NEEDS-NEW: uint needs a zero-extending _i4-input
                            // form (the existing d_u4 takes uint32_t and there is
                            // no unsigned VALUE cast code); float is an identity
                            // widening of the double slot with no d->d helper;
                            // decimal has no ->double converter. edit_convert.md.
                            _ => throw new NotSupportedException($"Convert.ToDouble param type 0x{ps[0]:X2} not wired (uint/float/decimal on the edit_convert.md needs-new list)"),
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
                            // NEEDS-NEW: long/ulong need icall_convert_to_bool_i8
                            // -- truncating the low 32 bits misreads 0x100000000
                            // as false. edit_convert.md.
                            0x0A or 0x0B
                                => throw new NotSupportedException("Convert.ToBoolean(long/ulong) needs a new icall_convert_to_bool_i8 (low-32 truncation would misread high values)"),
                            // net48 InvalidCastException ALWAYS (unsupported).
                            0x03 => throw new NotSupportedException("Convert.ToBoolean(char) throws InvalidCastException on net48 and no existing icall raises it"),
                            0x0C or 0x0D => throw new NotSupportedException("Convert.ToBoolean(float/double) throws InvalidCastException on net48 and no existing icall raises it"),
                            // The throwing parser exists now: null -> false,
                            // "true"/"false" (case-insensitive, trimmed),
                            // anything else FormatException.
                            0x0E => I("il2cpp::runtime::icall_convert_to_boolean_string", 1, true, false, false, new[] { "s" }),
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
                            // NEEDS-NEW: long/ulong need icall_convert_to_char_i8
                            // (low-32 truncation would accept 0x100000041 as 'A').
                            0x0A or 0x0B
                                => throw new NotSupportedException($"Convert.ToChar(long/ulong) needs a new icall_convert_to_char_i8 (low-32 truncation would mask out-of-range values)"),
                            // net48 InvalidCastException ALWAYS (unsupported).
                            0x02 => throw new NotSupportedException("Convert.ToChar(bool) throws InvalidCastException on net48 and no existing icall raises it"),
                            0x0C or 0x0D => throw new NotSupportedException("Convert.ToChar(float/double) throws InvalidCastException on net48 and no existing icall raises it"),
                            // NEEDS-NEW: null -> '\\0', length != 1 -> FormatException.
                            0x0E => throw new NotSupportedException("Convert.ToChar(string) needs a new icall (null -> '\\0', single-char else FormatException)"),
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
