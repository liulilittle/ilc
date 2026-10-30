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
#include "linq_family.h"       // BATCH4: the decimal cell-equality kernel
#include "linq_sort.h"         // BATCH4: order_by_key(_desc)_dec_* reuse by the bucket OrderBy wrappers

#include "arraysort.h"            // FindVirtualSlotByNameAndSignature (user comparers)
#include "genericcomparer_obj.h"  // the default EqualityComparer/Comparer singletons
#include "il2cpp_boxing_ext.h"    // icall_box_double
#include "il2cpp_boxing_ext2.h"   // icall_box_long
#include "il2cpp_nullable.h"      // impl-linq2 (C): icall_box_date_time / icall_box_guid
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_value_semantics.h"  // p0grp: icall_object_equals (ref-key equality)
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

// p0grp: a DateTime[] source. The runtime's DateTime array cell is 8 bytes
// (the ticks; the transpiler's ArrayElementType maps System.DateTime to
// (8, false)), so the family check uses the CELL width, not sizeof(Il2CppDateTime)
// (the struct carries the extra kind member and is 16 bytes).
void RequireDateTimeFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 8)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.DateTime arrays are supported.");
    }
}

// vtsrc: a Guid[] source -- the 16-byte raw value cell (guid.h).
void RequireGuidFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != static_cast<int32_t>(sizeof(Il2CppGuid)))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.Guid arrays are supported.");
    }
}

// delta 2: a TimeSpan[] source -- the 8-byte ticks cell (il2cpp_time.h).
void RequireTimeSpanFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != 8)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.TimeSpan arrays are supported.");
    }
}

// delta 2: a DateTimeOffset[] source -- the 16-byte Il2CppDateTimeOffset cell
// (il2cpp_dtoffset.h).
void RequireDtoffsetFamily(Il2CppArray* src)
{
    if (src->isRefElem || src->elemSize != static_cast<int32_t>(sizeof(runtime::Il2CppDateTimeOffset)))
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional System.DateTimeOffset arrays are supported.");
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
// The reference-source, int32-RESULT selector ABI (Func<string,char> /
// Func<string,int32> -- the selector returns a char/int32 in the int32 slot).
typedef int32_t       (*SelRefI4Fn)(Il2CppObject*);
typedef int32_t       (*SelRefI4ThisFn)(Il2CppObject*, Il2CppObject*);

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

int32_t InvokeSelRefI4(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefI4ThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefI4Fn>(d->methodPtr)(o);
}

// The reference-source WIDE-RESULT selector ABIs (Func<string,long> /
// Func<string,double> / Func<string,float>): the selector returns in the
// int64 / F-stack slot. Same per-binding dispatch contract as InvokeSelRefI4.
typedef int64_t       (*SelRefI8Fn)(Il2CppObject*);
typedef int64_t       (*SelRefI8ThisFn)(Il2CppObject*, Il2CppObject*);
typedef double        (*SelRefDFn)(Il2CppObject*);
typedef double        (*SelRefDThisFn)(Il2CppObject*, Il2CppObject*);
typedef float         (*SelRefFFn)(Il2CppObject*);
typedef float         (*SelRefFThisFn)(Il2CppObject*, Il2CppObject*);

int64_t InvokeSelRefI8(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefI8ThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefI8Fn>(d->methodPtr)(o);
}

double InvokeSelRefD(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefDThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefDFn>(d->methodPtr)(o);
}

float InvokeSelRefF(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefFThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefFFn>(d->methodPtr)(o);
}

//------------------------------------------------------------------------------
// The WIDE-source selector ABIs (net48 Sum/Min/Max/Average<TSource,
// TResult>) with TSource = long / double / float. The element crosses in its
// OWN slot (int64 for long, the F-stack carrier for float/double), and the
// selector's result crosses in the int32 / int64 / F-stack slot.
//------------------------------------------------------------------------------
typedef int32_t       (*SelI8SrcI4Fn)(int64_t);
typedef int32_t       (*SelI8SrcI4ThisFn)(Il2CppObject*, int64_t);
typedef int64_t       (*SelI8SrcI8Fn)(int64_t);
typedef int64_t       (*SelI8SrcI8ThisFn)(Il2CppObject*, int64_t);
typedef double        (*SelI8SrcDFn)(int64_t);
typedef double        (*SelI8SrcDThisFn)(Il2CppObject*, int64_t);
typedef float         (*SelI8SrcFFn)(int64_t);
typedef float         (*SelI8SrcFThisFn)(Il2CppObject*, int64_t);

int32_t InvokeSelI8SrcI4(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI8SrcI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI8SrcI4Fn>(d->methodPtr)(v);
}
int64_t InvokeSelI8SrcI8(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI8SrcI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI8SrcI8Fn>(d->methodPtr)(v);
}
double InvokeSelI8SrcD(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI8SrcDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI8SrcDFn>(d->methodPtr)(v);
}
float InvokeSelI8SrcF(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI8SrcFThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI8SrcFFn>(d->methodPtr)(v);
}

typedef int32_t       (*SelDSrcI4Fn)(double);
typedef int32_t       (*SelDSrcI4ThisFn)(Il2CppObject*, double);
typedef int64_t       (*SelDSrcI8Fn)(double);
typedef int64_t       (*SelDSrcI8ThisFn)(Il2CppObject*, double);
typedef double        (*SelDSrcDFn)(double);
typedef double        (*SelDSrcDThisFn)(Il2CppObject*, double);
typedef float         (*SelDSrcFFn)(double);
typedef float         (*SelDSrcFThisFn)(Il2CppObject*, double);

int32_t InvokeSelDSrcI4(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDSrcI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDSrcI4Fn>(d->methodPtr)(v);
}
int64_t InvokeSelDSrcI8(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDSrcI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDSrcI8Fn>(d->methodPtr)(v);
}
double InvokeSelDSrcD(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDSrcDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDSrcDFn>(d->methodPtr)(v);
}
float InvokeSelDSrcF(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDSrcFThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDSrcFFn>(d->methodPtr)(v);
}

// A FLOAT source element rides the F-stack carrier (double), so the float-
// source selector ABIs have the same shape as the double-source ones.
int32_t InvokeSelFSrcI4(Il2CppDelegate* d, double v) { return InvokeSelDSrcI4(d, v); }
int64_t InvokeSelFSrcI8(Il2CppDelegate* d, double v) { return InvokeSelDSrcI8(d, v); }
double InvokeSelFSrcD(Il2CppDelegate* d, double v) { return InvokeSelDSrcD(d, v); }
float InvokeSelFSrcF(Il2CppDelegate* d, double v) { return InvokeSelDSrcF(d, v); }

//------------------------------------------------------------------------------
// The reference-source INDEX selector ABIs (net48 Func<TSource,int,TResult>):
// the element crosses as Il2CppObject* and the zero-based source index as the
// second argument.
//------------------------------------------------------------------------------
typedef int32_t       (*SelRefI4IdxFn)(Il2CppObject*, int32_t);
typedef int32_t       (*SelRefI4IdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);
typedef int64_t       (*SelRefI8IdxFn)(Il2CppObject*, int32_t);
typedef int64_t       (*SelRefI8IdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);
typedef double        (*SelRefDIdxFn)(Il2CppObject*, int32_t);
typedef double        (*SelRefDIdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);
typedef float         (*SelRefFIdxFn)(Il2CppObject*, int32_t);
typedef float         (*SelRefFIdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

int32_t InvokeSelRefI4Idx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefI4IdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<SelRefI4IdxFn>(d->methodPtr)(o, index);
}
int64_t InvokeSelRefI8Idx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefI8IdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<SelRefI8IdxFn>(d->methodPtr)(o, index);
}
double InvokeSelRefDIdx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefDIdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<SelRefDIdxFn>(d->methodPtr)(o, index);
}
float InvokeSelRefFIdx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefFIdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<SelRefFIdxFn>(d->methodPtr)(o, index);
}

// p0grp: the DECIMAL-source KEY-SELECTOR ABI (Func<System.Decimal,int32>).
// The generated leaf takes the decimal struct BY VALUE and returns int32:
//   int32_t (*)(il2cpp::runtime::Il2CppDecimal)                    (static)
//   int32_t (*)(Il2CppObject*, il2cpp::runtime::Il2CppDecimal)     (instance)
// The struct is passed by value exactly like the decimal arithmetic icalls
// return it (decimal.h T3 contract); a pointer parameter would change the
// calling convention.
typedef int32_t (*SelDecI4Fn)(runtime::Il2CppDecimal);
typedef int32_t (*SelDecI4ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

int32_t InvokeSelDecI4(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecI4Fn>(d->methodPtr)(v);
}

// p0grp: the decimal-source selector returning a REFERENCE result (the
// Func<System.Decimal,TElement> element-selector shape with a reference
// TElement).
typedef Il2CppObject* (*SelDecRefFn)(runtime::Il2CppDecimal);
typedef Il2CppObject* (*SelDecRefThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

Il2CppObject* InvokeSelDecRef(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecRefThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecRefFn>(d->methodPtr)(v);
}

// p0grp: the decimal-source predicate ABI (Func<System.Decimal,bool>).
typedef bool (*SelDecBoolFn)(runtime::Il2CppDecimal);
typedef bool (*SelDecBoolThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

bool InvokeSelDecBool(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecBoolThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecBoolFn>(d->methodPtr)(v);
}

// BATCH4: the wide-key selector ABIs over a DECIMAL source element (the
// Func<System.Decimal,TKey> shape, TKey wider than the int32 slot). The key
// returns BY VALUE in its own family slot (the same by-value convention as the
// int32-source wide-key ABIs).
typedef int64_t (*SelDecI8Fn)(runtime::Il2CppDecimal);
typedef int64_t (*SelDecI8ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
typedef runtime::Il2CppDecimal (*SelDecDecKeyFn)(runtime::Il2CppDecimal);
typedef runtime::Il2CppDecimal (*SelDecDecKeyThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
typedef runtime::Il2CppDateTime (*SelDecDtKeyFn)(runtime::Il2CppDecimal);
typedef runtime::Il2CppDateTime (*SelDecDtKeyThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
typedef Il2CppGuid (*SelDecGuidKeyFn)(runtime::Il2CppDecimal);
typedef Il2CppGuid (*SelDecGuidKeyThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

int64_t InvokeSelDecI8(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecI8Fn>(d->methodPtr)(v);
}

runtime::Il2CppDecimal InvokeSelDecDecKey(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecDecKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecDecKeyFn>(d->methodPtr)(v);
}

// p0grp: the decimal-source element selector returning a DateTime (the bucket
// DateTime element family).
typedef runtime::Il2CppDateTime (*SelDecDtElemFn)(runtime::Il2CppDecimal);
typedef runtime::Il2CppDateTime (*SelDecDtElemThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

runtime::Il2CppDateTime InvokeSelDecDtElem(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecDtElemThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecDtElemFn>(d->methodPtr)(v);
}

// The DECIMAL-source selector returning a double / float result (net48
// Select<decimal,double> / Select<decimal,float>).
typedef double (*SelDecDFn)(runtime::Il2CppDecimal);
typedef double (*SelDecDThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
typedef float (*SelDecFFn)(runtime::Il2CppDecimal);
typedef float (*SelDecFThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

double InvokeSelDecD(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecDFn>(d->methodPtr)(v);
}

float InvokeSelDecF(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecFThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecFFn>(d->methodPtr)(v);
}

// The DECIMAL-source INDEX selector ABIs (net48 Select<decimal,int,TResult>):
// the element crosses by value and the zero-based source index as the second
// argument.
typedef int32_t (*SelDecIdxI4Fn)(runtime::Il2CppDecimal, int32_t);
typedef int32_t (*SelDecIdxI4ThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);
typedef int64_t (*SelDecIdxI8Fn)(runtime::Il2CppDecimal, int32_t);
typedef int64_t (*SelDecIdxI8ThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);
typedef double (*SelDecIdxDFn)(runtime::Il2CppDecimal, int32_t);
typedef double (*SelDecIdxDThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);
typedef float (*SelDecIdxFFn)(runtime::Il2CppDecimal, int32_t);
typedef float (*SelDecIdxFThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);
typedef runtime::Il2CppDecimal (*SelDecIdxDecFn)(runtime::Il2CppDecimal, int32_t);
typedef runtime::Il2CppDecimal (*SelDecIdxDecThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);

int32_t InvokeSelDecIdxI4(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxI4ThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxI4Fn>(d->methodPtr)(v, index);
}
int64_t InvokeSelDecIdxI8(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxI8ThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxI8Fn>(d->methodPtr)(v, index);
}
double InvokeSelDecIdxD(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxDThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxDFn>(d->methodPtr)(v, index);
}
float InvokeSelDecIdxF(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxFThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxFFn>(d->methodPtr)(v, index);
}
runtime::Il2CppDecimal InvokeSelDecIdxDec(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxDecThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxDecFn>(d->methodPtr)(v, index);
}

// impl-linqleft: the 3-arg decimal-source selector returning a REFERENCE
// result (Select<decimal,int,TResult> with a reference TResult).
typedef Il2CppObject* (*SelDecIdxRefFn)(runtime::Il2CppDecimal, int32_t);
typedef Il2CppObject* (*SelDecIdxRefThisFn)(Il2CppObject*, runtime::Il2CppDecimal, int32_t);

Il2CppObject* InvokeSelDecIdxRef(Il2CppDelegate* d, const runtime::Il2CppDecimal& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecIdxRefThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<SelDecIdxRefFn>(d->methodPtr)(v, index);
}

runtime::Il2CppDateTime InvokeSelDecDtKey(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecDtKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecDtKeyFn>(d->methodPtr)(v);
}

Il2CppGuid InvokeSelDecGuidKey(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecGuidKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecGuidKeyFn>(d->methodPtr)(v);
}

// BATCH4: the wide KEY-selector ABIs (the TKey is a 16-byte value type).
// The generated leaf's ReturnTypeCpp maps the struct to its C++ value type, so
// the leaf returns the struct BY VALUE (the MS x64 ABI lowers that to sret --
// the same convention SelectToDecimal's leaf relies on, and the same contract
// the runtime's icall_decimal_* / il2cpp_ienum_get_current_dec entries use):
//   il2cpp::runtime::Il2CppDecimal (*)(int32_t)                    (static)
//   il2cpp::runtime::Il2CppDecimal (*)(Il2CppObject*, int32_t)     (instance)
// The SOURCE element is an int32 here (the shape loud-p0-grp-key-long pins).
typedef runtime::Il2CppDecimal (*SelI4DecKeyFn)(int32_t);
typedef runtime::Il2CppDecimal (*SelI4DecKeyThisFn)(Il2CppObject*, int32_t);
typedef runtime::Il2CppDateTime (*SelI4DtKeyFn)(int32_t);
typedef runtime::Il2CppDateTime (*SelI4DtKeyThisFn)(Il2CppObject*, int32_t);
typedef Il2CppGuid (*SelI4GuidKeyFn)(int32_t);
typedef Il2CppGuid (*SelI4GuidKeyThisFn)(Il2CppObject*, int32_t);

runtime::Il2CppDecimal InvokeSelI4DecKey(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4DecKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4DecKeyFn>(d->methodPtr)(v);
}

runtime::Il2CppDateTime InvokeSelI4DtKey(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4DtKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4DtKeyFn>(d->methodPtr)(v);
}

// p0grp: the int32-source element selector returning a DateTime (the bucket
// DateTime element family).
typedef runtime::Il2CppDateTime (*SelI4DtElemFn)(int32_t);
typedef runtime::Il2CppDateTime (*SelI4DtElemThisFn)(Il2CppObject*, int32_t);

runtime::Il2CppDateTime InvokeSelI4DtElem(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4DtElemThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4DtElemFn>(d->methodPtr)(v);
}

// p0grp: the reference-source element selector returning a DateTime.
typedef runtime::Il2CppDateTime (*SelRefDtElemFn)(Il2CppObject*);
typedef runtime::Il2CppDateTime (*SelRefDtElemThisFn)(Il2CppObject*, Il2CppObject*);

runtime::Il2CppDateTime InvokeSelRefDtElem(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefDtElemThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefDtElemFn>(d->methodPtr)(o);
}

Il2CppGuid InvokeSelI4GuidKey(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelI4GuidKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelI4GuidKeyFn>(d->methodPtr)(v);
}

// p0grp: the wide KEY-selector ABIs over a DateTime source element (the
// Func<Il2CppDateTime,TKey> leaf ABI -- the DateTime crosses BY VALUE).
typedef int32_t (*SelDtI4Fn)(runtime::Il2CppDateTime);
typedef int32_t (*SelDtI4ThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef int64_t (*SelDtI8Fn)(runtime::Il2CppDateTime);
typedef int64_t (*SelDtI8ThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef double (*SelDtDFn)(runtime::Il2CppDateTime);
typedef double (*SelDtDThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef Il2CppObject* (*SelDtRefFn)(runtime::Il2CppDateTime);
typedef Il2CppObject* (*SelDtRefThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef runtime::Il2CppDecimal (*SelDtDecKeyFn)(runtime::Il2CppDateTime);
typedef runtime::Il2CppDecimal (*SelDtDecKeyThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef runtime::Il2CppDateTime (*SelDtDtKeyFn)(runtime::Il2CppDateTime);
typedef runtime::Il2CppDateTime (*SelDtDtKeyThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef Il2CppGuid (*SelDtGuidKeyFn)(runtime::Il2CppDateTime);
typedef Il2CppGuid (*SelDtGuidKeyThisFn)(Il2CppObject*, runtime::Il2CppDateTime);

int32_t InvokeSelDtI4(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtI4Fn>(d->methodPtr)(v);
}
int64_t InvokeSelDtI8(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtI8Fn>(d->methodPtr)(v);
}
double InvokeSelDtD(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtDFn>(d->methodPtr)(v);
}
Il2CppObject* InvokeSelDtRef(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtRefThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtRefFn>(d->methodPtr)(v);
}
runtime::Il2CppDecimal InvokeSelDtDecKey(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtDecKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtDecKeyFn>(d->methodPtr)(v);
}
runtime::Il2CppDateTime InvokeSelDtDtKey(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtDtKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtDtKeyFn>(d->methodPtr)(v);
}
Il2CppGuid InvokeSelDtGuidKey(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDtGuidKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDtGuidKeyFn>(d->methodPtr)(v);
}

// vtsrc: the key/element selector ABIs over a GUID source element. The Guid is
// a 16-byte value type, so it crosses the generated leaf BY VALUE (the same
// aggregate ABI the decimal/DateTime selectors use).
typedef int32_t (*SelGuidI4Fn)(Il2CppGuid);
typedef int32_t (*SelGuidI4ThisFn)(Il2CppObject*, Il2CppGuid);
typedef int64_t (*SelGuidI8Fn)(Il2CppGuid);
typedef int64_t (*SelGuidI8ThisFn)(Il2CppObject*, Il2CppGuid);
typedef double (*SelGuidDFn)(Il2CppGuid);
typedef double (*SelGuidDThisFn)(Il2CppObject*, Il2CppGuid);
typedef Il2CppObject* (*SelGuidRefFn)(Il2CppGuid);
typedef Il2CppObject* (*SelGuidRefThisFn)(Il2CppObject*, Il2CppGuid);
typedef runtime::Il2CppDecimal (*SelGuidDecKeyFn)(Il2CppGuid);
typedef runtime::Il2CppDecimal (*SelGuidDecKeyThisFn)(Il2CppObject*, Il2CppGuid);
typedef runtime::Il2CppDateTime (*SelGuidDtKeyFn)(Il2CppGuid);
typedef runtime::Il2CppDateTime (*SelGuidDtKeyThisFn)(Il2CppObject*, Il2CppGuid);
typedef Il2CppGuid (*SelGuidGuidKeyFn)(Il2CppGuid);
typedef Il2CppGuid (*SelGuidGuidKeyThisFn)(Il2CppObject*, Il2CppGuid);

int32_t InvokeSelGuidI4(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidI4Fn>(d->methodPtr)(v);
}
int64_t InvokeSelGuidI8(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidI8Fn>(d->methodPtr)(v);
}
double InvokeSelGuidD(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidDFn>(d->methodPtr)(v);
}
Il2CppObject* InvokeSelGuidRef(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidRefThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidRefFn>(d->methodPtr)(v);
}
runtime::Il2CppDecimal InvokeSelGuidDecKey(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidDecKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidDecKeyFn>(d->methodPtr)(v);
}
runtime::Il2CppDateTime InvokeSelGuidDtKey(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidDtKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidDtKeyFn>(d->methodPtr)(v);
}
Il2CppGuid InvokeSelGuidGuidKey(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelGuidGuidKeyThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelGuidGuidKeyFn>(d->methodPtr)(v);
}

// p0grp: the same wide KEY-selector ABIs over a REFERENCE source element (the
// Func<Il2CppObject*,TKey> leaf ABI -- the ref-source GroupBy key selector).
typedef runtime::Il2CppDecimal (*SelRefDecKeyFn)(Il2CppObject*);
typedef runtime::Il2CppDecimal (*SelRefDecKeyThisFn)(Il2CppObject*, Il2CppObject*);
typedef runtime::Il2CppDateTime (*SelRefDtKeyFn)(Il2CppObject*);
typedef runtime::Il2CppDateTime (*SelRefDtKeyThisFn)(Il2CppObject*, Il2CppObject*);
typedef Il2CppGuid (*SelRefGuidKeyFn)(Il2CppObject*);
typedef Il2CppGuid (*SelRefGuidKeyThisFn)(Il2CppObject*, Il2CppObject*);

runtime::Il2CppDecimal InvokeSelRefDecKey(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefDecKeyThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefDecKeyFn>(d->methodPtr)(o);
}

runtime::Il2CppDateTime InvokeSelRefDtKey(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefDtKeyThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefDtKeyFn>(d->methodPtr)(o);
}

Il2CppGuid InvokeSelRefGuidKey(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefGuidKeyThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<SelRefGuidKeyFn>(d->methodPtr)(o);
}

//------------------------------------------------------------------------------
// impl-groupbykey: the GENERIC (source element, key/element result) selector
// invocation used by the GroupBy key/element dispatch. The generated leaf's
// C++ signature is exactly Key (*)(Src) (static) or Key (*)(Il2CppObject*, Src)
// (instance); declaring both Src and Key BY VALUE reproduces that ABI for every
// family: the int32_t slot for the int32/narrow contract, the int64_t / double
// registers for the wide families, Il2CppObject* for a reference and the
// aggregate (hidden-pointer) convention for the 8/16-byte value structs
// (Il2CppTimeSpan / Il2CppDateTime / Il2CppGuid / Il2CppDecimal /
// Il2CppDateTimeOffset). This is what lets the bucketing kernel serve every
// (source family, key family) pair without one hand-written ABI per pair.
//------------------------------------------------------------------------------
template <typename Src, typename Key>
Key InvokeGroupKeySel(Il2CppDelegate* d, Src v)
{
    typedef Key (*Fn)(Src);
    typedef Key (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

// A USER ENUM key. TypeEmitter emits a translated enum as
//   struct E { Il2CppObject _obj; int32_t value__; };   // 16 bytes (instanceSize)
// so a leaf returning an enum uses the 16-byte aggregate (hidden-pointer)
// return convention and the VALUE lives at offset 8. Every translated enum has
// exactly this image (no user inheritance, no other instance fields), so the
// runtime can read the key without naming the enum's generated C++ type.
struct Il2CppEnumKeyImage
{
    void* header;   // _obj (offset 0)
    int32_t value;  // value__ (offset 8)
    int32_t pad;    // tail padding (sizeof == 16, align 8)
};

template <typename Src>
int32_t InvokeGroupEnumKeySel(Il2CppDelegate* d, Src v)
{
    typedef Il2CppEnumKeyImage (*Fn)(Src);
    typedef Il2CppEnumKeyImage (*ThisFn)(Il2CppObject*, Src);
    const Il2CppEnumKeyImage r = d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
    return r.value;
}

// The ELEMENT-selector face of the same generic invocation (the bucket storage
// family decides the result type; the source family decides Src).
template <typename Src, typename Elem>
Elem InvokeGroupElemSel(Il2CppDelegate* d, Src v)
{
    typedef Elem (*Fn)(Src);
    typedef Elem (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
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

// The REFERENCE-source, 2-arg index PREDICATE ABI (net48 Func<TSource,int,bool>
// over a reference-element source): bool(*)(Il2CppObject*, int32_t) for a
// STATIC leaf; bool(*)(Il2CppObject*, Il2CppObject*, int32_t) for an INSTANCE
// leaf (target in the `this` slot -- the per-binding dispatch above).
typedef bool (*PredRefIdxFn)(Il2CppObject*, int32_t);
typedef bool (*PredRefIdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

bool InvokePredRefIdx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredRefIdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<PredRefIdxFn>(d->methodPtr)(o, index);
}

// The REFERENCE-source, 2-arg index SELECTOR ABI (net48
// Func<TSource,int,TResult> returning a reference): Il2CppObject*(*)(Il2CppObject*,
// int32_t) for a STATIC leaf; Il2CppObject*(*)(Il2CppObject*, Il2CppObject*,
// int32_t) for an INSTANCE leaf (target in the `this` slot).
typedef Il2CppObject* (*SelRefRefIdxFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelRefRefIdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

Il2CppObject* InvokeSelRefRefIdx(Il2CppDelegate* d, Il2CppObject* o, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<SelRefRefIdxThisFn>(d->methodPtr)(d->target, o, index)
        : reinterpret_cast<SelRefRefIdxFn>(d->methodPtr)(o, index);
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
static_assert(offsetof(Il2CppLinqGrouping, keyHi) == 4 * sizeof(void*),
    "Il2CppLinqGrouping: keyHi must sit at slot 4 (NOT in the bitmap)");
static_assert(offsetof(Il2CppLinqGrouping, keyRef) == 5 * sizeof(void*),
    "Il2CppLinqGrouping: keyRef must sit at slot 5 (bitmap bit 5)");
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
    static_cast<int32_t>(sizeof(Il2CppLinqGrouping)), // instanceSize (40: 16-byte key)
    0b100100u,                               // refFieldBitmap: bit 2 = elements, bit 5 = keyRef
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
        nullptr, grp->elements->elemSize, isRef, newLength);
    // impl-linq2 (C): the grown backing store keeps the bucket's recorded
    // element kind. The old reallocation reset elemKind to kElemKindNone, so a
    // grown 16-byte cell array (System.Decimal vs System.Guid vs the padded
    // System.DateTime {ticks,kind}) lost the only identity that tells the
    // bucket-enumerator box dispatch apart -- every grown DateTime/Guid cell
    // was re-boxed as a System.Decimal.
    newElems->elemKind = grp->elements->elemKind;
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
        // p0grp: VALUE cells are copied at the bucket's OWN element width.
        // The int32 family is a 4-byte cell, but a decimal-source bucket holds
        // 16-byte System.Decimal cells -- copying int32-per-slot would keep
        // only the low dword of every decimal.
        std::memcpy(newElems->data, grp->elements->data,
                    static_cast<size_t>(n) * static_cast<size_t>(grp->elements->elemSize));
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

Il2CppArray* il2cpp_linq_where_idx_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // SINGLE index-aware predicate pass over the reference family (net48
    // Func<TSource,int,bool>): the index is the element's 0-based SOURCE
    // position. The scratch and the exact result are reference arrays, so
    // every store goes through the write barrier (a collecting predicate may
    // move both the source and the scratch).
    Il2CppArray* scratch = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRefIdx(d, elem, i))
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(scratch, w));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), elem);
            *slot = elem;
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
        Il2CppObject* moved = *reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(scratch, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(dst), moved);
        *dst = moved;
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
// The reference-source Select with a VALUE result (net48
// Select<TSource,TResult> with a non-reference TResult over a reference source):
// int32 / int64 / double / float. The result array carries the matching
// elemKind so downstream formatting/scans read the real cell width.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(4, kElemKindI4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefI4(d, RefAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindI8, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefI8(d, RefAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindD, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefD(d, RefAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    // A float result rides the F-stack carrier: an 8-byte double cell stamped
    // elemKind F (the same convention as il2cpp_linq_select_i4_f).
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindF, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            static_cast<double>(InvokeSelRefF(d, RefAt(src, i)));
    return out;
}

//------------------------------------------------------------------------------
// The reference-source INDEX Select with a VALUE result (net48
// Select<TSource,int,TResult>): the selector receives the element AND its
// zero-based source index.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_idx_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(4, kElemKindI4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefI4Idx(d, RefAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindI8, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefI8Idx(d, RefAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindD, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelRefDIdx(d, RefAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindF, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            static_cast<double>(InvokeSelRefFIdx(d, RefAt(src, i), i));
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

// impl-linq-vtpred: the reference-source PREDICATE forms of First /
// FirstOrDefault. Same forward short-circuit walk and rooting discipline as
// il2cpp_linq_first_predicate; the element is an Il2CppObject* slot copy, so
// no managed address is held across the callback.
Il2CppObject* il2cpp_linq_first_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem)) return elem;
    }
    il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return nullptr; // unreachable (the factory throws)
}

Il2CppObject* il2cpp_linq_first_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem)) return elem;
    }
    return nullptr;
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

//------------------------------------------------------------------------------
// Single / SingleOrDefault WITH a predicate (net48 Func<TSource,bool>).
//
// Unlike the source-only forms (linq_family.h, which answer on the array
// length), these walk EVERY element and count the matches. net48 does NOT
// short-circuit -- a second match must remain detectable even when it appears
// early -- so the predicate runs on every element:
//   * Single(predicate)          -- 0 matches -> InvalidOperationException
//                                  ("no matching element"); >1 -> InvalidOperation-
//                                  Exception ("more than one matching element");
//                                  exactly 1 -> that element.
//   * SingleOrDefault(predicate) -- 0 -> default(T); >1 -> InvalidOperationException
//                                  ("more than one matching element").
//------------------------------------------------------------------------------
int32_t il2cpp_linq_single_pred_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t result = 0;
    int32_t count = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem))
        {
            result = elem;
            ++count;
        }
    }
    if (count == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    if (count > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
    return result;
}

int32_t il2cpp_linq_single_or_default_pred_i4(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireInt32Family(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t result = 0;
    int32_t count = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t elem = I4At(src, i);
        if (InvokePredI4(d, elem))
        {
            result = elem;
            ++count;
        }
    }
    if (count > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
    return result;
}

Il2CppObject* il2cpp_linq_single_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // The element is a managed reference, so it is re-read through the rooted
    // 'src' slot on every iteration and kept only through the result local
    // (also rooted), never cached across a callback invocation.
    Il2CppObject* result = nullptr;
    int32_t count = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem))
        {
            result = elem;
            ++count;
        }
    }
    if (count == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    if (count > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
    return result;
}

Il2CppObject* il2cpp_linq_single_or_default_pred_ref(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* result = nullptr;
    int32_t count = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        if (InvokePredRef(d, elem))
        {
            result = elem;
            ++count;
        }
    }
    if (count > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
    return result;
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
// impl-linq-vtpred: the SOURCE PREDICATE family over the by-value struct
// element families (System.DateTime / System.TimeSpan / System.Guid).
//
// The generated predicate leaf takes the element struct BY VALUE (the
// ParamTypesCpp value-parameter ABI: Il2CppDateTime / Il2CppTimeSpan /
// Il2CppGuid), so every entry below dispatches through that exact signature.
// The element is READ at the source array's real cell width (8 / 8 / 16
// bytes) through the linq_family cell helpers; the result arrays are built by
// LinqNewArray / LinqCellCopy at the same width. A mis-dispatched call is
// rejected by LinqRequireCell instead of reading a foreign width.
//
// GC discipline: the source array and the delegate are rooted across the
// predicate loop (the predicate may allocate -> the collector may compact),
// and an element-returning entry passes the struct BY VALUE -- no managed
// address is ever held across the callback.
//------------------------------------------------------------------------------
namespace {

typedef bool (*PredDtFn)(runtime::Il2CppDateTime);
typedef bool (*PredDtThisFn)(Il2CppObject*, runtime::Il2CppDateTime);
typedef bool (*PredDtIdxFn)(runtime::Il2CppDateTime, int32_t);
typedef bool (*PredDtIdxThisFn)(Il2CppObject*, runtime::Il2CppDateTime, int32_t);
typedef bool (*PredTsFn)(runtime::Il2CppTimeSpan);
typedef bool (*PredTsThisFn)(Il2CppObject*, runtime::Il2CppTimeSpan);
typedef bool (*PredTsIdxFn)(runtime::Il2CppTimeSpan, int32_t);
typedef bool (*PredTsIdxThisFn)(Il2CppObject*, runtime::Il2CppTimeSpan, int32_t);
typedef bool (*PredGuidFn)(Il2CppGuid);
typedef bool (*PredGuidThisFn)(Il2CppObject*, Il2CppGuid);
typedef bool (*PredGuidIdxFn)(Il2CppGuid, int32_t);
typedef bool (*PredGuidIdxThisFn)(Il2CppObject*, Il2CppGuid, int32_t);
// impl-listvtops: the 16-byte System.DateTimeOffset predicate family (the
// ARRAY surface had no entry at all, so a DateTimeOffset[] source dispatched
// its predicates into the Guid kernels -- a 16-byte cell of the WRONG family).
typedef bool (*PredDtoFn)(runtime::Il2CppDateTimeOffset);
typedef bool (*PredDtoThisFn)(Il2CppObject*, runtime::Il2CppDateTimeOffset);
typedef bool (*PredDtoIdxFn)(runtime::Il2CppDateTimeOffset, int32_t);
typedef bool (*PredDtoIdxThisFn)(Il2CppObject*, runtime::Il2CppDateTimeOffset, int32_t);

bool InvokePredDt(Il2CppDelegate* d, const runtime::Il2CppDateTime& v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDtThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredDtFn>(d->methodPtr)(v);
}
bool InvokePredDtIdx(Il2CppDelegate* d, const runtime::Il2CppDateTime& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDtIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredDtIdxFn>(d->methodPtr)(v, index);
}
bool InvokePredTs(Il2CppDelegate* d, const runtime::Il2CppTimeSpan& v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredTsThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredTsFn>(d->methodPtr)(v);
}
bool InvokePredTsIdx(Il2CppDelegate* d, const runtime::Il2CppTimeSpan& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredTsIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredTsIdxFn>(d->methodPtr)(v, index);
}
bool InvokePredGuid(Il2CppDelegate* d, const Il2CppGuid& v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredGuidThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredGuidFn>(d->methodPtr)(v);
}
bool InvokePredGuidIdx(Il2CppDelegate* d, const Il2CppGuid& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredGuidIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredGuidIdxFn>(d->methodPtr)(v, index);
}
bool InvokePredDto(Il2CppDelegate* d, const runtime::Il2CppDateTimeOffset& v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDtoThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredDtoFn>(d->methodPtr)(v);
}
bool InvokePredDtoIdx(Il2CppDelegate* d, const runtime::Il2CppDateTimeOffset& v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDtoIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredDtoIdxFn>(d->methodPtr)(v, index);
}

// net48 Where: one predicate invocation per element, first-appearance order.
template <typename T>
Il2CppArray* WhereValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> keep;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const T v = read(src, i);
        if (invoke(d, v)) keep.push_back(i);
    }

    Il2CppArray* out = LinqNewArray(cell, static_cast<int32_t>(keep.size()));
    roots.Add(reinterpret_cast<void**>(&out));
    for (size_t i = 0; i < keep.size(); ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, static_cast<int32_t>(i)),
                     nullptr, src, keep[i], cell);
    }
    return out;
}

template <typename T>
Il2CppArray* WhereValueFamilyIdx(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&, int32_t))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    std::vector<int32_t> keep;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const T v = read(src, i);
        if (invoke(d, v, i)) keep.push_back(i);
    }

    Il2CppArray* out = LinqNewArray(cell, static_cast<int32_t>(keep.size()));
    roots.Add(reinterpret_cast<void**>(&out));
    for (size_t i = 0; i < keep.size(); ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, static_cast<int32_t>(i)),
                     nullptr, src, keep[i], cell);
    }
    return out;
}

// net48 TakeWhile: the longest PREFIX satisfying the predicate; the first
// failing element is excluded and the predicate is not called again.
template <typename T>
Il2CppArray* TakeWhileValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t w = 0;
    while (w < src->length)
    {
        const T v = read(src, w);
        if (!invoke(d, v)) break;
        ++w;
    }

    Il2CppArray* out = LinqNewArray(cell, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, i), nullptr, src, i, cell);
    }
    return out;
}

template <typename T>
Il2CppArray* TakeWhileValueFamilyIdx(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&, int32_t))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t w = 0;
    while (w < src->length)
    {
        const T v = read(src, w);
        if (!invoke(d, v, w)) break;
        ++w;
    }

    Il2CppArray* out = LinqNewArray(cell, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, i), nullptr, src, i, cell);
    }
    return out;
}

// net48 SkipWhile: everything after the prefix; the first failing element and
// the rest are kept, and the predicate is never called after the prefix ends.
template <typename T>
Il2CppArray* SkipWhileValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t s = 0;
    while (s < src->length)
    {
        const T v = read(src, s);
        if (!invoke(d, v)) break;
        ++s;
    }
    const int32_t n = src->length - s;

    Il2CppArray* out = LinqNewArray(cell, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, i), nullptr, src, s + i, cell);
    }
    return out;
}

template <typename T>
Il2CppArray* SkipWhileValueFamilyIdx(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&, int32_t))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t s = 0;
    while (s < src->length)
    {
        const T v = read(src, s);
        if (!invoke(d, v, s)) break;
        ++s;
    }
    const int32_t n = src->length - s;

    Il2CppArray* out = LinqNewArray(cell, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        LinqCellCopy(il2cpp_array_get_elem_ptr(out, i), nullptr, src, s + i, cell);
    }
    return out;
}

template <typename T>
bool AllValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const T v = read(src, i);
        if (!invoke(d, v)) return false;
    }
    return true;
}

template <typename T>
int32_t CountPredValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t n = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const T v = read(src, i);
        if (invoke(d, v)) ++n;
    }
    return n;
}

template <typename T>
bool AnyPredValueFamily(Il2CppArray* src, Il2CppObject* pred, int32_t cell,
    T (*read)(Il2CppArray*, int32_t), bool (*invoke)(Il2CppDelegate*, const T&))
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    LinqRequireCell(src, cell);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    for (int32_t i = 0; i < src->length; ++i)
    {
        const T v = read(src, i);
        if (invoke(d, v)) return true;
    }
    return false;
}

template <typename T>
T FirstValueFamily(Il2CppArray* src, int32_t cell, T (*read)(Il2CppArray*, int32_t))
{
    RequireArray(src);
    LinqRequireCell(src, cell);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
        return T{};
    }
    return read(src, 0);
}

template <typename T>
T FirstOrDefaultValueFamily(Il2CppArray* src, int32_t cell, T (*read)(Il2CppArray*, int32_t))
{
    RequireArray(src);
    LinqRequireCell(src, cell);
    if (src->length == 0) return T{};
    return read(src, 0);
}

template <typename T>
T LastValueFamily(Il2CppArray* src, int32_t cell, T (*read)(Il2CppArray*, int32_t))
{
    RequireArray(src);
    LinqRequireCell(src, cell);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
        return T{};
    }
    return read(src, src->length - 1);
}

template <typename T>
T LastOrDefaultValueFamily(Il2CppArray* src, int32_t cell, T (*read)(Il2CppArray*, int32_t))
{
    RequireArray(src);
    LinqRequireCell(src, cell);
    if (src->length == 0) return T{};
    return read(src, src->length - 1);
}

} // namespace

#define IL2CPP_LINQ_DEF_VT_PRED_ENTRIES(FAM, CELL, TYPE, READ, INVOKE, INVOKE_IDX)              \
Il2CppArray* il2cpp_linq_where_##FAM(Il2CppArray* src, Il2CppObject* pred)                      \
{                                                                                               \
    return WhereValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                               \
}                                                                                               \
Il2CppArray* il2cpp_linq_where_idx_##FAM(Il2CppArray* src, Il2CppObject* pred)                  \
{                                                                                               \
    return WhereValueFamilyIdx<TYPE>(src, pred, CELL, READ, INVOKE_IDX);                        \
}                                                                                               \
Il2CppArray* il2cpp_linq_take_while_##FAM(Il2CppArray* src, Il2CppObject* pred)                 \
{                                                                                               \
    return TakeWhileValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                           \
}                                                                                               \
Il2CppArray* il2cpp_linq_take_while_idx_##FAM(Il2CppArray* src, Il2CppObject* pred)             \
{                                                                                               \
    return TakeWhileValueFamilyIdx<TYPE>(src, pred, CELL, READ, INVOKE_IDX);                    \
}                                                                                               \
Il2CppArray* il2cpp_linq_skip_while_##FAM(Il2CppArray* src, Il2CppObject* pred)                 \
{                                                                                               \
    return SkipWhileValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                           \
}                                                                                               \
Il2CppArray* il2cpp_linq_skip_while_idx_##FAM(Il2CppArray* src, Il2CppObject* pred)             \
{                                                                                               \
    return SkipWhileValueFamilyIdx<TYPE>(src, pred, CELL, READ, INVOKE_IDX);                    \
}                                                                                               \
bool il2cpp_linq_all_##FAM(Il2CppArray* src, Il2CppObject* pred)                                \
{                                                                                               \
    return AllValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                                 \
}                                                                                               \
int32_t il2cpp_linq_count_pred_##FAM(Il2CppArray* src, Il2CppObject* pred)                      \
{                                                                                               \
    return CountPredValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                           \
}                                                                                               \
bool il2cpp_linq_any_pred_##FAM(Il2CppArray* src, Il2CppObject* pred)                           \
{                                                                                               \
    return AnyPredValueFamily<TYPE>(src, pred, CELL, READ, INVOKE);                             \
}                                                                                               \
TYPE il2cpp_linq_first_##FAM(Il2CppArray* src)                                                  \
{                                                                                               \
    return FirstValueFamily<TYPE>(src, CELL, READ);                                             \
}                                                                                               \
TYPE il2cpp_linq_first_or_default_##FAM(Il2CppArray* src)                                       \
{                                                                                               \
    return FirstOrDefaultValueFamily<TYPE>(src, CELL, READ);                                    \
}                                                                                               \
TYPE il2cpp_linq_last_##FAM(Il2CppArray* src)                                                   \
{                                                                                               \
    return LastValueFamily<TYPE>(src, CELL, READ);                                              \
}                                                                                               \
TYPE il2cpp_linq_last_or_default_##FAM(Il2CppArray* src)                                        \
{                                                                                               \
    return LastOrDefaultValueFamily<TYPE>(src, CELL, READ);                                     \
}

IL2CPP_LINQ_DEF_VT_PRED_ENTRIES(dt, IL2CPP_LINQ_CELL_DATETIME, runtime::Il2CppDateTime,
    LinqCellReadDt, InvokePredDt, InvokePredDtIdx)
IL2CPP_LINQ_DEF_VT_PRED_ENTRIES(ts, IL2CPP_LINQ_CELL_TIMESPAN, runtime::Il2CppTimeSpan,
    LinqCellReadTs, InvokePredTs, InvokePredTsIdx)
IL2CPP_LINQ_DEF_VT_PRED_ENTRIES(guid, IL2CPP_LINQ_CELL_GUID, Il2CppGuid,
    LinqCellReadGuid, InvokePredGuid, InvokePredGuidIdx)
// impl-listvtops: the System.DateTimeOffset ARRAY predicate family (the
// counterpart of the List<DateTimeOffset> surface; without it a DateTimeOffset[]
// source bound the Guid kernels and tripped LinqRequireCell at runtime).
IL2CPP_LINQ_DEF_VT_PRED_ENTRIES(dtoffset, IL2CPP_LINQ_CELL_DTOFFSET,
    runtime::Il2CppDateTimeOffset, LinqCellReadDto, InvokePredDto, InvokePredDtoIdx)

#undef IL2CPP_LINQ_DEF_VT_PRED_ENTRIES

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

// ToList<Guid> -- a 16-byte value element boxed through icall_box_guid (see
// listobj.h). The Guid element carries no separate image: the array cell and
// the boxed payload are the same 16 bytes.
Il2CppObject* il2cpp_linq_to_list_guid(Il2CppArray* src)
{
    RequireArray(src);
    if (src->isRefElem || src->elemSize != static_cast<int32_t>(sizeof(Il2CppGuid)))
    {
        il2cpp_throw_not_supported_exception(
            "ToList<Guid> needs a 16-byte value-element source array.");
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppGuid elem;
        std::memcpy(&elem, il2cpp_array_get_elem_ptr(src, i), sizeof(elem));
        il2cpp_list_obj_add_guid(list, reinterpret_cast<const uint8_t*>(&elem));
    }
    return list;
}

// delta 2: ToList<System.DateTimeOffset>. The listobj element is a reference
// slot, so each 16-byte DateTimeOffset cell is BOXED on entry through
// icall_box_date_time_offset (nullable.cpp) -- the Guid/Decimal contract.
// GC SAFETY: the source and the growing list are rooted across every add.
Il2CppObject* il2cpp_linq_to_list_dtoffset(Il2CppArray* src)
{
    RequireArray(src);
    RequireDtoffsetFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppObject* list = il2cpp_list_obj_create();
    roots.Add(reinterpret_cast<void**>(&list));

    for (int32_t i = 0; i < src->length; ++i)
    {
        runtime::Il2CppDateTimeOffset elem;
        std::memcpy(&elem, il2cpp_array_get_elem_ptr(src, i), sizeof(elem));
        il2cpp_list_obj_add_dtoffset(list, elem);
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
// P0LINQ1 (D2) follow-up: the SELECTOR aggregates with a DECIMAL projection
// (net48 Sum/Min/Max/Average<TSource>(source, Func<TSource,decimal>)) over an
// INT32 source. The 16-byte decimal result rides an explicit result slot (the
// decimal T3 contract -- the slot is the FIRST parameter); the selector
// returns the decimal BY VALUE (the SelectToDecimal callback ABI).
// GC SAFETY: the delegate and the source are rooted across every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDecimal (*SelDecProjFn)(int32_t v);
typedef runtime::Il2CppDecimal (*SelDecProjThisFn)(Il2CppObject* self, int32_t v);

static runtime::Il2CppDecimal InvokeSelDecProj(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<SelDecProjThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<SelDecProjFn>(d->methodPtr)(v);
}

void il2cpp_linq_sum_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecProj(d, I4At(src, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
}

void il2cpp_linq_min_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelDecProj(d, I4At(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecProj(d, I4At(src, i));
        if (runtime::icall_decimal_compare(&elem, &best) < 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_max_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelDecProj(d, I4At(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecProj(d, I4At(src, i));
        if (runtime::icall_decimal_compare(&elem, &best) > 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_average_sel_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecProj(d, I4At(src, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
    runtime::Il2CppDecimal count;
    std::memset(&count, 0, sizeof(runtime::Il2CppDecimal));
    runtime::icall_decimal_ctor_int32(&count, src->length);
    runtime::icall_decimal_div(result, result, &count);
}

//------------------------------------------------------------------------------
// impl-linq2 (B-1): the selector aggregates with a DECIMAL projection over a
// REFERENCE source (net48 Sum/Min/Max/Average<TSource>(source,
// Func<TSource,decimal>) -- Enumerable.cs:1528/1735/1939/2133). The selector
// takes the reference element and returns the decimal BY VALUE (the
// SelRefDecKey ABI -- the ref-source GroupBy element-selector precedent);
// the 16-byte result rides an explicit result slot (the decimal T3 contract).
// GC SAFETY: the delegate and the source are rooted across every callback.
//------------------------------------------------------------------------------
void il2cpp_linq_sum_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelRefDecKey(d, RefAt(src, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
}

void il2cpp_linq_min_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelRefDecKey(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelRefDecKey(d, RefAt(src, i));
        if (runtime::icall_decimal_compare(&elem, &best) < 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_max_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelRefDecKey(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelRefDecKey(d, RefAt(src, i));
        if (runtime::icall_decimal_compare(&elem, &best) > 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_average_sel_ref_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelRefDecKey(d, RefAt(src, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
    runtime::Il2CppDecimal count;
    std::memset(&count, 0, sizeof(runtime::Il2CppDecimal));
    runtime::icall_decimal_ctor_int32(&count, src->length);
    runtime::icall_decimal_div(result, result, &count);
}

//------------------------------------------------------------------------------
// P0LINQ1 (D2) follow-up: Aggregate with a DECIMAL accumulator (net48
// Aggregate<TSource,decimal>(source, seed, Func<decimal,TSource,decimal>)).
// The 16-byte decimal result rides an explicit result slot (the decimal T3
// contract -- the slot is the FIRST parameter); the seed crosses as a decimal
// value slot and the callback returns the decimal BY VALUE. An empty source
// returns the seed unchanged (net48).
// GC SAFETY: the delegate and the source are rooted across every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDecimal (*DecAccFn)(runtime::Il2CppDecimal acc, int32_t elem);
typedef runtime::Il2CppDecimal (*DecAccThisFn)(Il2CppObject* self, runtime::Il2CppDecimal acc, int32_t elem);

static runtime::Il2CppDecimal FoldDecimalAccI4Src(Il2CppArray* src,
                                                  const runtime::Il2CppDecimal* seed,
                                                  Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    runtime::Il2CppDecimal acc = *seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = I4At(src, i);
        acc = d->target != nullptr
            ? reinterpret_cast<DecAccThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<DecAccFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

void il2cpp_linq_aggregate_seed_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                   const runtime::Il2CppDecimal* seed, Il2CppObject* func)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(func, "func");
    const runtime::Il2CppDecimal acc = FoldDecimalAccI4Src(src, seed, d);
    std::memcpy(result, &acc, sizeof(runtime::Il2CppDecimal));
}

//------------------------------------------------------------------------------
// impl-linq2 (A): the DECIMAL-SOURCE Aggregate family (net48 Aggregate
// over a System.Decimal source -- Enumerable.cs:1379/1391/1399). The decimal
// cells are read BY VALUE (DecAt) and the callback folds decimal x decimal ->
// decimal; the 16-byte result rides the explicit result slot (the decimal T3
// contract -- the slot is the FIRST parameter). The old mapping answered null
// for the no-seed form and ran the int32-reader fold (I4At) over the 16-byte
// decimal cells (a silent wrong value).
// GC SAFETY: the delegate and the source are rooted across every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDecimal (*DecBinDecFn)(runtime::Il2CppDecimal acc, runtime::Il2CppDecimal elem);
typedef runtime::Il2CppDecimal (*DecBinDecThisFn)(Il2CppObject* self, runtime::Il2CppDecimal acc, runtime::Il2CppDecimal elem);

static runtime::Il2CppDecimal InvokeDecBinDec(Il2CppDelegate* d, runtime::Il2CppDecimal acc,
                                              runtime::Il2CppDecimal elem)
{
    return d->target != nullptr
        ? reinterpret_cast<DecBinDecThisFn>(d->methodPtr)(d->target, acc, elem)
        : reinterpret_cast<DecBinDecFn>(d->methodPtr)(acc, elem);
}

static runtime::Il2CppDecimal FoldDecimalAccDecSrc(Il2CppArray* src,
                                                   const runtime::Il2CppDecimal* seed,
                                                   Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    runtime::Il2CppDecimal acc = *seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        acc = InvokeDecBinDec(d, acc, *DecAt(src, i));
    }
    return acc;
}

void il2cpp_linq_aggregate_seed_dec_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,
                                        const runtime::Il2CppDecimal* seed, Il2CppObject* func)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(func, "func");
    const runtime::Il2CppDecimal acc = FoldDecimalAccDecSrc(src, seed, d);
    std::memcpy(result, &acc, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_aggregate_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* func)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(func, "func");
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    runtime::Il2CppDecimal acc = *DecAt(src, 0);
    for (int32_t i = 1; i < src->length; ++i)
    {
        acc = InvokeDecBinDec(d, acc, *DecAt(src, i));
    }
    std::memcpy(result, &acc, sizeof(runtime::Il2CppDecimal));
}

//------------------------------------------------------------------------------
// impl-linq2 (A): Aggregate(seed, func, resultSelector) with a DECIMAL
// accumulator (net48 Aggregate<TSource,TAccumulate,TResult> -- Enumerable.cs
// :1399). The fold runs first (int32 source: DecAccFn ABI; decimal source:
// DecBinDecFn ABI), then the result selector runs ONCE on the final decimal
// accumulator. TResult picks the projection ABI (the managed leaf returns
// int32_t / int64_t / float / double / Il2CppDecimal-by-value / reference).
// The old mapping answered the 3-parameter seed kernel for this arity (the
// resultSelector was dropped and the emitter consumed one stack value too
// few -- a stack desync).
//------------------------------------------------------------------------------
#define IL2CPP_LINQ_AGG_DEC_RS(NAME, FOLDEXPR)                                     \
    int32_t NAME##_rs_i4(Il2CppArray* src, const runtime::Il2CppDecimal* seed,     \
                         Il2CppObject* func, Il2CppObject* resSel)                 \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef int32_t (*ResFn)(runtime::Il2CppDecimal);                          \
        typedef int32_t (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal);       \
        return rd->target != nullptr                                               \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc);                         \
    }                                                                              \
    int64_t NAME##_rs_i8(Il2CppArray* src, const runtime::Il2CppDecimal* seed,     \
                         Il2CppObject* func, Il2CppObject* resSel)                 \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef int64_t (*ResFn)(runtime::Il2CppDecimal);                          \
        typedef int64_t (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal);       \
        return rd->target != nullptr                                               \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc);                         \
    }                                                                              \
    double NAME##_rs_f(Il2CppArray* src, const runtime::Il2CppDecimal* seed,       \
                       Il2CppObject* func, Il2CppObject* resSel)                   \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef float (*ResFn)(runtime::Il2CppDecimal);                            \
        typedef float (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal);         \
        return static_cast<double>(rd->target != nullptr                           \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc));                        \
    }                                                                              \
    double NAME##_rs_d(Il2CppArray* src, const runtime::Il2CppDecimal* seed,       \
                       Il2CppObject* func, Il2CppObject* resSel)                   \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef double (*ResFn)(runtime::Il2CppDecimal);                           \
        typedef double (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal);        \
        return rd->target != nullptr                                               \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc);                         \
    }                                                                              \
    Il2CppObject* NAME##_rs_ref(Il2CppArray* src, const runtime::Il2CppDecimal* seed, \
                                Il2CppObject* func, Il2CppObject* resSel)          \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef Il2CppObject* (*ResFn)(runtime::Il2CppDecimal);                    \
        typedef Il2CppObject* (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal); \
        return rd->target != nullptr                                               \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc);                         \
    }                                                                              \
    void NAME##_rs_dec(runtime::Il2CppDecimal* result, Il2CppArray* src,           \
                       const runtime::Il2CppDecimal* seed, Il2CppObject* func,     \
                       Il2CppObject* resSel)                                       \
    {                                                                              \
        RequireArray(src);                                                         \
        Il2CppDelegate* fd = RequireCallback(func, "func");                        \
        Il2CppDelegate* rd = RequireCallback(resSel, "resultSelector");            \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        const runtime::Il2CppDecimal acc = FOLDEXPR;                               \
        typedef runtime::Il2CppDecimal (*ResFn)(runtime::Il2CppDecimal);           \
        typedef runtime::Il2CppDecimal (*ResThisFn)(Il2CppObject*, runtime::Il2CppDecimal); \
        const runtime::Il2CppDecimal r = rd->target != nullptr                     \
            ? reinterpret_cast<ResThisFn>(rd->methodPtr)(rd->target, acc)          \
            : reinterpret_cast<ResFn>(rd->methodPtr)(acc);                         \
        std::memcpy(result, &r, sizeof(runtime::Il2CppDecimal));                   \
    }

//------------------------------------------------------------------------------
// impl-aggvt: Aggregate with a DECIMAL accumulator over a NON-decimal
// VALUE-TYPE source (System.DateTime / System.TimeSpan / System.Guid).
//
// net48's Aggregate<TSource,TAccumulate>(source, seed, Func<TAcc,TSource,TAcc>)
// takes TSource BY VALUE (ParamTypesCpp: Il2CppDateTime / Il2CppTimeSpan /
// Il2CppGuid), so the callback leaf is
//   Il2CppDecimal (*)(Il2CppDecimal, DateTime|TimeSpan|Guid)
// and the element MUST be read at the SOURCE ARRAY's real cell width
// (8 / 8 / 16 bytes). The legacy int32 reader (FoldDecimalAccI4Src) sliced 4
// bytes out of those cells: a SILENT wrong value for the Days fold and a wild
// (out-of-bounds) read for the packed DateTime cell. Each entry validates its
// own cell width through LinqRequireCell -- a mis-dispatched call is rejected
// instead of reading a foreign width.
//
// The 16-byte decimal result keeps the T3 contract (explicit result slot as
// the FIRST parameter; the seed crosses by pointer) and the 4-arity
// (seed, func, resultSelector) forms reuse IL2CPP_LINQ_AGG_DEC_RS below.
// GC SAFETY: the delegate and the source are rooted across every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDecimal (*DecAccDtFn)(runtime::Il2CppDecimal acc, runtime::Il2CppDateTime elem);
typedef runtime::Il2CppDecimal (*DecAccDtThisFn)(Il2CppObject* self, runtime::Il2CppDecimal acc,
                                                 runtime::Il2CppDateTime elem);
typedef runtime::Il2CppDecimal (*DecAccTsFn)(runtime::Il2CppDecimal acc, runtime::Il2CppTimeSpan elem);
typedef runtime::Il2CppDecimal (*DecAccTsThisFn)(Il2CppObject* self, runtime::Il2CppDecimal acc,
                                                 runtime::Il2CppTimeSpan elem);
typedef runtime::Il2CppDecimal (*DecAccGuidFn)(runtime::Il2CppDecimal acc, Il2CppGuid elem);
typedef runtime::Il2CppDecimal (*DecAccGuidThisFn)(Il2CppObject* self, runtime::Il2CppDecimal acc,
                                                   Il2CppGuid elem);

#define IL2CPP_LINQ_DEF_DECACC_VT(FAM, FOLD, CELL, TYPE, READ, FN, THISFN)                    \
    static runtime::Il2CppDecimal FoldDecimalAcc##FOLD##Src(Il2CppArray* src,                 \
                                                           const runtime::Il2CppDecimal* seed,\
                                                           Il2CppDelegate* d)                 \
    {                                                                                         \
        LinqRequireCell(src, CELL);                                                           \
        RootScope roots;                                                                      \
        roots.Add(reinterpret_cast<void**>(&d));                                              \
        roots.Add(reinterpret_cast<void**>(&src));                                            \
        runtime::Il2CppDecimal acc = *seed;                                                   \
        for (int32_t i = 0; i < src->length; ++i)                                             \
        {                                                                                     \
            const TYPE elem = READ(src, i);                                                   \
            acc = d->target != nullptr                                                        \
                ? reinterpret_cast<THISFN>(d->methodPtr)(d->target, acc, elem)                 \
                : reinterpret_cast<FN>(d->methodPtr)(acc, elem);                              \
        }                                                                                     \
        return acc;                                                                           \
    }                                                                                         \
    void il2cpp_linq_aggregate_seed_##FAM##_dec(runtime::Il2CppDecimal* result,               \
                                                Il2CppArray* src,                             \
                                                const runtime::Il2CppDecimal* seed,           \
                                                Il2CppObject* func)                           \
    {                                                                                         \
        RequireArray(src);                                                                    \
        Il2CppDelegate* d = RequireCallback(func, "func");                                    \
        const runtime::Il2CppDecimal acc = FoldDecimalAcc##FOLD##Src(src, seed, d);           \
        std::memcpy(result, &acc, sizeof(runtime::Il2CppDecimal));                            \
    }

IL2CPP_LINQ_DEF_DECACC_VT(dt, Dt, IL2CPP_LINQ_CELL_DATETIME, runtime::Il2CppDateTime,
    LinqCellReadDt, DecAccDtFn, DecAccDtThisFn)
IL2CPP_LINQ_DEF_DECACC_VT(ts, Ts, IL2CPP_LINQ_CELL_TIMESPAN, runtime::Il2CppTimeSpan,
    LinqCellReadTs, DecAccTsFn, DecAccTsThisFn)
IL2CPP_LINQ_DEF_DECACC_VT(guid, Guid, IL2CPP_LINQ_CELL_GUID, Il2CppGuid,
    LinqCellReadGuid, DecAccGuidFn, DecAccGuidThisFn)

#undef IL2CPP_LINQ_DEF_DECACC_VT

IL2CPP_LINQ_AGG_DEC_RS(il2cpp_linq_aggregate_seed_dec,
                       FoldDecimalAccI4Src(src, seed, fd))
IL2CPP_LINQ_AGG_DEC_RS(il2cpp_linq_aggregate_seed_decsrc,
                       FoldDecimalAccDecSrc(src, seed, fd))
// impl-aggvt: the value-type SOURCE cell families (8 / 8 / 16-byte cells).
IL2CPP_LINQ_AGG_DEC_RS(il2cpp_linq_aggregate_seed_dt_dec,
                       FoldDecimalAccDtSrc(src, seed, fd))
IL2CPP_LINQ_AGG_DEC_RS(il2cpp_linq_aggregate_seed_ts_dec,
                       FoldDecimalAccTsSrc(src, seed, fd))
IL2CPP_LINQ_AGG_DEC_RS(il2cpp_linq_aggregate_seed_guid_dec,
                       FoldDecimalAccGuidSrc(src, seed, fd))
#undef IL2CPP_LINQ_AGG_DEC_RS

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
// The DECIMAL-source Select with a VALUE result (net48
// Select<decimal,TResult> with a non-decimal TResult): int32 / int64 / double /
// float. The element crosses as a 16-byte decimal cell BY VALUE.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_dec_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(4, kElemKindI4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelDecI4(d, *DecAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_dec_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindI8, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelDecI8(d, *DecAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_dec_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindD, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = InvokeSelDecD(d, *DecAt(src, i));
    return out;
}

Il2CppArray* il2cpp_linq_select_dec_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindF, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
            for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            static_cast<double>(InvokeSelDecF(d, *DecAt(src, i)));
    return out;
}

//------------------------------------------------------------------------------
// p0grp: Select over a DECIMAL source with a REFERENCE result (net48
// Select<decimal,TResult> with a reference TResult, e.g. x => x.ToString()).
// The selector returns Il2CppObject*; the result is a fresh reference array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_dec_ref(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(void*), true, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* r = InvokeSelDecRef(d, *DecAt(src, i));
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
        *slot = r;
    }
    return out;
}

//------------------------------------------------------------------------------
// p0grp: Where over a DECIMAL source (net48 Where<decimal>(source,
// Func<decimal,bool>)). The predicate takes the decimal BY VALUE; the result is
// a fresh decimal[] with the matching elements (first-appearance order).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_where_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RequireDecimalFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // SINGLE predicate pass: fill a src-length scratch, then materialize the
    // exact-size result (net48 Where is lazy but the runtime materializes).
    Il2CppArray* scratch = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, src->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(scratch, w)) = v;
            ++w;
        }
    }
    if (w == src->length)
    {
        return scratch; // already exact
    }
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) =
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(scratch, i));
    }
    return out;
}

//------------------------------------------------------------------------------
// p0grp: the DECIMAL-source element operators (net48 First / Last / ElementAt /
// Contains / Reverse / Take / Skip). Each reads the 16-byte decimal cell; the
// element-returning entries return the decimal BY VALUE (the decimal T3
// contract), the range entries materialize a fresh decimal[].
//------------------------------------------------------------------------------
void il2cpp_linq_first_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_last_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memcpy(result, DecAt(src, src->length - 1), sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_element_at_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, int32_t index)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (index < 0 || index >= src->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
        return;
    }
    std::memcpy(result, DecAt(src, index), sizeof(runtime::Il2CppDecimal));
}

bool il2cpp_linq_contains_dec(Il2CppArray* src, runtime::Il2CppDecimal value)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (runtime::icall_decimal_compare(DecAt(src, i), &value) == 0)
            return true;
    }
    return false;
}

Il2CppArray* il2cpp_linq_reverse_dec(Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, src->length - 1 - i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_take_dec(Il2CppArray* src, int32_t count)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    if (count < 0) count = 0;
    if (count > src->length) count = src->length;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, count);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < count; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_skip_dec(Il2CppArray* src, int32_t count)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    if (count < 0) count = 0;
    if (count > src->length) count = src->length;
    const int32_t n = src->length - count;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, count + i);
    }
    return out;
}

//------------------------------------------------------------------------------
// impl-linq2 (B-3): the remaining DECIMAL-source element / predicate operators
// (net48 FirstOrDefault / LastOrDefault / Single / SingleOrDefault and their
// Func<decimal,bool> predicate forms, LongCount, Count(pred), Any(pred), All,
// TakeWhile / SkipWhile -- Enumerable.cs:1065/1079/1088/1109/1124/1145/1157/
// 1176/1194/1213/1341/1352/605/618/647/661 applied to a decimal source). The
// default(decimal) cell is the zero decimal. The predicate ABI is the shared
// InvokeSelDecBool (bool(*)(decimal)); the element-returning entries follow
// the decimal T3 result-slot contract.
//------------------------------------------------------------------------------
void il2cpp_linq_first_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length > 0)
    {
        std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
        return;
    }
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_last_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length > 0)
    {
        std::memcpy(result, DecAt(src, src->length - 1), sizeof(runtime::Il2CppDecimal));
        return;
    }
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_first_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            std::memcpy(result, &v, sizeof(runtime::Il2CppDecimal));
            return;
        }
    }
    il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
}

void il2cpp_linq_first_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            std::memcpy(result, &v, sizeof(runtime::Il2CppDecimal));
            return;
        }
    }
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_last_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    // net48 Last(pred) walks FORWARD keeping the last match.
    bool found = false;
    runtime::Il2CppDecimal best{};
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            best = v;
            found = true;
        }
    }
    if (!found)
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_last_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            std::memcpy(result, &v, sizeof(runtime::Il2CppDecimal));
        }
    }
}

void il2cpp_linq_single_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    if (src->length > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_single_or_default_dec(runtime::Il2CppDecimal* result, Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length > 1)
        il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    if (src->length == 1)
        std::memcpy(result, DecAt(src, 0), sizeof(runtime::Il2CppDecimal));
    else
        std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_single_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    // net48 Single(pred) walks every element and counts the matches.
    bool found = false;
    runtime::Il2CppDecimal best{};
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            if (found)
                il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
            best = v;
            found = true;
        }
    }
    if (!found)
        il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_single_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    bool found = false;
    runtime::Il2CppDecimal best{};
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        if (InvokeSelDecBool(d, v))
        {
            if (found)
                il2cpp_throw_invalid_operation_exception("Sequence contains more than one matching element");
            best = v;
            found = true;
        }
    }
    if (found)
        std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
    else
        std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
}

int32_t il2cpp_linq_count_pred_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int32_t n = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (InvokeSelDecBool(d, *DecAt(src, i))) ++n;
    }
    return n;
}

bool il2cpp_linq_any_pred_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (InvokeSelDecBool(d, *DecAt(src, i))) return true;
    }
    return false;
}

bool il2cpp_linq_all_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (!InvokeSelDecBool(d, *DecAt(src, i))) return false;
    }
    return true;
}

int64_t il2cpp_linq_long_count_dec(Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    return static_cast<int64_t>(src->length);
}

Il2CppArray* il2cpp_linq_take_while_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    // net48 TakeWhile: the longest PREFIX satisfying the predicate.
    int32_t w = 0;
    while (w < src->length && InvokeSelDecBool(d, *DecAt(src, w))) ++w;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_skip_while_dec(Il2CppArray* src, Il2CppObject* pred)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    // net48 SkipWhile: everything after the prefix; the first failing element
    // is KEPT and the predicate is never called again.
    int32_t s = 0;
    while (s < src->length && InvokeSelDecBool(d, *DecAt(src, s))) ++s;
    const int32_t n = src->length - s;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, s + i);
    }
    return out;
}

//------------------------------------------------------------------------------
// p0grp: the DECIMAL-source set / concat / equality operators (net48 Distinct /
// Except / Concat / DefaultIfEmpty / SequenceEqual). The decimal equality is
// icall_decimal_compare == 0 (net48's decimal.Equals).
//------------------------------------------------------------------------------
static bool DecEquals(const runtime::Il2CppDecimal& a, const runtime::Il2CppDecimal& b)
{
    return runtime::icall_decimal_compare(&a, &b) == 0;
}

Il2CppArray* il2cpp_linq_distinct_dec(Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t n = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        bool dup = false;
        for (int32_t j = 0; j < n; ++j)
        {
            if (DecEquals(v, *reinterpret_cast<runtime::Il2CppDecimal*>(
                    il2cpp_array_get_elem_ptr(out, j))))
            {
                dup = true;
                break;
            }
        }
        if (!dup)
        {
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(out, n)) = v;
            ++n;
        }
    }
    out->length = n;
    return out;
}

Il2CppArray* il2cpp_linq_except_dec(Il2CppArray* src, Il2CppArray* second)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RequireArray(second);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&second));
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t n = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        bool inSecond = false;
        for (int32_t j = 0; j < second->length; ++j)
        {
            if (DecEquals(v, *DecAt(second, j))) { inSecond = true; break; }
        }
        if (inSecond) continue;
        bool dup = false;
        for (int32_t j = 0; j < n; ++j)
        {
            if (DecEquals(v, *reinterpret_cast<runtime::Il2CppDecimal*>(
                    il2cpp_array_get_elem_ptr(out, j))))
            {
                dup = true;
                break;
            }
        }
        if (!dup)
        {
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(out, n)) = v;
            ++n;
        }
    }
    out->length = n;
    return out;
}

Il2CppArray* il2cpp_linq_concat_dec(Il2CppArray* src, Il2CppArray* second)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RequireArray(second);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&second));
    const int32_t n = src->length + second->length;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, i)) = *DecAt(src, i);
    }
    for (int32_t i = 0; i < second->length; ++i)
    {
        *reinterpret_cast<runtime::Il2CppDecimal*>(
            il2cpp_array_get_elem_ptr(out, src->length + i)) = *DecAt(second, i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_default_if_empty_dec(Il2CppArray* src, runtime::Il2CppDecimal defaultValue)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length != 0) return src;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, 1);
    *reinterpret_cast<runtime::Il2CppDecimal*>(il2cpp_array_get_elem_ptr(out, 0)) = defaultValue;
    return out;
}

// impl-linq2 (B-3): the source-only DefaultIfEmpty<decimal>() -- the empty
// source yields the single default(decimal) (zero) cell. The old mapping
// pointed the 1-arity call at the 2-argument kernel (an under-arg'd call).
Il2CppArray* il2cpp_linq_default_if_empty1_dec(Il2CppArray* src)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    if (src->length != 0) return src;
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, 1);
    std::memset(il2cpp_array_get_elem_ptr(out, 0), 0, sizeof(runtime::Il2CppDecimal));
    return out;
}

bool il2cpp_linq_sequence_equal_dec(Il2CppArray* src, Il2CppArray* second)
{
    RequireArray(src);
    RequireDecimalFamily(src);
    RequireArray(second);
    if (src->length != second->length) return false;
    for (int32_t i = 0; i < src->length; ++i)
    {
        if (!DecEquals(*DecAt(src, i), *DecAt(second, i))) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// p0linq4 (D3) follow-up: the INT32-source Select with a DateTime / TimeSpan /
// Guid result (net48 Select<int,TResult>). The selector returns the struct BY
// VALUE (the same convention SelectToDecimal relies on) and the output cell
// carries the raw struct image (DateTime: packed ticks + kind; TimeSpan: the
// ticks; Guid: the 16 bytes).
// GC SAFETY: the delegate, the source and the result are rooted across the
// per-element callback; the slot address is re-derived after every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDateTime (*SelI4ToDtFn)(int32_t v);
typedef runtime::Il2CppDateTime (*SelI4ToDtThisFn)(Il2CppObject* self, int32_t v);
typedef runtime::Il2CppDateTime (*SelI4ToDtIdxFn)(int32_t v, int32_t index);
typedef runtime::Il2CppDateTime (*SelI4ToDtIdxThisFn)(Il2CppObject* self, int32_t v, int32_t index);
typedef runtime::Il2CppTimeSpan (*SelI4ToTsFn)(int32_t v);
typedef runtime::Il2CppTimeSpan (*SelI4ToTsThisFn)(Il2CppObject* self, int32_t v);
typedef runtime::Il2CppTimeSpan (*SelI4ToTsIdxFn)(int32_t v, int32_t index);
typedef runtime::Il2CppTimeSpan (*SelI4ToTsIdxThisFn)(Il2CppObject* self, int32_t v, int32_t index);
typedef Il2CppGuid (*SelI4ToGuidFn)(int32_t v);
typedef Il2CppGuid (*SelI4ToGuidThisFn)(Il2CppObject* self, int32_t v);
typedef Il2CppGuid (*SelI4ToGuidIdxFn)(int32_t v, int32_t index);
typedef Il2CppGuid (*SelI4ToGuidIdxThisFn)(Il2CppObject* self, int32_t v, int32_t index);

Il2CppArray* il2cpp_linq_select_i4_dt(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDateTime)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const runtime::Il2CppDateTime slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToDtThisFn>(d->methodPtr)(d->target, v)
            : reinterpret_cast<SelI4ToDtFn>(d->methodPtr)(v);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppDateTime));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_i4_ts(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppTimeSpan)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const runtime::Il2CppTimeSpan slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToTsThisFn>(d->methodPtr)(d->target, v)
            : reinterpret_cast<SelI4ToTsFn>(d->methodPtr)(v);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppTimeSpan));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_i4_guid(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(Il2CppGuid)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const Il2CppGuid slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToGuidThisFn>(d->methodPtr)(d->target, v)
            : reinterpret_cast<SelI4ToGuidFn>(d->methodPtr)(v);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(Il2CppGuid));
    }
    return out;
}

// The INDEX overloads: the selector receives the element AND its zero-based
// source index.
Il2CppArray* il2cpp_linq_select_idx_i4_dt(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDateTime)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const runtime::Il2CppDateTime slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToDtIdxThisFn>(d->methodPtr)(d->target, v, i)
            : reinterpret_cast<SelI4ToDtIdxFn>(d->methodPtr)(v, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppDateTime));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_ts(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppTimeSpan)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const runtime::Il2CppTimeSpan slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToTsIdxThisFn>(d->methodPtr)(d->target, v, i)
            : reinterpret_cast<SelI4ToTsIdxFn>(d->methodPtr)(v, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppTimeSpan));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_i4_guid(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireInt32Family(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(Il2CppGuid)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = I4At(src, i);
        const Il2CppGuid slot = d->target != nullptr
            ? reinterpret_cast<SelI4ToGuidIdxThisFn>(d->methodPtr)(d->target, v, i)
            : reinterpret_cast<SelI4ToGuidIdxFn>(d->methodPtr)(v, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(Il2CppGuid));
    }
    return out;
}

//------------------------------------------------------------------------------
// delta 2: Select<TResult = System.DateTimeOffset>.
//
// The generated selector returns the 16-byte Il2CppDateTimeOffset BY VALUE
// (the decimal/DateTime/Guid by-value selector convention), so the result
// array is a 16-byte non-reference cell array. ONE core walks the source and
// calls the selector through the SOURCE family's declared parameter ABI:
//   i4 slot  -- int32          i8 -- int64
//   f/d      -- double         ref -- Il2CppObject*
//   decimal  -- Il2CppDecimal  dt -- Il2CppDateTime
//   ts       -- Il2CppTimeSpan guid -- Il2CppGuid
//   dtoffset -- Il2CppDateTimeOffset
// The (element, index) form passes the 0-based source index as well.
// GC SAFETY: the delegate, the source and the result are rooted across the
// per-element callback; the result cell address is re-derived AFTER the call.
//------------------------------------------------------------------------------
namespace {

int32_t ReadDtoSrcI4(Il2CppArray* a, int32_t i) { return I4At(a, i); }
int64_t ReadDtoSrcI8(Il2CppArray* a, int32_t i)
{
    return *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i));
}
double ReadDtoSrcF(Il2CppArray* a, int32_t i)
{
    return static_cast<double>(*reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(a, i)));
}
double ReadDtoSrcD(Il2CppArray* a, int32_t i)
{
    return *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(a, i));
}
Il2CppObject* ReadDtoSrcRef(Il2CppArray* a, int32_t i)
{
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(a, i));
}
runtime::Il2CppDecimal ReadDtoSrcDec(Il2CppArray* a, int32_t i)
{
    return *DecAt(a, i);
}
runtime::Il2CppDateTime ReadDtoSrcDt(Il2CppArray* a, int32_t i)
{
    return runtime::il2cpp_datetime_from_array_bits(
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)));
}
runtime::Il2CppTimeSpan ReadDtoSrcTs(Il2CppArray* a, int32_t i)
{
    return runtime::Il2CppTimeSpan(
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)));
}
Il2CppGuid ReadDtoSrcGuid(Il2CppArray* a, int32_t i)
{
    Il2CppGuid g;
    std::memcpy(g.b, il2cpp_array_get_elem_ptr(a, i), sizeof(g.b));
    return g;
}
runtime::Il2CppDateTimeOffset ReadDtoSrcDto(Il2CppArray* a, int32_t i)
{
    runtime::Il2CppDateTimeOffset v;
    std::memcpy(&v, il2cpp_array_get_elem_ptr(a, i), sizeof(v));
    return v;
}

template <typename Src>
runtime::Il2CppDateTimeOffset InvokeSelDto(Il2CppDelegate* d, Src v)
{
    typedef runtime::Il2CppDateTimeOffset (*Fn)(Src);
    typedef runtime::Il2CppDateTimeOffset (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
runtime::Il2CppDateTimeOffset InvokeSelDtoIdx(Il2CppDelegate* d, Src v, int32_t index)
{
    typedef runtime::Il2CppDateTimeOffset (*Fn)(Src, int32_t);
    typedef runtime::Il2CppDateTimeOffset (*ThisFn)(Il2CppObject*, Src, int32_t);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<Fn>(d->methodPtr)(v, index);
}

template <typename Src>
Il2CppArray* SelectDtoffsetCore(Il2CppArray* src, Il2CppObject* sel,
    Src (*read)(Il2CppArray*, int32_t), bool indexed)
{
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDateTimeOffset)),
        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const Src v = read(src, i);
        const runtime::Il2CppDateTimeOffset slot = indexed
            ? InvokeSelDtoIdx(d, v, i)
            : InvokeSelDto(d, v);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(slot));
    }
    return out;
}

#define IL2CPP_LINQ_SELECT_DTO_WRAP(NAME, SRC, REQUIRE, READFN)                 \
Il2CppArray* il2cpp_linq_select_##NAME##_dtoffset(Il2CppArray* src, Il2CppObject* sel) \
{                                                                              \
    RequireArray(src);                                                         \
    REQUIRE(src);                                                              \
    return SelectDtoffsetCore<SRC>(src, sel, READFN, false);                    \
}                                                                              \
Il2CppArray* il2cpp_linq_select_idx_##NAME##_dtoffset(Il2CppArray* src, Il2CppObject* sel) \
{                                                                              \
    RequireArray(src);                                                         \
    REQUIRE(src);                                                              \
    return SelectDtoffsetCore<SRC>(src, sel, READFN, true);                     \
}

} // namespace

IL2CPP_LINQ_SELECT_DTO_WRAP(i4, int32_t, RequireInt32Family, ReadDtoSrcI4)
IL2CPP_LINQ_SELECT_DTO_WRAP(i8, int64_t, RequireI8Family, ReadDtoSrcI8)
IL2CPP_LINQ_SELECT_DTO_WRAP(f, double, RequireFloatFamily, ReadDtoSrcF)
IL2CPP_LINQ_SELECT_DTO_WRAP(d, double, RequireDoubleFamily, ReadDtoSrcD)
IL2CPP_LINQ_SELECT_DTO_WRAP(ref, Il2CppObject*, RequireRefFamily, ReadDtoSrcRef)
IL2CPP_LINQ_SELECT_DTO_WRAP(dec, runtime::Il2CppDecimal, RequireDecimalFamily, ReadDtoSrcDec)
IL2CPP_LINQ_SELECT_DTO_WRAP(dt, runtime::Il2CppDateTime, RequireDateTimeFamily, ReadDtoSrcDt)
IL2CPP_LINQ_SELECT_DTO_WRAP(ts, runtime::Il2CppTimeSpan, RequireTimeSpanFamily, ReadDtoSrcTs)
IL2CPP_LINQ_SELECT_DTO_WRAP(guid, Il2CppGuid, RequireGuidFamily, ReadDtoSrcGuid)
IL2CPP_LINQ_SELECT_DTO_WRAP(dto, runtime::Il2CppDateTimeOffset, RequireDtoffsetFamily, ReadDtoSrcDto)

#undef IL2CPP_LINQ_SELECT_DTO_WRAP

//------------------------------------------------------------------------------
// The DECIMAL-source INDEX Select (net48 Select<decimal,int,TResult>): the
// selector receives the element AND its zero-based source index. TResult is
// int32 / int64 / double / float / decimal.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_idx_dec_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(4, kElemKindI4, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
            InvokeSelDecIdxI4(d, *DecAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_dec_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindI8, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
            InvokeSelDecIdxI8(d, *DecAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_dec_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindD, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            InvokeSelDecIdxD(d, *DecAt(src, i), i);
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_dec_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new_typed(8, kElemKindF, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            static_cast<double>(InvokeSelDecIdxF(d, *DecAt(src, i), i));
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_dec_dec(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        const runtime::Il2CppDecimal slot = InvokeSelDecIdxDec(d, *DecAt(src, i), i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(slot));
    }
    return out;
}

// impl-linqleft: Select over a DECIMAL source with an (element, index)
// selector returning a REFERENCE result (net48 Select<decimal,int,TResult>).
// The selector returns Il2CppObject*; the result is a fresh reference array.
Il2CppArray* il2cpp_linq_select_idx_dec_ref(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireDecimalFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, sizeof(void*), true, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* r = InvokeSelDecIdxRef(d, *DecAt(src, i), i);
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
        *slot = r;
    }
    return out;
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

namespace {

typedef runtime::Il2CppDecimal (*SelRefToDecIdxFn)(Il2CppObject* elem, int32_t index);
typedef runtime::Il2CppDecimal (*SelRefToDecIdxThisFn)(Il2CppObject* self, Il2CppObject* elem,
                                                       int32_t index);

} // namespace

// impl-linqleft: the reference-source INDEX Select with a DECIMAL result
// (net48 Select<TSource,int,decimal>). The element crosses as Il2CppObject*,
// the zero-based source index as int32, and the 16-byte decimal result rides
// the return slot into a fresh decimal[].
Il2CppArray* il2cpp_linq_select_idx_ref_dec(Il2CppArray* src, Il2CppObject* sel)
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
            ? reinterpret_cast<SelRefToDecIdxThisFn>(d->methodPtr)(d->target, elem, i)
            : reinterpret_cast<SelRefToDecIdxFn>(d->methodPtr)(elem, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot,
                    sizeof(runtime::Il2CppDecimal));
    }
    return out;
}

//------------------------------------------------------------------------------
// p0linq4 (D3) follow-up: the reference-source Select with a DateTime /
// TimeSpan / Guid result. The element crosses as Il2CppObject* and the result
// struct rides the return slot (the same by-value convention as the decimal
// selector above). The output cell carries the raw struct image (DateTime: the
// packed ticks + kind; TimeSpan: the ticks; Guid: the 16 bytes).
// GC SAFETY: identical to the decimal selector -- the delegate, the source and
// the result are rooted across the per-element callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDateTime (*SelRefToDtFn)(Il2CppObject* elem);
typedef runtime::Il2CppDateTime (*SelRefToDtThisFn)(Il2CppObject* self, Il2CppObject* elem);
typedef runtime::Il2CppDateTime (*SelRefToDtIdxFn)(Il2CppObject* elem, int32_t index);
typedef runtime::Il2CppDateTime (*SelRefToDtIdxThisFn)(Il2CppObject* self, Il2CppObject* elem, int32_t index);
typedef runtime::Il2CppTimeSpan (*SelRefToTsFn)(Il2CppObject* elem);
typedef runtime::Il2CppTimeSpan (*SelRefToTsThisFn)(Il2CppObject* self, Il2CppObject* elem);
typedef runtime::Il2CppTimeSpan (*SelRefToTsIdxFn)(Il2CppObject* elem, int32_t index);
typedef runtime::Il2CppTimeSpan (*SelRefToTsIdxThisFn)(Il2CppObject* self, Il2CppObject* elem, int32_t index);
typedef Il2CppGuid (*SelRefToGuidFn)(Il2CppObject* elem);
typedef Il2CppGuid (*SelRefToGuidThisFn)(Il2CppObject* self, Il2CppObject* elem);
typedef Il2CppGuid (*SelRefToGuidIdxFn)(Il2CppObject* elem, int32_t index);
typedef Il2CppGuid (*SelRefToGuidIdxThisFn)(Il2CppObject* self, Il2CppObject* elem, int32_t index);

Il2CppArray* il2cpp_linq_select_ref_dt(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDateTime)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const runtime::Il2CppDateTime slot = d->target != nullptr
            ? reinterpret_cast<SelRefToDtThisFn>(d->methodPtr)(d->target, elem)
            : reinterpret_cast<SelRefToDtFn>(d->methodPtr)(elem);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppDateTime));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_ts(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppTimeSpan)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const runtime::Il2CppTimeSpan slot = d->target != nullptr
            ? reinterpret_cast<SelRefToTsThisFn>(d->methodPtr)(d->target, elem)
            : reinterpret_cast<SelRefToTsFn>(d->methodPtr)(elem);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppTimeSpan));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_ref_guid(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(Il2CppGuid)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const Il2CppGuid slot = d->target != nullptr
            ? reinterpret_cast<SelRefToGuidThisFn>(d->methodPtr)(d->target, elem)
            : reinterpret_cast<SelRefToGuidFn>(d->methodPtr)(elem);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(Il2CppGuid));
    }
    return out;
}

// The INDEX overloads: the selector receives the element AND its zero-based
// source index. Same GC-safety contract.
Il2CppArray* il2cpp_linq_select_idx_ref_dt(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDateTime)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const runtime::Il2CppDateTime slot = d->target != nullptr
            ? reinterpret_cast<SelRefToDtIdxThisFn>(d->methodPtr)(d->target, elem, i)
            : reinterpret_cast<SelRefToDtIdxFn>(d->methodPtr)(elem, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppDateTime));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_ref_ts(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppTimeSpan)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const runtime::Il2CppTimeSpan slot = d->target != nullptr
            ? reinterpret_cast<SelRefToTsIdxThisFn>(d->methodPtr)(d->target, elem, i)
            : reinterpret_cast<SelRefToTsIdxFn>(d->methodPtr)(elem, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(runtime::Il2CppTimeSpan));
    }
    return out;
}

Il2CppArray* il2cpp_linq_select_idx_ref_guid(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(Il2CppGuid)),
                                        false, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* elem = RefAt(src, i);
        const Il2CppGuid slot = d->target != nullptr
            ? reinterpret_cast<SelRefToGuidIdxThisFn>(d->methodPtr)(d->target, elem, i)
            : reinterpret_cast<SelRefToGuidIdxFn>(d->methodPtr)(elem, i);
        std::memcpy(il2cpp_array_get_elem_ptr(out, i), &slot, sizeof(Il2CppGuid));
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
    // p0grp: a System.Decimal bucket element (16-byte value cell) -- the
    // decimal-source GroupBy with a decimal TElement.
    Decimal = 4,
    // p0grp: a System.DateTime bucket element (8-byte value cell) -- the
    // DateTime-source GroupBy with a DateTime TElement.
    DateTime = 5,
    // vtsrc: a System.Guid bucket element (16-byte value cell) -- the
    // Guid-source GroupBy with a Guid TElement.
    Guid = 6,
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

// BATCH4: the WIDE-key result selector ABIs (TKey is a 64-bit or 16-byte struct
// value). The leaf receives the key in its REAL family slot (int64 for a long
// key; a 16-byte value for a struct key) and the bucket element array second.
typedef int32_t (*SelKeyGroupI8I4Fn)(int64_t, Il2CppObject*);
typedef int32_t (*SelKeyGroupI8I4ThisFn)(Il2CppObject*, int64_t, Il2CppObject*);
typedef Il2CppObject* (*SelKeyGroupI8RefFn)(int64_t, Il2CppObject*);
typedef Il2CppObject* (*SelKeyGroupI8RefThisFn)(Il2CppObject*, int64_t, Il2CppObject*);

int32_t InvokeSelKeyGroupI8I4(Il2CppDelegate* d, int64_t key, Il2CppObject* bucket)
{
    return d->target != nullptr
        ? reinterpret_cast<SelKeyGroupI8I4ThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<SelKeyGroupI8I4Fn>(d->methodPtr)(key, bucket);
}

Il2CppObject* InvokeSelKeyGroupI8Ref(Il2CppDelegate* d, int64_t key, Il2CppObject* bucket)
{
    return d->target != nullptr
        ? reinterpret_cast<SelKeyGroupI8RefThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<SelKeyGroupI8RefFn>(d->methodPtr)(key, bucket);
}

template <typename Res>
Res InvokeSelKeyGroupI8Typed(Il2CppDelegate* d, int64_t key, Il2CppObject* bucket)
{
    typedef Res (*Fn)(int64_t, Il2CppObject*);
    typedef Res (*WithThisFn)(Il2CppObject*, int64_t, Il2CppObject*);
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

// BATCH4: the default int64 (long/ulong) comparer singletons. ulong rides the
// same 64-bit bit-pattern equality as long, so one predicate serves both.
bool IsDefaultLongComparer(Il2CppObject* cmp)
{
    return cmp == il2cpp_equality_comparer_default_long() || cmp == il2cpp_comparer_default_long();
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

// BATCH4: the wide-key comparer dispatch. The 16-byte key carrier holds the
// key in two 8-byte slots (keyLo/keyHi); the default contract compares the
// full carrier by value, and a user IEqualityComparer<long> is reflected
// through the int64 Equals(long,long) slot. (A user comparer over a 16-byte
// struct key is registered as LOUD -- the struct Equals slot cannot be named
// from the byte alone; see docs/audit/b5-gaps.md.)
bool ComparerKeyEqualsLong(Il2CppObject* cmp, int64_t key, int64_t existing)
{
    if (cmp == nullptr || IsDefaultLongComparer(cmp))
    {
        return key == existing;
    }
    int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Equals", "(l,l)");
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "IEqualityComparer<long>.Equals dispatch: the comparer type has no "
            "reflected virtual Equals(long,long) slot.");
        return false; // unreachable (the factory throws)
    }
    typedef bool (*EqualsFn)(Il2CppObject*, int64_t, int64_t);
    return reinterpret_cast<EqualsFn>(cmp->klass->vtable[slot])(cmp, key, existing);
}

// Materialize the 16-byte struct key (keyKind 8/9/10) from the bucket carrier.
// (Declared here because the key-equality kernel below needs the decimal form.)
runtime::Il2CppDecimal KeyCarrierToDecimal(int64_t lo, int64_t hi);

bool GroupKeysEqual(Il2CppObject* cmp, int32_t keyKind,
                    int64_t keyLo, int64_t keyHi, Il2CppObject* keyRef,
                    const Il2CppLinqGrouping* existing)
{
    if (keyKind == 11)
    {
        // A REFERENCE key: the default contract is reference equality (a user
        // IEqualityComparer over a reference key is not wired).
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a reference key "
                "(System.String / class / object): the reference key comparer is not "
                "wired; the default comparer (null) compares by reference.");
            return false; // unreachable (the factory throws)
        }
        return existing->keyRef == keyRef ||
               (existing->keyRef != nullptr && keyRef != nullptr &&
                runtime::icall_object_equals(existing->keyRef, keyRef));
    }
    if (keyKind == 8)
    {
        // impl-groupbykey: System.Decimal keys compare by VALUE
        // (EqualityComparer<decimal>.Default -> Decimal.Equals), so 1.0m,
        // 1.00m and 1.000m are ONE net48 bucket. The raw 16-byte carrier
        // differs in the scale word -- comparing it byte-wise (the pre-fix
        // behavior) SPLIT net48's buckets (probe: 3 buckets instead of 2).
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a System.Decimal key is "
                "not wired; the default comparer (null) compares by decimal value.");
            return false; // unreachable (the factory throws)
        }
        const runtime::Il2CppDecimal k = KeyCarrierToDecimal(keyLo, keyHi);
        const runtime::Il2CppDecimal e = KeyCarrierToDecimal(existing->key, existing->keyHi);
        return runtime::icall_decimal_equals(&k, &e);
    }
    if (keyKind == 9)
    {
        // impl-groupbykey: DateTime keys compare by TICKS only
        // (DateTime.Equals ignores DateTimeKind -- probe: the same instant
        // with Unspecified/Utc/Local kinds is ONE net48 bucket). The carrier
        // still keeps the FIRST key's kind so get_Key reports it.
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a System.DateTime key is "
                "not wired; the default comparer (null) compares the ticks.");
            return false; // unreachable (the factory throws)
        }
        return keyLo == existing->key;
    }
    if (keyKind == 10)
    {
        // System.Guid: field-wise value equality == the raw 16-byte image
        // (Guid has no padding and no normalization).
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a System.Guid key is "
                "not wired; the default comparer (null) compares the raw 16-byte value.");
            return false; // unreachable (the factory throws)
        }
        return keyLo == existing->key && keyHi == existing->keyHi;
    }
    if (keyKind == 12)
    {
        // System.TimeSpan: value equality == the tick count.
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a System.TimeSpan key is "
                "not wired; the default comparer (null) compares the ticks.");
            return false; // unreachable (the factory throws)
        }
        return keyLo == existing->key;
    }
    if (keyKind == 13)
    {
        // System.DateTimeOffset: net48 equality is the UTC INSTANT
        // (DateTimeOffset.Equals/CompareTo ignore the offset -- probe: the same
        // instant at +00:00/+02:00/+03:00 is ONE bucket). The carrier keeps the
        // FIRST key's offset so get_Key reports it.
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over a System.DateTimeOffset key "
                "is not wired; the default comparer (null) compares the UTC instant.");
            return false; // unreachable (the factory throws)
        }
        return keyLo == existing->key;
    }
    if (keyKind == 14)
    {
        // impl-groupbykey: a USER ENUM key. The selector leaf returns the
        // generated enum struct (a 16-byte aggregate) and the kernel keeps its
        // int32 value__; EqualityComparer<E>.Default compares that value.
        if (cmp != nullptr)
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy with a user IEqualityComparer over an enum key is not wired; "
                "the default comparer (null) compares the underlying value.");
            return false; // unreachable (the factory throws)
        }
        return static_cast<int32_t>(keyLo) == static_cast<int32_t>(existing->key);
    }
    if (keyKind == 7)
    {
        return ComparerKeyEqualsLong(cmp, keyLo, existing->key);
    }
    return ComparerKeyEquals(cmp, static_cast<int32_t>(keyLo),
                             static_cast<int32_t>(existing->key), keyKind);
}

// NewGroupElementArray -- the bucket storage for one element family.
// impl-linq2 (C): the element KIND is recorded on the array (kElemKindI4 /
// kElemKindDateTime / kElemKindDecimal / kElemKindGuid) so the bucket
// enumerator and the get_Current carriers can tell the 16-byte System.Decimal
// cell from the 16-byte System.Guid cell (identical widths -- the old
// elemSize-only dispatch misboxed every Guid cell as a decimal).
Il2CppArray* NewGroupElementArray(GroupElemKind kind, int32_t length)
{
    if (kind == GroupElemKind::Decimal)
        return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)),
                                      kElemKindDecimal, length);
    if (kind == GroupElemKind::DateTime)
        return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(runtime::Il2CppDateTime)),
                                      kElemKindDateTime, length);
    if (kind == GroupElemKind::Guid)
        return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(Il2CppGuid)),
                                      kElemKindGuid, length);
    if (kind == GroupElemKind::Int32)
        return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(int32_t)), kElemKindI4, length);
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, length);
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

// impl-groupbykey: the SOURCE CELL reader for a narrow (1/2/4-byte) element
// family. The narrow cells widen into the int32 delegate slot exactly like the
// CLR's parameter rule, so the int32-source selector ABIs serve them unchanged.
int32_t NarrowCellAt(Il2CppArray* src, int32_t index, int32_t cell)
{
    const void* p = il2cpp_array_get_elem_ptr(src, index);
    switch (cell)
    {
        case 0: return static_cast<int32_t>(*reinterpret_cast<const int8_t*>(p));   // sbyte
        case 1: return static_cast<int32_t>(*reinterpret_cast<const uint8_t*>(p));  // byte
        case 2: return *reinterpret_cast<const uint8_t*>(p) != 0 ? 1 : 0;           // bool
        case 3: return static_cast<int32_t>(*reinterpret_cast<const int16_t*>(p));  // short
        case 4: return static_cast<int32_t>(*reinterpret_cast<const uint16_t*>(p)); // ushort
        case 5: return static_cast<int32_t>(*reinterpret_cast<const uint16_t*>(p)); // char
        case 6: return *reinterpret_cast<const int32_t*>(p);                        // uint (bit pattern)
        default: return *reinterpret_cast<const int32_t*>(p);                       // int32
    }
}

// impl-groupbykey: the KEY family of the GroupBy bucket carrier. Kept in
// lockstep with MethodMap.Linq.cs KeyKindFor / KeyKindForNamed:
//   0 int32 | 1 uint8 | 2 int8 | 3 uint16/char | 4 int16 | 5 uint32 | 6 bool
//   7 int64/ulong | 8 System.Decimal | 9 System.DateTime | 10 System.Guid
//   11 reference | 12 System.TimeSpan | 13 System.DateTimeOffset
//   14 user ENUM (the int32 value__ carrier)
//
// The selector leaf is invoked through the GENERIC template (exact leaf ABI for
// every source family) and the result is flattened into the 16-byte bucket
// carrier (keyLo/keyHi) plus the reference slot.
template <typename Src>
void ComputeGroupKey(Il2CppDelegate* kd, Src v, int32_t keyKind,
                     int64_t& keyLo, int64_t& keyHi, Il2CppObject*& keyRef)
{
    switch (keyKind)
    {
        case 7: // int64 / ulong
            keyLo = InvokeGroupKeySel<Src, int64_t>(kd, v);
            break;
        case 8: // System.Decimal (16-byte raw carrier)
        {
            const runtime::Il2CppDecimal k = InvokeGroupKeySel<Src, runtime::Il2CppDecimal>(kd, v);
            std::memcpy(&keyLo, &k, sizeof(keyLo));
            std::memcpy(&keyHi, reinterpret_cast<const uint8_t*>(&k) + 8, sizeof(keyHi));
            break;
        }
        case 9: // System.DateTime (ticks + kind)
        {
            const runtime::Il2CppDateTime k = InvokeGroupKeySel<Src, runtime::Il2CppDateTime>(kd, v);
            keyLo = k.ticks;
            keyHi = k.kind;
            break;
        }
        case 10: // System.Guid (16-byte raw carrier)
        {
            const Il2CppGuid k = InvokeGroupKeySel<Src, Il2CppGuid>(kd, v);
            std::memcpy(&keyLo, &k, sizeof(keyLo));
            std::memcpy(&keyHi, reinterpret_cast<const uint8_t*>(&k) + 8, sizeof(keyHi));
            break;
        }
        case 11: // reference (System.String / class / object)
            keyRef = InvokeGroupKeySel<Src, Il2CppObject*>(kd, v);
            break;
        case 12: // System.TimeSpan (int64 ticks)
            keyLo = InvokeGroupKeySel<Src, runtime::Il2CppTimeSpan>(kd, v).ticks;
            break;
        case 13: // System.DateTimeOffset (utcTicks + offsetMinutes)
        {
            const runtime::Il2CppDateTimeOffset k =
                InvokeGroupKeySel<Src, runtime::Il2CppDateTimeOffset>(kd, v);
            keyLo = k.utcTicks;
            keyHi = k.offsetMinutes;
            break;
        }
        case 14: // user enum (the int32 value__)
            keyLo = InvokeGroupEnumKeySel<Src>(kd, v);
            break;
        default: // 0..6: the int32-slot families, narrowed to the declared width
            keyLo = NarrowKey(static_cast<int32_t>(InvokeGroupKeySel<Src, int32_t>(kd, v)),
                              keyKind);
            break;
    }
}

// impl-groupbykey: the ELEMENT selector result of one source element, in the
// bucket's storage family. Same generic invocation as ComputeGroupKey.
template <typename Src>
void ComputeGroupElem(Il2CppDelegate* ed, Src v, GroupElemKind kind, int32_t& elemI4,
                      Il2CppObject*& elemRef, int64_t& elemI8, double& elemD,
                      runtime::Il2CppDecimal& elemDec, runtime::Il2CppDateTime& elemDt,
                      Il2CppGuid& elemGuid)
{
    switch (kind)
    {
        case GroupElemKind::Int32:
            elemI4 = InvokeGroupElemSel<Src, int32_t>(ed, v);
            break;
        case GroupElemKind::Ref:
            elemRef = InvokeGroupElemSel<Src, Il2CppObject*>(ed, v);
            break;
        case GroupElemKind::I8:
            elemI8 = InvokeGroupElemSel<Src, int64_t>(ed, v);
            break;
        case GroupElemKind::D:
            elemD = InvokeGroupElemSel<Src, double>(ed, v);
            break;
        case GroupElemKind::Decimal:
            elemDec = InvokeGroupElemSel<Src, runtime::Il2CppDecimal>(ed, v);
            break;
        case GroupElemKind::DateTime:
            elemDt = InvokeGroupElemSel<Src, runtime::Il2CppDateTime>(ed, v);
            break;
        case GroupElemKind::Guid:
            elemGuid = InvokeGroupElemSel<Src, Il2CppGuid>(ed, v);
            break;
    }
}

// BuildGroups -- the shared bucketing pass (see the block comment).
Il2CppObject* BuildGroups(Il2CppArray* src, Il2CppObject* keySel, Il2CppObject* elemSel,
                          GroupElemKind kind, Il2CppObject* cmp, int32_t keyKind = 0,
                          int32_t srcKind = 0, int32_t srcCell = 0)
{
    RequireArray(src);
    Il2CppDelegate* kd = RequireCallback(keySel, "keySelector");
    Il2CppDelegate* ed = elemSel != nullptr ? RequireCallback(elemSel, "elementSelector") : nullptr;
    // p0grp: a REFERENCE-element source reads its elements as Il2CppObject*
    // (the key/element selectors take the reference); an int32-cell source reads
    // I4At. The bucket storage for both is the Ref family when the source is a
    // reference array (the element selector projects a reference).
    const bool refSource = src->isRefElem != 0;
    const bool decimalSource = !refSource && srcKind == 2;
    const bool dtSource = !refSource && srcKind == 3;
    const bool guidSource = !refSource && srcKind == 4;
    // impl-groupbykey: the WIDE / NARROW source families. srcKind 5 is the
    // int64/uint64 8-byte cell family, 6 the double 8-byte family and 7 the
    // narrow (1/2/4-byte) integral family whose cell code rides srcCell.
    const bool i8Source = !refSource && srcKind == 5;
    const bool dSource = !refSource && srcKind == 6;
    const bool narrowSource = !refSource && srcKind == 7;
    if (refSource) RequireRefFamily(src);
    else if (decimalSource) RequireDecimalFamily(src);
    else if (dtSource) RequireDateTimeFamily(src);
    else if (guidSource) RequireGuidFamily(src);
    else if (i8Source) RequireI8Family(src);
    else if (dSource) RequireDoubleFamily(src);
    else if (narrowSource)
    {
        // A narrow cell must be narrower than (or equal to) the 4-byte int32
        // slot the selector ABI widens into; a reference/wide cell is a caller
        // contract violation, never a silent over-read.
        if (src->elemSize > static_cast<int32_t>(sizeof(int32_t)))
        {
            il2cpp_throw_not_supported_exception(
                "GroupBy: a narrow-element source must hold 1/2/4-byte cells.");
        }
    }
    else RequireInt32Family(src);
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
    // p0grp: the reference-source element is re-read from the rooted source each
    // iteration (the key selector may allocate -> collect), so the local copy is
    // rooted too.
    Il2CppObject* srcElem = nullptr;
    roots.Add(reinterpret_cast<void**>(&srcElem));

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
        // p0grp: a reference source element crosses as Il2CppObject*; a decimal
        // source element as the 16-byte value struct; an int32 source element as
        // the 4-byte cell value.
        const int32_t v = narrowSource ? NarrowCellAt(src, i, srcCell)
            : ((refSource || decimalSource || dtSource || guidSource || i8Source || dSource)
                ? 0 : I4At(src, i));
        // impl-groupbykey: the wide source elements cross their selector leaf in
        // the int64_t / double register slot.
        const int64_t v8 = i8Source
            ? *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(src, i)) : 0;
        const double vd = dSource
            ? *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(src, i)) : 0.0;
        const runtime::Il2CppDecimal dv = decimalSource ? *DecAt(src, i) : runtime::Il2CppDecimal();
        const runtime::Il2CppDateTime dts = dtSource
            ? runtime::Il2CppDateTime(*reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i)), 0)
            : runtime::Il2CppDateTime();
        Il2CppGuid gs{};
        if (guidSource)
            std::memcpy(&gs, il2cpp_array_get_elem_ptr(src, i), sizeof(gs));
        srcElem = refSource
            ? *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, i))
            : nullptr;
        // BATCH4: the key is a 16-byte carrier split low64 -> keyLo, high64 ->
        // keyHi. int32-slot keys (keyKind 0..6) ride keyLo; an int64 key
        // (keyKind 7) is the 64-bit value in keyLo; a 16-byte struct key
        // (keyKind 8/9/10) carries its raw bytes across keyLo/keyHi. The
        // callback may allocate -> collect, so every consumer re-derives its
        // pointers from the rooted result (never caches a raw pointer).
        int64_t keyLo = 0;
        int64_t keyHi = 0;
        Il2CppObject* keyRef = nullptr;
        // impl-groupbykey: ONE key-family dispatch for every source family. The
        // generic template reproduces the selector leaf's ABI exactly (see
        // InvokeGroupKeySel), so the bucket carrier is computed uniformly:
        //   keyKind 0..6 int32-slot (narrowed) | 7 int64 | 8 System.Decimal |
        //   9 System.DateTime | 10 System.Guid | 11 reference |
        //   12 System.TimeSpan | 13 System.DateTimeOffset | 14 user ENUM.
        // The callback may allocate -> collect, so every consumer re-derives its
        // pointers from the rooted result (never caches a raw pointer).
        if (dtSource)
            ComputeGroupKey<runtime::Il2CppDateTime>(kd, dts, keyKind, keyLo, keyHi, keyRef);
        else if (guidSource)
            ComputeGroupKey<Il2CppGuid>(kd, gs, keyKind, keyLo, keyHi, keyRef);
        else if (decimalSource)
            ComputeGroupKey<runtime::Il2CppDecimal>(kd, dv, keyKind, keyLo, keyHi, keyRef);
        else if (refSource)
            ComputeGroupKey<Il2CppObject*>(kd, srcElem, keyKind, keyLo, keyHi, keyRef);
        else if (i8Source)
            ComputeGroupKey<int64_t>(kd, v8, keyKind, keyLo, keyHi, keyRef);
        else if (dSource)
            ComputeGroupKey<double>(kd, vd, keyKind, keyLo, keyHi, keyRef);
        else
            ComputeGroupKey<int32_t>(kd, v, keyKind, keyLo, keyHi, keyRef);

        // The projected element (only for the element-selector overloads).
        // Boxed families are NOT boxed yet: the growth allocation below could
        // move the box, so the box happens after the capacity check.
        int32_t elemI4 = 0;
        elemRef = nullptr; // nb9 fix H1: drop the previous iteration's root
        int64_t elemI8 = 0;
        double elemD = 0;
        runtime::Il2CppDecimal elemDec{};
        runtime::Il2CppDateTime elemDt{};
        Il2CppGuid elemGuid{};
        if (ed != nullptr)
        {
            // impl-groupbykey: ONE element-family dispatch for every source
            // family (the generic template reproduces the leaf ABI exactly).
            if (dtSource)
                ComputeGroupElem<runtime::Il2CppDateTime>(ed, dts, kind, elemI4, elemRef,
                                                          elemI8, elemD, elemDec, elemDt, elemGuid);
            else if (guidSource)
                ComputeGroupElem<Il2CppGuid>(ed, gs, kind, elemI4, elemRef, elemI8, elemD,
                                             elemDec, elemDt, elemGuid);
            else if (decimalSource)
                ComputeGroupElem<runtime::Il2CppDecimal>(ed, dv, kind, elemI4, elemRef, elemI8,
                                                         elemD, elemDec, elemDt, elemGuid);
            else if (refSource)
                ComputeGroupElem<Il2CppObject*>(ed, srcElem, kind, elemI4, elemRef, elemI8,
                                                elemD, elemDec, elemDt, elemGuid);
            else if (i8Source)
                ComputeGroupElem<int64_t>(ed, v8, kind, elemI4, elemRef, elemI8, elemD,
                                          elemDec, elemDt, elemGuid);
            else if (dSource)
                ComputeGroupElem<double>(ed, vd, kind, elemI4, elemRef, elemI8, elemD,
                                         elemDec, elemDt, elemGuid);
            else
                ComputeGroupElem<int32_t>(ed, v, kind, elemI4, elemRef, elemI8, elemD,
                                          elemDec, elemDt, elemGuid);
        }
        else if (kind == GroupElemKind::Int32)
        {
            elemI4 = v;
        }
        else if (refSource && kind == GroupElemKind::Ref)
        {
            // p0grp: the key-only reference-source overload -- the bucket
            // element IS the source reference.
            elemRef = srcElem;
        }
        else if (guidSource && kind == GroupElemKind::Guid)
        {
            // vtsrc: the key-only Guid-source overload -- the bucket element
            // IS the 16-byte source Guid.
            elemGuid = gs;
        }
        else if (dtSource && kind == GroupElemKind::DateTime)
        {
            // impl-linq2 (C): the key-only DateTime-source overload -- the
            // bucket element IS the source DateTime struct. The old code had
            // no arm here, so every key-only bucket cell stayed the default
            // (ticks 0 -> 0001-01-01).
            elemDt = dts;
        }
        else if (decimalSource && kind == GroupElemKind::Decimal)
        {
            // impl-groupbykey: the key-only DECIMAL-source overload -- the
            // bucket element IS the 16-byte source decimal cell (the old
            // derivation defaulted the element family to int32, so a
            // GroupBy(source, key, comparer) bucket stored zeros).
            elemDec = dv;
        }
        else if (i8Source && kind == GroupElemKind::I8)
        {
            // impl-groupbykey: the key-only int64/uint64-source overload.
            elemI8 = v8;
        }
        else if (dSource && kind == GroupElemKind::D)
        {
            // impl-groupbykey: the key-only double-source overload.
            elemD = vd;
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
            if (GroupKeysEqual(cmp, keyKind, keyLo, keyHi, keyRef, AsGrouping(slots[j])))
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
                case GroupElemKind::Decimal:
                    *reinterpret_cast<runtime::Il2CppDecimal*>(
                        il2cpp_array_get_elem_ptr(grp->elements, at)) = elemDec;
                    break;
                case GroupElemKind::DateTime:
                    *reinterpret_cast<runtime::Il2CppDateTime*>(
                        il2cpp_array_get_elem_ptr(grp->elements, at)) = elemDt;
                    break;
                case GroupElemKind::Guid:
                    std::memcpy(il2cpp_array_get_elem_ptr(grp->elements, at),
                                &elemGuid, sizeof(elemGuid));
                    break;
            }
            ++grp->count;
        }
        else
        {
            Il2CppObject* ng = il2cpp_object_new(&g_groupingClass);
            Il2CppLinqGrouping* grp = AsGrouping(ng);
            grp->key = keyLo;
            grp->keyHi = keyHi;
            grp->keyRef = keyRef;
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->keyRef), keyRef);
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
                case GroupElemKind::Decimal:
                    *reinterpret_cast<runtime::Il2CppDecimal*>(
                        il2cpp_array_get_elem_ptr(grp->elements, 0)) = elemDec;
                    break;
                case GroupElemKind::DateTime:
                    *reinterpret_cast<runtime::Il2CppDateTime*>(
                        il2cpp_array_get_elem_ptr(grp->elements, 0)) = elemDt;
                    break;
                case GroupElemKind::Guid:
                    std::memcpy(il2cpp_array_get_elem_ptr(grp->elements, 0),
                                &elemGuid, sizeof(elemGuid));
                    break;
            }
            grp->count = 1;
        }
    }
    return result;
}

// ApplyResultSelector -- run Func<TKey,IEnumerable<TElement>,TResult> once per
// bucket, in first-appearance order, and materialize the results into an array
// of the TResult family (int32[] or a reference array).
// BATCH4: the wide-key result-selector invocation. The key rides its REAL
// family slot (int64 for keyKind 7; a 16-byte struct for keyKind >= 8); the
// TResult family decides the leaf's return ABI. One template covers every
// (TKey, TResult) pair.
template <typename TKey, typename Res>
Res InvokeSelKeyGroupKeyTyped(Il2CppDelegate* d, TKey key, Il2CppObject* bucket)
{
    typedef Res (*Fn)(TKey, Il2CppObject*);
    typedef Res (*ThisFn)(Il2CppObject*, TKey, Il2CppObject*);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, key, bucket)
        : reinterpret_cast<Fn>(d->methodPtr)(key, bucket);
}

// Materialize the 16-byte struct key (keyKind 8/9/10) from the bucket carrier.
runtime::Il2CppDecimal KeyCarrierToDecimal(int64_t lo, int64_t hi)
{
    runtime::Il2CppDecimal k;
    std::memcpy(&k, &lo, 8);
    std::memcpy(reinterpret_cast<uint8_t*>(&k) + 8, &hi, 8);
    return k;
}

Il2CppGuid KeyCarrierToGuid(int64_t lo, int64_t hi)
{
    Il2CppGuid g;
    std::memcpy(&g, &lo, 8);
    std::memcpy(reinterpret_cast<uint8_t*>(&g) + 8, &hi, 8);
    return g;
}

Il2CppArray* ApplyResultSelector(Il2CppObject* result, Il2CppObject* resSel, int32_t rsKind,
                                 int32_t keyKind = 0)
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
        const Il2CppLinqGrouping* grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
            res->groups->data + i * sizeof(void*)));
        const int64_t kLo = grp->key;
        const int64_t kHi = grp->keyHi;
        const int32_t kI4 = static_cast<int32_t>(kLo);

        // The key slot the leaf receives: int32 (keyKind <= 6), int64 (7), a
        // 16-byte struct (8/9/10), a REFERENCE (11), System.TimeSpan (12),
        // System.DateTimeOffset (13) or a user ENUM (14). Each branch uses the
        // matching leaf ABI.
        if (keyKind == 11)
        {
            // p0grp: a REFERENCE key. The result selector's first parameter is
            // the bucket key as an Il2CppObject* -- passing the int32 carrier
            // here (the pre-fix fall-through) handed the leaf a garbage pointer.
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupKeyTyped<Il2CppObject*, Il2CppObject*>(d, grp->keyRef, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppObject*, int64_t>(d, grp->keyRef, elemsObj);
            else if (rsKind == 4)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppObject*, double>(d, grp->keyRef, elemsObj);
            else if (rsKind == 5)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                    InvokeSelKeyGroupKeyTyped<Il2CppObject*, float>(d, grp->keyRef, elemsObj));
            else
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppObject*, int32_t>(d, grp->keyRef, elemsObj);
        }
        else if (keyKind == 12)
        {
            // impl-groupbykey: System.TimeSpan key (the ticks carrier).
            const runtime::Il2CppTimeSpan k(kLo);
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupKeyTyped<runtime::Il2CppTimeSpan, Il2CppObject*>(d, k, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppTimeSpan, int64_t>(d, k, elemsObj);
            else if (rsKind == 4)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppTimeSpan, double>(d, k, elemsObj);
            else if (rsKind == 5)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppTimeSpan, float>(d, k, elemsObj));
            else
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppTimeSpan, int32_t>(d, k, elemsObj);
        }
        else if (keyKind == 13)
        {
            // impl-groupbykey: System.DateTimeOffset key (UTC instant + offset;
            // the leaf takes the 16-byte struct by value).
            runtime::Il2CppDateTimeOffset k;
            k.utcTicks = kLo;
            k.offsetMinutes = static_cast<int32_t>(kHi);
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTimeOffset, Il2CppObject*>(d, k, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTimeOffset, int64_t>(d, k, elemsObj);
            else if (rsKind == 4)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTimeOffset, double>(d, k, elemsObj);
            else if (rsKind == 5)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTimeOffset, float>(d, k, elemsObj));
            else
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTimeOffset, int32_t>(d, k, elemsObj);
        }
        else if (keyKind == 14)
        {
            // impl-groupbykey: a user ENUM key. The leaf takes the generated
            // enum struct by value; Il2CppEnumKeyImage has the same 16-byte
            // image (header + value__ + padding), so the register/aggregate
            // ABI matches without naming the enum type.
            Il2CppEnumKeyImage k;
            k.header = nullptr;
            k.value = static_cast<int32_t>(kLo);
            k.pad = 0;
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupKeyTyped<Il2CppEnumKeyImage, Il2CppObject*>(d, k, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppEnumKeyImage, int64_t>(d, k, elemsObj);
            else if (rsKind == 4)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppEnumKeyImage, double>(d, k, elemsObj);
            else if (rsKind == 5)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                    InvokeSelKeyGroupKeyTyped<Il2CppEnumKeyImage, float>(d, k, elemsObj));
            else
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupKeyTyped<Il2CppEnumKeyImage, int32_t>(d, k, elemsObj);
        }
        else if (keyKind >= 8)
        {
            if (keyKind == 8)
            {
                const runtime::Il2CppDecimal k = KeyCarrierToDecimal(kLo, kHi);
                if (refResult)
                {
                    Il2CppObject* r = InvokeSelKeyGroupKeyTyped<runtime::Il2CppDecimal, Il2CppObject*>(d, k, elemsObj);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                }
                else if (rsKind == 3)
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDecimal, int64_t>(d, k, elemsObj);
                else if (rsKind == 4)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDecimal, double>(d, k, elemsObj);
                else if (rsKind == 5)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDecimal, float>(d, k, elemsObj));
                else
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDecimal, int32_t>(d, k, elemsObj);
            }
            else if (keyKind == 9)
            {
                const runtime::Il2CppDateTime k(kLo, static_cast<int32_t>(kHi));
                if (refResult)
                {
                    Il2CppObject* r = InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTime, Il2CppObject*>(d, k, elemsObj);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                }
                else if (rsKind == 3)
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTime, int64_t>(d, k, elemsObj);
                else if (rsKind == 4)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTime, double>(d, k, elemsObj);
                else if (rsKind == 5)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTime, float>(d, k, elemsObj));
                else
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<runtime::Il2CppDateTime, int32_t>(d, k, elemsObj);
            }
            else
            {
                const Il2CppGuid k = KeyCarrierToGuid(kLo, kHi);
                if (refResult)
                {
                    Il2CppObject* r = InvokeSelKeyGroupKeyTyped<Il2CppGuid, Il2CppObject*>(d, k, elemsObj);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                }
                else if (rsKind == 3)
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<Il2CppGuid, int64_t>(d, k, elemsObj);
                else if (rsKind == 4)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<Il2CppGuid, double>(d, k, elemsObj);
                else if (rsKind == 5)
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        InvokeSelKeyGroupKeyTyped<Il2CppGuid, float>(d, k, elemsObj));
                else
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        InvokeSelKeyGroupKeyTyped<Il2CppGuid, int32_t>(d, k, elemsObj);
            }
        }
        else if (keyKind == 7)
        {
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupI8Ref(d, kLo, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupI8Typed<int64_t>(d, kLo, elemsObj);
            else if (rsKind == 4)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupI8Typed<double>(d, kLo, elemsObj);
            else if (rsKind == 5)
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                    InvokeSelKeyGroupI8Typed<float>(d, kLo, elemsObj));
            else
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                    InvokeSelKeyGroupI8I4(d, kLo, elemsObj);
        }
        else
        {
            if (refResult)
            {
                Il2CppObject* r = InvokeSelKeyGroupRef(d, kI4, elemsObj);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
            }
            else if (rsKind == 3)
            {
                const int64_t r = InvokeSelKeyGroupTyped<int64_t>(d, kI4, elemsObj);
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
            }
            else if (rsKind == 4)
            {
                const double r = InvokeSelKeyGroupTyped<double>(d, kI4, elemsObj);
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = r;
            }
            else if (rsKind == 5)
            {
                const float r = InvokeSelKeyGroupTyped<float>(d, kI4, elemsObj);
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(r);
            }
            else
            {
                const int32_t r = InvokeSelKeyGroupI4(d, kI4, elemsObj);
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
            }
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
                                            int32_t rsKind, int32_t keyKind,
                                            int32_t srcKind)
{
    return il2cpp_linq_group_by_full_key_cell(src, keySel, elemSel, cmp, resSel, elemKind,
                                              rsKind, keyKind, srcKind, /*srcCell=*/0);
}

// impl-groupbykey: the NARROW-source form. srcKind 7 (the 1/2/4-byte integral
// families) needs the SOURCE CELL CODE -- the transpiler's LinqNarrowCode --
// so the kernel reads sbyte/byte/bool/short/ushort/char/uint at its real width
// before widening into the int32 selector slot.
Il2CppObject* il2cpp_linq_group_by_full_key_cell(Il2CppArray* src, Il2CppObject* keySel,
                                                 Il2CppObject* elemSel, Il2CppObject* cmp,
                                                 Il2CppObject* resSel, int32_t elemKind,
                                                 int32_t rsKind, int32_t keyKind,
                                                 int32_t srcKind, int32_t srcCell)
{
    if (cmp == nullptr)
    {
        // No comparer supplied: the default int32 equality (nullptr).
    }
    if (elemSel == nullptr)
    {
        // key-only: the element IS the source element. An int32 source keeps
        // the int32 bucket; a REFERENCE source keeps the reference bucket (the
        // element is the source Il2CppObject*); a GUID source keeps the 16-byte
        // Guid bucket. impl-linq2 (C): a DATETIME source keeps the DateTime
        // bucket -- the old derivation fell through to the int32 code for a
        // DateTime source, so every key-only bucket cell was a ZERO int32 (the
        // source element is read as the 16-byte runtime struct and the Int32
        // arm stored elemI4 = 0 -- a silent wrong bucket).
        // impl-groupbykey: the DECIMAL (2) / int64 (5) / double (6) source
        // families keep their own bucket family too; a narrow source (7) rides
        // the int32 bucket because every 1/2/4-byte cell widens into that slot.
        elemKind = srcKind == 4 ? 6
            : srcKind == 3 ? 5
            : srcKind == 2 ? 4
            : srcKind == 5 ? 2
            : srcKind == 6 ? 3
            : ((src->isRefElem != 0) ? 1 : 0);
    }
    GroupElemKind kind = GroupElemKind::Int32;
    switch (elemKind)
    {
        case 0: kind = GroupElemKind::Int32; break;
        case 1: kind = GroupElemKind::Ref; break;
        case 2: kind = GroupElemKind::I8; break;
        case 3: kind = GroupElemKind::D; break;
        case 4: kind = GroupElemKind::Decimal; break;
        case 5: kind = GroupElemKind::DateTime; break;
        case 6: kind = GroupElemKind::Guid; break;
        default:
            il2cpp_throw_not_supported_exception("GroupBy: unsupported TElement family code.");
    }
    Il2CppObject* result = BuildGroups(src, keySel, elemSel, kind, cmp, keyKind, srcKind, srcCell);
    if (rsKind == 0)
    {
        return result;
    }
    if (rsKind < 1 || rsKind > 5)
    {
        il2cpp_throw_not_supported_exception("GroupBy: unsupported TResult family code.");
    }
    return reinterpret_cast<Il2CppObject*>(
        ApplyResultSelector(result, resSel, rsKind, keyKind));
}

// B4-1 (P0-04): the identity-key entry every pre-existing caller keeps using.
Il2CppObject* il2cpp_linq_group_by_full(Il2CppArray* src, Il2CppObject* keySel,
                                        Il2CppObject* elemSel, Il2CppObject* cmp,
                                        Il2CppObject* resSel, int32_t elemKind, int32_t rsKind)
{
    return il2cpp_linq_group_by_full_key(src, keySel, elemSel, cmp, resSel,
                                         elemKind, rsKind, /*keyKind=*/0);
}

// p0grp: GroupBy over a DECIMAL-element array source (int32-slot key).
//
// The int32-source kernel (BuildGroups) reads its elements through I4At (a
// 4-byte cell) and materializes int32 buckets. A System.Decimal[] source has
// 16-byte cells, so this entry is a separate pass over the same managed object
// model:
//
//   * RequireDecimalFamily validates the 16-byte non-reference cell layout;
//   * the key selector is invoked with the decimal element BY VALUE
//     (InvokeSelDecI4 -- the generated Func<Decimal,int> leaf ABI);
//   * the bucket key is the int32 slot narrowed by keyKind (the same
//     KeyKindFor codes the integer entry uses -- byte/sbyte/short/ushort/
//     char/bool/uint narrowing, int32 identity);
//   * each bucket stores its elements as 16-byte decimal VALUE cells (no
//     boxing: scale and sign are copied verbatim), so a bucket's element
//     array is a real decimal[] whose layout matches net48's ToArray.
//
// The shared IGrouping readers dispatch on the bucket array's own layout:
// il2cpp_linq_grouping_get_elements copies by elemSize (so 16-byte cells are
// copied whole) and il2cpp_linq_grouping_get_enumerator boxes a decimal cell
// as System.Decimal. The int32-only bucket OPERATORS (Where/Select/Sum/Min/
// Max/First/ElementAt/Contains/Count(pred)/Any(pred)/All/ToArray) refuse a
// non-4-byte bucket loudly (RequireI4GroupElements) instead of reading a
// 16-byte cell as an int32 -- a silent wrong value is never possible.
//
// GC safety follows BuildGroups: the source / delegate / result are rooted
// across the callback loop, and every cached pointer is re-derived from the
// rooted result after an allocation (bucket growth).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_group_by_dec_key(Il2CppArray* src, Il2CppObject* keySel,
                                           int32_t keyKind)
{
    RequireArray(src);
    Il2CppDelegate* kd = RequireCallback(keySel, "keySelector");
    RequireDecimalFamily(src);

    EnsureGroupingClassesRegistered();

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&kd));
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
        const runtime::Il2CppDecimal v = *DecAt(src, i);
        // BATCH4: the key rides its real family carrier (int32 slot, int64, or a
        // 16-byte struct). The callback may allocate -> collect, so every
        // consumer re-derives its pointers from the rooted result.
        // impl-groupbykey: the dispatch is the SHARED key-family kernel, so a
        // decimal source accepts every key family (7 int64 / 8 decimal /
        // 9 DateTime / 10 Guid / 11 reference / 12 TimeSpan / 13 DateTimeOffset
        // / 14 user enum / 0..6 int32-slot) through the same leaf ABI.
        int64_t keyLo = 0;
        int64_t keyHi = 0;
        Il2CppObject* keyRef = nullptr;
        ComputeGroupKey<runtime::Il2CppDecimal>(kd, v, keyKind, keyLo, keyHi, keyRef);

        res = AsGroupingResult(result);
        const int32_t n = res->count;

        int32_t found = -1;
        for (int32_t j = 0; j < n; ++j)
        {
            Il2CppLinqGroupingResult* cur = AsGroupingResult(result);
            Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(cur->groups->data);
            if (GroupKeysEqual(nullptr, keyKind, keyLo, keyHi, keyRef, AsGrouping(slots[j])))
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
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(grp->elements, at)) = v;
            ++grp->count;
        }
        else
        {
            Il2CppObject* ng = il2cpp_object_new(&g_groupingClass);
            Il2CppLinqGrouping* grp = AsGrouping(ng);
            grp->key = keyLo;
            grp->keyHi = keyHi;
            grp->keyRef = keyRef;
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->keyRef), keyRef);
            grp->elements = nullptr;
            grp->count = 0;
            res = AsGroupingResult(result);
            Il2CppObject** groupSlots = reinterpret_cast<Il2CppObject**>(res->groups->data);
            il2cpp_write_barrier(reinterpret_cast<void**>(&groupSlots[res->count]), ng);
            groupSlots[res->count] = ng;
            ++res->count;

            Il2CppArray* elems = il2cpp_array_new(
                nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, kMinGroupItems);
            res = AsGroupingResult(result); // re-derive after the allocation
            grp = AsGrouping(*reinterpret_cast<Il2CppObject**>(
                res->groups->data + (res->count - 1) * sizeof(void*)));
            grp->elements = elems;
            il2cpp_write_barrier(reinterpret_cast<void**>(&grp->elements),
                                 reinterpret_cast<Il2CppObject*>(elems));
            *reinterpret_cast<runtime::Il2CppDecimal*>(
                il2cpp_array_get_elem_ptr(grp->elements, 0)) = v;
            grp->count = 1;
        }
    }
    return result;
}

int32_t il2cpp_linq_grouping_sum(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    Il2CppLinqGrouping* grp = AsGrouping(grouping);
    // p0grp: a decimal-source bucket holds 16-byte System.Decimal cells; the
    // int32 accumulator would silently reinterpret every decimal (and the
    // checked add would be meaningless). Reject it loudly instead.
    if (grp->elements == nullptr || grp->elements->isRefElem ||
        grp->elements->elemSize != static_cast<int32_t>(sizeof(int32_t)))
    {
        il2cpp_throw_not_supported_exception(
            "GroupBy bucket operator: the bucket holds non-int32 elements "
            "(a decimal-source bucket has no int32 operator kernel).");
        return 0; // unreachable (the factory throws)
    }
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
    return static_cast<int32_t>(AsGrouping(grouping)->key);
}

//------------------------------------------------------------------------------
// BATCH4: the wide-key accessors. Each returns the bucket key in its REAL
// family ABI; the transpiler dispatches get_Key on the tracked grouping type
// suffix (System.Linq.IGrouping_long / _dec / _dt / _guid).
//------------------------------------------------------------------------------
int64_t il2cpp_linq_grouping_get_key_i8(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->key;
}

void il2cpp_linq_grouping_get_key_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    const Il2CppLinqGrouping* grp = AsGrouping(grouping);
    std::memcpy(result, &grp->key, sizeof(int64_t));
    std::memcpy(reinterpret_cast<uint8_t*>(result) + 8, &grp->keyHi, sizeof(int64_t));
}

runtime::Il2CppDateTime il2cpp_linq_grouping_get_key_dt(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    const Il2CppLinqGrouping* grp = AsGrouping(grouping);
    return runtime::Il2CppDateTime(grp->key, static_cast<int32_t>(grp->keyHi));
}

void il2cpp_linq_grouping_get_key_guid(Il2CppObject* grouping, uint8_t bytes[16])
{
    ThrowIfNullReceiver(grouping);
    const Il2CppLinqGrouping* grp = AsGrouping(grouping);
    std::memcpy(bytes, &grp->key, sizeof(int64_t));
    std::memcpy(bytes + 8, &grp->keyHi, sizeof(int64_t));
}

// p0grp: the REFERENCE-key accessor (the bucket key is an Il2CppObject*).
Il2CppObject* il2cpp_linq_grouping_get_key_ref(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->keyRef;
}

// impl-groupbykey: the System.TimeSpan key accessor (the bucket carrier is the
// raw ticks count; TimeSpan equality is ticks-only, so no second word exists).
runtime::Il2CppTimeSpan il2cpp_linq_grouping_get_key_ts(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return runtime::Il2CppTimeSpan(AsGrouping(grouping)->key);
}

// impl-groupbykey: the System.DateTimeOffset key accessor. The carrier keeps
// the UTC instant in `key` (the equality slot) and the offset minutes in
// `keyHi`; the value must be reconstructed with BOTH to round-trip.
runtime::Il2CppDateTimeOffset il2cpp_linq_grouping_get_key_dto(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    const Il2CppLinqGrouping* grp = AsGrouping(grouping);
    runtime::Il2CppDateTimeOffset v;
    v.utcTicks = grp->key;
    v.offsetMinutes = static_cast<int32_t>(grp->keyHi);
    return v;
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
    const int32_t elemSize = grp->elements != nullptr
        ? grp->elements->elemSize
        : static_cast<int32_t>(sizeof(int32_t));
    Il2CppArray* out = isRef
        ? il2cpp_array_new(nullptr, sizeof(void*), true, grp->count)
        : il2cpp_array_new(nullptr, elemSize, false, grp->count);
    // p0grp: propagate the VALUE element kind (a decimal bucket has no
    // recorded kind, an int32 bucket records kElemKindI4) so downstream Array
    // scans see the same element identity as the source array.
    if (grp->elements != nullptr) out->elemKind = grp->elements->elemKind;
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
        // p0grp: copy VALUE cells at the bucket's own element width. The int32
        // family is 4 bytes; a decimal-source bucket is 16-byte
        // System.Decimal cells (the pre-fix int32 loop kept only the low
        // dword of every decimal -- a silent wrong value).
        std::memcpy(out->data, grp->elements->data,
                    static_cast<size_t>(grp->count) * static_cast<size_t>(elemSize));
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
            // p0grp / impl-linq2 (C): VALUE cells dispatch on the bucket's OWN
            // recorded element kind (elemSize alone cannot tell the 16-byte
            // System.Decimal cell from the 16-byte System.Guid cell -- the old
            // width-only dispatch misboxed every Guid cell as a decimal and
            // threw for the 8-byte DateTime cell). The int32 family boxes a
            // 4-byte cell (the i4 get_Current carrier unboxes it); a DateTime
            // / decimal / Guid bucket boxes its own struct so the matching
            // get_Current carrier unboxes it.
            const Il2CppArray* cells = AsGrouping(grouping)->elements;
            const int32_t elemSize = cells->elemSize;
            if (cells->elemKind == kElemKindDecimal
                || (cells->elemKind == kElemKindNone
                    && elemSize == static_cast<int32_t>(sizeof(runtime::Il2CppDecimal))))
            {
                const runtime::Il2CppDecimal v = *DecAt(AsGrouping(grouping)->elements, i);
                boxed = runtime::icall_box_decimal(&v);
            }
            else if (cells->elemKind == kElemKindGuid)
            {
                Il2CppGuid v{};
                std::memcpy(&v, il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i), sizeof(v));
                boxed = runtime::icall_box_guid(&v);
            }
            else if (cells->elemKind == kElemKindDateTime)
            {
                const runtime::Il2CppDateTime v = *reinterpret_cast<const runtime::Il2CppDateTime*>(
                    il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i));
                boxed = runtime::icall_box_date_time(v);
            }
            else if (cells->elemKind == kElemKindTimeSpan)
            {
                // A TimeSpan bucket element is not produced by any grouping
                // surface today; the recorded kind still has a safe carrier
                // (an 8-byte ticks cell) so the snapshot stays well-typed.
                const int64_t v = *reinterpret_cast<const int64_t*>(
                    il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i));
                boxed = runtime::icall_box_long(v);
            }
            else if (cells->elemKind == kElemKindNone
                     && elemSize == static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)))
            {
                // The legacy untyped 16-byte cell bucket (the pre-impl-linq2
                // decimal bucket allocation) -- keep the historical decimal
                // box. A stamped bucket never reaches this arm.
                const runtime::Il2CppDecimal v = *DecAt(AsGrouping(grouping)->elements, i);
                boxed = runtime::icall_box_decimal(&v);
            }
            else if (elemSize == static_cast<int32_t>(sizeof(int32_t)))
            {
                const int32_t v = *reinterpret_cast<int32_t*>(
                    il2cpp_array_get_elem_ptr(AsGrouping(grouping)->elements, i));
                boxed = il2cpp_box_int32(nullptr, v);
            }
            else
            {
                il2cpp_throw_not_supported_exception(
                    "GroupBy: bucket element width has no enumerator carrier.");
                return nullptr; // unreachable (the factory throws)
            }
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

//------------------------------------------------------------------------------
// loud-p0-probe: the reference-source, int32-RESULT selector aggregates
// (Enumerable.Max/Min<TSource,TResult> over string[]/object[] with a
// Func<TSource,char/int32> selector). The selector result rides the int32
// slot, so the kernel projects inline and takes the max/min with
// Comparer<int32>.Default (ordinal) -- net48's Max<TSource,TResult> uses
// Comparer<TResult>.Default, which for char/int32 is ordinal.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_max_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int32_t best = InvokeSelRefI4(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int32_t v = InvokeSelRefI4(d, RefAt(src, i));
        if (v > best) best = v;
    }
    return best;
}

int32_t il2cpp_linq_min_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int32_t best = InvokeSelRefI4(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int32_t v = InvokeSelRefI4(d, RefAt(src, i));
        if (v < best) best = v;
    }
    return best;
}

//------------------------------------------------------------------------------
// The reference-source selector-aggregate family (net48
// Sum/Min/Max/Average<TSource>(Func<TSource,TResult>)). The source is a
// REFERENCE array; the selector's result crosses in the int32 / int64 /
// F-stack slot. Min/Max on an EMPTY sequence throw InvalidOperationException
// (net48); Sum returns the identity and Average throws on empty (net48).
// The delegate is rooted across the projection loop (it may allocate).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_sum_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int32_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefI4(d, RefAt(src, i));
    }
    return sum;
}

double il2cpp_linq_average_sel_ref_i4(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefI4(d, RefAt(src, i));
    }
    return static_cast<double>(sum) / static_cast<double>(src->length);
}

int64_t il2cpp_linq_sum_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefI8(d, RefAt(src, i));
    }
    return sum;
}

int64_t il2cpp_linq_min_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = InvokeSelRefI8(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t v = InvokeSelRefI8(d, RefAt(src, i));
        if (v < best) best = v;
    }
    return best;
}

int64_t il2cpp_linq_max_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = InvokeSelRefI8(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t v = InvokeSelRefI8(d, RefAt(src, i));
        if (v > best) best = v;
    }
    return best;
}

double il2cpp_linq_average_sel_ref_i8(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefI8(d, RefAt(src, i));
    }
    return static_cast<double>(sum) / static_cast<double>(src->length);
}

double il2cpp_linq_sum_sel_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefD(d, RefAt(src, i));
    }
    return sum;
}

double il2cpp_linq_min_sel_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double best = InvokeSelRefD(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double v = InvokeSelRefD(d, RefAt(src, i));
        if (v < best) best = v;
    }
    return best;
}

double il2cpp_linq_max_sel_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double best = InvokeSelRefD(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double v = InvokeSelRefD(d, RefAt(src, i));
        if (v > best) best = v;
    }
    return best;
}

double il2cpp_linq_average_sel_ref_d(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefD(d, RefAt(src, i));
    }
    return sum / static_cast<double>(src->length);
}

float il2cpp_linq_sum_sel_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    float sum = 0.0f;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += InvokeSelRefF(d, RefAt(src, i));
    }
    return sum;
}

float il2cpp_linq_min_sel_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    float best = InvokeSelRefF(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const float v = InvokeSelRefF(d, RefAt(src, i));
        if (v < best) best = v;
    }
    return best;
}

float il2cpp_linq_max_sel_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    float best = InvokeSelRefF(d, RefAt(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const float v = InvokeSelRefF(d, RefAt(src, i));
        if (v > best) best = v;
    }
    return best;
}

double il2cpp_linq_average_sel_ref_f(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<double>(InvokeSelRefF(d, RefAt(src, i)));
    }
    return sum / static_cast<double>(src->length);
}

//------------------------------------------------------------------------------
// The WIDE-source selector-aggregate family (net48
// Sum/Min/Max/Average<TSource,TResult> with TSource = long / float / double).
// The element crosses in its OWN slot (int64 for long, the F-stack carrier for
// float/double); the selector's result crosses in the int32 / int64 / F-stack
// slot. Min/Max/Average throw InvalidOperationException on an empty source;
// Sum returns the identity.
//------------------------------------------------------------------------------
static int64_t LinqReadI64Elem(Il2CppArray* a, int32_t i)
{
    return *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i));
}

static double LinqReadDElem(Il2CppArray* a, int32_t i)
{
    char* p = il2cpp_array_get_elem_ptr(a, i);
    // The F-stack carrier: a float CELL is 4 bytes, but this runtime's own
    // float-result arrays are 8-byte double cells carrying the float's bits in
    // the low 4 bytes (the same convention as LinqCellReadD).
    return a->elemSize == 8 ? *reinterpret_cast<double*>(p)
                            : static_cast<double>(*reinterpret_cast<float*>(p));
}

template <typename TSource, typename TResult, typename InvokeSel, typename ReadSrc>
static TResult WideSelSumT(Il2CppArray* src, Il2CppDelegate* d, ReadSrc readSrc, InvokeSel invoke)
{
    RequireArray(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    TResult sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<TResult>(invoke(d, readSrc(src, i)));
    }
    return sum;
}

template <typename TSource, typename TResult, typename InvokeSel, typename ReadSrc>
static TResult WideSelMinMaxT(Il2CppArray* src, Il2CppDelegate* d, ReadSrc readSrc,
                             InvokeSel invoke, bool wantMax)
{
    RequireArray(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    TResult best = invoke(d, readSrc(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const TResult v = invoke(d, readSrc(src, i));
        if (wantMax ? (v > best) : (v < best)) best = v;
    }
    return best;
}

template <typename TSource, typename TResult, typename InvokeSel, typename ReadSrc>
static double WideSelAverageT(Il2CppArray* src, Il2CppDelegate* d, ReadSrc readSrc, InvokeSel invoke)
{
    RequireArray(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<double>(invoke(d, readSrc(src, i)));
    }
    return sum / static_cast<double>(src->length);
}

// ---- long (int64) source: Func<long,int32/long/double/float> ----
int32_t il2cpp_linq_sum_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<int64_t,int32_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI4); }
int64_t il2cpp_linq_sum_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<int64_t,int64_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI8); }
double il2cpp_linq_sum_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<int64_t,double>(src, d, LinqReadI64Elem, InvokeSelI8SrcD); }
float il2cpp_linq_sum_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<int64_t,float>(src, d, LinqReadI64Elem, InvokeSelI8SrcF); }

int32_t il2cpp_linq_min_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,int32_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI4, false); }
int64_t il2cpp_linq_min_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,int64_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI8, false); }
double il2cpp_linq_min_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,double>(src, d, LinqReadI64Elem, InvokeSelI8SrcD, false); }
float il2cpp_linq_min_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,float>(src, d, LinqReadI64Elem, InvokeSelI8SrcF, false); }

int32_t il2cpp_linq_max_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,int32_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI4, true); }
int64_t il2cpp_linq_max_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,int64_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI8, true); }
double il2cpp_linq_max_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,double>(src, d, LinqReadI64Elem, InvokeSelI8SrcD, true); }
float il2cpp_linq_max_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<int64_t,float>(src, d, LinqReadI64Elem, InvokeSelI8SrcF, true); }

double il2cpp_linq_average_sel_i8src_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<int64_t,int32_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI4); }
double il2cpp_linq_average_sel_i8src_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<int64_t,int64_t>(src, d, LinqReadI64Elem, InvokeSelI8SrcI8); }
double il2cpp_linq_average_sel_i8src_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<int64_t,double>(src, d, LinqReadI64Elem, InvokeSelI8SrcD); }
double il2cpp_linq_average_sel_i8src_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<int64_t,float>(src, d, LinqReadI64Elem, InvokeSelI8SrcF); }

// ---- double source: Func<double,int32/long/double/float> ----
int32_t il2cpp_linq_sum_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<double,int32_t>(src, d, LinqReadDElem, InvokeSelDSrcI4); }
int64_t il2cpp_linq_sum_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<double,int64_t>(src, d, LinqReadDElem, InvokeSelDSrcI8); }
double il2cpp_linq_sum_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<double,double>(src, d, LinqReadDElem, InvokeSelDSrcD); }
float il2cpp_linq_sum_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<double,float>(src, d, LinqReadDElem, InvokeSelDSrcF); }

int32_t il2cpp_linq_min_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,int32_t>(src, d, LinqReadDElem, InvokeSelDSrcI4, false); }
int64_t il2cpp_linq_min_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,int64_t>(src, d, LinqReadDElem, InvokeSelDSrcI8, false); }
double il2cpp_linq_min_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,double>(src, d, LinqReadDElem, InvokeSelDSrcD, false); }
float il2cpp_linq_min_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,float>(src, d, LinqReadDElem, InvokeSelDSrcF, false); }

int32_t il2cpp_linq_max_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,int32_t>(src, d, LinqReadDElem, InvokeSelDSrcI4, true); }
int64_t il2cpp_linq_max_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,int64_t>(src, d, LinqReadDElem, InvokeSelDSrcI8, true); }
double il2cpp_linq_max_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,double>(src, d, LinqReadDElem, InvokeSelDSrcD, true); }
float il2cpp_linq_max_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<double,float>(src, d, LinqReadDElem, InvokeSelDSrcF, true); }

double il2cpp_linq_average_sel_dsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<double,int32_t>(src, d, LinqReadDElem, InvokeSelDSrcI4); }
double il2cpp_linq_average_sel_dsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<double,int64_t>(src, d, LinqReadDElem, InvokeSelDSrcI8); }
double il2cpp_linq_average_sel_dsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<double,double>(src, d, LinqReadDElem, InvokeSelDSrcD); }
double il2cpp_linq_average_sel_dsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<double,float>(src, d, LinqReadDElem, InvokeSelDSrcF); }

// ---- float source: Func<float,int32/long/double/float> (the float element
// rides the F-stack carrier) ----
int32_t il2cpp_linq_sum_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<float,int32_t>(src, d, LinqReadDElem, InvokeSelFSrcI4); }
int64_t il2cpp_linq_sum_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<float,int64_t>(src, d, LinqReadDElem, InvokeSelFSrcI8); }
double il2cpp_linq_sum_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<float,double>(src, d, LinqReadDElem, InvokeSelFSrcD); }
float il2cpp_linq_sum_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelSumT<float,float>(src, d, LinqReadDElem, InvokeSelFSrcF); }

int32_t il2cpp_linq_min_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,int32_t>(src, d, LinqReadDElem, InvokeSelFSrcI4, false); }
int64_t il2cpp_linq_min_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,int64_t>(src, d, LinqReadDElem, InvokeSelFSrcI8, false); }
double il2cpp_linq_min_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,double>(src, d, LinqReadDElem, InvokeSelFSrcD, false); }
float il2cpp_linq_min_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,float>(src, d, LinqReadDElem, InvokeSelFSrcF, false); }

int32_t il2cpp_linq_max_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,int32_t>(src, d, LinqReadDElem, InvokeSelFSrcI4, true); }
int64_t il2cpp_linq_max_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,int64_t>(src, d, LinqReadDElem, InvokeSelFSrcI8, true); }
double il2cpp_linq_max_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,double>(src, d, LinqReadDElem, InvokeSelFSrcD, true); }
float il2cpp_linq_max_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelMinMaxT<float,float>(src, d, LinqReadDElem, InvokeSelFSrcF, true); }

double il2cpp_linq_average_sel_fsrc_i4(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<float,int32_t>(src, d, LinqReadDElem, InvokeSelFSrcI4); }
double il2cpp_linq_average_sel_fsrc_i8(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<float,int64_t>(src, d, LinqReadDElem, InvokeSelFSrcI8); }
double il2cpp_linq_average_sel_fsrc_d(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<float,double>(src, d, LinqReadDElem, InvokeSelFSrcD); }
double il2cpp_linq_average_sel_fsrc_f(Il2CppArray* src, Il2CppObject* sel)
{ Il2CppDelegate* d = RequireCallback(sel, "selector"); return WideSelAverageT<float,float>(src, d, LinqReadDElem, InvokeSelFSrcF); }

//------------------------------------------------------------------------------
// impl-linq3 (A): the NARROW-INTEGRAL source selector aggregates (net48
// Sum/Min/Max/Average<TSource,TResult> with TSource = sbyte / byte / short /
// ushort / uint / char). The transpiler stores every narrow integral in an
// int32 slot (the arrayext2 narrow value model), so each element cell is read
// at its REAL width and WIDENED to int32 before the int32 selector ABI
// (a Func<byte,int> leaf is emitted as int32_t(int32_t) -- InvokeSelI4*).
// The fold runs over the PROJECTED values with the net48 semantics of the
// dec-projection aggregates (impl-linq2 B-2): the int32/int64 sums are
// CHECKED, the float sum accumulates in DOUBLE and casts at the end,
// Min/Max/Average throw InvalidOperationException on an empty source and Sum
// returns the identity. The delegate is rooted across the projection loop
// (it may allocate); the source is only read, never written.
//------------------------------------------------------------------------------
template <typename TCell>
static void RequireNarrowFamily(Il2CppArray* a)
{
    RequireArray(a);
    if (a->isRefElem || a->elemSize != static_cast<int32_t>(sizeof(TCell)))
    {
        il2cpp_throw_not_supported_exception(
            "The narrow-source selector aggregate requires a one-dimensional "
            "narrow-integral array of the matching element width.");
    }
}

template <typename TCell, typename InvokeSel>
static int32_t NarrowSelSumI4(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int32_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int32_t v = invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
        // net48 Sum<TSource>(Func<TSource,int>) is CHECKED.
        if ((v > 0 && sum > INT32_MAX - v) || (v < 0 && sum < INT32_MIN - v))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += v;
    }
    return sum;
}

template <typename TCell, typename InvokeSel>
static int64_t NarrowSelSumI8(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t v = invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
        // net48 Sum<TSource>(Func<TSource,long>) is CHECKED.
        if ((v > 0 && sum > INT64_MAX - v) || (v < 0 && sum < INT64_MIN - v))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += v;
    }
    return sum;
}

// net48 Sum<TSource>(Func<TSource,float>) accumulates in DOUBLE and casts the
// total to FLOAT at the end.
template <typename TCell, typename InvokeSel>
static float NarrowSelSumF(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
    }
    return static_cast<float>(sum);
}

template <typename TCell, typename InvokeSel>
static double NarrowSelSumD(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
    }
    return sum;
}

// The MIN/MAX/AVERAGE folds share one shape per projection (the dec-projection
// macro pattern): empty -> InvalidOperationException, the first projected
// value seeds the fold, then a single compare per element.
template <typename TCell, typename TResult, typename InvokeSel>
static TResult NarrowSelMinMax(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke, bool wantMax)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    TResult best = invoke(d, static_cast<int32_t>(
        *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, 0))));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const TResult v = invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
        if (wantMax ? (v > best) : (v < best)) best = v;
    }
    return best;
}

// net48 Average<TSource> divides the projected total by the count; the float
// form accumulates in DOUBLE and casts at the end (Average(Func<T,float>)
// returns float).
template <typename TCell, typename TResult, typename InvokeSel>
static double NarrowSelAverage(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<double>(invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i)))));
    }
    return sum / static_cast<double>(src->length);
}

template <typename TCell, typename InvokeSel>
static float NarrowSelAverageF(Il2CppArray* src, Il2CppDelegate* d, InvokeSel invoke)
{
    RequireNarrowFamily<TCell>(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += invoke(d, static_cast<int32_t>(
            *reinterpret_cast<const TCell*>(il2cpp_array_get_elem_ptr(src, i))));
    }
    return static_cast<float>(sum / static_cast<double>(src->length));
}

// The 96 explicit narrow-source entries: a macro keeps every (source, fold)
// pair in lockstep with the semantics above. NREAD expands to the widened
// int32 read of the source's cell type.
#define IL2CPP_LINQ_NARROW_AGG(SRC, CELL)                                              \
    int32_t il2cpp_linq_sum_sel_##SRC##_i4(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelSumI4<CELL>(src, d, InvokeSelI4I4); }                            \
    int64_t il2cpp_linq_sum_sel_##SRC##_i8(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelSumI8<CELL>(src, d, InvokeSelI4I8); }                            \
    float il2cpp_linq_sum_sel_##SRC##_f(Il2CppArray* src, Il2CppObject* sel)           \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelSumF<CELL>(src, d, InvokeSelI4F); }                              \
    double il2cpp_linq_sum_sel_##SRC##_d(Il2CppArray* src, Il2CppObject* sel)          \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelSumD<CELL>(src, d, InvokeSelI4D); }                              \
    int32_t il2cpp_linq_min_sel_##SRC##_i4(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, int32_t>(src, d, InvokeSelI4I4, false); }           \
    int64_t il2cpp_linq_min_sel_##SRC##_i8(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, int64_t>(src, d, InvokeSelI4I8, false); }           \
    float il2cpp_linq_min_sel_##SRC##_f(Il2CppArray* src, Il2CppObject* sel)           \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, float>(src, d, InvokeSelI4F, false); }              \
    double il2cpp_linq_min_sel_##SRC##_d(Il2CppArray* src, Il2CppObject* sel)          \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, double>(src, d, InvokeSelI4D, false); }             \
    int32_t il2cpp_linq_max_sel_##SRC##_i4(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, int32_t>(src, d, InvokeSelI4I4, true); }            \
    int64_t il2cpp_linq_max_sel_##SRC##_i8(Il2CppArray* src, Il2CppObject* sel)        \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, int64_t>(src, d, InvokeSelI4I8, true); }            \
    float il2cpp_linq_max_sel_##SRC##_f(Il2CppArray* src, Il2CppObject* sel)           \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, float>(src, d, InvokeSelI4F, true); }               \
    double il2cpp_linq_max_sel_##SRC##_d(Il2CppArray* src, Il2CppObject* sel)          \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelMinMax<CELL, double>(src, d, InvokeSelI4D, true); }              \
    double il2cpp_linq_average_sel_##SRC##_i4(Il2CppArray* src, Il2CppObject* sel)     \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelAverage<CELL, int32_t>(src, d, InvokeSelI4I4); }                 \
    double il2cpp_linq_average_sel_##SRC##_i8(Il2CppArray* src, Il2CppObject* sel)     \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelAverage<CELL, int64_t>(src, d, InvokeSelI4I8); }                 \
    float il2cpp_linq_average_sel_##SRC##_f(Il2CppArray* src, Il2CppObject* sel)       \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelAverageF<CELL>(src, d, InvokeSelI4F); }                          \
    double il2cpp_linq_average_sel_##SRC##_d(Il2CppArray* src, Il2CppObject* sel)      \
    { Il2CppDelegate* d = RequireCallback(sel, "selector");                            \
      return NarrowSelAverage<CELL, double>(src, d, InvokeSelI4D); }

IL2CPP_LINQ_NARROW_AGG(i1src, int8_t)
IL2CPP_LINQ_NARROW_AGG(u1src, uint8_t)
IL2CPP_LINQ_NARROW_AGG(i2src, int16_t)
IL2CPP_LINQ_NARROW_AGG(u2src, uint16_t)
IL2CPP_LINQ_NARROW_AGG(u4src, uint32_t)
IL2CPP_LINQ_NARROW_AGG(csrc, char16_t)
#undef IL2CPP_LINQ_NARROW_AGG

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

Il2CppArray* il2cpp_linq_select_idx_ref_ref(Il2CppArray* src, Il2CppObject* sel)
{
    RequireArray(src);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RequireRefFamily(src);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    // Index-aware reference selector (net48 Func<TSource,int,TResult>): the
    // index is the element's 0-based SOURCE position, and the selector runs
    // EXACTLY ONCE per element. The callback runs FIRST; the result slot is
    // computed afterwards through the rooted 'out' (a cached slot address
    // would be stale after a collecting selector). The result is a reference
    // array, so the store goes through the write barrier.
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, src->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < src->length; ++i)
    {
        Il2CppObject* r = InvokeSelRefRefIdx(d, RefAt(src, i), i);
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
        *slot = r;
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
//
// p0grp: these operators are int32-bucket ONLY. A decimal-source bucket stores
// 16-byte System.Decimal cells, so reading it through the int32 kernels would
// silently reinterpret every decimal as an int32 -- the guard below rejects
// such a bucket loudly instead. (Count / Any / get_Key / get_elements stay
// family-agnostic: they never read an element as an int32.)
//------------------------------------------------------------------------------
Il2CppArray* RequireI4GroupElements(Il2CppObject* grouping)
{
    Il2CppArray* elems = il2cpp_linq_grouping_get_elements(grouping);
    if (elems == nullptr || elems->isRefElem ||
        elems->elemSize != static_cast<int32_t>(sizeof(int32_t)))
    {
        il2cpp_throw_not_supported_exception(
            "GroupBy bucket operator: the bucket holds non-int32 elements "
            "(a decimal-source bucket has no int32 operator kernel).");
        return nullptr; // unreachable (the factory throws)
    }
    return elems;
}

Il2CppArray* il2cpp_linq_grouping_where(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_where_i4(RequireI4GroupElements(grouping), pred);
}

Il2CppArray* il2cpp_linq_grouping_select(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_select_i4_i4(RequireI4GroupElements(grouping), sel);
}

int32_t il2cpp_linq_grouping_count_pred(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_count_i4(RequireI4GroupElements(grouping), pred);
}

int32_t il2cpp_linq_grouping_sum_sel(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    // net48 Sum(selector) == Sum(Select(selector)) -- checked addition.
    Il2CppArray* projected = il2cpp_linq_select_i4_i4(
        RequireI4GroupElements(grouping), sel);
    return il2cpp_linq_sum_i4(projected);
}

double il2cpp_linq_grouping_average_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_average_i4(RequireI4GroupElements(grouping));
}

double il2cpp_linq_grouping_average_sel(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_average_sel_i4(RequireI4GroupElements(grouping), sel);
}

int32_t il2cpp_linq_grouping_min_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_min_i4(RequireI4GroupElements(grouping));
}

int32_t il2cpp_linq_grouping_max_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_max_i4(RequireI4GroupElements(grouping));
}

bool il2cpp_linq_grouping_any(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return AsGrouping(grouping)->count > 0;
}

bool il2cpp_linq_grouping_any_pred(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_any_i4(RequireI4GroupElements(grouping), pred);
}

bool il2cpp_linq_grouping_all(Il2CppObject* grouping, Il2CppObject* pred)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_all_i4(RequireI4GroupElements(grouping), pred);
}

int32_t il2cpp_linq_grouping_first_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_first_i4(RequireI4GroupElements(grouping));
}

int32_t il2cpp_linq_grouping_first_or_default_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_first_or_default_i4(RequireI4GroupElements(grouping));
}

int32_t il2cpp_linq_grouping_last_i4(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_last_i4(RequireI4GroupElements(grouping));
}

int32_t il2cpp_linq_grouping_element_at_i4(Il2CppObject* grouping, int32_t index)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_element_at_i4(RequireI4GroupElements(grouping), index);
}

bool il2cpp_linq_grouping_contains_i4(Il2CppObject* grouping, int32_t value)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_contains_i4(RequireI4GroupElements(grouping), value);
}

//------------------------------------------------------------------------------
// BATCH4: the DECIMAL-bucket operators. A decimal-source bucket stores 16-byte
// System.Decimal VALUE cells; these entries run the decimal array kernels over
// the bucket's exact-size element snapshot (RequireDecGroupElements) instead of
// reinterpreting every cell through the int32 kernels (a silent wrong value).
// Count / Any() / get_Key / ToArray() stay family-agnostic.
//------------------------------------------------------------------------------
Il2CppArray* RequireDecGroupElements(Il2CppObject* grouping)
{
    Il2CppArray* elems = il2cpp_linq_grouping_get_elements(grouping);
    if (elems == nullptr || elems->isRefElem ||
        elems->elemSize != static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)))
    {
        il2cpp_throw_not_supported_exception(
            "GroupBy bucket operator: the bucket does not hold System.Decimal elements "
            "(the decimal operator family requires a decimal-source bucket).");
        return nullptr; // unreachable (the factory throws)
    }
    return elems;
}

void il2cpp_linq_grouping_sum_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    il2cpp_linq_sum_dec(result, RequireDecGroupElements(grouping));
}

void il2cpp_linq_grouping_min_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    il2cpp_linq_min_dec(result, RequireDecGroupElements(grouping));
}

void il2cpp_linq_grouping_max_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    il2cpp_linq_max_dec(result, RequireDecGroupElements(grouping));
}

void il2cpp_linq_grouping_average_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    il2cpp_linq_average_dec(result, RequireDecGroupElements(grouping));
}

//------------------------------------------------------------------------------
// P0LINQ1 follow-up: the decimal-bucket SELECTOR aggregates (net48
// Sum/Min/Max/Average<decimal>(bucket, Func<decimal,decimal>)). The bucket's
// 16-byte decimal cells feed the selector (the SelectDecDecKey ABI -- the
// decimal parameter crosses by value, the projection returns by value) and the
// fold runs over the projected values. GC SAFETY: the delegate and the bucket
// snapshot are rooted across every callback.
//------------------------------------------------------------------------------
void il2cpp_linq_grouping_sum_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping,
                                      Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < elems->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecDecKey(d, *DecAt(elems, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
}

void il2cpp_linq_grouping_min_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping,
                                      Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    if (elems->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelDecDecKey(d, *DecAt(elems, 0));
    for (int32_t i = 1; i < elems->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecDecKey(d, *DecAt(elems, i));
        if (runtime::icall_decimal_compare(&elem, &best) < 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_grouping_max_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping,
                                      Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    if (elems->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = InvokeSelDecDecKey(d, *DecAt(elems, 0));
    for (int32_t i = 1; i < elems->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecDecKey(d, *DecAt(elems, i));
        if (runtime::icall_decimal_compare(&elem, &best) > 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_grouping_average_dec_sel(runtime::Il2CppDecimal* result, Il2CppObject* grouping,
                                          Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    if (elems->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    for (int32_t i = 0; i < elems->length; ++i)
    {
        const runtime::Il2CppDecimal elem = InvokeSelDecDecKey(d, *DecAt(elems, i));
        runtime::icall_decimal_add(result, result, &elem);
    }
    runtime::Il2CppDecimal count;
    std::memset(&count, 0, sizeof(runtime::Il2CppDecimal));
    runtime::icall_decimal_ctor_int32(&count, elems->length);
    runtime::icall_decimal_div(result, result, &count);
}

//------------------------------------------------------------------------------
// impl-linq2 (B-2): the decimal-bucket PROJECTION aggregates (net48
// Sum/Min/Max/Average<decimal>(bucket, Func<decimal,int32/int64/float/double>)
// -- Enumerable.cs:1496-1536 / 1703-1739 / 1907-1943 / 2101-2137 generic
// forms). The bucket's decimal cells feed the selector (the InvokeSelDec*
// ABIs -- the decimal element crosses by value) and the fold runs over the
// PROJECTED values: the int32/int64 sums are CHECKED (net48 Sum semantics),
// the float/double folds are IEEE, and Average divides by the count. The old
// mapping LOUD-rejected every non-decimal projection over a decimal bucket.
// GC SAFETY: the delegate and the bucket snapshot are rooted across every
// callback.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_grouping_sum_dec_proj_i4(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    int32_t sum = 0;
    for (int32_t i = 0; i < elems->length; ++i)
    {
        const int32_t v = InvokeSelDecI4(d, *DecAt(elems, i));
        // net48 Sum<TSource>(Func<TSource,int>) is CHECKED.
        if ((v > 0 && sum > INT32_MAX - v) || (v < 0 && sum < INT32_MIN - v))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += v;
    }
    return sum;
}

int64_t il2cpp_linq_grouping_sum_dec_proj_i8(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    int64_t sum = 0;
    for (int32_t i = 0; i < elems->length; ++i)
    {
        const int64_t v = InvokeSelDecI8(d, *DecAt(elems, i));
        // net48 Sum<TSource>(Func<TSource,long>) is CHECKED.
        if ((v > 0 && sum > INT64_MAX - v) || (v < 0 && sum < INT64_MIN - v))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += v;
    }
    return sum;
}

float il2cpp_linq_grouping_sum_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    // net48 Sum<TSource>(Func<TSource,float>) accumulates in DOUBLE and casts
    // the total to float at the end.
    double sum = 0.0;
    for (int32_t i = 0; i < elems->length; ++i)
    {
        sum += InvokeSelDecF(d, *DecAt(elems, i));
    }
    return static_cast<float>(sum);
}

double il2cpp_linq_grouping_sum_dec_proj_d(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    double sum = 0.0;
    for (int32_t i = 0; i < elems->length; ++i)
    {
        sum += InvokeSelDecD(d, *DecAt(elems, i));
    }
    return sum;
}

// The MIN/MAX/AVG entries share one fold shape per projection; a macro keeps
// the sixteen bodies in lockstep with the net48 semantics.
#define IL2CPP_LINQ_GRP_DEC_MINMAX(NAME, RES, INVOKE, CMP_LT)                          \
    RES NAME(Il2CppObject* grouping, Il2CppObject* sel)                                \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        Il2CppArray* elems = RequireDecGroupElements(grouping);                        \
        Il2CppDelegate* d = RequireCallback(sel, "selector");                          \
        RootScope roots;                                                               \
        roots.Add(reinterpret_cast<void**>(&d));                                       \
        roots.Add(reinterpret_cast<void**>(&elems));                                   \
        if (elems->length == 0)                                                        \
            il2cpp_throw_invalid_operation_exception("Sequence contains no elements"); \
        RES best = INVOKE(d, *DecAt(elems, 0));                                        \
        for (int32_t i = 1; i < elems->length; ++i)                                    \
        {                                                                              \
            const RES v = INVOKE(d, *DecAt(elems, i));                                 \
            if (CMP_LT) best = v;                                                      \
        }                                                                              \
        return best;                                                                   \
    }

IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_min_dec_proj_i4, int32_t, InvokeSelDecI4, v < best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_max_dec_proj_i4, int32_t, InvokeSelDecI4, v > best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_min_dec_proj_i8, int64_t, InvokeSelDecI8, v < best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_max_dec_proj_i8, int64_t, InvokeSelDecI8, v > best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_min_dec_proj_f, float, InvokeSelDecF, v < best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_max_dec_proj_f, float, InvokeSelDecF, v > best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_min_dec_proj_d, double, InvokeSelDecD, v < best)
IL2CPP_LINQ_GRP_DEC_MINMAX(il2cpp_linq_grouping_max_dec_proj_d, double, InvokeSelDecD, v > best)
#undef IL2CPP_LINQ_GRP_DEC_MINMAX

#define IL2CPP_LINQ_GRP_DEC_AVG(NAME, RES, INVOKE)                                     \
    double NAME(Il2CppObject* grouping, Il2CppObject* sel)                             \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        Il2CppArray* elems = RequireDecGroupElements(grouping);                        \
        Il2CppDelegate* d = RequireCallback(sel, "selector");                          \
        RootScope roots;                                                               \
        roots.Add(reinterpret_cast<void**>(&d));                                       \
        roots.Add(reinterpret_cast<void**>(&elems));                                   \
        if (elems->length == 0)                                                        \
            il2cpp_throw_invalid_operation_exception("Sequence contains no elements"); \
        double sum = 0.0;                                                              \
        for (int32_t i = 0; i < elems->length; ++i)                                    \
        {                                                                              \
            sum += static_cast<double>(INVOKE(d, *DecAt(elems, i)));                   \
        }                                                                              \
        return sum / static_cast<double>(elems->length);                               \
    }

IL2CPP_LINQ_GRP_DEC_AVG(il2cpp_linq_grouping_average_dec_proj_i4, int32_t, InvokeSelDecI4)
IL2CPP_LINQ_GRP_DEC_AVG(il2cpp_linq_grouping_average_dec_proj_i8, int64_t, InvokeSelDecI8)
IL2CPP_LINQ_GRP_DEC_AVG(il2cpp_linq_grouping_average_dec_proj_d, double, InvokeSelDecD)
#undef IL2CPP_LINQ_GRP_DEC_AVG

// net48 Average<TSource>(Func<TSource,float>) accumulates in DOUBLE and casts
// to FLOAT at the end.
float il2cpp_linq_grouping_average_dec_proj_f(Il2CppObject* grouping, Il2CppObject* sel)
{
    ThrowIfNullReceiver(grouping);
    Il2CppArray* elems = RequireDecGroupElements(grouping);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&elems));
    if (elems->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double sum = 0.0;
    for (int32_t i = 0; i < elems->length; ++i)
    {
        sum += InvokeSelDecF(d, *DecAt(elems, i));
    }
    return static_cast<float>(sum / static_cast<double>(elems->length));
}

bool il2cpp_linq_grouping_contains_dec(Il2CppObject* grouping, runtime::Il2CppDecimal value)
{
    ThrowIfNullReceiver(grouping);
    return il2cpp_linq_contains_family_dec(RequireDecGroupElements(grouping), value);
}

//------------------------------------------------------------------------------
// impl-linq2 (B-3): the DECIMAL-bucket operator surface. Each entry snapshots
// the bucket's exact-size decimal cells (RequireDecGroupElements) and reuses
// the DECIMAL-SOURCE kernel over the snapshot -- the same operator set net48
// exposes over IEnumerable<decimal> (Enumerable.cs:591-1213 / 1341-1352 /
// 605-661 / 732-744). The old mapping rejected every non-agnostic operator
// over a decimal bucket ("the int32 predicate/selector operators have no
// decimal bucket kernel").
//------------------------------------------------------------------------------
#define IL2CPP_LINQ_GRP_DEC_OPS(GRP, SRC)                                              \
    Il2CppArray* il2cpp_linq_grouping_where_dec(Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_where_dec(RequireDecGroupElements(grouping), pred);         \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_dec(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_dec(RequireDecGroupElements(grouping), sel);     \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_i4(Il2CppObject* grouping, Il2CppObject* sel)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_i4(RequireDecGroupElements(grouping), sel);      \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_i8(Il2CppObject* grouping, Il2CppObject* sel)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_i8(RequireDecGroupElements(grouping), sel);      \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_d(Il2CppObject* grouping, Il2CppObject* sel)   \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_d(RequireDecGroupElements(grouping), sel);       \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_f(Il2CppObject* grouping, Il2CppObject* sel)   \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_f(RequireDecGroupElements(grouping), sel);       \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_dec_ref(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_dec_ref(RequireDecGroupElements(grouping), sel);     \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_dec(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_dec(RequireDecGroupElements(grouping), sel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_i4(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_i4(RequireDecGroupElements(grouping), sel);  \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_i8(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_i8(RequireDecGroupElements(grouping), sel);  \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_d(Il2CppObject* grouping, Il2CppObject* sel)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_d(RequireDecGroupElements(grouping), sel);   \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_f(Il2CppObject* grouping, Il2CppObject* sel)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_f(RequireDecGroupElements(grouping), sel);   \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_select_idx_dec_ref(Il2CppObject* grouping, Il2CppObject* sel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_select_idx_dec_ref(RequireDecGroupElements(grouping), sel); \
    }                                                                                  \
    void il2cpp_linq_grouping_first_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_first_dec(result, RequireDecGroupElements(grouping));              \
    }                                                                                  \
    void il2cpp_linq_grouping_first_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_first_or_default_dec(result, RequireDecGroupElements(grouping));   \
    }                                                                                  \
    void il2cpp_linq_grouping_first_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_first_pred_dec(result, RequireDecGroupElements(grouping), pred);   \
    }                                                                                  \
    void il2cpp_linq_grouping_first_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_first_or_default_pred_dec(result, RequireDecGroupElements(grouping), pred); \
    }                                                                                  \
    void il2cpp_linq_grouping_last_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_last_dec(result, RequireDecGroupElements(grouping));               \
    }                                                                                  \
    void il2cpp_linq_grouping_last_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_last_or_default_dec(result, RequireDecGroupElements(grouping));    \
    }                                                                                  \
    void il2cpp_linq_grouping_last_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_last_pred_dec(result, RequireDecGroupElements(grouping), pred);    \
    }                                                                                  \
    void il2cpp_linq_grouping_last_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_last_or_default_pred_dec(result, RequireDecGroupElements(grouping), pred); \
    }                                                                                  \
    void il2cpp_linq_grouping_single_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_single_dec(result, RequireDecGroupElements(grouping));             \
    }                                                                                  \
    void il2cpp_linq_grouping_single_or_default_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_single_or_default_dec(result, RequireDecGroupElements(grouping));  \
    }                                                                                  \
    void il2cpp_linq_grouping_single_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_single_pred_dec(result, RequireDecGroupElements(grouping), pred);  \
    }                                                                                  \
    void il2cpp_linq_grouping_single_or_default_pred_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_single_or_default_pred_dec(result, RequireDecGroupElements(grouping), pred); \
    }                                                                                  \
    void il2cpp_linq_grouping_element_at_dec(runtime::Il2CppDecimal* result, Il2CppObject* grouping, int32_t index) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        il2cpp_linq_element_at_dec(result, RequireDecGroupElements(grouping), index);  \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_reverse_dec(Il2CppObject* grouping)              \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_reverse_dec(RequireDecGroupElements(grouping));             \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_take_dec(Il2CppObject* grouping, int32_t count)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_take_dec(RequireDecGroupElements(grouping), count);         \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_skip_dec(Il2CppObject* grouping, int32_t count)  \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_skip_dec(RequireDecGroupElements(grouping), count);         \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_take_while_dec(Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_take_while_dec(RequireDecGroupElements(grouping), pred);    \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_skip_while_dec(Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_skip_while_dec(RequireDecGroupElements(grouping), pred);    \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_distinct_dec(Il2CppObject* grouping)             \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_distinct_dec(RequireDecGroupElements(grouping));            \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_except_dec(Il2CppObject* grouping, Il2CppArray* second) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_except_dec(RequireDecGroupElements(grouping), second);      \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_concat_dec(Il2CppObject* grouping, Il2CppArray* second) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_concat_dec(RequireDecGroupElements(grouping), second);      \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_default_if_empty_dec(Il2CppObject* grouping, runtime::Il2CppDecimal value) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_default_if_empty_dec(RequireDecGroupElements(grouping), value); \
    }                                                                                  \
    bool il2cpp_linq_grouping_sequence_equal_dec(Il2CppObject* grouping, Il2CppArray* second) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_sequence_equal_dec(RequireDecGroupElements(grouping), second); \
    }                                                                                  \
    int64_t il2cpp_linq_grouping_long_count_dec(Il2CppObject* grouping)                \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_long_count_dec(RequireDecGroupElements(grouping));          \
    }                                                                                  \
    int32_t il2cpp_linq_grouping_count_pred_dec(Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_count_pred_dec(RequireDecGroupElements(grouping), pred);    \
    }                                                                                  \
    bool il2cpp_linq_grouping_any_pred_dec(Il2CppObject* grouping, Il2CppObject* pred) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_any_pred_dec(RequireDecGroupElements(grouping), pred);      \
    }                                                                                  \
    bool il2cpp_linq_grouping_all_dec(Il2CppObject* grouping, Il2CppObject* pred)      \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_all_dec(RequireDecGroupElements(grouping), pred);           \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_i4(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_i4(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_i8(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_i8(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_d(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_d(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_dec(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_dec(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_i4(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_i4(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_i8(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_i8(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_d(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_d(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_dec(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_dec(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_dt(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_dt(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_dt(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_dt(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_dec_guid(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_dec_guid(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_order_by_key_desc_dec_guid(Il2CppObject* grouping, Il2CppObject* keySel) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_order_by_key_desc_dec_guid(RequireDecGroupElements(grouping), keySel); \
    }                                                                                  \
    Il2CppArray* il2cpp_linq_grouping_then_by_key_dec(Il2CppObject* grouping, Il2CppObject* k1Sel, Il2CppObject* k2Sel, int32_t k1desc, int32_t k2desc, int32_t k1fam, int32_t k2fam) \
    {                                                                                  \
        ThrowIfNullReceiver(grouping);                                                 \
        return il2cpp_linq_then_by_key_dec(RequireDecGroupElements(grouping), k1Sel, k2Sel, k1desc, k2desc, k1fam, k2fam); \
    }

IL2CPP_LINQ_GRP_DEC_OPS(a, b)
#undef IL2CPP_LINQ_GRP_DEC_OPS

Il2CppArray* il2cpp_linq_grouping_to_array(Il2CppObject* grouping)
{
    ThrowIfNullReceiver(grouping);
    // Family-agnostic: get_elements copies VALUE cells at the bucket's own
    // element width, so a decimal bucket yields a real decimal[].
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


