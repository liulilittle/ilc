namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ConvertExtra -- System.Convert checked-narrowing gap surface.
// Owners: System.Convert
//
// The narrowing conversions Convert.ToByte/ToSByte/ToInt16/ToUInt16/ToUInt32/
// ToUInt64/ToSingle/ToDecimal have NO runtime icall on the M5 surface
// (il2cpp_convert.h carries only the int32/int64/double/char/bool/string
// forms wired in MethodMap.Convert.cs). net48 makes every one of them a
// CHECKED conversion (e.g. ToByte(int) throws OverflowException outside
// 0..255, ToInt16 outside -32768..32767). No existing icall can express
// those ranges, and reusing a wrong-range helper (e.g. the char icall, which
// accepts 0..0xFFFF) would silently change overflow behavior -- so these
// members are rejected LOUD (the manual's known-unwireable rule).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapConvertExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "ToByte":
            case "ToSByte":
            case "ToInt16":
            case "ToUInt16":
            case "ToUInt32":
            case "ToUInt64":
            case "ToSingle":
            case "ToDecimal":
                throw new NotSupportedException($"Convert.{name} is a CHECKED narrowing conversion with no runtime icall on the M5 surface (a wrong-range helper would silently change overflow semantics)");
            default: return null;
        }
    }
}
