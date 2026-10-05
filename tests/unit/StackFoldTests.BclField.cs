using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// BCL static-field reads the generated code has no storage for (external
// mscorlib types have no TypeDef, so EmitStaticStorage never emits their
// fields). bitconvcase L12 root cause: `ldsfld bool
// System.BitConverter::IsLittleEndian` emitted the undeclared symbol
// System_BitConverter_sf_IsLittleEndian (build error "use of undeclared
// identifier") -- IsLittleEndian is a static readonly FIELD in net48, so the
// field mapper never sees it. The fold pins the platform contract
// (little-endian = true) as the literal 1, mirroring the System.String.Empty
// fold (StackFold.cs Ldsfld case, the M5 wiring precedent).
//------------------------------------------------------------------------------
public class StackFoldTestsBclField
{
    [Fact]
    public void Fold_Ldsfld_BitConverterIsLittleEndian_FoldsToTrue()
    {
        // ldsfld System.BitConverter::IsLittleEndian; ret
        // -> RetStmt(IntConstExpr 1): the little-endian platform contract
        // (bitconverter.h baseline, Windows x86-64; net48 value = true).
        using var pe = Synthesize(BodyKind.LdsfldIsLittleEndian);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        Assert.Empty(stmts.OfType<AssignStmt>());
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_Ldsfld_StringEmpty_FoldsToEmptyLiteral()
    {
        // ldsfld System.String::Empty; ret -> RetStmt(StringConstExpr "")
        // (the M5 wiring precedent the IsLittleEndian fold mirrors).
        using var pe = Synthesize(BodyKind.LdsfldStringEmpty);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        Assert.Empty(stmts.OfType<AssignStmt>());
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // Test harness (the StackFoldTests.Ldtoken.cs Synthesize pattern: a
    // synthetic PE whose Main body carries the ldsfld under test).
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

    private enum BodyKind
    {
        LdsfldIsLittleEndian,   // ldsfld System.BitConverter::IsLittleEndian
        LdsfldStringEmpty,      // ldsfld System.String::Empty
    }

    private sealed record BclFieldTokens
    {
        public required int IsLittleEndian { get; init; }
        public required int StringEmpty { get; init; }
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var bitconvRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("BitConverter"));
        var stringRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        // FieldRefs: FIELD (0x06) + BOOLEAN (0x02) / STRING (0x0E).
        var isLittleEndian = mdb.AddMemberReference(bitconvRef, mdb.GetOrAddString("IsLittleEndian"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x02 }));
        var stringEmpty = mdb.AddMemberReference(stringRef, mdb.GetOrAddString("Empty"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x0E }));
        var tokens = new BclFieldTokens
        {
            IsLittleEndian = MetadataTokens.GetToken(isLittleEndian),
            StringEmpty = MetadataTokens.GetToken(stringEmpty),
        };

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.LdsfldIsLittleEndian:
                Ldsfld(encoder, tokens.IsLittleEndian);
                break;
            case BodyKind.LdsfldStringEmpty:
                Ldsfld(encoder, tokens.StringEmpty);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        // Main MUST be attached to a TypeDef (the AddTypeDefinition wrapper --
        // the pattern every other StackFold suite harness uses): a MethodDef
        // added directly to the MetadataBuilder is orphaned, the reader sees
        // zero TypeDefs, and the Fold helper's .Single(... == "Main") throws
        // "Sequence contains no matching element" before the fold runs.
        var dummyField = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_dummy"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Cases"), objRef, dummyField,
            mdb.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
                mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle)));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static void Ldsfld(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Ldsfld);
        e.Token(token);
    }
}
