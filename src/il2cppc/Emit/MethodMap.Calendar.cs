namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Calendar -- System.Globalization.GregorianCalendar icall mappings
// (il2cpp_calendar.h, implementation calendar.cpp).
// Owners: System.Globalization.GregorianCalendar
//
// OBJECT MODEL: a GregorianCalendar is a STATELESS managed object (no
// fields) allocated through the GLOBAL class descriptor
// System_Globalization_GregorianCalendar_class -- the transpiler's
// BclAllocExpr fallback emits il2cpp_object_new(&System_Globalization_
// GregorianCalendar_class) for `new GregorianCalendar()` (the System.Version
// contract: TypeCppName sanitizes "System.Globalization.GregorianCalendar"
// -> System_Globalization_GregorianCalendar, no explicit BclAllocExpr entry
// needed).
//
// Every icall takes the managed receiver (Il2CppObject* self) as its first
// parameter -- the receiver passes through the "" passthrough cast (the
// object IS the pointer). The int-based queries (IsLeapYear / GetDaysInMonth
// / GetDaysInYear) take plain int32 args ("i" cast). The DateTime-based
// queries (GetYear / GetMonth / GetDayOfMonth / GetDayOfYear / GetDayOfWeek)
// take Il2CppDateTime BY VALUE (one register on x64 -- the managed
// value-type ABI, see il2cpp_time.h): a DateTime ARGUMENT is a value-typed
// local passed through the "" cast (the value expression IS the argument --
// the DateTime.Subtract precedent in MethodMap.DateTime.cs, where the
// non-receiver DateTime operand uses "").
//
// net48 API note: the Calendar base class exposes ONLY the DateTime-based
// GetMonth/GetDayOfYear/GetDayOfMonth (no int-based overloads exist in
// .NET Framework 4.8). The runtime's int-based civil-date icalls
// (icall_calendar_get_month(year, dayOfYear) etc.) are internal helpers
// covered by test_calendar.cpp, not reachable from transpiled code.
//
// Known-unwireable members: the impl-tz2 audit found ZERO remaining
// NotSupportedException throw sites in this file -- the header's old
// "ToDateTime / GetLeapMonth / AddMonths / AddYears / GetEra / GetWeekOfYear
// throw LOUD" note is obsolete: ToDateTime(year..ms) is wired (the
// icall_calendar_to_datetime arm below) and the calendar arithmetic/era
// surface (GetLeapMonth / AddMonths / AddYears / GetEra / GetWeekOfYear)
// returns null -> the generated-name fallback (the standard unwired
// behavior, the TimeZone.cs:87 convention) -- no silent-emit regression
// because those members cannot reach a mapped symbol.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCalendar(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // .ctor(): the calendar is stateless -- the parameterless ctor is
            // a pure no-op (il2cpp_object_ctor_nop, the Object::.ctor
            // precedent in MethodMap.Object.cs). The descriptor's zero-init
            // is the whole construction.
            case ".ctor":
                if (count == 0) return I("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, true);
                return null;
            // bool IsLeapYear(int year): 1/0 int32 ABI.
            case "IsLeapYear":
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_calendar_is_leap_year", 1, true, false, true, new[] { "", "i" });
                return null;
            // int GetDaysInMonth(int year, int month): 28..31.
            case "GetDaysInMonth":
                if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_calendar_get_days_in_month", 2, true, false, true, new[] { "", "i", "i" });
                return null;
            // int GetDaysInYear(int year): 365/366.
            case "GetDaysInYear":
                if (ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::icall_calendar_get_days_in_year", 1, true, false, true, new[] { "", "i" });
                return null;
            // int GetMonth(DateTime).
            case "GetMonth":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_calendar_get_month_of", 1, true, false, true, new[] { "", "" });
                return null;
            // int GetDayOfYear(DateTime).
            case "GetDayOfYear":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_calendar_get_day_of_year_of", 1, true, false, true, new[] { "", "" });
                return null;
            // int GetDayOfMonth(DateTime).
            case "GetDayOfMonth":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_calendar_get_day_of_month_of", 1, true, false, true, new[] { "", "" });
                return null;
            // int GetYear(DateTime).
            case "GetYear":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_calendar_get_year", 1, true, false, true, new[] { "", "" });
                return null;
            // DayOfWeek GetDayOfWeek(DateTime): int32 encoding, Sunday = 0.
            case "GetDayOfWeek":
                if (ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::icall_calendar_get_day_of_week", 1, true, false, true, new[] { "", "" });
                return null;
            case "ToDateTime":
                // Calendar.ToDateTime(year, month, day, hour, minute, second,
                // millisecond): the 7-int civil-date composition. The
                // GregorianCalendar is stateless, so the receiver is ignored;
                // the component validation + tick composition live in the
                // runtime (icall_calendar_to_datetime). Other arities have no
                // net48 overload -> stay null (generated-name path).
                if (ps.Length == 7 && ps[0] == 0x08 && ps[1] == 0x08 && ps[2] == 0x08
                    && ps[3] == 0x08 && ps[4] == 0x08 && ps[5] == 0x08 && ps[6] == 0x08)
                    return I("il2cpp::runtime::icall_calendar_to_datetime", 7, true, false, true,
                        new[] { "", "i", "i", "i", "i", "i", "i", "i" }, "dt");
                return null;
            default: return null;
        }
    }
}
