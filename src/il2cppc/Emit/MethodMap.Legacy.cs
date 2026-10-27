namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Legacy -- LOUD rejections for the net48 audit section 3/section 6
// legacy families (icall-inventory-net48.md rows 786-824 / 980: "legacy
// technology, suggested LOUD reject" / "low-frequency/legacy").
//
//   * System.Runtime.Remoting.* -- the 6 public-face RemotingServices gaps
//     (CallDefaultCtor / AllocateInitializedObject / nSetRemoteActivation-
//     Configured / Unwrap / AlwaysUnwrap / CheckCast + the RealProxy/Context/
//     Message/StackBuilderSink icalls). The runtime has NO remoting
//     infrastructure (no transparent proxies, no ObjRef, no channels, no
//     context-bound objects), so EVERY member of the family is rejected as a
//     family -- a member-level list would just misname the same wall.
//   * System.AppDomain -- the runtime has no AppDomain object; the AOT
//     process IS the default domain (CreateDomain/Unload/evidence/probing
//     are all meaningless here).
//   * System.ArgIterator -- varargs reflection (__arglist) is not in the M3
//     model.
//   * System.Threading.ReaderWriterLock -- legacy low-frequency lock with no
//     runtime icall (Monitor / the Interlocked atomics cover the need).
//
// The BclResolve wrapper prefixes every message with "{owner}::{name}: " so
// the rejection self-identifies. Without these cases the members silently
// fell to the generated-name path (undefined C++ symbol at link).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapLegacy(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Runtime.Remoting.RemotingServices":
            case "System.Runtime.Remoting.Proxies.RealProxy":
            case "System.Runtime.Remoting.Contexts.Context":
            case "System.Runtime.Remoting.Messaging.Message":
            case "System.Runtime.Remoting.Messaging.StackBuilderSink":
            case "System.Runtime.Remoting.Channels.ChannelServices":
                // impl-tz2: whitelist 3 -- the runtime has no remoting object
                // model, so no member of the family can receive or return a
                // value (no transparent proxies / ObjRef / channels /
                // context-bound objects exist as runtime descriptors).
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit section 3 (6 public-face gaps, 'suggested LOUD reject'): LOUD whitelist (runtime descriptor: the runtime has no remoting object model -- no transparent proxies / ObjRef / channels / context-bound objects, so the member's values cannot exist in the wired surface) [Reference Source mscorlib/system/runtime/remoting/remotingservices.cs:46]");
            case "System.AppDomain":
                // adm1 (AppDomain minimal object closure): the public default-
                // domain surface wires to the appdomain.cpp object model (the
                // singleton, the process-exe-derived getters, the process-level
                // data table, the registered-type CreateInstance arms, the
                // constants and the UnhandledException event chain). The
                // private InternalCall arms below stay wired to the existing
                // single-domain stubs (stubs.cpp -- the AOT process IS the
                // default domain, so the answers are the documented constants).
                // Everything else (CAS objects / loader / culture-ToString)
                // stays LOUD.
                switch (name)
                {
                    case "get_CurrentDomain":
                        return I("il2cpp::runtime::icall_appdomain_get_current_domain", 0, true, true, false);
                    // GetType on an AppDomain-typed receiver: Roslyn emits
                    // callvirt against the STATIC type (the base-chain walk
                    // never reaches System.Object because this family THROWS
                    // for unknown names), so route the Object-domain icall
                    // explicitly (the p-adb gettype leg).
                    case "GetType":
                        return I("il2cpp::runtime::icall_object_get_type", 0, true, true, true);
                    case "get_Id":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_id_public", 0, true, false, true);
                        return null;
                    case "get_FriendlyName":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_friendly_name", 0, true, true, true);
                        return null;
                    case "get_BaseDirectory":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_base_directory", 0, true, true, true);
                        return null;
                    case "get_RelativeSearchPath":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_relative_search_path", 0, true, true, true);
                        return null;
                    case "get_DomainManager":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_domain_manager", 0, true, true, true);
                        return null;
                    case "get_SetupInformation":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_setup_information", 0, true, true, true);
                        return null;
                    case "GetData":
                        if (count == 1 && ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::icall_appdomain_get_data", 1, true, true, true);
                        return null;
                    case "SetData":
                        if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x1C)
                            return I("il2cpp::runtime::icall_appdomain_set_data", 2, false, false, true);
                        return null;
                    case "CreateInstance":
                        if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::icall_appdomain_create_instance", 2, true, true, true);
                        return null;
                    case "CreateInstanceAndUnwrap":
                        if (count == 2 && ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                            return I("il2cpp::runtime::icall_appdomain_create_instance_and_unwrap", 2, true, true, true);
                        return null;
                    case "get_IsFullyTrusted":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_is_fully_trusted", 0, true, false, true);
                        return null;
                    case "get_IsHomogenous":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_is_homogenous", 0, true, false, true);
                        return null;
                    case "IsDefaultAppDomain":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_is_default_app_domain", 0, true, false, true);
                        return null;
                    case "add_UnhandledException":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_appdomain_add_unhandled_exception", 1, false, false, true);
                        return null;
                    case "remove_UnhandledException":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_appdomain_remove_unhandled_exception", 1, false, false, true);
                        return null;
                    // adm1 LOUD runtime strains (single-source difftest mapping,
                    // appdomain.cpp): no assembly loader / CAS object model, so
                    // the runtime THROWS NotSupportedException (the probe pins
                    // the loud throw). ExecuteAssembly is byte-exact on the
                    // throws-any catch (net48 also throws for a missing file).
                    case "ExecuteAssembly":
                        if (count == 1 && ps.Length == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::icall_appdomain_execute_assembly", 1, true, false, true);
                        return null;
                    case "get_PermissionSet":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_permission_set", 0, true, true, true);
                        return null;
                    case "get_Evidence":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_evidence", 0, true, true, true);
                        return null;
                    case "get_ApplicationTrust":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_application_trust", 0, true, true, true);
                        return null;
                    case "GetAssemblies":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_get_assemblies", 0, true, true, true);
                        return null;
                    case "ToString":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_to_string", 0, true, true, true);
                        return null;
                    case "IsFinalizingForUnload":
                        return I("il2cpp::runtime::icall_appdomain_is_finalizing_for_unload", 0, true, false, false);
                    case "PublishAnonymouslyHostedDynamicMethodsAssembly":
                        return I("il2cpp::runtime::icall_appdomain_publish_anonymously_hosted_dynamic_methods_assembly", 0, false, false, false);
                    case "nCreateContext":
                        return I("il2cpp::runtime::icall_appdomain_n_create_context", 0, true, true, false);
                    case "IsStringInterned":
                        return I("il2cpp::runtime::icall_appdomain_is_string_interned", 1, true, true, false, new[] { "s" });
                    case "GetOrInternString":
                        return I("il2cpp::runtime::icall_appdomain_get_or_intern_string", 1, true, true, false, new[] { "s" });
                    case "GetId":
                        return I("il2cpp::runtime::icall_appdomain_get_id", 0, true, false, false);
                    case "GetDefaultDomain":
                        return I("il2cpp::runtime::icall_appdomain_get_default_domain", 0, true, true, false);
                    case "IsUnloadingForcedFinalize":
                        return I("il2cpp::runtime::icall_appdomain_is_unloading_forced_finalize", 0, true, false, false);
                    case "IsDomainIdValid":
                        return I("il2cpp::runtime::icall_appdomain_is_domain_id_valid", 1, true, false, false, new[] { "i" });
                    case "GetFusionContext":
                        return I("il2cpp::runtime::icall_appdomain_get_fusion_context", 0, true, true, false);
                    case "GetSecurityDescriptor":
                        return I("il2cpp::runtime::icall_appdomain_get_security_descriptor", 0, true, true, false);
                    case "nCreateDomain":
                        return I("il2cpp::runtime::icall_appdomain_n_create_domain", 3, true, false, false, new[] { "s", "", "" });
                    case "nCreateInstance":
                        return I("il2cpp::runtime::icall_appdomain_n_create_instance", 2, true, true, false, new[] { "i", "s" });
                    case "UpdateLoaderOptimization":
                        return I("il2cpp::runtime::icall_appdomain_update_loader_optimization", 1, false, false, false, new[] { "i" });
                    case "nUnload":
                        return I("il2cpp::runtime::icall_appdomain_n_unload", 1, false, false, false, new[] { "i" });
                }
                // impl-tz2: whitelist 3 -- the AppDomain object model covers
                // only the adm1 closure above; the remaining CAS/loader
                // members need AppDomain/domain objects the runtime does not
                // describe (CreateDomain/Unload would fabricate a domain the
                // single-domain AOT process cannot represent).
                throw new NotSupportedException(
                    $"{name} is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit section 3): LOUD whitelist (runtime descriptor: the AOT process IS the default domain and CreateDomain/Unload/evidence probing would need AppDomain objects the runtime does not model; the CAS/loader members are mapped to a runtime NotSupportedException in the adm1 closure) [Reference Source mscorlib/system/appdomain.cs:2737,2238]");
            case "System.AppDomainSetup":
                // adm1: the SetupInformation carrier's probed getters derive
                // from the process exe path (ApplicationBase == AppDomain.
                // BaseDirectory, ConfigurationFile == exeDir+exeName+".Config").
                switch (name)
                {
                    case "get_ApplicationBase":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_setup_get_application_base", 0, true, true, true);
                        return null;
                    case "get_ConfigurationFile":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_appdomain_setup_get_configuration_file", 0, true, true, true);
                        return null;
                    case "GetType":
                        return I("il2cpp::runtime::icall_object_get_type", 0, true, true, true);
                }
                // impl-tz2: whitelist 3 -- only the probed getter arms are
                // backed by the process exe path; the remaining setup
                // properties need an AppDomainSetup object the runtime does
                // not model.
                throw new NotSupportedException(
                    $"{name} is part of System.AppDomainSetup -- only the probed ApplicationBase/ConfigurationFile rack-arms are wired in the adm1 closure: LOUD whitelist (runtime descriptor: the remaining setup properties need an AppDomainSetup object the runtime does not model) [Reference Source mscorlib/system/AppDomainSetup.cs:48,933]");
            case "System.Runtime.Remoting.ObjectHandle":
                // adm1: the CreateInstance wrapper carrier -- Unwrap returns the
                // wrapped object (the only probed member of the handle).
                if (name == "Unwrap" && count == 0)
                    return I("il2cpp::runtime::icall_objecthandle_unwrap", 0, true, true, true);
                if (name == "GetType")
                    return I("il2cpp::runtime::icall_object_get_type", 0, true, true, true);
                // impl-tz2: whitelist 3 -- the remoting carrier object has no
                // runtime model beyond the Unwrap arm.
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.Remoting.ObjectHandle -- only the CreateInstance wrapper carrier (Unwrap) is wired in the adm1 closure: LOUD whitelist (runtime descriptor: the ObjectHandle carrier object has no runtime model beyond the Unwrap arm, the rest of the remoting surface is rejected per the net48 audit section 3) [Reference Source mscorlib/system/runtime/remoting/objecthandle.cs:32,49]");
            case "System.ArgIterator":
                // impl-tz2: whitelist 3 -- varargs reflection (__arglist) has
                // no runtime support/descriptor in the M3 model.
                throw new NotSupportedException(
                    $"{name} is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit section 3): LOUD whitelist (runtime descriptor: __arglist is not in the M3 model, the ArgIterator frame has no runtime support) [Reference Source mscorlib/system/argiterator.cs:18,29]");
            case "System.Threading.ReaderWriterLock":
                // net48 closure: AnyWritersSince wires to the constant-false
                // stub (stubs.cpp); the rest of the legacy lock stays LOUD.
                if (name == "AnyWritersSince" && count == 1)
                    return I("il2cpp::runtime::icall_reader_writer_lock_any_writers_since", 1, true, false, false, new[] { "i" });
                // impl-tz2: whitelist 3 -- the legacy lock has no runtime
                // object model (the AnyWritersSince constant stub above is
                // the only wired arm); Monitor/Interlocked cover the need.
                throw new NotSupportedException(
                    $"{name} is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit section 3): LOUD whitelist (runtime descriptor: the legacy lock has no runtime object model -- use the Monitor domain (monitor.h) or the Interlocked atomics (il2cpp_interlocked.h) instead) [Reference Source mscorlib/system/threading/readerwriterlock.cs:33,126]");
            default:
                return null;
        }
    }
}
