using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// Tuple-Rest chain typing (8-tuple long chains, e.g. TupleExtensions
// Deconstruct: t0=get_Rest(arg0); get_ItemK(t0); t1=get_Rest(t0)):
// get_Rest always yields a Tuple consumed as a receiver (reference slot);
// get_ItemK yields the K-th instantiation arg (int -> value slot,
// string/object -> reference slot). The default VAR->int32 classification
// mistyped both (r77: int32_t temps fed to Il2CppObject* params -> 302 nmf).
// RED basis: before the fix, tests 2-3 resolved ReturnsRef=false.
public class TupleRestChainTests
{
    [Fact]
    public void Resolve_TupleIntString_Item1_IsValue()
    {
        // Tuple`2<int,string>.get_Item1 -> int (value slot preserved).
        using var pe = BuildTupleAssembly(out var item1, out _, out _);
        var md = pe.GetMetadataReader();
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(item1), Array.Empty<TypeDef>());
        Assert.True(resolved.ReturnsValue);
        Assert.False(resolved.ReturnsRef);
        Assert.StartsWith("System_Tuple_2_", resolved.CppName);
    }

    [Fact]
    public void Resolve_TupleIntString_Item2_IsReference()
    {
        // Tuple`2<int,string>.get_Item2 -> string (reference slot).
        using var pe = BuildTupleAssembly(out _, out var item2, out _);
        var md = pe.GetMetadataReader();
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(item2), Array.Empty<TypeDef>());
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.StartsWith("System_Tuple_2_", resolved.CppName);
    }

    [Fact]
    public void Resolve_Tuple8Int_Rest_IsReference()
    {
        // Tuple`8<int x8>.get_Rest -> Tuple (reference slot, chain receiver).
        using var pe = BuildTupleAssembly(out _, out _, out var rest);
        var md = pe.GetMetadataReader();
        var resolved = MethodRefResolver.Resolve(md, MetadataTokens.GetToken(rest), Array.Empty<TypeDef>());
        Assert.True(resolved.ReturnsValue);
        Assert.True(resolved.ReturnsRef);
        Assert.StartsWith("System_Tuple_8_", resolved.CppName);
    }

    // TypeRefs: System.Tuple`2 (row 1 -> coded 0x05), System.Tuple`8
    // (row 2 -> coded 0x09). TypeSpecs: Tuple`2<int,string>
    // [15 12 05 02 08 0E]; Tuple`8<int x8> [15 12 09 08 08*8].
    // MemberRefs: get_Item1/get_Item2 (parent = Tuple`2 spec, instance sig
    // 20 00 13 01 = 0 params, VAR return); get_Rest (parent = Tuple`8
    // spec, same sig shape -- the resolver ignores the VAR index).
    private static PEReader BuildTupleAssembly(
        out MemberReferenceHandle item1, out MemberReferenceHandle item2, out MemberReferenceHandle rest)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var tuple2 = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Tuple`2"));
        var tuple8 = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Tuple`8"));
        var spec2 = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x05, 0x02, 0x08, 0x0E }));
        var spec8 = mdb.AddTypeSpecification(mdb.GetOrAddBlob(
            new byte[] { 0x15, 0x12, 0x09, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08 }));
        var itemSig = mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x13, 0x01 });
        item1 = mdb.AddMemberReference(spec2, mdb.GetOrAddString("get_Item1"), itemSig);
        item2 = mdb.AddMemberReference(spec2, mdb.GetOrAddString("get_Item2"), itemSig);
        rest = mdb.AddMemberReference(spec8, mdb.GetOrAddString("get_Rest"), itemSig);
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }
}
