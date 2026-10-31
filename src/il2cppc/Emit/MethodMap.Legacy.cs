namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Legacy -- LOUD rejections for the net48 audit section 3/section 6
// legacy families (icall-inventory-net48.md rows 786-824 / 980: "legacy
// technology, suggested LOUD reject" / "low-frequency/legacy").
//
//   * System.Runtime.Remoting.* -- p0c D31-D35 wires ONLY the five
//     public-face STATE ACCESSORS (RealProxy.GetProxiedType /
//     GetTransparentProxy / GetStubData / SetStubData + RemotingServices.
//     IsTransparentProxy / GetRealProxy) and the RealProxy ctors, on the
//     proxy-identity side table (il2cpp_remoting.h). There is still NO
//     remoting infrastructure (no ObjRef, no channels, no context-bound
//     objects), so every other member of the family stays LOUD.
//   * System.AppDomain -- the runtime has no AppDomain object; the AOT
//     process IS the default domain (CreateDomain/Unload/evidence/probing
//     are all meaningless here).
//   * System.ArgIterator -- varargs reflection (__arglist) is not in the M3
//     model (the `arglist` IL opcode itself is rejected in StackFold, so no
//     ArgIterator can even be constructed; see NOTES.md for the IL shape and
//     the separate varargs work item).
//   * System.Threading.ReaderWriterLock -- p0c D37 gives the legacy lock its
//     own object model (il2cpp_rwlock.h); only the LockCookie members stay
//     LOUD (no System.Threading.LockCookie value-type model).
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
            // p0c D31-D35: the FIVE public-face RealProxy/RemotingServices
            // STATE ACCESSORS are wired (il2cpp_remoting.h, remoting.cpp).
            // Measured net48 (probe-p0c): GetProxiedType answers the ctor's
            // classToProxy; GetTransparentProxy is identity-stable;
            // GetStubData answers the shared boxed IntPtr(-1) default stub
            // until SetStubData replaces it (null stub data is
            // ArgumentNullException); IsTransparentProxy is false for null and
            // for a RealProxy; GetRealProxy answers the SAME RealProxy
            // instance, and null (never a throw) for a non-proxy. net48's
            // `.ctor` is MANAGED (verified by reflection: GetTransparentProxy
            // is the only other managed member), but its body stores the
            // mscorlib-only fields, so it is replaced here by the runtime
            // side-table ctor. Everything else in the family stays LOUD.
            case "System.Runtime.Remoting.Proxies.RealProxy":
                switch (name)
                {
                    case ".ctor":
                        // protected RealProxy(Type classToProxy)
                        if (count == 1)
                            return I("il2cpp::runtime::icall_realproxy_ctor_type", 1, false, false, true,
                                new[] { "", "s" });
                        // protected RealProxy(Type, IntPtr stub, object stubData)
                        if (count == 3)
                            return I("il2cpp::runtime::icall_realproxy_ctor_full", 3, false, false, true,
                                new[] { "", "s", "n", "" });
                        return null;
                    case "GetProxiedType":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_realproxy_get_proxied_type", 0, true, true, true,
                                returnTypeName: "System.Type");
                        return null;
                    case "GetTransparentProxy":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_realproxy_get_transparent_proxy", 0, true, true, true);
                        return null;
                    case "GetStubData":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_realproxy_get_stub_data", 1, true, true, false);
                        return null;
                    case "SetStubData":
                        if (count == 2)
                            return I("il2cpp::runtime::icall_realproxy_set_stub_data", 2, false, false, false);
                        return null;
                    default:
                        break;
                }
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.Remoting -- only the five public-face RealProxy/RemotingServices state accessors (GetProxiedType / GetTransparentProxy / GetStubData / SetStubData / RemotingServices.IsTransparentProxy / GetRealProxy) and the RealProxy ctors are wired (il2cpp_remoting.h): LOUD whitelist (runtime descriptor: the rest of the family needs the remoting object model the runtime does not have -- no channels / ObjRef / context-bound objects) [Reference Source mscorlib/system/runtime/remoting/realproxy.cs:20,74]");
            case "System.Runtime.Remoting.RemotingServices":
                switch (name)
                {
                    case "IsTransparentProxy":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_remoting_services_is_transparent_proxy", 1, true, false, false);
                        return null;
                    case "GetRealProxy":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_remoting_services_get_real_proxy", 1, true, true, false);
                        return null;
                    default:
                        break;
                }
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.Remoting -- only the five public-face RealProxy/RemotingServices state accessors are wired (il2cpp_remoting.h): LOUD whitelist (runtime descriptor: the rest needs the remoting object model the runtime does not have -- no channels / ObjRef / context-bound objects) [Reference Source mscorlib/system/runtime/remoting/remotingservices.cs:46]");
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
                // varargs (__arglist): the public surface is a managed wrapper
                // over internal icalls; mscorlib is never translated, so the
                // PUBLIC members are the resolution targets (the p0c
                // ReaderWriterLock precedent). Runtime model: il2cpp_argiterator.h.
                // Both ctors/GetNextArg shapes and the boundary behaviour are
                // net48-measured (NOTES.md): GetRemainingCount decrements and
                // validates the handle cookie; GetNextArg past the end throws
                // InvalidOperationException; the RuntimeTypeHandle overload
                // consumes then requires the EXACT declared type
                // (InvalidCastException on mismatch); GetNextArgType peeks
                // without consuming.
                switch (name)
                {
                    case ".ctor":
                        // ArgIterator(RuntimeArgumentHandle): initialize the
                        // value slot from the caller-built frame handle. The
                        // newobj fold passes the slot as arg0 (vctor path) and
                        // the emitter addresses it for the icall.
                        if (count == 1)
                            return I("il2cpp::runtime::icall_argiterator_ctor", 1, false, false, true);
                        // ArgIterator(RuntimeArgumentHandle, void*) carries an
                        // unmanaged signature pointer -- not modelled (the frame
                        // already carries the declared types).
                        throw new NotSupportedException(
                            $"{name} is part of System.ArgIterator -- only the ArgIterator(RuntimeArgumentHandle) ctor is wired (the unmanaged void* signature-pointer ctor is not modelled; the typed frame carries the declared types) [Reference Source mscorlib/system/argiterator.cs:18,44]");
                    case "GetRemainingCount":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_argiterator_get_remaining_count", 1, true, false, false,
                                new[] { "ai" }, "i");
                        break;
                    case "GetNextArg":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_argiterator_get_next_arg", 1, true, false, false,
                                new[] { "ai" }, "tr");
                        if (count == 1 && ps.Length == 1)
                            return I("il2cpp::runtime::icall_argiterator_get_next_arg_typed", 2, true, false, false,
                                new[] { "ai", "" }, "tr");
                        break;
                    case "GetNextArgType":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_argiterator_get_next_arg_type", 1, true, false, false,
                                new[] { "ai" }, "ptr");
                        break;
                    case "End":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_argiterator_end", 1, false, false, false,
                                new[] { "ai" });
                        break;
                    case "GetHashCode":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_argiterator_get_hash_code", 1, true, false, false,
                                new[] { "ai" }, "i");
                        break;
                    case "Equals":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_argiterator_equals", 2, true, false, false,
                                new[] { "ai", "" }, "i");
                        break;
                }
                // Any other member of the type stays LOUD (no silent
                // generated-name symbol).
                throw new NotSupportedException(
                    $"{name} is part of System.ArgIterator -- only the varargs-consumption surface (ctors/GetRemainingCount/GetNextArg/GetNextArgType/End/GetHashCode/Equals) is wired: LOUD whitelist for the remaining members [Reference Source mscorlib/system/argiterator.cs:18,29]");
            case "System.Threading.ReaderWriterLock":
                // p0c D37: the legacy lock now HAS an object model
                // (il2cpp_rwlock.h, rwlock.cpp). The PUBLIC members below are
                // the ones a user program calls (net48: they are managed
                // wrappers over the *Internal icalls; mscorlib is never
                // translated, so the wrapper itself is the resolution target
                // and is replaced by the runtime icall). MEASURED net48
                // (probe-p0c): WriterSeqNum starts at 1 and advances on every
                // writer acquisition; AnyWritersSince(n) is exactly
                // `WriterSeqNum > n`; a reader acquisition does not advance it.
                switch (name)
                {
                    case ".ctor":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_ctor", 0, false, false, true);
                        return null;
                    case "AcquireReaderLock":
                        if (count == 1 && ps[0] == 0x08)
                            return I("il2cpp::runtime::icall_rwl_acquire_reader", 1, false, false, true,
                                new[] { "", "i" });
                        return null;
                    case "ReleaseReaderLock":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_release_reader", 0, false, false, true);
                        return null;
                    case "AcquireWriterLock":
                        if (count == 1 && ps[0] == 0x08)
                            return I("il2cpp::runtime::icall_rwl_acquire_writer", 1, false, false, true,
                                new[] { "", "i" });
                        return null;
                    case "ReleaseWriterLock":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_release_writer", 0, false, false, true);
                        return null;
                    case "get_IsReaderLockHeld":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_get_is_reader_lock_held", 0, true, false, true);
                        return null;
                    case "get_IsWriterLockHeld":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_get_is_writer_lock_held", 0, true, false, true);
                        return null;
                    case "get_WriterSeqNum":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_rwl_get_writer_seq_num", 0, true, false, true);
                        return null;
                    case "AnyWritersSince":
                        if (count == 1 && ps[0] == 0x08)
                            return I("il2cpp::runtime::icall_rwl_any_writers_since", 1, true, false, true,
                                new[] { "", "i" });
                        return null;
                    default:
                        break;
                }
                // NOT wired: UpgradeToWriterLock / DowngradeFromWriterLock /
                // ReleaseLock / RestoreLock exchange a System.Threading.
                // LockCookie -- a BCL value type with no C++ model here (a
                // TypeEmitter concern, a different owner's file domain). They
                // stay LOUD instead of pretending.
                throw new NotSupportedException(
                    $"{name} is part of System.Threading.ReaderWriterLock -- the lock itself is wired (il2cpp_rwlock.h: ctor / AcquireLock+ReleaseLock for reader and writer / IsReaderLockHeld / IsWriterLockHeld / WriterSeqNum / AnyWritersSince): LOUD whitelist (runtime descriptor: the LockCookie members UpgradeToWriterLock / DowngradeFromWriterLock / ReleaseLock / RestoreLock need a System.Threading.LockCookie value-type model this transpiler does not have) [Reference Source mscorlib/system/threading/readerwriterlock.cs:33,126]");
            case "System.Runtime.InteropServices.ExtensibleClassFactory":
                // p0c D11: RegisterObjectCreationCallback is an InternalCall in
                // net48. MEASURED (probe-p0c): a plain caller gets
                // InvalidOperationException ("only from the class constructor
                // of a COM-creatable class"), and a null delegate is rejected
                // FIRST with ArgumentNullException. The runtime reproduces
                // exactly those two outcomes (com_factory.cpp) -- there is no
                // COM-creatable class context in this runtime, so the callback
                // can never legitimately be registered.
                if (name == "RegisterObjectCreationCallback" && count == 1)
                    return I("il2cpp::runtime::icall_extensible_class_factory_register", 1, false, false, false);
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.InteropServices.ExtensibleClassFactory -- only RegisterObjectCreationCallback is wired (com_factory.cpp): LOUD whitelist (runtime descriptor: the rest needs the COM class-factory model the runtime does not have) [Reference Source mscorlib/system/runtime/interopservices/extensibleclassfactory.cs:18]");
            case "System.Runtime.InteropServices.ObjectCreationDelegate":
                // The delegate type RegisterObjectCreationCallback takes. The
                // transpiler's BCL-delegate rewrite table does not cover it, so
                // the generic newobj+ctor shape applies: il2cpp_object_new on
                // the descriptor, then this icall initializes the object with
                // the standard (object target, IntPtr method) pair (exactly
                // what the table-covered types get from il2cpp_delegate_new).
                if (name == ".ctor" && count == 2)
                    return I("il2cpp::runtime::icall_object_creation_delegate_ctor", 2, false, false, true);
                return null;
            default:
                return null;
        }
    }
}
