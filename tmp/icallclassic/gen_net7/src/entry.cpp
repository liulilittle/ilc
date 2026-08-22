#include "generated.h"

// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Attribute")
il2cpp::Il2CppClass System_Attribute_class = {
    "System.Attribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.AttributeUsageAttribute")
il2cpp::Il2CppClass System_AttributeUsageAttribute_class = {
    "System.AttributeUsageAttribute",
    static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,
    &System_Object_class, nullptr, 0, nullptr, 0,
};
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
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.CompilerGeneratedAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_CompilerGeneratedAttribute_class = {
    "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
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
    il2cpp::il2cpp_class_register(&System_Attribute_class);
    il2cpp::il2cpp_class_register(&System_AttributeUsageAttribute_class);
    il2cpp::il2cpp_class_register(&System_Diagnostics_DebuggableAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilationRelaxationsAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilerGeneratedAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class);
    // D-ATTR-1 item c: custom attributes of 'Microsoft.CodeAnalysis.EmbeddedAttribute' (attribute.h registry)
    Il2CppObject* __il2cpp_attr_0 = il2cpp_object_new(&Microsoft_CodeAnalysis_EmbeddedAttribute_class);
    il2cpp::runtime::il2cpp_object_ctor_nop(__il2cpp_attr_0);
    static Il2CppObject* __il2cpp_attr_arr_0[] = { __il2cpp_attr_0 };
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__il2cpp_attr_arr_0[0]));
    il2cpp::runtime::il2cpp_attribute_register("Microsoft.CodeAnalysis.EmbeddedAttribute", __il2cpp_attr_arr_0, 1);
    il2cpp::il2cpp_string_pool_fill();
    il2cpp::il2cpp_class_register(&Microsoft_CodeAnalysis_EmbeddedAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_class);
    il2cpp::il2cpp_typeinfo_register(&Microsoft_CodeAnalysis_EmbeddedAttribute_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Program_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails__typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_12_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Program_Main_c087fde924c8c721();
    return 0;
}
