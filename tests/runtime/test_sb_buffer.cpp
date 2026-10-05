//------------------------------------------------------------------------------
// test_sb_buffer.cpp -- StringBuilder.ReplaceBufferInternal /
// ReplaceBufferAnsiInternal unit tests (P6 net48 internal helpers).
//
// Coverage (normal + edge + error paths):
//   1. replace_buffer_internal: replaces the ENTIRE content (a shorter
//      buffer truncates -- never appends), returns the SAME builder
//      (chaining), and a short buffer FITS without growth (capacity and
//      the chars array are untouched).
//   2. Null buffer clears: length -> 0, capacity UNCHANGED, ToString ""
//      (both the wide and the ANSI entry point).
//   3. Exact-fit boundary: len == capacity does NOT grow (the grow check is
//      strictly len > capacity).
//   4. Long buffer grows via the shared GrowBuffer path (net48 Grow policy:
//      newCapacity = max(required, oldCapacity * 2)):
//        - required wins:   capacity 4  + 10 units -> 10;
//        - doubling wins:   capacity 16 + 20 units -> 32.
//      The fresh array is sized exactly to the new capacity and the content
//      is fully preserved.
//   5. replace_buffer_ansi_internal: each byte widens to its char via
//      ZERO-extension (static_cast<uint8_t>): byte 0x80 -> U+0080,
//      byte 0xFF -> U+00FF (sign extension would produce 0xFF80/0xFFFF);
//      ASCII passes through; null clears; the grow path matches section 4.
//   6. Capacity invariant (the fcap field-read path): generated code reads
//      sb.Capacity as a DIRECT struct-field load
//      ((Il2CppStringBuilder*)obj)->capacity (CodeGenerator.cs EmitIcallCallArgs),
//      so every assertion below reads the capacity FIELD itself and checks
//      capacity >= length AND chars->length == capacity after EVERY op.
//   7. Null receiver -> NullReferenceException on both entry points.
//
// Plain executable + minimal assertion harness (see test_stringbuilder.cpp):
// any failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_stringbuilder.h"

#include "il2cpp_exception.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "stringbuilder test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Expect 'obj' (a managed string) to hold exactly the ASCII literal 'ascii'.
void ExpectString(il2cpp::Il2CppObject* obj, const char* ascii, const char* what)
{
    const il2cpp::Il2CppString* s = reinterpret_cast<const il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (s == nullptr)
    {
        Expect(false, what);
        return;
    }
    if (static_cast<size_t>(s->length) != len)
    {
        Expect(false, what);
        return;
    }
    bool match = true;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// Expect the builder's UTF-16 payload to start with exactly 'count' units
// from 'expected' (direct struct-field reads -- the same shape the fcap
// collapse emits for sb.Capacity).
void ExpectUnits(const il2cpp::Il2CppStringBuilder* sb, const char16_t* expected, int32_t count, const char* what)
{
    if (sb->length != count || sb->chars == nullptr)
    {
        Expect(false, what);
        return;
    }
    const char16_t* units = reinterpret_cast<const char16_t*>(sb->chars->data);
    bool match = true;
    for (int32_t i = 0; i < count; ++i)
    {
        if (units[i] != expected[i])
        {
            match = false;
            break;
        }
    }
    Expect(match, what);
}

// The post-op invariant checked after EVERY mutation below: the capacity
// FIELD (read directly, mirroring the generated fcap field-load) is at least
// the used length, and the backing array is sized exactly to the capacity.
void ExpectInvariant(const il2cpp::Il2CppStringBuilder* sb, const char* what)
{
    Expect(sb->capacity >= sb->length && sb->chars != nullptr && sb->chars->length == sb->capacity, what);
}

// Run 'fn' expecting it to throw a managed exception whose TYPE NAME (the
// carrier's full "{Type},{Base}" chain) equals 'typeChain'.
template <typename Fn>
void ExpectThrows(Fn fn, const char* typeChain, const char* what)
{
    bool matched = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        matched = (std::strcmp(e.type_name(), typeChain) == 0);
    }
    Expect(matched, what);
}

// Allocate a fresh StringBuilder via the generated-code shape:
// il2cpp_object_new(klass) then the ctor icall.
il2cpp::Il2CppStringBuilder* NewDefault()
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_default(sb);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

il2cpp::Il2CppStringBuilder* NewCapacity(int32_t capacity)
{
    il2cpp::Il2CppObject* sb = il2cpp::il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class());
    il2cpp::runtime::icall_stringbuilder_ctor_capacity(sb, capacity);
    return reinterpret_cast<il2cpp::Il2CppStringBuilder*>(sb);
}

} // namespace

int main()
{
    using il2cpp::Il2CppObject;
    using il2cpp::Il2CppStringBuilder;
    using il2cpp::runtime::icall_stringbuilder_replace_buffer_internal;
    using il2cpp::runtime::icall_stringbuilder_replace_buffer_ansi_internal;
    using il2cpp::runtime::icall_stringbuilder_to_string;

    const char* kNre = "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

    // --- 1. replace_buffer_internal: full replacement, chaining, no-growth fit ---
    Il2CppStringBuilder* rb = NewDefault();
    Il2CppObject* ret = icall_stringbuilder_replace_buffer_internal(
        reinterpret_cast<Il2CppObject*>(rb), u"xyz");
    Expect(ret == reinterpret_cast<Il2CppObject*>(rb),
           "replace_buffer_internal returns the SAME builder (chaining)");
    Expect(rb->length == 3 && rb->capacity == 16,
           "replace_buffer_internal(\"xyz\") sets length 3, capacity stays 16");
    ExpectUnits(rb, u"xyz", 3, "replace_buffer_internal writes the buffer units");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rb)), "xyz",
                 "replace_buffer_internal content is \"xyz\"");
    ExpectInvariant(rb, "invariant holds after the first replace");

    // A SHORTER buffer TRUNCATES: the whole content is replaced, never appended.
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(rb), u"ab");
    Expect(rb->length == 2, "shorter buffer truncates the length to 2");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rb)), "ab",
                 "shorter buffer REPLACES the content (no \"abxyz\" residue)");
    Expect(rb->capacity == 16, "fitting replace keeps the capacity (16)");
    ExpectInvariant(rb, "invariant holds after the truncating replace");

    // --- 2. null buffer clears ---
    Il2CppObject* clearRet = icall_stringbuilder_replace_buffer_internal(
        reinterpret_cast<Il2CppObject*>(rb), nullptr);
    Expect(clearRet == reinterpret_cast<Il2CppObject*>(rb),
           "replace_buffer_internal(null) returns the SAME builder");
    Expect(rb->length == 0, "replace_buffer_internal(null) clears the length to 0");
    Expect(rb->capacity == 16, "replace_buffer_internal(null) keeps the capacity");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(rb)), "",
                 "replace_buffer_internal(null) yields the empty string");
    ExpectInvariant(rb, "invariant holds after the null clear");
    // Clearing an already-empty builder is a no-op.
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(rb), nullptr);
    Expect(rb->length == 0 && rb->capacity == 16,
           "replace_buffer_internal(null) on an empty builder is a no-op");

    // --- 3. exact-fit boundary: len == capacity does NOT grow ---
    Il2CppStringBuilder* fit = NewCapacity(4);
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(fit), u"abcd");
    Expect(fit->length == 4 && fit->capacity == 4,
           "exact-fit replace (len == capacity 4) does NOT grow");
    Expect(fit->chars->length == 4, "exact-fit keeps the original 4-unit array");
    ExpectUnits(fit, u"abcd", 4, "exact-fit content is intact");
    ExpectInvariant(fit, "invariant holds after the exact-fit replace");

    // --- 4. long buffer grows via the GrowBuffer path ---
    // Branch A (required wins): capacity 4 + 10 units -> max(10, 4 * 2) = 10.
    Il2CppStringBuilder* gr = NewCapacity(4);
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(gr), u"0123456789");
    Expect(gr->length == 10 && gr->capacity == 10,
           "grow policy (required wins): capacity 4 -> 10 (net48 baseline)");
    Expect(gr->chars->length == 10, "grown array is sized exactly to the new capacity (10)");
    ExpectUnits(gr, u"0123456789", 10, "growth preserves the full written content");
    ExpectString(icall_stringbuilder_to_string(reinterpret_cast<Il2CppObject*>(gr)), "0123456789",
                 "ToString after growth is \"0123456789\"");
    ExpectInvariant(gr, "invariant holds after the required-wins growth");

    // Branch B (doubling wins): capacity 16 + 20 units -> max(20, 16 * 2) = 32.
    Il2CppStringBuilder* gd = NewDefault();
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(gd),
                                                u"01234567890123456789");
    Expect(gd->length == 20 && gd->capacity == 32,
           "grow policy (doubling wins): capacity 16 -> 32 (net48 baseline)");
    Expect(gd->chars->length == 32, "grown array is sized exactly to the doubled capacity (32)");
    ExpectUnits(gd, u"01234567890123456789", 20, "doubling growth preserves the 20-unit content");
    ExpectInvariant(gd, "invariant holds after the doubling growth");

    // After growth, a fitting replace keeps the GROWN capacity (no shrink).
    icall_stringbuilder_replace_buffer_internal(reinterpret_cast<Il2CppObject*>(gr), u"xy");
    Expect(gr->length == 2 && gr->capacity == 10,
           "post-growth fitting replace keeps the grown capacity (10)");
    ExpectInvariant(gr, "invariant holds after the post-growth replace");

    // --- 5. replace_buffer_ansi_internal ---
    // ASCII passes through unchanged.
    Il2CppStringBuilder* an = NewDefault();
    Il2CppObject* anRet = icall_stringbuilder_replace_buffer_ansi_internal(
        reinterpret_cast<Il2CppObject*>(an), reinterpret_cast<const int8_t*>("AB"));
    Expect(anRet == reinterpret_cast<Il2CppObject*>(an),
           "replace_buffer_ansi_internal returns the SAME builder (chaining)");
    Expect(an->length == 2 && an->capacity == 16,
           "ANSI \"AB\" sets length 2, capacity stays 16");
    ExpectUnits(an, u"AB", 2, "ANSI ASCII bytes map to identical UTF-16 units");
    ExpectInvariant(an, "invariant holds after the ANSI replace");

    // Widen semantics: each byte widens by ZERO-extension through
    // static_cast<uint8_t> -- byte 0x80 -> U+0080, 0xFF -> U+00FF (sign
    // extension would produce 0xFF80 / 0xFFFF).
    const int8_t wide[] = { 'A', static_cast<int8_t>(0x80), static_cast<int8_t>(0xFF), 0 };
    icall_stringbuilder_replace_buffer_ansi_internal(reinterpret_cast<Il2CppObject*>(an), wide);
    Expect(an->length == 3, "ANSI high-byte buffer sets length 3");
    const char16_t widened[] = { u'A', 0x80, 0xFF, u'\0' };
    ExpectUnits(an, widened, 3,
                "ANSI widen is zero-extension: 0x80 -> U+0080, 0xFF -> U+00FF");
    ExpectInvariant(an, "invariant holds after the widening replace");

    // Null buffer clears (same contract as the wide entry point).
    Il2CppObject* anClear = icall_stringbuilder_replace_buffer_ansi_internal(
        reinterpret_cast<Il2CppObject*>(an), nullptr);
    Expect(anClear == reinterpret_cast<Il2CppObject*>(an),
           "replace_buffer_ansi_internal(null) returns the SAME builder");
    Expect(an->length == 0 && an->capacity == 16,
           "replace_buffer_ansi_internal(null) clears the length, keeps the capacity");
    ExpectInvariant(an, "invariant holds after the ANSI null clear");

    // Empty (non-null) ANSI buffer: a zero-length replacement.
    icall_stringbuilder_replace_buffer_ansi_internal(reinterpret_cast<Il2CppObject*>(an),
                                                     reinterpret_cast<const int8_t*>(""));
    Expect(an->length == 0 && an->capacity == 16,
           "ANSI empty buffer sets length 0 without growth");

    // Grow path: capacity 4 + 10 ANSI bytes -> max(10, 4 * 2) = 10.
    Il2CppStringBuilder* ag = NewCapacity(4);
    icall_stringbuilder_replace_buffer_ansi_internal(reinterpret_cast<Il2CppObject*>(ag),
                                                     reinterpret_cast<const int8_t*>("0123456789"));
    Expect(ag->length == 10 && ag->capacity == 10,
           "ANSI grow policy: capacity 4 -> 10 (net48 baseline)");
    Expect(ag->chars->length == 10, "ANSI grown array is sized exactly to the new capacity (10)");
    ExpectUnits(ag, u"0123456789", 10, "ANSI growth preserves the widened content");
    ExpectInvariant(ag, "invariant holds after the ANSI growth");

    // --- 6. capacity field-read sweep (fcap path): re-check every builder ---
    {
        struct Case { const Il2CppStringBuilder* sb; const char* what; };
        const Case cases[] = {
            { rb, "sweep: rb capacity >= length, array sized to capacity" },
            { fit, "sweep: fit capacity >= length, array sized to capacity" },
            { gr, "sweep: gr capacity >= length, array sized to capacity" },
            { gd, "sweep: gd capacity >= length, array sized to capacity" },
            { an, "sweep: an capacity >= length, array sized to capacity" },
            { ag, "sweep: ag capacity >= length, array sized to capacity" },
        };
        for (const Case& c : cases)
        {
            ExpectInvariant(c.sb, c.what);
        }
    }

    // --- 7. Null receivers ---
    ExpectThrows([] { (void)icall_stringbuilder_replace_buffer_internal(nullptr, u"a"); }, kNre,
                 "replace_buffer_internal on a null receiver throws NullReferenceException");
    ExpectThrows([] { (void)icall_stringbuilder_replace_buffer_ansi_internal(nullptr, reinterpret_cast<const int8_t*>("a")); }, kNre,
                 "replace_buffer_ansi_internal on a null receiver throws NullReferenceException");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d sb_buffer assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all sb_buffer tests passed\n");
    return 0;
}
