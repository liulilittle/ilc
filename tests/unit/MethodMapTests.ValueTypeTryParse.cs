using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;
using Xunit;

// MethodMap.ValueType TryParse cast tests (numchain fix, 2026-08-07): the
// il2cpp_parse.h try_parse family takes Il2CppObject* for the text parameter
// (NOT Il2CppString* -- the icalls cast internally, il2cpp_parse.h:75-93),
// so the FIRST parameter cast must be "" (passthrough), never "s" (the
// reinterpret to Il2CppString* fails to compile against the object slot --
// the numchain build blocker, discover.md). The out slot keeps its typed
// address cast ("addr:*").
//
// Same synthetic-PE pipeline as MethodMapTests.Interlocked.
public class MethodMapTestsValueTypeTryParse
{
    //--------------------------------------------------------------------------
    // Int32.TryParse(string, out int) -- ps[0]=0x0E (string), ps[1]=0x08
    // (BYREF element i4 -> the out slot).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int32TryParse_TextArgPassthrough_OutAddrTyped()
    {
        var resolved = ResolveMember(Synthesize("System", "Int32", "TryParse",
            Sig(2, 0x02, 0x10, 0x0E, 0x10, 0x08)), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_int32", resolved.CppName);
        Assert.Equal(2, resolved.ParamCount);
        Assert.Equal(new[] { "", "addr:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Int64.TryParse(string, out long) -- BYREF element 0x0A.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_Int64TryParse_TextArgPassthrough_OutAddrTyped()
    {
        var resolved = ResolveMember(Synthesize("System", "Int64", "TryParse",
            Sig(2, 0x02, 0x10, 0x0E, 0x10, 0x0A)), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_int64", resolved.CppName);
        Assert.Equal(new[] { "", "addr:l" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Double.TryParse(string, out double) -- BYREF element 0x0D.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_DoubleTryParse_TextArgPassthrough_OutAddrTyped()
    {
        var resolved = ResolveMember(Synthesize("System", "Double", "TryParse",
            Sig(2, 0x02, 0x10, 0x0E, 0x10, 0x0D)), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_double", resolved.CppName);
        Assert.Equal(new[] { "", "addr:d" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Boolean.TryParse(string, out bool) -- BYREF element 0x02.
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_BooleanTryParse_TextArgPassthrough_OutAddrTyped()
    {
        var resolved = ResolveMember(Synthesize("System", "Boolean", "TryParse",
            Sig(2, 0x02, 0x10, 0x0E, 0x10, 0x02)), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_bool", resolved.CppName);
        Assert.Equal(new[] { "", "addr:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Byte.TryParse(string, out byte) -- BYREF element 0x05 (u1); the out
    // byte lives in an int32 slot (addr:i).
    //--------------------------------------------------------------------------
    [Fact]
    public void Resolve_ByteTryParse_TextArgPassthrough_OutAddrTyped()
    {
        var resolved = ResolveMember(Synthesize("System", "Byte", "TryParse",
            Sig(2, 0x02, 0x10, 0x0E, 0x10, 0x05)), "TryParse");

        Assert.Equal("il2cpp::runtime::icall_try_parse_byte", resolved.CppName);
        Assert.Equal(new[] { "", "addr:i" }, resolved.ParamTypeNames);
    }

    //--------------------------------------------------------------------------
    // Helpers -- synthetic PE construction (the Interlocked-test pipeline).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMember(MemoryStream pe, string memberName)
    {
        using var reader = new PEReader(pe);
        var md = reader.GetMetadataReader();
        var handle = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == memberName);
        return MethodRefResolver.Resolve(md, MetadataTokens.GetToken(handle), Array.Empty<TypeDef>());
    }

    // Signature blob: header 0x00, MANAGED param count, ret byte, raw params
    // (a BYREF param is 0x10 + its element byte; count is the MANAGED count).
    private static byte[] Sig(int count, byte ret, params byte[] paramBytes)
    {
        var bytes = new byte[3 + paramBytes.Length];
        bytes[0] = 0x00;
        bytes[1] = (byte)count;
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
