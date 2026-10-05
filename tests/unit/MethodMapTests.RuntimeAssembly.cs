using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.RuntimeAssembly dispatch tests: prove the two QCall-shape stubs
// (TryMapRuntimeAssembly in MethodMap.RuntimeAssembly.cs) resolve to their
// unsupported icalls:
//
//   LoadModule (7-arg QCall) -> icall_assembly_loadmodule_unsupported
//     (all 7 args dropped; statement position -- callers do not consume a return)
//   GetVersion (5-arg QCall) -> icall_assembly_get_version_unsupported
//     (all 5 args dropped; statement position)
//
// Signature blobs: header 0x00 (DEFAULT), param count, return byte 0x01 (void),
// then one 0x1C (object) byte per parameter. Only the parameter COUNT drives
// TryMapRuntimeAssembly; the element bytes just need to parse.
public class MethodMapRuntimeAssemblyTests
{
    [Fact]
    public void Resolve_RuntimeAssembly_LoadModule7_MapsToUnsupportedIcall()
    {
        var m = ResolveMember(Synthesize("LoadModule", SigVoid(7)), "LoadModule");

        Assert.Equal("il2cpp::runtime::icall_assembly_loadmodule_unsupported", m.CppName);
        Assert.Equal(7, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_RuntimeAssembly_GetVersion5_MapsToUnsupportedIcall()
    {
        var m = ResolveMember(Synthesize("GetVersion", SigVoid(5)), "GetVersion");

        Assert.Equal("il2cpp::runtime::icall_assembly_get_version_unsupported", m.CppName);
        Assert.Equal(5, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_RuntimeAssembly_LoadModule_WrongArgCount_FallsBackToGenerated()
    {
        // When arg count doesn't match the QCall shape (7), TryMapRuntimeAssembly
        // returns null and the resolver falls back to the generated-name path
        // (IsFictionalBclFallback=true) -- not a null result.
        var m = ResolveMember(Synthesize("LoadModule", SigVoid(4)), "LoadModule");
        Assert.NotNull(m);
        Assert.True(m.IsFictionalBclFallback);
    }

    [Fact]
    public void Resolve_RuntimeAssembly_GetVersion_WrongArgCount_FallsBackToGenerated()
    {
        var m = ResolveMember(Synthesize("GetVersion", SigVoid(3)), "GetVersion");
        Assert.NotNull(m);
        Assert.True(m.IsFictionalBclFallback);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Timers.cs, owner System.Reflection.RuntimeAssembly).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] SigVoid(int paramCount)
    {
        var bytes = new byte[3 + paramCount];
        bytes[0] = 0x00;
        bytes[1] = (byte)paramCount;
        bytes[2] = 0x01; // void return
        for (int i = 0; i < paramCount; i++)
            bytes[3 + i] = 0x1C; // object param
        return bytes;
    }

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Reflection"),
            mdb.GetOrAddString("RuntimeAssembly"));
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
