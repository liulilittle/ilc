//------------------------------------------------------------------------------
// test_pinvokecb.cpp -- WindowProc-form delegate callback bridge unit tests
// (pinvokecb domain; see il2cpp_pinvoke_cb.h).
//
// Covers the fixed-pool thunk bridge that turns a single-cast managed
// delegate into a native WNDPROC-form function pointer:
//
//   void* il2cpp_pinvoke_cb_bind_wndproc(Il2CppObject* delegate, const char* name)
//   void  il2cpp_pinvoke_cb_unbind(const char* name)
//   bool  il2cpp_pinvoke_cb_is_bound(const char* name)
//
// Semantics verified (against the header contract + the pinvokeext callback
// registry model):
//   1. Bind a static-method delegate -> a non-null thunk; is_bound turns true.
//   2. Calling the thunk with WindowProc-form args dispatches into the
//      delegate's methodPtr and returns its result.
//   3. Bind an instance-method delegate (target set) -> the thunk passes the
//      stored target as the FIRST parameter (the generated instance ABI
//      {ret} M(Il2CppObject* this, ...)).
//   4. Unbind -> is_bound false; the strong GC reference is released.
//   5. Re-bind the SAME name returns the SAME thunk address (the native side
//      that stored the pointer keeps working).
//   6. Re-bind the SAME name after unbind reuses its RESERVED slot -> same
//      thunk address.
//   7. Binding a DIFFERENT name after unbind gets a DIFFERENT thunk address
//      (a freed slot is never re-assigned to another name -- a stale thunk
//      pointer keeps aborting loudly, never a silent wrong dispatch).
//   8. GC: the binding is a strong static root -- the delegate survives a
//      full collection (compaction rewrites the root slot), the thunk still
//      dispatches, and after unbind the delegate becomes collectible (the
//      strong reference was released).
//   9. Validation: null delegate -> NullReferenceException; a MULTICAST
//      container -> InvalidOperationException; a non-delegate object ->
//      InvalidOperationException.
//  10. is_bound returns false for null and unknown names; unbind of an
//      unknown name is a no-op.
//
// Plain executable + minimal assertion harness (see test_string.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_delegate_ext.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_pinvoke_cb.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "pinvoke-cb test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// WindowProc-form ABI: intptr_t (*)(intptr_t hwnd, int32_t msg, intptr_t w, intptr_t l).
typedef intptr_t (*WmFn)(intptr_t, int32_t, intptr_t, intptr_t);
typedef intptr_t (*WmThisFn)(il2cpp::Il2CppObject*, intptr_t, int32_t, intptr_t, intptr_t);

// Static-method delegate target.
intptr_t WmAdd(intptr_t hwnd, int32_t msg, intptr_t w, intptr_t l)
{
    return hwnd + static_cast<intptr_t>(msg) + w + l;
}

// Static-method delegate target (distinct method for replace tests).
intptr_t WmMul(intptr_t hwnd, int32_t msg, intptr_t w, intptr_t l)
{
    return hwnd * (static_cast<intptr_t>(msg) + 1) * w * (l + 1);
}

// Instance-method ABI: {ret} M(Il2CppObject* this, ...). The target object is
// passed in the FIRST parameter slot.
intptr_t WmInstance(il2cpp::Il2CppObject* self, intptr_t hwnd, int32_t msg,
                    intptr_t w, intptr_t l)
{
    if (self == nullptr) return -1;
    return hwnd + static_cast<intptr_t>(msg) + w + l + 1;
}

// Wrap a method pointer into a single-cast delegate (mirrors generated
// ldftn + newobj DelegateCtor).
il2cpp::Il2CppDelegate* MakeDel(void* fn)
{
    return il2cpp::il2cpp_delegate_new(fn);
}

il2cpp::Il2CppObject* Combine(il2cpp::Il2CppObject* a, il2cpp::Il2CppObject* b)
{
    return il2cpp::runtime::icall_delegate_combine(a, b);
}

// Raise-and-catch helper: run 'fn', capture the managed exception carrier.
template <typename Fn>
bool ThrowsDotNet(Fn fn, const char* expectedType)
{
    try
    {
        fn();
        return false;
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedType) == 0;
    }
}

} // namespace

int main()
{
    il2cpp::Il2CppDelegate* add = MakeDel(reinterpret_cast<void*>(&WmAdd));
    il2cpp::Il2CppObject*   oadd = reinterpret_cast<il2cpp::Il2CppObject*>(add);
    il2cpp::Il2CppDelegate* mul = MakeDel(reinterpret_cast<void*>(&WmMul));
    il2cpp::Il2CppObject*   omul = reinterpret_cast<il2cpp::Il2CppObject*>(mul);

    // --- 1. Bind a static-method delegate; is_bound turns true ---
    void* t1 = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(oadd, "wm_one");
    Expect(t1 != nullptr, "bind returns a non-null thunk address");
    Expect(il2cpp::il2cpp_pinvoke_cb_is_bound("wm_one"), "is_bound is true after bind");

    // --- 2. Calling the thunk dispatches into the delegate ---
    Expect(reinterpret_cast<WmFn>(t1)(10, 1, 100, 1000) == 1111,
           "static thunk dispatches the delegate (10 + 1 + 100 + 1000)");

    // --- 3. Instance-method delegate: target passed as the first param ---
    il2cpp::Il2CppDelegate* inst = MakeDel(reinterpret_cast<void*>(&WmInstance));
    inst->target = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("wm-instance-target"));
    void* ti = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(
        reinterpret_cast<il2cpp::Il2CppObject*>(inst), "wm_inst");
    Expect(ti != nullptr, "instance-method delegate binds to a thunk");
    Expect(ti != t1, "different names get distinct thunks");
    Expect(reinterpret_cast<WmFn>(ti)(10, 2, 100, 1000) == 1113,
           "instance thunk passes target as this (10 + 2 + 100 + 1000 + 1)");
    Expect(il2cpp::il2cpp_pinvoke_cb_is_bound("wm_inst"), "instance binding is bound");

    // --- 4. Unbind releases the binding (is_bound false) ---
    il2cpp::il2cpp_pinvoke_cb_unbind("wm_inst");
    Expect(!il2cpp::il2cpp_pinvoke_cb_is_bound("wm_inst"), "unbind turns is_bound false");

    // --- 5. Same-name re-bind returns the SAME thunk address ---
    void* t2 = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(omul, "wm_one");
    Expect(t2 == t1, "re-binding the same name returns the same thunk address");
    Expect(reinterpret_cast<WmFn>(t2)(2, 1, 3, 4) == 60,
           "re-bound thunk dispatches the NEW delegate (2 * 2 * 3 * 5)");
    Expect(il2cpp::il2cpp_pinvoke_cb_is_bound("wm_one"), "same-name re-bind stays bound");

    // --- 6. Same-name re-bind AFTER unbind reuses the reserved slot ---
    il2cpp::il2cpp_pinvoke_cb_unbind("wm_one");
    Expect(!il2cpp::il2cpp_pinvoke_cb_is_bound("wm_one"), "unbind before reserved re-bind");
    void* t3 = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(oadd, "wm_one");
    Expect(t3 == t1, "same-name re-bind after unbind reuses the reserved slot (same thunk)");
    Expect(reinterpret_cast<WmFn>(t3)(1, 1, 1, 1) == 4, "reserved-slot thunk still dispatches");

    // --- 7. A DIFFERENT name never reuses a freed slot ---
    il2cpp::il2cpp_pinvoke_cb_unbind("wm_one");
    void* t4 = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(omul, "wm_other");
    Expect(t4 != nullptr, "a new name binds successfully");
    Expect(t4 != t1, "a different name gets a DIFFERENT thunk (freed slot is never re-assigned)");
    Expect(reinterpret_cast<WmFn>(t4)(2, 1, 3, 4) == 60, "new-name thunk dispatches correctly");

    // --- 8. GC: the binding is a strong static root ---
    // The ONLY strong reference to this delegate is the binding's registered
    // root (the local probe is a WEAK slot, never a root): the delegate must
    // survive collection while bound and become collectible after unbind.
    il2cpp::Il2CppObject* probe = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeDel(reinterpret_cast<void*>(&WmAdd)));
    il2cpp::il2cpp_gc_add_weak_ref(reinterpret_cast<void**>(&probe));
    void* tg = il2cpp::il2cpp_pinvoke_cb_bind_wndproc(probe, "wm_gc");
    il2cpp::il2cpp_gc_collect();
    Expect(probe != nullptr,
           "the bound delegate survives collection (the binding's strong root holds it)");
    Expect(il2cpp::il2cpp_gc_get_generation(probe) >= il2cpp::kGcGeneration1,
           "the surviving delegate was promoted/compacted by the collection");
    Expect(reinterpret_cast<WmFn>(tg)(5, 2, 50, 500) == 557,
           "the thunk still dispatches after compaction moved the delegate");
    Expect(il2cpp::il2cpp_pinvoke_cb_is_bound("wm_gc"), "binding stays live across collection");
    il2cpp::il2cpp_pinvoke_cb_unbind("wm_gc");
    il2cpp::il2cpp_gc_collect();
    Expect(probe == nullptr,
           "after unbind the delegate becomes collectible (strong reference released)");
    Expect(!il2cpp::il2cpp_pinvoke_cb_is_bound("wm_gc"), "unbind of the GC binding clears it");
    il2cpp::il2cpp_gc_remove_weak_ref(reinterpret_cast<void**>(&probe));

    // --- 9. Validation error paths ---
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_pinvoke_cb_bind_wndproc(nullptr, "wm_bad"); },
                        "System.NullReferenceException,System.Exception"),
           "bind(null, name) raises the NullReferenceException carrier");
    il2cpp::Il2CppObject* combo = Combine(oadd, omul);
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_pinvoke_cb_bind_wndproc(combo, "wm_bad"); },
                        "System.InvalidOperationException,System.Exception"),
           "bind(multicast, name) is loudly rejected");
    il2cpp::Il2CppObject* notADelegate = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("not-a-delegate"));
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_pinvoke_cb_bind_wndproc(notADelegate, "wm_bad"); },
                        "System.InvalidOperationException,System.Exception"),
           "bind(non-delegate object, name) is loudly rejected");

    // --- 10. is_bound / unbind edge cases ---
    Expect(!il2cpp::il2cpp_pinvoke_cb_is_bound(nullptr), "is_bound(null) is false");
    Expect(!il2cpp::il2cpp_pinvoke_cb_is_bound("wm_never_bound"), "is_bound(unknown) is false");
    il2cpp::il2cpp_pinvoke_cb_unbind("wm_never_bound"); // no-op, must not crash

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pinvoke-cb assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pinvoke-cb tests passed\n");
    return 0;
}
