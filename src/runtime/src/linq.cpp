//------------------------------------------------------------------------------
// linq.cpp -- System.Linq array-family icalls (LINQ domain, T1 card).
//
// See linq.h for the contract. Implementation notes:
//   * Element access goes through il2cpp_array_get_elem_ptr (bounds-checked,
//     read-only -- these operations never mutate the source array).
//   * Any/All short-circuit: the loop returns at the first hit / first miss;
//     an empty sequence never calls the predicate. Count(predicate) does NOT
//     short-circuit (it must count every match, net48).
//   * Sum uses CHECKED int32 addition (net48 Enumerable.Sum is checked --
//     D-LINQ-3, verified by csc probe: overflow throws OverflowException).
//   * Min/Max seed from the FIRST element (never a sentinel -- INT32_MIN /
//     INT32_MAX are legal values), so a single-element sequence returns that
//     element and the empty case is rejected before any element is read.
//   * Predicate validation (linq_map.cpp RequireCallback precedent -- the
//     linqib mode): null -> ArgumentNullException, a multicast container ->
//     InvalidOperationException, a non-delegate object -> ArgumentException.
//     INSTANCE-BOUND single-cast delegates (target != null -- a bound instance
//     method like p.IsBig) ARE accepted: the invoke side dispatches per
//     binding, passing the stored target in the generated instance method's
//     `this` slot (delegateext.cpp / linq_map.cpp precedent), so the callback
//     reads the bound instance's payload.
//   * GC SAFETY (physical contract, il2cpp_gc.h): the collector COMPACTS the
//     young generation, so a predicate that allocates can trigger a collection
//     that MOVES the source array. Every predicate loop therefore registers
//     the ADDRESS OF THE SOURCE LOCAL as a static root slot (RootScope) --
//     the collector rewrites the slot on compaction, so re-reading the local
//     after each callback observes the fresh address. The element is passed
//     BY VALUE to the callback, so no managed pointer is held across the call.
//------------------------------------------------------------------------------

#include "linq.h"

#include "il2cpp_array.h"
#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// Source null check: ArgumentNullException ("source"), per the LINQ
// exception contract (docs/design/linq-domain.md section 1.3). Never returns on
// failure.
Il2CppArray* CheckSource(Il2CppArray* src)
{
    if (src == nullptr)
    {
        il2cpp_throw_argument_null_exception("source");
    }
    return src;
}

// The multicast container's class name (delegateext.cpp descriptor).
const char kMulticastClass[] = "System.MulticastDelegate";

// The single-cast delegate class name (delegate.cpp descriptor).
const char kDelegateClass[] = "System.Delegate";

// Predicate validation (linq_map.cpp RequireCallback precedent, the linqib
// mode): null -> ArgumentNullException; a multicast container -> 
// InvalidOperationException (its `first` field sits at the same offset as
// methodPtr, so invoking it as a bare callback would call a data pointer as
// code); a non-delegate object is a caller bug -> ArgumentException.
// INSTANCE-BOUND single-cast delegates ARE accepted -- the delegate carries
// BOTH methodPtr and target (il2cpp_delegate.h), and the invoke side
// dispatches per binding (target in the `this` slot). Returns the validated
// delegate.
Il2CppDelegate* RequireCallback(Il2CppObject* pred)
{
    if (pred == nullptr)
    {
        il2cpp_throw_argument_null_exception("predicate");
        return nullptr; // unreachable (the factory throws)
    }
    const char* name = pred->klass != nullptr ? pred->klass->name : nullptr;
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
    return reinterpret_cast<Il2CppDelegate*>(pred);
}

// Generated-code delegate ABI (il2cpp_delegate.h): a STATIC-method leaf
// (target == null) has the fixed family signature bool(*)(T); an
// INSTANCE-method leaf (target != null) has `this` (the stored target) in
// the FIRST parameter slot (CodeGenerator: instance arg0 = this), so the
// call is (target, args...) -- the linqib dispatch (linq_map.cpp precedent).
typedef bool (*I4PredFn)(int32_t);
typedef bool (*I4PredThisFn)(Il2CppObject*, int32_t);
typedef bool (*RefPredFn)(Il2CppObject*);
typedef bool (*RefPredThisFn)(Il2CppObject*, Il2CppObject*);
typedef int32_t (*I4BinFn)(int32_t, int32_t);
typedef int32_t (*I4BinThisFn)(Il2CppObject*, int32_t, int32_t);

// Per-binding dispatch. The target is re-read through the ROOTED delegate
// slot (d is registered as a static root by every callback entry) so a
// compaction between callbacks (the leaf may allocate) rewrites both d and
// d->target.
bool InvokePredI4(Il2CppDelegate* d, int32_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<I4PredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<I4PredFn>(d->methodPtr)(v);
}

bool InvokePredRef(Il2CppDelegate* d, Il2CppObject* o)
{
    return d->target != nullptr
        ? reinterpret_cast<RefPredThisFn>(d->methodPtr)(d->target, o)
        : reinterpret_cast<RefPredFn>(d->methodPtr)(o);
}

//------------------------------------------------------------------------------
// RootScope -- GC rooting guard for the predicate loops. The source array's
// address is registered as a static root slot so the collector rewrites the
// LOCAL when a compaction moves the array (see the file header). Removal is
// RAII -- the predicate may throw a managed exception, and the roots must not
// leak (gc.cpp root table is fixed-capacity). Slots are re-read through the
// registered variable; never cache a copy of the source pointer across a
// callback invocation.
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
    void** m_slots[2]; // today: delegate + source array
    int32_t m_count;
};

} // namespace

//------------------------------------------------------------------------------
// Type-independent entries (the array is self-describing).
//------------------------------------------------------------------------------

int32_t il2cpp_linq_count(Il2CppArray* src)
{
    return CheckSource(src)->length;
}

bool il2cpp_linq_any(Il2CppArray* src)
{
    return CheckSource(src)->length > 0;
}

//------------------------------------------------------------------------------
// Predicate entries: int32 family (bool(*)(int32_t)).
//------------------------------------------------------------------------------

bool il2cpp_linq_any_i4(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    // The predicate may allocate -> a collection can move 'src' AND the
    // delegate (whose target field is a GC reference); both are rooted and
    // re-read through their slots on every iteration.
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredI4(d, elem)) return true; // short circuit at the first hit
    }
    return false;
}

bool il2cpp_linq_all_i4(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (!InvokePredI4(d, elem)) return false; // short circuit at the first miss
    }
    return true; // empty sequence: vacuous truth
}

//------------------------------------------------------------------------------
// Count(predicate) -- the number of elements satisfying the predicate
// (net48 Enumerable.Count<T>(IEnumerable<T>, Func<T,bool>)). Unlike Any/All
// there is NO short circuit: every element is examined. Empty -> 0 without
// calling the predicate. Same rooting discipline as the other predicate loops.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_count_i4(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t count = 0;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredI4(d, elem)) ++count;
    }
    return count;
}

int32_t il2cpp_linq_sum_i4(Il2CppArray* src)
{
    CheckSource(src);
    const int32_t n = src->length;
    int32_t sum = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        // net48 Enumerable.Sum(int) is CHECKED (D-LINQ-3, csc-probe verified):
        // overflow throws OverflowException. The guard tests the boundaries
        // in-range (time.cpp checked-add pattern), so the addition itself can
        // never overflow; the empty sequence takes no additions -> 0.
        if ((elem > 0 && sum > INT32_MAX - elem) ||
            (elem < 0 && sum < INT32_MIN - elem))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += elem;
    }
    return sum; // empty -> 0
}

int32_t il2cpp_linq_min_i4(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int32_t best = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, 0));
    const int32_t n = src->length;
    for (int32_t i = 1; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem < best) best = elem;
    }
    return best;
}

int32_t il2cpp_linq_max_i4(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int32_t best = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, 0));
    const int32_t n = src->length;
    for (int32_t i = 1; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem > best) best = elem;
    }
    return best;
}

//------------------------------------------------------------------------------
// Predicate entries: reference family (bool(*)(Il2CppObject*)).
//------------------------------------------------------------------------------

bool il2cpp_linq_any_ref(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredRef(d, elem)) return true; // short circuit at the first hit
    }
    return false;
}

bool il2cpp_linq_all_ref(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, i));
        if (!InvokePredRef(d, elem)) return false; // short circuit at the first miss
    }
    return true; // empty sequence: vacuous truth
}

int32_t il2cpp_linq_count_ref(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t count = 0;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* elem = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredRef(d, elem)) ++count;
    }
    return count;
}

//------------------------------------------------------------------------------
// Average on int32[]: the arithmetic mean as a double (net48 Enumerable.Average
// returns double). Empty -> InvalidOperationException. The sum is computed as
// int64 to avoid intermediate overflow (net48 uses a checked long accumulator);
// the final division is double (net48 casts the long sum to double before
// dividing).
//------------------------------------------------------------------------------
double il2cpp_linq_average_i4(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t sum = 0;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        sum += elem;
    }
    return static_cast<double>(sum) / static_cast<double>(n);
}

//------------------------------------------------------------------------------
// Reverse on int32[]: a NEW int32[] with elements in reverse order. The source
// is never modified; empty -> a new empty array. No callback, so the only GC
// interaction is the single allocation: the source is rooted across
// il2cpp_array_new and every element is read BY VALUE through the rooted slot
// (to_array discipline, linq_map.cpp).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_reverse_i4(Il2CppArray* src)
{
    CheckSource(src);
    const int32_t n = src->length;

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));

    Il2CppArray* out = il2cpp_array_new(nullptr, 4, false, n);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, n - 1 - i)) = elem;
    }
    return out;
}

//------------------------------------------------------------------------------
// Last on int32[]: the last element; empty -> InvalidOperationException.
// No allocation, no callback -- no rooting needed (first_i4 precedent).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_last_i4(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    return *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, src->length - 1));
}

// LastOrDefault on int32[]: the last element, or 0 (int32 default) when empty.
int32_t il2cpp_linq_last_or_default_i4(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0
        ? *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, src->length - 1))
        : 0;
}

//------------------------------------------------------------------------------
// ElementAt on int32[]: the element at the given index. index < 0 or
// index >= length -> ArgumentOutOfRangeException (net48).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_element_at_i4(Il2CppArray* src, int32_t index)
{
    CheckSource(src);
    if (index < 0 || index >= src->length)
    {
        il2cpp_throw_argument_out_of_range_exception("index", "Index was out of range.");
    }
    return *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, index));
}

// Contains on int32[]: true iff the sequence contains the given value (net48
// ordinal int32 equality, EqualityComparer<int>.Default). Empty -> false.
bool il2cpp_linq_contains_i4(Il2CppArray* src, int32_t value)
{
    CheckSource(src);
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem == value) return true;
    }
    return false;
}

//------------------------------------------------------------------------------
// Aggregate(source, func) on int32[]: fold the sequence left-to-right using
// the binary func, seeding from the FIRST element (net48 Aggregate without a
// seed). Empty -> InvalidOperationException. The func delegate ABI is
// int32_t(*)(int32_t, int32_t) (static leaf) / int32_t(*)(Il2CppObject*,
// int32_t, int32_t) (instance leaf -- target in the `this` slot). Each step's
// result becomes the next accumulator. The func may allocate -> rooting
// discipline (predicate-loop precedent).
//------------------------------------------------------------------------------
int32_t il2cpp_linq_aggregate_i4(Il2CppArray* src, Il2CppObject* func)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t acc = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, 0));
    const int32_t n = src->length;
    for (int32_t i = 1; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<I4BinThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<I4BinFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

//------------------------------------------------------------------------------
// il2cpp_linq_long_count -- LongCount on int32[]: the number of elements as
// int64 (net48 Enumerable.LongCount returns long). Type-independent (the
// array is self-describing); null source -> ArgumentNullException.
//------------------------------------------------------------------------------
int64_t il2cpp_linq_long_count(Il2CppArray* src)
{
    CheckSource(src);
    return static_cast<int64_t>(src->length);
}

//------------------------------------------------------------------------------
// il2cpp_linq_sequence_equal_i4 -- SequenceEqual on int32[]: true iff both
// arrays have the same length and every pair of elements at the same position
// is equal (net48 ordinal int32 equality). Null source ->
// ArgumentNullException; different lengths -> false.
//------------------------------------------------------------------------------
bool il2cpp_linq_sequence_equal_i4(Il2CppArray* first, Il2CppArray* second)
{
    CheckSource(first);
    CheckSource(second);
    if (first->length != second->length)
    {
        return false;
    }
    const int32_t n = first->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t a = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(first, i));
        const int32_t b = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(second, i));
        if (a != b)
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_linq_default_if_empty_i4 -- DefaultIfEmpty on int32[]: if the source
// is non-empty, return it as-is; if empty, return a NEW single-element
// int32[] containing 0 (the int32 default). Null source ->
// ArgumentNullException.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_default_if_empty_i4(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length > 0)
    {
        return src;
    }
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(int32_t), false, 1);
    *reinterpret_cast<int32_t*>(result->data) = 0;
    return result;
}

} // namespace il2cpp
