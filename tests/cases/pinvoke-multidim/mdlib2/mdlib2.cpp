// mdlib2.cpp -- full multidim-array DllImport probe (net48 accepts them).
#include <cstdint>

extern "C" {

// Identity of the buffer pointer (is it the SAME storage as the managed array?)
intptr_t md2_addr(int32_t* p) { return reinterpret_cast<intptr_t>(p); }

// NUL/null-ness of the buffer.
int32_t md2_isnull(int32_t* p) { return p ? 0 : 1; }

// Read the first n elements as a flat row-major sequence.
int32_t md2_dump(int32_t* p, int32_t n, int32_t* out)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) out[i] = p[i];
    return n;
}

// Write through the buffer: is the managed multidim array updated (pin) or
// not (copy)?
int32_t md2_store(int32_t* p, int32_t n)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) p[i] = 100 + i;
    return n;
}

// Element width check with a different element type.
int32_t md2_dump16(int16_t* p, int32_t n, int16_t* out)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) out[i] = p[i];
    return n;
}

} // extern "C"
