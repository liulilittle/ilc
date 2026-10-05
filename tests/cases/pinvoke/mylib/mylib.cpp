// mylib.cpp -- native library for P/Invoke tests (M4).
// Exported with C linkage so the transpiled import declaration binds.

#include <cstdint>
#include <cstring>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#else
    #include <cerrno>
#endif

extern "C" {

// P/Invoke Task 1: simple integer addition.
int32_t add_ints(int32_t a, int32_t b)
{
    return a + b;
}

// P/Invoke Task 3: string length wrapper (CharSet.Ansi).
int32_t strlen_wrap(const char* s)
{
    return static_cast<int32_t>(std::strlen(s != nullptr ? s : ""));
}

// P/Invoke Task 10: sum an int array (pointer + length).
int32_t sum_array(const int32_t* data, int32_t count)
{
    int32_t sum = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        sum += data[i];
    }
    return sum;
}

// M4 Task 4: SetLastError demo -- sets the OS error code and returns -1.
int32_t fail_with_error(int32_t code)
{
#if defined(_WIN32)
    SetLastError(static_cast<DWORD>(code));
#else
    errno = code;
#endif
    return -1;
}

// Audit H5: two string parameters -- a correct call site marshals each
// string into its OWN buffer; if both arguments alias the same buffer the
// result is strlen(last)+strlen(last) instead of strlen(a)+strlen(b).
int32_t concat_len(const char* a, const char* b)
{
    return static_cast<int32_t>(std::strlen(a != nullptr ? a : "")) +
           static_cast<int32_t>(std::strlen(b != nullptr ? b : ""));
}

} // extern "C"
