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

// MethodMap.Resources dispatch tests (RES-3, 2026-08-07): prove the
// System.Reflection.Assembly + System.Resources.ResourceManager domain
// (MethodMap.Resources.cs -> TryMapResources, il2cpp_resources.h) maps the
// embedded-manifest surface:
//
//   Assembly.GetManifestResourceStream(string) -> il2cpp_assembly_get_manifest_resource_stream
//   Assembly.GetManifestResourceNames()        -> il2cpp_assembly_get_manifest_resource_names
//   ResourceManager.GetString(string)          -> il2cpp_resource_manager_get_string
//
// RECEIVER DROP: the Assembly queries are INSTANCE members whose receiver the
// single-assembly runtime ignores -- the mapping drops it (the "drop" cast on
// args[0]), so GetManifestResourceStream(name) emits the 1-arg call and
// GetManifestResourceNames() the zero-arg call. ResourceManager.GetString
// passes the receiver through as the resourcesBlob slot (handle-gap forward
// ABI -- the ctor wiring's blob store is the prerequisite, MethodMap.Xml.cs
// precedent).
//
// WF-M2 (2026-08-07): the P/Invoke branch of MethodRefResolver translates
// the registered struct encodings ("V:System.Drawing.Rectangle" and the
// BYREF "B:V:" form) to the STABLE emitter cast codes ("rect"/"point"/"msg")
// so EmitIcallCallArgs can dispatch (the emitter cases are CG-FIX work).
// Unknown struct encodings keep their raw token.
public class MethodMapResourcesTests
{
    //--------------------------------------------------------------------------
    // Assembly.GetManifestResourceStream(string) -> the stream icall; the
    // Assembly receiver is DROPPED (single-assembly registry), the name
    // passes through uncast.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Assembly_GetManifestResourceStream_MapsToStreamIcall()
    {
        var m = ResolveMember("System.Reflection", "Assembly", "GetManifestResourceStream",
            Sig(Ret(0x0E), 0x0E));

        Assert.Equal("il2cpp::il2cpp_assembly_get_manifest_resource_stream", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // byte[]
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "drop", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Assembly.GetManifestResourceNames() -> the zero-arg names icall; the
    // receiver is DROPPED (the call takes no parameters).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Assembly_GetManifestResourceNames_MapsToNamesIcall()
    {
        var m = ResolveMember("System.Reflection", "Assembly", "GetManifestResourceNames",
            Sig(Ret(0x0E)));

        Assert.Equal("il2cpp::il2cpp_assembly_get_manifest_resource_names", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // string[]
        Assert.Equal(0, m.ParamCount);
        Assert.Equal(new[] { "drop" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Wrong-arity GetManifestResourceStream falls through to the
    // generated-name path (the resolver guards the shape).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Assembly_GetManifestResourceStream_WrongArity_FallsThrough()
    {
        var m = ResolveMember("System.Reflection", "Assembly", "GetManifestResourceStream",
            Sig(Ret(0x0E), 0x0E, 0x0E));

        Assert.StartsWith("System_Reflection_Assembly_GetManifestResourceStream_", m.CppName);
    }

    //--------------------------------------------------------------------------
    // ResourceManager.GetString(string) -> the minimal .resources parser icall;
    // the receiver passes through as the resourcesBlob slot (handle-gap ABI).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ResourceManager_GetString_MapsToStringIcall()
    {
        var m = ResolveMember("System.Resources", "ResourceManager", "GetString",
            Sig(Ret(0x0E), 0x0E));

        Assert.Equal("il2cpp::il2cpp_resource_manager_get_string", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // string
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // b23 + R2 (MethodMap.Resource.cs:75-79): the 2-arg GetString(string,
    // CultureInfo) maps to the culture-aware icall
    // il2cpp_resource_manager_get_string_culture -- the net48 fallback chain
    // walks the registry (the culture arg is the CultureInfo handle-gap name
    // string). Sync'd to the current wiring (resource batch b713eee1).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ResourceManager_GetString_TwoArgs_MapsToGetString()
    {
        var m = ResolveMember("System.Resources", "ResourceManager", "GetString",
            Sig(Ret(0x0E), 0x0E, 0x0E));

        Assert.Equal("il2cpp::il2cpp_resource_manager_get_string_culture", m.CppName);
        Assert.True(m.IsInstance);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // string
        Assert.Equal(2, m.ParamCount);
        Assert.Equal(new[] { "", "", "" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // WF-M2: the P/Invoke branch translates the registered struct encodings
    // to the stable emitter cast codes. A DllImport method definition with a
    // V:System.Drawing.Rectangle param -> ParamTypeNames ["rect"].
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_PInvokeStruct_RectangleParam_TranslatedToRectCast()
    {
        var m = ResolvePInvokeMethod("GetRect", PInvokeSig(0x08, ParamValueType()), "System.Drawing", "Rectangle");

        Assert.True(m.IsPInvoke);
        Assert.Equal("GetRect", m.CppName);
        Assert.Equal(new[] { "rect" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PInvokeStruct_PointParam_TranslatedToPointCast()
    {
        var m = ResolvePInvokeMethod("GetPt", PInvokeSig(0x08, ParamValueType()), "System.Drawing", "Point");

        Assert.Equal(new[] { "point" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_PInvokeStruct_MessageParam_TranslatedToMsgCast()
    {
        var m = ResolvePInvokeMethod("GetMsg", PInvokeSig(0x08, ParamValueType()), "System.Windows.Forms", "Message");

        Assert.Equal(new[] { "msg" }, m.ParamTypeNames);
    }

    // The BYREF struct form (out RECT): MethodSignatureCoder encodes it
    // "B:V:System.Drawing.Rectangle" -- translated to the same stable code.
    [Fact]
    public void Resolve_PInvokeStruct_ByRefRectangle_TranslatedToRectCast()
    {
        var m = ResolvePInvokeMethod("Peek", PInvokeSig(0x08, ParamByRefValueType()), "System.Drawing", "Rectangle");

        Assert.Equal(new[] { "rect" }, m.ParamTypeNames);
    }

    // An unregistered struct keeps its raw encoding (the emitter LOUD-rejects
    // unknown cast codes -- never a silent emit).
    [Fact]
    public void Resolve_PInvokeStruct_UnknownValueType_KeepsRawEncoding()
    {
        var m = ResolvePInvokeMethod("GetThing", PInvokeSig(0x08, ParamValueType()), "System.Drawing", "Color");

        Assert.Equal(new[] { "V:System.Drawing.Color" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (MethodMapTests.Marshal pipeline).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(string ns, string typeName, string memberName, byte[] signature)
    {
        using var reader = new PEReader(Synthesize(ns, typeName, memberName, signature));
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Resolve a synthetic DllImport MethodDefinition (the P/Invoke branch).
    private static ResolvedMethod ResolvePInvokeMethod(string methodName, byte[] signature,
        string structNs, string structName)
    {
        using var reader = new PEReader(SynthesizePInvoke(methodName, signature, structNs, structName));
        var md = reader.GetMetadataReader();
        var handle = md.MethodDefinitions.Single(h => md.GetString(md.GetMethodDefinition(h).Name) == methodName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    private static byte[] Ret(byte ret, params byte[] trailing) =>
        new[] { ret }.Concat(trailing).ToArray();

    // Default (non-generic, static-shape) MemberRef signature blob: header
    // 0x00, compressed param count, return bytes, then the raw param bytes.
    private static byte[] Sig(byte[] retBytes, params byte[] rawParams)
    {
        var count = 0;
        var i = 0;
        while (i < rawParams.Length)
        {
            count++;
            var b = rawParams[i];
            if (b is 0x11 or 0x12 or 0x40) i += 2; // + TypeHandle byte (TypeRef row 1 -> 0x05)
            else i += 1;
        }
        var bytes = new byte[3 + retBytes.Length + rawParams.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
        Array.Copy(retBytes, 0, bytes, 2, retBytes.Length);
        Array.Copy(rawParams, 0, bytes, 2 + retBytes.Length, rawParams.Length);
        return bytes;
    }

    // One VALUETYPE (0x11 + TypeDefOrRef coded index) param: the struct
    // TypeRef is ROW 1 in the synthetic assembly, so the coded index is
    // (1 << 2) | TypeRefTag(1) = 5.
    private static byte[] ParamValueType() => new byte[] { 0x11, 0x05 };

    // BYREF (0x10) + the VALUETYPE element.
    private static byte[] ParamByRefValueType() => new byte[] { 0x10, 0x11, 0x05 };

    // DllImport method-definition signature: header 0x00, one param, ret byte,
    // the raw param bytes.
    private static byte[] PInvokeSig(byte ret, byte[] paramBytes)
    {
        var bytes = new byte[3 + paramBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = 0x01; // exactly one param in these tests
        bytes[2] = ret;
        Array.Copy(paramBytes, 0, bytes, 3, paramBytes.Length);
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

    // A DllImport method definition: the struct TypeRef must be ROW 1 (the
    // signature's coded index 0x05 names it); System.Object follows at row 2
    // (the TypeDef base).
    private static MemoryStream SynthesizePInvoke(string methodName, byte[] signature,
        string structNs, string structName)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var structRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString(structNs), mdb.GetOrAddString(structName));
        Assert.Equal(1, MetadataTokens.GetRowNumber(structRef)); // must be the coded-index target
        var obj = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var method = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PinvokeImpl,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(methodName),
            mdb.GetOrAddBlob(signature),
            0, default(ParameterHandle));
        var moduleRef = mdb.AddModuleReference(mdb.GetOrAddString("native.dll"));
        mdb.AddMethodImport(method, MethodImportAttributes.None, mdb.GetOrAddString(methodName), moduleRef);
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Case"), mdb.GetOrAddString("Native"), obj, default(FieldDefinitionHandle), method);
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
