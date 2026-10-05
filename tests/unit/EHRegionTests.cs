using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Xunit;

//------------------------------------------------------------------------------
// EH region coverage (exdeep gaps): catch-all decode + sibling-catch shapes.
//
//  - catch-all: C# `catch { }` / `catch (object)` compile to a catch region
//    whose CatchType is a System.Object TypeRef (NOT nil -- verified against
//    exdeep f19 + CatchRet via the metadata EH table). The runtime's
//    il2cpp_exception_matches never matches System.Object, so the decoder
//    must normalize it to the catch-all marker (CatchTypeToken == 0).
//  - region order: Roslyn emits the EH table INNER-FIRST (smallest try first,
//    ECMA-335 II.16.4.1 sort order). The emission walk picks the innermost
//    region by "last matching region wins", which requires OUTER-FIRST
//    order -- the decoder re-sorts (outer-first, inner-last, siblings kept
//    in metadata dispatch order).
//------------------------------------------------------------------------------
public class EHRegionTests
{
    // TypeName: catch type simple name ("Object"/"Exception"/...) resolved
    // inside the synthesized assembly; null + IsFinally=false = nil catch
    // type (ECMA catch-all); IsFinally=true = finally region.
    internal sealed record SynthRegion(string? TypeName, int TryOff, int TryLen, int HndOff, int HndLen, bool IsFinally);

    // Synthesize a real ECMA-335 assembly whose only method (Acme.Util::Nope,
    // static void) carries the given IL and the given exception region table.
    // The emit callback receives the InstructionEncoder plus the boundary
    // label map (offset -> label); it MUST mark every boundary label exactly
    // once at the corresponding offset (the regions' edges).
    internal static PEReader SynthesizeEh(Action<InstructionEncoder, Dictionary<int, LabelHandle>> emit, params SynthRegion[] regions)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("eh.dll"), mdb.GetOrAddGuid(Guid.NewGuid()), default, default);
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default, default, 0, default);
        var obj = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ex = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));
        var ioe = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("InvalidOperationException"));
        var ae = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ArgumentException"));

        // The code and the method body stream must be SEPARATE BlobBuilders:
        // AddMethodBody copies the code (with branch fixup) from the code
        // builder into the stream; a shared builder would copy into itself
        // and corrupt the layout.
        var codeBlob = new BlobBuilder();
        var cf = new ControlFlowBuilder();
        var inst = new InstructionEncoder(codeBlob, cf);

        // Boundary labels: one per distinct region edge offset.
        var boundaries = new Dictionary<int, LabelHandle>();
        LabelHandle At(int off)
        {
            if (!boundaries.TryGetValue(off, out var label))
            {
                label = inst.DefineLabel();
                boundaries[off] = label;
            }
            return label;
        }
        foreach (var r in regions)
        {
            At(r.TryOff); At(r.TryOff + r.TryLen); At(r.HndOff); At(r.HndOff + r.HndLen);
        }

        emit(inst, boundaries);

        foreach (var r in regions)
        {
            var tryStart = At(r.TryOff);
            var tryEnd = At(r.TryOff + r.TryLen);
            var hndStart = At(r.HndOff);
            var hndEnd = At(r.HndOff + r.HndLen);
            if (r.IsFinally)
                cf.AddFinallyRegion(tryStart, tryEnd, hndStart, hndEnd);
            else
            {
                var catchType = r.TypeName switch
                {
                    "Object" => obj,
                    "Exception" => ex,
                    "InvalidOperationException" => ioe,
                    "ArgumentException" => ae,
                    null => default(EntityHandle),
                    _ => throw new InvalidOperationException($"unknown catch type {r.TypeName}"),
                };
                cf.AddCatchRegion(tryStart, tryEnd, hndStart, hndEnd, catchType);
            }
        }

        var ilBuilder = new BlobBuilder();
        var bodyStream = new MethodBodyStreamEncoder(ilBuilder);
        var bodyOffset = bodyStream.AddMethodBody(inst, maxStack: 2);

        var method = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), // default, 0 params, void
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Util"), obj, default, method);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilBuilder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    private static MethodBodyInfo ReadBody(PEReader pe)
    {
        var md = pe.GetMetadataReader();
        var typeDef = new TypeDef { Name = "Util", Namespace = "Acme" };
        var methodDef = new MethodDef { Name = "Nope", Owner = typeDef, ParamCount = 0, ParamTypes = "" };
        return MethodBodyReader.Read(pe, md, typeDef, methodDef);
    }

    // try=[0,3) handler=[3,6): ldc.i4.0; leave.s END; pop; leave.s END; ret.
    // Both leaves target the chain end (6) -- the Roslyn handler shape.
    private static void EmitSingleCatch(InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        inst.MarkLabel(b[0]);                  // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);        // @0
        inst.Branch(ILOpCode.Leave_s, b[6]);   // @1 -> 6
        inst.MarkLabel(b[3]);                  // @3
        inst.OpCode(ILOpCode.Pop);             // @3
        inst.Branch(ILOpCode.Leave_s, b[6]);   // @4 -> 6
        inst.MarkLabel(b[6]);                  // @6
        inst.OpCode(ILOpCode.Ret);             // @6
    }

    [Fact]
    public void Decode_CatchAll_SystemObjectTypeRef_NormalizedToCatchAllMarker()
    {
        // `catch { }` (f19 shape): CatchType = System.Object TypeRef, not nil.
        // Pre-fix the raw TypeRef token survives -> the emission emits an
        // il2cpp_exception_matches("System.Object", ...) check that can never
        // match -> the handler never runs and the exception is rethrown.
        using var pe = SynthesizeEh(EmitSingleCatch, new SynthRegion("Object", 0, 3, 3, 3, false));
        var info = ReadBody(pe);

        var region = Assert.Single(info.ExceptionRegions);
        Assert.Equal(ExceptionRegionKind.Catch, region.Kind);
        Assert.Equal(0, region.CatchTypeToken); // catch-all marker
    }

    [Fact]
    public void Decode_TypedCatch_PreservesCatchTypeToken()
    {
        // A real typed catch must KEEP its token: the emission's type check
        // (il2cpp_exception_matches) is what dispatches typed catches.
        using var pe = SynthesizeEh(EmitSingleCatch, new SynthRegion("Exception", 0, 3, 3, 3, false));
        var info = ReadBody(pe);

        var region = Assert.Single(info.ExceptionRegions);
        Assert.NotEqual(0, region.CatchTypeToken);
    }

    // f1 shape: nested catch -- inner try=[0,0xB) handler=[0xB,0x18) IOE,
    // outer try=[0,0x1A) handler=[0x1A,0x27) Exception. Table order INNER
    // FIRST (Roslyn/ECMA sort); the decoder re-sorts outer-first.
    private static void EmitNestedCatch(InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        inst.MarkLabel(b[0x00]);                   // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);            // @0
        inst.Branch(ILOpCode.Leave_s, b[0x18]);    // @1 -> 0x18
        for (var i = 0; i < 8; i++) inst.OpCode(ILOpCode.Nop); // @3..0xA (pad inner try)
        inst.MarkLabel(b[0x0B]);                   // @0xB
        inst.OpCode(ILOpCode.Pop);                 // @0xB
        inst.Branch(ILOpCode.Leave_s, b[0x18]);    // @0xC -> 0x18
        for (var i = 0; i < 10; i++) inst.OpCode(ILOpCode.Nop); // @0xE..0x17 (pad inner handler)
        inst.MarkLabel(b[0x18]);                   // @0x18
        inst.Branch(ILOpCode.Leave_s, b[0x27]);    // @0x18 -> 0x27
        inst.MarkLabel(b[0x1A]);                   // @0x1A
        inst.OpCode(ILOpCode.Pop);                 // @0x1A
        inst.Branch(ILOpCode.Leave_s, b[0x27]);    // @0x1B -> 0x27
        for (var i = 0; i < 10; i++) inst.OpCode(ILOpCode.Nop); // @0x1D..0x26 (pad outer handler)
        inst.MarkLabel(b[0x27]);                   // @0x27
        inst.OpCode(ILOpCode.Ret);                 // @0x27
    }

    [Fact]
    public void Decode_NestedCatchRegions_OuterFirstInnerLast()
    {
        // Roslyn writes the table inner-first (ECMA-335 II.16.4.1 sort); the
        // emission walk picks the innermost region by last-match-wins, which
        // needs outer-first order -- otherwise the inner catch is dropped
        // (exdeep f1). The decoder re-sorts (stable: siblings keep dispatch
        // order).
        using var pe = SynthesizeEh(EmitNestedCatch,
            new SynthRegion("InvalidOperationException", 0x00, 0x0B, 0x0B, 0x0D, false), // inner, table-first
            new SynthRegion("Exception", 0x00, 0x1A, 0x1A, 0x0D, false)); // outer
        var info = ReadBody(pe);

        var regions = info.ExceptionRegions;
        Assert.Equal(2, regions.Count);
        Assert.Equal(0x1A, regions[0].TryLength); // outer first
        Assert.Equal(0x0B, regions[1].TryLength); // inner last
        Assert.Equal(0x1A, regions[0].HandlerOffset);
        Assert.Equal(0x0B, regions[1].HandlerOffset);
    }

    // CatchRet shape: catch try=[0,0xB) handler=[0xB,0x13) System.Object
    // (catch-all) + finally try=[0,0x13) handler=[0x13,0x1E). The finally
    // construct is OUTER (its try contains the catch's try).
    private static void EmitCatchThenFinally(InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        inst.MarkLabel(b[0x00]);                   // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);            // @0
        inst.Branch(ILOpCode.Leave_s, b[0x1E]);    // @1 -> 0x1E
        for (var i = 0; i < 8; i++) inst.OpCode(ILOpCode.Nop); // @3..0xA
        inst.MarkLabel(b[0x0B]);                   // @0xB
        inst.OpCode(ILOpCode.Pop);                 // @0xB
        inst.Branch(ILOpCode.Leave_s, b[0x1E]);    // @0xC -> 0x1E
        for (var i = 0; i < 5; i++) inst.OpCode(ILOpCode.Nop); // @0xE..0x12
        inst.MarkLabel(b[0x13]);                   // @0x13
        inst.OpCode(ILOpCode.Endfinally);          // @0x13
        for (var i = 0; i < 10; i++) inst.OpCode(ILOpCode.Nop); // @0x14..0x1D
        inst.MarkLabel(b[0x1E]);                   // @0x1E
        inst.OpCode(ILOpCode.Ret);                 // @0x1E
    }

    [Fact]
    public void Decode_CatchThenFinally_OutermostTryFirst()
    {
        // Table order (Roslyn): catch first, finally second. The finally's
        // try [0,0x13) contains the catch's try [0,0xB) -> outer-first decode
        // must yield [finally, catch].
        using var pe = SynthesizeEh(EmitCatchThenFinally,
            new SynthRegion("Object", 0x00, 0x0B, 0x0B, 0x08, false), // catch, table-first
            new SynthRegion(null, 0x00, 0x13, 0x13, 0x0B, true));   // finally (outer)
        var info = ReadBody(pe);

        var regions = info.ExceptionRegions;
        Assert.Equal(2, regions.Count);
        Assert.Equal(ExceptionRegionKind.Finally, regions[0].Kind); // outermost first
        Assert.Equal(ExceptionRegionKind.Catch, regions[1].Kind);
        Assert.Equal(0x13, regions[0].TryLength);
        Assert.Equal(0x0B, regions[1].TryLength);
    }

    // f17/f18 shape: sibling catch chain -- try=[0,6) handler1=[6,10),
    // handler2=[10,14), all leaves (try body + every handler) target the
    // chain end 14.
    private static void EmitSiblingChain(InstructionEncoder inst, Dictionary<int, LabelHandle> b, bool badHandler1Leave)
    {
        inst.MarkLabel(b[0]);                  // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);        // @0
        inst.Branch(ILOpCode.Leave_s, b[14]);  // @1 -> 14
        for (var i = 0; i < 3; i++) inst.OpCode(ILOpCode.Nop); // @3..5
        inst.MarkLabel(b[6]);                  // @6
        inst.OpCode(ILOpCode.Pop);             // @6 (h1 body)
        inst.Branch(ILOpCode.Leave_s, badHandler1Leave ? b[10] : b[14]); // @7 -> 10 (bad) or 14
        inst.OpCode(ILOpCode.Nop);             // @9
        inst.MarkLabel(b[10]);                 // @10
        inst.OpCode(ILOpCode.Pop);             // @10 (h2 body)
        inst.Branch(ILOpCode.Leave_s, b[14]);  // @11 -> 14
        inst.OpCode(ILOpCode.Nop);             // @13
        inst.MarkLabel(b[14]);                 // @14
        inst.OpCode(ILOpCode.Ret);             // @14
    }

    [Fact]
    public void Decode_SiblingCatchChain_SameTryExtentsDispatchOrderPreserved()
    {
        // Sibling catches share TryOffset/TryLength; the decode must keep
        // them in metadata (dispatch) order -- the type check cascade depends
        // on it.
        using var pe = SynthesizeEh((i, b) => EmitSiblingChain(i, b, false),
            new SynthRegion("Exception", 0, 6, 6, 4, false),
            new SynthRegion("InvalidOperationException", 0, 6, 10, 4, false));
        var info = ReadBody(pe);

        var regions = info.ExceptionRegions;
        Assert.Equal(2, regions.Count);
        Assert.Equal(0, regions[0].TryOffset);
        Assert.Equal(0, regions[1].TryOffset);
        Assert.Equal(6, regions[0].TryLength);
        Assert.Equal(6, regions[1].TryLength);
        Assert.Equal(6, regions[0].HandlerOffset);
        Assert.Equal(10, regions[1].HandlerOffset);
        Assert.NotEqual(regions[0].CatchTypeToken, regions[1].CatchTypeToken);
    }

    [Fact]
    public void Fold_SiblingChain_AllLeavesToChainEnd_NoOp()
    {
        // Every handler of a multi-catch construct ends with a leave to the
        // CHAIN end (exdeep f9/f17/f18 shape) -- the leave must fold to a
        // no-op instead of a loud reject. End-to-end through the DECODED
        // regions (real synthesized metadata).
        using var pe = SynthesizeEh((i, b) => EmitSiblingChain(i, b, false),
            new SynthRegion("Exception", 0, 6, 6, 4, false),
            new SynthRegion("InvalidOperationException", 0, 6, 10, 4, false));
        var info = ReadBody(pe);

        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(info.IlBytes),
            new MethodContext { ArgCount = 0, ReturnsVoid = true, ExceptionRegions = info.ExceptionRegions },
            pe.GetMetadataReader(), Array.Empty<TypeDef>());
        Assert.Single(stmts.OfType<RetStmt>());
        Assert.Empty(stmts.OfType<CallStmt>());
    }

    [Fact]
    public void Fold_SiblingChain_LeaveInsideChain_WarnsAndSkips()
    {
        using var pe = SynthesizeEh((i, b) => EmitSiblingChain(i, b, true),
            new SynthRegion("Exception", 0, 6, 6, 4, false),
            new SynthRegion("InvalidOperationException", 0, 6, 10, 4, false));
        var info = ReadBody(pe);

        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(info.IlBytes),
            new MethodContext { ArgCount = 0, ReturnsVoid = true, ExceptionRegions = info.ExceptionRegions },
            pe.GetMetadataReader(), Array.Empty<TypeDef>());
        Assert.Empty(stmts);
    }
}
//------------------------------------------------------------------------------
// EMISSION-side EH shape tests (exdeep gen/src/Cases_Exdeep.cpp failures):
// the C++ wrapper walk must produce PAIRED try/catch blocks and no bare
// `try {` / no `catch` inside a catch body.
//  - nested catch (f1/f7): a statement in the INNER handler AND the outer
//    try must open the outer catch INSIDE the outer try -- the pre-fix walk
//    opened the outer `try {` after the inner catch, leaving a bare try.
//  - finally + outer catch (f5/f6/f9/f20): the outer catch must open AFTER
//    the finally catch-all closes -- the pre-fix walk opened it inside the
//    catch-all (right after its `throw;` -> "expected expression").
//  - sibling chain (f17/f18): ONE try block, N catch clauses.
//  - empty catch-all (f19): the empty handler still gets a catch clause.
//  - catch-then-finally (CatchRet/f23): the catch handler stays inside the
//    finally's try; the finally catch-all closes before the code after.
// The bodies use `ldstr; call Console::WriteLine` pairs -- call statements
// SURVIVE the fold (constant stores are dead-store-eliminated), so each
// try/handler range carries a real statement with a SourceOffset the region
// walk keys on.
//------------------------------------------------------------------------------
public class EHRegionEmitTests
{
    private const string Catch = @"catch \(il2cpp::DotNetException& __il2cpp_ex\) \{";

    private static string GenerateCpp(Action<MetadataBuilder, InstructionEncoder, Dictionary<int, LabelHandle>> emit, params EHRegionTests.SynthRegion[] regions)
    {
        using var pe = SynthesizeEhStmts(emit, regions);
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_ehemit_" + Guid.NewGuid().ToString("N") + ".dll");
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_ehgen_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(tmp, pe.GetEntireImage().GetContent(0, pe.GetEntireImage().Length).ToArray());
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            return File.ReadAllText(Path.Combine(dir, "src", "Acme_Util.cpp"));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private static void AssertBalanced(string cpp)
    {
        Assert.Equal(Regex.Matches(cpp, @"\{").Count, Regex.Matches(cpp, @"\}").Count);
        // Multi-catch chains legitimately have more catch clauses than try
        // blocks; the real invariants are: no catch after a handler throw,
        // and every try has at least one catch (checked per shape below).
        Assert.True(Regex.Matches(cpp, @"try \{").Count <= Regex.Matches(cpp, Catch).Count);
        Assert.DoesNotMatch(new Regex(@"throw;[^\n]*\n\s*catch \(il2cpp"), cpp);
    }

    // Same shape as EHRegionTests.SynthesizeEh but the emit callback also
    // receives the MetadataBuilder (for ldstr user strings + the WriteLine
    // MemberRef) -- real call statements, not dead-store-eliminated stores.
    private static PEReader SynthesizeEhStmts(Action<MetadataBuilder, InstructionEncoder, Dictionary<int, LabelHandle>> emit, params EHRegionTests.SynthRegion[] regions)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("eh.dll"), mdb.GetOrAddGuid(Guid.NewGuid()), default, default);
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default, default, 0, default);
        var obj = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ex = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));
        var ioe = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("InvalidOperationException"));
        var ae = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("ArgumentException"));

        var codeBlob = new BlobBuilder();
        var cf = new ControlFlowBuilder();
        var inst = new InstructionEncoder(codeBlob, cf);

        var boundaries = new Dictionary<int, LabelHandle>();
        LabelHandle At(int off)
        {
            if (!boundaries.TryGetValue(off, out var label))
            {
                label = inst.DefineLabel();
                boundaries[off] = label;
            }
            return label;
        }
        foreach (var r in regions)
        {
            At(r.TryOff); At(r.TryOff + r.TryLen); At(r.HndOff); At(r.HndOff + r.HndLen);
        }

        emit(mdb, inst, boundaries);

        foreach (var r in regions)
        {
            var tryStart = At(r.TryOff);
            var tryEnd = At(r.TryOff + r.TryLen);
            var hndStart = At(r.HndOff);
            var hndEnd = At(r.HndOff + r.HndLen);
            if (r.IsFinally)
                cf.AddFinallyRegion(tryStart, tryEnd, hndStart, hndEnd);
            else
            {
                var catchType = r.TypeName switch
                {
                    "Object" => obj,
                    "Exception" => ex,
                    "InvalidOperationException" => ioe,
                    "ArgumentException" => ae,
                    null => default(EntityHandle),
                    _ => throw new InvalidOperationException($"unknown catch type {r.TypeName}"),
                };
                cf.AddCatchRegion(tryStart, tryEnd, hndStart, hndEnd, catchType);
            }
        }

        var ilBuilder = new BlobBuilder();
        var bodyStream = new MethodBodyStreamEncoder(ilBuilder);
        var bodyOffset = bodyStream.AddMethodBody(inst, maxStack: 2);

        var method = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Util"), obj, default, method);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, ilBuilder);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }

    // MemberRef: void [mscorlib]System.Console::WriteLine(string) -- the
    // MethodRefResolver maps it to icall_console_write_line_s.
    private static MemberReferenceHandle AddWriteLine(MetadataBuilder mdb)
    {
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default, default, 0, default);
        var console = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Console"));
        return mdb.AddMemberReference(console, mdb.GetOrAddString("WriteLine"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x01, 0x01, 0x0E })); // default, 1 param, void, string
    }

    // f1 shape: inner try=[0,0xC) handler=[0xC,0x19) IOE; outer try=[0,0x1A)
    // handler=[0x1A,0x27) Exception. Statements: call @0x05 (both trys),
    // call @0x12 (inner handler + outer try), call @0x20 (outer handler).
    private static void EmitNestedCatchStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x12 call
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x19]);
        inst.OpCode(ILOpCode.Nop);
        inst.MarkLabel(b[0x1A]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h2"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x20 call
        inst.Branch(ILOpCode.Leave_s, b[0x27]);
        inst.MarkLabel(b[0x27]);
        inst.OpCode(ILOpCode.Nop);
        inst.OpCode(ILOpCode.Nop);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_NestedCatch_OuterCatchWrapsWholeConstruct()
    {
        var cpp = GenerateCpp(EmitNestedCatchStmts,
            new EHRegionTests.SynthRegion("InvalidOperationException", 0x00, 0x0C, 0x0C, 0x0D, false), // inner, table-first
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x1A, 0x1A, 0x0D, false)); // outer
        AssertBalanced(cpp);

        // try(outer) { try(inner) { ... } catch(inner) { ... } } catch(outer) { ... }
        var m = Regex.Match(cpp,
            @"try \{\s*try \{\s*(?<innerBody>.*?)\}\s*" + Catch + @"\s*(?<innerCatch>.*?)\} // end catch handler\s*(?<gap>\})\s*" + Catch + @"\s*(?<outerCatch>.*?)\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "nested try/catch shape missing:\n" + cpp);
        Assert.DoesNotContain("try", m.Groups["innerCatch"].Value);
        Assert.Contains("InvalidOperationException", m.Groups["innerCatch"].Value);
        Assert.Contains("System.Exception", m.Groups["outerCatch"].Value);
        Assert.Equal("}", m.Groups["gap"].Value.Trim());
    }

    // f5 shape: finally try=[0,0xC) handler=[0xC,0xD) (empty finally);
    // outer catch Exception try=[0,0xD) handler=[0xD,0x1A).
    private static void EmitFinallyThenOuterCatch(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x1A]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Endfinally);
        inst.MarkLabel(b[0x0D]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x13 call
        inst.Branch(ILOpCode.Leave_s, b[0x1A]);
        inst.MarkLabel(b[0x1A]);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_FinallyThenOuterCatch_OuterCatchAfterCatchAllCloses()
    {
        var cpp = GenerateCpp(EmitFinallyThenOuterCatch,
            new EHRegionTests.SynthRegion(null, 0x00, 0x0C, 0x0C, 0x01, true), // finally, table-first
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x0D, 0x0D, 0x0D, false)); // outer catch
        AssertBalanced(cpp);

        // try(outer) { try(inner) { ... } catch { throw; } } catch(outer) { ... }
        var m = Regex.Match(cpp,
            @"try \{\s*try \{\s*(?<innerBody>.*?)\}\s*" + Catch + @"\s*(?<finallyCatch>.*?)\} // end catch handler\s*(?<gap>\})\s*" + Catch + @"\s*(?<outerCatch>.*?)\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "finally+outer-catch shape missing:\n" + cpp);
        Assert.Contains("throw", m.Groups["finallyCatch"].Value); // the finally catch-all rethrow
        Assert.DoesNotContain("catch", m.Groups["finallyCatch"].Value);
        Assert.Contains("System.Exception", m.Groups["outerCatch"].Value);
        Assert.Equal("}", m.Groups["gap"].Value.Trim());
    }

    // f17/f18 shape: sibling catches try=[0,0xC) h1=[0xC,0x19) h2=[0x19,0x26).
    private static void EmitSiblingChainStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x26]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x12 call
        inst.Branch(ILOpCode.Leave_s, b[0x26]);
        inst.MarkLabel(b[0x19]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h2"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x1F call
        inst.Branch(ILOpCode.Leave_s, b[0x26]);
        inst.MarkLabel(b[0x26]);
        inst.OpCode(ILOpCode.Nop);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_SiblingChain_SingleCatchWithDispatchChain()
    {
        // exdeep f18 root cause: sibling catch clauses sharing one try range
        // MUST dispatch through a SINGLE C++ catch + if/else-if chain. GCC
        // semantics (verified with a standalone probe on this toolchain): a
        // bare `throw;` inside a handler resumes the search AFTER the current
        // try-block's handler list -- remaining same-try clauses are never
        // considered ("-Wexceptions: will be caught by earlier handler"), so
        // N sibling `catch (DotNetException&)` clauses with guard-rethrow can
        // only ever reach the FIRST clause. The chained form evaluates every
        // clause in order and rethrows only when NONE matches.
        var cpp = GenerateCpp(EmitSiblingChainStmts,
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x0C, 0x0C, 0x0D, false),
            new EHRegionTests.SynthRegion("InvalidOperationException", 0x00, 0x0C, 0x19, 0x0D, false));
        AssertBalanced(cpp);

        Assert.Single(Regex.Matches(cpp, @"try \{"));
        Assert.Single(Regex.Matches(cpp, Catch));
        var m = Regex.Match(cpp,
            @"try \{\s*(?<body>.*?)\}\s*" + Catch + @"\s*" +
            @"if \(il2cpp::il2cpp_exception_matches\(""System\.Exception"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c1>.*?)\}\s*" +
            @"else if \(il2cpp::il2cpp_exception_matches\(""System\.InvalidOperationException"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c2>.*?)\}\s*else \{\s*" +
            @"throw; // no clause matched: rethrow \(unwinding continues\)\s*" +
            @"\}\s*\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "sibling dispatch chain shape missing:\n" + cpp);
        Assert.DoesNotContain("catch", m.Groups["c1"].Value);
        Assert.DoesNotContain("catch", m.Groups["c2"].Value);
        // T3 pooling: the ldstr contents live in string-pool.h, the method
        // body references them as g_strPool[id] (s1=0, h1=1, h2=2 -- pool
        // ids follow first-seen ldstr order). The write calls are the proof
        // that each dispatch target carries its own statement.
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[0])", m.Groups["body"].Value); // s1 write
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[1])", m.Groups["c1"].Value);   // h1 write
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[2])", m.Groups["c2"].Value);   // h2 write
    }

    // f18 shape: THREE sibling catches on one try (Exception /
    // InvalidOperationException / catch-all Object). The dispatch chain must
    // reach the SECOND clause (the pre-fix multi-catch guard-rethrow skipped
    // every clause after the first -> unhandled); the catch-all LAST clause
    // becomes the final `else` (no rethrow tail).
    private static void EmitTripleChainStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x33]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x12 call
        inst.Branch(ILOpCode.Leave_s, b[0x33]);
        inst.MarkLabel(b[0x19]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h2"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x1F call
        inst.Branch(ILOpCode.Leave_s, b[0x33]);
        inst.MarkLabel(b[0x26]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h3"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x2C call
        inst.Branch(ILOpCode.Leave_s, b[0x33]);
        inst.MarkLabel(b[0x33]);
        inst.OpCode(ILOpCode.Nop);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_TripleChain_SecondClauseReachable_CatchAllIsFinalElse()
    {
        var cpp = GenerateCpp(EmitTripleChainStmts,
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x0C, 0x0C, 0x0D, false),
            new EHRegionTests.SynthRegion("InvalidOperationException", 0x00, 0x0C, 0x19, 0x0D, false),
            new EHRegionTests.SynthRegion("Object", 0x00, 0x0C, 0x26, 0x0D, false));
        AssertBalanced(cpp);

        Assert.Single(Regex.Matches(cpp, @"try \{"));
        Assert.Single(Regex.Matches(cpp, Catch));
        // Three clause guards in dispatch order; the catch-all ends the chain
        // as a plain `else` with its body (no rethrow tail).
        var m = Regex.Match(cpp,
            @"catch \(il2cpp::DotNetException& __il2cpp_ex\) \{\s*" +
            @"if \(il2cpp::il2cpp_exception_matches\(""System\.Exception"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c1>.*?)\}\s*" +
            @"else if \(il2cpp::il2cpp_exception_matches\(""System\.InvalidOperationException"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c2>.*?)\}\s*else \{\s*" +
            @"(?<c3>.*?)\}\s*\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "triple dispatch chain shape missing:\n" + cpp);
        Assert.DoesNotContain("throw;", m.Groups["c3"].Value);
        // T3 pooling: ldstr contents are pooled (s1=0, h1=1, h2=2, h3=3 in
        // first-seen ldstr order); each clause's body references its write
        // via g_strPool[id].
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[1])", m.Groups["c1"].Value); // h1 write
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[2])", m.Groups["c2"].Value); // h2 write
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[3])", m.Groups["c3"].Value); // h3 write
    }

    // c8 shape: the FIRST sibling's handler is EMPTY (catch (Exception) { }),
    // the second clause carries the body. The dispatch chain must still
    // include the empty clause (a matching exception is swallowed there) and
    // reach the second.
    private static void EmitEmptyFirstSiblingStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop); // empty handler 1
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x0F]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h2"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x15 call
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x19]);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_EmptyFirstSibling_EmptyClauseKeptInChain()
    {
        var cpp = GenerateCpp(EmitEmptyFirstSiblingStmts,
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x0C, 0x0C, 0x03, false),
            new EHRegionTests.SynthRegion("InvalidOperationException", 0x00, 0x0C, 0x0F, 0x0A, false));
        AssertBalanced(cpp);

        Assert.Single(Regex.Matches(cpp, @"try \{"));
        Assert.Single(Regex.Matches(cpp, Catch));
        var m = Regex.Match(cpp,
            @"catch \(il2cpp::DotNetException& __il2cpp_ex\) \{\s*" +
            @"if \(il2cpp::il2cpp_exception_matches\(""System\.Exception"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c1>.*?)\}\s*" +
            @"else if \(il2cpp::il2cpp_exception_matches\(""System\.InvalidOperationException"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<c2>.*?)\}\s*else \{\s*" +
            @"throw; // no clause matched: rethrow \(unwinding continues\)\s*" +
            @"\}\s*\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "empty-first dispatch chain shape missing:\n" + cpp);
        // The empty first clause: nothing between its guard and the next
        // else-if (an exception matching it is swallowed by the empty body).
        Assert.DoesNotContain("h", m.Groups["c1"].Value);
        // T3 pooling: h2 is pooled (s1=0, h2=1 in first-seen ldstr order); the
        // second clause's write references g_strPool[1].
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[1])", m.Groups["c2"].Value); // h2 write
    }

    // f19 shape: catch-all try=[0,0xC) handler=[0xC,0xF) with NO handler
    // statements (only pop + leave).
    private static void EmitEmptyCatchAll(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x0F]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop);
        inst.Branch(ILOpCode.Leave_s, b[0x0F]);
        inst.MarkLabel(b[0x0F]);
        inst.OpCode(ILOpCode.Nop);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_EmptyCatchAll_StillGetsCatchClause()
    {
        var cpp = GenerateCpp(EmitEmptyCatchAll,
            new EHRegionTests.SynthRegion("Object", 0x00, 0x0C, 0x0C, 0x03, false)); // System.Object -> catch-all marker
        AssertBalanced(cpp);

        Assert.Single(Regex.Matches(cpp, @"try \{"));
        Assert.Single(Regex.Matches(cpp, Catch));
        Assert.Contains("(empty handler)", cpp);
        Assert.Matches(new Regex(@"try \{\s*(?<b>.*?)\}\s*" + Catch + @"\s*(?<c>.*?)\} // end catch handler \(empty handler\)",
            RegexOptions.Singleline), cpp);
    }

    // CatchRet/f23 shape: catch-all try=[0,0xC) handler=[0xC,0x19);
    // finally try=[0,0x19) handler=[0x19,0x1A) (empty finally). The catch
    // handler's call @0x12 is inside BOTH the catch handler and the finally
    // try -- the finally catch-all must open after the catch closes.
    private static void EmitCatchThenFinallyStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call
        inst.Branch(ILOpCode.Leave_s, b[0x19]);
        inst.MarkLabel(b[0x0C]);
        inst.OpCode(ILOpCode.Pop);
        inst.LoadString(mdb.GetOrAddUserString("h1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x12 call
        inst.Branch(ILOpCode.Leave_s, b[0x1A]);
        inst.MarkLabel(b[0x19]);
        inst.OpCode(ILOpCode.Endfinally);
        inst.MarkLabel(b[0x1A]);
        inst.OpCode(ILOpCode.Ret);
    }

    [Fact]
    public void Emit_CatchThenFinally_CatchHandlerStaysInsideFinallyTry()
    {
        var cpp = GenerateCpp(EmitCatchThenFinallyStmts,
            new EHRegionTests.SynthRegion("Object", 0x00, 0x0C, 0x0C, 0x0D, false), // catch, table-first
            new EHRegionTests.SynthRegion(null, 0x00, 0x19, 0x19, 0x01, true));   // finally (outer)
        AssertBalanced(cpp);

        Assert.Equal(2, Regex.Matches(cpp, @"try \{").Count);
        Assert.Equal(2, Regex.Matches(cpp, Catch).Count);
        var m = Regex.Match(cpp,
            @"try \{\s*try \{\s*(?<innerBody>.*?)\}\s*" + Catch + @"\s*(?<catchBody>.*?)\} // end catch handler\s*(?<gap>\})\s*" + Catch + @"\s*(?<finallyCatch>.*?)\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "catch-then-finally shape missing:\n" + cpp);
        Assert.DoesNotContain("try", m.Groups["catchBody"].Value);
        Assert.Equal("}", m.Groups["gap"].Value.Trim());
        Assert.Contains("throw", m.Groups["finallyCatch"].Value);
        Assert.DoesNotContain("catch", m.Groups["finallyCatch"].Value);
    }

    // FMT-2 shape: a sibling multi-catch chain nested INSIDE a catch body.
    // outer Exception catch try=[0,0xF) handler=[0xF,0x42); the chain try
    // [0x10,0x1C) with handlers [0x1C,0x29) IOE + [0x29,0x36) AE sits inside
    // the outer handler. Statements: call @0x05 (outer try), @0x15 (outer
    // handler + chain try), @0x22 (IOE handler), @0x2F (AE handler),
    // @0x3B (outer handler only). The pre-fix walk closed the outer catch
    // when the statement moved to the chain's SECOND sibling (the chain
    // continuation `continue` popped past the enclosing handler) -> the AE
    // clause, the dispatch tail and the outer catch were all mis-nested.
    private static void EmitChainInCatchStmts(MetadataBuilder mdb, InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        var wr = AddWriteLine(mdb);
        inst.MarkLabel(b[0x00]);
        inst.LoadString(mdb.GetOrAddUserString("s1"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x05 call (outer try)
        inst.OpCode(ILOpCode.Nop);               // @0x0A
        inst.OpCode(ILOpCode.Nop);               // @0x0B
        inst.OpCode(ILOpCode.Nop);               // @0x0C
        inst.Branch(ILOpCode.Leave_s, b[0x42]);  // @0x0D
        inst.MarkLabel(b[0x0F]);
        inst.OpCode(ILOpCode.Pop);               // @0x0F (outer handler)
        inst.MarkLabel(b[0x10]);
        inst.LoadString(mdb.GetOrAddUserString("s2"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x15 call (chain try)
        inst.Branch(ILOpCode.Leave_s, b[0x42]);  // @0x1A
        inst.MarkLabel(b[0x1C]);
        inst.OpCode(ILOpCode.Pop);               // @0x1C (IOE handler)
        inst.LoadString(mdb.GetOrAddUserString("s3"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x22 call (IOE handler)
        inst.Branch(ILOpCode.Leave_s, b[0x42]);  // @0x27
        inst.MarkLabel(b[0x29]);
        inst.OpCode(ILOpCode.Pop);               // @0x29 (AE handler)
        inst.LoadString(mdb.GetOrAddUserString("s4"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x2F call (AE handler)
        inst.Branch(ILOpCode.Leave_s, b[0x42]);  // @0x34
        inst.MarkLabel(b[0x36]);
        inst.LoadString(mdb.GetOrAddUserString("s5"));
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(wr)); // @0x3B call (outer handler)
        inst.Branch(ILOpCode.Leave_s, b[0x42]);  // @0x40
        inst.MarkLabel(b[0x42]);
        inst.OpCode(ILOpCode.Ret);               // @0x42
    }

    // FMT-2 indentation checker: every brace-bearing line must close EXACTLY
    // at the indent it opened. `try {` / `catch (...) {` / chain `if (...) {`
    // push their leading-space indent; `}` / `} // end catch handler` pop and
    // must reproduce it; merged `} else if (...) {` / `} else {` lines close
    // one body and reopen the next at the SAME indent. A violation is a
    // w.Open/w.Close pairing leak in the EH walk (a `}` or `catch` drifting
    // +4 per nesting level).
    private static void AssertEhIndentAligned(string cpp)
    {
        var stack = new Stack<int>();
        var lineNo = 0;
        foreach (var raw in cpp.Split('\n'))
        {
            lineNo++;
            var t = raw.TrimEnd('\r').Trim();
            if (t.Length == 0) continue;
            var indent = raw.Length - raw.TrimStart().Length;
            if (t.StartsWith("} else", StringComparison.Ordinal))
            {
                Assert.True(stack.Count > 0, $"unbalanced `}} else` at line {lineNo}:\n{cpp}");
                Assert.Equal(stack.Pop(), indent); // the previous body closes at its open indent
                stack.Push(indent);                // the next guard opens at the same indent
            }
            else if (t.StartsWith("}"))
            {
                Assert.True(stack.Count > 0, $"unbalanced `}}` at line {lineNo}:\n{cpp}");
                Assert.Equal(stack.Pop(), indent);
            }
            else if (t.EndsWith("{"))
            {
                stack.Push(indent);
            }
        }
        Assert.Empty(stack);
    }

    [Fact]
    public void Emit_ChainInCatch_ChainDispatchStaysInsideOuterCatch()
    {
        var cpp = GenerateCpp(EmitChainInCatchStmts,
            new EHRegionTests.SynthRegion("InvalidOperationException", 0x10, 0x0C, 0x1C, 0x0D, false), // chain, table-first
            new EHRegionTests.SynthRegion("ArgumentException", 0x10, 0x0C, 0x29, 0x0D, false),        // chain sibling
            new EHRegionTests.SynthRegion("Exception", 0x00, 0x0F, 0x0F, 0x33, false));              // outer (longest try -> first after re-sort)
        AssertBalanced(cpp);
        AssertEhIndentAligned(cpp);

        Assert.Equal(2, Regex.Matches(cpp, @"try \{").Count);
        Assert.Equal(2, Regex.Matches(cpp, Catch).Count);
        // try(outer) { s1 } catch(outer) { s2; try(chain) { s2 } catch {
        //   if(IOE) s3; else if(AE) s4; else rethrow; } s5; }
        var m = Regex.Match(cpp,
            @"try \{\s*(?<body>.*?)\}\s*" + Catch + @"\s*" +
            @"if \(!il2cpp::il2cpp_exception_matches\(""System\.Exception"", __il2cpp_ex\.type_name\(\)\)\) throw;[^\n]*\s*" +
            @"(?<outerBody>.*?)\}\s*" + Catch + @"\s*" +
            @"if \(il2cpp::il2cpp_exception_matches\(""System\.InvalidOperationException"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<ioe>.*?)\}\s*" +
            @"else if \(il2cpp::il2cpp_exception_matches\(""System\.ArgumentException"", __il2cpp_ex\.type_name\(\)\)\) \{\s*" +
            @"(?<ae>.*?)\}\s*else \{\s*" +
            @"throw; // no clause matched: rethrow \(unwinding continues\)\s*" +
            @"\}\s*\} // end catch handler\s*" +
            @"(?<after>.*?)\} // end catch handler",
            RegexOptions.Singleline);
        Assert.True(m.Success, "chain-in-catch shape missing:\n" + cpp);
        // T3 pooling: s1=0, s2=1, s3=2, s4=3, s5=4 in first-seen ldstr order.
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[0])", m.Groups["body"].Value);    // s1
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[1])", m.Groups["outerBody"].Value); // s2 (chain try)
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[2])", m.Groups["ioe"].Value);       // s3
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[3])", m.Groups["ae"].Value);        // s4
        Assert.Contains("icall_console_write_line_s(il2cpp::g_strPool[4])", m.Groups["after"].Value);     // s5: STILL inside the outer catch
        Assert.DoesNotContain("try", m.Groups["after"].Value);
        Assert.DoesNotContain("catch", m.Groups["after"].Value);

        // FMT-2 indent: the chain's catch/close and the trailing s5 must sit
        // one level inside the outer catch; every close lands on its opener.
        var outerCatch = Regex.Match(cpp, @"(?m)^(?<indent>\s*)" + Catch + @"\s*$");
        Assert.True(outerCatch.Success, "outer catch line missing:\n" + cpp);
        var outerIndent = outerCatch.Groups["indent"].Value.Length;
        var chainCatch = Regex.Match(cpp,
            @"(?m)^(?<indent>\s*)" + Catch + @"\s*$" +
            @"\n\s*if \(il2cpp::il2cpp_exception_matches\(""System\.InvalidOperationException""");
        Assert.True(chainCatch.Success, "chain catch line missing:\n" + cpp);
        var chainIndent = chainCatch.Groups["indent"].Value.Length;
        Assert.Equal(outerIndent + 4, chainIndent); // the chain is nested inside the outer catch body
        var s5 = Regex.Match(cpp, @"(?m)^(?<indent>\s*)il2cpp::runtime::icall_console_write_line_s\(il2cpp::g_strPool\[4\]\)");
        Assert.True(s5.Success, "s5 line missing:\n" + cpp);
        Assert.Equal(chainIndent, s5.Groups["indent"].Value.Length); // s5 in the outer catch body, not the chain
        var closes = Regex.Matches(cpp, @"(?m)^(?<indent>\s*)\} // end catch handler");
        Assert.Equal(2, closes.Count);
        Assert.Equal(chainIndent, closes[0].Groups["indent"].Value.Length); // chain closes at ITS catch indent
        Assert.Equal(outerIndent, closes[1].Groups["indent"].Value.Length); // outer catch closes at ITS indent
    }
}

//------------------------------------------------------------------------------
// EMISSION-side base-ctor test (exdeep MyException.cpp build failure):
// a user exception ctor calling the BCL base ctor (Exception::.ctor(string))
// must emit il2cpp_object_ctor_nop(THIS) -- the runtime no-op takes ONLY the
// receiver (il2cpp_object_ctor_nop(Il2CppObject* this_)); the message args
// are the throw carrier's job. Pre-fix the drop-cast walk dropped the
// receiver and passed the message instead (or nothing for the parameterless
// base ctor) -> "too few arguments to function call, single argument 'this_'
// was not specified".
//------------------------------------------------------------------------------
public class EHBaseCtorTests
{
    private static string GenerateMyExceptionCpp(int ctorParamCount)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("eh.dll"), mdb.GetOrAddGuid(Guid.NewGuid()), default, default);
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default, default, 0, default);
        var obj = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ex = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));

        // MemberRef: System.Exception::.ctor(string) or .ctor().
        var baseCtor = mdb.AddMemberReference(ex, mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(ctorParamCount == 1
                ? new byte[] { 0x00, 0x01, 0x01, 0x0E }
                : new byte[] { 0x00, 0x00, 0x01 }));

        // MyException ctor body: ldarg.0; [ldarg.1]; call base.ctor; ret.
        var codeBlob = new BlobBuilder();
        var inst = new InstructionEncoder(codeBlob);
        inst.OpCode(ILOpCode.Ldarg_0);
        if (ctorParamCount == 1) inst.OpCode(ILOpCode.Ldarg_1);
        inst.OpCode(ILOpCode.Call);
        inst.Token(MetadataTokens.GetToken(baseCtor));
        inst.OpCode(ILOpCode.Ret);
        codeBlob.Align(4);
        var stream = new MethodBodyStreamEncoder(codeBlob);
        var bodyOffset = stream.AddMethodBody(inst, 2, default(StandaloneSignatureHandle), MethodBodyAttributes.None);

        var ctor = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            mdb.GetOrAddString(".ctor"),
            mdb.GetOrAddBlob(ctorParamCount == 1
                ? new byte[] { 0x20, 0x01, 0x01, 0x0E } // instance, 1 param, void, string
                : new byte[] { 0x20, 0x00, 0x01 }),
            bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public, mdb.GetOrAddString("Acme"),
            mdb.GetOrAddString("MyException"), ex, default, ctor);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, codeBlob);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);

        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_ctor_" + Guid.NewGuid().ToString("N") + ".dll");
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_ctorgen_" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(tmp, blob.ToArray());
            var types = MetadataLoader.LoadAssembly(tmp);
            CodeGenerator.Generate(types, dir, tmp);
            return File.ReadAllText(Path.Combine(dir, "src", "Acme_MyException.cpp"));
        }
        finally
        {
            File.Delete(tmp);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Emit_BaseCtorWithMessage_PassesReceiverOnly()
    {
        var cpp = GenerateMyExceptionCpp(1);
        Assert.Contains("il2cpp_object_ctor_nop(arg0)", cpp);
        Assert.DoesNotContain("il2cpp_object_ctor_nop(arg1)", cpp);
        Assert.DoesNotContain("il2cpp_object_ctor_nop()", cpp);
    }

    [Fact]
    public void Emit_ParameterlessBaseCtor_PassesReceiver()
    {
        var cpp = GenerateMyExceptionCpp(0);
        Assert.Contains("il2cpp_object_ctor_nop(arg0)", cpp);
        Assert.DoesNotContain("il2cpp_object_ctor_nop()", cpp);
    }
}
