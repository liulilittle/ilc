using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// B7: DefaultInterpolatedStringHandler.AppendFormatted<T> over a USER value type
// (a struct or enum TypeDef in the input assembly).
//
//   * The resolver boxes the value slot through the "box:us:<name>" cast (the
//     same composite-engine path as the decimal/DateTime/Guid/TimeSpan arms).
//   * The emitter (CodeGenerator.EmitUserStructBoxCast) resolves the TypeDef and
//     applies the GC-safety gate: an enum boxes through icall_box_enum, a
//     reference-free user struct through icall_box_user_struct, and a user
//     struct with GC reference fields is REJECTED (icall_box_user_struct would
//     place an object reference at slot 1 and the boxed mark scan indexes
//     refFieldBitmap from the header -- a ref field would be miscounted as a
//     pure header, a GC-safety leak).
public class MethodMapTestsAppendFormattedUser
{
    [Theory]
    [InlineData("Cases.Point", "box:us:Cases.Point")]
    [InlineData("Cases.Color", "box:us:Cases.Color")]
    public void Resolve_HandlerAppendFormatted_UserValueType_MapsToBoxUs(string typeFullName, string expectedCast)
    {
        var dot = typeFullName.IndexOf('.');
        var ns = typeFullName.Substring(0, dot);
        var nm = typeFullName.Substring(dot + 1);
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x01, 0x1C, 0x1E, 0x00 },
            new byte[] { 0x0A, 0x01, 0x11, 0x09 },
            ns, nm));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o", resolved.CppName);
        Assert.Equal(new[] { "deref:r", expectedCast }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Emit_UserStructBox_RefFieldStruct_ThrowsLoud()
    {
        var def = new TypeDef
        {
            Name = "RefHolder",
            Namespace = "Cases",
            IsValueType = true,
            BaseTypeName = "System.ValueType",
            RefFieldCount = 1,
        };
        var ex = Assert.Throws<NotSupportedException>(() =>
            CodeGenerator.EmitUserStructBoxCast("Cases.RefHolder", "loc", new[] { def }));
        Assert.Contains("GC reference fields", ex.Message);
    }

    [Fact]
    public void Emit_UserStructBox_ReferenceFreeStruct_BoxesUserStruct()
    {
        var def = new TypeDef
        {
            Name = "Point",
            Namespace = "Cases",
            IsValueType = true,
            BaseTypeName = "System.ValueType",
            RefFieldCount = 0,
        };
        var expr = CodeGenerator.EmitUserStructBoxCast("Cases.Point", "loc", new[] { def });
        Assert.Contains("icall_box_user_struct(&Cases_Point_class", expr);
        Assert.Contains("sizeof(Cases_Point)", expr);
    }

    [Fact]
    public void Emit_UserStructBox_Enum_BoxesEnum()
    {
        var def = new TypeDef
        {
            Name = "Color",
            Namespace = "Cases",
            IsValueType = true,
            BaseTypeName = "System.Enum",
            RefFieldCount = 0,
        };
        var expr = CodeGenerator.EmitUserStructBoxCast("Cases.Color", "loc", new[] { def });
        Assert.Contains("icall_box_enum(&Cases_Color_class", expr);
    }

    [Fact]
    public void Emit_UserStructBox_UnresolvedType_ThrowsLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            CodeGenerator.EmitUserStructBoxCast("System.KeyValuePair`2", "loc", Array.Empty<TypeDef>()));
        Assert.Contains("no generated descriptor", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Reference generic arguments: the value slot is already an Il2CppObject*,
    // so the Append(object) dispatch (and its _w/_f/_wf trios) is used.
    //--------------------------------------------------------------------------
    [Theory]
    [InlineData(new byte[] { 0x0A, 0x01, 0x12, 0x09 }, new byte[] { 0x00, 0x01, 0x1C, 0x1E, 0x00 },
        "il2cpp::runtime::icall_stringbuilder_append_o", new[] { "deref:r", "" })]
    [InlineData(new byte[] { 0x0A, 0x01, 0x1C }, new byte[] { 0x00, 0x01, 0x1C, 0x1E, 0x00 },
        "il2cpp::runtime::icall_stringbuilder_append_o", new[] { "deref:r", "" })]
    [InlineData(new byte[] { 0x0A, 0x01, 0x1D, 0x0E }, new byte[] { 0x00, 0x01, 0x1C, 0x1E, 0x00 },
        "il2cpp::runtime::icall_stringbuilder_append_o", new[] { "deref:r", "" })]
    public void Resolve_HandlerAppendFormatted_ReferenceType_MapsToAppendObject(
        byte[] genericSignature, byte[] memberRefSignature, string cppName, string[] casts)
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            memberRefSignature, genericSignature, "Cases", "Widget"));

        Assert.Equal(cppName, resolved.CppName);
        Assert.Equal(casts, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_HandlerAppendFormatted_ReferenceTypeWidth_MapsToAppendObjectW()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x02, 0x1C, 0x1E, 0x00, 0x08 },
            new byte[] { 0x0A, 0x01, 0x12, 0x09 }, "Cases", "Widget"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o_w", resolved.CppName);
        Assert.Equal(new[] { "deref:r", "", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_HandlerAppendFormatted_ReferenceTypeWidthFormat_MapsToAppendObjectWf()
    {
        var resolved = ResolveMethodSpec(SynthesizeGeneric(
            "System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", "AppendFormatted",
            new byte[] { 0x00, 0x03, 0x1C, 0x1E, 0x00, 0x08, 0x0E },
            new byte[] { 0x0A, 0x01, 0x12, 0x09 }, "Cases", "Widget"));

        Assert.Equal("il2cpp::runtime::icall_stringbuilder_append_o_wf", resolved.CppName);
        Assert.Equal(new[] { "deref:r", "", "i", "" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same harness as MethodMapTests.StringBuilder)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>(), null, false, null, null);
    }

    private static MemoryStream SynthesizeGeneric(string ns, string typeName, string memberName,
        byte[] memberRefSignature, byte[] genericSignature, string genArgNs, string genArgName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString(genArgNs), mdb.GetOrAddString(genArgName));
        var memberRef = mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(memberRefSignature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }

    private static MemoryStream Serialize(MetadataBuilder mdb)
    {
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
