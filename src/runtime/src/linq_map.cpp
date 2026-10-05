//------------------------------------------------------------------------------
// linq_map.cpp -- System.Linq filtering / mapping icalls (LINQ T2 card).
//
// See linq_map.h for the domain contract. Implementation summary:
//   * Every entry validates the source array (null -> ArgumentNullException,
//     non-array klass -> ArgumentException, wrong element family for the
//     entry -> NotSupportedException) and the callback delegate (null ->
//     ArgumentNullException; multicast container -> InvalidOperationException)
//     BEFORE any element work. INSTANCE-BOUND single-cast delegates (target
//     != null -- a bound instance method like p.IsEven) ARE accepted: the
//     invoke side dispatches per binding, passing the stored target in the
//     generated instance method's `this` slot (delegateext.cpp / pinvokecb.cpp
//     precedent).
//   * Callbacks are invoked through their methodPtr with the family's fixed
//     signature; the result arrays are built by the runtime itself
//     (il2cpp_array_new) -- the result side is not restricted by the
//     transpiler's newarr limits (e.g. int32[] -> string[] works).
//   * Where runs the predicate exactly ONCE per element (net48 lazy
//     enumeration + ToArray semantics; rlaudit3). The single pass fills a
//     src-length scratch, then the exact-size result is copied out (or the
//     scratch returned directly when every element matched).
//
// GC SAFETY (physical contract, il2cpp_gc.h): the collector COMPACTS the
// young generation, so any allocation can move every live object. The
// callback can allocate on every element, so a collection may fire in the
// middle of a loop. The icalls therefore:
//   (a) root every managed array they hold across the callback loop by
//       registering the ADDRESS OF THE LOCAL as a static root slot (the
//       collector rewrites the slot on compaction, so re-reading the local
//       always observes the fresh address -- never cache a copy);
//   (b) never cache a managed ADDRESS across a callback: reference elements
//       are re-read through the rooted source array AFTER the callback
//       returns, and result-slot addresses are computed from the rooted
//       result array only AFTER the callback returns -- a pointer cached
//       across the call could be stale if a collection moved the object.
//       The re-read / address computation and the store into the result are
//       separated by NO allocation, so no collection can interleave between
//       them;
//   (c) remove the roots on every exit path (RAII scope guard; the
//       callback may throw a managed exception).
//------------------------------------------------------------------------------

#include "linq_map.h"

#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "listi4.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <vector>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Validation helpers (arraysort.cpp / arrayext.cpp conventions).
//------------------------------------------------------------------------------

// The multicast container's class name (delegateext.cpp descriptor).
const char kMulticastClass[] = "System.MulticastDelegate";

// The single-cast delegate class name (delegate.cpp descriptor).
const char kDelegateClass[] = "System.Delegate";

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// RequireArray -- validate a source-array argument (static-method semantics:
// null -> ArgumentNullException; a non-array object (caller bug) ->
// ArgumentException; never UB).
Il2CppArray* RequireArray(Il2CppArray* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr; // unreachable (the factory throws)
    }
    if (src->obj.klass == nullptr || src->obj.klass->name == nullptr ||
        src->obj.klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_argument_exception("Object is not an array.");
        return nullptr;
    }
    return src;
}

// RequireInt32Family / RequireRefFamily -- the entry's element-family
// contract: each family-specific entry only accepts arrays of its own kind
// (the array is self-describing via isRefElem/elemSize).
void RequireInt32Family(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 4)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int32 arrays are supported.");
    }
}

void RequireRefFamily(Il2CppArray* src)
{
    if (!src->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only reference-element arrays are supported.");
    }
}

// RequireCallback -- validate the callback delegate (LINQ callback
// contract, linq-domain.md §2): null -> ArgumentNullException; a multicast
// container -> InvalidOperationException (the multicast container is not a
// single leaf -- its `first` field sits at the same offset as methodPtr, so
// invoking it as a bare callback would call a data pointer as code); a
// non-delegate object is a caller bug -> ArgumentException.
//
// INSTANCE-BOUND (target-carrying) single-cast delegates ARE accepted: the
// delegate carries BOTH methodPtr and target (il2cpp_delegate.h), and the
// invoke side dispatches per binding -- a target-bound leaf is called with
// its target in the `this` slot (delegateext.cpp icall_multicast_invoke_i4 /
// pinvokecb.cpp InvokeWmBinding precedent). The caller re-reads d->target
// through the rooted delegate slot after every callback. Returns the
// validated delegate.
Il2CppDelegate* RequireCallback(Il2CppObject* cb, const char* paramName)
{
    if (cb == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
        return nullptr; // unreachable (the factory throws)
    }
    const char* name = cb->klass != nullptr ? cb->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, kMulticastClass) == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a LINQ callback.");
        return nullptr;
    }
    if (std::strcmp(name, kDelegateClass) != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
        return nullptr;
    }
    return reinterpret_cast<Il2CppDelegate*>(cb);
}

//------------------------------------------------------------------------------
// GC rooting scope: registers managed-pointer locals as static-root slots so
// the collector (which compacts the young generation) rewrites the LOCALS
// themselves when objects move. Removal is RAII -- the callback may throw a
// managed exception, and the roots must not leak (gc.cpp root table is
// fixed-capacity). Slots are re-read through the registered variables; never
// cache a copy of a managed pointer across a callback invocation.
//------------------------------------------------------------------------------
class RootScope
{
public:
    RootScope() : m_count(0) {}
    ~RootScope()
    {
        while (m_count > 0)
        {
            il2cpp_gc_remove_static_root(m_slots[m_count - 1]);
            --m_count;
        }
    }
    void Add(void** slot)
    {
        il2cpp_gc_add_static_root(slot);
        m_slots[m_count++] = slot;
    }

private:
    void** m_slots[4]; // max 4 used today (zip: delegate + first + second + result)
    int32_t m_count;
};

//------------------------------------------------------------------------------
// Element access (bounds-checked via il2cpp_array_get_elem_ptr).
//------------------------------------------------------------------------------
int32_t I4At(Il2CppArray* arr, int32_t index)
{
    return *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(arr, index));
}

Il2CppObject* RefAt(Il2CppArray* arr, int32_t index)
{
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, index));
}

//------------------------------------------------------------------------------
// Callback signatures (generated-code delegate ABI, il2cpp_delegate.h).
//   PlainFn    -- static-method leaf (target == null): the family's fixed
//                 signature bool(*)(T) / Out(*)(T).
//   ThisFn     -- instance-method leaf (target != null): the generated
//                 instance ABI puts `this` (the stored target) in the FIRST
//                 parameter slot (CodeGenerator: instance arg0 = this), so
//                 the call is (target, args...) -- the
//                 icall_multicast_invoke_i4 / InvokeWmBinding dispatch.
//------------------------------------------------------------------------------
typedef bool          (*PredI4Fn)(int32_t);
typedef bool          (*PredI4ThisFn)(Il2CppObject*, int32_t);
typedef bool          (*PredRefFn)(Il2CppObject*);
typedef bool          (*PredRefThisFn)(Il2CppObject*, Il2CppObject*);
typedef int32_t       (*SelI4I4Fn)(int32_t);
typedef int32_t       (*SelI4I4ThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelI4RefFn)(int32_t);
typedef Il2CppObject* (*SelI4RefThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelRefRefFn)(Il2CppObject*);
typedef Il2CppObject* (*SelRefRefThisFn)(Il2CppObject*, Il2CppObject*);

// Per-binding dispatch: an instance-bound leaf (target != null) receives its
// target in the `this` slot, a static leaf the plain family signature. The
// target is re-read through the ROOTED delegate slot (d is registered as a
// static root by every callback entry) so a compaction between callbacks
// (the leaf may allocate) rewrites both d and d->target.
bool InvokePredI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredI4Fn>(d->methodPtr)(v);
}

bool InvokePredRef(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<PredRefThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<PredRefFn>(d->methodPtr)(o);
}

int32_t InvokeSelI4I4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4I4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4I4Fn>(d->methodPtr)(v);
}

Il2CppObject* InvokeSelI4Ref(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4RefThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4RefFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeSelRefRef(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefRefThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefRefFn>(d->methodPtr)(o);
}

// The 2-arg int32 selector family (D-LINQZ-1 Zip, the Func<TSource,TSecond,
// TResult> ABI): int32_t(*)(int32_t, int32_t) for a STATIC leaf;
// int32_t(*)(Il2CppObject*, int32_t, int32_t) for an INSTANCE leaf (target in
// the `this` slot -- the linqib dispatch above).
typedef int32_t (*ZipI4I4Fn)(int32_t, int32_t);
typedef int32_t (*ZipI4I4ThisFn)(Il2CppObject*, int32_t, int32_t);

int32_t InvokeZipI4I4(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<ZipI4I4ThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ZipI4I4Fn>(d->methodPtr)(a, b);
}

// The 2-arg int32 PREDICATE family (D-LT-2, the TakeWhile/SkipWhile index
// overloads -- net48 Func<TSource,int,bool>): bool(*)(int32_t, int32_t) for a
// STATIC leaf; bool(*)(Il2CppObject*, int32_t, int32_t) for an INSTANCE leaf
// (target in the `this` slot -- the linqib dispatch above, Zip precedent).
typedef bool (*PredI4I4Fn)(int32_t, int32_t);
typedef bool (*PredI4I4ThisFn)(Il2CppObject*, int32_t, int32_t);

bool InvokePredI4I4(Il2CppDelegate* d, int32_t v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4I4ThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredI4I4Fn>(d->methodPtr)(v, index);
}

//------------------------------------------------------------------------------
// LINQ GROUPING domain (D-LINQ-4) -- managed object model, see linq_map.h.
// Layout pinning: the grouping classes' refFieldBitmap bits are literal
// constants below; the static_asserts tie them to the struct layout so a
// layout change with a stale bitmap cannot link silently (listi4.cpp
// precedent).
//------------------------------------------------------------------------------

// The full struct definitions live in linq_map.h (listi4.h precedent); the
// layout pinning below ties the class descriptors' refFieldBitmap bits to the
// struct layout so a layout change with a stale bitmap cannot link silently.
static_assert(offsetof(Il2CppLinqGrouping, elements) == 2 * sizeof(void*),
    "Il2CppLinqGrouping: elements must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppLinqGrouping, count) == 3 * sizeof(void*),
    "Il2CppLinqGrouping: count sits at slot 3 (NOT in the bitmap)");
static_assert(offsetof(Il2CppLinqGroupingResult, groups) == 1 * sizeof(void*),
    "Il2CppLinqGroupingResult: groups must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppLinqGroupingResult, count) == 2 * sizeof(void*),
    "Il2CppLinqGroupingResult: count sits at slot 2 (NOT in the bitmap)");

// The int32 key selector callback ABI is the select_i4_i4 family
// (int32(*)(int32_t) / int32(*)(Il2CppObject*, int32_t)) -- InvokeSelI4I4
// above dispatches per binding.

// Minimum bucket element-array length (listi4 kMinItems precedent: avoids
// resizing on the very first add).
constexpr int32_t kMinGroupItems = 4;

// Doubling cap: keeps the doubling loop inside int32 (no overflow).
constexpr int32_t kMaxGroupItems = 1 << 30;

int32_t GroupNextLength(int32_t oldLength)
{
    int32_t newLength = oldLength * 2;
    if (newLength < oldLength || newLength > kMaxGroupItems)
    {
        newLength = oldLength;
    }
    return newLength;
}

Il2CppLinqGrouping* AsGrouping(Il2CppObject* o)
{
    return reinterpret_cast<Il2CppLinqGrouping*>(o);
}

Il2CppLinqGroupingResult* AsGroupingResult(Il2CppObject* o)
{
    return reinterpret_cast<Il2CppLinqGroupingResult*>(o);
}

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// The grouping managed descriptors (registered lazily, listi4 precedent).
Il2CppClass g_groupingClass = {
    "System.Linq.Grouping",                 // name (runtime-internal descriptor)
    static_cast<int32_t>(sizeof(Il2CppLinqGrouping)), // instanceSize (32)
    0b100u,                                 // refFieldBitmap: bit 2 = elements
    1,                                      // refFieldCount
    nullptr,                                // baseClass
    nullptr,                                // vtable
    0,                                      // vtableSize
    nullptr,                                // interfaces
    0,                                      // interfaceCount
};

Il2CppClass g_groupingResultClass = {
    "System.Linq.GroupingResult",           // name (runtime-internal descriptor)
    static_cast<int32_t>(sizeof(Il2CppLinqGroupingResult)), // instanceSize (24)
    0b010u,                                 // refFieldBitmap: bit 1 = groups
    1,                                      // refFieldCount
    nullptr,                                // baseClass
    nullptr,                                // vtable
    0,                                      // vtableSize
    nullptr,                                // interfaces
    0,                                      // interfaceCount
};

bool g_groupingClassesRegistered = false;

void EnsureGroupingClassesRegistered()
{
    if (g_groupingClassesRegistered) return;
    il2cpp_class_register(&g_groupingClass);
    il2cpp_class_register(&g_groupingResultClass);
    g_groupingClassesRegistered = true;
}

// Grow the bucket's element array (doubling, listi4 GrowList discipline).
// The allocation may collect and COMPACT, so the bucket is re-derived through
// the ROOTED result's groups array afterwards; the old array stays reachable
// through the re-derived bucket while the pure int32 value copy runs (no
// allocation inside the copy).
void GrowGroupElements(Il2CppObject** resultSlot, int32_t bucketIndex)
{
    Il2CppLinqGroupingResult* res = AsGroupingResult(*resultSlot);
    Il2CppLinqGrouping* grp = AsGrouping(
        *reinterpret_cast<Il2CppObject**>(res->groups->data + bucketIndex * sizeof(void*)));
    const int32_t oldLength = grp->elements->length;
    const int32_t newLength = GroupNextLength(oldLength);
    if (newLength == oldLength)
    {
        return; // at the 2^30 cap
    }
    Il2CppArray* newElems = il2cpp_array_new(nullptr, sizeof(int32_t), false, newLength);
    res = AsGroupingResult(*resultSlot); // re-derive after the (possibly collecting) allocation
    grp = AsGrouping(
        *reinterpret_cast<Il2CppObject**>(res->groups->data + bucketIndex * sizeof(void*))); // re-derive the bucket
    const int32_t n = grp->count;
    int32_t* dst = reinterpret_cast<int32_t*>(newElems->data);
    const int32_t* src = reinterpret_cast<int32_t*>(grp->elements->data);
    for (int32_t i = 0; i < n; ++i)
    {
        dst[i] = src[i];
    }
    il2cpp_write_barrier(reinterpret_cast<void**>(&grp->elements),
                         reinterpret_cast<Il2CppObject*>(newElems));
    grp->elements = newElems;
}

} // namespace

//------------------------------------------------------------------------------
// Where -- filter (see linq_map.h).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_where_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // SINGLE predicate pass (rlaudit3): fill a src-length scratch.
    Il2CppArray* scratch = il2cpp_array_new(nullptr, 4, false, src->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (InvokePredI4(d, v))
        {
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(scratch, w)) = v;
            ++w;
        }
    }
    if (w == src->length)
    {
        return scratch; // already exact
    }
    // Exact-size result (net48 ToArray).
    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(scratch, i));
    }
    return out;
}

Il2CppArray* il2cpp_linq_where_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* scratch = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (InvokePredRef(d, RefAt(src, i)))
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(scratch, w));
            *slot = RefAt(src, i);
            ++w;
        }
    }
    if (w == src->length) return scratch;
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        *dst = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(scratch, i));
    }
    return out;
}

//------------------------------------------------------------------------------
// Select -- map (see linq_map.h).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_i4_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        // The callback runs FIRST; the result slot is computed afterwards
        // through the rooted 'out' (the callback may collect and compact,
        // moving the array -- a slot address cached across the call would be
        // stale). No allocation separates the address computation from the
        // store, so no collection can interleave.
        int32_t r = InvokeSelI4I4(d, I4At(src, i));
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_i4_ref(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        // The callback's return value is stored immediately after the call;
        // the slot address is computed from the rooted 'out' only AFTER the
        // callback returns (see select_i4_i4: a cached slot address would be
        // stale if the callback collected and moved the array).
        Il2CppObject* r = InvokeSelI4Ref(d, I4At(src, i));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        *slot = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_ref(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* r = InvokeSelRefRef(d, RefAt(src, i));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        *slot = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// First / FirstOrDefault -- no allocation at all, so no collection can
// interleave and no rooting is needed (arraysort.cpp precedent).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_first_i4(Il2CppArray* src)
{
    RequireArray(src);
    RequireInt32Family(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
        return 0; // unreachable (the factory throws)
    }
    return I4At(src, 0);
}

int32_t il2cpp_linq_first_predicate(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    // The predicate may allocate -> a collection can move 'src' AND the
    // delegate (whose target field is a GC reference); both are rooted and
    // re-read through their slots on every iteration. The element is passed
    // BY VALUE to the callback, so no managed pointer is held across the
    // call (linq.cpp predicate-loop discipline).
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // net48 First(predicate): return the FIRST element satisfying the
    // predicate (short-circuit); no match (or empty) -> InvalidOperationException.
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem)) return elem;
    }
    il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return 0; // unreachable (the factory throws)
}

Il2CppObject* il2cpp_linq_first_ref(Il2CppArray* src)
{
    RequireArray(src);
    RequireRefFamily(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
        return nullptr; // unreachable (the factory throws)
    }
    return RefAt(src, 0);
}

int32_t il2cpp_linq_first_or_default_i4(Il2CppArray* src)
{
    RequireArray(src);
    RequireInt32Family(src);
    return src->length != 0 ? I4At(src, 0) : 0;
}

Il2CppObject* il2cpp_linq_first_or_default_ref(Il2CppArray* src)
{
    RequireArray(src);
    RequireRefFamily(src);
    return src->length != 0 ? RefAt(src, 0) : nullptr;
}

int32_t il2cpp_linq_first_or_default_predicate(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    // The predicate may allocate -> a collection can move 'src' AND the
    // delegate (whose target field is a GC reference); both are rooted and
    // re-read through their slots on every iteration. The element is passed
    // BY VALUE to the callback, so no managed pointer is held across the
    // call (first_predicate discipline).
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // net48 FirstOrDefault(predicate): return the FIRST element satisfying
    // the predicate (short-circuit); no match (or empty) -> 0 (the int32
    // default) -- never throws for an empty / no-match source.
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem)) return elem;
    }
    return 0;
}

//------------------------------------------------------------------------------
// Take / Skip -- array slicing (net48 semantics, probe-verified). A NEGATIVE
// count does NOT throw: net48's deferred TakeIterator walks only while
// `count > 0` (so Take(-n) is an empty result) and SkipIterator yields
// everything when `count <= 0` (so Skip(-n) is a full copy). No callback, so
// the only GC interaction is the single allocation: the source is rooted
// across il2cpp_array_new (a collection can move it before the payload copy)
// and every element is read BY VALUE through the rooted slot afterwards (no
// allocation separates a read from its store, so no collection can
// interleave -- to_array discipline).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_take_i4(Il2CppArray* src, int32_t count)
{
    RequireArray(src);
    RequireInt32Family(src);

    const int32_t n = count <= 0 ? 0 : (count < src->length ? count : src->length);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, n);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = I4At(src, i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_skip_i4(Il2CppArray* src, int32_t count)
{
    RequireArray(src);
    RequireInt32Family(src);

    const int32_t from = count <= 0 ? 0 : (count < src->length ? count : src->length);
    const int32_t n = src->length - from;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, n);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = I4At(src, from + i);
    }
    return out;
}

//------------------------------------------------------------------------------
// TakeWhile / SkipWhile -- predicate-adjacent prefix/skip (D-LT-2, net48
// semantics, probe-verified). TakeWhile yields the longest PREFIX of elements
// for which the predicate is true and STOPS at the first failure (that
// element is excluded); SkipWhile skips the prefix and yields the first
// failing element plus EVERYTHING after it -- the predicate is NEVER called
// again after the prefix ends (net48). Both run the predicate EXACTLY ONCE
// per visited element (no two-pass deviation -- unlike Where). The output
// accumulator lives on the NATIVE heap (std::vector), so the scan is
// allocation-free on the managed side: the ONLY managed allocation is the
// result, with the source rooted across it (distinct_i4 discipline). The
// delegate and the source are rooted across the callback loop (the predicate
// may allocate -> a collection can move both), and every element is passed BY
// VALUE to the callback, so no managed pointer is held across a callback.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_take_while_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> out;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (!InvokePredI4(d, v)) break;
        out.push_back(v);
    }

    Il2CppArray* res = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(out.size()));
    roots.Add(reinterpret_cast<void**>(&res));

    for (size_t i = 0; i < out.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(res, static_cast<int32_t>(i))) = out[i];
    }
    return res;
}

Il2CppArray* il2cpp_linq_skip_while_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> out;
    bool skipping = true;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (skipping)
        {
            if (InvokePredI4(d, v)) continue; // still inside the skip prefix
            skipping = false;                 // first failure: keep THIS element too
        }
        out.push_back(v);
    }

    Il2CppArray* res = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(out.size()));
    roots.Add(reinterpret_cast<void**>(&res));

    for (size_t i = 0; i < out.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(res, static_cast<int32_t>(i))) = out[i];
    }
    return res;
}

// The INDEX overloads: the 2-arg (value, source-index) predicate ABI
// (InvokePredI4I4). The index is the element's 0-based position in the
// SOURCE array (net48 passes the source index, not a per-iteration counter).
Il2CppArray* il2cpp_linq_take_while_idx_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> out;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (!InvokePredI4I4(d, v, i)) break;
        out.push_back(v);
    }

    Il2CppArray* res = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(out.size()));
    roots.Add(reinterpret_cast<void**>(&res));

    for (size_t i = 0; i < out.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(res, static_cast<int32_t>(i))) = out[i];
    }
    return res;
}

Il2CppArray* il2cpp_linq_skip_while_idx_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> out;
    bool skipping = true;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (skipping)
        {
            if (InvokePredI4I4(d, v, i)) continue; // still inside the skip prefix
            skipping = false;                      // first failure: keep THIS element too
        }
        out.push_back(v);
    }

    Il2CppArray* res = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(out.size()));
    roots.Add(reinterpret_cast<void**>(&res));

    for (size_t i = 0; i < out.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(res, static_cast<int32_t>(i))) = out[i];
    }
    return res;
}

//------------------------------------------------------------------------------
// ToArray -- deep clone (element-agnostic; the array is self-describing).
// The source is rooted across the one allocation: a collect-then-retry
// allocation could otherwise move (or, if unrooted, reclaim) it before the
// payload copy (concat.cpp precedent).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_to_array(Il2CppArray* src)
{
    RequireArray(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(src->elemClass, src->elemSize, src->isRefElem, src->length);
    if (src->length > 0)
    {
        const size_t bytes = static_cast<size_t>(src->length) * static_cast<size_t>(src->elemSize);
        std::memcpy(out->data, src->data, bytes);
    }
    return out;
}

//------------------------------------------------------------------------------
// ToList -- materialize into a List<int> managed object (listi4 domain).
// GC safety: every add may grow the internal items array, i.e. ALLOCATE, and
// any such allocation can trigger a compaction that moves the source array
// AND the list object itself -- both are therefore rooted (RootScope) and
// re-read through their slots on every iteration. The element value is read
// through the rooted 'src' and passed BY VALUE to the add, so no managed
// pointer is held across an allocation inside the loop body.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_to_list(Il2CppArray* src)
{
    RequireArray(src);
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_i4_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        il2cpp_list_i4_add(list, I4At(src, i));
    }
    return list;
}

//------------------------------------------------------------------------------
// LINQ GROUPING domain (D-LINQ-4) -- Enumerable.GroupBy, int32 family.
//
// GC SAFETY: the key selector may allocate on every element, so a compaction
// can fire mid-loop. The delegate, the source array and the result object
// are all rooted (RootScope) and re-read through their slots after every
// callback. Managed pointers are NEVER cached across a callback or across an
// allocation:
//   * the result's 'groups' reference array is reached through the rooted
//     result each time (a collection can move both),
//   * a bucket is re-derived through the rooted result's groups array after
//     any allocation (bucket element-array growth),
//   * a FRESH bucket is published into the preallocated groups array with NO
//     allocation between its creation and the publication -- the groups array
//     is preallocated at the SOURCE LENGTH (the worst case: every element a
//     distinct key), so publishing a bucket never allocates and the unrooted
//     fresh bucket can never be moved/reclaimed before it becomes reachable
//     through the rooted result.
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_linq_grouping_class()
{
    EnsureGroupingClassesRegistered();
    return &g_groupingClass;
}

Il2CppClass* il2cpp_linq_grouping_result_class()
{
    EnsureGroupingClassesRegistered();
    return &g_groupingResultClass;
}

Il2CppObject* il2cpp_linq_group_by_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(keySel, "keySelector");
    RequireInt32Family(src);

    EnsureGroupingClassesRegistered();

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* result = il2cpp_object_new(&g_groupingResultClass);
    roots.Add(reinterpret_cast<void**>(&result));

    // Preallocate the groups array at the source length (worst case: every
    // element is a distinct key) so publishing a new bucket NEVER allocates
    // (see the header comment -- a fresh bucket stays unrooted until it is
    // published, so the publication path must be allocation-free).
    Il2CppArray* groups = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    Il2CppLinqGroupingResult* res = AsGroupingResult(result);
    res->groups = groups;
    il2cpp_write_barrier(reinterpret_cast<void**>(&res->groups),
                         reinterpret_cast<Il2CppObject*>(groups));
    res->count = 0;

    // The selector runs EXACTLY ONCE per source element (net48 GroupBy caches
    // each element's key on first enumeration -- a second pass would re-run a
    // side-effecting selector).
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const int32_t key = InvokeSelI4I4(d, v); // may allocate -> collect

        // Re-derive everything through the rooted slots after the callback.
        res = AsGroupingResult(result);
        const int32_t n = res->count;
        Il2CppObject** groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);

        // First-appearance bucket lookup: linear scan over the distinct keys
        // seen so far (int32 equality -- net48 EqualityComparer<int>.Default).
        int32_t found = -1;
        for (int32_t j = 0; j < n; ++j)
        {
            if (AsGrouping(groupSlots[j])->key == key)
            {
                found = j;
                break;
            }
        }

        if (found >= 0)
        {
            // Append v to the bucket's element array. The growth allocation
            // may collect, so the bucket is re-derived through the rooted
            // result's groups array afterwards (the bucket itself stays
            // reachable through the rooted result the whole time -- it needs
            // no root of its own).
            Il2CppLinqGrouping* grp = AsGrouping(groupSlots[found]);
            if (grp->count == grp->elements->length)
            {
                GrowGroupElements(&result, found);
            }
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                AsGroupingResult(result)->groups->data + found * sizeof(void*)));
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, grp->count)) = v;
            ++grp->count;
        }
        else
        {
            // New bucket: publish it into the preallocated groups slot with
            // NO allocation between creation and publication (the fresh
            // object's address stays valid), then attach the element array
            // (that allocation may collect -- by then the bucket is reachable
            // through the rooted result, so it survives).
            Il2CppObject* ng = il2cpp_object_new(&g_groupingClass);
            Il2CppLinqGrouping* grp = AsGrouping(ng);
            grp->key = key;
            grp->elements = nullptr;
            grp->count = 0;
            res = AsGroupingResult(result);
            groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);
            groupSlots[res->count] = ng;
            ++res->count;

            Il2CppArray* elems = il2cpp_array_new(
                nullptr, sizeof(int32_t), false, kMinGroupItems);
            res = AsGroupingResult(result); // re-derive after the (possibly collecting) allocation
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                res->groups->data + (res->count - 1) * sizeof(void*)));
            grp->elements = elems;
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->elements),
                                 reinterpret_cast<Il2CppObject*>(elems));
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, 0)) = v;
            grp->count = 1;
        }
    }
    return result;
}

int32_t il2cpp_linq_grouping_sum(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    Il2CppLinqGrouping* grp = AsGrouping(grouping);
    // Checked int32 addition (net48 Enumerable.Sum, D-LINQ-3); an empty
    // bucket takes no additions -> 0.
    int32_t sum = 0;
    for (int32_t i = 0; i < grp->count; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(grp->elements, i));
        if ((elem > 0 && sum > INT32_MAX - elem) ||
            (elem < 0 && sum < INT32_MIN - elem))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += elem;
    }
    return sum;
}

int32_t il2cpp_linq_grouping_result_get_count(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    return AsGroupingResult(result)->count;
}

Il2CppObject* il2cpp_linq_grouping_result_get_grouping(Il2CppObject* result, int32_t index)
{
    ThrowIfNullReceiver(result);
    Il2CppLinqGroupingResult* res = AsGroupingResult(result);
    if (index < 0 || index >= res->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
        return nullptr; // unreachable (the factory throws)
    }
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(res->groups, index));
}

int32_t il2cpp_linq_grouping_get_key(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->key;
}

int32_t il2cpp_linq_grouping_get_count(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->count;
}

Il2CppArray* il2cpp_linq_grouping_get_elements(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    // The bucket's internal element array is a GROWABLE backing store (its
    // length is a capacity >= count), so the public surface returns an
    // EXACT-SIZE copy (listi4 to_array contract). The allocation may collect
    // and compact, so the grouping is rooted and re-derived afterwards; the
    // copy is a pure int32 value pass with no allocation inside.
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&grouping));
    Il2CppLinqGrouping* grp = AsGrouping(grouping);
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(int32_t), false, grp->count);
    grp = AsGrouping(grouping); // re-derive after the (possibly collecting) allocation
    for (int32_t i = 0; i < grp->count; ++i)
    {
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, i));
    }
    return out;
}

//------------------------------------------------------------------------------
// SEQUENCE-COMBINATION / SET-FAMILY OPERATORS (D-LINQZ-1) -- int32 family,
// eager materialization into a FRESH int32[] (the stage-1 AOT form).
// net48 semantics (probe-pinned by the linqzip e2e case): Concat keeps order
// AND duplicates; Distinct removes duplicates keeping FIRST-APPEARANCE order;
// Except yields the first-appearance-order, deduplicated difference; Zip pairs
// element-wise to the SHORTER length with a two-arg selector invoked exactly
// once per output element.
//------------------------------------------------------------------------------

// Concat -- the concatenation of the two int32 arrays (net48 Concat: order
// preserved, duplicates kept; length = sum of the parts; an empty operand
// contributes nothing). No callback, so the ONLY managed allocation is the
// result: both sources are rooted across it (a collect-then-retry allocation
// could otherwise move -- or, if unrooted, reclaim -- them before the payload
// copy) and re-read through the rooted slots afterwards. The copy is a pure
// int32 value pass with no allocation inside, so no collection can interleave.
Il2CppArray* il2cpp_linq_concat_i4(Il2CppArray* first, Il2CppArray* second)
{
    RequireArray(first);
    RequireArray(second);
    RequireInt32Family(first);
    RequireInt32Family(second);

    // A result length is an int32; a length sum that overflows int32 cannot
    // be materialized (net48 Concat materializes firstCount + secondCount --
    // checked, an overflow is an OverflowException).
    const int64_t total = static_cast<int64_t>(first->length) + second->length;
    if (total > INT32_MAX)
    {
        il2cpp_throw_overflow_exception("The concatenated array length exceeds Int32.MaxValue.");
        return nullptr; // unreachable (the factory throws)
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(total));
    roots.Add(reinterpret_cast<void**>(&out));

    const int32_t n1 = first->length;
    const int32_t n2 = second->length;
    int32_t* dst = reinterpret_cast<int32_t*>(out->data);
    for (int32_t i = 0; i < n1; ++i) dst[i] = I4At(first, i);
    for (int32_t i = 0; i < n2; ++i) dst[n1 + i] = I4At(second, i);
    return out;
}

// Distinct -- the source with duplicates removed, keeping FIRST-APPEARANCE
// order (net48 default ordinal int32 equality). No callback: the distinct
// accumulator lives on the NATIVE heap (std::vector), so the whole scan is
// allocation-free on the managed side -- only the result allocation below can
// collect, and the source is rooted across it (to_array discipline).
Il2CppArray* il2cpp_linq_distinct_i4(Il2CppArray* src)
{
    RequireArray(src);
    RequireInt32Family(src);

    std::vector<int32_t> seen;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (std::find(seen.begin(), seen.end(), v) == seen.end())
        {
            seen.push_back(v);
        }
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(seen.size()));
    roots.Add(reinterpret_cast<void**>(&out));

    for (size_t i = 0; i < seen.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(out, static_cast<int32_t>(i))) = seen[i];
    }
    return out;
}

// Except -- the elements of first NOT present in second (set membership --
// duplicates in second are irrelevant), first-appearance order, DEDUPLICATED
// output (net48). The output accumulator is native-heap (no managed allocation
// in the scan, so no collection can fire mid-scan); the result allocation at
// the end is the only managed allocation, with both sources rooted across it.
Il2CppArray* il2cpp_linq_except_i4(Il2CppArray* first, Il2CppArray* second)
{
    RequireArray(first);
    RequireArray(second);
    RequireInt32Family(first);
    RequireInt32Family(second);

    std::vector<int32_t> out;
    for (int32_t i = 0; i < first->length; ++i)
    {
        const int32_t v = I4At(first, i);
        bool inSecond = false;
        for (int32_t j = 0; j < second->length; ++j)
        {
            if (I4At(second, j) == v)
            {
                inSecond = true;
                break;
            }
        }
        if (inSecond) continue;
        if (std::find(out.begin(), out.end(), v) != out.end()) continue;
        out.push_back(v);
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    Il2CppArray* res = il2cpp_array_new(nullptr, 4, false, static_cast<int32_t>(out.size()));
    roots.Add(reinterpret_cast<void**>(&res));

    for (size_t i = 0; i < out.size(); ++i)
    {
        *reinterpret_cast<int32_t*>(
            il2cpp_array_get_elem_ptr(res, static_cast<int32_t>(i))) = out[i];
    }
    return res;
}

// Zip -- element-wise pairing to the SHORTER length (net48): the selector
// runs EXACTLY ONCE per output element, an empty operand yields an empty
// result with NO selector invocation. The selector may allocate on every
// element -> a collection can move the delegate AND both arrays AND the
// result; all four are rooted (RootScope) and re-read through their rooted
// slots every iteration, and the result slot is computed only AFTER the
// callback returns (select_i4_i4 discipline -- no allocation separates the
// address computation from the store).
Il2CppArray* il2cpp_linq_zip_i4(Il2CppArray* first, Il2CppArray* second,
                                Il2CppObject* sel)
{
    RequireArray(first);
    RequireArray(second);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(first);
    RequireInt32Family(second);

    const int32_t n = first->length < second->length ? first->length : second->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, n);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t a = I4At(first, i);
        const int32_t b = I4At(second, i);
        const int32_t r = InvokeZipI4I4(d, a, b);
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

} // namespace il2cpp
