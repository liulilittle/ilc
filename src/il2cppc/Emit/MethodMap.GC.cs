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
                throw new NotSupportedException("GC.Collect(int) not wired: the runtime has only the parameterless icall");
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
            case "GetTotalAllocatedBytes": return I("il2cpp::runtime::icall_gc_get_total_allocated_bytes_approximate", 1, true, false, false, new[] { "drop" }, "l");
            // M7 gcext wiring (2026-08-08): GC.WaitForPendingFinalizers() -> the
            // direct finalizer trigger (il2cpp_gc_ext.h -- icall_gc_finalize_trigger
            // runs any due/pending finalizers synchronously, the gcext two-
            // collection model's synchronous counterpart to .NET's finalizer
            // thread; see the gcext.cpp docblock + test_gcext.cpp §9). Static,
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
            case "GetGCLatencyMode": return I("il2cpp::runtime::icall_gc_get_latency_mode", 0, true, false, false);
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
            case "GetSegmentSize": return I("il2cpp::runtime::il2cpp_gc_get_segment_size", 0, true, false, false, null, "l");
            case "GetLastGCPercentTimeInGC": return I("il2cpp::runtime::il2cpp_gc_get_last_gc_percent_time_in_gc", 0, true, false, false);
            case "GetGenerationSize": return I("il2cpp::runtime::il2cpp_gc_get_generation_size", 1, true, false, false, new[] { "i" }, "l");
            case "GetMaxGeneration": return I("il2cpp::runtime::il2cpp_gc_get_max_generation", 0, true, false, false);
            case "GetAllocatedBytesForCurrentThread": return I("il2cpp::runtime::il2cpp_gc_get_allocated_bytes_for_current_thread", 0, true, false, false, null, "l");
            case "GetFragmentedBytes": return I("il2cpp::runtime::il2cpp_gc_get_fragmented_bytes", 0, true, false, false, null, "l");
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
            case "GetStackFramesInternal": return I("il2cpp::runtime::icall_stack_trace_get_stack_frames_internal", 2, true, true, false, new[] { "", "i", "i" });
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
            // OverlappedData stubs
            case "get_PendingCount": return I("il2cpp::runtime::icall_overlapped_data_get_pending_count", 0, true, false, false);
            case "get_WaitHandle": return I("il2cpp::runtime::icall_overlapped_data_get_wait_handle", 0, true, false, false);
            case "get_ErrorCode": return I("il2cpp::runtime::icall_overlapped_data_get_error_code", 0, true, false, false);
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
            case "FailFast": return I("il2cpp::runtime::icall_environment_fail_fast", 1, false, false, false, new[] { "s" });
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
            default: return null;
        }
    }
}
