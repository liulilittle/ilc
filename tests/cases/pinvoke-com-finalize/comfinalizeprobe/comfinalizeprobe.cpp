//------------------------------------------------------------------------------
// comfinalizeprobe.cpp -- native fixture for tests/cases/pinvoke-com-finalize.
//
// Builds its OWN COM objects (a real IUnknown vtable) so the managed side can
// wrap a raw pointer with Marshal.GetTypedObjectForIUnknown and then let the
// GC decide the lifetime. Every object COUNTS AddRef/Release and reports the
// live reference count; Release NEVER frees the memory and just decrements, so
// an erroneous second Release is observable as refs 0 / -1 instead of a crash.
//
// fin_new_raw(slot) creates (or resets) object 'slot' with ONE reference and
// returns the raw IUnknown*. The managed caller keeps only this IntPtr, which
// does not root the RCW -- the wrapper's own reference is therefore the only
// one the CLR (or the transpiled runtime) can release.
//
// IID layout: IID_IUnknown, the single-identity view the measured oracle uses
// (com-verify NOTES section 3 -- a second interface view makes coreclr cache
// two interface entries on one native reference and double-counts the ledger).
//------------------------------------------------------------------------------
#include <cstdint>
#include <cstring>

#if defined(_WIN32)
#define COM_CALL __stdcall
#else
#define COM_CALL
#endif

namespace {

struct FinCom;

typedef int32_t(COM_CALL* QueryInterfaceFn)(FinCom*, const void*, void**);
typedef uint32_t(COM_CALL* AddRefFn)(FinCom*);
typedef uint32_t(COM_CALL* ReleaseFn)(FinCom*);

struct FinCom
{
    void** vtbl;      // MUST be the first member: the IUnknown* points here
    int32_t refs;
    int32_t addrefs;
    int32_t releases;
};

int32_t COM_CALL QueryInterfaceImpl(FinCom* self, const void* riid, void** ppv);
uint32_t COM_CALL AddRefImpl(FinCom* self);
uint32_t COM_CALL ReleaseImpl(FinCom* self);

const uint8_t kIidIUnknown[16] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
};

void* g_vtbl[3] = {
    reinterpret_cast<void*>(&QueryInterfaceImpl),
    reinterpret_cast<void*>(&AddRefImpl),
    reinterpret_cast<void*>(&ReleaseImpl),
};

// One independent object per test scenario.
FinCom g_com[4] = {
    { g_vtbl, 1, 0, 0 },
    { g_vtbl, 1, 0, 0 },
    { g_vtbl, 1, 0, 0 },
    { g_vtbl, 1, 0, 0 },
};

int32_t COM_CALL QueryInterfaceImpl(FinCom* self, const void* riid, void** ppv)
{
    if (riid == nullptr || ppv == nullptr) return static_cast<int32_t>(0x80004003);
    if (std::memcmp(riid, kIidIUnknown, 16) == 0)
    {
        AddRefImpl(self);
        *ppv = self;
        return 0;
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t COM_CALL AddRefImpl(FinCom* self)
{
    ++self->addrefs;
    return static_cast<uint32_t>(++self->refs);
}

uint32_t COM_CALL ReleaseImpl(FinCom* self)
{
    ++self->releases;
    --self->refs;
    return static_cast<uint32_t>(self->refs);
}

} // namespace

extern "C" {

// A fresh object (refs = 1) as a raw IUnknown*. 'slot' selects which of the
// independent objects to (re)initialise, so every scenario's ledger starts at
// a known state regardless of the earlier scenarios' releases.
void* fin_new_raw(int slot)
{
    if (slot < 0 || slot >= 4) slot = 0;
    FinCom* o = &g_com[slot];
    o->vtbl = g_vtbl;
    o->refs = 1;
    o->addrefs = 0;
    o->releases = 0;
    return o;
}

int fin_refs(void* p)
{
    return p != nullptr ? static_cast<FinCom*>(p)->refs : -1;
}

int fin_addrefs(void* p)
{
    return p != nullptr ? static_cast<FinCom*>(p)->addrefs : -1;
}

int fin_releases(void* p)
{
    return p != nullptr ? static_cast<FinCom*>(p)->releases : -1;
}

} // extern "C"
