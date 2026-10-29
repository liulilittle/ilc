using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// StackFoldTests.PostfixInc -- static-field postfix increment (`SF++`).
//
// IL for `x = SF++` / `Foo(SF++)` / `arr[SF++] = v` is always
//     ldsfld SF; dup; ldc.i4.1; add; stsfld SF; <consumer>
// The `dup` copy is a LAZY StaticFieldExpr read: it must observe the value the
// field held BEFORE the `stsfld`. A local/param store is already protected --
// EmitStore snapshots any live stack value that references the store target
// into a temp BEFORE the assignment (ReferencesTarget, the `n-- > 0` pattern).
// The Stsfld branch (and the folded static-property setter) had NO such
// snapshot, so the dup copy was read AFTER the store and every consumer got
// the post-incremented value (probe-postfixinc: 6 DIFFs, sfield/sfieldl only;
// probe-postfixinc2: sfield/sfieldl + sprop/spropl).
//
// RED shape (before the fix):   [SF = SF + 1; return SF]
// GREEN shape (after the fix):  [tsnap0 = SF; SF = SF + 1; return tsnap0]
// The snapshot slot is deliberately NOT named `tN`: TempSlotPass's
// metadata-free kind mirror classifies a static-field read "val", so a `tN`
// snapshot of a LONG static field was merged into an int32 slot (truncation).
//------------------------------------------------------------------------------
public class StackFoldTestsPostfixInc
{
    [Fact]
    public void Fold_StaticFieldPostIncrement_SnapshotsOldValueBeforeStore()
    {
        using var pe = Synthesize(BodyKind.StaticFieldInt);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md, Array.Empty<string>(), Array.Empty<string>());

        var store = Assert.Single(stmts.OfType<AssignStmt>(),
            a => a.Target == "Cases.PostIncStatic.SF");
        var storeIndex = stmts.IndexOf(store);
        Assert.True(storeIndex > 0,
            "the static-field store must be preceded by the old-value snapshot");

        var snapshot = Assert.IsType<AssignStmt>(stmts[storeIndex - 1]);
        Assert.Matches(@"^tsnap\d+$", snapshot.Target);
        var snapRead = Assert.IsType<StaticFieldExpr>(snapshot.Value);
        Assert.Equal("Cases.PostIncStatic.SF", snapRead.FieldName);

        // The dup'd consumer must read the SNAPSHOT temp, not the field.
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var retLoc = Assert.IsType<LocExpr>(ret.Value);
        Assert.Equal(snapshot.Target, retLoc.Name);
    }

    [Fact]
    public void Fold_StaticFieldLongPostIncrement_SnapshotsOldValueBeforeStore()
    {
        using var pe = Synthesize(BodyKind.StaticFieldLong);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md, Array.Empty<string>(), Array.Empty<string>());

        var store = Assert.Single(stmts.OfType<AssignStmt>(),
            a => a.Target == "Cases.PostIncStatic.SL");
        var storeIndex = stmts.IndexOf(store);
        Assert.True(storeIndex > 0,
            "the static-field store must be preceded by the old-value snapshot");

        var snapshot = Assert.IsType<AssignStmt>(stmts[storeIndex - 1]);
        Assert.Equal("Cases.PostIncStatic.SL", Assert.IsType<StaticFieldExpr>(snapshot.Value).FieldName);
        Assert.Equal(snapshot.Target, Assert.IsType<LocExpr>(Assert.IsType<RetStmt>(stmts[^1]).Value).Name);
    }

    // Regression control: the LOCAL postfix increment shape must keep its
    // existing snapshot (EmitStore/ReferencesTarget) -- the static-field fix
    // must not change it (a local/param snapshot stays a plain `tN` slot).
    [Fact]
    public void Fold_LocalPostIncrement_KeepsSnapshotShape()
    {
        using var pe = Synthesize(BodyKind.LocalInt);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md, new[] { "loc0" }, new[] { "i" });

        var store = Assert.Single(stmts.OfType<AssignStmt>(), a => a.Target == "loc0");
        var storeIndex = stmts.IndexOf(store);
        Assert.True(storeIndex > 0);
        var snapshot = Assert.IsType<AssignStmt>(stmts[storeIndex - 1]);
        Assert.Matches(@"^t\d+$", snapshot.Target);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(snapshot.Value).Name);
        Assert.Equal(snapshot.Target, Assert.IsType<LocExpr>(Assert.IsType<RetStmt>(stmts[^1]).Value).Name);
    }

    // A static-field store that does NOT re-read the field must not grow a
    // snapshot statement (no consumer of the old value on the stack).
    [Fact]
    public void Fold_PlainStaticFieldStore_EmitsNoSnapshot()
    {
        using var pe = Synthesize(BodyKind.PlainStore);
        var md = pe.GetMetadataReader();

        var stmts = Fold(pe, md, Array.Empty<string>(), Array.Empty<string>());

        Assert.Single(stmts.OfType<AssignStmt>(), a => a.Target == "Cases.PostIncStatic.SF");
        Assert.DoesNotContain(stmts.OfType<AssignStmt>(), a => a.Target.StartsWith("tsnap", StringComparison.Ordinal));
    }

    //--------------------------------------------------------------------------
    // Test harness (the StackFoldTests.BclField Synthesize pattern).
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md, string[] locNames, string[] localKinds)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext
        {
            ArgCount = 0,
            LocNames = locNames,
            LocalKinds = localKinds,
        }, md, Array.Empty<TypeDef>());
    }

    private enum BodyKind
    {
        StaticFieldInt,   // ldsfld SF; dup; ldc.i4.1; add; stsfld SF; ret
        StaticFieldLong,  // ldsfld SL; dup; ldc.i4.1; conv.i8; add; stsfld SL; ret
        LocalInt,         // ldloc.0; dup; ldc.i4.1; add; stloc.0; ret
        PlainStore,       // ldc.i4.7; stsfld SF; ret
    }

    private static PEReader Synthesize(BodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        var sf = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static,
            mdb.GetOrAddString("SF"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 }));
        var sl = mdb.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static,
            mdb.GetOrAddString("SL"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x0A }));
        var sfToken = MetadataTokens.GetToken(sf);
        var slToken = MetadataTokens.GetToken(sl);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        byte retType;
        switch (kind)
        {
            case BodyKind.StaticFieldInt:
                encoder.OpCode(ILOpCode.Ldsfld); encoder.Token(sfToken);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadConstantI4(1);
                encoder.OpCode(ILOpCode.Add);
                encoder.OpCode(ILOpCode.Stsfld); encoder.Token(sfToken);
                retType = 0x08;
                break;
            case BodyKind.StaticFieldLong:
                encoder.OpCode(ILOpCode.Ldsfld); encoder.Token(slToken);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadConstantI4(1);
                encoder.OpCode(ILOpCode.Conv_i8);
                encoder.OpCode(ILOpCode.Add);
                encoder.OpCode(ILOpCode.Stsfld); encoder.Token(slToken);
                retType = 0x0A;
                break;
            case BodyKind.LocalInt:
                encoder.OpCode(ILOpCode.Ldloc_0);
                encoder.OpCode(ILOpCode.Dup);
                encoder.LoadConstantI4(1);
                encoder.OpCode(ILOpCode.Add);
                encoder.OpCode(ILOpCode.Stloc_0);
                retType = 0x08;
                break;
            case BodyKind.PlainStore:
                encoder.LoadConstantI4(7);
                encoder.OpCode(ILOpCode.Stsfld); encoder.Token(sfToken);
                retType = 0x01;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle),
            MethodBodyAttributes.None);

        // Static method signature blob: DEFAULT (0x00), 0 params, <retType>.
        var sig = mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, retType });
        _ = mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("PostIncStatic"), objRef, sf,
            mdb.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
                sig, bodyOffset, default(ParameterHandle)));

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
