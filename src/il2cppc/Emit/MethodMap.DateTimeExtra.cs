namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.DateTimeExtra -- System.DateTime remainder wiring: loud rejection
// for the known-but-unwireable surface (M5 convention: every wiring gap must
// be LOUD and self-identifying -- see BclResolve's NotSupportedException
// wrap). Tried LAST in the dispatch chain (DateTime -> TimeExt2 -> Extra),
// so it only sees members that missed both the MethodMap.DateTime.cs icalls
// and the timeext2.h surface.
//
// The runtime implements: construction (from_ymd/from_ymdhms), Now/UtcNow,
// Year..Second/DayOfWeek accessors, AddDays/Hours/Minutes, AddTicks/
// SubtractTicks, Subtract(DateTime), Compare/CompareTo, ToString(0/1/2),
// ParseExact(string, string), ToFileTime(Utc)/FromFileTime(Utc), GetHashCode.
// EVERY other common DateTime member has no icall -- a silent fallback
// would emit a C++ call to a symbol that does not exist (late link error);
// the loud throw names the gap instead.
//
// Owners: System.DateTime (TimeSpan: nothing -- MethodMap.TimeSpan.cs already
// rejects its own gaps: ToString / total-* accessors).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDateTimeExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.DateTime") return null;
        switch (name)
        {
            case "get_Ticks":
            case "get_Date":
            case "get_TimeOfDay":
            case "get_Millisecond":
            case "get_DayOfYear":
            case "get_Kind":
            case "get_Today":
            case "AddSeconds":
            case "AddMilliseconds":
            case "AddMonths":
            case "AddYears":
            case "Add":
            case "op_Addition":
            case "Parse":
            case "TryParse":
            case "DaysInMonth":
            case "IsLeapYear":
            case "SpecifyKind":
            case "ToOADate":
            case "FromOADate":
            case "ToBinary":
            case "FromBinary":
            case "IsDaylightSavingTime":
            case "GetTypeCode":
            case "GetDateTimeFormats":
            case "Equals":
            case "op_Equality":
            case "op_Inequality":
            case "op_LessThan":
            case "op_GreaterThan":
            case "op_LessThanOrEqual":
            case "op_GreaterThanOrEqual":
            case "ToShortDateString":
            case "ToLongDateString":
            case "ToShortTimeString":
            case "ToLongTimeString":
            case "ToLocalTime":
            case "ToUniversalTime":
                throw new NotSupportedException($"DateTime.{name} not wired: no runtime icall (the wired surface is construction, Now/UtcNow, Year..Second/DayOfWeek, AddDays/Hours/Minutes, AddTicks, Subtract, Compare/CompareTo, ToString, ParseExact, ToFileTime(Utc)/FromFileTime(Utc), GetHashCode)");
            default: return null;
        }
    }
}
