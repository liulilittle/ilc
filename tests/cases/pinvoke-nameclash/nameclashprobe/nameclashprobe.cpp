// nameclashprobe.cpp -- native fixture for tests/cases/pinvoke-nameclash.
//
// The managed structs name Windows SDK typedefs (LOGFONTW / TEXTMETRICW) but
// are passed BY POINTER, so the fixture only touches the leading int32 fields
// that are at their natural offsets in both the net48 marshal buffer and the
// transpiled layout. It reports the IN values as a checksum and writes back
// DIFFERENT values, making the round trip observable on the managed side.
#include <cstdint>

extern "C" {

int32_t lf_roundtrip(int32_t* p)
{
    int32_t in = p[0] * 100 + p[1] * 10 + p[2];
    p[0] += 1;
    p[1] += 2;
    p[2] += 3;
    return in;
}

int32_t tm_roundtrip(int32_t* p)
{
    int32_t in = p[0] * 1000 + p[1] * 100 + p[2] * 10 + p[3];
    p[0] += 11;
    p[1] += 22;
    p[2] += 33;
    p[3] += 44;
    return in;
}

} // extern "C"
