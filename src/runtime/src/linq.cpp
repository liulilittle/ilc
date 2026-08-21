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

#include "il2cpp_delegate.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstring>

namespace il2cpp {

namespace {

// Source null check: ArgumentNullException ("source"), per the LINQ
// exception contract (docs/design/linq-domain.md §1.3). Never returns on
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

} // namespace il2cpp
