using System.Reflection.Metadata;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// ModelCompletenessTests -- Model/ domain completeness audit (md1 b23).
//
// Covers the semantic surface added by the audit:
//   FieldDef  : static field types -- IsReference + FieldTypeName round-trip
//               (the M1 static-field signature gap: static fields were loaded
//               type-less; the MODEL carries the full type surface for them).
//   MethodDef : ReturnsVoid / IsInstance / ResultKind / generic arity.
//   TypeDef   : nested-type links (IsNested / DeclaringTypeName / DeclaringType
//               / NestedTypes) + IsGenericType convenience.
//------------------------------------------------------------------------------
public class ModelCompletenessTests
{
    private static TypeDef MakeType(string name = "Calc.Point")
        => new() { Name = name, Namespace = "Calc" };

    // ---- FieldDef: static field type semantics (the required audit test) ----

    [Fact]
    public void StaticReferenceField_CarriesTypeAndReferenceSemantics()
    {
        var t = MakeType();
        var f = new FieldDef
        {
            Name = "s_name",
            Owner = t,
            IsStatic = true,
            TypeKind = FieldTypeKind.Reference,
            FieldTypeName = "System.String",
        };
        Assert.True(f.IsStatic);
        Assert.True(f.IsReference);
        Assert.Equal(FieldTypeKind.Reference, f.TypeKind);
        Assert.Equal("System.String", f.FieldTypeName);
    }

    [Fact]
    public void StaticValueField_DefaultInt32_IsNotReference()
    {
        var t = MakeType();
        var f = new FieldDef
        {
            Name = "counter",
            Owner = t,
            IsStatic = true,
        };
        Assert.True(f.IsStatic);
        Assert.Equal(FieldTypeKind.Int32, f.TypeKind);
        Assert.False(f.IsReference);
        Assert.Equal("", f.FieldTypeName);
    }

    [Fact]
    public void StaticReferenceField_SlotIndex_RoundTrips()
    {
        var t = MakeType();
        var f = new FieldDef
        {
            Name = "s_next",
            Owner = t,
            IsStatic = true,
            TypeKind = FieldTypeKind.Reference,
            FieldTypeName = "Calc.Node",
        };
        f.SlotIndex = 3;
        Assert.Equal(3, f.SlotIndex);
        Assert.Equal("Calc.Node", f.FieldTypeName);
    }

    [Fact]
    public void InstanceReferenceField_PreservesFieldTypeName()
    {
        var t = MakeType();
        var f = new FieldDef
        {
            Name = "Next",
            Owner = t,
            TypeKind = FieldTypeKind.Reference,
            FieldTypeName = "Calc.Node",
        };
        Assert.False(f.IsStatic);
        Assert.True(f.IsReference);
        Assert.Equal("Calc.Node", f.FieldTypeName);
    }

    [Fact]
    public void StaticReferenceField_TypeKindReference_MatchesIsReference()
    {
        var t = MakeType();
        var f = new FieldDef
        {
            Name = "s_delegate",
            Owner = t,
            IsStatic = true,
            TypeKind = FieldTypeKind.Reference,
            FieldTypeName = "System.Func<int32,int32>",
        };
        // IsReference is the single semantic surface the emitter consults for
        // the static extern declaration (Il2CppObject* vs int32_t).
        Assert.True(f.IsReference);
        Assert.Equal("System.Func<int32,int32>", f.FieldTypeName);
    }

    // ---- MethodDef: ReturnsVoid / IsInstance / ResultKind / generic arity ----

    private static MethodDef MakeMethod(
        TypeDef owner, string name, bool isStatic = false, SignatureTypeCode returnType = SignatureTypeCode.Int32)
        => new()
        {
            Name = name,
            Owner = owner,
            IsStatic = isStatic,
            ReturnType = returnType,
        };

    [Fact]
    public void VoidMethod_ReturnsVoid_AndResultKindVoid()
    {
        var m = MakeMethod(MakeType(), "Write", returnType: SignatureTypeCode.Void);
        Assert.True(m.ReturnsVoid);
        Assert.Equal(MethodResultKind.Void, m.ResultKind);
    }

    [Fact]
    public void Int32Method_NotReturnsVoid_ResultKindInt32()
    {
        var m = MakeMethod(MakeType(), "Add", returnType: SignatureTypeCode.Int32);
        Assert.False(m.ReturnsVoid);
        Assert.Equal(MethodResultKind.Int32, m.ResultKind);
    }

    [Fact]
    public void InstanceMethod_IsInstance_StaticMethod_IsNot()
    {
        var t = MakeType();
        var instance = MakeMethod(t, "GetX", isStatic: false);
        var statik = MakeMethod(t, "Main", isStatic: true);
        Assert.True(instance.IsInstance);
        Assert.False(instance.IsStatic);
        Assert.False(statik.IsInstance);
        Assert.True(statik.IsStatic);
    }

    [Fact]
    public void StringReturn_ResultKindReference()
    {
        var m = MakeMethod(MakeType(), "ToString", returnType: SignatureTypeCode.String);
        Assert.Equal(MethodResultKind.Reference, m.ResultKind);
        Assert.False(m.ReturnsVoid);
    }

    [Fact]
    public void ClassReturn_RawByte012_ResultKindReference()
    {
        // MetadataLoader stores the RAW signature byte: a class return is the
        // unnamed enum value 18 (0x12) -- SignatureTypeCode has no Class member.
        var m = MakeMethod(MakeType(), "GetNode", returnType: (SignatureTypeCode)0x12);
        Assert.Equal(MethodResultKind.Reference, m.ResultKind);
    }

    [Fact]
    public void ValueTypeReturn_RawByte011_ResultKindValueType()
    {
        // Raw VALUETYPE byte 0x11 (unnamed enum value 17).
        var m = MakeMethod(MakeType(), "GetCoord", returnType: (SignatureTypeCode)0x11);
        Assert.Equal(MethodResultKind.ValueType, m.ResultKind);
    }

    [Fact]
    public void Int64Return_ResultKindInt64()
    {
        var m = MakeMethod(MakeType(), "GetLong", returnType: SignatureTypeCode.Int64);
        Assert.Equal(MethodResultKind.Int64, m.ResultKind);
    }

    [Fact]
    public void DoubleReturn_ResultKindFloating()
    {
        var m = MakeMethod(MakeType(), "GetPi", returnType: SignatureTypeCode.Double);
        Assert.Equal(MethodResultKind.Floating, m.ResultKind);
    }

    [Fact]
    public void GenericParamReturns_ResultKindGeneric()
    {
        // VAR 0x13 = GenericTypeParameter; MVAR 0x1E = GenericMethodParameter.
        var varReturn = MakeMethod(MakeType(), "Id", returnType: SignatureTypeCode.GenericTypeParameter);
        var mvarReturn = MakeMethod(MakeType(), "M", returnType: SignatureTypeCode.GenericMethodParameter);
        Assert.Equal(MethodResultKind.Generic, varReturn.ResultKind);
        Assert.Equal(MethodResultKind.Generic, mvarReturn.ResultKind);
    }

    [Fact]
    public void GenericMethod_Arity_IsGenericMethod()
    {
        var t = MakeType();
        var generic = new MethodDef
        {
            Name = "Id",
            Owner = t,
            ReturnType = SignatureTypeCode.GenericMethodParameter,
            GenericArity = 1,
        };
        var plain = new MethodDef { Name = "NoArgs", Owner = t, ReturnType = SignatureTypeCode.Int32 };
        Assert.True(generic.IsGenericMethod);
        Assert.Equal(1, generic.GenericArity);
        Assert.False(plain.IsGenericMethod);
        Assert.Equal(0, plain.GenericArity);
    }

    // ---- TypeDef: nested / generic / interface semantics ----

    [Fact]
    public void NestedType_DeclaringLinks_AndChildren()
    {
        var outer = new TypeDef { Name = "Recurse", Namespace = "Cases" };
        var nested = new TypeDef
        {
            Name = "Node",
            Namespace = "",
            IsNested = true,
            DeclaringTypeName = outer.FullName,
        };
        nested.DeclaringType = outer;
        outer.NestedTypes.Add(nested);

        Assert.True(nested.IsNested);
        Assert.Equal("Cases.Recurse", nested.DeclaringTypeName);
        Assert.Same(outer, nested.DeclaringType);
        Assert.Single(outer.NestedTypes);
        Assert.Same(nested, outer.NestedTypes[0]);
    }

    [Fact]
    public void TopLevelType_IsNotNested_NoDeclaringType()
    {
        var t = MakeType("Calc.Program");
        Assert.False(t.IsNested);
        Assert.Equal("", t.DeclaringTypeName);
        Assert.Null(t.DeclaringType);
        Assert.Empty(t.NestedTypes);
    }

    [Fact]
    public void GenericType_IsGenericType()
    {
        var generic = new TypeDef { Name = "Box`1", Namespace = "Calc", GenericArity = 1 };
        var plain = new TypeDef { Name = "Box", Namespace = "Calc", GenericArity = 0 };
        Assert.True(generic.IsGenericType);
        Assert.False(plain.IsGenericType);
    }

    [Fact]
    public void InterfaceType_CarriesInterfaceSurface()
    {
        var iface = new TypeDef { Name = "IWalk", Namespace = "Calc", IsInterface = true };
        var impl = new TypeDef { Name = "Dog", Namespace = "Calc" };
        impl.InterfaceNames.Add(iface.FullName);
        impl.InterfaceTypes.Add(iface);

        Assert.True(iface.IsInterface);
        Assert.Equal("Calc.IWalk", impl.InterfaceNames[0]);
        Assert.Same(iface, impl.InterfaceTypes[0]);
    }
}
