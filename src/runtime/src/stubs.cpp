// stubs.cpp -- Internal CLR helper stubs for remaining MethodInternalCall gaps.
// The ILC runtime reimplements BCL natively; these are stubs that return
// sensible defaults for methods the BCL's internal infrastructure calls.

#include "../include/il2cpp_api.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h" // il2cpp_write_barrier (RhBulkMove card marking)
#include "il2cpp_interlocked.h" // il2cpp_interlocked_compare_exchange (out-bool wrapper)
#include "il2cpp_time.h" // Il2CppDateTime (Guid_NewGuid stub)
#include "il2cpp_environment.h" // icall_environment_get_processor_count (ThreadPool alignment)
#include "threadpool.h" // il2cpp_threadpool_get_thread_count (live worker count)

#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace il2cpp {
namespace runtime {

// System.Array.GetCorElementTypeOfElementType
int32_t icall_array_get_cor_element_type_of_element_type(Il2CppObject*)
{
    return 4; // ELEMENT_TYPE_I4 (int32 default)
}

// System.GC.AllocateNewArray
Il2CppObject* icall_gc_allocate_new_array(int32_t, int32_t)
{
    return nullptr; // stub
}

// System.ComAwareWeakReference.HasInteropInfo
int32_t icall_com_aware_weak_reference_has_interop_info(Il2CppObject*)
{
    return 0; // false
}

// System.Exception.SaveStackTracesFromDeepCopy
void icall_exception_save_stack_traces_from_deep_copy(Il2CppObject*)
{
    // no-op
}

// System.Exception message carrier (ilistException): {exception object ->
// message} side table. Both slots live in fixed static storage rooted once
// at insert (the M2 collector never moves objects, so raw-pointer keys stay
// valid). Fixed capacity following the kMaxElementArrayClasses /
// kMaxAttributeTypes precedents; overflow keeps the first entries (exceptions
// carrying explicit messages are rare -- the e2e surface uses three).
//------------------------------------------------------------------------------
namespace {
struct ExceptionMessageEntry { Il2CppObject* obj; Il2CppString* msg; };
constexpr int32_t kMaxExceptionMessages = 64;
ExceptionMessageEntry g_exceptionMessages[kMaxExceptionMessages];
int32_t g_exceptionMessageCount = 0;
} // namespace
void icall_exception_ctor_message(Il2CppObject* self, Il2CppString* message)
{
    if (self == nullptr) return;
    for (int32_t i = 0; i < g_exceptionMessageCount; ++i)
    {
        if (g_exceptionMessages[i].obj == self)
        {
            g_exceptionMessages[i].msg = message;
            return;
        }
    }
    if (g_exceptionMessageCount >= kMaxExceptionMessages) return;
    const int32_t i = g_exceptionMessageCount++;
    g_exceptionMessages[i].obj = self;
    g_exceptionMessages[i].msg = message;
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_exceptionMessages[i].obj));
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_exceptionMessages[i].msg));
}
Il2CppString* icall_exception_get_message(Il2CppObject* obj)
{
    if (obj != nullptr)
    {
        for (int32_t i = 0; i < g_exceptionMessageCount; ++i)
        {
            if (g_exceptionMessages[i].obj == obj && g_exceptionMessages[i].msg != nullptr)
                return g_exceptionMessages[i].msg;
        }
    }
    return il2cpp_string_new(nullptr, 0); // no captured message -> empty string
}

// System.Exception.GetExceptionCount
int32_t icall_exception_get_exception_count()
{
    return 0;
}

// System.Type.GetTypeFromHandleUnsafe
Il2CppObject* icall_type_get_type_from_handle_unsafe(intptr_t)
{
    return nullptr; // stub
}

// System.Runtime.JitInfo.GetCompiledILBytes
int64_t icall_jit_info_get_compiled_il_bytes()
{
    return 0;
}

// System.Runtime.JitInfo.GetCompiledMethodCount
int64_t icall_jit_info_get_compiled_method_count()
{
    return 0;
}

// System.Runtime.Loader.AssemblyLoadContext.GetLoadedAssemblies
Il2CppObject* icall_assembly_load_context_get_loaded_assemblies()
{
    return nullptr; // empty array
}

// System.Runtime.Loader.AssemblyLoadContext.IsTracingEnabled
int32_t icall_assembly_load_context_is_tracing_enabled()
{
    return 0; // false
}

// System.Diagnostics.StackTrace.GetStackFramesInternal
Il2CppObject* icall_stack_trace_get_stack_frames_internal(Il2CppObject*, int32_t, int32_t)
{
    return nullptr; // empty array
}

// Number formatting stubs
Il2CppObject* icall_number_format_decimal(Il2CppObject*, int32_t) { return nullptr; }
Il2CppObject* icall_number_format_double(double, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_number_format_int32(int32_t, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_number_format_uint32(uint32_t, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_number_format_int64(int64_t, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_number_format_uint64(uint64_t, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_number_format_single(float, Il2CppString*, Il2CppObject*) { return nullptr; }
void icall_number_number_buffer_to_decimal(Il2CppObject*, Il2CppObject*) {}
double icall_number_number_buffer_to_double(Il2CppObject*) { return 0.0; }
Il2CppObject* icall_number_format_number_buffer(int32_t, int32_t) { return nullptr; }

// ParseNumbers stubs
int64_t icall_parse_numbers_string_to_long(Il2CppString*, int32_t, int32_t) { return 0; }
int32_t icall_parse_numbers_string_to_int(Il2CppString*, int32_t, int32_t) { return 0; }
Il2CppObject* icall_parse_numbers_int_to_string(int32_t, int32_t, int32_t) { return nullptr; }
Il2CppObject* icall_parse_numbers_long_to_string(int64_t, int32_t, int32_t) { return nullptr; }

// CurrentSystemTimeZone stubs
int32_t icall_current_system_timezone_native_get_time_zone_minute_offset() { return 0; }
Il2CppObject* icall_current_system_timezone_native_get_daylight_name() { return nullptr; }
Il2CppObject* icall_current_system_timezone_native_get_standard_name() { return nullptr; }
Il2CppObject* icall_current_system_timezone_native_get_daylight_changes(int32_t) { return nullptr; }

// CultureData stubs
int32_t icall_culture_data_lcid_to_locale_name(int32_t) { return 0; }
int32_t icall_culture_data_locale_name_to_lcid(Il2CppString*) { return 0; }
void icall_culture_data_native_init_culture_data(Il2CppObject*) {}
Il2CppObject* icall_culture_data_native_get_number_format_info_values(Il2CppObject*) { return nullptr; }

// CodeAccessSecurityEngine stubs
void icall_code_access_security_engine_special_demand(int32_t) {}
int32_t icall_code_access_security_engine_quick_check_for_all_demands() { return 1; }
int32_t icall_code_access_security_engine_all_domains_homogeneous_with_no_stack_modifiers() { return 1; }
int32_t icall_code_access_security_engine_check_n_return_so(int32_t) { return 0; }

// CompressedStack stubs
Il2CppObject* icall_compressed_stack_get_delayed_compressed_stack(Il2CppObject*) { return nullptr; }
void icall_compressed_stack_destroy_delayed_compressed_stack(Il2CppObject*) {}
void icall_compressed_stack_destroy_dcs_list(Il2CppObject*) {}
int32_t icall_compressed_stack_get_dcs_count(Il2CppObject*) { return 0; }

// DomainCompressedStack stubs
int32_t icall_domain_compressed_stack_create_domain_compressed_stack(Il2CppObject*) { return 0; }
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_size(Il2CppObject*) { return 0; }
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_flags(Il2CppObject*) { return 0; }
Il2CppObject* icall_domain_compressed_stack_get_domain_compressed_stack_stack_blobs(Il2CppObject*) { return nullptr; }

// ThreadPool counting -- net48-aligned (GN-20, probe-tpcountcase). The ILC
// runtime's pool is FIXED (min(CPU,4) workers); net48's ThreadPool sizes are
// environment-dependent and MUST be reported byte-exact from the same
// environment so net48-oracle e2e diffing passes. Semantic basis (verified
// by the tpcountcase net48 probe on the oracle host):
//   * GetMinThreads -> (worker = ProcessorCount, io = ProcessorCount). .NET
//     Framework's default minimum is one worker (and one IOCP) per logical
//     processor; the probe confirmed minW == minIO == ProcessCount (32/32).
//   * GetMaxThreads -> (worker = 32767 on 64-bit / 1023 on 32-bit, io = 1000).
//     These are the framework's fixed maxima (probe: 32767/1000 on x64).
//   * GetAvailableThreads -> (maxWorkers - busy, maxIO - busyIO). At
//     quiescence (the probe never queues work) available == max on both
//     engines; busier-than-idle reports are inherently timing-dependent on
//     net48 and are recorded as a W7 deterministic-deviation (see the report).
//   * ThreadCount -> the NUMBER OF LIVE POOL WORKER THREADS (0 before the
//     fixed pool is first used, then the fixed count). net48 has no
//     ThreadPool.ThreadCount member (probe-verified zero static properties),
//     so this slot has no net48 oracle; the live count is the honest value.
// ProcessorCount is taken from icall_environment_get_processor_count() -- the
// SAME source as Environment.ProcessorCount -- so the values stay internally
// consistent with the probe's own processorCount line on any host.
namespace {
int32_t thread_pool_max_worker()
{
    return (sizeof(void*) == 8) ? 32767 : 1023;
}
constexpr int32_t kThreadPoolMaxIo = 1000;
} // namespace

int32_t icall_thread_pool_get_thread_count()
{
    return il2cpp_threadpool_get_thread_count();
}
int32_t icall_thread_pool_get_min_threads()
{
    return icall_environment_get_processor_count();
}
int32_t icall_thread_pool_get_max_threads()
{
    return thread_pool_max_worker();
}
int32_t icall_thread_pool_get_available_threads()
{
    // Quiescent model: the ILC fixed pool is never "busier than idle" between
    // work items by construction (min(CPU,4) workers all idle), and net48
    // reports max - busy == max under the same read-only probe. See the
    // W7 note: a busy interval cannot be byte-exact (net48 timing-dependent).
    return thread_pool_max_worker();
}
// The PUBLIC out-param forms (GetMinThreads/GetMaxThreads/
// GetAvailableThreads(out workerThreads, out completionPortThreads)).
void icall_thread_pool_get_min_threads_out(int32_t* workerThreads, int32_t* completionPortThreads)
{
    const int32_t minWorker = icall_environment_get_processor_count();
    if (workerThreads) *workerThreads = minWorker;
    if (completionPortThreads) *completionPortThreads = minWorker;
}
void icall_thread_pool_get_max_threads_out(int32_t* workerThreads, int32_t* completionPortThreads)
{
    if (workerThreads) *workerThreads = thread_pool_max_worker();
    if (completionPortThreads) *completionPortThreads = kThreadPoolMaxIo;
}
void icall_thread_pool_get_available_threads_out(int32_t* workerThreads, int32_t* completionPortThreads)
{
    // Quiescence: available == max (see the single-value form above).
    if (workerThreads) *workerThreads = thread_pool_max_worker();
    if (completionPortThreads) *completionPortThreads = kThreadPoolMaxIo;
}
// net6/net7: CanSetMin/MaxIOCompletionThreads -- the simplified pool has no
// IOCP model; the honest constant answer is false on every platform.
bool icall_thread_pool_can_set_io_threads() { return false; }

// OverlappedData stubs
int32_t icall_overlapped_data_get_pending_count(Il2CppObject*) { return 0; }
int32_t icall_overlapped_data_get_wait_handle(Il2CppObject*) { return 0; }
int32_t icall_overlapped_data_get_error_code(Il2CppObject*) { return 0; }

// Crypto/X509 stubs -- LOUD by doctrine (parity-T7, 2026-09-20): these
// QCall-shaped helpers are transpile-WIRED (MethodMap.GC), so a silent
// nullptr would surface far downstream as a NullReferenceException. The
// CSP/X509 models do not exist in this runtime; every object-returning stub
// throws NotSupportedException at the true gap location. (The four int32
// constant stubs stay untouched -- out of the nullptr family; follow-up to
// verify each constant against net48.)
#define IL2CPP_CRYPTO_X509_LOUD(what) \
    il2cpp_throw_not_supported_exception(what); \
    return nullptr; // unreachable (the throw never returns)
Il2CppObject* icall_crypto_utils_create_csp(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto CSP creation requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_decrypt_data(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto decryption requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_encrypt_data(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto encryption requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_export_key(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto key export requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_generate_key(int32_t) { IL2CPP_CRYPTO_X509_LOUD("Crypto key generation requires the CryptoAPI model (not present in this runtime)"); }
int32_t icall_crypto_utils_get_enforce_fips_policy_setting() { return 0; }
Il2CppObject* icall_crypto_utils_get_key_parameter(Il2CppObject*, Il2CppString*) { IL2CPP_CRYPTO_X509_LOUD("Crypto key parameters require the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_get_key_set_security_info(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto key security info requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_get_provider_parameter(Il2CppObject*, Il2CppString*) { IL2CPP_CRYPTO_X509_LOUD("Crypto provider parameters require the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_get_user_key(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto user keys require the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_import_bulk_key(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto bulk-key import requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_import_csp_blob(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto CSP-blob import requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_import_key(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto key import requires the CryptoAPI model (not present in this runtime)"); }
int32_t icall_crypto_utils_produce_legacy_hmac_values() { return 0; }
Il2CppObject* icall_crypto_utils_open_csp(Il2CppObject*, int32_t) { IL2CPP_CRYPTO_X509_LOUD("Crypto CSP open requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_crypto_utils_acquire_csp(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("Crypto CSP acquire requires the CryptoAPI model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_add_certificate_to_store(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 certificate stores require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_export_certificates_to_blob(Il2CppObject*, Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 certificate export requires the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_cert_raw_data(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 certificate data requires the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_date_not_after(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 certificate dates require the X509 model (not present in this runtime)"); }

// Remaining X509 stubs
Il2CppObject* icall_x509_utils_get_date_not_before(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 certificate dates require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_issuer_name(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 issuer names require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_public_key_oid(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 public keys require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_public_key_parameters(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 public keys require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_public_key_value(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 public keys require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_subject_info(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 subject info requires the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_serial_number(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 serial numbers require the X509 model (not present in this runtime)"); }
Il2CppObject* icall_x509_utils_get_thumbprint(Il2CppObject*) { IL2CPP_CRYPTO_X509_LOUD("X509 thumbprints require the X509 model (not present in this runtime)"); }
int32_t icall_x509_utils_query_cert_blob_type(Il2CppObject*) { return 0; }
int32_t icall_x509_utils_query_cert_file_type(Il2CppString*) { return 0; }
#undef IL2CPP_CRYPTO_X509_LOUD

// Remaining Environment stubs
Il2CppObject* icall_environment_get_environment_variable_native(Il2CppString*) { return nullptr; }
// (icall_environment_get_version/_ex moved to the PNSE contract at the tail)

// Remaining DateTime stubs
int64_t icall_datetime_get_system_time_as_file_time() { return 0; }
int32_t icall_datetime_validate_system_time(int64_t) { return 1; }
int64_t icall_datetime_get_system_time_with_leap_seconds_handling() { return 0; }
int64_t icall_datetime_system_file_time_to_system_time(int64_t ticks) { return ticks; }
int64_t icall_datetime_system_time_to_system_file_time(int64_t ticks) { return ticks; }

// Remaining String stubs
int32_t icall_string_native_compare_ordinal_ex(Il2CppObject*, Il2CppObject*, int32_t, int32_t, int32_t, int32_t) { return 0; }
int32_t icall_string_native_compare_ordinal_ignore_case_wc(Il2CppString*, Il2CppString*) { return 0; }
Il2CppObject* icall_string_internal_marvin32_hash_string(Il2CppString*) { return nullptr; }

// NOTE: icall_environment_fail_fast lives in environment.cpp:402 and
// icall_string_set_trail_byte / icall_string_try_get_trail_byte in
// stringext3.cpp -- do NOT define them here (ld.lld duplicate-symbol when
// the generated exe links every runtime TU; net9/10 E2E icallcase).


// net9/10 E2E campaign (environment sweep): 64-bit process/OS probes --
// pointer width IS the process width; the OS is 64-bit on every supported
// target (win-x64 / linux-x64 / android-arm64), so both report 1.
int32_t icall_environment_get_is_64bit_process()
{
    return sizeof(void*) == 8 ? 1 : 0;
}

int32_t icall_environment_get_is_64bit_operating_system()
{
    return 1; // all supported targets are 64-bit operating systems
}
// net9/10 E2E icallcase: Thread.SpinWait(iterations) -- a busy-wait primitive.
// The simplified runtime has no preemptive scheduler to yield to, so the
// faithful implementation is the pause loop itself (O(iterations), no side
// effects; net48 contract: never throws, returns void).
void icall_threadobj_spin_wait(int32_t iterations)
{
    for (volatile int32_t i = 0; i < iterations; ++i)
    {
        // Compiler-opaque busy iteration (the volatile target defeats DCE).
    }
}

// Remaining miscellaneous stubs
// (icall_object_memberwise_clone moved to object.cpp -- real impl, not a stub)
Il2CppObject* icall_array_get_data_ptr_offset_internal(Il2CppObject*) { return nullptr; }
Il2CppObject* icall_string_is_fast_sort(Il2CppString*) { return nullptr; }
int32_t icall_string_is_ascii(Il2CppString*) { return 1; }
// set/try_get_trail_byte: real impls in stringext3.cpp (see NOTE above).
int32_t icall_delegate_internal_equal_types(Il2CppObject*, Il2CppObject*) { return 1; }
int32_t icall_delegate_compare_unmanaged_function_ptrs(Il2CppObject*, Il2CppObject*) { return 0; }
void icall_buffer_internal_block_copy(Il2CppObject*, int32_t, Il2CppObject*, int32_t, int32_t) {}
Il2CppObject* icall_base_config_handler_run_parser() { return nullptr; }
int32_t icall_decimal_fcall_to_int32(Il2CppObject*) { return 0; }
int32_t icall_default_binder_can_convert_primitive_object_to_type(Il2CppObject*, int32_t) { return 0; }
Il2CppObject* icall_enum_internal_get_underlying_type(Il2CppClass*) { return nullptr; }
Il2CppObject* icall_app_domain_setup_update_context_property(Il2CppObject*, Il2CppString*, Il2CppObject*) { return nullptr; }
Il2CppObject* icall_marshal_by_ref_object_get_com_i_unknown(Il2CppObject*) { return nullptr; }
Il2CppObject* icall_missing_member_exception_format_signature(Il2CppString*) { return nullptr; }
int32_t icall_type_op_inequality(Il2CppObject* t1, Il2CppObject* t2)
{
    // Type-as-name model: a Type VALUE is the full-name string (see
    // il2cpp_typeext.h assumption 1), so != is string inequality. Null
    // doctrine: ReferenceEquals semantics on the pointers.
    if (t1 == nullptr || t2 == nullptr)
    {
        return t1 != t2 ? 1 : 0;
    }
    const Il2CppString* x = reinterpret_cast<const Il2CppString*>(t1);
    const Il2CppString* y = reinterpret_cast<const Il2CppString*>(t2);
    if (x->length != y->length)
    {
        return 1;
    }
    for (int32_t i = 0; i < x->length; ++i)
    {
        if (x->chars[i] != y->chars[i])
        {
            return 1;
        }
    }
    return 0;
}
int32_t icall_value_type_get_hash_code_of_ptr(void*) { return 0; }
int32_t icall_clr_config_check_legacy_managed_deflate_stream() { return 0; }
int32_t icall_clr_config_check_throw_unobserved_task_exceptions() { return 0; }
int32_t icall_security_context_get_impersonation_flow_mode() { return 0; }
int32_t icall_security_context_is_default_thread_security_info() { return 1; }
void icall_security_manager_set_thread_security(int32_t) {}
Il2CppObject* icall_security_runtime_get_security_object_for_frame() { return nullptr; }
Il2CppObject* icall_security_principal_win32_open_thread_token() { return nullptr; }
int32_t icall_globalization_calendar_data_native_get_two_digit_year_max(int32_t) { return 0; }
Il2CppObject* icall_globalization_calendar_data_native_get_calendars(int32_t) { return nullptr; }
Il2CppObject* icall_globalization_culture_info_native_get_locale_info_ex(int32_t) { return nullptr; }

// Remaining Environment/Calendar/CultureInfo stubs
int32_t icall_environment_get_resource_from_default(Il2CppString*, Il2CppString*) { return 0; }
int32_t icall_environment_get_compatibility_flag(Il2CppString*) { return 0; }
int32_t icall_calendar_data_native_get_two_digit_year_max(int32_t) { return 0; }
int32_t icall_calendar_data_native_get_calendars(int32_t, int32_t*) { return 0; }
int32_t icall_culture_info_native_get_locale_info_ex_int(int32_t) { return 0; }
void icall_culture_info_native_set_thread_locale(int32_t) {}
Il2CppObject* icall_encoding_table_native_create_open_file_mapping() { return nullptr; }
void icall_assert_show_default_assert_dialog() {}
void icall_log_add_log_switch(Il2CppString*, int32_t) {}
void icall_log_modify_log_switch(Il2CppString*, int32_t) {}

// Remaining Thread/Interlocked stubs
void icall_synchronization_context_wait_helper(int32_t) {}
int32_t icall_compressed_stack_is_immediate_completion_candidate() { return 0; }
Il2CppObject* icall_compressed_stack_get_domain_compressed_stack() { return nullptr; }
Il2CppObject* icall_compressed_stack_get_homogeneous_pls() { return nullptr; }
int32_t icall_domain_compressed_stack_get_desc_count() { return 0; }
Il2CppObject* icall_domain_compressed_stack_get_domain_permission_sets() { return nullptr; }
Il2CppObject* icall_domain_compressed_stack_get_descriptor_info() { return nullptr; }
int32_t icall_domain_compressed_stack_ignore_domain(int32_t) { return 0; }
void icall_host_execution_context_manager_release_host_security_context() {}
Il2CppObject* icall_host_execution_context_manager_clone_host_security_context() { return nullptr; }
Il2CppObject* icall_overlapped_data_free_native_overlapped() { return nullptr; }
Il2CppObject* icall_overlapped_data_get_overlapped_from_native() { return nullptr; }
int32_t icall_overlapped_data_check_vm_for_io_packet() { return 0; }
void icall_thread_pool_notify_work_item_complete() {}
void icall_thread_pool_report_thread_status(int32_t) {}
void icall_thread_pool_notify_work_item_progress_native() {}
int32_t icall_thread_pool_is_thread_pool_hosted() { return 0; }
void icall_thread_pool_clear_notify() {}

// AppDomain stubs
Il2CppObject* icall_appdomain_is_unloading_forced_finalize() { return nullptr; }
int32_t icall_appdomain_is_finalizing_for_unload() { return 0; }
Il2CppObject* icall_appdomain_publish_anonymously_hosted_dynamic_methods_assembly() { return nullptr; }
int32_t icall_appdomain_is_domain_id_valid(int32_t) { return 0; }
Il2CppObject* icall_appdomain_get_default_domain() { return nullptr; }
Il2CppObject* icall_appdomain_get_fusion_context() { return nullptr; }
Il2CppObject* icall_appdomain_get_security_descriptor() { return nullptr; }
int32_t icall_appdomain_n_create_domain(Il2CppString*, Il2CppObject*, Il2CppObject*) { return 0; }
Il2CppObject* icall_appdomain_n_create_instance(int32_t, Il2CppString*) { return nullptr; }
void icall_appdomain_update_loader_optimization(int32_t) {}
Il2CppObject* icall_appdomain_is_string_interned(Il2CppString*) { return nullptr; }
Il2CppObject* icall_appdomain_get_or_intern_string(Il2CppString*) { return nullptr; }
void icall_appdomain_n_unload(int32_t) {}
int32_t icall_appdomain_get_id() { return 0; }

// AppDomain manager / fusion PNSE stubs (net48 QCalls; declared in stubs.h)
void icall_appdomain_disable_fusion_updates_from_ad_manager(int32_t /*domainId*/)
{
    il2cpp_throw_not_supported_exception(
        "AppDomain.DisableFusionUpdatesFromADManager requires the fusion update model (not present in this runtime)");
}

void icall_appdomain_get_app_domain_manager_type(int32_t /*domainId*/, Il2CppString** /*assemblyName*/, Il2CppString** /*typeName*/)
{
    il2cpp_throw_not_supported_exception(
        "AppDomain.GetAppDomainManagerType requires the AppDomain manager model (not present in this runtime)");
}

void icall_appdomain_set_app_domain_manager_type(int32_t /*domainId*/, Il2CppString* /*assemblyName*/, Il2CppString* /*typeName*/)
{
    il2cpp_throw_not_supported_exception(
        "AppDomain.SetAppDomainManagerType requires the AppDomain manager model (not present in this runtime)");
}

void icall_appdomain_set_canonical_conditional_aptca_list(int32_t /*domainId*/, Il2CppString* /*list*/)
{
    il2cpp_throw_not_supported_exception(
        "AppDomain.SetCanonicalConditionalAptcaList requires the security transparency model (not present in this runtime)");
}

//------------------------------------------------------------------------------
// Full-framework audit-gap closure (2026-08-22): every remaining public
// InternalCall surface resolved -- either an honest implementation or the
// documented PNSE contract (never a dangling symbol, never silent wrong).
//------------------------------------------------------------------------------

// GC.GetMemoryInfo (netcore21: 5 outs / netcore31: 6 outs) -- the simplified
// GC has no highWaterMark/pause telemetry; the honest default answer is
// zeros in every out slot (the telemetry-absent contract).
void icall_gc_get_memory_info_5(uint32_t* highWaterMark, int64_t* totalAvailable,
                                uint32_t* lastGcTime, uintptr_t* pauseTime0,
                                uintptr_t* pauseTime1)
{
    if (highWaterMark) *highWaterMark = 0;
    if (totalAvailable) *totalAvailable = 0;
    if (lastGcTime) *lastGcTime = 0;
    if (pauseTime0) *pauseTime0 = 0;
    if (pauseTime1) *pauseTime1 = 0;
}

void icall_gc_get_memory_info_6(uint64_t* highWaterMark, uint64_t* totalAvailable,
                                uint64_t* lastGcTime, uint32_t* pauseTime0,
                                uintptr_t* pauseTime1, uintptr_t* pauseTime2)
{
    if (highWaterMark) *highWaterMark = 0;
    if (totalAvailable) *totalAvailable = 0;
    if (lastGcTime) *lastGcTime = 0;
    if (pauseTime0) *pauseTime0 = 0;
    if (pauseTime1) *pauseTime1 = 0;
    if (pauseTime2) *pauseTime2 = 0;
}

// RuntimeImports.RhBulkMoveWithWriteBarrier (netcore21/31 GC internal):
// a bulk move whose destination slots must be card-marked when they may
// bridge generations. The move itself is memmove (overlap-safe); the
// conservative barrier marks every destination slot through the same
// write-barrier primitive the array-copy path uses.
void icall_rh_bulk_move_with_write_barrier(void* dst, void* src, int64_t size)
{
    if (dst == nullptr || src == nullptr || size <= 0)
    {
        return; // degenerate ranges are no-ops (the CLR contract)
    }
    std::memmove(dst, src, static_cast<size_t>(size));
    auto** slots = static_cast<void**>(dst);
    const intptr_t count = static_cast<intptr_t>(size) / static_cast<intptr_t>(sizeof(void*));
    for (intptr_t i = 0; i < count; ++i)
    {
        il2cpp_write_barrier(&slots[i], static_cast<Il2CppObject*>(slots[i]));
    }
}

// Thread.GetThreadDeserializationTracker (netcore31/net5 remoting-era
// surface): the simplified runtime has no remoting deserialization pipeline
// -- the documented PNSE contract.
Il2CppObject* icall_thread_get_deserialization_tracker(Il2CppObject*)
{
    il2cpp_throw_not_supported_exception(
        "Thread.GetThreadDeserializationTracker requires the remoting infrastructure (not present in this runtime)");
    return nullptr;
}

// Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs
// (netcore21/31 ETW callback payload): instances are constructed ONLY by
// the runtime during ETW tracing callbacks, which never fire in this
// runtime -- the ctor is a validated no-op and the getters answer the
// tracing-disabled defaults (Enabled=false, TraceLevel=0=Off).
void icall_tracing_status_changed_ctor(Il2CppObject*) {}
int32_t icall_tracing_status_changed_get_enabled(Il2CppObject*) { return 0; }
int32_t icall_tracing_status_changed_get_trace_level(Il2CppObject*) { return 0; }

// Interlocked.CompareExchange(ref int, int, int, out bool) (the netcore21/31
// success-indicator overload): the original value is returned AND 'found'
// reports whether the swap happened (location == comparand).
int32_t icall_interlocked_compare_exchange_found(int32_t* location, int32_t value,
                                                 int32_t comparand, int32_t* found)
{
    const int32_t original = il2cpp_interlocked_compare_exchange(location, value, comparand);
    if (found != nullptr) *found = (original == comparand) ? 1 : 0;
    return original;
}

// WeakReference.Finalize (netcore21/31): finalization is driven by the GC's
// finalizer machinery (icall_gc_finalize_trigger); the explicit Finalize
// icall surface is a validated no-op here.
void icall_weak_reference_finalize(Il2CppObject*) {}

// AppDomain.nCreateContext (netcore21): the single-domain model has no
// context infrastructure -- returns null (the "no context" answer).
Il2CppObject* icall_appdomain_n_create_context() { return nullptr; }

// Environment.GetVersion / GetVersionEx (net48): the OperatingSystem OBJECT
// return needs the OS/Version object model, which the simplified runtime
// does not carry (get_OSVersion serves the string surface) -- the documented
// PNSE contract.

void icall_environment_get_version_ex()
{
    il2cpp_throw_not_supported_exception(
        "Environment.GetVersionEx requires the OperatingSystem object model (use Environment.OSVersion for the string surface)");
}

// ReaderWriterLock.AnyWritersSince (net48): the legacy lock has no runtime
// model -- the constant false answer (no writer ever held it).
int32_t icall_reader_writer_lock_any_writers_since(int32_t)
{
    return 0;
}

// System.Number.DoubleToNumber / NumberToDouble (net48 internal formatting
// surface): NumberBuffer is internal (user code cannot construct it) -- the
// documented PNSE contract (see MethodMap.GC.cs DoubleToNumber).
void icall_number_double_to_number(double, int32_t, void*)
{
    il2cpp_throw_not_supported_exception(
        "System.Number formatting internals require the NumberBuffer infrastructure (not present in this runtime)");
}

double icall_number_number_to_double(void*)
{
    il2cpp_throw_not_supported_exception(
        "System.Number formatting internals require the NumberBuffer infrastructure (not present in this runtime)");
    return 0.0;
}

// RuntimeAssembly.LoadModule (net48): dynamic module emission has no runtime
// model (AOT static compilation) -- the documented PNSE contract.
void icall_assembly_loadmodule_unsupported()
{
    il2cpp_throw_not_supported_exception(
        "RuntimeAssembly.LoadModule requires dynamic module emission (not present in this runtime)");
}

// RuntimeAssembly.GetVersion (net48): the assembly version table is not
// carried (no version metadata model) -- the documented PNSE contract.
void icall_assembly_get_version_unsupported()
{
    il2cpp_throw_not_supported_exception(
        "RuntimeAssembly.GetVersion requires the assembly version table (not present in this runtime)");
}

// Win32.SetThreadToken (net48 QCall): thread impersonation tokens have no
// runtime model -- the documented PNSE contract. Zero call sites in
// mscorlib; the mapping exists to suppress the naked QCall decl, which
// collides with the 2-param Windows SDK SetThreadToken.
void icall_win32_set_thread_token_unsupported(int32_t)
{
    il2cpp_throw_not_supported_exception(
        "Win32.SetThreadToken requires thread impersonation tokens (not present in this runtime)");
}

// Security domain stubs (r126 Security residual - missing icall declarations)
void System_Reflection_SecurityContextFrame_Push_6486941fdb5c9d56(Il2CppObject* /*arg0*/)
{
    il2cpp_throw_not_supported_exception(
        "SecurityContextFrame.Push requires the security frame infrastructure (not present in this runtime)");
}

void System_Reflection_SecurityContextFrame_Pop_0e14d38423f93423()
{
    il2cpp_throw_not_supported_exception(
        "SecurityContextFrame.Pop requires the security frame infrastructure (not present in this runtime)");
}

void System_Runtime_CompilerServices_RuntimeHelpers_ProbeForSufficientStack_822160b172e6de22()
{
    // No-op: stack probe is a no-op in this runtime (no constrained execution region)
}

void System_Guid_NewGuid_03c02fe8f2eb79a9(Il2CppDateTime* ret)
{
    // Return a zero GUID (the runtime does not have access to the OS GUID generator)
    if (ret) {
        ret->ticks = 0;
    }
}

void System_Runtime_InteropServices_Marshal_CopyToManaged_462fd10303ea5155(Il2CppObject* /*arg0*/, int32_t /*arg1*/, Il2CppObject* /*arg2*/, Il2CppObject* /*arg3*/)
{
    il2cpp_throw_not_supported_exception(
        "Marshal.CopyToManaged requires the marshaling infrastructure (not present in this runtime)");
}

// Security P/Invoke stubs (declared in windows.h but not visible across TUs)
extern "C" int32_t ConvertSecurityDescriptorToStringSecurityDescriptorW(int32_t, int32_t, int32_t, int32_t, int32_t)
{
    return 0; // Not supported in this runtime
}

extern "C" int32_t GetNamedSecurityInfoW(const char16_t*, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t)
{
    return 0; // Not supported in this runtime
}

extern "C" int32_t GetSecurityInfo(int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t)
{
    return 0; // Not supported in this runtime
}

extern "C" int32_t SetNamedSecurityInfoW(const char16_t*, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t)
{
    return 0; // Not supported in this runtime
}

extern "C" int32_t SetSecurityInfo(int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t)
{
    return 0; // Not supported in this runtime
}

// Additional Security icall stubs (method bodies not generated by transpiler)
void System_Security_AccessControl_Win32_GetSecurityInfo_2be2e0a5f0a13616(Il2CppObject* /*arg0*/, Il2CppObject* /*arg1*/, Il2CppObject* /*arg2*/, Il2CppObject* /*arg3*/, int32_t /*arg4*/)
{
    il2cpp_throw_not_supported_exception(
        "Win32.GetSecurityInfo requires the Windows security API (not present in this runtime)");
}

}} // namespace il2cpp::runtime
