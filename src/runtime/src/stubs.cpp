// stubs.cpp -- Internal CLR helper stubs for remaining MethodInternalCall gaps.
// The ILC runtime reimplements BCL natively; these are stubs that return
// sensible defaults for methods the BCL's internal infrastructure calls.

#include "../include/il2cpp_api.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

// System.Array.GetCorElementTypeOfElementType
int32_t icall_array_get_cor_element_type_of_element_type(Il2CppObject* arr)
{
    return 4; // ELEMENT_TYPE_I4 (int32 default)
}

// System.GC.AllocateNewArray
Il2CppObject* icall_gc_allocate_new_array(int32_t length, int32_t elementType)
{
    return nullptr; // stub
}

// System.ComAwareWeakReference.HasInteropInfo
int32_t icall_com_aware_weak_reference_has_interop_info(Il2CppObject* self)
{
    return 0; // false
}

// System.Exception.SaveStackTracesFromDeepCopy
void icall_exception_save_stack_traces_from_deep_copy(Il2CppObject* self)
{
    // no-op
}

// System.Exception.GetExceptionCount
int32_t icall_exception_get_exception_count()
{
    return 0;
}

// System.Type.GetTypeFromHandleUnsafe
Il2CppObject* icall_type_get_type_from_handle_unsafe(intptr_t handle)
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
Il2CppObject* icall_stack_trace_get_stack_frames_internal(Il2CppObject* self, int32_t skipFrames, int32_t fNeedFileInfo)
{
    return nullptr; // empty array
}

// Number formatting stubs
Il2CppObject* icall_number_format_decimal(Il2CppObject* str, int32_t digits) { return nullptr; }
Il2CppObject* icall_number_format_double(double value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
Il2CppObject* icall_number_format_int32(int32_t value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
Il2CppObject* icall_number_format_uint32(uint32_t value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
Il2CppObject* icall_number_format_int64(int64_t value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
Il2CppObject* icall_number_format_uint64(uint64_t value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
Il2CppObject* icall_number_format_single(float value, Il2CppString* fmt, Il2CppObject* provider) { return nullptr; }
void icall_number_number_buffer_to_decimal(Il2CppObject* str, Il2CppObject* decimal) {}
double icall_number_number_buffer_to_double(Il2CppObject* str) { return 0.0; }
Il2CppObject* icall_number_format_number_buffer(int32_t options, int32_t numBase) { return nullptr; }

// ParseNumbers stubs
int64_t icall_parse_numbers_string_to_long(Il2CppString* str, int32_t radix, int32_t flags) { return 0; }
int32_t icall_parse_numbers_string_to_int(Il2CppString* str, int32_t radix, int32_t flags) { return 0; }
Il2CppObject* icall_parse_numbers_int_to_string(int32_t value, int32_t radix, int32_t flags) { return nullptr; }
Il2CppObject* icall_parse_numbers_long_to_string(int64_t value, int32_t radix, int32_t flags) { return nullptr; }

// CurrentSystemTimeZone stubs
int32_t icall_current_system_timezone_native_get_time_zone_minute_offset() { return 0; }
Il2CppObject* icall_current_system_timezone_native_get_daylight_name() { return nullptr; }
Il2CppObject* icall_current_system_timezone_native_get_standard_name() { return nullptr; }
Il2CppObject* icall_current_system_timezone_native_get_daylight_changes(int32_t year) { return nullptr; }

// CultureData stubs
int32_t icall_culture_data_lcid_to_locale_name(int32_t lcid) { return 0; }
int32_t icall_culture_data_locale_name_to_lcid(Il2CppString* name) { return 0; }
void icall_culture_data_native_init_culture_data(Il2CppObject* self) {}
Il2CppObject* icall_culture_data_native_get_number_format_info_values(Il2CppObject* self) { return nullptr; }

// CodeAccessSecurityEngine stubs
void icall_code_access_security_engine_special_demand(int32_t flag) {}
int32_t icall_code_access_security_engine_quick_check_for_all_demands() { return 1; }
int32_t icall_code_access_security_engine_all_domains_homogeneous_with_no_stack_modifiers() { return 1; }
int32_t icall_code_access_security_engine_check_n_return_so(int32_t flag) { return 0; }

// CompressedStack stubs
Il2CppObject* icall_compressed_stack_get_delayed_compressed_stack(Il2CppObject* self) { return nullptr; }
void icall_compressed_stack_destroy_delayed_compressed_stack(Il2CppObject* self) {}
void icall_compressed_stack_destroy_dcs_list(Il2CppObject* self) {}
int32_t icall_compressed_stack_get_dcs_count(Il2CppObject* self) { return 0; }

// DomainCompressedStack stubs
int32_t icall_domain_compressed_stack_create_domain_compressed_stack(Il2CppObject* self) { return 0; }
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_size(Il2CppObject* self) { return 0; }
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_flags(Il2CppObject* self) { return 0; }
Il2CppObject* icall_domain_compressed_stack_get_domain_compressed_stack_stack_blobs(Il2CppObject* self) { return nullptr; }

// ThreadPool stubs
int32_t icall_thread_pool_get_thread_count() { return 1; }
int32_t icall_thread_pool_get_min_threads() { return 1; }
int32_t icall_thread_pool_get_max_threads() { return 1; }
int32_t icall_thread_pool_get_available_threads() { return 1; }

// OverlappedData stubs
int32_t icall_overlapped_data_get_pending_count(Il2CppObject* self) { return 0; }
int32_t icall_overlapped_data_get_wait_handle(Il2CppObject* self) { return 0; }
int32_t icall_overlapped_data_get_error_code(Il2CppObject* self) { return 0; }

// Crypto/X509 stubs
Il2CppObject* icall_crypto_utils_create_csp(Il2CppObject* self) { return nullptr; }
Il2CppObject* icall_crypto_utils_decrypt_data(Il2CppObject* self, Il2CppObject* data) { return nullptr; }
Il2CppObject* icall_crypto_utils_encrypt_data(Il2CppObject* self, Il2CppObject* data) { return nullptr; }
Il2CppObject* icall_crypto_utils_export_key(Il2CppObject* self, Il2CppObject* password) { return nullptr; }
Il2CppObject* icall_crypto_utils_generate_key(int32_t keySize) { return nullptr; }
int32_t icall_crypto_utils_get_enforce_fips_policy_setting() { return 0; }
Il2CppObject* icall_crypto_utils_get_key_parameter(Il2CppObject* self, Il2CppString* name) { return nullptr; }
Il2CppObject* icall_crypto_utils_get_key_set_security_info(Il2CppObject* self) { return nullptr; }
Il2CppObject* icall_crypto_utils_get_provider_parameter(Il2CppObject* self, Il2CppString* name) { return nullptr; }
Il2CppObject* icall_crypto_utils_get_user_key(Il2CppObject* self) { return nullptr; }
Il2CppObject* icall_crypto_utils_import_bulk_key(Il2CppObject* self, Il2CppObject* data) { return nullptr; }
Il2CppObject* icall_crypto_utils_import_csp_blob(Il2CppObject* self, Il2CppObject* data) { return nullptr; }
Il2CppObject* icall_crypto_utils_import_key(Il2CppObject* self, Il2CppObject* data) { return nullptr; }
int32_t icall_crypto_utils_produce_legacy_hmac_values() { return 0; }
Il2CppObject* icall_crypto_utils_open_csp(Il2CppObject* self, int32_t flags) { return nullptr; }
Il2CppObject* icall_crypto_utils_acquire_csp(Il2CppObject* self) { return nullptr; }
Il2CppObject* icall_x509_utils_add_certificate_to_store(Il2CppObject* self, Il2CppObject* cert) { return nullptr; }
Il2CppObject* icall_x509_utils_export_certificates_to_blob(Il2CppObject* self, Il2CppObject* password) { return nullptr; }
Il2CppObject* icall_x509_utils_get_cert_raw_data(Il2CppObject* self) { return nullptr; }
Il2CppObject* icall_x509_utils_get_date_not_after(Il2CppObject* self) { return nullptr; }

// AppDomain stubs
Il2CppObject* icall_appdomain_is_unloading_forced_finalize() { return nullptr; }
int32_t icall_appdomain_is_finalizing_for_unload() { return 0; }
Il2CppObject* icall_appdomain_publish_anonymously_hosted_dynamic_methods_assembly() { return nullptr; }
int32_t icall_appdomain_is_domain_id_valid(int32_t id) { return 0; }
Il2CppObject* icall_appdomain_get_default_domain() { return nullptr; }
Il2CppObject* icall_appdomain_get_fusion_context() { return nullptr; }
Il2CppObject* icall_appdomain_get_security_descriptor() { return nullptr; }
int32_t icall_appdomain_n_create_domain(Il2CppString* name, Il2CppObject* info, Il2CppObject* evidence) { return 0; }
Il2CppObject* icall_appdomain_n_create_instance(int32_t id, Il2CppString* typeName) { return nullptr; }
void icall_appdomain_update_loader_optimization(int32_t optimization) {}
Il2CppObject* icall_appdomain_is_string_interned(Il2CppString* str) { return nullptr; }
Il2CppObject* icall_appdomain_get_or_intern_string(Il2CppString* str) { return nullptr; }
void icall_appdomain_n_unload(int32_t id) {}
int32_t icall_appdomain_get_id() { return 0; }

}} // namespace il2cpp::runtime
