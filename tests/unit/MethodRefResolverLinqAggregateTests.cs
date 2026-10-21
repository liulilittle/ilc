using System;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Xunit;

// B1: resolver-level tests for the net48 CONCRETE aggregate signature decoder
// (LinqAggregateElementByte). The decoder must
//   * skip the GENERIC signature header (0x10 + GenericParamCount) -- net48's
//     Sum<TSource>/Min<TSource>/Max<TSource>/Average<TSource> selectors are
//     generic method DEFINITIONS when they arrive as plain MemberRefs;
//   * consume every trailing byte per ECMA-335 (VAR/MVAR index, GenericInst
//     marker+handle+args, ...) so the next param cannot drift;
//   * return the VAR/MVAR placeholder codes (0x13/0x1E) instead of pretending
//     they are a concrete element family;
//   * end EXACTLY at the blob end (surplus bytes -> no hint).
public class MethodRefResolverLinqAggregateTests
{
    // 0x10 = GENERIC header, 0x01 = GenericParamCount, 0x01 = param count,
    // 0x01 = void ret, then the first param:
    //   0x15 GenericInst, 0x11 VALUETYPE, 0x0B TypeDefOrRef, 0x01 arg count,
    //   0x08 int32 element.
    [Fact]
    public void GenericHeader_ValueTypeElement_ReturnsInt32_AndEndsAtBlobEnd()
    {
        AssertDecode(
            new byte[] { 0x10, 0x01, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01, 0x08 },
            new byte[] { 0x08 });
    }

    // Same shape without the generic header -- the pre-existing concrete
    // (non-generic) overload path must keep working.
    [Fact]
    public void NonGenericHeader_ValueTypeElement_ReturnsInt32_AndEndsAtBlobEnd()
    {
        AssertDecode(
            new byte[] { 0x00, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01, 0x08 },
            new byte[] { 0x08 });
    }

    // VAR (0x13) element: the index 0x00 is payload and MUST be consumed; the
    // returned code stays the generic-placeholder 0x13.
    [Fact]
    public void GenericHeader_VarElement_ReturnsPlaceholderAndConsumesIndex()
    {
        AssertDecode(
            new byte[] { 0x10, 0x01, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01, 0x13, 0x00 },
            new byte[] { 0x13 });
    }

    // MVAR (0x1E) element: same rule -- consume the index, return 0x1E.
    [Fact]
    public void GenericHeader_MVarElement_ReturnsPlaceholderAndConsumesIndex()
    {
        AssertDecode(
            new byte[] { 0x10, 0x01, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01, 0x1E, 0x00 },
            new byte[] { 0x1E });
    }

    // GENERICINST (0x15) element: a nested instantiation carries marker +
    // TypeDefOrRef + arg count + args; all of it must be consumed.
    [Fact]
    public void GenericHeader_GenericInstElement_ConsumesNestedPayload()
    {
        AssertDecode(
            new byte[]
            {
                0x10, 0x01, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01,
                0x15, 0x11, 0x0C, 0x01, 0x08,
            },
            new byte[] { 0x15 });
    }

    // Selector form: two parameters. The decoder must walk the second
    // parameter too, otherwise the "exactly at blob end" contract fails.
    [Fact]
    public void GenericHeader_SelectorForm_ConsumesBothParameters()
    {
        AssertDecode(
            new byte[]
            {
                0x10, 0x01, 0x02, 0x01,
                0x15, 0x11, 0x0B, 0x01, 0x08,             // param 1: IEnumerable<int32>
                0x15, 0x12, 0x0C, 0x02, 0x13, 0x00, 0x08, // param 2: Func<TSource,int32>
            },
            new byte[] { 0x08 });
    }

    // Surplus trailing bytes: the decoder must refuse (no hint) rather than
    // return a value decoded from a drifted cursor.
    [Fact]
    public void TrailingSurplusByte_ReturnsNoHint()
    {
        AssertDecode(
            new byte[] { 0x00, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01, 0x08, 0xAA },
            expected: null,
            expectedRemaining: 1);
    }

    // Truncated payload (generic arg count says 1 but the element is missing):
    // must fail loudly instead of returning a bogus hint.
    [Fact]
    public void TruncatedElement_Throws()
    {
        using var pe = new PEReader(Synthesize(
            new byte[] { 0x10, 0x01, 0x01, 0x01, 0x15, 0x11, 0x0B, 0x01 }));
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single();
        var sig = md.GetBlobReader(md.GetMemberReference(handle).Signature);

        Assert.ThrowsAny<Exception>(
            () => MethodRefResolver.DecodeLinqAggregateElementByte(md, ref sig));
    }

    private static void AssertDecode(byte[] blob, byte[]? expected, int expectedRemaining = 0)
    {
        using var pe = new PEReader(Synthesize(blob));
        var md = pe.GetMetadataReader();
        var handle = md.MemberReferences.Single();
        var sig = md.GetBlobReader(md.GetMemberReference(handle).Signature);

        var got = MethodRefResolver.DecodeLinqAggregateElementByte(md, ref sig);

        Assert.Equal(expected, got);
        Assert.Equal(expectedRemaining, sig.RemainingBytes);
    }

    // One MemberReference whose signature blob is exactly `blob`.
    private static MemoryStream Synthesize(byte[] blob)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Linq"),
            mdb.GetOrAddString("Enumerable"));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString("Average"), mdb.GetOrAddBlob(blob));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var outBlob = new BlobBuilder();
        peBuilder.Serialize(outBlob);
        return new MemoryStream(outBlob.ToArray());
    }
}
