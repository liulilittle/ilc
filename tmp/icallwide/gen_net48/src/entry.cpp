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
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__WideArray_b__12_0_df1e59d54d499122), "<WideArray>b__12_0");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__WideArray_b__12_1_55c36bdc6a60f807), "<WideArray>b__12_1");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__WideArray_b__12_2_80ffb1e50fa6f9e4), "<WideArray>b__12_2");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Program_DoubleToInt_448f9aef0096d5a6), "DoubleToInt");
    il2cpp::il2cpp_class_register(&Microsoft_CodeAnalysis_EmbeddedAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_class);
    il2cpp::il2cpp_class_register(&Color_class);
    il2cpp::il2cpp_class_register(&Cloneable_class);
    il2cpp::il2cpp_class_register(&Finalizable_class);
    il2cpp::il2cpp_class_register(&__c_class);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__O_sf__0___DoubleToInt));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__12_0));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__12_1));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__c_sf___9__12_2));
    il2cpp::il2cpp_gc_register_type_finalizer(&Finalizable_class, &Finalizable_Finalize_400e46cac37eabfb);
    // Enum metadata for Color (il2cpp_enum_register).
static const char* const s_Color_names[] = { "Red", "Green", "Blue" };
static const int64_t s_Color_values[] = { 0, 1, 2 };
static const il2cpp::runtime::Il2CppEnumInfo s_Color_enumInfo = { 9, 3, s_Color_names, s_Color_values };
il2cpp::runtime::il2cpp_enum_register("Color", &s_Color_enumInfo);

    il2cpp::il2cpp_typeinfo_register(&Microsoft_CodeAnalysis_EmbeddedAttribute_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&System_Runtime_CompilerServices_RefSafetyRulesAttribute_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Color_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cloneable_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Program_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails__typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Finalizable_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__O_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__c_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_3_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_12_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__StaticArrayInitTypeSize_20_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Program_Main_c087fde924c8c721();
    return 0;
}
