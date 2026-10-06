#pragma once
// string_encode.h -- UTF-8/UTF-16 bulk helpers (GC-free, noexcept).
#include <cstddef>
#include <cstdint>
namespace il2cpp {
bool il2cpp_utf8_is_ascii(const char* utf8, size_t len) noexcept;
bool il2cpp_utf16_is_ascii_raw(const char16_t* chars, int32_t len) noexcept;
size_t il2cpp_utf8_to_utf16_length(const char* utf8, size_t len) noexcept;
size_t il2cpp_utf16_to_utf8_length(const char16_t* chars, int32_t len) noexcept;
int32_t il2cpp_utf8_decode_one(const char* s, size_t avail, char32_t* out) noexcept;
int il2cpp_utf8_validate(const char* utf8, size_t len) noexcept;
} // namespace il2cpp
