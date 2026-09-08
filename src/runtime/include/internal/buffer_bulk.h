#pragma once
// buffer_bulk.h -- Bulk buffer primitives (GC-free, noexcept).
// Provides 8x-unrolled memset/memcpy/memcmp/is_zero with early-exit.
// Used by Array.Clear / Buffer.BlockCopy fast paths and diagnostics.
#include <cstddef>
#include <cstdint>
namespace il2cpp {
bool il2cpp_buffer_is_zero_bulk(const void* src, size_t bytes) noexcept;
void il2cpp_buffer_memset_bulk(void* dst, uint8_t value, size_t bytes) noexcept;
void il2cpp_buffer_memcpy_bulk(void* dst, const void* src, size_t bytes) noexcept;
int  il2cpp_buffer_memcmp_bulk(const void* a, const void* b, size_t bytes) noexcept;
bool il2cpp_buffer_try_copy_checked(void* dst, size_t dstSize, const void* src, size_t srcSize, size_t bytes) noexcept;
} // namespace il2cpp
