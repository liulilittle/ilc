#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_string_pool_fill();
    il2cpp::il2cpp_typeinfo_register(&Cases_ConvCase_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_ConvCase_Main_be5f1e94bc88ed96();
    return 0;
}
