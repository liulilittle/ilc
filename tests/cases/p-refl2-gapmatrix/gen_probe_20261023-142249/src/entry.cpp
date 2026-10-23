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
// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType("System.SerializableAttribute")
il2cpp::Il2CppClass System_SerializableAttribute_class = {
    "System.SerializableAttribute",
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
    il2cpp::il2cpp_class_register(&System_SerializableAttribute_class);
    il2cpp::il2cpp_string_pool_fill();
    // CG6: register delegate-created method SHORT names (Delegate.Method.Name)
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A01_651ee362b9275ffb), "A01");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A02_802b6262c8c6a6f6), "A02");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A03_774b9962c3b09451), "A03");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A04_90a49862d1dd6d4c), "A04");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A05_88d4ef62cdaec437), "A05");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A06_a29aee62dc386f12), "A06");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A07_9917c562d69757fd), "A07");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A08_298ca46297b6cf78), "A08");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A09_20099b629215eec3), "A09");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A10_b8d4a259cb07e415), "A10");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A11_c257ab59d0a8c4ca), "A11");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A12_a74b4c59c109b42f), "A12");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A13_aff4d559c5f1caa4), "A13");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A14_97087659b8212069), "A14");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A15_9f7b9f59bcdb044e), "A15");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A16_84dc0059ad9858d3), "A16");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A17_8d854959b2800288), "A17");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A18_fdea6a59f21a2ecd), "A18");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A19_06939359f701a222), "A19");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A20_044e0450d85d6e5e), "A20");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A21_fb015b50d2eac039), "A21");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A22_f257ae50ce026c98), "A22");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A23_ea87a550c9d32063), "A23");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A24_2650d050eb72d15a), "A24");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A25_1da70750e68a4e25), "A25");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A26_14c77a50e174a174), "A26");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A27_0cf73150dd44e87f), "A27");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A28_48f66c50ff124416), "A28");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A29_4016a350f9fc3171), "A29");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A30_a4178a4a5954dff3), "A30");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A31_acc1534a5e3d6328), "A31");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A32_b491004a626c1309), "A32");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A33_bd04294a6725f6ee), "A33");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A34_c687564a6cc714cf), "A34");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A35_cf30df4a71af2b44), "A35");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A36_d810ac4a76c544b5), "A36");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A37_dfe0b54a7af490ea), "A37");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A38_e853d24a7fae606b), "A38");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A39_f1d67b4a854e9e00), "A39");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A40_d883ac8379e67824), "A40");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A41_cfda238374fe61af), "A41");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A42_eae68283849d724a), "A42");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A43_e16379837efc9195), "A43");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A44_b61420836674b008), "A44");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A45_ad6ad783618d0653), "A45");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A46_c80a768370cfb1ce), "A46");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A47_bf974d836c15cde9), "A47");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A48_1edf9483a20dbbdc), "A48");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A49_1635eb839d256f07), "A49");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A50_2390327a86df6db9), "A50");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A51_2cdcdb7a8c521bde), "A51");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_A52_13167c7a7dc7cde3), "A52");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B01_6e209b976ab7b80e), "B01");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B02_5380fc975b750c93), "B02");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B03_5c2a4597605cb648), "B03");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B04_87799e9778e497d5), "B04");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B05_90fca7977e85788a), "B05");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B06_75f048976ee667ef), "B06");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B07_7e99d19773ce7e64), "B07");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B08_a9e93a978c567b21), "B08");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B09_b2c94397916cfa86), "B09");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B10_1aa0eca05904de94), "B10");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B11_111e23a053646a9f), "B11");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B12_2c2a42a063030e7a), "B12");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B13_2380f9a05e1b64c5), "B13");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B14_f831a0a045938338), "B14");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B15_eeae97a03ff2a283), "B15");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B16_087476a04e7c16fe), "B16");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B17_00dacda04a7afd59), "B17");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B18_5f4994a07fba210c), "B18");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B19_5779eba07b8b77f7), "B19");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B20_0faf0a861bd969d7), "B20");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_B21_19323386217a80ec), "B21");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C01_67f84bcca43583a1), "C01");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C02_5f4efecc9f4dd320), "C02");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C03_577ed5cc9b1e508b), "C03");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C04_9347a0ccbcbd5e62), "C04");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C05_8a9e77ccb7d5eb0d), "C05");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C06_8304caccb3d4ca9c), "C06");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C07_7a5b21ccaeec7dc7), "C07");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C08_2c2faccc8296c08e), "C08");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_C09_23bc83cc7ddcdca9), "C09");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_D01_70208401550d1174), "D01");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_D02_790011015a22be25), "D02");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_E01_6db2100b74939157), "E01");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_E02_87780f0b831d3c32), "E02");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_E03_7fa8660b7eee931d), "E03");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&Cases_PRefl2Matrix_E04_5458ad0b66660e70), "E04");
    il2cpp::il2cpp_class_register(&Cases_IGap_class);
    il2cpp::il2cpp_class_register(&Cases_GapBase_class);
    il2cpp::il2cpp_class_register(&Cases_GapDerived_class);
    il2cpp::il2cpp_class_register(&Cases_GapEnum_class);
    il2cpp::il2cpp_class_register(&Cases_GapStruct_class);
    // Enum metadata for Cases.GapEnum (il2cpp_enum_register).
static const char* const s_Cases_GapEnum_names[] = { "A", "B" };
static const int64_t s_Cases_GapEnum_values[] = { 0, 1 };
static const il2cpp::runtime::Il2CppEnumInfo s_Cases_GapEnum_enumInfo = { 9, 2, s_Cases_GapEnum_names, s_Cases_GapEnum_values, 0 };
il2cpp::runtime::il2cpp_enum_register("Cases.GapEnum", &s_Cases_GapEnum_enumInfo);

    il2cpp::il2cpp_typeinfo_register(&Cases_IGap_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_GapBase_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_GapDerived_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_GapEnum_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_GapStruct_typeinfo);
    il2cpp::il2cpp_typeinfo_register(&Cases_PRefl2Matrix_typeinfo);
}

IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {
    il2cpp_runtime_init();
    Cases_PRefl2Matrix_Main_64e0b7673161b442();
    return 0;
}
