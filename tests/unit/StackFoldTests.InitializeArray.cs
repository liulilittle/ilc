using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

//------------------------------------------------------------------------------
// RuntimeHelpers.InitializeArray array-initializer fold (e2e G1 -- the last
// remaining ldtoken blocking pattern, 15 e2e cases). net48 csc compiles
// `new byte[] {...}` / `new int[] {...}` (non-tiny arrays) to
//   ldc.i4.N; newarr <elem>; dup; ldtoken <RVA field>; call InitializeArray
// (probe-verified: base64case.exe "Man" at 0x0007, "Hello" later; arrayext2
// int[]{1,3,5,7,9} at 0x0049). The fold turns the pair into a stelem
// sequence loaded from the field's RVA blob (FieldDefinition
// .GetRelativeVirtualAddress() -> PE section mapping). The element WIDTH and
// COUNT come from the newarr instruction -- csc DEDUPES prefix blobs (the
// 1-byte and 2-byte arrays in base64case reuse the 3-byte "Man" blob), so
// only the newarr length bounds the copy.
//
// Ground truth (probe, metadump):
//   * the Constant table is EMPTY for these fields -- the data lives in the
//     FieldRva table (HasFieldRVA), NOT in a Constant blob;
//   * the blob is 4-byte aligned/padded ("Man" -> 4D 61 6E 00, "Hello" ->
//     48 65 6C 6C 6F 00 00 00);
//   * int32 blobs are little-endian (01 00 00 00 03 00 00 00 ...).
// Fixtures: the real net48 csc-built e2e case executables (tests/cases/
// <name>/obj/Release/net48/). The fold itself never runs without the PE
// image (it must read the RVA blob) -- the pe==null path is a loud wiring
// gap until the fold call sites pass the PEReader.
//------------------------------------------------------------------------------
public class StackFoldTestsInitializeArray
{
    //--- real-exe fixtures (net48 csc ground truth) -------------------------

    [Fact]
    public void Fold_InitializeArray_RealBase64Case_ThreeByteStelems()
    {
        // `new byte[] { 'M', 'a', 'n' }` -- the 3-byte blob (prefix-deduped
        // source for the 1/2-byte arrays too).
        using var pe = OpenCaseExe("base64case");
        var md = pe.GetMetadataReader();
        var slice = SliceFirstInitializer(pe, md, occurrence: 0);
        var stmts = Fold(pe, md, slice, peArg: pe);

        Assert.Empty(stmts.OfType<CallStmt>().Where(s => s.Function.Contains("InitializeArray")));
        var newarr = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.StartsWith("newarr_", Assert.IsType<CallExpr>(newarr.Value).Function);
        var stelems = stmts.Skip(1).OfType<CallStmt>().ToList();
        Assert.Equal(3, stelems.Count);
        Assert.All(stelems, s => Assert.Equal("stelem_Stelem_i1", s.Function));
        AssertStelem(stelems[0], 0, 77);  // 'M'
        AssertStelem(stelems[1], 1, 97);  // 'a'
        AssertStelem(stelems[2], 2, 110); // 'n'
    }

    [Fact]
    public void Fold_InitializeArray_RealBase64Case_FiveByteStelems()
    {
        // The slice contains the earlier folded "Man" initializer AND the
        // direct-stelem 1/2-byte arrays (net48 csc inlines tiny initializers
        // as newarr + stelem -- no ldtoken) -- the LAST five stelems are the
        // "Hello" fold.
        using var pe = OpenCaseExe("base64case");
        var md = pe.GetMetadataReader();
        var slice = SliceFirstInitializer(pe, md, occurrence: 1);
        var stmts = Fold(pe, md, slice, peArg: pe);

        var stelems = stmts.OfType<CallStmt>().Where(s => s.Function.StartsWith("stelem_", StringComparison.Ordinal)).ToList();
        Assert.True(stelems.Count >= 8, $"expected at least 8 stelems, got {stelems.Count}");
        Assert.Equal("stelem_Stelem_i1", stelems[^5].Function);
        AssertStelem(stelems[^5], 0, 72);  // 'H'
        AssertStelem(stelems[^4], 1, 101); // 'e'
        AssertStelem(stelems[^3], 2, 108); // 'l'
        AssertStelem(stelems[^2], 3, 108); // 'l'
        AssertStelem(stelems[^1], 4, 111); // 'o'
    }

    [Fact]
    public void Fold_InitializeArray_RealArrayExt2_Int32Stelems()
    {
        // The slice starts at Main's beginning: the `string[] names` init
        // (direct newarr + stelem.ref for {"pear", null, "apple", "Cherry",
        // "cherry"}) precedes the int[] fold -- the LAST five stelems are the
        // folded int32 sequence.
        using var pe = OpenCaseExe("arrayext2");
        var md = pe.GetMetadataReader();
        var slice = SliceFirstInitializer(pe, md, occurrence: 0);
        var stmts = Fold(pe, md, slice, peArg: pe);

        var stelems = stmts.OfType<CallStmt>().Where(s => s.Function.StartsWith("stelem_", StringComparison.Ordinal)).ToList();
        Assert.True(stelems.Count >= 5, $"expected at least 5 stelems, got {stelems.Count}");
        Assert.All(stelems.Skip(stelems.Count - 5), s => Assert.Equal("stelem_Stelem_i4", s.Function));
        AssertStelem(stelems[^5], 0, 1);
        AssertStelem(stelems[^4], 1, 3);
        AssertStelem(stelems[^3], 2, 5);
        AssertStelem(stelems[^2], 3, 7);
        AssertStelem(stelems[^1], 4, 9);
    }

    [Fact]
    public void Fold_InitializeArray_MissingPe_ThrowsLoudWiringGap()
    {
        // The fold needs the PE image to read the RVA blob; without it the
        // pattern must not emit (silent miscompile) -- loud with the wiring
        // point (the fold call sites pass the PEReader).
        using var pe = OpenCaseExe("base64case");
        var md = pe.GetMetadataReader();
        var slice = SliceFirstInitializer(pe, md, occurrence: 0);

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md, slice, peArg: null));
        Assert.Contains("InitializeArray", ex.Message);
        Assert.Contains("PEReader", ex.Message);
    }

    //--- synthetic controls -------------------------------------------------

    [Fact]
    public void Fold_InitializeArray_Synth_FieldWithoutRvaData_ThrowsLoud()
    {
        // Full pattern shape (ldc.i4.3; newarr Byte; dup; ldtoken FIELD;
        // call InitializeArray) against a field with NO RVA row -> loud (the
        // pattern requires a .data-backed static field).
        foreach (var parentIsRuntimeHelpers in new[] { false, true })
        {
            using var pe = SynthFullShape(parentIsRuntimeHelpers);
            var md = pe.GetMetadataReader();
            var body = MainBody(pe, md);

            var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md, body, peArg: pe));
            Assert.Contains("InitializeArray", ex.Message);
            Assert.Contains("RVA data", ex.Message);
        }
    }

    [Fact]
    public void Fold_LdtokenField_NonInitializeArrayConsumer_ThrowsLoud()
    {
        // ldtoken FIELD consumed by a plain call: not the InitializeArray
        // pattern -> the ldtoken loud guard fires (no silent token flow).
        using var pe = Synth(SynthTail.NopeCall);
        var md = pe.GetMetadataReader();
        var body = MainBody(pe, md);

        var ex = Assert.Throws<NotSupportedException>(() => Fold(pe, md, body, peArg: pe));
        Assert.Contains("ldtoken", ex.Message);
        Assert.Contains("no fold", ex.Message);
    }

    [Fact]
    public void Fold_InitializeArray_NonLdtokenArg_DoesNotFold()
    {
        // control: InitializeArray on a NON-ldtoken argument (ldc.i4.0
        // before the call) must NOT fold -- the pattern gate is prevIns ==
        // Ldtoken. The call goes to the resolver path (emit or loud), never
        // through the fold's stelem emission.
        using var pe = Synth(SynthTail.PlainInitializeArrayCall);
        var md = pe.GetMetadataReader();
        var body = MainBody(pe, md);

        var ex = Record.Exception(() => Fold(pe, md, body, peArg: pe));
        if (ex == null)
        {
            var stmts = Fold(pe, md, body, peArg: pe);
            Assert.DoesNotContain(stmts, s => s is CallStmt c && c.Function.StartsWith("stelem_", StringComparison.Ordinal));
        }
        else
        {
            Assert.DoesNotContain(ex.Message, "PEReader");
            Assert.DoesNotContain(ex.Message, "RVA data");
            Assert.DoesNotContain(ex.Message, "consumes an ldtoken");
        }
    }

    //--------------------------------------------------------------------------
    // Harness.
    //--------------------------------------------------------------------------
    private static List<Stmt> Fold(PEReader pe, MetadataReader md, byte[] ilSlice, PEReader? peArg)
    {
        var insns = IlDecoder.DecodeMethod(ilSlice, md);
        return StackFold.Fold(insns, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>(), null, peArg);
    }

    // Locate the repo root from the test assembly location (tests/unit/
    // bin/Release/net8.0 -> E:\il2cpp).
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "il2cppc", "il2cppc.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the repo root from " + AppContext.BaseDirectory);
    }

    // The e2e case exe (net48 csc ground truth). Requires the case to have
    // been built (run-tests.ps1 / dotnet build tests/cases/<name>).
    private static PEReader OpenCaseExe(string name)
    {
        var path = Path.Combine(RepoRoot(), "tests", "cases", name, "obj", "Release", "net48", name + ".exe");
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"fixture {path} not found -- build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
        return new PEReader(File.OpenRead(path));
    }

    // Main body IL: find the single "Main" MethodDef in the PE.
    private static byte[] MainBody(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var body = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        return body!.GetILBytes()!;
    }

    // Slice the IL from the start of Main up to the END of the Nth (0-based)
    // InitializeArray call -- the pattern (newarr; dup; ldtoken FIELD; call
    // InitializeArray) plus everything before it. Occurrence 0 of base64case
    // is exactly the 3-byte "Man" initializer.
    private static byte[] SliceFirstInitializer(PEReader pe, MetadataReader md, int occurrence)
    {
        var il = MainBody(pe, md);
        var initTok = md.MemberReferences
            .Single(h => md.GetString(md.GetMemberReference(h).Name) == "InitializeArray");
        var tokBytes = BitConverter.GetBytes(MetadataTokens.GetToken(initTok));
        var seen = 0;
        for (var i = 0; i + 5 <= il.Length; i++)
        {
            if (il[i] != (byte)ILOpCode.Call) continue; // 0x28
            var match = true;
            for (var k = 0; k < 4; k++)
                if (il[i + 1 + k] != tokBytes[k]) { match = false; break; }
            if (!match) continue;
            if (seen++ != occurrence) continue;
            return il[..(i + 5)];
        }
        throw new InvalidOperationException($"InitializeArray call #{occurrence} not found in Main");
    }

    private static void AssertStelem(CallStmt s, long index, long value)
    {
        var arr = Assert.IsType<LocExpr>(s.Args[0]);
        Assert.StartsWith("t", arr.Name);
        Assert.Equal(index, Assert.IsType<IntConstExpr>(s.Args[1]).Value);
        Assert.Equal(value, Assert.IsType<IntConstExpr>(s.Args[2]).Value);
    }

    //--------------------------------------------------------------------------
    // Synthetic PE (the full pattern shape; the field has NO RVA row, so the
    // fold must reject it loudly at the RVA check).
    //--------------------------------------------------------------------------
    private enum SynthTail
    {
        NopeCall,                  // ldtoken FIELD; call Nope  -> loud guard
        PlainInitializeArrayCall,  // ldc.i4.0; call InitializeArray -> no fold
    }

    private static PEReader Synth(SynthTail tail)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var byteRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Byte"));
        var arrRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Array"));
        var rfthRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("RuntimeFieldHandle"));

        // RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle) : void
        var initSig = new BlobBuilder();
        initSig.WriteByte(0x00);
        initSig.WriteCompressedInteger(2);
        initSig.WriteByte(0x01);
        initSig.WriteByte(0x12);
        initSig.WriteCompressedInteger(CodedIndex(arrRef));
        initSig.WriteByte(0x11);
        initSig.WriteCompressedInteger(CodedIndex(rfthRef));
        var initializeArray = mdb.AddMemberReference(arrRef, mdb.GetOrAddString("InitializeArray"),
            mdb.GetOrAddBlob(initSig));

        var field = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_data"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x11 }));
        var nope = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), 0, default(ParameterHandle));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, nope);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (tail)
        {
            case SynthTail.NopeCall:
                LdToken(encoder, MetadataTokens.GetToken(field));
                encoder.OpCode(ILOpCode.Call);
                encoder.Token(MetadataTokens.GetToken(nope));
                break;
            case SynthTail.PlainInitializeArrayCall:
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Call);
                encoder.Token(MetadataTokens.GetToken(initializeArray));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tail));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        _ = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        _ = byteRef;

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    // Full pattern shape: ldc.i4.3; newarr Byte; dup; ldtoken FIELD; call
    // InitializeArray -- the real base64case initializer IL.
    private static PEReader SynthFullShape(bool parentIsRuntimeHelpers)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var byteRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Byte"));
        var arrRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Array"));
        var rfthRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("RuntimeFieldHandle"));
        var rthParent = parentIsRuntimeHelpers
            ? mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System.Runtime.CompilerServices"), mdb.GetOrAddString("RuntimeHelpers"))
            : arrRef;

        var initSig = new BlobBuilder();
        initSig.WriteByte(0x00);
        initSig.WriteCompressedInteger(2);
        initSig.WriteByte(0x01);
        initSig.WriteByte(0x12);
        initSig.WriteCompressedInteger(CodedIndex(arrRef));
        initSig.WriteByte(0x11);
        initSig.WriteCompressedInteger(CodedIndex(rfthRef));
        var initializeArray = mdb.AddMemberReference(rthParent, mdb.GetOrAddString("InitializeArray"),
            mdb.GetOrAddBlob(initSig));

        var field = mdb.AddFieldDefinition(FieldAttributes.Private | FieldAttributes.Static,
            mdb.GetOrAddString("s_data"), mdb.GetOrAddBlob(new byte[] { 0x06, 0x11 }));
        var nope = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }), 0, default(ParameterHandle));

        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Base"), objRef, field, nope);

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        encoder.OpCode(ILOpCode.Ldc_i4_3);
        encoder.OpCode(ILOpCode.Newarr);
        encoder.Token(MetadataTokens.GetToken(byteRef));
        encoder.OpCode(ILOpCode.Dup);
        LdToken(encoder, MetadataTokens.GetToken(field));
        encoder.OpCode(ILOpCode.Call);
        encoder.Token(MetadataTokens.GetToken(initializeArray));
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 1, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        _ = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static void LdToken(InstructionEncoder e, int token)
    {
        e.OpCode(ILOpCode.Ldtoken);
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
