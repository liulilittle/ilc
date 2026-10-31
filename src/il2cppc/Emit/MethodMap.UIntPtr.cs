namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.UIntPtr -- System.UIntPtr pointer-arithmetic icall mappings
// (src/runtime/include/internal/intptr.h).
// Owners: System.UIntPtr
//
// The runtime reuses the IntPtr helpers for UIntPtr values: Add/Subtract/
// op_Equality/CompareTo behave identically on the bit pattern, and the bit
// pattern of a UIntPtr passes through an intptr_t parameter unchanged.
// UIntPtr has NO ToInt64/ToInt32 mapping: its unsigned conversions are plain
// zero-extension casts the transpiler emits directly (intptr.h).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapUIntPtr(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, byte ret = 0x00)
    {
        // G8: Roslyn emits the native-uint encoding (0x19) for UIntPtr
        // parameters/returns, while hand-built or mscorlib blobs use 0x11
        // (VALUETYPE) -- the IntPtr predicate plus 0x19.
        static bool IsUIntPtrByte(byte b) => IsIntPtrByte(b) || b == 0x19;
        switch (name)
        {
            // static int Size -- platform pointer width (envnf D5), same
            // sizeof(void*) contract as IntPtr.Size.
            case "get_Size":
                return I("il2cpp::runtime::il2cpp_intptr_get_size", 0, true, false, false);
            // Batch 155c: UIntPtr(int32/int64/uint64) ctor -- the value is
            // the pointer carrier (the runtime ctor takes intptr_t; the
            // unsigned bit pattern passes through).
            case ".ctor":
                if (ps.Length != 1 || (ps[0] != 0x08 && ps[0] != 0x09 && ps[0] != 0x0A && ps[0] != 0x0B)) return null;
                // G7: ldloca-address ctor shape -- see MethodMap.IntPtr.cs.
                return I("il2cpp::runtime::il2cpp_intptr_ctor", 1, true, false, true,
                    new[] { "l" }, "ptr", resultIsReceiver: true);
            // instance ulong ToUInt64(): the pointer value as unsigned
            // (the int64 bit pattern carries the unsigned value).
            case "ToUInt64":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int64", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:v" : "" }, "l");
            // instance long ToInt64(): the pointer value sign-extended.
            case "ToInt64":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int64", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:v" : "" }, "l");
            // static UIntPtr Add/Subtract(UIntPtr, int): same bit-pattern math
            // as IntPtr (intptr.h).
            case "Add":
            case "Subtract":
                if (ps.Length != 2 || !IsUIntPtrByte(ps[0]) || ps[1] != 0x08) return null;
                return I(name == "Add"
                        ? "il2cpp::runtime::il2cpp_intptr_add"
                        : "il2cpp::runtime::il2cpp_intptr_subtract",
                    2, true, false, false);
            // static bool op_Equality(UIntPtr, UIntPtr): raw bit-pattern equality.
            case "op_Equality":
                if (ps.Length != 2 || !IsUIntPtrByte(ps[0]) || !IsUIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_op_equality", 2, true, false, false);
            // UIntPtr.op_Inequality(UIntPtr, UIntPtr): negation of the
            // bit-pattern equality (same runtime helper as IntPtr).
            case "op_Inequality":
                if (ps.Length != 2 || !IsUIntPtrByte(ps[0]) || !IsUIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_op_inequality", 2, true, false, false);
            // instance int CompareTo(UIntPtr): unsigned-address ordering.
            case "CompareTo":
                if (ps.Length != 1) return null;
                if (ps[0] == 0x1C)
                    throw new NotSupportedException(
                        "LOUD whitelist (no runtime descriptor/model): UIntPtr.CompareTo(object) -- the icall takes an intptr_t " +
                        "argument and a boxed pointer has no runtime box class " +
                        "[Reference Source: net48 mscorlib System.UIntPtr.CompareTo]");
                if (!IsUIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_compare", 1, true, false, true,
                    new[] { receiverIsAddress ? "deref:v" : "", "" });
            // instance uint ToUInt32(): CHECKED conversion (net48 ToUInt32
            // throws System.OverflowException above UINT32_MAX).
            case "ToUInt32":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_uintptr_to_uint32", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:v" : "" });
            // instance string ToString(): UNSIGNED DECIMAL
            // (UIntPtr(ulong.MaxValue) -> "18446744073709551615"). net48 has
            // NO UIntPtr.ToString(string) overload (csc CS1501 on
            // ua.ToString("X")), so only the 0-argument form is mapped.
            case "ToString":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_uintptr_to_string", 0, true, true, true,
                    new[] { receiverIsAddress ? "deref:v" : "" });
            // instance int GetHashCode(): low 32 bits MASKED to [0, INT32_MAX]
            // (ulong.MaxValue -> 2147483647, 0x80000000 -> 0).
            case "GetHashCode":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_uintptr_get_hash_code", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:v" : "" });
            // G8: op_Explicit -- BOTH directions of the explicit operators.
            // (UIntPtr)uint/ulong -> carrier (ret = native uint 0x19).
            case "op_Explicit":
                if (ps.Length == 1 && ret == 0x19 && (ps[0] == 0x09 || ps[0] == 0x0B || IsUIntPtrByte(ps[0])))
                    return I("il2cpp::runtime::il2cpp_intptr_ctor", 1, true, false, false,
                        new[] { "l" }, "ptr");
                // UIntPtr -> ulong: unsigned bit pattern (identity on 64-bit).
                if (ps.Length == 1 && ret == 0x0B && IsUIntPtrByte(ps[0]))
                    return I("il2cpp::runtime::il2cpp_intptr_to_int64", 1, true, false, false,
                        null, "l");
                // UIntPtr -> uint: CHECKED (net48 operator uint == ToUInt32).
                if (ps.Length == 1 && ret == 0x09 && IsUIntPtrByte(ps[0]))
                    return I("il2cpp::runtime::il2cpp_uintptr_to_uint32", 1, true, false, false);
                return null;
            default: return null;
        }
    }
}
