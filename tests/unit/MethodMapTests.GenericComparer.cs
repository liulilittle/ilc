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
    // B8-1: Comparer<uint>.Create must ride the INT32 carrier.
    //
    // The RED this pins: before the fix the uint tag fell through to the `_ =>`
    // arm of the Create switch and produced il2cpp_comparer_create_ref -- a REF
    // wrapper. Array.Sort<uint> then invoked that wrapper through the ref ABI
    // with the raw element word reinterpreted as an Il2CppObject*, which threw
    // "Failed to compare two elements in the array." at runtime. uint shares
    // the int32 slot ABI (the wrapped delegate applies its own unsigned order),
    // so the carrier MUST be the int one.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ComparerUInt_Create_RidesInt32Carrier()
    {
        // static Comparer<T> Create(Comparison<T>) -- both the return and the
        // parameter are GENERICINST (0x15) over VAR(0) (the resolver keys the
        // Create arm on ps[0] == 0x15).
        var resolved = ResolveMember(SynthesizeComparerCreate("Create"), "Create",
            receiverType: "System.Collections.Generic.Comparer`1_uint");

        Assert.Equal("il2cpp::runtime::il2cpp_comparer_create_int", resolved.CppName);
        Assert.True(resolved.ReturnsRef);
        Assert.Equal("System.Collections.Generic.Comparer`1_uint", resolved.ReturnTypeName);
    }

    [Fact]
    public void Resolve_ComparerUInt_GetDefault_RidesIntSingleton()
    {
        // Comparer<uint>.Default rides the int singleton (the uint searches
        // treat a Comparer`1* default by name as unsigned) -- unchanged, pinned
        // here so the B8-1 U4 family work does not silently move it.
        var resolved = ResolveMember(Synthesize("System.Collections.Generic", "Comparer`1", "get_Default",
            Blob(0x00, 0x00, 0x12, 0x05)), "get_Default",
            receiverType: "System.Collections.Generic.Comparer`1_uint");

        Assert.Equal("il2cpp::il2cpp_comparer_default_int", resolved.CppName);
        Assert.Equal("System.Collections.Generic.Comparer`1_uint", resolved.ReturnTypeName);
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

    // static Comparer<T> Create(Comparison<T>): both the RETURN and the single
    // PARAMETER are GENERICINST(0x15) over VAR(0) -- the resolver's Create arm
    // keys on ps[0] == 0x15, so the parameter must be the generic-inst shape,
    // not a CLASS byte. The comparer TypeRefs (Comparison`1 row 2) are added so
    // the GENERICINST definition index resolves.
    private static MemoryStream SynthesizeComparerCreate(string memberName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var comparerRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("Comparer`1"));
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Comparison`1"));
        // ret = GENERICINST CLASS(0x12) Comparer`1 row1 <VAR 0>
        // param = GENERICINST CLASS(0x12) Comparison`1 row2 <VAR 0>
        // The trailing TypeDefOrRef coded index is TypeRef<<2|row:
        // row1 -> 0x05, row2 -> 0x09.
        var sig = new byte[]
        {
            0x00, 0x01,                                     // default conv, 1 param
            0x15, 0x12, 0x05, 0x01, 0x13, 0x00,             // ret: Comparer`1<VAR0>
            0x15, 0x12, 0x09, 0x01, 0x13, 0x00,             // param: Comparison`1<VAR0>
        };
        var memberRef = mdb.AddMemberReference(comparerRef, mdb.GetOrAddString(memberName),
            mdb.GetOrAddBlob(sig));
        _ = memberRef;
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
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
