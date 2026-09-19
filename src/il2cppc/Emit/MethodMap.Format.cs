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
        // Only the format-STRING overloads carry a format through. The
        // provider-only ToString(IFormatProvider) has no format slot, so the
        // provider is dropped and the receiver routes to the DEFAULT formatter
        // (icall_int32/int64_to_string) with the IL arity preserved (the fold
        // pops the provider; "drop" keeps it out of the C++ call). The culture
        // is fixed by the runtime, so the net48 value for the common cultures
        // is unchanged.
        if (ps[0] != 0x0E)
            return owner switch
            {
                "System.Int32" or "System.UInt32" => I("il2cpp::runtime::icall_int32_to_string", 1, true, true, true,
                    new[] { receiverIsAddress ? "deref:i" : "unbox:i", "drop" }),
                "System.Int64" or "System.UInt64" => I("il2cpp::runtime::icall_int64_to_string", 1, true, true, true,
                    new[] { receiverIsAddress ? "deref:l" : "unbox:l", "drop" }),
                "System.Double" or "System.Single" => I("il2cpp::runtime::icall_double_to_string", 1, true, true, true,
                    new[] { receiverIsAddress ? "deref:d" : "unbox:d", "drop" }),
                _ => throw new NotSupportedException("ToString(IFormatProvider) not wired: icall_format_*_custom takes a format string and the M-stage emitter cannot inject a null format (culture is fixed anyway)"),
            };
        return owner switch
        {
            // ToString(string format) / ToString(string, IFormatProvider) --
            // the format_ext domain honors the standard/custom subset
            // (D/N/C/E/G/X...); the format string is passed through
            // (Il2CppObject* slot, no cast). saudit4: the provider overload
            // follows the double/decimal precedent -- ProviderCulture passes
            // the culture through the trailing slot (nullptr for the 1-arg
            // form via the runtime default argument), so invariant C/P
            // inside ToString(format, provider) are honored.
            "System.Int32" => I("il2cpp::runtime::icall_format_int32_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:i" : "unbox:i", ps.Length),
                providerCulture: ps.Length == 2),
            "System.Int64" => I("il2cpp::runtime::icall_format_int64_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:l" : "unbox:l", ps.Length),
                providerCulture: ps.Length == 2),
            // ToString(string format) -- the format_ext double formatter
            // (F/N/E/G/C/P + custom '0'/'.' patterns). cultureprobe: the
            // 2-arg provider overload PASSES the provider through
            // (ProviderCulture; the cast list keeps its unit-pinned "drop"
            // shape) and the 1-arg form appends a nullptr provider so the
            // runtime's new 3-param ABI stays satisfied on both shapes.
            "System.Double" => I("il2cpp::runtime::icall_format_double_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:d" : "unbox:d", ps.Length),
                appendLiterals: ps.Length == 1 ? new[] { "nullptr" } : null,
                providerCulture: ps.Length == 2),
            // float values live in double slots (the icall_double_to_string
            // precedent in MethodMap.ValueType.cs); ToString(format) routes
            // to the double formatter the same way.
            "System.Single" => I("il2cpp::runtime::icall_format_double_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:d" : "unbox:d", ps.Length),
                appendLiterals: ps.Length == 1 ? new[] { "nullptr" } : null,
                providerCulture: ps.Length == 2),
            // decimal: the custom-format branch of the decimal domain (T2
            // icall_decimal_to_string_custom, G/F/N/E/C subset). The icall
            // takes const Il2CppDecimal* -- the SHAPE-AWARE "dec" cast renders
            // (&slot) for a ldloca address and (&(value)) for a value read
            // (never the deref that "deref:dec" would produce). cultureprobe:
            // same provider passthrough/nullptr-append shape as double.
            "System.Decimal" => I("il2cpp::runtime::icall_decimal_to_string_custom", ps.Length, true, true, true,
                ps.Length == 1 ? new[] { "dec", "s" } : new[] { "dec", "s", "drop" },
                appendLiterals: ps.Length == 1 ? new[] { "nullptr" } : null,
                providerCulture: ps.Length == 2),
            // Unsigned integers ride the SIGNED int engines (the value lives
            // in an int32/int64 slot). Hex ("X") is exact for the full
            // unsigned range (two's-complement bit pattern -- .NET X on
            // uint/ulong prints the full width). DOCUMENTED SUBSET BOUNDARY:
            // decimal-family formats (D/N/C/E/F/P/G) of values >= 2^31
            // (uint32) / >= 2^63 (uint64) would render through the signed
            // digit loop and print a negative number -- out of subset.
            "System.UInt32" => I("il2cpp::runtime::icall_format_int32_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:i" : "unbox:i", ps.Length),
                providerCulture: ps.Length == 2),
            "System.UInt64" => I("il2cpp::runtime::icall_format_int64_custom", ps.Length, true, true, true,
                FormatCasts(receiverIsAddress ? "deref:l" : "unbox:l", ps.Length),
                providerCulture: ps.Length == 2),
            _ => null,
        };
    }

    // Receiver cast + format-string slot, plus the dropped culture argument
    // for the ToString(string, IFormatProvider) overload -- same cast shape
    // as icall_datetime_to_string_format (MethodMap.DateTime.cs).
    private static string[] FormatCasts(string receiver, int argCount) =>
        argCount == 1 ? new[] { receiver, "" } : new[] { receiver, "", "drop" };
}
