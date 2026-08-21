//------------------------------------------------------------------------------
// test_linq.cpp -- System.Linq array-family runtime unit tests (T1 card:
// count / count(predicate) / any / all / sum / min / max).
//
// Coverage (docs/design/linq-domain.md §5 T1):
//   1. count/any/all/sum/min/max over empty / single / multi-element arrays.
//   2. Empty-sequence truth table: Count -> 0, Any()/Any(pred) -> false,
//      All(pred) -> true, Sum -> 0, Min/Max -> InvalidOperationException;
//      predicates are NEVER called on an empty sequence (counter asserts).
//   3. Sum overflow is CHECKED (net48 Enumerable.Sum, D-LINQ-3): INT32_MAX+1
//      and INT32_MIN+(-1) throw OverflowException; negative sums are exact.
//   4. Min/Max boundaries: INT32_MIN/INT32_MAX, single element, negatives.
//   5. Null source -> ArgumentNullException on EVERY entry.
//   6. Null predicate -> ArgumentNullException on every predicate entry.
//   7. Any/All short-circuit: a side-effect counter proves the predicate is
//      called exactly up to the first hit / first miss.
//   8. Predicates arrive as real delegate OBJECTS (il2cpp_delegate_new -- the
//      generated ldftn + newobj DelegateCtor shape); the runtime extracts
//      methodPtr itself (never a bare function pointer at the call site).
//   9. Multicast predicates are loudly rejected with InvalidOperationException;
//      INSTANCE-BOUND single-cast predicates (a bound instance method, the
//      linqcase shape p.IsEven / p.IsBig) are FIRST-CLASS callbacks -- the
//      runtime dispatches them through the generated instance ABI (target in
//      the `this` slot), reading the bound instance's payload (linqib mode).
//  10. ref family (string[]): any/all walk the _ref entries with
//      bool(*)(Il2CppObject*) predicates; count works type-independently.
//  11. i8/d/b families are NOT implemented (newarr not extended, §4.2) --
//      no stub exists to call (asserted at compile time by their absence).
//  12. count_i4 / count_ref (the net48 Count<T>(IEnumerable<T>,
//      Func<T,bool>) predicate overload -- the linqops e2e uses Count(even)):
//      exact match counts, NO short circuit (the predicate is called on EVERY
//      element, unlike Any/All), empty -> 0 with the predicate never called,
//      null source / null predicate -> ArgumentNullException, multicast
//      predicate -> InvalidOperationException, instance-bound predicate ->
//      dispatched per binding (the bound instance's payload decides).
//  13. GC: a predicate that runs il2cpp_gc_collect() mid-loop (a compacting
//      collection moves the source array) must not corrupt count/any/all --
//      the icalls root the source AND the delegate across the callback loop
//      (d->target is re-read through the rooted slot).
//
// Plain executable + minimal assertion harness (see test_delegate_cb.cpp).
//------------------------------------------------------------------------------

#include "linq.h"

#include "il2cpp_array.h"
#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "linq test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// The generated-code callback ABI for Func<int,bool> / Func<T,bool>:
// predicates are single-cast static-method delegates called through
// methodPtr with one element argument.
typedef bool (*I4PredFn)(int32_t);
typedef bool (*RefPredFn)(il2cpp::Il2CppObject*);

// Side-effect counters (short-circuit / never-called assertions).
int g_evenCalls = 0; // predicate invocations into PredEven
int g_longCalls = 0; // predicate invocations into PredLenGreaterThan2

bool PredEven(int32_t v)
{
    ++g_evenCalls;
    return (v & 1) == 0;
}

bool PredAlwaysTrue(int32_t v)
{
    (void)v;
    return true;
}

bool PredAlwaysFalse(int32_t v)
{
    (void)v;
    return false;
}

bool PredLenGreaterThan2(il2cpp::Il2CppObject* s)
{
    ++g_longCalls;
    return il2cpp::il2cpp_string_get_length(s) > 2;
}

// GC probes: force a compacting collection in the middle of the icall's
// element loop (test_linq_map.cpp pattern -- the collector moves the source
// array; the icall must root it across the callback).
int g_gcCalls = 0;
bool GcPredI4(int32_t v)
{
    ++g_gcCalls;
    il2cpp::il2cpp_gc_collect();
    return (v & 1) == 0;
}

bool GcPredRef(il2cpp::Il2CppObject* s)
{
    il2cpp::il2cpp_gc_collect();
    return il2cpp::il2cpp_string_get_length(s) > 2;
}

//------------------------------------------------------------------------------
// Instance-bound predicate fixtures (the linqcase shape: p.IsBig). A test
// instance is a GC object with an int payload; the instance methods below
// read it through their `this` (arg0) -- exactly what transpiler-generated
// instance methods do (CodeGenerator: instance arg0 = this, so the runtime
// calls (target, args...) for an instance-bound delegate).
//------------------------------------------------------------------------------
struct LinqPredInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t threshold;        // instance payload the bound method reads
};

il2cpp::Il2CppClass g_predInstClass = {
    "LinqTest.PredInst",                 // name
    static_cast<int32_t>(sizeof(LinqPredInst)), // instanceSize
    0u,                                  // refFieldBitmap: no reference fields
    0,                                   // refFieldCount
    nullptr,                             // baseClass
    nullptr,                             // vtable
    0,                                   // vtableSize
    nullptr,                             // interfaces
    0,                                   // interfaceCount
};

bool g_predInstRegistered = false;

LinqPredInst* NewPredInst(int32_t threshold)
{
    if (!g_predInstRegistered)
    {
        il2cpp::il2cpp_class_register(&g_predInstClass);
        g_predInstRegistered = true;
    }
    LinqPredInst* t = reinterpret_cast<LinqPredInst*>(
        il2cpp::il2cpp_gc_alloc(&g_predInstClass));
    t->threshold = threshold;
    return t;
}

// Instance methods with the generated-code instance ABI: arg0 = this.
// (linqcase LinqPred.IsEven/IsBig shape -- reads the instance payload.)
bool InstIsBig(il2cpp::Il2CppObject* this_, int32_t v)
{
    return v > reinterpret_cast<LinqPredInst*>(this_)->threshold;
}

// GC probe: reads the bound payload FIRST (a managed pointer must not be held
// across a collection inside the callback -- the int32 threshold is a by-value
// copy), then forces a compaction mid-loop.
bool InstIsBigCollect(il2cpp::Il2CppObject* this_, int32_t v)
{
    const int32_t threshold = reinterpret_cast<LinqPredInst*>(this_)->threshold;
    il2cpp::il2cpp_gc_collect();
    return v > threshold;
}

// Ref-family instance method: string length > the bound instance's threshold.
bool InstIsLong(il2cpp::Il2CppObject* this_, il2cpp::Il2CppObject* s)
{
    return il2cpp::il2cpp_string_get_length(s) > reinterpret_cast<LinqPredInst*>(this_)->threshold;
}

// The generated-code instance ABI: bool(*)(Il2CppObject* this, int32_t v).
typedef bool (*I4PredThisFn)(il2cpp::Il2CppObject*, int32_t);
typedef bool (*RefPredThisFn)(il2cpp::Il2CppObject*, il2cpp::Il2CppObject*);

// Wrap a ref-family method pointer + target into an instance-bound delegate.
il2cpp::Il2CppDelegate* MakeRefInstDel(RefPredThisFn fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn), target);
}

// Wrap a method pointer + target into an instance-bound delegate (mirrors
// the transpiler's 2-arg emission: il2cpp_delegate_new(&M, obj)).
il2cpp::Il2CppDelegate* MakeI4InstDel(I4PredThisFn fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn), target);
}

// Wrap a predicate into a single-cast delegate (mirrors the generated
// ldftn + newobj DelegateCtor; the LINQ icall receives the delegate OBJECT).
il2cpp::Il2CppDelegate* MakeI4Del(I4PredFn fn)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn));
}

il2cpp::Il2CppDelegate* MakeRefDel(RefPredFn fn)
{
    return il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(fn));
}

il2cpp::Il2CppArray* MakeI4Array(const int32_t* vals, int32_t n)
{
    il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(nullptr, 4, false, n);
    int32_t* dst = reinterpret_cast<int32_t*>(a->data);
    for (int32_t i = 0; i < n; ++i)
    {
        dst[i] = vals[i];
    }
    return a;
}

il2cpp::Il2CppArray* MakeStringArray(const char* const* vals, int32_t n)
{
    il2cpp::Il2CppArray* a = il2cpp::il2cpp_array_new(
        il2cpp::il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, n);
    il2cpp::Il2CppObject** dst = reinterpret_cast<il2cpp::Il2CppObject**>(a->data);
    for (int32_t i = 0; i < n; ++i)
    {
        dst[i] = reinterpret_cast<il2cpp::Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs(vals[i]));
    }
    return a;
}

// Raise-and-catch helper: run 'fn', capture the managed exception carrier
// and compare the full type-name chain (see test_delegate_cb.cpp).
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

const char* kArgNullChain = "System.ArgumentNullException,System.ArgumentNullException,"
                            "System.ArgumentException,System.SystemException,System.Exception";
const char* kInvalidOpChain = "System.InvalidOperationException,System.InvalidOperationException,"
                              "System.SystemException,System.Exception";
const char* kOverflowChain = "System.OverflowException,System.OverflowException,"
                             "System.ArithmeticException,System.SystemException,System.Exception";

} // namespace

int main()
{
    // --- 1. count: empty / single / multi; ref family ---
    const int32_t kEmpty[1] = {0};
    const int32_t kSingle[1] = {7};
    const int32_t kMulti[3] = {5, -2, 0};
    il2cpp::Il2CppArray* empty = MakeI4Array(kEmpty, 0);
    il2cpp::Il2CppArray* single = MakeI4Array(kSingle, 1);
    il2cpp::Il2CppArray* multi = MakeI4Array(kMulti, 3);

    Expect(il2cpp::il2cpp_linq_count(empty) == 0, "count(empty) == 0");
    Expect(il2cpp::il2cpp_linq_count(single) == 1, "count(single) == 1");
    Expect(il2cpp::il2cpp_linq_count(multi) == 3, "count(multi) == 3");

    // --- 2. any(): empty -> false, non-empty -> true ---
    Expect(!il2cpp::il2cpp_linq_any(empty), "any(empty) == false");
    Expect(il2cpp::il2cpp_linq_any(single), "any(single) == true");
    Expect(il2cpp::il2cpp_linq_any(multi), "any(multi) == true");

    // --- 3. sum: exact, negatives, checked overflow (net48 Enumerable.Sum
    //    is CHECKED -- D-LINQ-3), empty -> 0 ---
    const int32_t kSum1[3] = {1, 2, 3};
    const int32_t kSumNeg[3] = {-2, -5, -3};
    const int32_t kSumWrap[2] = {INT32_MAX, 1};
    const int32_t kSumWrapNeg[2] = {INT32_MIN, -1};
    const int32_t kSumExtreme[2] = {INT32_MIN, INT32_MAX};
    Expect(il2cpp::il2cpp_linq_sum_i4(MakeI4Array(kSum1, 3)) == 6, "sum({1,2,3}) == 6");
    Expect(il2cpp::il2cpp_linq_sum_i4(MakeI4Array(kSumNeg, 3)) == -10, "sum({-2,-5,-3}) == -10");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_sum_i4(MakeI4Array(kSumWrap, 2)); }, kOverflowChain),
           "sum({INT32_MAX,1}) throws OverflowException (checked, net48)");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_sum_i4(MakeI4Array(kSumWrapNeg, 2)); }, kOverflowChain),
           "sum({INT32_MIN,-1}) throws OverflowException (checked, net48)");
    Expect(il2cpp::il2cpp_linq_sum_i4(MakeI4Array(kSumExtreme, 2)) == -1,
           "sum({INT32_MIN,INT32_MAX}) == -1");
    Expect(il2cpp::il2cpp_linq_sum_i4(empty) == 0, "sum(empty) == 0");

    // --- 4. min/max: extremes, single element, negatives ---
    const int32_t kMinMax[4] = {5, 3, 9, 1};
    const int32_t kOnlyNeg[1] = {-4};
    const int32_t kExtremes[2] = {INT32_MAX, INT32_MIN};
    Expect(il2cpp::il2cpp_linq_min_i4(MakeI4Array(kMinMax, 4)) == 1, "min({5,3,9,1}) == 1");
    Expect(il2cpp::il2cpp_linq_max_i4(MakeI4Array(kMinMax, 4)) == 9, "max({5,3,9,1}) == 9");
    Expect(il2cpp::il2cpp_linq_min_i4(MakeI4Array(kOnlyNeg, 1)) == -4, "min({-4}) == -4");
    Expect(il2cpp::il2cpp_linq_max_i4(MakeI4Array(kOnlyNeg, 1)) == -4, "max({-4}) == -4");
    Expect(il2cpp::il2cpp_linq_min_i4(MakeI4Array(kExtremes, 2)) == INT32_MIN,
           "min({INT32_MAX,INT32_MIN}) == INT32_MIN");
    Expect(il2cpp::il2cpp_linq_max_i4(MakeI4Array(kExtremes, 2)) == INT32_MAX,
           "max({INT32_MAX,INT32_MIN}) == INT32_MAX");

    // --- 5. min/max on empty -> InvalidOperationException ---
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_min_i4(empty); }, kInvalidOpChain),
           "min(empty) raises InvalidOperationException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_max_i4(empty); }, kInvalidOpChain),
           "max(empty) raises InvalidOperationException");

    // --- 6. null source -> ArgumentNullException on EVERY entry ---
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_count(nullptr); }, kArgNullChain),
           "count(null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_any(nullptr); }, kArgNullChain),
           "any(null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_sum_i4(nullptr); }, kArgNullChain),
           "sum(null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_min_i4(nullptr); }, kArgNullChain),
           "min(null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_max_i4(nullptr); }, kArgNullChain),
           "max(null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_any_i4(nullptr, nullptr); }, kArgNullChain),
           "any_i4(null, null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_all_ref(nullptr, nullptr); }, kArgNullChain),
           "all_ref(null, null) raises ArgumentNullException");

    // --- 7. any_i4: hit short-circuits (calls stop at the first true) ---
    const int32_t kOddThenEven[4] = {1, 3, 5, 2};
    g_evenCalls = 0;
    Expect(il2cpp::il2cpp_linq_any_i4(MakeI4Array(kOddThenEven, 4),
                                      reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "any_i4({1,3,5,2}, even) == true");
    Expect(g_evenCalls == 4, "any_i4 hit: predicate called exactly 4 times (1,3,5 miss, 2 hit)");

    // --- 8. any_i4: miss calls every element; empty calls none ---
    const int32_t kAllOdd[4] = {1, 3, 5, 7};
    g_evenCalls = 0;
    Expect(!il2cpp::il2cpp_linq_any_i4(MakeI4Array(kAllOdd, 4),
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "any_i4({1,3,5,7}, even) == false");
    Expect(g_evenCalls == 4, "any_i4 miss: predicate called exactly 4 times");
    g_evenCalls = 0;
    Expect(!il2cpp::il2cpp_linq_any_i4(empty,
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "any_i4(empty, even) == false");
    Expect(g_evenCalls == 0, "any_i4(empty): predicate NEVER called");

    // --- 9. all_i4: miss short-circuits; full pass; empty -> true ---
    const int32_t kEvenThenOdd[3] = {2, 4, 3};
    const int32_t kAllEven[3] = {2, 4, 6};
    g_evenCalls = 0;
    Expect(!il2cpp::il2cpp_linq_all_i4(MakeI4Array(kEvenThenOdd, 3),
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "all_i4({2,4,3}, even) == false");
    Expect(g_evenCalls == 3, "all_i4 miss: predicate called exactly 3 times (2,4 pass, 3 fails)");
    g_evenCalls = 0;
    Expect(il2cpp::il2cpp_linq_all_i4(MakeI4Array(kAllEven, 3),
                                      reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "all_i4({2,4,6}, even) == true");
    Expect(g_evenCalls == 3, "all_i4 pass: predicate called exactly 3 times");
    g_evenCalls = 0;
    Expect(il2cpp::il2cpp_linq_all_i4(empty,
                                      reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredEven))),
           "all_i4(empty, even) == true (vacuous truth)");
    Expect(g_evenCalls == 0, "all_i4(empty): predicate NEVER called");

    // --- 10. constant predicates ---
    Expect(il2cpp::il2cpp_linq_all_i4(multi,
                                      reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredAlwaysTrue))),
           "all_i4(multi, always-true) == true");
    Expect(!il2cpp::il2cpp_linq_any_i4(multi,
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(PredAlwaysFalse))),
           "any_i4(multi, always-false) == false");

    // --- 11. null predicate -> ArgumentNullException ---
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_any_i4(multi, nullptr); }, kArgNullChain),
           "any_i4(src, null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_all_i4(multi, nullptr); }, kArgNullChain),
           "all_i4(src, null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_any_ref(multi, nullptr); }, kArgNullChain),
           "any_ref(src, null) raises ArgumentNullException");
    Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_all_ref(multi, nullptr); }, kArgNullChain),
           "all_ref(src, null) raises ArgumentNullException");

    // --- 12. multicast predicate -> InvalidOperationException; INSTANCE-BOUND
    //    predicates are FIRST-CLASS callbacks (linqib mode: dispatched with the
    //    bound target in the generated `this` slot) ---
    {
        il2cpp::Il2CppDelegate* bound = MakeI4Del(PredEven);
        il2cpp::Il2CppDelegate* extra = MakeI4Del(PredAlwaysTrue);
        il2cpp::Il2CppObject* multicast = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<il2cpp::Il2CppObject*>(bound),
            reinterpret_cast<il2cpp::Il2CppObject*>(extra));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_any_i4(multi, multicast); }, kInvalidOpChain),
               "any_i4 with a MULTICAST predicate is loudly rejected");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_count_i4(multi, multicast); }, kInvalidOpChain),
               "count_i4 with a MULTICAST predicate is loudly rejected");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_all_i4(multi, multicast); }, kInvalidOpChain),
               "all_i4 with a MULTICAST predicate is loudly rejected");
    }

    // The linqcase shape: an instance method (p.IsBig) bound to a delegate.
    // The runtime dispatches (target, args...) -- the bound instance's payload
    // decides each element. count(even)/any/all are exercised in linqib mode.
    {
        const int32_t kMixed[6] = {1, 6, 3, 8, 2, 9};
        const int32_t kAllBig[3] = {7, 8, 9};
        const int32_t kAllSmall[3] = {1, 2, 3};
        il2cpp::Il2CppArray* mixed = MakeI4Array(kMixed, 6);
        il2cpp::Il2CppArray* allBig = MakeI4Array(kAllBig, 3);
        il2cpp::Il2CppArray* allSmall = MakeI4Array(kAllSmall, 3);

        // Instance "p" with threshold 5: IsBig matches {6,8,9} in kMixed.
        LinqPredInst* p = NewPredInst(5);
        il2cpp::Il2CppObject* big = reinterpret_cast<il2cpp::Il2CppObject*>(
            MakeI4InstDel(&InstIsBig, reinterpret_cast<il2cpp::Il2CppObject*>(p)));

        Expect(il2cpp::il2cpp_linq_count_i4(mixed, big) == 3,
               "count_i4 instance-bound: counts the 3 elements above threshold 5");
        Expect(il2cpp::il2cpp_linq_any_i4(mixed, big),
               "any_i4 instance-bound: finds an element above threshold 5");
        Expect(!il2cpp::il2cpp_linq_all_i4(mixed, big),
               "all_i4 instance-bound: not every element is above threshold 5");
        Expect(il2cpp::il2cpp_linq_all_i4(allBig, big),
               "all_i4 instance-bound: every element above threshold 5");

        // The SAME method bound to a DIFFERENT instance reads its payload.
        LinqPredInst* q = NewPredInst(8);
        il2cpp::Il2CppObject* big8 = reinterpret_cast<il2cpp::Il2CppObject*>(
            MakeI4InstDel(&InstIsBig, reinterpret_cast<il2cpp::Il2CppObject*>(q)));
        Expect(il2cpp::il2cpp_linq_count_i4(mixed, big8) == 1,
               "a different bound instance counts with its own payload ({9})");
        Expect(!il2cpp::il2cpp_linq_any_i4(allSmall, big8),
               "any_i4 instance-bound: no element above the higher threshold 8");

        // Null predicate still raises ArgumentNullException (validation is
        // independent of the binding form).
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_count_i4(mixed, nullptr); }, kArgNullChain),
               "instance-bound path keeps the null-predicate ArgumentNullException");

        // GC: an instance-bound predicate that collects mid-loop. The bound
        // payload is read into a by-value copy BEFORE the collect (a managed
        // pointer must never be held across a collection inside the
        // callback); the delegate is rooted and d->target re-read.
        int32_t many[16];
        for (int32_t i = 0; i < 16; ++i) many[i] = i + 1;
        il2cpp::Il2CppArray* gcSrc = MakeI4Array(many, 16);
        LinqPredInst* gp = NewPredInst(4);
        il2cpp::Il2CppObject* gcBig = reinterpret_cast<il2cpp::Il2CppObject*>(
            MakeI4InstDel(&InstIsBigCollect, reinterpret_cast<il2cpp::Il2CppObject*>(gp)));
        Expect(il2cpp::il2cpp_linq_count_i4(gcSrc, gcBig) == 12,
               "count_i4 instance-bound collecting predicate counts all 12 above-threshold elements");
        Expect(il2cpp::il2cpp_linq_any_i4(gcSrc, gcBig),
               "any_i4 instance-bound collecting predicate still finds a match");
        Expect(!il2cpp::il2cpp_linq_all_i4(gcSrc, gcBig),
               "all_i4 instance-bound collecting predicate still fails on a small element");
    }

    // --- 13. ref family: string[] any/all via the _ref entries ---
    const char* const kStr3[3] = {"a", "bb", "ccc"};
    const char* const kStr2[2] = {"a", "bb"};
    il2cpp::Il2CppArray* strs3 = MakeStringArray(kStr3, 3);
    il2cpp::Il2CppArray* strs2 = MakeStringArray(kStr2, 2);
    il2cpp::Il2CppArray* strsEmpty = MakeStringArray(kStr2, 0);

    Expect(il2cpp::il2cpp_linq_count(strs3) == 3, "count(string[3]) == 3 (type-independent)");
    g_longCalls = 0;
    Expect(il2cpp::il2cpp_linq_any_ref(strs3,
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(PredLenGreaterThan2))),
           "any_ref({a,bb,ccc}, len>2) == true");
    Expect(g_longCalls == 3, "any_ref: predicate called through the whole array (hit at last)");
    g_longCalls = 0;
    Expect(!il2cpp::il2cpp_linq_any_ref(strs2,
                                        reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(PredLenGreaterThan2))),
           "any_ref({a,bb}, len>2) == false");
    Expect(g_longCalls == 2, "any_ref miss: predicate called exactly 2 times");
    g_longCalls = 0;
    Expect(!il2cpp::il2cpp_linq_all_ref(strs2,
                                        reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(PredLenGreaterThan2))),
           "all_ref({a,bb}, len>2) == false");
    Expect(g_longCalls == 1, "all_ref: short-circuits at the first false ('a')");
    g_longCalls = 0;
    Expect(il2cpp::il2cpp_linq_all_ref(strsEmpty,
                                       reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(PredLenGreaterThan2))),
           "all_ref(empty, len>2) == true (vacuous truth)");
    Expect(g_longCalls == 0, "all_ref(empty): predicate NEVER called");

    // --- 13b. ref family, INSTANCE-BOUND predicate (linqib mode): the bound
    //    instance's threshold decides len>threshold for count/any/all. ---
    {
        const char* const kStrs[4] = {"a", "bbb", "c", "dddd"};
        const char* const kShortOnly[2] = {"a", "bb"};
        il2cpp::Il2CppArray* strs = MakeStringArray(kStrs, 4);
        il2cpp::Il2CppArray* shortOnly = MakeStringArray(kShortOnly, 2);
        LinqPredInst* rp = NewPredInst(2); // len > 2 -> {bbb, dddd}
        il2cpp::Il2CppObject* longish = reinterpret_cast<il2cpp::Il2CppObject*>(
            MakeRefInstDel(&InstIsLong, reinterpret_cast<il2cpp::Il2CppObject*>(rp)));
        Expect(il2cpp::il2cpp_linq_count_ref(strs, longish) == 2,
               "count_ref instance-bound: counts the 2 strings longer than threshold 2");
        Expect(il2cpp::il2cpp_linq_any_ref(strs, longish),
               "any_ref instance-bound: finds a string longer than threshold 2");
        Expect(!il2cpp::il2cpp_linq_all_ref(strs, longish),
               "all_ref instance-bound: not every string is longer than threshold 2");
        Expect(!il2cpp::il2cpp_linq_any_ref(shortOnly, longish),
               "any_ref instance-bound: no short-only string exceeds threshold 2");
    }

    // --- 14. GC: a predicate that runs il2cpp_gc_collect() mid-loop (a
    //    compacting collection MOVES the source array) must not corrupt
    //    count/any/all -- the icalls root the source across the callback
    //    loop. The Gc* probes collect on every invocation; every value below
    //    is verified through FRESH icall reads (the probe collects again on
    //    the way in, so any cached pointer would already be stale). ---
    {
        int32_t gcVals[32];
        for (int32_t i = 0; i < 32; ++i) gcVals[i] = i + 1;
        il2cpp::Il2CppArray* gcSrc = MakeI4Array(gcVals, 32);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&gcSrc));

        g_gcCalls = 0;
        Expect(il2cpp::il2cpp_linq_count_i4(
                   gcSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(GcPredI4))) == 16,
               "count_i4 with a collecting predicate still counts all 16 evens");
        Expect(g_gcCalls == 32, "count_i4 GC probe: predicate called once per element (no short circuit)");

        g_gcCalls = 0;
        Expect(il2cpp::il2cpp_linq_any_i4(
                   gcSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(GcPredI4))),
               "any_i4 with a collecting predicate still finds an even element");
        Expect(g_gcCalls == 2, "any_i4 GC probe: short-circuits at the first hit (pred calls on 1=odd then 2=even)");

        g_gcCalls = 0;
        Expect(!il2cpp::il2cpp_linq_all_i4(
                   gcSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeI4Del(GcPredI4))),
               "all_i4 with a collecting predicate still finds an odd element (fails)");
        Expect(g_gcCalls == 1, "all_i4 GC probe: short-circuits at the first miss (element 1)");

        // Ref family: the collecting predicate moves a string[] source.
        const char* const kGcStrs[4] = { "a", "bbb", "c", "dddd" };
        il2cpp::Il2CppArray* gcRefSrc = MakeStringArray(kGcStrs, 4);
        il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&gcRefSrc));
        g_longCalls = 0;
        Expect(il2cpp::il2cpp_linq_count_ref(
                   gcRefSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(GcPredRef))) == 2,
               "count_ref with a collecting predicate still counts the 2 long strings");
        g_longCalls = 0;
        Expect(il2cpp::il2cpp_linq_any_ref(
                   gcRefSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(GcPredRef))),
               "any_ref with a collecting predicate still finds a long string");
        g_longCalls = 0;
        Expect(!il2cpp::il2cpp_linq_all_ref(
                   gcRefSrc, reinterpret_cast<il2cpp::Il2CppObject*>(MakeRefDel(GcPredRef))),
               "all_ref with a collecting predicate still fails on a short string");

        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&gcRefSrc));
        il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&gcSrc));
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d linq assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime linq T1 tests passed\n");
    return 0;
}
