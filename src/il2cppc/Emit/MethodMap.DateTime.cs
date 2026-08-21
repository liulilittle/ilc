namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.DateTime -- System.DateTime mappings (il2cpp_time.h /
// il2cpp_time_ext.h). The runtime represents a DateTime as an 8-byte VALUE
// (Il2CppDateTime -- int64 ticks). Receivers arrive as ldloca ADDRESSES of a
// value-typed local ("deref:dt" cast); results are carried in "dt"-kind
// temps. Value-type .ctor icalls return the initialized value -- the emitter
// stores it into the receiver slot (ResultIsReceiver).
// Owners: System.DateTime
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDateTime(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (ps.Length == 1 && ps[0] == 0x0A)
                    // DateTime(long ticks) -- batch 151.
                    return I("il2cpp::runtime::icall_datetime_from_ticks", 1, false, false, true, new[] { "", "l" }, "dt", resultIsReceiver: true);
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_datetime_from_ymd", 3, false, false, true, null, "dt", resultIsReceiver: true);
                if (ps.Length == 6)
                    return I("il2cpp::runtime::icall_datetime_from_ymdhms", 6, false, false, true, null, "dt", resultIsReceiver: true);
                throw new NotSupportedException($"DateTime ctor with {ps.Length} arg(s) not wired in M5 (the runtime has from_ticks/from_ymd/from_ymdhms only)");
            case "get_UtcNow":
                return I("il2cpp::runtime::icall_datetime_utc_now", 0, true, false, false, null, "dt");
            case "get_Now":
                return I("il2cpp::runtime::icall_datetime_now", 0, true, false, false, null, "dt");
            case "get_Year": return I("il2cpp::runtime::icall_datetime_get_year", 0, true, false, true, new[] { "deref:dt" });
            case "get_Month": return I("il2cpp::runtime::icall_datetime_get_month", 0, true, false, true, new[] { "deref:dt" });
            case "get_Day": return I("il2cpp::runtime::icall_datetime_get_day", 0, true, false, true, new[] { "deref:dt" });
            case "get_Hour": return I("il2cpp::runtime::icall_datetime_get_hour", 0, true, false, true, new[] { "deref:dt" });
            case "get_Minute": return I("il2cpp::runtime::icall_datetime_get_minute", 0, true, false, true, new[] { "deref:dt" });
            case "get_Second": return I("il2cpp::runtime::icall_datetime_get_second", 0, true, false, true, new[] { "deref:dt" });
            case "get_Ticks": return I("il2cpp::runtime::icall_datetime_get_ticks", 0, true, false, true, new[] { "deref:dt" }, "l");
            case "get_DayOfWeek": return I("il2cpp::runtime::icall_datetime_get_dayofweek", 0, true, false, true, new[] { "deref:dt" });
            case "AddDays": return I("il2cpp::runtime::icall_datetime_add_days", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddHours": return I("il2cpp::runtime::icall_datetime_add_hours", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddMinutes": return I("il2cpp::runtime::icall_datetime_add_minutes", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "Subtract":
                // DateTime.Subtract(DateTime) -> TimeSpan.
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_datetime_subtract", 1, true, false, true, new[] { "deref:dt", "" }, "ts")
                    : throw new NotSupportedException("DateTime.Subtract(TimeSpan) not wired in M5: the runtime has only the (DateTime, DateTime) -> TimeSpan icall");
            case "op_Subtraction":
                // DateTime - DateTime -> TimeSpan (static).
                return I("il2cpp::runtime::icall_datetime_subtract", 2, true, false, false, new[] { "", "" }, "ts");
            case "CompareTo":
                return I("il2cpp::runtime::icall_datetime_compare_to", 1, true, false, true, new[] { "deref:dt", "" });
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_to_string", 0, true, true, true, new[] { "deref:dt" });
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_datetime_to_string_format", 1, true, true, true, new[] { "deref:dt", "" });
                if (ps.Length == 2)
                    // ToString(string, IFormatProvider): ParamCount = the IL
                    // arity (the FOLD pops 2); the culture arg is dropped at
                    // emission -- the runtime fixes en-US/invariant output.
                    return I("il2cpp::runtime::icall_datetime_to_string_format", 2, true, true, true, new[] { "deref:dt", "", "drop" });
                throw new NotSupportedException($"DateTime.ToString with {ps.Length} arg(s) not wired in M5");
            default: return null;
        }
    }
}
