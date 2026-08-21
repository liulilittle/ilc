using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// Ldtoken decode support (reflection audit F1 -- the biggest single
// transpiler gap: 17/50 failing e2e cases blocked on it). typeof(T)
// compiles to `ldtoken <TypeDefOrRef>; call System.Type::GetTypeFromHandle`
// (net48 typecheckcase.exe IL dump: 00CA Ldtoken 0x02000003 / 00CF Call
// System.Type::GetTypeFromHandle). The fold turns the pair into the type's
// FULL-NAME string constant -- the simplified System.Type model
// (il2cpp_typeext.h assumption 1: a Type VALUE is the full name string, the
// exact output the runtime GetTypeFromHandle icall produces). FIELD tokens
// are the RuntimeHelpers.InitializeArray array-initializer pattern (net48
// `new byte[] {...}` emits ldtoken<RVA field>; call InitializeArray) -- no
// fold, loud reject. METHOD tokens likewise.
//------------------------------------------------------------------------------
public class StackFoldTestsLdtoken
{
    [Fact]
    public void Fold_LdtokenTypeDef_GetTypeFromHandle_FoldsToFullNameString()
    {
        // ldtoken <TypeDef Cases.Base>; call System.Type::GetTypeFromHandle; ret
        using var pe = Synthesize(BodyKind.TypeDefGetTypeFromHandle);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        // the whole pair folds away: no call remains, ret carries the string
        Assert.Empty(stmts.OfType<CallStmt>());
        Assert.Empty(stmts.OfType<AssignStmt>());
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("Cases.Base", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_LdtokenTypeRef_GetTypeFromHandle_FoldsToFullNameString()
    {
        // ldtoken <TypeRef System.Int32>; call GetTypeFromHandle; ret -- the
        // typecheckcase typeof(int) shape (00DE Ldtoken 0x0100000E).
        using var pe = Synthesize(BodyKind.TypeRefGetTypeFromHandle);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md);

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("System.Int32", Assert.IsType<StringConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_LdtokenField_NonInitializeArrayConsumer_ThrowsLoud()
    {
        // ldtoken FIELD consumed by a NON-InitializeArray call: the field
        // marker folds only in the RuntimeHelpers.InitializeArray
        // array-initializer pattern (net48 `new byte[] {...}`) -- any other
        // consumer is the ldtoken loud guard (no silent token flow).
        using var pe = Synthesize(BodyKind.FieldNonInitializeArrayConsumer);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("ldtoken", ex.Message);
        Assert.Contains("no fold", ex.Message);
    }

    [Fact]
    public void Fold_LdtokenMethod_ThrowsLoud()
    {
        using var pe = Synthesize(BodyKind.MethodLdtoken);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("ldtoken", ex.Message);
        Assert.Contains("METHOD", ex.Message);
    }

    [Fact]
    public void Fold_GetTypeFromHandle_NonLdtokenArg_EmitsCall()
    {
        // control: GetTypeFromHandle on a NON-ldtoken argument must NOT fold
        // -- the resolver's Type-domain icall emission stays (the fold is
        // gated on prevIns being Ldtoken).
        using var pe = Synthesize(BodyKind.NonLdtokenGetTypeFromHandle);
        var md = pe.GetMetadataReader();
        var tokens = PeTokens(pe);

        var stmts = Fold(pe, md);

        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal($"call_{tokens.GetTypeFromHandle}", call.Function);
    }

    [Fact]
    public void Fold_Ldtoken_NonGetTypeFromHandleConsumer_ThrowsLoud()
    {
        // an ldtoken result consumed by anything else must not flow into
        // emission as a raw token (silent miscompile guard).
        using var pe = Synthesize(BodyKind.NonGetTypeFromHandleConsumer);
        var md = pe.GetMetadataReader();

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md));
        Assert.Contains("ldtoken", ex.Message);
    }

    //--------------------------------------------------------------------------
    // Test harness.
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
        TypeDefGetTypeFromHandle,   // ldtoken(TypeDef) + call GetTypeFromHandle
        TypeRefGetTypeFromHandle,   // ldtoken(TypeRef) + call GetTypeFromHandle
        FieldInitializeArray,       // ldtoken(FieldDef) + call Nope (non-InitializeArray consumer)
        FieldNonInitializeArrayConsumer = FieldInitializeArray,
        MethodLdtoken,              // ldtoken(MethodDef) + ret
        NonLdtokenGetTypeFromHandle,// ldc.i4.1 + call GetTypeFromHandle
        NonGetTypeFromHandleConsumer, // ldtoken(TypeDef) + call Nope
    }

    private sealed record LdTokenTokens
    {
        public required int BaseTypeDef { get; init; }
        public required int Int32TypeRef { get; init; }
        public required int GetTypeFromHandle { get; init; }
        public required int InitializeArray { get; init; }
        public required int FieldDef { get; init; }
        public required int NopeMethod { get; init; }
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var typeRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Type"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        var rthRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("RuntimeTypeHandle"));
        var arrRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Array"));

        // Type.GetTypeFromHandle(RuntimeTypeHandle) : Type -- static, 1 param
        // (sig: default cc, 1 param, ret CLASS<Type>, param VALUETYPE<RuntimeTypeHandle>)
        var gtfhSig = new BlobBuilder();
        gtfhSig.WriteByte(0x00);
        gtfhSig.WriteCompressedInteger(1);
        gtfhSig.WriteByte(0x12);
        gtfhSig.WriteCompressedInteger(CodedIndex(typeRef));
        gtfhSig.WriteByte(0x11);
        gtfhSig.WriteCompressedInteger(CodedIndex(rthRef));
        var getTypeFromHandle = mdb.AddMemberReference(typeRef, mdb.GetOrAddString("GetTypeFromHandle"),
            mdb.GetOrAddBlob(gtfhSig));
        // RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle) : void -- static, 2 params
        var initSig = new BlobBuilder();
        initSig.WriteByte(0x00);
        initSig.WriteCompressedInteger(2);
        initSig.WriteByte(0x01);
        initSig.WriteByte(0x12);
        initSig.WriteCompressedInteger(CodedIndex(arrRef));
        initSig.WriteByte(0x11);
        initSig.WriteCompressedInteger(CodedIndex(rthRef));
        var initializeArray = mdb.AddMemberReference(arrRef, mdb.GetOrAddString("InitializeArray"),
            mdb.GetOrAddBlob(initSig));

        // Cases.Base with a static field s_data (the RVA-initializer target)
        var field = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_data"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var nope = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), 0, default(ParameterHandle));

        var tokens = new LdTokenTokens
        {
            BaseTypeDef = MetadataTokens.GetToken(
                mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
                    mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, nope)),
            Int32TypeRef = MetadataTokens.GetToken(int32Ref),
            GetTypeFromHandle = MetadataTokens.GetToken(getTypeFromHandle),
            InitializeArray = MetadataTokens.GetToken(initializeArray),
            FieldDef = MetadataTokens.GetToken(field),
            NopeMethod = MetadataTokens.GetToken(nope),
        };

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case BodyKind.TypeDefGetTypeFromHandle:
                LdToken(encoder, tokens.BaseTypeDef);
                Call(encoder, tokens.GetTypeFromHandle);
                break;
            case BodyKind.TypeRefGetTypeFromHandle:
                LdToken(encoder, tokens.Int32TypeRef);
                Call(encoder, tokens.GetTypeFromHandle);
                break;
            case BodyKind.FieldInitializeArray:
                LdToken(encoder, tokens.FieldDef);
                Call(encoder, tokens.NopeMethod);
                break;
            case BodyKind.MethodLdtoken:
                LdToken(encoder, tokens.NopeMethod);
                break;
            case BodyKind.NonLdtokenGetTypeFromHandle:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                Call(encoder, tokens.GetTypeFromHandle);
                break;
            case BodyKind.NonGetTypeFromHandleConsumer:
                LdToken(encoder, tokens.BaseTypeDef);
                Call(encoder, tokens.NopeMethod);
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

    // Re-read the synthesized tokens back from the PE (the builder's handles
    // encode the same rows; this guards against builder/reader drift).
    private static LdTokenTokens PeTokens(PEReader pe)
    {
        var md = pe.GetMetadataReader();
        var typeRefs = md.TypeReferences.ToDictionary(
            h => md.GetString(md.GetTypeReference(h).Name), h => MetadataTokens.GetToken(h));
        var members = md.MemberReferences.ToDictionary(
            h => md.GetString(md.GetMemberReference(h).Name), h => MetadataTokens.GetToken(h));
        var types = md.TypeDefinitions.ToDictionary(
            h => md.GetString(md.GetTypeDefinition(h).Name), h => MetadataTokens.GetToken(h));
        var fields = md.FieldDefinitions.ToDictionary(
            h => md.GetString(md.GetFieldDefinition(h).Name), h => MetadataTokens.GetToken(h));
        var methods = md.MethodDefinitions.ToDictionary(
            h => md.GetString(md.GetMethodDefinition(h).Name), h => MetadataTokens.GetToken(h));
        return new LdTokenTokens
        {
            BaseTypeDef = types["Base"],
            Int32TypeRef = typeRefs["Int32"],
            GetTypeFromHandle = members["GetTypeFromHandle"],
            InitializeArray = members["InitializeArray"],
            FieldDef = fields["s_data"],
            NopeMethod = methods["Nope"],
        };
    }

    private static void LdToken(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Ldtoken);
        e.Token(token);
    }

    private static void Call(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Call);
        e.Token(token);
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

