namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Nullable -- System.Nullable`1<T> member mappings (GN-2).
// Owners: System.Nullable`1
//
// The runtime models a Nullable<T> VALUE as an Il2CppObject* (null = no
// value, boxed T = a value -- the TypeEmitter M6 contract for Nullable
// fields); it has no generated C++ struct and no nullable icalls. The
// members therefore RETURN SYNTHETIC MARKERS that the CodeGenerator
// intercepts and inlines (the boxed-object semantics):
//   * get_HasValue -> (obj != nullptr)
//   * get_Value    -> unbox by the underlying T (il2cpp_unbox_int32/...)
// The markers are NOT runtime symbols -- they must never reach the C++ text
// as a call (the emitter special-cases them in the call emission paths;
// any leak would be an undefined-symbol link failure, so both the
// expression and statement emitters intercept them).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapNullable(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // get_HasValue(): instance getter, receiver-only (0 explicit params),
            // returns bool/int32. ParamCount=0 = the fold pops only the receiver
            // (the call-path instance branch pops this separately).
            case "get_HasValue":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_has_value", 0, true, false, true);
                break;
            // get_Value(): instance getter, receiver-only, returns the underlying T.
            case "get_Value":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_nullable_get_value", 0, true, false, true);
                break;
            default:
                break;
        }
        return null;
    }
}