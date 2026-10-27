namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.TimeZone -- System.TimeZoneInfo / System.TimeZone (net48) wiring
// (timezone.h: the TimeZone simplification domain).
// Owners: System.TimeZoneInfo, System.TimeZone
//
// The runtime surface (timezone.h -- the TimeZone simplification domain):
// the per-date family (il2cpp_timezone_get_utc_offset_at / _legacy /
// _is_daylight_saving_at / _is_invalid_time) evaluates the machine zone's
// Windows-registry DST rules (net48's data source) at the DateTime argument,
// so the instance DateTime APIs are per-date byte-exact with net48 -- on a
// no-DST host (UTC+8) they collapse to a constant offset. The current-
// instant family (il2cpp_timezone_get_utc_offset() /
// il2cpp_datetime_to_local / il2cpp_datetime_from_local /
// il2cpp_timezone_get_is_daylight_saving) backs DateTime.Now/ToLocalTime and
// is one current-instant offset lookup -- no per-date DST table, no zone
// registry, no names:
//   il2cpp_timezone_get_utc_offset()          int64 SECONDS (local - UTC) at now
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
//   Local tag -> icall_timespan_from_ticks(il2cpp_timezone_get_utc_offset_at(
//                dt) * 10000000LL) -- the PER-DATE offset in SECONDS -> TICKS
//                conversion (1 s = 10^7 ticks, il2cpp_time.h) carried by the
//                "tzsec_at" receiver cast (EmitIcallCallArgs, the "enum:l"/
//                "tostring:sb" wrap precedent): the folded-null receiver
//                expression is IGNORED and the per-date offset call + scale
//                is emitted as the (single) argument, evaluated at the
//                DateTime MEMBER argument (net48 GetUtcOffset semantics --
//                the zone's per-year DST rule, registry-modeled).
//   Utc tag   -> icall_timespan_from_ticks(0) (PrependZeroArg -- the
//                Hashtable ctor precedent) -- TimeZoneInfo.Utc has a ZERO
//                offset, never the machine offset.
//   owner "System.TimeZone" (the obsolete TimeZone API, folded to the Local
//                tag) -> icall_timespan_from_ticks(il2cpp_timezone_get_utc_
//                offset_legacy(dt) * 10^7) via the "tzsec_legacy" cast: the
//                net48 TimeZoneInfoWrapper is KIND-aware -- a Kind=Utc input
//                returns TimeSpan.Zero, otherwise the per-date offset
//                (probe-pinned: legacy.utckind.min net48=0 vs 480).
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
// IsDaylightSavingTime(DateTime) -> il2cpp_timezone_get_is_daylight_saving_at
//   (per-date 0/1 at the DateTime argument, net48 semantics -- the
//   DateTimeOffset overload shares the 1-arg shape and cannot be
//   distinguished by the raw signature bytes; unreachable in the wired
//   surface because no DateTimeOffset value can be constructed).
// IsInvalidTime(DateTime) -> il2cpp_timezone_get_is_invalid_time (per-date
//   1 when the wall clock falls in the spring-forward gap; a no-DST host is
//   a constant false).
//
// LOUD rejections (net48 members with no runtime icall -- the
// EnvironmentExtra gate precedent): the name/registry/zone-table surface
// (get_Id/get_DisplayName/get_StandardName/get_DaylightName/get_BaseUtcOffset/
// get_SupportsDaylightSavingTime/GetSystemTimeZones/CreateCustomTimeZone/
// ClearCachedData/ConvertTime/ConvertTimeBySystemTimeZoneId/
// GetAmbiguousTimeOffsets; TimeZone's GetDaylightChanges).
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
                // surface -- see the header). impl-tz2: net48 GetUtcOffset is
                // 1-arg on BOTH owners (timezoneinfo.cs:428/433,
                // timezone.cs:90) -- any other arity cannot come from net48.
                if (ps.Length != 1)
                    throw new NotSupportedException($"{(name == "get_UtcOffset" ? "TimeZoneInfo" : "TimeZone")}.{name} with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: the net48 overloads are the 1-arg DateTime/DateTimeOffset forms) [Reference Source mscorlib/system/timezoneinfo.cs:428,433; mscorlib/system/timezone.cs:90]");
                // receiverType == null is the fold's FIRST resolution (the
                // callvirt receiver carries the zone tag -- the members are
                // VIRTUAL, see the header): DEFER to the receiver-aware
                // re-resolve (the Encoding deferral precedent -- a null
                // here routes through StackFold.cs:1355 with the folded
                // null's tag).
                if (receiverType == null) return null;
                // Receiver dispatch: the Utc singleton has a ZERO offset;
                // Local (and the untracked default) evaluates PER-DATE at the
                // DateTime argument (net48 GetUtcOffset semantics -- audit
                // GN-20). The receiver EXPRESSION is ignored in every shape
                // (the folded null has no identity -- "tzsec_at"/
                // "tzsec_legacy" substitute the argument evaluation, the
                // PrependZeroArg path drops it).
                if (receiverType == utcTag)
                    return I("il2cpp::runtime::icall_timespan_from_ticks", 1, true, false, true,
                        new[] { "drop", "drop" }, "ts", prependZero: true, returnTypeName: "System.TimeSpan");
                // The obsolete System.TimeZone.GetUtcOffset (owner
                // "System.TimeZone") is the net48 TimeZoneInfoWrapper: KIND-
                // aware -- a Kind==Utc input returns TimeSpan.Zero, otherwise
                // the per-date offset (the "tzsec_legacy" runtime arm, probe-
                // pinned legacy.utckind: net48=0 vs the current-instant 480).
                // The modern System.TimeZoneInfo.GetUtcOffset (owner
                // "System.TimeZoneInfo") is per-date regardless of Kind.
                return owner == "System.TimeZone"
                    ? I("il2cpp::runtime::icall_timespan_from_ticks", 1, true, false, true,
                        new[] { "tzsec_legacy", "drop" }, "ts", returnTypeName: "System.TimeSpan")
                    : I("il2cpp::runtime::icall_timespan_from_ticks", 1, true, false, true,
                        new[] { "tzsec_at", "drop" }, "ts", returnTypeName: "System.TimeSpan");

            // --- the DateTime converters (current-offset application) ---
            case "ToLocalTime":
                // TimeZoneInfo.ToLocalTime(DateTime) -> DateTime (utc + offset).
                // The receiver is dropped (the tag never reached an icall);
                // the DateTime arg passes by value ("dt" result kind).
                // impl-tz2: net48 has NO TimeZoneInfo.ToLocalTime member at
                // all -- the only net48 ToLocalTime is the obsolete
                // TimeZone.ToLocalTime(DateTime) (timezone.cs:112), 1-arg, so
                // any non-1-arg shape cannot come from net48.
                if (ps.Length != 1)
                    throw new NotSupportedException($"ToLocalTime with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: net48 has no TimeZoneInfo.ToLocalTime member and TimeZone.ToLocalTime is the 1-arg (DateTime) form only) [Reference Source mscorlib/system/timezone.cs:112]");
                return I("il2cpp::runtime::il2cpp_datetime_to_local", 1, true, false, true,
                    new[] { "drop", "" }, "dt", returnTypeName: "System.DateTime");
            case "ToUniversalTime":
                // impl-tz2: same evidence as ToLocalTime -- the only net48
                // ToUniversalTime is TimeZone.ToUniversalTime(DateTime)
                // (timezone.cs:95), 1-arg.
                if (ps.Length != 1)
                    throw new NotSupportedException($"ToUniversalTime with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: net48 has no TimeZoneInfo.ToUniversalTime member and TimeZone.ToUniversalTime is the 1-arg (DateTime) form only) [Reference Source mscorlib/system/timezone.cs:95]");
                return I("il2cpp::runtime::il2cpp_datetime_from_local", 1, true, false, true,
                    new[] { "drop", "" }, "dt", returnTypeName: "System.DateTime");
            case "IsDaylightSavingTime":
                // net48 IsDaylightSavingTime(DateTime) per-date 0/1: the DST
                // model evaluates the transition window at the DateTime's
                // date (timezone.h per-date family). A no-DST host is a
                // constant false -- byte-exact with net48.
                if (ps.Length != 1)
                {
                    // impl-tz2: the obsolete TimeZone.IsDaylightSavingTime(
                    // DateTime, DaylightTime) 2-arg form (timezone.cs:132)
                    // needs a DaylightTime VALUE, which GetDaylightChanges
                    // would produce -- but the DaylightTime object has no
                    // runtime box class/descriptor, so the value cannot exist
                    // in the wired surface. Every other non-1-arg shape is
                    // not a net48 arity (TimeZoneInfo exposes only the 1-arg
                    // DateTime/DateTimeOffset forms, timezoneinfo.cs:542/549).
                    if (owner == "System.TimeZone" && ps.Length == 2)
                        throw new NotSupportedException($"IsDaylightSavingTime with 2 arg(s) not wired: LOUD whitelist (runtime descriptor: the net48 (DateTime, DaylightTime) overload needs a DaylightTime value and the DaylightTime object has no runtime box class/descriptor) [Reference Source mscorlib/system/timezone.cs:124,132]");
                    throw new NotSupportedException($"IsDaylightSavingTime with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: TimeZoneInfo exposes only the 1-arg DateTime/DateTimeOffset forms; the TimeZone 2-arg DaylightTime form has no runtime descriptor) [Reference Source mscorlib/system/timezoneinfo.cs:542,549]");
                }
                return I("il2cpp::runtime::il2cpp_timezone_get_is_daylight_saving_at", 1, true, false, true,
                    new[] { "drop", "" });
            case "IsInvalidTime":
                // net48 IsInvalidTime(DateTime): 1 when the wall clock falls
                // in the non-existent spring-forward hour (the per-date DST
                // model); a no-DST host is a constant false.
                // impl-tz2: the only net48 form is the 1-arg (DateTime)
                // (timezoneinfo.cs:620) -- any other arity is not net48.
                if (ps.Length != 1)
                    throw new NotSupportedException($"IsInvalidTime with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: the only net48 overload is IsInvalidTime(DateTime)) [Reference Source mscorlib/system/timezoneinfo.cs:620]");
                return I("il2cpp::runtime::il2cpp_timezone_get_is_invalid_time", 1, true, false, true,
                    new[] { "drop", "" });

            // --- tzimpl: minimal zone registry + per-date conversion ---
            case "FindSystemTimeZoneById":
                // Static: TimeZoneInfo FindSystemTimeZoneById(string id).
                // timezone_registry.cpp resolves the closed set (Eastern/
                // Pacific/Central Standard Time + UTC) to a STABLE handle;
                // unknown ids throw NotSupportedException carrying the id.
                // impl-tz2: the only net48 form is (string) (timezoneinfo.cs:1944).
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_timezone_find_by_id", 1, true, true, false,
                        new[] { "s" }, returnTypeName: "System.TimeZoneInfo");
                throw new NotSupportedException(
                    $"FindSystemTimeZoneById with {count} arg(s) not wired: LOUD whitelist (net48 arity: the only net48 overload is FindSystemTimeZoneById(string)) [Reference Source mscorlib/system/timezoneinfo.cs:1944]");
            case "ConvertTimeFromUtc":
                // Static: DateTime ConvertTimeFromUtc(DateTime, TimeZoneInfo).
                // The per-date offset comes from the zone handle (US 2007+
                // DST rules); a pre-2007 instant throws the LOUD boundary.
                // impl-tz2: the only net48 form is (DateTime, TimeZoneInfo)
                // (timezoneinfo.cs:831) -- any other arity is not net48.
                if (count == 2)
                    return I("il2cpp::runtime::il2cpp_timezone_convert_from_utc", 2, true, false, false,
                        new[] { "", "" }, "dt", returnTypeName: "System.DateTime");
                throw new NotSupportedException(
                    $"ConvertTimeFromUtc with {count} arg(s) not wired: LOUD whitelist (net48 arity: the only net48 overload is ConvertTimeFromUtc(DateTime, TimeZoneInfo)) [Reference Source mscorlib/system/timezoneinfo.cs:831]");

            // --- LOUD whitelist rejections: net48 members with no runtime
            // icall (the name/registry/zone-table surface -- timezone.h ships
            // only the offset/conversion/daylight icalls). impl-tz2: the
            // members split on the sanctioned category -- the registry
            // NAME/state family reads the MACHINE's zone registry (whitelist
            // 1, machine state); the members that need a TimeZoneInfo /
            // DaylightTime OBJECT have no runtime box class/descriptor for
            // it (whitelist 3).
            case "get_Id":                       // timezoneinfo.cs:279
            case "get_DisplayName":              // timezoneinfo.cs:285
            case "get_StandardName":             // timezoneinfo.cs:291
            case "get_DaylightName":             // timezoneinfo.cs:297
            case "get_BaseUtcOffset":            // timezoneinfo.cs:303
            case "get_SupportsDaylightSavingTime": // timezoneinfo.cs:309
            case "GetSystemTimeZones":           // timezoneinfo.cs:923
            case "ClearCachedData":              // timezoneinfo.cs:647
            case "ConvertTimeBySystemTimeZoneId": // timezoneinfo.cs:658,665,671
            case "GetAmbiguousTimeOffsets":      // timezoneinfo.cs:339,376
                throw new NotSupportedException(
                    $"{owner}.{name} not wired: LOUD whitelist (machine state: the value is the machine zone registry's identity/base-bias/zone table, which the single-zone runtime does not model) [Reference Source mscorlib/system/timezoneinfo.cs:279-309,647,658-691,923,339-376]");
            case "CreateCustomTimeZone":         // timezoneinfo.cs:1144,1166,1194
            case "ConvertTime":                  // timezoneinfo.cs:703,727,746
            case "GetDaylightChanges":           // timezone.cs:124
                throw new NotSupportedException(
                    $"{owner}.{name} not wired: LOUD whitelist (runtime descriptor: the TimeZoneInfo/DaylightTime object has no runtime box class/descriptor, so a zone object cannot exist in the wired surface) [Reference Source mscorlib/system/timezoneinfo.cs:1144,703,727,746; mscorlib/system/timezone.cs:124]");
            default: return null;
        }
    }
}
