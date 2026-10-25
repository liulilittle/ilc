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
//   * decimal sources: no decimal converter exists at all (and
//     icall_decimal_to_int32 TRUNCATES -- the explicit-cast semantics, not
//     Convert's banker's rounding).
//   * char sources of ToByte/ToSByte/ToInt16/ToUInt16: net48 semantics not
//     pinned by any e2e run yet (ToInt32(char) IS pinned as zero-extend;
//     the narrower targets are not) -- pin before wiring, never guess.
//   * ToSingle(uint)/ToDouble(uint): the existing u4-source helpers take
//     uint32_t params and there is no unsigned VALUE cast code in
//     EmitIcallCallArgs (only deref:ui) -- needs an _i4-input helper form.
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
                if (ps.Length != 1)
                    throw new NotSupportedException($"Convert.{name} with {ps.Length} param(s) not wired");
                // (icall suffix, net48 checked range, rounding-_d icall infix)
                var (suffix, _, dName) = name switch
                {
                    "ToByte" => ("u1", "[0, 255]", "byte"),
                    "ToSByte" => ("i1", "[-128, 127]", "sbyte"),
                    "ToInt16" => ("i2", "[-32768, 32767]", "int16"),
                    _ => ("u2", "[0, 65535]", "uint16"),
                };
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
                    // Semantics NOT pinned against net48 yet (ToInt32(char) is;
                    // these are not) -- pin before wiring, never guess.
                    0x03 => throw new NotSupportedException($"Convert.{name}(char): net48 semantics not pinned by any e2e run (pin before wiring; see edit_convert.md)"),
                    // T1 parity: rounding-aware icall_convert_to_<kind>_d
                    // (net48 rounds half-to-even first; the ovf_*_d truncate).
                    // Float rides the double slot ("d", the ToInt32 precedent).
                    0x0C or 0x0D => I($"il2cpp::runtime::icall_convert_to_{dName}_d", 1, true, false, false, new[] { "d" }),
                    // Convert.ToXxx(string): the throwing parse icall with the
                    // null-means-zero rule (conv-str-parse e2e).
                    0x0E => I($"il2cpp::runtime::icall_convert_to_{dName}_string", 1, true, false, false, new[] { "s" }),
                    _ => throw new NotSupportedException($"Convert.{name} param type 0x{ps[0]:X2} not wired (decimal/object on the edit_convert.md needs-new list)"),
                };
            }
            case "ToUInt32":
            {
                if (ps.Length != 1)
                    throw new NotSupportedException($"Convert.ToUInt32 with {ps.Length} param(s) not wired");
                return ps[0] switch
                {
                    // Signed small integers + byte/ushort: checked [0, 2^32)
                    // on the sign-extended slot value (negatives throw).
                    0x04 or 0x05 or 0x06 or 0x07 or 0x08
                        => I("il2cpp::runtime::icall_conv_ovf_u4_i4", 1, true, false, false, new[] { "i" }),
                    // NEEDS-NEW: uint identity -- no zero-extending u4->u4
                    // helper exists, and both reuse routes (ovf_u4_i4/_i8 via
                    // sign extension) would WRONGLY throw for uint >= 2^31.
                    0x09 => throw new NotSupportedException("Convert.ToUInt32(uint) is an identity conversion with no matching runtime icall (sign-extending routes would wrongly throw for uint >= 2^31; see edit_convert.md)"),
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
                    _ => throw new NotSupportedException($"Convert.ToUInt32 param type 0x{ps[0]:X2} not wired (object on the edit_convert.md needs-new list)"),
                };
            }
            case "ToUInt64":
            {
                if (ps.Length != 1)
                    throw new NotSupportedException($"Convert.ToUInt64 with {ps.Length} param(s) not wired");
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
                    _ => throw new NotSupportedException($"Convert.ToUInt64 param type 0x{ps[0]:X2} not wired (object on the edit_convert.md needs-new list)"),
                };
            }
            case "ToSingle":
            {
                if (ps.Length != 1)
                    throw new NotSupportedException($"Convert.ToSingle with {ps.Length} param(s) not wired");
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
                    // NEEDS-NEW: the u4-source helper takes uint32_t and there
                    // is no unsigned VALUE cast code (a sign-extending route
                    // would produce a negative float for uint >= 2^31).
                    0x09 => throw new NotSupportedException("Convert.ToSingle(uint) needs an _i4-input helper (zero-extension inside the runtime) or an unsigned value-cast code; see edit_convert.md"),
                    // p0conv2 (D2): Convert.ToSingle(decimal) is the exact
                    // VarR4FromDec conversion -- net48 probe confirms it is
                    // bit-identical to the explicit (float)decimal cast
                    // (12.5m and 0.1m both agree), so the existing
                    // decimal->float icall serves it ("d" carrier).
                    0x11 => I("il2cpp::runtime::icall_decimal_to_float", 1, true, false, false, new[] { "dec" }, "d"),
                    // net48 InvalidCastException ALWAYS (unsupported).
                    0x02 or 0x03 => throw new NotSupportedException($"Convert.ToSingle({(ps[0] == 0x02 ? "bool" : "char")}) throws InvalidCastException on net48 and no existing icall raises it"),
                    0x0E => I("il2cpp::runtime::icall_convert_to_single_string", 1, true, false, false, new[] { "s" }, "d"),
                    _ => throw new NotSupportedException($"Convert.ToSingle param type 0x{ps[0]:X2} not wired (object on the edit_convert.md needs-new list)"),
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
                if (ps.Length != 1)
                    throw new NotSupportedException($"Convert.ToDecimal with {ps.Length} param(s) not wired (object/2-arg provider overloads on the edit_convert.md needs-new list)");
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
                    _ => throw new NotSupportedException($"Convert.ToDecimal param type 0x{ps[0]:X2} not wired (object/2-arg provider overloads on the edit_convert.md needs-new list)"),
                };
            default: return null;
        }
    }
}
