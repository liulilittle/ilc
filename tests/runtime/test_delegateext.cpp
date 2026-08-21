//------------------------------------------------------------------------------
// test_delegateext.cpp -- multicast delegate extension unit tests.
//
// Coverage (normal + edge + error paths), semantics verified against the
// .NET Framework 4.8 reference run of tests/cases/delegatecase:
//   1. Combine null handling: (null, b) -> b, (a, null) -> a, (null, null)
//      -> null (reference identity of the surviving input).
//   2. Combine of two singles -> one container node; invocation list order
//      [a, b]; invocation returns the LAST result (B wins).
//   3. Appending to a chain (left operand is a chain) keeps order [a, b, c].
//   4. A chain as the RIGHT operand keeps the same list ([a, b, c]) -- tree
//      shape is invisible to the list (net48: a(bc).Equals(abc) == true).
//   5. Remove: head / middle / tail / absent (identity) / single from a
//      2-chain leaves the bare remaining delegate / only element -> null /
//      whole sequence -> null / LAST occurrence (net48: [a,b,a] - a ->
//      [a,b]) / contiguous sequence direction ([a,b,c,a,b] - [a,b] ->
//      [a,b,c]) / non-contiguous sequence -> identity / null inputs.
//   6. Equals: same methodPtr+target, different delegates, order matters,
//      shape does not, null combos.
//   7. GetTarget: null for static-method delegates; explicit target
//      (instance simulation) is returned; a chain returns the LAST leaf's
//      target.
//   8. Invocation: call order observed through a side-effect log; nested
//      tree shapes invoke in flattened order; the last result is returned.
//   9. GC safety: a rooted chain survives collection (generation advance
//      for the container AND every leaf -- the container's two child slots
//      are marked GC references); the flattened list stays intact.
//  10. Error path: invoking a null delegate raises the managed
//      NullReferenceException carrier.
//  11. Instance-method binding: target stored + passed as `this` at invoke.
//  12. get_Method (delegate2 gap): Delegate.Method.Name resolves through the
//      method-name registry -- registered single-cast static, registered
//      instance-bound, multicast LAST leaf, register-replaces (last wins),
//      null input NRE, unregistered method IOE, registry survives GC.
//  13. Exception interruption (multicast2 deep probe): a throwing leaf aborts
//      the invocation -- mid-chain throw skips later leaves, first-leaf throw
//      runs nothing, last-leaf throw runs everything, the chain stays
//      immutable, and a throw inside a NESTED chain skips the whole tail.
//  14. Combine/Remove dedup with INSTANCE-BOUND leaves: same delegate twice
//      and equal twins (same method+target) BOTH concatenate to length 2 (no
//      dedup), both run in order, Remove drops one occurrence by equality.
//  15. Nested-multicast removal deep edges: remove a nested VALUE (contiguous
//      sequence) from a nested source, whole-source removal across shapes,
//      duplicate nested value (LAST occurrence), value larger than the
//      source, and a non-contiguous multi-leaf value (all identity cases).
//  18. batch 177b BUG-2/BUG-3:
//      - BUG-2 Combine type guard (in icall_delegate_combine, driven by the
//        type id stamped on each delegate at creation): the net48
//        heterogeneous-combine ArgumentException guard. Null handling first
//        (net48 order), equal type ids concatenate, differing non-zero type
//        ids raise ArgumentException (net48 probe A: Func<int,int> +
//        Action<int> throws; same-type succeeds), a chain's stored id keeps
//        the container-side check O(1), and the untracked (0) sentinel skips
//        the check conservatively.
//      - BUG-3 delegate GetHashCode (icall_delegate_get_hash_code): same
//        method -> same hash (net48 probe B), the target does NOT contribute
//        (net48 probe B3), equal chains hash equally, the hash is stable
//        across a COMPACTING collection (methodPtr is a code address that
//        never moves -- the old address-hash path broke here), the
//        Object.GetHashCode dispatch routes delegates to the delegate hash
//        (never the ptr >> 3 fallback), and null raises the NRE carrier.
//
// Plain executable + minimal assertion harness (see test_string.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_delegate_ext.h"

#include "il2cpp_value_semantics.h" // icall_object_get_hash_code (BUG-3 routing check)

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "delegate-ext test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

//----------------------------------------------------------------------------
// Test method pointers with the generated-code ABI
// int32_t(*)(int32_t, int32_t, int32_t, int32_t). Every function logs its
// identity (1 = A, 2 = B, 3 = C, 4 = D) into g_order so the tests can
// observe the exact invocation ORDER, not just the returned value.
//----------------------------------------------------------------------------

int32_t g_order[64];
int32_t g_orderLen;

void ResetOrder()
{
    g_orderLen = 0;
}

void LogOrder(int32_t id)
{
    if (g_orderLen < 64)
    {
        g_order[g_orderLen++] = id;
    }
}

bool OrderIs(const int32_t* ids, int32_t count)
{
    if (g_orderLen != count) return false;
    for (int32_t i = 0; i < count; ++i)
    {
        if (g_order[i] != ids[i]) return false;
    }
    return true;
}

int32_t FnA(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    LogOrder(1);
    return a0 + 1;
}

int32_t FnB(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    LogOrder(2);
    return a0 * 10;
}

int32_t FnC(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    LogOrder(3);
    return a0 - 5;
}

int32_t FnD(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    LogOrder(4);
    return a0 + 100;
}

//------------------------------------------------------------------------------
// batch 161/162: the EVENT-HANDLER ABI -- void(*)(Il2CppObject*, Il2CppObject*)
// (two managed-ref args, void return -- EventHandler(object, EventArgs)). The
// runtime dispatches multicast chains through icall_multicast_invoke_rr_void.
//------------------------------------------------------------------------------

void RrA(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    (void)sender; (void)e;
    LogOrder(11);
}

void RrB(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    (void)sender; (void)e;
    LogOrder(12);
}

void RrC(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    (void)sender; (void)e;
    LogOrder(13);
}

// Instance-bound rr_void leaf (the generated instance ABI: this, sender, e).
void RrThis(il2cpp::Il2CppObject* this_, il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    (void)this_; (void)sender; (void)e;
    LogOrder(14);
}

// batch 174 (section 17): argument-RECORDING rr_void leaves. Every leaf stores
// the exact (sender, e) pointers it received -- the pre-174 Rr leaves ignored
// both arguments, so sender/e passthrough (a0, a1 identity) was unasserted.
struct RrRec
{
    il2cpp::Il2CppObject* this_;  // instance-bound leaf: the target (null for static)
    il2cpp::Il2CppObject* sender;
    il2cpp::Il2CppObject* e;
    bool ran;
};

RrRec g_rec[5]; // 0 = A, 1 = B, 2 = C, 3 = D, 4 = instance-bound leaf

void ResetRec()
{
    for (RrRec& r : g_rec)
    {
        r.this_ = nullptr;
        r.sender = nullptr;
        r.e = nullptr;
        r.ran = false;
    }
}

void RrRecA(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    g_rec[0].sender = sender;
    g_rec[0].e = e;
    g_rec[0].ran = true;
    LogOrder(16);
}

void RrRecB(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    g_rec[1].sender = sender;
    g_rec[1].e = e;
    g_rec[1].ran = true;
    LogOrder(17);
}

void RrRecC(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    g_rec[2].sender = sender;
    g_rec[2].e = e;
    g_rec[2].ran = true;
    LogOrder(18);
}

void RrRecD(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    g_rec[3].sender = sender;
    g_rec[3].e = e;
    g_rec[3].ran = true;
    LogOrder(19);
}

void RrRecInst(il2cpp::Il2CppObject* this_, il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    g_rec[4].this_ = this_;
    g_rec[4].sender = sender;
    g_rec[4].e = e;
    g_rec[4].ran = true;
    LogOrder(20);
}

// rr_void leaf that RAISES a managed exception (the RrThrow analogue of the
// i4 FnThrow): the icall has no try/catch, so the leaf's DotNetException must
// propagate out of the dispatch loop and abort the invocation. Logs nothing.
void RrThrow(il2cpp::Il2CppObject* sender, il2cpp::Il2CppObject* e)
{
    (void)sender; (void)e;
    il2cpp::il2cpp_throw_new("System.Exception,System.Exception", "boom");
}

void InvokeRr(il2cpp::Il2CppObject* d, il2cpp::Il2CppObject* sender = nullptr,
              il2cpp::Il2CppObject* e = nullptr)
{
    il2cpp::runtime::icall_multicast_invoke_rr_void(d, sender, e);
}


// FnE is a method that is NEVER registered in the name registry -- the
// get_Method unregistered-path fixture.
int32_t FnE(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a0; (void)a1; (void)a2; (void)a3;
    LogOrder(6);
    return 7;
}

// FnThrow is a leaf that RAISES a managed exception (net48: a throwing
// handler aborts the whole multicast invocation -- every LATER handler is
// skipped; the exception propagates to the caller). It logs NOTHING on the
// order log: its observable event is the throw itself, so the order-log
// assertions cleanly prove whether the leaves AFTER it ran.
int32_t FnThrow(int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a0; (void)a1; (void)a2; (void)a3;
    il2cpp::il2cpp_throw_new("System.Exception,System.Exception", "boom");
    return 0; // unreachable (il2cpp_throw_new always throws)
}

typedef int32_t (*I4Fn)(int32_t, int32_t, int32_t, int32_t);
typedef int32_t (*I4ThisFn)(il2cpp::Il2CppObject*, int32_t, int32_t, int32_t, int32_t);

// Wrap a method pointer into a single-cast delegate (mirrors generated
// ldftn + newobj DelegateCtor). typeId (batch 177b BUG-2) is the delegate
// TYPE id the transpiler stamps at creation (FNV-1a of the type name);
// 0 = untracked.
il2cpp::Il2CppDelegate* MakeDel(I4Fn fn, uint64_t typeId = 0)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn), nullptr, typeId);
}

//------------------------------------------------------------------------------
// Instance-method binding fixtures. A "test instance" is a GC object with one
// int payload field; the instance methods below read it through their `this`
// (arg0) -- exactly what transpiler-generated instance methods do
// (CodeGenerator: instance arg0 = this, Il2CppObject*).
//------------------------------------------------------------------------------

struct TestInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t value;            // instance payload the bound method reads
};

il2cpp::Il2CppClass g_instClass = {
    "InstTest.Counter",           // name
    static_cast<int32_t>(sizeof(TestInst)), // instanceSize
    0u,                           // refFieldBitmap: no reference fields
    0,                            // refFieldCount
    nullptr,                      // baseClass
    nullptr,                      // vtable
    0,                            // vtableSize
    nullptr,                      // interfaces
    0,                            // interfaceCount
};

bool g_instRegistered = false;

TestInst* NewInst(int32_t value)
{
    if (!g_instRegistered)
    {
        il2cpp::il2cpp_class_register(&g_instClass);
        g_instRegistered = true;
    }
    TestInst* t = reinterpret_cast<TestInst*>(il2cpp::il2cpp_gc_alloc(&g_instClass));
    t->value = value;
    return t;
}

// Instance methods with the generated-code instance ABI: arg0 = this.
int32_t InstAdd(il2cpp::Il2CppObject* this_, int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    (void)a1; (void)a2; (void)a3;
    LogOrder(5);
    return reinterpret_cast<TestInst*>(this_)->value + a0;
}

// Wrap a method pointer + target into an instance-bound delegate (mirrors
// the transpiler's future 2-arg emission: il2cpp_delegate_new(&M, obj)).
// typeId (batch 177b BUG-2): the delegate TYPE id; 0 = untracked.
il2cpp::Il2CppDelegate* MakeInstDel(I4ThisFn fn, il2cpp::Il2CppObject* target, uint64_t typeId = 0)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn), target, typeId);
}

// Convenience wrappers over the icall surface under test.
il2cpp::Il2CppObject* Combine(il2cpp::Il2CppObject* a, il2cpp::Il2CppObject* b)
{
    return il2cpp::runtime::icall_delegate_combine(a, b);
}

il2cpp::Il2CppObject* Remove(il2cpp::Il2CppObject* s, il2cpp::Il2CppObject* v)
{
    return il2cpp::runtime::icall_delegate_remove(s, v);
}

int32_t Invoke4(il2cpp::Il2CppObject* d, int32_t x)
{
    return il2cpp::runtime::icall_multicast_invoke_i4(d, x, 0, 0, 0);
}

int32_t ListLen(il2cpp::Il2CppObject* d)
{
    il2cpp::Il2CppObject* arr = il2cpp::runtime::icall_delegate_get_invocation_list(d);
    if (arr == nullptr) return -1;
    return il2cpp::il2cpp_array_get_length(reinterpret_cast<il2cpp::Il2CppArray*>(arr));
}

il2cpp::Il2CppDelegate* ListElem(il2cpp::Il2CppObject* d, int32_t i)
{
    il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(
        il2cpp::runtime::icall_delegate_get_invocation_list(d));
    return *reinterpret_cast<il2cpp::Il2CppDelegate**>(
        il2cpp::il2cpp_array_get_elem_ptr(arr, i));
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

// Compare a managed string's content against an ASCII literal (the method
// names the registry stores are short ASCII identifiers by convention).
bool StrEquals(il2cpp::Il2CppObject* obj, const char* ascii)
{
    if (obj == nullptr || ascii == nullptr) return obj == nullptr && ascii == nullptr;
    il2cpp::Il2CppString* s = reinterpret_cast<il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != len) return false;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i])) return false;
    }
    return true;
}

} // namespace

int main()
{
    il2cpp::Il2CppDelegate* a = MakeDel(FnA);
    il2cpp::Il2CppDelegate* b = MakeDel(FnB);
    il2cpp::Il2CppDelegate* c = MakeDel(FnC);
    il2cpp::Il2CppDelegate* d = MakeDel(FnD);
    il2cpp::Il2CppObject* oa = reinterpret_cast<il2cpp::Il2CppObject*>(a);
    il2cpp::Il2CppObject* ob = reinterpret_cast<il2cpp::Il2CppObject*>(b);
    il2cpp::Il2CppObject* oc = reinterpret_cast<il2cpp::Il2CppObject*>(c);
    il2cpp::Il2CppObject* od = reinterpret_cast<il2cpp::Il2CppObject*>(d);

    // --- 1. Combine null handling (identity of the surviving input) ---
    Expect(Combine(nullptr, ob) == ob, "Combine(null, b) returns b (identity)");
    Expect(Combine(oa, nullptr) == oa, "Combine(a, null) returns a (identity)");
    Expect(Combine(nullptr, nullptr) == nullptr, "Combine(null, null) returns null");

    // --- 2. Combine two singles ---
    il2cpp::Il2CppObject* ab = Combine(oa, ob);
    Expect(ab != nullptr && ab != oa && ab != ob, "Combine(a, b) allocates a new container");
    Expect(ListLen(ab) == 2, "Combine(a, b) has a 2-element invocation list");
    Expect(ListElem(ab, 0) == a && ListElem(ab, 1) == b, "invocation list order is [a, b]");
    ResetOrder();
    Expect(Invoke4(ab, 2) == 20, "multicast invoke returns the LAST result (b(2) = 20)");
    const int32_t abOrder[2] = { 1, 2 };
    Expect(OrderIs(abOrder, 2), "multicast invoke calls a then b (in order)");

    // --- 3. Append to a chain (left operand is a chain) ---
    il2cpp::Il2CppObject* abc = Combine(ab, oc);
    Expect(ListLen(abc) == 3, "appending to a chain yields length 3");
    Expect(ListElem(abc, 0) == a && ListElem(abc, 1) == b && ListElem(abc, 2) == c,
           "appended chain list is [a, b, c]");
    ResetOrder();
    Expect(Invoke4(abc, 3) == -2, "chain invoke returns the last result (c(3) = -2)");
    const int32_t abcOrder[3] = { 1, 2, 3 };
    Expect(OrderIs(abcOrder, 3), "chain invoke order is a, b, c");

    // --- 4. Chain as the RIGHT operand (shape-invariant list) ---
    il2cpp::Il2CppObject* aBC = Combine(oa, Combine(ob, oc));
    Expect(ListLen(aBC) == 3, "a + (b + c) has length 3");
    ResetOrder();
    Expect(Invoke4(aBC, 7) == 2, "a + (b + c) invokes in order with c last (c(7) = 2)");
    const int32_t abc2Order[3] = { 1, 2, 3 };
    Expect(OrderIs(abc2Order, 3), "a + (b + c) invoke order is a, b, c");
    Expect(il2cpp::runtime::icall_delegate_equals(aBC, abc) == true,
           "a(bc).Equals(abc) is true (same list, different tree shape)");

    // --- 5. Remove ---
    // 5a. absent value -> original object identity
    Expect(Remove(abc, od) == abc, "removing an absent delegate returns the source (identity)");
    // 5b. middle
    il2cpp::Il2CppObject* noB = Remove(abc, ob);
    Expect(ListLen(noB) == 2 && ListElem(noB, 0) == a && ListElem(noB, 1) == c,
           "removing the middle leaves [a, c]");
    ResetOrder();
    Expect(Invoke4(noB, 4) == -1, "abc - b invokes with c last (c(4) = -1)");
    // 5c. head
    il2cpp::Il2CppObject* noA = Remove(abc, oa);
    Expect(ListLen(noA) == 2 && ListElem(noA, 0) == b && ListElem(noA, 1) == c,
           "removing the head leaves [b, c]");
    // 5d. tail
    il2cpp::Il2CppObject* noC = Remove(abc, oc);
    Expect(ListLen(noC) == 2 && ListElem(noC, 0) == a && ListElem(noC, 1) == b,
           "removing the tail leaves [a, b]");
    // 5e. single from a 2-chain leaves the BARE remaining delegate
    il2cpp::Il2CppObject* abMinusA = Remove(ab, oa);
    Expect(ListLen(abMinusA) == 1 && ListElem(abMinusA, 0) == b,
           "ab - a is the bare delegate b (single element, no wrapper)");
    ResetOrder();
    Expect(Invoke4(abMinusA, 5) == 50, "ab - a invokes b (b(5) = 50)");
    // 5f. only element -> null
    Expect(Remove(oa, oa) == nullptr, "a - a is null");
    Expect(Remove(oa, ob) == oa, "a - b is a (identity, nothing matched)");
    // 5g. whole sequence -> null
    Expect(Remove(ab, ab) == nullptr, "ab - ab is null (whole list removed)");
    // 5h. LAST occurrence (net48: [a,b,a] - a == [a,b])
    il2cpp::Il2CppObject* aba = Combine(ab, oa);
    Expect(ListLen(aba) == 3, "[a, b, a] has length 3");
    il2cpp::Il2CppObject* abaMinusA = Remove(aba, oa);
    Expect(ListLen(abaMinusA) == 2 && ListElem(abaMinusA, 0) == a && ListElem(abaMinusA, 1) == b,
           "removing a single delegate removes the LAST occurrence ([a,b,a] - a == [a,b])");
    // 5i. contiguous sequence removal (net48: [a,b,c,a,b] - [a,b] == [a,b,c])
    il2cpp::Il2CppObject* abcab = Combine(abc, ab);
    Expect(ListLen(abcab) == 5, "[a, b, c, a, b] has length 5");
    il2cpp::Il2CppObject* abcabMinusAB = Remove(abcab, ab);
    Expect(ListLen(abcabMinusAB) == 3 &&
           ListElem(abcabMinusAB, 0) == a &&
           ListElem(abcabMinusAB, 1) == b &&
           ListElem(abcabMinusAB, 2) == c,
           "removing a multicast value removes the LAST contiguous occurrence "
           "([a,b,c,a,b] - [a,b] == [a,b,c])");
    // 5j. non-contiguous sequence -> identity
    il2cpp::Il2CppObject* ac = Combine(oa, oc);
    Expect(Remove(abc, ac) == abc, "removing a non-contiguous sequence returns the source (identity)");
    // 5k. null inputs
    Expect(Remove(nullptr, oa) == nullptr, "Remove(null, value) is null");
    Expect(Remove(abc, nullptr) == abc, "Remove(source, null) is the source (identity)");

    // --- 6. Equals ---
    Expect(il2cpp::runtime::icall_delegate_equals(oa, oa) == true, "a.Equals(a) is true");
    Expect(il2cpp::runtime::icall_delegate_equals(oa, ob) == false, "a.Equals(b) is false");
    il2cpp::Il2CppDelegate* a2 = MakeDel(FnA);
    Expect(il2cpp::runtime::icall_delegate_equals(oa, reinterpret_cast<il2cpp::Il2CppObject*>(a2)) == true,
           "two delegates over the same method are equal");
    Expect(il2cpp::runtime::icall_delegate_equals(oa, nullptr) == false, "a.Equals(null) is false");
    Expect(il2cpp::runtime::icall_delegate_equals(nullptr, nullptr) == true, "null.Equals(null) is true");
    Expect(il2cpp::runtime::icall_delegate_equals(ab, abc) == false, "ab.Equals(abc) is false");
    il2cpp::Il2CppObject* ab2 = Combine(oa, ob);
    Expect(il2cpp::runtime::icall_delegate_equals(ab, ab2) == true,
           "ab.Equals(a + b) is true (same list, different container)");
    Expect(il2cpp::runtime::icall_delegate_equals(ab, Combine(ob, oa)) == false,
           "ab.Equals(b + a) is false (order matters)");
    Expect(il2cpp::runtime::icall_delegate_equals(abc, aBC) == true,
           "abc.Equals(a(bc)) is true (shape does not matter)");

    // --- 7. GetTarget ---
    Expect(il2cpp::runtime::icall_delegate_get_target(oa) == nullptr,
           "a static-method delegate has a null target");
    il2cpp::Il2CppObject* t1 = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("target-1"));
    a->target = t1; // simulate an instance-method delegate (struct member)
    Expect(il2cpp::runtime::icall_delegate_get_target(oa) == t1,
           "a delegate with an explicit target returns it");
    Expect(il2cpp::runtime::icall_delegate_get_target(ab) == nullptr,
           "a chain returns the LAST leaf's target (b has none)");
    b->target = t1;
    Expect(il2cpp::runtime::icall_delegate_get_target(ab) == t1,
           "a chain returns the LAST leaf's target when set");
    a->target = nullptr;
    b->target = nullptr;
    Expect(il2cpp::runtime::icall_delegate_get_target(oa) == nullptr,
           "restoring the target to null is observed");

    // --- 8. Invocation: nested shapes, order, last result ---
    il2cpp::Il2CppObject* nested = Combine(Combine(oa, ob), Combine(oc, od));
    ResetOrder();
    Expect(Invoke4(nested, 0) == 100, "nested tree invoke returns the last result (d(0) = 100)");
    const int32_t nestedOrder[4] = { 1, 2, 3, 4 };
    Expect(OrderIs(nestedOrder, 4), "nested tree invoke order is a, b, c, d");

    // --- 9. GC safety: the container's child slots are traced ---
    // NOTE: this runtime's collection COMPACTS the young generation (surviving
    // Gen0 objects are copied to a fresh Gen1 segment). C++ locals are not GC
    // roots, so every leaf pointer below is re-derived THROUGH the rooted
    // chain after the collection -- the collector rewrites the static root
    // and the object graph, but cannot update arbitrary stack locals.
    il2cpp::Il2CppObject* root = Combine(Combine(oa, ob), oc);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&root));
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_gc_get_generation(root) >= il2cpp::kGcGeneration1,
           "the rooted container survives collection");
    il2cpp::Il2CppDelegate* r0 = ListElem(root, 0);
    il2cpp::Il2CppDelegate* r1 = ListElem(root, 1);
    il2cpp::Il2CppDelegate* r2 = ListElem(root, 2);
    Expect(r0 != nullptr && r1 != nullptr && r2 != nullptr,
           "every leaf is reachable through the rooted chain after collection");
    Expect(il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(r0)) >= il2cpp::kGcGeneration1 &&
           il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(r1)) >= il2cpp::kGcGeneration1 &&
           il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(r2)) >= il2cpp::kGcGeneration1,
           "every leaf of the rooted chain survives collection (child slots are GC refs)");
    Expect(ListLen(root) == 3, "the flattened invocation list is intact after collection");
    ResetOrder();
    Expect(Invoke4(root, 3) == -2, "invocation works after collection (c(3) = -2)");
    const int32_t gcOrder[3] = { 1, 2, 3 };
    Expect(OrderIs(gcOrder, 3), "post-collection invoke order is a, b, c");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&root));

    // --- 10. Error path: invoking a null delegate ---
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_multicast_invoke_i4(nullptr, 1, 2, 3, 4); },
                        "System.NullReferenceException,System.Exception"),
           "invoking a null delegate raises the NullReferenceException carrier");

    // --- 11. Instance-method binding (target != null) ---
    TestInst* inst1 = NewInst(100);
    TestInst* inst2 = NewInst(1000);

    // 11a. binding stores target + method pointer
    il2cpp::Il2CppDelegate* ia = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst1));
    Expect(ia->target == reinterpret_cast<il2cpp::Il2CppObject*>(inst1),
           "binding stores the target on the delegate");
    Expect(ia->methodPtr == reinterpret_cast<void*>(InstAdd),
           "binding stores the method pointer");

    // 11b. invoke passes the target as `this`; the method reads the instance field
    ResetOrder();
    Expect(Invoke4(reinterpret_cast<il2cpp::Il2CppObject*>(ia), 5) == 105,
           "instance-bound invoke calls the method with the bound target (t1->value + 5 = 105)");
    const int32_t instOrder[1] = { 5 };
    Expect(OrderIs(instOrder, 1), "instance-bound invoke reaches the instance method");

    // 11c. the same method bound to a DIFFERENT instance reads that instance
    il2cpp::Il2CppDelegate* ib = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst2));
    Expect(Invoke4(reinterpret_cast<il2cpp::Il2CppObject*>(ib), 5) == 1005,
           "a different target receives its own instance state (t2->value + 5 = 1005)");

    // 11d. GetTarget returns the bound instance
    Expect(il2cpp::runtime::icall_delegate_get_target(reinterpret_cast<il2cpp::Il2CppObject*>(ia))
               == reinterpret_cast<il2cpp::Il2CppObject*>(inst1),
           "GetTarget returns the bound instance");

    // 11e. static delegates still invoke through the plain 4-arg ABI (no shift)
    ResetOrder();
    Expect(Invoke4(oa, 7) == 8, "a static delegate still invokes through the plain ABI (a(7) = 8)");
    const int32_t staticOrder[1] = { 1 };
    Expect(OrderIs(staticOrder, 1), "a static delegate still reaches the static method");

    // 11f. mixed chain [static, instance]: both called in order, last result wins
    il2cpp::Il2CppObject* mix = Combine(oa, reinterpret_cast<il2cpp::Il2CppObject*>(ia));
    ResetOrder();
    Expect(Invoke4(mix, 10) == 110, "mixed chain invoke returns the LAST result (ia: 100 + 10 = 110)");
    const int32_t mixOrder[2] = { 1, 5 };
    Expect(OrderIs(mixOrder, 2), "mixed chain calls the static leaf then the instance leaf (in order)");

    // 11g. Equals is target-aware through the real API
    il2cpp::Il2CppDelegate* ia2 = MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst1));
    Expect(il2cpp::runtime::icall_delegate_equals(reinterpret_cast<il2cpp::Il2CppObject*>(ia),
           reinterpret_cast<il2cpp::Il2CppObject*>(ia2)) == true,
           "delegates over the same method AND target are equal");
    Expect(il2cpp::runtime::icall_delegate_equals(reinterpret_cast<il2cpp::Il2CppObject*>(ia),
           reinterpret_cast<il2cpp::Il2CppObject*>(ib)) == false,
           "delegates over the same method but DIFFERENT targets are not equal");
    Expect(il2cpp::runtime::icall_delegate_equals(reinterpret_cast<il2cpp::Il2CppObject*>(ia), oa) == false,
           "an instance-bound and a static delegate over the same method are not equal");

    // 11h. Remove is target-aware through the real API
    il2cpp::Il2CppObject* iaib = Combine(reinterpret_cast<il2cpp::Il2CppObject*>(ia),
                                         reinterpret_cast<il2cpp::Il2CppObject*>(ib));
    il2cpp::Il2CppObject* iaibMinusIb = Remove(iaib, reinterpret_cast<il2cpp::Il2CppObject*>(ib));
    Expect(ListLen(iaibMinusIb) == 1 && ListElem(iaibMinusIb, 0) == ia,
           "removing an instance-bound delegate uses method+target matching (leaves [ia])");
    // a SEPARATELY created delegate over the same method AND target also
    // matches (.NET: equal delegates are interchangeable in Remove)
    il2cpp::Il2CppObject* ibTwin = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst2)));
    il2cpp::Il2CppObject* iaibMinusTwin = Remove(iaib, ibTwin);
    Expect(ListLen(iaibMinusTwin) == 1 && ListElem(iaibMinusTwin, 0) == ia,
           "removing a twin delegate over the same method+target matches the bound leaf");
    // a delegate bound to a DIFFERENT target does NOT match
    TestInst* inst3 = NewInst(300);
    il2cpp::Il2CppObject* ibAlt3 = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst3)));
    Expect(Remove(iaib, ibAlt3) == iaib,
           "removing a delegate bound to a DIFFERENT target object leaves the chain unchanged");

    // 11i. GC: the bound target is a traced GC reference -- a rooted
    // instance-bound delegate keeps the instance alive across collection and
    // the collector rewrites the target field to the moved object
    il2cpp::Il2CppObject* gcRoot = reinterpret_cast<il2cpp::Il2CppObject*>(ia);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&gcRoot));
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_gc_get_generation(gcRoot) >= il2cpp::kGcGeneration1,
           "the rooted instance-bound delegate survives collection");
    TestInst* moved = reinterpret_cast<TestInst*>(
        il2cpp::runtime::icall_delegate_get_target(gcRoot));
    Expect(moved != nullptr, "the delegate's target field is a traced GC reference (set after collection)");
    Expect(il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(moved))
               >= il2cpp::kGcGeneration1,
           "the bound instance survives collection through the delegate");
    ResetOrder();
    Expect(Invoke4(gcRoot, 3) == moved->value + 3,
           "invoke post-collection reads the MOVED instance's field (target rewritten by the collector)");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&gcRoot));

    // --- 12. get_Method (delegate2 gap): Delegate.Method.Name ---
    // NOTE: section 11i ran a COMPACTING collection, so every earlier C++
    // local pointer (oa/ob/abc/ia) may be STALE (the collector rewrites GC
    // roots and object graphs, never stack locals -- see section 9). Create
    // FRESH delegates for these tests; the name registry itself is static
    // (methodPtr -> name), so it needs no rooting.
    // 12a. register short names, then resolve single-cast static delegates
    il2cpp::Il2CppDelegate* na = MakeDel(FnA);
    il2cpp::Il2CppDelegate* nb = MakeDel(FnB);
    il2cpp::Il2CppObject* ona = reinterpret_cast<il2cpp::Il2CppObject*>(na);
    il2cpp::Il2CppObject* onb = reinterpret_cast<il2cpp::Il2CppObject*>(nb);
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(FnA), "FnA");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(FnB), "FnB");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(FnC), "FnC");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(InstAdd), "InstAdd");
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(ona), "FnA"),
           "get_Method on a registered static delegate returns its SHORT name");
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(onb), "FnB"),
           "get_Method resolves a second registered method independently");

    // 12b. register-replaces (last registration wins) -- the same contract as
    // class_register / the pinvokeext callback table
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(FnB), "FnB2");
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(onb), "FnB2"),
           "re-registering a methodPtr replaces its name (last wins)");
    il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(FnB), "FnB");
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(onb), "FnB"),
           "restoring the original name is observed");

    // 12c. multicast: the LAST leaf's method name (net48 MulticastDelegate.Method)
    il2cpp::Il2CppObject* nc = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnC));
    il2cpp::Il2CppObject* nabc = Combine(Combine(ona, onb), nc);
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(nabc), "FnC"),
           "get_Method on a chain returns the LAST leaf's method name");

    // 12d. instance-bound delegate: name resolves regardless of the target
    TestInst* inst4 = NewInst(7);
    il2cpp::Il2CppObject* nia = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(inst4)));
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(nia), "InstAdd"),
           "get_Method on an instance-bound delegate returns the method name");

    // 12e. null input -> NullReferenceException carrier
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_method_name(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "get_Method(null) raises the NullReferenceException carrier");

    // 12f. unregistered methodPtr -> InvalidOperationException (loud -- the
    // runtime cannot invent a name for a bare code address)
    il2cpp::Il2CppDelegate* e = MakeDel(FnE);
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_method_name(
                                    reinterpret_cast<il2cpp::Il2CppObject*>(e)); },
                        "System.InvalidOperationException,System.Exception"),
           "get_Method on an unregistered method raises InvalidOperationException");

    // 12g. the registry is static, keyed by methodPtr (a code address): a
    // rooted delegate survives collection and still resolves its name
    il2cpp::Il2CppObject* mnRoot = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeDel(FnA));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&mnRoot));
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_gc_get_generation(mnRoot) >= il2cpp::kGcGeneration1,
           "the rooted delegate survives collection (get_Method fixture)");
    Expect(StrEquals(il2cpp::runtime::icall_delegate_get_method_name(mnRoot), "FnA"),
           "get_Method works on a moved delegate after collection (registry key is the code address)");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&mnRoot));

    // --- 13. Exception interruption (net48: a throwing handler aborts the
    // invocation -- later handlers never run; the chain itself is immutable) ---
    // NOTE: 12g ran a COMPACTING collection, so every earlier C++ local is
    // stale (12g pattern). Create FRESH delegates for these tests.
    il2cpp::Il2CppObject* fA = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
    il2cpp::Il2CppObject* fB = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnB));
    il2cpp::Il2CppObject* fC = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnC));
    il2cpp::Il2CppObject* fD = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnD));
    il2cpp::Il2CppObject* fT = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnThrow));

    // 13a. mid-chain throw: [a, throw, b] -- a runs, b is SKIPPED
    il2cpp::Il2CppObject* midThrow = Combine(Combine(fA, fT), fB);
    ResetOrder();
    Expect(ThrowsDotNet([&]() { Invoke4(midThrow, 1); }, "System.Exception,System.Exception"),
           "invoke of [a, throw, b] raises the managed Exception carrier");
    {
        const int32_t midOrder[1] = { 1 };
        Expect(OrderIs(midOrder, 1), "mid-chain throw stops the chain (only a ran)");
    }
    // 13b. first-leaf throw: NOTHING runs
    il2cpp::Il2CppObject* firstThrow = Combine(fT, fA);
    ResetOrder();
    Expect(ThrowsDotNet([&]() { Invoke4(firstThrow, 1); }, "System.Exception,System.Exception"),
           "invoke of [throw, a] raises the managed Exception carrier");
    Expect(g_orderLen == 0, "first-leaf throw runs nothing");
    // 13c. last-leaf throw: every earlier leaf still runs
    il2cpp::Il2CppObject* lastThrow = Combine(Combine(fA, fB), fT);
    ResetOrder();
    Expect(ThrowsDotNet([&]() { Invoke4(lastThrow, 1); }, "System.Exception,System.Exception"),
           "invoke of [a, b, throw] raises the managed Exception carrier");
    {
        const int32_t lastOrder[2] = { 1, 2 };
        Expect(OrderIs(lastOrder, 2), "last-leaf throw still runs every earlier leaf (a, b)");
    }
    // 13d. the chain is IMMUTABLE: the invocation list is intact after a throw
    Expect(ListLen(midThrow) == 3, "the chain keeps 3 entries after a throwing invoke (immutable)");
    // 13e. throw inside a NESTED chain skips the WHOLE later tail
    il2cpp::Il2CppObject* nestedThrow = Combine(Combine(fA, fT), Combine(fC, fD));
    ResetOrder();
    Expect(ThrowsDotNet([&]() { Invoke4(nestedThrow, 1); }, "System.Exception,System.Exception"),
           "invoke of nested [a, throw, c, d] raises the managed Exception carrier");
    {
        const int32_t nestedThrowOrder[1] = { 1 };
        Expect(OrderIs(nestedThrowOrder, 1), "nested throw skips every later leaf (c, d never ran)");
    }

    // --- 14. Combine/Remove dedup with INSTANCE-BOUND leaves (same method AND
    // same target -- the transpiled C# can't reach this, the target is dropped
    // there). net48: Combine NEVER dedups -- the same leaf object twice still
    // concatenates to length 2; a twin delegate over the same method+target
    // also concatenates. ---
    TestInst* di1 = NewInst(50);
    il2cpp::Il2CppObject* instA = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(di1)));
    il2cpp::Il2CppObject* instB = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(di1)));
    il2cpp::Il2CppDelegate* instADel = reinterpret_cast<il2cpp::Il2CppDelegate*>(instA);
    // 14a. the SAME instance delegate combined with itself: length 2 (no dedup)
    il2cpp::Il2CppObject* selfPair = Combine(instA, instA);
    Expect(ListLen(selfPair) == 2, "combining an instance delegate with ITSELF yields length 2 (no dedup)");
    // 14b. two EQUAL delegates (same method+target): length 2 (no dedup)
    il2cpp::Il2CppObject* twinPair = Combine(instA, instB);
    Expect(ListLen(twinPair) == 2, "combining equal instance delegates (same method+target) yields length 2 (no dedup)");
    // 14c. both leaves still run in order (two identical entries)
    ResetOrder();
    Expect(Invoke4(twinPair, 3) == 53, "twin instance pair invokes both leaves (50 + 3 = 53)");
    {
        const int32_t twinOrder[2] = { 5, 5 };
        Expect(OrderIs(twinOrder, 2), "both instance leaves run in order (same method+target twice)");
    }
    // 14d. Remove drops ONE occurrence of an equal instance delegate
    il2cpp::Il2CppObject* twinMinusOne = Remove(twinPair, instA);
    Expect(ListLen(twinMinusOne) == 1 && ListElem(twinMinusOne, 0) == instADel,
           "removing one equal instance delegate leaves one ([inst,inst] - inst)");
    // 14e. Remove of the SECOND twin (method+target equality) also matches
    il2cpp::Il2CppObject* twinMinusTwin = Remove(twinPair, instB);
    Expect(ListLen(twinMinusTwin) == 1 && ListElem(twinMinusTwin, 0) == instADel,
           "removing an equal twin matches the bound leaf by method+target");
    // 14f. after removing both: null
    Expect(Remove(twinMinusOne, instB) == nullptr, "[inst] - inst is null");

    // --- 15. Nested-multicast removal deep edge cases ---
    // 15a. remove a nested multicast VALUE (contiguous sequence) from a nested
    //      source: ([a,b,c,d] as (a+b)+(c+d)) - [c,d] == [a,b]
    il2cpp::Il2CppObject* srcN = Combine(Combine(fA, fB), Combine(fC, fD));
    il2cpp::Il2CppObject* valCd = Combine(fC, fD);
    il2cpp::Il2CppObject* remCd = Remove(srcN, valCd);
    Expect(ListLen(remCd) == 2 && ListElem(remCd, 0) == reinterpret_cast<il2cpp::Il2CppDelegate*>(fA) &&
           ListElem(remCd, 1) == reinterpret_cast<il2cpp::Il2CppDelegate*>(fB),
           "removing a nested multicast VALUE removes its contiguous sequence ([a,b,c,d] - [c,d])");
    // 15b. value == whole source (different object, same list): null
    Expect(Remove(srcN, Combine(fA, Combine(fB, Combine(fC, fD)))) == nullptr,
           "removing a value equal to the whole source (different tree shape) yields null");
    // 15c. duplicate nested value: LAST occurrence wins ([a,b,a,b] - [a,b] == [a,b])
    il2cpp::Il2CppObject* srcDup = Combine(Combine(fA, fB), Combine(fA, fB));
    il2cpp::Il2CppObject* abVal = Combine(fA, fB);
    il2cpp::Il2CppObject* remDup = Remove(srcDup, abVal);
    Expect(ListLen(remDup) == 2 && ListElem(remDup, 0) == reinterpret_cast<il2cpp::Il2CppDelegate*>(fA) &&
           ListElem(remDup, 1) == reinterpret_cast<il2cpp::Il2CppDelegate*>(fB),
           "duplicate nested value removal drops the LAST occurrence ([a,b,a,b] - [a,b] == [a,b])");
    // 15d. a nested value LARGER than the source: identity
    il2cpp::Il2CppObject* bigVal = Combine(fA, Combine(fB, Combine(fC, fD)));
    Expect(Remove(abVal, bigVal) == abVal,
           "removing a value larger than the source returns the source (identity)");
    // 15e. value is a NON-CONTIGUOUS multi-leaf sequence: identity
    il2cpp::Il2CppObject* src4 = Combine(Combine(fA, fB), Combine(fC, fD)); // [a,b,c,d]
    il2cpp::Il2CppObject* nonContig = Combine(fA, Combine(fC, fD));         // [a,c,d] not contiguous
    Expect(Remove(src4, nonContig) == src4,
           "removing a non-contiguous multi-leaf value returns the source (identity)");

    // --- 16. rr_void invoke (batch 161/162 event-handler ABI) ----------------
    // Two-ref-arg void multicast: single-cast, combined chain order, and the
    // instance-bound leaf (this, sender, e) dispatch.
    il2cpp::Il2CppObject* rrA = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrA), nullptr));
    il2cpp::Il2CppObject* rrB = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrB), nullptr));
    il2cpp::Il2CppObject* rrC = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrC), nullptr));
    // 16a. single-cast invokes exactly one leaf.
    ResetOrder();
    InvokeRr(rrA);
    Expect(OrderIs(g_order, 1) && g_order[0] == 11, "rr_void single-cast invokes its one leaf");
    // 16b. combined chain invokes in order [A, B, C].
    il2cpp::Il2CppObject* chain = Combine(Combine(rrA, rrB), rrC);
    ResetOrder();
    InvokeRr(chain);
    const int32_t rrOrder[3] = { 11, 12, 13 };
    Expect(OrderIs(rrOrder, 3), "rr_void combined chain invokes leaves in order [A,B,C]");
    // 16c. instance-bound leaf: (this, sender, e) -- this in the first slot.
    il2cpp::Il2CppObject* rrInst = reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrThis),
        reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(7))));
    ResetOrder();
    InvokeRr(rrInst);
    Expect(OrderIs(g_order, 1) && g_order[0] == 14, "rr_void instance-bound leaf dispatches (this, sender, e)");
    // 16d. mixed static+instance chain invokes all leaves in order.
    il2cpp::Il2CppObject* mixed = Combine(rrA, rrInst);
    ResetOrder();
    InvokeRr(mixed);
    const int32_t mixedOrder[2] = { 11, 14 };
    Expect(OrderIs(mixedOrder, 2), "rr_void mixed static+instance chain invokes in order");
    // 16e. null chain -> NullReferenceException carrier.
    bool threwNull = false;
    try
    {
        InvokeRr(nullptr);
    }
    catch (il2cpp::DotNetException&)
    {
        threwNull = true;
    }
    Expect(threwNull, "rr_void null delegate throws the NRE carrier");

    // --- 17. rr_void deep coverage (batch 174: audit gaps -- sender/e
    // argument passthrough, exception interruption, GC survival,
    // remove-to-empty, and isinst/baseClass walking) ---
    // NOTE: sections 11i/12g ran COMPACTING collections and 17d below runs
    // one too, so every §17 delegate is created FRESH in this section; any
    // pointer used after 17d's collection is created fresh or re-derived
    // through the rooted chain (17d pattern).
    il2cpp::Il2CppObject* rA = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecA), nullptr));
    il2cpp::Il2CppObject* rB = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecB), nullptr));
    il2cpp::Il2CppObject* rC = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecC), nullptr));
    il2cpp::Il2CppObject* rD = reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecD), nullptr));

    // 17a. ARGUMENT PASSTHROUGH: the icall must deliver the EXACT sender and
    // e pointers (a0, a1) to every leaf -- identity, not just non-null.
    {
        il2cpp::Il2CppObject* chain17a = Combine(Combine(rA, rB), rC);
        il2cpp::Il2CppObject* senderA = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(777));
        il2cpp::Il2CppObject* eArgA = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(888));
        ResetRec();
        ResetOrder();
        InvokeRr(chain17a, senderA, eArgA);
        const int32_t recOrder[3] = { 16, 17, 18 };
        Expect(OrderIs(recOrder, 3), "rr_void [A,B,C] chain invokes every recording leaf");
        Expect(g_rec[0].ran && g_rec[1].ran && g_rec[2].ran,
               "rr_void chain runs every leaf");
        Expect(g_rec[0].sender == senderA && g_rec[1].sender == senderA && g_rec[2].sender == senderA,
               "every rr_void leaf receives the SAME non-null sender pointer (a0 identity)");
        Expect(g_rec[0].e != nullptr && g_rec[1].e != nullptr && g_rec[2].e != nullptr,
               "every rr_void leaf receives a non-null e argument (a1)");
        Expect(g_rec[0].e == eArgA && g_rec[1].e == eArgA && g_rec[2].e == eArgA,
               "every rr_void leaf receives the same e pointer (a1 identity)");
    }
    // 17a2. instance-bound dispatch path: (this, sender, e) -- the target goes
    // in slot 0 and the caller's sender/e STILL arrive in slots 1/2.
    {
        TestInst* inst17a = NewInst(777);
        il2cpp::Il2CppObject* rInst = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecInst),
                                        reinterpret_cast<il2cpp::Il2CppObject*>(inst17a)));
        il2cpp::Il2CppObject* mixed17a = Combine(rA, rInst);
        il2cpp::Il2CppObject* senderA2 = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(999));
        il2cpp::Il2CppObject* eArgA2 = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(1000));
        ResetRec();
        ResetOrder();
        InvokeRr(mixed17a, senderA2, eArgA2);
        const int32_t mixedRecOrder[2] = { 16, 20 };
        Expect(OrderIs(mixedRecOrder, 2), "mixed static+instance rr_void chain invokes in order");
        Expect(g_rec[4].ran && g_rec[4].this_ == reinterpret_cast<il2cpp::Il2CppObject*>(inst17a),
               "instance rr_void leaf receives its target as `this` (slot 0)");
        Expect(g_rec[4].sender == senderA2 && g_rec[4].e == eArgA2,
               "instance rr_void leaf receives the caller's sender/e (slots 1/2)");
    }
    // 17b. NESTED tree: Combine(Combine(A,B),Combine(C,D)) -- flattened order
    // [A,B,C,D] under the rr_void ABI.
    {
        il2cpp::Il2CppObject* nested17b = Combine(Combine(rA, rB), Combine(rC, rD));
        il2cpp::Il2CppObject* senderB = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(11));
        il2cpp::Il2CppObject* eArgB = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(22));
        ResetRec();
        ResetOrder();
        InvokeRr(nested17b, senderB, eArgB);
        const int32_t nestedRecOrder[4] = { 16, 17, 18, 19 };
        Expect(OrderIs(nestedRecOrder, 4), "rr_void nested tree [A,B,C,D] invokes in flattened order");
        Expect(g_rec[0].sender == senderB && g_rec[1].sender == senderB &&
               g_rec[2].sender == senderB && g_rec[3].sender == senderB,
               "nested rr_void tree passes the same sender to all four leaves");
        Expect(g_rec[0].e != nullptr && g_rec[1].e != nullptr &&
               g_rec[2].e != nullptr && g_rec[3].e != nullptr,
               "nested rr_void tree passes a non-null e to all four leaves");
    }
    // 17c. EXCEPTION INTERRUPTION under the rr_void ABI: a throwing leaf aborts
    // the invocation -- later leaves are skipped. The icall
    // (delegateext.cpp:icall_multicast_invoke_rr_void) has no try/catch, so the
    // leaf's DotNetException propagates out of the dispatch loop exactly like
    // the i4 variant (section 13).
    {
        il2cpp::Il2CppObject* rT = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrThrow), nullptr));
        il2cpp::Il2CppObject* senderC = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(33));
        il2cpp::Il2CppObject* eArgC = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(44));
        il2cpp::Il2CppObject* mid17c = Combine(Combine(rA, rT), rB);      // [A, throw, B]
        il2cpp::Il2CppObject* first17c = Combine(rT, rA);                 // [throw, A]
        il2cpp::Il2CppObject* last17c = Combine(Combine(rA, rB), rT);     // [A, B, throw]
        il2cpp::Il2CppObject* nested17c = Combine(Combine(rA, rT), Combine(rC, rD)); // [A, throw, C, D]
        // 17c1. mid-chain throw: A runs, B is SKIPPED.
        ResetRec();
        ResetOrder();
        Expect(ThrowsDotNet([&]() { InvokeRr(mid17c, senderC, eArgC); },
                            "System.Exception,System.Exception"),
               "rr_void invoke of [A, throw, B] raises the managed Exception carrier");
        {
            const int32_t midRecOrder[1] = { 16 };
            Expect(OrderIs(midRecOrder, 1), "rr_void mid-chain throw stops the chain (only A ran)");
        }
        Expect(!g_rec[1].ran, "rr_void mid-chain throw skips the later leaf B");
        // 17c2. first-leaf throw: NOTHING runs.
        ResetRec();
        ResetOrder();
        Expect(ThrowsDotNet([&]() { InvokeRr(first17c, senderC, eArgC); },
                            "System.Exception,System.Exception"),
               "rr_void invoke of [throw, A] raises the managed Exception carrier");
        Expect(g_orderLen == 0 && !g_rec[0].ran, "rr_void first-leaf throw runs nothing");
        // 17c3. last-leaf throw: every earlier leaf still runs.
        ResetRec();
        ResetOrder();
        Expect(ThrowsDotNet([&]() { InvokeRr(last17c, senderC, eArgC); },
                            "System.Exception,System.Exception"),
               "rr_void invoke of [A, B, throw] raises the managed Exception carrier");
        {
            const int32_t lastRecOrder[2] = { 16, 17 };
            Expect(OrderIs(lastRecOrder, 2), "rr_void last-leaf throw still runs A then B");
        }
        // 17c4. the chain is IMMUTABLE after a throwing rr_void invoke.
        Expect(ListLen(mid17c) == 3, "rr_void chain keeps 3 entries after a throwing invoke (immutable)");
        // 17c5. throw inside a NESTED tree skips the WHOLE later tail.
        ResetRec();
        ResetOrder();
        Expect(ThrowsDotNet([&]() { InvokeRr(nested17c, senderC, eArgC); },
                            "System.Exception,System.Exception"),
               "rr_void nested [A, throw, C, D] raises the managed Exception carrier");
        {
            const int32_t nestedThrowOrder[1] = { 16 };
            Expect(OrderIs(nestedThrowOrder, 1), "rr_void nested throw skips every later leaf (C, D never ran)");
        }
        Expect(!g_rec[2].ran && !g_rec[3].ran, "rr_void nested throw leaves C and D unrun");
    }
    // 17d. GC SURVIVAL under the rr_void ABI (section-9 pattern): a rooted
    // rr_void chain survives a compacting collection -- the container AND
    // every leaf advance to Gen1+ -- and a post-collection invoke with a
    // FRESH sender/e (created after the collection, so never stale) still
    // reaches every leaf in order with the passed arguments.
    {
        il2cpp::Il2CppObject* chain17d = Combine(Combine(rA, rB), rC);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&chain17d));
        il2cpp::il2cpp_gc_collect();
        Expect(il2cpp::il2cpp_gc_get_generation(chain17d) >= il2cpp::kGcGeneration1,
               "the rooted rr_void container survives collection");
        il2cpp::Il2CppDelegate* c0 = ListElem(chain17d, 0);
        il2cpp::Il2CppDelegate* c1 = ListElem(chain17d, 1);
        il2cpp::Il2CppDelegate* c2 = ListElem(chain17d, 2);
        Expect(c0 != nullptr && c1 != nullptr && c2 != nullptr,
               "every rr_void leaf is reachable through the rooted chain after collection");
        Expect(il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(c0)) >= il2cpp::kGcGeneration1 &&
               il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(c1)) >= il2cpp::kGcGeneration1 &&
               il2cpp::il2cpp_gc_get_generation(reinterpret_cast<il2cpp::Il2CppObject*>(c2)) >= il2cpp::kGcGeneration1,
               "every rr_void leaf survives collection (child slots are GC refs)");
        Expect(ListLen(chain17d) == 3, "the rr_void invocation list is intact after collection");
        il2cpp::Il2CppObject* senderD = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(55));
        il2cpp::Il2CppObject* eArgD = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(66));
        ResetRec();
        ResetOrder();
        InvokeRr(chain17d, senderD, eArgD);
        {
            const int32_t gcRecOrder[3] = { 16, 17, 18 };
            Expect(OrderIs(gcRecOrder, 3), "post-collection rr_void invoke order is A, B, C");
        }
        Expect(g_rec[0].sender == senderD && g_rec[1].sender == senderD && g_rec[2].sender == senderD,
               "post-collection rr_void invoke still passes the same sender to every leaf");
        Expect(g_rec[0].e != nullptr && g_rec[1].e != nullptr && g_rec[2].e != nullptr,
               "post-collection rr_void invoke passes a non-null e to every leaf");
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&chain17d));
    }
    // 17e. REMOVE-TO-EMPTY on an rr_void chain: stepwise Remove down to null
    // (net48: only element -> null; empty result -> null). NOTE: 17d ran a
    // compacting collection, so create FRESH leaves here.
    {
        il2cpp::Il2CppObject* eA = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecA), nullptr));
        il2cpp::Il2CppObject* eB = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecB), nullptr));
        il2cpp::Il2CppObject* eC = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&RrRecC), nullptr));
        il2cpp::Il2CppObject* chain17e = Combine(Combine(eA, eB), eC);
        Expect(ListLen(chain17e) == 3, "rr_void remove fixture starts at length 3");
        il2cpp::Il2CppObject* remC = Remove(chain17e, eC);
        Expect(ListLen(remC) == 2 && ListElem(remC, 0) == reinterpret_cast<il2cpp::Il2CppDelegate*>(eA) &&
               ListElem(remC, 1) == reinterpret_cast<il2cpp::Il2CppDelegate*>(eB),
               "rr_void chain - C leaves [A, B]");
        il2cpp::Il2CppObject* remB = Remove(remC, eB);
        Expect(ListLen(remB) == 1 && ListElem(remB, 0) == reinterpret_cast<il2cpp::Il2CppDelegate*>(eA),
               "rr_void chain - B leaves the bare delegate A");
        Expect(Remove(remB, eA) == nullptr, "rr_void chain - A is null (only element removed)");
        Expect(Remove(chain17e, chain17e) == nullptr, "rr_void chain - whole chain is null");
    }
    // 17f. isinst / baseClass walk: single-cast AND multicast delegates are
    // instances of System.Delegate -- the MULTICAST container only conforms
    // through the baseClass chain (batch 162 wired g_multicastClass.baseClass
    // = &System_Delegate_class), while non-delegate objects do NOT.
    // il2cpp_is_instance is the exact API the transpiler emits for
    // isinst/castclass.
    {
        il2cpp::Il2CppObject* singleObj = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
        il2cpp::Il2CppObject* multi = Combine(reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnB)),
                                              reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnC)));
        il2cpp::Il2CppObject* nonDel = reinterpret_cast<il2cpp::Il2CppObject*>(NewInst(1));
        il2cpp::Il2CppObject* strObj = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("not-a-delegate"));
        Expect(il2cpp::il2cpp_is_instance(singleObj, &il2cpp::System_Delegate_class),
               "a single-cast delegate is an instance of System.Delegate");
        Expect(il2cpp::il2cpp_is_instance(multi, &il2cpp::System_Delegate_class),
               "a multicast container is an instance of System.Delegate (baseClass walk)");
        Expect(!il2cpp::il2cpp_is_instance(nonDel, &il2cpp::System_Delegate_class),
               "a non-delegate object is NOT an instance of System.Delegate");
        Expect(!il2cpp::il2cpp_is_instance(strObj, &il2cpp::System_Delegate_class),
               "a managed string is NOT an instance of System.Delegate");
        Expect(!il2cpp::il2cpp_is_instance(nullptr, &il2cpp::System_Delegate_class),
               "isinst(null, System.Delegate) is false");
        Expect(!il2cpp::il2cpp_is_instance(multi, &g_instClass),
               "a multicast container is NOT an instance of an unrelated class");
    }

    // --- 18. batch 177b BUG-2/BUG-3: Combine type guard + delegate GetHashCode ---
    // NOTE: every earlier section ran COMPACTING collections (§9/11i/12g/17d),
    // so the §1-17 C++ locals are STALE -- create FRESH delegates here. §18i
    // below runs one more collection; everything before it stays valid, and
    // only the rooted pointer is read after it.
    // BUG-2 fixtures: qA/qB carry DIFFERENT delegate-type ids (the net48 probe
    // shapes -- Func<int,int> id 1 vs Action<int> id 2); qC/qD share one id.
    il2cpp::Il2CppDelegate* qA = MakeDel(FnA, 1);
    il2cpp::Il2CppDelegate* qB = MakeDel(FnB, 2);
    il2cpp::Il2CppDelegate* qC = MakeDel(FnC, 1);
    il2cpp::Il2CppDelegate* qD = MakeDel(FnD, 1);
    il2cpp::Il2CppObject* qoA = reinterpret_cast<il2cpp::Il2CppObject*>(qA);
    il2cpp::Il2CppObject* qoB = reinterpret_cast<il2cpp::Il2CppObject*>(qB);
    il2cpp::Il2CppObject* qoC = reinterpret_cast<il2cpp::Il2CppObject*>(qC);
    il2cpp::Il2CppObject* qoD = reinterpret_cast<il2cpp::Il2CppObject*>(qD);

    // 18a. BUG-2 null handling comes FIRST (net48 Combine checks null before
    // the type check -- reference-source order); a null operand is returned
    // even when the two delegates are of DIFFERENT types.
    Expect(il2cpp::runtime::icall_delegate_combine(nullptr, qoB) == qoB,
           "Combine(null, b) returns b even across delegate types (null check first)");
    Expect(il2cpp::runtime::icall_delegate_combine(qoA, nullptr) == qoA,
           "Combine(a, null) returns a even across delegate types (null check first)");
    Expect(il2cpp::runtime::icall_delegate_combine(nullptr, nullptr) == nullptr,
           "Combine(null, null) returns null");

    // 18b. BUG-2 the net48 probe A verbatim: DIFFERENT delegate types
    // (Func<int,int> id 1 vs Action<int> id 2) -> ArgumentException.
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_combine(qoA, qoB); },
                        "System.ArgumentException,System.Exception"),
           "heterogeneous delegate Combine raises ArgumentException (net48 probe A)");

    // 18c. BUG-2 the net48 probe A2 verbatim: the SAME delegate type (id 1 ==
    // id 1) concatenates.
    il2cpp::Il2CppObject* chkSame = il2cpp::runtime::icall_delegate_combine(qoC, qoD);
    Expect(chkSame != nullptr && chkSame != qoC && chkSame != qoD,
           "same-type Combine allocates a fresh container");
    Expect(ListLen(chkSame) == 2 && ListElem(chkSame, 0) == qC && ListElem(chkSame, 1) == qD,
           "same-type Combine concatenates [c, d] in order");

    // 18d. BUG-2 every pair in a homogeneous chain carries the chain's id
    // (a container is readable O(1) at the next Combine): appending a chain to
    // a same-type leaf succeeds, a DIFFERENT-type leaf still throws, and the
    // chain's stored id keeps the container-side check correct.
    il2cpp::Il2CppObject* chainCD = chkSame;                                   // [c, d], id 1
    Expect(ListLen(il2cpp::runtime::icall_delegate_combine(chainCD, qoA)) == 3,
           "appending a same-type (id 1) leaf to a chain concatenates (chain id readable)");
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_combine(chainCD, qoB); },
                        "System.ArgumentException,System.Exception"),
           "appending a DIFFERENT-type leaf to a chain raises ArgumentException");

    // 18e. BUG-2 the unknown (0) sentinel: a delegate created WITHOUT a type id
    // (untracked creation path) skips the check conservatively -- the runtime
    // cannot prove the types, so it allows (still safe: the invoke side
    // dispatches each leaf by its own ABI).
    il2cpp::Il2CppDelegate* untracked = MakeDel(FnA); // typeId 0
    il2cpp::Il2CppObject* untrackedObj = reinterpret_cast<il2cpp::Il2CppObject*>(untracked);
    Expect(ListLen(il2cpp::runtime::icall_delegate_combine(untrackedObj, qoA)) == 2,
           "Combine with one untracked (typeId 0) delegate concatenates (check skipped)");
    Expect(ListLen(il2cpp::runtime::icall_delegate_combine(untrackedObj, untrackedObj)) == 2,
           "Combine with both delegates untracked (typeId 0) concatenates (check skipped)");
    // 18e2. a chain built from untracked leaves also stays untracked (id 0).
    il2cpp::Il2CppObject* untrackedChain = il2cpp::runtime::icall_delegate_combine(untrackedObj, qoC);
    Expect(ListLen(untrackedChain) == 2,
           "combining an untracked delegate with a typed one concatenates");

    // 18f. BUG-3: the SAME method (+ same null target) -> the SAME hash
    // (net48 probe B: two delegates over the same method hash identically).
    il2cpp::Il2CppDelegate* gA = MakeDel(FnA);
    Expect(il2cpp::runtime::icall_delegate_get_hash_code(qoA) ==
           il2cpp::runtime::icall_delegate_get_hash_code(reinterpret_cast<il2cpp::Il2CppObject*>(gA)),
           "GetHashCode of two delegates over the same method is equal");

    // 18f2. BUG-3 (net48 probe B3): the hash does NOT depend on the target --
    // the same method bound to DIFFERENT instances hashes identically (net48
    // Delegate.GetHashCode is type-derived; the reference run showed equal
    // hashes for different-target instance delegates).
    TestInst* t18a = NewInst(5);
    TestInst* t18b = NewInst(6);
    il2cpp::Il2CppObject* instDel1 = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t18a)));
    il2cpp::Il2CppObject* instDel2 = reinterpret_cast<il2cpp::Il2CppObject*>(
        MakeInstDel(InstAdd, reinterpret_cast<il2cpp::Il2CppObject*>(t18b)));
    Expect(il2cpp::runtime::icall_delegate_get_hash_code(instDel1) ==
           il2cpp::runtime::icall_delegate_get_hash_code(instDel2),
           "GetHashCode ignores the target (same method, different targets, equal hash -- net48 probe B3)");

    // 18g. BUG-3: DIFFERENT methods hash differently (the FNV-1a methodPtr
    // mix discriminates the two fixed code addresses).
    Expect(il2cpp::runtime::icall_delegate_get_hash_code(qoA) !=
           il2cpp::runtime::icall_delegate_get_hash_code(qoB),
           "GetHashCode of delegates over DIFFERENT methods differs");

    // 18h. BUG-3: a multicast chain's hash covers EVERY leaf -- two chains
    // with the same list (different containers) hash identically. (The two
    // chains combine freely: chA/chB are untracked (typeId 0) so no type
    // guard fires -- the same-type combine path.)
    il2cpp::Il2CppObject* chA = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
    il2cpp::Il2CppObject* chB = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnB));
    il2cpp::Il2CppObject* chainX = il2cpp::runtime::icall_delegate_combine(chA, chB);
    il2cpp::Il2CppObject* chainY = il2cpp::runtime::icall_delegate_combine(chA, chB);
    Expect(il2cpp::runtime::icall_delegate_get_hash_code(chainX) ==
           il2cpp::runtime::icall_delegate_get_hash_code(chainY),
           "GetHashCode of equal chains (same list, different containers) is equal");

    // 18j. BUG-3 routing: the Object.GetHashCode dispatch (valuesemantics.cpp
    // icall_object_get_hash_code) routes delegate receivers -- single-cast
    // AND multicast containers -- to the delegate hash, never the ptr >> 3
    // address fallback.
    il2cpp::Il2CppObject* dispA = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
    Expect(il2cpp::runtime::icall_object_get_hash_code(dispA) ==
           il2cpp::runtime::icall_delegate_get_hash_code(dispA),
           "Object.GetHashCode on a single-cast delegate routes to the delegate hash");
    Expect(il2cpp::runtime::icall_object_get_hash_code(chainX) ==
           il2cpp::runtime::icall_delegate_get_hash_code(chainX),
           "Object.GetHashCode on a multicast container routes to the delegate hash");

    // 18i. BUG-3 GC STABILITY: the hash is methodPtr-based (a code address
    // never moves), so a ROOTED delegate hashes identically across a
    // COMPACTING collection (the old address-hash path returned a different
    // value after the object moved).
    il2cpp::Il2CppObject* hRoot = reinterpret_cast<il2cpp::Il2CppObject*>(MakeDel(FnA));
    const int32_t hBefore = il2cpp::runtime::icall_delegate_get_hash_code(hRoot);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&hRoot));
    il2cpp::il2cpp_gc_collect();
    Expect(il2cpp::il2cpp_gc_get_generation(hRoot) >= il2cpp::kGcGeneration1,
           "the rooted GetHashCode fixture survives collection");
    Expect(il2cpp::runtime::icall_delegate_get_hash_code(hRoot) == hBefore,
           "GetHashCode is stable across a compacting collection (methodPtr never moves)");
    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&hRoot));

    // 18k. BUG-3: null input -> NullReferenceException carrier.
    Expect(ThrowsDotNet([&]() { il2cpp::runtime::icall_delegate_get_hash_code(nullptr); },
                        "System.NullReferenceException,System.Exception"),
           "GetHashCode(null) raises the NullReferenceException carrier");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d delegate-ext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime delegate-ext tests passed\n");
    return 0;
}

