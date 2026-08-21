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
                throw new NotSupportedException($"TimeSpan ctor with {ps.Length} arg(s) not wired in M5 (the runtime has from_ticks/from_hms/from_dhms only)");
            case "get_Ticks": return I("il2cpp::runtime::icall_timespan_get_ticks", 0, true, false, true, new[] { "deref:ts" }, "l");
            case "get_Days": return I("il2cpp::runtime::icall_timespan_get_days", 0, true, false, true, new[] { "deref:ts" });
            case "get_Hours": return I("il2cpp::runtime::icall_timespan_get_hours", 0, true, false, true, new[] { "deref:ts" });
            case "get_Minutes": return I("il2cpp::runtime::icall_timespan_get_minutes", 0, true, false, true, new[] { "deref:ts" });
            case "get_Seconds": return I("il2cpp::runtime::icall_timespan_get_seconds", 0, true, false, true, new[] { "deref:ts" });
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
            // net9/10 E2E icallcase: the static From* factories (time.cpp).
            case "FromMinutes":
                return I("il2cpp::runtime::icall_timespan_from_minutes", 1, true, false, false, new[] { "d" }, "ts");
            case "ToString":
                throw new NotSupportedException("TimeSpan.ToString not wired: the runtime has no timespan formatter icall");
            default: return null;
        }
    }
}
