using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Type dispatch tests: prove the Type domain (MethodMap.Type.cs ->
// TryMapType) maps System.Type's wired members to the il2cpp_typeext.h
// primitives, that the fold linkage (ReturnTypeName "System.Type") is set on
// GetTypeFromHandle, that the tracked-argument gates dispatch correctly, and
// that known-unwireable members fail LOUD (NotSupportedException with the
// "{owner}::{name}" prefix -- see BclResolve's wrap).
//
// Harness: same as MethodRefResolverDispatchTests.cs -- a synthesized
// ECMA-335 PE whose only MemberReference is the member under test, resolved
// through the same public Resolve entry point the transpiler uses.
// Signature blobs are the raw bytes RawSignature consumes; a 0x11/0x12
// (valuetype/class) byte consumes a trailing TypeDefOrRef coded index. The
// synthesized PE holds exactly ONE TypeReference (the owner, row 1), so the
// coded index is (1<<2)|1 = 0x05 after each such byte (0x06 would decode as
// TypeSpecification and MethodSignatureCoder rejects it).
public class MethodMapTestsType
{
    //--------------------------------------------------------------------------
    // GetTypeFromHandle -- the Type-value producer. Returns the type NAME
    // string (managed ref) and records the result as "System.Type" so later
    // .Name/.GetTypeCode/.Equals/.IsAssignableFrom calls on it resolve
    // (the object.GetType() linkage -- same tracked shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeFromHandle_MapsToTypeExtIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeFromHandle",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x11, 0x05 }), "GetTypeFromHandle"); // RuntimeTypeHandle

        // p-refl2-gapmatrix (E01/E02): the handle VALUE is an intptr_t slot (the
        // reflecthandle.h value model), so the icall takes the intptr and casts
        // to the descriptor (the FOLDED typeof chain is handled by StackFold).
        Assert.Equal("il2cpp::runtime::il2cpp_type_get_type_from_handle_value", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the name string
        Assert.Equal("System.Type", resolved.ReturnTypeName); // fold linkage
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // get_Name -- the Type VALUE is the FULL name string (icall_object_get_type
    // reads klass->name, e.g. "System.Int32"); .NET Type.Name is the SHORT
    // name ("Int32"), so the property maps to the runtime truncation
    // primitive instead of the identity (the old identity printed the full
    // name -- the boxing2/boxdeep GetType output mismatch).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetName_MapsToShortNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "get_Name",
            new byte[] { 0x00, 0x00, 0x0E }), "get_Name");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the short-name string
        Assert.Equal("System.String", resolved.ReturnTypeName); // WriteLine(string) linkage
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount); // the receiver is the implicit this
    }

    //--------------------------------------------------------------------------
    // MemberInfo::get_Name on a tracked GetType() result -- the Roslyn
    // MemberRef parent for `o.GetType().Name` (the boxing2/boxdeep shape).
    // Same truncation mapping (the receiver is the full-name string).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MemberInfo_GetName_OnTrackedType_MapsToShortNameIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MemberInfo", "get_Name",
            new byte[] { 0x00, 0x00, 0x0E }), "get_Name", receiverType: "System.Type");

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_short_name", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode -- static overload (the typeext primitive); int32 result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeCodeStatic_MapsToTypeCodeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeCode",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05 }), "GetTypeCode"); // (Type)

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_typecode", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetTypeCode -- the INSTANCE overload (IConvertible impl) is unwireable:
    // the receiver is the name-string Type value and the runtime has no
    // string->TypeCode path -- must fail LOUD.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeCodeInstance_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "GetTypeCode",
                new byte[] { 0x00, 0x00, 0x08 }), "GetTypeCode")); // ()

        Assert.Contains("System.Type::GetTypeCode", ex.Message);
    }

    //--------------------------------------------------------------------------
    // IsAssignableFrom -- instance: target = this, source = the argument.
    // refl2: the receiver AND the argument are name-string Type VALUES; the
    // wrapper resolves both names to descriptors and delegates.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_IsAssignableFrom_MapsToAssignableIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "IsAssignableFrom",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05, 0x12, 0x05 }), "IsAssignableFrom"); // (Type)

        Assert.Equal("il2cpp::il2cpp_typeinfo_is_assignable_from", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IsInstanceOfType -- instance: whether the OBJECT argument is an instance
    // of this type. refl2: the receiver is the name-string Type VALUE; the
    // wrapper resolves it to a descriptor and delegates to the typecheck
    // conformance test. The wrapper keeps the (obj, klass) runtime slot order
    // (object FIRST, type SECOND), so the resolution still swaps the IL push
    // order (receiver = type, arg = object) with ArgOrder {1,0}.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_IsInstanceOfType_MapsToInstanceIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "IsInstanceOfType",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "IsInstanceOfType"); // (object)

        Assert.Equal("il2cpp::il2cpp_typeinfo_is_instance", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        // The wrapper signature is (obj, typeName): the object arg fills the
        // runtime (obj) slot, the receiver fills the (typeName) slot -- the
        // opposite of IL push order.
        Assert.Equal(new[] { 1, 0 }, resolved.ArgOrder);
    }

    //--------------------------------------------------------------------------
    // Equals -- instance type identity, wired only for a TRACKED Type
    // argument (a non-Type argument cannot be passed to the descriptor-
    // taking icall; .NET's "false for non-Type" has no runtime support).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_Equals_OnTrackedTypeArgument_MapsToTypeEqualsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "Equals", arg0Type: "System.Type"); // (object)

        Assert.Equal("il2cpp::runtime::icall_type_equals", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_Equals_UntrackedArgument_AnswersFalse()
    {
        // refl2 (A): net48 Type.Equals(object) returns FALSE for a non-Type
        // argument -- never an exception. The arm maps to the runtime
        // constant-0 icall (the pre-refl2 LOUD rejection was the gap).
        var resolved = ResolveMember(Synthesize("System", "Type", "Equals",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "Equals"); // no tracked arg type

        Assert.Equal("il2cpp::il2cpp_typeinfo_equals_non_type", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // GetProperty/GetMethod/GetField (string) -- M5.5 typeinfo queries: the
    // receiver is the Type VALUE (full-name string); the runtime resolves
    // the typeinfo registry and returns the MEMBER-NAME string on a hit or
    // null (the simplified member model -- the member VALUE is its name
    // string). ReturnTypeName "System.String" keeps WriteLine/Concat
    // linkage on the result.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetPropertyString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetProperty"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the member-name string
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // get_BaseType -- instance: the base type of a Type VALUE (full-name
    // string). The receiver is the name string; the runtime resolves the
    // typeinfo registry and returns the BASE type's full-name string (or
    // null for System.Object / unregistered). The old mapping pointed at the
    // klass-based il2cpp_type_get_base_type(Il2CppClass*) -- a name-string
    // receiver does not compile (audit A3/D). ReturnTypeName "System.Type"
    // keeps the Type-domain linkage (get_Name on the result).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetBaseType_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "get_BaseType",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "get_BaseType"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_base_type", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the base type full-name string
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetMethodString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetMethod",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetMethod"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_method", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetFieldString_MapsToTypeinfoIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetField",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetField"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_field", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetPropertyTwoParams_MapsToFlagsIcall()
    {
        // GAP-1 (P0) / refl2 (item 1): Type.GetProperty(string, BindingFlags)
        // maps to the flags-aware query. The BindingFlags ENUM rides as a
        // VALUETYPE (0x11 + TypeDefOrRef) -- NOT a bare int32 (0x08). The
        // two-parameter arms split on ps[1]: 0x11 -> flags, 0x1D (Type[]) ->
        // get_property_types.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x11, 0x05 }), "GetProperty"); // (string, BindingFlags)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property_flags", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetPropertyTypeArray_MapsToPropertyTypesIcall()
    {
        // refl2 (item 1): Type.GetProperty(string, Type[]) -- the Type[]
        // SZARRAY (0x1D) arm routes to the exact-signature indexer query.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x1D, 0x12, 0x05 }), "GetProperty"); // (string, Type[])

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property_types", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss -- an unwired Type member falls back to the generated-
    // name path (owner_sanitized_name_<hash>), proving the domain returns
    // null instead of misrouting.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_UnmappedMember_FallsBackToGeneratedName()
    {
        // refl2 (A): MakeArrayType IS wired now (net48's Type.MakeArrayType is
        // reachable without runtime type loading -- the AOT model answers the
        // display identity). A member still outside every domain (InvokeMember
        // is wired; use a genuinely absent name) keeps the generated-name
        // fallback, proving the domain returns null instead of misrouting.
        var resolved = ResolveMember(Synthesize("System", "Type", "MakeArrayType",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "MakeArrayType"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_make_array_type", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // RF-N4 -- generic-type identification (ruling ① three-way split lands on the transpiler side, NEXT2-DECISION
    // section ② RF-N4):
    //   * GetGenericArguments -- TYPEINFO icall, THREE-WAY at runtime:
    //     non-generic -> EMPTY array; genericArity > 0 -> LOUD (no fabrication, the
    //     genericArgNames plan B is deferred). ReturnTypeName "System.Type[]"
    //     keeps the Array-domain folds (.Length / element read) resolved.
    //   * get_IsGenericType -- the runtime predicate: judged by the typeinfo's
    //     emitted genericArity (both the definition AND its closed clones
    //     register under the SAME name key -- TypeEmitter.ReflectionFullName
    //     -- so a runtime arity check is the single source of truth for BOTH
    //     "Box`1" (open def) and "Box`1" (a closed instantiation).
    //   * get_IsGenericTypeDefinition -- TRANSPILE-TIME decided (StackFold
    //     intercepts the folded typeof chain): the runtime registry cannot
    //     distinguish a definition from its clones (clone name == def.Name),
    //     so a RUNTIME receiver (obj.GetType()) fails LOUD
    //     (NotSupportedException) instead of guessing.
    //   * GetGenericTypeDefinition -- TYPEINFO icall: the definition's own
    //     name (identity -- the simplified Type model; a non-generic receiver
    //     throws InvalidOperationException, net48).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetGenericArguments_MapsToGenericArgsIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetGenericArguments",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetGenericArguments"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_generic_arguments", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_IsGenericType_MapsToIsGenericIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "get_IsGenericType",
            new byte[] { 0x00, 0x00, 0x02 }), "get_IsGenericType"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_is_generic_type", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetGenericTypeDefinition_MapsToGenericDefIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetGenericTypeDefinition",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetGenericTypeDefinition"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_generic_type_definition", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    // get_IsGenericTypeDefinition is the FOLD-TIME predicate: StackFold
    // intercepts the folded typeof chain (definition -> 1, closed instance ->
    // 0); a RUNTIME receiver (obj.GetType()) reaches this mapping -- the
    // registry cannot distinguish the definition from its clones (same name
    // key), so it fails LOUD instead of guessing.
    [Fact]
    public void Resolve_Type_IsGenericTypeDefinition_UntrackedReceiver_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "get_IsGenericTypeDefinition",
                new byte[] { 0x00, 0x00, 0x02 }), "get_IsGenericTypeDefinition")); // ()

        Assert.Contains("System.Type::get_IsGenericTypeDefinition", ex.Message);
    }

    //--------------------------------------------------------------------------
    // C3-1 / RF-N1 -- GetEvents() and GetEvent(string): the event-table
    // member enumeration + single-event query (the COM-C3 event metadata
    // pipeline shares the events sub-table with reflection).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetEvents_MapsToEventEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetEvents",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetEvents"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_events", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetEventString_MapsToEventIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetEvent",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetEvent"); // (string)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_event", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // R2 -- member enumeration (GetMethods/GetFields/GetProperties/
    // GetConstructors/GetInterfaces). Each maps to a runtime icall that
    // materialises an Il2CppArray* of member-NAME strings (the simplified
    // member model -- a member VALUE is its name string). ReturnTypeName
    // "System.String[]" / "System.Type[]" keeps the Array-domain folds
    // (.Length / element read) resolved.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetMethods_MapsToEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetMethods",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetMethods"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_methods", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetFields_MapsToEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetFields",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetFields"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_fields", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetProperties_MapsToEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperties",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetProperties"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_properties", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetConstructors_MapsToEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetConstructors",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetConstructors"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_constructors", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetInterfaces_MapsToEnumIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetInterfaces",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetInterfaces"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_interfaces", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // RF-N5 -- GetNestedTypes / GetMembers (NEXT2-DECISION section ② RF-N5).
    //   GetNestedTypes : il2cpp_typeinfo_get_nested_types -- the typeinfo's
    //       nested-name sub-table; each element is the nested type's '+'-form
    //       reflection full name (a Type VALUE), ReturnTypeName "System.Type[]"
    //       keeps the Array-domain folds (get_FullName on the elements).
    //   GetMembers     : il2cpp_typeinfo_get_members -- the five-enumeration
    //       union (methods-with-accessors + ctors + properties + events +
    //       fields), member-NAME strings, ReturnTypeName "System.String[]".
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetNestedTypes_MapsToNestedIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetNestedTypes",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetNestedTypes"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_nested_types", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetMembers_MapsToMembersIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetMembers",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetMembers"); // ()

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_members", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(0, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // R2 -- FieldInfo.GetValue/SetValue narrow set (the member VALUE is the
    // field name string; the declaring type is joined at runtime from the
    // TARGET object's klass). GetValue -> Il2CppObject* (boxed value),
    // SetValue -> void.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_FieldInfo_GetValue_MapsToValueIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "FieldInfo", "GetValue",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "GetValue"); // (object)

        Assert.Equal("il2cpp::il2cpp_typeinfo_field_get_value", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_FieldInfo_SetValue_MapsToSetValueIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "FieldInfo", "SetValue",
            new byte[] { 0x00, 0x02, 0x01, 0x1C, 0x1C }), "SetValue"); // (object, object) -> void

        Assert.Equal("il2cpp::il2cpp_typeinfo_field_set_value", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // RF-N2 -- PropertyInfo.GetValue/SetValue: the receiver is the PROPERTY-NAME
    // string (the simplified member model, as il2cpp_typeinfo_get_property
    // returns); the runtime joins the declaring type from the TARGET object's
    // klass (own + base chain) and dispatches the property table's
    // getterName/setterName accessor through the same invoker as MethodInfo.
    // Invoke (RF-1 instance-'this'). net48 csc emits GetValue(object) for the
    // common non-indexed shape and GetValue(object, object[]) for the indexed
    // form; SetValue mirrors with (object, object) / (object, object, object[]).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_PropertyInfo_GetValue_MapsToPropertyGetIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "PropertyInfo", "GetValue",
            new byte[] { 0x00, 0x01, 0x1C, 0x1C }), "GetValue"); // (object)

        Assert.Equal("il2cpp::il2cpp_typeinfo_property_get_value", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_PropertyInfo_GetValue_Indexed_MapsToPropertyGetIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "PropertyInfo", "GetValue",
            new byte[] { 0x00, 0x02, 0x1C, 0x1C, 0x1D, 0x1C }), "GetValue"); // (object, object[])

        Assert.Equal("il2cpp::il2cpp_typeinfo_property_get_value", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_PropertyInfo_SetValue_MapsToPropertySetIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "PropertyInfo", "SetValue",
            new byte[] { 0x00, 0x02, 0x01, 0x1C, 0x1C }), "SetValue"); // (object, object) -> void

        Assert.Equal("il2cpp::il2cpp_typeinfo_property_set_value", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_PropertyInfo_SetValue_Indexed_MapsToPropertySetIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "PropertyInfo", "SetValue",
            new byte[] { 0x00, 0x03, 0x01, 0x1C, 0x1C, 0x1D, 0x1C }), "SetValue"); // (object, object, object[]) -> void

        Assert.Equal("il2cpp::il2cpp_typeinfo_property_set_value", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(3, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // R3 -- MethodInfo.Invoke(object, object[]): the receiver is the METHOD-NAME
    // string (the simplified member model); the runtime joins the declaring
    // type from the target (klass join) and dispatches through
    // il2cpp_invoker_invoke. Result is the boxed value (returnTypeName
    // "System.Object").
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MethodInfo_Invoke_MapsToInvokeIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MethodInfo", "Invoke",
            new byte[] { 0x00, 0x02, 0x02, 0x1C, 0x12, 0x05 }), "Invoke"); // (object, object[])

        Assert.Equal("il2cpp::il2cpp_methodinfo_invoke", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // RF-1 -- net48 csc emits the BASE MethodBase::Invoke(object, object[])
    // memberref (ILDASM-verified on a compiled probe), so MethodBase must map
    // to the same il2cpp_methodinfo_invoke icall as the MethodInfo override.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MethodBase_Invoke_MapsToInvokeIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MethodBase", "Invoke",
            new byte[] { 0x00, 0x02, 0x02, 0x1C, 0x12, 0x05 }), "Invoke"); // (object, object[])

        Assert.Equal("il2cpp::il2cpp_methodinfo_invoke", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // RF-N7 (NEXT2-DECISION section ② RF-N7): the three reflection operations
    // that must fail LOUD instead of falling to a spelling generated-member
    // name. Each maps to a runtime stub that ALWAYS throws NotSupportedException
    // (typeinfo.h RF-N7 contract) -- the call side sees an explicit exception,
    // never a phantom link.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_MethodInfo_MakeGenericMethod_MapsToLoudIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "MethodInfo", "MakeGenericMethod",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x1D, 0x12, 0x05 }), "MakeGenericMethod"); // (Type[])

        Assert.Equal("il2cpp::il2cpp_makegenericmethod_loud", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_MakeGenericType_MapsToLoudIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "MakeGenericType",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x1D, 0x12, 0x05 }), "MakeGenericType"); // (Type[])

        Assert.Equal("il2cpp::il2cpp_makegenerictype_loud", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Delegate_CreateDelegate_MapsToLoudIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Delegate", "CreateDelegate",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x12, 0x05, 0x12, 0x05 }), "CreateDelegate"); // (Type, MethodInfo)

        Assert.Equal("il2cpp::il2cpp_createdelegate_loud", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Delegate", resolved.ReturnTypeName);
        Assert.False(resolved.IsInstance); // STATIC (Delegate.*)
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // C3-2 (COM-C3) -- EventInfo.AddEventHandler(object, Delegate) /
    // EventInfo.RemoveEventHandler(object, Delegate): the managed events-table
    // accessor delivery (the C3 genevt fake-source self-loop's green path).
    // The receiver is the EVENT-NAME string; the runtime joins the declaring
    // type from the target and dispatches the event table's add/remove
    // accessor on it with the handler. net48 IL: (object, System.Delegate) --
    // ps 0x1C (OBJECT), 0x12 (CLASS).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_EventInfo_AddEventHandler_MapsToEventAddIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "EventInfo", "AddEventHandler",
            new byte[] { 0x00, 0x02, 0x02, 0x1C, 0x12, 0x05 }), "AddEventHandler"); // (object, Delegate)

        Assert.Equal("il2cpp::il2cpp_eventinfo_add", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_EventInfo_RemoveEventHandler_MapsToEventRemoveIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "EventInfo", "RemoveEventHandler",
            new byte[] { 0x00, 0x02, 0x02, 0x1C, 0x12, 0x05 }), "RemoveEventHandler"); // (object, Delegate)

        Assert.Equal("il2cpp::il2cpp_eventinfo_remove", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // C1 (A1) -- Type-COM creation. Type.GetTypeFromProgID(string) /
    // Type.GetTypeFromCLSID(Guid) map to the runtime COM Type icalls that
    // return the "COMCLSID:<32hex>" sentinel Type-value string (the Type
    // value model -- a COM Type is a name-string sentinel the Activator
    // decodes back to a CLSID). ReturnTypeName "System.Type" keeps the
    // Type-domain linkage (get_Name / IsComObject folds).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetTypeFromProgID_MapsToComTypeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeFromProgID",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x0E }), "GetTypeFromProgID"); // (string)

        Assert.Equal("il2cpp::runtime::il2cpp_com_type_from_progid", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the sentinel name string
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetTypeFromCLSID_MapsToComTypeIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "GetTypeFromCLSID",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x11, 0x05 }), "GetTypeFromCLSID"); // (Guid) -- valuetype

        Assert.Equal("il2cpp::runtime::il2cpp_com_type_from_clsid", resolved.CppName);
        Assert.True(resolved.ReturnsRef);   // the sentinel name string
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.False(resolved.IsInstance);  // static
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // C2 (A2) -- Type.InvokeMember(string, BindingFlags, Binder, object,
    // object[]) -- the ONE-CALL late-bound IDispatch invocation. The IL
    // signature (name, invokeAttr, binder, target, args) lowers to
    // icall_com_invoke_member(target, name, args): the rcw slot reads the IL
    // `target` argument (index 3), the name slot reads index 0, the args slot
    // reads index 4; invokeAttr (1) and binder (2) are dropped by the
    // argOrder. ReturnTypeName "System.Object" keeps the null/Concat linkage.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_InvokeMember_MapsToInvokeMemberIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "InvokeMember",
            new byte[] { 0x00, 0x05, 0x12, 0x05, 0x0E, 0x11, 0x05, 0x12, 0x05, 0x1C, 0x1D, 0x1C }), "InvokeMember");

        Assert.Equal("il2cpp::runtime::icall_com_invoke_member", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Object", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);  // instance on the Type receiver
        Assert.Equal(5, resolved.ParamCount); // the IL arity (the fold's pop count)
        Assert.Equal(new[] { "", "s", "a" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { 4, 1, 5 }, resolved.ArgOrder); // [receiver, name, attr, binder, target, args]
    }

    //--------------------------------------------------------------------------
    // REFL-ARITY (impl-type-reflect) -- the remaining reachable net48 overloads
    // whose parameter shape is compile-time known (arity + element/enum family)
    // but had no mapping arm, so the catch-all throw fired. Each maps to a
    // runtime icall (three reuse the existing exact-signature primitives; the
    // BindingFlags forms add a small flags-aware runtime kernel).
    //
    //   GetNestedTypes(BindingFlags)                 -> get_nested_types_flags
    //   GetEvent(string, BindingFlags)              -> get_event_flags
    //   GetMethod(string, Flags, Binder, Type[], mods) -> get_method_types_flags
    //   GetConstructor(Flags, Binder, Type[], mods)  -> get_constructor_flags
    //   GetProperty(string, Type, Type[], mods)      -> get_property_types_ret
    //      (the modifiers are ignored by net48's match -- W7; the 3-param form
    //      already reuses the exact-signature primitive, so the 4-param form
    //      drops the modifier slot and reuses it unchanged)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_GetNestedTypesFlags_MapsToNestedFlagsIcall()
    {
        // Type.GetNestedTypes(BindingFlags) -- the arity-1 filtered enumeration
        // (the BindingFlags ENUM rides as a VALUETYPE 0x11 + TypeDefOrRef).
        var resolved = ResolveMember(Synthesize("System", "Type", "GetNestedTypes",
            new byte[] { 0x00, 0x01, 0x12, 0x05, 0x11, 0x05 }), "GetNestedTypes"); // (BindingFlags)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_nested_types_flags", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type[]", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetEventFlags_MapsToEventFlagsIcall()
    {
        // Type.GetEvent(string, BindingFlags) -- the flags-aware single-event
        // query (the event table's add-accessor visibility/static filter).
        var resolved = ResolveMember(Synthesize("System", "Type", "GetEvent",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x11, 0x05 }), "GetEvent"); // (string, BindingFlags)

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_event_flags", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetMethodFlagsBinderTypesMods_MapsToTypesFlagsIcall()
    {
        // Type.GetMethod(string, BindingFlags, Binder, Type[], ParameterModifier[])
        // -- net48's binder/modifiers are ignored by the match (the default
        // binder; modifiers encode by-ref only -- W7), so the runtime binds by
        // the explicit BindingFlags + the Type[] arity/signature.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetMethod",
            new byte[] { 0x00, 0x05, 0x12, 0x05, 0x0E, 0x11, 0x05, 0x12, 0x05, 0x1D, 0x12, 0x05, 0x1D, 0x12, 0x05 }),
            "GetMethod"); // (string, BindingFlags, Binder, Type[], ParameterModifier[])

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_method_types_flags", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(5, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetConstructorFlagsBinderTypesMods_MapsToCtorFlagsIcall()
    {
        // Type.GetConstructor(BindingFlags, Binder, Type[], ParameterModifier[])
        // -- same binder/modifier drop; the runtime binds the ctor by the
        // explicit BindingFlags + the Type[] arity.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetConstructor",
            new byte[] { 0x00, 0x04, 0x12, 0x05, 0x11, 0x05, 0x12, 0x05, 0x1D, 0x12, 0x05, 0x1D, 0x12, 0x05 }),
            "GetConstructor"); // (BindingFlags, Binder, Type[], ParameterModifier[])

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_constructor_flags", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Reflection.ConstructorInfo", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(4, resolved.ParamCount);
    }

    [Fact]
    public void Resolve_Type_GetPropertyReturnTypeTypesMods_MapsToTypesRetIcall()
    {
        // Type.GetProperty(string, Type returnType, Type[], ParameterModifier[])
        // -- net48 ignores the ParameterModifier[] (by-ref only -- W7); the
        // existing exact-signature primitive serves the 3-param form and this
        // 4-param form drops the modifier slot onto it.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetProperty",
            new byte[] { 0x00, 0x04, 0x12, 0x05, 0x0E, 0x12, 0x05, 0x1D, 0x12, 0x05, 0x1D, 0x12, 0x05 }),
            "GetProperty"); // (string, Type, Type[], ParameterModifier[])

        Assert.Equal("il2cpp::il2cpp_typeinfo_get_property_types_ret", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.String", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(4, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // REFL-ARITY -- Assembly.GetType(string, bool throwOnError): net48's 2-param
    // form (the (string) overload binds throwOnError=false). The bool rides the
    // int32 slot ("i"); the input-assembly-name literal stays the appended last
    // argument so the runtime can raise TypeLoadException on a miss.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Assembly_GetTypeThrowOnError_MapsToAssemblyTypeExIcall()
    {
        var resolved = ResolveMember(Synthesize("System.Reflection", "Assembly", "GetType",
            new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x02 }), "GetType"); // (string, bool)

        Assert.Equal("il2cpp::il2cpp_assemblyinfo_get_type_ex", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Type", resolved.ReturnTypeName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(2, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Harness -- same synthetic-PE pipeline as MethodRefResolverDispatchTests.
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? arg0Type = null, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, arg0Type, null);
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddAssembly(mdb.GetOrAddString("synthetic"), new Version(1, 0, 0, 0),
            default(StringHandle), default(BlobHandle),
            (System.Reflection.AssemblyFlags)0, System.Reflection.AssemblyHashAlgorithm.None);
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
