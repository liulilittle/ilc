// reflib2.cpp -- second iteration: `ref string` is NOT char*, it is char**.
//
// The first probe declared `char* p` and the callee read
// F0 61 82 11 19 02 00 00 -- a 64-bit ADDRESS, i.e. the bytes of the pointer
// itself, meaning the marshaller handed over a POINTER TO the string pointer.
// Declaring `char** p` below makes that explicit and lets us measure:
//   * whether *p is the managed string's content (or a private copy),
//   * whether writing *p replaces the managed REFERENCE slot,
//   * what an `out string` slot looks like on entry.
#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <objbase.h>

extern "C" {

// Is the parameter itself NULL? (distinguishes "no slot" from "null string")
int32_t ref2_slot_isnull(char** p) { return p ? 0 : 1; }

// Is *p NULL (a null managed string)?
int32_t ref2_value_isnull(char** p) { return (p && *p) ? 0 : 1; }

// Copy *p's bytes out (up to n), return the count actually copied, or -1 for a
// null slot / null string.
int32_t ref2_bytes(char** p, int32_t n, uint8_t* out)
{
    if (!p || !*p) return -1;
    int32_t i = 0;
    for (; i < n && (*p)[i]; ++i) out[i] = static_cast<uint8_t>((*p)[i]);
    return i;
}

// strlen of *p.
int32_t ref2_len(char** p) { return (p && *p) ? static_cast<int32_t>(::strlen(*p)) : -1; }

// OVERWRITE the string CONTENT in place (no pointer replacement). This is the
// writable-buffer half of the ABI and needs no allocator.
int32_t ref2_overwrite(char** p)
{
    if (!p || !*p) return -1;
    (*p)[0] = 'z'; (*p)[1] = 'z'; (*p)[2] = '\0';
    return 2;
}

// REPLACE *p with NULL. net48 copies the CALLEE's pointer value back into the
// managed ref slot, so the slot becomes null. A NULL needs no matching
// allocator, which keeps this oracle library free of an ole32 link dependency
// (a non-null replacement was measured separately: net48 frees the old buffer
// with CoTaskMemFree and adopts the new pointer, so only a CoTaskMemAlloc
// buffer survives -- hence the in-place overwrite above is what this case
// pins for the write direction).
int32_t ref2_clear(char** p) { if (!p) return -1; *p = nullptr; return 0; }


//------------------------------------------------------------------------------
// CharSet.Unicode twins. A `ref string` under Unicode is char16_t** -- the
// cell's ELEMENT width is the character type, so an Ansi-only implementation
// would read half of each UTF-16 unit and produce garbage. These probes make
// that distinction observable.
//------------------------------------------------------------------------------

int32_t ref2w_bytes(char16_t** p, int32_t n, uint16_t* out)
{
    if (!p || !*p) return -1;
    int32_t i = 0;
    for (; i < n && (*p)[i] != 0; ++i) out[i] = static_cast<uint16_t>((*p)[i]);
    return i;
}

int32_t ref2w_len(char16_t** p)
{
    if (!p || !*p) return -1;
    int32_t i = 0;
    while ((*p)[i] != 0) ++i;
    return i;
}
} // extern "C"
