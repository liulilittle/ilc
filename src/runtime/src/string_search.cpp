//==============================================================================
// string_search.cpp -- Bulk search helpers for managed strings.
//
// Provides O(n) primitives used by BCL IndexOf/LastIndexOf/Contains/
// StartsWith/EndsWith paths. All helpers are GC-free, noexcept, and operate
// directly on the UTF-16 payload (Il2CppString::chars). 8x unrolled loops
// halve branches vs scalar scans; early-exit preserves first-hit semantics.
//
// No allocation, no barriers, no exceptions. Callers validate start/count and
// null; these helpers assume non-null s and 0<=start<=len invariants (defensive
// null checks return sentinel). The bulk discipline mirrors string_ascii.cpp:
// OR-merged equality test + lane-by-lane early exit.
//
// Future: SIMD lane (SSE2/NEON) can replace the 8x body without changing
// callers; the API boundary is stable.
//==============================================================================

#include "il2cpp_string.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

//------------------------------------------------------------------------------
// IndexOf(char) -- first occurrence of c at or after start. -1 if not found.
// 8x bulk, early exit on first lane hit.
//------------------------------------------------------------------------------
int32_t il2cpp_string_index_of_char(const Il2CppString* s, char16_t c, int32_t start) noexcept
{
    if (s == nullptr) 
    {
        return -1;
    }
    const int32_t len = s->length;
    if (start < 0) start = 0;
    if (start >= len) 
    {
        return -1;
    }
    const char16_t* p = s->chars;
    int32_t i = start;
    const int32_t bulk = len & ~7;
    if (i < bulk)
    {
        // Align bulk start to 8 boundary past 'start' for simple loop.
        // Handle head scalar until aligned, then bulk.
        for (; i < bulk && (i & 7) != 0; ++i)
        {
            if (p[i] == c) 
            {
                return i;
            }
        }
        for (; i < bulk; i += 8)
        {
            const char16_t c0 = p[i + 0];
            const char16_t c1 = p[i + 1];
            const char16_t c2 = p[i + 2];
            const char16_t c3 = p[i + 3];
            const char16_t c4 = p[i + 4];
            const char16_t c5 = p[i + 5];
            const char16_t c6 = p[i + 6];
            const char16_t c7 = p[i + 7];
            if (c0 == c || c1 == c || c2 == c || c3 == c ||
                c4 == c || c5 == c || c6 == c || c7 == c) 
            {
                if (c0 == c) return i + 0;
                if (c1 == c) return i + 1;
                if (c2 == c) return i + 2;
                if (c3 == c) return i + 3;
                if (c4 == c) return i + 4;
                if (c5 == c) return i + 5;
                if (c6 == c) return i + 6;
                return i + 7;
            }
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] == c) 
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// LastIndexOf(char) -- last occurrence at or before start.
//------------------------------------------------------------------------------
int32_t il2cpp_string_last_index_of_char(const Il2CppString* s, char16_t c, int32_t start) noexcept
{
    if (s == nullptr) 
    {
        return -1;
    }
    const int32_t len = s->length;
    if (len == 0) 
    {
        return -1;
    }
    if (start < 0) 
    {
        return -1;
    }
    if (start >= len) start = len - 1;
    const char16_t* p = s->chars;
    // Tail bulk: work backwards in 8-blocks where possible.
    int32_t i = start;
    // Handle tail scalar until i aligned to 7 mod 8.
    for (; i >= 0 && (i & 7) != 7; --i)
    {
        if (p[i] == c) 
        {
            return i;
        }
    }
    for (; i >= 7; i -= 8)
    {
        const char16_t c0 = p[i - 7];
        const char16_t c1 = p[i - 6];
        const char16_t c2 = p[i - 5];
        const char16_t c3 = p[i - 4];
        const char16_t c4 = p[i - 3];
        const char16_t c5 = p[i - 2];
        const char16_t c6 = p[i - 1];
        const char16_t c7 = p[i - 0];
        if (c0 == c || c1 == c || c2 == c || c3 == c ||
            c4 == c || c5 == c || c6 == c || c7 == c) 
        {
            if (c7 == c) return i - 0;
            if (c6 == c) return i - 1;
            if (c5 == c) return i - 2;
            if (c4 == c) return i - 3;
            if (c3 == c) return i - 4;
            if (c2 == c) return i - 5;
            if (c1 == c) return i - 6;
            return i - 7;
        }
    }
    for (; i >= 0; --i)
    {
        if (p[i] == c) 
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// Contains(char) -- wrapper over IndexOf, branchless for tiny strings.
//------------------------------------------------------------------------------
bool il2cpp_string_contains_char(const Il2CppString* s, char16_t c) noexcept
{
    if (s == nullptr || s->length == 0) 
    {
        return false;
    }
    return il2cpp_string_index_of_char(s, c, 0) != -1;
}

//------------------------------------------------------------------------------
// Count occurrences of c in [start, start+count).
//------------------------------------------------------------------------------
int32_t il2cpp_string_count_char(const Il2CppString* s, char16_t c, int32_t start, int32_t count) noexcept
{
    if (s == nullptr || count <= 0) 
    {
        return 0;
    }
    const int32_t len = s->length;
    if (start < 0) start = 0;
    if (start >= len) return 0;
    if (start + count > len) count = len - start;
    const char16_t* p = s->chars + start;
    int32_t n = 0;
    int32_t i = 0;
    const int32_t bulk = count & ~7;
    for (; i < bulk; i += 8)
    {
        n += (p[i + 0] == c) ? 1 : 0;
        n += (p[i + 1] == c) ? 1 : 0;
        n += (p[i + 2] == c) ? 1 : 0;
        n += (p[i + 3] == c) ? 1 : 0;
        n += (p[i + 4] == c) ? 1 : 0;
        n += (p[i + 5] == c) ? 1 : 0;
        n += (p[i + 6] == c) ? 1 : 0;
        n += (p[i + 7] == c) ? 1 : 0;
    }
    for (; i < count; ++i)
    {
        n += (p[i] == c) ? 1 : 0;
    }
    return n;
}

//------------------------------------------------------------------------------
// StartsWith -- prefix equality, memcmp for bulk ASCII fast path.
//------------------------------------------------------------------------------
bool il2cpp_string_starts_with(const Il2CppString* s, const Il2CppString* prefix) noexcept
{
    if (prefix == nullptr || prefix->length == 0) 
    {
        return true;
    }
    if (s == nullptr) 
    {
        return false;
    }
    const int32_t plen = prefix->length;
    if (plen > s->length) 
    {
        return false;
    }
    return std::memcmp(s->chars, prefix->chars, static_cast<size_t>(plen) * sizeof(char16_t)) == 0;
}

bool il2cpp_string_ends_with(const Il2CppString* s, const Il2CppString* suffix) noexcept
{
    if (suffix == nullptr || suffix->length == 0) 
    {
        return true;
    }
    if (s == nullptr) 
    {
        return false;
    }
    const int32_t slen = suffix->length;
    if (slen > s->length) 
    {
        return false;
    }
    return std::memcmp(s->chars + (s->length - slen), suffix->chars, static_cast<size_t>(slen) * sizeof(char16_t)) == 0;
}

//------------------------------------------------------------------------------
// IndexOfAny -- bulk bitmap already exists in stringext.cpp; this module
// provides a thin forwarding helper that reuses the same 8KB bitmap strategy
// for callers that link against search helpers only. Thin wrapper to avoid
// code duplication; kept for API completeness.
//------------------------------------------------------------------------------
int32_t il2cpp_string_index_of_any_bulk(const Il2CppString* s, const char16_t* any, int32_t anyLen, int32_t start) noexcept
{
    if (s == nullptr || any == nullptr || anyLen <= 0) 
    {
        return -1;
    }
    const int32_t len = s->length;
    if (start < 0) start = 0;
    if (start >= len) return -1;
    // Build 8KB bitmap on stack (same as stringext Split/TrimSet).
    // For small anyLen (<64) linear scan would be similar, but bitmap gives
    // O(1) per char and is already proven in Split/Trim paths.
    uint8_t map[8192] = {0};
    for (int32_t i = 0; i < anyLen; ++i)
    {
        const uint16_t cu = static_cast<uint16_t>(any[i]);
        map[cu >> 3] |= static_cast<uint8_t>(1u << (cu & 7));
    }
    const char16_t* p = s->chars;
    for (int32_t i = start; i < len; ++i)
    {
        const uint16_t cu = static_cast<uint16_t>(p[i]);
        if (map[cu >> 3] & (1u << (cu & 7))) 
        {
            return i;
        }
    }
    return -1;
}

} // namespace il2cpp

