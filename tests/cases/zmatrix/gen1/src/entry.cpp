#include "generated.h"

// G1 arch interlock: layout pointer width = 8 (target: host).
static_assert(sizeof(void*) == 8, "il2cppc G1 interlock: generated layout assumes 8-byte pointers (source compiles as 64-bit); rebuild for the matching target (--arch) or the object layout/GC bitmap drifts");

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
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_CompilerGeneratedAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_CompilerServices_RuntimeCompatibilityAttribute_class);
    il2cpp::il2cpp_class_register(&System_Runtime_Versioning_TargetFrameworkAttribute_class);
    il2cpp::il2cpp_string_pool_fill();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_i4_320c11aa95252893), "P_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_i4_d966743a5c54e463), "PI_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_i4_61fb360e7a5fa812), "S_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_i4_f01feb0d071a1670), "SI_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_i4_8e01a74181a47cea), "K_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_i4_1116f527d648d278), "Z_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_i4_750debf1b1d9d7c5), "A_i4");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_i8_db10de0fd8ba1b9e), "P_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_i8_d985a5dd829d2556), "PI_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_i8_b7c7ca2e996dea37), "S_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_i8_3db2300e97dfa7c5), "SI_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_i8_e48fdadc3db2b7ff), "K_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_i8_bae3430e39b691e4), "Z_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_i8_c584f069c8f3cea1), "A_i8");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_f_232d3e33f33b6111), "P_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_f_8d29cf52aa28dfaf), "PI_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_f_d61d0a9791e0f146), "S_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_f_cf46aff4edf9f3f2), "SI_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_f_141e7d0fee708ccd), "Z_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_f_8a2ce7161ed8ad1a), "A_f");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_d_37e34a4364e83221), "P_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_d_49042c55a5ba262f), "PI_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_d_4a4b2e886d419ce6), "S_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_d_5b0b4683af1d55b2), "SI_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_d_67a3fbeb1f9c58db), "Z_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_d_e73dcfe243f0df5c), "A_d");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_by_2d4f00795a49d706), "P_by");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_by_7d874a457a950740), "PI_by");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_by_c2b626bdb081db99), "S_by");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_by_a4a47552d144b375), "SI_by");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_by_fc86890a3167f9fd), "A_by");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_sb_71ebf079308f9242), "P_sb");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_sb_3ffdb3af1adb3308), "PI_sb");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_sb_8c2d26520ce5c901), "S_sb");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_sb_3bb00855653d993d), "SI_sb");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_sb_337dca6bf19ad47d), "A_sb");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_sh_3bc000208d212d0b), "P_sh");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_sh_061cedcbbfb9b3c5), "PI_sh");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_sh_b5199352ce35a0f4), "S_sh");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_sh_68b87d9e1149438c), "SI_sh");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_sh_f4d90efdb0813bc5), "A_sh");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_us_b765fa1f1ec56bdc), "P_us");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_us_8802c907c6be0f0e), "PI_us");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_us_dfed1898d419d7ef), "S_us");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_us_5d9bf6cb09b467bf), "SI_us");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_us_42e082e970062280), "A_us");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_ui_b1ccccbe0f1bf276), "P_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_ui_d02a89282e73acbe), "PI_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_ui_46040e420112526f), "S_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_ui_4e2161325ec3387d), "SI_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_ui_caf2e3cfedb6ff65), "Z_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_ui_9c846ff9da981f70), "A_ui");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_ul_5c5e0ee80cb990c7), "P_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_ul_2150f6e2b31efe87), "PI_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_ul_9e6c2c1805fbae5e), "S_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_ul_81c487f446991c84), "SI_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_ul_910ec8fa7f53ecc6), "K_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_Z_ul_c2ca37e5e359d9f4), "Z_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_ul_3223849f01efd6a9), "A_ul");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_bo_51144b3fc9aa6856), "P_bo");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_bo_07882e85cd14caee), "PI_bo");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_bo_6600807b2c81924f), "S_bo");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_bo_3c9634a157e51fc9), "SI_bo");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_bo_1817e7602d2e73cf), "A_bo");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_ch_9c0f1cb433245647), "P_ch");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_PI_ch_fed0d17143f5470b), "PI_ch");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_ch_c0d928cd057213ea), "S_ch");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_SI_ch_45233ff4d7f34684), "SI_ch");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_ch_bff9a66869977a6b), "A_ch");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_dec_265e987407d7cc7d), "P_dec");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_dec_4be6725c39ed9060), "S_dec");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_dec_03187e1b758adca8), "K_dec");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_dt_8880eeca860cf471), "P_dt");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_dt_dd15110a014a9dcc), "K_dt");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_ts_590aae53b8c86732), "P_ts");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_ts_d28f4dd665fe84cb), "K_ts");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_gd_d7d2bf29558f2548), "P_gd");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_gd_dbb23c8e7f529675), "K_gd");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_P_sr_aa6152df0ac450a2), "P_sr");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_S_sr_7140ed58f370b313), "S_sr");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_K_sr_ff09713098a62a4b), "K_sr");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_ZMatrix_A_sr_3aa3a1838e9e91bd), "A_sr");
    il2cpp::il2cpp_typeinfo_register(&Cases_ZMatrix_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails__typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails____StaticArrayInitTypeSize_3_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails____StaticArrayInitTypeSize_6_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails____StaticArrayInitTypeSize_12_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&_PrivateImplementationDetails____StaticArrayInitTypeSize_24_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_ZMatrix_Main_a47482b619c2d2c9();
    return 0;
}
