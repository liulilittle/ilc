namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.CultureInfo -- System.Globalization.CultureInfo. C42 FOLD FIX:
// get_CurrentCulture now maps to the runtime icall il2cpp_culture_get_current
// (cultureinfo.cpp reads the OS user locale via GetUserDefaultLCID -> the
// culture NAME, "zh-CN" on the zh-CN verification host) instead of folding
// to the fixed "en-US" literal -- CultureInfo.CurrentCulture.Name now prints
// "zh-CN", matching the net48 oracle on this host. get_InvariantCulture still
// folds to null (no culture object model); the InvariantCulture argument of
// ToString(string, IFormatProvider) is dropped at the call site.
// Owners: System.Globalization.CultureInfo
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapCultureInfo(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case "get_InvariantCulture":
                // Handle-gap model: a CultureInfo IS its culture-name string
                // (get_CurrentCulture returns the name). The invariant culture
                // is the EMPTY name, non-null -- net48 probes assert
                // `InvariantCulture == null` False and `.Name == ""` True
                // (cultureprobe); the old NullMarker fold made both wrong.
                // Formatting providers are dropped at the call sites
                // regardless of this value.
                return I("il2cpp::runtime::il2cpp_culture_get_invariant", 0, true, true, false,
                    returnTypeName: "System.Globalization.CultureInfo");
            case "get_CurrentCulture":
                // C42 fold fix: the runtime reads the OS user locale name
                // (cultureinfo.cpp, GetUserDefaultLCID -> "zh-CN" on the
                // verification host) -- net48-aligned, host-aware. The result
                // is the culture NAME string, so the get_Name identity fold
                // (below) keeps working on it.
                return I("il2cpp::runtime::il2cpp_culture_get_current", 0, true, true, false,
                    returnTypeName: "System.String");
            case "get_Name":
                // CultureInfo.CurrentCulture.Name on the culture-name string
                // is an identity (the getter returns the string). The
                // invariant carrier is likewise the invariant name string, so
                // a CultureInfo-hinted receiver is the same identity (the
                // empty name returns "").
                return receiverType is "System.String" or "System.Globalization.CultureInfo"
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true)
                    : null;
            // nb2 CULT-GET: CultureInfo.GetCultureInfo(string) -- handle-gap:
            // a CultureInfo IS its culture-name string (the get_Name identity
            // above operates on it). The runtime icall normalizes the name
            // (canonical case for a known culture, verbatim echo for a
            // well-formed unknown whose primary is a known language; null for
            // an unknown primary, M3 -- net48 throws CultureNotFoundException).
            // ReturnTypeName stamps the receiver so the chained get_Name /
            // get_TwoLetterISOLanguageName resolve on the tracked type.
            case "GetCultureInfo":
                // (string name): the canonical named lookup (nb2 CULT-GET).
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_culture_get_culture_info", 1, true, true, false,
                        returnTypeName: "System.Globalization.CultureInfo");
                // (int culture): the net48 LCID overload
                // (globalization_cultureinfo.cs:1887) -- the runtime validates a
                // positive culture and resolves it through its fixed LCID table.
                if (count == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::il2cpp_culture_get_culture_info_by_lcid", 1, true, true, false,
                        returnTypeName: "System.Globalization.CultureInfo");
                // (string name, string altName): the net48 alias overload
                // (globalization_cultureinfo.cs:1932).
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_culture_get_culture_info_alt", 2, true, true, false,
                        returnTypeName: "System.Globalization.CultureInfo");
                throw new NotSupportedException($"CultureInfo.GetCultureInfo with {count} param(s) not wired (expected (string), (int culture) or (string, string))");
            case "get_TwoLetterISOLanguageName":
                // nb2 CULT-GET: the culture-name carrier's primary segment,
                // mapped through the ISO 639-1 code table ("en-US" -> "en";
                // net48 pin: "no" -> "nb", "iw" -> "he", ...). Applies to any
                // CultureInfo/current-culture carrier (the receiver is the
                // name string; the icall is receiver-first).
                return receiverType is "System.Globalization.CultureInfo" or "System.String"
                    ? I("il2cpp::runtime::il2cpp_culture_get_two_letter_from_name", 0, true, true, true,
                        returnTypeName: "System.String")
                    : null;
            case "get_DisplayName":
                // nb2 CULT-GET: no culture display-name table on the runtime
                // surface (net48 returns the localized display name, e.g.
                // "英语(美国)" on zh-CN). Explicit LOUD instead of a silent
                // phantom / fold mis-resolution.
                throw new NotSupportedException("CultureInfo.DisplayName is not wired: the runtime has no culture display-name table");
            // P6: CultureInfo internal methods (stubs).
            case "nativeGetLocaleInfoEx": return I("il2cpp::runtime::icall_culture_info_native_get_locale_info_ex_int", 1, true, true, false, new[] { "i" });
            case "nativeGetLocaleInfoExInt": return I("il2cpp::runtime::icall_culture_info_native_get_locale_info_ex_int", 1, true, false, false, new[] { "i" });
            case "nativeSetThreadLocale": return I("il2cpp::runtime::icall_culture_info_native_set_thread_locale", 1, false, false, false, new[] { "i" });
            default: return null;
        }
    }
}
