namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.DateTimeExtra -- System.DateTime remainder wiring: the dispatch
// chain's LAST arm for the owner (DateTime -> TimeExt2 -> Extra), so it only
// sees members that missed the MethodMap.DateTime.cs icalls and the
// timeext2.h surface (impl-tz2 governance: every member here is either
// WIRED to a runtime icall or carries the self-identifying whitelist message
// "LOUD whitelist (reason) [Reference Source file:line]" for one of the 4
// sanctioned categories -- machine state/culture, dynamic dispatch, no
// runtime descriptor, net48 missing overload/arity).
//
// Wired here (compile-time analyzable, runtime icall added where the entry
// did not exist):
//   * Add(TimeSpan) / op_Addition(DateTime, TimeSpan)
//     -> il2cpp_datetime_add_ticks with the "ts_ticks" cast: net48
//     Add(TimeSpan) IS AddTicks(value._ticks) (datetime.cs:456-461), the
//     Subtract(TimeSpan) "ts_ticks" precedent -- no new runtime entry.
//   * TryParse(string, out DateTime) / TryParse(string, IFormatProvider,
//     DateTimeStyles, out DateTime) -> il2cpp_datetime_try_parse[_styles]
//     (the no-throw twin of il2cpp_datetime_parse; the provider is dropped,
//     the styles flags ride the invariant subset -- datetime.cs:1466-1472).
//   * get_Today -> icall_datetime_today (net48 Today == Now.Date,
//     datetime.cs:1262-1268; Kind=Local preserved).
//   * ToOADate / FromOADate -> icall_datetime_to_oa_date / _from_oa_date
//     (net48 TicksToOADate / DoubleDateToTicks, datetime.cs:1346-1361/779).
//   * ToBinary / FromBinary -> icall_datetime_to_binary / _from_binary
//     (net48 datetime.cs:808-831 / 700-742: the Local flavor stores the UTC
//     ticks under the Local flag bits, the machine per-date offset decides).
//   * IsDaylightSavingTime() -> icall_datetime_is_daylight_saving (the
//     Kind=Utc input is false, otherwise the per-date machine probe --
//     datetime.cs:797-802 + timezone.h).
//   * GetTypeCode() -> icall_datetime_get_type_code (TypeCode.DateTime == 16,
//     referencesource mscorlib/system/typecode.cs:44).
//
// LOUD whitelist rejections:
//   * culture-dependent members (GetDateTimeFormats / the ToShort/Long
//     String/TimeString family): the pattern set comes from the machine's
//     current culture, which the invariant-culture runtime does not model
//     (whitelist 1 -- machine state/culture).
//   * Parse shapes that missed TimeExt2 are non-net48 arities (whitelist 4).
//
// Members wired upstream (MethodMap.DateTime.cs / MethodMap.TimeExt2.cs)
// never reach this switch; the previous dead case list (get_Ticks, Equals,
// the op_* family, ...) was removed with the impl-tz2 governance pass --
// a name that misses EVERY arm returns null -> the generated-name fallback
// (the standard unwired behavior, the TimeZone.cs:87 convention).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapDateTimeExtra(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner != "System.DateTime") return null;
        switch (name)
        {
            // DateTime.ToLocalTime() / ToUniversalTime() -- the KIND-AWARE
            // instance converters (net48 semantics): ToLocalTime is the
            // identity on a Kind=Local input and converts otherwise; the
            // runtime's il2cpp_datetime_to_local_kind / _from_local_kind
            // encode that (the same offset entries TimeZoneInfo's converters
            // use, plus the Kind gate). The receiver is the DateTime VALUE
            // (deref:dt); the result is a DateTime ("dt" kind, ReturnTypeName
            // for chained Year/Month/... resolution). net48 on the same host
            // is byte-exact (the single-zone doctrine).
            case "ToLocalTime":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::il2cpp_datetime_to_local_kind", 0, true, false, true,
                        new[] { "deref:dt" }, "dt", returnTypeName: "System.DateTime");
                return null;
            case "ToUniversalTime":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::il2cpp_datetime_from_local_kind", 0, true, false, true,
                        new[] { "deref:dt" }, "dt", returnTypeName: "System.DateTime");
                return null;

            // --- Add(TimeSpan) / op_Addition(DateTime, TimeSpan) ---
            // net48 Add(TimeSpan) delegates to AddTicks(value._ticks)
            // (datetime.cs:456-461), so the existing il2cpp_datetime_add_ticks
            // entry serves both shapes with the "ts_ticks" cast that lifts the
            // TimeSpan's raw tick count (the Subtract(TimeSpan) precedent).
            case "Add":
                if (count == 1 && ps.Length == 1 && ps[0] == 0x11)
                    return I("il2cpp::runtime::il2cpp_datetime_add_ticks", 1, true, false, true,
                        new[] { "deref:dt", "ts_ticks" }, "dt", returnTypeName: "System.DateTime");
                return null;
            case "op_Addition":
                if (count == 2 && ps.Length == 2)
                    return I("il2cpp::runtime::il2cpp_datetime_add_ticks", 2, true, false, false,
                        new[] { "", "ts_ticks" }, "dt", returnTypeName: "System.DateTime");
                return null;

            // --- TryParse: the no-throw twin of il2cpp_datetime_parse ---
            // TryParse(string, out DateTime) (datetime.cs:1466-1468): the out
            // slot is the managed local's ADDRESS ("" passthrough, the
            // TryParseExact out-slot precedent); a null/unparseable input
            // returns false leaving the slot untouched.
            case "TryParse":
                if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x11)
                    return I("il2cpp::runtime::il2cpp_datetime_try_parse", 2, true, false, false,
                        new[] { "s", "" });
                // TryParse(string, IFormatProvider, DateTimeStyles, out
                // DateTime) (datetime.cs:1470-1473): the provider (0x12 CLASS)
                // is dropped (invariant-culture runtime); the styles enum
                // (0x11 VALUETYPE) rides the "i" cast.
                if (count == 4 && ps.Length == 4 && ps[0] == 0x0E && ps[1] == 0x12 && ps[2] == 0x11 && ps[3] == 0x11)
                    return I("il2cpp::runtime::il2cpp_datetime_try_parse_styles", 4, true, false, false,
                        new[] { "s", "drop", "i", "" });
                return null;

            // --- Today: net48 Today == Now.Date (datetime.cs:1262-1268) ---
            case "get_Today":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_today", 0, true, false, false, null, "dt",
                        returnTypeName: "System.DateTime");
                return null;

            // --- ToOADate / FromOADate (net48 datetime.cs:1346-1361 / 779) ---
            case "ToOADate":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_to_oa_date", 0, true, false, true,
                        new[] { "deref:dt" }, "d");
                return null;
            case "FromOADate":
                if (ps.Length == 1 && ps[0] == 0x0D)
                    return I("il2cpp::runtime::icall_datetime_from_oa_date", 1, true, false, false,
                        new[] { "d" }, "dt");
                return null;

            // --- ToBinary / FromBinary (net48 datetime.cs:808-831 / 700-742) ---
            case "ToBinary":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_to_binary", 0, true, false, true,
                        new[] { "deref:dt" }, "l");
                return null;
            case "FromBinary":
                if (ps.Length == 1 && ps[0] == 0x0A)
                    return I("il2cpp::runtime::icall_datetime_from_binary", 1, true, false, false,
                        new[] { "l" }, "dt");
                return null;

            // --- IsDaylightSavingTime() instance probe ---
            // net48 DateTime.IsDaylightSavingTime() (datetime.cs:797-802): a
            // Kind=Utc input is false; anything else evaluates the per-date
            // machine DST window (timezone.h get_is_daylight_saving_at).
            case "IsDaylightSavingTime":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_is_daylight_saving", 0, true, false, true,
                        new[] { "deref:dt" });
                return null;

            // --- GetTypeCode(): TypeCode.DateTime == 16 (typecode.cs:44) ---
            case "GetTypeCode":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_datetime_get_type_code", 0, true, false, true,
                        new[] { "deref:dt" });
                return null;

            // --- LOUD whitelist 1 (culture) ---
            // The pattern enumeration / short-long layouts read the machine's
            // CURRENT culture, which the invariant-culture runtime does not
            // model (net48: DateTimeFormatInfo.CurrentInfo drives every one
            // of these; datetime.cs:1422-1440/1535-1573).
            case "GetDateTimeFormats":
            case "ToShortDateString":
            case "ToLongDateString":
            case "ToShortTimeString":
            case "ToLongTimeString":
                throw new NotSupportedException(
                    $"DateTime.{name} not wired: LOUD whitelist (culture: the pattern set comes from the machine's current culture, which the invariant-culture runtime does not model) [Reference Source mscorlib/system/datetime.cs:1422-1440,1535]");

            // --- LOUD whitelist 4 (net48 arity) ---
            // Parse shapes that missed TimeExt2 (the (string), (string,
            // IFormatProvider) and (string, IFormatProvider, DateTimeStyles)
            // forms are all wired there) cannot come from net48.
            case "Parse":
                throw new NotSupportedException(
                    $"DateTime.Parse with {ps.Length} arg(s) not wired: LOUD whitelist (net48 arity: the net48 overloads are exactly (string), (string, IFormatProvider) and (string, IFormatProvider, DateTimeStyles), all wired in MethodMap.TimeExt2.cs) [Reference Source mscorlib/system/datetime.cs:1293,1301,1305]");

            default: return null;
        }
    }
}
