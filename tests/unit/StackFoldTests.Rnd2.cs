using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using Xunit;

//------------------------------------------------------------------------------
// D-RND2-1/2/3 -- the three randomdeep2 emission defects (transpiler):
//
//   D-RND2-1  `int[] arr; arr[i]++` folds to ldelema -> ldind -> add -> stind
//             (the load/store pair through the element address). The fold
//             shape is a CallStmt stind_*; the emitter's stind/stobj/cpobj/
//             cpblk/initblk statement arms DROPPED the trailing ';' -- the
//             increment's C++ line had no terminator ("expected ';' after
//             expression" build abort). [emitter fix]
//   D-RND2-2  `hist += buckets[i]` -- Roslyn calls Int32::ToString() on the
//             ldelema ADDRESS (the byref receiver), materialized into a
//             "ptr"-kind temp. The receiver must route through the resolver's
//             "deref:*" LOAD arm; the old address detection missed "ptr"
//             temps, so the resolver picked "unbox:i" and emitted
//             il2cpp_unbox_int32(static_cast<Il2CppObject*>(intptr_t)) --
//             "cannot cast from type 'intptr_t' to 'Il2CppObject*'". [fold fix]
//   D-RND2-3  `(int)(NextDouble() * 10)` in a COMPARISON -- the double-typed
//             multiply stays an unmaterialized BinaryExpr on the stack; the
//             conv.i4 fold only matched consts/locals/call-results, so the
//             truncation silently vanished and the comparison ran in double
//             space. [fold fix]
//------------------------------------------------------------------------------
public class StackFoldTestsRnd2
{
    [Fact]
    public void Fold_ConvI4_DoubleBinaryExpr_TruncatesViaIcall()
    {
        // (int)(d * 10.0) with d a double local: ldloc.0; ldc.r8 10.0; mul;
        // conv.i4; ret -> icall_conv_i4_d(BinaryExpr) (truncate toward zero),
        // NOT a dropped conversion.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x06,                                                      // ldloc.0 (double loc0)
                0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x24, 0x40,      // ldc.r8 10.0
                0x5A,                                                      // mul
                0x69,                                                      // conv.i4
                0x2A,                                                      // ret
            }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "d" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_i4_d", conv.Function);
        var bin = Assert.IsType<BinaryExpr>(conv.Args[0]);
        Assert.Equal(BinaryOp.Mul, bin.Op);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(bin.Left).Name);
    }

    [Fact]
    public void Fold_Stind_ElementIncrement_EmitsPointerStoreStatement()
    {
        // `arr[i]++` -- ldelema; dup; ldind.i4; ldc.i4.1; add; stind.i4: the
        // increment folds to a stind_i4 CallStmt (addr, value) through the
        // ldelema address temp -- the load/store pair D-RND2-1 needs (the
        // emitter fix adds the missing statement ';').
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x06,                                  // ldloc.0 (array)
                0x07,                                  // ldloc.1 (index)
                0x8F, 0x01, 0x00, 0x00, 0x01,          // ldelema <TypeRef System.Int32>
                0x25,                                  // dup
                0x4A,                                  // ldind.i4
                0x17,                                  // ldc.i4.1
                0x58,                                  // add
                0x54,                                  // stind.i4
            }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "r", "i" } });

        var stind = Assert.Single(stmts.OfType<CallStmt>(), s => s.Function.StartsWith("stind_"));
        Assert.Equal("t0", Assert.IsType<LocExpr>(stind.Args[0]).Name);
        var val = Assert.IsType<BinaryExpr>(stind.Args[1]);
        Assert.Equal(BinaryOp.Add, val.Op);
        var ldind = Assert.IsType<CallExpr>(val.Left);
        Assert.StartsWith("ldind_", ldind.Function);
    }

    [Fact]
    public void Fold_LdelemaReceiver_ToString_ResolvesDerefCast()
    {
        // `hist += buckets[i]` -- Roslyn calls Int32::ToString() on the
        // ldelema ADDRESS (byref receiver). The address is materialized into
        // a "ptr"-kind temp; the receiver must resolve with the "deref:i"
        // LOAD cast -- NOT "unbox:i" (which would emit an intptr_t -> object
        // unbox, the D-RND2-2 build abort).
        using var pe = new PEReader(SynthesizeInt32ToString());
        var md = pe.GetMetadataReader();
        var int32Ref = md.TypeReferences.Single(h => md.GetString(md.GetTypeReference(h).Name) == "Int32");
        var toStringRef = md.MemberReferences.Single(h => md.GetString(md.GetMemberReference(h).Name) == "ToString");
        var int32Tok = MetadataTokens.GetToken(int32Ref);
        var callTok = MetadataTokens.GetToken(toStringRef);

        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x06,                                  // ldloc.0 (array)
                0x07,                                  // ldloc.1 (index)
                0x8F,                                  // ldelema <Int32>
                (byte)(int32Tok & 0xFF), (byte)((int32Tok >> 8) & 0xFF),
                (byte)((int32Tok >> 16) & 0xFF), (byte)((int32Tok >> 24) & 0xFF),
                0x28,                                  // call instance string Int32::ToString()
                (byte)(callTok & 0xFF), (byte)((callTok >> 8) & 0xFF),
                (byte)((callTok >> 16) & 0xFF), (byte)((callTok >> 24) & 0xFF),
            }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "r", "i" } },
            md, Array.Empty<TypeDef>());

        var assign = Assert.Single(stmts.OfType<AssignStmt>(),
            a => a.Value is CallExpr { Function: var fn } && fn.StartsWith("call_"));
        var ce = Assert.IsType<CallExpr>(assign.Value);
        Assert.NotNull(ce.Resolved);
        Assert.Equal("il2cpp::runtime::icall_int32_to_string", ce.Resolved.CppName);
        Assert.Equal("deref:i", ce.Resolved.ParamTypeNames![0]);
    }

    //--------------------------------------------------------------------------
    // A synthetic assembly whose only MemberReference is
    // System.Int32::ToString() (HAS_THIS header, 0 params, string return).
    //--------------------------------------------------------------------------
    private static MemoryStream SynthesizeInt32ToString()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var int32Ref = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        mdb.AddMemberReference(int32Ref, mdb.GetOrAddString("ToString"),
            mdb.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x0E }));
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
