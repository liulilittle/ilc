using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.Xml dispatch tests: prove the System.Xml.XmlDocument domain
// (TryMapXml in MethodMap.Xml.cs) maps the basic-domain surface to the
// runtime xml_doc.h icalls. HANDLE-GAP MODE (XmlDocument simplifies to the
// root XmlNode* handle -- see MethodMap.Xml.cs):
//
//   XmlDocument.ctor()      -> il2cpp_object_ctor_nop (instance, void)
//   XmlDocument.LoadXml(s)  -> il2cpp_xml_load_xml (instance, void IL,
//                              ReturnsRef = root handle, ResultIsReceiver,
//                              casts ["", "s"], ReturnTypeName
//                              "System.Xml.XmlDocument")
//   XmlDocument.get_OuterXml-> il2cpp_xml_outer_xml (instance, returns string,
//                              receiver cast "node")
//   XmlDocument.SelectNodes/SelectSingleNode/GetAttribute/InnerText/InnerXml
//                          -> the xml-b23 5-member surface (managed-string
//                             XPath via "s"; "node" receiver cast)
//   XmlElement routes to the same TryMapXml (GetAttribute owner)
//   XmlDocument.<unwired>   -> LOUD "member not wired" (default case)
//
// NOTE (dispatch-line conflict): these tests need the
// "case "System.Xml.XmlDocument": return TryMapXml(...)" line in
// BclResolveCore (MethodRefResolver.cs) -- the line is PENDING because a
// parallel agent exclusively owns MethodRefResolver.cs. Until it lands, the
// ctor/LoadXml/OuterXml assertions FAIL with the generated-name fallback
// (dispatch miss), proving the pending state; the LOUD tests still pass
// (the throw happens inside TryMapXml only when the dispatch reaches it --
// NO: a dispatch miss means even SelectNodes falls to the generated-name
// path. So ALL Xml assertions are red until the dispatch line lands).
//
// Signature blobs are the raw bytes RawSignature consumes (0x01=void,
// 0x0E=string); CLASS (0x12) params carry the TypeDefOrRef coded index of
// the owner TypeRef after the 0x12 byte, exactly as RawSignature reads them.
public class MethodMapXmlTests
{
    //--------------------------------------------------------------------------
    // .ctor() -> il2cpp_object_ctor_nop (instance, void; newobj folds to
    // nullptr via BclAllocExpr -- no runtime XmlDocument descriptor)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_XmlDocument_Ctor_MapsToCtorNop()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", ".ctor", Sig(0x01)), ".ctor");

        Assert.Equal("il2cpp::runtime::il2cpp_object_ctor_nop", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.False(m.ReturnsValue);
    }

    //--------------------------------------------------------------------------
    // LoadXml(string) -> il2cpp_xml_load_xml: instance, void IL, root handle
    // result (ReturnsRef), ResultIsReceiver (root lands in the receiver
    // local), receiver dropped ("") + string as Il2CppString* ("s"), and the
    // tracked result type keeps get_OuterXml resolvable on the same local.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_XmlDocument_LoadXml_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "LoadXml", Sig(0x01, 0x0E)), "LoadXml");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_load_xml", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue);
        Assert.True(m.ReturnsRef); // the root handle rides the ref slot
        Assert.True(m.ResultIsReceiver); // doc = load_xml(s)
        Assert.Equal(new[] { "", "s" }, m.ParamTypeNames);
        Assert.Equal("System.Xml.XmlDocument", m.ReturnTypeName);
    }

    //--------------------------------------------------------------------------
    // get_OuterXml() -> il2cpp_xml_outer_xml: instance, returns string,
    // receiver cast "node" (Il2CppObject* handle slot -> XmlNode*)
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_XmlDocument_OuterXml_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "get_OuterXml", Sig(0x0E)), "get_OuterXml");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_outer_xml", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(new[] { "node" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // SelectNodes/SelectSingleNode/GetAttribute/InnerText/InnerXml -- xml-b23
    // 5-member closure: managed-string XPath (the "s" cast), node receiver
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_XmlDocument_SelectNodes_MapsToIcall()
    {
        // XmlNodeList SelectNodes(string xpath): ret object (0x1C), one
        // string param.
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "SelectNodes", Sig(0x1C, 0x0E)), "SelectNodes");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_select_nodes", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.False(m.ReturnsValue); // void-IL wrapper; the array result
        Assert.True(m.ReturnsRef);    // rides the ref slot
        Assert.Equal(new[] { "node", "s" }, m.ParamTypeNames);
        Assert.Equal("System.Xml.XmlNodeList", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_XmlDocument_SelectSingleNode_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "SelectSingleNode", Sig(0x1C, 0x0E)), "SelectSingleNode");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_select_single_node", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.Equal(new[] { "node", "s" }, m.ParamTypeNames);
        Assert.Equal("System.Xml.XmlNode", m.ReturnTypeName);
    }

    [Fact]
    public void Resolve_XmlElement_GetAttribute_MapsToIcall()
    {
        // XmlElement.GetAttribute(string) -> string; owner routes to TryMapXml.
        var m = ResolveMember(Synthesize("System.Xml", "XmlElement", "GetAttribute", Sig(0x0E, 0x0E)), "GetAttribute");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_get_attribute_s", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(1, m.ParamCount);
        Assert.True(m.ReturnsValue);
        Assert.True(m.ReturnsRef);
        Assert.Equal(new[] { "node", "s" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_XmlDocument_InnerText_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "get_InnerText", Sig(0x0E)), "get_InnerText");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_get_inner_text", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsRef);
        Assert.Equal(new[] { "node" }, m.ParamTypeNames);
    }

    [Fact]
    public void Resolve_XmlDocument_InnerXml_MapsToIcall()
    {
        var m = ResolveMember(Synthesize("System.Xml", "XmlDocument", "get_InnerXml", Sig(0x0E)), "get_InnerXml");

        Assert.Equal("il2cpp::runtime::il2cpp_xml_get_inner_xml", m.CppName);
        Assert.True(m.IsInstance);
        Assert.Equal(0, m.ParamCount);
        Assert.True(m.ReturnsRef);
        Assert.Equal(new[] { "node" }, m.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Unwired members hit the default -> LOUD "member not wired"
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_XmlDocument_GetOuterXml_WithArg_ThrowsLoudNotWired()
    {
        // get_InnerXml/get_InnerText/get_OuterXml are wired; a member outside
        // the covered surface still hits the default LOUD.
        var ex = Assert.Throws<NotSupportedException>(() =>
            ResolveMember(Synthesize("System.Xml", "XmlDocument", "Save", Sig(0x01, 0x0E)), "Save"));

        Assert.StartsWith("System.Xml.XmlDocument::Save: ", ex.Message);
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
    // 0x1C = object (XmlNodeList return shape for SelectNodes).
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
