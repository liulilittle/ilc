//------------------------------------------------------------------------------
// comoutrefprobe.cpp -- native fixture for tests/cases/pinvoke-com-outref.
//
// Builds its OWN real COM objects (IUnknown vtable only) so the managed side
// never needs an OS factory. Every object COUNTS AddRef / Release and is
// never freed, so an erroneous extra Release is observable as a refs count
// that drops too far (never a crash).
//
// The single-identity view (IID_IUnknown) is used for every interface
// section: a second interface entry on the same native reference makes the
// ledger ambiguous.
//
// The fixture's QueryInterface DOES AddRef (the real COM contract), so the
// ref counts the managed test prints are the true reference counts of the
// object -- exactly what the phase-0 oracle measured.
//
// Slots are allocated in a rotating pool so each scenario gets its own
// object; the counters of a scenario's object are never touched by another.
//------------------------------------------------------------------------------
#include <cstdint>
#include <cstring>

#if defined(_WIN32)
#define COM_CALL __stdcall
#else
#define COM_CALL
#endif

namespace {

struct OrCom;

typedef int32_t(COM_CALL* QueryInterfaceFn)(OrCom*, const void*, void**);
typedef uint32_t(COM_CALL* AddRefFn)(OrCom*);
typedef uint32_t(COM_CALL* ReleaseFn)(OrCom*);

struct OrCom
{
    void** vtbl;      // MUST be the first member: the IUnknown* points here
    int32_t refs;
    int32_t id;
    int32_t addrefs;
    int32_t releases;
    int32_t qis;
};

int32_t COM_CALL QueryInterfaceImpl(OrCom* self, const void* riid, void** ppv);
uint32_t COM_CALL AddRefImpl(OrCom* self);
uint32_t COM_CALL ReleaseImpl(OrCom* self);

const uint8_t kIidIUnknown[16] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
};

void* g_vtbl[3] = {
    reinterpret_cast<void*>(&QueryInterfaceImpl),
    reinterpret_cast<void*>(&AddRefImpl),
    reinterpret_cast<void*>(&ReleaseImpl),
};

const int32_t kMaxSlots = 24;
OrCom g_com[kMaxSlots];
int32_t g_nextSlot = 0;
int32_t g_nextId = 1;

// The object the most recent successful out/ref hand-back produced.
OrCom* g_handed = nullptr;
// The object the callee observed on the IN side of the last ref call.
OrCom* g_seen = nullptr;
// Ledger of g_seen at callee entry / exit (the arg-side +1/-1 evidence).
int32_t g_entryRefs = -1;
int32_t g_exitRefs = -1;

int32_t COM_CALL QueryInterfaceImpl(OrCom* self, const void* riid, void** ppv)
{
    if (riid == nullptr || ppv == nullptr) return static_cast<int32_t>(0x80004003);
    ++self->qis;
    if (std::memcmp(riid, kIidIUnknown, 16) == 0)
    {
        *ppv = self;
        AddRefImpl(self);
        return 0;
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t COM_CALL AddRefImpl(OrCom* self)
{
    ++self->addrefs;
    return static_cast<uint32_t>(++self->refs);
}

uint32_t COM_CALL ReleaseImpl(OrCom* self)
{
    ++self->releases;
    --self->refs;
    return static_cast<uint32_t>(self->refs);
}

OrCom* NewSlot()
{
    const int32_t i = g_nextSlot;
    g_nextSlot = (g_nextSlot + 1) % kMaxSlots;
    OrCom* o = &g_com[i];
    o->vtbl = g_vtbl;
    o->refs = 1;
    o->id = g_nextId++;
    o->addrefs = 0;
    o->releases = 0;
    o->qis = 0;
    g_handed = o;
    return o;
}

} // namespace

extern "C" {

// A fresh object with exactly ONE reference owned by the caller.
void* or_new()
{
    return NewSlot();
}

//------------------------------------------------------------------ out forms

// out: create a fresh object (refs=1) and hand its reference out.
int32_t or_out_new(void** pp)
{
    *pp = NewSlot();
    return 0;
}

// out: write NULL.
int32_t or_out_null(void** pp)
{
    g_handed = nullptr;
    *pp = nullptr;
    return 0;
}

// out: hand out a reference to an ALREADY-EXISTING object (AddRef first, the
// COM ownership contract for a callee that keeps the object alive).
int32_t or_out_existing(void** pp)
{
    OrCom* o = NewSlot();          // refs = 1 (the fixture's own)
    AddRefImpl(o);                 // +1 -> the reference handed to the caller
    *pp = o;
    return 0;
}

//------------------------------------------------------------------ ref forms

// ref: observe the incoming pointer, do NOT replace it.
int32_t or_ref_noop(void** pp)
{
    OrCom* o = static_cast<OrCom*>(*pp);
    g_seen = o;
    g_entryRefs = (o != nullptr) ? o->refs : -1;
    g_exitRefs = g_entryRefs;
    g_handed = o;
    return 0;
}

// ref: observe, replace with a fresh object, do NOT release the old.
int32_t or_ref_replace(void** pp)
{
    OrCom* old = static_cast<OrCom*>(*pp);
    g_seen = old;
    g_entryRefs = (old != nullptr) ? old->refs : -1;
    OrCom* o = NewSlot();
    *pp = o;
    g_exitRefs = (old != nullptr) ? old->refs : -1;
    return 0;
}

// ref: observe, replace with a fresh object AND release the old (the COM
// ownership contract -- the callee consumes the reference it was given).
int32_t or_ref_replace_owner(void** pp)
{
    OrCom* old = static_cast<OrCom*>(*pp);
    g_seen = old;
    g_entryRefs = (old != nullptr) ? old->refs : -1;
    if (old != nullptr) ReleaseImpl(old);
    OrCom* o = NewSlot();
    *pp = o;
    g_exitRefs = (old != nullptr) ? old->refs : -1;
    return 0;
}

// ref: write NULL through the in/out pointer (the callee drops the object).
int32_t or_ref_null(void** pp)
{
    OrCom* old = static_cast<OrCom*>(*pp);
    g_seen = old;
    g_entryRefs = (old != nullptr) ? old->refs : -1;
    *pp = nullptr;
    g_exitRefs = -1;
    g_handed = nullptr;
    return 0;
}

//------------------------------------------------------------------ probes

int32_t or_refs(void* p)     { return (p != nullptr) ? static_cast<OrCom*>(p)->refs : -1; }
int32_t or_addrefs(void* p)  { return (p != nullptr) ? static_cast<OrCom*>(p)->addrefs : -1; }
int32_t or_releases(void* p) { return (p != nullptr) ? static_cast<OrCom*>(p)->releases : -1; }
int32_t or_id(void* p)       { return (p != nullptr) ? static_cast<OrCom*>(p)->id : -1; }

void*   or_handed()          { return g_handed; }
void*   or_seen()            { return g_seen; }
int32_t or_entry_refs()      { return g_entryRefs; }
int32_t or_exit_refs()       { return g_exitRefs; }

int32_t or_release_ptr(void* p)
{
    if (p == nullptr) return -1;
    return static_cast<int32_t>(ReleaseImpl(static_cast<OrCom*>(p)));
}

} // extern "C"
