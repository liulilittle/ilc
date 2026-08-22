#pragma once
// stubs.h -- Internal CLR helper stub declarations (see stubs.cpp).
// Self-contained: pulls the API types via a header-relative path (resolves
// from ANY including TU -- generated code lives outside src/runtime).

#include "../il2cpp_api.h"

#include <cstdint>

namespace il2cpp {
namespace runtime {

int32_t icall_array_get_cor_element_type_of_element_type(Il2CppObject* arr);
Il2CppObject* icall_gc_allocate_new_array(int32_t length, int32_t elementType);
int32_t icall_com_aware_weak_reference_has_interop_info(Il2CppObject* self);
void icall_exception_save_stack_traces_from_deep_copy(Il2CppObject* self);
int32_t icall_exception_get_exception_count();
Il2CppObject* icall_type_get_type_from_handle_unsafe(intptr_t handle);
int64_t icall_jit_info_get_compiled_il_bytes();
int64_t icall_jit_info_get_compiled_method_count();
Il2CppObject* icall_assembly_load_context_get_loaded_assemblies();
int32_t icall_assembly_load_context_is_tracing_enabled();
Il2CppObject* icall_stack_trace_get_stack_frames_internal(Il2CppObject* self, int32_t skipFrames, int32_t fNeedFileInfo);

// Number formatting stubs
Il2CppObject* icall_number_format_decimal(Il2CppObject* str, int32_t digits);
Il2CppObject* icall_number_format_double(double value, Il2CppString* fmt, Il2CppObject* provider);
Il2CppObject* icall_number_format_int32(int32_t value, Il2CppString* fmt, Il2CppObject* provider);
Il2CppObject* icall_number_format_uint32(uint32_t value, Il2CppString* fmt, Il2CppObject* provider);
Il2CppObject* icall_number_format_int64(int64_t value, Il2CppString* fmt, Il2CppObject* provider);
Il2CppObject* icall_number_format_uint64(uint64_t value, Il2CppString* fmt, Il2CppObject* provider);
Il2CppObject* icall_number_format_single(float value, Il2CppString* fmt, Il2CppObject* provider);
void icall_number_number_buffer_to_decimal(Il2CppObject* str, Il2CppObject* decimal);
double icall_number_number_buffer_to_double(Il2CppObject* str);
Il2CppObject* icall_number_format_number_buffer(int32_t options, int32_t numBase);

// ParseNumbers stubs
int64_t icall_parse_numbers_string_to_long(Il2CppString* str, int32_t radix, int32_t flags);
int32_t icall_parse_numbers_string_to_int(Il2CppString* str, int32_t radix, int32_t flags);
Il2CppObject* icall_parse_numbers_int_to_string(int32_t value, int32_t radix, int32_t flags);
Il2CppObject* icall_parse_numbers_long_to_string(int64_t value, int32_t radix, int32_t flags);

// CurrentSystemTimeZone stubs
int32_t icall_current_system_timezone_native_get_time_zone_minute_offset();
Il2CppObject* icall_current_system_timezone_native_get_daylight_name();
Il2CppObject* icall_current_system_timezone_native_get_standard_name();
Il2CppObject* icall_current_system_timezone_native_get_daylight_changes(int32_t year);

// CultureData stubs
int32_t icall_culture_data_lcid_to_locale_name(int32_t lcid);
int32_t icall_culture_data_locale_name_to_lcid(Il2CppString* name);
void icall_culture_data_native_init_culture_data(Il2CppObject* self);
Il2CppObject* icall_culture_data_native_get_number_format_info_values(Il2CppObject* self);

// CodeAccessSecurityEngine stubs
void icall_code_access_security_engine_special_demand(int32_t flag);
int32_t icall_code_access_security_engine_quick_check_for_all_demands();
int32_t icall_code_access_security_engine_all_domains_homogeneous_with_no_stack_modifiers();
int32_t icall_code_access_security_engine_check_n_return_so(int32_t flag);

// CompressedStack stubs
Il2CppObject* icall_compressed_stack_get_delayed_compressed_stack(Il2CppObject* self);
void icall_compressed_stack_destroy_delayed_compressed_stack(Il2CppObject* self);
void icall_compressed_stack_destroy_dcs_list(Il2CppObject* self);
int32_t icall_compressed_stack_get_dcs_count(Il2CppObject* self);

// DomainCompressedStack stubs
int32_t icall_domain_compressed_stack_create_domain_compressed_stack(Il2CppObject* self);
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_size(Il2CppObject* self);
int32_t icall_domain_compressed_stack_get_domain_compressed_stack_flags(Il2CppObject* self);
Il2CppObject* icall_domain_compressed_stack_get_domain_compressed_stack_stack_blobs(Il2CppObject* self);

// ThreadPool stubs
int32_t icall_thread_pool_get_thread_count();
int32_t icall_thread_pool_get_min_threads();
int32_t icall_thread_pool_get_max_threads();
// net6/net7: CanSetMin/MaxIOCompletionThreads -- constant false (no IOCP model).
bool icall_thread_pool_can_set_io_threads();
int32_t icall_thread_pool_get_available_threads();

// OverlappedData stubs
int32_t icall_overlapped_data_get_pending_count(Il2CppObject* self);
int32_t icall_overlapped_data_get_wait_handle(Il2CppObject* self);
int32_t icall_overlapped_data_get_error_code(Il2CppObject* self);

// Crypto/X509 stubs
Il2CppObject* icall_crypto_utils_create_csp(Il2CppObject* self);
Il2CppObject* icall_crypto_utils_decrypt_data(Il2CppObject* self, Il2CppObject* data);
Il2CppObject* icall_crypto_utils_encrypt_data(Il2CppObject* self, Il2CppObject* data);
Il2CppObject* icall_crypto_utils_export_key(Il2CppObject* self, Il2CppObject* password);
Il2CppObject* icall_crypto_utils_generate_key(int32_t keySize);
int32_t icall_crypto_utils_get_enforce_fips_policy_setting();
Il2CppObject* icall_crypto_utils_get_key_parameter(Il2CppObject* self, Il2CppString* name);
Il2CppObject* icall_crypto_utils_get_key_set_security_info(Il2CppObject* self);
Il2CppObject* icall_crypto_utils_get_provider_parameter(Il2CppObject* self, Il2CppString* name);
Il2CppObject* icall_crypto_utils_get_user_key(Il2CppObject* self);
Il2CppObject* icall_crypto_utils_import_bulk_key(Il2CppObject* self, Il2CppObject* data);
Il2CppObject* icall_crypto_utils_import_csp_blob(Il2CppObject* self, Il2CppObject* data);
Il2CppObject* icall_crypto_utils_import_key(Il2CppObject* self, Il2CppObject* data);
int32_t icall_crypto_utils_produce_legacy_hmac_values();
Il2CppObject* icall_crypto_utils_open_csp(Il2CppObject* self, int32_t flags);
Il2CppObject* icall_crypto_utils_acquire_csp(Il2CppObject* self);
Il2CppObject* icall_x509_utils_add_certificate_to_store(Il2CppObject* self, Il2CppObject* cert);
Il2CppObject* icall_x509_utils_export_certificates_to_blob(Il2CppObject* self, Il2CppObject* password);
Il2CppObject* icall_x509_utils_get_cert_raw_data(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_date_not_after(Il2CppObject* self);

// AppDomain stubs
Il2CppObject* icall_appdomain_is_unloading_forced_finalize();
int32_t icall_appdomain_is_finalizing_for_unload();
Il2CppObject* icall_appdomain_publish_anonymously_hosted_dynamic_methods_assembly();
int32_t icall_appdomain_is_domain_id_valid(int32_t id);
Il2CppObject* icall_appdomain_get_default_domain();
Il2CppObject* icall_appdomain_get_fusion_context();
Il2CppObject* icall_appdomain_get_security_descriptor();
int32_t icall_appdomain_n_create_domain(Il2CppString* name, Il2CppObject* info, Il2CppObject* evidence);
Il2CppObject* icall_appdomain_n_create_instance(int32_t id, Il2CppString* typeName);
void icall_appdomain_update_loader_optimization(int32_t optimization);
Il2CppObject* icall_appdomain_is_string_interned(Il2CppString* str);
Il2CppObject* icall_appdomain_get_or_intern_string(Il2CppString* str);
void icall_appdomain_n_unload(int32_t id);
int32_t icall_appdomain_get_id();

// Remaining X509 stubs
Il2CppObject* icall_x509_utils_get_date_not_before(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_issuer_name(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_public_key_oid(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_public_key_parameters(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_public_key_value(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_subject_info(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_serial_number(Il2CppObject* self);
Il2CppObject* icall_x509_utils_get_thumbprint(Il2CppObject* self);
int32_t icall_x509_utils_query_cert_blob_type(Il2CppObject* self);
int32_t icall_x509_utils_query_cert_file_type(Il2CppString* path);

// Remaining Environment stubs
Il2CppObject* icall_environment_get_environment_variable_native(Il2CppString* name);
int32_t icall_environment_get_version();
int32_t icall_environment_get_version_ex();

// Remaining DateTime stubs
int64_t icall_datetime_get_system_time_as_file_time();
int32_t icall_datetime_validate_system_time(int64_t ticks);
int64_t icall_datetime_get_system_time_with_leap_seconds_handling();
int64_t icall_datetime_system_file_time_to_system_time(int64_t ticks);
int64_t icall_datetime_system_time_to_system_file_time(int64_t ticks);

// Remaining String stubs
int32_t icall_string_native_compare_ordinal_ex(Il2CppObject* str1, Il2CppObject* str2, int32_t offset1, int32_t count1, int32_t offset2, int32_t count2);
int32_t icall_string_native_compare_ordinal_ignore_case_wc(Il2CppString* str, Il2CppString* value);
Il2CppObject* icall_string_internal_marvin32_hash_string(Il2CppString* str);

// Remaining Environment stubs
void icall_environment_fail_fast(Il2CppObject* message);

// Remaining miscellaneous stubs
Il2CppObject* icall_object_memberwise_clone(Il2CppObject* self);
Il2CppObject* icall_array_get_data_ptr_offset_internal(Il2CppObject* arr);
Il2CppObject* icall_string_is_fast_sort(Il2CppString* str);
int32_t icall_string_is_ascii(Il2CppString* str);
void icall_string_set_trail_byte(Il2CppObject* self, uint8_t value);
bool icall_string_try_get_trail_byte(Il2CppObject* self, uint8_t* result);
int32_t icall_delegate_internal_equal_types(Il2CppObject* d1, Il2CppObject* d2);
int32_t icall_delegate_compare_unmanaged_function_ptrs(Il2CppObject* d1, Il2CppObject* d2);
void icall_buffer_internal_block_copy(Il2CppObject* src, int32_t srcOffset, Il2CppObject* dst, int32_t dstOffset, int32_t count);
Il2CppObject* icall_base_config_handler_run_parser();
int32_t icall_decimal_fcall_to_int32(Il2CppObject* d);
int32_t icall_default_binder_can_convert_primitive_object_to_type(Il2CppObject* obj, int32_t targetType);
Il2CppObject* icall_enum_internal_get_underlying_type(Il2CppClass* enumType);
Il2CppObject* icall_app_domain_setup_update_context_property(Il2CppObject* self, Il2CppString* name, Il2CppObject* value);
Il2CppObject* icall_marshal_by_ref_object_get_com_i_unknown(Il2CppObject* self);
Il2CppObject* icall_missing_member_exception_format_signature(Il2CppString* str);
int32_t icall_type_op_inequality(Il2CppObject* t1, Il2CppObject* t2);
int32_t icall_value_type_get_hash_code_of_ptr(void* ptr);
int32_t icall_clr_config_check_legacy_managed_deflate_stream();
int32_t icall_clr_config_check_throw_unobserved_task_exceptions();
int32_t icall_security_context_get_impersonation_flow_mode();
int32_t icall_security_context_is_default_thread_security_info();
void icall_security_manager_set_thread_security(int32_t flag);
Il2CppObject* icall_security_runtime_get_security_object_for_frame();
Il2CppObject* icall_security_principal_win32_open_thread_token();
int32_t icall_globalization_calendar_data_native_get_two_digit_year_max(int32_t calendarId);
Il2CppObject* icall_globalization_calendar_data_native_get_calendars(int32_t calendarId);
Il2CppObject* icall_globalization_culture_info_native_get_locale_info_ex(int32_t lcid);

// Remaining Thread/Interlocked stubs
void icall_synchronization_context_wait_helper(int32_t millisecondsTimeout);
int32_t icall_compressed_stack_is_immediate_completion_candidate();
Il2CppObject* icall_compressed_stack_get_domain_compressed_stack();
Il2CppObject* icall_compressed_stack_get_homogeneous_pls();
int32_t icall_domain_compressed_stack_get_desc_count();
Il2CppObject* icall_domain_compressed_stack_get_domain_permission_sets();
Il2CppObject* icall_domain_compressed_stack_get_descriptor_info();
int32_t icall_domain_compressed_stack_ignore_domain(int32_t id);
void icall_host_execution_context_manager_release_host_security_context();
Il2CppObject* icall_host_execution_context_manager_clone_host_security_context();
Il2CppObject* icall_overlapped_data_free_native_overlapped();
Il2CppObject* icall_overlapped_data_get_overlapped_from_native();
int32_t icall_overlapped_data_check_vm_for_io_packet();
void icall_thread_pool_notify_work_item_complete();
void icall_thread_pool_report_thread_status(int32_t status);
void icall_thread_pool_notify_work_item_progress_native();
int32_t icall_thread_pool_is_thread_pool_hosted();
void icall_thread_pool_clear_notify();
void icall_threadobj_spin_wait(int32_t iterations);
// net9/10 E2E campaign (environment sweep): 64-bit probes (stubs.cpp).
int32_t icall_environment_get_is_64bit_process();
int32_t icall_environment_get_is_64bit_operating_system();

// Remaining Environment/Calendar/CultureInfo/EncodingTable/Diagnostics stubs
int32_t icall_environment_get_resource_from_default(Il2CppString* name, Il2CppString* value);
int32_t icall_environment_get_compatibility_flag(Il2CppString* flag);
int32_t icall_calendar_data_native_get_two_digit_year_max(int32_t calendarId);
int32_t icall_calendar_data_native_get_calendars(int32_t calendarId, int32_t* calendars);
int32_t icall_culture_info_native_get_locale_info_ex_int(int32_t lcid);
void icall_culture_info_native_set_thread_locale(int32_t lcid);
Il2CppObject* icall_encoding_table_native_create_open_file_mapping();
void icall_assert_show_default_assert_dialog();
void icall_log_add_log_switch(Il2CppString* name, int32_t level);
void icall_log_modify_log_switch(Il2CppString* name, int32_t level);

} // namespace runtime
} // namespace il2cpp
