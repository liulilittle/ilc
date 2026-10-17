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

#include "il2cpp_array.h"        // il2cpp_array_new (ToByteArray)
#include "il2cpp_exception.h"    // il2cpp_throw_new
#include "il2cpp_exception_ext.h" // il2cpp_throw_divide_by_zero_exception /
                                  // il2cpp_throw_overflow_exception /
                                  // il2cpp_throw_format_exception /
                                  // il2cpp_throw_argument_out_of_range_exception
#include "il2cpp_string.h"        // il2cpp_string_new_from_utf8

#include <cstdint>
#include <cstdio>
#include <cstring> // std::memcpy (ToByteArray)
#include <cwctype>
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
        // bigaudit1: lo/hi span [0, 2^32] -- a uint32 bound wrapped when
        // mid == 0xFFFFFFFF (lo = mid + 1 -> 0 -> non-termination for
        // quotient limbs that reach the full 32-bit range). Search in
        // uint64_t; the digit itself is still a uint32 limb.
        uint32_t q = 0;
        uint64_t lo = 0;
        uint64_t hi = 0xFFFFFFFFull;
        while (lo <= hi)
        {
            const uint64_t mid = lo + (hi - lo) / 2;
            const std::vector<uint32_t> prod = MulMagnitude(b, { static_cast<uint32_t>(mid) });
            if (CompareMagnitude(prod, remainder) <= 0)
            {
                q = static_cast<uint32_t>(mid);
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
            if ((mag >> 32) != 0)
            {
                state->limbs.push_back(static_cast<uint32_t>(mag >> 32));
            }
        }
    }
    else
    {
        state->negative = false;
        const uint64_t mag = static_cast<uint64_t>(value);
        if (mag != 0)
        {
            state->limbs.push_back(static_cast<uint32_t>(mag));
            if ((mag >> 32) != 0)
            {
                state->limbs.push_back(static_cast<uint32_t>(mag >> 32));
            }
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

//------------------------------------------------------------------------------
// nb4 NUM-BI: parsing / Pow / GCD / ToByteArray / wide conversions.
//------------------------------------------------------------------------------

namespace {

// The net48 char.IsWhiteSpace subset that decimal parsing trims (the common
// whitespace codepoints -- the probe surface is ASCII; the full Unicode table
// is deliberately not replicated).
bool IsDecimalWhitespace(char16_t c)
{
    return c == u' ' || c == u'\t' || c == u'\n' || c == u'\v' || c == u'\f'
        || c == u'\r' || c == 0x0085 || c == 0x00A0 || c == 0x1680
        || (c >= 0x2000 && c <= 0x200A) || c == 0x2028 || c == 0x2029
        || c == 0x202F || c == 0x205F || c == 0x3000;
}

// Accumulate the decimal digits (most significant first) into an unsigned
// state: acc = acc * 10 + digit per digit. Returns false on a non-digit.
bool AccumulateDecimal(const char16_t* chars, int32_t length,
                       const std::vector<uint32_t>& ten,
                       std::vector<uint32_t>& acc)
{
    for (int32_t i = 0; i < length; ++i)
    {
        const char16_t c = chars[i];
        if (c < u'0' || c > u'9')
        {
            return false;
        }
        const std::vector<uint32_t> digit = { static_cast<uint32_t>(c - u'0') };
        acc = AddMagnitude(MulMagnitude(acc, ten), digit);
    }
    return true;
}

// Build a one-limb state for the small positive value.
BigIntegerState* StateFromUint32(uint32_t value)
{
    auto* state = new BigIntegerState();
    state->negative = false;
    if (value != 0)
    {
        state->limbs.push_back(value);
    }
    return state;
}

// The little-endian magnitude bytes (per limb, low byte first, trailing zero
// bytes stripped -- the MINIMAL magnitude). The SIGN rules live in
// icall_biginteger_to_byte_array: positive values get a 0x00 sign byte only
// when the top byte's high bit is set; negative values are two's-complemented
// over the minimal bytes and only then get a 0xFF sign byte when the resulting
// top byte's high bit is clear.
std::vector<uint8_t> ToByteArrayMagnitude(const BigIntegerState& s)
{
    std::vector<uint8_t> bytes;
    bytes.reserve(s.limbs.size() * 4);
    for (uint32_t limb : s.limbs)
    {
        bytes.push_back(static_cast<uint8_t>(limb));
        bytes.push_back(static_cast<uint8_t>(limb >> 8));
        bytes.push_back(static_cast<uint8_t>(limb >> 16));
        bytes.push_back(static_cast<uint8_t>(limb >> 24));
    }
    while (bytes.size() > 1 && bytes.back() == 0)
    {
        bytes.pop_back();
    }
    return bytes;
}

void ApplyTwosComplement(std::vector<uint8_t>& bytes)
{
    int carry = 1;
    for (auto& b : bytes)
    {
        const int v = static_cast<int>(static_cast<uint8_t>(~b)) + carry;
        b = static_cast<uint8_t>(v);
        carry = v >> 8;
    }
}

} // namespace

void icall_biginteger_parse(Il2CppBigInteger* out, Il2CppString* text)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    const Il2CppString* s = text;
    int32_t i = 0;
    int32_t n = s->length;
    while (i < n && IsDecimalWhitespace(s->chars[i])) ++i;
    while (n > i && IsDecimalWhitespace(s->chars[n - 1])) --n;
    if (i == n)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    bool negative = false;
    if (s->chars[i] == u'-')
    {
        negative = true;
        ++i;
    }
    else if (s->chars[i] == u'+')
    {
        ++i;
    }
    if (i == n)
    {
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    auto* state = new BigIntegerState();
    const std::vector<uint32_t> ten = { 10 };
    if (!AccumulateDecimal(s->chars + i, n - i, ten, state->limbs))
    {
        delete state;
        il2cpp_throw_format_exception("Input string was not in a correct format.");
    }
    Normalize(*state);
    state->negative = negative && !state->limbs.empty();
    out->state = state;
}

bool icall_biginteger_try_parse(Il2CppString* text, Il2CppBigInteger* out)
{
    auto* state = new BigIntegerState();
    if (text != nullptr)
    {
        const Il2CppString* s = text;
        int32_t i = 0;
        int32_t n = s->length;
        while (i < n && IsDecimalWhitespace(s->chars[i])) ++i;
        while (n > i && IsDecimalWhitespace(s->chars[n - 1])) --n;
        if (i < n)
        {
            bool negative = false;
            if (s->chars[i] == u'-')
            {
                negative = true;
                ++i;
            }
            else if (s->chars[i] == u'+')
            {
                ++i;
            }
            bool ok = false;
            if (i < n)
            {
                const std::vector<uint32_t> ten = { 10 };
                ok = AccumulateDecimal(s->chars + i, n - i, ten, state->limbs);
            }
            if (ok)
            {
                Normalize(*state);
                state->negative = negative && !state->limbs.empty();
                out->state = state;
                return true;
            }
        }
    }
    out->state = state; // failure: BigInteger.Zero
    return false;
}

void icall_biginteger_pow(Il2CppBigInteger* out, Il2CppBigInteger value,
                          int32_t exponent)
{
    if (exponent < 0)
    {
        il2cpp_throw_argument_out_of_range_exception(
            "exponent", "The exponent must be a non-negative value.");
    }
    BigIntegerState resultState;
    resultState.negative = false;
    resultState.limbs = { 1 };
    BigIntegerState* baseState = StateFromUint32(0);
    baseState->negative = value.state->negative;
    baseState->limbs = value.state->limbs;
    int32_t e = exponent;
    while (e > 0)
    {
        if (e & 1)
        {
            resultState.limbs = MulMagnitude(resultState.limbs, baseState->limbs);
        }
        e >>= 1;
        if (e > 0)
        {
            baseState->limbs = MulMagnitude(baseState->limbs, baseState->limbs);
        }
    }
    delete baseState;
    if ((exponent & 1) != 0 && value.state->negative)
    {
        resultState.negative = true;
    }
    Normalize(resultState);
    auto* state = new BigIntegerState();
    state->negative = resultState.negative;
    state->limbs = std::move(resultState.limbs);
    out->state = state;
}

void icall_biginteger_gcd(Il2CppBigInteger* out, Il2CppBigInteger a,
                          Il2CppBigInteger b)
{
    // Euclidean algorithm over the magnitudes; the result is always
    // non-negative (gcd(0, 0) == 0, gcd(0, x) == |x|).
    std::vector<uint32_t> x = a.state->limbs;
    std::vector<uint32_t> y = b.state->limbs;
    while (!y.empty())
    {
        std::vector<uint32_t> q, r;
        DivModMagnitude(x, y, q, r);
        x = std::move(y);
        y = std::move(r);
    }
    auto* state = new BigIntegerState();
    state->negative = false;
    state->limbs = std::move(x);
    Normalize(*state);
    out->state = state;
}

Il2CppArray* icall_biginteger_to_byte_array(Il2CppBigInteger a)
{
    if (a.state->limbs.empty())
    {
        return il2cpp_array_new(nullptr, 1, false, 1); // { 0x00 }, zero-initialized
    }
    std::vector<uint8_t> bytes = ToByteArrayMagnitude(*a.state);
    if (!a.state->negative)
    {
        if (bytes.back() & 0x80)
        {
            bytes.push_back(0); // positive values keep a clear sign byte
        }
    }
    else
    {
        // net48 (双审 defect 3): the two's complement is taken over the
        // MINIMAL magnitude bytes FIRST; only when the resulting top byte's
        // high bit is CLEAR (the magnitude would read as positive) is a 0xFF
        // sign byte appended. NEVER pre-push a byte from the magnitude's high
        // bit -- that over-pads boundary magnitudes like -128 (0x80, already
        // a negative-signed byte) into 0x80-0xFF.
        ApplyTwosComplement(bytes);
        if ((bytes.back() & 0x80) == 0)
        {
            bytes.push_back(0xFF);
        }
    }
    Il2CppArray* arr = il2cpp_array_new(nullptr, 1, false,
                                        static_cast<int32_t>(bytes.size()));
    std::memcpy(arr->data, bytes.data(), bytes.size());
    return arr;
}

int64_t icall_biginteger_to_int64(Il2CppBigInteger a)
{
    if (a.state->limbs.size() > 2)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for an Int64.");
    }
    uint64_t mag = 0;
    for (size_t i = a.state->limbs.size(); i-- > 0;)
    {
        mag = (mag << 32) | a.state->limbs[i];
    }
    if (a.state->negative)
    {
        if (mag > 0x8000000000000000ull)
        {
            il2cpp_throw_overflow_exception(
                "Value was either too large or too small for an Int64.");
        }
        // mag == 2^63 exactly maps to Int64.MinValue.
        return mag == 0x8000000000000000ull
            ? INT64_MIN
            : -static_cast<int64_t>(mag);
    }
    if (mag > 0x7FFFFFFFFFFFFFFFull)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for an Int64.");
    }
    return static_cast<int64_t>(mag);
}

uint32_t icall_biginteger_to_uint32(Il2CppBigInteger a)
{
    if (a.state->negative)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for a UInt32.");
    }
    if (a.state->limbs.size() > 1)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for a UInt32.");
    }
    return a.state->limbs.empty() ? 0 : a.state->limbs[0];
}

uint64_t icall_biginteger_to_uint64(Il2CppBigInteger a)
{
    // net48 (ulong)BigInteger: truncate toward zero; the range is
    // [0, ulong.MaxValue]. A negative magnitude always overflows; any
    // magnitude past 2^64-1 overflows (2 limbs max for the valid range).
    if (a.state->negative)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for a UInt64.");
    }
    if (a.state->limbs.size() > 2)
    {
        il2cpp_throw_overflow_exception(
            "Value was either too large or too small for a UInt64.");
    }
    uint64_t mag = 0;
    for (size_t i = a.state->limbs.size(); i-- > 0;)
    {
        mag = (mag << 32) | a.state->limbs[i];
    }
    return mag;
}

}} // namespace il2cpp::runtime
