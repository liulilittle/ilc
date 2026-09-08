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
//                 IcallArgOrder (CodeGenerator.cs).
//   GetByte -> icall_buffer_get_byte (2 args, int32); SetByte -> LOUD
//                 rejection (no runtime icall).
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
                    throw new NotSupportedException($"BlockCopy with {ps.Length} params is not the 5-arg (Array, int, Array, int, int) form");
                return I("il2cpp::runtime::il2cpp_buffer_block_copy", 5, false, false, false,
                    new[] { "", "i", "", "i", "i" });
            // Buffer.ByteLength(Array array) -> int32
            case "ByteLength":
                if (ps.Length != 1)
                    throw new NotSupportedException($"ByteLength with {ps.Length} params is not the 1-arg (Array) form");
                return I("il2cpp::runtime::il2cpp_buffer_byte_length", 1, true, false, false,
                    new[] { "" });
            // Buffer.MemoryCopy(void* source, void* destination, long
            // destinationSizeInBytes, long sourceBytesToCopy) -- the runtime's
            // il2cpp_buffer_memory_copy_sized(dst, src, destSize, srcBytes)
            // takes destination FIRST; IcallArgOrder reorders IL args
            // (source, destination, ...) into runtime (dst, src, ...) order.
            case "MemoryCopy":
                if (ps.Length != 4)
                    throw new NotSupportedException($"MemoryCopy with {ps.Length} params is not the 4-arg (void*, void*, long, long) form");
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
                throw new NotSupportedException($"GetByte with {ps.Length} params is not the 2-arg (Array, int) form");
            case "SetByte":
                throw new NotSupportedException("no runtime icall (runtime surface: block_copy/byte_length/get_byte/memory_copy only)");
            default: return null;
        }
    }
}
