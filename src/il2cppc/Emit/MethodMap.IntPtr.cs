namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.IntPtr -- System.IntPtr pointer-arithmetic icall mappings
// (src/runtime/include/internal/intptr.h).
// Owners: System.IntPtr
//
// An IntPtr value rides in an int32_t slot (the M2 value carrier -- the
// emitter has no intptr_t cast code); the helpers take intptr_t and the
// int32_t value sign-extends on the parameter conversion, which is the
// semantic baseline in intptr.h. The receiver cast is therefore:
//   * ldloca receiver (receiverIsAddress)  -> "deref:i" (read the int32 slot)
//   * plain VALUE receiver (Add/Subtract result chains) -> "" pass-through
//   * boxed receivers are UNREACHABLE (the runtime has no IntPtr box class;
//     the emitter's box table rejects them LOUDLY) -- no unbox cast exists.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapIntPtr(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // wf2 (winforms1): IntPtr(int32) ctor -- the int32 value IS the
            // pointer carrier (identity sign-extend to pointer width).
            // Batch 155b: IntPtr(int64) ctor -- the int64 value carries
            // directly (the runtime ctor now takes intptr_t, so both widths
            // route to the same icall).
            case ".ctor":
                if (ps.Length != 1 || (ps[0] != 0x08 && ps[0] != 0x0A)) return null;
                return I("il2cpp::runtime::il2cpp_intptr_ctor", 1, true, false, false,
                    new[] { "l" }, "ptr", resultIsReceiver: true);
            // static IntPtr Add/Subtract(IntPtr, int): offset is a SIGNED int32
            // sign-extended to pointer width before the addition (intptr.h).
            // (G2: IntPtr params are raw native-int 0x17/0x18 in the real
            // mscorlib blobs, 0x11 VALUETYPE in Roslyn user signatures --
            // every guard accepts all three via IsIntPtrByte.)
            case "Add":
            case "Subtract":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || ps[1] != 0x08) return null;
                return I(name == "Add"
                        ? "il2cpp::runtime::il2cpp_intptr_add"
                        : "il2cpp::runtime::il2cpp_intptr_subtract",
                    2, true, false, false);
            // static bool op_Equality(IntPtr, IntPtr): raw bit-pattern equality.
            case "op_Equality":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || !IsIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_op_equality", 2, true, false, false);
            // static bool op_Inequality(IntPtr, IntPtr): the negation of the
            // bit-pattern equality (intptr.h: il2cpp_intptr_op_inequality).
            // (G1: this case was missing entirely, so the member fell back to
            // the name heuristic -> INSTANCE classification -> StackFold
            // "pops this" on `p != IntPtr.Zero`.)
            case "op_Inequality":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || !IsIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_op_inequality", 2, true, false, false);
            // instance int64 ToInt64(): sign-extends the pointer value
            // (identity on 64-bit targets).
            case "ToInt64":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int64", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:l" : "" }, "l");
            // instance int32 ToInt32(): low 32 bits of the pointer value.
            case "ToInt32":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::il2cpp_intptr_to_int32", 0, true, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "" });
            // instance int CompareTo(IntPtr): orders pointers as UNSIGNED
            // addresses, returns -1 / 0 / 1.
            case "CompareTo":
                if (ps.Length != 1) return null;
                if (ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: the icall takes an intptr_t argument and a boxed pointer cannot reach it in the M-stage");
                if (!IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::il2cpp_intptr_compare", 1, true, false, true,
                    new[] { receiverIsAddress ? "deref:i" : "", "" });
            // batch 177 (intptrret probe): `(IntPtr)long` / `(IntPtr)int`
            // explicit conversion calls IntPtr::op_Explicit -- the int64/int32
            // value IS the pointer carrier (same semantics as the IntPtr ctor:
            // the int64 sign-extends, the int32 widens; il2cpp_intptr_ctor
            // takes intptr_t). Without this case the member fell through to
            // the user-method generated-name path and emitted a call to a
            // function that is never defined (compilation error).
            case "op_Explicit":
                if (ps.Length != 1 || (ps[0] != 0x08 && ps[0] != 0x0A && ps[0] != 0x0B)) return null;
                return I("il2cpp::runtime::il2cpp_intptr_ctor", 1, true, false, false,
                    new[] { "l" }, "ptr");
            default: return null;
        }
    }
}
