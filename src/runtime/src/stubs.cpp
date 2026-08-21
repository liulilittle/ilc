// stubs.cpp -- Internal CLR helper stubs for remaining MethodInternalCall gaps.
// The ILC runtime reimplements BCL natively; these are stubs that return
// sensible defaults for methods the BCL's internal infrastructure calls.

#include "../include/il2cpp_api.h"
#include "il2cpp_object.h"

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

}} // namespace il2cpp::runtime
