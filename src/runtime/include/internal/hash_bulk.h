#pragma once
// hash_bulk.h -- Bulk hash helpers, GC-free, noexcept.
#include "il2cpp_string.h"
#include <cstdint>
#include <cstddef>
namespace il2cpp {
uint32_t il2cpp_string_hash_ordinal(const Il2CppString* s) noexcept;
uint32_t il2cpp_string_hash_ordinal_ignore_case(const Il2CppString* s) noexcept;
uint32_t il2cpp_buffer_hash_fnv1a(const void* data, size_t bytes) noexcept;
uint32_t il2cpp_array_hash_i4_bulk(const void* data, size_t count) noexcept;
} // namespace il2cpp
