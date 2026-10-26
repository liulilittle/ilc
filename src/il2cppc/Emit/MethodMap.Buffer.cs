namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Buffer -- System.Buffer icall mappings (buffer.h / buffer.cpp).
// Owners: System.Buffer
//
//   BlockCopy  -> il2cpp::runtime::il2cpp_buffer_block_copy  (5 args, void)
//   ByteLength -> il2cpp::runtime::il2cpp_buffer_byte_length (1 arg, int32)
//   MemoryCopy -> il2cpp::runtime::il2cpp_buffer_memory_copy_sized (4 args,
//                 void): IL order (source, destination, destinationSizeInBytes,
//                 sourceBytesToCopy) reorders to runtime (dst, src, ...) via
//                 IcallArgOrder (CodeGenerator.cs). The net48 ulong overload
//                 (buffer.cs:518) takes the same 4-arg shape with U8 cells and
//                 rides the u64 entry (no checked-negative conversion).
//   GetByte -> icall_buffer_get_byte (2 args, int32)
//   SetByte -> icall_buffer_set_byte (3 args, void)
//
// Every arity outside the net48 overload set is a LOUD whitelist (class 4:
// net48 has no such overload) with the Reference Source line.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapBuffer(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // Buffer.BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
            case "BlockCopy":
                if (ps.Length != 5)
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Buffer.BlockCopy arity {ps.Length} -- " +
                        "the only net48 overload is the 5-arg (Array, int, Array, int, int) form " +
                        "[Reference Source mscorlib/system/buffer.cs:52]");
                return I("il2cpp::runtime::il2cpp_buffer_block_copy", 5, false, false, false,
                    new[] { "", "i", "", "i", "i" });
            // Buffer.ByteLength(Array array) -> int32
            case "ByteLength":
                if (ps.Length != 1)
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Buffer.ByteLength arity {ps.Length} -- " +
                        "the only net48 overload is the 1-arg (Array) form " +
                        "[Reference Source mscorlib/system/buffer.cs:222]");
                return I("il2cpp::runtime::il2cpp_buffer_byte_length", 1, true, false, false,
                    new[] { "" });
            // Buffer.MemoryCopy(void* source, void* destination, long
            // destinationSizeInBytes, long sourceBytesToCopy) -- the runtime's
            // il2cpp_buffer_memory_copy_sized(dst, src, destSize, srcBytes)
            // takes destination FIRST; IcallArgOrder reorders IL args
            // (source, destination, ...) into runtime (dst, src, ...) order.
            // net48 also has the ulong twin (buffer.cs:518): the same IL order
            // and parameter count, distinguished by the U8 parameter bytes.
            case "MemoryCopy":
                if (ps.Length != 4)
                    throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): Buffer.MemoryCopy arity {ps.Length} -- " +
                        "the only net48 overloads are the 4-arg (void*, void*, long, long) and " +
                        "(void*, void*, ulong, ulong) forms " +
                        "[Reference Source mscorlib/system/buffer.cs:503 and :518]");
                if (ps.Length >= 4 && ps[2] == 0x0B && ps[3] == 0x0B)
                    return I("il2cpp::runtime::il2cpp_buffer_memory_copy_sized_u64", 4, false, false, false,
                        new[] { "", "", "", "" });
                return I("il2cpp::runtime::il2cpp_buffer_memory_copy_sized", 4, false, false, false,
                    new[] { "", "", "", "" });
            // Known members with no runtime icall on the current surface.
            case "InternalBlockCopy":
                // Buffer.InternalBlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
                // Same as BlockCopy (the internal name).
                return I("il2cpp::runtime::il2cpp_buffer_block_copy", 5, false, false, false,
                    new[] { "", "i", "", "i", "i" });
            case "GetByte":
                // D5 cov2: Buffer.GetByte(Array, int) -- int32 carrier byte
                // read at the raw-data offset (icall_buffer_get_byte).
                if (ps.Length == 2)
                    return I("il2cpp::runtime::icall_buffer_get_byte", 2, true, false, false,
                        new[] { "", "i" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Buffer.GetByte arity {ps.Length} -- " +
                    "the only net48 overload is the 2-arg (Array, int) form " +
                    "[Reference Source mscorlib/system/buffer.cs:162]");
            case "SetByte":
                // Buffer.SetByte(Array, int, byte): the net48 3-arg shape. The
                // byte value arrives in an int32 carrier (the IL evaluation
                // stack widens it), so the "i" cast reaches the int32_t param.
                // Validation/exception contract mirrors GetByte (buffer.cpp).
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_buffer_set_byte", 3, false, false, false,
                        new[] { "", "i", "i" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): Buffer.SetByte arity {ps.Length} -- " +
                    "the only net48 overload is the 3-arg (Array, int, byte) form " +
                    "[Reference Source mscorlib/system/buffer.cs:191]");
            default: return null;
        }
    }
}
