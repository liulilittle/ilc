//------------------------------------------------------------------------------
// pinvokecb.cpp -- delegate -> native callback bridge, WindowProc form (see
// il2cpp_pinvoke_cb.h).
//
// A FIXED POOL of WindowProc-form thunks: the native side stores the thunk
// ADDRESS (WNDCLASS.lpfnWndProc) and later calls it with no context
// parameter, so the thunk must know WHICH binding it belongs to. Each pool
// slot owns a DISTINCT thunk function (macro-generated below); the thunk
// reads the slot's delegate and forwards the call. The pool size is the
// distinct-name bound -- at most 16 DIFFERENT binding names over the
// process lifetime (a WinForms process registers a handful of window
// classes); expanding means adding macro expansions + switch cases.
//
// Slot ownership is PERMANENT per name: the first bind claims a slot for
// that name, and unbind only releases the strong reference -- the slot (and
// its thunk address) stays reserved for the name. This is a correctness
// guarantee, not a convenience: the native side may keep a stale thunk
// pointer after unbind, so a slot must NEVER be re-assigned to a different
// name (that would turn a stale call into a SILENT wrong dispatch instead
// of the loud abort the header promises). A same-name re-bind re-lives the
// SAME slot and returns the SAME thunk address, so a native side that
// already stored the pointer keeps working.
//
// Lifetime: a binding slot holds a STRONG GC reference (its delegate field
// is a registered static root -- the collector rewrites the slot when the
// delegate object moves under Gen0 compaction), so the delegate and its
// instance target stay alive until unbind.
//
// Storage is static (constraint #12 -- no global-constructor allocation);
// the pool is a bounded flat array that aborts loudly on overflow, exactly
// like the pinvokeext.cpp tables it extends.

#include "il2cpp_metadata.h"
#include "il2cpp_pinvoke_cb.h"

#include "il2cpp_delegate.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {

// WindowProc-form callback convention: on x86 the Win32 CALLBACK (WNDPROC)
// ABI is __stdcall, so the thunks the native side stores/ calls MUST be
// __stdcall; on x64 the Win32 calling conventions collapse to the single
// native convention and the decoration is a no-op (com.cpp IL2CPP_COM_CALL
// precedent).
#if defined(_M_IX86) || defined(__i386__)
    #define IL2CPP_WM_CALL __stdcall
#else
    #define IL2CPP_WM_CALL
#endif

namespace {

constexpr int32_t kMaxWmBindings = 16;

struct WmBinding
{
    const char*   name;     // stable registration name (literal, owned by the caller)
    Il2CppObject* delegate; // STRONG GC reference (registered static root)
    bool          live;
};

WmBinding g_bindings[kMaxWmBindings];

// The single-cast delegate class descriptor name (delegate.cpp) versus the
// multicast container descriptor name (delegateext.cpp). Every object that
// reaches this layer was created through il2cpp_delegate_new (stamps the
// "System.Delegate" descriptor) or icall_delegate_combine (stamps
// "System.MulticastDelegate"), so these two names partition the space.
const char* kDelegateClassName  = "System.Delegate";
const char* kMulticastClassName = "System.MulticastDelegate";

bool IsMulticastContainer(Il2CppObject* obj)
{
    return obj != nullptr && obj->klass != nullptr
        && std::strcmp(obj->klass->name, kMulticastClassName) == 0;
}

bool IsSingleCastDelegate(Il2CppObject* obj)
{
    return obj != nullptr && obj->klass != nullptr
        && std::strcmp(obj->klass->name, kDelegateClassName) == 0;
}

//------------------------------------------------------------------------------
// InvokeWmBinding -- dispatch the binding at 'slot' with WindowProc-form
// args. The thunk forwards here; the delegate's methodPtr is dispatched by
// binding (static vs instance -- see the header).
//------------------------------------------------------------------------------
intptr_t InvokeWmBinding(int32_t slot, intptr_t hwnd, int32_t msg,
                         intptr_t wparam, intptr_t lparam)
{
    if (slot < 0 || slot >= kMaxWmBindings || !g_bindings[slot].live)
    {
        std::fprintf(stderr,
                     "il2cpp_pinvoke_cb: thunk for slot %d called while unbound "
                     "(binding was released or never created)\n",
                     static_cast<int>(slot));
        std::abort();
    }
    Il2CppDelegate* leaf = reinterpret_cast<Il2CppDelegate*>(g_bindings[slot].delegate);
    if (leaf->target != nullptr)
    {
        // Instance-method delegate: the generated instance ABI puts `this`
        // (the stored target) in the FIRST parameter slot.
        typedef intptr_t (*WmThisFn)(Il2CppObject*, intptr_t, int32_t, intptr_t, intptr_t);
        return reinterpret_cast<WmThisFn>(leaf->methodPtr)(
            leaf->target, hwnd, msg, wparam, lparam);
    }
    typedef intptr_t (*WmFn)(intptr_t, int32_t, intptr_t, intptr_t);
    return reinterpret_cast<WmFn>(leaf->methodPtr)(hwnd, msg, wparam, lparam);
}

// One distinct thunk per pool slot (the native side stores the thunk address,
// so each binding needs a stable, distinct function).
#define IL2CPP_WM_THUNK(slot)                                                        \
    intptr_t IL2CPP_WM_CALL WmCallbackThunk##slot(intptr_t a0, int32_t a1, intptr_t a2, intptr_t a3) \
    {                                                                                 \
        return InvokeWmBinding(slot, a0, a1, a2, a3);                                 \
    }

IL2CPP_WM_THUNK(0)  IL2CPP_WM_THUNK(1)  IL2CPP_WM_THUNK(2)  IL2CPP_WM_THUNK(3)
IL2CPP_WM_THUNK(4)  IL2CPP_WM_THUNK(5)  IL2CPP_WM_THUNK(6)  IL2CPP_WM_THUNK(7)
IL2CPP_WM_THUNK(8)  IL2CPP_WM_THUNK(9)  IL2CPP_WM_THUNK(10) IL2CPP_WM_THUNK(11)
IL2CPP_WM_THUNK(12) IL2CPP_WM_THUNK(13) IL2CPP_WM_THUNK(14) IL2CPP_WM_THUNK(15)

#undef IL2CPP_WM_THUNK

void* WmThunkForSlot(int32_t slot)
{
    switch (slot)
    {
        case 0:  return reinterpret_cast<void*>(&WmCallbackThunk0);
        case 1:  return reinterpret_cast<void*>(&WmCallbackThunk1);
        case 2:  return reinterpret_cast<void*>(&WmCallbackThunk2);
        case 3:  return reinterpret_cast<void*>(&WmCallbackThunk3);
        case 4:  return reinterpret_cast<void*>(&WmCallbackThunk4);
        case 5:  return reinterpret_cast<void*>(&WmCallbackThunk5);
        case 6:  return reinterpret_cast<void*>(&WmCallbackThunk6);
        case 7:  return reinterpret_cast<void*>(&WmCallbackThunk7);
        case 8:  return reinterpret_cast<void*>(&WmCallbackThunk8);
        case 9:  return reinterpret_cast<void*>(&WmCallbackThunk9);
        case 10: return reinterpret_cast<void*>(&WmCallbackThunk10);
        case 11: return reinterpret_cast<void*>(&WmCallbackThunk11);
        case 12: return reinterpret_cast<void*>(&WmCallbackThunk12);
        case 13: return reinterpret_cast<void*>(&WmCallbackThunk13);
        case 14: return reinterpret_cast<void*>(&WmCallbackThunk14);
        case 15: return reinterpret_cast<void*>(&WmCallbackThunk15);
        default:
            std::fprintf(stderr, "il2cpp_pinvoke_cb: invalid slot %d\n",
                         static_cast<int>(slot));
            std::abort();
    }
}

// Index of the live binding for 'name'; -1 when absent.
int32_t FindBinding(const char* name)
{
    for (int32_t i = 0; i < kMaxWmBindings; ++i)
    {
        if (g_bindings[i].live && std::strcmp(g_bindings[i].name, name) == 0)
        {
            return i;
        }
    }
    return -1;
}

// A free slot index for 'name', or -1 when the pool is full.
//
// A slot is reserved by the name it was first bound to (see the file
// header): a freed slot may be re-allocated ONLY for the SAME name (the
// same-name re-bind re-lives the same slot, so the thunk address stays
// stable); a never-used slot is claimed for a NEW name; a freed slot whose
// reserved name differs is never touched (a stale thunk pointer held by the
// native side must keep aborting loudly, never dispatch to another binding).
int32_t AllocSlot(const char* name)
{
    int32_t sameNameSlot = -1;
    for (int32_t i = 0; i < kMaxWmBindings; ++i)
    {
        if (!g_bindings[i].live)
        {
            if (g_bindings[i].name != nullptr && std::strcmp(g_bindings[i].name, name) == 0)
            {
                return i; // same-name re-bind reuses its reserved slot
            }
            if (g_bindings[i].name == nullptr && sameNameSlot < 0)
            {
                sameNameSlot = i; // candidate: never-used slot (fall back)
            }
        }
    }
    return sameNameSlot;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_pinvoke_cb_bind_wndproc -- bind a delegate as a WindowProc-form
// native callback (see the header for the full contract).
//------------------------------------------------------------------------------
void* il2cpp_pinvoke_cb_bind_wndproc(Il2CppObject* delegate, const char* name)
{
    if (name == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_cb_bind_wndproc: null name\n");
        std::abort();
    }
    if (delegate == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (IsMulticastContainer(delegate))
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "A multicast delegate has no single native callback.");
    }
    if (!IsSingleCastDelegate(delegate))
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "The object is not a single-cast delegate.");
    }

    // Re-bind an existing name in place (stable thunk address); otherwise
    // allocate a fresh slot (same-name re-binds reuse their reserved slot).
    int32_t slot = FindBinding(name);
    if (slot < 0)
    {
        slot = AllocSlot(name);
    }
    if (slot < 0)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_cb_bind_wndproc: binding pool overflow\n");
        std::abort();
    }

    g_bindings[slot].name = name;
    g_bindings[slot].delegate = delegate;
    g_bindings[slot].live = true;
    // Strong reference: the slot is static storage, so registering its
    // delegate field as a GC root is a bounded, idempotent operation (the
    // collector rewrites the slot when the object moves under compaction).
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_bindings[slot].delegate));

    return WmThunkForSlot(slot);
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_cb_unbind -- release a binding (see header). Frees the
// strong reference; the pool slot stays RESERVED for the name (see the file
// header) -- the slot's thunk must not be called afterwards (it aborts
// loudly) and will never be re-assigned to a different name.
//------------------------------------------------------------------------------
void il2cpp_pinvoke_cb_unbind(const char* name)
{
    if (name == nullptr)
    {
        std::fprintf(stderr, "il2cpp_pinvoke_cb_unbind: null name\n");
        std::abort();
    }
    const int32_t slot = FindBinding(name);
    if (slot < 0)
    {
        return; // unknown name: no-op
    }
    il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&g_bindings[slot].delegate));
    g_bindings[slot].delegate = nullptr;
    g_bindings[slot].live = false;
    // NOTE: g_bindings[slot].name is intentionally kept -- the slot remains
    // reserved for this name so a stale thunk pointer can never be silently
    // re-purposed by a different binding (see AllocSlot).
}

//------------------------------------------------------------------------------
// il2cpp_pinvoke_cb_is_bound -- diagnostic (see header).
//------------------------------------------------------------------------------
bool il2cpp_pinvoke_cb_is_bound(const char* name)
{
    return name != nullptr && FindBinding(name) >= 0;
}

} // namespace il2cpp
