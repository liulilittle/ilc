//==============================================================================
// string_ascii.cpp -- ASCII fast-path helpers for managed strings.
//
// High-throughput ASCII predicates and prefix scanners used as guards for
// bulk fast paths (ToUpper/ToLower, UTF-8 transcoding, interop). All helpers
// are O(n) single-pass, branch-minimal, early-exit, and GC-free (no
// allocation, no barrier). They operate directly on the Il2CppString payload
// (UTF-16 units) and are safe for null/empty inputs.
//
// Performance shape:
//   * 8x unrolled loops for bulk ASCII detection (reduces branch mispredict).
//   * / on hot/cold branches matching observed data
//     (generated-code strings are predominantly ASCII in the test corpus).
//   * No vector intrinsics here: plain C++ keeps -Wall -Wextra clean and
//     portable (MSVC + MinGW + POSIX). A future SIMD lane can replace the
//     unrolled body without changing callers.
//   * All functions are [[nodiscard]] where applicable and do not throw.
//==============================================================================

#include "il2cpp_string.h"

#include <cstdint>
#include <cstddef>

namespace il2cpp {

//------------------------------------------------------------------------------
// Bulk ASCII predicate -- 8x unrolled, early exit on first non-ASCII.
// Returns true for empty string (vacuously ASCII, mirrors .NET IsAscii
// semantics used as fast-path guard). Null -> false (defensive).
//------------------------------------------------------------------------------
bool il2cpp_string_is_ascii_bulk(const Il2CppString* s) noexcept
{
    if (s == nullptr) 
    {
        return false;
    }
    const int32_t len = s->length;
    if (len == 0) 
    {
        return true;
    }
    const char16_t* p = s->chars;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
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
        if ((c0 | c1 | c2 | c3 | c4 | c5 | c6 | c7) >= 0x80) 
        {
            if (c0 >= 0x80) return false;
            if (c1 >= 0x80) return false;
            if (c2 >= 0x80) return false;
            if (c3 >= 0x80) return false;
            if (c4 >= 0x80) return false;
            if (c5 >= 0x80) return false;
            if (c6 >= 0x80) return false;
            return false; // c7
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] >= 0x80) 
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// Find first non-ASCII index. Returns -1 if all ASCII (or empty). Null -> -1
// sentinel (caller treats as not-found; bulk guard already rejected null).
//------------------------------------------------------------------------------
int32_t il2cpp_string_find_non_ascii(const Il2CppString* s) noexcept
{
    if (s == nullptr) 
    {
        return -1;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
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
        if ((c0 | c1 | c2 | c3 | c4 | c5 | c6 | c7) >= 0x80) 
        {
            if (c0 >= 0x80) return i + 0;
            if (c1 >= 0x80) return i + 1;
            if (c2 >= 0x80) return i + 2;
            if (c3 >= 0x80) return i + 3;
            if (c4 >= 0x80) return i + 4;
            if (c5 >= 0x80) return i + 5;
            if (c6 >= 0x80) return i + 6;
            return i + 7;
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] >= 0x80) 
        {
            return i;
        }
    }
    return -1;
}

//------------------------------------------------------------------------------
// ASCII prefix length: count of leading ASCII units (0..len). For a fully
// ASCII string returns len; for non-ASCII returns index of first non-ASCII.
// Useful to split bulk ASCII prefix memcpy from slow suffix handling.
//------------------------------------------------------------------------------
int32_t il2cpp_string_ascii_prefix_length(const Il2CppString* s) noexcept
{
    if (s == nullptr) 
    {
        return 0;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
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
        if ((c0 | c1 | c2 | c3 | c4 | c5 | c6 | c7) >= 0x80) 
        {
            if (c0 >= 0x80) return i + 0;
            if (c1 >= 0x80) return i + 1;
            if (c2 >= 0x80) return i + 2;
            if (c3 >= 0x80) return i + 3;
            if (c4 >= 0x80) return i + 4;
            if (c5 >= 0x80) return i + 5;
            if (c6 >= 0x80) return i + 6;
            return i + 7;
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] >= 0x80) 
        {
            return i;
        }
    }
    return len;
}

//------------------------------------------------------------------------------
// Count non-ASCII units. Trivial O(n) scan; 8x unrolled with branchless
// accumulation (sum of (c >= 0x80) widened). Early-out not used -- caller
// wants exact count for diagnostics.
//------------------------------------------------------------------------------
int32_t il2cpp_string_count_non_ascii(const Il2CppString* s) noexcept
{
    if (s == nullptr || s->length == 0) 
    {
        return 0;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t count = 0;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i += 8)
    {
        count += (p[i + 0] >= 0x80) ? 1 : 0;
        count += (p[i + 1] >= 0x80) ? 1 : 0;
        count += (p[i + 2] >= 0x80) ? 1 : 0;
        count += (p[i + 3] >= 0x80) ? 1 : 0;
        count += (p[i + 4] >= 0x80) ? 1 : 0;
        count += (p[i + 5] >= 0x80) ? 1 : 0;
        count += (p[i + 6] >= 0x80) ? 1 : 0;
        count += (p[i + 7] >= 0x80) ? 1 : 0;
    }
    for (; i < len; ++i)
    {
        count += (p[i] >= 0x80) ? 1 : 0;
    }
    return count;
}

//------------------------------------------------------------------------------
// Count units that would change under ASCII ToUpper (a-z -> A-Z). Used to
// decide whether ToUpper can take the prefix-memcpy + suffix-only path vs
// full copy. Pure predicate, no allocation.
//------------------------------------------------------------------------------
int32_t il2cpp_string_count_ascii_lower(const Il2CppString* s) noexcept
{
    if (s == nullptr || s->length == 0) 
    {
        return 0;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t count = 0;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i += 8)
    {
        count += (p[i + 0] >= u'a' && p[i + 0] <= u'z') ? 1 : 0;
        count += (p[i + 1] >= u'a' && p[i + 1] <= u'z') ? 1 : 0;
        count += (p[i + 2] >= u'a' && p[i + 2] <= u'z') ? 1 : 0;
        count += (p[i + 3] >= u'a' && p[i + 3] <= u'z') ? 1 : 0;
        count += (p[i + 4] >= u'a' && p[i + 4] <= u'z') ? 1 : 0;
        count += (p[i + 5] >= u'a' && p[i + 5] <= u'z') ? 1 : 0;
        count += (p[i + 6] >= u'a' && p[i + 6] <= u'z') ? 1 : 0;
        count += (p[i + 7] >= u'a' && p[i + 7] <= u'z') ? 1 : 0;
    }
    for (; i < len; ++i)
    {
        count += (p[i] >= u'a' && p[i] <= u'z') ? 1 : 0;
    }
    return count;
}

//------------------------------------------------------------------------------
// Count units that would change under ASCII ToLower (A-Z -> a-z) -- 8x unrolled.
//------------------------------------------------------------------------------
int32_t il2cpp_string_count_ascii_upper(const Il2CppString* s) noexcept
{
    if (s == nullptr || s->length == 0) 
    {
        return 0;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t count = 0;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
    for (; i < bulk; i += 8)
    {
        count += (p[i + 0] >= u'A' && p[i + 0] <= u'Z') ? 1 : 0;
        count += (p[i + 1] >= u'A' && p[i + 1] <= u'Z') ? 1 : 0;
        count += (p[i + 2] >= u'A' && p[i + 2] <= u'Z') ? 1 : 0;
        count += (p[i + 3] >= u'A' && p[i + 3] <= u'Z') ? 1 : 0;
        count += (p[i + 4] >= u'A' && p[i + 4] <= u'Z') ? 1 : 0;
        count += (p[i + 5] >= u'A' && p[i + 5] <= u'Z') ? 1 : 0;
        count += (p[i + 6] >= u'A' && p[i + 6] <= u'Z') ? 1 : 0;
        count += (p[i + 7] >= u'A' && p[i + 7] <= u'Z') ? 1 : 0;
    }
    for (; i < len; ++i)
    {
        count += (p[i] >= u'A' && p[i] <= u'Z') ? 1 : 0;
    }
    return count;
}

//------------------------------------------------------------------------------
// Fast check whether ToUpper would be a no-op (no a-z). 8x unrolled early
// exit. Mirrors firstDiff scan in stringext.cpp but extracted for reuse.
//------------------------------------------------------------------------------
bool il2cpp_string_is_ascii_upper_noop(const Il2CppString* s) noexcept
{
    if (s == nullptr) 
    {
        return true;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
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
        if ((c0 >= u'a' && c0 <= u'z') || (c1 >= u'a' && c1 <= u'z') ||
            (c2 >= u'a' && c2 <= u'z') || (c3 >= u'a' && c3 <= u'z') ||
            (c4 >= u'a' && c4 <= u'z') || (c5 >= u'a' && c5 <= u'z') ||
            (c6 >= u'a' && c6 <= u'z') || (c7 >= u'a' && c7 <= u'z')) 
        {
            if (c0 >= u'a' && c0 <= u'z') return false;
            if (c1 >= u'a' && c1 <= u'z') return false;
            if (c2 >= u'a' && c2 <= u'z') return false;
            if (c3 >= u'a' && c3 <= u'z') return false;
            if (c4 >= u'a' && c4 <= u'z') return false;
            if (c5 >= u'a' && c5 <= u'z') return false;
            if (c6 >= u'a' && c6 <= u'z') return false;
            return false; // c7
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] >= u'a' && p[i] <= u'z') 
        {
            return false;
        }
    }
    return true;
}

bool il2cpp_string_is_ascii_lower_noop(const Il2CppString* s) noexcept
{
    if (s == nullptr) 
    {
        return true;
    }
    const int32_t len = s->length;
    const char16_t* p = s->chars;
    int32_t i = 0;
    const int32_t bulk = len & ~7;
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
        if ((c0 >= u'A' && c0 <= u'Z') || (c1 >= u'A' && c1 <= u'Z') ||
            (c2 >= u'A' && c2 <= u'Z') || (c3 >= u'A' && c3 <= u'Z') ||
            (c4 >= u'A' && c4 <= u'Z') || (c5 >= u'A' && c5 <= u'Z') ||
            (c6 >= u'A' && c6 <= u'Z') || (c7 >= u'A' && c7 <= u'Z')) 
        {
            if (c0 >= u'A' && c0 <= u'Z') return false;
            if (c1 >= u'A' && c1 <= u'Z') return false;
            if (c2 >= u'A' && c2 <= u'Z') return false;
            if (c3 >= u'A' && c3 <= u'Z') return false;
            if (c4 >= u'A' && c4 <= u'Z') return false;
            if (c5 >= u'A' && c5 <= u'Z') return false;
            if (c6 >= u'A' && c6 <= u'Z') return false;
            return false; // c7
        }
    }
    for (; i < len; ++i)
    {
        if (p[i] >= u'A' && p[i] <= u'Z') 
        {
            return false;
        }
    }
    return true;
}

} // namespace il2cpp

