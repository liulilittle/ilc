//------------------------------------------------------------------------------
// combyvalprobe.cpp -- native fixture for tests/cases/pinvoke-com-byval.
//
// Builds its OWN real COM object (IUnknown + one interface method at vtable
// slot 3) instead of taking one from the OS: this stage has no out/ref COM
// parameter, so an OS factory (CreateStreamOnHGlobal(..., IStream**)) cannot be
// called from managed code. The managed side wraps the returned IUnknown* with
// Marshal.GetObjectForIUnknown, giving an RCW exactly like the net48 case.
//
// TWO independent objects: create()/com_refcount()/probe_* operate on the
// "current" object (object 0); create2() switches to object 1, used only by the
// released-RCW record so its reference bookkeeping is free of the other tests'
// typed-RCW references.
//
// The object counts every AddRef/Release and records the refcount it observes
// at the callee's entry and exit. The managed test prints DELTAS against the
// refcount sampled immediately before the call, so the oracle (net48) and the
// transpiled runtime agree even though GetObjectForIUnknown's own AddRef policy
// differs between them (measured divergence, com-verify NOTES section 6).
//
// IID layout: [Guid("8F2A1B3C-4D5E-6F70-8192-A3B4C5D6E7F8")] -> Data1 LE /
// Data2 LE / Data3 LE / Data4 in order (the .NET Guid byte layout).
//------------------------------------------------------------------------------
#include <cstdint>
#include <cwchar>

#if defined(_WIN32)
#define COM_CALL __stdcall
#else
#define COM_CALL
#endif

namespace {

struct TestCom;

typedef int32_t(COM_CALL* QueryInterfaceFn)(TestCom*, const void*, void**);
typedef uint32_t(COM_CALL* AddRefFn)(TestCom*);
typedef uint32_t(COM_CALL* ReleaseFn)(TestCom*);
typedef int32_t(COM_CALL* GetValueFn)(TestCom*);

struct TestCom
{
    void** vtbl;      // MUST be the first member: the IUnknown* points here
    int32_t refcount;
    int32_t addrefs;
    int32_t releases;
    int32_t value;
    int32_t entryRefs;
    int32_t exitRefs;
    int32_t sawNull;
};

int32_t COM_CALL QueryInterfaceImpl(TestCom* self, const void* riid, void** ppv);
uint32_t COM_CALL AddRefImpl(TestCom* self);
uint32_t COM_CALL ReleaseImpl(TestCom* self);
int32_t COM_CALL GetValueImpl(TestCom* self);

const uint8_t kIidIUnknown[16] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46,
};
const uint8_t kIidITestCom[16] = {
    0x3C, 0x1B, 0x2A, 0x8F, 0x5E, 0x4D, 0x70, 0x6F,
    0x81, 0x92, 0xA3, 0xB4, 0xC5, 0xD6, 0xE7, 0xF8,
};

void* g_vtbl[4] = {
    reinterpret_cast<void*>(&QueryInterfaceImpl),
    reinterpret_cast<void*>(&AddRefImpl),
    reinterpret_cast<void*>(&ReleaseImpl),
    reinterpret_cast<void*>(&GetValueImpl),
};

TestCom g_com[2] = {
    { g_vtbl, 1, 0, 0, 42, 0, 0, 0 },
    { g_vtbl, 1, 0, 0, 42, 0, 0, 0 },
};
TestCom* g_cur = &g_com[0];

bool IidEquals(const void* a, const uint8_t b[16])
{
    const uint8_t* p = static_cast<const uint8_t*>(a);
    for (int32_t i = 0; i < 16; ++i)
    {
        if (p[i] != b[i]) return false;
    }
    return true;
}

int32_t COM_CALL QueryInterfaceImpl(TestCom* self, const void* riid, void** ppv)
{
    if (ppv == nullptr) return static_cast<int32_t>(0x80004003); // E_POINTER
    if (IidEquals(riid, kIidIUnknown) || IidEquals(riid, kIidITestCom))
    {
        // Single-vtable object: IUnknown and ITestCom are the same address.
        *ppv = self;
        AddRefImpl(self);
        return 0; // S_OK
    }
    *ppv = nullptr;
    return static_cast<int32_t>(0x80004002); // E_NOINTERFACE
}

uint32_t COM_CALL AddRefImpl(TestCom* self)
{
    ++self->addrefs;
    return static_cast<uint32_t>(++self->refcount);
}

uint32_t COM_CALL ReleaseImpl(TestCom* self)
{
    ++self->releases;
    if (self->refcount > 0) --self->refcount;
    // Never freed: the process exits after the test and the managed side reads
    // the counters (including after a ReleaseComObject that drives the count
    // to 0 on this runtime).
    return static_cast<uint32_t>(self->refcount);
}

int32_t COM_CALL GetValueImpl(TestCom* self)
{
    return self->value;
}

int32_t CallGetValue(void* iface)
{
    TestCom* self = static_cast<TestCom*>(iface);
    GetValueFn fn = reinterpret_cast<GetValueFn>(self->vtbl[3]);
    return fn(self);
}

void Reset(TestCom* t)
{
    t->refcount = 1;
    t->addrefs = 0;
    t->releases = 0;
    t->value = 42;
    t->entryRefs = 0;
    t->exitRefs = 0;
    t->sawNull = 0;
}

} // namespace

extern "C" {

// A fresh object (index 0) with exactly one reference owned by the caller.
void* com_create()
{
    g_cur = &g_com[0];
    Reset(g_cur);
    return g_cur;
}

// A fresh, INDEPENDENT object (index 1) with one caller-owned reference.
void* com_create2()
{
    g_cur = &g_com[1];
    Reset(g_cur);
    return g_cur;
}

// BYVAL COM-interface parameter probe: records the refcount the callee sees at
// entry and exit, then returns GetValue(). A NULL argument is recorded and
// answered with -1 (net48 passes NULL for a null interface).
int32_t probe_byval(void* obj)
{
    g_cur->sawNull = (obj == nullptr) ? 1 : 0;
    if (obj == nullptr)
    {
        g_cur->entryRefs = -1;
        g_cur->exitRefs = -1;
        return -1;
    }
    g_cur->entryRefs = g_cur->refcount;
    const int32_t v = CallGetValue(obj);
    g_cur->exitRefs = g_cur->refcount;
    return v;
}

// The COM parameter coexisting with a CharSet.Unicode string and an int32.
int32_t probe_mixed(void* obj, int32_t x, const wchar_t* s)
{
    if (obj == nullptr) return -1;
    const int32_t len = (s == nullptr) ? -1 : static_cast<int32_t>(wcslen(s));
    return CallGetValue(obj) + x + len;
}

int32_t com_refcount()
{
    return g_cur->refcount;
}

int32_t com_entry()
{
    return g_cur->entryRefs;
}

int32_t com_exit()
{
    return g_cur->exitRefs;
}

int32_t com_was_null()
{
    return g_cur->sawNull;
}

} // extern "C"
