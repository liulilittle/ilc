//------------------------------------------------------------------------------
// structretprobe.cpp -- native fixture for BY-VALUE struct/enum DllImport
// RETURNS (tests/cases/pinvoke-structreturn).
//
// Every function returns a KNOWN aggregate with distinctive field values, so
// the net48 oracle's stdout and the transpiled runtime's stdout pin:
//   (a) an 8-byte two-int32 aggregate keeps BOTH fields      [make_pair]
//   (b) an 8-byte int64 aggregate keeps the FULL 64 bits     [make_wide]
//   (c) a 12-byte three-int32 aggregate keeps all fields     [make_triple]
//   (d) a 4-byte aggregate                                     [make_four]
// plus the audited Forms/Drawing declaration SHAPES: a nested-enum return
// beside 1/2/3 HandleRef params, an enum param and out-struct/out-Point.
//
// The enum entries return the int32 backing explicitly (the CLR marshals a
// managed enum return as its underlying type; the enum's native ABI is that
// int32 -- the case NOTES record the measurement).
//------------------------------------------------------------------------------
#include <cstdint>

extern "C" {

// Layout twins of the managed structs (C linkage binds by symbol name; the
// aggregate return ABI follows the struct layout, not the typedef name).
struct AbiPair
{
    int32_t x;
    int32_t y;
};

struct AbiWide
{
    int64_t v;
};

struct AbiTriple
{
    int32_t a;
    int32_t b;
    int32_t c;
};

struct AbiFour
{
    int32_t v;
};

struct AbiRect
{
    int32_t left;
    int32_t top;
    int32_t right;
    int32_t bottom;
};

struct AbiPoint
{
    int32_t x;
    int32_t y;
};

// (a) two int32 fields -- 8-byte aggregate.
AbiPair zero_pair()
{
    AbiPair p = {7, 8};
    return p;
}

AbiPair make_pair(int32_t seed)
{
    AbiPair p = {seed, seed + 1};
    return p;
}

AbiPair add_pair(AbiPair value, int32_t dx, int32_t dy)
{
    AbiPair p = {value.x + dx, value.y + dy};
    return p;
}

// (b) one int64 field -- 8-byte aggregate, full 64-bit payload.
AbiWide make_wide(int64_t seed)
{
    AbiWide w = {seed};
    return w;
}

// (c) three int32 fields -- 12-byte aggregate.
AbiTriple make_triple(int32_t seed)
{
    AbiTriple t = {seed, seed + 1, seed + 2};
    return t;
}

// (d) 4-byte aggregate.
AbiFour make_four(int32_t seed)
{
    AbiFour f = {seed ^ 0x5A5A5A5A};
    return f;
}

// Audited shape: enum return (BOOL int32) + out Point (registered POINT
// layout, but declared by ADDRESS here) -- GetCaretPos / GetPhysicalCursorPos.
int32_t GetCaretPos(AbiPoint* pt)
{
    pt->x = 41;
    pt->y = 43;
    return 1;
}

int32_t GetPhysicalCursorPos(AbiPoint* pt)
{
    pt->x = 51;
    pt->y = 53;
    return 1;
}

// Reads the byref Point back on the native side: the audited GetCaretPos
// shape returns the POINT by address, and the managed caller must see those
// bytes. (Point.X/Y accessors are an unrelated emitter gap -- do not depend
// on it here.)
int32_t sum_point(AbiPoint p)
{
    return p.x * 100 + p.y;
}

// Audited shape: enum return (DPI_AWARENESS int32) + IntPtr param.
int32_t GetAwarenessFromDpiAwarenessContext(intptr_t context)
{
    return static_cast<int32_t>(context & 3);
}

// Audited shape: enum return (RegionFlags int32) + 3 HandleRef params
// (decayed to intptr_t) + enum param.
int32_t IntCombineRgn(intptr_t a, intptr_t b, intptr_t c, int32_t mode)
{
    return (a == 0x1000 && b == 0x2000 && c == 0x3000 && mode == 2) ? 3 : 0;
}

// Audited shape: enum return + HandleRef + out same-assembly RECT.
int32_t IntGetRgnBox(intptr_t rgn, AbiRect* box)
{
    if (rgn != 0x1111) return 0;
    box->left = 1;
    box->top = 2;
    box->right = 3;
    box->bottom = 4;
    return 2;
}

// Audited shape: enum return + 2 HandleRef params.
int32_t IntSelectClipRgn(intptr_t hdc, intptr_t rgn)
{
    return (hdc == 0x4000 && rgn == 0x2222) ? 1 : 0;
}

} // extern "C"
