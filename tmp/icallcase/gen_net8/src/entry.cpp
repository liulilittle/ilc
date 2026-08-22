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
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.Runtime.CompilerServices.CompilerGeneratedAttribute")
il2cpp::Il2CppClass System_Runtime_CompilerServices_CompilerGeneratedAttribute_class = {
    "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
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
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilerGeneratedAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class);
    il2cpp::il2cpp_string_pool_fill();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__2_0_f4e3f49ba08c0670), "<Main>b__2_0");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__2_1_368886a4e3dbf3f5), "<Main>b__2_1");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__2_2_e026ec8c2ed91d7e), "<Main>b__2_2");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__2_3_55f29e934b37f083), "<Main>b__2_3");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&il2cpp::runtime::icall_string_parse_int), "Parse");
    il2cpp::il2cpp_class_register(&__c_class);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__O_sf__0___Parse));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__2_0));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__2_1));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__2_2));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__2_3));
    il2cpp::il2cpp_typeinfo_register(&Program_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails__typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__O_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__c_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_12_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_16_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_20_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    return Program_Main_c087fde924c8c721();
}
