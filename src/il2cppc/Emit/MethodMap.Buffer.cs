namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Buffer -- System.Buffer icall mappings (buffer.h / buffer.cpp).
// Owners: System.Buffer
//
//   BlockCopy  -> il2cpp::runtime::il2cpp_buffer_block_copy  (5 args, void)
//   ByteLength -> il2cpp::runtime::il2cpp_buffer_byte_length (1 arg, int32)
//   MemoryCopy -> LOUD rejection: the IL order (source, destination,
//                 destinationSizeInBytes, sourceBytesToCopy) cannot be
//                 positionally reordered to the runtime's memmove-ordered
//                 il2cpp_buffer_memory_copy(dst, src, count) -- a naive
//                 positional wiring would copy in the WRONG direction (data
//                 corruption). The M5 emitter has no arg-reordering facility
//                 (EmitIcallCallArgs emits in IR order), so the raw-pointer
//                 form is unwireable until reordering exists.
//   GetByte / SetByte -> LOUD rejection: known System.Buffer members with no
//                 runtime icall (the runtime surface is block_copy /
//                 byte_length / memory_copy only).
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
            // il2cpp_buffer_memory_copy(dst, src, count) is memmove-ordered;
            // positional IL-order emission would swap source/destination.
            case "MemoryCopy":
                throw new NotSupportedException(
                    "raw-pointer form: IL order (source, destination, destinationSizeInBytes, sourceBytesToCopy) cannot map positionally to runtime il2cpp_buffer_memory_copy(dst, src, count)");
            // Known members with no runtime icall on the current surface.
            case "GetByte":
            case "SetByte":
                throw new NotSupportedException("no runtime icall (runtime surface: block_copy/byte_length/memory_copy only)");
            default: return null;
        }
    }
}
