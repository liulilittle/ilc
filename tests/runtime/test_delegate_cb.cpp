//------------------------------------------------------------------------------
// test_delegate_cb.cpp -- delegate -> C callback bridge unit tests.
//
// Covers the two new icalls on top of the existing callback registry
// (il2cpp_pinvoke_register_callback / il2cpp_pinvoke_find_callback):
//
//   void* icall_delegate_get_fn_ptr(Il2CppObject* delegate)
//   void  icall_delegate_to_callback(Il2CppObject* delegate, const char* name)
//
// Semantics verified against the .NET reference model (a delegate passed to a
// native function must be a SINGLE-CAST STATIC-method delegate -- the bare
// method pointer carries no invocation context):
//   1. Registry round trip: register + find + call through the pointer.
//   2. Lookup of an unknown name returns nullptr (never a default).
//   3. Re-registering the same name REPLACES the entry (last wins).
//   4. get_fn_ptr on a single-cast static delegate returns the methodPtr;
//      calling it produces the method's result.
//   5. get_fn_ptr null input raises the NullReferenceException carrier.
//   6. get_fn_ptr on a MULTICAST container raises InvalidOperationException
//      (the pointer would only cover one leaf -- loud rejection).
//   7. get_fn_ptr on an instance-method delegate (target set) raises
//      InvalidOperationException (the bare pointer has no 'this' to bind).
//   8. to_callback registers the delegate's methodPtr under the name; the
//      registry lookup then returns a callable pointer.
//   9. to_callback re-registration replaces (same table semantics).
//  10. to_callback null / multicast inputs raise the same exceptions.
//  11. GC: a rooted delegate survives collection; the callback entry (a code
//      address) stays valid afterwards.
//
// Plain executable + minimal assertion harness (see test_string.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_delegate_ext.h"

#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_pinvoke_ext.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "delegate-cb test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

typedef int32_t (*I4Fn)(int32_t, int32_t, int32_t, int32_t);

// Test methods with the generated-code ABI int32_t(*)(i4, i4, i4, i4).
int32_t FnA(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    return a0 + 1;
}

int32_t FnB(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    return a0 * 10;
}

// Wrap a method pointer into a single-cast delegate (mirrors generated
// ldftn + newobj DelegateCtor).
il2cpp::Il2CppDelegate* MakeDel(I4Fn fn)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn));
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
    il2cpp::Il2CppDelegate* a = MakeDel(FnA);
    il2cpp::Il2CppDelegate* b = MakeDel(FnB);
    il2cpp::Il2CppObject* oa = reinterpret_cast<il2cpp::Il2CppObject*>(a);
    il2cpp::Il2CppObject* ob = reinterpret_cast<il2cpp::Il2CppObject*>(b);

    // --- 1. Registry round trip: register + find + call ---
    il2cpp::icall_pinvoke_register_callback("cb_one", reinterpret_cast<void*>(FnA));
    void* found = il2cpp::il2cpp_pinvoke_find_callback("cb_one");
    Expect(found == reinterpret_cast<void*>(FnA), "registered callback is found by name");
    Expect(found != nullptr, "found callback is non-null");
    Expect(reinterpret_cast<I4Fn>(found)(41, 0, 0, 0) == 42,
           "calling the found callback yields the method result");

    // --- 2. Unknown name -> nullptr (never a default) ---
    Expect(il2cpp::il2cpp_pinvoke_find_callback("cb_no_such") == nullptr,
           "lookup of an unknown name returns nullptr");

    // --- 3. Re-registration replaces (last wins) ---
    il2cpp::icall_pinvoke_register_callback("cb_swap", reinterpret_cast<void*>(FnA));
    Expect(reinterpret_cast<I4Fn>(il2cpp::il2cpp_pinvoke_find_callback("cb_swap"))(5, 0, 0, 0) == 6,
           "first registration of cb_swap is FnA (5 -> 6)");
    il2cpp::icall_pinvoke_register_callback("cb_swap", reinterpret_cast<void*>(FnB));
    Expect(reinterpret_cast<I4Fn>(il2cpp::il2cpp_pinvoke_find_callback("cb_swap"))(5, 0, 0, 0) == 50,
           "re-registering cb_swap replaces the entry (5 -> 50, last wins)");

    // --- 4. get_fn_ptr on a single-cast static delegate ---
    void* fp = il2cpp::runtime::icall_delegate_get_fn_ptr(oa);
    Expect(fp == reinterpret_cast<void*>(FnA), "get_fn_ptr returns the leaf's methodPtr");
    Expect(reinterpret_cast<I4Fn>(fp)(7, 0, 0, 0) == 8, "the returned pointer calls FnA (7 -> 8)");
    Expect(il2cpp::runtime::icall_delegate_get_fn_ptr(ob) == reinterpret_cast<void*>(FnB),
           "get_fn_ptr on a second delegate returns its own methodPtr");

    // --- 5. get_fn_ptr null input -> NullReferenceException ---
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_fn_ptr(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "get_fn_ptr(null) raises the NullReferenceException carrier");

    // --- 6. get_fn_ptr on a multicast container -> InvalidOperationException ---
    il2cpp::Il2CppObject* ab = Combine(oa, ob);
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_fn_ptr(ab); },
                        "System.InvalidOperationException,System.Exception"),
           "get_fn_ptr on a multicast container is loudly rejected");

    // --- 7. get_fn_ptr on an instance-method delegate -> InvalidOperationException ---
    a->target = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("cb-target"));
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_fn_ptr(oa); },
                        "System.InvalidOperationException,System.Exception"),
           "get_fn_ptr on a target-bound delegate is loudly rejected (no 'this' in the pointer)");
    a->target = nullptr;

    // --- 8. to_callback registers a DELEGATE under a name ---
    il2cpp::runtime::icall_delegate_to_callback(oa, "cb_del");
    void* delFn = il2cpp::il2cpp_pinvoke_find_callback("cb_del");
    Expect(delFn == reinterpret_cast<void*>(FnA), "to_callback registered the delegate's methodPtr");
    Expect(reinterpret_cast<I4Fn>(delFn)(9, 0, 0, 0) == 10, "the registered callback calls FnA (9 -> 10)");

    // --- 9. to_callback re-registration replaces ---
    il2cpp::runtime::icall_delegate_to_callback(ob, "cb_del");
    Expect(reinterpret_cast<I4Fn>(il2cpp::il2cpp_pinvoke_find_callback("cb_del"))(9, 0, 0, 0) == 90,
           "re-registering cb_del with another delegate replaces the entry (9 -> 90)");

    // --- 10. to_callback error paths ---
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_to_callback(nullptr, "cb_bad"); },
                        "System.NullReferenceException,System.Exception"),
           "to_callback(null, name) raises the NullReferenceException carrier");
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_to_callback(ab, "cb_bad"); },
                        "System.InvalidOperationException,System.Exception"),
           "to_callback on a multicast container is loudly rejected");

    // --- 11. GC: a rooted delegate survives; the callback entry stays valid ---
    // NOTE: this runtime's collection COMPACTS the young generation. The
    // rooted delegate object moves during the collection; the static-root
    // slot is rewritten by the collector, so 'root' (the slot's variable)
    // always holds the fresh address.
    il2cpp::Il2CppObject* root = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&root));
    il2cpp::runtime::icall_delegate_to_callback(root, "cb_root");
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_gc_get_generation(root) >= il2cpp::kGcGeneration1,
           "the rooted delegate survives collection");
    void* rootFn = il2cpp::il2cpp_pinvoke_find_callback("cb_root");
    Expect(rootFn == reinterpret_cast<void*>(FnA),
           "the callback entry (a code address) is unaffected by collection");
    Expect(reinterpret_cast<I4Fn>(rootFn)(3, 0, 0, 0) == 4, "the callback still calls FnA (3 -> 4)");
    Expect(il2cpp::runtime::icall_delegate_get_fn_ptr(root) == reinterpret_cast<void*>(FnA),
           "get_fn_ptr on the moved delegate still returns the methodPtr");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&root));

    // --- 12. P-D2 #3: icall_delegate_from_fn_ptr (GetDelegateForFunctionPointer<T>)
    // wraps a native function pointer into a single-cast delegate ---
    // null ptr -> ArgumentNullException
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_from_fn_ptr(0, 0); },
                        "System.ArgumentNullException,System.Exception"),
           "from_fn_ptr(null) raises the ArgumentNullException carrier");
    // normal path: the returned delegate carries the pointer + typeId, and the
    // pointer invokes the native function (the wrapped delegate's methodPtr IS
    // the native function -- GetDelegateForFunctionPointer wraps raw code).
    il2cpp::Il2CppObject* gd = il2cpp::runtime::icall_delegate_from_fn_ptr(
        reinterpret_cast<intptr_t>(FnA), 0x1234ULL);
    il2cpp::Il2CppDelegate* gdLeaf = reinterpret_cast<il2cpp::Il2CppDelegate*>(gd);
    Expect(gdLeaf != nullptr, "from_fn_ptr returns a delegate object");
    Expect(gdLeaf->methodPtr == reinterpret_cast<void*>(FnA),
           "from_fn_ptr wraps the supplied function pointer as methodPtr");
    Expect(gdLeaf->typeId == 0x1234ULL, "from_fn_ptr stores the supplied typeId");
    Expect(gdLeaf->target == nullptr,
           "from_fn_ptr produces a static (target-free) delegate");
    Expect(reinterpret_cast<I4Fn>(gdLeaf->methodPtr)(40, 0, 0, 0) == 41,
           "the wrapped pointer calls the native function (40 -> 41)");
    // the wrapped delegate combines with a same-type single-cast delegate
    // (identical typeId) -- the net48 GDFFP Combine surface
    il2cpp::Il2CppDelegate* gd2 = il2cpp::il2cpp_delegate_new(
        reinterpret_cast<void*>(FnB), nullptr, 0x1234ULL);
    il2cpp::Il2CppObject* combo = il2cpp::runtime::icall_delegate_combine(gd,
        reinterpret_cast<il2cpp::Il2CppObject*>(gd2));
    Expect(combo != nullptr, "from_fn_ptr delegate combines with a same-typeId delegate");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d delegate-cb assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime delegate-cb tests passed\n");
    return 0;
}
