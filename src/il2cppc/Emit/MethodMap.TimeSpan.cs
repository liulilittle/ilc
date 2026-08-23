namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.TimeSpan -- System.TimeSpan mappings (il2cpp_time.h) -- same
// value-type model as DateTime (Il2CppTimeSpan = int64 ticks).
// Owners: System.TimeSpan
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapTimeSpan(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_timespan_from_ticks", 1, false, false, true, new[] { "", "l" }, "ts", resultIsReceiver: true);
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_timespan_from_hms", 3, false, false, true, null, "ts", resultIsReceiver: true);
                if (ps.Length == 4)
                    // TimeSpan(days, hours, minutes, seconds) -- batch 152.
                    return I("il2cpp::runtime::icall_timespan_from_dhms", 4, false, false, true, null, "ts", resultIsReceiver: true);
                if (ps.Length == 5)
                    // TimeSpan(days, hours, minutes, seconds, milliseconds).
                    return I("il2cpp::runtime::icall_timespan_from_dhmsm", 5, false, false, true, null, "ts", resultIsReceiver: true);
                throw new NotSupportedException($"TimeSpan ctor with {ps.Length} arg(s) not wired in M5 (the runtime has from_ticks/from_hms/from_dhms/from_dhmsm only)");
            case "get_Ticks": return I("il2cpp::runtime::icall_timespan_get_ticks", 0, true, false, true, new[] { "deref:ts" }, "l");
            case "get_Days": return I("il2cpp::runtime::icall_timespan_get_days", 0, true, false, true, new[] { "deref:ts" });
            case "get_Hours": return I("il2cpp::runtime::icall_timespan_get_hours", 0, true, false, true, new[] { "deref:ts" });
            case "get_Minutes": return I("il2cpp::runtime::icall_timespan_get_minutes", 0, true, false, true, new[] { "deref:ts" });
            case "get_Seconds": return I("il2cpp::runtime::icall_timespan_get_seconds", 0, true, false, true, new[] { "deref:ts" });
            case "get_Milliseconds": return I("il2cpp::runtime::icall_timespan_get_milliseconds", 0, true, false, true, new[] { "deref:ts" });
            // GetHashCode / Equals(TimeSpan): the ticks hash / ticks equality.
            case "GetHashCode": return I("il2cpp::runtime::icall_timespan_get_hash_code", 0, true, false, true, new[] { "deref:ts" });
            case "Equals": return I("il2cpp::runtime::icall_timespan_equals", 1, true, false, true, new[] { "deref:ts", "" });
            // The relational operators: pure ticks comparison.
            case "op_Equality": return I("il2cpp::runtime::icall_timespan_op_equality", 2, true, false, false, new[] { "", "" });
            case "op_Inequality": return I("il2cpp::runtime::icall_timespan_op_inequality", 2, true, false, false, new[] { "", "" });
            case "op_LessThan": return I("il2cpp::runtime::icall_timespan_op_less_than", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThan": return I("il2cpp::runtime::icall_timespan_op_greater_than", 2, true, false, false, new[] { "", "" });
            case "op_LessThanOrEqual": return I("il2cpp::runtime::icall_timespan_op_less_or_equal", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThanOrEqual": return I("il2cpp::runtime::icall_timespan_op_greater_or_equal", 2, true, false, false, new[] { "", "" });
            case "get_TotalSeconds": return I("il2cpp::runtime::icall_timespan_get_total_seconds", 0, true, false, true, new[] { "deref:ts" }, "d");
            case "get_TotalMinutes": return I("il2cpp::runtime::icall_timespan_get_total_minutes", 0, true, false, true, new[] { "deref:ts" }, "d");
            case "get_TotalHours": return I("il2cpp::runtime::icall_timespan_get_total_hours", 0, true, false, true, new[] { "deref:ts" }, "d");
            case "get_TotalDays": return I("il2cpp::runtime::icall_timespan_get_total_days", 0, true, false, true, new[] { "deref:ts" }, "d");
            case "get_TotalMilliseconds": return I("il2cpp::runtime::icall_timespan_get_total_milliseconds", 0, true, false, true, new[] { "deref:ts" }, "d");
            case "Add":
            case "op_Addition":
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_timespan_add", 1, true, false, true, new[] { "deref:ts", "" }, "ts")
                    : I("il2cpp::runtime::icall_timespan_add", 2, true, false, false, new[] { "", "" }, "ts");
            case "Subtract":
            case "op_Subtraction":
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_timespan_subtract", 1, true, false, true, new[] { "deref:ts", "" }, "ts")
                    : I("il2cpp::runtime::icall_timespan_subtract", 2, true, false, false, new[] { "", "" }, "ts");
            case "Negate":
            case "op_UnaryNegation":
                return ps.Length == 0
                    ? I("il2cpp::runtime::icall_timespan_negate", 0, true, false, true, new[] { "deref:ts" }, "ts")
                    : I("il2cpp::runtime::icall_timespan_negate", 1, true, false, false, new[] { "" }, "ts");
            case "CompareTo":
                return I("il2cpp::runtime::icall_timespan_compare_to", 1, true, false, true, new[] { "deref:ts", "" });
            // static int Compare(TimeSpan, TimeSpan): the same comparator
            // without the receiver slot.
            case "Compare":
                return I("il2cpp::runtime::icall_timespan_compare_to", 2, true, false, false, new[] { "", "" });
            // Duration(): the absolute value.
            case "Duration":
                return I("il2cpp::runtime::icall_timespan_duration", 0, true, false, true, new[] { "deref:ts" }, "ts");
            // net9/10 E2E icallcase: the static From* factories (time.cpp).
            case "FromMinutes":
                return I("il2cpp::runtime::icall_timespan_from_minutes", 1, true, false, false, new[] { "d" }, "ts");
            case "FromHours":
                return I("il2cpp::runtime::icall_timespan_from_hours", 1, true, false, false, new[] { "d" }, "ts");
            case "FromDays":
                return I("il2cpp::runtime::icall_timespan_from_days", 1, true, false, false, new[] { "d" }, "ts");
            case "FromSeconds":
                return I("il2cpp::runtime::icall_timespan_from_seconds", 1, true, false, false, new[] { "d" }, "ts");
            case "FromMilliseconds":
                return I("il2cpp::runtime::icall_timespan_from_milliseconds", 1, true, false, false, new[] { "d" }, "ts");
            case "FromTicks":
                // NOTE: the icall name differs from the 1-arg ctor's
                // from_ticks only in suffix -- distinct entry, same semantics.
                return I("il2cpp::runtime::icall_timespan_from_ticks_exact", 1, true, false, false, new[] { "l" }, "ts");
            case "ToString":
                return I("il2cpp::runtime::icall_timespan_to_string", 0, true, true, true, new[] { "deref:ts" });
            default: return null;
        }
    }
}
