using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// Box`1::Get/Set definition bodies read `this.item` through a MemberRef whose
// parent is the OPEN TypeSpec Box<T> (blob 15-12-..-01-13-00 -- metadump of
// boxunbox.exe: `item parentBlob=15-12-08-01-13-00`). Folding a closed
// instantiation body (e.g. Box_1_int::Get, hint="int") must resolve that VAR
// through the enclosing clone's suffix (the R2b P2 InstantiationSuffixHint
// mechanism), not throw "generic argument byte 0x13 in M3" (the boxunbox /
// generics interim609 failure: Box`1::Get/Set stubs -> Box`1::Set/Main not
// transpiled).
//------------------------------------------------------------------------------
public class StackFoldTestsOpenGenericField
{
    [Fact]
    public void Fold_Ldfld_OpenGenericParent_ResolvesThroughHint()
    {
        // Box<T>.Get shape: ldarg.0; ldfld item (parent = open Box<T>
        // TypeSpec); ret -- folded under hint "int" (the Box_1_int body pass).
        using var pe = Synthesize();
        var md = pe.GetMetadataReader();

        var prev = MethodRefResolver.InstantiationSuffixHint;
        MethodRefResolver.InstantiationSuffixHint = "int";
        List<Stmt> stmts;
        try
        {
            stmts = Fold(pe, md);
        }
        finally
        {
            MethodRefResolver.InstantiationSuffixHint = prev;
        }

        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        var field = Assert.IsType<FieldExpr>(ret.Value);
        Assert.Equal("Cases.Box`1_int", field.OwnerFullName);
        Assert.Equal("item", field.FieldCppName);
    }

    [Fact]
    public void Fold_Newobj_DottedExceptionArg_AllocatesReally()
    {
        // A5 (myIList): `new List<System.Exception>()` (parent TypeSpec
        // List`1 + 0x12 System.Exception) is a LIST, not a BCL exception --
        // the fold must emit a real NewObjExpr allocation, not the
        // BCL-exception null model (which stored nullptr into the field).
        using var pe = SynthesizeListOfException();
        var md = pe.GetMetadataReader();

        var stmts = FoldNewobj(pe, md);

        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.IsType<NewObjExpr>(assign.Value);
    }

    //--------------------------------------------------------------------------
    // Test harness (the StackFoldTests.Ldflda.cs Synthesize pattern).
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Get");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 1 }, md, Array.Empty<TypeDef>());
    }

    private static List<Stmt> FoldNewobj(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Make");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    private static PEReader Synthesize()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var boxRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Box`1"));

        // OPEN TypeSpec Box<T>: 0x15 GenericInst, 0x12 CLASS, coded def index,
        // 1 arg, VAR (0x13) index 0 -- the exact Roslyn shape metadump shows
        // for `this.item` inside a generic definition body.
        var tsBlob = new BlobBuilder();
        tsBlob.WriteByte(0x15);
        tsBlob.WriteByte(0x12);
        tsBlob.WriteCompressedInteger(CodedIndex(boxRef));
        tsBlob.WriteCompressedInteger(1);
        tsBlob.WriteByte(0x13);
        tsBlob.WriteCompressedInteger(0);
        var openSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(tsBlob));

        // FieldRef item (FIELD 0x06 + VAR 0x13 index 0) parented on the open spec.
        var field = mdb.AddMemberReference(openSpec, mdb.GetOrAddString("item"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x13, 0x00 }));
        var fieldToken = MetadataTokens.GetToken(field);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldarg_0);
        encoder.OpCode(ILOpCode.Ldfld);
        encoder.Token(fieldToken);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var get = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Get"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x08 }), bodyOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Box`1"), objRef,
            default(FieldDefinitionHandle), get);

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

    // new List<System.Exception> shape: TypeRefs Object (row 1), List`1
    // (row 2), Exception (row 3); TypeSpec [15 12 09 01 12 0D]; .ctor
    // MemberRef (parent = TypeSpec, sig 20 00 01); a TypeDef whose Make
    // body does newobj; pop; ret.
    private static PEReader SynthesizeListOfException()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System.Collections.Generic"), mdb.GetOrAddString("List`1"));
        mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));
        var typeSpec = mdb.AddTypeSpecification(mdb.GetOrAddBlob(new byte[] { 0x15, 0x12, 0x09, 0x01, 0x12, 0x0D }));
        var ctor = mdb.AddMemberReference(typeSpec, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 }));
        var ctorToken = MetadataTokens.GetToken(ctor);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Newobj);
        encoder.Token(ctorToken);
        encoder.OpCode(ILOpCode.Pop);
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var make = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Make"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Maker"), objRef, default, make);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }
}
