//------------------------------------------------------------------------------
// test_pinvoke_utf16.cpp -- UTF-16 (WCHAR*) P/Invoke marshalling runtime
// unit tests (WinForms stage-1 gap #6: CharSet.Auto 479 P/Invokes all use
// the W-entry variants).
//
// Coverage (normal + edge paths):
//   1. il2cpp_pinvoke_string_to_utf16_alloc copies the managed string into a
//      heap char16_t buffer with a trailing NUL unit; from_utf16 decodes it
//      back -- a full round trip.
//   2. Byte-exact UTF-16LE encoding on little-endian hosts: U+4E2D (0x4E2D)
//      marshals as the byte sequence 2D 4E.
//   3. Embedded NUL semantics: the alloc side is LENGTH-based and preserves
//      an embedded NUL unit verbatim; from_utf16 (null-terminated scan)
//      stops at the first NUL (PtrToStringUni(IntPtr) semantics); the
//      explicit-length from_utf16_len preserves the embedded NUL
//      (PtrToStringUni(IntPtr, int) semantics).
//   4. Null/edge inputs: to_utf16_alloc(nullptr) -> NULL (net48 passes NULL
//      to the callee for a null managed string);
//      from_utf16(nullptr) -> the empty string; from_utf16_len(nullptr,0)
//      and a negative length -> the empty string.
//   5. Very long input (50k units -- 100k bytes, far beyond the old 4096-byte
//      rotating buffer): the alloc copy is unbounded and round trips exactly.
//   6. StringToHGlobalUni integration: the existing hglobal Uni encoder
//      (marshalext2.cpp) produces a UTF-16 buffer that from_utf16 decodes
//      back, and the buffer is released through the hglobal free icall --
//      the exact wiring shape the MethodMap icalls will use.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke_ext.h"
#include "il2cpp_marshal_ext2.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Byte-exact comparison of a char16_t buffer against expected bytes
// (verifies the on-memory little-endian representation).
bool UnitBytesMatch(const char16_t* s, size_t unitCount, const unsigned char* expectedBytes)
{
    return std::memcmp(s, expectedBytes, unitCount * sizeof(char16_t)) == 0;
}

} // namespace

int main()
{
    // --- 1. ASCII round trip via the UTF-16 alloc + decode pair ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"hello", 5);
        char16_t* buf = il2cpp::il2cpp_pinvoke_string_to_utf16_alloc(s);
        Expect(buf != nullptr, "to_utf16_alloc returns a non-null buffer");
        Expect(std::memcmp(buf, u"hello", 5 * sizeof(char16_t)) == 0,
               "ASCII string copies into the UTF-16 buffer 1:1");
        Expect(buf[5] == u'\0', "UTF-16 buffer carries a trailing NUL unit");

        il2cpp::Il2CppString* back = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16(buf));
        Expect(back != nullptr, "from_utf16 yields a managed string");
        Expect(back->length == 5, "from_utf16 decodes the correct unit count");
        Expect(back->chars[0] == u'h' && back->chars[4] == u'o',
               "from_utf16 decodes the correct units");
        il2cpp::il2cpp_pinvoke_utf16_free(buf);
    }

    // --- 2. Byte-exact UTF-16LE encoding (little-endian host) ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"\u4E2D", 1); // CJK 'zhong', 0x4E2D
        char16_t* buf = il2cpp::il2cpp_pinvoke_string_to_utf16_alloc(s);
        const unsigned char kZhongLE[] = { 0x2D, 0x4E }; // UTF-16LE of U+4E2D
        Expect(UnitBytesMatch(buf, 1, kZhongLE),
               "U+4E2D marshals as the little-endian byte pair 2D 4E");
        il2cpp::il2cpp_pinvoke_utf16_free(buf);
    }

    // --- 3. Embedded NUL semantics ---
    {
        const char16_t embedded[] = { u'a', u'\0', u'c' };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(embedded, 3);
        char16_t* buf = il2cpp::il2cpp_pinvoke_string_to_utf16_alloc(s);
        Expect(buf[0] == u'a' && buf[1] == u'\0' && buf[2] == u'c' && buf[3] == u'\0',
               "alloc preserves an embedded NUL unit verbatim + trailing NUL");

        // Null-terminated decode stops at the first NUL (PtrToStringUni(IntPtr)).
        il2cpp::Il2CppString* truncated = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16(buf));
        Expect(truncated->length == 1 && truncated->chars[0] == u'a',
               "from_utf16 stops at the first NUL unit");

        // Explicit-length decode preserves the embedded NUL (PtrToStringUni(IntPtr,int)).
        il2cpp::Il2CppString* full = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16_len(buf, 3));
        Expect(full->length == 3 && full->chars[0] == u'a'
               && full->chars[1] == u'\0' && full->chars[2] == u'c',
               "from_utf16_len preserves the embedded NUL unit");
        il2cpp::il2cpp_pinvoke_utf16_free(buf);
    }

    // --- 4. Null / edge inputs ---
    {
        char16_t* empty = il2cpp::il2cpp_pinvoke_string_to_utf16_alloc(nullptr);
        Expect(empty == nullptr, "null input yields NULL (net48 null string)");
        il2cpp::il2cpp_pinvoke_utf16_free(empty);

        il2cpp::Il2CppString* none = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16(nullptr));
        Expect(none != nullptr && none->length == 0,
               "from_utf16(nullptr) yields the empty string");

        il2cpp::Il2CppString* noneLen = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16_len(nullptr, 0));
        Expect(noneLen != nullptr && noneLen->length == 0,
               "from_utf16_len(nullptr, 0) yields the empty string");

        const char16_t abc[] = { u'a', u'b', u'c' };
        il2cpp::Il2CppString* neg = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16_len(abc, -7));
        Expect(neg != nullptr && neg->length == 0,
               "from_utf16_len with a negative length clamps to the empty string");
    }

    // --- 5. Very long input (50k units -- far beyond the 4096-byte rotating
    //         UTF-8 buffer; the alloc copy is unbounded) ---
    {
        const int32_t kLongLen = 50000;
        char16_t* longChars = static_cast<char16_t*>(std::malloc(static_cast<size_t>(kLongLen) * sizeof(char16_t)));
        Expect(longChars != nullptr, "long-input test buffer allocates");
        for (int32_t i = 0; i < kLongLen; ++i)
        {
            longChars[i] = static_cast<char16_t>(u'a' + (i % 26));
        }
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(longChars, kLongLen);
        char16_t* buf = il2cpp::il2cpp_pinvoke_string_to_utf16_alloc(s);
        Expect(buf[kLongLen] == u'\0', "long buffer carries a trailing NUL unit");
        Expect(std::memcmp(buf, longChars, static_cast<size_t>(kLongLen) * sizeof(char16_t)) == 0,
               "long buffer copies every unit verbatim");

        il2cpp::Il2CppString* back = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16_len(buf, kLongLen));
        Expect(back->length == kLongLen, "long buffer round trips its exact unit count");
        Expect(back->chars[0] == longChars[0] && back->chars[kLongLen - 1] == longChars[kLongLen - 1],
               "long buffer round trips its boundary units");
        il2cpp::il2cpp_pinvoke_utf16_free(buf);
        std::free(longChars);
    }

    // --- 6. StringToHGlobalUni integration: hglobal encoder -> from_utf16
    //         decode -> hglobal free (the wired icall shape) ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"winforms", 8);
        void* hg = il2cpp::runtime::il2cpp_marshal_string_to_hglobal_uni(s);
        Expect(hg != nullptr, "StringToHGlobalUni allocates a non-null buffer");
        auto* units = static_cast<char16_t*>(hg);
        Expect(std::memcmp(units, u"winforms", 8 * sizeof(char16_t)) == 0,
               "hglobal Uni buffer holds the exact UTF-16 units");
        Expect(units[8] == u'\0', "hglobal Uni buffer carries a trailing NUL unit");

        il2cpp::Il2CppString* back = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf16(units));
        Expect(back->length == 8 && back->chars[0] == u'w' && back->chars[7] == u's',
               "from_utf16 decodes the hglobal Uni buffer back to the source string");

        il2cpp::runtime::icall_marshal_free_hglobal(hg);
    }

    // --- 7. StringToHGlobalUni icall shape: intptr_t carrier, released via
    //         the hglobal free icall; null -> IntPtr.Zero ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"x", 1);
        intptr_t hg = il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal(s);
        Expect(hg != 0, "hglobal icall wrapper returns a non-zero intptr_t carrier");
        auto* units = reinterpret_cast<char16_t*>(hg);
        Expect(units[0] == u'x' && units[1] == u'\0',
               "hglobal icall wrapper buffer holds the UTF-16 units + NUL");
        il2cpp::runtime::icall_marshal_free_hglobal(reinterpret_cast<void*>(hg));

        Expect(il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal(nullptr) == 0,
               "hglobal icall wrapper: null input -> IntPtr.Zero (0)");
    }

#if defined(_WIN32)
    // --- 8. StringToHGlobalUni Windows GlobalAlloc roundtrip (clipboard
    //         protocol: SetClipboardData takes ownership of the handle and
    //         later frees it with GlobalFree, so the buffer MUST be a real
    //         GlobalAlloc object -- GlobalFree on a malloc'd pointer is
    //         heap corruption 0xC0000374) ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"clip", 4);
        intptr_t hg = il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal(s);
        Expect(hg != 0, "hglobal icall allocates a non-zero handle");
        // GlobalSize reports the allocation size only for a genuine Global
        // heap object (0 for a foreign pointer). The (len+1)*2-byte payload
        // must be present.
        const SIZE_T gsize = GlobalSize(reinterpret_cast<void*>(hg));
        Expect(gsize >= static_cast<SIZE_T>(5 * sizeof(char16_t)),
               "hglobal icall result is a genuine GlobalAlloc object (GlobalSize >= 10)");
        // The handle is a DIRECT pointer (GMEM_FIXED): content is readable
        // without GlobalLock, matching net48 StringToHGlobalUni/AllocHGlobal.
        auto* units = reinterpret_cast<char16_t*>(hg);
        Expect(units[0] == u'c' && units[1] == u'l' && units[2] == u'i' &&
               units[3] == u'p' && units[4] == u'\0',
               "GlobalAlloc handle holds the UTF-16 units + trailing NUL");
        // Release through the shared hglobal free (GlobalFree on Windows).
        il2cpp::runtime::icall_marshal_free_hglobal(reinterpret_cast<void*>(hg));
        // A second free of the same handle is undefined (single-use
        // contract, .NET FreeHGlobal) -- NOT exercised here.
        Expect(il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal(nullptr) == 0,
               "null input -> IntPtr.Zero (0) after the Windows roundtrip");
    }
#endif

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvoke_utf16 assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke_utf16 tests passed\n");
    return 0;
}
