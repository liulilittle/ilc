namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.BinaryIO -- System.IO.BinaryReader / BinaryWriter icall mappings
// (binaryio.h: the in-memory little-endian byte-stream protocol).
// Owners: System.IO.BinaryReader / BinaryWriter
//
// Wired surface -- exactly one runtime icall per primitive of the protocol:
//   BinaryWriter.Write(int32/int64/uint32/double/single/bool/string) ->
//     il2cpp_binary_write_{int32,int64,uint32,double,single,bool,string}
//   BinaryReader.ReadInt32/ReadInt64/ReadUInt32/ReadDouble/ReadSingle/
//     ReadBoolean/ReadString -> il2cpp_binary_read_*
//
// Overload dispatch uses the RAW parameter byte (ps[0]) because the
// signature encoder maps char/bool/int32 to the same int32 slot: only the
// RawSignature byte separates Write(int32) (0x08) from Write(char) (0x03)
// and Write(bool) (0x02). The receiver occupies casts index 0 ("" -- a
// managed reference slot, same as StringBuilder); the value cast follows.
//
// LOUD rejections (known members with no runtime counterpart -- a
// close-but-wrong mapping would corrupt the stream protocol):
//   * Write(byte/sbyte/short/ushort/char/ulong): the runtime writes only
//     the fixed-width set above -- routing Write(byte) through write_int32
//     would emit 4 bytes instead of 1 (protocol corruption).
//   * Write(decimal)/Write(byte[])/Write(char[]): no runtime icall.
//   * ReadByte: il2cpp_binary_read_bool normalizes to 0/1, while .NET
//     ReadByte returns the raw byte (0x02 reads back as 2, not 1).
//   * ReadSByte/ReadInt16/ReadUInt16/ReadUInt64/ReadChar/ReadDecimal: no
//     runtime icall for the 1/2/8-byte and decimal widths.
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
    private static ResolvedMethod? TryMapBinaryIO(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.IO.BinaryWriter":
                switch (name)
                {
                    case "Write":
                        return ps[0] switch
                        {
                            0x08 => I("il2cpp::runtime::icall_binary_write_int32", 1, false, false, true, new[] { "", "i" }),
                            0x0A => I("il2cpp::runtime::icall_binary_write_int64", 1, false, false, true, new[] { "", "l" }),
                            0x09 => I("il2cpp::runtime::icall_binary_write_uint32", 1, false, false, true, new[] { "", "i" }),
                            0x0D => I("il2cpp::runtime::icall_binary_write_double", 1, false, false, true, new[] { "", "d" }),
                            0x0C => I("il2cpp::runtime::icall_binary_write_single", 1, false, false, true, new[] { "", "d" }),
                            0x02 => I("il2cpp::runtime::icall_binary_write_bool", 1, false, false, true, new[] { "", "b" }),
                            0x0E => I("il2cpp::runtime::icall_binary_write_string", 1, false, false, true),
                            0x04 or 0x05 or 0x06 or 0x07 or 0x03 or 0x0B =>
                                throw new NotSupportedException($"Write(0x{ps[0]:X2}): the runtime writes only int32/int64/uint32/double/single/bool/string"),
                            0x11 => throw new NotSupportedException("Write(decimal): no runtime icall in the binary protocol"),
                            0x1D => throw new NotSupportedException("Write(array): no byte[]/char[] icall in the binary protocol"),
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
                        throw new NotSupportedException("ReadByte: il2cpp_binary_read_bool normalizes to 0/1, .NET ReadByte returns the raw byte 0..255");
                    case "ReadSByte":
                    case "ReadInt16":
                    case "ReadUInt16":
                    case "ReadUInt64":
                    case "ReadChar": throw new NotSupportedException("no runtime icall for this read width in the binary protocol");
                    case "ReadDecimal": throw new NotSupportedException("ReadDecimal: no runtime icall in the binary protocol");
                    case "get_BaseStream":
                        throw new NotSupportedException("BaseStream: no runtime icall -- the wrapped stream object is not stored on the BinaryReader/Writer (only the shared handle)");
                    default: return null;
                }
            default: return null;
        }
    }
}
