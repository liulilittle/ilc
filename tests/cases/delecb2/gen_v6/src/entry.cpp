#include "generated.h"

// M4 Task 7: the C API contract -- il2cpp_runtime_init() must
// leave the runtime fully usable: runtime bootstrap + export
// registrations + type registrations + Main-type .cctor. This
// generated implementation is what external hosts call.
IL2CPP_EXPORT void il2cpp_runtime_init(void) {
    il2cpp::runtime::runtime_init();
    il2cpp::il2cpp_string_pool_fill();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&__c__DisplayClass3_0__MakeAdder_b__0_e666332caf16eefa), "<MakeAdder>b__0");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_Delecb2_Inc_b736ebbfdb57ca77), "Inc");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_Delecb2_Dbl_196b0fb90e645f79), "Dbl");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_Accum_Add_641fd91f741ed664), "Add");
    il2cpp::il2cpp_class_register(&Cases_Accum_class);
    il2cpp::il2cpp_class_register(&__c__DisplayClass3_0_class);
    il2cpp::il2cpp_typeinfo_register(&Cases_Accum_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&__c__DisplayClass3_0_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_Delecb2_Main_f3548533330f7113();
    return 0;
}
