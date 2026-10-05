using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.GenericComparer W6 expansion (T2): EqualityComparer<string/long/
// double/object/class> + Comparer<int/string/long/object> ride the
// genericcomparer_obj singleton domain (singletons + typed icalls),
// mirroring the int/double precedent:
//
//   EqualityComparer.get_Default -> il2cpp_equality_comparer_default_<kind>
//     (tracked under EqualityComparer`1_<kind>); Equals -> typed icall
//     (long "l,l"; double "d,d"; string/object/class reference ""/"").
//   Comparer.get_Default -> il2cpp_comparer_default_<kind>; Compare ->
//     typed icall (int "i,i"; long "l,l"; string/object "").
public class MethodMapTestsComparerVal
{
    public static IEnumerable<object[]> EqKinds => new List<object[]>
    {
        new object[] { "string", "", "" },
        new object[] { "long", "l", "l" },
        new object[] { "double", "d", "d" },
        new object[] { "object", "", "" },
        new object[] { "class", "", "" },
    };

    public static IEnumerable<object[]> CmpKinds => new List<object[]>
    {
        new object[] { "int", "i", "i" },
        new object[] { "string", "", "" },
        new object[] { "long", "l", "l" },
        new object[] { "object", "", "" },
    };

    [Theory]
    [MemberData(nameof(EqKinds))]
    public void Resolve_EqCmp_GetDefault_ReturnsTrackedSingleton(string kind, string _, string __)
    {
        var tag = $"System.Collections.Generic.EqualityComparer`1_{kind}";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default", receiverType: tag);

        Assert.Equal($"il2cpp::il2cpp_equality_comparer_default_{kind}", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(tag, resolved.ReturnTypeName);
    }

    [Theory]
    [MemberData(nameof(EqKinds))]
    public void Resolve_EqCmp_Equals_MapsToTypedIcall(string kind, string c0, string c1)
    {
        var tag = $"System.Collections.Generic.EqualityComparer`1_{kind}";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "Equals",
            Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x13, 0x01)), "Equals", receiverType: tag);

        Assert.Equal($"il2cpp::runtime::icall_equality_comparer_{kind}_equals", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(new[] { "", c0, c1 }, resolved.ParamTypeNames);
    }

    [Theory]
    [MemberData(nameof(CmpKinds))]
    public void Resolve_Cmp_GetDefault_ReturnsTrackedSingleton(string kind, string _, string __)
    {
        var tag = $"System.Collections.Generic.Comparer`1_{kind}";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default", receiverType: tag);

        Assert.Equal($"il2cpp::il2cpp_comparer_default_{kind}", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(tag, resolved.ReturnTypeName);
    }

    [Theory]
    [MemberData(nameof(CmpKinds))]
    public void Resolve_Cmp_Compare_MapsToTypedIcall(string kind, string c0, string c1)
    {
        var tag = $"System.Collections.Generic.Comparer`1_{kind}";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "Compare",
            Blob(0x20, 0x02, 0x08, 0x13, 0x00, 0x13, 0x01)), "Compare", receiverType: tag);

        Assert.Equal($"il2cpp::runtime::icall_comparer_{kind}_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", c0, c1 }, resolved.ParamTypeNames);
    }

    // User reference-type instantiation (e.g. EqualityComparer<Prog/Box>):
    // get_Default normalizes the instance to the "…_class" tag, and the
    // Equals call on that TRACKED receiver rides the class icall (the real
    // e2e shape: var ce = EqualityComparer<Box>.Default; ce.Equals(a, b)).
    // An UNTRACKED receiver (raw "…_Prog/Box" tag) must stay P26 loud --
    // it may hold a custom comparer whose override must never be silently
    // replaced by reference identity (StackFold caudit2 soundness).
    [Fact]
    public void Resolve_EqCmp_UserClass_GetDefault_RidesClassSingleton()
    {
        var tag = "System.Collections.Generic.EqualityComparer`1_Prog/Box";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default", receiverType: tag);

        Assert.Equal("il2cpp::il2cpp_equality_comparer_default_class", resolved.CppName);
    }

    [Fact]
    public void Resolve_EqCmp_UserClass_Equals_RidesClassIcall()
    {
        var tag = "System.Collections.Generic.EqualityComparer`1_class";
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "Equals",
            Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x13, 0x01)), "Equals", receiverType: tag);

        Assert.Equal("il2cpp::runtime::icall_equality_comparer_class_equals", resolved.CppName);
    }

    [Fact]
    public void Resolve_EqCmp_UserClass_Equals_Untracked_StaysLoud()
    {
        var tag = "System.Collections.Generic.EqualityComparer`1_Prog/Box";
        Assert.Throws<NotSupportedException>(() => ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "Equals",
            Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x13, 0x01)), "Equals", receiverType: tag));
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as the resolver)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName, string? receiverType = null)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            receiverType, false, null, null);
    }

    private static byte[] Blob(params byte[] bytes) => bytes;

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
