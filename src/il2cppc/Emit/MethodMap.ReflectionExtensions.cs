namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ReflectionExtensions -- Microsoft.Reflection.ReflectionExtensions
// + System.Reflection.IntrospectionExtensions icall mappings (il2cpp_typeext.h).
// Owner: Microsoft.Reflection.ReflectionExtensions, System.Reflection.
// IntrospectionExtensions
//
//   * GetTypeCode(Type) -- STATIC: wraps il2cpp_type_get_typecode. Returns the
//     net48 TypeCode value for the given runtime type descriptor.
//   * GetTypeInfo(Type) -- STATIC extension (system domain): the fold-identity
//     (REFL4 item 2). net48 returns a System.Reflection.TypeInfo, a TYPE
//     SUBCLASS -- every Type member (.Name/.FullName/.IsClass/...) resolves
//     with ordinary Type semantics. The simplified Type model carries a Type
//     VALUE as the full-name string, so GetTypeInfo maps to the runtime
//     IDENTITY icall (il2cpp_typeinfo_get_type_info -- returns the receiver
//     unchanged); ReturnTypeName "System.Type" keeps every downstream
//     Type-domain mapping (.Name/.FullName/.IsClass/BaseType/
//     GetProperty/==) resolving on the Type registry receiver.
//
// LOUD classification (impl-type2): all three LOUD throws are
// WHITELIST. GetTypeCode/IntrospectionExtensions.GetTypeInfo take exactly one
// (Type) argument in net48 (the arity catch-alls are category (4)
// net48-nonexistent); the TypeInfo-only surface (DeclaredMethods/
// DeclaredFields/DeclaredProperties/DeclaredEvents/DeclaredMembers/
// DeclaredNestedTypes + the GetDeclared* queries) is category (3) -- net48
// HAS these members (System.Reflection.TypeInfo) but the simplified
// reflection model has no descriptor to answer them, so they stay LOUD rather
// than silently resolving to a generated-name symbol.
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

    //--------------------------------------------------------------------------
    // REFL4 (item 2) -- System.Reflection.IntrospectionExtensions.GetTypeInfo
    // (that static-visibility extension, csc emits `call` with the Type VALUE
    // as arg0). The receiver is the Type VALUE full-name string; the result
    // must be the SAME string with the Type domain tracked (a TypeInfo is a
    // Type subclass -- its members are the Type members). Fold to identity.
    // The TypeInfo-only members (DeclaredMethods/DeclaredFields/... and
    // GetDeclared*) have NO simplified-model surface -- they must fail LOUD
    // (net48 has them, the model does not).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapIntrospectionExtensions(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (name == "GetTypeInfo")
        {
            // csc emits the STATIC extension as `call IntrospectionExtensions::
            // GetTypeInfo(Type)` -- the receiver rides as the single explicit
            // argument (the Type VALUE full-name string). Maps to the runtime
            // IDENTITY icall (typeinfo.cpp il2cpp_typeinfo_get_type_info --
            // returns the receiver unchanged); ReturnTypeName "System.Type"
            // records the result's managed type so every downstream Type
            // domain call (.Name/.FullName/.IsClass/BaseType/GetProperty/==)
            // resolves on the Type registry receiver -- including
            // typeof(int).GetTypeInfo() (identity survives the unregistered
            // BCL name). A fold-time IdentityMarker would lose the tracked
            // "System.Type" hint (the folded StringConstExpr result stores no
            // type name), so the real icall is used.
            if (ps.Length == 1 && ps[0] == 0x12)
                return I("il2cpp::il2cpp_typeinfo_get_type_info", 1, true, true, false,
                    new[] { "" }, returnTypeName: "System.Type");
            throw new NotSupportedException(
                $"IntrospectionExtensions.GetTypeInfo with {ps.Length} parameter(s) not wired -- only the (Type) overload folds to identity");
        }
        // TypeInfo-only members: explicit LOUD (never the generated-name path).
        if (name is "get_DeclaredMethods" or "get_DeclaredFields" or "get_DeclaredProperties"
            or "get_DeclaredEvents" or "get_DeclaredMembers" or "get_DeclaredNestedTypes"
            or "GetDeclaredMethod" or "GetDeclaredField" or "GetDeclaredProperty"
            or "GetDeclaredEvent" or "GetDeclaredNestedType")
        {
            throw new NotSupportedException(
                $"System.Reflection.TypeInfo::{name} is not wired in REFL4 -- the TypeInfo-only surface (Declared*/GetDeclared*) is absent from the simplified reflection model (net48 has it, the model does not)");
        }
        return null;
    }
}
