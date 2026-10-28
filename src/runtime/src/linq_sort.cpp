//------------------------------------------------------------------------------
// linq_sort.cpp -- System.Linq OrderBy (linq_sort domain, see linq_sort.h).
//
// Implementation notes:
//   - Every entry validates the source array (null -> ArgumentNullException,
//     non-array object -> ArgumentException, element-family mismatch ->
//     NotSupportedException) and the comparator delegate (null ->
//     ArgumentNullException; multicast container / instance-bound ->
//     InvalidOperationException -- the same loud-rejection policy as
//     icall_delegate_get_fn_ptr, design section 2).
//   - The comparator is extracted from the delegate's methodPtr and cast to
//     the fixed per-family signature (int32_t(*)(T,T)).
//   - The stable sort is a comparator-driven top-down MERGE sort (template
//     over the element type; recursion depth O(log n); native scratch
//     buffer). Stability rule: during a merge the LEFT run wins every tie
//     (the right element is taken only when cmp(right, left) < 0), so equal
//     keys keep their source order -- .NET OrderBy is stable.
//   - Copy semantics: the result array is allocated fresh (il2cpp_array_new)
//     and filled from the source AFTER sorting, so the source is never
//     written. Reference-slot stores go through the write barrier.
//   - GC SAFETY (physical contract, il2cpp_gc.h): the comparator is a USER
//     callback that may ALLOCATE, and the collector COMPACTS the young
//     generation -- a collection inside the comparator can move the source
//     and result arrays. Every entry therefore roots both arrays through
//     registered static-root slots (RootScope) and NEVER holds a raw pointer
//     into a managed payload across a comparator invocation:
//       * the int32 sort runs entirely on native heap (std::vector) and
//         writes back through the freshly re-read rooted result only after
//         the last comparator call;
//       * the reference sort sorts native ORDER INDICES, reading each element
//         FRESH through the rooted source array (whose element slots the
//         collector rewrites after a compaction) at every comparison and at
//         materialization -- no stale object address is ever compared or
//         stored.
//   - Deliberately NO shared code with the arraysort domain quicksorts
//     (design section 4.5).
//------------------------------------------------------------------------------

#include "linq_sort.h"

#include "il2cpp_exception.h"
#include "arraysort.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"
#include "string_compare.h"
#include "decimal.h"     // impl-linq3 (B): the decimal-source keyed sort cell
#include "guid.h"        // impl-linq3 (B): the System.Guid key carrier + compare
#include "il2cpp_time.h" // impl-linq3 (B): the System.DateTime key carrier

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <new>
#include <vector>

namespace il2cpp {

namespace {

// The '[' prefix marks an array class descriptor (array.cpp convention).
const char kArrayClassMarker = '[';

// The multicast container descriptor name (delegateext.cpp registration);
// only that class is registered under this name, so a name match identifies
// a multicast composition tree (which has no single methodPtr).
const char kMulticastDelegateName[] = "System.MulticastDelegate";

// The single-cast delegate class name (delegate.cpp descriptor).
const char kDelegateClass[] = "System.Delegate";

// Comparator callback shapes per element family.
using CompareI4Fn = int32_t (*)(int32_t left, int32_t right);
using CompareRefFn = int32_t (*)(Il2CppObject* left, Il2CppObject* right);

// Key-selector callback shapes (il2cpp_linq_order_by_key_i4): the .NET
// Func<TSource,TKey> ABI -- 1 arg, key returned. A STATIC-method leaf
// (target == null) has the fixed family signature int32_t(*)(int32_t); an
// INSTANCE-method leaf (target != null) has `this` (the stored target) in the
// FIRST parameter slot (CodeGenerator: instance arg0 = this -- the linqib
// dispatch, linq.cpp precedent).
using KeyI4Fn = int32_t (*)(int32_t value);
using KeyI4ThisFn = int32_t (*)(Il2CppObject* self, int32_t value);

// Key-selector callback shapes for a REFERENCE source element (the
// Func<TRef,TKey> ABI: the element crosses as Il2CppObject*).
using KeyI4RefFn = int32_t (*)(Il2CppObject* elem);
using KeyI4RefThisFn = int32_t (*)(Il2CppObject* self, Il2CppObject* elem);
using KeyI8RefFn = int64_t (*)(Il2CppObject* elem);
using KeyI8RefThisFn = int64_t (*)(Il2CppObject* self, Il2CppObject* elem);
using KeyDRefFn = double (*)(Il2CppObject* elem);
using KeyDRefThisFn = double (*)(Il2CppObject* self, Il2CppObject* elem);
using KeyRefRefFn = Il2CppObject* (*)(Il2CppObject* elem);
using KeyRefRefThisFn = Il2CppObject* (*)(Il2CppObject* self, Il2CppObject* elem);

// Per-binding dispatch for the reference-source key selectors (target in the
// `this` slot for an instance-bound leaf -- the linqib mode).
int32_t InvokeKeyRefI4(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4RefThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyI4RefFn>(d->methodPtr)(elem);
}

int64_t InvokeKeyRefI8(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8RefThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyI8RefFn>(d->methodPtr)(elem);
}

double InvokeKeyRefD(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDRefThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyDRefFn>(d->methodPtr)(elem);
}

// impl-orderfam: the FLOAT-key reader for a REFERENCE source (the
// Func<TRef,float> ABI). A float leaf returns a 4-byte float per the MS x64
// ABI (XMM0 low 32 bits), NOT a double; reading that return through KeyDRefFn
// mixed the callee's XMM0 residue into every key (the b5-gaps §14 misread --
// the thenby-refkey probe).
using KeyFRefFn = float (*)(Il2CppObject* elem);
using KeyFRefThisFn = float (*)(Il2CppObject* self, Il2CppObject* elem);

float InvokeKeyRefF(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyFRefThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyFRefFn>(d->methodPtr)(elem);
}

Il2CppObject* InvokeKeyRefRef(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefRefThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyRefRefFn>(d->methodPtr)(elem);
}

// Key-selector callback shapes for an INT64 value SOURCE element (p0conv2 D3):
// the Func<long,TKey> ABI -- the element crosses in an int64 slot (the i8
// family's CLR parameter ABI), the key returns in its own family slot.
using KeyI8SrcI4Fn = int32_t (*)(int64_t value);
using KeyI8SrcI4ThisFn = int32_t (*)(Il2CppObject* self, int64_t value);
using KeyI8SrcI8Fn = int64_t (*)(int64_t value);
using KeyI8SrcI8ThisFn = int64_t (*)(Il2CppObject* self, int64_t value);
using KeyI8SrcDFn = double (*)(int64_t value);
using KeyI8SrcDThisFn = double (*)(Il2CppObject* self, int64_t value);
// impl-floatkey: the FLOAT-key reader for an int64 source. A Func<long,float>
// leaf returns a 4-byte float per the MS x64 ABI; reading that return through
// a double(*) fn-pointer mixes the callee's XMM0 residue into every key (the
// b5-gaps §14 misread -- thenby-floatprobe).
using KeyI8SrcFFn = float (*)(int64_t value);
using KeyI8SrcFThisFn = float (*)(Il2CppObject* self, int64_t value);

// Per-binding dispatch for the int64-source key selectors (target in the
// `this` slot for an instance-bound leaf -- the linqib mode).
int32_t InvokeKeyI8SrcI4(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcI4ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcI4Fn>(d->methodPtr)(value);
}

int64_t InvokeKeyI8SrcI8(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcI8ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcI8Fn>(d->methodPtr)(value);
}

double InvokeKeyI8SrcD(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcDThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcDFn>(d->methodPtr)(value);
}

float InvokeKeyI8SrcF(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcFThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcFFn>(d->methodPtr)(value);
}

// Key-selector callback shapes for an INT32 value SOURCE element with a WIDE
// key (the Func<int,long> / Func<int,double> ABI): the element crosses in the
// int32 slot, the key returns in the int64 / F-stack slot.
using KeyI4I8Fn = int64_t (*)(int32_t value);
using KeyI4I8ThisFn = int64_t (*)(Il2CppObject* self, int32_t value);
using KeyI4DFn = double (*)(int32_t value);
using KeyI4DThisFn = double (*)(Il2CppObject* self, int32_t value);
// impl-floatkey: the FLOAT-key reader for an int32 source (the Func<int,float>
// ABI -- a 4-byte float return in the XMM0 low half, NOT a double; reading it
// through KeyI4DFn mixed the callee's XMM0 residue into every key, the
// b5-gaps §14 silent mis-order -- thenby-floatprobe).
using KeyI4FFn = float (*)(int32_t value);
using KeyI4FThisFn = float (*)(Il2CppObject* self, int32_t value);

int64_t InvokeKeyI4I8(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4I8ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4I8Fn>(d->methodPtr)(value);
}

double InvokeKeyI4D(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4DThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4DFn>(d->methodPtr)(value);
}

float InvokeKeyI4F(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4FThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4FFn>(d->methodPtr)(value);
}

//------------------------------------------------------------------------------
// impl-linq3 (B): the STRUCT key-selector ABIs (net48 OrderBy<TSource,
// System.DateTime/Guid>). The generated leaf returns the key struct BY VALUE
// (the MS x64 ABI lowers that to sret -- the same convention the GroupBy
// key selectors use, linq_map.cpp SelI4DtKeyFn / SelI4GuidKeyFn):
//   runtime::Il2CppDateTime (*)(int32_t)            (static)
//   runtime::Il2CppDateTime (*)(Il2CppObject*, int32_t) (instance)
//   Il2CppGuid (*)(int32_t) / (Il2CppObject*, int32_t)
// The element slots follow the source family: int32 (i4), int64 (i8), the
// 16-byte decimal BY VALUE (dec), Il2CppObject* (ref).
//------------------------------------------------------------------------------
typedef runtime::Il2CppDateTime (*KeyI4DtFn)(int32_t value);
typedef runtime::Il2CppDateTime (*KeyI4DtThisFn)(Il2CppObject* self, int32_t value);
typedef Il2CppGuid (*KeyI4GuidFn)(int32_t value);
typedef Il2CppGuid (*KeyI4GuidThisFn)(Il2CppObject* self, int32_t value);

runtime::Il2CppDateTime InvokeKeyI4Dt(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4DtThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4DtFn>(d->methodPtr)(value);
}

Il2CppGuid InvokeKeyI4Guid(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4GuidThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4GuidFn>(d->methodPtr)(value);
}

// The int64-source struct-key ABIs (Func<long,DateTime/Guid>).
typedef runtime::Il2CppDateTime (*KeyI8SrcDtFn)(int64_t value);
typedef runtime::Il2CppDateTime (*KeyI8SrcDtThisFn)(Il2CppObject* self, int64_t value);
typedef Il2CppGuid (*KeyI8SrcGuidFn)(int64_t value);
typedef Il2CppGuid (*KeyI8SrcGuidThisFn)(Il2CppObject* self, int64_t value);

runtime::Il2CppDateTime InvokeKeyI8SrcDt(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcDtThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcDtFn>(d->methodPtr)(value);
}

Il2CppGuid InvokeKeyI8SrcGuid(Il2CppDelegate* d, int64_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI8SrcGuidThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI8SrcGuidFn>(d->methodPtr)(value);
}

// The DECIMAL-source struct-key ABIs (Func<decimal,DateTime/Guid>): the
// decimal element crosses BY VALUE (the InvokeKeyDec* convention).
typedef runtime::Il2CppDateTime (*KeyDecDtFn)(runtime::Il2CppDecimal);
typedef runtime::Il2CppDateTime (*KeyDecDtThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
typedef Il2CppGuid (*KeyDecGuidFn)(runtime::Il2CppDecimal);
typedef Il2CppGuid (*KeyDecGuidThisFn)(Il2CppObject*, runtime::Il2CppDecimal);

runtime::Il2CppDateTime InvokeKeyDecDt(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDecDtThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<KeyDecDtFn>(d->methodPtr)(v);
}

Il2CppGuid InvokeKeyDecGuid(Il2CppDelegate* d, const runtime::Il2CppDecimal& v)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDecGuidThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<KeyDecGuidFn>(d->methodPtr)(v);
}

// The REFERENCE-source struct-key ABIs (Func<object,DateTime/Guid>).
typedef runtime::Il2CppDateTime (*KeyRefDtFn)(Il2CppObject*);
typedef runtime::Il2CppDateTime (*KeyRefDtThisFn)(Il2CppObject*, Il2CppObject*);
typedef Il2CppGuid (*KeyRefGuidFn)(Il2CppObject*);
typedef Il2CppGuid (*KeyRefGuidThisFn)(Il2CppObject*, Il2CppObject*);

runtime::Il2CppDateTime InvokeKeyRefDt(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefDtThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyRefDtFn>(d->methodPtr)(elem);
}

Il2CppGuid InvokeKeyRefGuid(Il2CppDelegate* d, Il2CppObject* elem)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefGuidThisFn>(d->methodPtr)(d->target, elem)
        : reinterpret_cast<KeyRefGuidFn>(d->methodPtr)(elem);
}

// The struct-key comparisons (the key comparison CARRIERS):
//   * DateTime keys compare by their ticks (net48 DateTime.CompareTo is a
//     pure ticks comparison; this runtime has a single-zone Kind model, so
//     the kind field never orders -- icall_datetime_compare_to precedent).
//   * Guid keys defer to the net48 field-wise CompareTo contract
//     (il2cpp_guid_compare, guid.cpp).
int32_t CmpDateTimeKey(const runtime::Il2CppDateTime& a, const runtime::Il2CppDateTime& b)
{
    if (a.ticks < b.ticks) return -1;
    if (a.ticks > b.ticks) return 1;
    return 0;
}

int32_t CmpGuidKey(const Il2CppGuid& a, const Il2CppGuid& b)
{
    return il2cpp_guid_compare(a.b, b.b);
}

// Key-selector callback shapes for a DOUBLE value SOURCE element (the
// Func<double,TKey> ABI): the element crosses in the F-stack slot.
using KeyDSrcDFn = double (*)(double value);
using KeyDSrcDThisFn = double (*)(Il2CppObject* self, double value);

double InvokeKeyDSrcD(Il2CppDelegate* d, double value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDSrcDThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyDSrcDFn>(d->methodPtr)(value);
}

// impl-orderfam: the WIDE ELEMENT-SOURCE key readers (the Func<double,TKey> /
// Func<float,TKey> ABI). The element crosses in the F-stack slot -- a float
// element WIDENS to double, the linq-domain FPredFn / InvokePredF convention
// (linq.cpp) -- and the key returns in its own family slot: int32 in the
// int32 slot, int64 in the int64 slot, double in the F-stack slot, and a
// FLOAT key through a float(*) reader (a 4-byte float return read through a
// double(*) fn-pointer mixes the callee's XMM0 residue into every key -- the
// impl-floatkey b5-gaps §14 misread, the orderwide-elem probe).
using KeyDSrcI4Fn = int32_t (*)(double value);
using KeyDSrcI4ThisFn = int32_t (*)(Il2CppObject* self, double value);
using KeyDSrcI8Fn = int64_t (*)(double value);
using KeyDSrcI8ThisFn = int64_t (*)(Il2CppObject* self, double value);
using KeyDSrcFFn = float (*)(double value);
using KeyDSrcFThisFn = float (*)(Il2CppObject* self, double value);

int32_t InvokeKeyDSrcI4(Il2CppDelegate* d, double value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDSrcI4ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyDSrcI4Fn>(d->methodPtr)(value);
}

int64_t InvokeKeyDSrcI8(Il2CppDelegate* d, double value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDSrcI8ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyDSrcI8Fn>(d->methodPtr)(value);
}

float InvokeKeyDSrcF(Il2CppDelegate* d, double value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyDSrcFThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyDSrcFFn>(d->methodPtr)(value);
}

// Per-binding dispatch. The target is re-read through the ROOTED delegate
// slot (every key-selector entry roots the delegate) so a compaction between
// calls (the selector may allocate) rewrites both the delegate and its target.
int32_t InvokeKeyI4(Il2CppDelegate* d, int32_t value)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyI4ThisFn>(d->methodPtr)(d->target, value)
        : reinterpret_cast<KeyI4Fn>(d->methodPtr)(value);
}

//------------------------------------------------------------------------------
// RootScope -- GC rooting guard for the comparator-driven sort (see the file
// header): both the source and the result array are registered as static-root
// slots so the collector REWRITES THE LOCALS when a comparator-triggered
// collection compacts and moves them. Removal is RAII -- the comparator may
// throw a managed exception, and the roots must not leak (gc.cpp root table
// is fixed-capacity).
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
        // The slot table grows on demand: a fixed table with an std::abort()
        // overflow check would turn a rooting-count mistake into a silent
        // process exit (exit 3, no message, no managed exception). An
        // allocation failure now surfaces as a real managed exception.
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
    std::vector<void**> m_slots; // source + result (+ then-by selectors)
};

// Reference element at 'index', read through the (rooted, hence fresh) array.
Il2CppObject* RefSlot(Il2CppArray* arr, int32_t index)
{
    return *reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(arr, index));
}

//------------------------------------------------------------------------------
// RequireLinqArray -- validate a LINQ source array argument.
//   null                -> ArgumentNullException
//   non-array object    -> ArgumentException (caller bug, never UB)
//------------------------------------------------------------------------------
Il2CppArray* RequireLinqArray(Il2CppArray* arr)
{
    if (arr == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
    }
    if (arr->obj.klass == nullptr || arr->obj.klass->name == nullptr ||
        arr->obj.klass->name[0] != kArrayClassMarker)
    {
        il2cpp_throw_argument_exception("Object is not an array.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireValueLinqArray -- value-array domain contract (i4 family):
// isRefElem == false and the exact element stride (4). Anything else is
// unsupported in this entry.
//------------------------------------------------------------------------------
Il2CppArray* RequireValueLinqArray(Il2CppArray* arr, int32_t elemSize)
{
    if (arr->isRefElem || arr->elemSize != elemSize)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional value-element arrays of the expected stride are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireRefLinqArray -- reference-array domain contract (ref / string
// families). The elements are Il2CppObject* slots; the element class
// descriptor is not consulted (tests and generated code may create reference
// arrays with a null element descriptor).
//------------------------------------------------------------------------------
Il2CppArray* RequireRefLinqArray(Il2CppArray* arr)
{
    if (!arr->isRefElem)
    {
        il2cpp_throw_not_supported_exception(
            "Only one-dimensional reference-element arrays are supported.");
    }
    return arr;
}

//------------------------------------------------------------------------------
// RequireLinqComparator -- validate the comparator delegate (LINQ callback
// contract, design section 2, mirroring icall_delegate_get_fn_ptr):
//   null                -> ArgumentNullException
//   multicast container -> InvalidOperationException (no single methodPtr)
//   instance-bound      -> InvalidOperationException (no target binding)
// Returns the single-cast static-method delegate's methodPtr.
//------------------------------------------------------------------------------
void* RequireLinqComparator(Il2CppObject* cmp)
{
    if (cmp == nullptr)
    {
        il2cpp_throw_argument_null_exception("comparer");
    }
    if (cmp->klass != nullptr && cmp->klass->name != nullptr &&
        std::strcmp(cmp->klass->name, kMulticastDelegateName) == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate is not a valid LINQ comparer (no single function pointer).");
    }
    Il2CppDelegate* leaf = reinterpret_cast<Il2CppDelegate*>(cmp);
    if (leaf->target != nullptr)
    {
        il2cpp_throw_invalid_operation_exception(
            "An instance-method delegate is not a valid LINQ comparer (no target binding).");
    }
    return leaf->methodPtr;
}

//------------------------------------------------------------------------------
// RequireKeySelector -- validate the KEY-SELECTOR delegate (the
// il2cpp_linq_order_by_key_i4 surface; linq.cpp RequireCallback precedent, the
// linqib mode):
//   null                -> ArgumentNullException
//   multicast container -> InvalidOperationException (no single methodPtr)
//   non-delegate object -> ArgumentException (caller bug, never UB)
// INSTANCE-BOUND single-cast selectors ARE accepted -- the invoke side
// dispatches per binding (target in the `this` slot). Returns the validated
// delegate.
//------------------------------------------------------------------------------
Il2CppDelegate* RequireKeySelector(Il2CppObject* keySel)
{
    if (keySel == nullptr)
    {
        il2cpp_throw_argument_null_exception("keySelector");
        return nullptr; // unreachable (the factory throws)
    }
    const char* name = keySel->klass != nullptr ? keySel->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, kMulticastDelegateName) == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a LINQ key selector.");
        return nullptr;
    }
    if (std::strcmp(name, kDelegateClass) == 0)
    {
        return reinterpret_cast<Il2CppDelegate*>(keySel);
    }
    il2cpp_throw_argument_exception("Object is not a delegate.");
    return nullptr;
}

//------------------------------------------------------------------------------
// MergeSortStable -- comparator-driven stable top-down merge sort over
// data[lo..hi) (half-open). 'scratch' is a caller-owned buffer of >= hi
// elements. The LEFT run wins every tie (cmp(right, left) < 0 is the only
// right-run take), which is exactly what makes the sort stable.
//------------------------------------------------------------------------------
template <typename T, typename Compare>
void MergeSortStable(T* data, T* scratch, int32_t lo, int32_t hi, Compare cmp)
{
    if (hi - lo < 2)
    {
        return; // 0 or 1 element: trivially sorted and stable
    }
    const int32_t mid = lo + (hi - lo) / 2;
    MergeSortStable(data, scratch, lo, mid, cmp);
    MergeSortStable(data, scratch, mid, hi, cmp);
    int32_t i = lo;
    int32_t j = mid;
    int32_t k = lo;
    while (i < mid && j < hi)
    {
        if (cmp(data[j], data[i]) < 0)
        {
            scratch[k++] = data[j++]; // strictly smaller right run wins
        }
        else
        {
            scratch[k++] = data[i++]; // tie (and smaller left) keeps left order
        }
    }
    while (i < mid)
    {
        scratch[k++] = data[i++];
    }
    while (j < hi)
    {
        scratch[k++] = data[j++];
    }
    std::memcpy(data + lo, scratch + lo,
                static_cast<size_t>(hi - lo) * sizeof(T));
}

//------------------------------------------------------------------------------
// SortInt32Copy -- build a new int32 array holding the stable OrderBy of
// 'src' under 'cmp'. The source is only read; the sort runs entirely on
// native heap (std::vector -- value elements cannot go stale), and the result
// payload is written back through the freshly re-read rooted array only after
// the last comparator call (no collection can interleave there).
//------------------------------------------------------------------------------
Il2CppArray* SortInt32Copy(Il2CppArray* src, CompareI4Fn cmp)
{
    const int32_t n = src->length;

    // 'src' must survive the result allocation; 'result' must survive the
    // comparator phase (a comparator may allocate -> compaction can move both).
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(int32_t), false, n);
    roots.Add(reinterpret_cast<void**>(&result));

    if (n > 0)
    {
        std::vector<int32_t> work(static_cast<size_t>(n));
        std::memcpy(work.data(), src->data,
                    static_cast<size_t>(n) * sizeof(int32_t));
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(work.data(), scratch.data(), 0, n, cmp);
        // After the last comparator call: no allocation -> no collection can
        // interleave between re-reading result->data and the store.
        std::memcpy(result->data, work.data(),
                    static_cast<size_t>(n) * sizeof(int32_t));
    }
    return result;
}

//------------------------------------------------------------------------------
// SortRefsCopy -- build a new reference array holding the stable OrderBy of
// 'src' under 'cmp' (Il2CppObject* slots). The source is only read. The sort
// orders native INDEX slots with the comparator reading each element FRESH
// through the rooted source (whose element slots the collector rewrites after
// a compaction), and the result is materialized from the source by index
// after the last comparator call -- so no stale object address is ever
// compared or stored. Every reference-slot store of the result runs the write
// barrier (bulk copy, then a single barrier pass -- the il2cpp_array_copy_to
// pattern).
//------------------------------------------------------------------------------
Il2CppArray* SortRefsCopy(Il2CppArray* src, CompareRefFn cmp)
{
    const int32_t n = src->length;

    // 'src' must survive the result allocation; 'result' must survive the
    // comparator phase.
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* result = il2cpp_array_new(src->elemClass, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&result));

    if (n > 0)
    {
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            return cmp(RefSlot(src, a), RefSlot(src, b));
                        });
        // Materialize the result from the rooted source by index: no
        // comparator runs here and no allocation separates the re-read from
        // the store, so every stored address is current.
        for (int32_t i = 0; i < n; ++i)
        {
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(result, i));
            *slot = RefSlot(src, order[i]);
        }
        Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// SortInt32KeyCopy -- build a new int32 array holding the stable OrderBy of
// 'src' under the KEY SELECTOR 'keySel' (net48 EnumerableSorter semantics):
// every element's key is computed EXACTLY ONCE and cached, then the sort
// orders native INDICES by the cached keys and the result is materialized
// from the element snapshot by index.
//
// GC SAFETY (physical contract, il2cpp_gc.h): the selector is a USER callback
// that may ALLOCATE, and the collector COMPACTS the young generation. Two
// shields:
//   * the elements are snapshotted into the native `elems` vector BEFORE any
//     selector call (no managed allocation precedes the memcpy, so 'src'
//     cannot have moved) -- the key phase and the materialization never touch
//     managed element memory;
//   * the delegate is ROOTED across the key phase (its target is a GC
//     reference re-read on every dispatch).
// The result array is allocated ONLY after the last selector call, and no
// allocation separates its creation from the payload writes, so it cannot be
// moved between creation and fill.
//------------------------------------------------------------------------------
// Three-way key comparison (net48 Comparer<T>.Default for the numeric key
// families): negative / zero / positive. Shared by every keyed sort entry.
template <typename T>
int32_t CmpKeyValue(T a, T b)
{
    return (a > b) ? 1 : ((a < b) ? -1 : 0);
}

template <typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortInt32KeyCopyT(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                               InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;

    // The delegate survives the key phase (a selector may allocate ->
    // compaction can move it and its target). 'src' is read only during the
    // pre-callback snapshot below; it is rooted defensively anyway (the
    // house pattern).
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n > 0)
    {
        // Element snapshot taken before ANY selector call: no managed
        // allocation preceded this memcpy, so 'src' cannot have been moved.
        std::vector<int32_t> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(int32_t));
        std::vector<TKey> keys(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        // Cache each element's key EXACTLY ONCE (net48 EnumerableSorter):
        // a side-effecting selector is never re-invoked.
        for (int32_t i = 0; i < n; ++i)
        {
            keys[i] = invokeKey(keySel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        // Order INDICES by the cached keys; the merge keeps the LEFT run on
        // ties (cmp(right, left) < 0 is the only right-run take), so equal
        // keys keep their original index order -- the stable contract.
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c = cmp(keys[a], keys[b]);
                            return desc ? -c : c;
                        });
        // Materialize from the native snapshot by index. Last selector call
        // done; the result allocation below cannot be followed by a move.
        Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(int32_t), false, n);
        int32_t* out = reinterpret_cast<int32_t*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new(nullptr, sizeof(int32_t), false, 0);
}

Il2CppArray* SortInt32KeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortInt32KeyCopyT<int32_t>(src, keySel, desc, InvokeKeyI4,
                                      CmpKeyValue<int32_t>);
}

//------------------------------------------------------------------------------
// p0grp: the DECIMAL-source keyed sort. The element snapshot holds the 16-byte
// System.Decimal cells; the key selector takes the decimal BY VALUE (the
// InvokeSelDec* ABI). Same GC-safety two shields as SortInt32KeyCopyT.
//------------------------------------------------------------------------------
static int32_t InvokeKeyDecI4(Il2CppDelegate* keySel, const runtime::Il2CppDecimal& v)
{
    typedef int32_t (*Fn)(runtime::Il2CppDecimal);
    typedef int32_t (*ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
    return keySel->target != nullptr
        ? reinterpret_cast<ThisFn>(keySel->methodPtr)(keySel->target, v)
        : reinterpret_cast<Fn>(keySel->methodPtr)(v);
}

template <typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortDecKeyCopyT(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                             InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));
    if (n > 0)
    {
        std::vector<runtime::Il2CppDecimal> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(runtime::Il2CppDecimal));
        std::vector<TKey> keys(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) order[i] = i;
        for (int32_t i = 0; i < n; ++i) keys[i] = invokeKey(keySel, elems[i]);
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c = cmp(keys[a], keys[b]);
                            return desc ? -c : c;
                        });
        Il2CppArray* result = il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
        runtime::Il2CppDecimal* out = reinterpret_cast<runtime::Il2CppDecimal*>(result->data);
        for (int32_t i = 0; i < n; ++i) out[i] = elems[order[i]];
        return result;
    }
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, 0);
}

Il2CppArray* SortDecI4KeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortDecKeyCopyT<int32_t>(src, keySel, desc, InvokeKeyDecI4,
                                    CmpKeyValue<int32_t>);
}

//------------------------------------------------------------------------------
// impl-linqleft: SortDecTwoKeysCopyT -- the DECIMAL-source counterpart of
// SortInt32TwoKeysCopy / SortInt64TwoKeysCopy. Builds a fresh decimal[] holding
// the stable (k1, k2) composite order of 'src' under the key selectors 'k1Sel'
// and 'k2Sel' (net48 EnumerableSorter chain semantics for
// OrderBy(k1).ThenBy(k2) / .ThenByDescending(k2)). Each element's PRIMARY key
// and SECONDARY key are cached EXACTLY ONCE, so a side-effecting selector is
// never re-invoked. The two key types are INDEPENDENT (e.g. DateTime primary +
// Guid secondary). GC SAFETY: the elements are snapshotted into a native
// vector BEFORE any selector call and BOTH selectors are rooted across the key
// phase; the result array is allocated only after the last selector call.
//------------------------------------------------------------------------------
template <typename TK1, typename TK2, typename Invoke1, typename Invoke2,
          typename Cmp1, typename Cmp2>
Il2CppArray* SortDecTwoKeysCopyT(Il2CppArray* src, Il2CppDelegate* k1Sel,
                                 Il2CppDelegate* k2Sel, bool k1Desc, bool k2Desc,
                                 Invoke1 invoke1, Invoke2 invoke2,
                                 Cmp1 cmp1, Cmp2 cmp2)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&k1Sel));
    roots.Add(reinterpret_cast<void**>(&k2Sel));

    if (n > 0)
    {
        std::vector<runtime::Il2CppDecimal> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(runtime::Il2CppDecimal));
        std::vector<TK1> k1(static_cast<size_t>(n));
        std::vector<TK2> k2(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) order[i] = i;
        for (int32_t i = 0; i < n; ++i) k1[i] = invoke1(k1Sel, elems[i]);
        for (int32_t i = 0; i < n; ++i) k2[i] = invoke2(k2Sel, elems[i]);
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c1 = cmp1(k1[a], k1[b]);
                            if (c1 != 0)
                            {
                                return k1Desc ? -c1 : c1;
                            }
                            const int32_t c2 = cmp2(k2[a], k2[b]);
                            return k2Desc ? -c2 : c2;
                        });
        Il2CppArray* result = il2cpp_array_new(
            nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
        runtime::Il2CppDecimal* out =
            reinterpret_cast<runtime::Il2CppDecimal*>(result->data);
        for (int32_t i = 0; i < n; ++i) out[i] = elems[order[i]];
        return result;
    }
    return il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, 0);
}

// p0grp: the DECIMAL-source keyed sort with an int64 / double / decimal key.
static int64_t InvokeKeyDecI8(Il2CppDelegate* keySel, const runtime::Il2CppDecimal& v)
{
    typedef int64_t (*Fn)(runtime::Il2CppDecimal);
    typedef int64_t (*ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
    return keySel->target != nullptr
        ? reinterpret_cast<ThisFn>(keySel->methodPtr)(keySel->target, v)
        : reinterpret_cast<Fn>(keySel->methodPtr)(v);
}

static double InvokeKeyDecD(Il2CppDelegate* keySel, const runtime::Il2CppDecimal& v)
{
    typedef double (*Fn)(runtime::Il2CppDecimal);
    typedef double (*ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
    return keySel->target != nullptr
        ? reinterpret_cast<ThisFn>(keySel->methodPtr)(keySel->target, v)
        : reinterpret_cast<Fn>(keySel->methodPtr)(v);
}

static runtime::Il2CppDecimal InvokeKeyDecDec(Il2CppDelegate* keySel, const runtime::Il2CppDecimal& v)
{
    typedef runtime::Il2CppDecimal (*Fn)(runtime::Il2CppDecimal);
    typedef runtime::Il2CppDecimal (*ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
    return keySel->target != nullptr
        ? reinterpret_cast<ThisFn>(keySel->methodPtr)(keySel->target, v)
        : reinterpret_cast<Fn>(keySel->methodPtr)(v);
}

static int32_t CmpDecKeyValue(const runtime::Il2CppDecimal& a, const runtime::Il2CppDecimal& b)
{
    return runtime::icall_decimal_compare(&a, &b);
}

Il2CppArray* SortDecI8KeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortDecKeyCopyT<int64_t>(src, keySel, desc, InvokeKeyDecI8,
                                    CmpKeyValue<int64_t>);
}

Il2CppArray* SortDecDKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortDecKeyCopyT<double>(src, keySel, desc, InvokeKeyDecD,
                                   CmpKeyValue<double>);
}

Il2CppArray* SortDecDecKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortDecKeyCopyT<runtime::Il2CppDecimal>(src, keySel, desc, InvokeKeyDecDec,
                                                   CmpDecKeyValue);
}

// impl-orderfam: the FLOAT-key reader for a DECIMAL source (the
// Func<decimal,float> ABI). A float leaf returns a 4-byte float per the MS
// x64 ABI, NOT a double; the old family-"2" fold read it through InvokeKeyDecD
// and mixed the callee's XMM0 residue into every key (the b5-gaps §14
// misread -- the thenby-decfkey probe).
static float InvokeKeyDecF(Il2CppDelegate* keySel, const runtime::Il2CppDecimal& v)
{
    typedef float (*Fn)(runtime::Il2CppDecimal);
    typedef float (*ThisFn)(Il2CppObject*, runtime::Il2CppDecimal);
    return keySel->target != nullptr
        ? reinterpret_cast<ThisFn>(keySel->methodPtr)(keySel->target, v)
        : reinterpret_cast<Fn>(keySel->methodPtr)(v);
}

Il2CppArray* SortDecFKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc = false)
{
    return SortDecKeyCopyT<float>(src, keySel, desc, InvokeKeyDecF,
                                  CmpKeyValue<float>);
}

//------------------------------------------------------------------------------
// SortDoubleKeyCopy -- the DOUBLE-SOURCE counterpart of SortInt32KeyCopy (and
// of the SortInt64KeyCopy template): build a new double array holding the
// stable OrderBy of 'src' under the double KEY SELECTOR 'keySel' (each key
// cached EXACTLY ONCE -- net48 EnumerableSorter). Same GC-safety two shields:
// the element snapshot is taken before the first selector call, and the
// delegate is rooted across the key phase.
//------------------------------------------------------------------------------
Il2CppArray* SortDoubleKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n > 0)
    {
        std::vector<double> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(double));
        std::vector<double> keys(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        for (int32_t i = 0; i < n; ++i)
        {
            keys[i] = InvokeKeyDSrcD(keySel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c = CmpKeyValue<double>(keys[a], keys[b]);
                            return desc ? -c : c;
                        });
        Il2CppArray* result = il2cpp_array_new_typed(sizeof(double), kElemKindD, n);
        double* out = reinterpret_cast<double*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new_typed(sizeof(double), kElemKindD, 0);
}

//------------------------------------------------------------------------------
// SortInt32TwoKeysCopy -- build a new int32 array holding the stable
// (k1, k2) composite order of 'src' under the key selectors 'k1Sel' and
// 'k2Sel' (net48 EnumerableSorter chain semantics for OrderBy(k1).ThenBy(k2)
// / .ThenByDescending(k2), D-LO2-1): each element's PRIMARY key is cached
// EXACTLY ONCE and the SECONDARY key EXACTLY ONCE -- a side-effecting
// selector is never re-invoked (net48 contract) -- then the stable merge
// sort orders native INDICES by the tuple (k1 asc|desc, k2 asc / desc).
//
// GC SAFETY: the same two shields as SortInt32KeyCopy -- the elements are
// snapshotted into the native `elems` vector BEFORE any selector call, and
// BOTH selectors are rooted (RootScope) across the key phase (their targets
// are GC references re-read on every dispatch). The result array is
// allocated only after the last selector call.
//
// impl-thenbyfam: the PRIMARY and SECONDARY key families are INDEPENDENT. The
// callback ABI depends only on the key family (an int32 key returns in the
// int32 slot, an int64 key in the int64 slot, a double key in the F-stack
// slot), so each key has its own cache type, its own reader and its own
// comparator. A mixed pair (e.g. Func<int,double> + Func<int,int>) that used
// one shared reader read the secondary key from the WRONG return slot -- a
// silent mis-order on x64 (eax / xmm0 residue).
//------------------------------------------------------------------------------
template <typename TK1, typename TK2, typename Invoke1, typename Invoke2,
          typename Cmp1, typename Cmp2>
Il2CppArray* SortInt32TwoKeysCopyT(Il2CppArray* src, Il2CppDelegate* k1Sel,
                                   Il2CppDelegate* k2Sel, bool k1Desc, bool k2Desc,
                                   Invoke1 invoke1, Invoke2 invoke2,
                                   Cmp1 cmp1, Cmp2 cmp2)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&k1Sel));
    roots.Add(reinterpret_cast<void**>(&k2Sel));

    if (n > 0)
    {
        // Element snapshot taken before ANY selector call: no managed
        // allocation preceded this memcpy, so 'src' cannot have been moved.
        std::vector<int32_t> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(int32_t));
        std::vector<TK1> k1(static_cast<size_t>(n));
        std::vector<TK2> k2(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        // Cache each element's PRIMARY key once, then the SECONDARY key once
        // (net48 EnumerableSorter chain: no selector is ever re-invoked).
        for (int32_t i = 0; i < n; ++i)
        {
            k1[i] = invoke1(k1Sel, elems[i]);
        }
        for (int32_t i = 0; i < n; ++i)
        {
            k2[i] = invoke2(k2Sel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        // Order INDICES by the (k1, k2) tuple; the merge keeps the LEFT run
        // on full ties (cmp(right, left) < 0 is the only right-run take), so
        // fully-equal tuples keep their original index order -- the stable
        // contract. ThenByDescending flips ONLY the secondary comparison.
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c1 = cmp1(k1[a], k1[b]);
                            if (c1 != 0)
                            {
                                return k1Desc ? -c1 : c1;
                            }
                            const int32_t c2 = cmp2(k2[a], k2[b]);
                            return k2Desc ? -c2 : c2;
                        });
        // Materialize from the native snapshot by index. Last selector call
        // done; the result allocation below cannot be followed by a move.
        Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(int32_t), false, n);
        int32_t* out = reinterpret_cast<int32_t*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new(nullptr, sizeof(int32_t), false, 0);
}

Il2CppArray* SortInt32TwoKeysCopy(Il2CppArray* src, Il2CppDelegate* k1Sel,
                                  Il2CppDelegate* k2Sel, bool k1Desc, bool k2Desc)
{
    return SortInt32TwoKeysCopyT<int32_t, int32_t>(
        src, k1Sel, k2Sel, k1Desc, k2Desc, InvokeKeyI4, InvokeKeyI4,
        CmpKeyValue<int32_t>, CmpKeyValue<int32_t>);
}

//------------------------------------------------------------------------------
// SortInt64KeyCopy -- build a new int64 array holding the stable OrderBy of
// 'src' under the KEY SELECTOR 'keySel' (p0conv2 D3; net48 EnumerableSorter
// semantics: every element's key is computed EXACTLY ONCE and cached).
//
// The int64 SOURCE is a value family with 8-byte cells; the result is a fresh
// int64 array stamped elemKind I8 so downstream formatting/scans read the full
// 64-bit cell. GC SAFETY is the same two-shield contract as SortInt32KeyCopy:
// the elements are snapshotted into a native vector BEFORE any selector call,
// and the delegate is rooted across the key phase. The result array is
// allocated only after the last selector call.
//------------------------------------------------------------------------------
template <typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortInt64KeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                              InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n > 0)
    {
        // Element snapshot taken before ANY selector call (no managed
        // allocation precedes the memcpy, so 'src' cannot have moved).
        std::vector<int64_t> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(int64_t));
        std::vector<TKey> keys(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        // Cache each element's key EXACTLY ONCE (net48 EnumerableSorter).
        for (int32_t i = 0; i < n; ++i)
        {
            keys[i] = invokeKey(keySel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        // Order INDICES by the cached keys; the merge keeps the LEFT run on
        // ties, so equal keys keep their original index order (stability).
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c = cmp(keys[a], keys[b]);
                            return desc ? -c : c;
                        });
        // Materialize from the native snapshot by index. Last selector call
        // done; the result allocation below cannot be followed by a move.
        Il2CppArray* result = il2cpp_array_new_typed(sizeof(int64_t), kElemKindI8, n);
        int64_t* out = reinterpret_cast<int64_t*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new_typed(sizeof(int64_t), kElemKindI8, 0);
}

//------------------------------------------------------------------------------
// p0linq4 (D4): SortInt64TwoKeysCopy -- the int64-SOURCE counterpart of
// SortInt32TwoKeysCopy. Builds a new int64 array holding the stable
// (k1, k2) composite order of 'src' under the key selectors 'k1Sel' and
// 'k2Sel' (net48 EnumerableSorter chain semantics for OrderBy(k1).ThenBy(k2)
// / .ThenByDescending(k2)): each element's PRIMARY key is cached EXACTLY ONCE
// and the SECONDARY key EXACTLY ONCE -- a side-effecting selector is never
// re-invoked -- then the stable merge sort orders native INDICES by the tuple
// (k1 asc|desc, k2 asc / desc).
//
// The elements are 8-byte int64 cells; the result is a fresh int64 array
// stamped elemKind I8 (the same contract as SortInt64KeyCopy). GC SAFETY is
// the same two-shield contract: the elements are snapshotted into a native
// vector BEFORE any selector call, and BOTH selectors are rooted across the
// key phase. The result array is allocated only after the last selector call.
//
// impl-thenbyfam: as with SortInt32TwoKeysCopyT, the two key families are
// INDEPENDENT (own cache type / reader / comparator per key).
//------------------------------------------------------------------------------
template <typename TK1, typename TK2, typename Invoke1, typename Invoke2,
          typename Cmp1, typename Cmp2>
Il2CppArray* SortInt64TwoKeysCopy(Il2CppArray* src, Il2CppDelegate* k1Sel,
                                  Il2CppDelegate* k2Sel, bool k1Desc, bool k2Desc,
                                  Invoke1 invoke1, Invoke2 invoke2,
                                  Cmp1 cmp1, Cmp2 cmp2)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&k1Sel));
    roots.Add(reinterpret_cast<void**>(&k2Sel));

    if (n > 0)
    {
        // Element snapshot taken before ANY selector call (no managed
        // allocation precedes the memcpy, so 'src' cannot have moved).
        std::vector<int64_t> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(int64_t));
        std::vector<TK1> k1(static_cast<size_t>(n));
        std::vector<TK2> k2(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        // Cache each element's PRIMARY key once, then the SECONDARY key once
        // (net48 EnumerableSorter chain: no selector is ever re-invoked).
        for (int32_t i = 0; i < n; ++i)
        {
            k1[i] = invoke1(k1Sel, elems[i]);
        }
        for (int32_t i = 0; i < n; ++i)
        {
            k2[i] = invoke2(k2Sel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        // Order INDICES by the (k1, k2) tuple; the merge keeps the LEFT run
        // on full ties, so fully-equal tuples keep their original index order
        // (the stable contract). ThenByDescending flips ONLY the secondary.
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c1 = cmp1(k1[a], k1[b]);
                            if (c1 != 0)
                            {
                                return k1Desc ? -c1 : c1;
                            }
                            const int32_t c2 = cmp2(k2[a], k2[b]);
                            return k2Desc ? -c2 : c2;
                        });
        // Materialize from the native snapshot by index. Last selector call
        // done; the result allocation below cannot be followed by a move.
        Il2CppArray* result = il2cpp_array_new_typed(sizeof(int64_t), kElemKindI8, n);
        int64_t* out = reinterpret_cast<int64_t*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new_typed(sizeof(int64_t), kElemKindI8, 0);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_linq_order_by_i4 -- OrderBy over int32 arrays (see header).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_i4(Il2CppArray* src, Il2CppObject* cmp)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    CompareI4Fn fn = reinterpret_cast<CompareI4Fn>(RequireLinqComparator(cmp));
    return SortInt32Copy(s, fn);
}

//------------------------------------------------------------------------------
// il2cpp_linq_order_by_ref -- OrderBy over reference arrays (see header).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_ref(Il2CppArray* src, Il2CppObject* cmp)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    CompareRefFn fn = reinterpret_cast<CompareRefFn>(RequireLinqComparator(cmp));
    return SortRefsCopy(s, fn);
}

//------------------------------------------------------------------------------
// il2cpp_linq_order_by_string -- OrderBy over string arrays (see header).
// Same ABI as the ref entry; the caller's comparator decides the string
// order (and the null policy -- null slots ARE passed to the callback).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_string(Il2CppArray* src, Il2CppObject* cmp)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    CompareRefFn fn = reinterpret_cast<CompareRefFn>(RequireLinqComparator(cmp));
    return SortRefsCopy(s, fn);
}

//------------------------------------------------------------------------------
// il2cpp_linq_order_by_key_i4 -- OrderBy(source, keySelector) over int32
// arrays (the D-LINQ-1 ABI bridge, see header): the 1-arg key selector is
// cached once per element and the stable sort runs on the cached keys.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopy(s, d, false);
}

//------------------------------------------------------------------------------
// il2cpp_linq_order_by_key_desc_i4 -- OrderByDescending(source, keySelector)
// over int32 arrays: identical to order_by_key_i4 except DESCENDING order.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_desc_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopy(s, d, true);
}

//------------------------------------------------------------------------------
// p0grp: OrderBy / OrderByDescending over a DECIMAL source (net48
// OrderBy<decimal,TKey> with an int32 TKey). The decimal element crosses BY
// VALUE to the key selector; the result is a fresh decimal[].
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_dec_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecI4KeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecI4KeyCopy(s, d, true);
}

// p0grp: the DECIMAL-source keyed sort with an int64 / double / decimal key.
Il2CppArray* il2cpp_linq_order_by_key_dec_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecI8KeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecI8KeyCopy(s, d, true);
}

Il2CppArray* il2cpp_linq_order_by_key_dec_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecDKeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecDKeyCopy(s, d, true);
}

//------------------------------------------------------------------------------
// impl-orderfam: the DECIMAL-source FLOAT-key counterparts (the
// Func<decimal,float> ABI). Same cached-once / stable / fresh decimal[]
// result contract; the key returns in the float slot and is read through
// InvokeKeyDecF, never through the double reader (b5-gaps §14).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_dec_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecFKeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecFKeyCopy(s, d, true);
}

Il2CppArray* il2cpp_linq_order_by_key_dec_dec(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecDecKeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_dec(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecDecKeyCopy(s, d, true);
}

//------------------------------------------------------------------------------
// il2cpp_linq_then_by_key_i4 -- ThenBy over int32 arrays (see header): the
// fused (k1 asc|desc, k2 asc) composite sort behind
// OrderBy(k1) / OrderByDescending(k1) followed by ThenBy(k2).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                        Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopy(s, d1, d2, k1desc != 0, false);
}

//------------------------------------------------------------------------------
// il2cpp_linq_then_by_key_desc_i4 -- ThenByDescending over int32 arrays (see
// header): the fused (k1, k2 desc) composite sort behind
// OrderBy(k1).ThenByDescending(k2).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_desc_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                             Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopy(s, d1, d2, k1desc != 0, true);
}

//------------------------------------------------------------------------------
// Enumerable.OrderBy / OrderByDescending over an INT32 source with a WIDE key
// family (int64 / double). The int32-source int32-key entries read the key from
// the int32 slot; a long/double key crosses in the int64 / F-stack slot, so it
// needs its own entry. The contract is otherwise identical (cached-once keys,
// stable merge sort, source never written, fresh int32[] result).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i4_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<int64_t>(s, d, false, InvokeKeyI4I8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i4_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<int64_t>(s, d, true, InvokeKeyI4I8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_i4_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<double>(s, d, false, InvokeKeyI4D, CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i4_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<double>(s, d, true, InvokeKeyI4D, CmpKeyValue<double>);
}

//------------------------------------------------------------------------------
// impl-floatkey: the FLOAT-key entries (Enumerable.OrderBy/OrderByDescending
// with a Func<TSource,float> selector over an int32 source). The key returns
// in the 4-byte float slot (the MS x64 ABI) and is compared as float.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i4_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<float>(s, d, false, InvokeKeyI4F, CmpKeyValue<float>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i4_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<float>(s, d, true, InvokeKeyI4F, CmpKeyValue<float>);
}

//------------------------------------------------------------------------------
// Enumerable.ThenBy / ThenByDescending over an INT32 source with a WIDE
// secondary key (int64 / double) -- the fused (k1, k2) composite sort with the
// same single-cache contract as il2cpp_linq_then_by_key_i4.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i4_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopyT<int64_t, int64_t>(
        s, d1, d2, k1desc != 0, false, InvokeKeyI4I8, InvokeKeyI4I8,
        CmpKeyValue<int64_t>, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_then_by_key_desc_i4_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopyT<int64_t, int64_t>(
        s, d1, d2, k1desc != 0, true, InvokeKeyI4I8, InvokeKeyI4I8,
        CmpKeyValue<int64_t>, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_then_by_key_i4_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                          Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopyT<double, double>(
        s, d1, d2, k1desc != 0, false, InvokeKeyI4D, InvokeKeyI4D,
        CmpKeyValue<double>, CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_then_by_key_desc_i4_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                               Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysCopyT<double, double>(
        s, d1, d2, k1desc != 0, true, InvokeKeyI4D, InvokeKeyI4D,
        CmpKeyValue<double>, CmpKeyValue<double>);
}

//------------------------------------------------------------------------------
// OrderBy over a REFERENCE source with a KEY SELECTOR (see linq_sort.h).
//
// SortRefsKeyCopy<TKey> caches each element's key EXACTLY ONCE (net48
// EnumerableSorter -- a side-effecting selector is never re-invoked), orders
// native INDEX slots by the cached keys with a stable merge sort, then
// materializes the result from the rooted source by index. No raw element
// address is held across a selector call: the element is read through the
// rooted array each time, and the result is allocated only after the last
// selector call.
//------------------------------------------------------------------------------
template <typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortRefsKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                             InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n == 0)
    {
        return il2cpp_array_new(src->elemClass, sizeof(void*), true, 0);
    }

    std::vector<TKey> keys(static_cast<size_t>(n));
    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
        keys[i] = invokeKey(keySel, RefSlot(src, i));
    }

    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        const int32_t c = cmp(keys[a], keys[b]);
                        return desc ? -c : c;
                    });

    Il2CppArray* result = il2cpp_array_new(src->elemClass, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&result));
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(result, i));
        *slot = RefSlot(src, order[i]);
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(result->data);
    for (int32_t i = 0; i < n; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&slots[i]), slots[i]);
    }
    return result;
}

// Ordering of two reference KEYS.
//
// A string key is rejected OUT LOUD, never routed through an ordinal shortcut:
// Enumerable.OrderBy compares keys with Comparer<TKey>.Default, and net48's
// Comparer<string>.Default is CULTURE-aware (it sorts "a" before "A" and
// applies OS-NLS collation weights). This runtime has no collation
// infrastructure, so an ordinal comparison would SILENTLY MISORDER mixed-case
// input -- a wrong value, not a documented deviation. The transpiler rejects a
// STATICALLY string TKey (MethodMap.Linq.cs); an `object`/interface key cannot
// be judged at compile time, so the runtime instance check below is the only
// correct place for those.
//
// Every other reference type defers to IComparable.CompareTo on the first key
// (Comparer<T>.Default), null-first.
//
// The NULL checks come FIRST, deliberately: net48 orders a null key before
// every non-null key without consulting the comparer at all, so
// `object[] { null, "a" }.OrderBy(x => x)` must return ",a" -- rejecting that
// would be a LOUD misfire (a shape the runtime CAN answer correctly). The
// collation rejection therefore only fires when two NON-NULL keys are compared
// and at least one is a string.
int32_t CompareRefKeys(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return 0;
    if (a == nullptr) return -1;
    if (b == nullptr) return 1;
    if (a->klass == il2cpp_string_class() || b->klass == il2cpp_string_class())
    {
        il2cpp_throw_not_supported_exception(
            "Enumerable.OrderBy: a System.String key requires culture-aware collation, "
            "which the runtime does not implement (see docs/audit/b5-gaps.md); an ordinal "
            "comparison would silently misorder mixed-case input (net48 "
            "Comparer<string>.Default sorts a before A)");
    }
    return il2cpp_icomparable_compare_to(a, b);
}

Il2CppArray* il2cpp_linq_order_by_key_ref_i4(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc){
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<int32_t>(s, d, desc != 0, InvokeKeyRefI4,
        CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_ref_i8(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<int64_t>(s, d, desc != 0, InvokeKeyRefI8,
        CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_ref_d(Il2CppArray* src, Il2CppObject* keySel,
                                            int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<double>(s, d, desc != 0, InvokeKeyRefD,
        CmpKeyValue<double>);
}

//------------------------------------------------------------------------------
// impl-orderfam: the REFERENCE-source FLOAT-key counterpart (the
// Func<TRef,float> ABI). Same cached-once / stable / fresh reference-array
// result contract; the key returns in the float slot and is read through
// InvokeKeyRefF, never through the double reader (b5-gaps §14).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_ref_f(Il2CppArray* src, Il2CppObject* keySel,
                                            int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<float>(s, d, desc != 0, InvokeKeyRefF,
        CmpKeyValue<float>);
}

Il2CppArray* il2cpp_linq_order_by_key_ref_ref(Il2CppArray* src, Il2CppObject* keySel,
                                              int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<Il2CppObject*>(s, d, desc != 0, InvokeKeyRefRef,
        CompareRefKeys);
}

//------------------------------------------------------------------------------
// p0conv2 (D3): OrderBy / OrderByDescending over an INT64 value SOURCE with a
// KEY SELECTOR (see linq_sort.h). The source must be a one-dimensional
// value-element array with 8-byte cells (long[] / ulong[]); the key family is
// fixed by the entry (int32 or int64 keys, cached once per element).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i8_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<int32_t>(s, d, false, InvokeKeyI8SrcI4,
        CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<int32_t>(s, d, true, InvokeKeyI8SrcI4,
        CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_i8_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<int64_t>(s, d, false, InvokeKeyI8SrcI8,
        CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<int64_t>(s, d, true, InvokeKeyI8SrcI8,
        CmpKeyValue<int64_t>);
}

//------------------------------------------------------------------------------
// The int64-source float/double-key counterparts: the key crosses in the
// F-stack slot. Same cached-once / stable / fresh int64[] result contract.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i8_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<double>(s, d, false, InvokeKeyI8SrcD,
        CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<double>(s, d, true, InvokeKeyI8SrcD,
        CmpKeyValue<double>);
}

//------------------------------------------------------------------------------
// impl-floatkey: the int64-source FLOAT-key counterparts (the Func<long,float>
// ABI). Same cached-once / stable / fresh int64[] result contract.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_i8_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<float>(s, d, false, InvokeKeyI8SrcF,
        CmpKeyValue<float>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<float>(s, d, true, InvokeKeyI8SrcF,
        CmpKeyValue<float>);
}

//------------------------------------------------------------------------------
// The DOUBLE-SOURCE key-selector entries: the elements are 8-byte double
// cells and the key crosses in the F-stack slot. Same cached-once / stable /
// fresh double[] result contract.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_order_by_key_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDoubleKeyCopy(s, d, false);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDoubleKeyCopy(s, d, true);
}

//------------------------------------------------------------------------------
// impl-orderfam: the WIDE ELEMENT-SOURCE keyed sorts (the double[]/float[]
// OrderBy(keySelector) surface). One template per element family serves every
// key family the element ABI carries: the element crosses to the selector in
// the F-stack slot (a float element WIDENS to double -- the InvokePredF /
// FPredFn convention, linq.cpp) and the key returns in its own family slot
// (a float key through the float(*) reader -- impl-floatkey, b5-gaps §14).
// Same cached-once / stable / fresh-source-family result contract as
// SortInt32KeyCopyT / SortInt64KeyCopyT: the elements are snapshotted before
// any selector call, the delegate is rooted across the key phase, and the
// result array carries the SOURCE's element kind (a float[] source yields a
// fresh float[], a double[] source a fresh double[]).
//------------------------------------------------------------------------------
template <typename TElem, int32_t ElemKind, typename TKey, typename InvokeKey,
          typename Cmp>
Il2CppArray* SortWideElemKeyCopyT(Il2CppArray* src, Il2CppDelegate* keySel,
                                  bool desc, InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n > 0)
    {
        // Element snapshot taken before ANY selector call: no managed
        // allocation preceded this memcpy, so 'src' cannot have been moved.
        std::vector<TElem> elems(static_cast<size_t>(n));
        std::memcpy(elems.data(), src->data,
                    static_cast<size_t>(n) * sizeof(TElem));
        std::vector<TKey> keys(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i)
        {
            order[i] = i;
        }
        // Cache each element's key EXACTLY ONCE (net48 EnumerableSorter):
        // a side-effecting selector is never re-invoked. A float element
        // widens into the F-stack slot implicitly.
        for (int32_t i = 0; i < n; ++i)
        {
            keys[i] = invokeKey(keySel, elems[i]);
        }
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c = cmp(keys[a], keys[b]);
                            return desc ? -c : c;
                        });
        Il2CppArray* result =
            il2cpp_array_new_typed(static_cast<int32_t>(sizeof(TElem)),
                                   ElemKind, n);
        TElem* out = reinterpret_cast<TElem*>(result->data);
        for (int32_t i = 0; i < n; ++i)
        {
            out[i] = elems[order[i]];
        }
        return result;
    }
    return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(TElem)),
                                  ElemKind, 0);
}

// The DOUBLE-source entries: the elements are 8-byte double cells and the
// result is a fresh double[]. The pre-existing plain _d entry (above) is the
// double-key face; the _d_<keyfam> composite names serve the other key
// families (the int64-source naming convention, p0conv2 D3).
Il2CppArray* il2cpp_linq_order_by_key_d_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, int32_t>(
        s, d, false, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_d_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, int32_t>(
        s, d, true, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_d_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, int64_t>(
        s, d, false, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_d_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, int64_t>(
        s, d, true, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_d_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, float>(
        s, d, false, InvokeKeyDSrcF, CmpKeyValue<float>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_d_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<double, kElemKindD, float>(
        s, d, true, InvokeKeyDSrcF, CmpKeyValue<float>);
}

// The FLOAT-source entries: the elements are 4-byte float cells (widened
// into the F-stack slot per selector call) and the result is a fresh
// float[] stamped with elemKind F.
Il2CppArray* il2cpp_linq_order_by_key_f_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, int32_t>(
        s, d, false, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_f_i4(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, int32_t>(
        s, d, true, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_f_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, int64_t>(
        s, d, false, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_f_i8(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, int64_t>(
        s, d, true, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_order_by_key_f_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, double>(
        s, d, false, InvokeKeyDSrcD, CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_f_d(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, double>(
        s, d, true, InvokeKeyDSrcD, CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_order_by_key_f_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, float>(
        s, d, false, InvokeKeyDSrcF, CmpKeyValue<float>);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_f_f(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortWideElemKeyCopyT<float, kElemKindF, float>(
        s, d, true, InvokeKeyDSrcF, CmpKeyValue<float>);
}

//------------------------------------------------------------------------------
// p0linq4 (D4): ThenBy / ThenByDescending over an INT64 value SOURCE (see
// linq_sort.h). The fused (k1, k2) composite sort; the key family is fixed by
// the entry (int32 or int64 keys, each cached once per element).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i8_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<int32_t, int32_t>(
        s, d1, d2, k1desc != 0, false, InvokeKeyI8SrcI4, InvokeKeyI8SrcI4,
        CmpKeyValue<int32_t>, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_then_by_key_desc_i8_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<int32_t, int32_t>(
        s, d1, d2, k1desc != 0, true, InvokeKeyI8SrcI4, InvokeKeyI8SrcI4,
        CmpKeyValue<int32_t>, CmpKeyValue<int32_t>);
}

Il2CppArray* il2cpp_linq_then_by_key_i8_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                           Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<int64_t, int64_t>(
        s, d1, d2, k1desc != 0, false, InvokeKeyI8SrcI8, InvokeKeyI8SrcI8,
        CmpKeyValue<int64_t>, CmpKeyValue<int64_t>);
}

Il2CppArray* il2cpp_linq_then_by_key_desc_i8_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<int64_t, int64_t>(
        s, d1, d2, k1desc != 0, true, InvokeKeyI8SrcI8, InvokeKeyI8SrcI8,
        CmpKeyValue<int64_t>, CmpKeyValue<int64_t>);
}

//------------------------------------------------------------------------------
// The int64-source float/double-key ThenBy counterparts: the secondary key
// crosses in the F-stack slot. Same fused composite contract.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_i8_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                          Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<double, double>(
        s, d1, d2, k1desc != 0, false, InvokeKeyI8SrcD, InvokeKeyI8SrcD,
        CmpKeyValue<double>, CmpKeyValue<double>);
}

Il2CppArray* il2cpp_linq_then_by_key_desc_i8_d(Il2CppArray* src, Il2CppObject* k1Sel,
                                               Il2CppObject* k2Sel, int32_t k1desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysCopy<double, double>(
        s, d1, d2, k1desc != 0, true, InvokeKeyI8SrcD, InvokeKeyI8SrcD,
        CmpKeyValue<double>, CmpKeyValue<double>);
}

//------------------------------------------------------------------------------
// impl-thenbyfam: the MIXED key-family fused composite. The primary and the
// secondary key families are INDEPENDENT, so ONE dispatch entry per element
// family serves every (k1fam, k2fam) pair the transpiler can produce; the two
// families ride trailing literals (0=int32, 1=int64, 2=double, 3=float -- the
// LinqValKeyFamilyCode contract), exactly like the decimal carrier above. An
// out-of-range family code cannot come from the transpiler (it is gated
// compile-time) -- it is a LOUD failure, never a guessed reader.
//------------------------------------------------------------------------------
namespace
{
Il2CppArray* SortInt32TwoKeysFamDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                         Il2CppDelegate* d2, bool k1Desc,
                                         bool k2Desc, int32_t k1fam, int32_t k2fam)
{
// impl-floatkey: family 3 = float (the Func<TSource,float> ABI) -- a float
// key MUST be read through a float(*) reader, never through the double
// KeyI4D reader (b5-gaps §14). 4 x 4 = 16 (k1fam, k2fam) combinations.
#define I4SRC_K1_CASES(K1TYPE, K1INV, K1CMP)                                                \
    case 0: return SortInt32TwoKeysCopyT<K1TYPE, int32_t>(s, d1, d2, k1Desc, k2Desc, K1INV, \
        InvokeKeyI4, K1CMP, CmpKeyValue<int32_t>);                                          \
    case 1: return SortInt32TwoKeysCopyT<K1TYPE, int64_t>(s, d1, d2, k1Desc, k2Desc, K1INV, \
        InvokeKeyI4I8, K1CMP, CmpKeyValue<int64_t>);                                        \
    case 2: return SortInt32TwoKeysCopyT<K1TYPE, double>(s, d1, d2, k1Desc, k2Desc, K1INV,  \
        InvokeKeyI4D, K1CMP, CmpKeyValue<double>);                                          \
    case 3: return SortInt32TwoKeysCopyT<K1TYPE, float>(s, d1, d2, k1Desc, k2Desc, K1INV,   \
        InvokeKeyI4F, K1CMP, CmpKeyValue<float>);

    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            I4SRC_K1_CASES(int32_t, InvokeKeyI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            I4SRC_K1_CASES(int64_t, InvokeKeyI4I8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            I4SRC_K1_CASES(double, InvokeKeyI4D, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            I4SRC_K1_CASES(float, InvokeKeyI4F, CmpKeyValue<float>)
        default: break;
        }
        break;
    default: break;
    }
#undef I4SRC_K1_CASES
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown int32-source key family");
    return nullptr;
}

Il2CppArray* SortInt64TwoKeysFamDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                         Il2CppDelegate* d2, bool k1Desc,
                                         bool k2Desc, int32_t k1fam, int32_t k2fam)
{
// impl-floatkey: family 3 = float (read through InvokeKeyI8SrcF, b5-gaps
// §14). 4 x 4 = 16 (k1fam, k2fam) combinations.
#define I8SRC_K1_CASES(K1TYPE, K1INV, K1CMP)                                                \
    case 0: return SortInt64TwoKeysCopy<K1TYPE, int32_t>(s, d1, d2, k1Desc, k2Desc, K1INV,  \
        InvokeKeyI8SrcI4, K1CMP, CmpKeyValue<int32_t>);                                     \
    case 1: return SortInt64TwoKeysCopy<K1TYPE, int64_t>(s, d1, d2, k1Desc, k2Desc, K1INV,  \
        InvokeKeyI8SrcI8, K1CMP, CmpKeyValue<int64_t>);                                     \
    case 2: return SortInt64TwoKeysCopy<K1TYPE, double>(s, d1, d2, k1Desc, k2Desc, K1INV,   \
        InvokeKeyI8SrcD, K1CMP, CmpKeyValue<double>);                                       \
    case 3: return SortInt64TwoKeysCopy<K1TYPE, float>(s, d1, d2, k1Desc, k2Desc, K1INV,    \
        InvokeKeyI8SrcF, K1CMP, CmpKeyValue<float>);

    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            I8SRC_K1_CASES(int32_t, InvokeKeyI8SrcI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            I8SRC_K1_CASES(int64_t, InvokeKeyI8SrcI8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            I8SRC_K1_CASES(double, InvokeKeyI8SrcD, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            I8SRC_K1_CASES(float, InvokeKeyI8SrcF, CmpKeyValue<float>)
        default: break;
        }
        break;
    default: break;
    }
#undef I8SRC_K1_CASES
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown int64-source key family");
    return nullptr;
}
} // namespace

Il2CppArray* il2cpp_linq_then_by_key_val_i4(Il2CppArray* src, Il2CppObject* k1Sel,
                                            Il2CppObject* k2Sel, int32_t k1desc,
                                            int32_t k2desc, int32_t k1fam,
                                            int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt32TwoKeysFamDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                       k1fam, k2fam);
}

Il2CppArray* il2cpp_linq_then_by_key_val_i8(Il2CppArray* src, Il2CppObject* k1Sel,
                                            Il2CppObject* k2Sel, int32_t k1desc,
                                            int32_t k2desc, int32_t k1fam,
                                            int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortInt64TwoKeysFamDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                       k1fam, k2fam);
}

//------------------------------------------------------------------------------
// impl-linq3 (B): OrderBy / OrderByDescending with a DateTime / Guid KEY
// (net48 OrderBy<TSource,System.DateTime/Guid>). The key selector returns the
// struct BY VALUE; the keys are cached EXACTLY ONCE (net48 EnumerableSorter),
// the stable merge sort orders indices by the CARRIER comparison (DateTime
// ticks / the net48 Guid field-wise contract) and the result materializes
// from the element snapshot by index. Same GC two-shield contract as the
// numeric key sorts. The 16-byte Guid carrier and the 16-byte
// Il2CppDateTime{ticks,kind} struct are plain value caches in std::vector --
// no managed allocation is involved in the key phase beyond the selector's
// own (the delegate is rooted).
//------------------------------------------------------------------------------
// The INT32 source: a fresh int32[] result.
Il2CppArray* il2cpp_linq_order_by_key_i4_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<runtime::Il2CppDateTime>(s, d, false, InvokeKeyI4Dt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i4_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<runtime::Il2CppDateTime>(s, d, true, InvokeKeyI4Dt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_i4_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<Il2CppGuid>(s, d, false, InvokeKeyI4Guid,
        CmpGuidKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i4_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt32KeyCopyT<Il2CppGuid>(s, d, true, InvokeKeyI4Guid,
        CmpGuidKey);
}

// The INT64 source: a fresh int64[] result stamped elemKind I8.
Il2CppArray* il2cpp_linq_order_by_key_i8_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<runtime::Il2CppDateTime>(s, d, false, InvokeKeyI8SrcDt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<runtime::Il2CppDateTime>(s, d, true, InvokeKeyI8SrcDt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_i8_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<Il2CppGuid>(s, d, false, InvokeKeyI8SrcGuid,
        CmpGuidKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_i8_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortInt64KeyCopy<Il2CppGuid>(s, d, true, InvokeKeyI8SrcGuid,
        CmpGuidKey);
}

// The DECIMAL source: the decimal cells snapshot and a fresh decimal[] result.
Il2CppArray* il2cpp_linq_order_by_key_dec_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecKeyCopyT<runtime::Il2CppDateTime>(s, d, false, InvokeKeyDecDt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_dt(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecKeyCopyT<runtime::Il2CppDateTime>(s, d, true, InvokeKeyDecDt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_dec_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecKeyCopyT<Il2CppGuid>(s, d, false, InvokeKeyDecGuid,
        CmpGuidKey);
}

Il2CppArray* il2cpp_linq_order_by_key_desc_dec_guid(Il2CppArray* src, Il2CppObject* keySel)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortDecKeyCopyT<Il2CppGuid>(s, d, true, InvokeKeyDecGuid,
        CmpGuidKey);
}

//------------------------------------------------------------------------------
// impl-linqleft: the DECIMAL-element fused composite sort behind
// OrderBy(k1).ThenBy(k2) / .ThenByDescending(k2) over a decimal[] source or a
// decimal GroupBy bucket. The primary and secondary key families are
// INDEPENDENT, so one dispatch entry serves every combination the OrderBy
// surface supports. Family codes: 0=int32, 1=int64, 2=double, 3=decimal,
// 4=System.DateTime, 5=System.Guid, 6=float (impl-orderfam: the float family
// has its own float(*) reader -- a 4-byte float return read through a double
// fn-pointer mixes the callee's XMM0 residue into every key, the b5-gaps §14
// misread; the numeric code column mirrors the impl-floatkey LinqValKeyFamily
// split while the pre-existing 0..5 codes keep their meaning). Each selector
// is cached EXACTLY ONCE.
//------------------------------------------------------------------------------
namespace
{
Il2CppArray* SortDecTwoKeysDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                    Il2CppDelegate* d2, bool k1Desc, bool k2Desc,
                                    int32_t k1fam, int32_t k2fam)
{
#define DEC_K2_CASES(K1TYPE, K1INV, K1CMP)                                                   \
    case 0: return SortDecTwoKeysCopyT<K1TYPE, int32_t>(s, d1, d2, k1Desc, k2Desc, K1INV,   \
        InvokeKeyDecI4, K1CMP, CmpKeyValue<int32_t>);                                        \
    case 1: return SortDecTwoKeysCopyT<K1TYPE, int64_t>(s, d1, d2, k1Desc, k2Desc, K1INV,   \
        InvokeKeyDecI8, K1CMP, CmpKeyValue<int64_t>);                                        \
    case 2: return SortDecTwoKeysCopyT<K1TYPE, double>(s, d1, d2, k1Desc, k2Desc, K1INV,    \
        InvokeKeyDecD, K1CMP, CmpKeyValue<double>);                                          \
    case 3: return SortDecTwoKeysCopyT<K1TYPE, runtime::Il2CppDecimal>(s, d1, d2, k1Desc,    \
        k2Desc, K1INV, InvokeKeyDecDec, K1CMP, CmpDecKeyValue);                              \
    case 4: return SortDecTwoKeysCopyT<K1TYPE, runtime::Il2CppDateTime>(s, d1, d2, k1Desc,   \
        k2Desc, K1INV, InvokeKeyDecDt, K1CMP, CmpDateTimeKey);                               \
    case 5: return SortDecTwoKeysCopyT<K1TYPE, Il2CppGuid>(s, d1, d2, k1Desc, k2Desc,        \
        K1INV, InvokeKeyDecGuid, K1CMP, CmpGuidKey);                                         \
    case 6: return SortDecTwoKeysCopyT<K1TYPE, float>(s, d1, d2, k1Desc, k2Desc, K1INV,      \
        InvokeKeyDecF, K1CMP, CmpKeyValue<float>);

    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            DEC_K2_CASES(int32_t, InvokeKeyDecI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            DEC_K2_CASES(int64_t, InvokeKeyDecI8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            DEC_K2_CASES(double, InvokeKeyDecD, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            DEC_K2_CASES(runtime::Il2CppDecimal, InvokeKeyDecDec, CmpDecKeyValue)
        default: break;
        }
        break;
    case 4:
        switch (k2fam)
        {
            DEC_K2_CASES(runtime::Il2CppDateTime, InvokeKeyDecDt, CmpDateTimeKey)
        default: break;
        }
        break;
    case 5:
        switch (k2fam)
        {
            DEC_K2_CASES(Il2CppGuid, InvokeKeyDecGuid, CmpGuidKey)
        default: break;
        }
        break;
    case 6:
        switch (k2fam)
        {
            DEC_K2_CASES(float, InvokeKeyDecF, CmpKeyValue<float>)
        default: break;
        }
        break;
    default: break;
    }
#undef DEC_K2_CASES
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown decimal key family");
    return nullptr;
}
} // namespace

Il2CppArray* il2cpp_linq_then_by_key_dec(Il2CppArray* src, Il2CppObject* k1Sel,
                                         Il2CppObject* k2Sel, int32_t k1desc,
                                         int32_t k2desc, int32_t k1fam, int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(runtime::Il2CppDecimal));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortDecTwoKeysDispatch(s, d1, d2, k1desc != 0, k2desc != 0, k1fam, k2fam);
}

// The REFERENCE source: the elements are read FRESH through the rooted source
// (no raw address crosses a selector call) and the result is a new reference
// array; the desc flag arrives as an APPENDED literal.
Il2CppArray* il2cpp_linq_order_by_key_ref_dt(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<runtime::Il2CppDateTime>(s, d, desc != 0, InvokeKeyRefDt,
        CmpDateTimeKey);
}

Il2CppArray* il2cpp_linq_order_by_key_ref_guid(Il2CppArray* src, Il2CppObject* keySel,
                                               int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<Il2CppGuid>(s, d, desc != 0, InvokeKeyRefGuid,
        CmpGuidKey);
}

} // namespace il2cpp
