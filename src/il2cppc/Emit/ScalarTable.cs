using System.Collections.Generic;

namespace Il2Cpp.Transpiler.Emit;

// B-class scalar-family 238-row unification: a single scalar table (C6 knowledge duplication elimination)
// Covers: IsPrimValTypeShort + ScalarElemCpp + the box/unbox switch key set + the KnownBclEnumNames whitelist
// Kept in sync with CodeGenerator.EmitTask9Call / EmitIcallCallArgs (the two functions used to maintain the same table independently)
internal static class ScalarTable
{
    internal sealed record ScalarDescriptor(string CppType, string BoxHelper, string UnboxHelper, bool NeedsAddressOf);

    // main table: scalar full name -> descriptor (static readonly D1)
    internal static readonly IReadOnlyDictionary<string, ScalarDescriptor> Table = new Dictionary<string, ScalarDescriptor>
    {
        ["System.Boolean"] = new("int32_t", "il2cpp::runtime::icall_box_bool", "il2cpp::runtime::icall_unbox_bool", false),
        ["System.Byte"] = new("int32_t", "il2cpp::runtime::icall_box_byte", "il2cpp::runtime::icall_unbox_byte", false),
        ["System.SByte"] = new("int32_t", "il2cpp::runtime::icall_box_sbyte", "il2cpp::runtime::icall_unbox_sbyte", false),
        ["System.Char"] = new("int32_t", "il2cpp::runtime::icall_box_char", "il2cpp::runtime::icall_unbox_char", false),
        ["System.Int16"] = new("int32_t", "il2cpp::runtime::icall_box_short", "il2cpp::runtime::icall_unbox_short", false),
        ["System.UInt16"] = new("int32_t", "il2cpp::runtime::icall_box_ushort", "il2cpp::runtime::icall_unbox_ushort", false),
        ["System.Int32"] = new("int32_t", "il2cpp::il2cpp_box_int32", "il2cpp::il2cpp_unbox_int32", false),
        ["System.UInt32"] = new("int32_t", "il2cpp::runtime::icall_box_uint", "il2cpp::runtime::icall_unbox_uint", false),
        ["System.Int64"] = new("int64_t", "il2cpp::runtime::icall_box_long", "il2cpp::runtime::icall_unbox_long", false),
        ["System.UInt64"] = new("int64_t", "il2cpp::runtime::icall_box_ulong", "il2cpp::runtime::icall_unbox_ulong", false),
        ["System.Single"] = new("float", "il2cpp::runtime::icall_box_float", "il2cpp::runtime::icall_unbox_float", false),
        ["System.Double"] = new("double", "il2cpp::runtime::icall_box_double", "il2cpp::runtime::icall_unbox_double", false),
        ["System.DateTime"] = new("il2cpp::runtime::Il2CppDateTime", "il2cpp::runtime::icall_box_date_time", "il2cpp::runtime::icall_unbox_date_time", true),
        ["System.TimeSpan"] = new("il2cpp::runtime::Il2CppTimeSpan", "il2cpp::runtime::icall_box_time_span", "il2cpp::runtime::icall_unbox_time_span", true),
        ["System.Decimal"] = new("il2cpp::runtime::Il2CppDecimal", "il2cpp::runtime::icall_box_decimal", "il2cpp::runtime::icall_unbox_decimal", true),
        ["System.Guid"] = new("System_Guid", "il2cpp::runtime::icall_box_guid", "il2cpp::runtime::icall_unbox_guid", true),
        ["System.DateTimeOffset"] = new("il2cpp::runtime::Il2CppDateTimeOffset", "il2cpp::runtime::icall_box_date_time_offset", "il2cpp::runtime::icall_unbox_date_time_offset", true),
        ["System.Numerics.BigInteger"] = new("il2cpp::runtime::Il2CppBigInteger", "il2cpp::runtime::icall_box_big_integer", "il2cpp::runtime::icall_unbox_big_integer", true),
        // IntPtr/UIntPtr: pointer-width value types -- reuse the int64/uint64
        // box helper (same 8-byte payload layout) with a reinterpret cast at the
        // call site (CodeGenerator emits static_cast<int64_t>(reinterpret_cast<intptr_t>(...))).
        ["System.IntPtr"] = new("intptr_t", "il2cpp::runtime::icall_box_long", "il2cpp::runtime::icall_unbox_long", false),
        ["System.UIntPtr"] = new("uintptr_t", "il2cpp::runtime::icall_box_ulong", "il2cpp::runtime::icall_unbox_ulong", false),
    };

    internal static bool IsScalar(string fullName) => Table.ContainsKey(fullName);

    internal static bool TryGet(string fullName, out ScalarDescriptor desc) => Table.TryGetValue(fullName, out desc!);
}
