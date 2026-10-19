namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Dtoffset -- System.DateTimeOffset icall mappings
// (il2cpp_dtoffset.h, implementation dtoffset.cpp).
// Owners: System.DateTimeOffset
//
// VALUE-TYPE MODEL: a DateTimeOffset is a 16-byte value (Il2CppDateTimeOffset
// -- utcTicks + offsetMinutes, see il2cpp_dtoffset.h). Like DateTime/TimeSpan,
// the icalls take Il2CppDateTimeOffset BY VALUE (16 bytes = two registers on
// x64). Receivers arrive as ldloca ADDRESSES of a value-typed local and use
// the "deref:dto" cast (dereference the address to the struct VALUE -- the
// DateTime "deref:dt" precedent); results are carried in "dto"-kind temps
// (the CodeGenerator "dto" temp declaration + TypeCppName entry +
// result-kind propagation).
//
// The DateTime/TimeSpan ARGUMENTS (ctor operands, CompareTo/Equals operands)
// are value-typed locals passed through the "" cast (the value expression IS
// the argument -- the DateTime.Subtract precedent).
//
// RESULT-SLOT CONTRACT (the decimal ctor precedent): the value-type ctors
// take the RESULT SLOT as their FIRST parameter -- the newobj fold's vctor
// path prepends the slot (write-through, NO ResultIsReceiver), so
// icall_dtoffset_ctor(out, local, offset) / icall_dtoffset_ctor_ymd(out, ...).
//
// Known-unwireable members throw NotSupportedException (LOUD):
//   * Equals(object) / CompareTo(object) -- a boxed DateTimeOffset has no
//     runtime box class, so the boxed value cannot reach the icall.
//   * ToLocalTime / ToUniversalTime / Add* / Subtract -- the DateTimeOffset
//     arithmetic surface is out of scope for this domain.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDtoffset(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string? paramTypes = null)
    {
        switch (name)
        {
            // DateTimeOffset.UtcNow: static, offset ZERO (net48 oracle
            // dtaudit4: "00:00:00"). Mirrors the DateTime.get_UtcNow mapping
            // shape with result kind "dto".
            case "get_UtcNow":
                return I("il2cpp::runtime::icall_dtoffset_utc_now", 0, true, false, false, null, "dto");
            // DateTimeOffset.Now: the local clock instant + the host offset.
            case "get_Now":
                return I("il2cpp::runtime::icall_dtoffset_now", 0, true, false, false, null, "dto");
            // Ticks (local clock) / UtcTicks (UTC instant) -> int64.
            case "get_Ticks":
                return I("il2cpp::runtime::icall_dtoffset_get_ticks", 0, true, false, true, new[] { "deref:dto" }, "l");
            case "get_UtcTicks":
                return I("il2cpp::runtime::icall_dtoffset_get_utc_ticks", 0, true, false, true, new[] { "deref:dto" }, "l");
            case "get_DayOfWeek":
                return I("il2cpp::runtime::icall_dtoffset_get_dayofweek", 0, true, false, true, new[] { "deref:dto" });
            case "get_DayOfYear":
                return I("il2cpp::runtime::icall_dtoffset_get_dayofyear", 0, true, false, true, new[] { "deref:dto" });
            // .ctor(DateTime, TimeSpan) / .ctor(int, int, int, int, int, int,
            // TimeSpan): the result slot is prepended by the vctor path (the
            // icall writes the slot -- the decimal ctor precedent). The
            // DateTime/TimeSpan operands pass as values ("" default cast).
            case ".ctor":
                if (ps.Length == 2 && ps[0] == 0x11 && ps[1] == 0x11)
                    return I("il2cpp::runtime::icall_dtoffset_ctor", 2, false, false, true, null, "dto");
                if (ps.Length == 7 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x08 && ps[6] == 0x11)
                    return I("il2cpp::runtime::icall_dtoffset_ctor_ymd", 7, false, false, true, null, "dto");
                return null;
            // DateTime get_DateTime: the LOCAL time (UTC + offset) as a
            // DateTime value ("dt" result kind).
            case "get_DateTime":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_datetime", 0, true, false, true, new[] { "deref:dto" }, "dt");
                return null;
            // DateTime get_UtcDateTime: the UTC instant ("dt" result kind).
            case "get_UtcDateTime":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_utc_datetime", 0, true, false, true, new[] { "deref:dto" }, "dt");
                return null;
            // TimeSpan get_Offset ("ts" result kind).
            case "get_Offset":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_offset", 0, true, false, true, new[] { "deref:dto" }, "ts");
                return null;
            // Component accessors of the LOCAL time.
            case "get_Year":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_year", 0, true, false, true, new[] { "deref:dto" });
                return null;
            case "get_Month":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_month", 0, true, false, true, new[] { "deref:dto" });
                return null;
            case "get_Day":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_day", 0, true, false, true, new[] { "deref:dto" });
                return null;
            case "get_Hour":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_hour", 0, true, false, true, new[] { "deref:dto" });
                return null;
            case "get_Minute":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_minute", 0, true, false, true, new[] { "deref:dto" });
                return null;
            case "get_Second":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_get_second", 0, true, false, true, new[] { "deref:dto" });
                return null;
            // int CompareTo(DateTimeOffset): -1 / 0 / 1 on the UTC instants.
            case "CompareTo":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_dtoffset_compare", 1, true, false, true, new[] { "deref:dto", "" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("CompareTo(object) not wired: a boxed DateTimeOffset has no runtime box class, so the boxed value cannot reach the icall");
                return null;
            // bool Equals(DateTimeOffset): UTC-instant equality.
            case "Equals":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_dtoffset_equals", 1, true, false, true, new[] { "deref:dto", "" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    throw new NotSupportedException("Equals(object) not wired: a boxed DateTimeOffset has no runtime box class, so the boxed value cannot reach the icall");
                return null;
            // AddDays/AddHours/AddMinutes/AddSeconds/AddMilliseconds(double):
            // the double rides the "d" cast, the receiver is the deref:dto
            // address; the result is a fresh DateTimeOffset ("dto").
            case "AddDays":
                return I("il2cpp::runtime::icall_dtoffset_add_days", 1, true, false, true, new[] { "deref:dto", "d" }, "dto");
            // nb9 (M10): AddYears/AddMonths (LOCAL-clock semantics; the old
            // mapping had no arm, so the call became a C0 phantom symbol).
            case "AddYears":
                return I("il2cpp::runtime::icall_dtoffset_add_years", 1, true, false, true,
                    new[] { "deref:dto", "i" }, "dto");
            case "AddMonths":
                return I("il2cpp::runtime::icall_dtoffset_add_months", 1, true, false, true,
                    new[] { "deref:dto", "i" }, "dto");
            case "AddHours":
                return I("il2cpp::runtime::icall_dtoffset_add_hours", 1, true, false, true, new[] { "deref:dto", "d" }, "dto");
            case "AddMinutes":
                return I("il2cpp::runtime::icall_dtoffset_add_minutes", 1, true, false, true, new[] { "deref:dto", "d" }, "dto");
            case "AddSeconds":
                return I("il2cpp::runtime::icall_dtoffset_add_seconds", 1, true, false, true, new[] { "deref:dto", "d" }, "dto");
            case "AddMilliseconds":
                return I("il2cpp::runtime::icall_dtoffset_add_milliseconds", 1, true, false, true, new[] { "deref:dto", "d" }, "dto");
            case "AddTicks":
                return I("il2cpp::runtime::icall_dtoffset_add_ticks", 1, true, false, true, new[] { "deref:dto", "l" }, "dto");
            case "Add":
                // Add(TimeSpan) only (the DateTime operand form does not exist).
                if (count == 1 && ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_dtoffset_add", 1, true, false, true, new[] { "deref:dto", "" }, "dto");
                return null;
            case "Subtract":
                // Subtract(TimeSpan) -> DateTimeOffset; Subtract(DateTimeOffset)
                // -> TimeSpan. Both operands are valuetypes (0x11), so the
                // parameter-type text disambiguates.
                if (count != 1) return null;
                if ((paramTypes ?? arg0Type ?? "").Contains("System.TimeSpan", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_dtoffset_subtract_ts", 1, true, false, true, new[] { "deref:dto", "" }, "dto");
                if ((paramTypes ?? arg0Type ?? "").Contains("System.DateTimeOffset", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_dtoffset_subtract_dto", 1, true, false, true, new[] { "deref:dto", "" }, "ts");
                return null;
            case "ToUnixTimeSeconds":
                return I("il2cpp::runtime::icall_dtoffset_to_unix_seconds", 0, true, false, true, new[] { "deref:dto" }, "l");
            case "ToUnixTimeMilliseconds":
                return I("il2cpp::runtime::icall_dtoffset_to_unix_milliseconds", 0, true, false, true, new[] { "deref:dto" }, "l");
            case "ToOffset":
                return I("il2cpp::runtime::icall_dtoffset_to_offset", 1, true, false, true, new[] { "deref:dto", "" }, "dto");
            // The relational operators compare the UTC instants.
            case "op_Addition":
                return I("il2cpp::runtime::icall_dtoffset_add", 2, true, false, false, new[] { "", "" }, "dto");
            case "op_Subtraction":
                // (DateTimeOffset, TimeSpan) -> DateTimeOffset;
                // (DateTimeOffset, DateTimeOffset) -> TimeSpan.
                if ((paramTypes ?? arg1Type ?? "").Contains("System.TimeSpan", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_dtoffset_subtract_ts", 2, true, false, false, new[] { "", "" }, "dto");
                if ((paramTypes ?? arg1Type ?? "").Contains("System.DateTimeOffset", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_dtoffset_subtract_dto", 2, true, false, false, new[] { "", "" }, "ts");
                return null;
            case "op_Equality":
                return I("il2cpp::runtime::icall_dtoffset_op_equality", 2, true, false, false, new[] { "", "" });
            case "op_Inequality":
                return I("il2cpp::runtime::icall_dtoffset_op_inequality", 2, true, false, false, new[] { "", "" });
            case "op_LessThan":
                return I("il2cpp::runtime::icall_dtoffset_op_less_than", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThan":
                return I("il2cpp::runtime::icall_dtoffset_op_greater_than", 2, true, false, false, new[] { "", "" });
            case "op_LessThanOrEqual":
                return I("il2cpp::runtime::icall_dtoffset_op_less_or_equal", 2, true, false, false, new[] { "", "" });
            case "op_GreaterThanOrEqual":
                return I("il2cpp::runtime::icall_dtoffset_op_greater_or_equal", 2, true, false, false, new[] { "", "" });
            // string ToString(): the parameterless overload keeps its own
            // runtime entry (culture "G" pattern + " zzz", delegating to the
            // format icall); ToString(string) / ToString(string,
            // IFormatProvider) reuse the shared DateTime formatting engine
            // (il2cpp_time_fmt.h) with the DateTimeOffset offset semantics.
            case "ToString":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_dtoffset_to_string", 0, true, true, true, new[] { "deref:dto" });
                if (ps.Length == 1 && ps[0] == 0x0E)
                    // ToString(string format): the runtime ABI takes a
                    // trailing IFormatProvider slot; the IL call has none, so
                    // append the nullptr literal (host default zh-CN) -- the
                    // DateTime appendLiterals precedent.
                    return I("il2cpp::runtime::icall_dtoffset_to_string_format", 1, true, true, true,
                        new[] { "deref:dto", "" }, appendLiterals: new[] { "nullptr" });
                if (ps.Length == 2)
                    // ToString(string, IFormatProvider): the provider carrier
                    // PASSES THROUGH (ProviderCulture; the DateTime
                    // cultureprobe precedent) instead of being dropped. The
                    // runtime resolves: empty name (InvariantCulture) ->
                    // invariant patterns, "zh-CN" -> zh-CN, nullptr -> host
                    // default, unknown -> invariant fallback.
                    return I("il2cpp::runtime::icall_dtoffset_to_string_format", 2, true, true, true,
                        new[] { "deref:dto", "", "drop" }, providerCulture: true);
                if (ps.Length == 1 && ps[0] == 0x12)
                    // ToString(IFormatProvider): provider-only (the single
                    // arg is a CLASS -- the interface marker). The runtime
                    // ABI takes (dto, format, provider); the ArgOrder
                    // [0,-1,1] places the provider in the TRAILING slot with
                    // a null-format literal (-1), so the provider never lands
                    // in the format slot. B4 (dtoprobe).
                    return I("il2cpp::runtime::icall_dtoffset_to_string_format", 1, true, true, true,
                        new[] { "deref:dto", "", "drop" }, providerCulture: true, argOrder: new[] { 0, -1, 1 });
                return null;
            default: return null;
        }
    }
}
