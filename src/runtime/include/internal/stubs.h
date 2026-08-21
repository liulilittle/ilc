#pragma once
// stubs.h -- Internal CLR helper stub declarations (see stubs.cpp).

#include "il2cpp_api.h"

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

}} // namespace runtime
} // namespace il2cpp
