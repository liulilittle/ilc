using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Calendar dispatch tests: prove the
// System.Globalization.GregorianCalendar domain (TryMapCalendar in
// MethodMap.Calendar.cs) routes ToDateTime to the runtime composition icall
// (icall_calendar_to_datetime, calendar.cpp). The GregorianCalendar is
// stateless, so the receiver is ignored; the 7 int32 components cross as
// plain int32 slots.
//
// Signature blobs are the raw bytes RawSignature consumes (0x08 = i4,
// 0x11 = valuetype + TypeDefOrRef coded index, 0x0E = string).
public class MethodMapCalendarTests
{
    [Fact]
    public void Resolve_Calendar_ToDateTime_MapsToCompositionIcall()
    {
        // instance DateTime ToDateTime(int, int, int, int, int, int, int):
        // ret VALUETYPE (0x11) + the DateTime TypeRef coded index (0x05),
        // 7 int32 params.
        var resolved = ResolveMember(
            Synthesize("System.Globalization", "GregorianCalendar", "ToDateTime",
                new byte[] { 0x00, 0x07, 0x11, 0x05, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08, 0x08 }),
            "ToDateTime");

        Assert.Equal("il2cpp::runtime::icall_calendar_to_datetime", resolved.CppName);
        Assert.True(resolved.IsInstance);
        Assert.True(resolved.ReturnsValue);
        Assert.Equal(7, resolved.ParamCount);
        Assert.Equal(new[] { "", "i", "i", "i", "i", "i", "i", "i" }, resolved.ParamTypeNames);
        Assert.Equal("dt", resolved.ResultKind);
    }

    [Fact]
    public void Resolve_Calendar_ToDateTime_WrongArity_FallsBackToGeneratedName()
    {
        // A non-7-int shape is not the net48 overload -> null (the
        // generated-name fallback keeps it loud at link).
        var resolved = ResolveMember(
            Synthesize("System.Globalization", "GregorianCalendar", "ToDateTime",
                new byte[] { 0x00, 0x02, 0x11, 0x05, 0x08, 0x08 }),
            "ToDateTime");

        // Resolve wraps the null into the fictional generated-name fallback
        // (the loud C++ link error).
        Assert.NotNull(resolved);
        Assert.True(resolved!.IsFictionalBclFallback);
        Assert.StartsWith("System_Globalization_GregorianCalendar_ToDateTime_", resolved.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as MethodMapTests.Guid)
    //--------------------------------------------------------------------------
    private static ResolvedMethod? ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static MemoryStream Synthesize(string ns, string typeName, string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        // DateTime TypeRef FIRST (row 1 -> coded 0x05, matching the blob's
        // VALUETYPE return), then the owner.
        mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("DateTime"));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(ns), mdb.GetOrAddString(typeName));
        mdb.AddMemberReference(typeRef, mdb.GetOrAddString(memberName), mdb.GetOrAddBlob(signature));
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
