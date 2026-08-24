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
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
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
                return I("il2cpp::runtime::il2cpp_intptr_ctor", 1, true, false, false,
                    new[] { "l" }, "ptr", resultIsReceiver: true);
            // instance ulong ToUInt64(): the pointer value as unsigned
            // (the int64 bit pattern carries the unsigned value).
            case "ToUInt64":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int64", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, "l");
            // instance long ToInt64(): the pointer value sign-extended.
            case "ToInt64":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int64", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, "l");
            // static UIntPtr Add/Subtract(UIntPtr, int): same bit-pattern math
            // as IntPtr (intptr.h).
            case "Add":
            case "Subtract":
                if (ps.Length != 2 || ps[0] != 0x11 || ps[1] != 0x08) return null;
                return I(name == "Add"
                        ? "il2cpp::runtime::il2cpp_intptr_add"
                        : "il2cpp::runtime::il2cpp_intptr_subtract",
                    2, true, false, false);
            // static bool op_Equality(UIntPtr, UIntPtr): raw bit-pattern equality.
            case "op_Equality":
                if (ps.Length != 2 || ps[0] != 0x11 || ps[1] != 0x11) return null;
                return I("il2cpp::runtime::il2cpp_intptr_op_equality", 2, true, false, false);
            // instance int CompareTo(UIntPtr): unsigned-address ordering.
            case "CompareTo":
                if (ps.Length != 1) return null;
                if (ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: the icall takes an intptr_t argument and a boxed pointer cannot reach it in the M-stage");
                if (ps[0] != 0x11) return null;
                return I("il2cpp::runtime::il2cpp_intptr_compare", 1, true, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "", "" });
            default: return null;
        }
    }
}
