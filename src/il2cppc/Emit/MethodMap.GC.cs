namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.GC -- System.GC icall mappings (il2cpp_gc.h).
// Owners: System.GC
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapGC(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // stacktrace-real: System.Diagnostics.StackTrace / StackFrame now have a
        // REAL runtime object model (il2cpp_stacktrace.h + stacktrace.cpp), so
        // their managed surface resolves here instead of folding as a
        // "not wired in M5" gap. Guarded by owner at the TOP so the generic
        // names (ToString/GetFrameCount/GetFileName/...) can never capture
        // another owner's member that happens to share the name.
        if (owner is "System.Diagnostics.StackTrace" or "System.Diagnostics.StackFrame")
            return TryMapStackTraceDiagnostics(owner, name, ps, count);
        switch (name)
        {
            case "Collect":
                if (count == 0) return I("il2cpp::runtime::icall_gc_collect", 0, false, false, false);
                // CG6 fix (gcops, 2026-08-09): the int32 generation argument
                // is DROPPED (the runtime has only the parameterless
                // full-collection icall) but ParamCount MUST be 1 -- the fold
                // pops ParamCount stack slots at the call, so 0 left the IL
                // argument stranded on the shadow stack (cg6 stack-desync
                // attribution). The "drop" cast skips it at emission.
                if (count == 1) return I("il2cpp::runtime::icall_gc_collect", 1, false, false, false, new[] { "drop" });
                // Collect(int generation, GCCollectionMode mode) -- batch
                // 150c: both arguments are dropped (the runtime has only the
                // parameterless full-collection icall; generation/mode are
                // ignored -- documented deviation). The fold still pops all
                // ParamCount slots.
                if (count == 2) return I("il2cpp::runtime::icall_gc_collect", 2, false, false, false, new[] { "drop", "drop" });
                // nb1 GC-LEAK: Collect(int generation, GCCollectionMode mode,
                // bool blocking) -- net48 3-arg form. Same drop-arm (the
                // runtime still has only the parameterless full-collection
                // icall; the bool blocking flag is ignored -- the runtime's
                // collection is always synchronous/blocking, so dropping it
                // is observable-exact). netcore's 4-arg (..., bool,
                // bool compacting) rides the same drop shape.
                if (count == 3) return I("il2cpp::runtime::icall_gc_collect", 3, false, false, false, new[] { "drop", "drop", "drop" });
                if (count == 4) return I("il2cpp::runtime::icall_gc_collect", 4, false, false, false, new[] { "drop", "drop", "drop", "drop" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): GC.Collect arity {count} -- net48 has only Collect(), " +
                    "Collect(int), Collect(int, GCCollectionMode) and Collect(int, GCCollectionMode, bool) " +
                    "[Reference Source gc.cs:200,193,206,212]");
            // nb1 GC-LEAK: AddMemoryPressure(long) / RemoveMemoryPressure(long)
            // -- net48 CLR memory-pressure counters. The simplified collector
            // has no pressure-based collection trigger, so the icalls are
            // no-ops that still validate the argument shape (a long count,
            // accepted and dropped). The net48 oracle surface: both return
            // void and never throw for a valid positive count.
            case "AddMemoryPressure":
                return I("il2cpp::runtime::icall_gc_add_memory_pressure", 1, false, false, false, new[] { "l" });
            case "RemoveMemoryPressure":
                return I("il2cpp::runtime::icall_gc_remove_memory_pressure", 1, false, false, false, new[] { "l" });
            case "GetTotalMemory":
                return I("il2cpp::runtime::icall_gc_get_total_memory", 1, true, false, false, new[] { "drop" }, "l");
            case "GetGeneration": return I("il2cpp::runtime::icall_gc_get_generation", 1, true, false, false);
            case "KeepAlive": return I("il2cpp::runtime::icall_gc_keep_alive", 1, false, false, false);
            case "SuppressFinalize": return I("il2cpp::runtime::icall_gc_suppress_finalize", 1, false, false, false);
            case "ReRegisterForFinalize": return I("il2cpp::runtime::icall_gc_re_register_for_finalize", 1, false, false, false);
            case "GetAllocatedBytes": return I("il2cpp::runtime::icall_gc_get_allocated_bytes", 0, true, false, false, null, "l");
            // P6: GC.GetTotalAllocatedBytes(bool precise): approximate total
            // allocated bytes. The 'precise' arg is dropped (the runtime's
            // counter is always approximate; no generational sub-allocator).
            case "GetTotalAllocatedBytes": return I("il2cpp::il2cpp_gc_get_total_allocated_bytes_approximate", 1, true, false, false, new[] { "drop" }, "l");
            // M7 gcext wiring (2026-08-08): GC.WaitForPendingFinalizers() -> the
            // direct finalizer trigger (il2cpp_gc_ext.h -- icall_gc_finalize_trigger
            // runs any due/pending finalizers synchronously, the gcext two-
            // collection model's synchronous counterpart to .NET's finalizer
            // thread; see the gcext.cpp docblock + test_gcext.cpp section 9). Static,
            // void, 0 params -- the same shape as the parameterless Collect.
            // The runtime surface was verified present (gcext.cpp:181) before
            // wiring -- no fabricated entry.
            case "WaitForPendingFinalizers":
                return I("il2cpp::runtime::icall_gc_finalize_trigger", 0, false, false, false);
            // GC latency-mode / LOH-compaction-mode preferences (net48 + netcore).
            // The collector is a single-threaded mark-sweep with no generational
            // tuning, so these are stored preferences echoed back by the getters
            // (see gcapi.cpp). GCLatencyMode / GCLargeObjectHeapCompactionMode are
            // int32 enums passed by value.
            case "get_LatencyMode":
            case "GetGCLatencyMode": return I("il2cpp::runtime::icall_gc_get_latency_mode", 0, true, false, false);
            case "set_LatencyMode":
            case "SetGCLatencyMode": return I("il2cpp::runtime::icall_gc_set_latency_mode", 1, false, false, false);
            case "GetLOHCompactionMode": return I("il2cpp::runtime::icall_gc_get_loh_compaction_mode", 0, true, false, false);
            case "SetLOHCompactionMode": return I("il2cpp::runtime::icall_gc_set_loh_compaction_mode", 1, false, false, false);
            case "IsServerGC":
            case "get_IsServerGC": return I("il2cpp::runtime::icall_gc_is_server_gc", 0, true, false, false);
            // P6: GCFrameRegistration stubs (no-op in simplified model).
            case "RegisterForGCReporting": return I("il2cpp::runtime::icall_gc_register_for_gc_reporting", 0, false, false, false);
            case "UnregisterForGCReporting": return I("il2cpp::runtime::icall_gc_unregister_for_gc_reporting", 0, false, false, false);
            // P6 batch: GC stats methods -- all runtime functions already exist
            // in il2cpp_gc.h / gc.cpp; just need MethodMap wiring.
            case "GetSegmentSize": return I("il2cpp::il2cpp_gc_get_segment_size", 0, true, false, false, null, "l");
            case "GetLastGCPercentTimeInGC": return I("il2cpp::il2cpp_gc_get_last_gc_percent_time_in_gc", 0, true, false, false);
            case "GetGenerationSize": return I("il2cpp::il2cpp_gc_get_generation_size", 1, true, false, false, new[] { "i" }, "l");
            // GC.CollectionCount(int generation): the per-generation counter
            // (il2cpp_gc.h GC API layer -- the il2cpp:: prefix, NOT runtime::).
            case "CollectionCount": return I("il2cpp::il2cpp_gc_get_collection_count", 1, true, false, false, new[] { "i" }, "l");
            // nb4 GC-SPOT: RegisterForFullGCNotification(int, int) /
            // CancelFullGCNotification() -- the runtime entries already exist
            // (gc.cpp icall surface; il2cpp_gc.h:292) and are BLOCKING-
            // collector no-op registrations (always succeed). net48 return:
            // bool, discarded by the callers.
            case "RegisterForFullGCNotification":
                if (count == 2)
                    return I("il2cpp::il2cpp_gc_register_for_full_gc_notification", 2, true, false, false,
                        new[] { "i", "i" });
                return null;
            case "CancelFullGCNotification":
                if (count == 0)
                    return I("il2cpp::il2cpp_gc_cancel_full_gc_notification", 0, true, false, false);
                return null;
            // net9/10 E2E icallcase namespace fix: these two live in
            // il2cpp_gc.h = namespace il2cpp (the GC API layer), NOT
            // il2cpp::runtime -- the wrong prefix failed to compile.
            // get_MaxGeneration: the netcore public PROPERTY getter name
            // (net48's GetMaxGeneration method kept as alias).
            case "get_MaxGeneration":
            case "GetMaxGeneration": return I("il2cpp::il2cpp_gc_get_max_generation", 0, true, false, false);
            case "GetAllocatedBytesForCurrentThread": return I("il2cpp::il2cpp_gc_get_allocated_bytes_for_current_thread", 0, true, false, false, null, "l");
            case "GetFragmentedBytes": return I("il2cpp::il2cpp_gc_get_fragmented_bytes", 0, true, false, false, null, "l");
            // P6 batch: remaining internal CLR helper stubs.
            case "GetCorElementTypeOfElementType": return I("il2cpp::runtime::icall_array_get_cor_element_type_of_element_type", 1, true, false, false);
            case "AllocateNewArray": return I("il2cpp::runtime::icall_gc_allocate_new_array", 2, true, true, false, new[] { "i", "i" });
            case "HasInteropInfo": return I("il2cpp::runtime::icall_com_aware_weak_reference_has_interop_info", 0, true, false, true);
            case "SaveStackTracesFromDeepCopy": return I("il2cpp::runtime::icall_exception_save_stack_traces_from_deep_copy", 0, false, false, true);
            case "GetExceptionCount": return I("il2cpp::runtime::icall_exception_get_exception_count", 0, true, false, false);
            case "GetTypeFromHandleUnsafe": return I("il2cpp::runtime::icall_type_get_type_from_handle_unsafe", 1, true, true, false, new[] { "i" });
            case "GetCompiledILBytes": return I("il2cpp::runtime::icall_jit_info_get_compiled_il_bytes", 0, true, false, false, null, "l");
            case "GetCompiledMethodCount": return I("il2cpp::runtime::icall_jit_info_get_compiled_method_count", 0, true, false, false, null, "l");
            case "GetLoadedAssemblies": return I("il2cpp::runtime::icall_assembly_load_context_get_loaded_assemblies", 0, true, true, false);
            case "IsTracingEnabled": return I("il2cpp::runtime::icall_assembly_load_context_is_tracing_enabled", 0, true, false, false);
            // GetStackFramesInternal -- net48 real ABI (verified from the
            // net48 reference-assembly metadata): static void
            // (StackFrameHelper sfh, int iSkip, bool fNeedFileInfo, Exception e).
            // Semantics (live-stack frame enumeration yielding method names,
            // absolute PDB paths, line/col) are W7 (p-stacktrace GAPS.md); the
            // icall is the empty-helper whitelist stub (stubs.cpp). "b" = the
            // bool cast code.
            case "GetStackFramesInternal":
                if (count == 4)
                    return I("il2cpp::runtime::icall_stack_trace_get_stack_frames_internal", 4, false, false, false,
                        new[] { "", "i", "b", "" });
                return null;
            // Number formatting stubs
            case "FormatDecimal": return I("il2cpp::runtime::icall_number_format_decimal", 2, true, true, false, new[] { "", "i" });
            case "FormatDouble": return I("il2cpp::runtime::icall_number_format_double", 3, true, true, false, new[] { "d", "s", "" });
            case "FormatInt32": return I("il2cpp::runtime::icall_number_format_int32", 3, true, true, false, new[] { "i", "s", "" });
            case "FormatUInt32": return I("il2cpp::runtime::icall_number_format_uint32", 3, true, true, false, new[] { "i", "s", "" });
            case "FormatInt64": return I("il2cpp::runtime::icall_number_format_int64", 3, true, true, false, new[] { "l", "s", "" });
            case "FormatUInt64": return I("il2cpp::runtime::icall_number_format_uint64", 3, true, true, false, new[] { "l", "s", "" });
            case "FormatSingle": return I("il2cpp::runtime::icall_number_format_single", 3, true, true, false, new[] { "d", "s", "" });
            case "NumberBufferToDecimal": return I("il2cpp::runtime::icall_number_number_buffer_to_decimal", 2, false, false, false, new[] { "", "" });
            case "NumberBufferToDouble": return I("il2cpp::runtime::icall_number_number_buffer_to_double", 1, true, false, false, null, "d");
            case "FormatNumberBuffer": return I("il2cpp::runtime::icall_number_format_number_buffer", 2, true, true, false, new[] { "i", "i" });
            // ParseNumbers stubs
            case "StringToLong": return I("il2cpp::runtime::icall_parse_numbers_string_to_long", 3, true, false, false, new[] { "s", "i", "i" }, "l");
            case "StringToInt": return I("il2cpp::runtime::icall_parse_numbers_string_to_int", 3, true, false, false, new[] { "s", "i", "i" });
            case "IntToString": return I("il2cpp::runtime::icall_parse_numbers_int_to_string", 3, true, true, false, new[] { "i", "i", "i" });
            case "LongToString": return I("il2cpp::runtime::icall_parse_numbers_long_to_string", 3, true, true, false, new[] { "l", "i", "i" });
            // CurrentSystemTimeZone stubs
            case "nativeGetTimeZoneMinuteOffset": return I("il2cpp::runtime::icall_current_system_timezone_native_get_time_zone_minute_offset", 0, true, false, false);
            case "nativeGetDaylightName": return I("il2cpp::runtime::icall_current_system_timezone_native_get_daylight_name", 0, true, true, false);
            case "nativeGetStandardName": return I("il2cpp::runtime::icall_current_system_timezone_native_get_standard_name", 0, true, true, false);
            case "nativeGetDaylightChanges": return I("il2cpp::runtime::icall_current_system_timezone_native_get_daylight_changes", 1, true, true, false, new[] { "i" });
            // CultureData stubs
            case "LCIDToLocaleName": return I("il2cpp::runtime::icall_culture_data_lcid_to_locale_name", 1, true, false, false, new[] { "i" });
            case "LocaleNameToLCID": return I("il2cpp::runtime::icall_culture_data_locale_name_to_lcid", 1, true, false, false, new[] { "s" });
            case "nativeInitCultureData": return I("il2cpp::runtime::icall_culture_data_native_init_culture_data", 0, false, false, false);
            case "nativeGetNumberFormatInfoValues": return I("il2cpp::runtime::icall_culture_data_native_get_number_format_info_values", 0, true, true, false);
            // CodeAccessSecurityEngine stubs
            case "SpecialDemand": return I("il2cpp::runtime::icall_code_access_security_engine_special_demand", 1, false, false, false, new[] { "i" });
            case "QuickCheckForAllDemands": return I("il2cpp::runtime::icall_code_access_security_engine_quick_check_for_all_demands", 0, true, false, false);
            case "AllDomainsHomogeneousWithNoStackModifiers": return I("il2cpp::runtime::icall_code_access_security_engine_all_domains_homogeneous_with_no_stack_modifiers", 0, true, false, false);
            case "CheckNReturnSO": return I("il2cpp::runtime::icall_code_access_security_engine_check_n_return_so", 1, true, false, false, new[] { "i" });
            // CompressedStack stubs
            case "GetDelayedCompressedStack": return I("il2cpp::runtime::icall_compressed_stack_get_delayed_compressed_stack", 0, true, true, false);
            case "DestroyDelayedCompressedStack": return I("il2cpp::runtime::icall_compressed_stack_destroy_delayed_compressed_stack", 0, false, false, false);
            case "DestroyDCSList": return I("il2cpp::runtime::icall_compressed_stack_destroy_dcs_list", 0, false, false, false);
            case "GetDCSCount": return I("il2cpp::runtime::icall_compressed_stack_get_dcs_count", 0, true, false, false);
            // DomainCompressedStack stubs
            case "CreateDomainCompressedStack": return I("il2cpp::runtime::icall_domain_compressed_stack_create_domain_compressed_stack", 0, true, false, false);
            case "GetDomainCompressedStackSize": return I("il2cpp::runtime::icall_domain_compressed_stack_get_domain_compressed_stack_size", 0, true, false, false);
            case "GetDomainCompressedStackFlags": return I("il2cpp::runtime::icall_domain_compressed_stack_get_domain_compressed_stack_flags", 0, true, false, false);
            case "GetDomainCompressedStackStackBlobs": return I("il2cpp::runtime::icall_domain_compressed_stack_get_domain_compressed_stack_stack_blobs", 0, true, true, false);
            // ThreadPool stubs
            case "get_ThreadCount": return I("il2cpp::runtime::icall_thread_pool_get_thread_count", 0, true, false, false);
            case "get_MinThreads": return I("il2cpp::runtime::icall_thread_pool_get_min_threads", 0, true, false, false);
            case "get_MaxThreads": return I("il2cpp::runtime::icall_thread_pool_get_max_threads", 0, true, false, false);
            case "GetAvailableThreads": return I("il2cpp::runtime::icall_thread_pool_get_available_threads", 0, true, false, false);
            case "NotifyWorkItemComplete": return I("il2cpp::runtime::icall_thread_pool_notify_work_item_complete", 0, false, false, false);
            case "ReportThreadStatus": return I("il2cpp::runtime::icall_thread_pool_report_thread_status", 1, false, false, false, new[] { "i" });
            case "NotifyWorkItemProgressNative": return I("il2cpp::runtime::icall_thread_pool_notify_work_item_progress_native", 0, false, false, false);
            case "IsThreadPoolHosted": return I("il2cpp::runtime::icall_thread_pool_is_thread_pool_hosted", 0, true, false, false);
            // WeakReference`1.Finalize -- the generic weak reference's implicit
            // finalizer (base Object.Finalize). Same no-op as Object::Finalize.
            case "Finalize": return I("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, true);
            // OverlappedData stubs
            case "get_PendingCount": return I("il2cpp::runtime::icall_overlapped_data_get_pending_count", 0, true, false, false);
            case "get_WaitHandle": return I("il2cpp::runtime::icall_overlapped_data_get_wait_handle", 0, true, false, false);
            case "get_ErrorCode": return I("il2cpp::runtime::icall_overlapped_data_get_error_code", 0, true, false, false);
            case "FreeNativeOverlapped": return I("il2cpp::runtime::icall_overlapped_data_free_native_overlapped", 0, true, true, false);
            case "GetOverlappedFromNative": return I("il2cpp::runtime::icall_overlapped_data_get_overlapped_from_native", 0, true, true, false);
            case "CheckVMForIOPacket": return I("il2cpp::runtime::icall_overlapped_data_check_vm_for_io_packet", 0, true, false, false);
            // Remaining X509 stubs
            case "_GetDateNotBefore": return I("il2cpp::runtime::icall_x509_utils_get_date_not_before", 0, true, true, false);
            case "_GetIssuerName": return I("il2cpp::runtime::icall_x509_utils_get_issuer_name", 0, true, true, false);
            case "_GetPublicKeyOid": return I("il2cpp::runtime::icall_x509_utils_get_public_key_oid", 0, true, true, false);
            case "_GetPublicKeyParameters": return I("il2cpp::runtime::icall_x509_utils_get_public_key_parameters", 0, true, true, false);
            case "_GetPublicKeyValue": return I("il2cpp::runtime::icall_x509_utils_get_public_key_value", 0, true, true, false);
            case "_GetSubjectInfo": return I("il2cpp::runtime::icall_x509_utils_get_subject_info", 0, true, true, false);
            case "_GetSerialNumber": return I("il2cpp::runtime::icall_x509_utils_get_serial_number", 0, true, true, false);
            case "_GetThumbprint": return I("il2cpp::runtime::icall_x509_utils_get_thumbprint", 0, true, true, false);
            case "_QueryCertBlobType": return I("il2cpp::runtime::icall_x509_utils_query_cert_blob_type", 0, true, false, false);
            case "_QueryCertFileType": return I("il2cpp::runtime::icall_x509_utils_query_cert_file_type", 1, true, false, false, new[] { "s" });
            // Remaining Environment stubs
            case "nativeGetEnvironmentVariable": return I("il2cpp::runtime::icall_environment_get_environment_variable_native", 1, true, true, false, new[] { "s" });
            case "get_Version": return I("il2cpp::runtime::icall_environment_get_version", 0, true, false, false);
            case "GetVersionEx": return I("il2cpp::runtime::icall_environment_get_version_ex", 0, true, false, false);
            // FailFast: handled by MethodMap.Environment.cs via TryMapEnvironment
            // Remaining DateTime stubs
            case "GetSystemTimeAsFileTime": return I("il2cpp::runtime::icall_datetime_get_system_time_as_file_time", 0, true, false, false, null, "l");
            case "ValidateSystemTime": return I("il2cpp::runtime::icall_datetime_validate_system_time", 1, true, false, false, new[] { "l" });
            case "GetSystemTimeWithLeapSecondsHandling": return I("il2cpp::runtime::icall_datetime_get_system_time_with_leap_seconds_handling", 0, true, false, false, null, "l");
            case "SystemFileTimeToSystemTime": return I("il2cpp::runtime::icall_datetime_system_file_time_to_system_time", 1, true, false, false, new[] { "l" }, "l");
            case "SystemTimeToSystemFileTime": return I("il2cpp::runtime::icall_datetime_system_time_to_system_file_time", 1, true, false, false, new[] { "l" }, "l");
            // Remaining String stubs
            case "nativeCompareOrdinalEx": return I("il2cpp::runtime::icall_string_native_compare_ordinal_ex", 6, true, false, false, new[] { "", "", "i", "i", "i", "i" });
            case "nativeCompareOrdinalIgnoreCaseWC": return I("il2cpp::runtime::icall_string_native_compare_ordinal_ignore_case_wc", 2, true, false, false, new[] { "", "" });
            case "InternalMarvin32HashString": return I("il2cpp::runtime::icall_string_internal_marvin32_hash_string", 1, true, false, false, new[] { "" });
            // Remaining miscellaneous stubs
            case "IsFastSort": return I("il2cpp::runtime::icall_string_is_fast_sort", 0, true, true, false);
            case "IsAscii": return I("il2cpp::runtime::icall_string_is_ascii", 0, true, false, false);
            case "InternalEqualTypes": return I("il2cpp::runtime::icall_delegate_internal_equal_types", 2, true, false, false);
            case "CompareUnmanagedFunctionPtrs": return I("il2cpp::runtime::icall_delegate_compare_unmanaged_function_ptrs", 2, true, false, false);
            case "InternalBlockCopy": return I("il2cpp::runtime::icall_buffer_internal_block_copy", 5, false, false, false, new[] { "", "i", "", "i", "i" });
            case "RunParser": return I("il2cpp::runtime::icall_base_config_handler_run_parser", 0, true, true, false);
            case "FCallToInt32": return I("il2cpp::runtime::icall_decimal_fcall_to_int32", 0, true, false, false);
            case "CanConvertPrimitiveObjectToType": return I("il2cpp::runtime::icall_default_binder_can_convert_primitive_object_to_type", 2, true, false, false, new[] { "", "i" });
            case "InternalGetUnderlyingType": return I("il2cpp::runtime::icall_enum_internal_get_underlying_type", 1, true, true, false, new[] { "" });
            case "UpdateContextProperty": return I("il2cpp::runtime::icall_app_domain_setup_update_context_property", 2, true, true, false, new[] { "", "s", "" });
            case "GetComIUnknown": return I("il2cpp::runtime::icall_marshal_by_ref_object_get_com_i_unknown", 0, true, true, false);
            case "FormatSignature": return I("il2cpp::runtime::icall_missing_member_exception_format_signature", 1, true, true, false, new[] { "s" });
            case "op_Inequality": return I("il2cpp::runtime::icall_type_op_inequality", 2, true, false, false);
            case "GetHashCodeOfPtr": return I("il2cpp::runtime::icall_value_type_get_hash_code_of_ptr", 1, true, false, false);
            case "CheckLegacyManagedDeflateStream": return I("il2cpp::runtime::icall_clr_config_check_legacy_managed_deflate_stream", 0, true, false, false);
            case "CheckThrowUnobservedTaskExceptions": return I("il2cpp::runtime::icall_clr_config_check_throw_unobserved_task_exceptions", 0, true, false, false);
            case "GetImpersonationFlowMode": return I("il2cpp::runtime::icall_security_context_get_impersonation_flow_mode", 0, true, false, false);
            case "IsDefaultThreadSecurityInfo": return I("il2cpp::runtime::icall_security_context_is_default_thread_security_info", 0, true, false, false);
            case "_SetThreadSecurity": return I("il2cpp::runtime::icall_security_manager_set_thread_security", 1, false, false, false, new[] { "i" });
            case "GetSecurityObjectForFrame": return I("il2cpp::runtime::icall_security_runtime_get_security_object_for_frame", 0, true, true, false);
            // CompressedStack/DomainCompressedStack GAP methods (dispatched to TryMapGC).
            case "IsImmediateCompletionCandidate": return I("il2cpp::runtime::icall_compressed_stack_is_immediate_completion_candidate", 0, true, false, false);
            case "GetDomainCompressedStack": return I("il2cpp::runtime::icall_compressed_stack_get_domain_compressed_stack", 0, true, true, false);
            case "GetHomogeneousPLS": return I("il2cpp::runtime::icall_compressed_stack_get_homogeneous_pls", 0, true, true, false);
            case "GetDescCount": return I("il2cpp::runtime::icall_domain_compressed_stack_get_desc_count", 0, true, false, false);
            case "GetDomainPermissionSets": return I("il2cpp::runtime::icall_domain_compressed_stack_get_domain_permission_sets", 0, true, true, false);
            case "GetDescriptorInfo": return I("il2cpp::runtime::icall_domain_compressed_stack_get_descriptor_info", 0, true, true, false);
            case "IgnoreDomain": return I("il2cpp::runtime::icall_domain_compressed_stack_ignore_domain", 1, true, false, false, new[] { "i" });
            // Remaining GAP methods (Calendar, CultureInfo, Encoding, Diagnostics)
            case "GetResourceFromDefault": return I("il2cpp::runtime::icall_environment_get_resource_from_default", 2, true, false, false, new[] { "s", "" });
            case "GetCompatibilityFlag": return I("il2cpp::runtime::icall_environment_get_compatibility_flag", 1, true, false, false, new[] { "s" });
            case "nativeGetLocaleInfoExInt": return I("il2cpp::runtime::icall_culture_info_native_get_locale_info_ex_int", 1, true, false, false, new[] { "i" });
            case "nativeSetThreadLocale": return I("il2cpp::runtime::icall_culture_info_native_set_thread_locale", 1, false, false, false, new[] { "i" });
            case "nativeCreateOpenFileMapping": return I("il2cpp::runtime::icall_encoding_table_native_create_open_file_mapping", 0, true, true, false);
            case "ShowDefaultAssertDialog": return I("il2cpp::runtime::icall_assert_show_default_assert_dialog", 0, false, false, false);
            case "AddLogSwitch": return I("il2cpp::runtime::icall_log_add_log_switch", 2, false, false, false, new[] { "s", "i" });
            case "ModifyLogSwitch": return I("il2cpp::runtime::icall_log_modify_log_switch", 2, false, false, false, new[] { "s", "i" });
            case "nativeGetTwoDigitYearMax": return I("il2cpp::runtime::icall_globalization_calendar_data_native_get_two_digit_year_max", 1, true, false, false, new[] { "i" });
            case "nativeGetCalendars": return I("il2cpp::runtime::icall_globalization_calendar_data_native_get_calendars", 1, true, true, false, new[] { "i" });
            case "nativeGetLocaleInfoEx": return I("il2cpp::runtime::icall_globalization_culture_info_native_get_locale_info_ex", 1, true, true, false, new[] { "i" });
            // Crypto stubs (16 Utils + 14 X509)
            case "_CreateCSP": return I("il2cpp::runtime::icall_crypto_utils_create_csp", 0, true, true, false);
            case "_DecryptData": return I("il2cpp::runtime::icall_crypto_utils_decrypt_data", 1, true, true, false);
            case "_EncryptData": return I("il2cpp::runtime::icall_crypto_utils_encrypt_data", 1, true, true, false);
            case "_ExportKey": return I("il2cpp::runtime::icall_crypto_utils_export_key", 1, true, true, false);
            case "_GenerateKey": return I("il2cpp::runtime::icall_crypto_utils_generate_key", 1, true, false, false, new[] { "i" });
            case "_GetEnforceFipsPolicySetting": return I("il2cpp::runtime::icall_crypto_utils_get_enforce_fips_policy_setting", 0, true, false, false);
            case "_GetKeyParameter": return I("il2cpp::runtime::icall_crypto_utils_get_key_parameter", 1, true, true, false, new[] { "", "s" });
            case "_GetKeySetSecurityInfo": return I("il2cpp::runtime::icall_crypto_utils_get_key_set_security_info", 0, true, true, false);
            case "_GetProviderParameter": return I("il2cpp::runtime::icall_crypto_utils_get_provider_parameter", 1, true, true, false, new[] { "", "s" });
            case "_GetUserKey": return I("il2cpp::runtime::icall_crypto_utils_get_user_key", 0, true, true, false);
            case "_ImportBulkKey": return I("il2cpp::runtime::icall_crypto_utils_import_bulk_key", 1, true, true, false);
            case "_ImportCspBlob": return I("il2cpp::runtime::icall_crypto_utils_import_csp_blob", 1, true, true, false);
            case "_ImportKey": return I("il2cpp::runtime::icall_crypto_utils_import_key", 1, true, true, false);
            case "_ProduceLegacyHmacValues": return I("il2cpp::runtime::icall_crypto_utils_produce_legacy_hmac_values", 0, true, false, false);
            case "_OpenCSP": return I("il2cpp::runtime::icall_crypto_utils_open_csp", 1, true, true, false, new[] { "i" });
            case "_AcquireCSP": return I("il2cpp::runtime::icall_crypto_utils_acquire_csp", 0, true, true, false);
            // X509 stubs
            case "_AddCertificateToStore": return I("il2cpp::runtime::icall_x509_utils_add_certificate_to_store", 1, true, true, false);
            case "_ExportCertificatesToBlob": return I("il2cpp::runtime::icall_x509_utils_export_certificates_to_blob", 1, true, true, false);
            case "_GetCertRawData": return I("il2cpp::runtime::icall_x509_utils_get_cert_raw_data", 0, true, true, false);
            case "_GetDateNotAfter": return I("il2cpp::runtime::icall_x509_utils_get_date_not_after", 0, true, true, false);
            // AppDomain: handled by TryMapLegacy (LOUD -- no runtime AppDomain object)
            // ---- Full-framework audit-gap closure (2026-08-22) ----
            // GC.GetMemoryInfo -- the netcore21 5-out / netcore31 6-out
            // telemetry fills (raw sigs verified: every param BYREF). The
            // simplified GC answers zeros in every out slot (stubs.cpp).
            case "GetMemoryInfo":
                if (count == 5)
                    return I("il2cpp::runtime::icall_gc_get_memory_info_5", 5, false, false, false,
                        new[] { "addr:i", "addr:l", "addr:i", "addr:l", "addr:l" });
                if (count == 6)
                    return I("il2cpp::runtime::icall_gc_get_memory_info_6", 6, false, false, false,
                        new[] { "addr:l", "addr:l", "addr:l", "addr:i", "addr:l", "addr:l" });
                return null;
            // System.Number formatting internals: NumberBuffer is internal
            // (user code cannot construct it) -- the runtime entries throw
            // PNSE with the documented reason (stubs.cpp).
            case "DoubleToNumber":
                if (count == 3)
                    return I("il2cpp::runtime::icall_number_double_to_number", 3, false, false, false,
                        new[] { "d", "i", "" });
                return null;
            case "NumberToDouble":
                if (count == 1)
                    return I("il2cpp::runtime::icall_number_number_to_double", 1, true, false, false,
                        new[] { "" }, "d");
                return null;
            // RuntimeImports.RhBulkMoveWithWriteBarrier (owner routed here
            // from BclResolveCore): memmove + per-slot card marking.
            case "RhBulkMoveWithWriteBarrier":
                if (count == 3)
                    return I("il2cpp::runtime::icall_rh_bulk_move_with_write_barrier", 3, false, false, false,
                        new[] { "p", "p", "l" });
                return null;
            // Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs
            // (owner routed here): ETW callback payload -- instances are
            // constructed only by the runtime during ETW callbacks, which
            // never fire; ctor no-op + tracing-disabled defaults.
            // (The System.Diagnostics.StackTrace ctor arm that used to live
            // here moved to TryMapStackTraceDiagnostics: the class has a real
            // object model now, so its ctors capture the frame stack.)
            case ".ctor":
                if (count == 0 && owner.EndsWith("TracingStatusChangedEventArgs", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_tracing_status_changed_ctor", 0, false, false, true);
                return null;
            case "get_Enabled":
                if (count == 0 && owner.EndsWith("TracingStatusChangedEventArgs", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_tracing_status_changed_get_enabled", 0, true, false, true);
                return null;
            case "get_TraceLevel":
                if (count == 0 && owner.EndsWith("TracingStatusChangedEventArgs", StringComparison.Ordinal))
                    return I("il2cpp::runtime::icall_tracing_status_changed_get_trace_level", 0, true, false, true);
                return null;

            // ---- corelib-p0: the CoreLib-internal GC helpers ----------------
            // docs/audit/corelib-p0-reachability.md measured the shipped
            // System.Private.CoreLib 8.0.31/9.0.20 and found these members
            // present in BOTH as the QCall/FCall bodies behind the public
            // GC API. The public wrappers above are the user-visible surface;
            // the arms below close the internal surface so a CoreLib-shaped
            // assembly (private helper reached from a non-transpiled wrapper)
            // also resolves instead of emitting an undefined extern.
            //
            // Signatures are the MEASURED ones (MetadataReader dump of
            // 8.0.31), not the CoreCLR main-branch reference text -- e.g.
            // shipped `_Collect` takes (int generation, int mode), while the
            // main branch has grown a third bool.
            case "_Collect":
                // private void _Collect(int generation, int mode): the body of
                // GC.Collect. This collector only has a full blocking
                // collection, so both selectors are dropped (the "drop" casts
                // keep the fold's ParamCount in sync with the IL stack).
                if (count == 2)
                    return I("il2cpp::runtime::icall_gc_collect", 2, false, false, false,
                        new[] { "drop", "drop" });
                return null;
            case "_CollectionCount":
                // private int _CollectionCount(int generation, int
                // getSpecialGCCount): the second argument selects BCL-only
                // special counters that this runtime does not maintain; it is
                // dropped. The value is the same per-generation counter the
                // public GC.CollectionCount reads.
                if (count == 2)
                    return I("il2cpp::il2cpp_gc_get_collection_count", 2, true, false, false,
                        new[] { "i", "drop" }, "l");
                return null;
            case "_GetTotalPauseDuration":
                // private long _GetTotalPauseDuration(): total collection
                // pause time in TimeSpan ticks (100ns) -- exactly what
                // il2cpp_gc_get_total_pause_ticks accumulates in gc.cpp.
                if (count == 0)
                    return I("il2cpp::il2cpp_gc_get_total_pause_ticks", 0, true, false, false, null, "l");
                return null;
            case "_RegisterForFullGCNotification":
                // private bool _RegisterForFullGCNotification(int, int): the
                // BLOCKING collector always accepts (gc.cpp returns true).
                if (count == 2)
                    return I("il2cpp::il2cpp_gc_register_for_full_gc_notification", 2, true, false, false,
                        new[] { "i", "i" });
                return null;
            case "_CancelFullGCNotification":
                if (count == 0)
                    return I("il2cpp::il2cpp_gc_cancel_full_gc_notification", 0, true, false, false);
                return null;
            case "_AddMemoryPressure":
                // private void _AddMemoryPressure(ulong bytesAllocated): the
                // collector has no pressure-driven trigger, so the count is
                // accepted and ignored (the net48 AddMemoryPressure contract).
                if (count == 1)
                    return I("il2cpp::runtime::icall_gc_add_memory_pressure", 1, false, false, false,
                        new[] { "l" });
                return null;
            case "_RemoveMemoryPressure":
                if (count == 1)
                    return I("il2cpp::runtime::icall_gc_remove_memory_pressure", 1, false, false, false,
                        new[] { "l" });
                return null;
            case "_WaitForPendingFinalizers":
                // private void _WaitForPendingFinalizers(): run every due
                // finalizer synchronously (the gcext trigger -- the same entry
                // the public WaitForPendingFinalizers uses).
                if (count == 0)
                    return I("il2cpp::runtime::icall_gc_finalize_trigger", 0, false, false, false);
                return null;
            case "_EndNoGCRegion":
                // private int _EndNoGCRegion() -> EndNoGCRegionStatus: no NoGC
                // region can ever be in progress in this collector, so the
                // honest status is NotInProgress (1). The public wrapper turns
                // that into InvalidOperationException -- the exact .NET
                // behaviour of EndNoGCRegion outside a region.
                if (count == 0)
                    return I("il2cpp::runtime::icall_gc_end_no_gc_region", 0, true, false, false);
                return null;
            case "_EnableNoGCRegionCallback":
                // private EnableNoGCRegionCallbackStatus
                // _EnableNoGCRegionCallback(NoGCRegionCallbackFinalizerWorkItem*,
                // long): only reachable after a successful NoGC-region start,
                // which this collector never grants -> NotStarted (1). The
                // work item is never enqueued, so both arguments are dropped
                // (the pointer's pointee is a CoreLib-internal struct with no
                // C++ model -- and nothing would ever read it).
                if (count == 2)
                    return I("il2cpp::runtime::icall_gc_enable_no_gc_region_callback", 2, true, false, false,
                        new[] { "drop", "drop" });
                return null;
            case "_GetGenerationBudget":
                // private long _GetGenerationBudget(int generation): bytes
                // left before this collector's next implicit collection
                // (gc.cpp g_bumpedSinceCollect vs kCollectAfterBumpedBytes).
                if (count == 1)
                    return I("il2cpp::il2cpp_gc_get_generation_budget", 1, true, false, false, new[] { "i" }, "l");
                return null;
            case "_RegisterFrozenSegment":
                // private IntPtr _RegisterFrozenSegment(IntPtr sectionAddress,
                // nint sectionSize): the collector has no frozen-segment heap,
                // so registration only records an opaque handle (see
                // icall_gc_register_frozen_segment in stubs.cpp).
                if (count == 2)
                    return I("il2cpp::runtime::icall_gc_register_frozen_segment", 2, true, false, false,
                        new[] { "", "" }, "ptr");
                return null;
            case "_UnregisterFrozenSegment":
                // private void _UnregisterFrozenSegment(IntPtr segmentHandle)
                if (count == 1)
                    return I("il2cpp::runtime::icall_gc_unregister_frozen_segment", 1, false, false, false,
                        new[] { "" });
                return null;
            case "_EnumerateConfigurationValues":
                // private void _EnumerateConfigurationValues(void*
                // configurationDictionary, callback): this collector exposes no
                // tunable configuration values, so the enumeration completes
                // without invoking the callback (the public
                // GC.GetConfigurationVariables sees an empty map). The target
                // dictionary and the native function pointer are CoreLib-side
                // objects that nothing here would touch -> both dropped.
                if (count == 2)
                    return I("il2cpp::runtime::icall_gc_enumerate_configuration_values", 2, false, false, false,
                        new[] { "drop", "drop" });
                return null;

            // ---- corelib-p0: the PUBLIC wrappers over the QCall bodies above --
            // A net8/net9-compiled caller binds these directly (the wrapper IL
            // lives in CoreLib and is never transpiled), so the public shape
            // needs its own arm. Each one reports the same truth the internal
            // arm reports; the behaviour measured on the shipped 8.0.31 runtime
            // is recorded next to each arm.
            case "GetTotalPauseDuration":
                // public static TimeSpan GetTotalPauseDuration() -- net8
                // returns new TimeSpan(_GetTotalPauseDuration()), i.e. the
                // collector's accumulated pause ticks (0 on a fresh process).
                if (count == 0)
                    return I("il2cpp::runtime::icall_gc_get_total_pause_duration", 0, true, false, false,
                        null, "ts", returnTypeName: "System.TimeSpan");
                return null;
            case "EndNoGCRegion":
                // public static void EndNoGCRegion(): the wrapper throws
                // InvalidOperationException when the internal status is
                // NotInProgress -- measured on the shipped runtime as
                // "NoGCRegion mode must be set." The runtime entry raises the
                // same exception (this collector never grants a NoGC region).
                if (count == 0)
                    return I("il2cpp::runtime::icall_gc_end_no_gc_region_public", 0, false, false, false);
                return null;
            case "RegisterNoGCRegionCallback":
                // public static void RegisterNoGCRegionCallback(long
                // totalSize, Action callback): same measured contract as
                // EndNoGCRegion outside a region -- InvalidOperationException
                // ("NoGCRegion mode must be set."). Both arguments are dropped:
                // no region exists, so the work item is never enqueued.
                if (count == 2)
                    return I("il2cpp::runtime::icall_gc_register_no_gc_region_callback_public", 2,
                        false, false, false, new[] { "l", "" });
                return null;
            case "RefreshMemoryLimit":
                // public static void RefreshMemoryLimit(): re-reads the
                // container/physical memory limits and reconfigures the
                // collector. A fixed-budget collector has nothing to
                // reconfigure, so the call is a no-op -- measured on the
                // shipped runtime as a normal return.
                if (count == 0)
                    return I("il2cpp::runtime::icall_gc_refresh_memory_limit", 0, false, false, false);
                return null;
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // TryMapStackTraceDiagnostics -- System.Diagnostics.StackTrace /
    // StackFrame managed surface (stacktrace-real).
    //
    // WHY IT IS REAL: net48's stack trace is NOT undocumented CLR state for
    // this runtime -- il2cpp_frame.h keeps a per-thread shadow stack with one
    // record per live managed call (the emitter fills each record's managed
    // spelling, CodeGenerator.WrapFrameRoots). So the net48 frame text is
    // reproducible from that stack; what cannot be reproduced is the PDB
    // (file/line) and the CLR-internal helper frames -- a FORMAT divergence,
    // not a reason to reject the caller. Rejecting here used to delete the
    // WHOLE enclosing method from the output (one Console.WriteLine(env.
    // StackTrace) made an entire program untranspilable).
    //
    // ctors -- capture the shadow stack (net48 captures at construction):
    //   ()                      icall_stacktrace_ctor
    //   (bool needFileInfo)     same (needFileInfo dropped: no PDB)
    //   (int skipFrames)        icall_stacktrace_ctor_skip
    //   (int, bool)             icall_stacktrace_ctor_skip (bool dropped)
    //   (Exception e)           icall_stacktrace_ctor_exception (live stack;
    //   (Exception, bool)       this runtime's exception object carries no
    //   (Exception, int)        throw-time frame list -- b5-gaps.md)
    //   (Exception, int, bool)
    //   (StackFrame[] frames)   icall_stacktrace_ctor_frames (adopts the array)
    //   (StackFrame[], bool)
    // ps codes: 0x02 bool, 0x08 int32, 0x12 class (Exception), 0x1D SZARRAY.
    // An unmodelled shape falls back to the plain live snapshot (dropped
    // arguments) rather than rejecting the caller.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapStackTraceDiagnostics(string owner, string name, byte[] ps, int count)
    {
        const string stackTraceCtor = "il2cpp::runtime::icall_stacktrace_ctor";
        const string stackTraceCtorSkip = "il2cpp::runtime::icall_stacktrace_ctor_skip";
        const string stackTraceCtorEx = "il2cpp::runtime::icall_stacktrace_ctor_exception";
        const string stackTraceCtorExSkip = "il2cpp::runtime::icall_stacktrace_ctor_exception_skip";
        const string stackTraceCtorFrames = "il2cpp::runtime::icall_stacktrace_ctor_frames";

        if (name == ".ctor")
        {
            if (owner == "System.Diagnostics.StackFrame")
            {
                // net48 StackFrame ctors ((int), (bool), (int,bool), (Thread,
                // bool), ...): every arity builds a frame with no method --
                // the frame selector arguments describe a CLR stack pointer
                // this build does not have. GetMethod() then answers null,
                // exactly like net48's `new StackFrame()`.
                if (count == 0)
                    return I("il2cpp::runtime::icall_stacktrace_frame_ctor", 0, false, false, true);
                var frameDrops = new string[count + 1];
                frameDrops[0] = "";
                for (var di = 1; di <= count; di++) frameDrops[di] = "drop";
                return I("il2cpp::runtime::icall_stacktrace_frame_ctor", count, false, false, true, frameDrops);
            }
            // System.Diagnostics.StackTrace -- see the shape table above.
            switch (count)
            {
                case 0:
                    return I(stackTraceCtor, 0, false, false, true);
                case 1:
                    if (ps.Length == 1 && ps[0] == 0x08)
                        return I(stackTraceCtorSkip, 1, false, false, true, new[] { "", "i" });
                    if (ps.Length == 1 && ps[0] == 0x02)
                        return I(stackTraceCtor, 1, false, false, true, new[] { "", "drop" });
                    if (ps.Length == 1 && ps[0] == 0x12)
                        return I(stackTraceCtorEx, 1, false, false, true, new[] { "", "" });
                    if (ps.Length == 1 && ps[0] == 0x1D)
                        return I(stackTraceCtorFrames, 1, false, false, true, new[] { "", "a" });
                    break;
                case 2:
                    if (ps.Length == 2 && ps[0] == 0x08 && ps[1] == 0x02)
                        return I(stackTraceCtorSkip, 2, false, false, true, new[] { "", "i", "drop" });
                    if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x02)
                        return I(stackTraceCtorEx, 2, false, false, true, new[] { "", "", "drop" });
                    if (ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x08)
                        return I(stackTraceCtorExSkip, 2, false, false, true, new[] { "", "", "i" });
                    if (ps.Length == 2 && ps[0] == 0x1D && ps[1] == 0x02)
                        return I(stackTraceCtorFrames, 2, false, false, true, new[] { "", "a", "drop" });
                    break;
                case 3:
                    if (ps.Length == 3 && ps[0] == 0x12 && ps[1] == 0x08 && ps[2] == 0x02)
                        return I(stackTraceCtorExSkip, 3, false, false, true, new[] { "", "", "i", "drop" });
                    break;
            }
            // Unmodelled ctor shape: still a real capture (live stack, no
            // skip), arguments dropped at emission. Never a rejection.
            var drops = new string[count + 1];
            drops[0] = "";
            for (var di = 1; di <= count; di++) drops[di] = "drop";
            return I(stackTraceCtor, count, false, false, true, drops);
        }

        if (owner == "System.Diagnostics.StackTrace")
        {
            switch (name)
            {
                case "get_FrameCount":
                    if (count == 0)
                        return I("il2cpp::runtime::icall_stacktrace_get_frame_count", 0, true, false, true);
                    return null;
                case "GetFrames":
                    if (count == 0)
                        return I("il2cpp::runtime::icall_stacktrace_get_frames", 0, true, true, true,
                            returnTypeName: "System.Diagnostics.StackFrame[]");
                    return null;
                case "GetFrame":
                    // net48 StackFrame GetFrame(int index): null for an
                    // out-of-range index.
                    if (count == 1 && ps.Length == 1 && ps[0] == 0x08)
                        return I("il2cpp::runtime::icall_stacktrace_get_frame", 1, true, true, true,
                            new[] { "", "i" }, returnTypeName: "System.Diagnostics.StackFrame");
                    return null;
                case "ToString":
                    if (count == 0)
                        return I("il2cpp::runtime::icall_stacktrace_to_string", 0, true, true, true,
                            returnTypeName: "System.String");
                    return null;
            }
            return null;
        }

        // System.Diagnostics.StackFrame
        switch (name)
        {
            case "GetMethod":
                // MethodBase VALUE: the method's SIMPLE managed name (the
                // simplified member model the reflection surface resolves --
                // MemberInfo.get_Name / get_DeclaringType / GetParameters).
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_get_method", 0, true, true, true,
                        returnTypeName: "System.Reflection.MethodBase");
                return null;
            case "GetFileName":
                // No PDB in an AOT build: net48's own answer for a frame built
                // without file info. The net48 " in <path>:line N" suffix is a
                // FORMAT divergence (b5-gaps.md), never a rejection.
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_get_file_name", 0, true, true, true,
                        returnTypeName: "System.String");
                return null;
            case "GetFileLineNumber":
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_get_file_line_number", 0, true, false, true);
                return null;
            case "GetFileColumnNumber":
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_get_file_column_number", 0, true, false, true);
                return null;
            case "GetILOffset":
            case "GetNativeOffset":
                // net48 answers -1 for a frame with no offset information
                // (both offsets are CLR code-layout state an AOT build does
                // not carry). A REAL answer, not a rejection.
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_get_unknown_offset", 0, true, false, true);
                return null;
            case "ToString":
                if (count == 0)
                    return I("il2cpp::runtime::icall_stackframe_to_string", 0, true, true, true,
                        returnTypeName: "System.String");
                return null;
        }
        return null;
    }
}
