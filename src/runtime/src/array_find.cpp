//==============================================================================
// array_find.cpp -- Bulk search helpers for Il2CppArray<int32_t>.
//
// GC-free, noexcept, O(n) primitives for BCL Array.IndexOf / LastIndexOf /
// Contains / Count paths. Operate directly on Il2CppArray::data as int32_t
// payload (elemSize==4). 8x unrolled loops halve branches vs scalar scans;
// early-exit preserves first/last-hit semantics. No allocation, no barriers.
//
// Contracts (defensive): arr may be null => sentinel; start/count are clamped.
// elemSize mismatch => returns sentinel (caller validates). isRefElem ignored
// here (primitive path); reference arrays use object identity search elsewhere.
//
// Mirrors string_search.cpp's 8x discipline: OR-merged equality + lane exit.
//==============================================================================

#include "il2cpp_array.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

//------------------------------------------------------------------------------
// IndexOf(int32) -- first occurrence at or after start. -1 if not found.
//------------------------------------------------------------------------------
int32_t il2cpp_array_index_of_i4(const Il2CppArray* arr, int32_t value, int32_t start) noexcept
{
    if (arr == nullptr) 
    {
        return -1;
    }
    const int32_t len = arr->length;
    if (arr->elemSize != 4) 
    {
        return -1;
    }
    if (start < 0) start = 0;
    if (start >= len) 
    {
        return -1;
    }
    const int32_t* p = reinterpret_cast<const int32_t*>(arr->data);
    int32_t i = start;
    const int32_t bulk = len & ~7;
    if (i < bulk)
    {
        for (; i < bulk && (i & 7) != 0; ++i)
        {
            if (p[i] == value) 
            {
                return i;
            }
        }
        for (; i < bulk; i += 8)
        {
            const int32_t v0 = p[i + 0];
            const int32_t v1 = p[i + 1];
            const int32_t v2 = p[i + 2];
            const int32_t v3 = p[i + 3];
            const int32_t v4 = p[i + 4];
            const int32_t v5 = p[i + 5];
            const int32_t v6 = p[i + 6];
            const int32_t v7 = p[i + 7];
            if (v0 == value || v1 == value || v2 == value || v3 == value ||
                v4 == value || v5 == value || v6 == value || v7 == value) 
            {
                if (v0 == value) return i + 0;
                if (v1 == value) return i + 1;
                if (v2 == value) return i + 2;
                if (v3 == value) return i + 3;
                if (v4 == value) return i + 4;
                if (v5 == value) return i + 5;
                if (v6 == value) return i + 6;
                return i + 7;
            }
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] == value) 
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// LastIndexOf(int32) -- last occurrence at or before start.
//------------------------------------------------------------------------------
int32_t il2cpp_array_last_index_of_i4(const Il2CppArray* arr, int32_t value, int32_t start) noexcept
{
    if (arr == nullptr) 
    {
        return -1;
    }
    const int32_t len = arr->length;
    if (len == 0) 
    {
        return -1;
    }
    if (arr->elemSize != 4) 
    {
        return -1;
    }
    if (start < 0) 
    {
        return -1;
    }
    if (start >= len) start = len - 1;
    const int32_t* p = reinterpret_cast<const int32_t*>(arr->data);
    int32_t i = start;
    for (; i >= 0 && (i & 7) != 7; --i)
    {
        if (p[i] == value) 
        {
            return i;
        }
    }
    for (; i >= 7; i -= 8)
    {
        const int32_t v0 = p[i - 7];
        const int32_t v1 = p[i - 6];
        const int32_t v2 = p[i - 5];
        const int32_t v3 = p[i - 4];
        const int32_t v4 = p[i - 3];
        const int32_t v5 = p[i - 2];
        const int32_t v6 = p[i - 1];
        const int32_t v7 = p[i - 0];
        if (v0 == value || v1 == value || v2 == value || v3 == value ||
            v4 == value || v5 == value || v6 == value || v7 == value) 
        {
            if (v7 == value) return i - 0;
            if (v6 == value) return i - 1;
            if (v5 == value) return i - 2;
            if (v4 == value) return i - 3;
            if (v3 == value) return i - 4;
            if (v2 == value) return i - 5;
            if (v1 == value) return i - 6;
            return i - 7;
        }
    }
    for (; i >= 0; --i)
    {
        if (p[i] == value) 
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// Contains(int32)
//------------------------------------------------------------------------------
bool il2cpp_array_contains_i4(const Il2CppArray* arr, int32_t value) noexcept
{
    if (arr == nullptr || arr->length == 0) 
    {
        return false;
    }
    if (arr->elemSize != 4) 
    {
        return false;
    }
    return il2cpp_array_index_of_i4(arr, value, 0) != -1;
}

//------------------------------------------------------------------------------
// Count occurrences in [start, start+count).
//------------------------------------------------------------------------------
int32_t il2cpp_array_count_i4(const Il2CppArray* arr, int32_t value, int32_t start, int32_t count) noexcept
{
    if (arr == nullptr || count <= 0) 
    {
        return 0;
    }
    const int32_t len = arr->length;
    if (arr->elemSize != 4) 
    {
        return 0;
    }
    if (start < 0) start = 0;
    if (start >= len) return 0;
    if (start + count > len) count = len - start;
    const int32_t* p = reinterpret_cast<const int32_t*>(arr->data + static_cast<size_t>(start) * 4);
    int32_t n = 0;
    int32_t i = 0;
    const int32_t bulk = count & ~7;
    for (; i < bulk; i += 8)
    {
        n += (p[i + 0] == value) ? 1 : 0;
        n += (p[i + 1] == value) ? 1 : 0;
        n += (p[i + 2] == value) ? 1 : 0;
        n += (p[i + 3] == value) ? 1 : 0;
        n += (p[i + 4] == value) ? 1 : 0;
        n += (p[i + 5] == value) ? 1 : 0;
        n += (p[i + 6] == value) ? 1 : 0;
        n += (p[i + 7] == value) ? 1 : 0;
    }
    for (; i < count; ++i)
    {
        n += (p[i] == value) ? 1 : 0;
    }
    return n;
}

//------------------------------------------------------------------------------
// Bulk copy fast path for int32 arrays -- wraps memmove with elemSize check.
// Used by Array.Copy hot path when types match and non-overlapping is known.
//------------------------------------------------------------------------------
bool il2cpp_array_try_bulk_copy_i4(Il2CppArray* dst, int32_t dstIndex, const Il2CppArray* src, int32_t srcIndex, int32_t count) noexcept
{
    if (dst == nullptr || src == nullptr || count <= 0) 
    {
        return false;
    }
    if (dst->elemSize != 4 || src->elemSize != 4) 
    {
        return false;
    }
    if (srcIndex < 0 || dstIndex < 0 || count < 0) 
    {
        return false;
    }
    if (srcIndex + count > src->length || dstIndex + count > dst->length) 
    {
        return false;
    }
    // Overlapping within same array must use memmove (not memcpy) -- std::memmove handles it.
    const void* s = src->data + static_cast<size_t>(srcIndex) * 4;
    void* d = dst->data + static_cast<size_t>(dstIndex) * 4;
    std::memmove(d, s, static_cast<size_t>(count) * 4);
    return true;
}

} // namespace il2cpp

