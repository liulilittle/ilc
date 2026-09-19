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
                // bcldeep: ctor(long ticks, DateTimeKind) -- the kind is
                // STORED by the runtime (net48 labels the instant; ticks are
                // never converted). The 2nd argument passes as int32.
                if (ps.Length == 2 && ps[0] == 0x0A && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_datetime_from_ticks_kind", 2, false, false, true, new[] { "", "l", "i" }, "dt", resultIsReceiver: true);
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_datetime_from_ymd", 3, false, false, true, null, "dt", resultIsReceiver: true);
                if (ps.Length == 6)
                    return I("il2cpp::runtime::icall_datetime_from_ymdhms", 6, false, false, true, null, "dt", resultIsReceiver: true);
                // bcldeep: ctor(y,m,d,h,mi,s,DateTimeKind) -- the trailing
                // DateTimeKind is STORED (net48: raw ticks + kind bits; the
                // single-zone doctrine keeps tick composition culture-free,
                // the kind only labels the value). NOTE: the casts array
                // INDEXES THE RECEIVER SLOT TOO (slot 0 = the value-slot
                // target; the from_ticks precedent's ["", "l"]), so null here
                // emits the standard 8-entry receiver+7-arg passthrough. The
                // 8-arg ms+kind form stays LOUD until a runtime entry with a
                // millisecond parameter exists.
                if (ps.Length == 7 && ps[6] == 0x11)
                    return I("il2cpp::runtime::icall_datetime_from_ymdhms_kind", 7, false, false, true,
                        null, "dt", resultIsReceiver: true);
                // D5 cov3: ctor(y,m,d,h,mi,s,ms) -- the 7-arg MILLISECOND form
                // (all-int32 signature; the kind-dropping 7-arg form above has
                // ps[6]==0x11 enum and is matched first).
                if (ps.Length == 7)
                    return I("il2cpp::runtime::icall_datetime_from_ymdhms_ms", 7, false, false, true,
                        null, "dt", resultIsReceiver: true);
                throw new NotSupportedException($"DateTime ctor with {ps.Length} arg(s) not wired in M5 (the runtime has from_ticks/from_ymd/from_ymdhms only)");
            case "get_UtcNow":
                return I("il2cpp::runtime::icall_datetime_utc_now", 0, true, false, false, null, "dt");
            case "get_Now":
                return I("il2cpp::runtime::icall_datetime_now", 0, true, false, false, null, "dt");
            case "get_Year": return I("il2cpp::runtime::icall_datetime_get_year", 0, true, false, true, new[] { "deref:dt" });
            case "get_Month": return I("il2cpp::runtime::icall_datetime_get_month", 0, true, false, true, new[] { "deref:dt" });
            case "get_Day": return I("il2cpp::runtime::icall_datetime_get_day", 0, true, false, true, new[] { "deref:dt" });
            case "get_Millisecond":
                // D5 cov3: the sub-second remainder (0..999).
                return I("il2cpp::runtime::icall_datetime_get_millisecond", 0, true, false, true, new[] { "deref:dt" });
            case "get_DayOfYear": return I("il2cpp::runtime::icall_datetime_get_day_of_year", 0, true, false, true, new[] { "deref:dt" });
            case "get_Hour": return I("il2cpp::runtime::icall_datetime_get_hour", 0, true, false, true, new[] { "deref:dt" });
            case "get_Minute": return I("il2cpp::runtime::icall_datetime_get_minute", 0, true, false, true, new[] { "deref:dt" });
            case "get_Second": return I("il2cpp::runtime::icall_datetime_get_second", 0, true, false, true, new[] { "deref:dt" });
            case "get_Ticks": return I("il2cpp::runtime::icall_datetime_get_ticks", 0, true, false, true, new[] { "deref:dt" }, "l");
            // ToFileTimeUtc / FromFileTimeUtc -- the 1601-epoch bridge
            // (time.cpp; the single-zone runtime has no Kind bits so the Utc
            // and Local forms coincide).
            case "ToFileTimeUtc":
                return I("il2cpp::runtime::icall_datetime_to_file_time_utc", 0, true, false, true, new[] { "deref:dt" }, "l");
            // B3 (diffdt001b): the Local form rides the same icall (the
            // single-zone doctrine above -- Utc and Local coincide).
            case "ToFileTime":
                return I("il2cpp::runtime::icall_datetime_to_file_time_utc", 0, true, false, true, new[] { "deref:dt" }, "l");
            case "FromFileTimeUtc":
                return I("il2cpp::runtime::icall_datetime_from_file_time_utc", 1, true, false, false, new[] { "l" }, "dt");
            // B3 (diffdt001b): the Local form rides the same icall.
            case "FromFileTime":
                return I("il2cpp::runtime::icall_datetime_from_file_time_utc", 1, true, false, false, new[] { "l" }, "dt");
            // AddMonths / AddYears -- civil-calendar arithmetic (the day
            // clamps to the target month's length; time-of-day preserved).
            case "AddMonths":
                return I("il2cpp::runtime::icall_datetime_add_months", 1, true, false, true, new[] { "deref:dt", "i" }, "dt");
            case "AddYears":
                return I("il2cpp::runtime::icall_datetime_add_years", 1, true, false, true, new[] { "deref:dt", "i" }, "dt");
            case "get_DayOfWeek": return I("il2cpp::runtime::icall_datetime_get_dayofweek", 0, true, false, true, new[] { "deref:dt" });
            case "AddDays": return I("il2cpp::runtime::icall_datetime_add_days", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddHours": return I("il2cpp::runtime::icall_datetime_add_hours", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddMinutes": return I("il2cpp::runtime::icall_datetime_add_minutes", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddSeconds": return I("il2cpp::runtime::icall_datetime_add_seconds", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            case "AddMilliseconds": return I("il2cpp::runtime::icall_datetime_add_milliseconds", 1, true, false, true, new[] { "deref:dt", "d" }, "dt");
            // Kind support (single-zone doctrine): every DateTime reads as
            // Utc; SpecifyKind is the identity on ticks (the kind argument
            // is dropped).
            case "get_Kind":
                return I("il2cpp::runtime::icall_datetime_get_kind", 0, true, false, true, new[] { "deref:dt" });
            case "SpecifyKind":
                return I("il2cpp::runtime::icall_datetime_specify_kind", 2, true, false, false, new[] { "", "i" }, "dt");
            // get_Date: midnight of the same day; get_TimeOfDay: the time
            // since midnight as a TimeSpan.
            case "get_Date":
                return I("il2cpp::runtime::icall_datetime_get_date", 0, true, false, true, new[] { "deref:dt" }, "dt");
            case "get_TimeOfDay":
                return I("il2cpp::runtime::icall_datetime_get_time_of_day", 0, true, false, true, new[] { "deref:dt" }, "ts");
            // Equals: pure ticks equality.
            case "Equals":
                return I("il2cpp::runtime::icall_datetime_equals", 1, true, false, true, new[] { "deref:dt", "" });
            // D1 (diffdt001 dt.hash): net48 ticks-fold
            // (unchecked((int)ticks) ^ (int)(ticks >> 32) -- the
            // icall_datetime_get_hash_code in time.cpp).
            case "GetHashCode":
                return I("il2cpp::runtime::icall_datetime_get_hash_code", 0, true, false, true, new[] { "deref:dt" });
            case "Subtract":
                // DateTime.Subtract(DateTime) -> TimeSpan.
                return ps.Length == 1
                    ? I("il2cpp::runtime::icall_datetime_subtract", 1, true, false, true, new[] { "deref:dt", "" }, "ts")
                    : throw new NotSupportedException("DateTime.Subtract(TimeSpan) not wired in M5: the runtime has only the (DateTime, DateTime) -> TimeSpan icall");
            case "op_Subtraction":
                // DateTime - DateTime -> TimeSpan (static).
                return I("il2cpp::runtime::icall_datetime_subtract", 2, true, false, false, new[] { "", "" }, "ts");
            case "op_Equality":
                return I("il2cpp::runtime::icall_datetime_op_equality", 2, true, false, false, new[] { "", "" });
            case "op_Inequality":
                return I("il2cpp::runtime::icall_datetime_op_inequality", 2, true, false, false, new[] { "", "" });
            case "op_LessThan":
                return I("il2cpp::runtime::icall_datetime_op_less_than", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThan":
                return I("il2cpp::runtime::icall_datetime_op_greater_than", 2, true, false, false, new[] { "", "" });
            case "op_LessThanOrEqual":
                return I("il2cpp::runtime::icall_datetime_op_less_or_equal", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThanOrEqual":
                return I("il2cpp::runtime::icall_datetime_op_greater_or_equal", 2, true, false, false, new[] { "", "" });
            case "CompareTo":
                return I("il2cpp::runtime::icall_datetime_compare_to", 1, true, false, true, new[] { "deref:dt", "" });
            // net9/10 E2E icallcase: DateTime.IsLeapYear / DaysInMonth --
            // pure calendar functions (stubs.cpp, proleptic Gregorian).
            case "IsLeapYear":
                return I("il2cpp::runtime::icall_datetime_is_leap_year", 1, true, false, false, new[] { "i" });
            case "DaysInMonth":
                return I("il2cpp::runtime::icall_datetime_days_in_month", 2, true, false, false, new[] { "i", "i" });
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_to_string", 0, true, true, true, new[] { "deref:dt" });
                if (ps.Length == 1 && ps[0] == 0x0E)
                    // ToString(string format): the runtime ABI now takes a
                    // trailing IFormatProvider slot; the IL call has none, so
                    // append the nullptr literal (the double/decimal
                    // appendLiterals precedent). nullptr selects the host
                    // default (zh-CN) -- the saudit5 oracle.
                    return I("il2cpp::runtime::icall_datetime_to_string_format", 1, true, true, true,
                        new[] { "deref:dt", "" }, appendLiterals: new[] { "nullptr" });
                if (ps.Length == 2)
                    // ToString(string, IFormatProvider): the provider carrier
                    // PASSES THROUGH (ProviderCulture; the double/decimal
                    // cultureprobe precedent) instead of being dropped. The
                    // runtime resolves: empty name (InvariantCulture) ->
                    // invariant patterns, "zh-CN" -> zh-CN, nullptr -> host
                    // default, unknown -> invariant fallback.
                    return I("il2cpp::runtime::icall_datetime_to_string_format", 2, true, true, true,
                        new[] { "deref:dt", "", "drop" }, providerCulture: true);
                if (ps.Length == 1)
                    // ToString(IFormatProvider): provider-only. The runtime
                    // ABI needs the provider in the TRAILING slot with a null
                    // format; appendLiterals can only append AFTER the
                    // argument list, so the emitter would place the provider
                    // in the format slot. LOUD until an emitter reorder exists.
                    throw new NotSupportedException("DateTime.ToString(IFormatProvider) not wired: the provider-only overload needs the provider in the trailing icall slot with a null format; use ToString()/ToString(format)/ToString(format, provider)");
                throw new NotSupportedException($"DateTime.ToString with {ps.Length} arg(s) not wired in M5");
            // P6: DateTime internal methods (stubs).
            case "GetSystemTimeAsFileTime": return I("il2cpp::runtime::icall_datetime_get_system_time_as_file_time", 0, true, false, false, null, "l");
            case "ValidateSystemTime": return I("il2cpp::runtime::icall_datetime_validate_system_time", 1, true, false, false, new[] { "l" });
            case "GetSystemTimeWithLeapSecondsHandling": return I("il2cpp::runtime::icall_datetime_get_system_time_with_leap_seconds_handling", 0, true, false, false, null, "l");
            case "SystemFileTimeToSystemTime": return I("il2cpp::runtime::icall_datetime_system_file_time_to_system_time", 1, true, false, false, new[] { "l" }, "l");
            case "SystemTimeToSystemFileTime": return I("il2cpp::runtime::icall_datetime_system_time_to_system_file_time", 1, true, false, false, new[] { "l" }, "l");
            default: return null;
        }
    }
}
