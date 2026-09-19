using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// The M3 outer decoder's VAR arm (MethodRefResolver side of the B2/B5
// family): a call inside a generic DEFINITION body (e.g. `inner.Add(v)` in
// MyList<T>) carries an OPEN TypeSpec parent (blob 15-12-..-01-13-00).
// Resolving it under the closed body pass's hint must decode through the
// hint (R2b P2 InstantiationSuffixHint), not throw
// "generic argument byte 0x13 in M3".
//------------------------------------------------------------------------------
public class MethodMapTestsOpenGenericParent
{
    [Fact]
    public void Resolve_Add_OpenListParent_ResolvesThroughHint()
    {
        // List<T>.Add(T) shape (blob 20 01 02 13 00) parented on open
        // List<T>; hint "int" (the List_1_int body pass) -> the int32 arm.
        using var pe = Synthesize();
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "Add");

        var prev = MethodRefResolver.InstantiationSuffixHint;
        MethodRefResolver.InstantiationSuffixHint = "int";
        ResolvedMethod resolved;
        try
        {
            resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
                null, false, null, null);
        }
        finally
        {
            MethodRefResolver.InstantiationSuffixHint = prev;
        }

        Assert.Equal("il2cpp::il2cpp_list_i4_add", resolved.CppName);
    }

    [Fact]
    public void Resolve_GetCurrent_DottedClassParent_MapsToRefCarrier()
    {
        // IEnumerator<Exception>.get_Current (B6 ilistException foreach):
        // instance, 0 params, ret VAR (0x13 0x00); parent TypeSpec
        // IEnumerator`1 + 0x12 System.Exception -> the reference arm.
        using var pe = SynthesizeEnumCurrent();
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "get_Current");

        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>(),
            null, false, null, null);

        Assert.Equal("il2cpp::il2cpp_ienum_get_current_ref", resolved.CppName);
    }

    private static MemoryStream Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var listRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("List`1"));

        // OPEN TypeSpec List<T>: 0x15, 0x12 CLASS, coded def index, 1 arg,
        // VAR (0x13) index 0.
        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(CodedIndex(listRef));
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x13);
        tsBlob.WriteCompressedInteger(0);
        var openSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        // void Add(T): instance, 1 param VAR (0x13 index 0).
        mdb.AddMemberReference(openSpec, mdb.GetOrAddString("Add"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x01, 0x01, 0x13, 0x00 }));
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

    private static MemoryStream SynthesizeEnumCurrent()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var ienumRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerator`1"));
        var excRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Exception"));

        // TypeSpec IEnumerator<Exception>: 0x15, 0x12 CLASS, coded def
        // index, 1 arg, 0x12 CLASS + coded Exception. Rows: ienumRef row 1
        // (coded 0x05), excRef row 2 (coded 0x09).
        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(0x05);
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(0x09);
        var spec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        // T get_Current(): instance, 0 params, ret VAR (0x13 index 0).
        mdb.AddMemberReference(spec, mdb.GetOrAddString("get_Current"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x13, 0x00 }));
        return Serialize(mdb);
    }

    private static int CodedIndex(EntityHandle h)
    {
        var row = MetadataTokens.GetToken(h) & 0xFFFFFF;
        var tag = h.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            _ => throw new ArgumentException($"handle kind {h.Kind}"),
        };
        return (row << 2) | tag;
    }
}
