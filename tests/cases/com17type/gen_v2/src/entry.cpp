#include "generated.h"

// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Diagnostics.DebuggableAttribute")
il2cpp::Il2CppClass System_Diagnostics_DebuggableAttribute_class = {
    "System.Diagnostics.DebuggableAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyCompanyAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyCompanyAttribute_class = {
    "System.Reflection.AssemblyCompanyAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyConfigurationAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyConfigurationAttribute_class = {
    "System.Reflection.AssemblyConfigurationAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyFileVersionAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyFileVersionAttribute_class = {
    "System.Reflection.AssemblyFileVersionAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyInformationalVersionAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyInformationalVersionAttribute_class = {
    "System.Reflection.AssemblyInformationalVersionAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyProductAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyProductAttribute_class = {
    "System.Reflection.AssemblyProductAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Reflection.AssemblyTitleAttribute")
il2cpp::Il2CppClass System_Reflection_AssemblyTitleAttribute_class = {
    "System.Reflection.AssemblyTitleAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.CompilationRelaxationsAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_CompilationRelaxationsAttribute_class = {
    "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.RuntimeCompatibilityAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class = {
    "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.Versioning.TargetFrameworkAttribute")
il2cpp::Il2CppClass System_Runtime_Versioning_TargetFrameworkAttribute_class = {
    "System.Runtime.Versioning.TargetFrameworkAttribute",
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
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyCompanyAttribute_class);
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyConfigurationAttribute_class);
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyFileVersionAttribute_class);
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyInformationalVersionAttribute_class);
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyProductAttribute_class);
    il2cpp::il2cpp_class_register(&System_Reflection_AssemblyTitleAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilationRelaxationsAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_Versioning_TargetFrameworkAttribute_class);
    il2cpp::il2cpp_string_pool_fill();
    il2cpp::il2cpp_class_register(&DragDropEffects_class);
    il2cpp::il2cpp_class_register(&DragEventArgs_class);
    il2cpp::il2cpp_class_register(&IDropTarget_class);
    il2cpp::il2cpp_class_register(&WfDropSource_class);
    il2cpp::il2cpp_class_register(&WfDropSource2_class);
    il2cpp::il2cpp_class_register(&IDataObject_class);
    il2cpp::il2cpp_class_register(&WfDataObject_class);
    il2cpp::il2cpp_class_register(&WfDataObject2_class);
    // Enum metadata for DragDropEffects (il2cpp_enum_register).
static const char* const s_DragDropEffects_names[] = { "None", "Copy", "Move", "Link" };
static const int64_t s_DragDropEffects_values[] = { 0, 1, 2, 4 };
static const il2cpp::runtime::Il2CppEnumInfo s_DragDropEffects_enumInfo = { 9, 4, s_DragDropEffects_names, s_DragDropEffects_values };
il2cpp::runtime::il2cpp_enum_register("DragDropEffects", &s_DragDropEffects_enumInfo);

    il2cpp::il2cpp_typeinfo_register(&DragDropEffects_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&DragEventArgs_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&IDropTarget_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&WfDropSource_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&WfDropSource2_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&IDataObject_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&WfDataObject_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&WfDataObject2_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_Com17type_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_Com17type_Main_8be1bf8b869ca35b();
    return 0;
}
