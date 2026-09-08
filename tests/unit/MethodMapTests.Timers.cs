using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Timers dispatch tests: prove the eight restored System.Timers.Timer
// members (TryMapTimers in MethodMap.Timers.cs) resolve to their runtime icalls:
//
//   Dispose(bool)                 -> icall_timers_dispose (drop the flag)
//   get_Interval                  -> icall_timers_get_interval (double)
//   get_AutoReset                 -> icall_timers_get_auto_reset (bool)
//   get_Enabled                   -> icall_timers_get_enabled (bool)
//   set_Enabled(bool)             -> icall_timers_set_enabled
//   remove_Elapsed(handler)       -> icall_timers_remove_elapsed
//   Close()                       -> icall_timers_close
//   get_SynchronizingObject       -> icall_timers_get_sync (object ref)
//   set_SynchronizingObject(obj)  -> icall_timers_set_sync
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x02=bool, 0x0D=double, 0x1C=object); only the parameter COUNT drives
// TryMapTimers, the element bytes just need to parse.
public class MethodMapTimersTests
{
    [Fact]
    public void Resolve_Timers_DisposeBool_MapsToDisposeIcall()
    {
        var m = ResolveMember(Synthesize("Dispose", Sig(0x01, 0x02)), "Dispose");

        Assert.Equal("il2cpp::runtime::icall_timers_dispose", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_GetInterval_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("get_Interval", Sig(0x0D)), "get_Interval");

        Assert.Equal("il2cpp::runtime::icall_timers_get_interval", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Timers_GetAutoReset_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("get_AutoReset", Sig(0x02)), "get_AutoReset");

        Assert.Equal("il2cpp::runtime::icall_timers_get_auto_reset", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_GetEnabled_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("get_Enabled", Sig(0x02)), "get_Enabled");

        Assert.Equal("il2cpp::runtime::icall_timers_get_enabled", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_SetEnabled_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("set_Enabled", Sig(0x01, 0x02)), "set_Enabled");

        Assert.Equal("il2cpp::runtime::icall_timers_set_enabled", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_RemoveElapsed_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("remove_Elapsed", Sig(0x01, 0x1C)), "remove_Elapsed");

        Assert.Equal("il2cpp::runtime::icall_timers_remove_elapsed", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_Close_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("Close", Sig(0x01)), "Close");

        Assert.Equal("il2cpp::runtime::icall_timers_close", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    [Fact]
    public void Resolve_Timers_GetSync_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("get_SynchronizingObject", Sig(0x1C)), "get_SynchronizingObject");

        Assert.Equal("il2cpp::runtime::icall_timers_get_sync", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    [Fact]
    public void Resolve_Timers_SetSync_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("set_SynchronizingObject", Sig(0x01, 0x1C)), "set_SynchronizingObject");

        Assert.Equal("il2cpp::runtime::icall_timers_set_sync", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.WeakReference.cs, owner System.Timers.Timer).
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

    private static MemoryStream Synthesize(string memberName, byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var typeRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System.Timers"), mdb.GetOrAddString("Timer"));
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
