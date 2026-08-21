#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_string_pool_fill();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_0_a4fe144d4a16ce8f), "<Main>b__0_0");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_1_ada79d4d4efee504), "<Main>b__0_1");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_2_b6876a4d5414fe75), "<Main>b__0_2");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_3_be57734d58444aaa), "<Main>b__0_3");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_4_828e484d36a499b3), "<Main>b__0_4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_5_8b38114d3b8d1ce8), "<Main>b__0_5");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_6_9307be4d3fbbccc9), "<Main>b__0_6");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_7_9b7ae74d4475b0ae), "<Main>b__0_7");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_8_e9a6dc4d70cc4767), "<Main>b__0_8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_9_f250054d75b3babc), "<Main>b__0_9");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__Main_b__0_10_e265f35db0b3dd82), "<Main>b__0_10");
    il2cpp::il2cpp_class_register(&__c_class);
    il2cpp::il2cpp_typeinfo_register(&Cases_Excext_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__c_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_Excext_Main_9d39cbb12050bea3();
    return 0;
}
