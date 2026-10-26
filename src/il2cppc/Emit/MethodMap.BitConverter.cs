namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.BitConverter -- System.BitConverter icall mappings
// (il2cpp_bitconverter_* in bitconverter.h).
// Owners: System.BitConverter
//
// The ToT family (ToBoolean/ToChar/ToInt16/ToUInt16/ToInt32/ToUInt32/ToInt64/
// ToUInt64/ToSingle/ToDouble) dispatches on ARITY: RawSignature decodes the
// byte[] params cleanly (ps[0] = 0x1D SZARRAY, element consumed) because the
// ToT signatures return non-array values. Both the offset-free 1-arity and
// the (byte[], int startIndex) 2-arity forms carry the byte[] param with the
// "a" array cast ((Il2CppArray*)(...) -- the concrete type the bitconverter.h
// icalls declare, the ToBase64String precedent). The 2-arity form DROPS the
// startIndex ("drop" cast): the runtime simplified form reads from index 0
// (the bitconverter.h contract) -- exact for startIndex = 0, divergent
// otherwise (documented simplification).
//
//   ToBoolean/ToChar/ToInt16/ToUInt16/ToInt32/ToUInt32 -> int32 slot (null
//     result kind; the int16/uint16/char/bool returns widen exactly)
//   ToInt64/ToUInt64 -> int64 slot (result kind "l"; the uint64 bits are
//     preserved in the int64_t slot)
//   ToSingle/ToDouble -> double slot (result kind "d"; the float return
//     widens EXACTLY into the double slot -- the IR holds all floats in
//     double slots, StackFold ldc.r4 convention)
//
// The bits bridges SingleToInt32Bits(float) / Int32BitsToSingle(int32) are
// wired to the runtime entries. DoubleToInt64Bits/Int64BitsToDouble are NOT
// re-mapped here: TryMapConvert (MethodMap.Convert.cs) owns them and runs
// FIRST in the ?? fallback chain (MethodRefResolver.cs dispatch) -- no
// duplication. SingleToInt32Bits passes the double-slot float arg with the
// "d" cast (the implicit double->float narrowing at the call is exact: every
// float round-trips through a double slot).
//
// KNOWN GAP (CLOSED): BitConverter.GetBytes was not wireable while
// RawSignature leaked the byte[] RETURN's element byte into ps[0] (SZARRAY
// ret 0x1D did not consume its 0x05 element) -- every overload resolved
// identically (ps=[0x05], count=1, probe-verified against the real net48
// bitconvcase.exe MemberRef blobs: "00 01 1D 05 0C" / ".. 08" / ".. 02").
// The G2 RawSignature fix (MethodRefResolver.cs, SZARRAY-return element
// consumption) makes ps[0] carry the VALUE param type, so all ten net48
// overloads dispatch to their exact get_bytes_* icall below. The byte[]
// result is a managed reference (ReturnsRef, FromBase64String precedent:
// the emitter wraps ref results in (Il2CppObject*)(...)).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapBitConverter(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The shared Convert??BitConverter??Base64??ConvertExtra chain routes
        // System.Convert calls through here too. The ToT family (ToInt16/
        // ToUInt16/ToUInt32/ToUInt64/ToSingle/...) belongs to System.BitConverter
        // EXCLUSIVELY: Convert.ToInt16(int) etc. are CHECKED narrowing
        // conversions with no runtime icall -- they MUST fall through to
        // TryMapConvertExtra's LOUD rejection, never emit the byte-array icall
        // for a scalar (pinned by MethodMapTests.Base64.cs NarrowingFamily).
        if (owner == "System.Convert")
        {
            return null;
        }
        switch (name)
        {
            case "GetBytes":
                // G2: ps[0] carries the value param type (the SZARRAY-return
                // element byte is consumed by RawSignature) -- all ten net48
                // overloads dispatch exactly. The byte[] result is a managed
                // reference (rr=true; the emitter wraps it in (Il2CppObject*),
                // exact-address reinterpret -- FromBase64String precedent).
                // int16/uint16 live in int32 slots (cast "i" narrows exactly);
                // single widens through the double slot (cast "d").
                return ps.Length == 1
                    ? ps[0] switch
                    {
                        0x02 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_bool", 1, true, true, false, new[] { "b" }),
                        0x03 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_char", 1, true, true, false, new[] { "c" }),
                        0x06 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_int16", 1, true, true, false, new[] { "i" }),
                        0x07 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint16", 1, true, true, false, new[] { "i" }),
                        0x08 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_int32", 1, true, true, false, new[] { "i" }),
                        0x09 => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint32", 1, true, true, false, new[] { "i" }),
                        0x0A => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_int64", 1, true, true, false, new[] { "l" }),
                        0x0B => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_uint64", 1, true, true, false, new[] { "q" }),
                        0x0C => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_single", 1, true, true, false, new[] { "d" }),
                        0x0D => I("il2cpp::runtime::il2cpp_bitconverter_get_bytes_double", 1, true, true, false, new[] { "d" }),
                        var b => throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): BitConverter.GetBytes param type 0x{b:X2} -- " +
                            "net48 has exactly the ten scalar overloads bool/char/int16/uint16/int32/uint32/int64/uint64/" +
                            "single/double [Reference Source bitconverter.cs:41-150]"),
                    }
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): BitConverter.GetBytes arity {ps.Length} -- " +
                        "net48 has the single-param scalar forms only [Reference Source bitconverter.cs:41-150]");
            case "ToBoolean": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_bool", ps);
            case "ToChar": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_char", ps);
            case "ToInt16": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_int16", ps);
            case "ToUInt16": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_uint16", ps);
            case "ToInt32": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_int32", ps);
            case "ToUInt32": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_uint32", ps);
            case "ToInt64": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_int64", ps, "l");
            case "ToUInt64": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_uint64", ps, "l");
            case "ToSingle": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_single", ps, "d");
            case "ToDouble": return MapToT("il2cpp::runtime::il2cpp_bitconverter_to_double", ps, "d");
            case "SingleToInt32Bits":
                return ps.Length == 1 && ps[0] == 0x0C
                    ? I("il2cpp::runtime::il2cpp_bitconverter_single_to_int32_bits", 1, true, false, false, new[] { "d" })
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): BitConverter.SingleToInt32Bits param type " +
                        $"0x{(ps.Length == 1 ? ps[0] : 0):X2} -- net48 has no SingleToInt32Bits at all (it is a .NET " +
                        "Core-only API; the float form is our extension) [Reference Source bitconverter.cs:41-455]");
            case "Int32BitsToSingle":
                return ps.Length == 1 && ps[0] == 0x08
                    ? I("il2cpp::runtime::il2cpp_bitconverter_int32_bits_to_single", 1, true, false, false, new[] { "i" }, "d")
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): BitConverter.Int32BitsToSingle param type " +
                        $"0x{(ps.Length == 1 ? ps[0] : 0):X2} -- net48 has no Int32BitsToSingle at all (it is a .NET " +
                        "Core-only API; the int32 form is our extension) [Reference Source bitconverter.cs:41-455]");
            case "ToString":
                // byte[] hex formatter: "AB-CD-EF" uppercase, '-' separated;
                // empty array -> "" (net48 probe-verified 2026-08-05). The
                // single-arity form maps to the runtime hex icall with the
                // byte[] "a" cast (Il2CppObject* -> Il2CppArray*, the
                // ToBase64String precedent -- the runtime icall declares the
                // concrete array type).
                //
                // net48 ALSO has ToString(byte[], int) and ToString(byte[],
                // int, int) (bcrange1): both map to the range icalls. The
                // 2-arity form delegates on the RUNTIME side (length =
                // value.Length - startIndex) because the transpiler has no
                // array-length fold for an argument list. The net48
                // validation ORDER is observable and lives in
                // bitconverter.cpp (startIndex > 0 guard -> "" for
                // ToString(new byte[0], 0); ArgumentException only for a
                // positive length that overruns).
                if (ps.Length >= 2 && ps[0] == 0x1D)
                {
                    if (ps.Length == 2 && ps[1] == 0x08)
                        return I("il2cpp::runtime::il2cpp_bitconverter_to_hex_string_from", 2, true, true, false, new[] { "a", "i" });
                    if (ps.Length == 3 && ps[1] == 0x08 && ps[2] == 0x08)
                        return I("il2cpp::runtime::il2cpp_bitconverter_to_hex_string_range", 3, true, true, false, new[] { "a", "i", "i" });
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): BitConverter.ToString ({ps.Length} arg(s), " +
                        $"ps[1]=0x{ps[1]:X2}) -- the net48 forms are (byte[]), (byte[], int) and (byte[], int, int) " +
                        "[Reference Source bitconverter.cs:361,405,414]");
                }
                return ps.Length == 1 && ps[0] == 0x1D
                    ? I("il2cpp::runtime::il2cpp_bitconverter_to_hex_string", 1, true, true, false, new[] { "a" })
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): BitConverter.ToString arity {ps.Length} -- " +
                        "the net48 forms are (byte[]), (byte[], int) and (byte[], int, int) " +
                        "[Reference Source bitconverter.cs:361,405,414]");
            case "get_IsLittleEndian":
                // net48 exposes IsLittleEndian as a static readonly FIELD
                // (ldsfld, folded to 1 in StackFold), so this property shape
                // never appears in net48 IL; the runtime entry exists for
                // completeness (and for a future property-shaped surface).
                return I("il2cpp::runtime::il2cpp_bitconverter_get_is_little_endian", 0, true, false, false);
            default: return null;
        }
    }

    // ToT(byte[]) -- offset-free form; ToT(byte[], int) -- the startIndex is
    // PASSED to the runtime _offset icall (D-A/D-B fix, 2026-09-19: it used to
    // be DROPPED, so a negative index returned a value instead of throwing and
    // a positive index was ignored). The byte[] param carries the "a" array
    // cast (the icalls declare Il2CppArray*, not Il2CppObject*).
    private static ResolvedMethod? MapToT(string icall, byte[] ps, string? resultKind = null)
        => ps.Length switch
        {
            1 => I(icall, 1, true, false, false, new[] { "a" }, resultKind),
            2 => ps[1] == 0x08
                ? I(icall + "_offset", 2, true, false, false, new[] { "a", "i" }, resultKind)
                : throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): BitConverter.ToT startIndex param type " +
                    $"0x{ps[1]:X2} -- every net48 ToT startIndex is int32 [Reference Source bitconverter.cs:154-337]"),
            _ => throw new NotSupportedException(
                $"LOUD whitelist (net48 has no such overload): BitConverter.ToT arity {ps.Length} -- net48 has " +
                "the (byte[],int) startIndex forms only [Reference Source bitconverter.cs:154-337]"),
        };
}
