#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_string_pool_fill();
    il2cpp::il2cpp_class_register(&MyException_class);
    il2cpp::il2cpp_class_register(&MyException2_class);
    il2cpp::il2cpp_typeinfo_register(&Cases_Exdeep_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&MyException_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&MyException2_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_Exdeep_Main_5a534244c371d78d();
    return 0;
}
