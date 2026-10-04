// extdll.cpp -- the P-D4 SHARED (out-of-manifest) native library. The
// transpiled exe does NOT link this library: every [DllImport("extdll")]
// call routes through il2cpp_pinvoke_resolve at runtime.
#include <cstdint>

extern "C" {

int32_t extdll_add(int32_t a, int32_t b)
{
    return a + b;
}

int32_t extdll_triple(int32_t x)
{
    return x * 3;
}

int32_t extdll_string_len(const char* s)
{
    int32_t n = 0;
    if (s == nullptr)
    {
        return 0;
    }
    while (s[n] != '\0')
    {
        ++n;
    }
    return n;
}

} // extern "C"