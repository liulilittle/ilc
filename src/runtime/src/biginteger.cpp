//------------------------------------------------------------------------------
// biginteger.cpp -- System.Numerics.BigInteger runtime support (see
// il2cpp_biginteger.h).
//
// An Il2CppBigInteger is an 8-byte value slot holding a pointer to a
// heap-allocated BigIntegerState (sign + little-endian u32 limbs). BigInteger
// is IMMUTABLE: every operation allocates a NEW state and never mutates its
// inputs, so pointer-copied values stay correct. The native states are never
// freed (no finalizer in this runtime -- the wait-handle/stream-handle
// precedent).
//
// Arithmetic: schoolbook multiplication, long division (per-limb binary
// search for the quotient digit), sign + magnitude comparison, repeated
// division by 10 for ToString.
//------------------------------------------------------------------------------

#include "il2cpp_biginteger.h"

#include "il2cpp_exception.h"     // il2cpp_throw_new
#include "il2cpp_exception_ext.h" // il2cpp_throw_divide_by_zero_exception /
                                  // il2cpp_throw_overflow_exception
#include "il2cpp_string.h"        // il2cpp_string_new_from_utf8

#include <cstdint>
#include <cstdio>
#include <string>
#include <vector>

namespace il2cpp { namespace runtime {

struct BigIntegerState
{
    bool negative;
    std::vector<uint32_t> limbs; // little-endian, no leading zero limbs
};

namespace {

// Strip leading zero limbs; zero is { negative=false, limbs={} }.
void Normalize(BigIntegerState& s)
{
    while (!s.limbs.empty() && s.limbs.back() == 0)
    {
        s.limbs.pop_back();
    }
    if (s.limbs.empty())
    {
        s.negative = false;
    }
}

// Compare magnitudes (ignoring sign): -1 / 0 / 1.
int32_t CompareMagnitude(const std::vector<uint32_t>& a, const std::vector<uint32_t>& b)
{
    if (a.size() != b.size())
    {
        return a.size() < b.size() ? -1 : 1;
    }
    for (size_t i = a.size(); i-- > 0;)
    {
        if (a[i] != b[i])
        {
            return a[i] < b[i] ? -1 : 1;
        }
    }
    return 0;
}

// result = |a| + |b|.
std::vector<uint32_t> AddMagnitude(const std::vector<uint32_t>& a,
                                   const std::vector<uint32_t>& b)
{
    std::vector<uint32_t> result;
    uint64_t carry = 0;
    const size_t n = a.size() > b.size() ? a.size() : b.size();
    result.reserve(n + 1);
    for (size_t i = 0; i < n; ++i)
    {
        const uint64_t av = i < a.size() ? a[i] : 0;
        const uint64_t bv = i < b.size() ? b[i] : 0;
        const uint64_t sum = av + bv + carry;
        result.push_back(static_cast<uint32_t>(sum));
        carry = sum >> 32;
    }
    if (carry)
    {
        result.push_back(static_cast<uint32_t>(carry));
    }
    return result;
}

// result = |a| - |b| (requires |a| >= |b|).
std::vector<uint32_t> SubMagnitude(const std::vector<uint32_t>& a,
                                   const std::vector<uint32_t>& b)
{
    std::vector<uint32_t> result;
    int64_t borrow = 0;
    result.reserve(a.size());
    for (size_t i = 0; i < a.size(); ++i)
    {
        const int64_t av = a[i];
        const int64_t bv = i < b.size() ? b[i] : 0;
        const int64_t diff = av - bv - borrow;
        if (diff < 0)
        {
            result.push_back(static_cast<uint32_t>(diff + (INT64_C(1) << 32)));
            borrow = 1;
        }
        else
        {
            result.push_back(static_cast<uint32_t>(diff));
            borrow = 0;
        }
    }
    while (!result.empty() && result.back() == 0)
    {
        result.pop_back();
    }
    return result;
}

// result = |a| * |b| (schoolbook).
std::vector<uint32_t> MulMagnitude(const std::vector<uint32_t>& a,
                                   const std::vector<uint32_t>& b)
{
    if (a.empty() || b.empty())
    {
        return {};
    }
    std::vector<uint32_t> result(a.size() + b.size(), 0);
    for (size_t i = 0; i < a.size(); ++i)
    {
        uint64_t carry = 0;
        for (size_t j = 0; j < b.size(); ++j)
        {
            const uint64_t cur = static_cast<uint64_t>(result[i + j])
                               + static_cast<uint64_t>(a[i]) * b[j] + carry;
            result[i + j] = static_cast<uint32_t>(cur);
            carry = cur >> 32;
        }
        result[i + b.size()] += static_cast<uint32_t>(carry);
    }
    while (!result.empty() && result.back() == 0)
    {
        result.pop_back();
    }
    return result;
}

// quotient = |a| / |b|, remainder = |a| % |b| (long division; requires |b| != 0).
void DivModMagnitude(const std::vector<uint32_t>& a, const std::vector<uint32_t>& b,
                     std::vector<uint32_t>& quotient, std::vector<uint32_t>& remainder)
{
    remainder.clear();
    quotient.assign(a.size(), 0);
    for (size_t i = a.size(); i-- > 0;)
    {
        // remainder = remainder * 2^32 + a[i] (insert at the least-significant
        // position -- index 0 -- which is the little-endian shift-left-by-32).
        remainder.insert(remainder.begin(), a[i]);
        while (!remainder.empty() && remainder.back() == 0)
        {
            remainder.pop_back();
        }
        // Binary search the largest q in [0, 2^32) with q*b <= remainder.
        uint32_t q = 0;
        uint32_t lo = 0;
        uint32_t hi = 0xFFFFFFFFu;
        while (lo <= hi)
        {
            const uint32_t mid = lo + (hi - lo) / 2;
            const std::vector<uint32_t> prod = MulMagnitude(b, { mid });
            if (CompareMagnitude(prod, remainder) <= 0)
            {
                q = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        quotient[i] = q;
        const std::vector<uint32_t> prod = MulMagnitude(b, { q });
        remainder = SubMagnitude(remainder, prod);
    }
    while (!quotient.empty() && quotient.back() == 0)
    {
        quotient.pop_back();
    }
    while (!remainder.empty() && remainder.back() == 0)
    {
        remainder.pop_back();
    }
}

// Decimal representation (repeated division by 10).
std::string ToDecimal(const BigIntegerState& s)
{
    if (s.limbs.empty())
    {
        return "0";
    }
    std::vector<uint32_t> limbs = s.limbs;
    std::string digits;
    while (!limbs.empty())
    {
        uint64_t rem = 0;
        for (size_t i = limbs.size(); i-- > 0;)
        {
            const uint64_t cur = (rem << 32) | limbs[i];
            limbs[i] = static_cast<uint32_t>(cur / 10);
            rem = cur % 10;
        }
        while (!limbs.empty() && limbs.back() == 0)
        {
            limbs.pop_back();
        }
        digits.push_back(static_cast<char>('0' + rem));
    }
    std::string result;
    if (s.negative)
    {
        result.push_back('-');
    }
    for (size_t i = digits.size(); i-- > 0;)
    {
        result.push_back(digits[i]);
    }
    return result;
}

} // namespace

//------------------------------------------------------------------------------
// Construction.
//------------------------------------------------------------------------------
void icall_biginteger_from_int32(Il2CppBigInteger* out, int32_t value)
{
    auto* state = new BigIntegerState();
    if (value < 0)
    {
        state->negative = true;
        const uint32_t mag = static_cast<uint32_t>(-static_cast<int64_t>(value));
        if (mag != 0)
        {
            state->limbs.push_back(mag);
        }
    }
    else
    {
        state->negative = false;
        if (value != 0)
        {
            state->limbs.push_back(static_cast<uint32_t>(value));
        }
    }
    out->state = state;
}

void icall_biginteger_from_int64(Il2CppBigInteger* out, int64_t value)
{
    auto* state = new BigIntegerState();
    if (value < 0)
    {
        state->negative = true;
        const uint64_t mag = static_cast<uint64_t>(-(value + 1)) + 1; // INT64_MIN-safe
        if (mag != 0)
        {
            state->limbs.push_back(static_cast<uint32_t>(mag));
            state->limbs.push_back(static_cast<uint32_t>(mag >> 32));
        }
    }
    else
    {
        state->negative = false;
        const uint64_t mag = static_cast<uint64_t>(value);
        if (mag != 0)
        {
            state->limbs.push_back(static_cast<uint32_t>(mag));
            state->limbs.push_back(static_cast<uint32_t>(mag >> 32));
        }
    }
    out->state = state;
}

//------------------------------------------------------------------------------
// Arithmetic (result-slot-first; inputs never mutated).
//------------------------------------------------------------------------------
void icall_biginteger_add(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b)
{
    auto* state = new BigIntegerState();
    if (a.state->negative == b.state->negative)
    {
        state->negative = a.state->negative;
        state->limbs = AddMagnitude(a.state->limbs, b.state->limbs);
    }
    else
    {
        const int32_t cmp = CompareMagnitude(a.state->limbs, b.state->limbs);
        if (cmp == 0)
        {
            state->negative = false;
        }
        else if (cmp > 0)
        {
            state->negative = a.state->negative;
            state->limbs = SubMagnitude(a.state->limbs, b.state->limbs);
        }
        else
        {
            state->negative = b.state->negative;
            state->limbs = SubMagnitude(b.state->limbs, a.state->limbs);
        }
    }
    Normalize(*state);
    out->state = state;
}

void icall_biginteger_sub(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b)
{
    auto* state = new BigIntegerState();
    if (a.state->negative != b.state->negative)
    {
        // a - b = a + |b| (different signs).
        state->negative = a.state->negative;
        state->limbs = AddMagnitude(a.state->limbs, b.state->limbs);
    }
    else
    {
        const int32_t cmp = CompareMagnitude(a.state->limbs, b.state->limbs);
        if (cmp == 0)
        {
            state->negative = false;
        }
        else if (cmp > 0)
        {
            state->negative = a.state->negative;
            state->limbs = SubMagnitude(a.state->limbs, b.state->limbs);
        }
        else
        {
            state->negative = !a.state->negative;
            state->limbs = SubMagnitude(b.state->limbs, a.state->limbs);
        }
    }
    Normalize(*state);
    out->state = state;
}

void icall_biginteger_mul(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b)
{
    auto* state = new BigIntegerState();
    state->negative = a.state->negative != b.state->negative;
    state->limbs = MulMagnitude(a.state->limbs, b.state->limbs);
    Normalize(*state);
    out->state = state;
}

void icall_biginteger_div(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b)
{
    if (b.state->limbs.empty())
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    auto* state = new BigIntegerState();
    std::vector<uint32_t> q, r;
    DivModMagnitude(a.state->limbs, b.state->limbs, q, r);
    state->negative = a.state->negative != b.state->negative;
    state->limbs = q;
    Normalize(*state);
    out->state = state;
}

void icall_biginteger_rem(Il2CppBigInteger* out, Il2CppBigInteger a, Il2CppBigInteger b)
{
    if (b.state->limbs.empty())
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    auto* state = new BigIntegerState();
    std::vector<uint32_t> q, r;
    DivModMagnitude(a.state->limbs, b.state->limbs, q, r);
    state->negative = a.state->negative; // remainder takes the dividend's sign
    state->limbs = r;
    Normalize(*state);
    out->state = state;
}

void icall_biginteger_negate(Il2CppBigInteger* out, Il2CppBigInteger a)
{
    auto* state = new BigIntegerState();
    state->negative = !a.state->negative;
    state->limbs = a.state->limbs;
    Normalize(*state);
    out->state = state;
}

//------------------------------------------------------------------------------
// Comparison / equality.
//------------------------------------------------------------------------------
int32_t icall_biginteger_compare(Il2CppBigInteger a, Il2CppBigInteger b)
{
    if (a.state->negative != b.state->negative)
    {
        return a.state->negative ? -1 : 1;
    }
    const int32_t cmp = CompareMagnitude(a.state->limbs, b.state->limbs);
    return a.state->negative ? -cmp : cmp;
}

bool icall_biginteger_equals(Il2CppBigInteger a, Il2CppBigInteger b)
{
    return icall_biginteger_compare(a, b) == 0;
}

//------------------------------------------------------------------------------
// Conversion.
//------------------------------------------------------------------------------
int32_t icall_biginteger_to_int32(Il2CppBigInteger a)
{
    if (a.state->limbs.size() > 1)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    const uint32_t mag = a.state->limbs.empty() ? 0 : a.state->limbs[0];
    if (a.state->negative)
    {
        if (mag > 0x80000000u)
        {
            il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
        }
        return static_cast<int32_t>(-static_cast<int64_t>(mag));
    }
    if (mag > 0x7FFFFFFFu)
    {
        il2cpp_throw_overflow_exception("Value was either too large or too small for an Int32.");
    }
    return static_cast<int32_t>(mag);
}

//------------------------------------------------------------------------------
// Formatting.
//------------------------------------------------------------------------------
Il2CppObject* icall_biginteger_to_string(Il2CppBigInteger a)
{
    const std::string text = ToDecimal(*a.state);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

}} // namespace il2cpp::runtime
