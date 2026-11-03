//------------------------------------------------------------------------------
// sbansiprobe.cpp -- native fixture for the `StringBuilder + CharSet.Ansi`
// DllImport case (tests/cases/pinvoke-stringbuilder-ansi). Built as a SHARED
// library by difftest.
//
// The callee receives a CP_ACP BYTE buffer (net48 measured): one byte per
// StringBuilder capacity unit, NUL-terminated after the encoded content. Every
// reader reports RAW BYTES with an explicit count -- the buffer's capacity and
// the encoded length are different things, and reading past the terminator
// would sample free space.
//------------------------------------------------------------------------------
#include <stdint.h>

// Byte length of the NUL-terminated content the callee received.
static int32_t content_len(const char* p)
{
    if (p == NULL) return -1;
    int32_t n = 0;
    while (p[n] != 0) n++;
    return n;
}

static int32_t dump_bytes(const char* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    if (p == NULL) return -1;
    int32_t n = nbytes < cap ? nbytes : cap;
    if (n < 0) n = 0;
    for (int32_t i = 0; i < n; i++) out[i] = (uint8_t)p[i];
    return n;
}

// Copy-back probe: write an ASCII string (bounded by the byte capacity).
static int32_t write_ascii(char* p, int32_t cap)
{
    if (p == NULL) return -1;
    const char* src = "Hi";
    int32_t n = 0;
    while (src[n] != 0 && n < cap - 1) { p[n] = src[n]; n++; }
    if (cap > 0) p[n] = 0;
    return n;
}

// Copy-back probe with CP936 bytes D6 D0 (U+4E2D): the managed side must decode
// the two bytes into ONE character.
static int32_t write_cjk(char* p, int32_t cap)
{
    if (p == NULL) return -1;
    if (cap >= 3) { p[0] = (char)0xD6; p[1] = (char)0xD0; p[2] = 0; return 2; }
    if (cap > 0) p[0] = 0;
    return 0;
}

// Write an unrepresentable byte sequence to check the decode fallback.
static int32_t write_latin(char* p, int32_t cap)
{
    if (p == NULL) return -1;
    if (cap >= 3) { p[0] = 'A'; p[1] = (char)0xE9; p[2] = 0; return 2; }
    if (cap > 0) p[0] = 0;
    return 0;
}

extern "C" {

int32_t sb_len(const char* p) { return content_len(p); }
int32_t sb_dump(const char* p, int32_t nbytes, int32_t cap, uint8_t* out)
{
    return dump_bytes(p, nbytes, cap, out);
}
int32_t sb_isnull(const void* p) { return p == NULL ? 1 : 0; }
int32_t sb_write_ascii(char* p, int32_t cap) { return write_ascii(p, cap); }
int32_t sb_write_cjk(char* p, int32_t cap) { return write_cjk(p, cap); }
int32_t sb_write_latin(char* p, int32_t cap) { return write_latin(p, cap); }

}
