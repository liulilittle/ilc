// bvaltstrprobe.cpp -- native fixture for tests/cases/pinvoke-bvaltstr.
//
// The structs below are the LAYOUT TWINS of the managed [MarshalAs(ByValTStr)]
// structs: net48 marshals the field as an INLINE WCHAR[SizeConst] buffer, so
// the fixture reads and writes the text through those exact bytes. Sizes and
// offsets are returned to the managed side, which prints them -- the oracle
// and the transpiled runtime must agree byte-for-byte.
#include <cstddef>
#include <cstdint>

extern "C" {

struct BvtLogFont
{
    int32_t lfHeight;
    int32_t lfWidth;
    int32_t lfWeight;
    char16_t lfFaceName[32];
};

struct BvtHeadTail
{
    int32_t head;
    char16_t name[8];
    int32_t tail;
};

static int32_t bvt_units(const char16_t* text, int32_t capacity)
{
    int32_t n = 0;
    while (n < capacity && text[n] != 0)
    {
        ++n;
    }
    return n;
}

static void bvt_copy(char16_t* dst, int32_t capacity, const char16_t* src)
{
    int32_t n = 0;
    while (src[n] != 0 && n < capacity - 1)
    {
        dst[n] = src[n];
        ++n;
    }
    for (int32_t i = n; i < capacity; ++i)
    {
        dst[i] = 0;
    }
}

// net48 Marshal.SizeOf(LOGFONTW) / offset of the inline face buffer.
int32_t lf_sizeof()
{
    return static_cast<int32_t>(sizeof(BvtLogFont));
}

int32_t lf_face_offset()
{
    return static_cast<int32_t>(offsetof(BvtLogFont, lfFaceName));
}

// Reads the IN values (ints + inline UTF-16 text), then writes different ones
// so the managed side observes both directions.
int32_t lf_roundtrip(BvtLogFont* lf)
{
    const int32_t n = bvt_units(lf->lfFaceName, 32);
    int32_t textSum = n;
    for (int32_t i = 0; i < n; ++i)
    {
        textSum = textSum * 131 + static_cast<int32_t>(lf->lfFaceName[i]);
    }
    const int32_t in = lf->lfHeight * 10000 + lf->lfWidth * 100 + lf->lfWeight;
    lf->lfHeight += 1;
    lf->lfWidth += 2;
    lf->lfWeight += 3;
    bvt_copy(lf->lfFaceName, 32, u"Probe W");
    return in ^ textSum;
}

// Length of the inline text the marshaller copied in (net48 truncates a
// too-long string to SizeConst-1 units + NUL).
int32_t lf_measure(BvtLogFont* lf)
{
    return bvt_units(lf->lfFaceName, 32);
}

// Fills the WHOLE buffer without a NUL unit -- net48 reads it back as 32 chars.
int32_t lf_write_full(BvtLogFont* lf)
{
    for (int32_t i = 0; i < 32; ++i)
    {
        lf->lfFaceName[i] = static_cast<char16_t>((i % 2 == 0) ? u'B' : u'C');
    }
    return 32;
}

int32_t ht_sizeof()
{
    return static_cast<int32_t>(sizeof(BvtHeadTail));
}

int32_t ht_name_offset()
{
    return static_cast<int32_t>(offsetof(BvtHeadTail, name));
}

int32_t ht_tail_offset()
{
    return static_cast<int32_t>(offsetof(BvtHeadTail, tail));
}

// The trailing scalar must survive on BOTH sides of the inline buffer.
int32_t ht_roundtrip(BvtHeadTail* ht)
{
    const int32_t in = ht->head * 100 + ht->tail + bvt_units(ht->name, 8);
    ht->head += 5;
    ht->tail += 7;
    bvt_copy(ht->name, 8, u"XY");
    return in;
}

} // extern "C"
