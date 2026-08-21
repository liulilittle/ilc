#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_class_register(&Shape_class);
    il2cpp::il2cpp_class_register(&Rect_class);
    il2cpp::il2cpp_class_register(&Square_class);
    il2cpp::il2cpp_class_register(&Tri_class);
    il2cpp::il2cpp_typeinfo_register(&Shape_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Rect_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Square_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Tri_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_Virtual_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_Virtual_Main_4c989bc248904a59();
    return 0;
}
