using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.BinaryIO -- System.IO.BinaryReader / BinaryWriter icall mappings
// (binaryio.h: the in-memory little-endian byte-stream protocol).
// Owners: System.IO.BinaryReader / BinaryWriter
//
// Wired surface -- exactly one runtime icall per primitive of the protocol:
//   BinaryWriter.Write(int32/int64/uint32/double/single/bool/string) ->
//     il2cpp_binary_write_{int32,int64,uint32,double,single,bool,string}
//   BinaryWriter.Write(char/decimal/byte[]/char[]) (T4b) ->
//     il2cpp_binary_write_{char,decimal,bytes_full,bytes,chars_full,chars}
//   BinaryReader.ReadInt32/ReadInt64/ReadUInt32/ReadDouble/ReadSingle/
//     ReadBoolean/ReadString/ReadByte -> il2cpp_binary_read_*
//   BinaryReader.ReadChar/ReadDecimal (T4b) -> il2cpp_binary_read_{char,decimal}
//
// Overload dispatch uses the RAW parameter byte (ps[0]) because the
// signature encoder maps char/bool/int32 to the same int32 slot: only the
// RawSignature byte separates Write(int32) (0x08) from Write(char) (0x03)
// and Write(bool) (0x02). The receiver occupies casts index 0 ("" -- a
// managed reference slot, same as StringBuilder); the value cast follows.
//
// LOUD rejections (known members with no runtime counterpart -- a
// close-but-wrong mapping would corrupt the stream protocol):
//   * Write(byte/sbyte/short/ushort/ulong): the runtime writes only
//     int32/int64/uint32/double/single/bool/string/char/decimal/byte[]/
//     char[] -- routing Write(byte) through write_int32 would emit 4 bytes
//     instead of 1 (protocol corruption).
//   * Write(array) with untracked element / unmatched shape: byte[] vs char[]
//     encodings differ; never guess.
//   * ReadSByte/ReadInt16/ReadUInt16/ReadUInt64: no runtime icall for the
//     1/2/8-byte widths.
//   * .ctor(Stream)/get_BaseStream: BaseStream untouched (GC slot pending).
//   * .ctor(Stream)/get_BaseStream: the RUNTIME object surface EXISTS
//     (streamio.cpp binrw-b23 -- System_IO_BinaryReader_class /
//     System_IO_BinaryWriter_class descriptors + icall_binary_{reader,writer}
//     _ctor wrapping a shared StreamState + the icall_binary_read_*/write_*
//     protocol icalls, all covered by test_binaryio), but the .ctor wiring is
//     BLOCKED at the dispatch layer: tests/unit/MethodMapTests.BinaryIO.cs
//     (READ-ONLY for this batch) pins BinaryWriter .ctor(Stream) as LOUD
//     (Resolve_BinaryWriter_CtorStream_ThrowsLoud) AND pins every Write/Read
//     CppName to the binaryio.h buffer-handle icalls -- so a .ctor(Stream)
//     mapping would also require redirecting the method icalls to the object
//     versions, which breaks those pins. Unblock recipe (needs tests/unit
//     write permission): (a) map .ctor(Stream) (ps[0]==0x12) to
//     icall_binary_reader_ctor / icall_binary_writer_ctor; (b) redirect the
//     Write/Read CppNames to icall_binary_write_*/read_*; (c) update the
//     pinned unit tests; (d) extend binaryiodeep with the direct
//     BinaryReader/Writer assertions (the runtime object surface is verified
//     today via test_binaryio section 18).
//
// Out-of-scope members (Read/ReadBytes/ReadChars/Read7BitEncodedInt/
// PeekChar/Dispose/Close) return null -> the generated-name path (same
// practice as StringBuilder's unwired members).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapBinaryIO(MetadataReader md, BlobHandle sigHandle, string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.IO.BinaryWriter":
                switch (name)
                {
                    case "Write":
                        // T4b parity: decimal + byte[]/char[] + char arms over
                        // the landed A2 icalls. Narrow/char ride the int32
                        // carrier ("i"); decimal rides the Il2CppDecimal* slot
                        // ("dec" -- VALUE locals take address, ldloca passes
                        // through). byte[] vs char[] dispatch on the tracked
                        // arg0Type (both decode as 0x1D); untracked stays LOUD.
                        if (count == 1 && ps[0] == 0x11)
                            return arg0Type is null or "System.Decimal"
                                ? I("il2cpp::runtime::icall_binary_write_decimal", 1, false, false, true, new[] { "", "dec" })
                                : throw new NotSupportedException($"Write(struct): only System.Decimal is wired (arg0Type='{arg0Type}')");
                        if (count == 1 && ps[0] == 0x1D)
                        {
                            // Signature authority (binrw3): SZARRAY element byte
                            // (0x05 byte vs 0x03 char) decides; the StackFold
                            // arg0Type hint is only fallback/corroboration.
                            var sigElem = BinaryIOArrayElementByte(md, sigHandle);
                            if (sigElem is 0x05)
                            {
                                if (arg0Type is "System.Char[]")
                                    throw new NotSupportedException($"Write(array): signature says byte[] but tracking says char[] (arg0Type='{arg0Type}')");
                                return I("il2cpp::runtime::icall_binary_write_bytes_full", 1, false, false, true, new[] { "", "a" });
                            }
                            if (sigElem is 0x03)
                            {
                                if (arg0Type is "System.Byte[]")
                                    throw new NotSupportedException($"Write(array): signature says char[] but tracking says byte[] (arg0Type='{arg0Type}')");
                                return I("il2cpp::runtime::icall_binary_write_chars_full", 1, false, false, true, new[] { "", "a" });
                            }
                            return arg0Type is "System.Byte[]"
                                ? I("il2cpp::runtime::icall_binary_write_bytes_full", 1, false, false, true, new[] { "", "a" })
                                : arg0Type is "System.Char[]"
                                ? I("il2cpp::runtime::icall_binary_write_chars_full", 1, false, false, true, new[] { "", "a" })
                                : throw new NotSupportedException($"Write(array): element type unprovable (arg0Type='{arg0Type ?? "<untracked>"}') -- byte[] vs char[] encodings differ; pass a tracked array");
                        }
                        if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                        {
                            var sigElem = BinaryIOArrayElementByte(md, sigHandle);
                            if (sigElem is 0x05)
                            {
                                if (arg0Type is "System.Char[]")
                                    throw new NotSupportedException($"Write(array, index, count): signature says byte[] but tracking says char[] (arg0Type='{arg0Type}')");
                                return I("il2cpp::runtime::icall_binary_write_bytes", 3, false, false, true, new[] { "", "a", "i", "i" });
                            }
                            if (sigElem is 0x03)
                            {
                                if (arg0Type is "System.Byte[]")
                                    throw new NotSupportedException($"Write(array, index, count): signature says char[] but tracking says byte[] (arg0Type='{arg0Type}')");
                                return I("il2cpp::runtime::icall_binary_write_chars", 3, false, false, true, new[] { "", "a", "i", "i" });
                            }
                            return arg0Type is "System.Byte[]"
                                ? I("il2cpp::runtime::icall_binary_write_bytes", 3, false, false, true, new[] { "", "a", "i", "i" })
                                : arg0Type is "System.Char[]"
                                ? I("il2cpp::runtime::icall_binary_write_chars", 3, false, false, true, new[] { "", "a", "i", "i" })
                                : throw new NotSupportedException($"Write(array, index, count): element type unprovable (arg0Type='{arg0Type ?? "<untracked>"}') -- pass a tracked array");
                        }
                        return ps[0] switch
                        {
                            0x08 => I("il2cpp::runtime::icall_binary_write_int32", 1, false, false, true, new[] { "", "i" }),
                            0x0A => I("il2cpp::runtime::icall_binary_write_int64", 1, false, false, true, new[] { "", "l" }),
                            0x09 => I("il2cpp::runtime::icall_binary_write_uint32", 1, false, false, true, new[] { "", "i" }),
                            0x0D => I("il2cpp::runtime::icall_binary_write_double", 1, false, false, true, new[] { "", "d" }),
                            0x0C => I("il2cpp::runtime::icall_binary_write_single", 1, false, false, true, new[] { "", "d" }),
                            0x02 => I("il2cpp::runtime::icall_binary_write_bool", 1, false, false, true, new[] { "", "b" }),
                            0x0E => I("il2cpp::runtime::icall_binary_write_string", 1, false, false, true),
                            0x03 => I("il2cpp::runtime::icall_binary_write_char", 1, false, false, true, new[] { "", "i" }),
                            0x04 or 0x05 or 0x06 or 0x07 or 0x0B =>
                                throw new NotSupportedException($"Write(0x{ps[0]:X2}): the runtime writes only int32/int64/uint32/double/single/bool/string/char/decimal/byte[]/char[]"),
                            0x1D => throw new NotSupportedException("Write(array): no byte[]/char[] icall matches this shape (whole-array takes 1 arg, ranged takes (array, int, int))"),
                            _ => throw new NotSupportedException($"Write param type 0x{ps[0]:X2} not wired in M5"),
                        };
                    case ".ctor":
                        // .ctor(System.IO.Stream) -- batch 150d: the runtime
                        // object surface exists (icall_binary_writer_ctor,
                        // il2cpp_streamio.h:274 -- the receiver is the newobj
                        // temp, the stream argument passes through uncast).
                        return I("il2cpp::runtime::icall_binary_writer_ctor", 1, false, false, true, new[] { "" });
                    case "get_BaseStream":
                        throw new NotSupportedException("BaseStream: no runtime icall -- the wrapped stream object is not stored on the BinaryReader/Writer (only the shared handle)");
                    default: return null;
                }
            case "System.IO.BinaryReader":
                switch (name)
                {
                    case ".ctor":
                        // .ctor(System.IO.Stream) -- batch 150d (see the
                        // BinaryWriter sibling; il2cpp_streamio.h:273).
                        return I("il2cpp::runtime::icall_binary_reader_ctor", 1, false, false, true, new[] { "" });
                    case "ReadInt32": return I("il2cpp::runtime::icall_binary_read_int32", 0, true, false, true);
                    case "ReadInt64": return I("il2cpp::runtime::icall_binary_read_int64", 0, true, false, true, null, "l");
                    case "ReadUInt32": return I("il2cpp::runtime::icall_binary_read_uint32", 0, true, false, true);
                    case "ReadDouble": return I("il2cpp::runtime::icall_binary_read_double", 0, true, false, true, null, "d");
                    case "ReadSingle": return I("il2cpp::runtime::icall_binary_read_single", 0, true, false, true, null, "d");
                    case "ReadBoolean": return I("il2cpp::runtime::icall_binary_read_bool", 0, true, false, true);
                    case "ReadString": return I("il2cpp::runtime::icall_binary_read_string", 0, true, true, true, null, null, false, false, false, "System.String");
                    case "ReadByte":
                        // WAVE2: the RAW next byte (0..255). NOT the bool
                        // icall: il2cpp_binary_read_bool normalizes to 0/1,
                        // which is only correct for ReadBoolean.
                        return I("il2cpp::runtime::icall_binary_read_byte", 0, true, false, true);
                    case "ReadSByte":
                    case "ReadInt16":
                    case "ReadUInt16":
                    case "ReadUInt64":
                        throw new NotSupportedException("no runtime icall for this read width in the binary protocol");
                    case "ReadChar": return I("il2cpp::runtime::icall_binary_read_char", 0, true, false, true);
                    // T4b parity: ReadDecimal -- the decimal arrives through
                    // the OUT slot (self, out); ResultKind "dec2" appends the
                    // addressed result temp LAST (the "dec" prepend contract
                    // would put it first -- wrong order here).
                    case "ReadDecimal": return I("il2cpp::runtime::icall_binary_read_decimal", 0, true, false, true, null, "dec2");
                    case "get_BaseStream":
                        throw new NotSupportedException("BaseStream: no runtime icall -- the wrapped stream object is not stored on the BinaryReader/Writer (only the shared handle)");
                    default: return null;
                }
            default: return null;
        }
    }

    // binrw3 single authority: the SZARRAY element byte of the FIRST
    // parameter (0x05 byte vs 0x03 char). Same consumption as RawSignature
    // (generic count, custom modifiers, full return consumption) so the
    // element never leaks or shifts. Returns null when the first param is
    // not SZARRAY or the blob lacks that shape -- callers fall back to the
    // StackFold arg0Type hint.
    private static byte? BinaryIOArrayElementByte(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger();
        var count = sig.ReadCompressedInteger();
        if (count < 1) return null;
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        if (ret is 0x15)
        {
            _ = sig.ReadByte();
            _ = sig.ReadTypeHandle();
            var n = sig.ReadCompressedInteger();
            for (var i = 0; i < n; i++) BinaryIOReadParam(md, ref sig);
        }
        else if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle();
        else if (ret is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
        else if (ret is 0x1D) BinaryIOReadParam(md, ref sig);
        var first = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        if (first != 0x1D) return null;
        return MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
    }

    // Local copy of the ReadParamByte consumption (SZARRAY/BYREF/PTR/
    // GenericInst/VAR trailing data) so this domain does not depend on the
    // resolver's private helper.
    private static void BinaryIOReadParam(MetadataReader md, ref BlobReader sig)
    {
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (b)
        {
            case 0x10:
            case 0x0F:
                BinaryIOReadParam(md, ref sig);
                break;
            case 0x11 or 0x12 or 0x40:
                _ = sig.ReadTypeHandle();
                break;
            case 0x1D:
                BinaryIOReadParam(md, ref sig);
                break;
            case 0x13 or 0x1E:
                _ = sig.ReadCompressedInteger();
                break;
            case 0x15:
                _ = sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++) BinaryIOReadParam(md, ref sig);
                break;
        }
    }
}
