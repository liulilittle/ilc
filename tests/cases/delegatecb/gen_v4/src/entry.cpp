#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_DelegateCb_Triple_27f249b965fd0da2), "Triple");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_DelegateCb_Half_07b2153926bf72e1), "Half");
    il2cpp::il2cpp_typeinfo_register(&Cases_DelegateCb_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_DelegateCb_Main_9de44f1b34b0e166();
    return 0;
}
