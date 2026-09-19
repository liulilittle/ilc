#if defined(__arm__) || defined(_M_ARM) || defined(__i386__) || defined(_M_IX86)
#error "il2cpp runtime: 32-bit targets are not supported in phase 1 (arm64-v8a/x86_64 only)"
#endif

#pragma once
// math_bulk.h -- Bulk math primitives, GC-free, noexcept, O(1) per scalar + O(n) bulk.
#include <cstdint>
#include <cstddef>
namespace il2cpp {
bool il2cpp_math_is_pow2_u32(uint32_t v) noexcept;
bool il2cpp_math_is_pow2_u64(uint64_t v) noexcept;
int il2cpp_math_ctz_u32(uint32_t v) noexcept;
int il2cpp_math_ctz_u64(uint64_t v) noexcept;
int il2cpp_math_clz_u32(uint32_t v) noexcept;
int il2cpp_math_clz_u64(uint64_t v) noexcept;
int il2cpp_math_popcount_u32(uint32_t v) noexcept;
int il2cpp_math_popcount_u64(uint64_t v) noexcept;
uint32_t il2cpp_math_round_up_pow2_u32(uint32_t v) noexcept;
uint64_t il2cpp_math_round_up_pow2_u64(uint64_t v) noexcept;
size_t il2cpp_math_count_pow2_bulk_u32(const uint32_t* data, size_t count) noexcept;
} // namespace il2cpp
