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
using Xunit;

//------------------------------------------------------------------------------
// MethodBodyReader unit coverage (audit mbr1) -- LOCAL VARIABLE SIGNATURE +
// EXCEPTION CLAUSE FORMS + MAXSTACK + CODE-SEGMENT BOUNDARY.
//
// LocalVarSig (ECMA-335 II.23.2.6): LOCAL_SIG(0x07) + compressed count + one
// type per slot. Each local type is consumed with ALL its trailing data so the
// kinds stay index-aligned with the IL slot numbers (the emitter declares
// int64_t/double/Il2CppObject*/int32_t from these kinds -- a misalignment
// silently declares the wrong slot width).
//   * multidim ARRAY (0x14): element + rank + sizes/lobounds lists -> "r".
//     The matrixcase probe E wall (formerly "multidim-array/function-pointer
//     local ... not supported" @MethodBodyReader.cs:193) is now decoded
//     (multidim-probe.md fix direction: "0x14 per-reference slot 'r' + full consumption").
//   * ELEMENT_TYPE_I (0x18, native int) / ELEMENT_TYPE_U (0x19, native uint):
//     single-byte, no trailing -> "ptr" (intptr_t) / "uptr" (uintptr_t). The
//     pointer width is authoritative: an assignment-free `out IntPtr p;` slot
//     was declared int32_t and silently truncated (pre-fix 0x19 was wrongly
//     lumped with multidim and REJECTED).
//   * PTR (0x0F) / BYREF (0x10): the element type byte follows and must be
//     consumed (pre-fix the element leaked into the next local).
//   * PINNED (0x45, `fixed` buffers): modifier wrapping the next type.
//   * FNPTR (0x1B): trailing method signature -- rejected LOUD (pre-fix it
//     silently fell through and misaligned every later local).
//
// EH clause kinds (ECMA-335 II.16.4.1): typed catch (0x00) / filter (0x01) /
// finally (0x02) / fault (0x04). filter + finally + catch are decoded (the
// fold rejects a filter clause's Endfilter LOUDLY -- StackFold.cs Endfilter
// case); fault is rejected LOUD at the reader (the emission layer has no fault
// model -- decoding would mis-emit it as a swallowing catch-all).
//
// maxstack: the method-header MaxStack (ECMA-335 II.15.4.3.2) is now surfaced
// on MethodBodyInfo (previously unread).
//------------------------------------------------------------------------------
public class MethodBodyReaderTests
{
    // One exception clause to synthesize. TypeName: catch type simple name
    // ("Object"/"Exception"); null + Catch = nil catch type (catch-all);
    // Filter/Finally/Fault ignore TypeName. FilterOff (filter clauses only):
    // the offset of the filter code, which precedes the catch HANDLER
    // [HndOff, HndOff+HndLen) and ends in endfilter (ECMA-335 II.16.4.1).
    internal sealed record SynthClause(ExceptionRegionKind Kind, int TryOff, int TryLen, int HndOff, int HndLen, string? TypeName, int? FilterOff = null);

    // Synthesize a real ECMA-335 assembly whose only method (Acme.Util::Nope,
    // static void) carries the given IL, optional local-signature blob, maxStack
    // and exception-region table. localSigBytes == null => no local signature.
    internal static PEReader Synthesize(
        Action<InstructionEncoder, Dictionary<int, LabelHandle>> emit,
        int maxStack,
        byte[]? localSigBytes,
        params SynthClause[] clauses)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("mbr.dll"), mdb.GetOrAddGuid(Guid.NewGuid()), default, default);
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default, default, 0, default);
        var obj = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var ex = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Exception"));

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
        foreach (var c in clauses)
        {
            At(c.TryOff); At(c.TryOff + c.TryLen); At(c.HndOff); At(c.HndOff + c.HndLen);
            if (c.Kind == ExceptionRegionKind.Filter) At(c.FilterOff ?? c.HndOff);
        }

        emit(inst, boundaries);

        foreach (var c in clauses)
        {
            var tryStart = At(c.TryOff);
            var tryEnd = At(c.TryOff + c.TryLen);
            var hndStart = At(c.HndOff);
            var hndEnd = At(c.HndOff + c.HndLen);
            switch (c.Kind)
            {
                case ExceptionRegionKind.Finally:
                    cf.AddFinallyRegion(tryStart, tryEnd, hndStart, hndEnd);
                    break;
                case ExceptionRegionKind.Filter:
                    // AddFilterRegion(tryStart, tryEnd, handlerStart, handlerEnd, filterStart)
                    cf.AddFilterRegion(tryStart, tryEnd, hndStart, hndEnd, At(c.FilterOff ?? c.HndOff));
                    break;
                case ExceptionRegionKind.Fault:
                    cf.AddFaultRegion(tryStart, tryEnd, hndStart, hndEnd);
                    break;
                default:
                {
                    var catchType = c.TypeName switch
                    {
                        "Object" => obj,
                        "Exception" => ex,
                        null => default(EntityHandle),
                        _ => throw new InvalidOperationException($"unknown catch type {c.TypeName}"),
                    };
                    cf.AddCatchRegion(tryStart, tryEnd, hndStart, hndEnd, catchType);
                    break;
                }
            }
        }

        var ilBuilder = new BlobBuilder();
        var bodyStream = new MethodBodyStreamEncoder(ilBuilder);
        var locSig = localSigBytes == null
            ? default
            : mdb.AddStandaloneSignature(mdb.GetOrAddBlob(localSigBytes));
        var bodyOffset = bodyStream.AddMethodBody(inst, maxStack, locSig);

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

    private static void EmitRet(InstructionEncoder inst, Dictionary<int, LabelHandle> b) => inst.OpCode(ILOpCode.Ret);

    //--------------------------------------------------------------------------
    // Local variable signature (LocalVarSig 0x07)
    //--------------------------------------------------------------------------

    [Fact]
    public void LocalSig_Leading07_CountAndTypeBytes_KindI()
    {
        // 0x07 + count 1 + bool(0x02): the bool slot is a plain int32 slot.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x02 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "loc0" }, info.LocNames);
        Assert.Equal(new[] { "i" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_StringSlot_0E_KindR()
    {
        // ELEMENT_TYPE_STRING (0x0E): managed reference slot.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_BareVar_ConsumesIndex_KindCarriesIndex()
    {
        // GN-15: a bare VAR (0x13) local inside a closed clone must surface the
        // generic-parameter INDEX so the emitter can classify the slot
        // per-argument (Pair<int[,],int>: VAR0 -> reference, VAR1 -> value).
        // locSig {07 01 13 00}: one local, VAR index 0.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x13, 0x00 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "v#0" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_BareMvar_IndexConsumed_LegacyKind()
    {
        // GN-15: MVAR (0x1E) is a METHOD-level parameter (GN-14 convention:
        // -1, no class parts) -- it keeps the legacy whole-suffix "v" kind.
        // locSig {07 01 1E 00}: one local, MVAR index 0.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x1E, 0x00 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "v" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_MixedPrimitives_WidthKindsAligned()
    {
        // i4("i") / i8("l") / r4("d") / bool("i") -- the int64/double widths the
        // emitter declares int64_t/double from.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x04, 0x08, 0x0A, 0x0C, 0x02 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i", "l", "d", "i" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_MultidimArray_NoSizes_TwoLobounds_KindR()
    {
        // int[,]: 0x14 elem=i4(0x08) rank=2 sizes=0 lobounds=2(0,0). matrixcase
        // probe E form (Roslyn: "14 08 02 00 02 00 00").
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x14, 0x08, 0x02, 0x00, 0x02, 0x00, 0x00 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_MultidimArray_WithSizesAndLobounds_KindR()
    {
        // int[2,3]: 0x14 08 02 | sizes 02(02 03) | lobounds 02(00 00).
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x14, 0x08, 0x02, 0x02, 0x02, 0x03, 0x02, 0x00, 0x00 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_MultidimArray_SzArrayElement_KindR()
    {
        // string[,]: 0x14 elem=SZARRAY(0x1D) string(0x0E) rank=2 sizes=0 lobounds=0.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x14, 0x1D, 0x0E, 0x02, 0x00, 0x00, 0x00 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_MultidimArray_TrailingListsConsumed_NextLocalAligned()
    {
        // loc0 = int[,] ("r"), loc1 = string (0x0E). The lobounds must be
        // consumed so loc1 decodes from the 0x0E byte -- a stale position would
        // decode the string slot from inside the lobound list.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x02, 0x14, 0x08, 0x02, 0x00, 0x02, 0x00, 0x00, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "r", "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_NativeUint_0x19_KindUptr()
    {
        // ELEMENT_TYPE_U (native uint, 0x19): single-byte, no trailing data.
        // The slot is the UNSIGNED pointer carrier (uintptr_t), not int32_t.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x19 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "uptr" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_NativeInt_0x18_KindPtr()
    {
        // ELEMENT_TYPE_I (native int, 0x18): single-byte, no trailing data --
        // the full-width intptr_t carrier (an assignment-free out-slot was
        // int32_t before the fix).
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x18 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "ptr" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_Erratum_0x17_KindPtr()
    {
        // 0x17 (the ECMA-335 PRINTED-table I byte, never emitted by Roslyn)
        // classifies with 0x18 so a hand-built blob cannot degrade to int32_t.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x17 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "ptr" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_Pointer_ElementConsumed_NextLocalAligned()
    {
        // loc0 = int* (0x0F 0x08), loc1 = string (0x0E). Pre-fix 0x0F fell
        // through WITHOUT consuming the 0x08 element -> the string slot decoded
        // from the element byte.
        // The element consumption is what this test pins (loc1 must be "r").
        // The pointer slot itself is "ptr" (the intptr_t carrier): an unsafe
        // pointer IS a raw address, so declaring it int32_t truncated every
        // x64 address at `(long)p`, pointer arithmetic and the P/Invoke arg
        // bridge (ptr-pinvoke).
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x02, 0x0F, 0x08, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "ptr", "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_ByRef_ElementConsumed_NextLocalAligned()
    {
        // loc0 = ref int (0x10 0x08), loc1 = string (0x0E).
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x02, 0x10, 0x08, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i", "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_Pinned_DelegatesToElement_NextLocalAligned()
    {
        // loc0 = pinned byte (0x45 0x05), loc1 = string (0x0E) -- the PINNED
        // modifier wraps the next type; the slot keeps the element's kind.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x02, 0x45, 0x05, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i", "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_FunctionPointer_Consumed_NativeIntSlot()
    {
        // delegate*<int,int> local: 0x1B conv(0) count(2) ret(i4) params(i4,i4).
        // The trailing method signature is consumed and the slot is a native-int
        // pointer ("i") -- the emitter declares it void* from the FuncPtrExpr store.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x1B, 0x00, 0x02, 0x08, 0x08, 0x08 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_FunctionPointer_NextLocalAligned()
    {
        // loc0 = delegate*<int,int> (0x1B 00 02 08 08 08), loc1 = string (0x0E).
        // If the fnptr signature is NOT consumed, the 0x0E would be parsed
        // inside the signature and the loc1 slot misaligns.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x02, 0x1B, 0x00, 0x02, 0x08, 0x08, 0x08, 0x0E });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i", "r" }, info.LocalKinds);
    }

    [Fact]
    public void LocalSig_Non07LeadingByte_RejectsLoud()
    {
        // A method-signature blob (default calling convention 0x00) is not a
        // local signature.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x00, 0x00, 0x01 });
        var ex = Assert.Throws<NotSupportedException>(() => ReadBody(pe));
        Assert.Contains("0x07", ex.Message);
    }

    [Fact]
    public void Decode_NilLocalSignature_EmptyLocals()
    {
        // No StandaloneSignature handle on the body: no locals.
        using var pe = Synthesize(EmitRet, 2, null);
        var info = ReadBody(pe);
        Assert.Empty(info.LocNames);
        Assert.Empty(info.LocalKinds);
    }

    //--------------------------------------------------------------------------
    // Code-segment boundary tolerance
    //--------------------------------------------------------------------------

    [Fact]
    public void Decode_BodyBoundary_TinyCodeSegment_IlBytesAndLocalsBothDecoded()
    {
        // A 1-byte body (ret) + a 1-local signature: the reader must decode BOTH
        // the code segment and the local table without any boundary coupling.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x01, 0x08 });
        var info = ReadBody(pe);
        Assert.Equal(new byte[] { 0x2A }, info.IlBytes); // ret
        Assert.Equal(new[] { "i" }, info.LocalKinds);
    }

    [Fact]
    public void Decode_BodyBoundary_LocalSigDeclaresMoreLocalsThanCode()
    {
        // A 3-local signature on a 1-byte body: compilers emit unused locals;
        // the reader must decode ALL of them, not stop at the code length.
        using var pe = Synthesize(EmitRet, 2, new byte[] { 0x07, 0x03, 0x08, 0x08, 0x08 });
        var info = ReadBody(pe);
        Assert.Equal(new[] { "i", "i", "i" }, info.LocalKinds);
    }

    //--------------------------------------------------------------------------
    // maxstack
    //--------------------------------------------------------------------------

    [Theory]
    [InlineData(9)]
    [InlineData(16)]
    public void Decode_MaxStack_FatBody_RoundTrips(int maxStack)
    {
        // A body whose maxStack EXCEEDS 8 gets a FAT header (ECMA-335 II.25.4.5:
        // tiny bodies carry no maxStack field -- maxstack <= 8 implies the tiny
        // format) and the declared value is preserved exactly.
        using var pe = Synthesize(EmitRet, maxStack, null);
        var info = ReadBody(pe);
        Assert.Equal(maxStack, info.MaxStack);
    }

    [Fact]
    public void Decode_MaxStack_TinyBody_ReportsImpliedEight()
    {
        // ECMA-335 II.25.4.5 tiny format: a body with code < 64 bytes, maxstack
        // <= 8, no locals and no exception regions has NO maxStack field -- the
        // CLI assumes maxstack 8. The reader must report the implied value (the
        // encoder writes 8, never a smaller declared value).
        using var pe = Synthesize(EmitRet, 2, null);
        var info = ReadBody(pe);
        Assert.Equal(8, info.MaxStack);
    }

    //--------------------------------------------------------------------------
    // Exception clause kinds 0x00-0x04
    //--------------------------------------------------------------------------

    // filter shape: try=[0,3) filter code @3, catch handler=[6,8). Body:
    // ldc.i4.0 @0; leave.s -> 8 @1; filter body ldc.i4.1 @3; endfilter @4
    // (2 bytes); handler pop @6 + nop @7; ret @8. (The filter code always ends
    // with endfilter -- the fold's gate.)
    private static void EmitFilter(InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        inst.MarkLabel(b[0]);                  // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);        // @0
        inst.Branch(ILOpCode.Leave_s, b[8]);   // @1 -> 8
        inst.MarkLabel(b[3]);                  // @3 filter code
        inst.OpCode(ILOpCode.Ldc_i4_1);        // @3
        inst.OpCode(ILOpCode.Endfilter);       // @4 (FE 11)
        inst.MarkLabel(b[6]);                  // @6 catch handler
        inst.OpCode(ILOpCode.Pop);             // @6
        inst.OpCode(ILOpCode.Nop);             // @7
        inst.MarkLabel(b[8]);                  // @8
        inst.OpCode(ILOpCode.Ret);             // @8
    }

    [Fact]
    public void Decode_FilterRegion_ParsedWithFilterKind()
    {
        // filter clause (0x01, C# `catch when`): must PARSE (not reject) -- the
        // reader is parse-complete for valid ECMA IL; the semantic gate lives in
        // the fold (Endfilter, asserted in the test below).
        using var pe = Synthesize(EmitFilter, 2, null,
            new SynthClause(ExceptionRegionKind.Filter, 0, 3, 6, 2, null, FilterOff: 3));
        var info = ReadBody(pe);
        var region = Assert.Single(info.ExceptionRegions);
        Assert.Equal(ExceptionRegionKind.Filter, region.Kind);
        Assert.Equal(0, region.TryOffset);
        Assert.Equal(3, region.TryLength);
        Assert.Equal(3, region.FilterOffset); // the filter code start is surfaced
        Assert.Equal(6, region.HandlerOffset); // the catch HANDLER follows the filter code
        Assert.Equal(2, region.HandlerLength);
        Assert.Equal(0, region.CatchTypeToken); // a filter clause has no catch type
    }

    [Fact]
    public void Fold_FilterRegion_EndfilterRejectedLoud()
    {
        // Decoding a filter clause must NOT silently reach the emitter: the
        // filter body's endfilter makes the fold throw LOUD (no EH model for
        // filter clauses), keeping the pipeline fail-fast.
        using var pe = Synthesize(EmitFilter, 2, null,
            new SynthClause(ExceptionRegionKind.Filter, 0, 3, 6, 2, null, FilterOff: 3));
        var info = ReadBody(pe);

        var ex = Assert.Throws<NotSupportedException>(() => StackFold.Fold(
            IlDecoder.DecodeMethod(info.IlBytes),
            new MethodContext { ArgCount = 0, ReturnsVoid = true, ExceptionRegions = info.ExceptionRegions },
            pe.GetMetadataReader(), Array.Empty<TypeDef>()));
        Assert.Contains("endfilter", ex.Message);
    }

    // fault shape: try=[0,3) fault handler=[3,6). Body: ldc.i4.0 @0; leave.s ->
    // 6 @1; endfinally @3; nop @4; nop @5; ret @6.
    private static void EmitFault(InstructionEncoder inst, Dictionary<int, LabelHandle> b)
    {
        inst.MarkLabel(b[0]);                  // @0
        inst.OpCode(ILOpCode.Ldc_i4_0);        // @0
        inst.Branch(ILOpCode.Leave_s, b[6]);   // @1 -> 6
        inst.MarkLabel(b[3]);                  // @3
        inst.OpCode(ILOpCode.Endfinally);      // @3
        inst.OpCode(ILOpCode.Nop);             // @4
        inst.OpCode(ILOpCode.Nop);             // @5
        inst.MarkLabel(b[6]);                  // @6
        inst.OpCode(ILOpCode.Ret);             // @6
    }

    [Fact]
    public void Decode_FaultRegion_RejectsLoud()
    {
        // fault clause (0x04): no emission model exists (a fault handler = cleanup
        // on the exceptional path only; decoding it would mis-emit it as a
        // swallowing catch-all) -- the reader stays LOUD with a precise message.
        using var pe = Synthesize(EmitFault, 2, null,
            new SynthClause(ExceptionRegionKind.Fault, 0, 3, 3, 3, null));
        var ex = Assert.Throws<NotSupportedException>(() => ReadBody(pe));
        Assert.Contains("fault", ex.Message);
        Assert.Contains("try=", ex.Message);
    }
}
