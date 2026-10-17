namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.BigInteger -- System.Numerics.BigInteger icall mappings
// (il2cpp_biginteger.h, implementation biginteger.cpp).
// Owners: System.Numerics.BigInteger
//
// VALUE-TYPE MODEL: a BigInteger is an 8-byte value (Il2CppBigInteger -- a
// pointer to the native variable-length state, see il2cpp_biginteger.h).
// Like DateTime/TimeSpan, the icalls take Il2CppBigInteger BY VALUE (8 bytes
// = one register on x64). The value-PRODUCING icalls take the RESULT SLOT as
// their FIRST parameter (the decimal ctor precedent -- the newobj fold's
// vctor path prepends the slot for the ctors; the AssignStmt "bi" special
// case prepends it for the operators).
//
// CASTS (the DateTime precedent -- 8-byte by-value icalls):
//   * STATIC operator operands (op_Addition etc.) are value-typed locals
//     loaded as VALUES -- the "" cast passes the value directly (the
//     DateTime op_Subtraction precedent).
//   * INSTANCE receivers (CompareTo/Equals/ToString) arrive as ldloca
//     ADDRESSES -- the "deref:bi" cast dereferences the address to the
//     struct VALUE (the DateTime "deref:dt" precedent); the other BigInteger
//     operand passes as a value ("" cast).
//
// Wired surface:
//   * .ctor(int32/int64) + op_Implicit(int32/int64) -> from_int32/int64
//   * op_Addition/Subtraction/Multiply/Division/Remainder/UnaryNegation
//   * op_Equality/Inequality/LessThan/GreaterThan/LessThanOrEqual/
//     GreaterThanOrEqual (compare superposition, the decimal precedent)
//   * CompareTo(BigInteger) / Equals(BigInteger)
//   * ToString()
//   * op_Explicit(BigInteger -> int32) -> to_int32 (OverflowException)
//
// Known-unwireable members return null (the generated-name path):
//   * op_Explicit(BigInteger -> int64/uint32/...) -- the int32 conversion
//     only (the runtime has to_int32).
//   * Equals(object) / CompareTo(object) -- a boxed BigInteger has no runtime
//     box class.
//   * Parse / TryParse / Pow / GreatestCommonDivisor / the static math
//     surface -- out of scope for this domain.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapBigInteger(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, byte ret = 0x01)
    {
        switch (name)
        {
            // .ctor(int32) / .ctor(int64): the result slot is prepended by the
            // vctor path (the icall writes the slot -- the decimal ctor
            // precedent).
            case ".ctor":
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_biginteger_from_int32", 1, false, false, true, null, "bi");
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_biginteger_from_int64", 1, false, false, true, null, "bi");
                return null;
            // static BigInteger op_Implicit(int32/int64): the result slot is
            // prepended by the AssignStmt "bi" special case.
            case "op_Implicit":
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_biginteger_from_int32", 1, true, false, false, null, "bi");
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_biginteger_from_int64", 1, true, false, false, null, "bi");
                return null;
            // static int32 op_Explicit(BigInteger): truncate toward zero,
            // OverflowException out of range. nb4 NUM-BI: the int64 / uint32 /
            // uint64 conversion arms discriminate on the RETURN byte (the
            // argument list is a lone [0x11] for every width, so `ret` selects
            // the carrier -- int32 0x08 / uint32 0x09 / int64 0x0A /
            // uint64 0x0B).
            case "op_Explicit":
                if (ps.Length == 1 && ps[0] == 0x11)
                {
                    if (ret == 0x09)
                        return I("il2cpp::runtime::icall_biginteger_to_uint32", 1, true, false, false, new[] { "" });
                    if (ret == 0x0A)
                        return I("il2cpp::runtime::icall_biginteger_to_int64", 1, true, false, false, new[] { "" }, "l");
                    if (ret == 0x0B)
                        return I("il2cpp::runtime::icall_biginteger_to_uint64", 1, true, false, false, new[] { "" }, "l");
                    return I("il2cpp::runtime::icall_biginteger_to_int32", 1, true, false, false, new[] { "" });
                }
                return null;
            // nb4 NUM-BI: BigInteger.Parse(string) / TryParse(string, out
            // BigInteger) -- result-slot-first parse (the newobj/pow precedent);
            // TryParse's out-slot address passes through the "" cast.
            case "Parse":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_biginteger_parse", 1, true, false, false, new[] { "s" }, "bi");
                return null;
            case "TryParse":
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_try_parse", 2, true, false, false, new[] { "s", "" });
                return null;
            // static BigInteger Pow(BigInteger, int) / GreatestCommonDivisor(
            // BigInteger, BigInteger): the result slot is prepended by the
            // AssignStmt "bi" special case; the value operands ride the ""
            // cast, the int exponent the "i" cast.
            case "Pow":
                if (count == 2 && ps[0] == 0x11 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_biginteger_pow", 2, true, false, false, new[] { "", "i" }, "bi");
                return null;
            case "GreatestCommonDivisor":
                if (count == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_gcd", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            // byte[] ToByteArray(): instance -- the receiver is an ldloca
            // ADDRESS ("deref:bi"); the result is a managed byte[] ref.
            case "ToByteArray":
                if (count == 0)
                    return I("il2cpp::runtime::icall_biginteger_to_byte_array", 0, true, true, true, new[] { "deref:bi" });
                return null;
            // static BigInteger operators: both operands are values ("" cast);
            // the result slot is prepended by the AssignStmt "bi" special case.
            case "op_Addition":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_add", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            case "op_Subtraction":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_sub", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            case "op_Multiply":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_mul", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            case "op_Division":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_div", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            case "op_Remainder":
            case "op_Modulus":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_rem", 2, true, false, false, new[] { "", "" }, "bi");
                return null;
            case "op_UnaryNegation":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_negate", 1, true, false, false, new[] { "" }, "bi");
                return null;
            // Ordering comparisons: the compare superposition (the decimal
            // precedent -- a PREFIX can stack on the compare icall).
            case "op_GreaterThan":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 < il2cpp::runtime::icall_biginteger_compare", 2, true, false, false, new[] { "", "" });
                return null;
            case "op_LessThan":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 > il2cpp::runtime::icall_biginteger_compare", 2, true, false, false, new[] { "", "" });
                return null;
            case "op_LessThanOrEqual":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 >= il2cpp::runtime::icall_biginteger_compare", 2, true, false, false, new[] { "", "" });
                return null;
            case "op_GreaterThanOrEqual":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 <= il2cpp::runtime::icall_biginteger_compare", 2, true, false, false, new[] { "", "" });
                return null;
            case "op_Equality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_equals", 2, true, false, false, new[] { "", "" });
                return null;
            case "op_Inequality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("!il2cpp::runtime::icall_biginteger_equals", 2, true, false, false, new[] { "", "" });
                return null;
            // int CompareTo(BigInteger) / bool Equals(BigInteger): instance --
            // the receiver is an ldloca ADDRESS ("deref:bi"), the operand a
            // value ("" cast).
            case "CompareTo":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_compare", 1, true, false, true, new[] { "deref:bi", "" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: a boxed BigInteger has no runtime box class");
                return null;
            case "Equals":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_biginteger_equals", 1, true, false, true, new[] { "deref:bi", "" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("Equals(object) not wired: a boxed BigInteger has no runtime box class");
                return null;
            // string ToString(): the receiver is an ldloca ADDRESS.
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_biginteger_to_string", 0, true, true, true, new[] { "deref:bi" });
                return null;
            default: return null;
        }
    }
}
