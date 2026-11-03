#include <cstdint>
#include <cstring>

// ansiprobe.cpp -- native fixture for tests/cases/pinvoke-string-ansi.
//
// The callee only OBSERVES the byte string net48/il2cpp handed it and copies
// the bytes back so both sides print the exact native view (difftest compares
// the raw hex, never a hard-coded expectation).
extern "C" {

int32_t ansi_is_null(const char* s)
{
    return s == nullptr ? 1 : 0;
}

int32_t ansi_len(const char* s)
{
    return s == nullptr ? -1 : static_cast<int32_t>(std::strlen(s));
}

int32_t ansi_first(const char* s)
{
    return s == nullptr ? -1 : static_cast<int32_t>(static_cast<uint8_t>(s[0]));
}

// Copy up to 'cap' bytes of the NUL-terminated native view; return strlen.
int32_t ansi_dump(const char* s, uint8_t* out, int32_t cap)
{
    if (s == nullptr)
    {
        return -1;
    }
    const int32_t n = static_cast<int32_t>(std::strlen(s));
    const int32_t m = n < cap ? n : cap;
    for (int32_t i = 0; i < m; ++i)
    {
        out[i] = static_cast<uint8_t>(s[i]);
    }
    return n;
}

// CharSet.Unicode: the callee sees UTF-16 UNITS (NOT converted).
int32_t uni_len(const char16_t* s)
{
    if (s == nullptr)
    {
        return -1;
    }
    int32_t n = 0;
    while (s[n] != 0)
    {
        ++n;
    }
    return n;
}

} // extern "C"
