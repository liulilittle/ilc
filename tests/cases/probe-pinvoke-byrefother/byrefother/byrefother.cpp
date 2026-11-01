// byrefother.cpp -- native fixture for `ref`/`out` of a FOREIGN-assembly VALUE
// TYPE and of a LOCAL blittable struct (tests/cases/probe-pinvoke-byrefother).
//
// Every entry reports the in-value the CALLEE observed and writes a KNOWN
// value back, so the oracle's stdout pins net48's marshalling semantics:
//   * a Guid& / struct& parameter hands the callee the ADDRESS of the managed
//     slot (blittable, pinned -- no copy), so the callee observes the caller's
//     current value and its writes are visible without a copy-back;
//   * `out` does NOT zero the slot in the marshal stub (.locals init zeroes at
//     method ENTRY only), so a fresh local reads zero and an already-valued
//     local passes THAT value;
//   * a callee that writes only the first half of a 16-byte Guid leaves the
//     second half exactly as the caller had it.
#include <cstdint>
#include <cstring>

extern "C" {

struct RawGuid { uint32_t a; uint16_t b; uint16_t c; uint8_t d[8]; };
struct RawPoint { int32_t x; int32_t y; };
struct RawLvh { int32_t x; int32_t y; int32_t flags; int32_t item; int32_t sub; };

// ---- Guid& (16-byte value slot) -------------------------------------------
// out: report whether the callee sees an all-zero 16-byte slot on entry.
int32_t out_guid_peek_zero(RawGuid* g)
{
    const uint8_t* p = reinterpret_cast<const uint8_t*>(g);
    for (int i = 0; i < 16; ++i)
        if (p[i] != 0) return 0;
    return 1;
}

// ref: the first field the caller stored (0x00112233 for the known value).
int32_t ref_guid_first(RawGuid* g) { return (int32_t)g->a; }

// 64-bit sum of the 16 bytes: lets the caller read back what it passed
// without a second managed round trip.
int32_t guid_sum(RawGuid* g)
{
    const uint8_t* p = reinterpret_cast<const uint8_t*>(g);
    int32_t s = 0;
    for (int i = 0; i < 16; ++i) s += p[i];
    return s;
}

// write all 16 bytes
void out_guid_full(RawGuid* g)
{
    g->a = 0x00112233u; g->b = 0x4455; g->c = 0x6677;
    for (int i = 0; i < 8; ++i) g->d[i] = (uint8_t)(0x88 + i);
}

// write ONLY the first 8 bytes (a/b/c) with a DIFFERENT value; d[0..7] keeps
// whatever the caller had there, so the record proves the callee wrote 8 bytes
// only and never touched the high half.
void guid_write_half(RawGuid* g)
{
    g->a = 0xDEADBEEFu; g->b = 0x1111; g->c = 0x2222;
}

// same half write through an `out` slot (the caller's slot is all zero for a
// fresh local, so d[0..7] must read back as zero -- proves the callee got the
// REAL 16-byte managed slot and did not touch bytes 8..15).
void out_guid_half(RawGuid* g) { guid_write_half(g); }

// ---- local blittable struct& (the NativeMethods+LVHITTESTINFO shape) ------
int32_t lvh_peek(RawLvh* p) { return p->x + p->y * 10 + p->flags * 100; }
void lvh_fill(RawLvh* p) { p->x = 7; p->y = 8; p->flags = 9; p->item = 10; p->sub = 11; }
int32_t lvh_sum(RawLvh* p) { return p->x + p->y + p->flags + p->item + p->sub; }

// ---- foreign blittable struct& (System.Drawing.Point shape) ---------------
void point_fill(RawPoint* p) { p->x = 11; p->y = 22; }
int32_t point_sum(RawPoint* p) { return p->x * 100 + p->y; }

// ---- the EXACT multi-parameter shapes of the gdiplus / shell32 rows -------
// GdipGetEncoderParameterList     (HandleRef, Guid&, int, IntPtr)
// GdipGetEncoderParameterListSize (HandleRef, Guid&, out int)
// reproduced without HandleRef (handleref is its own capability group) so the
// Guid& slot semantics are what this record isolates.
int32_t mix_ref_guid_int_out(RawGuid* g, int32_t n, int32_t* v)
{
    int32_t seen = (int32_t)g->a;
    g->a = (uint32_t)n;
    *v = seen + n;
    return seen;
}

// SHGetKnownFolderPath (Guid&, uint, IntPtr, out IntPtr) -- the Guid& plus a
// pointer in and a pointer out in ONE call.
int32_t mix_ref_guid_ptr(RawGuid* g, uint32_t flags, intptr_t* q)
{
    int32_t seen = (int32_t)g->b;
    g->b = (uint16_t)flags;
    *q = (intptr_t)(seen + (int32_t)flags);
    return seen;
}

} // extern "C"
