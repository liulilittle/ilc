//------------------------------------------------------------------------------
// remoting.cpp -- System.Runtime.Remoting state accessors (p0c D31-D35;
// contract in il2cpp_remoting.h).
//
// This is NOT a remoting implementation: it carries the three observable
// RealProxy slots (classToProxy / stubData / tp) and the transparent-proxy
// identity, exactly as net48's public accessors expose them. All measured
// net48 behavior is documented in the header; the side table uses NORMAL
// GCHandles (rooted, compaction-safe) instead of raw object pointers because
// the collector moves young objects.
//------------------------------------------------------------------------------

#include "il2cpp_remoting.h"

#include "il2cpp_gchandle.h"        // icall_gchandle_alloc_normal / get_target / free
#include "il2cpp_metadata.h"        // System_Object_class / System_IntPtr_class
#include "il2cpp_runtime.h"         // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_exception_ext.h"   // il2cpp_throw_argument_null_exception

#include <cstdint>
#include <vector>

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope -- the generated code references them by
// name: `&System_Runtime_Remoting_Proxies_RealProxy_class` is the base class
// of a user RealProxy subclass, and newobj on a ReaderWriterLock-style
// runtime object allocates through its own descriptor).
//
// RealProxy: header only. Its state is the side table (a translated subclass
// carries only its own fields -- the mscorlib base layout is not part of the
// generated struct), so the descriptor exists for identity/base-chain walks.
// TransparentProxy: header + ONE int32 side-table slot.
// IMessage: interface descriptor (never allocated; the generated interface
// map compares the pointer).
//------------------------------------------------------------------------------
::il2cpp::Il2CppClass System_Runtime_Remoting_Proxies_RealProxy_class = {
    "System.Runtime.Remoting.Proxies.RealProxy",
    static_cast<int32_t>(sizeof(::il2cpp::Il2CppObject)),
    0u,
    0,
    &System_Object_class,
    nullptr, 0, nullptr, 0,
};

struct Il2CppTransparentProxyObject
{
    ::il2cpp::Il2CppObject header;
    int32_t proxySlot; // index into the RealProxy side table
};

::il2cpp::Il2CppClass System_Runtime_Remoting_Proxies_TransparentProxy_class = {
    "System.Runtime.Remoting.Proxies.TransparentProxy",
    static_cast<int32_t>(sizeof(Il2CppTransparentProxyObject)),
    0u,
    0,
    &System_Object_class,
    nullptr, 0, nullptr, 0,
};

::il2cpp::Il2CppClass System_Runtime_Remoting_Messaging_IMessage_class = {
    "System.Runtime.Remoting.Messaging.IMessage",
    0,
    0u,
    0,
    nullptr,
    nullptr, 0, nullptr, 0,
};

namespace il2cpp { namespace runtime {

namespace {

// One RealProxy's observable state (see the header field model).
struct ProxySlot
{
    int32_t proxyHandle;        // Normal GCHandle -> the RealProxy instance
    int32_t classToProxyHandle; // Normal GCHandle -> the Type (full-name string)
    int32_t stubDataHandle;     // Normal GCHandle -> the stub data object
    int32_t tpHandle;           // Normal GCHandle -> the transparent proxy (0 = none)
};

std::vector<ProxySlot>& Slots()
{
    static std::vector<ProxySlot> slots;
    return slots;
}

bool g_remotingClassesRegistered = false;

void EnsureRemotingClasses()
{
    if (g_remotingClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Runtime_Remoting_Proxies_RealProxy_class);
    il2cpp_class_register(&System_Runtime_Remoting_Proxies_TransparentProxy_class);
    il2cpp_class_register(&System_Runtime_Remoting_Messaging_IMessage_class);
    g_remotingClassesRegistered = true;
}

// The net48 default stub: ONE boxed System.IntPtr(-1) singleton shared by
// every proxy (measured: GetStubData on a fresh proxy is that exact object).
Il2CppObject* DefaultStub()
{
    static int32_t handle = 0;
    if (handle == 0)
    {
        il2cpp_ensure_builtin_system_classes();
        Il2CppObject* box = il2cpp_object_new(&System_IntPtr_class);
        auto* payload = reinterpret_cast<intptr_t*>(
            reinterpret_cast<char*>(box) + sizeof(Il2CppObject));
        *payload = static_cast<intptr_t>(-1);
        handle = icall_gchandle_alloc_normal(box);
    }
    return icall_gchandle_get_target(handle);
}

int32_t FindSlot(Il2CppObject* self)
{
    auto& slots = Slots();
    for (size_t i = 0; i < slots.size(); ++i)
    {
        if (icall_gchandle_get_target(slots[i].proxyHandle) == self)
        {
            return static_cast<int32_t>(i);
        }
    }
    return -1;
}

// Look the proxy up (or create its state). The RealProxy itself is rooted by
// its Normal handle, so the collector may move it -- the identity comparison
// above always reads the CURRENT address.
int32_t EnsureSlot(Il2CppObject* self)
{
    const int32_t existing = FindSlot(self);
    if (existing >= 0)
    {
        return existing;
    }
    ProxySlot slot;
    slot.proxyHandle = icall_gchandle_alloc_normal(self);
    slot.classToProxyHandle = 0;
    slot.stubDataHandle = icall_gchandle_alloc_normal(DefaultStub());
    slot.tpHandle = 0;
    Slots().push_back(slot);
    return static_cast<int32_t>(Slots().size() - 1);
}

void SetStoredHandle(int32_t& handle, Il2CppObject* value)
{
    if (handle != 0)
    {
        icall_gchandle_free(handle);
    }
    handle = icall_gchandle_alloc_normal(value);
}

} // namespace

//------------------------------------------------------------------------------
// RealProxy construction.
//------------------------------------------------------------------------------
void icall_realproxy_ctor_type(Il2CppObject* self, Il2CppString* classToProxy)
{
    EnsureRemotingClasses();
    if (classToProxy == nullptr)
    {
        il2cpp_throw_argument_null_exception("classToProxy");
    }
    // NOTE: net48 additionally rejects a classToProxy that is neither
    // MarshalByRef-derived nor an interface (ArgumentException). This runtime
    // models a Type as its full-name string, so that kind information is not
    // available here -- documented deviation (probe-p0c only exercises the
    // valid shape).
    ProxySlot& slot = Slots()[static_cast<size_t>(EnsureSlot(self))];
    SetStoredHandle(slot.classToProxyHandle, reinterpret_cast<Il2CppObject*>(classToProxy));
}

void icall_realproxy_ctor_full(Il2CppObject* self, Il2CppString* classToProxy,
                               intptr_t stub, Il2CppObject* stubData)
{
    EnsureRemotingClasses();
    if (classToProxy == nullptr)
    {
        il2cpp_throw_argument_null_exception("classToProxy");
    }
    // net48: a real stub handle requires explicit stub data.
    if (stub != 0 && stubData == nullptr)
    {
        il2cpp_throw_argument_null_exception("stubData");
    }
    ProxySlot& slot = Slots()[static_cast<size_t>(EnsureSlot(self))];
    SetStoredHandle(slot.classToProxyHandle, reinterpret_cast<Il2CppObject*>(classToProxy));
    if (stubData != nullptr)
    {
        SetStoredHandle(slot.stubDataHandle, stubData);
    }
}

//------------------------------------------------------------------------------
// RealProxy accessors.
//------------------------------------------------------------------------------
Il2CppObject* icall_realproxy_get_proxied_type(Il2CppObject* self)
{
    EnsureRemotingClasses();
    const int32_t index = EnsureSlot(self);
    const ProxySlot& slot = Slots()[static_cast<size_t>(index)];
    if (slot.classToProxyHandle == 0)
    {
        return nullptr;
    }
    return icall_gchandle_get_target(slot.classToProxyHandle);
}

Il2CppObject* icall_realproxy_get_transparent_proxy(Il2CppObject* self)
{
    EnsureRemotingClasses();
    const int32_t index = EnsureSlot(self);
    ProxySlot& slot = Slots()[static_cast<size_t>(index)];
    if (slot.tpHandle != 0)
    {
        return icall_gchandle_get_target(slot.tpHandle);
    }
    // The transparent proxy: identity carrier only (see the header scope note).
    auto* tp = reinterpret_cast<Il2CppTransparentProxyObject*>(
        il2cpp_object_new(&System_Runtime_Remoting_Proxies_TransparentProxy_class));
    tp->proxySlot = index;
    slot.tpHandle = icall_gchandle_alloc_normal(reinterpret_cast<Il2CppObject*>(tp));
    return reinterpret_cast<Il2CppObject*>(tp);
}

Il2CppObject* icall_realproxy_get_stub_data(Il2CppObject* realProxy)
{
    EnsureRemotingClasses();
    if (realProxy == nullptr)
    {
        il2cpp_throw_argument_null_exception("rp");
    }
    const int32_t index = EnsureSlot(realProxy);
    return icall_gchandle_get_target(Slots()[static_cast<size_t>(index)].stubDataHandle);
}

void icall_realproxy_set_stub_data(Il2CppObject* realProxy, Il2CppObject* stubData)
{
    EnsureRemotingClasses();
    if (realProxy == nullptr)
    {
        il2cpp_throw_argument_null_exception("rp");
    }
    if (stubData == nullptr)
    {
        il2cpp_throw_argument_null_exception("stubData");
    }
    const int32_t index = EnsureSlot(realProxy);
    SetStoredHandle(Slots()[static_cast<size_t>(index)].stubDataHandle, stubData);
}

//------------------------------------------------------------------------------
// RemotingServices.
//------------------------------------------------------------------------------
int32_t icall_remoting_services_is_transparent_proxy(Il2CppObject* proxy)
{
    EnsureRemotingClasses();
    if (proxy == nullptr)
    {
        return 0;
    }
    return proxy->klass == &System_Runtime_Remoting_Proxies_TransparentProxy_class ? 1 : 0;
}

Il2CppObject* icall_remoting_services_get_real_proxy(Il2CppObject* proxy)
{
    EnsureRemotingClasses();
    if (proxy == nullptr || proxy->klass != &System_Runtime_Remoting_Proxies_TransparentProxy_class)
    {
        return nullptr; // measured net48: a non-proxy answers null (no throw)
    }
    const int32_t index = reinterpret_cast<Il2CppTransparentProxyObject*>(proxy)->proxySlot;
    if (index < 0 || static_cast<size_t>(index) >= Slots().size())
    {
        return nullptr;
    }
    return icall_gchandle_get_target(Slots()[static_cast<size_t>(index)].proxyHandle);
}

}} // namespace il2cpp::runtime
