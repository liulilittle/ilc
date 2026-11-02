// anyprobe.cpp -- native fixture for tests/cases/pinvoke-object-any.
//
// net48's [MarshalAs(AsAny)] hands the callee the ADDRESS of a NATIVE TEMP
// COPY of the boxed value's unmanaged image. Every check dereferences the
// received pointer, guarded by VirtualQuery so a wrong implementation (e.g.
// "pass the boxed IntPtr VALUE") reports a mismatch instead of crashing the
// harness.
#include <windows.h>
#include <cstdint>

namespace {

bool readable(const void* p)
{
    MEMORY_BASIC_INFORMATION mbi;
    if (VirtualQuery(p, &mbi, sizeof(mbi)) != sizeof(mbi)) return false;
    if (mbi.State != MEM_COMMIT) return false;
    const DWORD prot = mbi.Protect & 0xff;
    return prot == PAGE_READONLY || prot == PAGE_READWRITE || prot == PAGE_WRITECOPY
        || prot == PAGE_EXECUTE_READ || prot == PAGE_EXECUTE_READWRITE
        || prot == PAGE_EXECUTE_WRITECOPY;
}

} // namespace

extern "C" {

// null -> NULL.
int32_t any_null(void* p)
{
    return p == nullptr ? 1 : 0;
}

// The boxed struct's field bytes (int32 a, int32 b, int64 c) at offset 0.
int32_t any_struct(void* p, int32_t a, int32_t b, int64_t c)
{
    if (p == nullptr) return 0;
    if (!readable(p)) return -1;
    const int32_t* ip = reinterpret_cast<const int32_t*>(p);
    const int64_t* lp = reinterpret_cast<const int64_t*>(reinterpret_cast<const char*>(p) + 8);
    return (ip[0] == a && ip[1] == b && *lp == c) ? 1 : 0;
}

// Writes markers into the temp copy WITHOUT [Out]: the managed box must NOT
// change.
int32_t any_struct_write(void* p)
{
    if (p == nullptr || !readable(p)) return -1;
    int32_t* ip = reinterpret_cast<int32_t*>(p);
    ip[0] = 0x11111111;
    ip[1] = 0x22222222;
    return 1;
}

// Writes markers into the temp copy WITH [In, Out]: net48 copies the temp back
// into the managed box, so the caller observes the markers.
int32_t any_struct_write_io(void* p)
{
    if (p == nullptr || !readable(p)) return -1;
    int32_t* ip = reinterpret_cast<int32_t*>(p);
    ip[0] = 0x11111111;
    ip[1] = 0x22222222;
    return 1;
}

// The DEFAULT (no MarshalAs) object parameter: net48 marshals it as a pointer
// to a VARIANT -- vt at offset 0 (little-endian 16-bit). A boxed struct is
// VT_RECORD (0x24), so the record is the observed vt. Also verifies the struct
// bytes are NOT at offset 0 (they would be for the AsAny row).
int32_t any_variant(void* p, int32_t a, int32_t b, int64_t c)
{
    if (p == nullptr || !readable(p)) return -1;
    const uint16_t vt = *reinterpret_cast<const uint16_t*>(p);
    const int32_t* ip = reinterpret_cast<const int32_t*>(p);
    const bool structAtZero =
        ip[0] == a && ip[1] == b
        && *reinterpret_cast<const int64_t*>(reinterpret_cast<const char*>(p) + 8) == c;
    return structAtZero ? -2 : static_cast<int32_t>(vt);
}

} // extern "C"
