namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.CustomAttributeExtensions -- System.Reflection.CustomAttributeExtensions
// (refl3 item 1). Owner: System.Reflection.CustomAttributeExtensions.
//
// net48 semantics the arms reproduce (oracle-pinned by p-refl3-1attrs):
//   * GetCustomAttribute<T>(MemberInfo) -- the generic SINGULAR extension. The
//     receiver rides as arg0 (the element MemberInfo VALUE: for a Type receiver
//     the full type-name string -- the simplified member/type model). It routes
//     to il2cpp_attribute_get_single_ex_named(element, TFullName, inherit=1):
//     net48 default inherit for the generic singular is TRUE (the base-chain
//     attribute on Attr3Derived is found), and the attributeType filter is the
//     generic argument's FULL NAME (genArgNames[0]).
//   * GetCustomAttributes<T>(MemberInfo) -- the generic PLURAL extension. Same
//     element + attributeType routing to il2cpp_attribute_get_custom_ex_named
//     (inherit=1 -- net48 plural default), returning the attribute array.
//
// The emitted literal for the attributeType (T full name -- a compile-time
// constant, not an IL argument) is il2cpp::il2cpp_string_intern_literal
// (intern-pool: safe at startup and read-only after, no GC root needed -- the
// same literal the compile-time typeof(T) fold would produce).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCustomAttributeExtensions(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string[]? genArgNames)
    {
        if (name is "GetCustomAttribute" or "GetCustomAttributes")
        {
            // The generic form (GetCustomAttribute<T>(MemberInfo)): the IL
            // pushes ONLY the element (arg0). The attributeType (the generic
            // argument T) is a COMPILE-TIME full name carried by genArgNames[0]
            // -- no IL slot. inherit is net48's default for the generic
            // singular/plural = 1 (probe-pinned). The appendLiterals inject the
            // T-name string (intern) + the inherit literal after arg0.
            if (ps.Length == 1 && genArgNames is { Length: >= 1 } && genArgNames[0] is { } tName)
            {
                var cpp = name == "GetCustomAttributes"
                    ? "il2cpp::runtime::il2cpp_attribute_get_custom_ex_named"
                    : "il2cpp::runtime::il2cpp_attribute_get_single_ex_named";
                var tLit = $"il2cpp::il2cpp_string_intern_literal(\"{tName}\")";
                return I(cpp, 1, true, true, false, new[] { "" },
                    returnTypeName: name == "GetCustomAttributes" ? "System.Object[]" : "System.Attribute",
                    appendLiterals: new[] { tLit, "1" });
            }
            throw new NotSupportedException(
                $"LOUD whitelist (net48 has no such overload): {owner}::{name} arity {ps.Length} -- net48 has only the generic " +
                "GetCustomAttribute<T>(MemberInfo) / GetCustomAttributes<T>(MemberInfo) extension forms " +
                "[Reference Source: net48 mscorlib System.Reflection.CustomAttributeExtensions]");
        }
        // REFL3 (item 3): CustomAttributeExtensions.IsDefined(MemberInfo
        // element, Type attributeType) -- the 1-arg STATIC extension (net48
        // IL-owner; csc binds it for `typeof(X).IsDefined(typeof(T))`, shadowing
        // the instance MemberInfo.IsDefined(Type)). net48 default inherit =
        // TRUE (oracle-pinned by p-refl3-3gaps: the base-chain [Tag3G] on
        // Attr3GDerived answers True). Maps to the managed-string existence
        // primitive with an appended inherit literal -- the same resolution as
        // the 2-arg Attribute.IsDefined (element + attributeType name strings).
        if (name == "IsDefined" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
            return I("il2cpp::runtime::il2cpp_attribute_is_defined_named", 2, true, false, false,
                new[] { "", "" }, appendLiterals: new[] { "1" });
        if (name == "IsDefined")
            throw new NotSupportedException(
                $"LOUD whitelist (net48 has no such overload): {owner}::IsDefined arity {ps.Length} -- net48 has only " +
                "IsDefined(MemberInfo element, Type attributeType) " +
                "[Reference Source: net48 mscorlib System.Reflection.CustomAttributeExtensions.IsDefined]");
        return null;
    }
}