using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Il2Cpp.Transpiler.Il;

public static class IlDecoder
{
    // md is optional: null keeps pure byte decoding (existing test paths unchanged);
    // non-null makes Ldstr resolve string constants from the #US heap into StringOperand.
    public static List<IlInstruction> DecodeMethod(byte[] il, MetadataReader? md = null)
    {
        var result = new List<IlInstruction>();
        var reader = new IlByteReader(il);
        // Malformed-IL fault tolerance (operand-boundary audit b23): every
        // operand read is bounds-checked; a stream that ends in the middle of an
        // instruction throws TruncatedIlException. Once one operand is truncated
        // the instruction boundary is lost (the declared length cannot be
        // reconstructed), so decoding stops and the fully-decoded prefix is
        // returned -- the trailing partial instruction is dropped. An unknown
        // opcode still throws NotSupportedException (the decoder is the LOUD
        // byte-level gate for invalid opcodes; truncation is the ONLY tolerated
        // malformed-IL shape).
        try
        {
        while (reader.RemainingBytes > 0)
        {
            var offset = reader.Offset;
            var raw = reader.ReadByte();
            var opCode = (ILOpCode)raw;
            var kind = IlOperandKind.None;
            long operand = 0;
            string? strOp = null;
            int[]? switchTargets = null;

            // 0xFE prefix + second byte = two-byte opcode (.NET ILOpCode enum is
            // 0xFE00 | second byte).
            // Note: System.Reflection.Metadata.ILOpCode has no Prefix1/Prefix2
            // members, so the raw byte must be used for the decision.
            if (raw == 0xFE) opCode = (ILOpCode)(0xFE00 | reader.ReadByte());

            switch (opCode)
            {
                case ILOpCode.Nop:
                case ILOpCode.Ret:
                case ILOpCode.Pop:
                case ILOpCode.Dup:
                case ILOpCode.Add:
                case ILOpCode.Sub:
                case ILOpCode.Mul:
                case ILOpCode.Div:
                case ILOpCode.Rem:
                case ILOpCode.Neg:
                case ILOpCode.Not:
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Shr_un:
                case ILOpCode.Ldarg_0:
                case ILOpCode.Ldarg_1:
                case ILOpCode.Ldarg_2:
                case ILOpCode.Ldarg_3:
                case ILOpCode.Ldloc_0:
                case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2:
                case ILOpCode.Ldloc_3:
                case ILOpCode.Stloc_0:
                case ILOpCode.Stloc_1:
                case ILOpCode.Stloc_2:
                case ILOpCode.Stloc_3:
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Clt:
                case ILOpCode.Cgt_un:
                case ILOpCode.Clt_un:
                case ILOpCode.Ldnull:
                case ILOpCode.Endfinally:
                case ILOpCode.Ldlen:
                case ILOpCode.Ldelem_i1:
                case ILOpCode.Ldelem_u1:
                case ILOpCode.Ldelem_i2:
                case ILOpCode.Ldelem_u2:
                case ILOpCode.Ldelem_i4:
                case ILOpCode.Ldelem_u4:
                case ILOpCode.Ldelem_i8:
                case ILOpCode.Ldelem_i:
                case ILOpCode.Ldelem_r4:
                case ILOpCode.Ldelem_r8:
                case ILOpCode.Ldelem_ref:
                case ILOpCode.Stelem_i:
                case ILOpCode.Stelem_i1:
                case ILOpCode.Stelem_i2:
                case ILOpCode.Stelem_i4:
                case ILOpCode.Stelem_i8:
                case ILOpCode.Stelem_r4:
                case ILOpCode.Stelem_r8:
                case ILOpCode.Stelem_ref:
                    // IL instruction-set audit (b23): the remaining ECMA-335
                    // single-byte no-operand opcodes. Break is a debugger
                    // breakpoint (no-op in execution). Ldind_*/Stind_* are
                    // pointer loads/stores (unsafe code); Div_un/Rem_un are
                    // unsigned division/remainder; Add_ovf/Sub_ovf/Mul_ovf
                    // (+_un) are checked arithmetic; Ckfinite validates a float
                    // is finite; Arglist reads variadic args; Endfilter ends a
                    // filter clause; Cpblk/Initblk copy/init memory blocks;
                    // Refanytype extracts the type from a typed reference.
                    // All are decoded (stream-aligned); StackFold decides
                    // support (the fold default stays LOUD for the ones it
                    // cannot emit).
                case ILOpCode.Break:
                case ILOpCode.Ldind_i1:
                case ILOpCode.Ldind_u1:
                case ILOpCode.Ldind_i2:
                case ILOpCode.Ldind_u2:
                case ILOpCode.Ldind_i4:
                case ILOpCode.Ldind_u4:
                case ILOpCode.Ldind_i8:
                case ILOpCode.Ldind_i:
                case ILOpCode.Ldind_r4:
                case ILOpCode.Ldind_r8:
                case ILOpCode.Ldind_ref:
                case ILOpCode.Stind_ref:
                case ILOpCode.Stind_i1:
                case ILOpCode.Stind_i2:
                case ILOpCode.Stind_i4:
                case ILOpCode.Stind_i8:
                case ILOpCode.Stind_r4:
                case ILOpCode.Stind_r8:
                case ILOpCode.Stind_i:
                case ILOpCode.Div_un:
                case ILOpCode.Rem_un:
                case ILOpCode.Ckfinite:
                case ILOpCode.Add_ovf:
                case ILOpCode.Add_ovf_un:
                case ILOpCode.Mul_ovf:
                case ILOpCode.Mul_ovf_un:
                case ILOpCode.Sub_ovf:
                case ILOpCode.Sub_ovf_un:
                case ILOpCode.Arglist:
                case ILOpCode.Endfilter:
                case ILOpCode.Cpblk:
                case ILOpCode.Initblk:
                case ILOpCode.Refanytype:
                    break; // no operand
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0:
                case ILOpCode.Ldc_i4_1:
                case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4:
                case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6:
                case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                    break; // implicit constant, no operand
                case ILOpCode.Ldc_i4_s:
                    operand = (sbyte)reader.ReadByte(); kind = IlOperandKind.Int8; break;
                case ILOpCode.Ldc_i4:
                    operand = reader.ReadInt32(); kind = IlOperandKind.Int32; break;
                case ILOpCode.Ldc_i8:
                    operand = reader.ReadInt64(); kind = IlOperandKind.Int64; break;
                case ILOpCode.Ldc_r4:
                    operand = BitConverter.SingleToInt32Bits(reader.ReadSingle()); kind = IlOperandKind.Float; break;
                case ILOpCode.Ldc_r8:
                    operand = BitConverter.DoubleToInt64Bits(reader.ReadDouble()); kind = IlOperandKind.Double; break;
                case ILOpCode.Ldstr:
                    kind = IlOperandKind.StringToken; operand = reader.ReadInt32();
                    if (md != null)
                    {
                        var sh = MetadataTokens.UserStringHandle((int)operand);
                        if (!sh.IsNil) strOp = md.GetUserString(sh);
                    }
                    break;
                case ILOpCode.Ldarg:
                case ILOpCode.Starg:
                case ILOpCode.Ldloc:
                case ILOpCode.Stloc:
                case ILOpCode.Ldloca:
                case ILOpCode.Ldarga:
                    operand = reader.ReadUInt16(); kind = IlOperandKind.Int16; break;
                case ILOpCode.Ldloc_s:
                case ILOpCode.Stloc_s:
                case ILOpCode.Ldarg_s:
                case ILOpCode.Starg_s:
                case ILOpCode.Ldloca_s:
                case ILOpCode.Ldarga_s:
                    operand = reader.ReadByte(); kind = IlOperandKind.Int8; break;
                case ILOpCode.Br:
                case ILOpCode.Brfalse:
                case ILOpCode.Brtrue:
                case ILOpCode.Beq:
                case ILOpCode.Bge:
                case ILOpCode.Bgt:
                case ILOpCode.Ble:
                case ILOpCode.Blt:
                case ILOpCode.Bne_un:
                case ILOpCode.Bge_un:
                case ILOpCode.Bgt_un:
                case ILOpCode.Ble_un:
                case ILOpCode.Blt_un:
                case ILOpCode.Leave:
                case ILOpCode.Leave_s:
                    kind = IlOperandKind.BranchTarget;
                    // IL branch operands are relative to the NEXT instruction (the
                    // end of this instruction):
                    // long-form instructions are 5 bytes (1 opcode + 4 operand) -> target = offset + 5 + operand;
                    // short-form instructions are 2 bytes (1 + 1) -> target = offset + 2 + operand (Leave_s takes the else).
                    if (opCode is ILOpCode.Leave or ILOpCode.Br or ILOpCode.Brfalse
                        or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge
                        or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                        or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
                        or ILOpCode.Ble_un or ILOpCode.Blt_un)
                        operand = offset + 5 + reader.ReadInt32();
                    else
                        operand = offset + 2 + reader.ReadSByte();
                    break;
                case ILOpCode.Br_s:
                case ILOpCode.Brfalse_s:
                case ILOpCode.Brtrue_s:
                case ILOpCode.Beq_s:
                case ILOpCode.Bge_s:
                case ILOpCode.Bgt_s:
                case ILOpCode.Ble_s:
                case ILOpCode.Blt_s:
                case ILOpCode.Bne_un_s:
                case ILOpCode.Bge_un_s:
                case ILOpCode.Bgt_un_s:
                case ILOpCode.Ble_un_s:
                case ILOpCode.Blt_un_s:
                    kind = IlOperandKind.BranchTarget;
                    operand = offset + 2 + reader.ReadSByte();
                    break;
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                case ILOpCode.Newobj:
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                case ILOpCode.Jmp:
                    // jmp (0x27, ECMA III.3.39 -- tail jump to a method): the
                    // operand is a 4-byte MethodDef/MemberRef token. The
                    // decoder reads the token; the fold rejects jmp LOUD
                    // (no tail-jump in the transpiler model -- the decoder is
                    // the byte-level gate and must not reject valid IL).
                    kind = IlOperandKind.MethodToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Calli:
                    // calli (0x29, ECMA-335 III.3.19 -- indirect call): the
                    // operand is a 32-bit StandAloneSig token (0x11) carrying
                    // the target's method signature (unmanaged calling
                    // convention + param count + return + params). Grouped
                    // under MethodToken kind only because that is the existing
                    // "32-bit metadata token operand" slot -- like Ldtoken,
                    // the semantics are NOT implied; StackFold classifies the
                    // token by MetadataTokens.Handle kind at fold time (fnptr
                    // domain b23).
                    kind = IlOperandKind.MethodToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Throw:
                    break; // no operand (M3: pops the exception object, throws a managed exception)
                case ILOpCode.Rethrow:
                    // No operand (M3: bare `throw;` -- rethrows the current exception).
                    // Note: the byte encoding of rethrow is 0xFE 0x1A (computed here by the
                    // 0xFE00 | second byte switch); it is often mistaken for endfinally
                    // (the 0xFE1A vs 0xDC lore is confusing) -- endfinally is actually the
                    // single byte 0xDC (the Endfinally branch above), rethrow is 0xFE1A.
                    break;
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldfld:
                case ILOpCode.Ldsflda:
                case ILOpCode.Ldflda:
                case ILOpCode.Stsfld:
                case ILOpCode.Stfld:
                    kind = IlOperandKind.FieldToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Ldtoken:
                    // Ldtoken (0xD0, typeof support -- reflection audit F1): the operand is
                    // a 32-bit metadata token that can be TypeDefOrRef / FieldDef / MethodDef
                    // (typeof(T) = a type token; net48 array initializers = an RVA field
                    // token for RuntimeHelpers.InitializeArray). Grouping it under FieldToken
                    // is misleading -- the real classification happens at fold time by
                    // MetadataTokens.Handle kind (StackFold.cs Ldtoken case). The decoder
                    // does not parse token semantics (md is optional; the pure-byte path has
                    // no metadata), so the FieldToken value here merely reuses the "32-bit
                    // token operand" slot; the semantics are not implied.
                    kind = IlOperandKind.FieldToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Constrained:
                    // constrained. prefix (0xFE16, ECMA III.4.2): the standard Roslyn shape
                    // for virtual calls on value types (evidenced by guidcase.exe ildump:
                    // `ldloca <slot>; constrained. T; callvirt M` -- Guid and other value
                    // types always carry this prefix when calling Object virtual members
                    // like ToString/Equals via Object::ToString).
                    // The operand is a 32-bit TypeDefOrRef (the constrained type). The prefix
                    // has no stack effect -- the decoder completes the prefix pairing: this
                    // instruction is emitted as Nop (the operand is retained for future
                    // consumers), and all semantics land on the receiver shape of the
                    // following callvirt/call (StackFold's arg0IsAddr -> receiverIsAddress
                    // re-resolution: an ldloca address -> deref:* direct call to the
                    // value-type mapping; a reference-type receiver behaves like an ordinary
                    // callvirt). Emitted as Nop rather than keeping Constrained because the
                    // fold's main switch has no branch for this prefix (default LOUD), while
                    // all information consumers need is already present on the callvirt.
                    kind = IlOperandKind.TypeToken; operand = reader.ReadInt32(); opCode = ILOpCode.Nop; break;
                case ILOpCode.Volatile:
                case ILOpCode.Tail:
                case ILOpCode.Readonly:
                    // volatile./tail./readonly. prefixes (0xFE13/0xFE14/0xFE1E,
                    // ECMA III.4.2): no operand, no stack effect, apply to the
                    // FOLLOWING instruction (memory op / call / array access).
                    // Same pairing strategy as constrained.: emitted as a Nop
                    // marker -- the transpiler has no memory model (C++ single-
                    // thread emission), the IL tail hint is redundant with the
                    // transpiler's own tail-call optimization, and readonly. is
                    // a pure access-mode hint. The following instruction folds
                    // on its own semantics.
                    opCode = ILOpCode.Nop; break;
                case ILOpCode.Unaligned:
                    // unaligned. prefix (0xFE12, ECMA III.4.2): one byte
                    // alignment operand (ShortInlineI), no stack effect. The
                    // alignment hint is irrelevant on the emitted x86 target;
                    // consume the operand and pair to Nop (constrained.
                    // precedent).
                    operand = reader.ReadByte(); kind = IlOperandKind.Int8; opCode = ILOpCode.Nop; break;
                case ILOpCode.Castclass:
                case ILOpCode.Isinst:
                case ILOpCode.Newarr:
                case ILOpCode.Box:
                case ILOpCode.Unbox:
                case ILOpCode.Unbox_any:
                case ILOpCode.Ldobj:
                case ILOpCode.Stobj:
                case ILOpCode.Initobj:
                case ILOpCode.Sizeof:
                case ILOpCode.Cpobj:
                case ILOpCode.Ldelema:
                case ILOpCode.Ldelem:
                case ILOpCode.Stelem:
                case ILOpCode.Mkrefany:
                case ILOpCode.Refanyval:
                    kind = IlOperandKind.TypeToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Conv_i:
                case ILOpCode.Conv_i1:
                case ILOpCode.Conv_i2:
                case ILOpCode.Conv_i4:
                case ILOpCode.Conv_i8:
                case ILOpCode.Conv_u:
                case ILOpCode.Conv_u1:
                case ILOpCode.Conv_u2:
                case ILOpCode.Conv_u4:
                case ILOpCode.Conv_u8:
                case ILOpCode.Conv_r4:
                case ILOpCode.Conv_r8:
                case ILOpCode.Conv_r_un:
                case ILOpCode.Conv_ovf_i:
                case ILOpCode.Conv_ovf_i1:
                case ILOpCode.Conv_ovf_i2:
                case ILOpCode.Conv_ovf_i4:
                case ILOpCode.Conv_ovf_i8:
                case ILOpCode.Conv_ovf_u:
                case ILOpCode.Conv_ovf_u1:
                case ILOpCode.Conv_ovf_u2:
                case ILOpCode.Conv_ovf_u4:
                case ILOpCode.Conv_ovf_u8:
                case ILOpCode.Conv_ovf_i_un:
                case ILOpCode.Conv_ovf_i1_un:
                case ILOpCode.Conv_ovf_i2_un:
                case ILOpCode.Conv_ovf_i4_un:
                case ILOpCode.Conv_ovf_i8_un:
                case ILOpCode.Conv_ovf_u_un:
                case ILOpCode.Conv_ovf_u1_un:
                case ILOpCode.Conv_ovf_u2_un:
                case ILOpCode.Conv_ovf_u4_un:
                case ILOpCode.Conv_ovf_u8_un:
                    break; // no operand (unsigned overflow conversions; the _S signed variants are not in the .NET ILOpCode enum, not covered by M1)
                case ILOpCode.Localloc:
                    break; // M3 support
                case ILOpCode.Switch:
                    // ECMA-335 III.4.3 (switch): the operand is an int32 table size N
                    // plus N x int32 relative offsets, each relative to the "end of the
                    // instruction" (the next instruction after the whole table). Total
                    // instruction length = 1 + 4 + 4N. The table size goes into Operand
                    // (IlOperandKind.Switch), the absolute targets into SwitchTargets
                    // (kept in table order -- out-of-order/uneven spacing is preserved
                    // exactly as the compiler emitted them).
                    // Out-of-range/negative values fall to the default arm = the next
                    // instruction; the decoder needs no special handling (the stream
                    // naturally aligns). enumdeep.exe Main@169 evidenced: ldloc.0;
                    // ldc.i4.1; sub; switch (table [2,6,12,10] relative to instruction
                    // end 190 -> [192,196,202,200]).
                    // Malformed-table guard (operand-boundary audit b23): a negative or
                    // absurdly large table size would either produce a nonsensical
                    // tableEnd or a multi-GB allocation; if the declared N x int32 table
                    // cannot fit in the remaining stream the IL is truncated/malformed
                    // -> stop decoding (fault-tolerance contract).
                    {
                        var count = reader.ReadInt32();
                        if (count < 0 || (long)count * 4 > reader.RemainingBytes)
                            throw new TruncatedIlException();
                        var tableEnd = reader.Offset + 4 * count;
                        var targets = new int[count];
                        for (var i = 0; i < count; i++)
                            targets[i] = tableEnd + reader.ReadInt32();
                        operand = count;
                        kind = IlOperandKind.Switch;
                        switchTargets = targets;
                    }
                    break;
                default:
                    throw new NotSupportedException(
                        $"IL opcode 0x{(int)opCode:X4} at offset {offset} not supported in M1");
            }
            result.Add(new IlInstruction
            {
                OpCode = opCode, Offset = offset,
                OperandKind = kind, Operand = operand, StringOperand = strOp,
                SwitchTargets = switchTargets
            });
        }
        }
        catch (TruncatedIlException)
        {
            // Truncated/malformed IL (operand read past end of stream): keep the
            // fully-decoded prefix, drop the incomplete trailing instruction.
        }
        return result;
    }

    /// <summary>
    /// End-of-stream while reading an IL operand (a short-form/long-form/token/
    /// switch-table operand is shorter than its declared size): the byte stream is
    /// truncated or malformed. Caught by DecodeMethod, which stops decoding and
    /// returns the fully-decoded prefix (fault-tolerance contract).
    /// </summary>
    private sealed class TruncatedIlException : Exception { }

    /// <summary>A little-endian read-only stream reader over a byte[].</summary>
    /// <remarks>
    /// The original code used System.Reflection.Metadata.BlobReader, but in .NET 8 its
    /// only public constructor is the unsafe BlobReader(byte*, int); there is no public
    /// byte[] constructor, so new BlobReader(il) is impossible.
    /// A private replacement is therefore used (member signatures match BlobReader;
    /// the decode logic is preserved verbatim).
    /// Operand-boundary audit (b23): every read is bounds-checked via Advance --
    /// truncated IL must surface TruncatedIlException (the decode loop's fault-tolerance
    /// contract), never a raw IndexOutOfRangeException.
    /// </remarks>
    private struct IlByteReader
    {
        private readonly byte[] _il;
        private int _pos;

        public IlByteReader(byte[] il)
        {
            _il = il;
            _pos = 0;
        }

        public int Offset => _pos;
        public int RemainingBytes => _il.Length - _pos;

        // Advances only when `count` whole bytes remain; otherwise throws
        // TruncatedIlException (the stream ends mid-operand).
        private int Advance(int count)
        {
            if (_pos + count > _il.Length) throw new TruncatedIlException();
            var p = _pos;
            _pos += count;
            return p;
        }

        public byte ReadByte() => _il[Advance(1)];

        public sbyte ReadSByte() => unchecked((sbyte)_il[Advance(1)]);

        public ushort ReadUInt16()
        {
            var p = Advance(2);
            return (ushort)(_il[p] | (_il[p + 1] << 8));
        }

        public int ReadInt32()
        {
            var p = Advance(4);
            return _il[p] | (_il[p + 1] << 8) | (_il[p + 2] << 16) | (_il[p + 3] << 24);
        }

        public long ReadInt64()
        {
            var p = Advance(8);
            long v = 0;
            for (var i = 0; i < 8; i++) v |= (long)_il[p + i] << (8 * i);
            return v;
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public double ReadDouble() => BitConverter.Int64BitsToDouble(ReadInt64());
    }
}
