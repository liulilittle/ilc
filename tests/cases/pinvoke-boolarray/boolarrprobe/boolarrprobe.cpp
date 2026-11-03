//------------------------------------------------------------------------------
// boolarrprobe.cpp -- native fixture for the `bool[]` DllImport case
// (tests/cases/pinvoke-boolarray). Built as a SHARED library by difftest.
//
// Two element widths are exercised because net48 picks the native cell from the
// PARAMETER's [MarshalAs(UnmanagedType.LPArray, ArraySubType=...)] and defaults
// to a 4-byte BOOL:
//   * b4_* -- 4-byte int32 elements (the DEFAULT and ArraySubType=I4/BOOL)
//   * b1_* -- 1-byte elements (ArraySubType=U1/I1)
//
// Each reader copies RAW BYTES out (never a hash) so the element width is
// directly observable, and every case passes an explicit byte count because
// net48 does not terminate these buffers.
//------------------------------------------------------------------------------
#include <stdint.h>

static int32_t dump_bytes(const void* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    if (p == NULL) return -1;
    int32_t n = nbytes < cap ? nbytes : cap;
    if (n < 0) n = 0;
    const uint8_t* q = (const uint8_t*)p;
    for (int32_t i = 0; i < n; i++) out[i] = q[i];
    return n;
}

static int32_t is_null_ptr(const void* p) { return p == NULL ? 1 : 0; }

// Write distinguishing values so the copy-back policy is observable:
// element 0 -> 2 (true), element 1 -> 1 (true), element 2 -> 0 (false).
static int32_t write4(int32_t* p, int32_t n)
{
    if (p == NULL) return -1;
    for (int32_t i = 0; i < n; i++) p[i] = (i == 2) ? 0 : (i == 0 ? 2 : 1);
    return n;
}

static int32_t write1(uint8_t* p, int32_t n)
{
    if (p == NULL) return -1;
    for (int32_t i = 0; i < n; i++) p[i] = (uint8_t)((i == 2) ? 0 : 1);
    return n;
}

// Read-only seed probe: dump the buffer WITHOUT writing, so the [Out] seed
// policy (zeroed) is observable even though a writing callee overwrites it.
static int32_t peek_bytes(const void* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    return dump_bytes(p, nbytes, cap, out);
}
extern "C" {

int32_t b4_dump(const int32_t* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    return dump_bytes(p, nbytes, cap, out);
}
int32_t b1_dump(const uint8_t* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    return dump_bytes(p, nbytes, cap, out);
}
int32_t b_isnull(const void* p) { return is_null_ptr(p); }
int32_t b4_write(int32_t* p, int32_t n) { return write4(p, n); }
int32_t b4_peek(const int32_t* p, int32_t nbytes, int32_t cap, uint8_t* out) { return peek_bytes(p, nbytes, cap, out); }
int32_t b1_peek(const uint8_t* p, int32_t nbytes, int32_t cap, uint8_t* out) { return peek_bytes(p, nbytes, cap, out); }
int32_t b1_write(uint8_t* p, int32_t n) { return write1(p, n); }

}
