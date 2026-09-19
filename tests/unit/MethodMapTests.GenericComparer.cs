using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// G6 spec (RED until the generic comparer family lands): caudit1 needs
//   EqualityComparer<int>.Default.Equals(1, 1)   -> True
//   Comparer<double>.Default.Compare(1.0, 2.0)   -> -1
// The runtime models the DEFAULT generic comparers as singletons (the
// System.Collections.Comparer precedent, arraysort.h il2cpp_comparer_default):
//   get_Default -> singleton getter, result tracked under the pseudo-type
//   "System.Collections.Generic.EqualityComparer`1_int" /
//   "System.Collections.Generic.Comparer`1_double" so the INSTANCE member call
//   resolves on the receiver hint; the members map to the typed runtime
//   icalls. Any other instantiation falls back to the generated-name path
//   (loud at link).
public class MethodMapTestsGenericComparer
{
    public const string EqInt = "System.Collections.Generic.EqualityComparer`1_int";
    public const string CmpDouble = "System.Collections.Generic.Comparer`1_double";

    [Fact]
    public void Resolve_EqualityComparerInt_GetDefault_ReturnsTrackedSingleton()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default", receiverType: EqInt);

        Assert.Equal("il2cpp::il2cpp_equality_comparer_default_int", resolved.CppName);
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.False(resolved.IsInstance);
        Assert.Equal(EqInt, resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_EqualityComparerInt_Equals_ValueEquality()
    {
        // instance bool Equals(int, int): 20 02 02 13 00 13 01 (both VAR ->
        // int32 in the int instantiation)
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "Equals",
            Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x13, 0x01)), "Equals", receiverType: EqInt);

        Assert.Equal("il2cpp::runtime::icall_equality_comparer_int_equals", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.Equal(new[] { "", "i", "i" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_ComparerDouble_GetDefault_ReturnsTrackedSingleton()
    {
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default", receiverType: CmpDouble);

        Assert.Equal("il2cpp::il2cpp_comparer_default_double", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal(CmpDouble, resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComparerDouble_Compare_UnboxesDoublePair()
    {
        // instance int Compare(double, double): 20 02 08 13 00 13 01
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "Compare",
            Blob(0x20, 0x02, 0x08, 0x13, 0x00, 0x13, 0x01)), "Compare", receiverType: CmpDouble);

        Assert.Equal("il2cpp::runtime::icall_comparer_double_compare", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.Equal(new[] { "", "d", "d" }, resolved.ParamTypeNames);
    }

    [Fact]
    public void Resolve_EqualityComparerInt_UnwiredInstantiation_FallsBackToGeneratedName()
    {
        // No receiver hint: the null-tag path defers to the generated-name
        // fallback (a receiver-typed attempt would be a LOUD P26 gap).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "EqualityComparer`1", "Equals",
            Blob(0x20, 0x02, 0x02, 0x13, 0x00, 0x13, 0x01)), "Equals");

        Assert.StartsWith("System_Collections_Generic_EqualityComparer", resolved.CppName);
    }

    [Fact]
    public void Resolve_ComparerDouble_UnwiredInstantiation_FallsBackToGeneratedName()
    {
        // No receiver hint: the null-tag path defers to the generated-name
        // fallback (a receiver-typed attempt would be a LOUD P26 gap).
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "Compare",
            Blob(0x20, 0x02, 0x08, 0x13, 0x00, 0x13, 0x01)), "Compare");

        Assert.StartsWith("System_Collections_Generic_Comparer", resolved.CppName);
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
