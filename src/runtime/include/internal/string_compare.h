#pragma once
// string_compare.h -- Ordinal compare helpers, 8x bulk, GC-free.
#include "il2cpp_string.h"
namespace il2cpp {
int  il2cpp_string_compare_ordinal(const Il2CppString* a, const Il2CppString* b) noexcept;
int  il2cpp_string_compare_ordinal_ignore_case(const Il2CppString* a, const Il2CppString* b) noexcept;
bool il2cpp_string_equals_ordinal(const Il2CppString* a, const Il2CppString* b) noexcept;
bool il2cpp_string_equals_ordinal_ignore_case(const Il2CppString* a, const Il2CppString* b) noexcept;
} // namespace il2cpp
