using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.WeakReference dispatch tests: prove the System.WeakReference domain
// (TryMapWeakReference in MethodMap.WeakReference.cs). System.WeakReference is
// an OBJECT MODEL domain (W-1 weakrefcase work order): the runtime owns the
// System_WeakReference_class descriptor (Il2CppWeakRefObject: header + int32
// handle into the gcext weak-target handle table, il2cpp_gc_ext.h) and the
// object icalls, so every mapped member resolves to its icall:
//
//   WeakReference.ctor(object)  -> icall_weakref_ctor (instance, void)
//   WeakReference.get_IsAlive   -> icall_weakref_get_is_alive (instance, bool)
//   WeakReference.get_Target    -> icall_weakref_get_target (instance, object)
//   WeakReference.set_Target    -> icall_weakref_set_target (instance, void)
//   WeakReference.ctor(object,bool) -> throws (trackResurrection not wired)
//   WeakReference.get_TrackResurrection -> throws (member not wired)
//
// Signature blobs are the raw bytes RawSignature consumes (0x08=i4, 0x0E=
// string, 0x01=void, 0x02=bool, 0x1C=object); CLASS (0x12) return/params carry
// the TypeDefOrRef coded index of the owner TypeRef (row 1 -> 0x05) after the
// 0x12 byte, exactly as RawSignature reads them.
public class MethodMapWeakReferenceTests
{
    //--------------------------------------------------------------------------
    // .ctor(object) -> icall_weakref_ctor (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_CtorObject_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "WeakReference", ".ctor", Sig(0x01, 0x1C)), ".ctor");

        Assert.Equal("il2cpp::runtime::icall_weakref_ctor", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // get_IsAlive -> icall_weakref_get_is_alive (instance, bool)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_IsAlive_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "WeakReference", "get_IsAlive", Sig(0x02)), "get_IsAlive");

        Assert.Equal("il2cpp::runtime::icall_weakref_get_is_alive", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // get_Target -> icall_weakref_get_target (instance, returns object)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_GetTarget_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "WeakReference", "get_Target", Sig(0x1C)), "get_Target");

        Assert.Equal("il2cpp::runtime::icall_weakref_get_target", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // set_Target(object) -> icall_weakref_set_target (instance, void)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_SetTarget_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System", "WeakReference", "set_Target", Sig(0x01, 0x1C)), "set_Target");

        Assert.Equal("il2cpp::runtime::icall_weakref_set_target", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.False(m.ReturnsRef);
    }

    //--------------------------------------------------------------------------
    // ctor(object, bool trackResurrection) -> LOUD not wired (net48 has the
    // overload; the object model wires only the 1-arg ctor -- the handle table
    // does not track resurrection)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_CtorObjectBool_ThrowsLoudNotWired()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "WeakReference", ".ctor", Sig(0x01, 0x1C, 0x02)), ".ctor"));

        Assert.StartsWith("System.WeakReference::.ctor: ", ex.Message);
        Assert.Contains("not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // get_TrackResurrection -> LOUD not wired
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_WeakReference_TrackResurrection_ThrowsLoudNotWired()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System", "WeakReference", "get_TrackResurrection", Sig(0x02)), "get_TrackResurrection"));

        Assert.StartsWith("System.WeakReference::get_TrackResurrection: ", ex.Message);
        Assert.Contains("member not wired", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.VersionUri.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00 (DEFAULT, non-generic), param count, return
    // byte, then one byte per parameter (the raw bytes RawSignature decodes).
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
