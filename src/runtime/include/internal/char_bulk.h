#pragma once
// char_bulk.h -- Bulk char classification, 8x, GC-free.
#include "il2cpp_string.h"
namespace il2cpp {
bool il2cpp_char_all_digit(const Il2CppString* s) noexcept;
bool il2cpp_char_all_letter(const Il2CppString* s) noexcept;
bool il2cpp_char_all_letter_or_digit(const Il2CppString* s) noexcept;
int32_t il2cpp_char_count_digit(const Il2CppString* s) noexcept;
int32_t il2cpp_char_count_letter(const Il2CppString* s) noexcept;
int32_t il2cpp_char_count_whitespace(const Il2CppString* s) noexcept;
bool il2cpp_char_is_digit_bulk(char16_t c) noexcept;
bool il2cpp_char_is_letter_bulk(char16_t c) noexcept;
} // namespace il2cpp
