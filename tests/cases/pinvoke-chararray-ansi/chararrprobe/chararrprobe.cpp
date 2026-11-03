//------------------------------------------------------------------------------
// chararrprobe.cpp -- native fixture for the `char[] + CharSet.Ansi` DllImport
// case (tests/cases/pinvoke-chararray-ansi). Built as a SHARED library by
// difftest.
//
// IMPORTANT MEASURED FACT: net48 does NOT NUL-terminate the buffer it hands to
// the callee for a `char[]` parameter. It converts exactly the managed elements
// and stops. Using strlen here reads uninitialised heap beyond the conversion
// and produced NON-DETERMINISTIC lengths across runs of the same binary
// (measured 4, 4, 6 and 8 bytes for the same 2-character input). Every reader
// below therefore takes the byte count EXPLICITLY, so the record is stable.
//
// Each reader takes (const char* s, int32_t nbytes, uint8_t* out), copies at
// most nbytes bytes out and returns how many it copied, or -1 for a NULL
// pointer.
//------------------------------------------------------------------------------
#include <stdint.h>

static int32_t copy_out(const char* s, int32_t nbytes, int32_t cap, uint8_t* out)
{
    if (s == NULL) return -1;
    int32_t n = (nbytes < cap) ? nbytes : cap;
    if (n < 0) n = 0;
    for (int32_t i = 0; i < n; i++) out[i] = (uint8_t)s[i];
    return n;
}

// Report whether the pointer is NULL without reading through it. This is the
// ONLY stable property of an empty managed array (the bytes beyond it are
// uninitialised, so neither a length nor a first byte is meaningful).
static int32_t is_null(const char* s)
{
    return (s == NULL) ? 1 : 0;
}

// Overwrite the buffer in place (copy-back probe). Writes 'Z','Y',NUL when the
// byte capacity allows, and returns the number of characters written.
static int32_t write_zy(char* s, int32_t cap)
{
    if (s == NULL) return -1;
    if (cap > 0) s[0] = 'Z';
    if (cap > 1) s[1] = 'Y';
    if (cap > 2) s[2] = 0;
    return cap < 2 ? cap : 2;
}


// Read-only probe: hash the buffer WITHOUT writing to it. Used to prove that an
// [Out] parameter hands the callee a ZEROED buffer while [In,Out] hands it the
// converted input (measured: [Out] hashes to 0, [In,Out] to the 'ABC' hash).
static int32_t peek_hash(const char* s, int32_t nbytes)
{
    if (s == NULL) return -1;
    int32_t h = 0;
    for (int32_t i = 0; i < nbytes; i++) h = h * 31 + (uint8_t)s[i];
    return h;
}

extern "C" {

int32_t ca_read(const char* s, int32_t nbytes, int32_t cap, uint8_t* out)
{
    return copy_out(s, nbytes, cap, out);
}
int32_t ca_isnull(const char* s) { return is_null(s); }
int32_t ca_write(char* s, int32_t cap) { return write_zy(s, cap); }
int32_t ca_peek(const char* s, int32_t nbytes) { return peek_hash(s, nbytes); }

}
