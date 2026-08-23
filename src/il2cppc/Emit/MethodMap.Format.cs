namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Format -- the numeric ToString(string format) icalls
// (il2cpp_format_ext.h). The parameterless ToString/Parse/Equals/GetHashCode
// of the numeric types live in MethodMap.ValueType.cs; THIS file owns ONLY
// the custom-format branches (icall_format_int32/int64/double_custom), so a
// format wiring agent never collides with a value-semantics agent. The
// dispatch switch runs TryMapFormat BEFORE TryMapValueType for these owners.
// Owners: System.Int32 / System.Int64 / System.Double / System.Single
//         (ToString(format) only; Single routes to the double icall -- float
//         values live in double slots, the ValueType.cs precedent)
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapFormat(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (name != "ToString" || ps.Length == 0) return null;
        // Only the format-STRING overloads are wireable (ps[0] == 0x0E =
        // System.String). ToString(IFormatProvider) has no icall shape: the
        // format_ext icalls take a format STRING and the M-stage emitter
        // cannot inject a null format (there is no PrependNullArg) -- it must
        // be loud, per the wiring manual (the culture is fixed anyway).
        if (ps[0] != 0x0E)
            throw new NotSupportedException("ToString(IFormatProvider) not wired: icall_format_*_custom takes a format string and the M-stage emitter cannot inject a null format (culture is fixed anyway)");
        return owner switch
        {
            // ToString(string format) / ToString(string, IFormatProvider) --
            // the format_ext domain honors the standard/custom subset
            // (D/N/C/E/G/X...); the format string is passed through
            // (Il2CppObject* slot, no cast). The provider overload follows
            // the DateTime precedent: ParamCount = the IL arity (the FOLD
            // pops 2); the culture arg is dropped at emission.
            "System.Int32" => I("il2cpp::runtime::icall_format_int32_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:i" : "unbox:i", ps.Length)),
            "System.Int64" => I("il2cpp::runtime::icall_format_int64_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:l" : "unbox:l", ps.Length)),
            // ToString(string format) -- the format_ext double formatter
            // (F/N/E/G/C/P + custom '0'/'.' patterns).
            "System.Double" => I("il2cpp::runtime::icall_format_double_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:d" : "unbox:d", ps.Length)),
            // float values live in double slots (the icall_double_to_string
            // precedent in MethodMap.ValueType.cs); ToString(format) routes
            // to the double formatter the same way.
            "System.Single" => I("il2cpp::runtime::icall_format_double_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:d" : "unbox:d", ps.Length)),
            // decimal: the custom-format branch of the decimal domain (T2
            // icall_decimal_to_string_custom, G/F/N/E/C subset). The icall
            // takes const Il2CppDecimal* -- the SHAPE-AWARE "dec" cast renders
            // (&slot) for a ldloca address and (&(value)) for a value read
            // (never the deref that "deref:dec" would produce).
            "System.Decimal" => I("il2cpp::runtime::icall_decimal_to_string_custom", ps.Length, true, true, true,
                ps.Length == 1 ? new[] { "dec", "s" } : new[] { "dec", "s", "drop" }),
            // Unsigned integers ride the SIGNED int engines (the value lives
            // in an int32/int64 slot). Hex ("X") is exact for the full
            // unsigned range (two's-complement bit pattern -- .NET X on
            // uint/ulong prints the full width). DOCUMENTED SUBSET BOUNDARY:
            // decimal-family formats (D/N/C/E/F/P/G) of values >= 2^31
            // (uint32) / >= 2^63 (uint64) would render through the signed
            // digit loop and print a negative number -- out of subset.
            "System.UInt32" => I("il2cpp::runtime::icall_format_int32_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:i" : "unbox:i", ps.Length)),
            "System.UInt64" => I("il2cpp::runtime::icall_format_int64_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:l" : "unbox:l", ps.Length)),
            _ => null,
        };
    }

    // Receiver cast + format-string slot, plus the dropped culture argument
    // for the ToString(string, IFormatProvider) overload -- same cast shape
    // as icall_datetime_to_string_format (MethodMap.DateTime.cs).
    private static string[] FormatCasts(string receiver, int argCount) =>
        argCount == 1 ? new[] { receiver, "" } : new[] { receiver, "", "drop" };
}
