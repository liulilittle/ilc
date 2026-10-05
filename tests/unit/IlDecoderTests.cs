using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

public class IlDecoderTests
{
    [Fact]
    public void Decode_CalcAdd_HasExpectedOpcodeSequence()
    {
        // Calc.Program.Add(int a, int b) => a + b; the actual IL bytes of this
        // method in calc.dll are 02 03 58 2A = ldarg.0; ldarg.1; add; ret
        // (fixtures rebuilt by scripts/build-fixtures.ps1).
        var types = MetadataLoader.LoadAssembly(FixturePath());
        Assert.Contains(types, t => t.FullName == "Calc.Program" && t.Methods.Any(m => m.Name == "Add"));

        var insns = IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x58, 0x2A });
        Assert.NotEmpty(insns);
        Assert.Equal(4, insns.Count);
        Assert.Equal(ILOpCode.Ldarg_0, insns[0].OpCode);
        Assert.Equal(ILOpCode.Ldarg_1, insns[1].OpCode);
        Assert.Equal(ILOpCode.Add, insns[2].OpCode);
        Assert.Equal(ILOpCode.Ret, insns[3].OpCode);
        Assert.All(insns, i => Assert.Equal(IlOperandKind.None, i.OperandKind));
    }

    [Fact]
    public void Decode_ShortBranch_TargetIsNextInstructionRelative()
    {
        // Real branch sequence: 02 16 2C 02 17 2A 16 2A
        //   ldarg.0@0, ldc.i4.0@1, brfalse.s@2(operand 02), ldc.i4.1@4, ret@5, ldc.i4.0@6, ret@7
        // The short-branch operand is relative to the "next instruction" (end of this
        // instruction = offset+2), target = 2 + 2 + 2 = 6.
        // Before the fix the formula offset+1+ReadSByte gave 5 (landing inside ret@5);
        // this assertion failed (RED).
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x02, 0x16, 0x2C, 0x02, 0x17, 0x2A, 0x16, 0x2A });

        var br = insns.Single(i => i.OpCode == ILOpCode.Brfalse_s);
        Assert.Equal(2, br.Offset);
        Assert.Equal(IlOperandKind.BranchTarget, br.OperandKind);
        Assert.Equal(6, br.Operand);

        // The target must land on a real instruction (the ldc.i4.0@6 on the return-0 path)
        var target = insns.Single(i => i.Offset == 6);
        Assert.Equal(ILOpCode.Ldc_i4_0, target.OpCode);
    }

    [Fact]
    public void Decode_LongBranch_TargetIsNextInstructionRelative()
    {
        // Hand-built long-format branch (forward jump distance >127, the compiler
        // must use the long format):
        //   ldc.i4.0@0, brfalse@1(operand 0x82=130, long format 4 bytes),
        //   130×nop@6..135, ldc.i4.1@136, ret@137
        // The long-branch target = 1 + 5 + 130 = 136. Before the fix the formula
        // offset+4+ReadInt32 gave 135 (landing on a nop); this assertion failed (RED).
        var il = new byte[138];
        il[0] = 0x16;          // ldc.i4.0
        il[1] = 0x39;          // brfalse
        il[2] = 0x82;          // +130 (little-endian)
        // il[6..135] default 0x00 = nop (130 of them)
        il[136] = 0x17;        // ldc.i4.1
        il[137] = 0x2A;        // ret

        var insns = IlDecoder.DecodeMethod(il);

        var br = insns.Single(i => i.OpCode == ILOpCode.Brfalse);
        Assert.Equal(1, br.Offset);
        Assert.Equal(IlOperandKind.BranchTarget, br.OperandKind);
        Assert.Equal(136, br.Operand);

        var target = insns.Single(i => i.Offset == 136);
        Assert.Equal(ILOpCode.Ldc_i4_1, target.OpCode);
    }

    [Fact]
    public void Decode_Constrained_PairsIntoNoopMarker_ReadsTypeTokenOperand()
    {
        // constrained. prefix (0xFE16, ECMA III.4.2): the standard Roslyn shape for
        // virtual calls on value types like Guid (guidcase.exe ildump verified:
        // ldloca + constrained. System.Guid + callvirt Object::ToString). The operand
        // is a 32-bit TypeDefOrRef token (guidcase live 0x0100000D = TypeRef
        // System.Guid).
        // The prefix has no stack effect — the decoder pairs it at decode time and
        // emits it as a Nop marker (operand preserved).
        // Before the fix 0xFE16 had no switch case → default threw NotSupported
        // (IlDecoder.cs:251), blocking the guidcase transpile (e2e-recheck4 G3).
        var insns = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x16, 0x0D, 0x00, 0x00, 0x01 });

        var marker = Assert.Single(insns);
        Assert.Equal(0, marker.Offset);
        Assert.Equal(ILOpCode.Nop, marker.OpCode); // pairing marker: prefix has no standalone execution semantics
        Assert.Equal(IlOperandKind.TypeToken, marker.OperandKind);
        Assert.Equal(0x0100000D, marker.Operand);
    }

    [Fact]
    public void Decode_LdlocaConstrainedCallvirt_KeepsStreamAligned()
    {
        // Compact byte sequence of the live guidcase shape:
        //   ldloca.s 0 @0 (12 00)
        //   constrained. System.Guid @2 (FE 16 0D 00 00 01)
        //   callvirt Object::ToString @8 (6F 0C 00 00 0A)
        //   ret @13 (2A)
        // After prefix pairing the subsequent instruction offsets must still land on
        // real byte positions (callvirt@8, ret@13).
        var insns = IlDecoder.DecodeMethod(new byte[]
        {
            0x12, 0x00,
            0xFE, 0x16, 0x0D, 0x00, 0x00, 0x01,
            0x6F, 0x0C, 0x00, 0x00, 0x0A,
            0x2A,
        });

        Assert.Equal(4, insns.Count);
        Assert.Equal(ILOpCode.Ldloca_s, insns[0].OpCode);
        Assert.Equal(0, insns[0].Offset);
        Assert.Equal(ILOpCode.Nop, insns[1].OpCode); // constrained pairing marker
        Assert.Equal(2, insns[1].Offset);
        Assert.Equal(0x0100000D, insns[1].Operand);
        Assert.Equal(ILOpCode.Callvirt, insns[2].OpCode);
        Assert.Equal(8, insns[2].Offset);
        Assert.Equal(0x0A00000C, insns[2].Operand);
        Assert.Equal(ILOpCode.Ret, insns[3].OpCode);
        Assert.Equal(13, insns[3].Offset);
    }

    [Fact]
    public void Decode_Rethrow_DoesNotThrow()
    {
        // exdeep's (net48 Roslyn) catch handler ends with 0xFE 0x1A (a bare
        // `throw;`). This byte pair was repeatedly mislabeled as endfinally —
        // endfinally is actually the single byte 0xDC
        // (System.Reflection.Metadata.ILOpCode.Endfinally == 0x00DC); 0xFE1A is
        // rethrow (ILOpCode.Rethrow). Before the fix 0xFE1A had no switch case →
        // default threw NotSupported, blocking the exdeep transpile
        // (IlDecoder.cs:236).
        // Sequence: ldloc.0; rethrow(0xFE1A); ret; ldloc.1; endfinally(0xDC); ret
        var il = new byte[] { 0x06, 0xFE, 0x1A, 0x2A, 0x07, 0xDC, 0x2A };
        var insns = IlDecoder.DecodeMethod(il);

        Assert.Equal(6, insns.Count);
        Assert.Equal(ILOpCode.Rethrow, insns[1].OpCode);
        Assert.Equal(1, insns[1].Offset);
        Assert.Equal(IlOperandKind.None, insns[1].OperandKind);
        Assert.Equal(ILOpCode.Endfinally, insns[4].OpCode);
        Assert.Equal(5, insns[4].Offset);
        Assert.Equal(IlOperandKind.None, insns[4].OperandKind);
    }

    [Fact]
    public void Decode_Switch_TableSizeAndAbsoluteTargets()
    {
        // switch (0x45, ECMA III.4.3) operand = int32 table size N + N×int32 relative
        // offsets, each offset relative to the "end of the instruction" (the next
        // instruction after the whole table). The byte sequence is taken from the
        // real enumdeep.exe Main@169 jump table (relative offsets [2,6,12,10], with
        // out-of-order targets 12/10):
        //   45 04 00 00 00 02 00 00 00 06 00 00 00 0C 00 00 00 0A 00 00 00
        // Placed at offset 0: instruction end = 0 + 1 + 4 + 4×4 = 21 → absolute
        // targets [23,27,33,31]; out-of-range/negative values land on the default
        // arm = the instruction after the end of the instruction (stream-alignment
        // check).
        var il = new byte[]
        {
            0x45, 0x04, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x00, 0x00,
            0x0C, 0x00, 0x00, 0x00,
            0x0A, 0x00, 0x00, 0x00,
            0x06, // ldloc.0 @21 (default arm: the instruction after the switch)
            0x2A, // ret @22
        };
        var insns = IlDecoder.DecodeMethod(il);

        var sw = insns[0];
        Assert.Equal(ILOpCode.Switch, sw.OpCode);
        Assert.Equal(0, sw.Offset);
        Assert.Equal(IlOperandKind.Switch, sw.OperandKind);
        Assert.Equal(4, sw.Operand); // table size
        Assert.Equal(new[] { 23, 27, 33, 31 }, sw.SwitchTargets); // absolute jump targets (out-of-order preserved)
        Assert.Equal(ILOpCode.Ldloc_0, insns[1].OpCode);
        Assert.Equal(21, insns[1].Offset); // stream-aligned: table consumed, default arm follows immediately
        Assert.Equal(ILOpCode.Ret, insns[2].OpCode);
    }

    [Fact]
    public void Decode_Switch_SyntheticPe4BranchTable()
    {
        // Synthetic PE (ManagedPEBuilder + MethodBodyStreamEncoder hand-built byte
        // body, same method as GuidSlotWiringTests): Cases.Sw::Main's IL mirrors the
        // real Roslyn shape of enumdeep.exe Main@169 —
        //   ldloc.0; ldc.i4.1; sub; switch(4) (table entries relative to the end of
        //   the instruction, end=24 → absolute targets [24,26,28,30]); 4 arms +
        //   default (out-of-range lands on the instruction after the switch @32).
        // Exercises the real PE path: PEReader + MetadataReader +
        // IlDecoder.DecodeMethod(il, md).
        var tmp = Path.Combine(Path.GetTempPath(), "il2cpp_sw_" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            File.WriteAllBytes(tmp, SynthesizeSwitchPe());
            using var fs = File.OpenRead(tmp);
            using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
            var md = pe.GetMetadataReader();
            MethodDefinitionHandle mainHandle = default;
            foreach (var tdh in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(tdh);
                if (md.GetString(td.Namespace) != "Cases" || md.GetString(td.Name) != "Sw") continue;
                foreach (var mh in td.GetMethods())
                    if (md.GetString(md.GetMethodDefinition(mh).Name) == "Main") mainHandle = mh;
            }
            Assert.False(mainHandle.IsNil, "Cases.Sw::Main not found in synthetic PE");
            var main = md.GetMethodDefinition(mainHandle);
            var body = pe.GetMethodBody(main.RelativeVirtualAddress);
            var il = body.GetILBytes() ?? Array.Empty<byte>();

            var insns = IlDecoder.DecodeMethod(il, md);

            Assert.Equal(14, insns.Count);
            var sw = insns[3];
            Assert.Equal(ILOpCode.Switch, sw.OpCode);
            Assert.Equal(3, sw.Offset);
            Assert.Equal(IlOperandKind.Switch, sw.OperandKind);
            Assert.Equal(4, sw.Operand); // table size
            Assert.Equal(new[] { 24, 26, 28, 30 }, sw.SwitchTargets); // absolute jump targets
            var def = insns.Single(i => i.Offset == 32); // default arm = instruction after the switch
            Assert.Equal(ILOpCode.Ldc_i4_0, def.OpCode);
            Assert.Equal(ILOpCode.Sub, insns[2].OpCode); // stream-aligned: instructions after table consumption decode correctly
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // Synthetic PE: Cases.Sw::Main, IL bytes hand-built (the raw codeSize
    // MethodBodyStreamEncoder overload + body.Instructions.WriteBytes — the
    // instruction sequence is fully controllable, mirroring the enumdeep shape).
    private static byte[] SynthesizeSwitchPe()
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var objRef = mdb.AddTypeReference(mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
                new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle)),
            mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));

        // 06 17 59 = ldloc.0; ldc.i4.1; sub (enumdeep's c-1 shape); switch@3 table
        // entries [0,2,4,6] relative to instruction end 24 → [24,26,28,30]; each arm
        // 2 bytes; default@32.
        var code = new byte[]
        {
            0x06,                   // ldloc.0 @0
            0x17,                   // ldc.i4.1 @1
            0x59,                   // sub @2
            0x45, 0x04, 0x00, 0x00, 0x00, // switch table size 4 @3
            0x00, 0x00, 0x00, 0x00, // +0 → 24
            0x02, 0x00, 0x00, 0x00, // +2 → 26
            0x04, 0x00, 0x00, 0x00, // +4 → 28
            0x06, 0x00, 0x00, 0x00, // +6 → 30
            0x16, 0x2A,             // ldc.i4.0; ret @24 (arm0)
            0x17, 0x2A,             // ldc.i4.1; ret @26 (arm1)
            0x18, 0x2A,             // ldc.i4.2; ret @28 (arm2)
            0x19, 0x2A,             // ldc.i4.3; ret @30 (arm3)
            0x16, 0x2A,             // ldc.i4.0; ret @32 (default)
        };
        var il = new BlobBuilder();
        var stream = new MethodBodyStreamEncoder(il);
        var body = stream.AddMethodBody(code.Length, maxStack: 2, exceptionRegionCount: 0,
            hasSmallExceptionRegions: false, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        // Instructions is a read-only Blob view (no WriteBytes); GetBytes() returns
        // an ArraySegment over the underlying BlobBuilder single-block buffer — the
        // code region is pre-reserved as zero-fill by AddMethodBody, so we write
        // straight back (il is not touched after AddMethodBody; the reference is safe).
        var seg = body.Instructions.GetBytes();
        Array.Copy(code, 0, seg.Array!, seg.Offset, code.Length);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), // static, 0 params, ret void
            body.Offset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Sw"), objRef, default, main);
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return blob.ToArray();
    }

    // The test host's working directory is the bin output directory, so relative
    // paths cannot resolve fixtures directly. Walk up from
    // AppContext.BaseDirectory to the repo root to locate the fixed relative path
    // (same convention as MetadataLoaderTests).
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", "calc.dll");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "fixture calc.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/calc.dll)");
    }

    //--------------------------------------------------------------------------
    // IL instruction-set audit (b23): full ECMA-335 coverage of the decoder.
    // Every ILOpCode except Calli (FNPTR agent in flight) must decode without
    // throwing -- the decoder is the byte-level gate and must not reject valid
    // IL; StackFold/CodeGenerator are where unsupported semantics go LOUD.
    //--------------------------------------------------------------------------

    [Fact]
    public void Decode_NoOperandAuditGroup_DoesNotThrow()
    {
        var ops = new (ILOpCode op, byte[] bytes)[]
        {
            (ILOpCode.Break, new byte[] { 0x01 }),
            (ILOpCode.Ldind_i1, new byte[] { 0x46 }),
            (ILOpCode.Ldind_u1, new byte[] { 0x47 }),
            (ILOpCode.Ldind_i2, new byte[] { 0x48 }),
            (ILOpCode.Ldind_u2, new byte[] { 0x49 }),
            (ILOpCode.Ldind_i4, new byte[] { 0x4A }),
            (ILOpCode.Ldind_u4, new byte[] { 0x4B }),
            (ILOpCode.Ldind_i8, new byte[] { 0x4C }),
            (ILOpCode.Ldind_i, new byte[] { 0x4D }),
            (ILOpCode.Ldind_r4, new byte[] { 0x4E }),
            (ILOpCode.Ldind_r8, new byte[] { 0x4F }),
            (ILOpCode.Ldind_ref, new byte[] { 0x50 }),
            (ILOpCode.Stind_ref, new byte[] { 0x51 }),
            (ILOpCode.Stind_i1, new byte[] { 0x52 }),
            (ILOpCode.Stind_i2, new byte[] { 0x53 }),
            (ILOpCode.Stind_i4, new byte[] { 0x54 }),
            (ILOpCode.Stind_i8, new byte[] { 0x55 }),
            (ILOpCode.Stind_r4, new byte[] { 0x56 }),
            (ILOpCode.Stind_r8, new byte[] { 0x57 }),
            (ILOpCode.Stind_i, new byte[] { 0xDF }),
            (ILOpCode.Div_un, new byte[] { 0x5C }),
            (ILOpCode.Rem_un, new byte[] { 0x5E }),
            (ILOpCode.Ckfinite, new byte[] { 0xC3 }),
            (ILOpCode.Add_ovf, new byte[] { 0xD6 }),
            (ILOpCode.Add_ovf_un, new byte[] { 0xD7 }),
            (ILOpCode.Mul_ovf, new byte[] { 0xD8 }),
            (ILOpCode.Mul_ovf_un, new byte[] { 0xD9 }),
            (ILOpCode.Sub_ovf, new byte[] { 0xDA }),
            (ILOpCode.Sub_ovf_un, new byte[] { 0xDB }),
            (ILOpCode.Arglist, new byte[] { 0xFE, 0x00 }),
            (ILOpCode.Endfilter, new byte[] { 0xFE, 0x11 }),
            (ILOpCode.Cpblk, new byte[] { 0xFE, 0x17 }),
            (ILOpCode.Initblk, new byte[] { 0xFE, 0x18 }),
            (ILOpCode.Refanytype, new byte[] { 0xFE, 0x1D }),
        };
        foreach (var (op, bytes) in ops)
        {
            var ins = Assert.Single(IlDecoder.DecodeMethod(bytes));
            Assert.Equal(op, ins.OpCode);
            Assert.Equal(IlOperandKind.None, ins.OperandKind);
            Assert.Equal(0, ins.Offset);
        }
    }

    [Fact]
    public void Decode_TypeTokenAuditGroup_ReadsTypeTokenOperand()
    {
        // Cpobj(0x70); Ldelema(0x8F); Ldelem(0xA3); Stelem(0xA4);
        // Mkrefany(0xC6); Refanyval(0xC2) -- each reads a 4-byte TypeDefOrRef
        // token (InlineType).
        var ops = new (ILOpCode op, byte code)[]
        {
            (ILOpCode.Cpobj, 0x70),
            (ILOpCode.Ldelema, 0x8F),
            (ILOpCode.Ldelem, 0xA3),
            (ILOpCode.Stelem, 0xA4),
            (ILOpCode.Mkrefany, 0xC6),
            (ILOpCode.Refanyval, 0xC2),
        };
        foreach (var (op, code) in ops)
        {
            var insns = IlDecoder.DecodeMethod(new byte[] { code, 0x0D, 0x00, 0x00, 0x01, 0x2A });
            var ins = insns[0];
            Assert.Equal(op, ins.OpCode);
            Assert.Equal(IlOperandKind.TypeToken, ins.OperandKind);
            Assert.Equal(0x0100000D, ins.Operand);
            Assert.Equal(ILOpCode.Ret, insns[1].OpCode); // stream-aligned after 4-byte operand
        }
    }

    [Fact]
    public void Decode_Jmp_ReadsMethodTokenOperand()
    {
        // jmp (0x27, ECMA III.3.39): method token (InlineMethod) -- a tail
        // jump to a method. The decoder reads the 4-byte token; the fold
        // (FNPTR-adjacent, not in scope for b23) stays LOUD.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x27, 0x06, 0x00, 0x00, 0x0A, 0x2A });
        var jmp = insns[0];
        Assert.Equal(ILOpCode.Jmp, jmp.OpCode);
        Assert.Equal(IlOperandKind.MethodToken, jmp.OperandKind);
        Assert.Equal(0x0A000006, jmp.Operand);
        Assert.Equal(ILOpCode.Ret, insns[1].OpCode);
    }

    [Fact]
    public void Decode_MemoryPrefixes_PairIntoNoopMarkers()
    {
        // volatile.(0xFE13) / tail.(0xFE14) / readonly.(0xFE1E) / unaligned.
        // (0xFE12, one-byte alignment operand) -- same pairing strategy as
        // constrained.: the prefix has no standalone execution semantics in
        // the transpiled C++ (no memory model / the transpiler's own tail-call
        // optimization), so it decodes to a Nop marker.
        var vol = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x13, 0x2A });
        Assert.Equal(ILOpCode.Nop, vol[0].OpCode);
        Assert.Equal(IlOperandKind.None, vol[0].OperandKind);

        var tail = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x14, 0x2A });
        Assert.Equal(ILOpCode.Nop, tail[0].OpCode);

        var ro = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x1E, 0x2A });
        Assert.Equal(ILOpCode.Nop, ro[0].OpCode);

        var un = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x12, 0x02, 0x2A });
        Assert.Equal(ILOpCode.Nop, un[0].OpCode);
        Assert.Equal(IlOperandKind.Int8, un[0].OperandKind);
        Assert.Equal(2, un[0].Operand); // alignment operand retained
        Assert.Equal(ILOpCode.Ret, un[1].OpCode);
    }

    [Fact]
    public void Decode_AllEnumOpcodes_EveryOpcodeDecodes()
    {
        // Full ECMA-335 enumeration audit (218/218): every ILOpCode enum member in
        // net8.0 System.Reflection.Metadata decodes (operand reads padded with
        // zeros). The last remaining rejection, Calli (0x29), is now handled: the
        // decoder reads its 32-bit StandAloneSig token (IlDecoder.cs Calli case)
        // and the FNPTR chain (StackFold.EmitCalli + CodeGenerator.EmitCalliCall +
        // MethodSignatureCoder.DecodeStandalone) folds/emits the indirect call.
        // ANY NotSupportedException here is a completeness regression -- the
        // decoder is the byte-level gate and must not reject valid IL.
        var names = Enum.GetNames<ILOpCode>();
        Assert.Equal(218, names.Length);
        var missing = new List<ILOpCode>();
        foreach (var name in names)
        {
            var op = Enum.Parse<ILOpCode>(name);
            var v = (int)op;
            var il = v < 0x100 ? new byte[] { (byte)v } : new byte[] { 0xFE, (byte)(v & 0xFF) };
            var padded = new byte[il.Length + 64];
            Array.Copy(il, padded, il.Length);
            try
            {
                var r = IlDecoder.DecodeMethod(padded);
                // Prefix opcodes (Constrained/Unaligned/Volatile/Tail/Readonly)
                // intentionally pair to a Nop marker at decode time
                // (constrained. precedent).
                var expected = op is ILOpCode.Constrained or ILOpCode.Unaligned
                    or ILOpCode.Volatile or ILOpCode.Tail or ILOpCode.Readonly
                    ? ILOpCode.Nop : op;
                Assert.Equal(expected, r[0].OpCode); // first decoded instruction is the opcode (padding decodes as Nop)
            }
            catch (NotSupportedException)
            {
                missing.Add(op);
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Decode_Calli_ReadsStandAloneSigTokenOperand()
    {
        // calli (0x29, ECMA-335 III.3.19): 32-bit StandAloneSig token (0x11 table)
        // carrying the target's unmanaged calling convention + param count +
        // return + param types. The decoder reads the token; StackFold.EmitCalli
        // + CodeGenerator.EmitCalliCall (fnptr domain b23) fold and emit the
        // indirect call through the function pointer.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x29, 0x05, 0x00, 0x00, 0x11, 0x2A });

        var calli = insns[0];
        Assert.Equal(ILOpCode.Calli, calli.OpCode);
        Assert.Equal(0, calli.Offset);
        Assert.Equal(IlOperandKind.MethodToken, calli.OperandKind);
        Assert.Equal(0x11000005, calli.Operand);
        Assert.Equal(ILOpCode.Ret, insns[1].OpCode);
        Assert.Equal(5, insns[1].Offset); // stream-aligned after the 4-byte token
    }

    [Fact]
    public void Decode_Break_FoldsToNoOp()
    {
        // break (0x01) is a debugger breakpoint -- no executable semantics in
        // the generated C++; StackFold treats it as a no-op like nop.
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[] { 0x17, 0x01, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Decode_DivUn_FoldsToDivUnBinaryOp()
    {
        // div.un (0x5C) -> unsigned int32 division: BinaryOp.DivUn (Normalize
        // strength-reduces power-of-two constant divisors to ShrUn at IR
        // level). ldc.i4 5; ldc.i4 4; div.un; ret
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x20, 0x05, 0x00, 0x00, 0x00,
            0x20, 0x04, 0x00, 0x00, 0x00,
            0x5C, 0x2A,
        }), new MethodContext { ArgCount = 0 });
        var bin = Assert.IsType<BinaryExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal(BinaryOp.DivUn, bin.Op);
    }

    //--------------------------------------------------------------------------
    // Operand-boundary audit (b23): malformed/truncated IL fault tolerance.
    // Every operand read is bounds-checked (IlByteReader.Advance): a stream that
    // ends in the middle of an instruction stops decoding (the fully-decoded
    // prefix is returned, the trailing partial instruction is dropped) instead
    // of surfacing a raw IndexOutOfRangeException. Unknown opcodes still throw
    // NotSupportedException (the LOUD invalid-opcode gate).
    //--------------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0xFE }, 0)]                            // truncated 0xFE prefix (missing second byte)
    [InlineData(new byte[] { 0x0E }, 0)]                            // ldarg.s: missing 1-byte operand
    [InlineData(new byte[] { 0xFE, 0x0C, 0xFF }, 0)]                // ldloc (long-form 2-byte operand): 1 of 2
    [InlineData(new byte[] { 0x20, 0x01 }, 0)]                      // ldc.i4: 1 of 4
    [InlineData(new byte[] { 0x21, 0x01 }, 0)]                      // ldc.i8: 1 of 8
    [InlineData(new byte[] { 0x23, 0x00, 0x00, 0x00, 0x00 }, 0)]    // ldc.r8: 4 of 8
    [InlineData(new byte[] { 0x28, 0x06, 0x00 }, 0)]                // call: 2 of 4 token
    [InlineData(new byte[] { 0x38, 0x01, 0x00 }, 0)]                // br (long-form 4-byte branch): 2 of 4
    [InlineData(new byte[] { 0x2C }, 0)]                            // brfalse.s: missing 1-byte operand
    [InlineData(new byte[] { 0x72, 0x34 }, 0)]                      // ldstr: 1 of 4 token
    [InlineData(new byte[] { 0xFE, 0x16, 0x0D, 0x00 }, 0)]          // constrained.: 2 of 4 type token
    [InlineData(new byte[] { 0xFE, 0x12 }, 0)]                      // unaligned.: missing 1-byte alignment operand
    [InlineData(new byte[] { 0x45, 0x04, 0x00, 0x00, 0x00, 0x01, 0x02 }, 0)] // switch: table 4, only 2 table bytes present
    public void Decode_TruncatedOperand_StopsWithoutCrash(byte[] il, int expectedCount)
    {
        var insns = IlDecoder.DecodeMethod(il);
        Assert.Equal(expectedCount, insns.Count); // nothing fully decodable -> empty
    }

    [Fact]
    public void Decode_TruncatedStream_KeepsFullyDecodedPrefix()
    {
        // ldarg.0@0; ret@1; ldc.i4@2 truncated (2 of 4 operand bytes): the two
        // complete instructions decode, the trailing partial instruction is dropped.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x02, 0x2A, 0x20, 0x01 });
        Assert.Equal(2, insns.Count);
        Assert.Equal(ILOpCode.Ldarg_0, insns[0].OpCode);
        Assert.Equal(0, insns[0].Offset);
        Assert.Equal(ILOpCode.Ret, insns[1].OpCode);
        Assert.Equal(1, insns[1].Offset);

        // Prefix pairing survives truncation: volatile.@0 pairs to Nop; ret@2
        // decodes; calli@3 truncated (2 of 4 token bytes) stops.
        var insns2 = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x13, 0x2A, 0x29, 0x05, 0x00 });
        Assert.Equal(2, insns2.Count);
        Assert.Equal(ILOpCode.Nop, insns2[0].OpCode); // volatile. pairing marker
        Assert.Equal(ILOpCode.Ret, insns2[1].OpCode);
        Assert.Equal(2, insns2[1].Offset);
    }

    [Fact]
    public void Decode_Switch_MalformedTableCounts_StopWithoutOom()
    {
        // count=0x7FFFFFFF (half of int32 max): without the switch-table guard
        // this allocates a ~8 GB int[] (OOM); the guard treats the table as
        // unreadable and stops decoding.
        Assert.Empty(IlDecoder.DecodeMethod(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0x7F }));

        // negative count: tableEnd would be nonsense; guard stops decoding.
        Assert.Empty(IlDecoder.DecodeMethod(new byte[] { 0x45, 0xFF, 0xFF, 0xFF, 0xFF }));

        // count larger than the remaining stream: truncated table -> stop.
        Assert.Empty(IlDecoder.DecodeMethod(new byte[] { 0x45, 0x04, 0x00, 0x00, 0x00, 0x01, 0x02 }));

        // count=0 is a valid (degenerate) table: one Switch, zero targets,
        // default arm follows immediately.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x45, 0x00, 0x00, 0x00, 0x00, 0x2A });
        var sw = insns[0];
        Assert.Equal(ILOpCode.Switch, sw.OpCode);
        Assert.Equal(IlOperandKind.Switch, sw.OperandKind);
        Assert.Equal(0, sw.Operand);
        Assert.NotNull(sw.SwitchTargets);
        Assert.Empty(sw.SwitchTargets!);
        Assert.Equal(ILOpCode.Ret, insns[1].OpCode);
        Assert.Equal(5, insns[1].Offset); // stream-aligned after the empty table
    }

    [Fact]
    public void Decode_OperandReadBoundaries_ShortAndLongForm()
    {
        // ldarg.s 0xFF (short-form 1-byte operand, max value)
        var s = IlDecoder.DecodeMethod(new byte[] { 0x0E, 0xFF, 0x2A });
        Assert.Equal(ILOpCode.Ldarg_s, s[0].OpCode);
        Assert.Equal(IlOperandKind.Int8, s[0].OperandKind);
        Assert.Equal(255, s[0].Operand);

        // ldloc 0xFFFF (long-form 2-byte operand, max value; ldloc = 0xFE0C)
        var l = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x0C, 0xFF, 0xFF, 0x2A });
        Assert.Equal(ILOpCode.Ldloc, l[0].OpCode);
        Assert.Equal(IlOperandKind.Int16, l[0].OperandKind);
        Assert.Equal(65535, l[0].Operand);

        // ldc.i4.s -1 (sign-extended sbyte)
        var cs = IlDecoder.DecodeMethod(new byte[] { 0x1F, 0xFF, 0x2A });
        Assert.Equal(-1, cs[0].Operand);
        Assert.Equal(IlOperandKind.Int8, cs[0].OperandKind);

        // ldc.i4 -2 (little-endian int32)
        var c4 = IlDecoder.DecodeMethod(new byte[] { 0x20, 0xFE, 0xFF, 0xFF, 0xFF, 0x2A });
        Assert.Equal(-2, c4[0].Operand);
        Assert.Equal(IlOperandKind.Int32, c4[0].OperandKind);

        // ldc.i4 int32.MaxValue (little-endian int32, positive bound)
        var max = IlDecoder.DecodeMethod(new byte[] { 0x20, 0xFF, 0xFF, 0xFF, 0x7F, 0x2A });
        Assert.Equal(int.MaxValue, max[0].Operand);
        Assert.Equal(ILOpCode.Ret, max[1].OpCode); // stream-aligned after the 4-byte operand
    }

    [Fact]
    public void Decode_ConstantOperands_PreserveFloatingPointBits()
    {
        // ldc.r4 1.0f: stored as SingleToInt32Bits(1.0f) = 0x3F800000
        var r4 = IlDecoder.DecodeMethod(new byte[] { 0x22, 0x00, 0x00, 0x80, 0x3F, 0x2A });
        Assert.Equal(ILOpCode.Ldc_r4, r4[0].OpCode);
        Assert.Equal(IlOperandKind.Float, r4[0].OperandKind);
        Assert.Equal(0x3F800000, r4[0].Operand);

        // ldc.r8 1.0: DoubleToInt64Bits(1.0) = 0x3FF0000000000000
        var r8 = IlDecoder.DecodeMethod(new byte[] { 0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F, 0x2A });
        Assert.Equal(ILOpCode.Ldc_r8, r8[0].OpCode);
        Assert.Equal(IlOperandKind.Double, r8[0].OperandKind);
        Assert.Equal(0x3FF0000000000000, r8[0].Operand);

        // ldc.i8 -1 (64-bit little-endian)
        var i8 = IlDecoder.DecodeMethod(new byte[] { 0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x2A });
        Assert.Equal(ILOpCode.Ldc_i8, i8[0].OpCode);
        Assert.Equal(IlOperandKind.Int64, i8[0].OperandKind);
        Assert.Equal(-1, i8[0].Operand);

        // ldstr token (md=null: the #US resolution is skipped, token kept raw)
        var st = IlDecoder.DecodeMethod(new byte[] { 0x72, 0x34, 0x12, 0x00, 0x70, 0x2A });
        Assert.Equal(ILOpCode.Ldstr, st[0].OpCode);
        Assert.Equal(IlOperandKind.StringToken, st[0].OperandKind);
        Assert.Equal(0x70001234, st[0].Operand);
        Assert.Null(st[0].StringOperand);
    }

    [Fact]
    public void Decode_UnknownOpcode_StillThrowsNotSupported()
    {
        // Reserved/invalid opcode bytes are NOT tolerated (unlike truncation):
        // the decoder is the LOUD byte-level gate and rejects invalid opcodes.
        Assert.Throws<NotSupportedException>(() => IlDecoder.DecodeMethod(new byte[] { 0xFF }));
        Assert.Throws<NotSupportedException>(() => IlDecoder.DecodeMethod(new byte[] { 0xFE, 0xFF }));
        Assert.Throws<NotSupportedException>(() => IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x10 })); // reserved prefix2 area

        // A complete no-operand prefix sequence (volatile.) is a valid 2-byte
        // instruction and must decode (to its Nop pairing marker), not throw.
        var ok = IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x13 });
        Assert.Equal(ILOpCode.Nop, Assert.Single(ok).OpCode);
    }
}
