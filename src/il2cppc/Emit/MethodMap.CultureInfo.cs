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
                return new ResolvedMethod(ResolvedMethod.NullMarker, 0, false, true, true, Array.Empty<bool>(), false);
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
                // is an identity (the getter returns the string).
                return receiverType == "System.String"
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true, Array.Empty<bool>(), true)
                    : null;
            // P6: CultureInfo internal methods (stubs).
            case "nativeGetLocaleInfoExInt": return I("il2cpp::runtime::icall_culture_info_native_get_locale_info_ex_int", 1, true, false, false, new[] { "i" });
            case "nativeSetThreadLocale": return I("il2cpp::runtime::icall_culture_info_native_set_thread_locale", 1, false, false, false, new[] { "i" });
            default: return null;
        }
    }
}
