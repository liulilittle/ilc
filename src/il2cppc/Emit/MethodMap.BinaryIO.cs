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
// LOUD whitelist (impl-io2): every remaining NotSupportedException carries the
// standardized "LOUD whitelist (reason) [Reference Source file:line]" message.
// The rejectable shapes are all net48-nonexistent arities/element types:
//   * Write(struct) with a non-Decimal valuetype -- net48 has no generic
//     struct Write overload.
//   * Write(array) whose SZARRAY element is neither byte (0x05) nor char
//     (0x03) -- net48 has only (byte[]) and (char[]).
//   * Write/Write param types outside the net48 overload family.
//   * Close/Dispose/Close/Flush/Dispose with an arity net48 does not have.
//
// Signature authority: the SZARRAY element byte decides byte[] vs char[]; the
// StackFold arg0Type hint is only the fallback when the signature blob cannot
// be read (the two never disagree on net48-compiled IL, so the historical
// "signature says X but tracking says Y" throws were unreachable guards).
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
                        // through). byte[] vs char[] dispatch on the SZARRAY
                        // element byte (both decode as 0x1D) -- the SIGNATURE is
                        // the authority (the StackFold arg0Type hint is only the
                        // fallback when the blob cannot be read).
                        if (count == 1 && ps[0] == 0x11)
                            return arg0Type is null or "System.Decimal"
                                ? I("il2cpp::runtime::icall_binary_write_decimal", 1, false, false, true, new[] { "", "dec" })
                                : throw new NotSupportedException(
                                    $"LOUD whitelist (net48 has no such overload): Write(struct) with " +
                                    $"arg0Type='{arg0Type}' -- net48 BinaryWriter.Write has only the primitive, " +
                                    "decimal, string, char, byte[] and char[] overloads (no generic struct form) " +
                                    "[Reference Source binarywriter.cs:142-355]");
                        if (count == 1 && ps[0] == 0x1D)
                        {
                            // Signature authority (binrw3): SZARRAY element byte
                            // (0x05 byte vs 0x03 char) decides; the StackFold
                            // arg0Type hint is only fallback/corroboration.
                            var sigElem = BinaryIOArrayElementByte(md, sigHandle);
                            if (sigElem is 0x05)
                                return I("il2cpp::runtime::icall_binary_write_bytes_full", 1, false, false, true, new[] { "", "a" });
                            if (sigElem is 0x03)
                                return I("il2cpp::runtime::icall_binary_write_chars_full", 1, false, false, true, new[] { "", "a" });
                            return arg0Type is "System.Byte[]"
                                ? I("il2cpp::runtime::icall_binary_write_bytes_full", 1, false, false, true, new[] { "", "a" })
                                : arg0Type is "System.Char[]"
                                ? I("il2cpp::runtime::icall_binary_write_chars_full", 1, false, false, true, new[] { "", "a" })
                                : throw new NotSupportedException(
                                    $"LOUD whitelist (net48 has no such overload): Write(array) with element " +
                                    $"type 0x{(sigElem ?? 0):X2} (arg0Type='{arg0Type ?? "<untracked>"}') -- " +
                                    "net48 BinaryWriter.Write has only (byte[]) and (char[]) " +
                                    "[Reference Source binarywriter.cs:169,209]");
                        }
                        if (count == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                        {
                            var sigElem = BinaryIOArrayElementByte(md, sigHandle);
                            if (sigElem is 0x05)
                                return I("il2cpp::runtime::icall_binary_write_bytes", 3, false, false, true, new[] { "", "a", "i", "i" });
                            if (sigElem is 0x03)
                                return I("il2cpp::runtime::icall_binary_write_chars", 3, false, false, true, new[] { "", "a", "i", "i" });
                            return arg0Type is "System.Byte[]"
                                ? I("il2cpp::runtime::icall_binary_write_bytes", 3, false, false, true, new[] { "", "a", "i", "i" })
                                : arg0Type is "System.Char[]"
                                ? I("il2cpp::runtime::icall_binary_write_chars", 3, false, false, true, new[] { "", "a", "i", "i" })
                                : throw new NotSupportedException(
                                    $"LOUD whitelist (net48 has no such overload): Write(array, index, count) " +
                                    $"with element type 0x{(sigElem ?? 0):X2} (arg0Type='{arg0Type ?? "<untracked>"}') " +
                                    "-- net48 BinaryWriter.Write has only (byte[],int,int) and (char[],int,int) " +
                                    "[Reference Source binarywriter.cs:181,224]");
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
                            // batch 2026-10-05 (parity-T4a): the narrow/ulong
                            // widths -- the runtime icalls exist (streamio.cpp)
                            // and emit the exact 1/2/8-byte little-endian cells.
                            0x05 => I("il2cpp::runtime::icall_binary_write_byte", 1, false, false, true, new[] { "", "i" }),
                            0x04 => I("il2cpp::runtime::icall_binary_write_sbyte", 1, false, false, true, new[] { "", "i" }),
                            0x06 => I("il2cpp::runtime::icall_binary_write_int16", 1, false, false, true, new[] { "", "i" }),
                            0x07 => I("il2cpp::runtime::icall_binary_write_uint16", 1, false, false, true, new[] { "", "i" }),
                            0x0B => I("il2cpp::runtime::icall_binary_write_uint64", 1, false, false, true, new[] { "", "l" }),
                            0x1D => throw new NotSupportedException(
                                "LOUD whitelist (net48 has no such overload): Write(array) shape -- net48 " +
                                "BinaryWriter.Write takes the whole array in 1 arg and the ranged form in " +
                                "(array, int, int) [Reference Source binarywriter.cs:169,181]"),
                            _ => throw new NotSupportedException(
                                $"LOUD whitelist (net48 has no such overload): Write param type 0x{ps[0]:X2} -- " +
                                "net48 BinaryWriter.Write has no such parameter type " +
                                "[Reference Source binarywriter.cs:142-355]"),
                        };
                    case "Flush":
                        // BinaryWriter.Flush() (batch 2026-10-05): flushes the
                        // wrapped stream's backend (memory streams no-op).
                        return I("il2cpp::runtime::icall_stream_flush", 0, false, false, true);
                    case "Close":
                    case "Dispose":
                        // BinaryWriter.Close() (net48): closes the wrapped
                        // stream. The writer's OWN handle is released
                        // (refCounted); a MemoryStream the caller still holds
                        // stays readable (net48 quirk -- the probes rely on it).
                        if (count == 0) return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
                        if (count == 1) return I("il2cpp::runtime::icall_stream_close", 1, false, false, true, new[] { "drop" });
                        throw new NotSupportedException(
                            "LOUD whitelist (net48 has no such overload): Close/Dispose with an unsupported " +
                            "arity -- net48 BinaryWriter has only Close() and Dispose() (the protected " +
                            "Dispose(bool) is not callable) [Reference Source binarywriter.cs:95,110]");
                    case ".ctor":
                        // .ctor(System.IO.Stream) -- batch 150d: the runtime
                        // object surface exists (icall_binary_writer_ctor,
                        // il2cpp_streamio.h:274 -- the receiver is the newobj
                        // temp, the stream argument passes through uncast).
                        return I("il2cpp::runtime::icall_binary_writer_ctor", 1, false, false, true, new[] { "" });
                    case "get_BaseStream":
                        // get_BaseStream() -- binrw3: the runtime object now
                        // stores the wrapped stream at slot 2 (a GC ref, the
                        // Il2CppStreamObject.baseStream field) and returns it.
                        return I("il2cpp::runtime::icall_binary_get_base_stream", 0, true, true, true,
                            returnTypeName: "System.IO.Stream");
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
                        // batch 2026-10-05 (parity-T4a): the narrow read widths
                        // -- the runtime icalls exist (streamio.cpp): sbyte/
                        // int16 sign-extend into the int32 carrier, ushort
                        // zero-extends, ulong rides uint64 ("l" result slot).
                        if (name == "ReadSByte") return I("il2cpp::runtime::icall_binary_read_sbyte", 0, true, false, true);
                        if (name == "ReadInt16") return I("il2cpp::runtime::icall_binary_read_int16", 0, true, false, true);
                        if (name == "ReadUInt16") return I("il2cpp::runtime::icall_binary_read_uint16", 0, true, false, true);
                        return I("il2cpp::runtime::icall_binary_read_uint64", 0, true, false, true, null, "l");
                    case "Flush":
                    case "Close":
                    case "Dispose":
                        // BinaryReader.Close/Flush (batch 2026-10-05): the
                        // reader's own handle is released/refCounted (see the
                        // BinaryWriter.Close note).
                        if (name == "Flush" && count == 0) return I("il2cpp::runtime::icall_stream_flush", 0, false, false, true);
                        if (name == "Close" && count == 0) return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
                        if (name == "Dispose" && count == 0) return I("il2cpp::runtime::icall_stream_close", 0, false, false, true);
                        if (name == "Dispose" && count == 1) return I("il2cpp::runtime::icall_stream_close", 1, false, false, true, new[] { "drop" });
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): {name} with an unsupported arity " +
                            "-- net48 BinaryReader has only Close() and Dispose() [Reference Source " +
                            "binaryreader.cs:87,106]");
                    case "ReadChar": return I("il2cpp::runtime::icall_binary_read_char", 0, true, false, true);
                    // T4b parity: ReadDecimal -- the decimal arrives through
                    // the OUT slot (self, out); ResultKind "dec2" appends the
                    // addressed result temp LAST (the "dec" prepend contract
                    // would put it first -- wrong order here).
                    case "ReadDecimal": return I("il2cpp::runtime::icall_binary_read_decimal", 0, true, false, true, null, "dec2");
                    case "get_BaseStream":
                        // get_BaseStream() -- binrw3 (see the BinaryWriter
                        // sibling): the wrapped stream is stored at slot 2.
                        return I("il2cpp::runtime::icall_binary_get_base_stream", 0, true, true, true,
                            returnTypeName: "System.IO.Stream");
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
