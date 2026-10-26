namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ConvertExtra -- System.Convert checked-narrowing gap surface.
// Owners: System.Convert
//
// CONVERT-SWEEP (edit_convert, 2026-08-22): this file used to blanket-reject
// ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/ToUInt64/ToSingle/ToDecimal on the
// premise that NO runtime icall existed for them. That premise expired when
// il2cpp_conv.h grew the checked conv.ovf.* narrow family (batch 152e/154b:
// icall_conv_ovf_u1/i1/u2/i2 from _i8/_u8/_d) -- so every overload whose net48
// semantics an EXISTING icall reproduces exactly is wired here now.
//
// WIRING PROOFS (net48 baseline):
//   * Integer -> narrower integer is a CHECKED conversion (OverflowException
//     outside the target range). The conv.ovf narrow icalls are exactly that:
//     an integer range test on the value, no rounding involved.
//   * int-family sources ride int32 slots; the "l" cast sign-extends into the
//     _i8 forms. Sign-extension preserves every signed value; a uint bit
//     pattern >= 2^31 reads negative and fails any [0, max] test -- which is
//     precisely the throw net48 requires for those inputs (every narrow range
//     sits inside [0, 2^31)).
//   * ulong sources use the "q" reinterpret into the _u8 forms (the batch
//     154b contract: negative bit patterns fail the checked test).
//   * bool slots carry int32 0/1 (documented ABI invariant), on which
//     icall_conv_bool_i4 is the identity -- exact for the 0/1 results of
//     Convert.ToXxx(bool).
//   * Zero-extension targets (64-bit results from byte/ushort/char/bool/uint)
//     use icall_conv_u8_i4 ((long)uint.MaxValue == 4294967295, never -1).
// STILL REJECTED LOUD (each with its reason inline):
//   * float/double sources: net48 Convert ROUNDS half-to-even BEFORE the
//     range check; the existing double-input ovf helpers TRUNCATE first --
//     silently different results (Convert.ToByte(3.5) == 4, truncation == 3).
//     Needs new rounding-aware icalls (needs-new list in
//     tmp/reports/edit_convert.md).
//   * string sources: NOW WIRED (batch 5) -- the throwing parse icalls
//     icall_convert_to_{byte,sbyte,int16,uint16,uint32,uint64,single}_string
//     with the null-means-zero rule (conv-str-parse e2e).
//   * decimal sources: NOW WIRED for the narrow integral targets (batch 6) --
//     icall_decimal_to_{byte,sbyte,int16,uint16}_round round half-to-even then
//     range-check (the conv-narrow e2e pins them); the 32/64-bit forms were
//     wired by p0conv2 (icall_decimal_to_uint32_round/_uint64_round).
//   * char sources of ToByte/ToSByte/ToInt16/ToUInt16: NOW WIRED (batch 6) --
//     the checked conv.ovf_{u1,i1,i2,u2}_i8 helpers over the "l"-extended char
//     slot (conv-narrow e2e pins the checked ranges).
//   * ToSingle(uint)/ToDouble(uint): NOW WIRED (batch 7) -- the u4-source
//     helpers take uint32_t params and the new "u4" cast code in
//     EmitIcallCallArgs reinterprets the int32 carrier (zero-extension, so
//     uint >= 2^31 stays positive).
//   * ToSingle(bool/char): NOW WIRED (batch 7) -- net48 Convert.ToSingle(bool)
//     is `value ? 1f : 0f` (the bool slot carries int32 0/1 through
//     icall_conv_r4_i4), while Convert.ToSingle(char) throws
//     InvalidCastException (char has no IConvertible.ToSingle; the dedicated
//     icall_convert_to_single_char is the exact net48 contract).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapConvertExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            //------------------------------------------------------------------
            // The four NARROW targets: checked [range] conversions served by
            // the conv.ovf narrow family (il2cpp_conv.h batch 152e/154b).
            //------------------------------------------------------------------
            case "ToByte":
            case "ToSByte":
            case "ToInt16":
            case "ToUInt16":
            {
                // (icall suffix, net48 checked range, rounding-_d icall infix)
                var (suffix, _, dName) = name switch
                {
                    "ToByte" => ("u1", "[0, 255]", "byte"),
                    "ToSByte" => ("i1", "[-128, 127]", "sbyte"),
                    "ToInt16" => ("i2", "[-32768, 32767]", "int16"),
                    _ => ("u2", "[0, 65535]", "uint16"),
                };
                // Convert.{name}(value, IFormatProvider) / (string, int fromBase):
                // net48 ignores the provider; the radix form is the batch-9 icall
                // (convert.cs:1977/1993/2011/2030).
                if (ps.Length != 1)
                {
                    if (ps.Length == 2 && ps[1] == 0x12)
                        return ps[0] switch
                        {
                            0x1C => I($"il2cpp::runtime::icall_convert_to_{dName}_obj", 2, true, false, false, new[] { "", "drop" }),
                            0x0E => I($"il2cpp::runtime::icall_convert_to_{dName}_string", 2, true, false, false, new[] { "s", "drop" }),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Convert.{name} param type 0x{ps[0]:X2} with IFormatProvider -- " +
                                "net48 has only the (object, string) provider forms [Reference convert.cs:600,693,815,893,916,1007]"),
                        };
                    if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                        return I($"il2cpp::runtime::icall_convert_to_{dName}_string_base", 2, true, false, false, new[] { "s", "i" });
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.{name} with {ps.Length} param(s) -- " +
                        "net48 has the 1-arg value overloads, (object,string)+IFormatProvider and (string,int fromBase) " +
                        "[Reference convert.cs:595-1007]");
                }
                return ps[0] switch
                {
                    // All int32-carried integer sources through the checked
                    // int64 form ("l" sign-extension proof in the header).
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x0A
                        => I($"il2cpp::runtime::icall_conv_ovf_{suffix}_i8", 1, true, false, false, new[] { "l" }),
                    // ulong source: raw 64-bit pattern into the checked u8 form.
                    0x0B => I($"il2cpp::runtime::icall_conv_ovf_{suffix}_u8", 1, true, false, false, new[] { "q" }),
                    // bool: 0/1 identity on the documented bool-slot domain.
                    0x02 => I("il2cpp::runtime::icall_conv_bool_i4", 1, true, false, false),
                    // char: a CHECKED conversion of the 16-bit value (net48
                    // probe-pinned: ToByte('\u0100') -> OverflowException,
                    // ToSByte('\u00E9') -> OverflowException, ToUInt16('\uFFFF')
                    // == 65535). The "l" cast sign-extends the char slot
                    // (0..65535) into the checked int64 helper.
                    0x03 => I($"il2cpp::runtime::icall_conv_ovf_{suffix}_i8", 1, true, false, false, new[] { "l" }),
                    // decimal: net48 Convert ROUNDS half-to-even to an integer
                    // then range-checks the narrow target (probe-pinned:
                    // ToByte(3.5m)==4, ToByte(-0.5m)==0, ToByte(256m) ->
                    // OverflowException). NOT the truncating decimal icall.
                    0x11 => I($"il2cpp::runtime::icall_decimal_to_{dName}_round", 1, true, false, false, new[] { "dec" }),
                    // T1 parity: rounding-aware icall_convert_to_<kind>_d
                    // (net48 rounds half-to-even first; the ovf_*_d truncate).
                    // Float rides the double slot ("d", the ToInt32 precedent).
                    0x0C or 0x0D => I($"il2cpp::runtime::icall_convert_to_{dName}_d", 1, true, false, false, new[] { "d" }),
                    // Convert.ToXxx(string): the throwing parse icall with the
                    // null-means-zero rule (conv-str-parse e2e).
                    0x0E => I($"il2cpp::runtime::icall_convert_to_{dName}_string", 1, true, false, false, new[] { "s" }),
                    // BATCH 8: Convert.{name}(object) -- the boxed dispatch
                    // (null -> 0; the checked narrow range over the full-width
                    // int64/uint64 sources).
                    0x1C => I($"il2cpp::runtime::icall_convert_to_{dName}_obj", 1, true, false, false, new[] { "" }),
                    _ => throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.{name} param type 0x{ps[0]:X2} -- " +
                        "net48 has the primitive value overloads [Reference convert.cs:595-1007]"),
                };
            }
            case "ToUInt32":
            {
                // Convert.ToUInt32(value, IFormatProvider) / (string, int fromBase)
                // -- batch 9 (convert.cs:1141/1235/2058).
                if (ps.Length != 1)
                {
                    if (ps.Length == 2 && ps[1] == 0x12)
                        return ps[0] switch
                        {
                            0x1C => I("il2cpp::runtime::icall_convert_to_uint32_obj", 2, true, false, false, new[] { "", "drop" }),
                            0x0E => I("il2cpp::runtime::icall_convert_to_uint32_string", 2, true, false, false, new[] { "s", "drop" }),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Convert.ToUInt32 param type 0x{ps[0]:X2} with IFormatProvider -- " +
                                "net48 has only the (object, string) provider forms [Reference convert.cs:1141,1235]"),
                        };
                    if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                        return I("il2cpp::runtime::icall_convert_to_uint32_string_base", 2, true, false, false, new[] { "s", "i" });
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToUInt32 with {ps.Length} param(s) -- " +
                        "net48 has the 1-arg value overloads, (object,string)+IFormatProvider and (string,int fromBase) " +
                        "[Reference convert.cs:1136-1235]");
                }
                return ps[0] switch
                {
                    // Signed small integers + byte/ushort: checked [0, 2^32)
                    // on the sign-extended slot value (negatives throw).
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08
                        => I("il2cpp::runtime::icall_conv_ovf_u4_i4", 1, true, false, false, new[] { "i" }),
                    // uint identity: icall_conv_u4_i4 returns the SAME 32-bit
                    // pattern (int32 -> uint32 is a reinterpret, never a range
                    // test), so uint >= 2^31 survives bit-exact -- the exact
                    // net48 identity conversion (a sign-extending route would
                    // wrongly throw for uint >= 2^31).
                    0x09 => I("il2cpp::runtime::icall_conv_u4_i4", 1, true, false, false, new[] { "i" }),
                    0x0A => I("il2cpp::runtime::icall_conv_ovf_u4_i8", 1, true, false, false, new[] { "l" }),
                    0x0B => I("il2cpp::runtime::icall_conv_ovf_u4_u8", 1, true, false, false, new[] { "q" }),
                    0x02 => I("il2cpp::runtime::icall_conv_bool_i4", 1, true, false, false),
                    // char: zero-extend, never throws (the pinned ToInt32(char)
                    // family behavior; [0, 65535] always fits uint32).
                    0x03 => I("il2cpp::runtime::icall_convert_to_int32_char", 1, true, false, false),
                    // p0conv2 (D2): Convert.ToUInt32(decimal) ROUNDS half-to-even
                    // then range-checks (net48 probe: ToUInt32(3.5m)==4,
                    // ToUInt32(-0.5m)==0, ToUInt32(4294967296m) -> Overflow).
                    0x11 => I("il2cpp::runtime::icall_decimal_to_uint32_round", 1, true, false, false, new[] { "dec" }),
                    // T1 parity: rounding-aware icall_convert_to_uint32_d
                    // (ovf_u4_d truncates; net48 rounds half-to-even first).
                    0x0C or 0x0D => I("il2cpp::runtime::icall_convert_to_uint32_d", 1, true, false, false, new[] { "d" }),
                    0x0E => I("il2cpp::runtime::icall_convert_to_uint32_string", 1, true, false, false, new[] { "s" }),
                    // BATCH 8: Convert.ToUInt32(object) -- the boxed dispatch
                    // (null -> 0; negative signed sources throw).
                    0x1C => I("il2cpp::runtime::icall_convert_to_uint32_obj", 1, true, false, false, new[] { "" }),
                    _ => throw new NotSupportedException($"LOUD whitelist (net48 has no such overload): Convert.ToUInt32 param type 0x{ps[0]:X2} -- net48 has the primitive value overloads [Reference convert.cs:1136-1242]"),
                };
            }
            case "ToUInt64":
            {
                // Convert.ToUInt64(value, IFormatProvider) / (string, int fromBase)
                // -- batch 9 (convert.cs:1348/1434/2083).
                if (ps.Length != 1)
                {
                    if (ps.Length == 2 && ps[1] == 0x12)
                        return ps[0] switch
                        {
                            0x1C => I("il2cpp::runtime::icall_convert_to_uint64_obj", 2, true, false, false, new[] { "", "drop" }, "l"),
                            0x0E => I("il2cpp::runtime::icall_convert_to_uint64_string", 2, true, false, false, new[] { "s", "drop" }, "l"),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Convert.ToUInt64 param type 0x{ps[0]:X2} with IFormatProvider -- " +
                                "net48 has only the (object, string) provider forms [Reference convert.cs:1348,1434]"),
                        };
                    if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                        return I("il2cpp::runtime::icall_convert_to_uint64_string_base", 2, true, false, false, new[] { "s", "i" }, "l");
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToUInt64 with {ps.Length} param(s) -- " +
                        "net48 has the 1-arg value overloads, (object,string)+IFormatProvider and (string,int fromBase) " +
                        "[Reference convert.cs:1343-1434]");
                }
                return ps[0] switch
                {
                    // Signed sources: negative throws (checked [0, 2^64)).
                    0x04 or 0x06 or 0x08
                        => I("il2cpp::runtime::icall_conv_ovf_u8_i4", 1, true, false, false, new[] { "i" }, "l"),
                    // Non-negative domains (byte/ushort/uint/bool/char): plain
                    // zero-extension can never overflow -- the unchecked
                    // zero-extending helper is EXACT here.
                    0x02 or 0x03 or 0x05 or 0x07 or 0x09
                        => I("il2cpp::runtime::icall_conv_u8_i4", 1, true, false, false, new[] { "i" }, "l"),
                    0x0A => I("il2cpp::runtime::icall_conv_ovf_u8_i8", 1, true, false, false, new[] { "l" }, "l"),
                    // ulong identity: bit-pattern round-trip through the
                    // i8<->u8 reinterpret helper.
                    0x0B => I("il2cpp::runtime::icall_conv_i8_u8", 1, true, false, false, new[] { "q" }, "l"),
                    // p0conv2 (D2): Convert.ToUInt64(decimal) ROUNDS half-to-even
                    // then range-checks (net48 probe: ToUInt64(3.5m)==4,
                    // ToUInt64(-0.5m)==0). NOT the truncating decimal icall.
                    0x11 => I("il2cpp::runtime::icall_decimal_to_uint64_round", 1, true, false, false, new[] { "dec" }, "l"),
                    // T1 parity: rounding-aware icall_convert_to_uint64_d ("l"
                    // carries the uint64 slot, the ovf_u8 precedent).
                    0x0C or 0x0D => I("il2cpp::runtime::icall_convert_to_uint64_d", 1, true, false, false, new[] { "d" }, "l"),
                    0x0E => I("il2cpp::runtime::icall_convert_to_uint64_string", 1, true, false, false, new[] { "s" }, "l"),
                    // BATCH 8: Convert.ToUInt64(object) -- the boxed dispatch
                    // (null -> 0; negative signed sources throw).
                    0x1C => I("il2cpp::runtime::icall_convert_to_uint64_obj", 1, true, false, false, new[] { "" }, "l"),
                    _ => throw new NotSupportedException($"LOUD whitelist (net48 has no such overload): Convert.ToUInt64 param type 0x{ps[0]:X2} -- net48 has the primitive value overloads [Reference convert.cs:1343-1441]"),
                };
            }
            case "ToSingle":
            {
                // Convert.ToSingle(value, IFormatProvider) -- batch 9
                // (convert.cs:1455/1517).
                if (ps.Length != 1)
                {
                    if (ps.Length == 2 && ps[1] == 0x12)
                        return ps[0] switch
                        {
                            0x1C => I("il2cpp::runtime::icall_convert_to_single_obj", 2, true, false, false, new[] { "", "drop" }, "d"),
                            0x0E => I("il2cpp::runtime::icall_convert_to_single_string", 2, true, false, false, new[] { "s", "drop" }, "d"),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Convert.ToSingle param type 0x{ps[0]:X2} with IFormatProvider -- " +
                                "net48 has only the (object, string) provider forms [Reference convert.cs:1455,1517]"),
                        };
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToSingle with {ps.Length} param(s) -- " +
                        "net48 has the 1-arg value overloads and (object,string)+IFormatProvider " +
                        "[Reference convert.cs:1451-1517]");
                }
                return ps[0] switch
                {
                    // double -> float RNE narrowing, never throws (overflow ->
                    // +-Infinity) -- exactly conv.r4 / net48 (float)value. A
                    // float source rides a double slot holding the exact float,
                    // so the same helper is the identity for it. Result kind
                    // "d": the float return widens exactly into the double slot
                    // (the MathF mapping precedent).
                    0x0C or 0x0D => I("il2cpp::runtime::icall_conv_r4_d", 1, true, false, false, null, "d"),
                    // Exact integer widenings (<= 2^24 magnitudes are exact in
                    // single precision; larger ints take the helper's RNE --
                    // the net48 (float)int contract).
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08
                        => I("il2cpp::runtime::icall_conv_r4_i4", 1, true, false, false, new[] { "i" }, "d"),
                    0x0A => I("il2cpp::runtime::icall_conv_r4_i8", 1, true, false, false, new[] { "l" }, "d"),
                    0x0B => I("il2cpp::runtime::icall_conv_r4_u8", 1, true, false, false, new[] { "q" }, "d"),
                    // uint -> float: icall_conv_r4_u4 takes the raw 32-bit
                    // pattern ("u4" reinterpret -- a sign-extending route would
                    // make uint >= 2^31 a negative float); RNE to single.
                    0x09 => I("il2cpp::runtime::icall_conv_r4_u4", 1, true, false, false, new[] { "u4" }, "d"),
                    // p0conv2 (D2): Convert.ToSingle(decimal) is the exact
                    // VarR4FromDec conversion -- net48 probe confirms it is
                    // bit-identical to the explicit (float)decimal cast
                    // (12.5m and 0.1m both agree), so the existing
                    // decimal->float icall serves it ("d" carrier).
                    0x11 => I("il2cpp::runtime::icall_decimal_to_float", 1, true, false, false, new[] { "dec" }, "d"),
                    // bool -> float: net48 Convert.ToSingle(bool) is
                    // `value ? 1f : 0f`; the bool slot carries int32 0/1, on
                    // which icall_conv_r4_i4 is exactly 0.0f/1.0f.
                    0x02 => I("il2cpp::runtime::icall_conv_r4_i4", 1, true, false, false, new[] { "i" }, "d"),
                    // char -> float: net48 Convert.ToSingle(char) throws
                    // InvalidCastException (char has no IConvertible.ToSingle;
                    // the implicit char -> float widening exists but Convert
                    // does not expose it). The dedicated throwing icall is the
                    // exact net48 contract (probe-pinned for 'A').
                    0x03 => I("il2cpp::runtime::icall_convert_to_single_char", 1, true, false, false, new[] { "i" }, "d"),
                    0x0E => I("il2cpp::runtime::icall_convert_to_single_string", 1, true, false, false, new[] { "s" }, "d"),
                    // BATCH 8: Convert.ToSingle(object) -- the boxed dispatch
                    // (null -> 0f; char throws ICE, which the icall reproduces).
                    0x1C => I("il2cpp::runtime::icall_convert_to_single_obj", 1, true, false, false, new[] { "" }, "d"),
                    _ => throw new NotSupportedException($"LOUD whitelist (net48 has no such overload): Convert.ToSingle param type 0x{ps[0]:X2} -- net48 has the primitive value overloads [Reference convert.cs:1451-1529]"),
                };
            }
            case "ToDecimal":
                // p0conv2 (D1): Convert.ToDecimal now has a real decimal-target
                // path. The decimal ctors already exist (decimal.h:131-140) and
                // the "dec" ResultKind carries the T3 result-slot protocol
                // (MethodMap.Decimal.cs .ctor arm: the icall takes the target
                // slot FIRST and the AssignStmt "dec" branch prepends it), so
                // every integer source is EXACT:
                //   sbyte/byte/short/ushort/int -> ctor_int32 ("i" widen)
                //   uint                        -> ctor_uint32 ("i")
                //   long                        -> ctor_int64 ("l")
                //   ulong                       -> ctor_uint64 ("q")
                //   bool                        -> ctor_int32 (bool slots carry 0/1)
                //   float                       -> ctor_float (7 significant digits,
                //                                  the VarDecFromR4 contract)
                //   double                      -> ctor_double (15 significant
                //                                  digits, VarDecFromR8)
                //   decimal                     -> icall_decimal_copy (identity,
                //                                  scale preserved)
                //   string                      -> icall_convert_to_decimal_string
                //                                  (null -> 0m, the Convert rule)
                //   char                        -> InvalidCastException on net48
                //                                  (probe-pinned for 'A'), served by
                //                                  icall_convert_to_decimal_char
                // BATCH 8: Convert.ToDecimal(value, IFormatProvider) -- net48
                // ignores the provider for every primitive source (the string
                // source parses with the provider; the runtime parse is the
                // invariant grammar, which matches zh-CN's '.'/','), so the
                // 2-arg form drops the provider and routes to the 1-arg arm.
                if (ps.Length == 2 && ps[1] == 0x12)
                    return ps[0] switch
                    {
                        0x04 or 0x05 or 0x06 or 0x07 or 0x08
                            => I("il2cpp::runtime::icall_decimal_ctor_int32", 2, true, false, false, new[] { "i", "drop" }, "dec"),
                        0x09 => I("il2cpp::runtime::icall_decimal_ctor_uint32", 2, true, false, false, new[] { "i", "drop" }, "dec"),
                        0x0A => I("il2cpp::runtime::icall_decimal_ctor_int64", 2, true, false, false, new[] { "l", "drop" }, "dec"),
                        0x0B => I("il2cpp::runtime::icall_decimal_ctor_uint64", 2, true, false, false, new[] { "q", "drop" }, "dec"),
                        0x02 => I("il2cpp::runtime::icall_decimal_ctor_int32", 2, true, false, false, new[] { "i", "drop" }, "dec"),
                        0x0C => I("il2cpp::runtime::icall_decimal_ctor_float", 2, true, false, false, new[] { "", "drop" }, "dec"),
                        0x0D => I("il2cpp::runtime::icall_decimal_ctor_double", 2, true, false, false, new[] { "", "drop" }, "dec"),
                        0x11 => I("il2cpp::runtime::icall_decimal_copy", 2, true, false, false, new[] { "dec", "drop" }, "dec"),
                        0x0E => I("il2cpp::runtime::icall_convert_to_decimal_string", 2, true, false, false, new[] { "s", "drop" }, "dec"),
                        0x03 => I("il2cpp::runtime::icall_convert_to_decimal_char", 2, true, false, false, new[] { "i", "drop" }, "dec"),
                        0x1C => I("il2cpp::runtime::icall_convert_to_decimal_obj", 2, true, false, false, new[] { "", "drop" }, "dec"),
                        _ => throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Convert.ToDecimal param type 0x{ps[0]:X2} with IFormatProvider -- " +
                            "net48 has only the primitive + (object, string, decimal) provider forms [Reference convert.cs:1630,1688]"),
                    };
                if (ps.Length != 1)
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToDecimal with {ps.Length} param(s) -- " +
                        "net48 has the 1-arg value overloads and (object,string)+IFormatProvider [Reference convert.cs:1626-1698]");
                return ps[0] switch
                {
                    // Integer sources ride the existing ctor icalls. ResultKind
                    // "dec" makes the emitter write through the target slot.
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08
                        => I("il2cpp::runtime::icall_decimal_ctor_int32", 1, true, false, false, new[] { "i" }, "dec"),
                    0x09 => I("il2cpp::runtime::icall_decimal_ctor_uint32", 1, true, false, false, new[] { "i" }, "dec"),
                    0x0A => I("il2cpp::runtime::icall_decimal_ctor_int64", 1, true, false, false, new[] { "l" }, "dec"),
                    0x0B => I("il2cpp::runtime::icall_decimal_ctor_uint64", 1, true, false, false, new[] { "q" }, "dec"),
                    // bool slots carry int32 0/1 (the documented ABI invariant).
                    0x02 => I("il2cpp::runtime::icall_decimal_ctor_int32", 1, true, false, false, new[] { "i" }, "dec"),
                    // Float/double: the VarDecFromR4/R8 rounding ctors. The
                    // source value passes UNCAST (float rides the double slot
                    // exactly, the MathF precedent).
                    0x0C => I("il2cpp::runtime::icall_decimal_ctor_float", 1, true, false, false, new[] { "" }, "dec"),
                    0x0D => I("il2cpp::runtime::icall_decimal_ctor_double", 1, true, false, false, new[] { "" }, "dec"),
                    // decimal identity: net48 returns the operand unchanged.
                    0x11 => I("il2cpp::runtime::icall_decimal_copy", 1, true, false, false, new[] { "dec" }, "dec"),
                    // string: Convert's null-means-zero rule then decimal parse.
                    0x0E => I("il2cpp::runtime::icall_convert_to_decimal_string", 1, true, false, false, new[] { "s" }, "dec"),
                    // char: InvalidCastException on net48, never a value.
                    0x03 => I("il2cpp::runtime::icall_convert_to_decimal_char", 1, true, false, false, new[] { "i" }, "dec"),
                    // BATCH 8: Convert.ToDecimal(object) -- the boxed dispatch
                    // (null -> 0m; char throws ICE, which the icall reproduces).
                    0x1C => I("il2cpp::runtime::icall_convert_to_decimal_obj", 1, true, false, false, new[] { "" }, "dec"),
                    _ => throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Convert.ToDecimal param type 0x{ps[0]:X2} -- " +
                        "net48 has the primitive value overloads [Reference convert.cs:1626-1702]"),
                };
            default: return null;
        }
    }
}

