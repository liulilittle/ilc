namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.HandleRef -- System.Runtime.InteropServices.HandleRef members
// (src/runtime/include/internal/il2cpp_handleref.h: the two-field mirror
// struct + the P/Invoke argument bridge).
// Owner: System.Runtime.InteropServices.HandleRef
//
// VALUE MODEL: a HandleRef value IS the runtime mirror struct
// (Il2CppHandleRef: _wrapper, _handle) -- see CodeGenerator.TypeCppName and
// the "v:" local declaration arm. The members map to inline accessors over
// that struct; no call site writes a raw field offset.
//
// SURFACE (net48 reference assembly: HandleRef has exactly these members):
//   .ctor(object wrapper, IntPtr handle) -> il2cpp_handleref_ctor(&slot, ...)
//   get_Handle                           -> il2cpp_handleref_handle[_ptr]
//   get_Wrapper                          -> il2cpp_handleref_wrapper[_ptr]
//
// PARAM BYTES: object is 0x1C (OBJECT); IntPtr is the native-int family
// (0x18 "I" / 0x17/0x11 -- IsIntPtrByte).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapHandleRef(string owner, string name, byte[] ps, int count,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // new HandleRef(wrapper, handle): the value-type newobj path
                // addresses the destination slot ("hrp" is the receiver cast;
                // the ctor writes _wrapper/_handle through it). A null
                // wrapper is legal and passes through unchanged.
                if (count == 2 && ps[0] == 0x1C && IsIntPtrByte(ps[1]))
                    return I("il2cpp::il2cpp_handleref_ctor", 2, false, false, true,
                        new[] { "hrp", "", "n" });
                return null;
            case "get_Handle":
                // HandleRef.Handle: the native-int handle value read through
                // the mirror struct (address receiver -> the ldloca form).
                if (count != 0) return null;
                return I(receiverIsAddress
                        ? "il2cpp::il2cpp_handleref_handle_ptr"
                        : "il2cpp::il2cpp_handleref_handle",
                    0, true, false, true,
                    new[] { receiverIsAddress ? "hrp2" : "" }, "ptr");
            case "get_Wrapper":
                // HandleRef.Wrapper: the keep-alive object reference.
                if (count != 0) return null;
                return I(receiverIsAddress
                        ? "il2cpp::il2cpp_handleref_wrapper_ptr"
                        : "il2cpp::il2cpp_handleref_wrapper",
                    0, true, true, true,
                    new[] { receiverIsAddress ? "hrp2" : "" }, returnTypeName: "System.Object");
            default:
                // stacktrace-real message correction: "net48 has no such
                // member" was false -- net48's HandleRef also declares the
                // static ToIntPtr(HandleRef) plus the Object overrides. The
                // honest reason is "not wired in this runtime yet".
                throw new NotSupportedException(
                    $"System.Runtime.InteropServices.HandleRef::{name} is not wired in this runtime yet -- the wired " +
                    "surface covers .ctor(object, IntPtr)/get_Handle/get_Wrapper (net48 also declares static " +
                    "ToIntPtr and the Object overrides) " +
                    "[Reference Source runtime_interopservices_handleref.cs]");
        }
    }
}
