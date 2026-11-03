#include <cstdint>

// marshalasprobe.cpp -- native fixture for tests/cases/pinvoke-marshalas-string.
//
// A PARAMETER-level [MarshalAs(UnmanagedType.LPStr/LPWStr/LPTStr/LPUTF8Str)]
// OVERRIDES the import CharSet on net48; this fixture observes exactly which
// bytes the marshaler handed the callee and copies them back, so difftest can
// compare the net48 oracle with the transpiled runtime BYTE FOR BYTE.
//
// The probe copies ONE logical string's bytes up to and including its NUL
// terminator (`unit` = the expected native code-unit width: 1 for a char*
// string, 2 for a char16_t* string), never a fixed 16-byte window: bytes past
// the terminator are heap slack whose contents are allocator-specific and
// would make the oracle/runtime comparison non-deterministic. On the GREEN
// paths the hint always matches the real encoding, so the read ends exactly at
// the terminator and touches no slack.
static int32_t copy_units(const void* s, int32_t unit, uint8_t* out, int32_t cap)
{
    if (s == nullptr)
    {
        return -1;
    }
    const uint8_t* p = static_cast<const uint8_t*>(s);
    int32_t n = 0;
    while (n + unit <= cap)
    {
        bool zero = true;
        for (int32_t i = 0; i < unit; ++i)
        {
            out[n + i] = p[n + i];
            if (p[n + i] != 0)
            {
                zero = false;
            }
        }
        n += unit;
        if (zero)
        {
            break;
        }
    }
    return n;
}

extern "C" {

// Single-string probe: returns the byte count copied into `out`, or -1 for a
// NULL native pointer. `unit` only tells the probe where the terminator is.
int32_t probe(const void* s, int32_t unit, uint8_t* out, int32_t cap)
{
    return copy_units(s, unit, out, cap);
}

// Multi-parameter probe (per-parameter independent encoding). Concatenates
// each of the three strings as [1-byte length][payload..] and returns the
// total number of bytes written. Lengths above 255 are clamped (never hit).
int32_t probe3(const void* a, int32_t aw, const void* b, int32_t bw, const void* c, int32_t cw,
               uint8_t* out, int32_t cap)
{
    const void* args[3] = { a, b, c };
    const int32_t widths[3] = { aw, bw, cw };
    int32_t written = 0;
    for (int32_t k = 0; k < 3; ++k)
    {
        if (written + 1 > cap)
        {
            break;
        }
        const int32_t n = copy_units(args[k], widths[k], out + written + 1, cap - written - 1);
        out[written] = n < 0 ? 0xFF : static_cast<uint8_t>(n);
        if (n < 0)
        {
            written += 1;
            continue;
        }
        written += 1 + n;
    }
    return written;
}

} // extern "C"
