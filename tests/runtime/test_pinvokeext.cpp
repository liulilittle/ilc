//------------------------------------------------------------------------------
// test_pinvokeext.cpp -- P/Invoke extension runtime unit tests (M4 parallel).
//
// Coverage (normal + edge paths):
//   1. il2cpp_pinvoke_string_to_utf8_alloc transcodes ASCII 1:1 and the
//      buffer is heap-allocated (freed via il2cpp_pinvoke_utf8_free).
//   2. Multi-byte encode side is byte-exact: 2-byte (U+00E9), 3-byte (U+4E2D)
//      and 4-byte astral (U+1F600 via a surrogate pair) sequences. NOTE: the
//      DECODE side (il2cpp_string_new_from_utf8) is full UTF-8 with the net48
//      replacement fallback (string.cpp), so astral round trips are exact.
//   3. A lone (unpaired) surrogate encodes as the UTF-8 replacement char
//      U+FFFD (EF BF BD) -- .NET marshalling semantics.
//   4. Null string input marshals as an empty heap buffer (caller frees).
//   5. Return-value path: il2cpp_pinvoke_string_from_utf8 (existing pinvoke.h
//      function) yields a managed string; encoding it back round trips.
//   6. Callback table: register -> find returns the same pointer, the pointer
//      is callable, a missing name returns null, and re-registering the same
//      name REPLACES the previous entry (last registration wins).
//   7. il2cpp_pinvoke_clear_last_error resets the OS error slot (GetLastError
//      on Windows / errno on POSIX) to zero -- the P/Invoke pre-call cleanup;
//      a subsequent capture therefore observes zero.
//   8. Coexistence regression: the existing static-buffer
//      il2cpp_pinvoke_string_to_utf8 still works unchanged -- and now matches
//      the alloc encoder byte-for-byte on the astral / lone-surrogate paths
//      (pinv2 audit: the old static-buffer code emitted ill-formed UTF-8 for
//      surrogate code points).
//   9. Per-thread last-error isolation (net48 Marshal.GetLastWin32Error is
//      per-thread): two threads each capture their OWN OS error and neither
//      observes the other's -- the pinv2 audit fix (g_lastError is now
//      thread_local, matching il2cpp_pinvoke.h:54's documented "per-thread
//      slot").
//   10. byte[] buffer parameters (GetKeyboardState(byte[256]) form):
//       il2cpp_pinvoke_byte_array_data returns the array's inline payload
//       ADDRESS, native writes through it are observed in place (no copy-back),
//       and a null byte[] marshals as nullptr (.NET NULL-pointer pass-through
//       to the native callee, not an abort). The elemSize!=1 abort path is a
//       transpiler-emission bug exercised by the crash-probe e2e, not here.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_pinvoke_ext.h"
#include "il2cpp_pinvoke.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <thread>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
    #define TEST_SET_OS_ERROR(code) SetLastError(static_cast<DWORD>(code))
    #define TEST_GET_OS_ERROR() static_cast<int32_t>(GetLastError())
#else
    #include <cerrno>
    #define TEST_SET_OS_ERROR(code) errno = (code)
    #define TEST_GET_OS_ERROR() errno
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

// Byte-exact comparison of a null-terminated buffer against expected bytes.
bool BytesMatch(const char* s, const unsigned char* expected, size_t n)
{
    return std::strlen(s) == n && std::memcmp(s, expected, n) == 0;
}

// --- Callback test functions (registered via the callback table) ---
int32_t Twice(int32_t v)
{
    return v * 2;
}

int32_t Triple(int32_t v)
{
    return v * 3;
}

} // namespace

int main()
{
    // --- 1. ASCII transcode + heap allocation contract ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"hello", 5);
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        Expect(buf != nullptr, "to_utf8_alloc returns a non-null buffer");
        Expect(std::strcmp(buf, "hello") == 0, "ASCII string transcodes 1:1");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }

    // --- 2. Multi-byte encode is byte-exact (2/3/4-byte sequences) ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"\u00E9", 1); // e-acute
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        const unsigned char kEAcute[] = { 0xC3, 0xA9 };
        Expect(BytesMatch(buf, kEAcute, sizeof(kEAcute)),
               "U+00E9 encodes as 2-byte UTF-8 C3 A9");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"\u4E2D", 1); // CJK 'zhong'
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        const unsigned char kZhong[] = { 0xE4, 0xB8, 0xAD };
        Expect(BytesMatch(buf, kZhong, sizeof(kZhong)),
               "U+4E2D encodes as 3-byte UTF-8 E4 B8 AD");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }
    {
        // U+1F600 (grinning face) -- surrogate pair D83D DE00 -> 4-byte UTF-8.
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"\U0001F600", 2);
        Expect(s->length == 2, "astral char occupies two UTF-16 units");
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        const unsigned char kGrin[] = { 0xF0, 0x9F, 0x98, 0x80 };
        Expect(BytesMatch(buf, kGrin, sizeof(kGrin)),
               "U+1F600 surrogate pair encodes as 4-byte UTF-8 F0 9F 98 80");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }

    // --- 3. Lone surrogate -> U+FFFD replacement (EF BF BD) ---
    {
        // A lone high surrogate cannot be spelled as a universal character
        // name (the standard forbids UCNs for surrogate code points), so it
        // is constructed as an explicit UTF-16 unit array.
        const char16_t lone[] = { 0xD800, 0 };
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(lone, 1);
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        const unsigned char kReplacement[] = { 0xEF, 0xBF, 0xBD };
        Expect(BytesMatch(buf, kReplacement, sizeof(kReplacement)),
               "lone high surrogate encodes as U+FFFD (EF BF BD)");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }

    // --- 4. Null input -> empty heap buffer (uniform caller-free contract) ---
    {
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(nullptr);
        Expect(buf != nullptr, "null input yields a non-null buffer");
        Expect(std::strcmp(buf, "") == 0, "null input yields the empty string");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }

    // --- 5. Return-value path: UTF-8 -> managed string -> round trip ---
    {
        il2cpp::Il2CppString* back = reinterpret_cast<il2cpp::Il2CppString*>(
            il2cpp::il2cpp_pinvoke_string_from_utf8("abc"));
        Expect(back != nullptr, "from_utf8 yields a managed string");
        Expect(back->length == 3, "from_utf8 decodes the correct unit count");
        Expect(back->chars[0] == u'a' && back->chars[1] == u'b' && back->chars[2] == u'c',
               "from_utf8 decodes the correct units");
        char* buf = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(back);
        Expect(std::strcmp(buf, "abc") == 0, "round trip: string -> utf8 == source");
        il2cpp::il2cpp_pinvoke_utf8_free(buf);
    }

    // --- 6. Callback table: register / find / replace / miss ---
    {
        il2cpp::icall_pinvoke_register_callback("managed.twice", reinterpret_cast<void*>(&Twice));
        void* found = il2cpp::il2cpp_pinvoke_find_callback("managed.twice");
        Expect(found == reinterpret_cast<void*>(&Twice),
               "find returns the registered callback pointer");
        auto fn = reinterpret_cast<int32_t (*)(int32_t)>(found);
        Expect(fn(21) == 42, "registered callback is callable through the pointer");

        Expect(il2cpp::il2cpp_pinvoke_find_callback("managed.nope") == nullptr,
               "find on an unregistered name returns null");

        // Re-registering the same name replaces the previous entry.
        il2cpp::icall_pinvoke_register_callback("managed.twice", reinterpret_cast<void*>(&Triple));
        void* replaced = il2cpp::il2cpp_pinvoke_find_callback("managed.twice");
        Expect(replaced == reinterpret_cast<void*>(&Triple),
               "re-registering the same name replaces the entry");
        auto fn2 = reinterpret_cast<int32_t (*)(int32_t)>(replaced);
        Expect(fn2(21) == 63, "replaced callback is the new function");
    }

    // --- 7. clear_last_error: OS error slot resets to zero ---
    {
        TEST_SET_OS_ERROR(999);
        il2cpp::il2cpp_pinvoke_capture_last_error();
        Expect(il2cpp::il2cpp_pinvoke_get_last_error() == 999,
               "capture observes the OS error set by the callee");

        il2cpp::il2cpp_pinvoke_clear_last_error();
        Expect(TEST_GET_OS_ERROR() == 0,
               "clear_last_error resets the OS error slot to zero");

        // A capture AFTER the clear observes zero -- the pre-call cleanup
        // contract (a callee that sets nothing leaves zero behind).
        il2cpp::il2cpp_pinvoke_capture_last_error();
        Expect(il2cpp::il2cpp_pinvoke_get_last_error() == 0,
               "capture after clear observes zero");
    }

    // --- 8. Coexistence: the existing static-buffer marshal still works ---
    {
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"hi", 2);
        const char* legacy = il2cpp::il2cpp_pinvoke_string_to_utf8(
            reinterpret_cast<il2cpp::Il2CppObject*>(s));
        Expect(std::strcmp(legacy, "hi") == 0,
               "existing il2cpp_pinvoke_string_to_utf8 unchanged");
    }

    // --- 8b. pinv2: the static-buffer encoder matches the alloc encoder on
    //          the astral (surrogate PAIR) and lone-surrogate paths ---
    {
        // Astral char U+1F600 (surrogate pair D83D DE00) -> 4-byte UTF-8.
        il2cpp::Il2CppString* s = il2cpp::il2cpp_string_new(u"\U0001F600", 2);
        const char* legacy = il2cpp::il2cpp_pinvoke_string_to_utf8(
            reinterpret_cast<il2cpp::Il2CppObject*>(s));
        const unsigned char kGrin[] = { 0xF0, 0x9F, 0x98, 0x80 };
        Expect(BytesMatch(legacy, kGrin, sizeof(kGrin)),
               "static-buffer marshal encodes an astral pair as 4-byte UTF-8 "
               "(NOT two ill-formed 3-byte surrogates)");

        // The alloc encoder agrees byte-for-byte on the astral path -- compare
        // NOW, before the next to_utf8 call reuses the rotating buffer that
        // 'legacy' points into (the buffer is a shared thread_local slot).
        char* alloc = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(s);
        Expect(std::strcmp(legacy, alloc) == 0,
               "static-buffer and alloc encoders agree on astral bytes");
        il2cpp::il2cpp_pinvoke_utf8_free(alloc);

        // Lone (unpaired) surrogate -> U+FFFD (EF BF BD), never ill-formed.
        const char16_t lone[] = { 0xD800, 0 };
        il2cpp::Il2CppString* loneStr = il2cpp::il2cpp_string_new(lone, 1);
        const char* legacyLone = il2cpp::il2cpp_pinvoke_string_to_utf8(
            reinterpret_cast<il2cpp::Il2CppObject*>(loneStr));
        const unsigned char kReplacement[] = { 0xEF, 0xBF, 0xBD };
        Expect(BytesMatch(legacyLone, kReplacement, sizeof(kReplacement)),
               "static-buffer marshal encodes a lone surrogate as U+FFFD");
        char* allocLone = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(loneStr);
        Expect(std::strcmp(legacyLone, allocLone) == 0,
               "static-buffer and alloc encoders agree on lone-surrogate bytes");
        il2cpp::il2cpp_pinvoke_utf8_free(allocLone);
    }

    // --- 9. pinv2: per-thread last-error isolation (net48 semantics) ---
    {
        TEST_SET_OS_ERROR(111);
        il2cpp::il2cpp_pinvoke_capture_last_error();
        Expect(il2cpp::il2cpp_pinvoke_get_last_error() == 111,
               "main thread captures its own OS error");

        int32_t workerRead = -1;
        int32_t mainAfterWorker = -1;
        std::thread worker([&workerRead]() {
            TEST_SET_OS_ERROR(222);
            il2cpp::il2cpp_pinvoke_capture_last_error();
            workerRead = il2cpp::il2cpp_pinvoke_get_last_error();
        });
        worker.join();

        // The worker's capture must NOT have clobbered the main thread's
        // slot (the pre-fix global g_lastError would have overwritten it).
        mainAfterWorker = il2cpp::il2cpp_pinvoke_get_last_error();
        Expect(workerRead == 222, "worker thread reads its own captured error");
        Expect(mainAfterWorker == 111,
               "main thread's captured error survives another thread's capture "
               "(Marshal.GetLastWin32Error is per-thread)");
    }

    // --- 10. byte[] buffer parameters (GetKeyboardState(byte[256]) form) ---
    {
        // The helper hands out the ADDRESS of the array's inline payload:
        // writing through it is observed by the managed array (no copy-back).
        il2cpp::Il2CppArray* bytes = il2cpp::il2cpp_array_new(nullptr, 1, false, 4);
        uint8_t* data = il2cpp::il2cpp_pinvoke_byte_array_data(bytes);
        Expect(data == reinterpret_cast<uint8_t*>(bytes->data),
               "byte[] payload address is the array's inline data");
        data[0] = 0x11;
        data[1] = 0x22;
        data[2] = 0x33;
        data[3] = 0x44;
        Expect(*reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(bytes, 0)) == 0x11
               && *reinterpret_cast<uint8_t*>(il2cpp::il2cpp_array_get_elem_ptr(bytes, 3)) == 0x44,
               "native writes through the buffer are observed by the array in place");

        // A non-byte (elemSize != 1) array is a transpiler-emission bug: the
        // abort path is exercised by the crash-probe e2e (test_pinvoke.cpp),
        // not in-process -- abort() would end the test binary.

        // net48 semantics: a null byte[] marshals as a NULL pointer to the
        // native callee -- the helper passes nullptr through (no abort).
        Expect(il2cpp::il2cpp_pinvoke_byte_array_data(nullptr) == nullptr,
               "null byte[] marshals as nullptr (net48 NULL-pointer pass-through)");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvokeext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvokeext tests passed\n");
    return 0;
}
