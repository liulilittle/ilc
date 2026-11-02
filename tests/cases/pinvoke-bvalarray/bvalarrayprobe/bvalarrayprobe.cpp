// bvalarrayprobe.cpp -- native fixture for tests/cases/pinvoke-bvalarray.
//
// The structs below are the LAYOUT TWINS of the managed
// [MarshalAs(UnmanagedType.ByValArray, SizeConst = N)] structs: net48
// marshals the field as an INLINE native array (T[N]), so the fixture reads
// and writes the elements through those exact bytes. Sizes and offsets are
// returned to the managed side, which prints them -- the oracle and the
// transpiled runtime must agree byte-for-byte.
//
// Element widths mirror the net48 oracle measurements:
//   int    -> int32_t   (4 bytes)
//   byte   -> uint8_t   (1 byte)
//   short  -> int16_t   (2 bytes)
//   long   -> int64_t   (8 bytes, 8-byte alignment)
//   bool   -> int32_t   (Win32 BOOL, 4 bytes -- NOT the 1-byte managed cell)
//   bool with ArraySubType=U1 -> uint8_t (1 byte)
#include <cstddef>
#include <cstdint>

extern "C" {

struct BvaIntArray
{
    int32_t head;
    int32_t vals[4];
    int32_t tail;
};

struct BvaByteArray
{
    int32_t head;
    uint8_t b[5];
};

struct BvaShortArray
{
    int32_t head;
    int16_t s[3];
};

struct BvaLongArray
{
    int32_t head;
    int64_t l[3];
};

struct BvaBoolArray
{
    int32_t head;
    int32_t flags[4];
};

struct BvaBool1Array
{
    int32_t head;
    uint8_t flags[4];
};

int32_t int_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaIntArray));
}

int32_t int_vals_offset()
{
    return static_cast<int32_t>(offsetof(BvaIntArray, vals));
}

int32_t int_tail_offset()
{
    return static_cast<int32_t>(offsetof(BvaIntArray, tail));
}

// Reads head + the inline int array + tail, then writes different values in
// both directions so the managed side observes the full round trip.
int32_t int_roundtrip(BvaIntArray* m)
{
    const int32_t in = m->head * 100000 + m->tail * 10000
        + m->vals[0] * 1000 + m->vals[1] * 100 + m->vals[2] * 10 + m->vals[3];
    m->head += 1;
    m->tail += 2;
    m->vals[0] = 100;
    m->vals[1] = 101;
    m->vals[2] = 102;
    m->vals[3] = 103;
    return in;
}

int32_t byte_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaByteArray));
}

int32_t byte_offset()
{
    return static_cast<int32_t>(offsetof(BvaByteArray, b));
}

int32_t byte_roundtrip(BvaByteArray* m)
{
    int32_t sum = m->head;
    for (int32_t i = 0; i < 5; ++i)
    {
        sum += m->b[i];
    }
    m->head += 1;
    for (int32_t i = 0; i < 5; ++i)
    {
        m->b[i] = static_cast<uint8_t>(40 + i);
    }
    return sum;
}

int32_t short_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaShortArray));
}

int32_t short_offset()
{
    return static_cast<int32_t>(offsetof(BvaShortArray, s));
}

int32_t short_roundtrip(BvaShortArray* m)
{
    int32_t sum = m->head;
    for (int32_t i = 0; i < 3; ++i)
    {
        sum += m->s[i];
    }
    m->head += 1;
    for (int32_t i = 0; i < 3; ++i)
    {
        m->s[i] = static_cast<int16_t>(500 + i);
    }
    return sum;
}

int32_t long_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaLongArray));
}

int32_t long_offset()
{
    return static_cast<int32_t>(offsetof(BvaLongArray, l));
}

int32_t long_roundtrip(BvaLongArray* m)
{
    const int64_t sum = static_cast<int64_t>(m->head)
        + m->l[0] + m->l[1] * 3 + m->l[2] * 7;
    m->head += 1;
    for (int32_t i = 0; i < 3; ++i)
    {
        m->l[i] = 8000000000LL + i;
    }
    return static_cast<int32_t>(sum % 1000003);
}

int32_t bool_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaBoolArray));
}

int32_t bool_offset()
{
    return static_cast<int32_t>(offsetof(BvaBoolArray, flags));
}

int32_t bool_roundtrip(BvaBoolArray* m)
{
    int32_t sum = m->head;
    for (int32_t i = 0; i < 4; ++i)
    {
        sum += (m->flags[i] != 0) ? 1 : 0;
    }
    m->head += 1;
    m->flags[0] = 1;
    m->flags[1] = 0;
    m->flags[2] = 1;
    m->flags[3] = 1;
    return sum;
}

int32_t bool1_sizeof()
{
    return static_cast<int32_t>(sizeof(BvaBool1Array));
}

int32_t bool1_offset()
{
    return static_cast<int32_t>(offsetof(BvaBool1Array, flags));
}

int32_t bool1_roundtrip(BvaBool1Array* m)
{
    int32_t sum = m->head;
    for (int32_t i = 0; i < 4; ++i)
    {
        sum += (m->flags[i] != 0) ? 1 : 0;
    }
    m->head += 1;
    m->flags[0] = 1;
    m->flags[1] = 0;
    m->flags[2] = 1;
    m->flags[3] = 0;
    return sum;
}

} // extern "C"
