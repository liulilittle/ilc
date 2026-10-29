//------------------------------------------------------------------------------
// linq_list.cpp -- System.Linq over a List<T> receiver (nb15 B5 FIX, D-2).
//
// See linq_list.h. Every entry:
//   1. validates the receiver (null -> ArgumentNullException);
//   2. materializes the list's exact-size element snapshot through the list
//      domain's own ToArray accessor (il2cpp_list_i4_to_array /
//      il2cpp_list_obj_to_array_<kind>) -- an independent copy, so the list is
//      never mutated and no list internals are read directly;
//   3. walks the snapshot at the REAL element width and calls the delegate
//      with the widened value (narrow integral cells widen into the int32
//      slot, float widens to double -- the generated-code ABI).
//
// GC SAFETY: the list and the snapshot are rooted through RootScope slots
// while a callback runs, and no managed address is cached across a callback.
//------------------------------------------------------------------------------

#include "linq_list.h"

#include "linq.h"
#include "linq_family.h" // LinqRequireCell / LinqCellReadDt/Ts/Guid (value-cell folds)
#include "linq_map.h"
#include "linq_sort.h"

#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"
#include "il2cpp_runtime.h"
#include "il2cpp_value_semantics.h"
#include "il2cpp_valuetuple.h" // il2cpp_tuple_box / il2cpp_tuple_box_size (tuple Select results)
#include "listi4.h"
#include "listobj.h"

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <new>
#include <vector>

namespace il2cpp {

namespace {

//------------------------------------------------------------------------------
// Validation / rooting
//------------------------------------------------------------------------------
Il2CppObject* RequireList(Il2CppObject* list)
{
    if (list == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
        return nullptr; // unreachable (the factory throws)
    }
    return list;
}

// The callback may allocate, so every managed reference the entry holds across
// a callback must be a registered static root. The slot COUNT is dynamic: an
// entry can legitimately need more than a handful (except() holds list, source
// snapshot, second, result and the exact-size trim = 5). A fixed array with an
// overflow check turned the common "result got shorter" path of
// List<T>.Except into a silent process abort (exit 3, no message, no managed
// exception). The storage grows on demand and an allocation failure surfaces as
// a real managed exception, never a silent exit.
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
    std::vector<void**> m_slots;
};

//------------------------------------------------------------------------------
// Snapshot
//------------------------------------------------------------------------------
Il2CppArray* SnapshotOf(Il2CppObject* list, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I4:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_i4_to_array(list));
        case IL2CPP_LIST_KIND_REF:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array(list));
        case IL2CPP_LIST_KIND_I8:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_long(list));
        case IL2CPP_LIST_KIND_F:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_float(list));
        case IL2CPP_LIST_KIND_D:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_double(list));
        case IL2CPP_LIST_KIND_SBYTE:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_sbyte(list));
        case IL2CPP_LIST_KIND_BYTE:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_byte(list));
        case IL2CPP_LIST_KIND_BOOL:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_bool(list));
        case IL2CPP_LIST_KIND_SHORT:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_short(list));
        case IL2CPP_LIST_KIND_USHORT:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_ushort(list));
        case IL2CPP_LIST_KIND_CHAR:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_char(list));
        case IL2CPP_LIST_KIND_UINT:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_uint(list));
        case IL2CPP_LIST_KIND_DEC:
            return reinterpret_cast<Il2CppArray*>(il2cpp_list_obj_to_array_dec(list));
        default:
            il2cpp_throw_not_supported_exception("Unsupported List element kind for a LINQ operator.");
            return nullptr;
    }
}

//------------------------------------------------------------------------------
// Element reads at the real cell width
//------------------------------------------------------------------------------
int32_t ReadI4(Il2CppArray* arr, int32_t i, int32_t kind)
{
    char* p = il2cpp_array_get_elem_ptr(arr, i);
    switch (kind)
    {
        case IL2CPP_LIST_KIND_SBYTE:  return static_cast<int32_t>(*reinterpret_cast<int8_t*>(p));
        case IL2CPP_LIST_KIND_BYTE:   return static_cast<int32_t>(*reinterpret_cast<uint8_t*>(p));
        case IL2CPP_LIST_KIND_BOOL:   return *reinterpret_cast<uint8_t*>(p) != 0 ? 1 : 0;
        case IL2CPP_LIST_KIND_SHORT:  return static_cast<int32_t>(*reinterpret_cast<int16_t*>(p));
        case IL2CPP_LIST_KIND_USHORT: return static_cast<int32_t>(*reinterpret_cast<uint16_t*>(p));
        case IL2CPP_LIST_KIND_CHAR:   return static_cast<int32_t>(*reinterpret_cast<char16_t*>(p));
        case IL2CPP_LIST_KIND_UINT:   return static_cast<int32_t>(*reinterpret_cast<uint32_t*>(p));
        default:                      return *reinterpret_cast<int32_t*>(p);
    }
}

int64_t ReadI8(Il2CppArray* arr, int32_t i)
{
    return *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(arr, i));
}

double ReadD(Il2CppArray* arr, int32_t i)
{
    return il2cpp_array_get_elem_float_as_double(arr, i);
}

Il2CppObject* ReadRef(Il2CppArray* arr, int32_t i)
{
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, i));
}

// Element copy (same family) -- the snapshot is exact storage, so a byte copy
// is the family's value copy.
void CopyElem(Il2CppArray* dst, int32_t di, Il2CppArray* src, int32_t si)
{
    std::memcpy(il2cpp_array_get_elem_ptr(dst, di), il2cpp_array_get_elem_ptr(src, si),
                static_cast<size_t>(src->elemSize));
}

// The il2cpp_array_new_typed element kind for a LIST element family (1 int32,
// 2 int64, 3 single, 4 double, 5 uint32). Without it the materialized array
// carries elemKind 0, so an 8-byte cell is ambiguous and the non-generic array
// scans (string.Join, the element-kind Array gates) read a double as int64 --
// printing the raw IEEE754 bit pattern.
// The NARROW families (sbyte/byte/bool -> 1 byte, short/ushort/char -> 2 bytes)
// keep kind 0: their elemSize alone is NOT sufficient to tell the families
// apart (1 byte is sbyte, byte OR bool; 2 bytes is short, ushort OR char), so
// the exact family travels as the element-kind LITERAL the transpiler appends.
// Consequence registered in docs/audit/b5-gaps.md: a narrow-family result array
// has elemKind 0, and string.Join over it hits the PRE-EXISTING shared
// formatter limitation (stringext2.cpp ValueArrayElement has no signed/unsigned
// narrow or bool entry).
int32_t ValueElemKindFor(int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I4:   return 1;
        case IL2CPP_LIST_KIND_I8:   return 2;
        case IL2CPP_LIST_KIND_F:    return 3;
        case IL2CPP_LIST_KIND_D:    return 4;
        case IL2CPP_LIST_KIND_UINT: return 5;
        default:                    return 0; // narrow families
    }
}

// Materialize an output array with the SOURCE array's element layout AND the
// source family's element kind. A reference array keeps the plain allocation
// (it needs elemClass/isRefElem, not a primitive kind).
Il2CppArray* AllocElemArray(Il2CppArray* proto, int32_t kind, int32_t length)
{
    if (proto->isRefElem)
        return il2cpp_array_new(nullptr, proto->elemSize, true, length);
    return il2cpp_array_new_typed(proto->elemSize, ValueElemKindFor(kind), length);
}

//------------------------------------------------------------------------------
// Delegate ABI (generated-code leaf signatures)
//------------------------------------------------------------------------------
typedef bool (*PredI4Fn)(int32_t);
typedef bool (*PredI4ThisFn)(Il2CppObject*, int32_t);
typedef bool (*PredI8Fn)(int64_t);
typedef bool (*PredI8ThisFn)(Il2CppObject*, int64_t);
typedef bool (*PredDFn)(double);
typedef bool (*PredDThisFn)(Il2CppObject*, double);
typedef bool (*PredRefFn)(Il2CppObject*);
typedef bool (*PredRefThisFn)(Il2CppObject*, Il2CppObject*);

typedef int32_t (*SelI4I4Fn)(int32_t);
typedef int32_t (*SelI4I4ThisFn)(Il2CppObject*, int32_t);
typedef int64_t (*SelI4I8Fn)(int32_t);
typedef int64_t (*SelI4I8ThisFn)(Il2CppObject*, int32_t);
typedef double (*SelI4DFn)(int32_t);
typedef double (*SelI4DThisFn)(Il2CppObject*, int32_t);
typedef float (*SelI4FFn)(int32_t);
typedef float (*SelI4FThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelI4RefFn)(int32_t);
typedef Il2CppObject* (*SelI4RefThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelRefRefFn)(Il2CppObject*);
typedef Il2CppObject* (*SelRefRefThisFn)(Il2CppObject*, Il2CppObject*);

// Selector ABIs for the WIDE source families (the element crosses in its OWN
// slot: int64 for an i8 source, double for a float/double source) -- the
// selector-aggregate projections.
typedef int32_t (*SelI8I4Fn)(int64_t);
typedef int32_t (*SelI8I4ThisFn)(Il2CppObject*, int64_t);
typedef int64_t (*SelI8I8Fn)(int64_t);
typedef int64_t (*SelI8I8ThisFn)(Il2CppObject*, int64_t);
typedef double (*SelI8DFn)(int64_t);
typedef double (*SelI8DThisFn)(Il2CppObject*, int64_t);
typedef float (*SelI8FFn)(int64_t);
typedef float (*SelI8FThisFn)(Il2CppObject*, int64_t);
typedef int32_t (*SelDI4Fn)(double);
typedef int32_t (*SelDI4ThisFn)(Il2CppObject*, double);
typedef int64_t (*SelDI8Fn)(double);
typedef int64_t (*SelDI8ThisFn)(Il2CppObject*, double);
typedef double (*SelDDFn)(double);
typedef double (*SelDDThisFn)(Il2CppObject*, double);
typedef float (*SelDFFn)(double);
typedef float (*SelDFThisFn)(Il2CppObject*, double);

// Index-predicate ABI (net48 Func<TSource,int,bool>): the predicate receives
// the element AND its zero-based source index.
typedef bool (*PredI4IdxFn)(int32_t, int32_t);
typedef bool (*PredI4IdxThisFn)(Il2CppObject*, int32_t, int32_t);
typedef bool (*PredI8IdxFn)(int64_t, int32_t);
typedef bool (*PredI8IdxThisFn)(Il2CppObject*, int64_t, int32_t);
typedef bool (*PredDIdxFn)(double, int32_t);
typedef bool (*PredDIdxThisFn)(Il2CppObject*, double, int32_t);
typedef bool (*PredRefIdxFn)(Il2CppObject*, int32_t);
typedef bool (*PredRefIdxThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

// Index-selector ABI (net48 Func<TSource,int,TResult>): the selector receives
// the element AND its zero-based source index.
typedef int32_t (*SelI4IdxI4Fn)(int32_t, int32_t);
typedef int32_t (*SelI4IdxI4ThisFn)(Il2CppObject*, int32_t, int32_t);
typedef int64_t (*SelI4IdxI8Fn)(int32_t, int32_t);
typedef int64_t (*SelI4IdxI8ThisFn)(Il2CppObject*, int32_t, int32_t);
typedef double (*SelI4IdxDFn)(int32_t, int32_t);
typedef double (*SelI4IdxDThisFn)(Il2CppObject*, int32_t, int32_t);
typedef float (*SelI4IdxFFn)(int32_t, int32_t);
typedef float (*SelI4IdxFThisFn)(Il2CppObject*, int32_t, int32_t);
typedef Il2CppObject* (*SelI4IdxRefFn)(int32_t, int32_t);
typedef Il2CppObject* (*SelI4IdxRefThisFn)(Il2CppObject*, int32_t, int32_t);
typedef Il2CppObject* (*SelRefIdxRefFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelRefIdxRefThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

// Index-selector ABIs for the WIDE source families (the element crosses in its
// OWN slot: int64 for an i8 source, double for a float/double source).
typedef int32_t (*SelI8IdxI4Fn)(int64_t, int32_t);
typedef int32_t (*SelI8IdxI4ThisFn)(Il2CppObject*, int64_t, int32_t);
typedef int64_t (*SelI8IdxI8Fn)(int64_t, int32_t);
typedef int64_t (*SelI8IdxI8ThisFn)(Il2CppObject*, int64_t, int32_t);
typedef double (*SelI8IdxDFn)(int64_t, int32_t);
typedef double (*SelI8IdxDThisFn)(Il2CppObject*, int64_t, int32_t);
typedef float (*SelI8IdxFFn)(int64_t, int32_t);
typedef float (*SelI8IdxFThisFn)(Il2CppObject*, int64_t, int32_t);
typedef Il2CppObject* (*SelI8IdxRefFn)(int64_t, int32_t);
typedef Il2CppObject* (*SelI8IdxRefThisFn)(Il2CppObject*, int64_t, int32_t);

typedef int32_t (*SelDIdxI4Fn)(double, int32_t);
typedef int32_t (*SelDIdxI4ThisFn)(Il2CppObject*, double, int32_t);
typedef int64_t (*SelDIdxI8Fn)(double, int32_t);
typedef int64_t (*SelDIdxI8ThisFn)(Il2CppObject*, double, int32_t);
typedef double (*SelDIdxDFn)(double, int32_t);
typedef double (*SelDIdxDThisFn)(Il2CppObject*, double, int32_t);
typedef float (*SelDIdxFFn)(double, int32_t);
typedef float (*SelDIdxFThisFn)(Il2CppObject*, double, int32_t);
typedef Il2CppObject* (*SelDIdxRefFn)(double, int32_t);
typedef Il2CppObject* (*SelDIdxRefThisFn)(Il2CppObject*, double, int32_t);
// (The Zip reference-selector typedefs were removed together with the
// reference Zip branch: nothing in this file may call a selector through the
// reference ABI any more -- see il2cpp_linq_zip_family.)

bool InvokePredI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredI4Fn>(d->methodPtr)(v);
}

bool InvokePredI8(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI8ThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredI8Fn>(d->methodPtr)(v);
}

bool InvokePredD(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredDFn>(d->methodPtr)(v);
}

bool InvokePredRef(Il2CppDelegate* d, Il2CppObject* v)
{
    return d->target != nullptr
        ? reinterpret_cast<PredRefThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<PredRefFn>(d->methodPtr)(v);
}

bool InvokePredI4Idx(Il2CppDelegate* d, int32_t v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI4IdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredI4IdxFn>(d->methodPtr)(v, index);
}

bool InvokePredI8Idx(Il2CppDelegate* d, int64_t v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredI8IdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredI8IdxFn>(d->methodPtr)(v, index);
}

bool InvokePredDIdx(Il2CppDelegate* d, double v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredDIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredDIdxFn>(d->methodPtr)(v, index);
}

bool InvokePredRefIdx(Il2CppDelegate* d, Il2CppObject* v, int32_t index)
{
    return d->target != nullptr
        ? reinterpret_cast<PredRefIdxThisFn>(d->methodPtr)(d->target, v, index)
        : reinterpret_cast<PredRefIdxFn>(d->methodPtr)(v, index);
}

Il2CppDelegate* RequireCallback(Il2CppObject* cb, const char* paramName)
{
    if (cb == nullptr)
    {
        il2cpp_throw_argument_null_exception(paramName);
        return nullptr; // unreachable
    }
    const char* name = cb->klass != nullptr ? cb->klass->name : nullptr;
    if (name == nullptr || std::strcmp(name, "System.MulticastDelegate") == 0)
    {
        il2cpp_throw_invalid_operation_exception(
            "A multicast delegate cannot be used as a LINQ callback.");
        return nullptr;
    }
    if (std::strcmp(name, "System.Delegate") != 0)
    {
        il2cpp_throw_argument_exception("Object is not a delegate.");
        return nullptr;
    }
    return reinterpret_cast<Il2CppDelegate*>(cb);
}

// Family predicate dispatch on the LIST element kind.
bool PredicateOn(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_REF: return InvokePredRef(d, ReadRef(snap, i));
        case IL2CPP_LIST_KIND_I8:  return InvokePredI8(d, ReadI8(snap, i));
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:   return InvokePredD(d, ReadD(snap, i));
        default:                   return InvokePredI4(d, ReadI4(snap, i, kind));
    }
}

// The 2-arg index PREDICATE dispatch (net48 Func<TSource,int,bool>): the
// element is widened exactly like the 1-arg form, and its zero-based source
// index is passed as the second argument.
bool PredicateOnIdx(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_REF: return InvokePredRefIdx(d, ReadRef(snap, i), i);
        case IL2CPP_LIST_KIND_I8:  return InvokePredI8Idx(d, ReadI8(snap, i), i);
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:   return InvokePredDIdx(d, ReadD(snap, i), i);
        default:                   return InvokePredI4Idx(d, ReadI4(snap, i, kind), i);
    }
}

// Element equality (net48 EqualityComparer<T>.Default): the reference family
// uses the runtime's value-equality authority; value cells compare by width.
bool ElemEquals(Il2CppArray* a, int32_t i, Il2CppArray* b, int32_t j, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_REF:
            return runtime::icall_object_equals(ReadRef(a, i), ReadRef(b, j));
        case IL2CPP_LIST_KIND_I8:
            return ReadI8(a, i) == ReadI8(b, j);
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
            return ReadD(a, i) == ReadD(b, j);
        default:
            return ReadI4(a, i, kind) == ReadI4(b, j, kind);
    }
}

} // namespace

//------------------------------------------------------------------------------
// Count / ToArray / ToList
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_count(Il2CppObject* list)
{
    RequireList(list);
    return il2cpp_list_obj_get_count(list);
}

Il2CppArray* il2cpp_linq_list_snapshot(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    return SnapshotOf(list, kind);
}

Il2CppArray* il2cpp_linq_list_to_array(Il2CppObject* list, int32_t kind)
{
    return il2cpp_linq_list_snapshot(list, kind);
}

Il2CppObject* il2cpp_linq_list_to_list(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4)
    {
        // List<int> -> a fresh List<int> with the same elements.
        Il2CppObject* out = il2cpp_object_new(il2cpp_list_i4_class());
        roots.Add(reinterpret_cast<void**>(&out));
        il2cpp_list_i4_create_with_capacity(out, snap->length);
        for (int32_t i = 0; i < snap->length; ++i)
        {
            il2cpp_list_i4_add(out, *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(snap, i)));
        }
        return out;
    }
    // Reference family -> a fresh List<object>-domain list holding the same
    // references (the listobj storage is the shared reference carrier).
    Il2CppObject* out = il2cpp_list_obj_create_with_capacity(snap->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < snap->length; ++i)
    {
        il2cpp_list_obj_add(out, ReadRef(snap, i));
    }
    return out;
}

int64_t il2cpp_linq_list_long_count(Il2CppObject* list)
{
    RequireList(list);
    return static_cast<int64_t>(il2cpp_list_obj_get_count(list));
}

//------------------------------------------------------------------------------
// Source-only aggregates (Sum/Min/Max/Average) over a List<T>. The result
// family follows the list kind: i4 (int32 -- the narrow families widen into the
// int32 slot), i8 (int64), f (float), d (double). Average is double for the
// integer/double families and float for the float family (net48
// Enumerable.Average overloads). Sum is CHECKED for the integer families
// (net48), Min/Max/Average throw InvalidOperationException on an empty
// sequence.
// GC SAFETY: no callback runs, so the list and the snapshot are rooted across
// the single snapshot allocation.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_sum_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    int32_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = ReadI4(snap, i, kind);
        if ((elem > 0 && sum > INT32_MAX - elem) || (elem < 0 && sum < INT32_MIN - elem))
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        sum += elem;
    }
    return sum;
}

int32_t il2cpp_linq_list_min_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int32_t best = ReadI4(snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int32_t elem = ReadI4(snap, i, kind);
        if (elem < best) best = elem;
    }
    return best;
}

int32_t il2cpp_linq_list_max_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int32_t best = ReadI4(snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int32_t elem = ReadI4(snap, i, kind);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadI4(snap, i, kind);
    return static_cast<double>(sum) / static_cast<double>(n);
}

int64_t il2cpp_linq_list_sum_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = ReadI8(snap, i);
        if ((elem > 0 && sum > INT64_MAX - elem) || (elem < 0 && sum < INT64_MIN - elem))
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        sum += elem;
    }
    return sum;
}

int64_t il2cpp_linq_list_min_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t best = ReadI8(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int64_t elem = ReadI8(snap, i);
        if (elem < best) best = elem;
    }
    return best;
}

int64_t il2cpp_linq_list_max_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t best = ReadI8(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int64_t elem = ReadI8(snap, i);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadI8(snap, i);
    return static_cast<double>(sum) / static_cast<double>(n);
}

float il2cpp_linq_list_sum_f(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadD(snap, i);
    return static_cast<float>(sum);
}

float il2cpp_linq_list_min_f(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = ReadD(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = ReadD(snap, i);
        if (elem < best) best = elem;
    }
    return static_cast<float>(best);
}

float il2cpp_linq_list_max_f(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = ReadD(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = ReadD(snap, i);
        if (elem > best) best = elem;
    }
    return static_cast<float>(best);
}

float il2cpp_linq_list_average_f(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadD(snap, i);
    return static_cast<float>(sum / static_cast<double>(n));
}

double il2cpp_linq_list_sum_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadD(snap, i);
    return sum;
}

double il2cpp_linq_list_min_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = ReadD(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = ReadD(snap, i);
        if (elem < best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_max_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = ReadD(snap, 0);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = ReadD(snap, i);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += ReadD(snap, i);
    return sum / static_cast<double>(n);
}

//------------------------------------------------------------------------------
// Selector aggregates (Sum/Min/Max/Average(source, selector)) over a List<T>.
// The PROJECTION family is the entry suffix (i4/i8/d/f); the element is read at
// the LIST element kind and widened into the selector's first parameter slot.
// GC SAFETY: the list and the snapshot are rooted across every callback.
//------------------------------------------------------------------------------
static int32_t SelAggI4(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelI8I4ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI8I4Fn>(d->methodPtr)(v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelDI4ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelDI4Fn>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<SelI4I4ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI4I4Fn>(d->methodPtr)(v);
        }
    }
}

static int64_t SelAggI8(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelI8I8ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI8I8Fn>(d->methodPtr)(v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelDI8ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelDI8Fn>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<SelI4I8ThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI4I8Fn>(d->methodPtr)(v);
        }
    }
}

static double SelAggD(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelI8DThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI8DFn>(d->methodPtr)(v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelDDThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelDDFn>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<SelI4DThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI4DFn>(d->methodPtr)(v);
        }
    }
}

static float SelAggF(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelI8FThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI8FFn>(d->methodPtr)(v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<SelDFThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelDFFn>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<SelI4FThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<SelI4FFn>(d->methodPtr)(v);
        }
    }
}

int32_t il2cpp_linq_list_sum_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    int32_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = SelAggI4(d, snap, i, kind);
        if ((elem > 0 && sum > INT32_MAX - elem) || (elem < 0 && sum < INT32_MIN - elem))
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        sum += elem;
    }
    return sum;
}

int32_t il2cpp_linq_list_min_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int32_t best = SelAggI4(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int32_t elem = SelAggI4(d, snap, i, kind);
        if (elem < best) best = elem;
    }
    return best;
}

int32_t il2cpp_linq_list_max_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int32_t best = SelAggI4(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int32_t elem = SelAggI4(d, snap, i, kind);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_sel_i4(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggI4(d, snap, i, kind);
    return static_cast<double>(sum) / static_cast<double>(n);
}

int64_t il2cpp_linq_list_sum_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = SelAggI8(d, snap, i, kind);
        if ((elem > 0 && sum > INT64_MAX - elem) || (elem < 0 && sum < INT64_MIN - elem))
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        sum += elem;
    }
    return sum;
}

int64_t il2cpp_linq_list_min_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t best = SelAggI8(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int64_t elem = SelAggI8(d, snap, i, kind);
        if (elem < best) best = elem;
    }
    return best;
}

int64_t il2cpp_linq_list_max_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t best = SelAggI8(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const int64_t elem = SelAggI8(d, snap, i, kind);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_sel_i8(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    int64_t sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggI8(d, snap, i, kind);
    return static_cast<double>(sum) / static_cast<double>(n);
}

double il2cpp_linq_list_sum_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggD(d, snap, i, kind);
    return sum;
}

double il2cpp_linq_list_min_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = SelAggD(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = SelAggD(d, snap, i, kind);
        if (elem < best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_max_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = SelAggD(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = SelAggD(d, snap, i, kind);
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_list_average_sel_d(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggD(d, snap, i, kind);
    return sum / static_cast<double>(n);
}

float il2cpp_linq_list_sum_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggF(d, snap, i, kind);
    return static_cast<float>(sum);
}

float il2cpp_linq_list_min_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = SelAggF(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = SelAggF(d, snap, i, kind);
        if (elem < best) best = elem;
    }
    return static_cast<float>(best);
}

float il2cpp_linq_list_max_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double best = SelAggF(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const double elem = SelAggF(d, snap, i, kind);
        if (elem > best) best = elem;
    }
    return static_cast<float>(best);
}

float il2cpp_linq_list_average_sel_f(Il2CppObject* list, Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    double sum = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i) sum += SelAggF(d, snap, i, kind);
    return static_cast<float>(sum / static_cast<double>(n));
}

//------------------------------------------------------------------------------
// Selector aggregates with a DECIMAL projection (net48 Sum/Min/Max/Average(
// source, selector) over a decimal selector). The 16-byte decimal result rides
// an explicit result slot (the decimal T3 contract: the slot is the FIRST
// parameter). The element is read at the LIST element kind and widened into the
// selector's first slot; the decimal arithmetic uses the runtime's checked
// icall_decimal_* authority (net48 OverflowException on overflow).
// GC SAFETY: the list and the snapshot are rooted across every callback.
//------------------------------------------------------------------------------
typedef runtime::Il2CppDecimal (*ListSelI4ToDecFn)(int32_t);
typedef runtime::Il2CppDecimal (*ListSelI4ToDecThisFn)(Il2CppObject*, int32_t);
typedef runtime::Il2CppDecimal (*ListSelI8ToDecFn)(int64_t);
typedef runtime::Il2CppDecimal (*ListSelI8ToDecThisFn)(Il2CppObject*, int64_t);
typedef runtime::Il2CppDecimal (*ListSelDToDecFn)(double);
typedef runtime::Il2CppDecimal (*ListSelDToDecThisFn)(Il2CppObject*, double);

static runtime::Il2CppDecimal SelAggDec(Il2CppDelegate* d, Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<ListSelI8ToDecThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<ListSelI8ToDecFn>(d->methodPtr)(v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<ListSelDToDecThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<ListSelDToDecFn>(d->methodPtr)(v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<ListSelI4ToDecThisFn>(d->methodPtr)(d->target, v)
                : reinterpret_cast<ListSelI4ToDecFn>(d->methodPtr)(v);
        }
    }
}

void il2cpp_linq_list_sum_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const runtime::Il2CppDecimal elem = SelAggDec(d, snap, i, kind);
        runtime::icall_decimal_add(result, result, &elem);
    }
}

void il2cpp_linq_list_min_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = SelAggDec(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const runtime::Il2CppDecimal elem = SelAggDec(d, snap, i, kind);
        if (runtime::icall_decimal_compare(&elem, &best) < 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_list_max_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                  Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    runtime::Il2CppDecimal best = SelAggDec(d, snap, 0, kind);
    for (int32_t i = 1; i < snap->length; ++i)
    {
        const runtime::Il2CppDecimal elem = SelAggDec(d, snap, i, kind);
        if (runtime::icall_decimal_compare(&elem, &best) > 0) best = elem;
    }
    std::memcpy(result, &best, sizeof(runtime::Il2CppDecimal));
}

void il2cpp_linq_list_average_sel_dec(runtime::Il2CppDecimal* result, Il2CppObject* list,
                                      Il2CppObject* sel, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (snap->length == 0)
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    std::memset(result, 0, sizeof(runtime::Il2CppDecimal));
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const runtime::Il2CppDecimal elem = SelAggDec(d, snap, i, kind);
        runtime::icall_decimal_add(result, result, &elem);
    }
    runtime::Il2CppDecimal count;
    std::memset(&count, 0, sizeof(runtime::Il2CppDecimal));
    runtime::icall_decimal_ctor_int32(&count, n);
    runtime::icall_decimal_div(result, result, &count);
}

//------------------------------------------------------------------------------
// Predicate operators
//------------------------------------------------------------------------------
bool il2cpp_linq_list_any(Il2CppObject* list)
{
    return il2cpp_linq_list_count(list) > 0;
}

bool il2cpp_linq_list_any_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (PredicateOn(d, snap, i, kind)) return true;
    }
    return false;
}

int32_t il2cpp_linq_list_count_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    int32_t count = 0;
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (PredicateOn(d, snap, i, kind)) ++count;
    }
    return count;
}

bool il2cpp_linq_list_all_pred(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t n = snap->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!PredicateOn(d, snap, i, kind)) return false;
    }
    return true; // empty sequence: vacuous truth
}

//------------------------------------------------------------------------------
// Where -- keep the elements satisfying the predicate (same element family).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_where(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));

    // The int32 / reference families reuse the array kernels (identical
    // semantics, one predicate pass + exact-size result).
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_where_i4(snap, pred);
    if (kind == IL2CPP_LIST_KIND_REF) return il2cpp_linq_where_ref(snap, pred);

    Il2CppArray* scratch = AllocElemArray(snap, kind, snap->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (!PredicateOn(d, snap, i, kind)) continue;
        CopyElem(scratch, w, snap, i);
        ++w;
    }
    if (w == snap->length) return scratch;
    Il2CppArray* out = AllocElemArray(snap, kind, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i) CopyElem(out, i, scratch, i);
    return out;
}

//------------------------------------------------------------------------------
// Where with an INDEX predicate (net48 Func<TSource,int,bool>): the predicate
// receives the element AND its zero-based source index. Same element family.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_where_idx(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));

    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_where_idx_i4(snap, pred);
    if (kind == IL2CPP_LIST_KIND_REF) return il2cpp_linq_where_idx_ref(snap, pred);

    Il2CppArray* scratch = AllocElemArray(snap, kind, snap->length);
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (!PredicateOnIdx(d, snap, i, kind)) continue;
        CopyElem(scratch, w, snap, i);
        ++w;
    }
    if (w == snap->length) return scratch;
    Il2CppArray* out = AllocElemArray(snap, kind, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i) CopyElem(out, i, scratch, i);
    return out;
}

//------------------------------------------------------------------------------
// Select -- map every element through the selector (result family resKind).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_select(Il2CppObject* list, Il2CppObject* sel,
                                     int32_t kind, int32_t resKind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));

    if (kind == IL2CPP_LIST_KIND_I4)
    {
        switch (resKind)
        {
            case 0: return il2cpp_linq_select_i4_i4(snap, sel);
            case 1: return il2cpp_linq_select_i4_ref(snap, sel);
            case 2: return il2cpp_linq_select_i4_i8(snap, sel);
            case 3: return il2cpp_linq_select_i4_f(snap, sel);
            case 4: return il2cpp_linq_select_i4_d(snap, sel);
            default: break;
        }
    }
    if (kind == IL2CPP_LIST_KIND_REF && resKind == 1)
    {
        return il2cpp_linq_select_ref_ref(snap, sel);
    }

    // Generic path: allocate the result array by the RESULT family, then fill
    // it element by element (narrow/wide source cells are widened first).
    int32_t resElemSize = 4;
    bool resIsRef = false;
    int32_t resElemKind = 1;
    switch (resKind)
    {
        case 1: resElemSize = static_cast<int32_t>(sizeof(void*)); resIsRef = true; resElemKind = 0; break;
        case 2: resElemSize = 8; resElemKind = 2; break;
        case 3: resElemSize = 8; resElemKind = 3; break; // float -> double carrier
        case 4: resElemSize = 8; resElemKind = 4; break;
        default: resElemSize = 4; resElemKind = 1; break;
    }
    Il2CppArray* out = resIsRef
        ? il2cpp_array_new(nullptr, resElemSize, true, snap->length)
        : il2cpp_array_new_typed(resElemSize, resElemKind, snap->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (kind == IL2CPP_LIST_KIND_REF)
        {
            Il2CppObject* r = d->target != nullptr
                ? reinterpret_cast<SelRefRefThisFn>(d->methodPtr)(d->target, ReadRef(snap, i))
                : reinterpret_cast<SelRefRefFn>(d->methodPtr)(ReadRef(snap, i));
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
            *slot = r;
            continue;
        }
        if (kind == IL2CPP_LIST_KIND_I8)
        {
            const int32_t v = static_cast<int32_t>(ReadI8(snap, i));
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelI4RefThisFn>(d->methodPtr)(d->target, v)
                        : reinterpret_cast<SelI4RefFn>(d->methodPtr)(v);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I8ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I8Fn>(d->methodPtr)(v);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelI4FThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4FFn>(d->methodPtr)(v));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4DThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4DFn>(d->methodPtr)(v);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I4ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I4Fn>(d->methodPtr)(v);
                    break;
            }
            continue;
        }
        if (kind == IL2CPP_LIST_KIND_F || kind == IL2CPP_LIST_KIND_D)
        {
            // The double/float selector ABI takes the element as double; the
            // F-stack carrier means a double selector leaf has the same shape.
            const int32_t v = static_cast<int32_t>(ReadD(snap, i));
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelI4RefThisFn>(d->methodPtr)(d->target, v)
                        : reinterpret_cast<SelI4RefFn>(d->methodPtr)(v);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I8ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I8Fn>(d->methodPtr)(v);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelI4FThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4FFn>(d->methodPtr)(v));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4DThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4DFn>(d->methodPtr)(v);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I4ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I4Fn>(d->methodPtr)(v);
                    break;
            }
            continue;
        }
        {
            const int32_t v = ReadI4(snap, i, kind);
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelI4RefThisFn>(d->methodPtr)(d->target, v)
                        : reinterpret_cast<SelI4RefFn>(d->methodPtr)(v);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I8ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I8Fn>(d->methodPtr)(v);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelI4FThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4FFn>(d->methodPtr)(v));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4DThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4DFn>(d->methodPtr)(v);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4I4ThisFn>(d->methodPtr)(d->target, v)
                            : reinterpret_cast<SelI4I4Fn>(d->methodPtr)(v);
                    break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// Select with an INDEX selector (net48 Func<TSource,int,TResult>): the selector
// receives the element AND its zero-based source index. The int32-source and
// ref-source, int32-result shapes reuse the array index kernels; the generic
// path widens each source family into its OWN slot (int32 / int64 / double /
// reference) and passes the index as the second argument.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_select_idx(Il2CppObject* list, Il2CppObject* sel,
                                         int32_t kind, int32_t resKind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));

    if (kind == IL2CPP_LIST_KIND_I4)
    {
        switch (resKind)
        {
            case 0: return il2cpp_linq_select_idx_i4_i4(snap, sel);
            case 1: return il2cpp_linq_select_idx_i4_ref(snap, sel);
            case 2: return il2cpp_linq_select_idx_i4_i8(snap, sel);
            case 3: return il2cpp_linq_select_idx_i4_f(snap, sel);
            case 4: return il2cpp_linq_select_idx_i4_d(snap, sel);
            default: break;
        }
    }
    if (kind == IL2CPP_LIST_KIND_REF && resKind == 1)
    {
        return il2cpp_linq_select_idx_ref_ref(snap, sel);
    }

    int32_t resElemSize = 4;
    bool resIsRef = false;
    int32_t resElemKind = 1;
    switch (resKind)
    {
        case 1: resElemSize = static_cast<int32_t>(sizeof(void*)); resIsRef = true; resElemKind = 0; break;
        case 2: resElemSize = 8; resElemKind = 2; break;
        case 3: resElemSize = 8; resElemKind = 3; break; // float -> double carrier
        case 4: resElemSize = 8; resElemKind = 4; break;
        default: resElemSize = 4; resElemKind = 1; break;
    }
    Il2CppArray* out = resIsRef
        ? il2cpp_array_new(nullptr, resElemSize, true, snap->length)
        : il2cpp_array_new_typed(resElemSize, resElemKind, snap->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (kind == IL2CPP_LIST_KIND_REF)
        {
            Il2CppObject* r = d->target != nullptr
                ? reinterpret_cast<SelRefIdxRefThisFn>(d->methodPtr)(d->target, ReadRef(snap, i), i)
                : reinterpret_cast<SelRefIdxRefFn>(d->methodPtr)(ReadRef(snap, i), i);
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
            *slot = r;
            continue;
        }
        if (kind == IL2CPP_LIST_KIND_I8)
        {
            const int64_t v = ReadI8(snap, i);
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelI8IdxRefThisFn>(d->methodPtr)(d->target, v, i)
                        : reinterpret_cast<SelI8IdxRefFn>(d->methodPtr)(v, i);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI8IdxI8ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI8IdxI8Fn>(d->methodPtr)(v, i);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelI8IdxFThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI8IdxFFn>(d->methodPtr)(v, i));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI8IdxDThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI8IdxDFn>(d->methodPtr)(v, i);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI8IdxI4ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI8IdxI4Fn>(d->methodPtr)(v, i);
                    break;
            }
            continue;
        }
        if (kind == IL2CPP_LIST_KIND_F || kind == IL2CPP_LIST_KIND_D)
        {
            const double v = ReadD(snap, i);
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelDIdxRefThisFn>(d->methodPtr)(d->target, v, i)
                        : reinterpret_cast<SelDIdxRefFn>(d->methodPtr)(v, i);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelDIdxI8ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelDIdxI8Fn>(d->methodPtr)(v, i);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelDIdxFThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelDIdxFFn>(d->methodPtr)(v, i));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelDIdxDThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelDIdxDFn>(d->methodPtr)(v, i);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelDIdxI4ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelDIdxI4Fn>(d->methodPtr)(v, i);
                    break;
            }
            continue;
        }
        {
            const int32_t v = ReadI4(snap, i, kind);
            switch (resKind)
            {
                case 1:
                {
                    Il2CppObject* r = d->target != nullptr
                        ? reinterpret_cast<SelI4IdxRefThisFn>(d->methodPtr)(d->target, v, i)
                        : reinterpret_cast<SelI4IdxRefFn>(d->methodPtr)(v, i);
                    Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
                    il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                    *slot = r;
                    break;
                }
                case 2:
                    *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4IdxI8ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI4IdxI8Fn>(d->methodPtr)(v, i);
                    break;
                case 3:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = static_cast<double>(
                        d->target != nullptr
                            ? reinterpret_cast<SelI4IdxFThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI4IdxFFn>(d->methodPtr)(v, i));
                    break;
                case 4:
                    *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4IdxDThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI4IdxDFn>(d->methodPtr)(v, i);
                    break;
                default:
                    *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                        d->target != nullptr
                            ? reinterpret_cast<SelI4IdxI4ThisFn>(d->methodPtr)(d->target, v, i)
                            : reinterpret_cast<SelI4IdxI4Fn>(d->methodPtr)(v, i);
                    break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// Select with a VALUE-TYPE TUPLE result (resKind 5). The selector leaf returns
// the tuple struct BY VALUE; a trivial struct of <= 8 bytes rides the integer
// return register (the Itanium C++ ABI), so the 64-bit carrier is the struct's
// raw bits. Each result is boxed through the shared tuple-box contract and the
// result array is a REFERENCE array of boxes (the shared array enumerator reads
// the box back; il2cpp_ienum_get_current_tuple_t copies the payload).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_select_tuple(Il2CppObject* list, Il2CppObject* sel,
                                           int32_t kind, const char* kinds)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(sel, "selector");
    const int32_t payloadSize = runtime::il2cpp_tuple_box_size(kinds);
    if (payloadSize < 0 || payloadSize > 8)
    {
        // Only the register-carried tuple shapes (<= 8 bytes) are wired; a
        // larger struct uses the memory return class and is out of scope.
        il2cpp_throw_new(
            "System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception",
            "Select with this tuple result shape is not supported.");
    }
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    Il2CppArray* out = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, snap->length);
    roots.Add(reinterpret_cast<void**>(&out));

    typedef int64_t (*SelI4TupleFn)(int32_t);
    typedef int64_t (*SelI4TupleThisFn)(Il2CppObject*, int32_t);
    for (int32_t i = 0; i < snap->length; ++i)
    {
        const int32_t v = ReadI4(snap, i, kind);
        const int64_t bits = d->target != nullptr
            ? reinterpret_cast<SelI4TupleThisFn>(d->methodPtr)(d->target, v)
            : reinterpret_cast<SelI4TupleFn>(d->methodPtr)(v);
        Il2CppObject* box = runtime::il2cpp_tuple_box(&bits, kinds);
        if (box != nullptr)
        {
            roots.Add(reinterpret_cast<void**>(&box));
        }
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(out, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), box);
        *slot = box;
    }
    return out;
}

//------------------------------------------------------------------------------
// First / FirstOrDefault / Last / LastOrDefault
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_first_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadI4(snap, 0, kind);
}

int32_t il2cpp_linq_list_first_or_default_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadI4(snap, 0, kind) : 0;
}

int64_t il2cpp_linq_list_first_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadI8(snap, 0);
}

int64_t il2cpp_linq_list_first_or_default_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadI8(snap, 0) : 0;
}

double il2cpp_linq_list_first_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadD(snap, 0);
}

double il2cpp_linq_list_first_or_default_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadD(snap, 0) : 0.0;
}

Il2CppObject* il2cpp_linq_list_first_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadRef(snap, 0);
}

Il2CppObject* il2cpp_linq_list_first_or_default_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadRef(snap, 0) : nullptr;
}

int32_t il2cpp_linq_list_last_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadI4(snap, snap->length - 1, kind);
}

int32_t il2cpp_linq_list_last_or_default_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadI4(snap, snap->length - 1, kind) : 0;
}

int64_t il2cpp_linq_list_last_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadI8(snap, snap->length - 1);
}

int64_t il2cpp_linq_list_last_or_default_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadI8(snap, snap->length - 1) : 0;
}

double il2cpp_linq_list_last_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadD(snap, snap->length - 1);
}

double il2cpp_linq_list_last_or_default_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadD(snap, snap->length - 1) : 0.0;
}

Il2CppObject* il2cpp_linq_list_last_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return ReadRef(snap, snap->length - 1);
}

Il2CppObject* il2cpp_linq_list_last_or_default_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return snap->length != 0 ? ReadRef(snap, snap->length - 1) : nullptr;
}

//------------------------------------------------------------------------------
// Single / SingleOrDefault (source-only)
//
// net48 Enumerable.Single: an empty source and a multi-element source both
// throw InvalidOperationException ("Sequence contains no elements" /
// "Sequence contains more than one element"); SingleOrDefault answers
// default(T) for an empty source and throws only for more than one element.
// The snapshot is exact storage, so the single element is read at index 0.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_single_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return ReadI4(snap, 0, kind);
}

int64_t il2cpp_linq_list_single_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return ReadI8(snap, 0);
}

double il2cpp_linq_list_single_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return ReadD(snap, 0);
}

Il2CppObject* il2cpp_linq_list_single_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return ReadRef(snap, 0);
}

int32_t il2cpp_linq_list_single_or_default_i4(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return snap->length == 1 ? ReadI4(snap, 0, kind) : 0;
}

int64_t il2cpp_linq_list_single_or_default_i8(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return snap->length == 1 ? ReadI8(snap, 0) : 0;
}

double il2cpp_linq_list_single_or_default_d(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return snap->length == 1 ? ReadD(snap, 0) : 0.0;
}

Il2CppObject* il2cpp_linq_list_single_or_default_ref(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length > 1) il2cpp_throw_invalid_operation_exception("Sequence contains more than one element");
    return snap->length == 1 ? ReadRef(snap, 0) : nullptr;
}

//------------------------------------------------------------------------------
// First(predicate) / FirstOrDefault(predicate)
//------------------------------------------------------------------------------
namespace {

// The index of the first element satisfying the predicate, or -1.
int32_t FirstMatchIndex(Il2CppDelegate* d, Il2CppArray* snap, int32_t kind)
{
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (PredicateOn(d, snap, i, kind)) return i;
    }
    return -1;
}

} // namespace

int32_t il2cpp_linq_list_first_pred_i4(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    if (i < 0) il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return ReadI4(snap, i, kind);
}

int32_t il2cpp_linq_list_first_or_default_pred_i4(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    return i < 0 ? 0 : ReadI4(snap, i, kind);
}

int64_t il2cpp_linq_list_first_pred_i8(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    if (i < 0) il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return ReadI8(snap, i);
}

int64_t il2cpp_linq_list_first_or_default_pred_i8(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    return i < 0 ? 0 : ReadI8(snap, i);
}

double il2cpp_linq_list_first_pred_d(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    if (i < 0) il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return ReadD(snap, i);
}

double il2cpp_linq_list_first_or_default_pred_d(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    return i < 0 ? 0.0 : ReadD(snap, i);
}

Il2CppObject* il2cpp_linq_list_first_pred_ref(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    if (i < 0) il2cpp_throw_invalid_operation_exception("Sequence contains no matching element");
    return ReadRef(snap, i);
}

Il2CppObject* il2cpp_linq_list_first_or_default_pred_ref(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    const int32_t i = FirstMatchIndex(d, snap, kind);
    return i < 0 ? nullptr : ReadRef(snap, i);
}
//------------------------------------------------------------------------------
// ElementAt / Contains
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_element_at_i4(Il2CppObject* list, int32_t index, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (index < 0 || index >= snap->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
    return ReadI4(snap, index, kind);
}

int64_t il2cpp_linq_list_element_at_i8(Il2CppObject* list, int32_t index, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (index < 0 || index >= snap->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
    return ReadI8(snap, index);
}

double il2cpp_linq_list_element_at_d(Il2CppObject* list, int32_t index, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (index < 0 || index >= snap->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
    return ReadD(snap, index);
}

Il2CppObject* il2cpp_linq_list_element_at_ref(Il2CppObject* list, int32_t index, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (index < 0 || index >= snap->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index",
            "Index was out of range. Must be non-negative and less than the size of the collection.");
    }
    return ReadRef(snap, index);
}

bool il2cpp_linq_list_contains_i4(Il2CppObject* list, int32_t value, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (ReadI4(snap, i, kind) == value) return true;
    }
    return false;
}

bool il2cpp_linq_list_contains_i8(Il2CppObject* list, int64_t value, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (ReadI8(snap, i) == value) return true;
    }
    return false;
}

bool il2cpp_linq_list_contains_d(Il2CppObject* list, double value, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (ReadD(snap, i) == value) return true;
    }
    return false;
}

bool il2cpp_linq_list_contains_ref(Il2CppObject* list, Il2CppObject* value, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    for (int32_t i = 0; i < snap->length; ++i)
    {
        if (runtime::icall_object_equals(ReadRef(snap, i), value)) return true;
    }
    return false;
}

//------------------------------------------------------------------------------
// Reverse / DefaultIfEmpty
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_reverse(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_reverse_i4(snap);
    Il2CppArray* out = AllocElemArray(snap, kind, snap->length);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < snap->length; ++i)
    {
        CopyElem(out, i, snap, snap->length - 1 - i);
    }
    return out;
}

Il2CppArray* il2cpp_linq_list_default_if_empty(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (snap->length != 0) return snap;
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_default_if_empty_i4(snap);
    // One-element array holding the family default (zeroed storage).
    return AllocElemArray(snap, kind, 1);
}

//------------------------------------------------------------------------------
// Take / Skip / TakeWhile / SkipWhile / Concat
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_take(Il2CppObject* list, int32_t count, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_take_i4(snap, count);
    // net48 Take: count <= 0 -> empty, count >= length -> full copy.
    const int32_t n = count <= 0 ? 0 : (count >= snap->length ? snap->length : count);
    Il2CppArray* out = AllocElemArray(snap, kind, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i) CopyElem(out, i, snap, i);
    return out;
}

Il2CppArray* il2cpp_linq_list_skip(Il2CppObject* list, int32_t count, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_skip_i4(snap, count);
    // net48 Skip: count <= 0 -> full copy, count >= length -> empty.
    const int32_t start = count <= 0 ? 0 : (count >= snap->length ? snap->length : count);
    const int32_t n = snap->length - start;
    Il2CppArray* out = AllocElemArray(snap, kind, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i) CopyElem(out, i, snap, start + i);
    return out;
}

Il2CppArray* il2cpp_linq_list_take_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_take_while_i4(snap, pred);
    int32_t w = 0;
    while (w < snap->length && PredicateOn(d, snap, w, kind)) ++w;
    Il2CppArray* out = AllocElemArray(snap, kind, w);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i) CopyElem(out, i, snap, i);
    return out;
}

Il2CppArray* il2cpp_linq_list_skip_while(Il2CppObject* list, Il2CppObject* pred, int32_t kind)
{
    RequireList(list);
    Il2CppDelegate* d = RequireCallback(pred, "predicate");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_skip_while_i4(snap, pred);
    int32_t start = 0;
    while (start < snap->length && PredicateOn(d, snap, start, kind)) ++start;
    const int32_t n = snap->length - start;
    Il2CppArray* out = AllocElemArray(snap, kind, n);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < n; ++i) CopyElem(out, i, snap, start + i);
    return out;
}

Il2CppArray* il2cpp_linq_list_concat(Il2CppObject* list, Il2CppObject* second, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* a = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&a));
    Il2CppArray* b = reinterpret_cast<Il2CppArray*>(second);
    // The second operand is another sequence of the same family: an array, or
    // another list (normalized to its snapshot).
    if (b != nullptr && (b->obj.klass == nullptr || b->obj.klass->name == nullptr ||
                         b->obj.klass->name[0] != '['))
    {
        roots.Add(reinterpret_cast<void**>(&b));
        b = SnapshotOf(second, kind);
    }
    if (kind == IL2CPP_LIST_KIND_I4 && b != nullptr && b->elemSize == 4 && !b->isRefElem)
    {
        return il2cpp_linq_concat_i4(a, b);
    }
    const int32_t bn = b != nullptr ? b->length : 0;
    Il2CppArray* out = AllocElemArray(a, kind, a->length + bn);
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < a->length; ++i) CopyElem(out, i, a, i);
    for (int32_t i = 0; i < bn; ++i) CopyElem(out, a->length + i, b, i);
    return out;
}

//------------------------------------------------------------------------------
// Set operators / Zip / SequenceEqual
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_distinct(Il2CppObject* list, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* snap = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&snap));
    if (kind == IL2CPP_LIST_KIND_I4) return il2cpp_linq_distinct_i4(snap);
    Il2CppArray* out = AllocElemArray(snap, kind, snap->length);
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (int32_t i = 0; i < snap->length; ++i)
    {
        bool seen = false;
        for (int32_t j = 0; j < w && !seen; ++j) seen = ElemEquals(snap, i, out, j, kind);
        if (seen) continue;
        CopyElem(out, w, snap, i);
        ++w;
    }
    if (w == snap->length) return out;
    Il2CppArray* exact = AllocElemArray(snap, kind, w);
    roots.Add(reinterpret_cast<void**>(&exact));
    for (int32_t i = 0; i < w; ++i) CopyElem(exact, i, out, i);
    return exact;
}

Il2CppArray* il2cpp_linq_list_except(Il2CppObject* list, Il2CppObject* second, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* a = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&a));
    Il2CppArray* b = reinterpret_cast<Il2CppArray*>(second);
    if (b != nullptr && (b->obj.klass == nullptr || b->obj.klass->name == nullptr ||
                         b->obj.klass->name[0] != '['))
    {
        roots.Add(reinterpret_cast<void**>(&b));
        b = SnapshotOf(second, kind);
    }
    if (kind == IL2CPP_LIST_KIND_I4 && b != nullptr && b->elemSize == 4 && !b->isRefElem)
    {
        return il2cpp_linq_except_i4(a, b);
    }
    Il2CppArray* out = AllocElemArray(a, kind, a->length);
    roots.Add(reinterpret_cast<void**>(&out));
    int32_t w = 0;
    for (int32_t i = 0; i < a->length; ++i)
    {
        bool inSecond = false;
        for (int32_t j = 0; b != nullptr && j < b->length && !inSecond; ++j)
            inSecond = ElemEquals(a, i, b, j, kind);
        if (inSecond) continue;
        bool already = false;
        for (int32_t j = 0; j < w && !already; ++j) already = ElemEquals(a, i, out, j, kind);
        if (already) continue;
        CopyElem(out, w, a, i);
        ++w;
    }
    if (w == a->length) return out;
    Il2CppArray* exact = AllocElemArray(a, kind, w);
    roots.Add(reinterpret_cast<void**>(&exact));
    for (int32_t i = 0; i < w; ++i) CopyElem(exact, i, out, i);
    return exact;
}

// The ZIP SIDE descriptor (linq.h IL2CPP_LINQ_ZIP_*/IL2CPP_LINQ_NARROW_*) of a
// LIST element family. IL2CPP_LIST_KIND_* and the zip side codes are different
// enumerations that happen to share the values 0..6 with different meanings, so
// every entry is named -- deriving one from the other numerically would turn
// LIST_KIND SBYTE (5) into NARROW CHAR (5) / BYTE (6) into UINT (6).
int32_t ZipSideOfListKind(int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I4:     return IL2CPP_LINQ_ZIP_I4;
        case IL2CPP_LIST_KIND_I8:     return IL2CPP_LINQ_ZIP_I8;
        case IL2CPP_LIST_KIND_F:      return IL2CPP_LINQ_ZIP_F;
        case IL2CPP_LIST_KIND_D:      return IL2CPP_LINQ_ZIP_D;
        case IL2CPP_LIST_KIND_SBYTE:  return IL2CPP_LINQ_NARROW_SBYTE;
        case IL2CPP_LIST_KIND_BYTE:   return IL2CPP_LINQ_NARROW_BYTE;
        case IL2CPP_LIST_KIND_BOOL:   return IL2CPP_LINQ_NARROW_BOOL;
        case IL2CPP_LIST_KIND_SHORT:  return IL2CPP_LINQ_NARROW_SHORT;
        case IL2CPP_LIST_KIND_USHORT: return IL2CPP_LINQ_NARROW_USHORT;
        case IL2CPP_LIST_KIND_CHAR:   return IL2CPP_LINQ_NARROW_CHAR;
        case IL2CPP_LIST_KIND_UINT:   return IL2CPP_LINQ_NARROW_UINT;
        case IL2CPP_LIST_KIND_DEC:    return IL2CPP_LINQ_ZIP_DEC;
        default:
            il2cpp_throw_not_supported_exception(
                "Unsupported List element kind for Zip (IL2CPP_LIST_KIND_REF is handled "
                "by the reference branch above).");
            return 0;
    }
}
// Defined below (the unified Zip entry); declared here because the List entry
// is the thin wrapper that forwards to it.
Il2CppArray* il2cpp_linq_zip_family(Il2CppObject* first, Il2CppObject* second,
                                    Il2CppObject* sel, int32_t firstKind,
                                    int32_t secondKind, int32_t resKind);

Il2CppArray* il2cpp_linq_list_zip(Il2CppObject* list, Il2CppObject* second,
                                  Il2CppObject* sel, int32_t kind, int32_t resKind,
                                  int32_t secondKind)
{
    // Delegates to the UNIFIED entry: it tells a List object from an array at
    // run time (see il2cpp_linq_zip_family), which the transpiler cannot do.
    return il2cpp_linq_zip_family(list, second, sel, kind, secondKind, resKind);
}

// True when `o` is an ARRAY object (the array descriptor names start with '[').
bool IsArrayObject(Il2CppObject* o)
{
    return o != nullptr && o->klass != nullptr && o->klass->name != nullptr &&
           o->klass->name[0] == '[';
}

// The UNIFIED Zip entry (arrays AND lists, possibly different families).
//
// Why this exists: `Enumerable.Zip(first, second, selector)` has arity 3, so the
// transpiler cannot see the RECEIVER's tracked type (the stack top is the
// selector and the value below it is the SECOND source); and a `List<byte>`
// local is tracked as `System.Int32[]` by the type-tracking layer. Neither
// signal can tell a List object from an array, and guessing produced
// `il2cpp_linq_zip_wide(ListObject*, ...)` -- a C++ type error at best, an
// items-pointer-read-as-length at worst. The runtime CAN tell (the array
// descriptor name starts with '['), so the decision lives here.
//
// `firstKind` / `secondKind` are IL2CPP_LIST_KIND_* codes derived from the
// MethodSpec element bytes; `resKind` is the selector's return family.
Il2CppArray* il2cpp_linq_zip_family(Il2CppObject* first, Il2CppObject* second,
                                    Il2CppObject* sel, int32_t firstKind,
                                    int32_t secondKind, int32_t resKind)
{
    RequireCallback(sel, "selector");
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    if (first == nullptr) il2cpp_throw_argument_null_exception("first");
    if (second == nullptr) il2cpp_throw_argument_null_exception("second");

    // Snapshot a LIST at its own family; an ARRAY is used as-is.
    Il2CppArray* a;
    if (IsArrayObject(first))
    {
        a = reinterpret_cast<Il2CppArray*>(first);
    }
    else
    {
        a = SnapshotOf(first, firstKind);
        roots.Add(reinterpret_cast<void**>(&a));
    }
    Il2CppArray* b;
    if (IsArrayObject(second))
    {
        b = reinterpret_cast<Il2CppArray*>(second);
    }
    else
    {
        b = SnapshotOf(second, secondKind);
        roots.Add(reinterpret_cast<void**>(&b));
    }

    // BATCH4: a reference-element source (or a narrow result cell) goes through
    // the mixed entry, which reads a reference side as Il2CppObject* and calls
    // the selector through the STATICALLY DECLARED ABI (the old code stored a
    // value-result thunk's EAX as a GC pointer -- a measured 0xC0000005).
    if (firstKind == IL2CPP_LIST_KIND_REF || secondKind == IL2CPP_LIST_KIND_REF ||
        a->isRefElem || b->isRefElem || resKind >= 6)
        return il2cpp_linq_zip_mixed(a, b, sel, firstKind, secondKind, resKind);
    return il2cpp_linq_zip_wide(a, b, sel, ZipSideOfListKind(firstKind),
                                ZipSideOfListKind(secondKind), resKind);
}

bool il2cpp_linq_list_sequence_equal(Il2CppObject* list, Il2CppObject* second, int32_t kind)
{
    RequireList(list);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&list));
    Il2CppArray* a = SnapshotOf(list, kind);
    roots.Add(reinterpret_cast<void**>(&a));
    Il2CppArray* b = reinterpret_cast<Il2CppArray*>(second);
    if (b != nullptr && (b->obj.klass == nullptr || b->obj.klass->name == nullptr ||
                         b->obj.klass->name[0] != '['))
    {
        roots.Add(reinterpret_cast<void**>(&b));
        b = SnapshotOf(second, kind);
    }
    if (b == nullptr) il2cpp_throw_argument_null_exception("second");
    if (a->length != b->length) return false;
    for (int32_t i = 0; i < a->length; ++i)
    {
        if (!ElemEquals(a, i, b, i, kind)) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// Ordering (int32 elements + int32 keys -- the array key-selector surface)
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_order_by_key_i4(Il2CppObject* list, Il2CppObject* keySel, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_I4)
    {
        il2cpp_throw_not_supported_exception(
            "OrderBy over a List of a non-int32 element family is not wired (the runtime "
            "key-selector surface is int32 elements AND int32 keys).");
    }
    return il2cpp_linq_order_by_key_i4(snap, keySel);
}

Il2CppArray* il2cpp_linq_list_order_by_key_desc_i4(Il2CppObject* list, Il2CppObject* keySel, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_I4)
    {
        il2cpp_throw_not_supported_exception(
            "OrderByDescending over a List of a non-int32 element family is not wired.");
    }
    return il2cpp_linq_order_by_key_desc_i4(snap, keySel);
}

// OrderBy over a REFERENCE-element list: the snapshot is a reference array, so
// the array reference key-selector entry runs on it unchanged.
Il2CppArray* il2cpp_linq_list_order_by_key_ref_i4(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t desc, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return il2cpp_linq_order_by_key_ref_i4(snap, keySel, desc);
}

Il2CppArray* il2cpp_linq_list_order_by_key_ref_i8(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t desc, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return il2cpp_linq_order_by_key_ref_i8(snap, keySel, desc);
}

Il2CppArray* il2cpp_linq_list_order_by_key_ref_d(Il2CppObject* list, Il2CppObject* keySel,
                                                 int32_t desc, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return il2cpp_linq_order_by_key_ref_d(snap, keySel, desc);
}

// impl-orderfam: the List REFERENCE-element FLOAT-key face (the
// Func<TRef,float> ABI). The snapshot rides the array ref_f entry, whose
// float(*) reader keeps the 4-byte float return out of the double misread
// (b5-gaps §14).
Il2CppArray* il2cpp_linq_list_order_by_key_ref_f(Il2CppObject* list, Il2CppObject* keySel,
                                                 int32_t desc, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return il2cpp_linq_order_by_key_ref_f(snap, keySel, desc);
}

Il2CppArray* il2cpp_linq_list_order_by_key_ref_ref(Il2CppObject* list, Il2CppObject* keySel,
                                                   int32_t desc, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    return il2cpp_linq_order_by_key_ref_ref(snap, keySel, desc);
}

//------------------------------------------------------------------------------
// The DOUBLE-element list key-selector entries: the snapshot is a double array
// (8-byte cells) and the key crosses in the F-stack slot. The list is snapped
// through its own accessor, never cast to an array header.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_order_by_key_d(Il2CppObject* list, Il2CppObject* keySel,
                                             int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_D)
    {
        il2cpp_throw_not_supported_exception(
            "OrderBy over a List of a non-double element family is not wired for the "
            "double key-selector entry.");
    }
    return il2cpp_linq_order_by_key_d(snap, keySel);
}

Il2CppArray* il2cpp_linq_list_order_by_key_desc_d(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_D)
    {
        il2cpp_throw_not_supported_exception(
            "OrderByDescending over a List of a non-double element family is not wired "
            "for the double key-selector entry.");
    }
    return il2cpp_linq_order_by_key_desc_d(snap, keySel);
}

//------------------------------------------------------------------------------
// impl-orderfam: the DOUBLE-element list FLOAT-key entries (the
// Func<double,float> ABI). The snapshot is a double array (8-byte cells) and
// the key returns in the float slot -- il2cpp_linq_order_by_key_d_f reads it
// through the float(*) reader, never through the double reader (b5-gaps §14).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_list_order_by_key_f(Il2CppObject* list, Il2CppObject* keySel,
                                             int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_D)
    {
        il2cpp_throw_not_supported_exception(
            "OrderBy over a List of a non-double element family is not wired for the "
            "float key-selector entry.");
    }
    return il2cpp_linq_order_by_key_d_f(snap, keySel);
}

Il2CppArray* il2cpp_linq_list_order_by_key_desc_f(Il2CppObject* list, Il2CppObject* keySel,
                                                  int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_D)
    {
        il2cpp_throw_not_supported_exception(
            "OrderByDescending over a List of a non-double element family is not wired "
            "for the float key-selector entry.");
    }
    return il2cpp_linq_order_by_key_desc_d_f(snap, keySel);
}

Il2CppArray* il2cpp_linq_list_then_by_key_i4(Il2CppObject* list, Il2CppObject* k1,
                                             Il2CppObject* k2, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_I4)
    {
        il2cpp_throw_not_supported_exception("ThenBy over a List of a non-int32 element family is not wired.");
    }
    return il2cpp_linq_then_by_key_i4(snap, k1, k2);
}

Il2CppArray* il2cpp_linq_list_then_by_key_desc_i4(Il2CppObject* list, Il2CppObject* k1,
                                                  Il2CppObject* k2, int32_t kind)
{
    RequireList(list);
    Il2CppArray* snap = SnapshotOf(list, kind);
    if (kind != IL2CPP_LIST_KIND_I4)
    {
        il2cpp_throw_not_supported_exception("ThenByDescending over a List of a non-int32 element family is not wired.");
    }
    return il2cpp_linq_then_by_key_desc_i4(snap, k1, k2);
}

// The List<T> aggregate family is implemented explicitly above (and below):
// the source-only entries read the snapshot at the LIST element kind, and the
// selector entries dispatch the selector ABI on that same kind. The earlier
// macro forms delegated to the ARRAY kernels, which assume the snapshot's own
// family matches the projection -- reading a List<int> snapshot as int64 for a
// long projection (a silent wrong value). The explicit forms fix that.

//------------------------------------------------------------------------------
// Aggregate
//------------------------------------------------------------------------------
int32_t il2cpp_linq_list_aggregate_i4(Il2CppObject* list, Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_i4(SnapshotOf(list, kind), func);
}

int64_t il2cpp_linq_list_aggregate_i8(Il2CppObject* list, Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_i8(SnapshotOf(list, kind), func);
}

double il2cpp_linq_list_aggregate_d(Il2CppObject* list, Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_d(SnapshotOf(list, kind), func);
}

// floatfix: the FLOAT no-seed entry. The kind=F snapshot holds REAL 4-byte
// float cells (il2cpp_list_obj_to_array_float), so the fold must read them
// at that width and keep the accumulator float -- the double-family kernel
// (il2cpp_linq_aggregate_d) reinterprets pairs of float cells as doubles and
// accumulates without per-step R4 rounding (both a wrong value).
float il2cpp_linq_list_aggregate_f(Il2CppObject* list, Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_f_f(SnapshotOf(list, kind), func);
}

int32_t il2cpp_linq_list_aggregate_seed_i4(Il2CppObject* list, int32_t seed,
                                           Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_seed_i4(SnapshotOf(list, kind), seed, func);
}

int64_t il2cpp_linq_list_aggregate_seed_i8(Il2CppObject* list, int64_t seed,
                                           Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_seed_i8(SnapshotOf(list, kind), seed, func);
}

double il2cpp_linq_list_aggregate_seed_d(Il2CppObject* list, double seed,
                                           Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_seed_d(SnapshotOf(list, kind), seed, func);
}

// floatfix: the FLOAT seed entry. The seed crosses on the double F stack (a
// float parameter widens exactly -- narrowing back here is lossless because
// the caller's float value widened into it) and the accumulator stays float,
// folding through the FAccDFn float(*)(double,double) leaf ABI: a
// float-returning lambda is a C++ `float f(double, double)` and must be read
// through a float fn-pointer (the double read mixes the callee's
// XMM0[63:32] residue into every step). Per-step R4 rounding == net48.
float il2cpp_linq_list_aggregate_seed_f(Il2CppObject* list, double seed,
                                        Il2CppObject* func, int32_t kind)
{
    RequireList(list);
    return il2cpp_linq_aggregate_seed_f_f(SnapshotOf(list, kind),
                                          static_cast<float>(seed), func);
}

//------------------------------------------------------------------------------
// The (ELEMENT family, ACCUMULATOR family) seed-fold matrix over a List<T>.
//
// The snapshot holds the LIST kind's REAL cells: 4-byte int32 (and 1/2-byte
// narrow/8-bit bool cells), 8-byte int64, 4-byte float, 8-byte double, an
// 8-byte reference slot, or a 16-byte System.Decimal value. The accumulator
// keeps its OWN family and the callback is invoked through the matching typed
// fn-pointer (the generated lambda's ABI: narrow/integral element -> int32,
// int64 element -> int64, float/double element -> widened double, reference
// element -> Il2CppObject*, decimal element -> the 16-byte value struct;
// float accumulator -> float return with double parameters, decimal
// accumulator -> the 16-byte value return).
//
// The old entries delegated to the ARRAY kernels, whose element reader is
// fixed at int32 (FoldI4/FoldI8/FoldD/FoldF) or at the snapshot's own width
// (FoldDD/FoldI8I8/FoldFD), so every NON-DIAGONAL (element, accumulator) pair
// read the wrong number of bytes out of the cell and/or called the lambda
// through a mismatched fn-pointer -- a silent wrong value (List<long> folded
// into an int accumulator read the low 32 bits of every cell, List<float> into
// a double accumulator paired two float cells as one double, ...).
//
// These entries read the element at the LIST kind's real width and keep the
// accumulator in its own type, so every cell of the matrix is exact.
//------------------------------------------------------------------------------
runtime::Il2CppDecimal ReadDec(Il2CppArray* arr, int32_t i)
{
    runtime::Il2CppDecimal v;
    std::memcpy(&v, il2cpp_array_get_elem_ptr(arr, i), sizeof(v));
    return v;
}

// The accumulator/element fn-pointer pairs (leaf = static, This = bound
// instance; the `this` target rides the first slot). NOTE the FLOAT
// accumulator: its value rides the widened double F-stack slot (the generated
// leaf is `float f(double acc, ...)` -- the FAccDFn convention), so the first
// parameter is double, not float; only the RETURN is float.
#define LST_AGG_ABI(TAG, ACC, ELEM)                              \
    typedef ACC (*LstAgg##TAG##Fn)(ACC, ELEM);                   \
    typedef ACC (*LstAgg##TAG##ThisFn)(Il2CppObject*, ACC, ELEM);
#define LST_AGG_ABI_F(TAG, ELEM)                                 \
    typedef float (*LstAgg##TAG##Fn)(double, ELEM);              \
    typedef float (*LstAgg##TAG##ThisFn)(Il2CppObject*, double, ELEM);

LST_AGG_ABI(I4I4, int32_t, int32_t)
LST_AGG_ABI(I4I8, int32_t, int64_t)
LST_AGG_ABI(I4D, int32_t, double)
LST_AGG_ABI(I4Ref, int32_t, Il2CppObject*)
LST_AGG_ABI(I4Dec, int32_t, runtime::Il2CppDecimal)

LST_AGG_ABI(I8I4, int64_t, int32_t)
LST_AGG_ABI(I8I8, int64_t, int64_t)
LST_AGG_ABI(I8D, int64_t, double)
LST_AGG_ABI(I8Ref, int64_t, Il2CppObject*)
LST_AGG_ABI(I8Dec, int64_t, runtime::Il2CppDecimal)

LST_AGG_ABI_F(FI4, int32_t)
LST_AGG_ABI_F(FI8, int64_t)
LST_AGG_ABI_F(FD, double)
LST_AGG_ABI_F(FRef, Il2CppObject*)
LST_AGG_ABI_F(FDec, runtime::Il2CppDecimal)

LST_AGG_ABI(DI4, double, int32_t)
LST_AGG_ABI(DI8, double, int64_t)
LST_AGG_ABI(DD, double, double)
LST_AGG_ABI(DRef, double, Il2CppObject*)
LST_AGG_ABI(DDec, double, runtime::Il2CppDecimal)

LST_AGG_ABI(DecI4, runtime::Il2CppDecimal, int32_t)
LST_AGG_ABI(DecI8, runtime::Il2CppDecimal, int64_t)
LST_AGG_ABI(DecD, runtime::Il2CppDecimal, double)
LST_AGG_ABI(DecRef, runtime::Il2CppDecimal, Il2CppObject*)
LST_AGG_ABI(DecDec, runtime::Il2CppDecimal, runtime::Il2CppDecimal)

LST_AGG_ABI(RefI4, Il2CppObject*, int32_t)
LST_AGG_ABI(RefI8, Il2CppObject*, int64_t)
LST_AGG_ABI(RefD, Il2CppObject*, double)
LST_AGG_ABI(RefRef, Il2CppObject*, Il2CppObject*)
LST_AGG_ABI(RefDec, Il2CppObject*, runtime::Il2CppDecimal)

#undef LST_AGG_ABI
#undef LST_AGG_ABI_F

// One fold step per ACCUMULATOR family: read the element at the list kind's
// real width and call the accumulator's lambda through its own ABI.
static int32_t LstAggStepI4(Il2CppDelegate* d, int32_t acc, Il2CppArray* snap,
                            int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI4I8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI4I8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI4DThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI4DFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI4RefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI4RefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI4DecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI4DecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI4I4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI4I4Fn>(d->methodPtr)(acc, v);
        }
    }
}

static int64_t LstAggStepI8(Il2CppDelegate* d, int64_t acc, Il2CppArray* snap,
                            int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI8I8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI8I8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI8DThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI8DFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI8RefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI8RefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI8DecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI8DecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggI8I4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggI8I4Fn>(d->methodPtr)(acc, v);
        }
    }
}

static float LstAggStepF(Il2CppDelegate* d, float acc, Il2CppArray* snap,
                         int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggFI8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggFI8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggFDThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggFDFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggFRefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggFRefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggFDecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggFDecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggFI4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggFI4Fn>(d->methodPtr)(acc, v);
        }
    }
}

static double LstAggStepD(Il2CppDelegate* d, double acc, Il2CppArray* snap,
                          int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDI8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDI8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDDThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDDFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDRefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDRefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDDecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDDecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDI4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDI4Fn>(d->methodPtr)(acc, v);
        }
    }
}

static runtime::Il2CppDecimal LstAggStepDec(Il2CppDelegate* d, runtime::Il2CppDecimal acc,
                                            Il2CppArray* snap, int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDecI8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDecI8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDecDThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDecDFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDecRefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDecRefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDecDecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDecDecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggDecI4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggDecI4Fn>(d->methodPtr)(acc, v);
        }
    }
}

static Il2CppObject* LstAggStepRef(Il2CppDelegate* d, Il2CppObject* acc, Il2CppArray* snap,
                                   int32_t i, int32_t kind)
{
    switch (kind)
    {
        case IL2CPP_LIST_KIND_I8:
        {
            const int64_t v = ReadI8(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggRefI8ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggRefI8Fn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_F:
        case IL2CPP_LIST_KIND_D:
        {
            const double v = ReadD(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggRefDThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggRefDFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_REF:
        {
            Il2CppObject* v = ReadRef(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggRefRefThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggRefRefFn>(d->methodPtr)(acc, v);
        }
        case IL2CPP_LIST_KIND_DEC:
        {
            const runtime::Il2CppDecimal v = ReadDec(snap, i);
            return d->target != nullptr
                ? reinterpret_cast<LstAggRefDecThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggRefDecFn>(d->methodPtr)(acc, v);
        }
        default:
        {
            const int32_t v = ReadI4(snap, i, kind);
            return d->target != nullptr
                ? reinterpret_cast<LstAggRefI4ThisFn>(d->methodPtr)(d->target, acc, v)
                : reinterpret_cast<LstAggRefI4Fn>(d->methodPtr)(acc, v);
        }
    }
}

// The entry macro: validate/root, snapshot at the list kind, fold.
#define LST_AGG_SEED_ENTRY(NAME, ACC, STEP)                                    \
    ACC il2cpp_linq_list_aggregate_seed_##NAME(Il2CppObject* list, ACC seed,   \
                                               Il2CppObject* func, int32_t kind) \
    {                                                                          \
        RequireList(list);                                                     \
        Il2CppDelegate* d = RequireCallback(func, "func");                     \
        RootScope roots;                                                       \
        roots.Add(reinterpret_cast<void**>(&list));                            \
        Il2CppArray* snap = SnapshotOf(list, kind);                            \
        roots.Add(reinterpret_cast<void**>(&snap));                            \
        ACC acc = seed;                                                        \
        const int32_t n = snap->length;                                        \
        for (int32_t i = 0; i < n; ++i) acc = STEP(d, acc, snap, i, kind);      \
        return acc;                                                            \
    }

// The FLOAT accumulator's seed crosses on the double F stack (a float
// parameter widens exactly), and the accumulator stays float with per-step R4
// rounding (net48).
#define LST_AGG_SEED_ENTRY_F(NAME, STEP)                                       \
    float il2cpp_linq_list_aggregate_seed_##NAME(Il2CppObject* list, double seed, \
                                                 Il2CppObject* func, int32_t kind) \
    {                                                                          \
        RequireList(list);                                                     \
        Il2CppDelegate* d = RequireCallback(func, "func");                     \
        RootScope roots;                                                       \
        roots.Add(reinterpret_cast<void**>(&list));                            \
        Il2CppArray* snap = SnapshotOf(list, kind);                            \
        roots.Add(reinterpret_cast<void**>(&snap));                            \
        float acc = static_cast<float>(seed);                                  \
        const int32_t n = snap->length;                                        \
        for (int32_t i = 0; i < n; ++i) acc = STEP(d, acc, snap, i, kind);      \
        return acc;                                                            \
    }

// The DECIMAL accumulator rides the explicit 16-byte result slot (the decimal
// T3 contract: the slot is the FIRST parameter) and the seed crosses as a
// const pointer to its value slot.
#define LST_AGG_SEED_ENTRY_DEC(NAME, STEP)                                     \
    void il2cpp_linq_list_aggregate_seed_##NAME(                               \
        runtime::Il2CppDecimal* result, Il2CppObject* list,                    \
        const runtime::Il2CppDecimal* seed, Il2CppObject* func, int32_t kind)  \
    {                                                                          \
        RequireList(list);                                                     \
        Il2CppDelegate* d = RequireCallback(func, "func");                     \
        RootScope roots;                                                       \
        roots.Add(reinterpret_cast<void**>(&list));                            \
        Il2CppArray* snap = SnapshotOf(list, kind);                            \
        roots.Add(reinterpret_cast<void**>(&snap));                            \
        runtime::Il2CppDecimal acc = *seed;                                    \
        const int32_t n = snap->length;                                        \
        for (int32_t i = 0; i < n; ++i) acc = STEP(d, acc, snap, i, kind);      \
        std::memcpy(result, &acc, sizeof(acc));                                \
    }

// The REFERENCE accumulator: the accumulator itself is a managed reference,
// so its local is rooted across every callback (a lambda may allocate and
// compact the heap).
#define LST_AGG_SEED_ENTRY_REF(NAME, STEP)                                     \
    Il2CppObject* il2cpp_linq_list_aggregate_seed_##NAME(                      \
        Il2CppObject* list, Il2CppObject* seed, Il2CppObject* func, int32_t kind) \
    {                                                                          \
        RequireList(list);                                                     \
        Il2CppDelegate* d = RequireCallback(func, "func");                     \
        RootScope roots;                                                       \
        roots.Add(reinterpret_cast<void**>(&list));                            \
        Il2CppArray* snap = SnapshotOf(list, kind);                            \
        roots.Add(reinterpret_cast<void**>(&snap));                            \
        Il2CppObject* acc = seed;                                              \
        roots.Add(reinterpret_cast<void**>(&acc));                             \
        const int32_t n = snap->length;                                        \
        for (int32_t i = 0; i < n; ++i) acc = STEP(d, acc, snap, i, kind);      \
        return acc;                                                            \
    }

// int32 accumulator
LST_AGG_SEED_ENTRY(i4_i4, int32_t, LstAggStepI4)
LST_AGG_SEED_ENTRY(i4_i8, int32_t, LstAggStepI4)
LST_AGG_SEED_ENTRY(i4_f, int32_t, LstAggStepI4)
LST_AGG_SEED_ENTRY(i4_d, int32_t, LstAggStepI4)
LST_AGG_SEED_ENTRY(i4_ref, int32_t, LstAggStepI4)
LST_AGG_SEED_ENTRY(i4_dec, int32_t, LstAggStepI4)

// int64 accumulator
LST_AGG_SEED_ENTRY(i8_i4, int64_t, LstAggStepI8)
LST_AGG_SEED_ENTRY(i8_i8, int64_t, LstAggStepI8)
LST_AGG_SEED_ENTRY(i8_f, int64_t, LstAggStepI8)
LST_AGG_SEED_ENTRY(i8_d, int64_t, LstAggStepI8)
LST_AGG_SEED_ENTRY(i8_ref, int64_t, LstAggStepI8)
LST_AGG_SEED_ENTRY(i8_dec, int64_t, LstAggStepI8)

// float accumulator
LST_AGG_SEED_ENTRY_F(f_i4, LstAggStepF)
LST_AGG_SEED_ENTRY_F(f_i8, LstAggStepF)
LST_AGG_SEED_ENTRY_F(f_f, LstAggStepF)
LST_AGG_SEED_ENTRY_F(f_d, LstAggStepF)
LST_AGG_SEED_ENTRY_F(f_ref, LstAggStepF)
LST_AGG_SEED_ENTRY_F(f_dec, LstAggStepF)

// double accumulator
LST_AGG_SEED_ENTRY(d_i4, double, LstAggStepD)
LST_AGG_SEED_ENTRY(d_i8, double, LstAggStepD)
LST_AGG_SEED_ENTRY(d_f, double, LstAggStepD)
LST_AGG_SEED_ENTRY(d_d, double, LstAggStepD)
LST_AGG_SEED_ENTRY(d_ref, double, LstAggStepD)
LST_AGG_SEED_ENTRY(d_dec, double, LstAggStepD)

// decimal accumulator
LST_AGG_SEED_ENTRY_DEC(dec_i4, LstAggStepDec)
LST_AGG_SEED_ENTRY_DEC(dec_i8, LstAggStepDec)
LST_AGG_SEED_ENTRY_DEC(dec_f, LstAggStepDec)
LST_AGG_SEED_ENTRY_DEC(dec_d, LstAggStepDec)
LST_AGG_SEED_ENTRY_DEC(dec_ref, LstAggStepDec)
LST_AGG_SEED_ENTRY_DEC(dec_dec, LstAggStepDec)

// reference accumulator
LST_AGG_SEED_ENTRY_REF(ref_i4, LstAggStepRef)
LST_AGG_SEED_ENTRY_REF(ref_i8, LstAggStepRef)
LST_AGG_SEED_ENTRY_REF(ref_f, LstAggStepRef)
LST_AGG_SEED_ENTRY_REF(ref_d, LstAggStepRef)
LST_AGG_SEED_ENTRY_REF(ref_ref, LstAggStepRef)
LST_AGG_SEED_ENTRY_REF(ref_dec, LstAggStepRef)

#undef LST_AGG_SEED_ENTRY
#undef LST_AGG_SEED_ENTRY_F
#undef LST_AGG_SEED_ENTRY_DEC
#undef LST_AGG_SEED_ENTRY_REF

//------------------------------------------------------------------------------
// Aggregate(seed, func) over a NON-decimal VALUE-TYPE ARRAY source
// (System.DateTime / System.TimeSpan / System.Guid) with an INT32 / INT64 /
// FLOAT / DOUBLE accumulator.
//
// The ARRAY Aggregate arm used to route these to the int32-element base
// kernels (seed_i4/seed_i8/seed_f/seed_d): the fold read 4 bytes out of an
// 8-byte DateTime/TimeSpan cell (a silent wrong value) or 4 bytes out of the
// 16-byte Guid cell, AND called the lambda through an int32 element slot while
// the generated leaf takes the value struct BY VALUE (an ABI mismatch). The
// DECIMAL accumulator family was already wired by impl-aggvt
// (aggregate_seed_{dt,ts,guid}_dec, linq_map.cpp).
//
// These entries read the cell at its real width (LinqCellReadDt/Ts/Guid) and
// call the accumulator family's own leaf ABI. They live in this translation
// unit (with their declarations in linq_list.h) because the shared value-cell
// readers are exported by linq_family.h and the list-domain file is the
// header the generated code already includes for the value-cell helpers.
//------------------------------------------------------------------------------
namespace {

template <typename Acc, typename Param, typename Vt>
Acc FoldVtSeed(Il2CppArray* src, int32_t cell, Acc seed, Il2CppDelegate* d,
               Vt (*read)(Il2CppArray*, int32_t))
{
    LinqRequireCell(src, cell);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    typedef Acc (*Fn)(Param, Vt);
    typedef Acc (*ThisFn)(Il2CppObject*, Param, Vt);
    Acc acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const Vt elem = read(src, i);
        acc = d->target != nullptr
            ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, static_cast<Param>(acc), elem)
            : reinterpret_cast<Fn>(d->methodPtr)(static_cast<Param>(acc), elem);
    }
    return acc;
}

} // namespace

#define VT_SEED_ENTRY(NAME, ACC, PARAM, VT, CELL, READ)                         \
    ACC il2cpp_linq_aggregate_seed_##NAME(Il2CppArray* src, ACC seed,           \
                                          Il2CppObject* func)                   \
    {                                                                           \
        return FoldVtSeed<ACC, PARAM, VT>(src, CELL, seed,                      \
                                          RequireCallback(func, "func"), READ); \
    }

VT_SEED_ENTRY(dt_i4, int32_t, int32_t, runtime::Il2CppDateTime,
              IL2CPP_LINQ_CELL_DATETIME, LinqCellReadDt)
VT_SEED_ENTRY(dt_i8, int64_t, int64_t, runtime::Il2CppDateTime,
              IL2CPP_LINQ_CELL_DATETIME, LinqCellReadDt)
VT_SEED_ENTRY(dt_f, float, double, runtime::Il2CppDateTime,
              IL2CPP_LINQ_CELL_DATETIME, LinqCellReadDt)
VT_SEED_ENTRY(dt_d, double, double, runtime::Il2CppDateTime,
              IL2CPP_LINQ_CELL_DATETIME, LinqCellReadDt)

VT_SEED_ENTRY(ts_i4, int32_t, int32_t, runtime::Il2CppTimeSpan,
              IL2CPP_LINQ_CELL_TIMESPAN, LinqCellReadTs)
VT_SEED_ENTRY(ts_i8, int64_t, int64_t, runtime::Il2CppTimeSpan,
              IL2CPP_LINQ_CELL_TIMESPAN, LinqCellReadTs)
VT_SEED_ENTRY(ts_f, float, double, runtime::Il2CppTimeSpan,
              IL2CPP_LINQ_CELL_TIMESPAN, LinqCellReadTs)
VT_SEED_ENTRY(ts_d, double, double, runtime::Il2CppTimeSpan,
              IL2CPP_LINQ_CELL_TIMESPAN, LinqCellReadTs)

VT_SEED_ENTRY(guid_i4, int32_t, int32_t, Il2CppGuid,
              IL2CPP_LINQ_CELL_GUID, LinqCellReadGuid)
VT_SEED_ENTRY(guid_i8, int64_t, int64_t, Il2CppGuid,
              IL2CPP_LINQ_CELL_GUID, LinqCellReadGuid)
VT_SEED_ENTRY(guid_f, float, double, Il2CppGuid,
              IL2CPP_LINQ_CELL_GUID, LinqCellReadGuid)
VT_SEED_ENTRY(guid_d, double, double, Il2CppGuid,
              IL2CPP_LINQ_CELL_GUID, LinqCellReadGuid)

#undef VT_SEED_ENTRY

Il2CppObject* il2cpp_linq_list_group_by_full(Il2CppObject* list,
                                             Il2CppObject* keySel, Il2CppObject* elemSel,
                                             Il2CppObject* cmp, Il2CppObject* resSel,
                                             int32_t elemKind, int32_t rsKind)
{
    RequireList(list);
    return il2cpp_linq_group_by_full(SnapshotOf(list, IL2CPP_LIST_KIND_I4), keySel,
                                     elemSel, cmp, resSel, elemKind, rsKind);
}

} // namespace il2cpp
