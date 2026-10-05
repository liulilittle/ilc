namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ReflectionExtensions -- Microsoft.Reflection.ReflectionExtensions
// icall mappings (il2cpp_typeext.h).
// Owner: Microsoft.Reflection.ReflectionExtensions
//
//   * GetTypeCode(Type) -- STATIC: wraps il2cpp_type_get_typecode. Returns the
//     net48 TypeCode value for the given runtime type descriptor.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapReflectionExtensions(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // ReflectionExtensions.GetTypeCode(Type) -- static. Wraps the runtime
            // typecode primitive (il2cpp_type_get_typecode). The single argument
            // is a Type VALUE (the type's full-name string -- the simplified Type
            // model), the exact shape the 1-param icall consumes.
            case "GetTypeCode":
                if (ps.Length == 1 && ps[0] == 0x12)
                    return I("il2cpp::runtime::il2cpp_type_get_typecode", 1, true, false, false,
                        new[] { "klass" });
                throw new NotSupportedException(
                    $"ReflectionExtensions.GetTypeCode with {ps.Length} parameter(s) not wired -- only the (Type) overload maps to il2cpp_type_get_typecode");
            default: return null;
        }
    }
}
