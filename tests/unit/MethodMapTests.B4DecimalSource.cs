using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// p0grp: Enumerable.GroupBy over a System.Decimal[] SOURCE.
//
// Before this batch the decimal-source arm in MethodMap.Linq.cs handled only
// Select, so GroupBy fell past it, past the reference-source arm (which has no
// GroupBy) and into the int32-only gate -- whose LOUD list did not even name
// GroupBy, so the MethodSpec resolved to NULL and the resolver reported the
// phantom "generic instantiation of System.Linq.Enumerable.GroupBy has no BCL
// mapping in M5". These tests pin the real mapping and the self-describing
// rejections.
public class MethodMapTestsB4DecimalSource
{
    [Fact]
    public void GroupBy_DecimalSource_IntKey_MapsToDecimalSourceEntry()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(0x08));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_dec_key", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "a", "" }, resolved.ParamTypeNames);
        Assert.Equal(new[] { "0" }, resolved.AppendLiterals);
        Assert.Equal("System.Linq.IGroupingResult", resolved.ReturnTypeName);
    }

    [Fact]
    public void GroupBy_DecimalSource_ByteKey_CarriesNarrowingKeyKind()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(0x05));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_dec_key", resolved.CppName);
        Assert.Equal(new[] { "1" }, resolved.AppendLiterals);
    }

    // BATCH4: a 64-bit key rides the int64 carrier (keyKind 7) over the decimal
    // source; the decimal-source entry computes the key through the decimal-arg
    // wide-key ABI.
    [Fact]
    public void GroupBy_DecimalSource_LongKey_MapsWithKeyKind7()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(0x0A));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_dec_key", resolved.CppName);
        Assert.Equal(new[] { "7" }, resolved.AppendLiterals);
    }

    // BATCH4: a decimal KEY (0x11 value type) rides the 16-byte struct carrier
    // (keyKind 8) over the decimal source.
    [Fact]
    public void GroupBy_DecimalSource_DecimalKey_MapsWithKeyKind8()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(0x11, decimalKey: true));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_dec_key", resolved.CppName);
        Assert.Equal(new[] { "8" }, resolved.AppendLiterals);
    }

    // p0grp: a REFERENCE-element source (TSource = System.String) now routes to
    // the reference-source grouping surface (il2cpp_linq_group_by_full_key,
    // elemKind 1) instead of the old LOUD rejection.
    [Fact]
    public void GroupBy_ReferenceSource_MapsToGroupingFullKey()
    {
        var resolved = ResolveMethodSpec(SynthesizeGroupBy(0x08, sourceByte: 0x0E, sourceName: "String"));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full_key", resolved.CppName);
        Assert.Equal(new[] { "1", "0", "0" }, resolved.AppendLiterals);
    }

    // GroupBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>): the
    // signature the GroupBy branch decodes via LinqEncodedParamTypes.
    private static byte[] GroupByKeyOnlySignature(MetadataBuilder mdb, AssemblyReferenceHandle asmRef)
    {
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func2 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`2"));
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(2); // GenericParameterCount
        bb.WriteCompressedInteger(2); // param count
        bb.WriteByte(0x08);           // (simplified) return int32
        WriteGenericInst(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0); // VAR 0 (TSource)
        WriteGenericInst(bb, func2, 2);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1); // VAR 1 (TKey)
        return bb.ToArray();
    }

    private static void WriteGenericInst(BlobBuilder bb, TypeReferenceHandle def, int argc)
    {
        bb.WriteByte(0x15);
        bb.WriteByte(0x12); // CLASS marker
        bb.WriteCompressedInteger((MetadataTokens.GetRowNumber(def) << 2) | 1); // TypeRef coded
        bb.WriteCompressedInteger(argc);
    }

    // GroupBy<TSource,TKey> with TSource = System.Decimal (0x11 VALUETYPE) and
    // the requested TKey byte. `decimalKey` emits a System.Decimal TypeRef for
    // the key slot; `sourceByte`/`sourceName` override the source argument for
    // the reference-source LOUD probe.
    private static MemoryStream SynthesizeGroupBy(byte keyByte, bool decimalKey = false,
        byte sourceByte = 0x11, string sourceName = "Decimal")
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var sourceRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString(sourceName));
        var keyRef = decimalKey
            ? mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"))
            : default(TypeReferenceHandle);
        var signature = GroupByKeyOnlySignature(mdb, asmRef);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("GroupBy"),
            mdb.GetOrAddBlob(signature));

        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        if (sourceByte == 0x11)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(sourceRef) << 2) | 1);
        }
        else
        {
            spec.WriteByte(sourceByte);
        }
        if (keyByte == 0x11 && decimalKey)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(keyRef) << 2) | 1);
        }
        else
        {
            spec.WriteByte(keyByte);
        }
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(spec));
        return Serialize(mdb);
    }

    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
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
