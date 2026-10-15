namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.AppContext -- System.AppContext + System.Runtime.Versioning.Compati
// bilitySwitch icall mappings (appcontext.h).
//
// Owners:
//   * System.AppContext                           -- GetData / SetSwitch /
//     TryGetSwitch / get_BaseDirectory / get_TargetFrameworkName. All net48
//     methods are MANAGED (they read a no-config-empty data/switch store), so
//     the runtime icalls reproduce the default-process observable surface
//     byte-exactly (probe p-appcontext): GetData always null, switches are an
//     in-process store, BaseDirectory a non-empty host string, TargetFramework-
//     Name the fixed net48 host string ".NETFramework,Version=v4.8".
//   * System.Runtime.Versioning.CompatibilitySwitch -- the audited realGap row
//     (icall-inventory-net48.md row 780): GetAppContextOverridesInternalCall
//     (nocfg -> null) plus the private GetValueInternalCall (null) /
//     IsEnabledInternalCall (false). The public wraps IsEnabled/GetValue are
//     managed mscorlib methods -- they are NOT mapped here (their bodies live
//     in the transpiled program only when user code calls them; the internal
//     calls they delegate to cover the audit surface).
//
// Argument/null contracts follow the net48 measured behavior (p-appcontext
// records tsg.null.threw / ss.null.threw / ss.empty.threw): SetSwitch/Try-
// GetSwitch throw ArgumentNullException("switchName") on null and Argument-
// Exception on the empty string; GetData throws ArgumentNullException("name")
// on null. Delegate to the runtime which performs the throws.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapAppContext(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        if (owner == "System.AppContext")
        {
            switch (name)
            {
                // object GetData(string) -- no-config store is empty: always
                // null. String arg (0x0E) passes as the managed ref slot.
                case "GetData":
                    if (count == 1 && ps.Length == 1 && ps[0] == 0x0E)
                        return I("il2cpp::runtime::icall_appcontext_get_data", 1, true, true, false);
                    return null;
                // void SetSwitch(string, bool) -- bool arg rides an int32 slot.
                case "SetSwitch":
                    if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                        return I("il2cpp::runtime::icall_appcontext_set_switch", 2, false, false, false,
                            new[] { "", "b" });
                    return null;
                // bool TryGetSwitch(string, out bool) -- the out param is a
                // BYREF (0x10) that consumes its bool element; the ldloca
                // LocAddrExpr address passes through the "" passthrough slot
                // (the exception-message / dictsi out-slot precedent). The
                // runtime writes the int32 out slot and returns found.
                case "TryGetSwitch":
                    if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x02)
                        return I("il2cpp::runtime::icall_appcontext_try_get_switch", 2, true, false, false,
                            new[] { "", "" });
                    return null;
                case "get_BaseDirectory":
                    if (count == 0)
                        return I("il2cpp::runtime::icall_appcontext_get_base_directory", 0, true, true, false);
                    return null;
                case "get_TargetFrameworkName":
                    if (count == 0)
                        return I("il2cpp::runtime::icall_appcontext_get_target_framework_name", 0, true, true, false);
                    return null;
                // DefineSwitchDefault/DefineSwitchOverride (internal, not part
                // of the probe surface) -- fall through to the generated-name
                // path only if user code reaches them (unknown on the byte-exact
                // probe); leave them unmapped.
                default:
                    return null;
            }
        }

        if (owner == "System.Runtime.Versioning.CompatibilitySwitch")
        {
            switch (name)
            {
                case "GetAppContextOverridesInternalCall":
                    return I("il2cpp::runtime::icall_compatibility_switch_get_app_context_overrides", 0, true, true, false);
                case "GetValueInternalCall":
                    if (count == 2) return I("il2cpp::runtime::icall_compatibility_switch_get_value", 2, true, true, false);
                    return null;
                case "IsEnabledInternalCall":
                    if (count == 2) return I("il2cpp::runtime::icall_compatibility_switch_is_enabled", 2, true, false, false);
                    return null;
                default:
                    return null;
            }
        }

        return null;
    }
}