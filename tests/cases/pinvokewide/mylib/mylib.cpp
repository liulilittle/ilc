// mylib.cpp -- native library for the pinvokewide e2e case (P-D1 P0).
// The full-width DllImport marshalling matrix: the transpiled import
// declarations (extern "C" int64_t/double/float/uint64_t) must bind against
// these exact native signatures -- a mangled width here or on the managed
// side is the silent ABI corruption the P-D1 audit flagged.

#include <cstdint>

extern "C" {

int64_t add_longs(int64_t a, int64_t b)
{
    return a + b;
}

double add_doubles(double a, double b)
{
    return a + b;
}

float scale_float(float a)
{
    return a * 2.0f;
}

uint64_t sum_ulongs(uint64_t a, uint64_t b)
{
    return a + b;
}

} // extern "C"
