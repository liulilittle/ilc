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
#include "linq.h"              // int32 array kernels reused by the grouping wrappers

#include "arraysort.h"            // FindVirtualSlotByNameAndSignature (user comparers)
#include "genericcomparer_obj.h"  // the default EqualityComparer/Comparer singletons
#include "il2cpp_boxing_ext.h"    // icall_box_double
#include "il2cpp_boxing_ext2.h"   // icall_box_long
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "listi4.h"
#include "listobj.h"             // GroupBy-result ToList (List<object> carrier)
#include "sortedlist.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <new>
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

// P0LINQ1 (D1/D2): the non-int32 value families. Each entry accepts ONLY its
// own cell width (the array is self-describing via isRefElem/elemSize), so a
// mis-dispatched call is rejected instead of reading a narrower cell as a
// wider one.
void RequireI8Family(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 8)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Int64 arrays are supported.");
    }
}

void RequireDoubleFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 8)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Double arrays are supported.");
    }
}

void RequireFloatFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 4)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Single arrays are supported.");
    }
}

// decimal[] cells are 16 bytes (CodeGenerator ArrayElementType: System.Decimal
// -> (16, false)).
void RequireDecimalFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Decimal arrays are supported.");
    }
}

// RequireCallback -- validate the callback delegate (LINQ callback
// contract, linq-domain.md section 2): null -> ArgumentNullException; a multicast
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
    RootScope() {}
    ~RootScope()
    {
        for (size_t i = m_slots.size(); i > 0; --i)
            il2cpp_gc_remove_static_root(m_slots[i - 1]);
    }
    void Add(void** slot)
    {
        // The slot table grows on demand (the GroupBy result-selector path
        // roots 6); a fixed table with an std::abort() overflow check would
        // turn a rooting-count mistake into a silent process exit.
        try
        {
            m_slots.push_back(slot);
        }
        catch (const std::bad_alloc&)
        {
            il2cpp_throw_new(
                "System.OutOfMemoryException,System.OutOfMemoryException,System.SystemException,System.Exception",
                "Insufficient memory to continue the execution of the program.");
        }
        il2cpp_gc_add_static_root(slot);
    }

private:
    std::vector<void**> m_slots; // the GroupBy result-selector path roots 6
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

// The element-selector ABIs (Func<TSource,TElement>): int64 / double results.
typedef int64_t (*SelI4I8Fn)(int32_t);
typedef int64_t (*SelI4I8ThisFn)(Il2CppObject*, int32_t);
typedef double (*SelI4DFn)(int32_t);
typedef double (*SelI4DThisFn)(Il2CppObject*, int32_t);

int64_t InvokeSelI4I8(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4I8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4I8Fn>(d->methodPtr)(v);
}

double InvokeSelI4D(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4DThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4DFn>(d->methodPtr)(v);
}

// nb9 fix P0-1: a FLOAT-returning selector (Func<TSource,float>). The float
// comes back in a 32-bit register, so it must be read through a float-return
// pointer -- reading it as a double (the old behaviour) took the upper 32 bits
// as garbage.
typedef float (*SelI4FFn)(int32_t);
typedef float (*SelI4FThisFn)(Il2CppObject*, int32_t);

float InvokeSelI4F(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4FThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4FFn>(d->methodPtr)(v);
}

// The INDEX-aware 2-arg selector family (Func<TSource,int,TResult>): the same
// per-binding dispatch, with a reference / int64 / double result.
typedef Il2CppObject* (*ZipI4RefFn)(int32_t, int32_t);
typedef Il2CppObject* (*ZipI4RefThisFn)(Il2CppObject*, int32_t, int32_t);
typedef int64_t (*ZipI4I8Fn)(int32_t, int32_t);
typedef int64_t (*ZipI4I8ThisFn)(Il2CppObject*, int32_t, int32_t);
typedef double (*ZipI4DFn)(int32_t, int32_t);
typedef double (*ZipI4DThisFn)(Il2CppObject*, int32_t, int32_t);
// nb9 fix P0-1: the float-returning indexed selector (Func<T,int,float>).
typedef float (*ZipI4FFn)(int32_t, int32_t);
typedef float (*ZipI4FThisFn)(Il2CppObject*, int32_t, int32_t);

float InvokeZipI4F(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<ZipI4FThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ZipI4FFn>(d->methodPtr)(a, b);
}

Il2CppObject* InvokeZipI4Ref(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<ZipI4RefThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ZipI4RefFn>(d->methodPtr)(a, b);
}

int64_t InvokeZipI4I8(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<ZipI4I8ThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ZipI4I8Fn>(d->methodPtr)(a, b);
}

double InvokeZipI4D(Il2CppDelegate* d, int32_t a, int32_t b)
{
    return d->target != nullptr
        ? reinterpret_cast<ZipI4DThisFn>(d->methodPtr)(d->target, a, b)
        : reinterpret_cast<ZipI4DFn>(d->methodPtr)(a, b);
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
// nb9 FIX: family-aware. The bucket's element array is EITHER an int32 value
// array (the int32 element family) OR a REFERENCE array (the reference / boxed
// int64 / boxed double element families). The array self-describes
// (isRefElem/elemSize), so the copy is chosen from the OLD array -- a
// reference copy publishes every moved slot through a write barrier.
void GrowGroupElements(Il2CppObject** resultSlot, int32_t bucketIndex)
{
    Il2CppLinqGroupingResult* res = AsGroupingResult(*resultSlot);
    Il2CppLinqGrouping* grp = AsGrouping(
        *reinterpret_cast<Il2CppObject**>(res->groups->data + bucketIndex * sizeof(void*)));
    const int32_t oldLength = grp->elements->length;
    const bool isRef = grp->elements->isRefElem;
    const int32_t newLength = GroupNextLength(oldLength);
    if (newLength == oldLength)
    {
        return; // at the 2^30 cap
    }
    Il2CppArray* newElems = il2cpp_array_new(
        nullptr, isRef ? static_cast<int32_t>(sizeof(void*)) : static_cast<int32_t>(sizeof(int32_t)),
        isRef, newLength);
    res = AsGroupingResult(*resultSlot); // re-derive after the (possibly collecting) allocation
    grp = AsGrouping(
        *reinterpret_cast<Il2CppObject**>(res->groups->data + bucketIndex * sizeof(void*))); // re-derive the bucket
    const int32_t n = grp->count;
    if (isRef)
    {
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newElems->data);
        Il2CppObject** srcRefs = reinterpret_cast<Il2CppObject**>(grp->elements->data);
        for (int32_t i = 0; i < n; ++i)
        {
            dst[i] = srcRefs[i];
            il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
        }
    }
    else
    {
        int32_t* dst = reinterpret_cast<int32_t*>(newElems->data);
        const int32_t* src = reinterpret_cast<const int32_t*>(grp->elements->data);
        for (int32_t i = 0; i < n; ++i)
        {
            dst[i] = src[i];
        }
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

Il2CppArray* il2cpp_linq_where_idx_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // SINGLE predicate pass over the index-aware callback (net48
    // Func<TSource,int,bool>); the index is the element's position in the
    // SOURCE sequence (never the output position).
    Il2CppArray* scratch = il2cpp_array_new(nullptr, 4, false, src->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        if (InvokePredI4I4(d, v, i))
        {
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(scratch, w)) = v;
            ++w;
        }
    }
    if (w == src->length)
    {
        return scratch;
    }
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
            // nb9 fix M4: reference-array store needs a write barrier.
            Il2CppObject* kept = RefAt(src, i);
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), kept);
            *slot = kept;
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
        // nb9 fix M4: reference-array store needs a write barrier.
        Il2CppObject* moved = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(scratch, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(dst), moved);
        *dst = moved;
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

Il2CppArray* il2cpp_linq_select_idx_i4_i4(Il2CppArray* src, Il2CppObject* sel)
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
        // Index-aware selector (net48 Func<TSource,int,TResult>): the index is
        // the element's position in the SOURCE sequence. The callback runs
        // FIRST; the result slot is computed afterwards through the rooted
        // 'out' (a cached slot address would be stale after a collecting
        // callback).
        const int32_t r = InvokeZipI4I4(d, I4At(src, i), i);
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
        // nb9 fix M4: reference-array store needs a write barrier.
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
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
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
        *slot = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// nb9 FIX (M19) -- the remaining Select result families.
//
// net48 Select is generic in TResult, so the transpiler must dispatch on
// TResult: int32 -> int32[], a reference -> a reference array, int64/double ->
// their result arrays. The index overloads (Func<TSource,int,TResult>) run the
// selector once per element with the SOURCE index. The old mapping sent every
// non-int32 result through the int32 entry: the result array was int32[] and
// the foreach's ref/i8 carrier read the wrong layout (access violation).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_i4_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(int64_t)), 2, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t r = InvokeSelI4I8(d, I4At(src, i));
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_i4_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(double)), 4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const double r = InvokeSelI4D(d, I4At(src, i));
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_ref(Il2CppArray* src, Il2CppObject* sel)
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
        Il2CppObject* r = InvokeZipI4Ref(d, I4At(src, i), i);
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
        *slot = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(int64_t)), 2, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t r = InvokeZipI4I8(d, I4At(src, i), i);
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(double)), 4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const double r = InvokeZipI4D(d, I4At(src, i), i);
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = r;
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

//------------------------------------------------------------------------------
// Last(predicate) over a reference source (see linq_map.h): walk BACKWARD and
// return the LAST matching element (net48 short-circuits from the end). The
// predicate may allocate, so both the delegate and the source are rooted and
// re-read through their slots on every iteration; the element is passed by
// value (an Il2CppObject* slot copy), so no managed address is held across the
// callback.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_last_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // net48 4.8 Enumerable.Last(predicate) has NO TryGetLast reverse
    // optimization: it walks the source FORWARD, keeps the last match and never
    // short-circuits. A reverse walk would visit the predicate a different
    // number of times (and in a different order) -- observable with a
    // side-effecting predicate.
    bool found = false;
    Il2CppObject* last = nullptr;
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem))
        {
            last = elem;
            found = true;
        }
    }
    if (!found)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    }
    return last;
}

Il2CppObject* il2cpp_linq_last_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // Forward full walk (see last_pred_ref): the predicate runs once per
    // element even after a match.
    Il2CppObject* last = nullptr;
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem)) last = elem;
    }
    return last;
}

//------------------------------------------------------------------------------
// Last(predicate) / LastOrDefault(predicate) over the int32 family. Same
// forward-walk contract as the reference entries above (net48 has no reverse
// optimization for the predicate form).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_last_pred_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    bool found = false;
    int32_t last = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem))
        {
            last = elem;
            found = true;
        }
    }
    if (!found)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    }
    return last;
}

int32_t il2cpp_linq_last_or_default_pred_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t last = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem)) last = elem;
    }
    return last;
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
    // A3/B3: propagate the source element kind (the same clone contract as
    // il2cpp_linq_to_array_family -- int64/double formatting and the
    // element-kind Array scans depend on it).
    out->elemKind = src->elemKind;
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
// P0LINQ1 (D1) -- ToList over the non-int32 element families.
//
// net48's Enumerable.ToList<TSource> is generic in TSource, so the transpiler
// dispatches on the element family; the runtime carries each family through
// the listobj domain (the SAME List<T> object the reference/value List
// surface already uses, so Count/indexer/foreach keep resolving downstream).
//
// GC SAFETY (identical discipline to il2cpp_linq_to_list above): every add may
// grow the internal items array, i.e. ALLOCATE, and such an allocation can
// compact the heap -- the source array AND the list object are therefore
// rooted and re-read through their slots. The element value is read through
// the rooted source and passed BY VALUE (or, for a reference element, the
// reference itself is handed to the add, which roots it internally before its
// own allocation), so no managed pointer is held across an allocation.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_to_list_ref(Il2CppArray* src)
{
    RequireArray(src);
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        il2cpp_list_obj_add(list, RefAt(src, i));
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_i8(Il2CppArray* src)
{
    RequireArray(src);
    RequireI8Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        il2cpp_list_obj_add_long(list, elem);
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_d(Il2CppArray* src)
{
    RequireArray(src);
    RequireDoubleFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const double elem = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        il2cpp_list_obj_add_double(list, elem);
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_f(Il2CppArray* src)
{
    RequireArray(src);
    RequireFloatFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const float elem = *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(src, i));
        il2cpp_list_obj_add_float(list, static_cast<double>(elem));
    }
    return list;
}

//------------------------------------------------------------------------------
// p0linq4 (D1) -- ToList over the remaining value families.
//
// Same discipline as the entries above: the source and the growing list are
// rooted across every add (each add may grow the items array, i.e. ALLOCATE,
// and a compaction can move both), and the element is read through the rooted
// source before the add.
//
// The listobj storage is a reference array, so the element crosses as a BOX:
// decimal through icall_box_decimal (16-byte payload), DateTime/TimeSpan
// through the Int64 box carrying the SAME image a managed array cell holds,
// and a user struct through icall_box_user_struct with its own descriptor.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_to_list_dec(Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        runtime::Il2CppDecimal elem;
        std::memcpy(&elem, il2cpp_array_get_elem_ptr(src, i), sizeof(elem));
        il2cpp_list_obj_add_dec(list, &elem);
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_dt(Il2CppArray* src)
{
    RequireArray(src);
    RequireI8Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t bits = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        il2cpp_list_obj_add_dt(list, runtime::il2cpp_datetime_from_array_bits(bits));
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_ts(Il2CppArray* src)
{
    RequireArray(src);
    RequireI8Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t ticks = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        il2cpp_list_obj_add_ts(list, runtime::Il2CppTimeSpan(ticks));
    }
    return list;
}

Il2CppObject* il2cpp_linq_to_list_struct(Il2CppArray* src, Il2CppClass* klass,
                                         int32_t payloadSize)
{
    RequireArray(src);
    if (src->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "ToList over a user value type needs a value-element source array.");
    }
    if (klass == nullptr || payloadSize <= 0)
    {
        il2cpp_throw_argument_null_exception("klass");
    }
    if (src->elemSize != payloadSize)
    {
        il2cpp_throw_not_supported_exception(
            "ToList over a user value type: the source element stride does not match the struct size.");
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const void* elem = il2cpp_array_get_elem_ptr(src, i);
        il2cpp_list_obj_add_struct(list, elem, klass, static_cast<size_t>(payloadSize));
    }
    return list;
}

//------------------------------------------------------------------------------
// P0LINQ1 (D2) -- the SOURCE-ONLY decimal aggregates.
//
// decimal is a 16-byte value type (decimal.h), so every cell is read as the
// Il2CppDecimal image and the arithmetic goes through the decimal kernel
// (icall_decimal_add / icall_decimal_div / icall_decimal_compare -- the
// line-by-line DecCalc port). net48 semantics, probe-verified against the
// net48 oracle:
//   * Sum     -- repeated Decimal.Add; overflow -> OverflowException; an empty
//                source -> 0 (no addition runs).
//   * Average -- Sum / count with DECIMAL division (the net48 overload returns
//                decimal: {1.5,2.5} -> 2.0, not 2); empty ->
//                InvalidOperationException.
//   * Min/Max -- numeric comparison; the WINNING OPERAND is copied unchanged,
//                so its scale survives ({1.50,1.5} -> both 1.50); empty ->
//                InvalidOperationException.
// All four take the result slot FIRST (the decimal T3 contract -- a 16-byte
// value does not fit the return register). No callback and no allocation, so
// no GC root is needed: the source is read through the caller's rooted slot.
//------------------------------------------------------------------------------
namespace {

// The element cell of a decimal[] (bounds-checked element pointer).
const runtime::Il2CppDecimal* DecAt(Il2CppArray* arr, int32_t index)
{
    return reinterpret_cast<const runtime::Il2CppDecimal*>(il2cpp_array_get_elem_ptr(arr, index));
}

} // namespace

void il2cpp_linq_sum_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);

    // 0m has flags 0 (sign 0, scale 0, mantissa 0) -- the net48 decimal zero.
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        // The kernel loads every input before writing the result slot, so
        // result aliasing the accumulator is safe (the element lives in the
        // source array and never aliases it).
        runtime::icall_decimal_add(result, result, DecAt(src, i));
    }
}

void il2cpp_linq_min_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    // Seed from the FIRST element (never a sentinel) and keep the WINNING
    // OPERAND unchanged -- net48's Min returns the operand, scale included.
    std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal* elem = DecAt(src, i);
        if (runtime::icall_decimal_compare(elem, result) < 0)
        {
            std::memcpy(result, elem, sizeof(runtime::Il2CppDecimal));
        }
    }
}

void il2cpp_linq_max_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal* elem = DecAt(src, i);
        if (runtime::icall_decimal_compare(elem, result) > 0)
        {
            std::memcpy(result, elem, sizeof(runtime::Il2CppDecimal));
        }
    }
}

void il2cpp_linq_average_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        runtime::icall_decimal_add(result, result, DecAt(src, i));
    }
    // decimal / int32: the divisor is widened to decimal first (net48's
    // Average does exactly this through Decimal.Divide(sum, count)).
    runtime::Il2CppDecimal count;
    std::memset(&count, 0, sizeof(runtime::Il2CppDecimal));
    runtime::icall_decimal_ctor_int32(&count, src->length);
    runtime::icall_decimal_div(result, result, &count);
}

//------------------------------------------------------------------------------
// P0LINQ1 (D3) -- Select with TResult = System.Decimal.
//
// net48's Select is generic in TResult; decimal is a 16-byte value type, and
// the generated decimal-returning method RETURNS the struct BY VALUE
// (ParamTypesCpp / ReturnTypeCpp map System.Decimal to
// il2cpp::runtime::Il2CppDecimal as a value return -- never a hidden result
// pointer), so the callback signature is:
//   Il2CppDecimal (*)(TSource value)                    (static leaf)
//   Il2CppDecimal (*)(Il2CppObject* this, TSource value) (bound leaf)
//
// The SOURCE side may be any value family the transpiler can read: int32
// (_i4), int64 (_i8), double (_d), float (_f, widened) or decimal (_dec,
// 16-byte cell). Each entry validates its own source family.
//
// GC SAFETY: the selector may allocate, so the delegate and the source/result
// arrays are rooted; the result cell address is computed AFTER the callback
// returns (never cached across it).
//------------------------------------------------------------------------------
namespace {

typedef runtime::Il2CppDecimal (*SelToDecFn)(int32_t v);
typedef runtime::Il2CppDecimal (*SelToDecThisFn)(Il2CppObject* self, int32_t v);
typedef runtime::Il2CppDecimal (*SelI8ToDecFn)(int64_t v);
typedef runtime::Il2CppDecimal (*SelI8ToDecThisFn)(Il2CppObject* self, int64_t v);
typedef runtime::Il2CppDecimal (*SelDToDecFn)(double v);
typedef runtime::Il2CppDecimal (*SelDToDecThisFn)(Il2CppObject* self, double v);
typedef runtime::Il2CppDecimal (*SelDecToDecFn)(runtime::Il2CppDecimal v);
typedef runtime::Il2CppDecimal (*SelDecToDecThisFn)(Il2CppObject* self, runtime::Il2CppDecimal v);

// One shared body: allocate the 16-byte-cell result array, then walk the
// source at 'srcKind' and write each selector result into the array.
//   srcKind: 0 int32 | 1 int64 | 2 double | 3 float | 4 decimal
Il2CppArray* SelectToDecimal(Il2CppArray* src, Il2CppObject* sel, int32_t srcKind)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    switch (srcKind)
    {
        case 0: RequireInt32Family(src); break;
        case 1: RequireI8Family(src); break;
        case 2: RequireDoubleFamily(src); break;
        case 3: RequireFloatFamily(src); break;
        default: RequireDecimalFamily(src); break;
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        runtime::Il2CppDecimal slot;
        switch (srcKind)
        {
            case 1:
            {
                const int64_t v = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
                slot = d->target != nullptr
                    ? reinterpret_cast<SelI8ToDecThisFn>(d->methodPtr)(d->target, v)
                    : reinterpret_cast<SelI8ToDecFn>(d->methodPtr)(v);
                break;
            }
            case 2:
            {
                const double v = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
                slot = d->target != nullptr
                    ? reinterpret_cast<SelDToDecThisFn>(d->methodPtr)(d->target, v)
                    : reinterpret_cast<SelDToDecFn>(d->methodPtr)(v);
                break;
            }
            case 3:
            {
                const double v = static_cast<double>(
                    *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(src, i)));
                slot = d->target != nullptr
                    ? reinterpret_cast<SelDToDecThisFn>(d->methodPtr)(d->target, v)
                    : reinterpret_cast<SelDToDecFn>(d->methodPtr)(v);
                break;
            }
            case 4:
            {
                const runtime::Il2CppDecimal v = *DecAt(src, i);
                slot = d->target != nullptr
                    ? reinterpret_cast<SelDecToDecThisFn>(d->methodPtr)(d->target, v)
                    : reinterpret_cast<SelDecToDecFn>(d->methodPtr)(v);
                break;
            }
            default:
            {
                const int32_t v = I4At(src, i);
                slot = d->target != nullptr
                    ? reinterpret_cast<SelToDecThisFn>(d->methodPtr)(d->target, v)
                    : reinterpret_cast<SelToDecFn>(d->methodPtr)(v);
                break;
            }
        }
        // The slot address is derived from the ROOTED result array only AFTER
        // the callback returned (a compaction inside the callback can move it).
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot,
                    sizeof(runtime::Il2CppDecimal));
    }
    return out;
}

} // namespace

Il2CppArray* il2cpp_linq_select_i4_dec(Il2CppArray* src, Il2CppObject* sel)
{
    return SelectToDecimal(src, sel, 0);
}

Il2CppArray* il2cpp_linq_select_i8_dec(Il2CppArray* src, Il2CppObject* sel)
{
    return SelectToDecimal(src, sel, 1);
}

Il2CppArray* il2cpp_linq_select_d_dec(Il2CppArray* src, Il2CppObject* sel)
{
    return SelectToDecimal(src, sel, 2);
}

Il2CppArray* il2cpp_linq_select_f_dec(Il2CppArray* src, Il2CppObject* sel)
{
    return SelectToDecimal(src, sel, 3);
}

Il2CppArray* il2cpp_linq_select_dec_dec(Il2CppArray* src, Il2CppObject* sel)
{
    return SelectToDecimal(src, sel, 4);
}

//------------------------------------------------------------------------------
// p0linq4 (D2) -- Select with an (element, index) selector returning decimal.
//
// Same 16-byte-cell result array and the same BY-VALUE decimal return ABI as
// SelectToDecimal (the generated decimal-returning method returns the struct
// by value), with the element's 0-based SOURCE index as the second argument:
//   Il2CppDecimal (*)(int32_t value, int32_t index)
//   Il2CppDecimal (*)(Il2CppObject* self, int32_t value, int32_t index)
//
// GC SAFETY: identical to SelectToDecimal -- the delegate and the source and
// result arrays are rooted; the result cell address is computed AFTER the
// callback returns (never cached across it).
//------------------------------------------------------------------------------
namespace {

typedef runtime::Il2CppDecimal (*SelIdxToDecFn)(int32_t v, int32_t index);
typedef runtime::Il2CppDecimal (*SelIdxToDecThisFn)(Il2CppObject* self, int32_t v,
                                                    int32_t index);

} // namespace

Il2CppArray* il2cpp_linq_select_idx_i4_dec(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const runtime::Il2CppDecimal slot = d->target != nullptr
            ? reinterpret_cast<SelIdxToDecThisFn>(d->methodPtr)(d->target, v, i)
            : reinterpret_cast<SelIdxToDecFn>(d->methodPtr)(v, i);
        // The slot address is derived from the ROOTED result array only AFTER
        // the callback returned (a compaction inside the callback can move it).
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot,
                    sizeof(runtime::Il2CppDecimal));
    }
    return out;
}

//------------------------------------------------------------------------------
// p0linq4 (D3) -- Select over a REFERENCE source returning decimal.
//
// The element crosses as Il2CppObject* (the ref-family ABI), the result is a
// 16-byte decimal cell written through the rooted result array:
//   Il2CppDecimal (*)(Il2CppObject* elem)
//   Il2CppDecimal (*)(Il2CppObject* self, Il2CppObject* elem)
//
// GC SAFETY: the delegate, the source and the result array are rooted; the
// element is read through the rooted source each iteration, and the result
// cell address is computed only after the callback returns.
//------------------------------------------------------------------------------
namespace {

typedef runtime::Il2CppDecimal (*SelRefToDecFn)(Il2CppObject* elem);
typedef runtime::Il2CppDecimal (*SelRefToDecThisFn)(Il2CppObject* self, Il2CppObject* elem);

} // namespace

Il2CppArray* il2cpp_linq_select_ref_dec(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const runtime::Il2CppDecimal slot = d->target != nullptr
            ? reinterpret_cast<SelRefToDecThisFn>(d->methodPtr)(d->target, elem)
            : reinterpret_cast<SelRefToDecFn>(d->methodPtr)(elem);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot,
                    sizeof(runtime::Il2CppDecimal));
    }
    return out;
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
            // nb9 (M13): the groups array is preallocated at source length and
            // can be promoted to Gen1 / allocated in LOH, so publishing a
            // young bucket needs a card-marking barrier (the same contract the
            // grouping enumerators already use).
            il2cpp_write_barrier(reinterpret_cast<void**>(&groupSlots[res->count]), ng);
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

//------------------------------------------------------------------------------
// il2cpp_linq_group_by_elem_i4 -- GroupBy(source, keySelector,
// elementSelector) (net48 Func<TSource,TKey> + Func<TSource,TElement>).
// Identical bucketing to il2cpp_linq_group_by_i4, except that each element is
// projected through elemSel BEFORE being appended to its bucket. Both
// selectors run EXACTLY ONCE per source element (net48 GroupBy semantics).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_elem_i4(Il2CppArray* src, Il2CppObject* keySel, Il2CppObject* elemSel)
{
    RequireArray(src);
    Il2CppDelegate* kd = RequireCallback(keySel, "keySelector");
    Il2CppDelegate* ed = RequireCallback(elemSel, "elementSelector");
    RequireInt32Family(src);

    EnsureGroupingClassesRegistered();

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&kd));
    roots.Add(reinterpret_cast<void**>(&ed));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* result = il2cpp_object_new(&g_groupingResultClass);
    roots.Add(reinterpret_cast<void**>(&result));

    Il2CppArray* groups = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    Il2CppLinqGroupingResult* res = AsGroupingResult(result);
    res->groups = groups;
    il2cpp_write_barrier(reinterpret_cast<void**>(&res->groups),
                         reinterpret_cast<Il2CppObject*>(groups));
    res->count = 0;

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const int32_t key = InvokeSelI4I4(kd, v);   // may allocate -> collect
        const int32_t elem = InvokeSelI4I4(ed, v);  // may allocate -> collect

        // Re-derive everything through the rooted slots after the callbacks.
        res = AsGroupingResult(result);
        const int32_t n = res->count;
        Il2CppObject** groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);

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
            Il2CppLinqGrouping* grp = AsGrouping(groupSlots[found]);
            if (grp->count == grp->elements->length)
            {
                GrowGroupElements(&result, found);
            }
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                AsGroupingResult(result)->groups->data + found * sizeof(void*)));
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, grp->count)) = elem;
            ++grp->count;
        }
        else
        {
            Il2CppObject* ng = il2cpp_object_new(&g_groupingClass);
            Il2CppLinqGrouping* grp = AsGrouping(ng);
            grp->key = key;
            grp->elements = nullptr;
            grp->count = 0;
            res = AsGroupingResult(result);
            groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);
            // nb9 (M13): the groups array is preallocated at source length and
            // can be promoted to Gen1 / allocated in LOH, so publishing a
            // young bucket needs a card-marking barrier (the same contract the
            // grouping enumerators already use).
            il2cpp_write_barrier(reinterpret_cast<void**>(&groupSlots[res->count]), ng);
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
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, 0)) = elem;
            grp->count = 1;
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- the generalized GroupBy core.
//
// One bucketing pass shared by every remaining overload:
//   * the KEY selector always runs once per source element (int32 key);
//   * the ELEMENT selector (when present) runs once per source element, in
//     net48 order (key first);
//   * the bucket lookup compares the key through the supplied
//     IEqualityComparer<int> (nullptr = int32 equality).
//
// The element family is a compile-time decision of the caller (the TElement
// metadata byte) and only affects the STORAGE of a bucket:
//   Int32 -> an int32[] value array (no boxing)
//   Ref   -> a reference array holding the selector's Il2CppObject*
//   I8/D  -> a reference array holding BOXED int64/double values
// Readers (get_elements / get_enumerator) dispatch on the array's own
// isRefElem flag, so every family round-trips through the same IGrouping.
//------------------------------------------------------------------------------
namespace {

enum class GroupElemKind
{
    Int32 = 0,
    Ref = 1,
    I8 = 2,
    D = 3,
};


// The 2-argument result selector Func<TKey,IEnumerable<TElement>,TResult>:
// the key is int32, the second argument is a REFERENCE (the bucket's element
// array -- an IEnumerable<TElement> carrier with the grouping's contents).
typedef int32_t (*SelKeyGroupI4Fn)(int32_t, Il2CppObject*);
typedef int32_t (*SelKeyGroupI4ThisFn)(Il2CppObject*, int32_t, Il2CppObject*);
typedef Il2CppObject* (*SelKeyGroupRefFn)(int32_t, Il2CppObject*);
typedef Il2CppObject* (*SelKeyGroupRefThisFn)(Il2CppObject*, int32_t, Il2CppObject*);

int32_t InvokeSelKeyGroupI4(Il2CppDelegate* d, int32_t key, Il2CppObject* bucket)
{
    return d->target != nullptr
        ? reinterpret_cast<SelKeyGroupI4ThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<SelKeyGroupI4Fn>(d->methodPtr)(key, bucket);
}

Il2CppObject* InvokeSelKeyGroupRef(Il2CppDelegate* d, int32_t key, Il2CppObject* bucket)
{
    return d->target != nullptr
        ? reinterpret_cast<SelKeyGroupRefThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<SelKeyGroupRefFn>(d->methodPtr)(key, bucket);
}

// nb9 H-F4: the WIDE numeric TResult selectors (long/double/float). The
// managed leaf returns C++ int64_t/double/float respectively; the old runtime
// only had the int32 and reference arms, so a numeric TResult was invoked
// through the reference (or int32) ABI.
template <typename Res>
Res InvokeSelKeyGroupTyped(Il2CppDelegate* d, int32_t key, Il2CppObject* bucket)
{
    typedef Res (*Fn)(int32_t, Il2CppObject*);
    typedef Res (*WithThisFn)(Il2CppObject*, int32_t, Il2CppObject*);
    return d->target != nullptr
        ? reinterpret_cast<WithThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<Fn>(d->methodPtr)(key, bucket);
}

// IsDefaultIntComparer -- the runtime's default EqualityComparer<int> /
// Comparer<int> singletons compare int32 keys by value; recognize them by
// POINTER IDENTITY (they are static objects with no reflected typeinfo).
bool IsDefaultIntComparer(Il2CppObject* cmp)
{
    return cmp == il2cpp_equality_comparer_default_int() || cmp == il2cpp_comparer_default_int();
}

// ComparerKeyEquals -- IEqualityComparer<TKey>.Equals(key, existingKey).
//   * nullptr                       -> int32 equality (the default contract)
//   * the default singletons        -> int32 equality
//   * a user comparer               -> the reflected virtual Equals(TKey,TKey)
//
// B4-1 r2 (D5): the reflected slot is selected by the DECLARED TKey width, not
// by the int32 bucket slot. A user IEqualityComparer<byte> reflects
// Equals(byte,byte) whose encoded signature is "(u1,u1)" -- probing "(i,i)"
// found nothing and threw at RUN time (the pre-B4-1 gate rejected the shape at
// transpile time instead). The fragments below are the MethodSignatureCoder
// EncodeType leaves (src/il2cppc/Emit/MethodSignatureCoder.cs:334-354,
// mirrored by typeinfo.cpp kLeaf):
//   0 int32  -> "i"   1 byte -> "u1"   2 sbyte -> "i1"   3 uint16/char -> "u2"/"c"
//   4 short  -> "i2"  5 uint -> "i"    6 bool  -> "b"
// keyKind 3 covers BOTH System.UInt16 and System.Char (same narrowing), so the
// lookup tries "(u2,u2)" then "(c,c)".
//
// The CALL ABI stays int32_t for every narrow family: the transpiler emits
// narrow-parameter methods with int32_t slots (the M1 32-bit integer contract),
// e.g. `int32_t ByteCmp_Equals(Il2CppObject*, int32_t, int32_t)`. Only the
// reflected SIGNATURE metadata carries the narrow leaf code.
namespace {

const char* const* KeyComparerFragments(int32_t keyKind, int32_t& count)
{
    static const char* const kInt[] = { "(i,i)" };                  // int32 / uint32
    static const char* const kByte[] = { "(u1,u1)" };               // byte
    static const char* const kSByte[] = { "(i1,i1)" };              // sbyte
    static const char* const kU16OrChar[] = { "(u2,u2)", "(c,c)" }; // ushort / char
    static const char* const kI16[] = { "(i2,i2)" };                // short
    static const char* const kBool[] = { "(b,b)" };                 // bool
    switch (keyKind)
    {
        case 1: count = 1; return kByte;
        case 2: count = 1; return kSByte;
        case 3: count = 2; return kU16OrChar;
        case 4: count = 1; return kI16;
        case 6: count = 1; return kBool;
        case 0:
        case 5:
        default: count = 1; return kInt;
    }
}

// The key family name for the LOUD message (the reflected slot is missing).
const char* KeyComparerTypeName(int32_t keyKind)
{
    switch (keyKind)
    {
        case 1: return "byte";
        case 2: return "sbyte";
        case 3: return "ushort/char";
        case 4: return "short";
        case 5: return "uint";
        case 6: return "bool";
        default: return "int";
    }
}

} // namespace

bool ComparerKeyEquals(Il2CppObject* cmp, int32_t key, int32_t existing,
                       int32_t keyKind)
{
    if (cmp == nullptr || IsDefaultIntComparer(cmp))
    {
        return key == existing;
    }
    // B4-1 r2 (D5): the reflected slot is keyed by the DECLARED TKey width.
    int32_t fragmentCount = 0;
    const char* const* fragments = KeyComparerFragments(keyKind, fragmentCount);
    int32_t slot = -1;
    for (int32_t fi = 0; fi < fragmentCount && slot < 0; ++fi)
    {
        slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", fragments[fi]);
    }
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        char message[192];
        std::snprintf(message, sizeof(message),
                      "IEqualityComparer<%s>.Equals dispatch: the comparer type has no "
                      "reflected virtual Equals(%s,%s) slot.",
                      KeyComparerTypeName(keyKind), KeyComparerTypeName(keyKind),
                      KeyComparerTypeName(keyKind));
        il2cpp_throw_not_supported_exception(message);
        return false; // unreachable (the factory throws)
    }
    // Every narrow family rides int32_t parameter slots (M1 contract).
    typedef bool (*EqualsFn)(Il2CppObject*, int32_t, int32_t);
    return reinterpret_cast<EqualsFn>(cmp->klass->vtable[slot])(cmp, key, existing);
}

// NewGroupElementArray -- the bucket storage for one element family.
Il2CppArray* NewGroupElementArray(GroupElemKind kind, int32_t length)
{
    return kind == GroupElemKind::Int32
        ? il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(int32_t)), false, length)
        : il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
}

// B4-1 (P0-04): narrow the raw int32 read of a narrow-TKey selector result
// back to the DECLARED key width. The callback's C++ return type is
// uint8_t/int8_t/uint16_t/int16_t, but the ABI call reads int32_t, so the
// register's upper bits are not defined by the callee. Without this narrowing
// a byte key of 44 whose register held 300 split the net48 bucket.
int32_t NarrowKey(int32_t raw, int32_t keyKind)
{
    switch (keyKind)
    {
        case 1: return static_cast<int32_t>(static_cast<uint8_t>(raw));
        case 2: return static_cast<int32_t>(static_cast<int8_t>(raw));
        case 3: return static_cast<int32_t>(static_cast<uint16_t>(raw));
        case 4: return static_cast<int32_t>(static_cast<int16_t>(raw));
        case 5: return raw; // uint32: the int32 slot keeps the bit pattern
        case 6: return raw != 0 ? 1 : 0; // bool
        default: return raw; // 0 int32 identity
    }
}

// BuildGroups -- the shared bucketing pass (see the block comment).
Il2CppObject* BuildGroups(Il2CppArray* src, Il2CppObject* keySel, Il2CppObject* elemSel,
                          GroupElemKind kind, Il2CppObject* cmp, int32_t keyKind = 0)
{
    RequireArray(src);
    Il2CppDelegate* kd = RequireCallback(keySel, "keySelector");
    Il2CppDelegate* ed = elemSel != nullptr ? RequireCallback(elemSel, "elementSelector") : nullptr;
    RequireInt32Family(src);
    if (elemSel == nullptr && cmp == nullptr)
    {
        // Nothing extra to validate.
    }

    EnsureGroupingClassesRegistered();

    // nb9 fix H1: the projected REFERENCE element is produced by the element
    // selector BEFORE the bucket growth / box / element-array allocations run,
    // so it must be a GC root for the whole iteration -- a bare local would
    // point at the pre-compaction address once one of those allocations
    // collects. Hoisted out of the loop because RootScope is a scope guard
    // (one registration per function, not per iteration).
    Il2CppObject* elemRef = nullptr;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&kd));
    roots.Add(reinterpret_cast<void**>(&ed));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&cmp));
    roots.Add(reinterpret_cast<void**>(&elemRef));

    Il2CppObject* result = il2cpp_object_new(&g_groupingResultClass);
    roots.Add(reinterpret_cast<void**>(&result));

    // Preallocate at the source length so publishing a bucket never allocates.
    Il2CppArray* groups = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    Il2CppLinqGroupingResult* res = AsGroupingResult(result);
    res->groups = groups;
    il2cpp_write_barrier(reinterpret_cast<void**>(&res->groups),
                         reinterpret_cast<Il2CppObject*>(groups));
    res->count = 0;

    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        // B4-1 (P0-04): narrow the selector result to the declared TKey width.
        const int32_t key = NarrowKey(InvokeSelI4I4(kd, v), keyKind); // may allocate -> collect

        // The projected element (only for the element-selector overloads).
        // Boxed families are NOT boxed yet: the growth allocation below could
        // move the box, so the box happens after the capacity check.
        int32_t elemI4 = 0;
        elemRef = nullptr; // nb9 fix H1: drop the previous iteration's root
        int64_t elemI8 = 0;
        double elemD = 0;
        if (ed != nullptr)
        {
            switch (kind)
            {
                case GroupElemKind::Int32: elemI4 = InvokeSelI4I4(ed, v); break;
                case GroupElemKind::Ref: elemRef = InvokeSelI4Ref(ed, v); break;
                case GroupElemKind::I8: elemI8 = InvokeSelI4I8(ed, v); break;
                case GroupElemKind::D: elemD = InvokeSelI4D(ed, v); break;
            }
        }
        else if (kind == GroupElemKind::Int32)
        {
            elemI4 = v;
        }

        res = AsGroupingResult(result);
        const int32_t n = res->count;

        // nb9 M-6: ComparerKeyEquals may run USER code (a custom
        // IEqualityComparer<int>) that allocates, so a groupSlots pointer
        // cached before the probe can dangle afterwards. Re-derive it from the
        // rooted result on every iteration and after the scan.
        int32_t found = -1;
        for (int32_t j = 0; j < n; ++j)
        {
            Il2CppLinqGroupingResult* cur = AsGroupingResult(result);
            Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(cur->groups->data);
            if (ComparerKeyEquals(cmp, key, AsGrouping(slots[j])->key, keyKind))
            {
                found = j;
                break;
            }
        }

        if (found >= 0)
        {
            res = AsGroupingResult(result);
            Il2CppLinqGrouping* grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                res->groups->data + found * sizeof(void*)));
            if (grp->count == grp->elements->length)
            {
                GrowGroupElements(&result, found);
            }
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                AsGroupingResult(result)->groups->data + found * sizeof(void*)));
            const int32_t at = grp->count;
            switch (kind)
            {
                case GroupElemKind::Int32:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, at)) = elemI4;
                    break;
                case GroupElemKind::Ref:
                {
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, at));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), elemRef);
                    *slot = elemRef;
                    break;
                }
                case GroupElemKind::I8:
                {
                    // Box AFTER the growth (no allocation may interleave the
                    // box and the slot store).
                    Il2CppObject* boxed = runtime::icall_box_long(elemI8);
                    // nb9 fix H2: the box allocates, so 'grp' (cached before it)
                    // may have moved -- re-derive it from the rooted result
                    // before touching elements/count.
                    grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                        AsGroupingResult(result)->groups->data + found * sizeof(void*)));
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, at));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), boxed);
                    *slot = boxed;
                    break;
                }
                case GroupElemKind::D:
                {
                    Il2CppObject* boxed = runtime::icall_box_double(elemD);
                    grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                        AsGroupingResult(result)->groups->data + found * sizeof(void*)));
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, at));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), boxed);
                    *slot = boxed;
                    break;
                }
            }
            ++grp->count;
        }
        else
        {
            Il2CppObject* ng = il2cpp_object_new(&g_groupingClass);
            Il2CppLinqGrouping* grp = AsGrouping(ng);
            grp->key = key;
            grp->elements = nullptr;
            grp->count = 0;
            res = AsGroupingResult(result);
            Il2CppObject** groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);
            il2cpp_write_barrier(reinterpret_cast<void**>(&groupSlots[res->count]), ng);
            groupSlots[res->count] = ng;
            ++res->count;

            Il2CppArray* elems = NewGroupElementArray(kind, kMinGroupItems);
            res = AsGroupingResult(result); // re-derive after the allocation
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                res->groups->data + (res->count - 1) * sizeof(void*)));
            grp->elements = elems;
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->elements),
                                 reinterpret_cast<Il2CppObject*>(elems));
            switch (kind)
            {
                case GroupElemKind::Int32:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, 0)) = elemI4;
                    break;
                case GroupElemKind::Ref:
                {
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, 0));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), elemRef);
                    *slot = elemRef;
                    break;
                }
                case GroupElemKind::I8:
                {
                    Il2CppObject* boxed = runtime::icall_box_long(elemI8);
                    // nb9 fix H2: re-derive 'grp' after the box allocation.
                    res = AsGroupingResult(result);
                    grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                        res->groups->data + (res->count - 1) * sizeof(void*)));
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, 0));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), boxed);
                    *slot = boxed;
                    break;
                }
                case GroupElemKind::D:
                {
                    Il2CppObject* boxed = runtime::icall_box_double(elemD);
                    res = AsGroupingResult(result);
                    grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                        res->groups->data + (res->count - 1) * sizeof(void*)));
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                        il2cpp_array_get_elem_ptr(grp->elements, 0));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), boxed);
                    *slot = boxed;
                    break;
                }
            }
            grp->count = 1;
        }
    }
    return result;
}

// ApplyResultSelector -- run Func<TKey,IEnumerable<TElement>,TResult> once per
// bucket, in first-appearance order, and materialize the results into an array
// of the TResult family (int32[] or a reference array).
Il2CppArray* ApplyResultSelector(Il2CppObject* result, Il2CppObject* resSel, int32_t rsKind)
{
    Il2CppDelegate* d = RequireCallback(resSel, "resultSelector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&result));

    const int32_t n = AsGroupingResult(result)->count;
    // nb9 H-F4: the result array follows TResult's real element layout.
    const bool refResult = rsKind == 2;
    int32_t rsElemSize = static_cast<int32_t>(sizeof(int32_t));
    switch (rsKind)
    {
        case 1: rsElemSize = static_cast<int32_t>(sizeof(int32_t)); break;
        case 2: rsElemSize = static_cast<int32_t>(sizeof(void*)); break;
        case 3: rsElemSize = static_cast<int32_t>(sizeof(int64_t)); break;
        case 4: rsElemSize = static_cast<int32_t>(sizeof(double)); break;
        case 5: rsElemSize = static_cast<int32_t>(sizeof(double)); break;
        default: break;
    }
    Il2CppArray* out = refResult
        ? il2cpp_array_new(nullptr, rsElemSize, true, n)
        : il2cpp_array_new_typed(rsElemSize, rsKind == 3 ? 2 : (rsKind == 4 || rsKind == 5) ? 4 : 1, n);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* bucket = *reinterpret_cast<Il2CppObject**>(
            AsGroupingResult(result)->groups->data + i * sizeof(void*));
        Il2CppObject* elemsObj = reinterpret_cast<Il2CppObject*>(
            il2cpp_linq_grouping_get_elements(bucket));
        // nb9 fix M1: an exception-safe per-iteration root. The previous
        // manual add/remove pair leaked the static-root slot whenever the
        // result selector threw; after enough iterations the process aborted
        // with "il2cpp_gc: static root table overflow". A block-scoped
        // RootScope still registers exactly one slot per iteration (no
        // accumulation) and unwinds correctly on an exception.
        RootScope elemRoots;
        elemRoots.Add(reinterpret_cast<void**>(&elemsObj));
        // The get_elements allocation may have moved the result/out.
        Il2CppLinqGroupingResult* res = AsGroupingResult(result);
        const int32_t key = AsGrouping(*reinterpret_cast<Il2CppObject**>(
            res->groups->data + i * sizeof(void*)))->key;
        if (refResult)
        {
            Il2CppObject* r = InvokeSelKeyGroupRef(d, key, elemsObj);
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
            *slot = r;
        }
        else if (rsKind == 3)
        {
            const int64_t r = InvokeSelKeyGroupTyped<int64_t>(d, key, elemsObj);
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
        }
        else if (rsKind == 4)
        {
            const double r = InvokeSelKeyGroupTyped<double>(d, key, elemsObj);
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = r;
        }
        else if (rsKind == 5)
        {
            const float r = InvokeSelKeyGroupTyped<float>(d, key, elemsObj);
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(r);
        }
        else
        {
            const int32_t r = InvokeSelKeyGroupI4(d, key, elemsObj);
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
        }
    }
    return out;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_linq_group_by_full -- every GroupBy overload (see linq_map.h).
// elemKind: 0 int32 | 1 reference | 2 int64 (boxed) | 3 double (boxed)
// rsKind:   0 no result selector | 1 int32 TResult | 2 reference TResult |
//           3 int64 TResult | 4 double TResult | 5 float TResult (nb9 H-F4)
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_full_key(Il2CppArray* src, Il2CppObject* keySel,
                                            Il2CppObject* elemSel, Il2CppObject* cmp,
                                            Il2CppObject* resSel, int32_t elemKind,
                                            int32_t rsKind, int32_t keyKind)
{
    if (cmp == nullptr)
    {
        // No comparer supplied: the default int32 equality (nullptr).
    }
    if (elemSel == nullptr)
    {
        elemKind = 0; // key-only: the element IS the source int32
    }
    GroupElemKind kind = GroupElemKind::Int32;
    switch (elemKind)
    {
        case 0: kind = GroupElemKind::Int32; break;
        case 1: kind = GroupElemKind::Ref; break;
        case 2: kind = GroupElemKind::I8; break;
        case 3: kind = GroupElemKind::D; break;
        default:
            il2cpp_throw_not_supported_exception("GroupBy: unsupported TElement family code.");
    }
    Il2CppObject* result = BuildGroups(src, keySel, elemSel, kind, cmp, keyKind);
    if (rsKind == 0)
    {
        return result;
    }
    if (rsKind < 1 || rsKind > 5)
    {
        il2cpp_throw_not_supported_exception("GroupBy: unsupported TResult family code.");
    }
    return reinterpret_cast<Il2CppObject*>(
        ApplyResultSelector(result, resSel, rsKind));
}

// B4-1 (P0-04): the identity-key entry every pre-existing caller keeps using.
Il2CppObject* il2cpp_linq_group_by_full(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, Il2CppObject* cmp,
                                        Il2CppObject* resSel, int32_t elemKind, int32_t rsKind)
{
    return il2cpp_linq_group_by_full_key(src, keySel, elemSel, cmp, resSel,
                                         elemKind, rsKind, /*keyKind=*/0);
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
    // copy is a pure value pass with no allocation inside.
    //
    // nb9: the bucket storage family is read off the array itself -- an int32
    // value array (int32 element family) or a REFERENCE array (the reference /
    // boxed int64 / boxed double element families), so every GroupBy overload
    // round-trips through the same IGrouping surface.
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&grouping));
    Il2CppLinqGrouping* grp = AsGrouping(grouping);
    const bool isRef = grp->elements != nullptr && grp->elements->isRefElem;
    Il2CppArray* out = isRef
        ? il2cpp_array_new(nullptr, sizeof(void*), true, grp->count)
        : il2cpp_array_new(nullptr, sizeof(int32_t), false, grp->count);
    grp = AsGrouping(grouping); // re-derive after the (possibly collecting) allocation
    if (isRef)
    {
        for (int32_t i = 0; i < grp->count; ++i)
        {
            Il2CppObject* v = *reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(grp->elements, i));
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), v);
            *slot = v;
        }
    }
    else
    {
        for (int32_t i = 0; i < grp->count; ++i)
        {
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(grp->elements, i));
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// il2cpp_linq_grouping_result_get_enumerator -- IEnumerable.GetEnumerator over
// a GroupBy result: the buckets in first-appearance order. The internal groups
// array is a GROWABLE backing store (capacity = source length), so an
// EXACT-SIZE reference snapshot is materialized and wrapped in the shared
// managed array enumerator (il2cpp_sortedlist_snapshot_get_enumerator); the
// reference get_Current carrier yields the bucket object.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_grouping_result_get_enumerator(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    EnsureGroupingClassesRegistered();
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&result));
    const int32_t n = AsGroupingResult(result)->count;
    Il2CppArray* snap = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n);
    roots.Add(reinterpret_cast<void**>(&snap));
    Il2CppLinqGroupingResult* res = AsGroupingResult(result); // re-derive after the allocation
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(snap->data);
    Il2CppObject** src = reinterpret_cast<Il2CppObject**>(res->groups->data);
    for (int32_t i = 0; i < n; ++i)
    {
        dst[i] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    return il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(reinterpret_cast<Il2CppObject*>(snap));
}

//------------------------------------------------------------------------------
// il2cpp_linq_grouping_get_enumerator -- IEnumerable.GetEnumerator over ONE
// bucket: its elements as a BOXED-int32 exact-size snapshot (the SortedList
// Values precedent), so the i4 get_Current carrier unboxes the net48 int32
// value and the ref carrier yields the boxed element. il2cpp_box_int32 may
// collect/compact on every element: the bucket and the snapshot are rooted and
// re-read after each allocation (no cached slot address survives a call).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_grouping_get_enumerator(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    EnsureGroupingClassesRegistered();
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&grouping));
    const int32_t n = AsGrouping(grouping)->count;
    Il2CppArray* snap = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(void*)), true, n);
    roots.Add(reinterpret_cast<void**>(&snap));
    // nb9: a REFERENCE-element bucket already holds managed references (a
    // reference / boxed int64 / boxed double element) -- copy them directly
    // instead of boxing an int32. The int32 family keeps the boxed snapshot
    // (the i4 get_Current carrier unboxes it).
    const bool isRef = AsGrouping(grouping)->elements != nullptr &&
                       AsGrouping(grouping)->elements->isRefElem;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* boxed;
        if (isRef)
        {
            boxed = *reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i));
        }
        else
        {
            const int32_t v = *reinterpret_cast<int32_t*>(
                il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i));
            boxed = il2cpp_box_int32(nullptr, v);
        }
        // Re-derive both rooted slots AFTER the (possibly collecting) box.
        Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(snap->data);
        dst[i] = boxed;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), boxed);
    }
    return il2cpp::runtime::il2cpp_sortedlist_snapshot_get_enumerator(reinterpret_cast<Il2CppObject*>(snap));
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

//------------------------------------------------------------------------------
// nb9 FIX (M21) -- the SELECTOR forms of the int32 aggregates: project every
// element through the selector, then aggregate the projected int32 values.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_sum_sel_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return il2cpp_linq_sum_i4(il2cpp_linq_select_i4_i4(src, sel));
}

int32_t il2cpp_linq_min_sel_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return il2cpp_linq_min_i4(il2cpp_linq_select_i4_i4(src, sel));
}

int32_t il2cpp_linq_max_sel_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return il2cpp_linq_max_i4(il2cpp_linq_select_i4_i4(src, sel));
}

double il2cpp_linq_average_sel_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return il2cpp_linq_average_i4(il2cpp_linq_select_i4_i4(src, sel));
}

// int64 / double projection families (net48 Func<TSource,long/double>).
namespace {

int64_t SumI8Array(Il2CppArray* src)
{
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t v = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if ((v > 0 && sum > INT64_MAX - v) || (v < 0 && sum < INT64_MIN - v))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += v;
    }
    return sum;
}

int64_t MinI8Array(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t m = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t v = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (v < m) m = v;
    }
    return m;
}

int64_t MaxI8Array(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t m = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t v = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (v > m) m = v;
    }
    return m;
}

double AverageI8Array(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t sum = SumI8Array(src);
    return static_cast<double>(sum) / static_cast<double>(src->length);
}

double SumDArray(Il2CppArray* src)
{
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
    }
    return sum;
}

double MinDArray(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double m = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double v = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        if (v < m || (v != v)) m = v; // net48: NaN propagates
    }
    return m;
}

double MaxDArray(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double m = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double v = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        if (v > m || (v != v)) m = v;
    }
    return m;
}

double AverageDArray(Il2CppArray* src)
{
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    return SumDArray(src) / static_cast<double>(src->length);
}

} // namespace

int64_t il2cpp_linq_sum_sel_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return SumI8Array(il2cpp_linq_select_i4_i8(src, sel));
}

int64_t il2cpp_linq_min_sel_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return MinI8Array(il2cpp_linq_select_i4_i8(src, sel));
}

int64_t il2cpp_linq_max_sel_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return MaxI8Array(il2cpp_linq_select_i4_i8(src, sel));
}

double il2cpp_linq_average_sel_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return AverageI8Array(il2cpp_linq_select_i4_i8(src, sel));
}

double il2cpp_linq_sum_sel_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return SumDArray(il2cpp_linq_select_i4_d(src, sel));
}

double il2cpp_linq_min_sel_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return MinDArray(il2cpp_linq_select_i4_d(src, sel));
}

double il2cpp_linq_max_sel_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return MaxDArray(il2cpp_linq_select_i4_d(src, sel));
}

double il2cpp_linq_average_sel_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return AverageDArray(il2cpp_linq_select_i4_d(src, sel));
}

//------------------------------------------------------------------------------
// nb9 H-F2: the FLOAT selector projection materializes a double[] (a float is
// widened exactly -- the runtime's F-stack carrier convention) and the
// transpiler now tracks the result as System.Double[], NOT System.Single[].
// The old Single[] declaration made every element-level consumer read 4-byte
// floats from 8-byte cells (the H-F2 mismatch); declaring the REAL layout
// keeps indexing/foreach/ToArray correct.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_i4_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(double)), 4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const float r = InvokeSelI4F(d, I4At(src, i));
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(r);
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(static_cast<int32_t>(sizeof(double)), 4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const float r = InvokeZipI4F(d, I4At(src, i), i);
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(r);
    }
    return out;
}

float il2cpp_linq_sum_sel_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return static_cast<float>(SumDArray(il2cpp_linq_select_i4_f(src, sel)));
}float il2cpp_linq_min_sel_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return static_cast<float>(MinDArray(il2cpp_linq_select_i4_f(src, sel)));
}

float il2cpp_linq_max_sel_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return static_cast<float>(MaxDArray(il2cpp_linq_select_i4_f(src, sel)));
}

float il2cpp_linq_average_sel_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    return static_cast<float>(AverageDArray(il2cpp_linq_select_i4_f(src, sel)));
}

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- Enumerable operators over an IGrouping bucket / the whole
// GroupBy result (see linq_map.h). Each entry materializes the bucket's exact
// element snapshot and reuses the int32 array kernel, so the semantics are
// identical to the array surface (the grouping IS an IEnumerable<int>).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_grouping_where(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_where_i4(il2cpp_linq_grouping_get_elements(grouping), pred);
}

Il2CppArray* il2cpp_linq_grouping_select(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_select_i4_i4(il2cpp_linq_grouping_get_elements(grouping), sel);
}

int32_t il2cpp_linq_grouping_count_pred(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_count_i4(il2cpp_linq_grouping_get_elements(grouping), pred);
}

int32_t il2cpp_linq_grouping_sum_sel(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    // net48 Sum(selector) == Sum(Select(selector)) -- checked addition.
    Il2CppArray* projected = il2cpp_linq_select_i4_i4(
        il2cpp_linq_grouping_get_elements(grouping), sel);
    return il2cpp_linq_sum_i4(projected);
}

double il2cpp_linq_grouping_average_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_average_i4(il2cpp_linq_grouping_get_elements(grouping));
}

double il2cpp_linq_grouping_average_sel(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_average_sel_i4(il2cpp_linq_grouping_get_elements(grouping), sel);
}

int32_t il2cpp_linq_grouping_min_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_min_i4(il2cpp_linq_grouping_get_elements(grouping));
}

int32_t il2cpp_linq_grouping_max_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_max_i4(il2cpp_linq_grouping_get_elements(grouping));
}

bool il2cpp_linq_grouping_any(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->count > 0;
}

bool il2cpp_linq_grouping_any_pred(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_any_i4(il2cpp_linq_grouping_get_elements(grouping), pred);
}

bool il2cpp_linq_grouping_all(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_all_i4(il2cpp_linq_grouping_get_elements(grouping), pred);
}

int32_t il2cpp_linq_grouping_first_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_first_i4(il2cpp_linq_grouping_get_elements(grouping));
}

int32_t il2cpp_linq_grouping_first_or_default_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_first_or_default_i4(il2cpp_linq_grouping_get_elements(grouping));
}

int32_t il2cpp_linq_grouping_last_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_last_i4(il2cpp_linq_grouping_get_elements(grouping));
}

int32_t il2cpp_linq_grouping_element_at_i4(Il2CppObject* grouping, int32_t index)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_element_at_i4(il2cpp_linq_grouping_get_elements(grouping), index);
}

bool il2cpp_linq_grouping_contains_i4(Il2CppObject* grouping, int32_t value)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_contains_i4(il2cpp_linq_grouping_get_elements(grouping), value);
}

Il2CppArray* il2cpp_linq_grouping_to_array(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_grouping_get_elements(grouping);
}

// The whole GroupBy result: the buckets as a REFERENCE array (an
// IEnumerable<IGrouping> carrier), the first bucket, and Any().
Il2CppArray* il2cpp_linq_grouping_result_to_array(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&result));
    const int32_t n = AsGroupingResult(result)->count;
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&out));
    Il2CppLinqGroupingResult* res = AsGroupingResult(result);
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* bucket = *reinterpret_cast<Il2CppObject**>(
            res->groups->data + i * sizeof(void*));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), bucket);
        *slot = bucket;
    }
    return out;
}

Il2CppObject* il2cpp_linq_grouping_result_first(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    if (AsGroupingResult(result)->count == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    return il2cpp_linq_grouping_result_get_grouping(result, 0);
}

Il2CppObject* il2cpp_linq_grouping_result_first_or_default(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    if (AsGroupingResult(result)->count == 0)
    {
        return nullptr;
    }
    return il2cpp_linq_grouping_result_get_grouping(result, 0);
}

bool il2cpp_linq_grouping_result_any(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    return AsGroupingResult(result)->count > 0;
}

// ToList() over the GroupBy result: a List<object> (the listobj domain)
// holding the bucket objects in first-appearance order -- the same carrier the
// reference-element List<T> surface uses, so Count/indexer/foreach resolve.
Il2CppObject* il2cpp_linq_grouping_result_to_list(Il2CppObject* result)
{
    ThrowIfNullReceiver(result);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&result));
    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));
    const int32_t n = AsGroupingResult(result)->count;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* bucket = *reinterpret_cast<Il2CppObject**>(
            AsGroupingResult(result)->groups->data + i * sizeof(void*));
        il2cpp_list_obj_add(list, bucket);
    }
    return list;
}

} // namespace il2cpp
