#pragma once
//------------------------------------------------------------------------------
// il2cpp_remoting.h -- System.Runtime.Remoting state accessors (p0c D31-D35).
//
// SCOPE: the five PUBLIC-FACE accessors of the remoting object model, NOT
// remoting itself. No channels, no ObjRef, no context-bound objects, no
// message dispatch: a transparent proxy here is an identity carrier that
// answers IsTransparentProxy / GetRealProxy and nothing else. Every other
// member of the family stays LOUD (MethodMap.Legacy.cs).
//
// FIELD MODEL (measured against net48 4.8 mscorlib, probe-p0c):
//   * RealProxy's observable per-instance state is exactly three slots:
//       _classToProxy -- the Type handed to the ctor (GetProxiedType);
//       _stubData     -- an object slot (Get/SetStubData);
//       _tp           -- the lazily created transparent proxy
//                        (GetTransparentProxy caches it: two calls return the
//                        SAME instance).
//     net48 declares them as private managed fields (_tp is a real field;
//     the stub data is reached through the icalls). A translated derived
//     class carries only ITS OWN fields -- the mscorlib base layout is not
//     part of the generated struct -- so the three slots live in a
//     proxy-identity side table here. The table is keyed by a NORMAL
//     GCHandle (a rooted, compaction-safe reference), NOT by a raw object
//     pointer, because the collector MOVES young objects.
//   * The Type value in this runtime IS its full-name string (the
//     MethodMap.Type.cs model), so _classToProxy is an Il2CppString*.
//   * TransparentProxy is a managed object with ONE int32 payload: the side
//     table slot of its RealProxy. The RealProxy stays alive through its
//     Normal handle.
//
// MEASURED net48 semantics implemented here (probe-p0c):
//   * GetStubData on a fresh proxy returns the DEFAULT STUB: the SAME boxed
//     System.IntPtr(-1) singleton on every proxy (not null).
//   * SetStubData(proxy, null) throws ArgumentNullException.
//   * GetRealProxy(non-proxy) / GetRealProxy(null) return null (no throw).
//   * IsTransparentProxy(null) is false; a RealProxy itself is not one.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

#include <cstdint>

namespace il2cpp { namespace runtime {

// RealProxy::.ctor(Type) -- net48 stores classToProxy and initializes the
// stub data to the default stub. A null Type is ArgumentNullException.
void icall_realproxy_ctor_type(Il2CppObject* self, Il2CppString* classToProxy);

// RealProxy::.ctor(Type, IntPtr, object) -- the full protected ctor. A
// non-zero stub with null stubData is ArgumentNullException (net48).
void icall_realproxy_ctor_full(Il2CppObject* self, Il2CppString* classToProxy,
                               intptr_t stub, Il2CppObject* stubData);

// RealProxy.GetProxiedType() -- the Type (full-name string) from the ctor.
Il2CppObject* icall_realproxy_get_proxied_type(Il2CppObject* self);

// RealProxy.GetTransparentProxy() -- the cached transparent proxy (created
// on first call, then identity-stable).
Il2CppObject* icall_realproxy_get_transparent_proxy(Il2CppObject* self);

// RealProxy.GetStubData(RealProxy) / SetStubData(RealProxy, object) -- static.
Il2CppObject* icall_realproxy_get_stub_data(Il2CppObject* realProxy);
void icall_realproxy_set_stub_data(Il2CppObject* realProxy, Il2CppObject* stubData);

// RemotingServices.IsTransparentProxy(object) / GetRealProxy(object).
int32_t icall_remoting_services_is_transparent_proxy(Il2CppObject* proxy);
Il2CppObject* icall_remoting_services_get_real_proxy(Il2CppObject* proxy);

}} // namespace il2cpp::runtime
