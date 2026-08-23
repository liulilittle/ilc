namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Decimal -- System.Decimal icall mappings (T1 contract:
// src/runtime/include/internal/decimal.h, implementation decimal.cpp).
// Owners: System.Decimal
//
// VALUE-TYPE MODEL: a decimal is a 16-byte value (Il2CppDecimal -- flags/
// hi32/lo64, byte-identical to .NET). Like Guid, every decimal icall takes
// RAW POINTERS to value slots (16 bytes cannot ride the 8-byte single-
// register value channel). Receivers (ldloca ADDRESSES), decimal arguments
// and the result slot therefore pass through UNCAST ("" -- the address
// expression IS the icall's pointer argument).
//
// RESULT-SLOT CONTRACT (T1 must honor): icalls that PRODUCE a decimal take
// the RESULT SLOT as their FIRST parameter, and the AssignStmt special case
// (CodeGenerator.cs "dec" branch of the guid-style value-slot handling)
// emits `fn(target, args...)`. In-place mutation (DecCalc's d1-in-place
// shape) is T1's internal business (copy inputs, write the result slot).
// The "dec" ResultKind marker carries the slot shape to the declaration
// site (Il2CppDecimal temp -- the TypeCppName entry + "dec" kind branch).
//
//   icall_decimal_add   (Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
//   icall_decimal_sub   (Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
//   icall_decimal_mul   (Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
//   icall_decimal_div   (Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)  -- div0 -> DivideByZeroException
//   icall_decimal_rem   (Il2CppDecimal* result, const Il2CppDecimal* a, const Il2CppDecimal* b)
//   icall_decimal_negate(Il2CppDecimal* result, const Il2CppDecimal* a)                          -- flags ^ SignMask
//   icall_decimal_parse (Il2CppDecimal* result, Il2CppString* s)                                 -- OverflowException on overflow
//   icall_decimal_ctor_5(Il2CppDecimal* result, int32_t lo, int32_t mid, int32_t hi, bool sign, uint8_t scale)
//   icall_decimal_ctor_int32 (Il2CppDecimal* result, int32_t v)
//   icall_decimal_ctor_int64 (Il2CppDecimal* result, int64_t v)
//   icall_decimal_round (Il2CppDecimal* result, const Il2CppDecimal* d, int32_t decimals, int32_t mode)
//   icall_decimal_truncate (Il2CppDecimal* result, const Il2CppDecimal* d)
//   icall_decimal_floor / icall_decimal_ceiling (Il2CppDecimal* result, const Il2CppDecimal* d)
//   int32_t icall_decimal_compare(const Il2CppDecimal* a, const Il2CppDecimal* b)  -- VarDecCmp (-1/0/1)
//   bool    icall_decimal_equals(const Il2CppDecimal* a, const Il2CppDecimal* b)
//   int32_t icall_decimal_to_int32/int64/uint32/uint64(const Il2CppDecimal* d)     -- truncate + range check (OverflowException)
//   int32_t icall_decimal_get_hash_code(const Il2CppDecimal* d)
//   Il2CppString* icall_decimal_to_string(const Il2CppDecimal* d)                  -- G default (T2)
//
// DEPENDENCY GATE (decimal-domain.md 3.3): T1/T2 must land these symbols
// BEFORE any decimal-using test case is enabled -- the mapping is dormant
// (no current e2e case exercises decimal; fmtchain.cs:16 stays excluded).
//
// Known-unwireable members throw NotSupportedException (LOUD):
//   * ToString(IFormatProvider) -- no icall shape (TryMapFormat's guard).
//   * Equals(object)/CompareTo(object) -- a boxed decimal needs the runtime
//     box class (System_Decimal_class, T1 boxingext2) -- unwired until the
//     box icalls land (CodeGenerator box/unbox tables already route them).
//   * .ctor(float/double), ToDouble/ToSingle -- VarDecFromR4/R8 deferred (v1).
//   * ToString() on a BOXED decimal receiver routes through
//     icall_object_to_string (valuesemantics.cpp klass dispatch, T2 arm).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDecimal(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, byte ret = 0x00)
    {
        switch (name)
        {
            // op_Explicit(decimal) -> primitive: the SOURCE is always decimal
            // (ps[0] == 0x11); the TARGET type is only visible in the
            // signature's RETURN byte, which the dispatcher now forwards.
            case "op_Explicit":
                if (ps.Length == 1 && ps[0] == 0x11)
                {
                    switch (ret)
                    {
                        case 0x0D: return I("il2cpp::runtime::icall_decimal_to_double", 1, true, false, false, new[] { "dec" }, "d");
                        case 0x0C: return I("il2cpp::runtime::icall_decimal_to_float", 1, true, false, false, new[] { "dec" }, "d");
                        case 0x08: return I("il2cpp::runtime::icall_decimal_to_int32", 1, true, false, false, new[] { "dec" });
                        case 0x0A: return I("il2cpp::runtime::icall_decimal_to_int64", 1, true, false, false, new[] { "dec" }, "l");
                    }
                }
                return null;
            // static decimal op_Addition/op_Subtraction/op_Multiply/op_Division/
            // op_Remainder(decimal, decimal): both operands are ldloca ADDRESSES
            // of 16-byte slots -- uncast (Guid op_Equality precedent). The
            // result slot is prepended by the AssignStmt "dec" special case.
            case "op_Addition":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_add", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "op_Subtraction":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_sub", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "op_Multiply":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_mul", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "op_Division":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_div", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            // op_Modulus is the CLR NAME of the decimal % operator method
            // (net48 metadata: System.Decimal::op_Modulus(decimal, decimal));
            // op_Remainder is the ECMA generic-name alias the older map
            // carried -- both spellings route to the same rem icall.
            case "op_Remainder":
            case "op_Modulus":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_rem", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            // static decimal op_UnaryNegation(decimal): flags ^ SignMask.
            case "op_UnaryNegation":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_negate", 1, true, false, false, new[] { "dec" }, "dec");
                return null;
            // static decimal Negate(decimal) -- same icall.
            case "Negate":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_negate", 1, true, false, false, new[] { "dec" }, "dec");
                return null;
            // static decimal op_Implicit(int32/int64/uint32/uint64): the
            // integer widening folds to the ctor icalls (result slot prepended
            // by the AssignStmt "dec" special case -- `1m + 2` emits
            // `icall_decimal_ctor_int(t, 2)`). float/double are VarDecFromR4/R8
            // deferred (v1) -- LOUD.
            case "op_Implicit":
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_decimal_ctor_int32", 1, true, false, false, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_decimal_ctor_int64", 1, true, false, false, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x09)
                    return I("il2cpp::runtime::icall_decimal_ctor_uint32", 1, true, false, false, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0B)
                    return I("il2cpp::runtime::icall_decimal_ctor_uint64", 1, true, false, false, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_decimal_ctor_float", 1, true, false, false, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_decimal_ctor_double", 1, true, false, false, null, "dec");
                return null;
            // Ordering comparisons: the emitter formats calls as
            // {CppName}({args}) -- a SUFFIX cannot stack on the compare icall,
            // but a PREFIX can (op_Inequality's "!" precedent): `cmp > 0` is
            // `0 < icall_decimal_compare(a, b)` (decimal-domain.md 2.1's
            // "compare superposition" design, prefix form).
            case "op_GreaterThan":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 < il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            case "op_LessThan":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 > il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            case "op_LessThanOrEqual":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 >= il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            case "op_GreaterThanOrEqual":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("0 <= il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            // == / != : DecCalc.Equals on the byte pairs (Guid op_Equality
            // precedent -- the static 2-arg shape; != = "!" prefix).
            case "op_Equality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_equals", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            case "op_Inequality":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("!il2cpp::runtime::icall_decimal_equals", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            // static int32 Compare(decimal, decimal) / instance CompareTo(decimal).
            case "Compare":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            // The NAMED arithmetic API family (decimal.Add/Subtract/...):
            // same icalls as the operator spellings -- the fixture-facing
            // surface (icallwide E2E exercises the named forms).
            case "Add":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_add", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "Subtract":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_sub", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "Multiply":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_mul", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "Divide":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_div", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
            case "Remainder":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_rem", 2, true, false, false, new[] { "dec", "dec" }, "dec");
                return null;
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_compare", 2, true, false, false, new[] { "dec", "dec" });
                return null;
            case "CompareTo":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_compare", 1, true, false, true, new[] { "dec", "dec" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: a boxed decimal needs the runtime System_Decimal box class (T1 boxingext2), unwired until the box icalls land");
                return null;
            // bool Equals(decimal) -- instance, receiver address uncast.
            case "Equals":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_equals", 1, true, false, true, new[] { "dec", "dec" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("Equals(object) not wired: a boxed decimal needs the runtime System_Decimal box class (T1 boxingext2), unwired until the box icalls land");
                return null;
            case "GetHashCode":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_decimal_get_hash_code", 0, true, false, true, new[] { "dec" });
                return null;
            // string ToString(): receiver address uncast (the icall's const
            // Il2CppDecimal* input). ToString(format) routes through
            // TryMapFormat (icall_decimal_to_string_custom); the provider
            // overload has no icall shape (TryMapFormat's guard throws).
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_decimal_to_string", 0, true, true, true, new[] { "dec" });
                return null;
            // static decimal Parse(string): the result slot is prepended by
            // the AssignStmt "dec" special case -- fn(result, s).
            case "Parse":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_decimal_parse", 1, true, false, false, new[] { "s" }, "dec");
                return null;
            // static bool TryParse(string, out decimal): the out param is the
            // BYREF (0x10) ldloca address of the result slot -- passed through
            // UNCAST as the icall's output pointer (Guid.TryParse precedent;
            // ps[1] carries the 0x11 VALUETYPE element byte).
            case "TryParse":
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_try_parse", 2, true, false, false, new[] { "s", "" });
                return null;
            // static decimal Round(decimal[, int32[, MidpointRounding]]):
            // mode/decimals are int32 slots; the decimal is an address. The
            // runtime icall signature is icall_decimal_round(result, d,
            // decimals, mode) -- the C# 1-arg and 2-arg overloads push FEWER
            // arguments than the icall declares, so the missing trailing
            // defaults are injected as literal `0`s (decimals = 0, and
            // MidpointRounding.ToEven = 0 for mode). The 3-arg form passes
            // both through.
            case "Round":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_round", 1, true, false, false, new[] { "dec" }, "dec",
                        appendLiterals: new[] { "0", "0" });
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_decimal_round", 2, true, false, false, new[] { "dec", "i" }, "dec",
                        appendLiterals: new[] { "0" });
                if (ps.Length == 3 && ps[0] == 0x11 && ps[1] == 0x08 && ps[2] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_round", 3, true, false, false, new[] { "dec", "i", "i" }, "dec");
                return null;
            // static decimal Truncate/Floor/Ceiling(decimal).
            case "Truncate":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_truncate", 1, true, false, false, new[] { "dec" }, "dec");
                return null;
            case "Floor":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_floor", 1, true, false, false, new[] { "dec" }, "dec");
                return null;
            case "Ceiling":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_ceiling", 1, true, false, false, new[] { "dec" }, "dec");
                return null;
            // static int32/... ToInt32/ToInt64/ToUInt32/ToUInt64(decimal):
            // truncate + range check (OverflowException). Plain int32/int64
            // value results (ResultKind "l" for the 64-bit forms).
            case "ToInt32":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_to_int32", 1, true, false, false, new[] { "dec" });
                return null;
            case "ToUInt32":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_to_uint32", 1, true, false, false, new[] { "dec" });
                return null;
            case "ToInt64":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_to_int64", 1, true, false, false, new[] { "dec" }, "l");
                return null;
            case "ToUInt64":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_to_uint64", 1, true, false, false, new[] { "dec" }, "l");
                return null;
            // .ctor(int32)/.ctor(int64)/.ctor(int32,int32,int32,bool,uint8):
            // the newobj fold's vctor path passes the value slot as the FIRST
            // argument (write-through -- NO ResultIsReceiver; the icall writes
            // the slot, exactly the Guid parse output-buffer shape). The
            // 5-arg form is the IL shape of a decimal LITERAL (1.5m).
            case ".ctor":
                // The "dec" ResultKind marker is REQUIRED (blocker A): the
                // ldloca + call ctor shape folds to an AssignStmt whose value
                // is a CallExpr, and the emitter's "dec" special case turns
                // `t = ctor(...)` into the write-through `ctor((&t), ...)`
                // (the icall takes the RESULT SLOT first -- CodeGenerator.cs
                // AssignStmt "dec" branch). Without the marker the call falls
                // to the generic path and emits `t = icall_decimal_ctor_5(...)`
                // -- a void call in an assignment (C++ error).
                //
                // INSTANCE + VOID (blocker B): a decimal `.ctor` is an IL
                // INSTANCE call -- the receiver (an ldloca ADDRESS) rides the
                // stack below the arguments. With IsInstance=true +
                // ReturnsValue=false the fold pops the receiver as args[0]
                // and takes the void-CallStmt path: the LocAddrExpr receiver
                // renders `(&loc1)` -- exactly the icall's Il2CppDecimal* out
                // slot (no extra CodeGenerator addressing needed), the slot
                // gets DECLARED via the call-args-LocAddrExpr rule + typed by
                // ValueTypeCtorOwnerCpp, and the ctor pushes NOTHING (correct
                // IL stack semantics -- the value lands in the slot, the fold
                // no longer leaks a dangling LocAddrExpr receiver). The NEWOBJ
                // shape still routes through the vctor_ path (ResultIsReceiver
                // false -- the icall writes the slot), which consults neither
                // IsInstance nor ReturnsValue, so both shapes stay correct.
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_decimal_ctor_int32", 1, false, false, true, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_decimal_ctor_int64", 1, false, false, true, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x09)
                    return I("il2cpp::runtime::icall_decimal_ctor_uint32", 1, false, false, true, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0B)
                    return I("il2cpp::runtime::icall_decimal_ctor_uint64", 1, false, false, true, null, "dec");
                if (ps.Length == 5)
                    return I("il2cpp::runtime::icall_decimal_ctor_5", 5, false, false, true, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0C)
                    return I("il2cpp::runtime::icall_decimal_ctor_float", 1, false, false, true, null, "dec");
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_decimal_ctor_double", 1, false, false, true, null, "dec");
                return null;
            // ToDouble/ToSingle -- the VarR4/R8FromDec conversions (the
            // netcore21+ audit closure; decimal.cpp exact decomposition).
            case "ToDouble":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_decimal_to_double", 1, true, false, true, new[] { "dec" }, "d");
                return null;
            case "ToSingle":
                if (ps.Length == 1 && ps[0] == 0x11)
                    // float rides the double carrier (the M-stage
                    // float-in-double convention; no separate "f" kind).
                    return I("il2cpp::runtime::icall_decimal_to_float", 1, true, false, true, new[] { "dec" }, "d");
                return null;
            // P6: Decimal.FCallToInt32 (stub).
            case "FCallToInt32": return I("il2cpp::runtime::icall_decimal_fcall_to_int32", 0, true, false, false);
            default: return null;
        }
    }
}
