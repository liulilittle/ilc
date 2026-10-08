using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterTypeInfoTests -- M5.5 TypeInfo reflection table emission
// (R-REFL-01: the runtime typeinfo registry stayed EMPTY because the
// transpiler never emitted Il2CppTypeInfo tables).
//
// Contract (reflection-runtime-design.md §2 + typeinfo.h):
//   * One static const il2cpp::Il2CppTypeInfo {T}_typeinfo per REFLECTED
//     type, emitted by TypeEmitter (same pass as EmitClassDescriptor).
//   * Sub-tables: fields (offset/isRef/staticAddr), methods (signature/
//     flags/virtualSlot/declaringType/fnPtr), interfaces (TypeInfo ptr
//     array), properties (refl3/m7d: the PropertyDef model lands the
//     sub-table -- accessor names resolve through the same methods table).
//   * Kept scope: mode B manifest names decide which types get a table
//     (FilterReflectionManifest result); non-Kept types emit nothing.
//   * Registration: il2cpp::il2cpp_typeinfo_register(&{T}_typeinfo) via
//     EmitTypeInfoRegistration (wired by CodeGenerator's entry.cpp pass /
//     ensure_cctor hook -- the hook recipe is reported, not edited here).
//------------------------------------------------------------------------------
public class TypeEmitterTypeInfoTests
{
    // Fixture: Calc.Node { int X; Node Next; } : Calc.Base, Calc.Iface
    // with static int Count; methods: int Add(int,int) static public,
    // virtual int VirtualAdd(int), abstract int AbsAdd(int) (from Iface),
    // .ctor.
    private static TypeDef MakeNode()
    {
        var baseT = new TypeDef { Name = "Base", Namespace = "Calc" };
        var iface = new TypeDef { Name = "Iface", Namespace = "Calc", IsInterface = true };
        var t = new TypeDef { Name = "Node", Namespace = "Calc", BaseTypeName = "Calc.Base" };
        t.BaseType = baseT;
        t.InterfaceTypes.Add(iface);
        t.InstanceFields.Add(new FieldDef { Name = "X", Owner = t, TypeKind = FieldTypeKind.Int32 });
        t.InstanceFields.Add(new FieldDef { Name = "Next", Owner = t, TypeKind = FieldTypeKind.Reference, FieldTypeName = "Calc.Node" });
        t.StaticFields.Add(new FieldDef { Name = "Count", Owner = t, IsStatic = true });
        t.Methods.Add(new MethodDef
        {
            Name = ".ctor", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.Void,
        });
        t.Methods.Add(new MethodDef
        {
            Name = "Add", Owner = t, IsPublic = true, IsStatic = true,
            ParamCount = 2, ParamTypes = "i,i", ReturnType = SignatureTypeCode.Int32,
        });
        t.Methods.Add(new MethodDef
        {
            Name = "VirtualAdd", Owner = t, IsPublic = true, IsVirtual = true, VTableSlot = 1,
            ParamCount = 1, ParamTypes = "i", ReturnType = SignatureTypeCode.Int32,
        });
        t.Methods.Add(new MethodDef
        {
            Name = "AbsAdd", Owner = t, IsPublic = true, IsAbstract = true,
            ParamCount = 1, ParamTypes = "i", ReturnType = SignatureTypeCode.Int32,
        });
        return t;
    }

    [Fact]
    public void EmitTypeInfo_NotInKept_ReturnsEmpty()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t, new[] { "Calc.Other" });
        Assert.Equal("", code);
    }

    [Fact]
    public void EmitTypeInfo_KeptNull_EmitsTableWithNameAndKlass()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("il2cpp::Il2CppTypeInfo Calc_Node_typeinfo = {", code);
        Assert.Contains("\"Calc.Node\",", code);
        Assert.Contains("&Calc_Node_class,", code);
    }

    [Fact]
    public void EmitTypeInfo_MainTable_ExternalLinkage()
    {
        // The registration hook (EmitTypeInfoRegistration) takes
        // &{T}_typeinfo from entry.cpp -- a DIFFERENT translation unit. A
        // `static const` table is internal-linkage: the cross-TU reference
        // would fail to compile/link. The main table must be a NON-CONST
        // namespace-scope global (external linkage, always emitted, -Wall
        // silent) mirroring Il2CppClass {T}_class; sub-tables stay static
        // (same-TU references). Probe-verified: `inline`/`static const`
        // variants fail to link because the defining .cpp never odr-uses the
        // table.
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("il2cpp::Il2CppTypeInfo Calc_Node_typeinfo = {", code);
        Assert.DoesNotContain("static il2cpp::Il2CppTypeInfo Calc_Node_typeinfo = {", code);
        Assert.DoesNotContain("inline", code);
        // Sub-tables keep internal linkage (referenced only by the main table
        // in the same .cpp).
        Assert.Contains("static const il2cpp::Il2CppMethodInfo Calc_Node_typeinfo_methods[] = {", code);
        Assert.Contains("static const il2cpp::Il2CppFieldInfo Calc_Node_typeinfo_fields[] = {", code);
    }

    [Fact]
    public void EmitTypeInfo_PropertyTable_EmitsAccessorNames()
    {
        // refl3 (m7d, d1_prop): the PropertyDef model lands the property
        // sub-table. A property with get+set emits an Il2CppPropertyInfo row
        // carrying the accessor METHOD NAMES (the runtime resolves them by
        // name through the SAME methods table -- no duplicated fnPtr); a
        // missing accessor emits nullptr.
        var t = new TypeDef { Name = "ReflProp", Namespace = "Cases" };
        t.Methods.Add(new MethodDef
        {
            Name = ".ctor", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.Void,
        });
        t.Properties.Add(new PropertyDef
        {
            Name = "PropA", Owner = t, TypeName = "System.Int32",
            GetterName = "get_PropA", SetterName = "set_PropA", IndexParamCount = 0,
        });
        t.Properties.Add(new PropertyDef
        {
            Name = "ReadOnly", Owner = t, TypeName = "System.String",
            GetterName = "get_ReadOnly", SetterName = null, IndexParamCount = 0,
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("static const il2cpp::Il2CppPropertyInfo Cases_ReflProp_typeinfo_properties[] = {", code);
        Assert.Contains("{ \"PropA\", \"System.Int32\", \"get_PropA\", \"set_PropA\", 0 },", code);
        Assert.Contains("{ \"ReadOnly\", \"System.String\", \"get_ReadOnly\", nullptr, 0 },", code);
        Assert.Contains("Cases_ReflProp_typeinfo_properties, 2,", code);
    }

    [Fact]
    public void EmitTypeInfo_StaticClass_KlassNullPtr()
    {
        // A C# static class (no instance fields, no .ctor, no virtuals) has
        // NO {T}_class descriptor emitted by CodeGenerator -- the typeinfo
        // klass field must be nullptr, not &{T}_class (undeclared identifier
        // in generated C++ -- the reflectcase static Main type hit this).
        var t = new TypeDef { Name = "Runner", Namespace = "Cases" };
        t.Methods.Add(new MethodDef
        {
            Name = "Main", Owner = t, IsPublic = true, IsStatic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.Void,
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        var norm = code.Replace("\r", "").Split('\n');
        Assert.Contains("il2cpp::Il2CppTypeInfo Cases_Runner_typeinfo = {", code);
        Assert.Contains("\"Cases.Runner\",", code);
        // klass is the field immediately after the name; a static class has
        // no descriptor, so it must be nullptr (never &{T}_class).
        var nameIdx = Array.FindIndex(norm, l => l.Trim().StartsWith("\"Cases.Runner\","));
        Assert.True(nameIdx >= 0 && nameIdx + 1 < norm.Length);
        Assert.Contains("nullptr,", norm[nameIdx + 1]);
        Assert.DoesNotContain("&Cases_Runner_class", code);
    }

    [Fact]
    public void EmitTypeInfo_BaseType_UserBaseWired()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("&Calc_Base_typeinfo,", code);
    }

    [Fact]
    public void EmitTypeInfo_BaseTypeObject_NullPtr()
    {
        var t = new TypeDef { Name = "Root", Namespace = "Calc" };
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("nullptr, // baseType (System.Object)", code);
    }

    // NB-1: a USER class derived directly from System.Object (root class) must
    // carry baseTypeName "System.Object" (net48: root class BaseType=Object),
    // while an interface (also BaseTypeName "System.Object" via the nil-extends
    // decode) must keep baseTypeName nullptr (net48: interface BaseType=null).
    [Fact]
    public void EmitTypeInfo_BaseTypeName_RootClassEmitsObjectString()
    {
        var t = new TypeDef { Name = "Root", Namespace = "Calc" };
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("\"System.Object\",  // baseTypeName (BCL base terminal)", code);
    }

    [Fact]
    public void EmitTypeInfo_BaseTypeName_InterfaceNullPtr()
    {
        var t = new TypeDef { Name = "Iface", Namespace = "Calc", IsInterface = true };
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("nullptr,  // baseTypeName (BCL base terminal)", code);
    }

    [Fact]
    public void EmitTypeInfo_Interfaces_ArrayEmittedWithCount()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("Calc_Node_typeinfo_interfaces[] = {", code);
        Assert.Contains("&Calc_Iface_typeinfo,", code);
        Assert.Contains("1,", code); // interfaceCount
    }

    [Fact]
    public void EmitTypeInfo_FieldTable_InstanceOffsetAndRefFlag()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        // X: slot 1 -> offset 8, value field, no static addr
        Assert.Contains("{ \"X\", \"System.Int32\", 8, 0, nullptr, 0 },", code);
        // Next: slot 2 -> offset 16, GC reference flag 1
        Assert.Contains("{ \"Next\", \"Calc.Node\", 16, 1, nullptr, 0 },", code);
    }

    [Fact]
    public void EmitTypeInfo_InstanceValueTypeField_CarriesRealTypeName()
    {
        // P2 refl audit (TypeEmitter.cs:1258): a non-reference INSTANCE field
        // whose model carries a real value-type name (the 0x11 VALUETYPE
        // decode -- DecodeNestedFieldSignature returns
        // (FieldTypeKind.Int32, "<value type full name>")) must emit that name
        // in the TypeInfo field table, NOT the hardcoded "System.Int32"
        // (FieldInfo.FieldType would report the wrong type).
        var t = MakeNode();
        t.InstanceFields.Add(new FieldDef
        {
            Name = "Pt", Owner = t, TypeKind = FieldTypeKind.Int32,
            FieldTypeName = "Cases.Point", // value-type field (M3 VALUETYPE carrier)
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"Pt\", \"Cases.Point\", 24, 0, nullptr, 0 },", code);
        Assert.DoesNotContain("{ \"Pt\", \"System.Int32\",", code);
    }

    [Fact]
    public void EmitTypeInfo_StaticField_OffsetMinusOneWithStaticAddr()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"Count\", \"System.Int32\", -1, 0, &Calc_Node_sf_Count, 2 },", code);
    }

    [Fact]
    public void EmitTypeInfo_MethodTable_SignatureFlagsSlotFnPtr()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        var hash = SigHash.Hash("Calc.Node.Add(i,i)");
        Assert.Contains("{ \"Add\", \"i(i,i)\", 2, 0x3, -1, \"Calc.Node\", reinterpret_cast<void*>(&Calc_Node_Add_" + hash + ") },", code);
    }

    [Fact]
    public void EmitTypeInfo_VirtualMethod_SlotAndVirtualFlag()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        var hash = SigHash.Hash("Calc.Node.VirtualAdd(i)");
        // flags 0x9 = public(1) | virtual(8); virtualSlot 1
        Assert.Contains("{ \"VirtualAdd\", \"i(i)\", 1, 0x9, 1, \"Calc.Node\", reinterpret_cast<void*>(&Calc_Node_VirtualAdd_" + hash + ") },", code);
    }

    [Fact]
    public void EmitTypeInfo_AbstractMethod_FnPtrNull()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"AbsAdd\", \"i(i)\", 1, 0x1, -1, \"Calc.Node\", nullptr },", code);
    }

    [Fact]
    public void EmitTypeInfo_Ctor_CtorFlagAndVoidSignature()
    {
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t);
        var hash = SigHash.Hash("Calc.Node..ctor()");
        Assert.Contains("{ \".ctor\", \"v()\", 0, 0x5, -1, \"Calc.Node\", reinterpret_cast<void*>(&Calc_Node__ctor_" + hash + ") },", code);
    }

    [Fact]
    public void EmitTypeInfo_RefReturnType_ThrowsForUnsupportedType()
    {
        var t = MakeNode();
        t.Methods.Add(new MethodDef
        {
            Name = "GetNext", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.TypeHandle,
        });
        // TypeHandle return type is not representable in Il2CppMethodInfo.signature
        // (model gap, mirror of the M1 static-field classification gap).
        var ex = Assert.Throws<NotSupportedException>(() => TypeEmitter.EmitTypeInfo(t));
        Assert.Contains("TypeHandle", ex.Message);
    }

    //--------------------------------------------------------------------------
    // refl2 S1/D1: MethodDef.ReturnTypeName makes reference/value/generic-inst
    // returns nameful in the typeinfo method-table signature (the G2 guard's
    // removal condition -- no more LOUD for 0x11/0x12/0x15).
    //--------------------------------------------------------------------------

    [Fact]
    public void EmitTypeInfo_ClassReturn_NamefulSignature()
    {
        // 0x12 CLASS return with the decoded name -> "r:{name}" (no LOUD).
        var t = MakeNode();
        t.Methods.Add(new MethodDef
        {
            Name = "GetChild", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = (SignatureTypeCode)0x12,
            ReturnTypeName = "r:Calc.Node",
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"GetChild\", \"r:Calc.Node()\", 0, 0x1, -1, \"Calc.Node\",", code);
    }

    [Fact]
    public void EmitTypeInfo_ValueTypeReturn_NamefulSignature()
    {
        // 0x11 VALUETYPE return (System.Decimal) -> "V:System.Decimal".
        var t = MakeNode();
        t.Methods.Add(new MethodDef
        {
            Name = "GetRate", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = (SignatureTypeCode)0x11,
            ReturnTypeName = "V:System.Decimal",
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"GetRate\", \"V:System.Decimal()\", 0, 0x1, -1, \"Calc.Node\",", code);
    }

    [Fact]
    public void EmitTypeInfo_GenericInstReturn_NamefulSignature()
    {
        // 0x15 GENERICINST return (Func<int,int>) -> "r:System.Func`2<i,i>".
        var t = MakeNode();
        t.Methods.Add(new MethodDef
        {
            Name = "MakeAdder", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.GenericTypeInstance,
            ReturnTypeName = "r:System.Func`2<i,i>",
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"MakeAdder\", \"r:System.Func`2<i,i>()\", 0, 0x1, -1, \"Calc.Node\",", code);
    }

    //--------------------------------------------------------------------------
    // refl2 S2/G1: folded-accessor methods get fnPtr=nullptr (their function
    // symbol is never emitted -- the whole-type exclusion is removed).
    //--------------------------------------------------------------------------

    [Fact]
    public void EmitTypeInfo_FoldedAccessor_FnPtrNull()
    {
        var t = MakeNode();
        t.Methods.Add(new MethodDef
        {
            Name = "get_Value", Owner = t, IsPublic = true,
            ParamCount = 0, ParamTypes = "", ReturnType = SignatureTypeCode.Int32,
        });
        var code = TypeEmitter.EmitTypeInfo(t, foldedAccessorNames: new HashSet<string> { "get_Value" });
        Assert.Contains("{ \"get_Value\", \"i()\", 0, 0x1, -1, \"Calc.Node\", nullptr },", code);
        Assert.DoesNotContain("Calc_Node_get_Value_", code);
    }

    [Fact]
    public void EmitTypeInfo_NonFoldedMethod_StillHasFnPtr()
    {
        // A method NOT in the folded set keeps its real symbol pointer.
        var t = MakeNode();
        var code = TypeEmitter.EmitTypeInfo(t, foldedAccessorNames: new HashSet<string> { "get_Value" });
        var hash = SigHash.Hash("Calc.Node.Add(i,i)");
        Assert.Contains("&Calc_Node_Add_" + hash + ")", code);
    }

    //--------------------------------------------------------------------------
    // fixdel3 (b23): a custom delegate TypeDef's synthesized methods
    // (.ctor/Invoke/BeginInvoke/EndInvoke -- Roslyn "runtime managed"
    // MethodDefs, RVA=0) are NEVER emitted as C++ function symbols (the
    // CodeGenerator folds delegate construction/Invoke through the runtime
    // delegate machinery). The M7F TypeInfo table must not reference them --
    // fnPtr=nullptr per the typeinfo.h "null = runtime-unreachable (trimmed)"
    // contract, otherwise the generated Cb.cpp fails to compile (undeclared
    // identifier Cb_Invoke_...).
    //--------------------------------------------------------------------------
    private static TypeDef MakeDelegate()
    {
        var del = new TypeDef { Name = "Cb", Namespace = "Cases", BaseTypeName = "System.MulticastDelegate" };
        del.Methods.Add(new MethodDef
        {
            Name = ".ctor", Owner = del, IsPublic = true,
            ParamCount = 2, ParamTypes = "r:System.Object,n", ReturnType = SignatureTypeCode.Void,
        });
        del.Methods.Add(new MethodDef
        {
            Name = "Invoke", Owner = del, IsPublic = true, IsVirtual = true, VTableSlot = 2,
            ParamCount = 1, ParamTypes = "i", ReturnType = SignatureTypeCode.Int32,
        });
        del.Methods.Add(new MethodDef
        {
            Name = "BeginInvoke", Owner = del, IsPublic = true, IsVirtual = true, VTableSlot = 0,
            ParamCount = 3, ParamTypes = "i,r:System.AsyncCallback,r:System.Object",
            ReturnType = (SignatureTypeCode)0x12, ReturnTypeName = "r:System.IAsyncResult",
        });
        del.Methods.Add(new MethodDef
        {
            Name = "EndInvoke", Owner = del, IsPublic = true, IsVirtual = true, VTableSlot = 1,
            ParamCount = 1, ParamTypes = "r:System.IAsyncResult", ReturnType = SignatureTypeCode.Int32,
        });
        return del;
    }

    [Fact]
    public void EmitTypeInfo_DelegateType_AllMethodsFnPtrNull()
    {
        // Delegate-synthesized methods have no emitted C++ symbol -> every
        // typeinfo method entry must carry nullptr (not a dangling &Cb_*_ref).
        var t = MakeDelegate();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \".ctor\", \"v(r:System.Object,n)\", 2, 0x5, -1, \"Cases.Cb\", nullptr },", code);
        Assert.Contains("{ \"Invoke\", \"i(i)\", 1, 0x9, 2, \"Cases.Cb\", nullptr },", code);
        Assert.Contains("{ \"BeginInvoke\", \"r:System.IAsyncResult(i,r:System.AsyncCallback,r:System.Object)\", 3, 0x9, 0, \"Cases.Cb\", nullptr },", code);
        Assert.Contains("{ \"EndInvoke\", \"i(r:System.IAsyncResult)\", 1, 0x9, 1, \"Cases.Cb\", nullptr },", code);
        Assert.DoesNotContain("Cases_Cb_Invoke_", code);
        Assert.DoesNotContain("Cases_Cb__ctor_", code);
    }

    [Fact]
    public void EmitTypeInfo_DelegateType_KlassNullPtr()
    {
        // Delegate objects are runtime Il2CppDelegate instances (no descriptor)
        // -> the klass field must be nullptr like HasClassDescriptor rules.
        var t = MakeDelegate();
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("il2cpp::Il2CppTypeInfo Cases_Cb_typeinfo = {", code);
        Assert.Contains("nullptr,", code);
    }

    //--------------------------------------------------------------------------
    // refl2 S2/G3: Sanitize turns '=' into '_' so the compiler-generated
    // array-initializer struct's short name becomes a valid C++ identifier.
    //--------------------------------------------------------------------------

    [Fact]
    public void Sanitize_EqualSign_ProducesValidCppIdentifier()
    {
        var t = new TypeDef { Name = "__StaticArrayInitTypeSize=3", Namespace = "" };
        var cppName = TypeEmitter.TypeCppName(t);
        Assert.Equal("__StaticArrayInitTypeSize_3", cppName);
        Assert.DoesNotContain("=", cppName);
    }

    [Fact]
    public void TypeCppName_DottedInstantiationSuffix_SanitizesToValidCppIdentifier()
    {
        // myIList interim609: a user-generic closed suffix is a dotted full
        // name ("System.Exception") -- the raw append produced
        // `struct Cases_MyList_1_System.Exception` (generated.h:170,
        // "expected unqualified-id before '.' token").
        var t = new TypeDef { Name = "MyList`1", Namespace = "Cases", GenericArity = 1, InstantiationSuffix = "System.Exception" };
        var cppName = TypeEmitter.TypeCppName(t);
        Assert.Equal("Cases_MyList_1_System_Exception", cppName);
    }

    [Fact]
    public void EmitTypeInfoRegistration_EmitsRegisterCall()
    {
        var t = MakeNode();
        Assert.Equal("il2cpp::il2cpp_typeinfo_register(&Calc_Node_typeinfo);", TypeEmitter.EmitTypeInfoRegistration(t));
    }

    //--------------------------------------------------------------------------
    // EmitStaticStorage -- static-field storage type by field semantics
    // (delegate/reference -> Il2CppObject*, decimal -> Il2CppDecimal, value ->
    // int32_t). Root cause of the delechain cmake FAIL: EVERY static field
    // emitted int32_t, so a delegate field's storage slot was handed to
    // icall_delegate_combine/remove (Il2CppObject* parameters) and overwritten
    // by `static_cast<Il2CppObject*>(...)` -- both compile-time type
    // mismatches. The classification is driven by the FieldDef model
    // (TypeKind / FieldTypeName), which the metadata loader populates; a
    // static field WITHOUT model type info keeps the historical int32_t value
    // emission (no regression).
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitStaticStorage_ValueField_EmitsInt32()
    {
        var t = MakeNode(); // Count: static int (default Int32 kind, empty type name)
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("int32_t Calc_Node_sf_Count = 0;", code);
    }

    [Fact]
    public void EmitStaticStorage_ReferenceField_EmitsIl2CppObjectPtr()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Changed", Owner = t, IsStatic = true,
            TypeKind = FieldTypeKind.Reference, FieldTypeName = "System.Func`2<int,int>",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("Il2CppObject* Calc_Node_sf_Changed = nullptr; // GC reference (delegate / object)", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Changed", code);
    }

    [Fact]
    public void EmitStaticStorage_DecimalField_EmitsIl2CppDecimal()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Rate", Owner = t, IsStatic = true, FieldTypeName = "System.Decimal",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("il2cpp::runtime::Il2CppDecimal Calc_Node_sf_Rate = { 0, 0, 0 }; // 16-byte decimal value", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Rate", code);
    }

    [Fact]
    public void EmitStaticStorage_LiteralField_Skipped()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef { Name = "Version", Owner = t, IsStatic = true, IsLiteral = true, ConstantValue = "42" });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.DoesNotContain("Calc_Node_sf_Version", code);
    }

    [Fact]
    public void EmitStaticStorage_DateTimeOffsetMinMax_Emits16ByteImage()
    {
        // B4 (dtoprobe): DateTimeOffset.MinValue/MaxValue are BCL static
        // readonly VALUETYPE fields (Roslyn cannot fold the ldsfld) --
        // without storage the read hits "use of undeclared identifier".
        // MinValue = {utcTicks 0, offset 0}; MaxValue = {DateTime.MaxTicks,
        // offset 0} (net48: 9999-12-31 +00:00).
        var t = MakeNode();
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("System_DateTimeOffset_sf_MinValue = { 0, 0 };", code);
        Assert.Contains("System_DateTimeOffset_sf_MaxValue = { 3155378975999999999LL, 0 };", code);
    }

    [Fact]
    public void EmitTypeInfo_StaticReferenceField_TypeNameAndRefFlag()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Changed", Owner = t, IsStatic = true,
            TypeKind = FieldTypeKind.Reference, FieldTypeName = "System.Func`2<int,int>",
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        // typeName = the decoded FieldTypeName (not the "System.Int32"
        // fallback), isRef = 1 (the static slot holds a GC reference).
        Assert.Contains("{ \"Changed\", \"System.Func`2<int,int>\", -1, 1, &Calc_Node_sf_Changed, 2 },", code);
    }

    [Fact]
    public void EmitTypeInfo_StaticDecimalField_TypeNameDecimalNoRefFlag()
    {
        // decimalcase pairing (dec3 §5 Bug2 residual): a static
        // System.Decimal field's TypeInfo entry must name "System.Decimal"
        // (the 16-byte value type), isRef = 0 (a decimal is NOT a GC
        // reference), with the staticAddr pointing at the Il2CppDecimal
        // storage slot -- mirroring the EmitStaticStorage side (ty3), so the
        // reflection field table agrees with the definition the .cctor
        // writes. The model carries FieldTypeName="System.Decimal" (the
        // MetadataLoader extended static-field decode), which is the single
        // authority both sides classify from.
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Euler", Owner = t, IsStatic = true, FieldTypeName = "System.Decimal",
        });
        var code = TypeEmitter.EmitTypeInfo(t);
        Assert.Contains("{ \"Euler\", \"System.Decimal\", -1, 0, &Calc_Node_sf_Euler, 2 },", code);
    }

    [Fact]
    public void EmitStaticStorage_MetadataDecimalField_UsesIl2CppDecimal()
    {
        // The metadata-driven path (md supplied -- the CodeGenerator call-site
        // pairing): a static System.Decimal field (FIELD 0x06 + VALUETYPE 0x11
        // + TypeDefOrRef -> System.Decimal) must emit 16-byte Il2CppDecimal
        // storage, NOT the historical int32_t (decimal sf_Euler build FAIL:
        // the .cctor wrote an Il2CppDecimal into an int32_t slot). The model
        // FieldDef carries no TypeKind for this shape in the md-only path, so
        // the signature decode is what classifies it.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_sd_" + Guid.NewGuid().ToString("N") + ".dll");
        var bytes = SynthesizeAcme().ToArray();
        File.WriteAllBytes(tmp, bytes);
        try
        {
            using var stream = new MemoryStream(bytes);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            var types = MetadataLoader.LoadAssembly(tmp);
            var util = types.First(t => t.FullName == "Acme.Util");
            // Acme.Util carries the synthetic static Rate : System.Decimal
            // field (see SynthesizeAcme). The md arm decodes its VALUETYPE
            // signature to the Decimal semantic -- asserting the exact decimal
            // output proves the decode activated (a miss falls back to Value).
            var code = TypeEmitter.EmitStaticStorage(util, md);
            Assert.Contains("il2cpp::runtime::Il2CppDecimal Acme_Util_sf_Rate = { 0, 0, 0 }; // 16-byte decimal value", code);
            Assert.DoesNotContain("int32_t Acme_Util_sf_Rate", code);
            var tinfo = TypeEmitter.EmitTypeInfo(util, md: md);
            Assert.Contains("{ \"Rate\", \"System.Decimal\", -1, 0, &Acme_Util_sf_Rate, 3 },", tinfo);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // wf16 (winforms1): static IntPtr/Int64 fields must emit FULL-WIDTH storage
    // (intptr_t/int64_t), NOT the historical int32_t -- the M1 slot truncated a
    // 64-bit WNDPROC/HWND pointer on x64 (sf_P15OldProc / sf_EditHwnd shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitStaticStorage_IntPtrField_EmitsIntptrT()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Handle", Owner = t, IsStatic = true, FieldTypeName = "System.IntPtr",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("intptr_t Calc_Node_sf_Handle = 0; // native-int pointer carrier", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Handle", code);
    }

    [Fact]
    public void EmitStaticStorage_UIntPtrField_EmitsUintptrT()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Raw", Owner = t, IsStatic = true, FieldTypeName = "System.UIntPtr",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("uintptr_t Calc_Node_sf_Raw = 0; // native unsigned pointer carrier", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Raw", code);
    }

    [Fact]
    public void EmitStaticStorage_Int64Field_EmitsInt64T()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Ticks", Owner = t, IsStatic = true, FieldTypeName = "System.Int64",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("int64_t Calc_Node_sf_Ticks = 0; // 64-bit signed value", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Ticks", code);
    }

    [Fact]
    public void EmitStaticStorage_UInt64Field_EmitsUint64T()
    {
        var t = MakeNode();
        t.StaticFields.Add(new FieldDef
        {
            Name = "Stamp", Owner = t, IsStatic = true, FieldTypeName = "System.UInt64",
        });
        var code = TypeEmitter.EmitStaticStorage(t);
        Assert.Contains("uint64_t Calc_Node_sf_Stamp = 0; // 64-bit unsigned value", code);
        Assert.DoesNotContain("int32_t Calc_Node_sf_Stamp", code);
    }

    [Fact]
    public void StaticFieldCppType_IntPtrField_ReturnsIntptrT()
    {
        // The generated.h extern side (CodeGenerator's "static field storage
        // declarations" loop) must agree with the EmitStaticStorage definition
        // (decimalcase cmake FAIL rule): IntPtr -> intptr_t, Int64 -> int64_t.
        var t = MakeNode(); // StaticFields[0] = Count (int)
        t.StaticFields.Add(new FieldDef
        {
            Name = "Handle", Owner = t, IsStatic = true, FieldTypeName = "System.IntPtr",
        });
        t.StaticFields.Add(new FieldDef
        {
            Name = "Ticks", Owner = t, IsStatic = true, FieldTypeName = "System.Int64",
        });
        Assert.Equal("int32_t", TypeEmitter.StaticFieldCppType(t.StaticFields[0], null));
        Assert.Equal("intptr_t", TypeEmitter.StaticFieldCppType(t.StaticFields[1], null));
        Assert.Equal("int64_t", TypeEmitter.StaticFieldCppType(t.StaticFields[2], null));
    }

    [Fact]
    public void EmitStaticStorage_MetadataNativeIntField_UsesIntptrT()
    {
        // The metadata-driven path (md supplied -- the CodeGenerator call-site
        // pairing): a static native-int field (FIELD 0x06 + 0x18 -- the exact
        // winforms1 IntPtr static signature, probe-verified) must emit intptr_t
        // storage, NOT int32_t (wf16 sf_P15OldProc truncation). The model
        // FieldDef carries FieldTypeName="System.IntPtr" from the loader's
        // extended static decode; the md arm also decodes the raw 0x18 byte.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_sn_" + Guid.NewGuid().ToString("N") + ".dll");
        var bytes = SynthesizeAcme().ToArray();
        File.WriteAllBytes(tmp, bytes);
        try
        {
            using var stream = new MemoryStream(bytes);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            var types = MetadataLoader.LoadAssembly(tmp);
            var util = types.First(t => t.FullName == "Acme.Util");
            var code = TypeEmitter.EmitStaticStorage(util, md);
            Assert.Contains("intptr_t Acme_Util_sf_Handle = 0; // native-int pointer carrier", code);
            Assert.DoesNotContain("int32_t Acme_Util_sf_Handle", code);
            Assert.Contains("int64_t Acme_Util_sf_Wide = 0; // 64-bit signed value", code);
            Assert.DoesNotContain("int32_t Acme_Util_sf_Wide", code);
            // The reflection field table mirrors the classification: the wide
            // statics get their real type name (not the Int32 fallback).
            var tinfo = TypeEmitter.EmitTypeInfo(util, md: md);
            Assert.Contains("{ \"Handle\", \"System.IntPtr\", -1, 0, &Acme_Util_sf_Handle, 3 },", tinfo);
            Assert.Contains("{ \"Wide\", \"System.Int64\", -1, 0, &Acme_Util_sf_Wide, 3 },", tinfo);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // End-to-end: a SYNTHETIC ECMA-335 PE (Acme.Util : Acme.Base, Acme.Iface
    // with fields X/Y + method Add) flows through MetadataLoader into the
    // TypeInfo table + registration line.
    //--------------------------------------------------------------------------
    [Fact]
    public void EmitTypeInfo_SyntheticPe_GeneratesTableAndRegistration()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_ti_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeAcme().ToArray());
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            var util = types.First(t => t.FullName == "Acme.Util");
            var code = TypeEmitter.EmitTypeInfo(util, new[] { "Acme.Util" });
            Assert.Contains("il2cpp::Il2CppTypeInfo Acme_Util_typeinfo = {", code);
            Assert.Contains("\"Acme.Util\",", code);
            Assert.Contains("&Acme_Base_typeinfo,", code);        // user base resolved
            Assert.Contains("&Acme_Iface_typeinfo,", code);       // interface wired
            Assert.Contains("{ \"X\", \"System.Int32\", 8, 0, nullptr, 1 },", code);
            Assert.Contains("{ \"Y\", \"System.Int32\", -1, 0, &Acme_Util_sf_Y, 3 },", code);
            var hash = SigHash.Hash("Acme.Util.Add(i,i)");
            Assert.Contains("&Acme_Util_Add_" + hash + ")", code);
            Assert.Equal("il2cpp::il2cpp_typeinfo_register(&Acme_Util_typeinfo);", TypeEmitter.EmitTypeInfoRegistration(util));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // refl2 S3/D4: CliOptions consumption -- mode B manifest scope actually
    // limits TypeInfo table emission (the generator now reads the manifest).
    // Scope = FilterReflectionManifest Kept + base/interface closure (a kept
    // type's table references &{Base}_typeinfo / &{Iface}_typeinfo, which must
    // also emit or the link fails). granularity all = mode A (every type).
    // Uses SynthesizeAcmeBase (Acme.Base <- Acme.Leaf, NO interface -- the
    // Acme synthetic with IfaceMethod fails VTablePlanner's interface map
    // validation, an unrelated precondition for full Generate).
    //--------------------------------------------------------------------------
    [Fact]
    public void Generate_ModeBManifest_LimitsTypeInfoScopeWithClosure()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_mb_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeAcmeBase().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_mbout_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            var opts = new CliOptions
            {
                InputAssembly = tmp, OutputDir = dir,
                ReflectionListProvided = true,
                ReflectionList = new[] { "Acme.Leaf" },
                ReflectionGranularity = ReflectionGranularity.Members,
            };
            CodeGenerator.Generate(types, dir, tmp, options: opts);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            // The kept type + its base closure all emit externs.
            Assert.Contains("extern il2cpp::Il2CppTypeInfo Acme_Leaf_typeinfo;", header);
            Assert.Contains("extern il2cpp::Il2CppTypeInfo Acme_Base_typeinfo;", header);
            // The Leaf table itself is emitted.
            var leafCpp = File.ReadAllText(Path.Combine(dir, "src", "Acme_Leaf.cpp"));
            Assert.Contains("il2cpp::Il2CppTypeInfo Acme_Leaf_typeinfo = {", leafCpp);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Generate_ModeBManifest_ExcludesNonManifestTypes()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_mb2_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeAcmeBase().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_mb2out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            // Manifest names ONLY Acme.Base -> Leaf must NOT emit a table.
            var opts = new CliOptions
            {
                InputAssembly = tmp, OutputDir = dir,
                ReflectionListProvided = true,
                ReflectionList = new[] { "Acme.Base" },
                ReflectionGranularity = ReflectionGranularity.Members,
            };
            CodeGenerator.Generate(types, dir, tmp, options: opts);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern il2cpp::Il2CppTypeInfo Acme_Base_typeinfo;", header);
            Assert.DoesNotContain("Acme_Leaf_typeinfo", header);
            var leafCpp = File.ReadAllText(Path.Combine(dir, "src", "Acme_Leaf.cpp"));
            Assert.DoesNotContain("Acme_Leaf_typeinfo", leafCpp);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Generate_ModeAAllGranularity_IgnoresManifestScope()
    {
        // --reflection-granularity all = mode A: the whole type set gets
        // tables even when a manifest is present.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_ma_" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(tmp, SynthesizeAcmeBase().ToArray());
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_maout_" + Guid.NewGuid().ToString("N"));
        try
        {
            var types = MetadataLoader.LoadAssembly(tmp);
            var opts = new CliOptions
            {
                InputAssembly = tmp, OutputDir = dir,
                ReflectionListProvided = true,
                ReflectionList = new[] { "Acme.Base" },
                ReflectionGranularity = ReflectionGranularity.All,
            };
            CodeGenerator.Generate(types, dir, tmp, options: opts);
            var header = File.ReadAllText(Path.Combine(dir, "include", "internal", "generated.h"));
            Assert.Contains("extern il2cpp::Il2CppTypeInfo Acme_Leaf_typeinfo;", header);
            Assert.Contains("extern il2cpp::Il2CppTypeInfo Acme_Base_typeinfo;", header);
        }
        finally
        {
            Directory.Delete(dir, true);
            File.Delete(tmp);
        }
    }

    [Fact]
    public void EmitStaticStorage_MetadataReferenceField_UsesIl2CppObjectPtr()
    {
        // The metadata-driven path (md supplied -- the CodeGenerator call-site
        // pairing): a static STRING field (signature 0x06 0x0E) must emit
        // Il2CppObject* storage even though its FieldDef carries no TypeKind
        // (top-level static fields are loaded type-less -- the delechain
        // Func`2 field is the same shape). Value statics stay int32_t.
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_st_" + Guid.NewGuid().ToString("N") + ".dll");
        var bytes = SynthesizeAcme().ToArray();
        File.WriteAllBytes(tmp, bytes);
        try
        {
            using var stream = new MemoryStream(bytes);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            var types = MetadataLoader.LoadAssembly(tmp);
            var util = types.First(t => t.FullName == "Acme.Util");
            var code = TypeEmitter.EmitStaticStorage(util, md);
            Assert.Contains("Il2CppObject* Acme_Util_sf_Label = nullptr; // GC reference (delegate / object)", code);
            Assert.DoesNotContain("int32_t Acme_Util_sf_Label", code);
            Assert.Contains("int32_t Acme_Util_sf_Y = 0;", code);
            // The reflection field table mirrors the classification: the
            // static reference field gets its type name + the GC-reference flag.
            var tinfo = TypeEmitter.EmitTypeInfo(util, md: md);
            Assert.Contains("{ \"Label\", \"System.String\", -1, 1, &Acme_Util_sf_Label, 3 },", tinfo);
            Assert.Contains("{ \"Y\", \"System.Int32\", -1, 0, &Acme_Util_sf_Y, 3 },", tinfo);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    //--------------------------------------------------------------------------
    // Synthetic assembly: Acme.Base (class), Acme.Iface (interface with one
    // abstract method), Acme.Util : Base, Iface with
    //   public int X;           (instance value field)
    //   public static int Y;    (static field)
    //   public static string Label; (static REFERENCE field)
    //   public int Add(int,int) (static method, IL body)
    //   public .ctor()          (empty body)
    //--------------------------------------------------------------------------
    internal static MemoryStream SynthesizeAcme()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("acme.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var decRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));

        var baseTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Base"), objRef, default, default);

        var ifaceMethod = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("IfaceMethod"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }), // int32 IfaceMethod()
            0, default(ParameterHandle));
        var ifaceTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Iface"), default(EntityHandle), default, ifaceMethod);

        // Acme.Util fields: X (instance int32), Y (static int32),
        // Label (static string -- a REFERENCE-typed static field),
        // Rate (static System.Decimal -- a VALUETYPE static field).
        var fieldSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 });
        var fieldX = mdb.AddFieldDefinition(
            FieldAttributes.Public, mdb.GetOrAddString("X"), fieldSig);
        var fieldY = mdb.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Y"), fieldSig);
        var stringFieldSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x0E }); // FIELD + ELEMENT_TYPE_STRING
        var fieldLabel = mdb.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Label"), stringFieldSig);
        // FIELD + ELEMENT_TYPE_VALUETYPE + TypeDefOrRef coded index of the
        // System.Decimal TypeRef (CodedIndex.TypeDefOrRef packs the row
        // number + tag bits 01=TypeRef; compressed into the blob).
        var decFieldSigBlob = new BlobBuilder();
        decFieldSigBlob.WriteByte(0x06);
        decFieldSigBlob.WriteByte(0x11);
        decFieldSigBlob.WriteCompressedInteger(CodedIndex.TypeDefOrRef(decRef));
        var fieldRate = mdb.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Rate"),
            mdb.GetOrAddBlob(decFieldSigBlob));
        // wf16 (winforms1): native-int static field (FIELD 0x06 + ELEMENT_TYPE_I
        // 0x18 -- the exact signature winforms1's IntPtr statics carry) and an
        // int64 static (0x0A). Both must emit full-width storage, NOT int32_t.
        var nativeFieldSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x18 });
        var fieldHandle = mdb.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Handle"), nativeFieldSig);
        var int64FieldSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x0A });
        var fieldWide = mdb.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static, mdb.GetOrAddString("Wide"), int64FieldSig);

        // Method bodies: Add returns 0 (ldc.i4.0; ret); .ctor empty (ret).
        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var addEnc = new InstructionEncoder(new BlobBuilder());
        addEnc.OpCode(ILOpCode.Ldc_i4_0);
        addEnc.OpCode(ILOpCode.Ret);
        var addBody = stream.AddMethodBody(addEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var ctorEnc = new InstructionEncoder(new BlobBuilder());
        ctorEnc.OpCode(ILOpCode.Ret);
        var ctorBody = stream.AddMethodBody(ctorEnc, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }), // instance void .ctor()
            ctorBody, default(ParameterHandle));
        // MethodDef order must keep each type's methods CONTIGUOUS (the
        // TypeDef methodList points at the FIRST one): Util's .ctor and Add
        // are consecutive, so Add MUST be defined AFTER .ctor (a method
        // defined before .ctor would land in Acme.Iface's group).
        var add = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Add"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }), // static int Add(int32,int32)
            addBody, default(ParameterHandle));

        var utilTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Util"), baseTd, fieldX, ctor);        mdb.AddInterfaceImplementation(utilTd, ifaceTd);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, stream.Builder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    //--------------------------------------------------------------------------
    // Synthetic assembly for the full-Generate mode B tests (refl2 S3): a
    // plain base/derived pair with NO interface (SynthesizeAcme's Acme.Iface
    // is unimplemented by Util, which fails VTablePlanner's interface-map
    // validation during full CodeGenerator.Generate).
    //   Acme.Base              (class, no members)
    //   Acme.Leaf : Base       (public int X; .ctor; public static int Add)
    //--------------------------------------------------------------------------
    internal static MemoryStream SynthesizeAcmeBase()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("acmebase.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var baseTd = mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Base"), objRef, default, default);

        var fieldSig = mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 });
        var fieldX = mdb.AddFieldDefinition(
            FieldAttributes.Public, mdb.GetOrAddString("X"), fieldSig);

        var stream = new MethodBodyStreamEncoder(new BlobBuilder());
        var ctorEnc = new InstructionEncoder(new BlobBuilder());
        ctorEnc.OpCode(ILOpCode.Ret);
        var ctorBody = stream.AddMethodBody(ctorEnc, 0, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }), // instance void .ctor()
            ctorBody, default(ParameterHandle));
        var addEnc = new InstructionEncoder(new BlobBuilder());
        addEnc.OpCode(ILOpCode.Ldc_i4_0);
        addEnc.OpCode(ILOpCode.Ret);
        var addBody = stream.AddMethodBody(addEnc, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var add = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Add"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }), // static int Add(int32,int32)
            addBody, default(ParameterHandle));

        mdb.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Leaf"), baseTd, fieldX, ctor);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, stream.Builder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }
}
