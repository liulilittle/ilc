using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.MarshalMissing dispatch tests: prove the DestroyStructure / Copy
// elem-width / GetTypeFromProgID / CreateObject mappings added in r127 map to
// the correct runtime icalls (and pin the PNSE-stub COM surface).
public class MethodMapMarshalMissingTests
{
    //--------------------------------------------------------------------------
    // DestroyStructure(IntPtr, Type) -> icall_marshal_destroy_structure
    // (marshalext4.cpp:124 -- documented no-op, ILC has no COM/BSTR marshalling)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_DestroyStructure_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "DestroyStructure",
            Sig(Ret(0x01), 0x18, 0x12, 0x05)), "DestroyStructure");

        Assert.Equal("il2cpp::runtime::icall_marshal_destroy_structure", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "n", "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_DestroyStructure_ValuetypeTypeParam_MapsToIcall()
    {
        // Type param as valuetype (0x11 + TypeHandle) -- the Roslyn user form.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "DestroyStructure",
            Sig(Ret(0x01), 0x18, 0x11, 0x05)), "DestroyStructure");

        Assert.Equal("il2cpp::runtime::icall_marshal_destroy_structure", m.CppName);
        Assert.Equal(new[] { "n", "s" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Copy elem-width variants: the r127 mapping routes the existing Copy
    // signatures to the elem-width icalls (marshalext4.cpp:196/234) that
    // dispatch on arr->elemSize -- a drop-in replacement for the old
    // int32-stride icalls (runtime.cpp) that handles byte/short/char/int/
    // long/float/double/IntPtr arrays.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_Copy_ArrayToNative_MapsToElemWidthIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "Copy",
            Sig(Ret(0x01), 0x1D, 0x08, 0x08, 0x18, 0x08)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_marshal_copy_to_unmanaged_elem", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(4, m.ParamCount);
        Assert.Equal(new[] { "a", "i", "g", "i" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_Copy_NativeToArray_MapsToElemWidthIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "Copy",
            Sig(Ret(0x01), 0x18, 0x1D, 0x08, 0x08, 0x08)), "Copy");

        Assert.Equal("il2cpp::runtime::icall_marshal_copy_from_unmanaged_elem", m.CppName);
        Assert.False(m.IsInstance);
        Assert.False(m.ReturnsValue);
        Assert.Equal(4, m.ParamCount);
        Assert.Equal(new[] { "g", "a", "i", "i" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // GetTypeFromProgID(string) -> il2cpp::il2cpp_com_type_from_progid
    // CreateObject(string) -> il2cpp::runtime::icall_com_create_object
    // The C1 (A1) split retires the r127 PNSE stubs: net48
    // Marshal.GetTypeFromProgID == Type.GetTypeFromProgID (the COM Type VALUE
    // sentinel string), and Marshal.CreateObject CoCreateInstance's the
    // ProgID. Windows-only COM surface.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Marshal_GetTypeFromProgID_MapsToComTypeIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "GetTypeFromProgID",
            Sig(Ret(0x12, 0x05), 0x0E)), "GetTypeFromProgID");

        Assert.Equal("il2cpp::runtime::il2cpp_com_type_from_progid", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);   // the sentinel name string
        Assert.Equal("System.Type", m.ReturnTypeName);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_CreateObject_MapsToComCreateIcall()
    {
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "CreateObject",
            Sig(Ret(0x12, 0x05), 0x0E)), "CreateObject");

        Assert.Equal("il2cpp::runtime::icall_com_create_object", m.CppName);
        Assert.False(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_Marshal_CreateObject_NullProgId_FallsThrough()
    {
        // A non-string first param (e.g. object) falls through to the
        // generated-name path -- the stub only handles the string form.
        var m = ResolveMember(Synthesize("System.Runtime.InteropServices", "Marshal", "CreateObject",
            Sig(Ret(0x12, 0x05), 0x1C)), "CreateObject");

        Assert.StartsWith("System_Runtime_InteropServices_Marshal_CreateObject_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (same pipeline as
    // MethodMapTests.Marshal.cs)
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] Ret(byte ret, params byte[] trailing) =>
        new[] { ret }.Concat(trailing).ToArray();

    private static byte[] Sig(byte[] retBytes, params byte[] rawParams)
    {
        var count = 0;
        var i = 0;
        while (i < rawParams.Length)
        {
            count++;
            var b = rawParams[i];
            if (b is 0x11 or 0x12 or 0x40) i += 2;
            else if (b is 0x1D or 0x1F) i += 2;
            else i += 1;
        }
        var bytes = new byte[3 + retBytes.Length + rawParams.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        Array.Copy(retBytes, 0, bytes, 2, retBytes.Length);
        Array.Copy(rawParams, 0, bytes, 2 + retBytes.Length, rawParams.Length);
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
