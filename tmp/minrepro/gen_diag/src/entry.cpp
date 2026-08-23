#include "generated.h"

// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Diagnostics.DebuggableAttribute")
il2cpp::Il2CppClass System_Diagnostics_DebuggableAttribute_class = {
    "System.Diagnostics.DebuggableAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.CompilationRelaxationsAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_CompilationRelaxationsAttribute_class = {
    "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.RefSafetyRulesAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_RefSafetyRulesAttribute_class = {
    "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.RuntimeCompatibilityAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class = {
    "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_class_register(&System_Diagnostics_DebuggableAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilationRelaxationsAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class);
    il2cpp::il2cpp_string_pool_fill();
    il2cpp::il2cpp_class_register(&Color_class);
    il2cpp::il2cpp_class_register(&Cloneable_class);
    // Enum metadata for Color (il2cpp_enum_register).
static const char* const s_Color_names[] = { "Red", "Green", "Blue" };
static const int64_t s_Color_values[] = { 0, 1, 2 };
static const il2cpp::runtime::Il2CppEnumInfo s_Color_enumInfo = { 9, 3, s_Color_names, s_Color_values };
il2cpp::runtime::il2cpp_enum_register("Color", &s_Color_enumInfo);

    il2cpp::il2cpp_typeinfo_register(&Program_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Color_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cloneable_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Program_Main_c087fde924c8c721();
    return 0;
}
