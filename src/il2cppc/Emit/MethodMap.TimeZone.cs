namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.TimeZone -- System.TimeZoneInfo / System.TimeZone (net48) wiring
// (timezone.h: the TimeZone simplification domain).
// Owners: System.TimeZoneInfo, System.TimeZone
//
// The runtime surface (timezone.h -- FULLY green, translator side only) is a
// SIMPLIFICATION: ONE current-instant offset lookup -- no per-date DST table,
// no zone registry, no names:
//   il2cpp_timezone_get_utc_offset()          int64 SECONDS (local - UTC)
//   il2cpp_datetime_to_local(Il2CppDateTime)  utc ticks + offset (clamped)
//   il2cpp_datetime_from_local(Il2CppDateTime) local ticks - offset (clamped)
//   il2cpp_timezone_get_is_daylight_saving()  int32 0/1 at the current instant
//
// OBJECT MODEL: the carrier runtime has no TimeZoneInfo OBJECT, so the
// singletons fold to null (::null:: -- the Encoding singleton precedent,
// StackFold.cs:1573) with a pseudo-type receiver tag (NullExpr.TypeName)
// that routes the instance members:
//   TimeZoneInfo.get_Local    -> null + "System.TimeZoneInfo+Local" tag
//   TimeZoneInfo.get_Utc      -> null + "System.TimeZoneInfo+Utc" tag
//   TimeZone.get_CurrentTimeZone -> null + "System.TimeZoneInfo+Local" tag
//     (net48 CurrentTimeZone IS TimeZoneInfo.Local)
//
// get_UtcOffset(DateTime) -> TimeSpan:
//   Local tag -> icall_timespan_from_ticks(il2cpp_timezone_get_utc_offset()
//                * 10000000LL) -- the SECONDS -> TICKS conversion (1 s =
//                10^7 ticks, il2cpp_time.h) carried by the "tzsec" receiver
//                cast (EmitIcallCallArgs, the "enum:l"/"tostring:sb" wrap
//                precedent): the folded-null receiver expression is IGNORED
//                and the offset call + scale is emitted as the argument.
//   Utc tag   -> icall_timespan_from_ticks(0) (PrependZeroArg -- the
//                Hashtable ctor precedent) -- TimeZoneInfo.Utc has a ZERO
//                offset, never the machine offset.
//   The DateTime argument is DROPPED: the domain evaluates the offset at the
//   CURRENT instant for every input (the documented timezone.h simplification
//   -- GetUtcOffset(dt) is not DST-aware per-dt).
//
// FIRST-RESOLUTION SHAPE (receiver deferral -- the Encoding pattern): the
// DateTime overloads of TimeZoneInfo ARE VIRTUAL (GetUtcOffset(DateTime) /
// the TimeZone converters -- net48), so csc emits `callvirt` (timezonecase
// IL empirical: 0x0028 callvirt GetUtcOffset). The fold's first resolution
// (receiverType == null) DEFERS -- returns null -- to the callvirt-path
// re-resolve (StackFold.cs:1355, the receiver-hint re-resolve with
// resolved == null), which dispatches the Local-vs-Utc tag carried by the
// folded-null receiver's NullExpr.TypeName (StackFold.cs:1283). A receiver
// whose type is NOT tracked (TimeZoneInfo.Utc stored in a plain variable)
// keeps the Local mapping -- documented divergence, same untracked-receiver
// limitation as Encoding.
//
// ToLocalTime / ToUniversalTime (TimeZoneInfo non-virtual `call`; TimeZone
// virtual `callvirt` -- same icalls, no receiver dispatch):
//   -> il2cpp_datetime_to_local / il2cpp_datetime_from_local ("dt" result --
//   ReturnTypeName "System.DateTime" so chained .Year etc. resolve). The
//   simplification applies the current offset in both directions;
//   TimeZoneInfo.Utc.ToUniversalTime (an identity in .NET) diverges by the
//   offset -- documented simplification, out of the probe surface.
// IsDaylightSavingTime(DateTime) -> il2cpp_timezone_get_is_daylight_saving
//   (0/1 at the CURRENT instant; the per-instant simplification of the .NET
//   per-dt answer -- the DateTimeOffset overload shares the 1-arg shape and
//   cannot be distinguished by the raw signature bytes; unreachable in the
//   wired surface because no DateTimeOffset value can be constructed).
//
// LOUD rejections (net48 members with no runtime icall -- the
// EnvironmentExtra gate precedent): the name/registry/zone-table surface
// (get_Id/get_DisplayName/get_StandardName/get_DaylightName/get_BaseUtcOffset/
// get_SupportsDaylightSavingTime/FindSystemTimeZoneById/GetSystemTimeZones/
// CreateCustomTimeZone/ClearCachedData/ConvertTime/ConvertTimeBySystemTimeZoneId/
// GetAmbiguousTimeOffsets/IsInvalidTime; TimeZone's GetDaylightChanges).
// Members NOT listed here return null -> the generated-name fallback
// (standard unwired behavior).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapTimeZone(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // Receiver tags carried by the folded-null singletons (see the header):
        // TimeZoneInfo.Local / TimeZone.CurrentTimeZone behave as the LOCAL
        // zone; TimeZoneInfo.Utc has a ZERO offset.
        const string localTag = "System.TimeZoneInfo+Local";
        const string utcTag = "System.TimeZoneInfo+Utc";

        switch (name)
        {
            // --- singletons: fold to null with the receiver tag ---
            case "get_Local":
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false,
                    ReturnTypeName: localTag);
            case "get_Utc":
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false,
                    ReturnTypeName: utcTag);
            case "get_CurrentTimeZone":
                // net48 TimeZone.CurrentTimeZone == TimeZoneInfo.Local.
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false,
                    ReturnTypeName: localTag);

            // --- get_UtcOffset(DateTime) -> TimeSpan ---
            case "get_UtcOffset":
            case "GetUtcOffset":
                // The net48 1-arg DateTime overload is the only reachable form
                // (the DateTimeOffset overload shares the 0x11 arity-1 shape;
                // no DateTimeOffset value can be constructed in the wired
                // surface -- see the header).
                if (ps.Length != 1)
                    throw new NotSupportedException($"{(name == "get_UtcOffset" ? "TimeZoneInfo" : "TimeZone")}.{name} with {ps.Length} arg(s) not wired: the runtime has only the current-instant offset icall");
                // receiverType == null is the fold's FIRST resolution (the
                // callvirt receiver carries the zone tag -- the members are
                // VIRTUAL, see the header): DEFER to the receiver-aware
                // re-resolve (the Encoding deferral precedent -- a null
                // here routes through StackFold.cs:1355 with the folded
                // null's tag).
                if (receiverType == null) return null;
                // Receiver dispatch: the Utc singleton has a ZERO offset;
                // Local (and the untracked default) reads the machine offset.
                // The receiver EXPRESSION is ignored in both shapes (the
                // folded null has no identity -- "tzsec" ignores it, the
                // PrependZeroArg path drops it).
                return receiverType == utcTag
                    ? I("il2cpp::runtime::icall_timespan_from_ticks", 1, true, false, true,
                        new[] { "drop", "drop" }, "ts", prependZero: true, returnTypeName: "System.TimeSpan")
                    : I("il2cpp::runtime::icall_timespan_from_ticks", 1, true, false, true,
                        new[] { "tzsec", "drop" }, "ts", returnTypeName: "System.TimeSpan");

            // --- the DateTime converters (current-offset application) ---
            case "ToLocalTime":
                // TimeZoneInfo.ToLocalTime(DateTime) -> DateTime (utc + offset).
                // The receiver is dropped (the tag never reached an icall);
                // the DateTime arg passes by value ("dt" result kind).
                if (ps.Length != 1)
                    throw new NotSupportedException($"ToLocalTime with {ps.Length} arg(s) not wired: the runtime has only the (DateTime) converter");
                return I("il2cpp::runtime::il2cpp_datetime_to_local", 1, true, false, true,
                    new[] { "drop", "" }, "dt", returnTypeName: "System.DateTime");
            case "ToUniversalTime":
                if (ps.Length != 1)
                    throw new NotSupportedException($"ToUniversalTime with {ps.Length} arg(s) not wired: the runtime has only the (DateTime) converter");
                return I("il2cpp::runtime::il2cpp_datetime_from_local", 1, true, false, true,
                    new[] { "drop", "" }, "dt", returnTypeName: "System.DateTime");
            case "IsDaylightSavingTime":
                // 0/1 at the CURRENT instant (per-instant simplification of
                // the .NET per-dt answer -- timezone.h contract).
                if (ps.Length != 1)
                    throw new NotSupportedException($"IsDaylightSavingTime with {ps.Length} arg(s) not wired: the runtime has only the current-instant probe");
                return I("il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving", 1, true, false, true,
                    new[] { "drop", "drop" });

            // --- tzimpl: minimal zone registry + per-date conversion ---
            case "FindSystemTimeZoneById":
                // Static: TimeZoneInfo FindSystemTimeZoneById(string id).
                // timezone_registry.cpp resolves the closed set (Eastern/
                // Pacific/Central Standard Time + UTC) to a STABLE handle;
                // unknown ids throw NotSupportedException carrying the id.
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_timezone_find_by_id", 1, true, true, false,
                        new[] { "s" }, returnTypeName: "System.TimeZoneInfo");
                throw new NotSupportedException(
                    $"FindSystemTimeZoneById with {count} arg(s) not wired: the runtime registry takes a single zone id string");
            case "ConvertTimeFromUtc":
                // Static: DateTime ConvertTimeFromUtc(DateTime, TimeZoneInfo).
                // The per-date offset comes from the zone handle (US 2007+
                // DST rules); a pre-2007 instant throws the LOUD boundary.
                if (count == 2)
                    return I("il2cpp::runtime::il2cpp_timezone_convert_from_utc", 2, true, false, false,
                        new[] { "", "" }, "dt", returnTypeName: "System.DateTime");
                throw new NotSupportedException(
                    $"ConvertTimeFromUtc with {count} arg(s) not wired: the runtime takes (DateTime, TimeZoneInfo)");

            // --- LOUD rejections: net48 members with no runtime icall ---
            // (the name/registry/zone-table surface -- timezone.h ships only
            // the offset/conversion/daylight icalls).
            case "get_Id":
            case "get_DisplayName":
            case "get_StandardName":
            case "get_DaylightName":
            case "get_BaseUtcOffset":
            case "get_SupportsDaylightSavingTime":
            case "GetSystemTimeZones":
            case "CreateCustomTimeZone":
            case "ClearCachedData":
            case "ConvertTime":
            case "ConvertTimeBySystemTimeZoneId":
            case "GetAmbiguousTimeOffsets":
            case "IsInvalidTime":
            case "GetDaylightChanges":
                throw new NotSupportedException(
                    $"{owner}.{name} not wired: the runtime timezone.h surface has no per-zone registry, names, or zone-table icalls (only the current-instant offset/conversion/daylight probes)");
            default: return null;
        }
    }
}
