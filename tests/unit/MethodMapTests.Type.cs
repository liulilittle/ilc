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

        Assert.Equal("il2cpp::runtime::il2cpp_type_get_type_from_handle", resolved.CppName);
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
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_IsAssignableFrom_MapsToAssignableIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "IsAssignableFrom",
            new byte[] { 0x00, 0x01, 0x08, 0x12, 0x05, 0x12, 0x05 }), "IsAssignableFrom"); // (Type)

        Assert.Equal("il2cpp::runtime::il2cpp_type_is_assignable_from", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
    }

    //--------------------------------------------------------------------------
    // IsInstanceOfType -- instance: whether the OBJECT argument is an instance
    // of this type. Mirror of the IsAssignableFrom descriptor channel: the
    // runtime primitive icall_type_is_instance(obj, klass) takes the object
    // FIRST and the class descriptor SECOND (typecheck.cpp), while IL pushes
    // the receiver (this Type) before the object -- the resolution carries the
    // ArgOrder {1,0} swap so the emitted call passes (obj, this) in runtime
    // slot order.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_IsInstanceOfType_MapsToInstanceIcall()
    {
        var resolved = ResolveMember(Synthesize("System", "Type", "IsInstanceOfType",
            new byte[] { 0x00, 0x01, 0x02, 0x1C }), "IsInstanceOfType"); // (object)

        Assert.Equal("il2cpp::runtime::icall_type_is_instance", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.True(resolved.IsInstance);
        Assert.Equal(1, resolved.ParamCount);
        // Descriptor channel: the object arg fills the runtime (obj) slot, the
        // receiver fills the (klass) slot -- the opposite of IL push order.
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
    public void Resolve_Type_Equals_UntrackedArgument_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "Equals",
                new byte[] { 0x00, 0x01, 0x02, 0x1C }), "Equals")); // no tracked arg type

        Assert.Contains("System.Type::Equals", ex.Message);
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
    public void Resolve_Type_GetPropertyTwoParams_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "Type", "GetProperty",
                new byte[] { 0x00, 0x02, 0x12, 0x05, 0x0E, 0x08 }), "GetProperty")); // (string, BindingFlags)

        Assert.Contains("System.Type::GetProperty", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Dispatch miss -- an unwired Type member falls back to the generated-
    // name path (owner_sanitized_name_<hash>), proving the domain returns
    // null instead of misrouting.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Type_UnmappedMember_FallsBackToGeneratedName()
    {
        // GetGenericTypeDefinition is NOT part of the R2 member-enumeration set
        // (methods/fields/properties/constructors/interfaces/events), so it
        // still falls back to the generated-name path.
        var resolved = ResolveMember(Synthesize("System", "Type", "GetGenericTypeDefinition",
            new byte[] { 0x00, 0x00, 0x12, 0x05 }), "GetGenericTypeDefinition"); // ()

        Assert.StartsWith("System_Type_GetGenericTypeDefinition_", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(0, resolved.ParamCount);
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
