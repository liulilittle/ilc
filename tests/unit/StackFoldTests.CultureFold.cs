using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// Culture-fold tests: the callvirt path MUST apply the same marker folds as
// the call path. culturecase (NumberFormatInfo.CurrentInfo.NumberDecimal-
// Separator == ".") emits `call get_CurrentInfo` (-> NullMarker null) then
// `callvirt get_NumberDecimalSeparator` on the folded null receiver -- the
// resolver maps the getter to ResolvedMethod.LiteralMarker + "." and the
// callvirt branch (StackFold.cs:1442-1544) previously ONLY folded
// IdentityMarker, so the ::lit:: marker leaked into the emitted C++
// (`::lit::.(nullptr)` -- uncompilable). The fix adds the NullMarker /
// LiteralMarker folds to the callvirt branch (mirrors the call branch at
// StackFold.cs:1627-1640).
//------------------------------------------------------------------------------
public class StackFoldTestsCultureFold
{
    private enum BodyKind
    {
        CallvirtLiteral,      // ldnull; callvirt NumberFormatInfo::get_NumberDecimalSeparator; ret
        CallvirtNullMarker,   // call NumberFormatInfo::get_CurrentInfo; callvirt get_NumberDecimalSeparator; ret
    }

    [Fact]
    public void Fold_CallvirtLiteralMarker_FoldsToStringConst()
    {
        using var pe = Synthesize(BodyKind.CallvirtLiteral);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(".", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_CallvirtAfterNullMarkerReceiver_FoldsToStringConst()
    {
        // The full culturecase shape: get_CurrentInfo folds to a NullExpr
        // (NullMarker), then get_NumberDecimalSeparator is a callvirt on it.
        using var pe = Synthesize(BodyKind.CallvirtNullMarker);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(".", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // Harness: synthesized assembly -- NumberFormatInfo.get_NumberDecimal-
    // Separator MemberRef (instance getter, string return), Main calls it.
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var nfiRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System.Globalization"), mdb.GetOrAddString("NumberFormatInfo"));

        // NumberFormatInfo::get_NumberDecimalSeparator() instance string.
        var getDecSepSig = new BlobBuilder();
        getDecSepSig.WriteByte(0x00); // no generic params
        getDecSepSig.WriteCompressedInteger(0); // 0 params
        getDecSepSig.WriteByte(0x0E); // string return
        var getDecSep = mdb.AddMemberReference(nfiRef, mdb.GetOrAddString("get_NumberDecimalSeparator"),
            mdb.GetOrAddBlob(getDecSepSig));

        // NumberFormatInfo::get_CurrentInfo() static NumberFormatInfo.
        var getCurSig = new BlobBuilder();
        getCurSig.WriteByte(0x00);
        getCurSig.WriteCompressedInteger(0);
        getCurSig.WriteByte(0x12); // CLASS return
        getCurSig.WriteCompressedInteger(CodedIndex(nfiRef));
        var getCur = mdb.AddMemberReference(nfiRef, mdb.GetOrAddString("get_CurrentInfo"),
            mdb.GetOrAddBlob(getCurSig));

        var dummy = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Dummy"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), 0, default(ParameterHandle));
        _ = dummy;
        var progType = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Prog"), objRef, default(FieldDefinitionHandle),
            dummy);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.CallvirtLiteral:
                encoder.OpCode(ILOpCode.Ldnull);
                encoder.OpCode(ILOpCode.Callvirt);
                encoder.Token(MetadataTokens.GetToken(getDecSep));
                break;
            case BodyKind.CallvirtNullMarker:
                encoder.OpCode(ILOpCode.Call);
                encoder.Token(MetadataTokens.GetToken(getCur));
                encoder.OpCode(ILOpCode.Callvirt);
                encoder.Token(MetadataTokens.GetToken(getDecSep));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);

        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = main;

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static int CodedIndex(EntityHandle h)
    {
        var row = MetadataTokens.GetToken(h) & 0xFFFFFF;
        var tag = h.Kind switch
        {
            HandleKind.TypeDefinition => 0,
            HandleKind.TypeReference => 1,
            _ => throw new ArgumentException($"handle kind {h.Kind}"),
        };
        return (row << 2) | tag;
    }
}
