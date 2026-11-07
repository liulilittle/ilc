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

#include "linq_family.h"     // b5-gaps section 72: LinqNewArray / LinqCellReadI4

#include "il2cpp_exception.h"
#include "arraysort.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_string.h"
#include "string_compare.h"
#include "decimal.h"     // impl-linq3 (B): the decimal-source keyed sort cell
#include "guid.h"        // impl-linq3 (B): the System.Guid key carrier + compare
#include "il2cpp_time.h" // impl-linq3 (B): the System.DateTime key carrier
#include "il2cpp_dtoffset.h" // impl-vtsel: the System.DateTimeOffset key carrier
#include "il2cpp_string_ext2.h" // impl-collation: il2cpp_string_compare_culture (string keys)

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <cstdio>
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

namespace {

// One cached key cell. Family 0/1 (int32/int64) land in 'i'; family 2/3
// (double/float -- a float widens EXACTLY) land in 'd'. The family selects the
// READER, not just the comparison: a float key must never be read through the
// double reader (b5-gaps §14).
struct LkChainKey
{
    int64_t i;
    double d;
    // Reference key FAMILIES 4/5 only. The cell stays a raw pointer and is
    // NEVER the storage of record: the reference chain kernel keeps every
    // reference key in a MANAGED side array (so a compacting GC rewrites it)
    // and refreshes `o` from there before each comparison.
    Il2CppObject* o;
};

// The reference-key comparison used by the chained composite. It reproduces
// exactly what the two-key reference composite does through CompareStringKeys /
// CompareRefKeys (defined later in this file, where the reference sort surface
// lives): null-first, then culture collation for System.String and
// IComparable.CompareTo for every other reference. Inlined here so the chain
// kernel does not need a forward declaration that would create an overload set.
int32_t CmpLkRefKey(Il2CppObject* a, Il2CppObject* b, int32_t fam)
{
    if (a == b) return 0;
    if (a == nullptr) return -1;
    if (b == nullptr) return 1;
    if (fam == 4 && a->klass == il2cpp_string_class() && b->klass == il2cpp_string_class())
    {
        return runtime::il2cpp_string_compare_culture(a, b, 0);
    }
    return il2cpp_icomparable_compare_to(a, b);
}

int32_t CmpLkChainKey(const LkChainKey& a, const LkChainKey& b, int32_t fam)
{
    if (fam >= 4)
    {
        // A reference key: null-first, then the family own comparer
        // (family 4 = System.String -> culture collation, family 5 = any other
        // reference -> IComparable.CompareTo). CompareStringKeys / CompareRefKeys
        // are the SAME comparators the two-key reference composite uses.
        return CmpLkRefKey(a.o, b.o, fam);
    }
    // An int32 key and an int64 key compare identically once widened to
    // int64_t (the reader already sign-extends the int32 value).
    return fam <= 1 ? CmpKeyValue<int64_t>(a.i, b.i) : CmpKeyValue<double>(a.d, b.d);
}

void ReadLkChainKeyI4(int32_t elem, int32_t fam, Il2CppDelegate* d, LkChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyI4(d, elem)); break;
    case 1: out.i = InvokeKeyI4I8(d, elem); break;
    case 2: out.d = InvokeKeyI4D(d, elem); break;
    default: out.d = static_cast<double>(InvokeKeyI4F(d, elem)); break;
    }
}

void ReadLkChainKeyI8(int64_t elem, int32_t fam, Il2CppDelegate* d, LkChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyI8SrcI4(d, elem)); break;
    case 1: out.i = InvokeKeyI8SrcI8(d, elem); break;
    case 2: out.d = InvokeKeyI8SrcD(d, elem); break;
    default: out.d = static_cast<double>(InvokeKeyI8SrcF(d, elem)); break;
    }
}

// Item[] source: the numeric key families PLUS the REFERENCE key families
// (4 = System.String -- culture collation, 5 = any other reference) that only a
// reference-element source can produce. The reference key cell lives in
// LkChainKey::o and is held in a MANAGED side array (see the kernel below), so
// a compacting GC rewrites it instead of leaving a dangling raw pointer.
void ReadLkChainKeyF(float elem, int32_t fam, Il2CppDelegate* d, LkChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyDSrcI4(d, elem)); break;
    case 1: out.i = InvokeKeyDSrcI8(d, elem); break;
    case 2: out.d = InvokeKeyDSrcD(d, elem); break;
    default: out.d = static_cast<double>(InvokeKeyDSrcF(d, elem)); break;
    }
}

void ReadLkChainKeyD(double elem, int32_t fam, Il2CppDelegate* d, LkChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyDSrcI4(d, elem)); break;
    case 1: out.i = InvokeKeyDSrcI8(d, elem); break;
    case 2: out.d = InvokeKeyDSrcD(d, elem); break;
    default: out.d = static_cast<double>(InvokeKeyDSrcF(d, elem)); break;
    }
}
void ReadLkChainKeyRef(Il2CppObject* elem, int32_t fam, Il2CppDelegate* d, LkChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyRefI4(d, elem)); break;
    case 1: out.i = InvokeKeyRefI8(d, elem); break;
    case 2: out.d = InvokeKeyRefD(d, elem); break;
    case 3: out.d = static_cast<double>(InvokeKeyRefF(d, elem)); break;
    default: out.o = InvokeKeyRefRef(d, elem); break;
    }
}

// The shared chain kernel: snapshot the elements, cache every key level LEVEL
// BY LEVEL (the measured net48 order), stable-sort native INDICES by the key
// tuple, then materialize a NEW array of the source family. GC: the element
// snapshot precedes ANY selector call, every selector is rooted for the whole
// key phase, and the result is allocated after the last selector call.
template <typename TElem, typename ReadKey>
Il2CppArray* SortLkChainCopyT(Il2CppArray* src, int32_t nkeys, Il2CppDelegate** sels,
                              const int32_t* descs, const int32_t* fams, ReadKey readKey,
                              int32_t elemSize, int32_t elemKind)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        roots.Add(reinterpret_cast<void**>(&sels[l]));
    }

    if (n <= 0)
    {
        return il2cpp_array_new_typed(elemSize, elemKind, 0);
    }

    std::vector<TElem> elems(static_cast<size_t>(n));
    std::memcpy(elems.data(), src->data, static_cast<size_t>(n) * sizeof(TElem));

    std::vector<LkChainKey> keys(static_cast<size_t>(n) * static_cast<size_t>(nkeys));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        LkChainKey* level = keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
        for (int32_t i = 0; i < n; ++i)
        {
            readKey(elems[i], fams[l], sels[l], level[i]);
        }
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
    }
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        for (int32_t l = 0; l < nkeys; ++l)
                        {
                            const LkChainKey* level =
                                keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
                            const int32_t c = CmpLkChainKey(level[a], level[b], fams[l]);
                            if (c != 0)
                            {
                                return descs[l] != 0 ? -c : c;
                            }
                        }
                        return 0;
                    });

    Il2CppArray* result = il2cpp_array_new_typed(elemSize, elemKind, n);
    TElem* out = reinterpret_cast<TElem*>(result->data);
    for (int32_t i = 0; i < n; ++i)
    {
        out[i] = elems[order[i]];
    }
    return result;
}

// Validate the slot table and return the number of ACTIVE levels (a family of
// -1 terminates the chain). An out-of-range family is a LOUD rejection.
int32_t LkChainLevelCount(const int32_t* fams, int32_t maxLevels)
{
    int32_t used = 0;
    for (int32_t l = 0; l < maxLevels; ++l)
    {
        if (fams[l] < 0)
        {
            break;
        }
        if (fams[l] > 5)
        {
            il2cpp_throw_not_supported_exception(
                "Chained ThenBy: only the numeric (int32/int64/double/float) and "
                "reference (string/other) key families are wired.");
        }
        ++used;
    }
    return used;
}

// b5-gaps section 50: the DECIMAL-source chained ThenBy. The decimal chain is
// NOT the LkChainKey path above: a decimal element crosses BY VALUE to every
// selector (the InvokeKeyDec* ABI), and the decimal key families include a
// 16-byte System.Decimal (3), a DateTime (4) and a Guid (5), which cannot ride
// the int64/double LkChainKey cell. So the decimal chain keeps its own key cell
// per level and its own comparator dispatch, then reuses the level-by-level
// exactly-once / stable-index-merge machinery of SortLkChainCopyT.
struct LkDecChainKey
{
    int64_t i;                     // family 0 (int32) / 1 (int64)
    double d;                      // family 2 (double)
    runtime::Il2CppDecimal dec;    // family 3 (decimal)
    runtime::Il2CppDateTime dt;    // family 4 (DateTime)
    Il2CppGuid guid;               // family 5 (Guid)
    float f;                       // family 6 (float -- the decimal dispatch's
                                   // float column, b5-gaps 搂14)
};

void ReadLkDecChainKey(const runtime::Il2CppDecimal& elem, int32_t fam,
                       Il2CppDelegate* d, LkDecChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyDecI4(d, elem)); break;
    case 1: out.i = InvokeKeyDecI8(d, elem); break;
    case 2: out.d = InvokeKeyDecD(d, elem); break;
    case 3: out.dec = InvokeKeyDecDec(d, elem); break;
    case 4: out.dt = InvokeKeyDecDt(d, elem); break;
    case 5: out.guid = InvokeKeyDecGuid(d, elem); break;
    case 6: out.f = InvokeKeyDecF(d, elem); break;
    default:
        il2cpp_throw_not_supported_exception(
            "Chained ThenBy: unknown decimal-source key family.");
        break;
    }
}

int32_t CmpLkDecChainKey(const LkDecChainKey& a, const LkDecChainKey& b, int32_t fam)
{
    switch (fam)
    {
    case 0: return static_cast<int32_t>(a.i) < static_cast<int32_t>(b.i) ? -1
        : static_cast<int32_t>(a.i) > static_cast<int32_t>(b.i) ? 1 : 0;
    case 1: return CmpKeyValue<int64_t>(a.i, b.i);
    case 2: return CmpKeyValue<double>(a.d, b.d);
    case 3: return CmpDecKeyValue(a.dec, b.dec);
    case 4: return CmpDateTimeKey(a.dt, b.dt);
    case 5: return CmpGuidKey(a.guid, b.guid);
    case 6: return CmpKeyValue<float>(a.f, b.f);
    default: return 0;
    }
}

// The decimal chain level-count validator: the decimal dispatch families are
// 0..6 (int32/int64/double/decimal/DateTime/Guid/float), unlike the numeric
// chain's 0..5.
int32_t LkDecChainLevelCount(const int32_t* fams, int32_t maxLevels)
{
    int32_t used = 0;
    for (int32_t l = 0; l < maxLevels; ++l)
    {
        if (fams[l] < 0)
        {
            break;
        }
        if (fams[l] > 6)
        {
            il2cpp_throw_not_supported_exception(
                "Chained ThenBy: only the decimal-source key families (int32/int64/"
                "double/decimal/DateTime/Guid/float) are wired.");
        }
        ++used;
    }
    return used;
}

template <typename ReadKey, typename CmpKey>
Il2CppArray* SortDecChainCopyT(Il2CppArray* src, int32_t nkeys, Il2CppDelegate** sels,
                               const int32_t* descs, const int32_t* fams,
                               ReadKey readKey, CmpKey cmpKey)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        roots.Add(reinterpret_cast<void**>(&sels[l]));
    }

    if (n <= 0)
    {
        return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, 0);
    }

    std::vector<runtime::Il2CppDecimal> elems(static_cast<size_t>(n));
    std::memcpy(elems.data(), src->data,
                static_cast<size_t>(n) * sizeof(runtime::Il2CppDecimal));

    std::vector<LkDecChainKey> keys(static_cast<size_t>(n) * static_cast<size_t>(nkeys));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        LkDecChainKey* level = keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
        for (int32_t i = 0; i < n; ++i)
        {
            readKey(elems[i], fams[l], sels[l], level[i]);
        }
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
    }
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        for (int32_t l = 0; l < nkeys; ++l)
                        {
                            const LkDecChainKey* level =
                                keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
                            const int32_t c = cmpKey(level[a], level[b], fams[l]);
                            if (c != 0)
                            {
                                return descs[l] != 0 ? -c : c;
                            }
                        }
                        return 0;
                    });

    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)), false, n);
    runtime::Il2CppDecimal* out = reinterpret_cast<runtime::Il2CppDecimal*>(result->data);
    for (int32_t i = 0; i < n; ++i)
    {
        out[i] = elems[order[i]];
    }
    return result;
}

} // namespace

} // namespace

// The chained-composite entries (see the header). One per SOURCE family; the
// key families ride the trailing literal table exactly like the two-key
// composites. An unused level is family -1 with a nullptr selector.
Il2CppArray* il2cpp_linq_then_by_chain_i4(Il2CppArray* src,
                                          Il2CppObject* k1, Il2CppObject* k2,
                                          Il2CppObject* k3, Il2CppObject* k4,
                                          int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                          int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortLkChainCopyT<int32_t>(s, nkeys, sels, descs, fams, ReadLkChainKeyI4,
                                     static_cast<int32_t>(sizeof(int32_t)), kElemKindI4);
}

Il2CppArray* il2cpp_linq_then_by_chain_i8(Il2CppArray* src,
                                          Il2CppObject* k1, Il2CppObject* k2,
                                          Il2CppObject* k3, Il2CppObject* k4,
                                          int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                          int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortLkChainCopyT<int64_t>(s, nkeys, sels, descs, fams, ReadLkChainKeyI8,
                                     static_cast<int32_t>(sizeof(int64_t)), kElemKindI8);
}

// The WIDE-ELEMENT chained entries: a float[] / double[] source rides the
// float / double element slot (a float element widens to double per the
// selector ABI; the key families 0..3 select their own reader -- b5-gaps
// 14 never reads a float key through the double reader). Same cached-once,
// stable, source-family result contract and the same four-level table.
Il2CppArray* il2cpp_linq_then_by_chain_f(Il2CppArray* src,
                                         Il2CppObject* k1, Il2CppObject* k2,
                                         Il2CppObject* k3, Il2CppObject* k4,
                                         int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                         int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortLkChainCopyT<float>(s, nkeys, sels, descs, fams, ReadLkChainKeyF,
                                   static_cast<int32_t>(sizeof(float)), kElemKindF);
}

Il2CppArray* il2cpp_linq_then_by_chain_d(Il2CppArray* src,
                                         Il2CppObject* k1, Il2CppObject* k2,
                                         Il2CppObject* k3, Il2CppObject* k4,
                                         int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                         int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortLkChainCopyT<double>(s, nkeys, sels, descs, fams, ReadLkChainKeyD,
                                    static_cast<int32_t>(sizeof(double)), kElemKindD);
}

// The REFERENCE-element chained entry. Same contract as the numeric chain but
// the elements are Il2CppObject* slots (read FRESH through the rooted source,
// never through a raw cached address) and the result is a fresh reference
// array of the SOURCE's element class. Reference KEY families (4 string /
// 5 other) are legal here because ONLY a reference source can produce them.
//
// GC: the numeric chain can keep its keys in a native vector because int64 /
// double never move. A reference KEY does move, so every reference key cell is
// stored in a MANAGED side array (one per level, rooted for the whole sort) and
// the comparator re-reads it from that array -- a raw pointer cached in the
// native vector would dangle after the first compaction.
Il2CppArray* il2cpp_linq_then_by_chain_ref(Il2CppArray* src,
                                           Il2CppObject* k1, Il2CppObject* k2,
                                           Il2CppObject* k3, Il2CppObject* k4,
                                           int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                           int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    const int32_t n = s->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&s));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        roots.Add(reinterpret_cast<void**>(&sels[l]));
    }

    if (n <= 0)
    {
        return il2cpp_array_new(s->elemClass, static_cast<int32_t>(sizeof(void*)), true, 0);
    }

    // Numeric keys in a native vector; reference keys in MANAGED arrays.
    std::vector<LkChainKey> keys(static_cast<size_t>(n) * static_cast<size_t>(nkeys));
    Il2CppArray* refKeys[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        LkChainKey* level = keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
        if (fams[l] >= 4)
        {
            refKeys[l] = il2cpp_array_new(s->elemClass, static_cast<int32_t>(sizeof(void*)), true, n);
            roots.Add(reinterpret_cast<void**>(&refKeys[l]));
            for (int32_t i = 0; i < n; ++i)
            {
                // the reference key crosses in the object slot
                Il2CppObject* kv = InvokeKeyRefRef(sels[l], RefSlot(s, i));
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                    il2cpp_array_get_elem_ptr(refKeys[l], i));
                *slot = kv;
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), kv);
                level[i].o = kv;
            }
        }
        else
        {
            for (int32_t i = 0; i < n; ++i)
            {
                ReadLkChainKeyRef(RefSlot(s, i), fams[l], sels[l], level[i]);
            }
        }
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
    }
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        for (int32_t l = 0; l < nkeys; ++l)
                        {
                            const LkChainKey* level =
                                keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
                            LkChainKey ka = level[a];
                            LkChainKey kb = level[b];
                            if (fams[l] >= 4)
                            {
                                // Refresh from the MANAGED array: that is the
                                // storage of record, so a compaction between
                                // selector calls is already reflected there.
                                ka.o = *reinterpret_cast<Il2CppObject**>(
                                    il2cpp_array_get_elem_ptr(refKeys[l], a));
                                kb.o = *reinterpret_cast<Il2CppObject**>(
                                    il2cpp_array_get_elem_ptr(refKeys[l], b));
                            }
                            const int32_t c = CmpLkChainKey(ka, kb, fams[l]);
                            if (c != 0)
                            {
                                return descs[l] != 0 ? -c : c;
                            }
                        }
                        return 0;
                    });

    Il2CppArray* result = il2cpp_array_new(s->elemClass, static_cast<int32_t>(sizeof(void*)), true, n);
    roots.Add(reinterpret_cast<void**>(&result));
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(result, i));
        *slot = RefSlot(s, order[i]);
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), *slot);
    }
    return result;
}

// b5-gaps section 50: the DECIMAL-source chained ThenBy. The entries mirror
// the numeric chain contract exactly (up to four key selectors, parallel
// per-level direction / family literal tables, -1 terminates) but the element
// is a 16-byte System.Decimal cell crossed BY VALUE to each selector, and the
// key families are the decimal-dispatch codes 0..6 (int32/int64/double/
// decimal/DateTime/Guid/float -- the SortDecTwoKeysDispatch space). The result
// is a fresh decimal[].
Il2CppArray* il2cpp_linq_then_by_chain_dec(Il2CppArray* src,
                                          Il2CppObject* k1, Il2CppObject* k2,
                                          Il2CppObject* k3, Il2CppObject* k4,
                                          int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                          int32_t f1, int32_t f2, int32_t f3, int32_t f4)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, static_cast<int32_t>(sizeof(runtime::Il2CppDecimal)));
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkDecChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortDecChainCopyT(s, nkeys, sels, descs, fams,
                             ReadLkDecChainKey, CmpLkDecChainKey);
}

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
// impl-collation: a System.String key orders through
// runtime::il2cpp_string_compare_culture -- net48's Comparer<string>.Default
// (CultureInfo.CurrentCulture.CompareInfo, CompareOptions.None), implemented on
// the same OS NLS primitive (CompareStringEx with the current locale) net48
// itself uses on Windows. It is NOT an ordinal shortcut: an ordinal comparison
// would silently misorder mixed-case input ("a" sorts before "A" under culture,
// after it under ordinal), which is a wrong value rather than a deviation.
//
// Every other reference type defers to IComparable.CompareTo on the first key
// (Comparer<T>.Default), null-first.
//
// The NULL checks come FIRST, deliberately: net48 orders a null key before
// every non-null key without consulting the comparer at all, so
// `object[] { null, "a" }.OrderBy(x => x)` must return ",a". The string arm
// therefore only runs when BOTH keys are non-null.
//
// A mixed key pair (one string, one non-string) cannot happen for a
// Comparer<string>.Default sort: net48 throws ArgumentException ("Object must
// be of type String") -- and it is what the default comparer's own String arm
// does (arraysort.cpp TryCompareBoxedBuiltin). This comparator reproduces that
// through il2cpp_icomparable_compare_to, whose String arm throws the same way.
int32_t CompareRefKeys(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return 0;
    if (a == nullptr) return -1;
    if (b == nullptr) return 1;
    if (a->klass == il2cpp_string_class() && b->klass == il2cpp_string_class())
    {
        return runtime::il2cpp_string_compare_culture(a, b, 0);
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
// impl-collation: OrderBy / OrderByDescending over a REFERENCE-element source
// with a STATICALLY System.String key (Func<TSource,string>).
//
// net48 compares those keys with Comparer<string>.Default, i.e.
// CultureInfo.CurrentCulture.CompareInfo with CompareOptions.None -- CULTURE
// collation, so "a" sorts before "B" and, for strings equal apart from case,
// the lowercase form comes first. The comparison is
// runtime::il2cpp_string_compare_culture (the same OS NLS primitive net48 uses
// on Windows; the documented ASCII fold on non-Windows).
//
// This has its own entry (rather than reusing the generic _ref one) so the
// ordering is pinned at the call site: a string key never falls into the
// IComparable dispatch and never into an ordinal shortcut. Keys are cached
// EXACTLY ONCE per element and the sort is the shared STABLE merge sort
// (MergeSortStable -- net48 OrderBy is stable). null keys order first (net48)
// and the null checks run before the comparator, so a null key never reaches
// CompareStringEx. The result is a NEW reference array; the source is never
// written; the slot stores run the write barrier.
//------------------------------------------------------------------------------
namespace {

// net48 Comparer<string>.Default for a (source element) key pair: null-first,
// then the culture collation.
int32_t CompareStringKeys(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return 0;
    if (a == nullptr) return -1;
    if (b == nullptr) return 1;
    return runtime::il2cpp_string_compare_culture(a, b, 0);
}

} // namespace

Il2CppArray* il2cpp_linq_order_by_key_ref_string(Il2CppArray* src, Il2CppObject* keySel,
                                                 int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortRefsKeyCopy<Il2CppObject*>(s, d, desc != 0, InvokeKeyRefRef,
        CompareStringKeys);
}

//------------------------------------------------------------------------------
// b5-gaps section 56: OrderBy / OrderByDescending over a reference element
// source with a USER IComparer<TKey>. The comparer's own Compare is the
// authority (a string key no longer binds the culture collation). Dispatch
// mirrors the sort comparer machinery in arraysort.cpp: the StringComparer
// singletons take the direct ordinal path; other comparers ride the reflected
// "(r:" Compare slot; the non-generic IComparer falls to "(r:System.Object".
// Null keys behave exactly like the comparer says (the user comparer is
// called as-is, matching net48 OrderBy when a key is null).
//------------------------------------------------------------------------------
namespace {

// int32_t(*)(this, key, key) -- the generic-ref IComparer/T IComparer ABI.
typedef int32_t (*RefCompareFn)(Il2CppObject*, Il2CppObject*, Il2CppObject*);

int32_t LinqRefCompareCmp(Il2CppObject* cmp, Il2CppObject* a, Il2CppObject* b)
{
    if (cmp->klass != nullptr && cmp->klass->name != nullptr &&
        std::strncmp(cmp->klass->name, "System.StringComparer.", 22) == 0)
    {
        const bool ignoreCase = std::strcmp(
            cmp->klass->name, "System.StringComparer.OrdinalIgnoreCase") == 0;
        return ignoreCase
            ? il2cpp_string_compare_ordinal_ignore_case(
                  reinterpret_cast<const Il2CppString*>(a),
                  reinterpret_cast<const Il2CppString*>(b))
            : il2cpp_string_compare_ordinal(
                  reinterpret_cast<const Il2CppString*>(a),
                  reinterpret_cast<const Il2CppString*>(b));
    }
    int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Compare", "(r:");
    if (slot >= 0 && cmp->klass != nullptr && cmp->klass->vtable != nullptr &&
        slot < cmp->klass->vtableSize && cmp->klass->vtable[slot] != nullptr)
    {
        return reinterpret_cast<RefCompareFn>(cmp->klass->vtable[slot])(cmp, a, b);
    }
    slot = FindVirtualSlotByNameAndSignature(cmp, "Compare", "(r:System.Object");
    if (slot >= 0 && cmp->klass != nullptr && cmp->klass->vtable != nullptr &&
        slot < cmp->klass->vtableSize && cmp->klass->vtable[slot] != nullptr)
    {
        return reinterpret_cast<RefCompareFn>(cmp->klass->vtable[slot])(cmp, a, b);
    }
    il2cpp_throw_not_supported_exception(
        "OrderBy with a user IComparer over a reference key: the comparer type "
        "has no reflected virtual Compare(T,T) slot (see docs/audit/b5-gaps.md).");
    return 0; // unreachable (the factory throws)
}

} // namespace

//------------------------------------------------------------------------------
// SortRefsKeysCmpCopy -- SortRefsKeyCopy with a USER key comparer. The cached
// keys live in a ROOTED reference array (not a raw std::vector) because a user
// IComparer may allocate during the sort and the GC compacts: a raw key would
// be left MOVED, a rooted array is re-read through its (rooted) slot on every
// comparison (the M-6 groupby precedent). Keys are still cached EXACTLY ONCE
// per element (net48 EnumerableSorter contract).
//------------------------------------------------------------------------------
template <typename Cmp>
Il2CppArray* SortRefsKeysCmpCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                                 Cmp cmp)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n == 0)
    {
        return il2cpp_array_new(src->elemClass, sizeof(void*), true, 0);
    }

    Il2CppArray* keyArr = il2cpp_array_new(src->elemClass, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&keyArr));
    Il2CppObject** keySlot = reinterpret_cast<Il2CppObject**>(keyArr->data);
    for (int32_t i = 0; i < n; ++i)
    {
        keySlot[i] = InvokeKeyRefRef(keySel, RefSlot(src, i));
    }
    for (int32_t i = 0; i < n; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&keySlot[i]), keySlot[i]);
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) order[i] = i;

    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        // Re-read through the rooted array: a comparison may
                        // allocate and compact, moving the array object.
                        Il2CppObject* const* ks =
                            reinterpret_cast<Il2CppObject* const*>(keyArr->data);
                        const int32_t c = cmp(ks[a], ks[b]);
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

Il2CppArray* il2cpp_linq_order_by_key_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                              Il2CppObject* cmp, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    if (cmp == nullptr)
    {
        return SortRefsKeysCmpCopy(s, d, desc != 0, CompareStringKeys);
    }
    Il2CppObject* c = cmp;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    return SortRefsKeysCmpCopy(s, d, desc != 0,
        [c](Il2CppObject* a, Il2CppObject* b) -> int32_t {
            return LinqRefCompareCmp(c, a, b);
        });
}

//------------------------------------------------------------------------------
// impl-collation: the REFERENCE-element ThenBy / ThenByDescending FUSED
// COMPOSITE (net48 EnumerableSorter chain for OrderBy(k1).ThenBy(k2)). The
// primary key's comparison wins; the secondary key only breaks ties; equal
// (k1, k2) pairs keep their source order (stable). Each selector is invoked
// EXACTLY ONCE per element.
//
// The two key families are independent and ride trailing literals, the same
// contract as the numeric composites (0=int32, 1=int64, 2=double, 3=float,
// 4=System.String). A STRING key compares through
// runtime::il2cpp_string_compare_culture -- net48's Comparer<string>.Default,
// the same OS NLS primitive (CompareStringEx) with the same null-first rule.
//
// GC: elements are read through the ROOTED source array on every access and
// the result is allocated only after the last selector call, exactly like
// SortRefsKeyCopy.
//------------------------------------------------------------------------------
template <typename TK1, typename TK2, typename Invoke1, typename Invoke2,
          typename Cmp1, typename Cmp2>
Il2CppArray* SortRefsTwoKeysCopy(Il2CppArray* src, Il2CppDelegate* k1Sel,
                                 Il2CppDelegate* k2Sel, bool k1Desc, bool k2Desc,
                                 Invoke1 invoke1, Invoke2 invoke2,
                                 Cmp1 cmp1, Cmp2 cmp2)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&k1Sel));
    roots.Add(reinterpret_cast<void**>(&k2Sel));

    if (n == 0)
    {
        return il2cpp_array_new(src->elemClass, sizeof(void*), true, 0);
    }

    std::vector<TK1> k1(static_cast<size_t>(n));
    std::vector<TK2> k2(static_cast<size_t>(n));
    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
        k1[i] = invoke1(k1Sel, RefSlot(src, i));
    }
    for (int32_t i = 0; i < n; ++i)
    {
        k2[i] = invoke2(k2Sel, RefSlot(src, i));
    }

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

namespace {

// The (k1fam, k2fam) dispatch for the reference-element composite. 0=int32,
// 1=int64, 2=double, 3=float, 4=string -- the LinqValKeyFamilyCode contract
// extended with the collation family.
Il2CppArray* SortRefsTwoKeysFamDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                        Il2CppDelegate* d2, bool k1Desc,
                                        bool k2Desc, int32_t k1fam, int32_t k2fam)
{
#define REFSAM_K2_CASES(K1TYPE, K1INV, K1CMP)                                              \
    case 0: return SortRefsTwoKeysCopy<K1TYPE, int32_t>(s, d1, d2, k1Desc, k2Desc, K1INV,  \
        InvokeKeyRefI4, K1CMP, CmpKeyValue<int32_t>);                                      \
    case 1: return SortRefsTwoKeysCopy<K1TYPE, int64_t>(s, d1, d2, k1Desc, k2Desc, K1INV,  \
        InvokeKeyRefI8, K1CMP, CmpKeyValue<int64_t>);                                      \
    case 2: return SortRefsTwoKeysCopy<K1TYPE, double>(s, d1, d2, k1Desc, k2Desc, K1INV,   \
        InvokeKeyRefD, K1CMP, CmpKeyValue<double>);                                        \
    case 3: return SortRefsTwoKeysCopy<K1TYPE, float>(s, d1, d2, k1Desc, k2Desc, K1INV,    \
        InvokeKeyRefF, K1CMP, CmpKeyValue<float>);                                         \
    case 4: return SortRefsTwoKeysCopy<K1TYPE, Il2CppObject*>(s, d1, d2, k1Desc, k2Desc,   \
        K1INV, InvokeKeyRefRef, K1CMP, CompareStringKeys);                                 \
    case 5: return SortRefsTwoKeysCopy<K1TYPE, Il2CppObject*>(s, d1, d2, k1Desc, k2Desc,   \
        K1INV, InvokeKeyRefRef, K1CMP, CompareRefKeys);

    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            REFSAM_K2_CASES(int32_t, InvokeKeyRefI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            REFSAM_K2_CASES(int64_t, InvokeKeyRefI8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            REFSAM_K2_CASES(double, InvokeKeyRefD, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            REFSAM_K2_CASES(float, InvokeKeyRefF, CmpKeyValue<float>)
        default: break;
        }
        break;
    case 4:
        switch (k2fam)
        {
            REFSAM_K2_CASES(Il2CppObject*, InvokeKeyRefRef, CompareStringKeys)
        default: break;
        }
        break;
    case 5:
        // impl-collation: a key whose static family is a NON-string reference
        // (object / interface). The family is not judgement call: strings are
        // detected per instance by CompareRefKeys (culture collation), every
        // other reference defers to IComparable.CompareTo -- the net48
        // Comparer<TKey>.Default dispatch for a TKey that is not System.String.
        switch (k2fam)
        {
            REFSAM_K2_CASES(Il2CppObject*, InvokeKeyRefRef, CompareRefKeys)
        default: break;
        }
        break;
    default:
        break;
    }
#undef REFSAM_K2_CASES

    il2cpp_throw_not_supported_exception(
        "Enumerable.ThenBy over a reference-element source: the composite key "
        "family pair is not wired.");
    return nullptr;
}

} // namespace

Il2CppArray* il2cpp_linq_then_by_key_ref(Il2CppArray* src, Il2CppObject* k1Sel,
                                         Il2CppObject* k2Sel, int32_t k1desc,
                                         int32_t k2desc, int32_t k1fam, int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireRefLinqArray(s);
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    return SortRefsTwoKeysFamDispatch(s, d1, d2, k1desc != 0, k2desc != 0, k1fam, k2fam);
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
// b5-gaps section 71: OrderBy / OrderByDescending over a VALUE-element source
// with a USER IComparer<TKey> for the KEY (net48
// OrderBy<TSource,TKey>(source, keySelector, comparer)). The KEY is whatever
// the selector returns in its own family slot; the user comparer's Compare is
// the authority -- the per-family CmpKeyValue default is REPLACED, not kept.
//
// The reflected IComparer<T>.Compare(T,T) slot dispatch:
//   * a VALUE key rides the same cell-carrier ABI as LinqValueEqualsCmpCell
//     (linq_family.cpp: narrow/uint/int32 -> the int32 parameter slot, int64 ->
//     the int64 slot, float/double -> the double ABI); the slot fragment is
//     (i1/i1)/(u1,u1)/(b,b)/(i2,i2)/(u2,u2)/(c,c)/(i,i)/(l,l)/(f,f)/(d,d).
//     The comparer object is ROOTED for the whole sort (it may allocate while
//     comparing, and a compaction can move it), exactly the ref_cmp contract.
//
// The cached key type stays the selector's family:
//   * int32/narrow keys cache an int32_t, cached exactly once per element;
//   * keys that are 16-byte values (decimal/DateTime/TimeSpan/Guid) have NO
//     user comparer slot ABI measured yet and stay LOUD (b5-gaps section 57),
//     exactly as the value-comparer equality surface.
//   * a REFERENCE key over a value source (Func<TSource,string> -- the key
//     selector returns a boxed/class object) rides the rooted ref-key kernel
//     below (il2cpp_linq_order_by_key_<src>_ref_cmp): keys cached in a
//     ROOTED reference array and compared through LinqRefCompareCmp (the same
//     dispatch the reference-source comparator entry uses). This closes the
//     "any reference key over a value source had no sort kernel" gap for the
//     comparator surface (the non-comparator int32-source OrderBy still keys
//     through the per-family named entries).
//
// GC safety: the source is snapshotted into a native vector before ANY
// selector call (no allocation precedes the memcpy), the selector and the
// comparer are rooted across the key/sort phase, and the result array is
// allocated only after the last comparer call. The comparer dispatch re-reads
// the ROOTED comparer slot on every comparison (RootScope rewrites it after a
// compaction), exactly the SortRefsKeysCmpCopy discipline.
//------------------------------------------------------------------------------
namespace {

const char* LinqValueCompareFragment(int32_t cell)
{
    switch (cell)
    {
        case 0: return "(i1,i1)";  // sbyte
        case 1: return "(u1,u1)";  // byte
        case 2: return "(b,b)";    // bool
        case 3: return "(i2,i2)";  // short
        case 4: return "(u2,u2)";  // ushort
        case 5: return "(c,c)";    // char
        case 6: return "(i,i)";    // uint32
        case 7: return "(i,i)";    // int32
        case 8: return "(l,l)";    // int64
        case 9: return "(f,f)";    // float
        case 10: return "(d,d)";   // double
        default: return nullptr;
    }
}

// The user-comparer three-way dispatch over a VALUE key cell. `cmp` is the
// rooted comparer; the two keys arrive in their 64-bit carriers (int32
// widened, int64 direct, float/double as the DOUBLE bit image).
int32_t LinqValueCompareCmpCell(Il2CppObject* cmp, int32_t cell, int64_t a, int64_t b)
{
    const char* frag = LinqValueCompareFragment(cell);
    if (frag == nullptr)
    {
        il2cpp_throw_not_supported_exception(
            "a user IComparer over this value key cell family is not wired "
            "(b5-gaps section 71: decimal/DateTime/TimeSpan/Guid/DateTimeOffset "
            "comparers stay LOUD).");
        return 0;
    }
    const int32_t slot = FindVirtualSlotByNameAndSignature(cmp, "Compare", frag);
    if (slot < 0 || cmp->klass == nullptr || cmp->klass->vtable == nullptr ||
        slot >= cmp->klass->vtableSize || cmp->klass->vtable[slot] == nullptr)
    {
        char message[192];
        std::snprintf(message, sizeof(message),
                      "IComparer.Compare dispatch: the comparer type has no "
                      "reflected virtual Compare(%s) slot (b5-gaps section 71).",
                      frag);
        il2cpp_throw_not_supported_exception(message);
        return 0;
    }
    if (cell == 8) // int64
    {
        typedef int32_t (*CmpI8Fn)(Il2CppObject*, int64_t, int64_t);
        return reinterpret_cast<CmpI8Fn>(cmp->klass->vtable[slot])(cmp, a, b);
    }
    if (cell == 9 || cell == 10) // float / double -> double ABI
    {
        typedef int32_t (*CmpDFn)(Il2CppObject*, double, double);
        double da, db;
        std::memcpy(&da, &a, sizeof(double));
        std::memcpy(&db, &b, sizeof(double));
        return reinterpret_cast<CmpDFn>(cmp->klass->vtable[slot])(cmp, da, db);
    }
    typedef int32_t (*CmpI4Fn)(Il2CppObject*, int32_t, int32_t);
    return reinterpret_cast<CmpI4Fn>(cmp->klass->vtable[slot])(
        cmp, static_cast<int32_t>(a), static_cast<int32_t>(b));
}

} // namespace

// The int32-SOURCE user-comparer keyed sort. `cell` is the KEY cell:
//   * 0..7 -> int32-carrier key (InvokeKeyI4 / KeyI4* the selector ABI)
//   * 8    -> int64 key (InvokeKeyI4I8)
//   * 9    -> float key (InvokeKeyI4F)
//   * 10   -> double key (InvokeKeyI4D)
// Each key is cached EXACTLY ONCE in its OWN return-typed slot (the template's
// TKey) and compared via LinqValueCompareCmpCell on the 64-bit carrier.
namespace {

int64_t LinqCarrierOfDouble(double d)
{
    int64_t x = 0;
    std::memcpy(&x, &d, sizeof(double));
    return x;
}

inline int64_t LinqCarrierOfFloat(float f)
{
    return LinqCarrierOfDouble(static_cast<double>(f));
}

} // namespace

Il2CppArray* il2cpp_linq_order_by_key_i4_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                             Il2CppObject* cmp, int32_t keyCell,
                                             int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        // net48 OrderBy(source, keySelector, null) -> Comparer<TKey>.Default,
        // exactly the non-comparator named entries.
        switch (keyCell)
        {
        case 8:
            return SortInt32KeyCopyT<int64_t>(s, d, desc != 0, InvokeKeyI4I8,
                                              CmpKeyValue<int64_t>);
        case 9:
            return SortInt32KeyCopyT<float>(s, d, desc != 0, InvokeKeyI4F,
                                            CmpKeyValue<float>);
        case 10:
            return SortInt32KeyCopyT<double>(s, d, desc != 0, InvokeKeyI4D,
                                             CmpKeyValue<double>);
        default:
            return SortInt32KeyCopyT<int32_t>(s, d, desc != 0, InvokeKeyI4,
                                              CmpKeyValue<int32_t>);
        }
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    switch (keyCell)
    {
    case 8: // int64 key
        return SortInt32KeyCopyT<int64_t>(
            s, d, desc != 0, InvokeKeyI4I8,
            [c](int64_t a, int64_t b) { return LinqValueCompareCmpCell(c, 8, a, b); });
    case 9: // float key (own 4-byte slot; the compare reads the double image)
        return SortInt32KeyCopyT<float>(
            s, d, desc != 0, InvokeKeyI4F,
            [c](float a, float b) -> int32_t {
                return LinqValueCompareCmpCell(c, 9,
                    LinqCarrierOfFloat(a), LinqCarrierOfFloat(b));
            });
    case 10: // double key
        return SortInt32KeyCopyT<double>(
            s, d, desc != 0, InvokeKeyI4D,
            [c](double a, double b) -> int32_t {
                return LinqValueCompareCmpCell(c, 10,
                    LinqCarrierOfDouble(a), LinqCarrierOfDouble(b));
            });
    default: // 0..7 -> int32-carrier (narrow kinds all ride the int32 slot ABI)
        if (keyCell < 0 || keyCell > 7)
        {
            il2cpp_throw_not_supported_exception(
                "a user IComparer over this key cell is not wired "
                "(b5-gaps section 71).");
            return nullptr;
        }
        return SortInt32KeyCopyT<int32_t>(
            s, d, desc != 0, InvokeKeyI4,
            [c, keyCell](int32_t a, int32_t b) -> int32_t {
                return LinqValueCompareCmpCell(c, keyCell, a, (int64_t)b);
            });
    }
}

Il2CppArray* il2cpp_linq_order_by_key_d_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                            Il2CppObject* cmp, int32_t keyCell,
                                            int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        switch (keyCell)
        {
        case 8:
            return SortWideElemKeyCopyT<double, kElemKindD, int64_t>(
                s, d, desc != 0, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
        case 9:
            return SortWideElemKeyCopyT<double, kElemKindD, float>(
                s, d, desc != 0, InvokeKeyDSrcF, CmpKeyValue<float>);
        case 10:
            return SortWideElemKeyCopyT<double, kElemKindD, double>(
                s, d, desc != 0, InvokeKeyDSrcD, CmpKeyValue<double>);
        default:
            return SortWideElemKeyCopyT<double, kElemKindD, int32_t>(
                s, d, desc != 0, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
        }
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    switch (keyCell)
    {
    case 8:
        return SortWideElemKeyCopyT<double, kElemKindD, int64_t>(
            s, d, desc != 0, InvokeKeyDSrcI8,
            [c](int64_t a, int64_t b) { return LinqValueCompareCmpCell(c, 8, a, b); });
    case 9:
        return SortWideElemKeyCopyT<double, kElemKindD, float>(
            s, d, desc != 0, InvokeKeyDSrcF,
            [c](float a, float b) -> int32_t {
                return LinqValueCompareCmpCell(c, 9,
                    LinqCarrierOfFloat(a), LinqCarrierOfFloat(b));
            });
    case 10:
        return SortWideElemKeyCopyT<double, kElemKindD, double>(
            s, d, desc != 0, InvokeKeyDSrcD,
            [c](double a, double b) -> int32_t {
                return LinqValueCompareCmpCell(c, 10,
                    LinqCarrierOfDouble(a), LinqCarrierOfDouble(b));
            });
    default:
        if (keyCell < 0 || keyCell > 7)
        {
            il2cpp_throw_not_supported_exception(
                "a user IComparer over this key cell is not wired "
                "(b5-gaps section 71).");
            return nullptr;
        }
        return SortWideElemKeyCopyT<double, kElemKindD, int32_t>(
            s, d, desc != 0, InvokeKeyDSrcI4,
            [c, keyCell](int32_t a, int32_t b) -> int32_t {
                return LinqValueCompareCmpCell(c, keyCell, a, (int64_t)b);
            });
    }
}

Il2CppArray* il2cpp_linq_order_by_key_f_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                            Il2CppObject* cmp, int32_t keyCell,
                                            int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        switch (keyCell)
        {
        case 8:
            return SortWideElemKeyCopyT<float, kElemKindF, int64_t>(
                s, d, desc != 0, InvokeKeyDSrcI8, CmpKeyValue<int64_t>);
        case 9:
            return SortWideElemKeyCopyT<float, kElemKindF, float>(
                s, d, desc != 0, InvokeKeyDSrcF, CmpKeyValue<float>);
        case 10:
            return SortWideElemKeyCopyT<float, kElemKindF, double>(
                s, d, desc != 0, InvokeKeyDSrcD, CmpKeyValue<double>);
        default:
            return SortWideElemKeyCopyT<float, kElemKindF, int32_t>(
                s, d, desc != 0, InvokeKeyDSrcI4, CmpKeyValue<int32_t>);
        }
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    switch (keyCell)
    {
    case 8:
        return SortWideElemKeyCopyT<float, kElemKindF, int64_t>(
            s, d, desc != 0, InvokeKeyDSrcI8,
            [c](int64_t a, int64_t b) { return LinqValueCompareCmpCell(c, 8, a, b); });
    case 9:
        return SortWideElemKeyCopyT<float, kElemKindF, float>(
            s, d, desc != 0, InvokeKeyDSrcF,
            [c](float a, float b) -> int32_t {
                return LinqValueCompareCmpCell(c, 9,
                    LinqCarrierOfFloat(a), LinqCarrierOfFloat(b));
            });
    case 10:
        return SortWideElemKeyCopyT<float, kElemKindF, double>(
            s, d, desc != 0, InvokeKeyDSrcD,
            [c](double a, double b) -> int32_t {
                return LinqValueCompareCmpCell(c, 10,
                    LinqCarrierOfDouble(a), LinqCarrierOfDouble(b));
            });
    default:
        if (keyCell < 0 || keyCell > 7)
        {
            il2cpp_throw_not_supported_exception(
                "a user IComparer over this key cell is not wired "
                "(b5-gaps section 71).");
            return nullptr;
        }
        return SortWideElemKeyCopyT<float, kElemKindF, int32_t>(
            s, d, desc != 0, InvokeKeyDSrcI4,
            [c, keyCell](int32_t a, int32_t b) -> int32_t {
                return LinqValueCompareCmpCell(c, keyCell, a, (int64_t)b);
            });
    }
}

Il2CppArray* il2cpp_linq_order_by_key_i8_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                             Il2CppObject* cmp, int32_t keyCell,
                                             int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        switch (keyCell)
        {
        case 8:
            return SortWideElemKeyCopyT<int64_t, kElemKindI8, int64_t>(
                s, d, desc != 0, InvokeKeyI8SrcI8, CmpKeyValue<int64_t>);
        case 9:
            return SortWideElemKeyCopyT<int64_t, kElemKindI8, float>(
                s, d, desc != 0, InvokeKeyI8SrcF, CmpKeyValue<float>);
        case 10:
            return SortWideElemKeyCopyT<int64_t, kElemKindI8, double>(
                s, d, desc != 0, InvokeKeyI8SrcD, CmpKeyValue<double>);
        default:
            return SortWideElemKeyCopyT<int64_t, kElemKindI8, int32_t>(
                s, d, desc != 0, InvokeKeyI8SrcI4, CmpKeyValue<int32_t>);
        }
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    switch (keyCell)
    {
    case 8:
        return SortWideElemKeyCopyT<int64_t, kElemKindI8, int64_t>(
            s, d, desc != 0, InvokeKeyI8SrcI8,
            [c](int64_t a, int64_t b) { return LinqValueCompareCmpCell(c, 8, a, b); });
    case 9:
        return SortWideElemKeyCopyT<int64_t, kElemKindI8, float>(
            s, d, desc != 0, InvokeKeyI8SrcF,
            [c](float a, float b) -> int32_t {
                return LinqValueCompareCmpCell(c, 9,
                    LinqCarrierOfFloat(a), LinqCarrierOfFloat(b));
            });
    case 10:
        return SortWideElemKeyCopyT<int64_t, kElemKindI8, double>(
            s, d, desc != 0, InvokeKeyI8SrcD,
            [c](double a, double b) -> int32_t {
                return LinqValueCompareCmpCell(c, 10,
                    LinqCarrierOfDouble(a), LinqCarrierOfDouble(b));
            });
    default:
        if (keyCell < 0 || keyCell > 7)
        {
            il2cpp_throw_not_supported_exception(
                "a user IComparer over this key cell is not wired "
                "(b5-gaps section 71).");
            return nullptr;
        }
        return SortWideElemKeyCopyT<int64_t, kElemKindI8, int32_t>(
            s, d, desc != 0, InvokeKeyI8SrcI4,
            [c, keyCell](int32_t a, int32_t b) -> int32_t {
                return LinqValueCompareCmpCell(c, keyCell, a, (int64_t)b);
            });
    }
}

// The value-source REFERENCE-key comparator sort (Func<TSource,string> etc.):
// each key crosses the REF slot (a boxed object), cached in a ROOTED reference
// array and compared through LinqRefCompareCmp -- the same dispatched
// authority the reference-source comparator entry uses. `elemSize`/`ElemKind`
// re-materialize the SOURCE family.
template <typename TElem, int32_t ElemKind, typename InvokeRefKey>
Il2CppArray* SortValueSrcRefKeyCmpCopyT(Il2CppArray* src, Il2CppDelegate* keySel,
                                        Il2CppObject* cmp, bool desc,
                                        InvokeRefKey invokeRefKey)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n == 0)
    {
        return il2cpp_array_new_typed(static_cast<int32_t>(sizeof(TElem)),
                                      ElemKind, 0);
    }

    std::vector<TElem> elems(static_cast<size_t>(n));
    std::memcpy(elems.data(), src->data,
                static_cast<size_t>(n) * sizeof(TElem));

    Il2CppArray* keyArr = il2cpp_array_new(src->elemClass, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&keyArr));
    Il2CppObject** keySlot = reinterpret_cast<Il2CppObject**>(keyArr->data);
    for (int32_t i = 0; i < n; ++i)
    {
        keySlot[i] = invokeRefKey(keySel, elems[i]);
    }
    for (int32_t i = 0; i < n; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&keySlot[i]), keySlot[i]);
    }

    Il2CppObject* c = cmp;
    roots.Add(reinterpret_cast<void**>(&c));

    std::vector<int32_t> order(static_cast<size_t>(n));
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) order[i] = i;

    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        Il2CppObject* const* ks =
                            reinterpret_cast<Il2CppObject* const*>(keyArr->data);
                        const int32_t c1 = LinqRefCompareCmp(c, ks[a], ks[b]);
                        return desc ? -c1 : c1;
                    });

    Il2CppArray* result = il2cpp_array_new_typed(
        static_cast<int32_t>(sizeof(TElem)), ElemKind, n);
    roots.Add(reinterpret_cast<void**>(&result));
    TElem* out = reinterpret_cast<TElem*>(result->data);
    for (int32_t i = 0; i < n; ++i)
    {
        out[i] = elems[order[i]];
    }
    return result;
}

// The value-source REFERENCE-key selector invokes (Func<TSource,string> /
// Func<TSource,object> -- the element crosses BY VALUE in its own family slot
// and the key returns in the REF slot). The float source widens the element to
// the F-stack double slot exactly like the value-key readers.
namespace {

typedef Il2CppObject* (*KeyRefI4SrcFn)(int32_t value);
typedef Il2CppObject* (*KeyRefI4SrcThisFn)(Il2CppObject* self, int32_t value);
typedef Il2CppObject* (*KeyRefI8SrcFn)(int64_t value);
typedef Il2CppObject* (*KeyRefI8SrcThisFn)(Il2CppObject* self, int64_t value);
typedef Il2CppObject* (*KeyRefDSrcFn)(double value);
typedef Il2CppObject* (*KeyRefDSrcThisFn)(Il2CppObject* self, double value);

Il2CppObject* InvokeKeyI4SrcRef(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefI4SrcThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<KeyRefI4SrcFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeKeyI8SrcRef(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefI8SrcThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<KeyRefI8SrcFn>(d->methodPtr)(v);
}

Il2CppObject* InvokeKeyDSrcRef(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<KeyRefDSrcThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<KeyRefDSrcFn>(d->methodPtr)(v);
}

} // namespace

// The four value-source REFERENCE-key comparator entries: the key selector is
// Func<TSource,TKeyRef> (the element crosses by value, the key returns in the
// REF slot), caching the keys in a ROOTED array and sorting through the user
// comparer (LinqRefCompareCmp -- the ref_cmp authority). The result is a fresh
// array of the SOURCE family.
Il2CppArray* il2cpp_linq_order_by_key_i4_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                 Il2CppObject* cmp, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortValueSrcRefKeyCmpCopyT<int32_t, kElemKindNone>(
        s, d, cmp, desc != 0, InvokeKeyI4SrcRef);
}

Il2CppArray* il2cpp_linq_order_by_key_i8_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                 Il2CppObject* cmp, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortValueSrcRefKeyCmpCopyT<int64_t, kElemKindI8>(
        s, d, cmp, desc != 0, InvokeKeyI8SrcRef);
}

Il2CppArray* il2cpp_linq_order_by_key_d_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                Il2CppObject* cmp, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(double));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortValueSrcRefKeyCmpCopyT<double, kElemKindD>(
        s, d, cmp, desc != 0, InvokeKeyDSrcRef);
}

Il2CppArray* il2cpp_linq_order_by_key_f_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                Il2CppObject* cmp, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(float));
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortValueSrcRefKeyCmpCopyT<float, kElemKindF>(
        s, d, cmp, desc != 0, InvokeKeyDSrcRef);
}

//------------------------------------------------------------------------------
// b5-gaps section 72: OrderBy / OrderByDescending over a NARROW-element ARRAY
// source (sbyte[] / byte[] / bool[] / short[] / ushort[] / char[], plus the
// uint[] source whose cells widen into the int32 slot). The source is one of
// the six narrow cell families (IL2CPP_LINQ_CELL_SBYTE..CHAR, the srcCell
// trailing literal); each element is read at its REAL width and widened into
// the INT32 selector slot (the M1 rule -- the same widening a byte[]
// predicate/selector already uses, linq_family.cpp LinqCellReadI4). The result
// is a FRESH array of the SOURCE's own narrow cell (LinqNewArray stamps the
// section-70 element kind -- Bool/S1/S2/U2, byte and char decide by cell
// width), never a widened int32[].
//
// The KEY is whatever the selector returns in its own family slot:
//   * the PLAIN entry uses the per-family CmpKeyValue default comparator;
//   * the _cmp entry replaces it with a USER IComparer<TKey> -- the key cell
//     rides the SAME cell dispatch as il2cpp_linq_order_by_key_i4_cmp
//     (0..7 int32-carrier, 8 int64, 9 float, 10 double; the narrow/uint key
//     cells and the 16-byte value keys otherwise follow section 71 exactly);
//   * the _ref_cmp entry handles a REFERENCE key (Func<TSource,string> etc.):
//     keys cached in a ROOTED reference array and compared through
//     LinqRefCompareCmp -- the shared ref_cmp authority.
// GC SAFETY follows the sibling kernels: the elements are snapshotted into a
// native vector BEFORE any selector call, the selectors / comparer / key array
// are rooted across the key/sort phase (a compaction rewrites the locals), and
// the result is allocated only after the last selector/comparer call.
//------------------------------------------------------------------------------
namespace {

// Write a widened int32 carrier back into a narrow cell at its REAL width (the
// section-70 write-back: bool normalizes 0/1, the signed families narrow the
// low bits -- the widened value round-trips the original cell exactly).
void LinqNarrowWriteBack(Il2CppArray* out, int32_t index, int32_t srcCell,
                         int32_t value)
{
    char* dst = LinqCellPtr(out, index);
    switch (srcCell)
    {
    case IL2CPP_LINQ_CELL_SBYTE:
        *reinterpret_cast<int8_t*>(dst) = static_cast<int8_t>(value);
        break;
    case IL2CPP_LINQ_CELL_BYTE:
        *reinterpret_cast<uint8_t*>(dst) = static_cast<uint8_t>(value);
        break;
    case IL2CPP_LINQ_CELL_BOOL:
        *reinterpret_cast<uint8_t*>(dst) =
            value != 0 ? static_cast<uint8_t>(1) : static_cast<uint8_t>(0);
        break;
    case IL2CPP_LINQ_CELL_SHORT:
        *reinterpret_cast<int16_t*>(dst) = static_cast<int16_t>(value);
        break;
    case IL2CPP_LINQ_CELL_USHORT:
        *reinterpret_cast<uint16_t*>(dst) = static_cast<uint16_t>(value);
        break;
    case IL2CPP_LINQ_CELL_CHAR:
        *reinterpret_cast<char16_t*>(dst) = static_cast<char16_t>(value);
        break;
    default:
        std::memcpy(dst, &value, static_cast<size_t>(LinqCellSize(srcCell)));
        break;
    }
}

// The narrow-source keyed sort: the elements are widened into an int32 native
// snapshot (the M1 slider), every key is cached EXACTLY ONCE (net48
// EnumerableSorter), the INDICES are merged stably by the cached keys, and the
// result is a fresh array of the SOURCE narrow cell re-materialized by index.
template <typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortNarrowKeyCopyT(Il2CppArray* src, Il2CppDelegate* keySel,
                                bool desc, int32_t srcCell, InvokeKey invokeKey,
                                Cmp cmp)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n == 0)
    {
        return LinqNewArray(srcCell, 0);
    }

    // Element snapshot taken before ANY selector call: no managed allocation
    // preceded the reads, so 'src' cannot have been moved.
    std::vector<int32_t> elems(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        elems[i] = LinqCellReadI4(src, i, srcCell);
    }
    std::vector<TKey> keys(static_cast<size_t>(n));
    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
    }
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

    Il2CppArray* result = LinqNewArray(srcCell, n);
    roots.Add(reinterpret_cast<void**>(&result));
    for (int32_t i = 0; i < n; ++i)
    {
        LinqNarrowWriteBack(result, i, srcCell, elems[order[i]]);
    }
    return result;
}

// The narrow-source REFERENCE-key comparator sort (Func<TSource,string> etc.):
// the keys cross the REF slot, cached in a ROOTED reference array and compared
// through the shared LinqRefCompareCmp (StringComparer singletons + the
// reflected Compare(T,T) slot). The elements are widened into the int32
// snapshot exactly like the value-key kernel above.
template <typename InvokeRefKey>
Il2CppArray* SortNarrowRefKeyCmpCopyT(Il2CppArray* src, Il2CppDelegate* keySel,
                                      Il2CppObject* cmp, bool desc, int32_t srcCell,
                                      InvokeRefKey invokeRefKey)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n == 0)
    {
        return LinqNewArray(srcCell, 0);
    }

    std::vector<int32_t> elems(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        elems[i] = LinqCellReadI4(src, i, srcCell);
    }

    Il2CppArray* keyArr = il2cpp_array_new(src->elemClass, sizeof(void*), true, n);
    roots.Add(reinterpret_cast<void**>(&keyArr));
    Il2CppObject** keySlot = reinterpret_cast<Il2CppObject**>(keyArr->data);
    for (int32_t i = 0; i < n; ++i)
    {
        keySlot[i] = invokeRefKey(keySel, elems[i]);
    }
    for (int32_t i = 0; i < n; ++i)
    {
        il2cpp_write_barrier(reinterpret_cast<void**>(&keySlot[i]), keySlot[i]);
    }

    Il2CppObject* c = cmp;
    roots.Add(reinterpret_cast<void**>(&c));

    std::vector<int32_t> order(static_cast<size_t>(n));
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) order[i] = i;

    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        Il2CppObject* const* ks =
                            reinterpret_cast<Il2CppObject* const*>(keyArr->data);
                        const int32_t c1 = LinqRefCompareCmp(c, ks[a], ks[b]);
                        return desc ? -c1 : c1;
                    });

    Il2CppArray* result = LinqNewArray(srcCell, n);
    roots.Add(reinterpret_cast<void**>(&result));
    for (int32_t i = 0; i < n; ++i)
    {
        LinqNarrowWriteBack(result, i, srcCell, elems[order[i]]);
    }
    return result;
}

} // namespace

Il2CppArray* il2cpp_linq_order_by_key_narrow(Il2CppArray* src, Il2CppObject* keySel,
                                             int32_t keyCell, int32_t srcCell,
                                             int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    LinqRequireCell(s, srcCell);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    switch (keyCell)
    {
    case IL2CPP_LINQ_CELL_I8:
        return SortNarrowKeyCopyT<int64_t>(s, d, desc != 0, srcCell,
                                           InvokeKeyI4I8, CmpKeyValue<int64_t>);
    case IL2CPP_LINQ_CELL_F:
        return SortNarrowKeyCopyT<float>(s, d, desc != 0, srcCell,
                                         InvokeKeyI4F, CmpKeyValue<float>);
    case IL2CPP_LINQ_CELL_D:
        return SortNarrowKeyCopyT<double>(s, d, desc != 0, srcCell,
                                          InvokeKeyI4D, CmpKeyValue<double>);
    default:
        if (keyCell < 0 || keyCell > IL2CPP_LINQ_CELL_I4)
        {
            il2cpp_throw_not_supported_exception(
                "OrderBy over a narrow source: the key cell has no plain sort "
                "kernel (b5-gaps section 71/72: 16-byte and reference keys over "
                "a value source need the comparer entry).");
            return nullptr;
        }
        return SortNarrowKeyCopyT<int32_t>(s, d, desc != 0, srcCell,
                                           InvokeKeyI4, CmpKeyValue<int32_t>);
    }
}

Il2CppArray* il2cpp_linq_order_by_key_narrow_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                 Il2CppObject* cmp, int32_t keyCell,
                                                 int32_t srcCell, int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    LinqRequireCell(s, srcCell);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        // net48 OrderBy(source, keySelector, null) -> Comparer<TKey>.Default,
        // exactly the non-comparator entry.
        switch (keyCell)
        {
        case IL2CPP_LINQ_CELL_I8:
            return SortNarrowKeyCopyT<int64_t>(s, d, desc != 0, srcCell,
                                               InvokeKeyI4I8, CmpKeyValue<int64_t>);
        case IL2CPP_LINQ_CELL_F:
            return SortNarrowKeyCopyT<float>(s, d, desc != 0, srcCell,
                                             InvokeKeyI4F, CmpKeyValue<float>);
        case IL2CPP_LINQ_CELL_D:
            return SortNarrowKeyCopyT<double>(s, d, desc != 0, srcCell,
                                              InvokeKeyI4D, CmpKeyValue<double>);
        default:
            return SortNarrowKeyCopyT<int32_t>(s, d, desc != 0, srcCell,
                                               InvokeKeyI4, CmpKeyValue<int32_t>);
        }
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    switch (keyCell)
    {
    case IL2CPP_LINQ_CELL_I8:
        return SortNarrowKeyCopyT<int64_t>(
            s, d, desc != 0, srcCell, InvokeKeyI4I8,
            [c](int64_t a, int64_t b) { return LinqValueCompareCmpCell(c, 8, a, b); });
    case IL2CPP_LINQ_CELL_F:
        return SortNarrowKeyCopyT<float>(
            s, d, desc != 0, srcCell, InvokeKeyI4F,
            [c](float a, float b) -> int32_t {
                return LinqValueCompareCmpCell(c, 9,
                    LinqCarrierOfFloat(a), LinqCarrierOfFloat(b));
            });
    case IL2CPP_LINQ_CELL_D:
        return SortNarrowKeyCopyT<double>(
            s, d, desc != 0, srcCell, InvokeKeyI4D,
            [c](double a, double b) -> int32_t {
                return LinqValueCompareCmpCell(c, 10,
                    LinqCarrierOfDouble(a), LinqCarrierOfDouble(b));
            });
    default:
        if (keyCell < 0 || keyCell > IL2CPP_LINQ_CELL_I4)
        {
            il2cpp_throw_not_supported_exception(
                "a user IComparer over this key cell is not wired "
                "(b5-gaps section 71).");
            return nullptr;
        }
        return SortNarrowKeyCopyT<int32_t>(
            s, d, desc != 0, srcCell, InvokeKeyI4,
            [c, keyCell](int32_t a, int32_t b) -> int32_t {
                return LinqValueCompareCmpCell(c, keyCell, a, (int64_t)b);
            });
    }
}

Il2CppArray* il2cpp_linq_order_by_key_narrow_ref_cmp(Il2CppArray* src, Il2CppObject* keySel,
                                                     Il2CppObject* cmp, int32_t srcCell,
                                                     int32_t desc)
{
    Il2CppArray* s = RequireLinqArray(src);
    LinqRequireCell(s, srcCell);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    return SortNarrowRefKeyCmpCopyT(s, d, cmp, desc != 0, srcCell,
                                    InvokeKeyI4SrcRef);
}

//------------------------------------------------------------------------------
// b5-gaps section 73: the NARROW-ELEMENT chained ThenBy
// (OrderBy(k1).ThenBy(k2)[.ThenBy(k3)][.ThenBy(k4)]) over a narrow-element
// array source (sbyte[]/byte[]/bool[]/short[]/ushort[]/char[]). The folded
// chain fuses the primary + secondary orders like net48's EnumerableSorter:
// each element's key at EVERY level is cached EXACTLY ONCE, then the indices
// are merged stably on the (k1, k2, k3, k4) tuple.
//
// The SOURCE element is read at its REAL cell width and WIDENED into the INT32
// selector slot (the M1 rule, LinqCellReadI4); the per-level key families ride
// the numeric dispatch space 0..3 (int32/int64/double/float) over that int32
// element -- the same keys ReadLkChainKeyI4 produces. The result is a FRESH
// array of the SOURCE's own narrow cell (LinqNewArray + the section-72
// LinqNarrowWriteBack re-materialization, never a widened int32[]).
//------------------------------------------------------------------------------
namespace {

// The narrow-source level validator: the numeric dispatch families 0..3 only
// (a reference / decimal / value-struct key has no narrow-element reader).
int32_t NarrowChainLevelCount(const int32_t* fams, int32_t maxLevels)
{
    int32_t used = 0;
    for (int32_t l = 0; l < maxLevels; ++l)
    {
        if (fams[l] < 0)
        {
            break;
        }
        if (fams[l] > 3)
        {
            il2cpp_throw_not_supported_exception(
                "Chained ThenBy over a narrow source: only the numeric key "
                "families (int32/int64/double/float) are wired "
                "(b5-gaps section 73).");
        }
        ++used;
    }
    return used;
}

} // namespace

Il2CppArray* il2cpp_linq_then_by_chain_narrow(Il2CppArray* src,
                                              Il2CppObject* k1, Il2CppObject* k2,
                                              Il2CppObject* k3, Il2CppObject* k4,
                                              int32_t d1, int32_t d2, int32_t d3,
                                              int32_t d4,
                                              int32_t f1, int32_t f2, int32_t f3,
                                              int32_t f4, int32_t srcCell)
{
    Il2CppArray* s = RequireLinqArray(src);
    LinqRequireCell(s, srcCell);
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = NarrowChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };

    const int32_t n = s->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&s));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        roots.Add(reinterpret_cast<void**>(&sels[l]));
    }

    if (n <= 0)
    {
        return LinqNewArray(srcCell, 0);
    }

    // Element snapshot taken before ANY selector call: each element is read at
    // its REAL width and widened into the int32 selector slot (M1); no managed
    // allocation precedes the reads, so 'src' cannot have been moved.
    std::vector<int32_t> elems(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        elems[i] = LinqCellReadI4(s, i, srcCell);
    }

    std::vector<LkChainKey> keys(static_cast<size_t>(n) * static_cast<size_t>(nkeys));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        LkChainKey* level = keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
        for (int32_t i = 0; i < n; ++i)
        {
            ReadLkChainKeyI4(elems[i], fams[l], sels[l], level[i]);
        }
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i)
    {
        order[i] = i;
    }
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        for (int32_t l = 0; l < nkeys; ++l)
                        {
                            const LkChainKey* level =
                                keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
                            const int32_t c = CmpLkChainKey(level[a], level[b], fams[l]);
                            if (c != 0)
                            {
                                return descs[l] != 0 ? -c : c;
                            }
                        }
                        return 0;
                    });

    Il2CppArray* result = LinqNewArray(srcCell, n);
    roots.Add(reinterpret_cast<void**>(&result));
    for (int32_t i = 0; i < n; ++i)
    {
        LinqNarrowWriteBack(result, i, srcCell, elems[order[i]]);
    }
    return result;
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

//------------------------------------------------------------------------------
// b5-gaps section 74: the USER-COMPARER twins of the value-source two-key
// composites (OrderBy(k1).ThenBy(k2, comparer) / ThenByDescending). net48
// fuses the primary under Comparer<TKey1>.Default and the SECONDARY under the
// USER IComparer<TKey2> faithfully: each element's k1 and k2 are cached EXACTLY
// ONCE, then the indices are stable-merged on (k1 asc|desc, k2 asc|desc under
// the user comparer). The primary comparator stays CmpKeyValue; the secondary
// comparator is LinqValueCompareCmpCell (the section-71 value-comparer
// dispatch) on the cached K2 Value in its own carrier.
//------------------------------------------------------------------------------
#define I4SRC_K1_CASES_CMP(K1TYPE, K1INV, K1CMP)                                     \
    case 0: return SortInt32TwoKeysCopyT<K1TYPE, int32_t>(                          \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI4, K1CMP,                        \
        [c](int32_t a, int32_t b) -> int32_t {                                        \
            return LinqValueCompareCmpCell(c, 7, static_cast<int64_t>(a),             \
                                           static_cast<int64_t>(b));                  \
        });                                                                           \
    case 1: return SortInt32TwoKeysCopyT<K1TYPE, int64_t>(                          \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI4I8, K1CMP,                       \
        [c](int64_t a, int64_t b) -> int32_t {                                        \
            return LinqValueCompareCmpCell(c, 8, a, b);                               \
        });                                                                           \
    case 2: return SortInt32TwoKeysCopyT<K1TYPE, double>(                           \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI4D, K1CMP,                        \
        [c](double a, double b) -> int32_t {                                          \
            return LinqValueCompareCmpCell(c, 10, LinqCarrierOfDouble(a),             \
                                           LinqCarrierOfDouble(b));                   \
        });                                                                           \
    case 3: return SortInt32TwoKeysCopyT<K1TYPE, float>(                            \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI4F, K1CMP,                        \
        [c](float a, float b) -> int32_t {                                            \
            return LinqValueCompareCmpCell(c, 9, LinqCarrierOfFloat(a),               \
                                           LinqCarrierOfFloat(b));                    \
        });

Il2CppArray* SortInt32TwoKeysCmpDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                         Il2CppDelegate* d2, bool k1Desc,
                                         bool k2Desc, int32_t k1fam, int32_t k2fam,
                                         Il2CppObject* c)
{
    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            I4SRC_K1_CASES_CMP(int32_t, InvokeKeyI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            I4SRC_K1_CASES_CMP(int64_t, InvokeKeyI4I8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            I4SRC_K1_CASES_CMP(double, InvokeKeyI4D, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            I4SRC_K1_CASES_CMP(float, InvokeKeyI4F, CmpKeyValue<float>)
        default: break;
        }
        break;
    default: break;
    }
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown int32-source key family for the user comparer");
    return nullptr;
}
#undef I4SRC_K1_CASES_CMP

// The int64-SOURCE twin (SortInt64TwoKeysCopy with the section-74 user
// comparer on the secondary key).
#define I8SRC_K1_CASES_CMP(K1TYPE, K1INV, K1CMP)                                     \
    case 0: return SortInt64TwoKeysCopy<K1TYPE, int32_t>(                           \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI8SrcI4, K1CMP,                   \
        [c](int32_t a, int32_t b) -> int32_t {                                        \
            return LinqValueCompareCmpCell(c, 7, static_cast<int64_t>(a),             \
                                           static_cast<int64_t>(b));                  \
        });                                                                           \
    case 1: return SortInt64TwoKeysCopy<K1TYPE, int64_t>(                           \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI8SrcI8, K1CMP,                    \
        [c](int64_t a, int64_t b) -> int32_t {                                        \
            return LinqValueCompareCmpCell(c, 8, a, b);                               \
        });                                                                           \
    case 2: return SortInt64TwoKeysCopy<K1TYPE, double>(                            \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI8SrcD, K1CMP,                     \
        [c](double a, double b) -> int32_t {                                          \
            return LinqValueCompareCmpCell(c, 10, LinqCarrierOfDouble(a),             \
                                           LinqCarrierOfDouble(b));                   \
        });                                                                           \
    case 3: return SortInt64TwoKeysCopy<K1TYPE, float>(                             \
        s, d1, d2, k1Desc, k2Desc, K1INV, InvokeKeyI8SrcF, K1CMP,                     \
        [c](float a, float b) -> int32_t {                                            \
            return LinqValueCompareCmpCell(c, 9, LinqCarrierOfFloat(a),               \
                                           LinqCarrierOfFloat(b));                    \
        });

Il2CppArray* SortInt64TwoKeysCmpDispatch(Il2CppArray* s, Il2CppDelegate* d1,
                                         Il2CppDelegate* d2, bool k1Desc,
                                         bool k2Desc, int32_t k1fam, int32_t k2fam,
                                         Il2CppObject* c)
{
    switch (k1fam)
    {
    case 0:
        switch (k2fam)
        {
            I8SRC_K1_CASES_CMP(int32_t, InvokeKeyI8SrcI4, CmpKeyValue<int32_t>)
        default: break;
        }
        break;
    case 1:
        switch (k2fam)
        {
            I8SRC_K1_CASES_CMP(int64_t, InvokeKeyI8SrcI8, CmpKeyValue<int64_t>)
        default: break;
        }
        break;
    case 2:
        switch (k2fam)
        {
            I8SRC_K1_CASES_CMP(double, InvokeKeyI8SrcD, CmpKeyValue<double>)
        default: break;
        }
        break;
    case 3:
        switch (k2fam)
        {
            I8SRC_K1_CASES_CMP(float, InvokeKeyI8SrcF, CmpKeyValue<float>)
        default: break;
        }
        break;
    default: break;
    }
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown int64-source key family for the user comparer");
    return nullptr;
}
#undef I8SRC_K1_CASES_CMP

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
// b5-gaps section 74: OrderBy(k1).ThenBy(k2, userComparer) over an INT32 /
// INT64 VALUE source -- the two-key composite whose SECONDARY key is compared
// by the USER IComparer<TKey2>. The primary key keeps CmpKeyValue (net48's
// OrderBy primary has no user comparer; a null comparer operand falls back to
// Comparer<TKey2>.Default exactly like the OrderBy comparer twin). The
// comparer object is ROOTED across the sort (a Compare may allocate -> the GC
// compacts and moves it); the composite takes (src, k1Sel, k2Sel, cmp) plus the
// two desc + two key-family literals.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_then_by_key_val_i4_cmp(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, Il2CppObject* cmp,
                                                int32_t k1desc, int32_t k2desc,
                                                int32_t k1fam, int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int32_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        return SortInt32TwoKeysFamDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                           k1fam, k2fam);
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    return SortInt32TwoKeysCmpDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                       k1fam, k2fam, c);
}

Il2CppArray* il2cpp_linq_then_by_key_val_i8_cmp(Il2CppArray* src, Il2CppObject* k1Sel,
                                                Il2CppObject* k2Sel, Il2CppObject* cmp,
                                                int32_t k1desc, int32_t k2desc,
                                                int32_t k1fam, int32_t k2fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    RequireValueLinqArray(s, sizeof(int64_t));
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    Il2CppObject* c = cmp;
    if (c == nullptr)
    {
        return SortInt64TwoKeysFamDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                           k1fam, k2fam);
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&c));
    return SortInt64TwoKeysCmpDispatch(s, d1, d2, k1desc != 0, k2desc != 0,
                                       k1fam, k2fam, c);
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

//------------------------------------------------------------------------------
// impl-vtsel: the VALUE-STRUCT ELEMENT-SOURCE keyed sorts
// (net48 OrderBy<DateTime|TimeSpan|Guid|DateTimeOffset, TKey> over a List
// snapshot / an array).
//
// The element crosses to the key selector in its OWN by-value slot
// (Il2CppDateTime / Il2CppTimeSpan / Il2CppGuid / Il2CppDateTimeOffset) and
// the key returns in ITS OWN family slot. Reading such a cell through the
// int32 element reader (the pre-existing i4 surface) or calling the selector
// through a foreign ABI is a silent wrong value, which is what this surface
// removes.
//
// Key family codes (the transpiler's literals, shared with the ThenBy
// composite dispatch):
//   0 int32 | 1 int64 | 2 double | 3 float | 4 DateTime | 5 Guid |
//   6 TimeSpan | 7 DateTimeOffset
//
// Same GC contract as the other keyed sorts: elements are snapshotted into a
// native vector BEFORE any selector call, the delegate is rooted across the
// key phase, and the result is allocated only after the last selector call.
//------------------------------------------------------------------------------
namespace {

int32_t CmpTimeSpanKey(const runtime::Il2CppTimeSpan& a, const runtime::Il2CppTimeSpan& b)
{
    if (a.ticks < b.ticks) return -1;
    if (a.ticks > b.ticks) return 1;
    return 0;
}

int32_t CmpDtoKey(const runtime::Il2CppDateTimeOffset& a, const runtime::Il2CppDateTimeOffset& b)
{
    return runtime::icall_dtoffset_compare(a, b);
}

// impl-vtsel: the element CELL is read/written through the family accessor (a
// DateTime cell is the PACKED 8-byte image, NOT sizeof(Il2CppDateTime)).
template <typename TElem, typename TKey, typename InvokeKey, typename Cmp>
Il2CppArray* SortVtElemKeyCopy(Il2CppArray* src, Il2CppDelegate* keySel, bool desc,
                              int32_t elemSize, int32_t elemKind,
                              TElem (*readElem)(Il2CppArray*, int32_t),
                              void (*writeElem)(Il2CppArray*, int32_t, TElem),
                              InvokeKey invokeKey, Cmp cmp)
{
    const int32_t n = src->length;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&keySel));

    if (n > 0)
    {
        std::vector<TElem> elems(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) elems[i] = readElem(src, i);
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
        Il2CppArray* result = il2cpp_array_new_typed(elemSize, elemKind, n);
        for (int32_t i = 0; i < n; ++i) writeElem(result, i, elems[order[i]]);
        return result;
    }
    return il2cpp_array_new_typed(elemSize, elemKind, 0);
}

// --- key readers: source element family -> every key family ---------------
template <typename Src>
int32_t InvokeKeyVtI4(Il2CppDelegate* d, Src v)
{
    typedef int32_t (*Fn)(Src);
    typedef int32_t (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
int64_t InvokeKeyVtI8(Il2CppDelegate* d, Src v)
{
    typedef int64_t (*Fn)(Src);
    typedef int64_t (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
double InvokeKeyVtD(Il2CppDelegate* d, Src v)
{
    typedef double (*Fn)(Src);
    typedef double (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
float InvokeKeyVtF(Il2CppDelegate* d, Src v)
{
    typedef float (*Fn)(Src);
    typedef float (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
runtime::Il2CppDateTime InvokeKeyVtDt(Il2CppDelegate* d, Src v)
{
    typedef runtime::Il2CppDateTime (*Fn)(Src);
    typedef runtime::Il2CppDateTime (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
Il2CppGuid InvokeKeyVtGuid(Il2CppDelegate* d, Src v)
{
    typedef Il2CppGuid (*Fn)(Src);
    typedef Il2CppGuid (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
runtime::Il2CppTimeSpan InvokeKeyVtTs(Il2CppDelegate* d, Src v)
{
    typedef runtime::Il2CppTimeSpan (*Fn)(Src);
    typedef runtime::Il2CppTimeSpan (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

template <typename Src>
runtime::Il2CppDateTimeOffset InvokeKeyVtDto(Il2CppDelegate* d, Src v)
{
    typedef runtime::Il2CppDateTimeOffset (*Fn)(Src);
    typedef runtime::Il2CppDateTimeOffset (*ThisFn)(Il2CppObject*, Src);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<Fn>(d->methodPtr)(v);
}

// impl-vtsel: the ELEMENT accessors of each value-struct family (the CELL
// image, never the in-memory struct -- a DateTime cell is the PACKED 8 bytes).
template <typename Src>
struct VtElemAccess
{
    static Src Read(Il2CppArray* a, int32_t i);
    static void Write(Il2CppArray* a, int32_t i, Src v);
};

template <>
struct VtElemAccess<runtime::Il2CppDateTime>
{
    static runtime::Il2CppDateTime Read(Il2CppArray* a, int32_t i)
    {
        return runtime::il2cpp_datetime_from_array_bits(
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)));
    }
    static void Write(Il2CppArray* a, int32_t i, runtime::Il2CppDateTime v)
    {
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)) =
            runtime::il2cpp_datetime_to_array_bits(v);
    }
};

template <>
struct VtElemAccess<runtime::Il2CppTimeSpan>
{
    static runtime::Il2CppTimeSpan Read(Il2CppArray* a, int32_t i)
    {
        return runtime::Il2CppTimeSpan(
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)));
    }
    static void Write(Il2CppArray* a, int32_t i, runtime::Il2CppTimeSpan v)
    {
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(a, i)) = v.ticks;
    }
};

template <>
struct VtElemAccess<Il2CppGuid>
{
    static Il2CppGuid Read(Il2CppArray* a, int32_t i)
    {
        Il2CppGuid g;
        std::memcpy(g.b, il2cpp_array_get_elem_ptr(a, i), sizeof(g.b));
        return g;
    }
    static void Write(Il2CppArray* a, int32_t i, Il2CppGuid v)
    {
        std::memcpy(il2cpp_array_get_elem_ptr(a, i), v.b, sizeof(v.b));
    }
};

template <>
struct VtElemAccess<runtime::Il2CppDateTimeOffset>
{
    static runtime::Il2CppDateTimeOffset Read(Il2CppArray* a, int32_t i)
    {
        runtime::Il2CppDateTimeOffset v;
        std::memcpy(&v, il2cpp_array_get_elem_ptr(a, i), sizeof(v));
        return v;
    }
    static void Write(Il2CppArray* a, int32_t i, runtime::Il2CppDateTimeOffset v)
    {
        std::memcpy(il2cpp_array_get_elem_ptr(a, i), &v, sizeof(v));
    }
};

template <typename Src>
Il2CppArray* SortVtElemKeyFam(Il2CppArray* src, Il2CppDelegate* d, bool desc,
                              int32_t elemSize, int32_t elemKind, int32_t keyFam)
{
    switch (keyFam)
    {
        case 0: return SortVtElemKeyCopy<Src, int32_t>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtI4<Src>, CmpKeyValue<int32_t>);
        case 1: return SortVtElemKeyCopy<Src, int64_t>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtI8<Src>, CmpKeyValue<int64_t>);
        case 2: return SortVtElemKeyCopy<Src, double>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtD<Src>, CmpKeyValue<double>);
        case 3: return SortVtElemKeyCopy<Src, float>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtF<Src>, CmpKeyValue<float>);
        case 4: return SortVtElemKeyCopy<Src, runtime::Il2CppDateTime>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtDt<Src>, CmpDateTimeKey);
        case 5: return SortVtElemKeyCopy<Src, Il2CppGuid>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtGuid<Src>, CmpGuidKey);
        case 6: return SortVtElemKeyCopy<Src, runtime::Il2CppTimeSpan>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtTs<Src>, CmpTimeSpanKey);
        case 7: return SortVtElemKeyCopy<Src, runtime::Il2CppDateTimeOffset>(
            src, d, desc, elemSize, elemKind, &VtElemAccess<Src>::Read,
            &VtElemAccess<Src>::Write, InvokeKeyVtDto<Src>, CmpDtoKey);
        default: break;
    }
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.OrderBy: unknown value-struct-source key family");
    return nullptr;
}
// The (elemSize, elemKind) CELL of a value-struct element family: DateTime and
// TimeSpan cells are 8 bytes (their element kinds), Guid and DateTimeOffset are
// 16-byte value cells without a primitive element kind.
void VtElemCellOf(int32_t srcKind, int32_t& elemSize, int32_t& elemKind)
{
    switch (srcKind)
    {
        case 13: elemSize = 8; elemKind = kElemKindDateTime; return;
        case 14: elemSize = 8; elemKind = kElemKindTimeSpan; return;
        case 15: elemSize = 16; elemKind = kElemKindGuid; return;
        case 16: elemSize = 16; elemKind = kElemKindNone; return;
        default:
            il2cpp_throw_not_supported_exception(
                "OrderBy over a value-struct source: unknown element family.");
    }
}

template <typename Src>
Il2CppArray* SortVtElemDispatch(Il2CppArray* src, Il2CppDelegate* d, bool desc,
                                int32_t srcKind, int32_t keyFam)
{
    int32_t elemSize = 0;
    int32_t elemKind = kElemKindNone;
    VtElemCellOf(srcKind, elemSize, elemKind);
    return SortVtElemKeyFam<Src>(src, d, desc, elemSize, elemKind, keyFam);
}

// b5-gaps section 51: the VALUE-STRUCT-source chained ThenBy. Like the decimal
// chain, a per-level key cell must hold every key family the source can
// produce -- the vt families are 0=int32, 1=int64, 2=double, 3=float,
// 4=DateTime, 5=Guid, 6=TimeSpan, 7=DateTimeOffset. The element is crossed BY
// VALUE (VtElemAccess<Src>) and the result is a fresh array of the SAME
// element family (a DateTime[]/TimeSpan[]/Guid[]/DateTimeOffset[]).
struct LkVtChainKey
{
    int64_t i;                            // families 0 / 1 (int32 / int64)
    double d;                             // family 2 (double)
    float f;                              // family 3 (float)
    runtime::Il2CppDateTime dt;           // family 4 (DateTime)
    Il2CppGuid guid;                      // family 5 (Guid)
    runtime::Il2CppTimeSpan ts;           // family 6 (TimeSpan)
    runtime::Il2CppDateTimeOffset dto;    // family 7 (DateTimeOffset)
};

template <typename Src>
void ReadLkVtChainKey(Src elem, int32_t fam, Il2CppDelegate* d, LkVtChainKey& out)
{
    switch (fam)
    {
    case 0: out.i = static_cast<int64_t>(InvokeKeyVtI4<Src>(d, elem)); break;
    case 1: out.i = InvokeKeyVtI8<Src>(d, elem); break;
    case 2: out.d = InvokeKeyVtD<Src>(d, elem); break;
    case 3: out.f = InvokeKeyVtF<Src>(d, elem); break;
    case 4: out.dt = InvokeKeyVtDt<Src>(d, elem); break;
    case 5: out.guid = InvokeKeyVtGuid<Src>(d, elem); break;
    case 6: out.ts = InvokeKeyVtTs<Src>(d, elem); break;
    case 7: out.dto = InvokeKeyVtDto<Src>(d, elem); break;
    default:
        il2cpp_throw_not_supported_exception(
            "Chained ThenBy: unknown value-struct-source key family.");
        break;
    }
}

int32_t CmpLkVtChainKey(const LkVtChainKey& a, const LkVtChainKey& b, int32_t fam)
{
    switch (fam)
    {
    case 0: return CmpKeyValue<int32_t>(static_cast<int32_t>(a.i), static_cast<int32_t>(b.i));
    case 1: return CmpKeyValue<int64_t>(a.i, b.i);
    case 2: return CmpKeyValue<double>(a.d, b.d);
    case 3: return CmpKeyValue<float>(a.f, b.f);
    case 4: return CmpDateTimeKey(a.dt, b.dt);
    case 5: return CmpGuidKey(a.guid, b.guid);
    case 6: return CmpTimeSpanKey(a.ts, b.ts);
    case 7: return CmpDtoKey(a.dto, b.dto);
    default: return 0;
    }
}

// The value-struct-source chain level-count validator: families 0..7.
int32_t LkVtChainLevelCount(const int32_t* fams, int32_t maxLevels)
{
    int32_t used = 0;
    for (int32_t l = 0; l < maxLevels; ++l)
    {
        if (fams[l] < 0)
        {
            break;
        }
        if (fams[l] > 7)
        {
            il2cpp_throw_not_supported_exception(
                "Chained ThenBy: only the value-struct-source key families (int32/int64/"
                "double/float/DateTime/Guid/TimeSpan/DateTimeOffset) are wired.");
        }
        ++used;
    }
    return used;
}

template <typename Src>
Il2CppArray* SortVtChainCopyT(Il2CppArray* src, int32_t nkeys, Il2CppDelegate** sels,
                              const int32_t* descs, const int32_t* fams,
                              int32_t elemSize, int32_t elemKind)
{
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        roots.Add(reinterpret_cast<void**>(&sels[l]));
    }

    if (n <= 0)
    {
        return il2cpp_array_new_typed(elemSize, elemKind, 0);
    }

    std::vector<Src> elems(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) elems[i] = VtElemAccess<Src>::Read(src, i);

    std::vector<LkVtChainKey> keys(static_cast<size_t>(n) * static_cast<size_t>(nkeys));
    for (int32_t l = 0; l < nkeys; ++l)
    {
        LkVtChainKey* level = keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
        for (int32_t i = 0; i < n; ++i)
        {
            ReadLkVtChainKey(elems[i], fams[l], sels[l], level[i]);
        }
    }

    std::vector<int32_t> order(static_cast<size_t>(n));
    for (int32_t i = 0; i < n; ++i) order[i] = i;
    std::vector<int32_t> scratch(static_cast<size_t>(n));
    MergeSortStable(order.data(), scratch.data(), 0, n,
                    [&](int32_t a, int32_t b) -> int32_t {
                        for (int32_t l = 0; l < nkeys; ++l)
                        {
                            const LkVtChainKey* level =
                                keys.data() + static_cast<size_t>(l) * static_cast<size_t>(n);
                            const int32_t c = CmpLkVtChainKey(level[a], level[b], fams[l]);
                            if (c != 0)
                            {
                                return descs[l] != 0 ? -c : c;
                            }
                        }
                        return 0;
                    });

    Il2CppArray* result = il2cpp_array_new_typed(elemSize, elemKind, n);
    for (int32_t i = 0; i < n; ++i)
    {
        VtElemAccess<Src>::Write(result, i, elems[order[i]]);
    }
    return result;
}

Il2CppArray* SortVtChainDispatch(Il2CppArray* src, int32_t nkeys, Il2CppDelegate** sels,
                                 const int32_t* descs, const int32_t* fams,
                                 int32_t srcKind)
{
    int32_t elemSize = 0;
    int32_t elemKind = kElemKindNone;
    VtElemCellOf(srcKind, elemSize, elemKind);
    switch (srcKind)
    {
        case 13: return SortVtChainCopyT<runtime::Il2CppDateTime>(
            src, nkeys, sels, descs, fams, elemSize, elemKind);
        case 14: return SortVtChainCopyT<runtime::Il2CppTimeSpan>(
            src, nkeys, sels, descs, fams, elemSize, elemKind);
        case 15: return SortVtChainCopyT<Il2CppGuid>(
            src, nkeys, sels, descs, fams, elemSize, elemKind);
        case 16: return SortVtChainCopyT<runtime::Il2CppDateTimeOffset>(
            src, nkeys, sels, descs, fams, elemSize, elemKind);
        default:
            il2cpp_throw_not_supported_exception(
                "Chained ThenBy over a value-struct source: unknown element family.");
            return nullptr;
    }
}

} // namespace

Il2CppArray* il2cpp_linq_order_by_key_vt(Il2CppArray* src, Il2CppObject* keySel,
                                         int32_t desc, int32_t srcKind, int32_t keyFam)
{
    Il2CppArray* s = RequireLinqArray(src);
    Il2CppDelegate* d = RequireKeySelector(keySel);
    if (s->isRefElem) RequireRefLinqArray(s);
    switch (srcKind)
    {
        case 13: return SortVtElemDispatch<runtime::Il2CppDateTime>(
            s, d, desc != 0, srcKind, keyFam);
        case 14: return SortVtElemDispatch<runtime::Il2CppTimeSpan>(
            s, d, desc != 0, srcKind, keyFam);
        case 15: return SortVtElemDispatch<Il2CppGuid>(s, d, desc != 0, srcKind, keyFam);
        case 16: return SortVtElemDispatch<runtime::Il2CppDateTimeOffset>(
            s, d, desc != 0, srcKind, keyFam);
        default:
            il2cpp_throw_not_supported_exception(
                "OrderBy over a value-struct source: unknown element family.");
            return nullptr;
    }
}

//------------------------------------------------------------------------------
// impl-vtsel: ThenBy / ThenByDescending over a value-struct element source --
// the FUSED composite (net48 EnumerableSorter chain). Both keys are cached
// EXACTLY ONCE; the primary key decides first and the secondary only breaks
// ties. Both key families travel as literals, so every (TKey1, TKey2) pair is
// served by the same dispatch.
//------------------------------------------------------------------------------
namespace {

template <typename TElem, typename TK1, typename TK2, typename Invoke1,
          typename Invoke2, typename Cmp1, typename Cmp2>
Il2CppArray* SortVtElemTwoKeysCopy(Il2CppArray* src, Il2CppDelegate* d1,
                                   Il2CppDelegate* d2, bool k1Desc, bool k2Desc,
                                   int32_t elemSize, int32_t elemKind,
                                   TElem (*readElem)(Il2CppArray*, int32_t),
                                   void (*writeElem)(Il2CppArray*, int32_t, TElem),
                                   Invoke1 invoke1, Invoke2 invoke2,
                                   Cmp1 cmp1, Cmp2 cmp2)
{
    const int32_t n = src->length;
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&d1));
    roots.Add(reinterpret_cast<void**>(&d2));

    if (n > 0)
    {
        std::vector<TElem> elems(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) elems[i] = readElem(src, i);
        std::vector<TK1> k1(static_cast<size_t>(n));
        std::vector<TK2> k2(static_cast<size_t>(n));
        std::vector<int32_t> order(static_cast<size_t>(n));
        for (int32_t i = 0; i < n; ++i) order[i] = i;
        for (int32_t i = 0; i < n; ++i) k1[i] = invoke1(d1, elems[i]);
        for (int32_t i = 0; i < n; ++i) k2[i] = invoke2(d2, elems[i]);
        std::vector<int32_t> scratch(static_cast<size_t>(n));
        MergeSortStable(order.data(), scratch.data(), 0, n,
                        [&](int32_t a, int32_t b) -> int32_t {
                            const int32_t c1 = cmp1(k1[a], k1[b]);
                            if (c1 != 0) return k1Desc ? -c1 : c1;
                            const int32_t c2 = cmp2(k2[a], k2[b]);
                            return k2Desc ? -c2 : c2;
                        });
        Il2CppArray* result = il2cpp_array_new_typed(elemSize, elemKind, n);
        for (int32_t i = 0; i < n; ++i) writeElem(result, i, elems[order[i]]);
        return result;
    }
    return il2cpp_array_new_typed(elemSize, elemKind, 0);
}

// One key family, used as the PRIMARY key of the fused composite.
template <typename Src, typename TK1, typename Invoke1, typename Cmp1>
Il2CppArray* SortVtElemTwoKeysSecondary(Il2CppArray* src, Il2CppDelegate* d1,
                                        Il2CppDelegate* d2, bool k1Desc, bool k2Desc,
                                        int32_t elemSize, int32_t elemKind,
                                        Src (*readElem)(Il2CppArray*, int32_t),
                                        void (*writeElem)(Il2CppArray*, int32_t, Src),
                                        Invoke1 invoke1, Cmp1 cmp1, int32_t k2Fam)
{
    switch (k2Fam)
    {
        case 0: return SortVtElemTwoKeysCopy<Src, TK1, int32_t>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtI4<Src>, cmp1, CmpKeyValue<int32_t>);
        case 1: return SortVtElemTwoKeysCopy<Src, TK1, int64_t>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtI8<Src>, cmp1, CmpKeyValue<int64_t>);
        case 2: return SortVtElemTwoKeysCopy<Src, TK1, double>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtD<Src>, cmp1, CmpKeyValue<double>);
        case 3: return SortVtElemTwoKeysCopy<Src, TK1, float>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtF<Src>, cmp1, CmpKeyValue<float>);
        case 4: return SortVtElemTwoKeysCopy<Src, TK1, runtime::Il2CppDateTime>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtDt<Src>, cmp1, CmpDateTimeKey);
        case 5: return SortVtElemTwoKeysCopy<Src, TK1, Il2CppGuid>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtGuid<Src>, cmp1, CmpGuidKey);
        case 6: return SortVtElemTwoKeysCopy<Src, TK1, runtime::Il2CppTimeSpan>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtTs<Src>, cmp1, CmpTimeSpanKey);
        case 7: return SortVtElemTwoKeysCopy<Src, TK1, runtime::Il2CppDateTimeOffset>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, invoke1,
            InvokeKeyVtDto<Src>, cmp1, CmpDtoKey);
        default: break;
    }
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown secondary key family over a value-struct source");
    return nullptr;
}

template <typename Src>
Il2CppArray* SortVtElemTwoKeysFam(Il2CppArray* src, Il2CppDelegate* d1,
                                  Il2CppDelegate* d2, bool k1Desc, bool k2Desc,
                                  int32_t srcKind, int32_t k1Fam, int32_t k2Fam)
{
    int32_t elemSize = 0;
    int32_t elemKind = kElemKindNone;
    VtElemCellOf(srcKind, elemSize, elemKind);
    Src (*readElem)(Il2CppArray*, int32_t) = &VtElemAccess<Src>::Read;
    void (*writeElem)(Il2CppArray*, int32_t, Src) = &VtElemAccess<Src>::Write;
    switch (k1Fam)
    {
        case 0: return SortVtElemTwoKeysSecondary<Src, int32_t>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtI4<Src>,
            CmpKeyValue<int32_t>, k2Fam);
        case 1: return SortVtElemTwoKeysSecondary<Src, int64_t>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtI8<Src>,
            CmpKeyValue<int64_t>, k2Fam);
        case 2: return SortVtElemTwoKeysSecondary<Src, double>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtD<Src>,
            CmpKeyValue<double>, k2Fam);
        case 3: return SortVtElemTwoKeysSecondary<Src, float>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtF<Src>,
            CmpKeyValue<float>, k2Fam);
        case 4: return SortVtElemTwoKeysSecondary<Src, runtime::Il2CppDateTime>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtDt<Src>,
            CmpDateTimeKey, k2Fam);
        case 5: return SortVtElemTwoKeysSecondary<Src, Il2CppGuid>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtGuid<Src>,
            CmpGuidKey, k2Fam);
        case 6: return SortVtElemTwoKeysSecondary<Src, runtime::Il2CppTimeSpan>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtTs<Src>,
            CmpTimeSpanKey, k2Fam);
        case 7: return SortVtElemTwoKeysSecondary<Src, runtime::Il2CppDateTimeOffset>(
            src, d1, d2, k1Desc, k2Desc, elemSize, elemKind, readElem, writeElem, InvokeKeyVtDto<Src>,
            CmpDtoKey, k2Fam);
        default: break;
    }
    il2cpp_throw_invalid_operation_exception(
        "Enumerable.ThenBy: unknown primary key family over a value-struct source");
    return nullptr;
}

} // namespace

// b5-gaps section 51: the VALUE-STRUCT-source chained ThenBy. The source
// element family is the appended srcKind literal (13 DateTime / 14 TimeSpan /
// 15 Guid / 16 DateTimeOffset); the per-level key families are 0..7
// (LinqVtKeyFamilyCode). Result is a fresh array of the SOURCE element family.
Il2CppArray* il2cpp_linq_then_by_chain_vt(Il2CppArray* src,
                                         Il2CppObject* k1, Il2CppObject* k2,
                                         Il2CppObject* k3, Il2CppObject* k4,
                                         int32_t d1, int32_t d2, int32_t d3, int32_t d4,
                                         int32_t f1, int32_t f2, int32_t f3, int32_t f4,
                                         int32_t srcKind)
{
    Il2CppArray* s = RequireLinqArray(src);
    int32_t fams[4] = { f1, f2, f3, f4 };
    const int32_t nkeys = LkVtChainLevelCount(fams, 4);
    Il2CppObject* raw[4] = { k1, k2, k3, k4 };
    Il2CppDelegate* sels[4] = { nullptr, nullptr, nullptr, nullptr };
    for (int32_t l = 0; l < nkeys; ++l)
    {
        sels[l] = RequireKeySelector(raw[l]);
    }
    const int32_t descs[4] = { d1, d2, d3, d4 };
    return SortVtChainDispatch(s, nkeys, sels, descs, fams, srcKind);
}

Il2CppArray* il2cpp_linq_then_by_key_vt(Il2CppArray* src, Il2CppObject* k1Sel,
                                        Il2CppObject* k2Sel, int32_t k1Desc,
                                        int32_t k2Desc, int32_t srcKind,
                                        int32_t k1Fam, int32_t k2Fam)
{
    Il2CppArray* s = RequireLinqArray(src);
    Il2CppDelegate* d1 = RequireKeySelector(k1Sel);
    Il2CppDelegate* d2 = RequireKeySelector(k2Sel);
    switch (srcKind)
    {
        case 13: return SortVtElemTwoKeysFam<runtime::Il2CppDateTime>(
            s, d1, d2, k1Desc != 0, k2Desc != 0, srcKind, k1Fam, k2Fam);
        case 14: return SortVtElemTwoKeysFam<runtime::Il2CppTimeSpan>(
            s, d1, d2, k1Desc != 0, k2Desc != 0, srcKind, k1Fam, k2Fam);
        case 15: return SortVtElemTwoKeysFam<Il2CppGuid>(
            s, d1, d2, k1Desc != 0, k2Desc != 0, srcKind, k1Fam, k2Fam);
        case 16: return SortVtElemTwoKeysFam<runtime::Il2CppDateTimeOffset>(
            s, d1, d2, k1Desc != 0, k2Desc != 0, srcKind, k1Fam, k2Fam);
        default:
            il2cpp_throw_not_supported_exception(
                "ThenBy over a value-struct source: unknown element family.");
            return nullptr;
    }
}

} // namespace il2cpp
