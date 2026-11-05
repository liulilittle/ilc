// mylib.cpp -- native fixture for the probe-pinvoke-shapes case: every entry
// is element-agnostic about the pointer it receives, so the SAME function
// works for byte[,] / double[,] / struct[] probes. The managed side declares
// the DllImport with the shape under test.
#include <cstdint>
#include <cstddef>

extern "C" {

intptr_t pshape_addr(const void* p) { return reinterpret_cast<intptr_t>(p); }
int32_t pshape_isnull(const void* p) { return p ? 0 : 1; }

int32_t pshape_bdump(const uint8_t* p, int32_t n, uint8_t* out)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) out[i] = p[i];
    return n;
}
int32_t pshape_bstore(uint8_t* p, int32_t n)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) p[i] = static_cast<uint8_t>(200 + i);
    return n;
}
int32_t pshape_ddump(const double* p, int32_t n, double* out)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) out[i] = p[i];
    return n;
}

struct ProbeS { int32_t A; double B; };
int32_t pshape_slen(const ProbeS* p, int32_t n)
{
    if (!p) return -1;
    int32_t s = 0;
    for (int32_t i = 0; i < n; ++i) s += p[i].A;
    return s;
}
int32_t pshape_sdump(const ProbeS* p, int32_t n, double* out)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) out[i] = p[i].B;
    return n;
}
int32_t pshape_sstore(ProbeS* p, int32_t n)
{
    if (!p) return -1;
    for (int32_t i = 0; i < n; ++i) { p[i].A = 300 + i; p[i].B = 0.5 + i; }
    return n;
}

int32_t pshape_sarr_isnull(const char* const* pp, int32_t n)
{
    if (!pp) return -1;
    int32_t nonnull = 0;
    for (int32_t i = 0; i < n; ++i) if (pp[i]) ++nonnull;
    return nonnull;
}
int32_t pshape_sarr_first(const char* const* pp)
{
    if (!pp || !pp[0]) return -1;
    return static_cast<int32_t>(static_cast<unsigned char>(pp[0][0]));
}
int32_t pshape_sarr_utf16(const char16_t* const* pp)
{
    if (!pp || !pp[0]) return -1;
    return static_cast<int32_t>(pp[0][0]);
}

int32_t pshape_carr_slots(const void* const* pp, int32_t n)
{
    if (!pp) return -1;
    int32_t nonnull = 0;
    for (int32_t i = 0; i < n; ++i) if (pp[i]) ++nonnull;
    return nonnull;
}

} // extern "C"