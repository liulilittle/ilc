//------------------------------------------------------------------------------
// test_linq_map.cpp -- LINQ T2 domain unit tests (where / select / first /
// firstOrDefault / toArray), per docs/design/linq-domain.md T2 card.
//
// The callbacks are wrapped into real single-cast delegates through
// il2cpp_delegate_new (the generated-code ldftn + newobj shape) -- the
// icalls receive the delegate OBJECT and call methodPtr directly.
//
// Coverage (normal + edge + error paths):
//   1. where_i4: filtering correctness on a multi-element array; the
//      all-true and all-false predicates; the empty source; the null source
//      and the null predicate (ArgumentNullException).
//   2. where callback validation: a multicast delegate raises
//      InvalidOperationException; a wrong-element-family source raises
//      NotSupportedException.
//   2b. INSTANCE-BOUND predicates (the linqcase shape: p.IsEven): a bound
//      instance method with a target is a first-class LINQ callback --
//      where/first/firstOrDefault filter using the bound instance's payload,
//      and a collecting instance predicate survives a mid-loop compaction.
//   3. where_ref: filtering a string[] by content; result layout
//      (isRefElem / elemSize).
//   4. select_i4_i4: same-type mapping.
//   5. select_i4_ref: cross-type mapping int32 -> string (via the string
//      domain inside the callback); result layout.
//   6. select_ref_ref: reference -> reference mapping (identity).
//   7. Chaining: where(pred).select(sel) materializes array -> array (no
//      enumerator); both i4 -> i4 and i4 -> ref chains.
//   8. first / firstOrDefault: hit, empty -> InvalidOperationException
//      (first) vs. default 0/null (firstOrDefault, no throw); first_ref
//      returns a leading null element without throwing.
//   9. to_array: deep clone -- equal length and values, and mutating the
//      result NEVER affects the source (int32 and reference payloads).
//  10. GC: callbacks that run il2cpp_gc_collect() in the middle of the loop
//      force a compaction while the icall holds the source/result arrays;
//      the results must stay intact (the icalls root the arrays across the
//      callback loop).
//  11. to_list: materialization into a List<int> (listi4 domain) -- element
//      count and order via the list icalls; the to_array -> to_list round
//      trip; the empty source; the null source (ArgumentNullException); a
//      reference-element source (NotSupportedException); a 64-element
//      source that forces several internal grow allocations.
//  12. take_i4: the first `count` elements -- hit, count 0 / NEGATIVE
//      (empty WITHOUT throwing -- the net48 TakeIterator only walks while
//      count > 0, probe-verified), count == / > length (full copy), empty
//      source, null source (ArgumentNullException), ref array
//      (NotSupportedException), result/source mutation isolation.
//  13. skip_i4: everything after the first `count` elements -- hit, count 0 /
//      NEGATIVE (full copy WITHOUT throwing -- net48 SkipIterator yields
//      everything when count <= 0, probe-verified), count == / > length
//      (empty), empty source, null source, ref array, take/skip chaining.
//  14. group_by_i4 (D-LINQ-4): the GroupBy grouping surface -- parity + mod-3
//      buckets over a multi-element source (FIRST-APPEARANCE key order,
//      per-bucket Count/Sum/order), the iteration protocol (result count +
//      indexed bucket access + bucket key/count/exact-size element copy),
//      degenerate sources (empty / single / duplicate keys / negatives),
//      validation (null source/key selector -> ArgumentNullException, multicast
//      -> InvalidOperationException, ref array -> NotSupportedException), the
//      receiver guards, an INSTANCE-BOUND key selector (per-instance modulus),
//      and a collecting key selector (mid-loop compaction keeps the buckets).
//  15. concat_i4 (D-LINQZ-1): concatenation -- order preserved, duplicates
//      kept across the boundary, length = sum of the parts, empty-first /
//      empty-second / both-empty, chaining, mutation isolation, nulls
//      (ArgumentNullException), ref arrays (NotSupportedException).
//  16. distinct_i4 (D-LINQZ-1): dedup keeping FIRST-APPEARANCE order -- basic,
//      all-distinct, all-same, single, empty, negatives, after a wired where
//      chain, mutation isolation, null / ref-array validation.
//  17. except_i4 (D-LINQZ-1): set difference -- basic, no-overlap, full-overlap,
//      empty-first / empty-second, duplicates in the second (set membership),
//      negatives, after a wired where chain, nulls, ref arrays.
//  18. zip_i4 (D-LINQZ-1): element-wise pairing to the SHORTER length -- equal
//      lengths, first-shorter, second-shorter, single, empty operand (no
//      selector invocation), over a wired where chain, mutation isolation,
//      nulls, ref arrays, multicast rejection, an INSTANCE-BOUND 2-arg
//      selector (bound offset), and a collecting selector (mid-loop
//      compaction keeps all four rooted objects).
//  19. take_while_i4 / skip_while_i4 (D-LT-2): the net48 prefix semantics --
//      TakeWhile keeps the longest prefix and stops at the first failing
//      element (excluded); SkipWhile yields everything after the prefix (the
//      first failing element AND the rest, the predicate NEVER called after);
//      the once-per-visited-element call-count probes (TakeWhile 5 calls,
//      SkipWhile 5 calls over 1..10), all-true / all-false / empty degenerate
//      sources, chaining after where, mutation isolation, nulls, ref arrays.
//  19b. take_while_idx_i4 / skip_while_idx_i4 (D-LT-2): the 2-arg
//      (value, source-index) predicate overloads -- index-only predicates,
//      an INSTANCE-BOUND 2-arg index predicate (bound threshold), and a
//      collecting predicate (mid-loop compaction keeps the result).
//
// Plain executable + minimal assertion harness (see test_string.cpp).
//------------------------------------------------------------------------------

#include "linq_map.h"
#include "linq.h" // nb9 P0-1: il2cpp_linq_aggregate_seed_f / to_array_family

#include "il2cpp_delegate.h"
#include "il2cpp_delegate_ext.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "genericcomparer_obj.h" // nb9 P0-2: Comparer<T>.Create wrappers
#include "listi4.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "linq-map test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
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
        return il2cpp::il2cpp_exception_matches(expectedType, e.type_name());
    }
}

//------------------------------------------------------------------------------
// Fixture builders (fresh GC allocations; the caller must not hold raw
// pointers across a collection).
//------------------------------------------------------------------------------
il2cpp::Il2CppArray* MakeI4Array(const int32_t* values, int32_t length)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(nullptr, 4, false, length);
    for (int32_t i = 0; i < length; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(arr, i)) = values[i];
    }
    return arr;
}

il2cpp::Il2CppArray* MakeRefArray(const il2cpp::Il2CppObject* const* values, int32_t length)
{
    il2cpp::Il2CppArray* arr = il2cpp::il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
    for (int32_t i = 0; i < length; ++i)
    {
        *reinterpret_cast<il2cpp::Il2CppObject**>(
            il2cpp::il2cpp_array_get_elem_ptr(arr, i)) =
            const_cast<il2cpp::Il2CppObject*>(values[i]);
    }
    return arr;
}

// The standard 1..10 fixture.
void TenValues(int32_t* out)
{
    for (int32_t i = 0; i < 10; ++i) out[i] = i + 1;
}

// A string element equals an ASCII literal (length + payload).
bool IsAsciiString(il2cpp::Il2CppObject* obj, const char* expected)
{
    if (obj == nullptr) return false;
    il2cpp::Il2CppString* s = reinterpret_cast<il2cpp::Il2CppString*>(obj);
    const size_t len = std::strlen(expected);
    if (static_cast<size_t>(s->length) != len) return false;
    for (size_t i = 0; i < len; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(expected[i])) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// Callbacks (static-method delegates, the generated-code ABI).
//------------------------------------------------------------------------------
bool IsEven(int32_t v) { return (v & 1) == 0; }
bool IsOdd(int32_t v) { return (v & 1) == 1; }
bool AlwaysTrue(int32_t) { return true; }
bool AlwaysFalse(int32_t) { return false; }
int32_t PlusTen(int32_t v) { return v + 10; }

// D-LT-2 TakeWhile/SkipWhile fixtures (net48 prefix semantics).
bool Lt5(int32_t v) { return v < 5; }
bool Lt6(int32_t v) { return v < 6; }
int32_t g_predCalls = 0;
bool CountLt5(int32_t v) { ++g_predCalls; return v < 5; }
// The 2-arg (value, source-index) predicate ABI (Func<TSource,int,bool>).
bool IdxLt3(int32_t, int32_t i) { return i < 3; }
bool GcIdxLt5(int32_t v, int32_t)
{
    il2cpp::il2cpp_gc_collect();
    return v < 5;
}

// int32 -> string via the existing string domain (select_i4_ref callback).
il2cpp::Il2CppObject* IntToStr(int32_t v)
{
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%d", v);
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(buf));
}

bool RefNonEmpty(il2cpp::Il2CppObject* o)
{
    return il2cpp::il2cpp_string_get_length(o) > 0;
}

il2cpp::Il2CppObject* RefIdentity(il2cpp::Il2CppObject* o) { return o; }

//------------------------------------------------------------------------------
// Instance-bound predicate fixtures (the linqcase shape: p.IsEven). A test
// instance is a GC object with an int payload; the instance methods below
// read it through their `this` (arg0) -- exactly what transpiler-generated
// instance methods do (CodeGenerator: instance arg0 = this).
//------------------------------------------------------------------------------
struct LinqPredInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t threshold;        // instance payload the bound method reads
};

il2cpp::Il2CppClass g_predInstClass = {
    "LinqTest.PredInst",          // name
    static_cast<int32_t>(sizeof(LinqPredInst)), // instanceSize
    0u,                           // refFieldBitmap: no reference fields
    0,                            // refFieldCount
    nullptr,                      // baseClass
    nullptr,                      // vtable
    0,                            // vtableSize
    nullptr,                      // interfaces
    0,                            // interfaceCount
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

// D-LT-2 instance-bound 1-arg and 2-arg (value, source-index) prefix
// predicates: the bound threshold drives the keep/skip decision.
bool InstLt(il2cpp::Il2CppObject* this_, int32_t v)
{
    return v < reinterpret_cast<LinqPredInst*>(this_)->threshold;
}

bool InstIdxBound(il2cpp::Il2CppObject* this_, int32_t, int32_t i)
{
    return i < reinterpret_cast<LinqPredInst*>(this_)->threshold;
}

// GC probe (1-arg): reads the bound payload first (a managed pointer must not
// be held across a collection inside the callback), then compacts mid-loop.
bool InstLtCollect(il2cpp::Il2CppObject* this_, int32_t v)
{
    const int32_t threshold = reinterpret_cast<LinqPredInst*>(this_)->threshold;
    il2cpp::il2cpp_gc_collect();
    return v < threshold;
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

// Wrap a method pointer + target into an instance-bound delegate (mirrors
// the transpiler's 2-arg emission: il2cpp_delegate_new(&M, obj)).
il2cpp::Il2CppDelegate* DelInst(void* fn, il2cpp::Il2CppObject* target)
{
    return il2cpp::il2cpp_delegate_new(fn, target);
}

// GC probes: force a compaction in the middle of the icall's element loop.
bool GcPredI4(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    return (v & 1) == 0;
}

il2cpp::Il2CppObject* GcSelI4Ref(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    char buf[16];
    std::snprintf(buf, sizeof(buf), "%d", v);
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(buf));
}

// Wrap a method pointer into a single-cast delegate (mirrors generated
// ldftn + newobj DelegateCtor).
il2cpp::Il2CppDelegate* Del(void* fn)
{
    return il2cpp::il2cpp_delegate_new(fn);
}

//------------------------------------------------------------------------------
// GroupBy (D-LINQ-4) key selectors: int32 -> int32 (the select_i4_i4 ABI).
//------------------------------------------------------------------------------
int32_t KeyParity(int32_t v) { return v & 1; }
int32_t KeyMod3(int32_t v) { return v % 3; }
int32_t KeyIdent(int32_t v) { return v; }

// GC probe: collects (compacts the young generation) on every key read.
int32_t GcKeyParity(int32_t v)
{
    il2cpp::il2cpp_gc_collect();
    return v & 1;
}

// Instance-bound key selector fixture (the p.IsBig shape): the bound
// instance carries the modulus, the method reads it through `this` (arg0).
struct LinqKeyInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t mod;              // instance payload the bound method reads
};

il2cpp::Il2CppClass g_keyInstClass = {
    "LinqTest.KeyInst",                    // name
    static_cast<int32_t>(sizeof(LinqKeyInst)), // instanceSize
    0u,                                     // refFieldBitmap: no reference fields
    0,                                      // refFieldCount
    nullptr,                                // baseClass
    nullptr,                                // vtable
    0,                                      // vtableSize
    nullptr,                                // interfaces
    0,                                      // interfaceCount
};

bool g_keyInstRegistered = false;

LinqKeyInst* NewKeyInst(int32_t mod)
{
    if (!g_keyInstRegistered)
    {
        il2cpp::il2cpp_class_register(&g_keyInstClass);
        g_keyInstRegistered = true;
    }
    LinqKeyInst* t = reinterpret_cast<LinqKeyInst*>(
        il2cpp::il2cpp_gc_alloc(&g_keyInstClass));
    t->mod = mod;
    return t;
}

// Instance method with the generated-code instance ABI: arg0 = this.
int32_t InstKeyMod(il2cpp::Il2CppObject* this_, int32_t v)
{
    return v % reinterpret_cast<LinqKeyInst*>(this_)->mod;
}

//------------------------------------------------------------------------------
// Zip (D-LINQZ-1) 2-arg selector fixtures: int32_t(*)(int32_t, int32_t) for
// STATIC leaves; int32_t(*)(Il2CppObject*, int32_t, int32_t) for INSTANCE
// leaves (the bound instance's payload read through `this` -- arg0).
//------------------------------------------------------------------------------
int32_t ZipAdd(int32_t a, int32_t b) { return a + b; }
int32_t ZipMul(int32_t a, int32_t b) { return a * b; }
int32_t ZipDiff(int32_t a, int32_t b) { return a - b; }

// GC probe: forces a compaction on every selector invocation.
int32_t GcZipAdd(int32_t a, int32_t b)
{
    il2cpp::il2cpp_gc_collect();
    return a + b;
}

// Instance-bound 2-arg selector: adds the bound instance's offset.
struct LinqZipInst
{
    il2cpp::Il2CppObject obj; // object header
    int32_t offset;           // instance payload the bound method reads
};

il2cpp::Il2CppClass g_zipInstClass = {
    "LinqTest.ZipInst",                       // name
    static_cast<int32_t>(sizeof(LinqZipInst)), // instanceSize
    0u,                                       // refFieldBitmap: no reference fields
    0,                                        // refFieldCount
    nullptr,                                  // baseClass
    nullptr,                                  // vtable
    0,                                        // vtableSize
    nullptr,                                  // interfaces
    0,                                        // interfaceCount
};

bool g_zipInstRegistered = false;

LinqZipInst* NewZipInst(int32_t offset)
{
    if (!g_zipInstRegistered)
    {
        il2cpp::il2cpp_class_register(&g_zipInstClass);
        g_zipInstRegistered = true;
    }
    LinqZipInst* t = reinterpret_cast<LinqZipInst*>(
        il2cpp::il2cpp_gc_alloc(&g_zipInstClass));
    t->offset = offset;
    return t;
}

int32_t InstZipAddOffset(il2cpp::Il2CppObject* this_, int32_t a, int32_t b)
{
    return a + b + reinterpret_cast<LinqZipInst*>(this_)->offset;
}

//------------------------------------------------------------------------------
// nb9 fix (H1/H2) GroupBy element/result-selector fixtures for
// il2cpp_linq_group_by_full (the single entry every GroupBy overload uses;
// it drives BuildGroups + ApplyResultSelector internally).
//------------------------------------------------------------------------------
int32_t KeyZero(int32_t) { return 0; }

il2cpp::Il2CppObject* MakeCsString(const char* text)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(text));
}

// Element selector (Func<int,string>): "e<v>".
il2cpp::Il2CppObject* ElemStr(int32_t v)
{
    char buf[16];
    std::snprintf(buf, sizeof(buf), "e%d", v);
    return MakeCsString(buf);
}

// Element selector (Func<int,long>): a wide value (exercises the box path).
int64_t ElemI8(int32_t v) { return static_cast<int64_t>(v) * 1000000000LL; }

// H2 regression probe: same budget trick as GarbageElemStr, but for the boxed
// int64 family. The value itself is a scalar, so no root is needed; what is at
// stake is BuildGroups' cached 'grp' pointer, which the box allocation (a GC
// point) can invalidate before elements/count are touched.
int64_t GarbageElemI8(int32_t v)
{
    // Establish a low bumped-byte counter ONCE (the first call only). Forcing
    // a collection on every call would promote the freshly created bucket out
    // of Gen0 before the box runs, hiding the very hazard this probe targets.
    static bool primed = false;
    if (!primed)
    {
        primed = true;
        const int64_t before = il2cpp::il2cpp_gc_get_collection_count(0);
        while (il2cpp::il2cpp_gc_get_collection_count(0) == before)
        {
            (void)il2cpp::il2cpp_array_new(nullptr, 65536, false, 1);
        }
    }
    // Leave the bumped-byte counter above the budget so the NEXT allocation
    // (the box inside BuildGroups) runs a compacting collection while the
    // bucket is still a Gen0 object.
    (void)il2cpp::il2cpp_array_new(nullptr, 300 * 1024, false, 1);
    return ElemI8(v);
}

// H1 regression probe. BuildGroups runs, per iteration:
//     keySel -> elemSel -> (bucket growth if full) -> store
// so the projected reference element is produced BEFORE the growth allocation.
// This selector leaves the GC's bumped-byte counter ABOVE its collection budget
// (kCollectAfterBumpedBytes) without tripping a collection itself, so the very
// next allocation -- the bucket growth inside BuildGroups -- runs a full
// compacting collection. At that moment the projected string is reachable only
// through BuildGroups' element slot: unrooted (the pre-fix state) it is swept
// away, and the bucket stores a dangling pointer.
il2cpp::Il2CppObject* GarbageElemStr(int32_t v)
{
    // Step 1: drain the counter to a known state by forcing one collection.
    // (An allocation that starts above the budget collects BEFORE bumping, so
    // repeatedly allocating large blocks guarantees an observed collection.)
    const int64_t before = il2cpp::il2cpp_gc_get_collection_count(0);
    while (il2cpp::il2cpp_gc_get_collection_count(0) == before)
    {
        (void)il2cpp::il2cpp_array_new(nullptr, 65536, false, 1);
    }

    // Step 2: create the element this probe is about, and keep it alive only
    // through a FIXTURE-LOCAL root (removed before returning) -- the CALLER is
    // what must root it after the return.
    il2cpp::Il2CppObject* s = ElemStr(v);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&s));

    // Step 3: bump the counter past the budget WITHOUT collecting (this
    // allocation starts at ~0, so the pre-check does not fire). The next
    // allocation -- BuildGroups' bucket growth -- is then guaranteed to run a
    // compacting collection.
    (void)il2cpp::il2cpp_array_new(nullptr, 300 * 1024, false, 1);

    il2cpp::il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&s));
    return s;
}

// Result selector (Func<int,IEnumerable<int>,int>): key * 100 + count.
// NOTE: the second argument is the bucket's int32[] snapshot (the stage-1
// IEnumerable<TElement> carrier), so its length is the bucket count.
int32_t ResKeyCountI4(int32_t key, il2cpp::Il2CppObject* group)
{
    const int32_t n = reinterpret_cast<il2cpp::Il2CppArray*>(group)->length;
    return key * 100 + n;
}

// Result selector (Func<int,IEnumerable<int>,string>): "k<c>".
il2cpp::Il2CppObject* ResKeyCountStr(int32_t key, il2cpp::Il2CppObject* group)
{
    const int32_t n = reinterpret_cast<il2cpp::Il2CppArray*>(group)->length;
    char buf[32];
    std::snprintf(buf, sizeof(buf), "k%d=%d", key, n);
    return MakeCsString(buf);
}

// A boxed Int64 payload (the runtime box layout: klass at 0, value at 8).
int64_t BoxedI8Value(il2cpp::Il2CppObject* boxed)
{
    return *reinterpret_cast<int64_t*>(reinterpret_cast<char*>(boxed) + 8);
}

// nb9 P0-1/P0-2 fixtures: the float accumulator ABI is float(double,int32_t)
// (the F stack carries the accumulator as double), and the Comparison<T>
// delegates use the matching element ABI.
float FloatAdd(double a, int32_t b) { return static_cast<float>(a) + static_cast<float>(b); }
int32_t CmpDescI4(int32_t a, int32_t b) { return b < a ? -1 : b > a ? 1 : 0; }
int32_t CmpAscI8(int64_t a, int64_t b) { return a < b ? -1 : a > b ? 1 : 0; }
int32_t CmpDescD(double a, double b) { return b < a ? -1 : b > a ? 1 : 0; }

} // namespace

int main()
{
    using il2cpp::Il2CppArray;
    using il2cpp::Il2CppObject;

    // --- 1. where_i4: filtering, degenerate predicates, empty source ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);
        Il2CppArray* evens = il2cpp::il2cpp_linq_where_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven))));
        Expect(evens != nullptr, "where_i4 returns a non-null result");
        Expect(evens->length == 5, "where_i4 keeps the 5 even elements of 1..10");
        bool evensOk = true;
        for (int32_t i = 0; i < evens->length; ++i)
        {
            const int32_t v = *reinterpret_cast<int32_t*>(
                il2cpp::il2cpp_array_get_elem_ptr(evens, i));
            if (v != 2 * (i + 1)) evensOk = false;
        }
        Expect(evensOk, "where_i4 result values are the evens in source order");
        Expect(!evens->isRefElem && evens->elemSize == 4,
               "where_i4 result is an int32-layout array");
    }
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);
        Il2CppArray* all = il2cpp::il2cpp_linq_where_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(all->length == 10, "where with an all-true predicate keeps every element");
        bool same = all->length == src->length;
        for (int32_t i = 0; i < all->length; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(all, i)) != vals[i])
                same = false;
        }
        Expect(same, "where with an all-true predicate copies values in order");
        Il2CppArray* none = il2cpp::il2cpp_linq_where_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysFalse))));
        Expect(none->length == 0, "where with an all-false predicate yields an empty result");
    }
    {
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* out = il2cpp::il2cpp_linq_where_i4(
            empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven))));
        Expect(out->length == 0, "where on an empty source yields an empty result");
    }

    // --- 2. where_i4 validation: nulls, captured lambda, multicast, family ---
    {
        int32_t one[1] = { 1 };
        Il2CppArray* src = MakeI4Array(one, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_where_i4(
                    src, nullptr); },
                            "System.ArgumentNullException"),
               "where_i4 with a null predicate raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_where_i4(
                    nullptr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.ArgumentNullException"),
               "where_i4 with a null source raises ArgumentNullException");

        // Multicast container.
        il2cpp::Il2CppObject* mc = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_where_i4(
                    src, mc); },
                            "System.InvalidOperationException"),
               "where_i4 with a multicast delegate is loudly rejected");

        // Wrong family: where_i4 on a reference array.
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_where_i4(
                    refArr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.NotSupportedException"),
               "where_i4 on a reference array raises NotSupportedException");
    }

    // --- 2b. INSTANCE-BOUND predicates (the linqcase shape: p.IsEven) ---
    // An instance-method predicate bound to a delegate with a target is a
    // first-class LINQ callback: the runtime dispatches it through the
    // generated instance ABI (target in the `this` slot). where/first/
    // firstOrDefault must filter using the bound instance's payload, and a
    // collecting instance predicate must survive a mid-loop compaction (the
    // delegate is rooted; d->target is re-read through the rooted slot).
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);

        // Instance "p" with threshold 5: IsBig keeps {6,7,8,9,10}.
        LinqPredInst* p = NewPredInst(5);
        Il2CppObject* big = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstIsBig), reinterpret_cast<Il2CppObject*>(p)));

        Il2CppArray* out = il2cpp::il2cpp_linq_where_i4(src, big);
        Expect(out != nullptr && out->length == 5,
               "instance-bound where keeps the 5 elements above the bound threshold");
        bool ok = out != nullptr && out->length == 5;
        for (int32_t i = 0; i < 5 && ok; ++i)
        {
            const int32_t v = *reinterpret_cast<int32_t*>(
                il2cpp::il2cpp_array_get_elem_ptr(out, i));
            if (v != i + 6) ok = false; // {6,7,8,9,10}
        }
        Expect(ok, "instance-bound where result values use the bound instance payload");

        // The SAME method bound to a DIFFERENT threshold reads that instance.
        LinqPredInst* q = NewPredInst(8);
        Il2CppObject* big8 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstIsBig), reinterpret_cast<Il2CppObject*>(q)));
        Il2CppArray* out8 = il2cpp::il2cpp_linq_where_i4(src, big8);
        Expect(out8 != nullptr && out8->length == 2 && out8->length > 0 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(out8, 0)) == 9,
               "a different bound instance filters with its own payload ({9,10})");

        // Instance-bound first_predicate: first above-threshold element.
        Expect(il2cpp::il2cpp_linq_first_predicate(
                   src, big) == 6,
               "instance-bound first_predicate returns the first above-threshold element");

        int32_t small[3] = { 1, 2, 3 };
        Il2CppArray* smallSrc = MakeI4Array(small, 3);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    smallSrc, big); },
                            "System.InvalidOperationException"),
               "instance-bound first_predicate on a no-match source raises InvalidOperationException");

        // Instance-bound first_or_default_predicate: hit -> 6; miss -> 0.
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   src, big) == 6,
               "instance-bound first_or_default_predicate returns the first match (6)");
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   smallSrc, big) == 0,
               "instance-bound first_or_default_predicate on a no-match source returns 0");

        // Null predicate still raises ArgumentNullException (validation is
        // independent of the binding form).
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_where_i4(src, nullptr); },
                            "System.ArgumentNullException"),
               "instance-bound path keeps the null-predicate ArgumentNullException");

        // GC: an instance-bound predicate that collects mid-loop. The bound
        // payload is read into a by-value copy BEFORE the collect (a managed
        // pointer must never be held across a collection inside the
        // callback), then a compaction forces the arrays to move. The result
        // must stay intact.
        int32_t many[16];
        for (int32_t i = 0; i < 16; ++i) many[i] = i + 1;
        Il2CppArray* gcSrc = MakeI4Array(many, 16);
        LinqPredInst* gp = NewPredInst(4);
        Il2CppObject* gcBig = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstIsBigCollect), reinterpret_cast<Il2CppObject*>(gp)));
        Il2CppArray* gcOut = il2cpp::il2cpp_linq_where_i4(gcSrc, gcBig);
        Expect(gcOut != nullptr && gcOut->length == 12,
               "instance-bound where with a collecting predicate keeps all 12 above-threshold elements");
        bool gcOk = gcOut != nullptr && gcOut->length == 12;
        for (int32_t i = 0; i < 12 && gcOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(gcOut, i)) != i + 5)
                gcOk = false;
        }
        Expect(gcOk, "instance-bound where with a collecting predicate yields correct values");
    }

    // --- 3. where_ref: string[] filtering + result layout ---
    {
        il2cpp::Il2CppObject* sEmpty = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs(""));
        il2cpp::Il2CppObject* sA = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("a"));
        il2cpp::Il2CppObject* sBC = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("bc"));
        const Il2CppObject* words[4] = { sEmpty, sA, sEmpty, sBC };
        Il2CppArray* src = MakeRefArray(words, 4);
        Il2CppArray* out = il2cpp::il2cpp_linq_where_ref(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&RefNonEmpty))));
        Expect(out != nullptr && out->length == 2, "where_ref keeps the 2 non-empty strings");
        Expect(out->isRefElem && out->elemSize == static_cast<int32_t>(sizeof(void*)),
               "where_ref result is a reference-element array");
        bool orderOk = out->length == 2 &&
                       IsAsciiString(*reinterpret_cast<Il2CppObject**>(
                           il2cpp::il2cpp_array_get_elem_ptr(out, 0)), "a") &&
                       IsAsciiString(*reinterpret_cast<Il2CppObject**>(
                           il2cpp::il2cpp_array_get_elem_ptr(out, 1)), "bc");
        Expect(orderOk, "where_ref result keeps source order and element identity");
    }

    // --- 4. select_i4_i4: same-type mapping ---
    {
        int32_t vals[5] = { 1, 2, 3, 4, 5 };
        Il2CppArray* src = MakeI4Array(vals, 5);
        Il2CppArray* out = il2cpp::il2cpp_linq_select_i4_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&PlusTen))));
        Expect(out != nullptr && out->length == 5, "select_i4_i4 result length equals the source");
        bool ok = true;
        for (int32_t i = 0; i < 5; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(out, i)) != vals[i] + 10)
                ok = false;
        }
        Expect(ok, "select_i4_i4 maps every element (v -> v + 10)");
        Expect(!out->isRefElem && out->elemSize == 4, "select_i4_i4 result is an int32 array");
    }

    // --- 5. select_i4_ref: cross-type int32 -> string mapping ---
    {
        int32_t vals[3] = { 7, 8, 9 };
        Il2CppArray* src = MakeI4Array(vals, 3);
        Il2CppArray* out = il2cpp::il2cpp_linq_select_i4_ref(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IntToStr))));
        Expect(out != nullptr && out->length == 3, "select_i4_ref result length equals the source");
        Expect(out->isRefElem && out->elemSize == static_cast<int32_t>(sizeof(void*)),
               "select_i4_ref result is a reference-element array");
        bool ok = true;
        for (int32_t i = 0; i < 3; ++i)
        {
            Il2CppObject* e = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(out, i));
            char expected[8];
            std::snprintf(expected, sizeof(expected), "%d", vals[i]);
            if (!IsAsciiString(e, expected)) ok = false;
        }
        Expect(ok, "select_i4_ref maps int32 -> string via the string domain (7,8,9)");
    }

    // --- 6. select_ref_ref: reference -> reference mapping ---
    {
        il2cpp::Il2CppObject* s1 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("one"));
        il2cpp::Il2CppObject* s2 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("two"));
        const Il2CppObject* words[2] = { s1, s2 };
        Il2CppArray* src = MakeRefArray(words, 2);
        Il2CppArray* out = il2cpp::il2cpp_linq_select_ref_ref(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&RefIdentity))));
        Expect(out != nullptr && out->length == 2, "select_ref_ref result length equals the source");
        bool same = out->length == 2 &&
                    *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(out, 0)) == s1 &&
                    *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(out, 1)) == s2;
        Expect(same, "select_ref_ref identity mapping preserves element identity");
    }

    // --- 7. Chaining: where(pred).select(sel) materializes array -> array ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);
        Il2CppArray* mid = il2cpp::il2cpp_linq_where_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven))));
        Il2CppArray* out = il2cpp::il2cpp_linq_select_i4_i4(
            mid, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&PlusTen))));
        Expect(out != nullptr && out->length == 5, "chain where->select yields the 5 mapped evens");
        bool ok = true;
        for (int32_t i = 0; i < 5; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(out, i)) != 12 + 2 * i)
                ok = false;
        }
        Expect(ok, "chain where(even).select(+10) values are {12,14,16,18,20}");

        // Cross-type chain: where(even).select(int->string).
        Il2CppArray* out2 = il2cpp::il2cpp_linq_select_i4_ref(
            mid, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IntToStr))));
        Expect(out2 != nullptr && out2->length == 5 && out2->isRefElem,
               "chain where->select(int->string) yields a reference-element array");
        bool ok2 = out2->length == 5;
        for (int32_t i = 0; i < 5 && ok2; ++i)
        {
            char expected[8];
            std::snprintf(expected, sizeof(expected), "%d", 2 * (i + 1));
            Il2CppObject* e = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(out2, i));
            if (!IsAsciiString(e, expected)) ok2 = false;
        }
        Expect(ok2, "chain where(even).select(int->string) values are {\"2\",\"4\",\"6\",\"8\",\"10\"}");
    }

    // --- 8. first / firstOrDefault ---
    {
        int32_t vals[3] = { 42, 7, 9 };
        Il2CppArray* src = MakeI4Array(vals, 3);
        Expect(il2cpp::il2cpp_linq_first_i4(src) == 42, "first_i4 returns the first element");
        Expect(il2cpp::il2cpp_linq_first_or_default_i4(src) == 42,
               "first_or_default_i4 on a non-empty source returns the first element");

        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_i4(empty); },
                            "System.InvalidOperationException"),
               "first_i4 on an empty source raises InvalidOperationException");
        Expect(il2cpp::il2cpp_linq_first_or_default_i4(empty) == 0,
               "first_or_default_i4 on an empty source returns 0 without throwing");

        // Ref family: hit, empty -> IOE, default null, leading null element.
        il2cpp::Il2CppObject* s1 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("first"));
        const Il2CppObject* words[2] = { s1, nullptr };
        Il2CppArray* refSrc = MakeRefArray(words, 2);
        Expect(il2cpp::il2cpp_linq_first_ref(refSrc) == s1,
               "first_ref returns the first element");
        const Il2CppObject* nullFirst[2] = { nullptr, s1 };
        Il2CppArray* refNullFirst = MakeRefArray(nullFirst, 2);
        Expect(il2cpp::il2cpp_linq_first_ref(refNullFirst) == nullptr,
               "first_ref returns a leading null element without throwing");
        const Il2CppObject* none[1] = { nullptr };
        Il2CppArray* refEmpty = MakeRefArray(none, 0);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_ref(refEmpty); },
                            "System.InvalidOperationException"),
               "first_ref on an empty source raises InvalidOperationException");
        Expect(il2cpp::il2cpp_linq_first_or_default_ref(refEmpty) == nullptr,
               "first_or_default_ref on an empty source returns nullptr without throwing");
    }

    // --- 8b. first_predicate: first match, miss, empty, nulls, GC ---
    {
        int32_t vals[5] = { 1, 3, 5, 4, 7 };
        Il2CppArray* src = MakeI4Array(vals, 5);

        // Hit: the FIRST element satisfying the predicate.
        Expect(il2cpp::il2cpp_linq_first_predicate(
                   src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 4,
               "first_predicate returns the first even element (4)");

        int32_t first2[2] = { 2, 6 };
        Il2CppArray* src2 = MakeI4Array(first2, 2);
        Expect(il2cpp::il2cpp_linq_first_predicate(
                   src2, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 2,
               "first_predicate returns the element when the first element matches");

        // Miss: no element satisfies the predicate -> InvalidOperationException.
        int32_t allOdd[3] = { 1, 3, 5 };
        Il2CppArray* missSrc = MakeI4Array(allOdd, 3);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    missSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.InvalidOperationException"),
               "first_predicate on a no-match source raises InvalidOperationException");

        // Empty source -> InvalidOperationException.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.InvalidOperationException"),
               "first_predicate on an empty source raises InvalidOperationException");

        // Null source / null predicate -> ArgumentNullException.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    nullptr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.ArgumentNullException"),
               "first_predicate with a null source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    src, nullptr); },
                            "System.ArgumentNullException"),
               "first_predicate with a null predicate raises ArgumentNullException");

        // Wrong family: first_predicate on a reference array -> NotSupported.
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    refArr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.NotSupportedException"),
               "first_predicate on a reference array raises NotSupportedException");

        // GC: a collecting predicate that never matches -> the ioexception
        // path still removes the source root (RootScope RAII). GcPredI4
        // collects on every call and only matches evens; an all-odd source
        // forces a full scan + collection, then the miss throw.
        int32_t many[32];
        for (int32_t i = 0; i < 32; ++i) many[i] = 2 * i + 1; // all odd: 1..63
        Il2CppArray* gcSrc = MakeI4Array(many, 32);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_predicate(
                    gcSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcPredI4)))); },
                            "System.InvalidOperationException"),
               "first_predicate with a collecting no-match predicate raises InvalidOperationException");
    }

    // --- 8c. first_or_default_predicate: first match, miss -> 0, empty -> 0,
    //         nulls, GC (net48: miss/empty return the default, never throw) ---
    {
        int32_t vals[5] = { 1, 3, 5, 4, 7 };
        Il2CppArray* src = MakeI4Array(vals, 5);

        // Hit: the FIRST element satisfying the predicate.
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 4,
               "first_or_default_predicate returns the first even element (4)");

        int32_t first2[2] = { 2, 6 };
        Il2CppArray* src2 = MakeI4Array(first2, 2);
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   src2, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 2,
               "first_or_default_predicate returns the element when the first element matches");

        // Miss: no element satisfies the predicate -> 0 (no throw).
        int32_t allOdd[3] = { 1, 3, 5 };
        Il2CppArray* missSrc = MakeI4Array(allOdd, 3);
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   missSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 0,
               "first_or_default_predicate on a no-match source returns 0 without throwing");

        // Empty source -> 0 (no throw).
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))) == 0,
               "first_or_default_predicate on an empty source returns 0 without throwing");

        // Null source / null predicate -> ArgumentNullException.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_or_default_predicate(
                    nullptr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.ArgumentNullException"),
               "first_or_default_predicate with a null source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_or_default_predicate(
                    src, nullptr); },
                            "System.ArgumentNullException"),
               "first_or_default_predicate with a null predicate raises ArgumentNullException");

        // Wrong family: first_or_default_predicate on a reference array -> NotSupported.
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_first_or_default_predicate(
                    refArr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))); },
                            "System.NotSupportedException"),
               "first_or_default_predicate on a reference array raises NotSupportedException");

        // GC: a collecting predicate that never matches -> full scan with a
        // collection per call, then the 0 default path (RootScope RAII removes
        // the source root even though no exception is thrown).
        int32_t many[32];
        for (int32_t i = 0; i < 32; ++i) many[i] = 2 * i + 1; // all odd: 1..63
        Il2CppArray* gcSrc = MakeI4Array(many, 32);
        Expect(il2cpp::il2cpp_linq_first_or_default_predicate(
                   gcSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcPredI4)))) == 0,
               "first_or_default_predicate with a collecting no-match predicate returns 0");
    }

    // --- 9. to_array: deep clone, source never affected ---
    {
        int32_t vals[4] = { 1, 2, 3, 4 };
        Il2CppArray* src = MakeI4Array(vals, 4);
        Il2CppArray* out = il2cpp::il2cpp_linq_to_array(src);
        Expect(out != nullptr && out != src, "to_array returns a distinct array");
        Expect(out->length == src->length && out->elemSize == src->elemSize &&
               out->isRefElem == src->isRefElem,
               "to_array clone matches the source layout");
        bool same = true;
        for (int32_t i = 0; i < 4; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(out, i)) != vals[i])
                same = false;
        }
        Expect(same, "to_array clone holds identical values");
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(out, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(src, 0)) == 1,
               "mutating the to_array result does not affect the source (deep copy)");

        // Reference payload deep copy: overwriting a result slot leaves the
        // source slot untouched.
        il2cpp::Il2CppObject* s1 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("a"));
        il2cpp::Il2CppObject* s2 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("b"));
        const Il2CppObject* words[2] = { s1, s2 };
        Il2CppArray* refSrc = MakeRefArray(words, 2);
        Il2CppArray* refOut = il2cpp::il2cpp_linq_to_array(refSrc);
        Expect(refOut->isRefElem && refOut->elemSize == static_cast<int32_t>(sizeof(void*)),
               "to_array on a reference array keeps the reference layout");
        Expect(*reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(refOut, 0)) == s1 &&
               *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(refOut, 1)) == s2,
               "to_array reference clone holds identical element references");
        *reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(refOut, 0)) = nullptr;
        Expect(*reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(refSrc, 0)) == s1,
               "mutating a reference result slot does not affect the source");

        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* emptyOut = il2cpp::il2cpp_linq_to_array(empty);
        Expect(emptyOut != nullptr && emptyOut->length == 0,
               "to_array on an empty source yields an empty clone");
    }

    // --- 10. GC: collections inside the callback loop must not corrupt ---
    // The Gc* callbacks run il2cpp_gc_collect() (which COMPACTS the young
    // generation) mid-loop while the icall holds the source and result
    // arrays; the icalls root them across the loop, so every result value
    // must survive. NOTE: after these calls, the test's own 'src'/'del'
    // locals may be stale (test frames are not GC roots) -- they are never
    // touched again; only the returned result is verified (reads only).
    {
        int32_t vals[32];
        for (int32_t i = 0; i < 32; ++i) vals[i] = i + 1;
        Il2CppArray* src = MakeI4Array(vals, 32);

        Il2CppArray* evens = il2cpp::il2cpp_linq_where_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcPredI4))));
        Expect(evens != nullptr && evens->length == 16,
               "where_i4 with a collecting predicate keeps all 16 evens");
        bool evensOk = evens->length == 16;
        for (int32_t i = 0; i < 16 && evensOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(evens, i)) != 2 * (i + 1))
                evensOk = false;
        }
        Expect(evensOk, "where_i4 with a collecting predicate yields correct values");

        // select_i4_ref with a collecting selector (allocates a string per
        // element AND collects first): every mapped string must survive.
        Il2CppArray* strs = il2cpp::il2cpp_linq_select_i4_ref(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcSelI4Ref))));
        Expect(strs != nullptr && strs->length == 32,
               "select_i4_ref with a collecting selector keeps all 32 results");
        bool strsOk = strs->length == 32;
        for (int32_t i = 0; i < 32 && strsOk; ++i)
        {
            char expected[8];
            std::snprintf(expected, sizeof(expected), "%d", i + 1);
            Il2CppObject* e = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(strs, i));
            if (!IsAsciiString(e, expected)) strsOk = false;
        }
        Expect(strsOk, "select_i4_ref with a collecting selector yields correct strings");
    }

    // --- 11. to_list: List<int> materialization + to_array round trip ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);
        Il2CppObject* list = il2cpp::il2cpp_linq_to_list(src);
        Expect(list != nullptr, "to_list returns a non-null List<int> object");
        Expect(il2cpp::il2cpp_list_i4_get_count(list) == 10,
               "to_list list count equals the source length");
        bool ok = true;
        for (int32_t i = 0; i < 10; ++i)
        {
            if (il2cpp::il2cpp_list_i4_get_item(list, i) != vals[i]) ok = false;
        }
        Expect(ok, "to_list elements are the source values in order");

        // Round trip: to_array (clone) then to_list (materialize).
        Il2CppArray* clone = il2cpp::il2cpp_linq_to_array(src);
        Il2CppObject* roundTrip = il2cpp::il2cpp_linq_to_list(clone);
        Expect(il2cpp::il2cpp_list_i4_get_count(roundTrip) == 10,
               "to_array -> to_list round trip keeps the element count");
        bool ok2 = true;
        for (int32_t i = 0; i < 10; ++i)
        {
            if (il2cpp::il2cpp_list_i4_get_item(roundTrip, i) != vals[i]) ok2 = false;
        }
        Expect(ok2, "to_array -> to_list round trip preserves values");
        // Mutating the clone must not leak into the round-trip list.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(clone, 0)) = 999;
        Expect(il2cpp::il2cpp_list_i4_get_item(roundTrip, 0) == vals[0],
               "mutating the to_array clone does not affect the to_list result");
    }

    // --- 11b. to_list: empty source, error paths, grow allocations ---
    {
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppObject* list = il2cpp::il2cpp_linq_to_list(empty);
        Expect(list != nullptr && il2cpp::il2cpp_list_i4_get_count(list) == 0,
               "to_list on an empty source yields an empty list");

        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_to_list(nullptr); },
                            "System.ArgumentNullException"),
               "to_list with a null source raises ArgumentNullException");

        il2cpp::Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        const Il2CppObject* words[1] = { s };
        Il2CppArray* refArr = MakeRefArray(words, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_to_list(refArr); },
                            "System.NotSupportedException"),
               "to_list on a reference-element array raises NotSupportedException");

        // 64 elements force several internal grow allocations (4 -> 8 -> 16
        // -> 32 -> 64); every value must survive them.
        int32_t vals[64];
        for (int32_t i = 0; i < 64; ++i) vals[i] = 1000 + i;
        Il2CppArray* big = MakeI4Array(vals, 64);
        Il2CppObject* bigList = il2cpp::il2cpp_linq_to_list(big);
        Expect(il2cpp::il2cpp_list_i4_get_count(bigList) == 64,
               "to_list over a 64-element source grows the internal storage correctly");
        bool ok = true;
        for (int32_t i = 0; i < 64 && ok; ++i)
        {
            if (il2cpp::il2cpp_list_i4_get_item(bigList, i) != 1000 + i) ok = false;
        }
        Expect(ok, "to_list elements survive the internal grow allocations");
    }

    // --- 12. take_i4: first `count` elements (net48 slice semantics) ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);

        // hit: first 3 elements {1,2,3}.
        Il2CppArray* t3 = il2cpp::il2cpp_linq_take_i4(src, 3);
        Expect(t3 != nullptr && t3->length == 3, "take_i4(3) keeps the first 3 elements");
        bool ok = t3 != nullptr && t3->length == 3;
        for (int32_t i = 0; i < 3 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(t3, i)) != i + 1) ok = false;
        }
        Expect(ok, "take_i4(3) result values are {1,2,3} in source order");

        // count == 0 -> empty; NEGATIVE count -> empty WITHOUT throwing
        // (net48 TakeIterator only walks while `count > 0` -- probe-verified).
        Il2CppArray* t0 = il2cpp::il2cpp_linq_take_i4(src, 0);
        Expect(t0 != nullptr && t0->length == 0, "take_i4(0) yields an empty result");
        Il2CppArray* tneg = il2cpp::il2cpp_linq_take_i4(src, -1);
        Expect(tneg != nullptr && tneg->length == 0,
               "take_i4(-1) yields an empty result without throwing (net48)");

        // count == length -> full copy; count > length -> full copy.
        Il2CppArray* t10 = il2cpp::il2cpp_linq_take_i4(src, 10);
        Expect(t10 != nullptr && t10->length == 10 && t10 != src,
               "take_i4(10) yields a distinct full copy");
        bool fullOk = t10 != nullptr && t10->length == 10;
        for (int32_t i = 0; i < 10 && fullOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(t10, i)) != vals[i]) fullOk = false;
        }
        Expect(fullOk, "take_i4(10) full copy holds the source values");
        Il2CppArray* t99 = il2cpp::il2cpp_linq_take_i4(src, 99);
        Expect(t99 != nullptr && t99->length == 10,
               "take_i4(99) clamps to the source length");

        // empty source -> empty.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* tempty = il2cpp::il2cpp_linq_take_i4(empty, 3);
        Expect(tempty != nullptr && tempty->length == 0,
               "take_i4 on an empty source yields an empty result");

        // null source -> ArgumentNullException; ref array -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_take_i4(nullptr, 1); },
                            "System.ArgumentNullException"),
               "take_i4 with a null source raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_take_i4(refArr, 1); },
                            "System.NotSupportedException"),
               "take_i4 on a reference array raises NotSupportedException");

        // result is a NEW array: mutating it never affects the source.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(t3, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(src, 0)) == 1,
               "mutating the take_i4 result does not affect the source");
    }

    // --- 13. skip_i4: everything after the first `count` elements ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10);

        // hit: skip 3 -> {4,5,6,7,8,9,10}.
        Il2CppArray* s3 = il2cpp::il2cpp_linq_skip_i4(src, 3);
        Expect(s3 != nullptr && s3->length == 7, "skip_i4(3) keeps the 7 trailing elements");
        bool ok = s3 != nullptr && s3->length == 7;
        for (int32_t i = 0; i < 7 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(s3, i)) != i + 4) ok = false;
        }
        Expect(ok, "skip_i4(3) result values are {4,5,6,7,8,9,10}");

        // count == 0 -> full copy; NEGATIVE count -> full copy WITHOUT
        // throwing (net48 SkipIterator yields everything when count <= 0 --
        // probe-verified).
        Il2CppArray* s0 = il2cpp::il2cpp_linq_skip_i4(src, 0);
        Expect(s0 != nullptr && s0->length == 10 && s0 != src,
               "skip_i4(0) yields a distinct full copy");
        bool fullOk = s0 != nullptr && s0->length == 10;
        for (int32_t i = 0; i < 10 && fullOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(s0, i)) != vals[i]) fullOk = false;
        }
        Expect(fullOk, "skip_i4(0) full copy holds the source values");
        Il2CppArray* sneg = il2cpp::il2cpp_linq_skip_i4(src, -1);
        Expect(sneg != nullptr && sneg->length == 10,
               "skip_i4(-1) yields a full copy without throwing (net48)");

        // count == length / count > length -> empty.
        Il2CppArray* s9 = il2cpp::il2cpp_linq_skip_i4(src, 9);
        Expect(s9 != nullptr && s9->length == 1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(s9, 0)) == 10,
               "skip_i4(9) keeps only the last element");
        Il2CppArray* s10 = il2cpp::il2cpp_linq_skip_i4(src, 10);
        Expect(s10 != nullptr && s10->length == 0, "skip_i4(10) yields an empty result");
        Il2CppArray* s99 = il2cpp::il2cpp_linq_skip_i4(src, 99);
        Expect(s99 != nullptr && s99->length == 0, "skip_i4(99) clamps to an empty result");

        // empty source -> empty.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* sempty = il2cpp::il2cpp_linq_skip_i4(empty, 3);
        Expect(sempty != nullptr && sempty->length == 0,
               "skip_i4 on an empty source yields an empty result");

        // null source -> ArgumentNullException; ref array -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_skip_i4(nullptr, 1); },
                            "System.ArgumentNullException"),
               "skip_i4 with a null source raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_skip_i4(refArr, 1); },
                            "System.NotSupportedException"),
               "skip_i4 on a reference array raises NotSupportedException");

        // chaining: take then skip / skip then take compose element-wise.
        Il2CppArray* ts = il2cpp::il2cpp_linq_skip_i4(il2cpp::il2cpp_linq_take_i4(src, 6), 2);
        Expect(ts != nullptr && ts->length == 4 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ts, 0)) == 3,
               "take(6).skip(2) yields {3,4,5,6}");
        Il2CppArray* st = il2cpp::il2cpp_linq_take_i4(il2cpp::il2cpp_linq_skip_i4(src, 2), 3);
        Expect(st != nullptr && st->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(st, 0)) == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(st, 2)) == 5,
               "skip(2).take(3) yields {3,4,5}");
    }

    // --- 14. group_by_i4 (D-LINQ-4): parity + mod-3 buckets, Count/Sum/order
    //         (the net48 probe-verified semantics: FIRST-APPEARANCE key order,
    //         source order within a bucket, each element's key computed once) ---
    {
        // nums = {5,3,9,1,7,2,8,4,6}
        int32_t vals[9] = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
        Il2CppArray* src = MakeI4Array(vals, 9);

        // Parity: the FIRST element (5) is odd -> key 1 first; bucket order
        // {key 1} then {key 0}.
        Il2CppObject* res = il2cpp::il2cpp_linq_group_by_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Expect(res != nullptr, "group_by_i4 returns a non-null grouping result");
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(res) == 2,
               "group_by_i4 parity yields 2 buckets");

        Il2CppObject* odd = il2cpp::il2cpp_linq_grouping_result_get_grouping(res, 0);
        Il2CppObject* even = il2cpp::il2cpp_linq_grouping_result_get_grouping(res, 1);
        Expect(odd != nullptr && even != nullptr, "group_by_i4 buckets are non-null");
        Expect(il2cpp::il2cpp_linq_grouping_get_key(odd) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_key(even) == 0,
               "group_by_i4 buckets appear in FIRST-APPEARANCE key order (1 then 0)");
        Expect(il2cpp::il2cpp_linq_grouping_get_count(odd) == 5 &&
                   il2cpp::il2cpp_linq_grouping_get_count(even) == 4,
               "group_by_i4 parity bucket counts are {5,4}");
        Expect(il2cpp::il2cpp_linq_grouping_sum(odd) == 25 &&
                   il2cpp::il2cpp_linq_grouping_sum(even) == 20,
               "group_by_i4 parity bucket sums are {25,20}");

        Il2CppArray* oddElems = il2cpp::il2cpp_linq_grouping_get_elements(odd);
        Il2CppArray* evenElems = il2cpp::il2cpp_linq_grouping_get_elements(even);
        Expect(oddElems != nullptr && evenElems != nullptr &&
                   oddElems->length == 5 && evenElems->length == 4,
               "group_by_i4 bucket element arrays have the bucket counts");
        bool oddOk = oddElems != nullptr && oddElems->length == 5;
        const int32_t oddExpected[5] = { 5, 3, 9, 1, 7 };
        for (int32_t i = 0; i < 5 && oddOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(oddElems, i)) != oddExpected[i])
                oddOk = false;
        }
        Expect(oddOk, "group_by_i4 odd bucket keeps source order {5,3,9,1,7}");
        bool evenOk = evenElems != nullptr && evenElems->length == 4;
        const int32_t evenExpected[4] = { 2, 8, 4, 6 };
        for (int32_t i = 0; i < 4 && evenOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(evenElems, i)) != evenExpected[i])
                evenOk = false;
        }
        Expect(evenOk, "group_by_i4 even bucket keeps source order {2,8,4,6}");

        // Iteration protocol: get_grouping(0..count-1) walks every bucket in
        // first-appearance order (a collect-free walk, so no rooting needed).
        bool iterOk = true;
        for (int32_t i = 0; i < 2 && iterOk; ++i)
        {
            if (il2cpp::il2cpp_linq_grouping_result_get_grouping(res, i) == nullptr)
                iterOk = false;
        }
        Expect(iterOk, "group_by_i4 result iteration yields every bucket");

        // Mod 3: keys 5->2, 3->0, 9->0, 1->1, 7->1, 2->2, 8->2, 4->1, 6->0 =>
        // first-appearance key order {2, 0, 1}.
        Il2CppObject* r3 = il2cpp::il2cpp_linq_group_by_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyMod3))));
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(r3) == 3,
               "group_by_i4 mod-3 yields 3 buckets");
        Expect(il2cpp::il2cpp_linq_grouping_get_key(
                   il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 0)) == 2 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 1)) == 0 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 2)) == 1,
               "group_by_i4 mod-3 buckets appear in first-appearance key order {2,0,1}");
        Expect(il2cpp::il2cpp_linq_grouping_get_count(
                   il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 0)) == 3 &&
                   il2cpp::il2cpp_linq_grouping_get_count(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 1)) == 3 &&
                   il2cpp::il2cpp_linq_grouping_get_count(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 2)) == 3,
               "group_by_i4 mod-3 bucket counts are {3,3,3}");
        Expect(il2cpp::il2cpp_linq_grouping_sum(
                   il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 0)) == 15 &&
                   il2cpp::il2cpp_linq_grouping_sum(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 1)) == 18 &&
                   il2cpp::il2cpp_linq_grouping_sum(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 2)) == 12,
               "group_by_i4 mod-3 bucket sums are {15,18,12} (total 45)");
        Il2CppArray* m0 = il2cpp::il2cpp_linq_grouping_get_elements(
            il2cpp::il2cpp_linq_grouping_result_get_grouping(r3, 0));
        bool m0Ok = m0 != nullptr && m0->length == 3;
        const int32_t m0Expected[3] = { 5, 2, 8 };
        for (int32_t i = 0; i < 3 && m0Ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(m0, i)) != m0Expected[i])
                m0Ok = false;
        }
        Expect(m0Ok, "group_by_i4 mod-3 key-2 bucket keeps source order {5,2,8}");
    }

    // --- 14b. group_by_i4: degenerate sources (empty / single / duplicate
    //         keys / negatives) ---
    {
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppObject* r0 = il2cpp::il2cpp_linq_group_by_i4(
            empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(r0) == 0,
               "group_by_i4 on an empty source yields 0 buckets");

        int32_t oneV[1] = { 42 };
        Il2CppArray* one = MakeI4Array(oneV, 1);
        Il2CppObject* r1 = il2cpp::il2cpp_linq_group_by_i4(
            one, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Il2CppObject* g1 = il2cpp::il2cpp_linq_grouping_result_get_grouping(r1, 0);
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(r1) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_key(g1) == 0 &&
                   il2cpp::il2cpp_linq_grouping_get_count(g1) == 1 &&
                   il2cpp::il2cpp_linq_grouping_sum(g1) == 42,
               "group_by_i4 on a single element yields one bucket with the element");

        int32_t sameV[3] = { 7, 7, 7 };
        Il2CppArray* same = MakeI4Array(sameV, 3);
        Il2CppObject* rs = il2cpp::il2cpp_linq_group_by_i4(
            same, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyIdent))));
        Il2CppObject* gs = il2cpp::il2cpp_linq_grouping_result_get_grouping(rs, 0);
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(rs) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_key(gs) == 7 &&
                   il2cpp::il2cpp_linq_grouping_get_count(gs) == 3 &&
                   il2cpp::il2cpp_linq_grouping_sum(gs) == 21,
               "group_by_i4 collapses duplicate keys into one bucket");

        // Checked per-bucket sum overflow (net48 Enumerable.Sum is checked,
        // D-LINQ-3): {int.MaxValue, 1} are both odd (parity key 1) -> one
        // bucket whose sum overflows.
        int32_t ovfV[2] = { INT32_MAX, 1 };
        Il2CppArray* ovfSrc = MakeI4Array(ovfV, 2);
        Il2CppObject* ovfRes = il2cpp::il2cpp_linq_group_by_i4(
            ovfSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_sum(
                    il2cpp::il2cpp_linq_grouping_result_get_grouping(ovfRes, 0)); },
                            "System.OverflowException"),
               "grouping_sum overflow raises OverflowException (net48 checked Sum)");

        // Negatives (net48 probe-verified): key & 1 on negatives keeps the
        // parity buckets {-1,-5,-3} (key 1) and {-2,-4} (key 0).
        int32_t negV[5] = { -1, -5, -3, -2, -4 };
        Il2CppArray* negs = MakeI4Array(negV, 5);
        Il2CppObject* rn = il2cpp::il2cpp_linq_group_by_i4(
            negs, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(rn) == 2 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(rn, 0)) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(rn, 1)) == 0,
               "group_by_i4 over negatives keeps the parity buckets");
        Expect(il2cpp::il2cpp_linq_grouping_sum(
                   il2cpp::il2cpp_linq_grouping_result_get_grouping(rn, 0)) == -9 &&
                   il2cpp::il2cpp_linq_grouping_sum(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(rn, 1)) == -6,
               "group_by_i4 negative bucket sums are {-9,-6}");
    }

    // --- 14c. group_by_i4 validation + receiver guards ---
    {
        int32_t one[1] = { 1 };
        Il2CppArray* src = MakeI4Array(one, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_group_by_i4(
                    nullptr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity)))); },
                            "System.ArgumentNullException"),
               "group_by_i4 with a null source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_group_by_i4(src, nullptr); },
                            "System.ArgumentNullException"),
               "group_by_i4 with a null key selector raises ArgumentNullException");

        il2cpp::Il2CppObject* mc = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyMod3))));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_group_by_i4(src, mc); },
                            "System.InvalidOperationException"),
               "group_by_i4 with a multicast key selector is loudly rejected");

        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_group_by_i4(
                    refArr, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity)))); },
                            "System.NotSupportedException"),
               "group_by_i4 on a reference array raises NotSupportedException");

        // Receiver guards (the listi4 defensive contract).
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_sum(nullptr); },
                            "System.NullReferenceException"),
               "grouping_sum on a null grouping raises NullReferenceException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_result_get_count(nullptr); },
                            "System.NullReferenceException"),
               "result_get_count on a null result raises NullReferenceException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_get_key(nullptr); },
                            "System.NullReferenceException"),
               "grouping_get_key on a null grouping raises NullReferenceException");

        Il2CppObject* r = il2cpp::il2cpp_linq_group_by_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_result_get_grouping(r, 1); },
                            "System.ArgumentOutOfRangeException"),
               "result_get_grouping out of range raises ArgumentOutOfRangeException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_grouping_result_get_grouping(r, -1); },
                            "System.ArgumentOutOfRangeException"),
               "result_get_grouping negative index raises ArgumentOutOfRangeException");
    }

    // --- 14d. group_by_i4: INSTANCE-BOUND selector + collecting selector ---
    {
        int32_t vals[9] = { 5, 3, 9, 1, 7, 2, 8, 4, 6 };
        Il2CppArray* src = MakeI4Array(vals, 9);

        // An instance-bound key selector (mod 3) buckets by the bound
        // instance's payload; a DIFFERENT bound instance reads its own.
        LinqKeyInst* k3 = NewKeyInst(3);
        Il2CppObject* sel3 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstKeyMod), reinterpret_cast<Il2CppObject*>(k3)));
        Il2CppObject* r = il2cpp::il2cpp_linq_group_by_i4(src, sel3);
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(r) == 3 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r, 0)) == 2 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r, 2)) == 1,
               "instance-bound group_by_i4 buckets by the bound instance's modulus");
        Expect(il2cpp::il2cpp_linq_grouping_sum(
                   il2cpp::il2cpp_linq_grouping_result_get_grouping(r, 2)) == 12,
               "instance-bound group_by_i4 mod-3 bucket sums match");

        LinqKeyInst* k2 = NewKeyInst(2);
        Il2CppObject* sel2 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstKeyMod), reinterpret_cast<Il2CppObject*>(k2)));
        Il2CppObject* r2 = il2cpp::il2cpp_linq_group_by_i4(src, sel2);
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(r2) == 2 &&
                   il2cpp::il2cpp_linq_grouping_get_key(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r2, 0)) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_count(
                       il2cpp::il2cpp_linq_grouping_result_get_grouping(r2, 0)) == 5,
               "a different bound instance groups with its own modulus (parity)");

        // A key selector that collects on every read forces a compaction in
        // the middle of the grouping loop (the icall roots source/delegate/
        // result across the callbacks). NOTE: after these collects the test's
        // own locals may be stale -- only the returned result is read, and the
        // reads below allocate nothing, so no later collection can move it.
        int32_t many[16];
        for (int32_t i = 0; i < 16; ++i) many[i] = i + 1; // 1..16
        Il2CppArray* gcSrc = MakeI4Array(many, 16);
        Il2CppObject* gcRes = il2cpp::il2cpp_linq_group_by_i4(
            gcSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcKeyParity))));
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(gcRes) == 2,
               "group_by_i4 with a collecting selector keeps both parity buckets");
        Il2CppObject* gcOdd = il2cpp::il2cpp_linq_grouping_result_get_grouping(gcRes, 0);
        Il2CppObject* gcEven = il2cpp::il2cpp_linq_grouping_result_get_grouping(gcRes, 1);
        Expect(il2cpp::il2cpp_linq_grouping_get_key(gcOdd) == 1 &&
                   il2cpp::il2cpp_linq_grouping_get_count(gcOdd) == 8 &&
                   il2cpp::il2cpp_linq_grouping_get_count(gcEven) == 8 &&
                   il2cpp::il2cpp_linq_grouping_sum(gcOdd) == 64 &&
                   il2cpp::il2cpp_linq_grouping_sum(gcEven) == 72,
               "group_by_i4 with a collecting selector yields correct bucket values");
    }

    // --- 15. concat_i4 (D-LINQZ-1): concatenation, net48 semantics ---
    {
        int32_t aV[3] = { 1, 2, 3 };
        int32_t bV[4] = { 4, 5, 6, 7 };
        Il2CppArray* a = MakeI4Array(aV, 3);
        Il2CppArray* b = MakeI4Array(bV, 4);

        // Basic: order preserved across the boundary.
        Il2CppArray* c = il2cpp::il2cpp_linq_concat_i4(a, b);
        Expect(c != nullptr && c->length == 7, "concat_i4 length equals the sum of the parts");
        bool ok = c != nullptr && c->length == 7;
        for (int32_t i = 0; i < 7 && ok; ++i)
        {
            const int32_t expected[7] = { 1, 2, 3, 4, 5, 6, 7 };
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(c, i)) != expected[i]) ok = false;
        }
        Expect(ok, "concat_i4 keeps source order across the boundary {1..7}");
        Expect(!c->isRefElem && c->elemSize == 4, "concat_i4 result is an int32 array");

        // Duplicates preserved across the boundary.
        int32_t dV[3] = { 3, 1, 3 };
        Il2CppArray* d = MakeI4Array(dV, 3);
        Il2CppArray* cd = il2cpp::il2cpp_linq_concat_i4(d, d);
        Expect(cd != nullptr && cd->length == 6, "concat_i4 keeps duplicates across the boundary");
        bool okd = cd != nullptr && cd->length == 6;
        const int32_t dExpected[6] = { 3, 1, 3, 3, 1, 3 };
        for (int32_t i = 0; i < 6 && okd; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(cd, i)) != dExpected[i]) okd = false;
        }
        Expect(okd, "concat_i4 duplicate values are preserved in order");

        // Empty-first / empty-second / both-empty.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* ef = il2cpp::il2cpp_linq_concat_i4(empty, a);
        Expect(ef != nullptr && ef->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ef, 0)) == 1,
               "concat_i4 empty-first yields the second alone");
        Il2CppArray* es = il2cpp::il2cpp_linq_concat_i4(a, empty);
        Expect(es != nullptr && es->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(es, 2)) == 3,
               "concat_i4 empty-second yields the first alone");
        Il2CppArray* ee = il2cpp::il2cpp_linq_concat_i4(empty, empty);
        Expect(ee != nullptr && ee->length == 0, "concat_i4 both-empty yields an empty result");

        // Multi-sequence chain: (a+b)+a.
        Il2CppArray* chain = il2cpp::il2cpp_linq_concat_i4(
            il2cpp::il2cpp_linq_concat_i4(a, b), MakeI4Array(aV, 3));
        Expect(chain != nullptr && chain->length == 10, "concat_i4 chains ((a+b)+a) to length 10");
        bool okc = chain != nullptr && chain->length == 10;
        const int32_t cExpected[10] = { 1, 2, 3, 4, 5, 6, 7, 1, 2, 3 };
        for (int32_t i = 0; i < 10 && okc; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(chain, i)) != cExpected[i]) okc = false;
        }
        Expect(okc, "concat_i4 chain result preserves both operand orders");

        // Result is a NEW array: mutation isolation.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(c, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0)) == 1,
               "mutating the concat_i4 result does not affect the source");

        // Validation: nulls -> ArgumentNullException; ref arrays -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_concat_i4(nullptr, a); },
                            "System.ArgumentNullException"),
               "concat_i4 with a null first source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_concat_i4(a, nullptr); },
                            "System.ArgumentNullException"),
               "concat_i4 with a null second source raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_concat_i4(refArr, a); },
                            "System.NotSupportedException"),
               "concat_i4 on a reference first array raises NotSupportedException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_concat_i4(a, refArr); },
                            "System.NotSupportedException"),
               "concat_i4 on a reference second array raises NotSupportedException");
    }

    // --- 16. distinct_i4 (D-LINQZ-1): first-appearance dedup, net48 ---
    {
        int32_t dupV[6] = { 3, 1, 3, 2, 1, 4 };
        Il2CppArray* dup = MakeI4Array(dupV, 6);
        Il2CppArray* d = il2cpp::il2cpp_linq_distinct_i4(dup);
        Expect(d != nullptr && d->length == 4, "distinct_i4 collapses duplicates");
        bool ok = d != nullptr && d->length == 4;
        const int32_t expected[4] = { 3, 1, 2, 4 };
        for (int32_t i = 0; i < 4 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(d, i)) != expected[i]) ok = false;
        }
        Expect(ok, "distinct_i4 keeps FIRST-APPEARANCE order {3,1,2,4}");

        // All-distinct: unchanged length + order.
        int32_t aV[5] = { 1, 2, 3, 4, 5 };
        Il2CppArray* a = MakeI4Array(aV, 5);
        Il2CppArray* da = il2cpp::il2cpp_linq_distinct_i4(a);
        Expect(da != nullptr && da->length == 5 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(da, 0)) == 1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(da, 4)) == 5,
               "distinct_i4 on an all-distinct source is unchanged");

        // All-same: collapses to one element.
        int32_t sameV[4] = { 7, 7, 7, 7 };
        Il2CppArray* same = MakeI4Array(sameV, 4);
        Il2CppArray* ds = il2cpp::il2cpp_linq_distinct_i4(same);
        Expect(ds != nullptr && ds->length == 1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ds, 0)) == 7,
               "distinct_i4 on an all-same source collapses to one element");

        // Single / empty.
        int32_t oneV[1] = { 42 };
        Il2CppArray* one = MakeI4Array(oneV, 1);
        Il2CppArray* d1 = il2cpp::il2cpp_linq_distinct_i4(one);
        Expect(d1 != nullptr && d1->length == 1, "distinct_i4 on a single element passes through");
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* de = il2cpp::il2cpp_linq_distinct_i4(empty);
        Expect(de != nullptr && de->length == 0, "distinct_i4 on an empty source yields an empty result");

        // Negatives + zero: value-based dedup, first occurrence kept.
        int32_t negV[6] = { -3, -1, -3, 0, -2, -1 };
        Il2CppArray* negs = MakeI4Array(negV, 6);
        Il2CppArray* dn = il2cpp::il2cpp_linq_distinct_i4(negs);
        Expect(dn != nullptr && dn->length == 4 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(dn, 0)) == -3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(dn, 3)) == -2,
               "distinct_i4 dedups negatives by value keeping first occurrence");

        // Distinct after a Wired-operator chain (where -> distinct): the dup
        // odds are {3,3,1} -> first-appearance {3,1}.
        Il2CppArray* chain = il2cpp::il2cpp_linq_distinct_i4(
            il2cpp::il2cpp_linq_where_i4(dup, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsOdd)))));
        Expect(chain != nullptr && chain->length == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(chain, 0)) == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(chain, 1)) == 1,
               "distinct_i4 after where keeps first-appearance order of the filtered values");

        // Result is a NEW array: mutation isolation.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(d, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(dup, 0)) == 3,
               "mutating the distinct_i4 result does not affect the source");

        // Validation: null -> ArgumentNullException; ref array -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_distinct_i4(nullptr); },
                            "System.ArgumentNullException"),
               "distinct_i4 with a null source raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_distinct_i4(refArr); },
                            "System.NotSupportedException"),
               "distinct_i4 on a reference array raises NotSupportedException");
    }

    // --- 17. except_i4 (D-LINQZ-1): set difference, net48 ---
    {
        int32_t dupV[6] = { 3, 1, 3, 2, 1, 4 };
        Il2CppArray* dup = MakeI4Array(dupV, 6);
        int32_t oneV[1] = { 1 };
        Il2CppArray* one = MakeI4Array(oneV, 1);

        // Basic: {3,1,3,2,1,4} minus {1} -> {3,2,4}.
        Il2CppArray* e = il2cpp::il2cpp_linq_except_i4(dup, one);
        Expect(e != nullptr && e->length == 3, "except_i4 yields the difference length");
        bool ok = e != nullptr && e->length == 3;
        const int32_t expected[3] = { 3, 2, 4 };
        for (int32_t i = 0; i < 3 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(e, i)) != expected[i]) ok = false;
        }
        Expect(ok, "except_i4 keeps first-appearance order and dedups {3,2,4}");

        // No overlap: everything returned deduplicated in order.
        int32_t aV[5] = { 1, 2, 3, 4, 5 };
        Il2CppArray* a = MakeI4Array(aV, 5);
        int32_t noV[2] = { 90, 91 };
        Il2CppArray* no = MakeI4Array(noV, 2);
        Il2CppArray* en = il2cpp::il2cpp_linq_except_i4(a, no);
        Expect(en != nullptr && en->length == 5 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(en, 0)) == 1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(en, 4)) == 5,
               "except_i4 no-overlap returns everything in first-appearance order");

        // Full overlap: empty result.
        Il2CppArray* ef = il2cpp::il2cpp_linq_except_i4(a, a);
        Expect(ef != nullptr && ef->length == 0, "except_i4 full-overlap yields an empty result");

        // Empty-first: empty; empty-second: deduplicated first.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* ef0 = il2cpp::il2cpp_linq_except_i4(empty, a);
        Expect(ef0 != nullptr && ef0->length == 0, "except_i4 empty-first yields an empty result");
        Il2CppArray* es = il2cpp::il2cpp_linq_except_i4(dup, empty);
        Expect(es != nullptr && es->length == 4 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(es, 3)) == 4,
               "except_i4 empty-second returns the deduplicated first");

        // Duplicates in the second: set membership, not count ({1,1} removes all 1s).
        int32_t dup2V[2] = { 1, 1 };
        Il2CppArray* dup2 = MakeI4Array(dup2V, 2);
        Il2CppArray* e2 = il2cpp::il2cpp_linq_except_i4(dup, dup2);
        Expect(e2 != nullptr && e2->length == 3,
               "except_i4 duplicates in the second are set membership, not count");

        // Negatives interplay.
        int32_t negV[6] = { -3, -1, -3, 0, -2, -1 };
        Il2CppArray* negs = MakeI4Array(negV, 6);
        int32_t nV[1] = { -3 };
        Il2CppArray* n1 = MakeI4Array(nV, 1);
        Il2CppArray* en2 = il2cpp::il2cpp_linq_except_i4(negs, n1);
        Expect(en2 != nullptr && en2->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(en2, 0)) == -1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(en2, 2)) == -2,
               "except_i4 over negatives keeps first-appearance order");

        // Except after a Where chain: {3,1,3,2,1,4} evens = {2,4} minus {1} -> {2,4}.
        Il2CppArray* ew = il2cpp::il2cpp_linq_except_i4(
            il2cpp::il2cpp_linq_where_i4(dup, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))),
            one);
        Expect(ew != nullptr && ew->length == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ew, 0)) == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ew, 1)) == 4,
               "except_i4 after where keeps the surviving filtered values");

        // Validation: nulls -> ArgumentNullException; ref arrays -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_except_i4(nullptr, one); },
                            "System.ArgumentNullException"),
               "except_i4 with a null first source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_except_i4(dup, nullptr); },
                            "System.ArgumentNullException"),
               "except_i4 with a null second source raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_except_i4(refArr, one); },
                            "System.NotSupportedException"),
               "except_i4 on a reference first array raises NotSupportedException");
    }

    // --- 18. zip_i4 (D-LINQZ-1): element-wise pairing to the shorter length ---
    {
        int32_t aV[5] = { 1, 2, 3, 4, 5 };
        Il2CppArray* a = MakeI4Array(aV, 5);
        int32_t bV[5] = { 10, 21, 30, 41, 50 };
        Il2CppArray* b = MakeI4Array(bV, 5);

        // Equal lengths: Add -> {11,23,33,45,55} (1+10, 2+21, 3+30, 4+41, 5+50).
        Il2CppArray* z = il2cpp::il2cpp_linq_zip_i4(a, b,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))));
        Expect(z != nullptr && z->length == 5, "zip_i4 equal lengths keeps the full length");
        bool ok = z != nullptr && z->length == 5;
        const int32_t zExpected[5] = { 11, 23, 33, 45, 55 };
        for (int32_t i = 0; i < 5 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z, i)) != zExpected[i]) ok = false;
        }
        Expect(ok, "zip_i4 equal lengths pairs element-wise {11,23,33,45,55}");

        // First shorter: result length = min(5,3) = 3.
        int32_t s1V[3] = { 1, 2, 3 };
        Il2CppArray* s1 = MakeI4Array(s1V, 3);
        Il2CppArray* zs1 = il2cpp::il2cpp_linq_zip_i4(a, s1,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipMul))));
        Expect(zs1 != nullptr && zs1->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(zs1, 2)) == 9,
               "zip_i4 first-shorter pairs to the shorter length {1,4,9}");

        // Second shorter: result length = min(5,2) = 2.
        int32_t s2V[2] = { 100, 200 };
        Il2CppArray* s2 = MakeI4Array(s2V, 2);
        Il2CppArray* zs2 = il2cpp::il2cpp_linq_zip_i4(a, s2,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipDiff))));
        Expect(zs2 != nullptr && zs2->length == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(zs2, 1)) == -198,
               "zip_i4 second-shorter pairs to the shorter length {1-100,2-200}");

        // Single-element pair.
        int32_t oneV[1] = { 7 };
        Il2CppArray* one = MakeI4Array(oneV, 1);
        Il2CppArray* z1 = il2cpp::il2cpp_linq_zip_i4(one, one,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))));
        Expect(z1 != nullptr && z1->length == 1 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z1, 0)) == 14,
               "zip_i4 single-element pair sums to 14");

        // EMPTY operand -> empty result (no selector invocation).
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* ze1 = il2cpp::il2cpp_linq_zip_i4(a, empty,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))));
        Il2CppArray* ze2 = il2cpp::il2cpp_linq_zip_i4(empty, a,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))));
        Expect(ze1 != nullptr && ze1->length == 0 && ze2 != nullptr && ze2->length == 0,
               "zip_i4 with an empty operand yields an empty result");

        // Zip over a wired where chain: odds {1,3,5} x evens {10,30,50} -> {11,33,55}.
        Il2CppArray* zw = il2cpp::il2cpp_linq_zip_i4(
            il2cpp::il2cpp_linq_where_i4(a, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsOdd)))),
            il2cpp::il2cpp_linq_where_i4(b, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))));
        Expect(zw != nullptr && zw->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(zw, 0)) == 11 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(zw, 2)) == 55,
               "zip_i4 over where chains pairs the filtered values {11,33,55}");

        // Result is a NEW array: mutation isolation.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(a, 0)) == 1,
               "mutating the zip_i4 result does not affect the source");

        // Validation: nulls -> ArgumentNullException; ref array -> NotSupported;
        // multicast selector -> InvalidOperationException.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_zip_i4(nullptr, b,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd)))); },
                            "System.ArgumentNullException"),
               "zip_i4 with a null first source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_zip_i4(a, nullptr,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd)))); },
                            "System.ArgumentNullException"),
               "zip_i4 with a null second source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_zip_i4(a, b, nullptr); },
                            "System.ArgumentNullException"),
               "zip_i4 with a null selector raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_zip_i4(refArr, b,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd)))); },
                            "System.NotSupportedException"),
               "zip_i4 on a reference first array raises NotSupportedException");
        il2cpp::Il2CppObject* mc = il2cpp::runtime::icall_delegate_combine(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipAdd))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ZipMul))));
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_zip_i4(a, b, mc); },
                            "System.InvalidOperationException"),
               "zip_i4 with a multicast selector is loudly rejected");
    }

    // --- 18b. zip_i4: INSTANCE-BOUND 2-arg selector + collecting selector ---
    {
        int32_t aV[3] = { 1, 2, 3 };
        Il2CppArray* a = MakeI4Array(aV, 3);

        // An instance-bound 2-arg selector (offset 100) reads the bound
        // instance's payload through `this` (arg0) -> a + b + 100.
        LinqZipInst* p = NewZipInst(100);
        Il2CppObject* sel = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstZipAddOffset), reinterpret_cast<Il2CppObject*>(p)));
        Il2CppArray* z = il2cpp::il2cpp_linq_zip_i4(a, a, sel);
        Expect(z != nullptr && z->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z, 0)) == 102 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z, 2)) == 106,
               "instance-bound zip_i4 adds the bound instance's offset");

        // A DIFFERENT bound instance reads its own payload.
        LinqZipInst* q = NewZipInst(1000);
        Il2CppObject* sel2 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstZipAddOffset), reinterpret_cast<Il2CppObject*>(q)));
        Il2CppArray* z2 = il2cpp::il2cpp_linq_zip_i4(a, a, sel2);
        Expect(z2 != nullptr && z2->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(z2, 0)) == 1002,
               "a different bound zip_i4 instance reads its own offset");

        // A selector that collects on every invocation forces a compaction
        // mid-loop while the icall holds the delegate AND both arrays AND the
        // result (all four rooted). NOTE: after these collects the test's own
        // locals may be stale -- only the returned result is read, and the
        // reads allocate nothing, so no later collection can move it.
        int32_t big[16];
        for (int32_t i = 0; i < 16; ++i) big[i] = i + 1;
        Il2CppArray* src = MakeI4Array(big, 16);
        Il2CppArray* gz = il2cpp::il2cpp_linq_zip_i4(src, src,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcZipAdd))));
        Expect(gz != nullptr && gz->length == 16,
               "zip_i4 with a collecting selector keeps all 16 pairs");
        bool ok = gz != nullptr && gz->length == 16;
        for (int32_t i = 0; i < 16 && ok; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(gz, i)) != 2 * (i + 1)) ok = false;
        }
        Expect(ok, "zip_i4 with a collecting selector yields correct pair sums");
    }

    // --- 19. take_while_i4 / skip_while_i4 (D-LT-2): prefix semantics ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10); // {1..10}

        // TakeWhile(<5): {1,2,3,4} -- stops at 5 (excluded).
        Il2CppArray* tw = il2cpp::il2cpp_linq_take_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5))));
        Expect(tw != nullptr && tw->length == 4,
               "take_while_i4(<5) keeps the 4-element prefix");
        bool twOk = tw != nullptr && tw->length == 4;
        for (int32_t i = 0; i < 4 && twOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(tw, i)) != i + 1)
                twOk = false;
        }
        Expect(twOk, "take_while_i4(<5) result values are {1,2,3,4}");

        // All-true predicate -> distinct full copy; all-false -> empty.
        Il2CppArray* all = il2cpp::il2cpp_linq_take_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(all != nullptr && all->length == 10 && all != src,
               "take_while_i4(all-true) yields a distinct full copy");
        Il2CppArray* none = il2cpp::il2cpp_linq_take_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysFalse))));
        Expect(none != nullptr && none->length == 0,
               "take_while_i4(all-false) yields an empty result");

        // Empty source -> empty result.
        int32_t empty0[1] = { 0 };
        Il2CppArray* empty = MakeI4Array(empty0, 0);
        Il2CppArray* te = il2cpp::il2cpp_linq_take_while_i4(
            empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(te != nullptr && te->length == 0,
               "take_while_i4 on an empty source yields an empty result");

        // net48: the predicate runs EXACTLY ONCE per VISITED element and
        // stops at the first failure -- over 1..10 with <5 exactly 5 calls
        // ({1,2,3,4,5}; the failing element is visited but not kept).
        g_predCalls = 0;
        Il2CppArray* cnt = il2cpp::il2cpp_linq_take_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&CountLt5))));
        Expect(cnt != nullptr && cnt->length == 4 && g_predCalls == 5,
               "take_while_i4 calls the predicate once per visited element and stops at the first failure");

        // SkipWhile(<5): {5,6,7,8,9,10} -- the first failing element (5) AND
        // the rest are kept.
        Il2CppArray* sw = il2cpp::il2cpp_linq_skip_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5))));
        Expect(sw != nullptr && sw->length == 6,
               "skip_while_i4(<5) keeps the 6-element tail");
        bool swOk = sw != nullptr && sw->length == 6;
        for (int32_t i = 0; i < 6 && swOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(sw, i)) != i + 5)
                swOk = false;
        }
        Expect(swOk, "skip_while_i4(<5) result values are {5,6,7,8,9,10}");

        // All-true predicate -> empty result; the predicate is never called
        // after the prefix ends (net48: exactly 5 calls for <5 over 1..10).
        Il2CppArray* swAll = il2cpp::il2cpp_linq_skip_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(swAll != nullptr && swAll->length == 0,
               "skip_while_i4(all-true) yields an empty result");
        g_predCalls = 0;
        Il2CppArray* sc = il2cpp::il2cpp_linq_skip_while_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&CountLt5))));
        Expect(sc != nullptr && sc->length == 6 && g_predCalls == 5,
               "skip_while_i4 calls the predicate only over the skip prefix (5 calls)");

        // Empty source -> empty.
        Il2CppArray* se = il2cpp::il2cpp_linq_skip_while_i4(
            empty, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&AlwaysTrue))));
        Expect(se != nullptr && se->length == 0,
               "skip_while_i4 on an empty source yields an empty result");

        // Chaining: take_while after where. evens of 1..10 = {2,4,6,8,10};
        // TakeWhile(<6) = {2,4}.
        Il2CppArray* wtw = il2cpp::il2cpp_linq_take_while_i4(
            il2cpp::il2cpp_linq_where_i4(
                src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IsEven)))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt6))));
        Expect(wtw != nullptr && wtw->length == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(wtw, 0)) == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(wtw, 1)) == 4,
               "take_while after where filters the mapped values {2,4}");

        // Mutation isolation: the result is a fresh array.
        *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(tw, 0)) = 999;
        Expect(*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(src, 0)) == 1,
               "mutating the take_while_i4 result does not affect the source");

        // Validation: nulls -> ArgumentNullException; ref array -> NotSupported.
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_take_while_i4(nullptr,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5)))); },
                            "System.ArgumentNullException"),
               "take_while_i4 with a null source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_take_while_i4(src, nullptr); },
                            "System.ArgumentNullException"),
               "take_while_i4 with a null predicate raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_skip_while_i4(nullptr,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5)))); },
                            "System.ArgumentNullException"),
               "skip_while_i4 with a null source raises ArgumentNullException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_skip_while_i4(src, nullptr); },
                            "System.ArgumentNullException"),
               "skip_while_i4 with a null predicate raises ArgumentNullException");
        const Il2CppObject* s = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_cs("x"));
        Il2CppArray* refArr = MakeRefArray(&s, 1);
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_take_while_i4(refArr,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5)))); },
                            "System.NotSupportedException"),
               "take_while_i4 on a reference array raises NotSupportedException");
        Expect(ThrowsDotNet([&]() { il2cpp::il2cpp_linq_skip_while_i4(refArr,
                    reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&Lt5)))); },
                            "System.NotSupportedException"),
               "skip_while_i4 on a reference array raises NotSupportedException");

        // INSTANCE-BOUND 1-arg predicate (the linqcase shape): an instance
        // with threshold 5 -- InstLt keeps {1,2,3,4}; a different instance
        // (threshold 3) keeps {1,2}.
        LinqPredInst* p5 = NewPredInst(5);
        Il2CppObject* lt5 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstLt), reinterpret_cast<Il2CppObject*>(p5)));
        Il2CppArray* ibTw = il2cpp::il2cpp_linq_take_while_i4(src, lt5);
        Expect(ibTw != nullptr && ibTw->length == 4 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ibTw, 3)) == 4,
               "instance-bound take_while_i4 uses the bound instance's threshold");
        Il2CppArray* ibSw = il2cpp::il2cpp_linq_skip_while_i4(src, lt5);
        Expect(ibSw != nullptr && ibSw->length == 6 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ibSw, 0)) == 5,
               "instance-bound skip_while_i4 uses the bound instance's threshold");
        LinqPredInst* p3 = NewPredInst(3);
        Il2CppObject* lt3 = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstLt), reinterpret_cast<Il2CppObject*>(p3)));
        Il2CppArray* ibTw2 = il2cpp::il2cpp_linq_take_while_i4(src, lt3);
        Expect(ibTw2 != nullptr && ibTw2->length == 2 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ibTw2, 1)) == 2,
               "a different bound instance filters with its own threshold");

        // GC: an instance-bound collecting predicate that compacts mid-loop.
        // Threshold 8 -> keep {1..7} while the predicate collects 7 times.
        int32_t many[16];
        for (int32_t i = 0; i < 16; ++i) many[i] = i + 1;
        Il2CppArray* gcSrc = MakeI4Array(many, 16);
        LinqPredInst* gp = NewPredInst(8);
        Il2CppObject* gcLt = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstLtCollect), reinterpret_cast<Il2CppObject*>(gp)));
        Il2CppArray* gcTw = il2cpp::il2cpp_linq_take_while_i4(gcSrc, gcLt);
        Expect(gcTw != nullptr && gcTw->length == 7,
               "instance-bound take_while_i4 with a collecting predicate keeps the prefix");
        bool gcOk = gcTw != nullptr && gcTw->length == 7;
        for (int32_t i = 0; i < 7 && gcOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(gcTw, i)) != i + 1)
                gcOk = false;
        }
        Expect(gcOk, "instance-bound take_while_i4 with a collecting predicate yields correct values");
    }

    // --- 19b. take_while_idx / skip_while_idx: the 2-arg (value, source-index)
    //         predicates + instance-bound and collecting forms ---
    {
        int32_t vals[10];
        TenValues(vals);
        Il2CppArray* src = MakeI4Array(vals, 10); // {1..10}

        // Index predicate (i < 3): TakeWhile keeps {1,2,3} (element 4 sits at
        // index 3 -> fails); SkipWhile keeps {4,5,6,7,8,9,10}.
        Il2CppArray* ti = il2cpp::il2cpp_linq_take_while_idx_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IdxLt3))));
        Expect(ti != nullptr && ti->length == 3 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ti, 2)) == 3,
               "take_while_idx_i4(i<3) keeps the 3-element prefix {1,2,3}");
        Il2CppArray* si = il2cpp::il2cpp_linq_skip_while_idx_i4(
            src, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&IdxLt3))));
        Expect(si != nullptr && si->length == 7 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(si, 0)) == 4 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(si, 6)) == 10,
               "skip_while_idx_i4(i<3) keeps {4..10}");

        // An INSTANCE-BOUND 2-arg index predicate (threshold 5 -> keep while
        // i < 5): TakeWhile keeps {1..5}, SkipWhile keeps {6..10}.
        LinqPredInst* p5 = NewPredInst(5);
        Il2CppObject* ib = reinterpret_cast<Il2CppObject*>(
            DelInst(reinterpret_cast<void*>(&InstIdxBound), reinterpret_cast<Il2CppObject*>(p5)));
        Il2CppArray* ibTi = il2cpp::il2cpp_linq_take_while_idx_i4(src, ib);
        Expect(ibTi != nullptr && ibTi->length == 5 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ibTi, 4)) == 5,
               "instance-bound take_while_idx_i4 uses the bound threshold on the index");
        Il2CppArray* ibSi = il2cpp::il2cpp_linq_skip_while_idx_i4(src, ib);
        Expect(ibSi != nullptr && ibSi->length == 5 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(ibSi, 0)) == 6,
               "instance-bound skip_while_idx_i4 uses the bound threshold on the index");

        // A collecting 2-arg predicate compacts mid-loop while the icall holds
        // the delegate and the source; the result must survive. NOTE: after
        // these collects the test's own locals may be stale -- only the
        // returned result is read.
        int32_t big[32];
        for (int32_t i = 0; i < 32; ++i) big[i] = i + 1;
        Il2CppArray* gcSrc = MakeI4Array(big, 32);
        Il2CppArray* gcTw = il2cpp::il2cpp_linq_take_while_idx_i4(
            gcSrc, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GcIdxLt5))));
        Expect(gcTw != nullptr && gcTw->length == 4,
               "take_while_idx_i4 with a collecting predicate keeps the prefix");
        bool gcOk = gcTw != nullptr && gcTw->length == 4;
        for (int32_t i = 0; i < 4 && gcOk; ++i)
        {
            if (*reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(gcTw, i)) != i + 1)
                gcOk = false;
        }
        Expect(gcOk, "take_while_idx_i4 with a collecting predicate yields correct values");
    }

    // --- 20. nb9 fix H1/H2/M1: group_by_full element + result selectors ---
    {
        // 20a (H1): a REFERENCE element selector over a 12-element bucket
        // (crossing the 4/8 growth boundaries) where the selector leaves ~1MB
        // of unreachable garbage per call, so the bucket-growth allocation
        // itself trips the Gen0 budget and compacts the heap while the
        // projected element is live. Before the fix 'elemRef' was a bare local
        // and the bucket got stale/corrupt values.
        int32_t twelve[12];
        for (int32_t i = 0; i < 12; ++i) twelve[i] = i;
        Il2CppArray* src = MakeI4Array(twelve, 12);
        Il2CppObject* res = il2cpp::il2cpp_linq_group_by_full(
            src,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyZero))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GarbageElemStr))),
            nullptr, nullptr, /*elemKind=*/1, /*rsKind=*/0);
        Expect(il2cpp::il2cpp_linq_grouping_result_get_count(res) == 1,
               "group_by_full ref-element: a single key yields one bucket");
        Il2CppObject* g = il2cpp::il2cpp_linq_grouping_result_get_grouping(res, 0);
        Expect(il2cpp::il2cpp_linq_grouping_get_count(g) == 12,
               "group_by_full ref-element: the grown bucket keeps all 12 elements");
        Il2CppArray* elems = il2cpp::il2cpp_linq_grouping_get_elements(g);
        bool refOk = elems != nullptr && elems->length == 12 && elems->isRefElem;
        for (int32_t i = 0; i < 12 && refOk; ++i)
        {
            Il2CppObject* s = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(elems, i));
            char expected[16];
            std::snprintf(expected, sizeof(expected), "e%d", twelve[i]);
            if (!IsAsciiString(s, expected)) refOk = false;
        }
        Expect(refOk,
               "group_by_full ref-element: every projected string survives the growth compaction");

        // 20b (H2): a boxed int64 element selector over the same 12-element
        // bucket. Each append boxes AFTER the growth check, so 'grp' must be
        // re-derived from the rooted result before touching elements/count.
        int32_t wide[12];
        for (int32_t i = 0; i < 12; ++i) wide[i] = i + 1;
        Il2CppArray* srcW = MakeI4Array(wide, 12);
        Il2CppObject* resW = il2cpp::il2cpp_linq_group_by_full(
            srcW,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyZero))),
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&GarbageElemI8))),
            nullptr, nullptr, /*elemKind=*/2, /*rsKind=*/0);
        Il2CppObject* gW = il2cpp::il2cpp_linq_grouping_result_get_grouping(resW, 0);
        Expect(il2cpp::il2cpp_linq_grouping_get_count(gW) == 12,
               "group_by_full boxed-int64: the grown bucket keeps all 12 elements");
        Il2CppArray* elemsW = il2cpp::il2cpp_linq_grouping_get_elements(gW);
        bool wideOk = elemsW != nullptr && elemsW->length == 12 && elemsW->isRefElem;
        for (int32_t i = 0; i < 12 && wideOk; ++i)
        {
            Il2CppObject* boxed = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(elemsW, i));
            if (BoxedI8Value(boxed) != static_cast<int64_t>(wide[i]) * 1000000000LL)
                wideOk = false;
        }
        Expect(wideOk,
               "group_by_full boxed-int64: every boxed value survives the growth/box sequence");

        // 20c (M1): the result-selector surface (ApplyResultSelector) for both
        // TResult families -- one selector call per bucket, first-appearance
        // order. Source {1..10}: key 1 (5 elements) then key 0 (5 elements).
        int32_t ten[10];
        TenValues(ten);
        Il2CppArray* srcR = MakeI4Array(ten, 10);
        Il2CppObject* rI4 = il2cpp::il2cpp_linq_group_by_full(
            srcR,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))),
            nullptr, nullptr,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ResKeyCountI4))),
            /*elemKind=*/0, /*rsKind=*/1);
        Il2CppArray* outI4 = reinterpret_cast<Il2CppArray*>(rI4);
        Expect(outI4 != nullptr && outI4->length == 2 && !outI4->isRefElem &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(outI4, 0)) == 105 &&
                   *reinterpret_cast<int32_t*>(il2cpp::il2cpp_array_get_elem_ptr(outI4, 1)) == 5,
               "group_by_full result-selector: int32 TResult = {105,5}");

        Il2CppObject* rRef = il2cpp::il2cpp_linq_group_by_full(
            srcR,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&KeyParity))),
            nullptr, nullptr,
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&ResKeyCountStr))),
            /*elemKind=*/0, /*rsKind=*/2);
        Il2CppArray* outRef = reinterpret_cast<Il2CppArray*>(rRef);
        bool refSelOk = outRef != nullptr && outRef->length == 2 && outRef->isRefElem;
        if (refSelOk)
        {
            Il2CppObject* s0 = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(outRef, 0));
            Il2CppObject* s1 = *reinterpret_cast<Il2CppObject**>(
                il2cpp::il2cpp_array_get_elem_ptr(outRef, 1));
            refSelOk = IsAsciiString(s0, "k1=5") && IsAsciiString(s1, "k0=5");
        }
        Expect(refSelOk, "group_by_full result-selector: reference TResult = {k1=5,k0=5}");
    }

    // --- 21. nb9 P0 kernels: float fold, family ToArray, created comparers ---
    {
        // 21a (P0-1): a FLOAT accumulator over an int32 source. The lambda ABI
        // is float(double, int32_t) -- the accumulator rides the F stack as
        // double, the result comes back as float. Reading it as a double (the
        // pre-fix behaviour) yielded 10.5000019418076 instead of 10.5.
        int32_t four[4] = { 1, 2, 3, 4 };
        Il2CppArray* fsrc = MakeI4Array(four, 4);
        float facc = il2cpp::il2cpp_linq_aggregate_seed_f(
            fsrc, 0.5f, reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&FloatAdd))));
        Expect(facc == 10.5f, "aggregate_seed_f(0.5) over {1,2,3,4} == 10.5f");

        // 21b (P0-4): ToArray over a widened family clones by the source's own
        // layout (int64[] here); the int32-only clone would truncate.
        int64_t wide[3] = { 1, 2, 3 };
        Il2CppArray* wsrc = il2cpp::il2cpp_array_new(nullptr, 8, false, 3);
        for (int32_t i = 0; i < 3; ++i)
            *reinterpret_cast<int64_t*>(il2cpp::il2cpp_array_get_elem_ptr(wsrc, i)) = wide[i];
        Il2CppArray* wcopy = il2cpp::il2cpp_linq_to_array_family(wsrc);
        Expect(wcopy != nullptr && wcopy->length == 3 && wcopy->elemSize == 8 &&
                   !wcopy->isRefElem &&
                   *reinterpret_cast<int64_t*>(il2cpp::il2cpp_array_get_elem_ptr(wcopy, 2)) == 3,
               "to_array_family clones an int64[] with the 8-byte layout");

        // 21c (P0-2): Comparer<T>.Create must invoke the WRAPPED delegate.
        Il2CppObject* cmpI = il2cpp::runtime::il2cpp_comparer_create_int(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&CmpDescI4))));
        Expect(il2cpp::runtime::il2cpp_created_comparer_compare_int(cmpI, 1, 2) == 1 &&
                   il2cpp::runtime::il2cpp_created_comparer_compare_int(cmpI, 2, 1) == -1 &&
                   il2cpp::runtime::il2cpp_created_comparer_compare_int(cmpI, 2, 2) == 0,
               "Comparer<int>.Create(descending) Compare = {1,-1,0}");

        Il2CppObject* cmpL = il2cpp::runtime::il2cpp_comparer_create_long(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&CmpAscI8))));
        Expect(il2cpp::runtime::il2cpp_created_comparer_compare_long(cmpL, 5, 3) == 1,
               "Comparer<long>.Create(ascending) Compare(5,3) == 1");

        Il2CppObject* cmpD = il2cpp::runtime::il2cpp_comparer_create_double(
            reinterpret_cast<Il2CppObject*>(Del(reinterpret_cast<void*>(&CmpDescD))));
        Expect(il2cpp::runtime::il2cpp_created_comparer_compare_double(cmpD, 1.0, 2.0) == 1 &&
                   il2cpp::runtime::il2cpp_created_comparer_compare_double(cmpD, 2.0, 1.0) == -1,
               "Comparer<double>.Create(descending) Compare = {1,-1}");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d linq-map assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime linq-map tests passed\n");
    return 0;
}
