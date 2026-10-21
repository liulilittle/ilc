using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// B4-1 (zero-runtime-change batch):
//   * GenericArgFullNames must consume the MethodSpec blob EXACTLY (no leaked
//     payload, no missing bytes) and stay slot-parallel with genArgs before
//     any consumer trusts the name table.
//   * GroupBy keys: the runtime bucket is int32, so every int32-slot integer
//     key family (bool/char/sbyte/byte/short/ushort/int/uint/long) resolves;
//     decimal/DateTime (0x11) stay LOUD in this batch.
//   * Aggregate TAccumulate=ulong (0x0B) rides the existing i8 fold.
public class MethodMapTestsB4
{
    // Real Select MethodSpec shape: GENERICINST(0x0A) + argc=2 + TSource +
    // TResult. Select<int,decimal> -> [0x08,0x11] with names ["?","System.Decimal"];
    // Select<int,int> -> [0x08,0x08] and NO nameful slot (null).
    [Fact]
    public void GenericArgFullNames_SelectIntDecimal_SecondSlotNamed()
    {
        using var reader = new PEReader(SynthesizeSelect(0x11));
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));

        var names = InvokeGenericArgFullNames(md, ms.Signature, new byte[] { 0x08, 0x11 });

        Assert.NotNull(names);
        Assert.Equal(2, names!.Length);
        Assert.Equal("?", names[0]);
        Assert.Equal("System.Decimal", names[1]);
    }

    [Fact]
    public void GenericArgFullNames_SelectIntInt_NoNamefulSlotReturnsNull()
    {
        using var reader = new PEReader(SynthesizeSelect(0x08));
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));

        Assert.Null(InvokeGenericArgFullNames(md, ms.Signature, new byte[] { 0x08, 0x08 }));
    }

    // RED pre-fix: the name table used to ignore leftover bytes, so a drifted
    // MethodSpec (payload leak) silently returned names instead of failing.
    [Fact]
    public void GenericArgFullNames_TrailingByte_Throws()
    {
        using var reader = new PEReader(SynthesizeSelect(0x08, trailing: 0x00));
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));

        Assert.IsType<NotSupportedException>(
            InvokeCatches(md, ms.Signature, new byte[] { 0x08, 0x08 }));
    }

    // RED pre-fix: a slot-count mismatch used to return null; the contract is
    // "same slot count", enforced as a hard failure.
    [Fact]
    public void GenericArgFullNames_SlotCountMismatch_Throws()
    {
        using var reader = new PEReader(SynthesizeSelect(0x08));
        var md = reader.GetMetadataReader();
        var ms = md.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(1));

        Assert.IsType<NotSupportedException>(InvokeCatches(md, ms.Signature, new byte[] { 0x08 }));
    }

    // The runtime groups by an int32 key bucket; every integer key family that
    // rides that slot resolves to the full entry (0x0A = long is the int64
    // member of the same integer family; 0x0B ulong and 0x11 value types do
    // NOT belong to this batch).
    [Theory]
    [InlineData(0x02)] // bool
    [InlineData(0x03)] // char
    [InlineData(0x04)] // sbyte
    [InlineData(0x05)] // byte
    [InlineData(0x06)] // short
    [InlineData(0x07)] // ushort
    [InlineData(0x08)] // int
    [InlineData(0x09)] // uint
    [InlineData(0x0A)] // long
    public void GroupBy_IntegerKeyFamilies_MapToFullEntry(byte keyByte)
    {
        var resolved = ResolveMethodSpec(SynthesizeMethod("GroupBy", GroupByKeyOnlySignature,
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, keyByte }));

        Assert.Equal("il2cpp::il2cpp_linq_group_by_full", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "0", "0" }, resolved.AppendLiterals);
    }

    [Fact]
    public void GroupBy_DecimalKey_StaysLoud()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ResolveMethodSpec(SynthesizeMethod("GroupBy",
            GroupByKeyOnlySignature,
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x11, 0x09 })));

        Assert.Contains("int32", ex.Message);
    }

    // RED pre-fix: ulong TAccumulate hit the empty "unsupported accumulator
    // family" arm; the i8 fold (int64 slot) already exists at runtime.
    [Fact]
    public void Aggregate_UlongAccumulator_MapsToI8()
    {
        var resolved = ResolveMethodSpec(SynthesizeMethod("Aggregate", AggregateSeedSignature,
            genericSignature: new byte[] { 0x0A, 0x02, 0x08, 0x0B }));

        Assert.Equal("il2cpp::il2cpp_linq_aggregate_seed_i8", resolved.CppName);
        Assert.Equal("l", resolved.ResultKind);
        Assert.Equal(new[] { "a", "l", "" }, resolved.ParamTypeNames);
    }

    // GroupBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>): the
    // signature the GroupBy branch DECODES via LinqEncodedParamTypes, so the
    // TypeRefs must exist for real (coded handles computed from their rows).
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

    // Aggregate<TSource,TAccumulate>(IEnumerable<TSource>, TAccumulate,
    // Func<TAccumulate,TSource,TAccumulate>).
    private static byte[] AggregateSeedSignature(MetadataBuilder mdb, AssemblyReferenceHandle asmRef)
    {
        var iEnum = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Collections.Generic"),
            mdb.GetOrAddString("IEnumerable`1"));
        var func3 = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Func`3"));
        var bb = new BlobBuilder();
        bb.WriteByte(0x10);
        bb.WriteCompressedInteger(2);
        bb.WriteCompressedInteger(3);
        bb.WriteByte(0x08);
        WriteGenericInst(bb, iEnum, 1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x0B); // TAccumulate = ulong
        WriteGenericInst(bb, func3, 3);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(0);
        bb.WriteByte(0x13);
        bb.WriteCompressedInteger(1);
        return bb.ToArray();
    }

    private static void WriteGenericInst(BlobBuilder bb, TypeReferenceHandle def, int argc)
    {
        bb.WriteByte(0x15);
        bb.WriteByte(0x12); // CLASS marker (the shape the resolver's parsers walk)
        bb.WriteCompressedInteger((MetadataTokens.GetRowNumber(def) << 2) | 1); // TypeRef coded
        bb.WriteCompressedInteger(argc);
    }

    private static ResolvedMethod ResolveMethodSpec(MemoryStream pe)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var token = MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1));
        return MethodRefResolver.Resolve(md, token, Array.Empty<TypeDef>());
    }

    private static MemoryStream SynthesizeMethod(string methodName,
        Func<MetadataBuilder, AssemblyReferenceHandle, byte[]> signatureBuilder, byte[] genericSignature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var signature = signatureBuilder(mdb, asmRef);
        var memberRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(signature));
        mdb.AddMethodSpecification(memberRef, mdb.GetOrAddBlob(genericSignature));
        return Serialize(mdb);
    }

    // Select-shaped PE carrying a System.Decimal TypeReference so the 0x11
    // generic argument resolves to "System.Decimal". `second` is the SECOND
    // MethodSpec argument byte (0x08 int32 / 0x11 decimal); for 0x11 the
    // TypeDefOrRef coded index follows.
    private static MemoryStream SynthesizeSelect(byte second, byte? trailing = null)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var enumerableRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        var decimalRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"),
            mdb.GetOrAddString("Decimal"));
        var selectRef = mdb.AddMemberReference(enumerableRef, mdb.GetOrAddString("Select"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00 }));
        var spec = new BlobBuilder();
        spec.WriteByte(0x0A);
        spec.WriteCompressedInteger(2);
        spec.WriteByte(0x08);
        if (second == 0x11)
        {
            spec.WriteByte(0x11);
            spec.WriteCompressedInteger((MetadataTokens.GetRowNumber(decimalRef) << 2) | 1);
        }
        else
        {
            spec.WriteByte(second);
        }
        if (trailing is { } t) spec.WriteByte(t);
        mdb.AddMethodSpecification(selectRef, mdb.GetOrAddBlob(spec));
        return Serialize(mdb);
    }

    private static string[]? InvokeGenericArgFullNames(MetadataReader md, BlobHandle sigHandle, byte[] genArgs)
    {
        var mi = typeof(MethodRefResolver).GetMethod("GenericArgFullNames",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (string[]?)mi!.Invoke(null, new object[] { md, sigHandle, genArgs });
    }

    // Reflection wraps the helper's own exception in TargetInvocationException.
    private static Exception? InvokeCatches(MetadataReader md, BlobHandle sigHandle, byte[] genArgs)
    {
        try
        {
            _ = InvokeGenericArgFullNames(md, sigHandle, genArgs);
            return null;
        }
        catch (TargetInvocationException tie)
        {
            return tie.InnerException;
        }
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
