using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Delegate dispatch tests: prove the System.Delegate multicast
// surface (il2cpp_delegate_ext.h) maps to the runtime icalls.
//
//   Remove(Delegate, Delegate)    -> icall_delegate_remove     (LAST occurrence)
//   RemoveAll(Delegate, Delegate) -> icall_delegate_remove_all (EVERY occurrence)
//   Combine / op_Addition        -> icall_delegate_combine
//   op_Equality / op_Inequality  -> icall_delegate_equals
//
// RemoveAll was a LOUD rejection ("the runtime has only Remove"); the runtime
// now carries icall_delegate_remove_all (net48 removes EVERY occurrence of the
// value's invocation list, not just the last).
//
// The PEs are synthesized with System.Reflection.Metadata's own writer (the
// pipeline shared with MethodMapTests.Net). Raw signature bytes: header 0x00,
// param count, return byte, then one byte per parameter (0x1C = object).
public class MethodMapDelegateTests
{
    [Fact]
    public void Resolve_Delegate_RemoveAll_MapsToRemoveAllIcall()
    {
        var m = ResolveMember(Synthesize("System", "Delegate", "RemoveAll", Sig(0x1C, 0x1C, 0x1C)), "RemoveAll");

        Assert.Equal("il2cpp::runtime::icall_delegate_remove_all", m.CppName);
        Assert.False(m.IsInstance);
        Assert.Equal(2, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Delegate_Remove_MapsToRemoveIcall()
    {
        var m = ResolveMember(Synthesize("System", "Delegate", "Remove", Sig(0x1C, 0x1C, 0x1C)), "Remove");

        Assert.Equal("il2cpp::runtime::icall_delegate_remove", m.CppName);
        Assert.Equal(2, m.ParamCount);
    }

    [Fact]
    public void Resolve_Delegate_Combine_MapsToCombineIcall()
    {
        var m = ResolveMember(Synthesize("System", "Delegate", "Combine", Sig(0x1C, 0x1C, 0x1C)), "Combine");

        Assert.Equal("il2cpp::runtime::icall_delegate_combine", m.CppName);
        Assert.Equal(2, m.ParamCount);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as MethodMapTests.Net)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] Sig(byte ret, params byte[] ps)
    {
        var bytes = new byte[3 + ps.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)ps.Length;
        bytes[2] = ret;
        Array.Copy(ps, 0, bytes, 3, ps.Length);
        return bytes;
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
