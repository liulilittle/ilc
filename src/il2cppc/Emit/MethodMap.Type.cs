namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Type -- System.Type icall mappings (il2cpp_typeext.h).
// Owners: System.Type
//
// Simplified Type model (il2cpp_typeext.h): a Type VALUE is the full type
// NAME as a managed string -- the same shape icall_object_get_type produces
// for GetType().ToString(). The typeext primitives CONSUME runtime
// descriptors (Il2CppClass* -- the address of the static {Type}_class
// descriptor the transpiler emits per translated type), the transpiler
// passes the descriptor where the IL holds a RuntimeTypeHandle/typeof
// value. GetTypeFromHandle converts a descriptor to the name string; the
// result is recorded as "System.Type" (ReturnTypeName -> the fold's
// objTypes) so later get_Name/GetTypeCode/Equals/IsAssignableFrom calls on
// it resolve -- the GetType()-linkage the fold already uses.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapType(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // Type.GetTypeFromHandle(RuntimeTypeHandle) -- static. The handle
            // IS the runtime descriptor address; the icall returns the type
            // NAME string (managed ref). ReturnTypeName records the result as
            // "System.Type" (same tracked shape as icall_object_get_type), so
            // .Name/.GetTypeCode/.Equals/.IsAssignableFrom on the result
            // resolve through this domain (the fold records it in objTypes).
            case "GetTypeFromHandle":
                return I("il2cpp::runtime::il2cpp_type_get_type_from_handle", 1, true, true, false,
                    new[] { "" }, returnTypeName: "System.Type");
            // Type.Name -- TWO paths (the Type-value model split):
            //   * RUNTIME receivers (a GetType() result -- the FULL-name
            //     string the runtime icall produces from klass->name, e.g.
            //     "System.Int32"): the runtime truncation primitive
            //     (il2cpp_type_get_short_name: "System.Int32" -> "Int32" --
            //     net48 Type.Name semantics). The old identity was wrong
            //     here: the runtime value is the FULL name and .NET Name is
            //     the SHORT name (the boxing2/boxdeep GetType mismatch).
            //   * FOLDED receivers (typeof(X) -- the full-name string the
            //     ldtoken fold produces): StackFold intercepts get_Name
            //     BEFORE this mapping and truncates to the SHORT name
            //     (net48: typeof(int).Name == "Int32").
            case "get_Name":
                return I("il2cpp::runtime::il2cpp_type_get_short_name", 0, true, true, true, returnTypeName: "System.String");
            // Type.GetTypeCode(Type) -- STATIC overload: int32 TypeCode from
            // the descriptor's name (net48 values, identical table to
            // icall_object_get_type_code). TWO paths:
            //   * FOLDED argument (typeof(X)): StackFold intercepts and
            //     folds the full-name string to the typecode CONSTANT
            //     (no call emitted -- the runtime primitive takes a
            //     DESCRIPTOR, not the name string).
            //   * RUNTIME argument: the descriptor-ABI icall is emitted
            //     (the name-string receiver does not compile -- the Type
            //     domain's documented runtime-path limitation).
            case "GetTypeCode":
                if (ps.Length == 1)
                    return I("il2cpp::runtime::il2cpp_type_get_typecode", 1, true, false, false, new[] { "" });
                // INSTANCE GetTypeCode() (IConvertible impl): the receiver is
                // the name-string Type value -- the runtime has no
                // string->TypeCode path (the static primitive takes a
                // descriptor); unwireable, must be LOUD.
                throw new NotSupportedException("Type.GetTypeCode() (instance) not wired in M5 -- the simplified Type model has no name-string to TypeCode path (static Type.GetTypeCode(Type) is wired)");
            // Type.IsAssignableFrom(Type c) -- instance: target = this,
            // source = c (runtime conformance model: identity, array prefix,
            // baseClass chain, interface scan).
            case "IsAssignableFrom":
                return I("il2cpp::runtime::il2cpp_type_is_assignable_from", 1, true, false, true,
                    new[] { "", "" });
            // Type.Equals(object o) -- instance type identity (icall_type_equals:
            // descriptor identity fast path, else name equality). Wired only
            // for a TRACKED Type argument -- a non-Type argument cannot be
            // passed to the descriptor-taking icall (no .NET "false for
            // non-Type" support in the runtime).
            case "Equals":
                if (ps.Length == 1 && arg0Type == "System.Type")
                    return I("il2cpp::runtime::icall_type_equals", 1, true, false, true, new[] { "", "" });
                throw new NotSupportedException($"Type.Equals(object) with {(arg0Type == null ? "untracked" : $"non-Type argument {arg0Type}")} not wired in M5 -- only a tracked System.Type argument maps to icall_type_equals");
            // Type.GetProperty/GetMethod/GetField(string name) -- M5.5
            // typeinfo queries (see typeinfo.h managed-string ABI). The
            // receiver is the Type VALUE (the full-name string, same shape
            // as GetTypeFromHandle produces); the runtime resolves the type
            // through the typeinfo registry, scans the member table (own
            // table first, baseType chain second) and returns the MEMBER-NAME
            // string on a hit -- the simplified member model (the member
            // VALUE is its name string, like the Type model) -- or null
            // when the type is not reflected / the member is absent (.NET
            // "not found -> null" contract, no throw). ReturnTypeName
            // "System.String" keeps WriteLine(object)/Concat linkage working
            // on the result. Only the (string) overload is wired; the
            // BindingFlags overloads have no runtime support in M5.5.
            case "GetProperty":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_property", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetProperty with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_property");
            case "GetMethod":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_method", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetMethod with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_method");
            case "GetField":
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::il2cpp_typeinfo_get_field", 1, true, true, true,
                        new[] { "", "" }, returnTypeName: "System.String");
                throw new NotSupportedException($"Type.GetField with {ps.Length} parameter(s) not wired in M5.5 -- only the (string) overload maps to il2cpp_typeinfo_get_field");
            // Type.op_Equality(Type, Type) -- the C# == operator (csc emits
            // `call`, never callvirt; the resolver's default op_* heuristic
            // classifies it INSTANCE and the call path would throw "pops this
            // on empty stack" -- delechain's op_Equality precedent). The
            // simplified Type model (il2cpp_typeext.h) makes a Type VALUE the
            // full-name string, so equality is STRING content equality -- the
            // same icall_string_equals_static the String domain uses. net48
            // reference semantics reduce to name equality (one RuntimeType
            // instance per type): typeof(int) == typeof(int) -> "System.Int32"
            // vs "System.Int32" -> True, typeof(int) == typeof(string) ->
            // "System.Int32" vs "System.String" -> False.
            case "op_Equality":
                if (ps.Length == 2)
                    return I("il2cpp::runtime::icall_string_equals_static", 2, true, false, false);
                throw new NotSupportedException($"Type.op_Equality with {ps.Length} arg(s) not wired in M5 -- only the 2-arg (Type, Type) overload maps to string equality");
            // P6: Type.GetTypeFromHandleUnsafe (stub: returns nullptr).
            case "GetTypeFromHandleUnsafe": return I("il2cpp::runtime::icall_type_get_type_from_handle_unsafe", 1, true, true, false, new[] { "i" });
            default: return null;
        }
    }
}
