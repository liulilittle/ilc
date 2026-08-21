// mylib.cpp -- native library for the P/Invoke extension e2e case (M4 parallel).
// Exported with C linkage so the DllImport declarations bind.

#include <cstdint>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#else
    #include <cerrno>
#endif

extern "C" {

// String parameter demo: the byte length of a null-terminated string
// (CharSet.Ansi marshalling -- the runtime UTF-8 encoder is exercised by the
// C++ unit tests; the e2e verifies the DllImport plumbing end to end).
int32_t utf8_byte_len(const char* s)
{
    return static_cast<int32_t>(std::strlen(s != nullptr ? s : ""));
}

// String return demo: reverses the input into a static buffer owned by this
// DLL and returns its address. The managed side marshals it with
// PtrToStringAnsi (never a string-typed DllImport return -- the CLR would
// release it with CoTaskMemFree). Long inputs are truncated defensively.
int32_t reverse_static_impl(const char* s, char* out, int32_t cap)
{
    const size_t len = std::strlen(s != nullptr ? s : "");
    const int32_t n = (len < static_cast<size_t>(cap) - 1)
        ? static_cast<int32_t>(len) : (cap - 1);
    for (int32_t i = 0; i < n; ++i)
    {
        out[i] = s[n - 1 - i];
    }
    out[n] = '\0';
    return n;
}

const char* reverse_static(const char* s)
{
    static char g_reverseBuffer[256];
    reverse_static_impl(s, g_reverseBuffer, static_cast<int32_t>(sizeof(g_reverseBuffer)));
    return g_reverseBuffer;
}

// SetLastError demo: a negative code stores an OS error (SetLastError on
// Windows / errno on POSIX) and returns -1; a non-negative code returns 0
// WITHOUT touching the error slot.
int32_t set_error_if_negative(int32_t code)
{
    if (code < 0)
    {
#if defined(_WIN32)
        SetLastError(static_cast<DWORD>(-code));
#else
        errno = -code;
#endif
        return -1;
    }
    return 0;
}

// SetLastError pre-call cleanup: zeroes the OS error slot (SetLastError(0) /
// errno = 0). This is the native-side equivalent of the runtime's
// il2cpp_pinvoke_clear_last_error -- the pre-call reset the interop layer
// emits so the post-call capture reflects only what the callee set.
void clear_error()
{
#if defined(_WIN32)
    SetLastError(0);
#else
    errno = 0;
#endif
}

} // extern "C"
