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
#include "linq_family.h"  // impl-arrayagg: LinqCellRead* (the cell matrix)
#include "decimal.h"      // P0LINQ1: runtime::Il2CppDecimal (the zip value result)
#include "il2cpp_time.h"  // P0LINQ1: runtime::Il2CppDateTime / Il2CppTimeSpan
#include "guid.h"         // P0LINQ1: Il2CppGuid (the zip value result)

#include <cstdio>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <new>
#include <type_traits>
#include <vector>

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
    RootScope() {}
    ~RootScope()
    {
        for (size_t i = m_slots.size(); i > 0; --i)
            il2cpp_gc_remove_static_root(m_slots[i - 1]);
    }
    void Add(void** slot)
    {
        // The slot table grows on demand. A fixed table with an std::abort()
        // overflow check turns a rooting-count mistake into a silent process
        // exit (exit 3, no message, no managed exception) -- List<T>.Except hit
        // exactly that. An allocation failure now surfaces as a managed
        // exception instead.
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
    std::vector<void**> m_slots; // delegate + source array + seed (FoldRef)
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
// nb9 fix P0-4: the source-only aggregates over the WIDENED element families
// (an int64/double/float Select result is a real int64[]/double[]). net48's
// return carriers: Sum/Min/Max keep the element type, Average is double for
// the integer/double families and float for the float family.
//------------------------------------------------------------------------------
int64_t il2cpp_linq_sum_i8(Il2CppArray* src)
{
    CheckSource(src);
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        // net48 Enumerable.Sum(long) is CHECKED.
        if ((elem > 0 && sum > INT64_MAX - elem) ||
            (elem < 0 && sum < INT64_MIN - elem))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += elem;
    }
    return sum;
}

int64_t il2cpp_linq_min_i8(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem < best) best = elem;
    }
    return best;
}

int64_t il2cpp_linq_max_i8(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem > best) best = elem;
    }
    return best;
}

double il2cpp_linq_average_i8(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t sum = 0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if ((elem > 0 && sum > INT64_MAX - elem) ||
            (elem < 0 && sum < INT64_MIN - elem))
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
        sum += elem;
    }
    return static_cast<double>(sum) / static_cast<double>(src->length);
}

double il2cpp_linq_sum_d(Il2CppArray* src)
{
    CheckSource(src);
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
    }
    return sum;
}

double il2cpp_linq_min_d(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double best = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double elem = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem < best) best = elem;
    }
    return best;
}

double il2cpp_linq_max_d(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double best = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double elem = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem > best) best = elem;
    }
    return best;
}

//------------------------------------------------------------------------------
// delta 1: Min/Max over the comparable VALUE families (net48
// Comparer<T>.Default). DateTime array cells are the packed CLR image (62-bit
// ticks + 2-bit kind); the comparisons use TICKS only (the kind field never
// orders -- the CmpDateTimeKey precedent), while the WINNING OPERAND is
// returned unchanged (kind preserved). TimeSpan cells are raw ticks. Guid
// comparison follows the net48 field-wise CompareTo (il2cpp_guid_compare).
// An empty source throws InvalidOperationException like the numeric entries.
//------------------------------------------------------------------------------
namespace {

void RequireLinqCell(Il2CppArray* src, int32_t width)
{
    if (src->isRefElem || src->elemSize != width)
    {
        il2cpp_throw_not_supported_exception(
            "LINQ value-family aggregate: source element width mismatch.");
    }
}

} // namespace

runtime::Il2CppDateTime il2cpp_linq_min_dt(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 8);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t bestBits = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    int64_t bestTicks = bestBits & static_cast<int64_t>(runtime::kDateTimeArrayTickMask);
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t bits = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        const int64_t ticks = bits & static_cast<int64_t>(runtime::kDateTimeArrayTickMask);
        if (ticks < bestTicks) { bestTicks = ticks; bestBits = bits; }
    }
    return runtime::il2cpp_datetime_from_array_bits(bestBits);
}

runtime::Il2CppDateTime il2cpp_linq_max_dt(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 8);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t bestBits = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    int64_t bestTicks = bestBits & static_cast<int64_t>(runtime::kDateTimeArrayTickMask);
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t bits = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        const int64_t ticks = bits & static_cast<int64_t>(runtime::kDateTimeArrayTickMask);
        if (ticks > bestTicks) { bestTicks = ticks; bestBits = bits; }
    }
    return runtime::il2cpp_datetime_from_array_bits(bestBits);
}

runtime::Il2CppTimeSpan il2cpp_linq_min_ts(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 8);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t ticks = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (ticks < best) best = ticks;
    }
    return runtime::Il2CppTimeSpan(best);
}

runtime::Il2CppTimeSpan il2cpp_linq_max_ts(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 8);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    int64_t best = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t ticks = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (ticks > best) best = ticks;
    }
    return runtime::Il2CppTimeSpan(best);
}

Il2CppGuid il2cpp_linq_min_guid(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 16);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    Il2CppGuid best;
    std::memcpy(best.b, il2cpp_array_get_elem_ptr(src, 0), sizeof(best.b));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const uint8_t* elem = reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (il2cpp_guid_compare(elem, best.b) < 0)
        {
            std::memcpy(best.b, elem, sizeof(best.b));
        }
    }
    return best;
}

Il2CppGuid il2cpp_linq_max_guid(Il2CppArray* src)
{
    CheckSource(src);
    RequireLinqCell(src, 16);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    Il2CppGuid best;
    std::memcpy(best.b, il2cpp_array_get_elem_ptr(src, 0), sizeof(best.b));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const uint8_t* elem = reinterpret_cast<const uint8_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (il2cpp_guid_compare(elem, best.b) > 0)
        {
            std::memcpy(best.b, elem, sizeof(best.b));
        }
    }
    return best;
}

double il2cpp_linq_average_d(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
    }
    return sum / static_cast<double>(src->length);
}

// The float family (nb9 H-F2): a float Select materializes a REAL float[], so
// these read 4-byte elements and keep net48's float carriers (Sum/Min/Max ->
// float, Average -> float with a double accumulator).
float il2cpp_linq_sum_f(Il2CppArray* src)
{
    CheckSource(src);
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<double>(*reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, i)));
    }
    return static_cast<float>(sum);
}

float il2cpp_linq_min_f(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    float best = *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const float elem = *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem < best) best = elem;
    }
    return best;
}

float il2cpp_linq_max_f(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    float best = *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const float elem = *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, i));
        if (elem > best) best = elem;
    }
    return best;
}

float il2cpp_linq_average_f(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    double sum = 0.0;
    for (int32_t i = 0; i < src->length; ++i)
    {
        sum += static_cast<double>(*reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, i)));
    }
    return static_cast<float>(sum / static_cast<double>(src->length));
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
// nb15 B5 FIX -- WIDE / NARROW predicate families (see linq.h).
//
// Each entry reads the element cell at its REAL width (arr->elemSize drives
// il2cpp_array_get_elem_ptr, so a 1/2/8-byte cell is never over-read) and
// widens it into the delegate's ABI slot:
//   i8  -> int64_t slot      d/f -> double slot (float widened; a float leaf
//                            returns float, read as float then compared as
//                            nonzero)
//   narrow -> int32_t slot   (sign- or zero-extended per the elemKind code)
// The predicate ABI typedefs mirror the generated instance/static leaves
// (I4PredFn / I4PredThisFn / I8PredFn / I8PredThisFn / DPredFn / DPredThisFn).
//------------------------------------------------------------------------------
namespace {

typedef bool (*I8PredFn)(int64_t);
typedef bool (*I8PredThisFn)(Il2CppObject*, int64_t);
typedef bool (*DPredFn)(double);
typedef bool (*DPredThisFn)(Il2CppObject*, double);

// The float family crosses the boundary as double: ParamTypesCpp maps a
// float parameter to the double slot, and the bool RESULT rides the int32
// slot (ReturnTypeCpp), so a float predicate leaf has the SAME ABI as a
// double one. The entry stays separate so the transpiler can dispatch on the
// metadata family without the runtime guessing.
typedef bool (*FPredFn)(double);
typedef bool (*FPredThisFn)(Il2CppObject*, double);

bool InvokePredI8(Il2CppDelegate* d, int64_t v)
{
    return d->target != nullptr
        ? reinterpret_cast<I8PredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<I8PredFn>(d->methodPtr)(v);
}

bool InvokePredD(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<DPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<DPredFn>(d->methodPtr)(v);
}

bool InvokePredF(Il2CppDelegate* d, double v)
{
    return d->target != nullptr
        ? reinterpret_cast<FPredThisFn>(d->methodPtr)(d->target, v)
        : reinterpret_cast<FPredFn>(d->methodPtr)(v);
}

// Narrow integral cell -> the int32 slot the CLR passes for a narrow parameter.
// Zero- vs sign-extension follows the source element type (elemKind), NEVER a
// guess from the cell width.
// Selector callback shapes for the wide/narrow Select surface
// (il2cpp_linq_select_wide). The element crosses as the family's widened slot;
// the result follows TResult's family. Instance-bound leaves carry `this` in
// the first parameter slot.
typedef int32_t (*SelI4I4Fn2)(int32_t);
typedef int32_t (*SelI4I4ThisFn2)(Il2CppObject*, int32_t);
typedef int64_t (*SelI4I8Fn2)(int32_t);
typedef int64_t (*SelI4I8ThisFn2)(Il2CppObject*, int32_t);
typedef double (*SelI4DFn2)(int32_t);
typedef double (*SelI4DThisFn2)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*SelI4RefFn2)(int32_t);
typedef Il2CppObject* (*SelI4RefThisFn2)(Il2CppObject*, int32_t);

typedef int32_t (*SelI8I4Fn)(int64_t);
typedef int32_t (*SelI8I4ThisFn)(Il2CppObject*, int64_t);
typedef int64_t (*SelI8I8Fn)(int64_t);
typedef int64_t (*SelI8I8ThisFn)(Il2CppObject*, int64_t);
typedef double (*SelI8DFn)(int64_t);
typedef double (*SelI8DThisFn)(Il2CppObject*, int64_t);
typedef Il2CppObject* (*SelI8RefFn)(int64_t);
typedef Il2CppObject* (*SelI8RefThisFn)(Il2CppObject*, int64_t);

typedef int32_t (*SelDI4Fn)(double);
typedef int32_t (*SelDI4ThisFn)(Il2CppObject*, double);
typedef int64_t (*SelDI8Fn)(double);
typedef int64_t (*SelDI8ThisFn)(Il2CppObject*, double);
typedef double (*SelDDFn)(double);
typedef double (*SelDDThisFn)(Il2CppObject*, double);
typedef Il2CppObject* (*SelDRefFn)(double);
typedef Il2CppObject* (*SelDRefThisFn)(Il2CppObject*, double);

typedef int32_t (*SelRefI4Fn2)(Il2CppObject*);
typedef int32_t (*SelRefI4ThisFn2)(Il2CppObject*, Il2CppObject*);
typedef int64_t (*SelRefI8Fn2)(Il2CppObject*);
typedef int64_t (*SelRefI8ThisFn2)(Il2CppObject*, Il2CppObject*);
typedef double (*SelRefDFn2)(Il2CppObject*);
typedef double (*SelRefDThisFn2)(Il2CppObject*, Il2CppObject*);
typedef Il2CppObject* (*SelRefRefFn2)(Il2CppObject*);
typedef Il2CppObject* (*SelRefRefThisFn2)(Il2CppObject*, Il2CppObject*);

// (The Zip selector shapes are NOT typedef'd here: il2cpp_linq_zip_wide
// instantiates the call through the ZipCall<Integer,Integer,Result> template so
// the parameter ABI follows the selector's own statically declared types -- the
// two sources may have different families, and a hand-written per-shape table
// cannot express that. See ZipFill/ZipCall below.)


int32_t NarrowElemAt(Il2CppArray* arr, int32_t index, int32_t elemKind)
{
    char* p = il2cpp_array_get_elem_ptr(arr, index);
    switch (elemKind)
    {
        case IL2CPP_LINQ_NARROW_SBYTE:
            return static_cast<int32_t>(*reinterpret_cast<int8_t*>(p));
        case IL2CPP_LINQ_NARROW_BYTE:
            return static_cast<int32_t>(*reinterpret_cast<uint8_t*>(p));
        case IL2CPP_LINQ_NARROW_BOOL:
            return *reinterpret_cast<uint8_t*>(p) != 0 ? 1 : 0;
        case IL2CPP_LINQ_NARROW_SHORT:
            return static_cast<int32_t>(*reinterpret_cast<int16_t*>(p));
        case IL2CPP_LINQ_NARROW_USHORT:
            return static_cast<int32_t>(*reinterpret_cast<uint16_t*>(p));
        case IL2CPP_LINQ_NARROW_CHAR:
            return static_cast<int32_t>(*reinterpret_cast<char16_t*>(p));
        case IL2CPP_LINQ_NARROW_UINT:
            return static_cast<int32_t>(*reinterpret_cast<uint32_t*>(p));
        default:
            return static_cast<int32_t>(*reinterpret_cast<int32_t*>(p));
    }
}

} // namespace

bool il2cpp_linq_any_i8(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredI8(d, elem)) return true;
    }
    return false;
}

int32_t il2cpp_linq_count_i8(Il2CppArray* src, Il2CppObject* pred)
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
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (InvokePredI8(d, elem)) ++count;
    }
    return count;
}

bool il2cpp_linq_all_i8(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        if (!InvokePredI8(d, elem)) return false;
    }
    return true;
}

// The double family reads the cell by its REAL width: a double[] holds 8-byte
// cells, a float Select result is materialized as a widened double[] (the
// F-stack convention), but a genuine 4-byte float[] cell stays possible --
// il2cpp_array_get_elem_float_as_double is the runtime's authority for that.
bool il2cpp_linq_any_d(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (InvokePredD(d, il2cpp_array_get_elem_float_as_double(src, i))) return true;
    }
    return false;
}

int32_t il2cpp_linq_count_d(Il2CppArray* src, Il2CppObject* pred)
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
        if (InvokePredD(d, il2cpp_array_get_elem_float_as_double(src, i))) ++count;
    }
    return count;
}

bool il2cpp_linq_all_d(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!InvokePredD(d, il2cpp_array_get_elem_float_as_double(src, i))) return false;
    }
    return true;
}

bool il2cpp_linq_any_f(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (InvokePredF(d, il2cpp_array_get_elem_float_as_double(src, i))) return true;
    }
    return false;
}

int32_t il2cpp_linq_count_f(Il2CppArray* src, Il2CppObject* pred)
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
        if (InvokePredF(d, il2cpp_array_get_elem_float_as_double(src, i))) ++count;
    }
    return count;
}

bool il2cpp_linq_all_f(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!InvokePredF(d, il2cpp_array_get_elem_float_as_double(src, i))) return false;
    }
    return true;
}

bool il2cpp_linq_any_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (InvokePredI4(d, NarrowElemAt(src, i, elemKind))) return true;
    }
    return false;
}

int32_t il2cpp_linq_count_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind)
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
        if (InvokePredI4(d, NarrowElemAt(src, i, elemKind))) ++count;
    }
    return count;
}

bool il2cpp_linq_all_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!InvokePredI4(d, NarrowElemAt(src, i, elemKind))) return false;
    }
    return true;
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
// Last / LastOrDefault on a reference-element array (nb15 B5 FIX).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_linq_last_ref(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0)
    {
        il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    }
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, src->length - 1));
}

Il2CppObject* il2cpp_linq_last_or_default_ref(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0
        ? *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, src->length - 1))
        : nullptr;
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
// il2cpp_linq_aggregate_seed_i4 -- Aggregate(source, seed, func) on int32[]:
// fold left-to-right from 'seed' (net48 Enumerable.Aggregate(seed, func)).
// An EMPTY source returns the seed unchanged (net48: supplying a seed removes
// the "Sequence contains no elements" case). The func ABI is the shared
// binary int32 family (see il2cpp_linq_aggregate_i4); the callback may
// allocate, so the rooting discipline is identical.
//------------------------------------------------------------------------------
int32_t il2cpp_linq_aggregate_seed_i4(Il2CppArray* src, int32_t seed, Il2CppObject* func)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);

    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
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

//------------------------------------------------------------------------------
// nb9 FIX BATCH -- Aggregate's accumulator family (see linq.h).
//
// One fold per accumulator ABI; the RESULT selector (`Func<TAccumulate,TResult>`)
// runs ONCE on the final accumulator, after the fold (net48). An empty source
// returns the seed (and then projects it). Rooting: the delegate, the source
// and (for the reference accumulator) the seed/accumulator are rooted across
// every callback.
//------------------------------------------------------------------------------
namespace {

typedef int64_t (*I8BinFn)(int64_t, int32_t);
typedef int64_t (*I8BinThisFn)(Il2CppObject*, int64_t, int32_t);
typedef double (*DBinFn)(double, int32_t);
typedef double (*DBinThisFn)(Il2CppObject*, double, int32_t);
typedef Il2CppObject* (*RefBinFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*RefBinThisFn)(Il2CppObject*, Il2CppObject*, int32_t);

// Func<TAccumulate,TResult> -- the result selector ABI.
typedef int32_t (*AccI4ResFn)(int32_t);
typedef int32_t (*AccI4ResThisFn)(Il2CppObject*, int32_t);
typedef Il2CppObject* (*AccI4ResRefFn)(int32_t);
typedef Il2CppObject* (*AccI4ResRefThisFn)(Il2CppObject*, int32_t);
typedef int32_t (*AccI8ResFn)(int64_t);
typedef int32_t (*AccI8ResThisFn)(Il2CppObject*, int64_t);
typedef Il2CppObject* (*AccI8ResRefFn)(int64_t);
typedef Il2CppObject* (*AccI8ResRefThisFn)(Il2CppObject*, int64_t);
typedef int32_t (*AccDResFn)(double);
typedef int32_t (*AccDResThisFn)(Il2CppObject*, double);
typedef Il2CppObject* (*AccDResRefFn)(double);
typedef Il2CppObject* (*AccDResRefThisFn)(Il2CppObject*, double);
typedef int32_t (*AccRefResFn)(Il2CppObject*);
typedef int32_t (*AccRefResThisFn)(Il2CppObject*, Il2CppObject*);
typedef Il2CppObject* (*AccRefResRefFn)(Il2CppObject*);
typedef Il2CppObject* (*AccRefResRefThisFn)(Il2CppObject*, Il2CppObject*);

int64_t FoldI8(Il2CppArray* src, int64_t seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int64_t acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<I8BinThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<I8BinFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

double FoldD(Il2CppArray* src, double seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<DBinThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<DBinFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

Il2CppObject* FoldRef(Il2CppArray* src, Il2CppObject* seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&seed));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        seed = d->target != nullptr
            ? reinterpret_cast<RefBinThisFn>(d->methodPtr)(d->target, seed, elem)
            : reinterpret_cast<RefBinFn>(d->methodPtr)(seed, elem);
    }
    return seed;
}

// nb9 H-B6: the ref-accumulator folds per SOURCE element family. The old
// FoldRef always read a 4-byte int32 element and passed it in the int32 slot,
// so a string[]/double[]/long[] source crashed (an Il2CppObject* slot read as
// int32) or silently produced zeros. TSource decides the element ABI.
typedef Il2CppObject* (*RefBinRefFn)(Il2CppObject*, Il2CppObject*);
typedef Il2CppObject* (*RefBinRefThisFn)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
typedef Il2CppObject* (*RefBinI8Fn)(Il2CppObject*, int64_t);
typedef Il2CppObject* (*RefBinI8ThisFn)(Il2CppObject*, Il2CppObject*, int64_t);
typedef Il2CppObject* (*RefBinDFn)(Il2CppObject*, double);
typedef Il2CppObject* (*RefBinDThisFn)(Il2CppObject*, Il2CppObject*, double);

Il2CppObject* FoldRefRef(Il2CppArray* src, Il2CppObject* seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&seed));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        Il2CppObject* elem = reinterpret_cast<Il2CppObject**>(src->data)[i];
        seed = d->target != nullptr
            ? reinterpret_cast<RefBinRefThisFn>(d->methodPtr)(d->target, seed, elem)
            : reinterpret_cast<RefBinRefFn>(d->methodPtr)(seed, elem);
    }
    return seed;
}

Il2CppObject* FoldRefI8(Il2CppArray* src, Il2CppObject* seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&seed));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = *reinterpret_cast<const int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        seed = d->target != nullptr
            ? reinterpret_cast<RefBinI8ThisFn>(d->methodPtr)(d->target, seed, elem)
            : reinterpret_cast<RefBinI8Fn>(d->methodPtr)(seed, elem);
    }
    return seed;
}

Il2CppObject* FoldRefD(Il2CppArray* src, Il2CppObject* seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&seed));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = *reinterpret_cast<const double*>(il2cpp_array_get_elem_ptr(src, i));
        seed = d->target != nullptr
            ? reinterpret_cast<RefBinDThisFn>(d->methodPtr)(d->target, seed, elem)
            : reinterpret_cast<RefBinDFn>(d->methodPtr)(seed, elem);
    }
    return seed;
}

Il2CppObject* FoldRefF(Il2CppArray* src, Il2CppObject* seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    roots.Add(reinterpret_cast<void**>(&seed));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = static_cast<double>(
            *reinterpret_cast<const float*>(il2cpp_array_get_elem_ptr(src, i)));
        seed = d->target != nullptr
            ? reinterpret_cast<RefBinDThisFn>(d->methodPtr)(d->target, seed, elem)
            : reinterpret_cast<RefBinDFn>(d->methodPtr)(seed, elem);
    }
    return seed;
}



int32_t ProjectI4(Il2CppDelegate* d, int32_t acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccI4ResThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccI4ResFn>(d->methodPtr)(acc);
}

Il2CppObject* ProjectI4Ref(Il2CppDelegate* d, int32_t acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccI4ResRefThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccI4ResRefFn>(d->methodPtr)(acc);
}

int32_t ProjectI8(Il2CppDelegate* d, int64_t acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccI8ResThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccI8ResFn>(d->methodPtr)(acc);
}

Il2CppObject* ProjectI8Ref(Il2CppDelegate* d, int64_t acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccI8ResRefThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccI8ResRefFn>(d->methodPtr)(acc);
}

int32_t ProjectD(Il2CppDelegate* d, double acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccDResThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccDResFn>(d->methodPtr)(acc);
}

Il2CppObject* ProjectDRef(Il2CppDelegate* d, double acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccDResRefThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccDResRefFn>(d->methodPtr)(acc);
}

int32_t ProjectRef(Il2CppDelegate* d, Il2CppObject* acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccRefResThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccRefResFn>(d->methodPtr)(acc);
}

Il2CppObject* ProjectRefRef(Il2CppDelegate* d, Il2CppObject* acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccRefResRefThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccRefResRefFn>(d->methodPtr)(acc);
}

//------------------------------------------------------------------------------
// nb9 H-F3: the WIDE result-selector projectors. TResult numeric follows the
// RESULT family: int64_t for long, float for float (the managed leaf returns
// C++ float -- widening it into a double slot is an exact conversion), double
// for double. The old mapping always used the int32 projector, so a long
// TResult was a truncated int32 and a float TResult read the float return as
// an integer bit pattern.
//------------------------------------------------------------------------------
template <typename Res, typename Acc>
Res ProjectTyped(Il2CppDelegate* d, Acc acc)
{
    typedef Res (*LeafFn)(Acc);
    typedef Res (*ThisFn)(Il2CppObject*, Acc);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<LeafFn>(d->methodPtr)(acc);
}

int64_t ProjectI4I8(Il2CppDelegate* d, int32_t acc) { return ProjectTyped<int64_t, int32_t>(d, acc); }
float ProjectI4F(Il2CppDelegate* d, int32_t acc) { return ProjectTyped<float, int32_t>(d, acc); }
double ProjectI4D(Il2CppDelegate* d, int32_t acc) { return ProjectTyped<double, int32_t>(d, acc); }
int64_t ProjectI8I8(Il2CppDelegate* d, int64_t acc) { return ProjectTyped<int64_t, int64_t>(d, acc); }
float ProjectI8F(Il2CppDelegate* d, int64_t acc) { return ProjectTyped<float, int64_t>(d, acc); }
double ProjectI8D(Il2CppDelegate* d, int64_t acc) { return ProjectTyped<double, int64_t>(d, acc); }
int64_t ProjectDI8(Il2CppDelegate* d, double acc) { return ProjectTyped<int64_t, double>(d, acc); }
float ProjectDF(Il2CppDelegate* d, double acc) { return ProjectTyped<float, double>(d, acc); }
double ProjectDD(Il2CppDelegate* d, double acc) { return ProjectTyped<double, double>(d, acc); }
int64_t ProjectRefI8(Il2CppDelegate* d, Il2CppObject* acc) { return ProjectTyped<int64_t, Il2CppObject*>(d, acc); }
float ProjectRefF(Il2CppDelegate* d, Il2CppObject* acc) { return ProjectTyped<float, Il2CppObject*>(d, acc); }
double ProjectRefD(Il2CppDelegate* d, Il2CppObject* acc) { return ProjectTyped<double, Il2CppObject*>(d, acc); }
int64_t ProjectFI8(Il2CppDelegate* d, double acc) { return ProjectTyped<int64_t, double>(d, acc); }
float ProjectFF(Il2CppDelegate* d, double acc) { return ProjectTyped<float, double>(d, acc); }
double ProjectFD(Il2CppDelegate* d, double acc) { return ProjectTyped<double, double>(d, acc); }

} // namespace

int64_t il2cpp_linq_aggregate_seed_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldI8(src, seed, RequireCallback(func));
}

double il2cpp_linq_aggregate_seed_d(Il2CppArray* src, double seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldD(src, seed, RequireCallback(func));
}

Il2CppObject* il2cpp_linq_aggregate_seed_ref(Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldRef(src, seed, RequireCallback(func));
}

int32_t il2cpp_linq_aggregate_seed_rs_i4(Il2CppArray* src, int32_t seed,
                                         Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectI4(rd, il2cpp_linq_aggregate_seed_i4(src, seed, reinterpret_cast<Il2CppObject*>(fd)));
}

Il2CppObject* il2cpp_linq_aggregate_seed_rs_ref(Il2CppArray* src, int32_t seed,
                                                Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectI4Ref(rd, il2cpp_linq_aggregate_seed_i4(src, seed, reinterpret_cast<Il2CppObject*>(fd)));
}

int32_t il2cpp_linq_aggregate_seed_i8_rs_i4(Il2CppArray* src, int64_t seed,
                                            Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectI8(rd, FoldI8(src, seed, fd));
}

Il2CppObject* il2cpp_linq_aggregate_seed_i8_rs_ref(Il2CppArray* src, int64_t seed,
                                                   Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectI8Ref(rd, FoldI8(src, seed, fd));
}

int32_t il2cpp_linq_aggregate_seed_d_rs_i4(Il2CppArray* src, double seed,
                                           Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectD(rd, FoldD(src, seed, fd));
}

Il2CppObject* il2cpp_linq_aggregate_seed_d_rs_ref(Il2CppArray* src, double seed,
                                                  Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectDRef(rd, FoldD(src, seed, fd));
}

int32_t il2cpp_linq_aggregate_seed_ref_rs_i4(Il2CppArray* src, Il2CppObject* seed,
                                             Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    Il2CppObject* acc = FoldRef(src, seed, fd);
    // nb9 fix M1: an exception-safe per-call root (the manual add/remove pair
    // leaked a static-root slot whenever the selector threw).
    RootScope accRoots;
    accRoots.Add(reinterpret_cast<void**>(&acc));
    return ProjectRef(rd, acc);
}

Il2CppObject* il2cpp_linq_aggregate_seed_ref_rs_ref(Il2CppArray* src, Il2CppObject* seed,
                                                    Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    Il2CppObject* acc = FoldRef(src, seed, fd);
    RootScope accRoots;
    accRoots.Add(reinterpret_cast<void**>(&acc));
    return ProjectRefRef(rd, acc);
}

//------------------------------------------------------------------------------
// nb9 H-F3: the WIDE result-selector wrappers. TResult decides the return
// carrier: int64_t (long), double (float and double -- a float selector's C++
// float return is widened exactly), or the reference slot. The old mapping
// called the *_rs_i4 entry for every numeric TResult, so long was truncated
// and float read the float bits as int32.
//------------------------------------------------------------------------------
#define NB9_AGG_RS_INT(NAME, SEEDTYPE, ACCEXPR, PROJ)                              \
    int64_t NAME##_i8(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,         \
                      Il2CppObject* resSel)                                        \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ##I8(rd, ACCEXPR);                                                  \
    }                                                                              \
    double NAME##_f(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,           \
                    Il2CppObject* resSel)                                          \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return static_cast<double>(PROJ##F(rd, ACCEXPR));                      \
    }                                                                              \
    double NAME##_d(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,           \
                    Il2CppObject* resSel)                                          \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ##D(rd, ACCEXPR);                                          \
    }

NB9_AGG_RS_INT(il2cpp_linq_aggregate_seed_rs, int32_t,
               il2cpp_linq_aggregate_seed_i4(src, seed, reinterpret_cast<Il2CppObject*>(fd)),
               ProjectI4)
NB9_AGG_RS_INT(il2cpp_linq_aggregate_seed_i8_rs, int64_t,
               FoldI8(src, seed, fd), ProjectI8)
NB9_AGG_RS_INT(il2cpp_linq_aggregate_seed_d_rs, double,
               FoldD(src, seed, fd), ProjectD)
#undef NB9_AGG_RS_INT

// The reference-accumulator wide selectors (the accumulator must stay rooted).
int64_t il2cpp_linq_aggregate_seed_ref_rs_i8(Il2CppArray* src, Il2CppObject* seed,
                                             Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    Il2CppObject* acc = FoldRef(src, seed, fd);
    RootScope accRoots;
    accRoots.Add(reinterpret_cast<void**>(&acc));
    return ProjectRefI8(rd, acc);
}

double il2cpp_linq_aggregate_seed_ref_rs_f(Il2CppArray* src, Il2CppObject* seed,
                                           Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    Il2CppObject* acc = FoldRef(src, seed, fd);
    RootScope accRoots;
    accRoots.Add(reinterpret_cast<void**>(&acc));
    return static_cast<double>(ProjectRefF(rd, acc));
}

double il2cpp_linq_aggregate_seed_ref_rs_d(Il2CppArray* src, Il2CppObject* seed,
                                           Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    Il2CppObject* acc = FoldRef(src, seed, fd);
    RootScope accRoots;
    accRoots.Add(reinterpret_cast<void**>(&acc));
    return ProjectRefD(rd, acc);
}

//------------------------------------------------------------------------------
// nb9 fix P0-1: the FLOAT accumulator channel and the widened-source kernels.
//
// The transpiler puts float on the IL F stack, so a float-returning lambda is
// emitted as `float f(double, int32_t)` / `float f(double, double)` -- the
// accumulator arrives as double, the result is float. The old mapping folded
// float into the double family, i.e. the float return was read back as a
// double and the upper 32 bits were garbage (10.5000019418076 vs 10.5).
//
// Also here: the no-seed and widened-source variants of the int64/double
// folds (an int64/double/float Select result is a real int64[]/double[]).
//------------------------------------------------------------------------------
namespace {

typedef float (*FAccI4Fn)(double, int32_t);
typedef float (*FAccI4ThisFn)(Il2CppObject*, double, int32_t);
typedef float (*FAccDFn)(double, double);
typedef float (*FAccDThisFn)(Il2CppObject*, double, double);
typedef int64_t (*I8BinI8Fn)(int64_t, int64_t);
typedef int64_t (*I8BinI8ThisFn)(Il2CppObject*, int64_t, int64_t);
typedef double (*DBinDFn)(double, double);
typedef double (*DBinDThisFn)(Il2CppObject*, double, double);

// Func<float,TResult> -- the float result-selector ABI (the parameter rides
// the F stack as double).
typedef int32_t (*AccFResFn)(double);
typedef int32_t (*AccFResThisFn)(Il2CppObject*, double);
typedef Il2CppObject* (*AccFResRefFn)(double);
typedef Il2CppObject* (*AccFResRefThisFn)(Il2CppObject*, double);

// Fold a float accumulator over an int32-element source.
float FoldF(Il2CppArray* src, float seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    float acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int32_t elem = *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<FAccI4ThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<FAccI4Fn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

// Fold a float accumulator over a double-element source (a float Select
// result is materialized as double[]). floatfix: the element read adapts to
// the array's OWN elemSize -- a REAL float[] (4-byte cells, nb9 H-F2) and
// the 8-byte double carrier both feed the same kernel
// (il2cpp_array_get_elem_float_as_double); a raw double reinterpret read
// pairs of float cells (a silent wrong value + an OOB tail read).
float FoldFD(Il2CppArray* src, float seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    float acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = il2cpp_array_get_elem_float_as_double(src, i);
        acc = d->target != nullptr
            ? reinterpret_cast<FAccDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<FAccDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

// Fold a float accumulator over a REAL float-element source (nb9 H-F2: the
// float Select materializes float[]; the callback ABI is unchanged -- the
// float parameter still rides the F stack as double).
float FoldFF(Il2CppArray* src, float seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    float acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const float elem = *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<FAccDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<FAccDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

int64_t FoldI8I8(Il2CppArray* src, int64_t seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int64_t acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<I8BinI8ThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<I8BinI8Fn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

double FoldDD(Il2CppArray* src, double seed, Il2CppDelegate* d)
{
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<DBinDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<DBinDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

int32_t ProjectF(Il2CppDelegate* d, float acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccFResThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccFResFn>(d->methodPtr)(acc);
}

Il2CppObject* ProjectFRef(Il2CppDelegate* d, float acc)
{
    return d->target != nullptr
        ? reinterpret_cast<AccFResRefThisFn>(d->methodPtr)(d->target, acc)
        : reinterpret_cast<AccFResRefFn>(d->methodPtr)(acc);
}

} // namespace

// nb9 H-F3: the float-accumulator WIDE result selectors (int64 / float /
// double TResult). Defined after FoldF/ProjectF (they live in the float helper
// namespace above).
// nb9 H-B6: the ref-accumulator entries per SOURCE element family.
//   _refsrc  TSource is a reference (string/object/class) -- element pointer
//   _i8src   TSource is long
//   _dsrc    TSource is double
//   _fsrc    TSource is float (widened to the double F-stack carrier)
// The base il2cpp_linq_aggregate_seed_ref remains the int32 source.
//------------------------------------------------------------------------------
#define NB9_AGG_REF_SRC(SRC, FOLDEXPR)                                             \
    Il2CppObject* il2cpp_linq_aggregate_seed_ref_##SRC(Il2CppArray* src,           \
                                                       Il2CppObject* seed,         \
                                                       Il2CppObject* func)         \
    {                                                                              \
        CheckSource(src);                                                          \
        return FOLDEXPR(src, seed, RequireCallback(func));                         \
    }                                                                              \
    int32_t il2cpp_linq_aggregate_seed_ref_##SRC##_rs_i4(                          \
        Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func,                  \
        Il2CppObject* resSel)                                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        Il2CppObject* acc = FOLDEXPR(src, seed, fd);                               \
        RootScope accRoots;                                                        \
        accRoots.Add(reinterpret_cast<void**>(&acc));                              \
        return ProjectRef(rd, acc);                                                \
    }                                                                              \
    Il2CppObject* il2cpp_linq_aggregate_seed_ref_##SRC##_rs_ref(                   \
        Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func,                  \
        Il2CppObject* resSel)                                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        Il2CppObject* acc = FOLDEXPR(src, seed, fd);                               \
        RootScope accRoots;                                                        \
        accRoots.Add(reinterpret_cast<void**>(&acc));                              \
        return ProjectRefRef(rd, acc);                                             \
    }                                                                              \
    int64_t il2cpp_linq_aggregate_seed_ref_##SRC##_rs_i8(                          \
        Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func,                  \
        Il2CppObject* resSel)                                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        Il2CppObject* acc = FOLDEXPR(src, seed, fd);                               \
        RootScope accRoots;                                                        \
        accRoots.Add(reinterpret_cast<void**>(&acc));                              \
        return ProjectRefI8(rd, acc);                                              \
    }                                                                              \
    double il2cpp_linq_aggregate_seed_ref_##SRC##_rs_f(                            \
        Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func,                  \
        Il2CppObject* resSel)                                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        Il2CppObject* acc = FOLDEXPR(src, seed, fd);                               \
        RootScope accRoots;                                                        \
        accRoots.Add(reinterpret_cast<void**>(&acc));                              \
        return static_cast<double>(ProjectRefF(rd, acc));                          \
    }                                                                              \
    double il2cpp_linq_aggregate_seed_ref_##SRC##_rs_d(                            \
        Il2CppArray* src, Il2CppObject* seed, Il2CppObject* func,                  \
        Il2CppObject* resSel)                                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        Il2CppObject* acc = FOLDEXPR(src, seed, fd);                               \
        RootScope accRoots;                                                        \
        accRoots.Add(reinterpret_cast<void**>(&acc));                              \
        return ProjectRefD(rd, acc);                                               \
    }

NB9_AGG_REF_SRC(refsrc, FoldRefRef)
NB9_AGG_REF_SRC(i8src, FoldRefI8)
NB9_AGG_REF_SRC(dsrc, FoldRefD)
NB9_AGG_REF_SRC(fsrc, FoldRefF)

#undef NB9_AGG_REF_SRC

#define NB9_AGG_NUM_SRC(NAME, SEEDTYPE, FOLDEXPR, PROJ)                             \
    int32_t NAME##_rs_i4(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,       \
                         Il2CppObject* resSel)                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ(rd, FOLDEXPR(src, seed, fd));                                  \
    }                                                                              \
    Il2CppObject* NAME##_rs_ref(Il2CppArray* src, SEEDTYPE seed,                   \
                                Il2CppObject* func, Il2CppObject* resSel)          \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ##Ref(rd, FOLDEXPR(src, seed, fd));                             \
    }                                                                              \
    int64_t NAME##_rs_i8(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,       \
                         Il2CppObject* resSel)                                      \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ##I8(rd, FOLDEXPR(src, seed, fd));                              \
    }                                                                              \
    double NAME##_rs_f(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,         \
                       Il2CppObject* resSel)                                       \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return static_cast<double>(PROJ##F(rd, FOLDEXPR(src, seed, fd)));          \
    }                                                                              \
    double NAME##_rs_d(Il2CppArray* src, SEEDTYPE seed, Il2CppObject* func,         \
                       Il2CppObject* resSel)                                       \
    {                                                                              \
        CheckSource(src);                                                          \
        Il2CppDelegate* fd = RequireCallback(func);                                \
        Il2CppDelegate* rd = RequireCallback(resSel);                              \
        RootScope roots;                                                           \
        roots.Add(reinterpret_cast<void**>(&rd));                                  \
        return PROJ##D(rd, FOLDEXPR(src, seed, fd));                               \
    }

NB9_AGG_NUM_SRC(il2cpp_linq_aggregate_seed_i8_i8, int64_t, FoldI8I8, ProjectI8)
NB9_AGG_NUM_SRC(il2cpp_linq_aggregate_seed_d_d, double, FoldDD, ProjectD)

#undef NB9_AGG_NUM_SRC

int64_t il2cpp_linq_aggregate_seed_f_rs_i8(Il2CppArray* src, float seed,
                                           Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectFI8(rd, FoldF(src, seed, fd));
}

double il2cpp_linq_aggregate_seed_f_rs_f(Il2CppArray* src, float seed,
                                         Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return static_cast<double>(ProjectFF(rd, FoldF(src, seed, fd)));
}

double il2cpp_linq_aggregate_seed_f_rs_d(Il2CppArray* src, float seed,
                                         Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectFD(rd, FoldF(src, seed, fd));
}

// No-seed Aggregate over the WIDENED source families (the accumulator IS
// TSource): the first element seeds the fold.
int64_t il2cpp_linq_aggregate_i8(Il2CppArray* src, Il2CppObject* func)
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
    int64_t acc = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const int64_t elem = *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<I8BinI8ThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<I8BinI8Fn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

double il2cpp_linq_aggregate_d(Il2CppArray* src, Il2CppObject* func)
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
    double acc = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double elem = *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<DBinDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<DBinDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

float il2cpp_linq_aggregate_f_d(Il2CppArray* src, Il2CppObject* func)
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
    // floatfix: the element read adapts to the array's OWN elemSize (a REAL
    // float[] is 4-byte cells; a float Select carrier is 8-byte doubles).
    float acc = static_cast<float>(il2cpp_array_get_elem_float_as_double(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const double elem = il2cpp_array_get_elem_float_as_double(src, i);
        acc = d->target != nullptr
            ? reinterpret_cast<FAccDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<FAccDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

// nb9 H-F2: no-seed float aggregate over a REAL float[] source.
float il2cpp_linq_aggregate_f_f(Il2CppArray* src, Il2CppObject* func)
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
    float acc = *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(src, 0));
    for (int32_t i = 1; i < src->length; ++i)
    {
        const float elem = *reinterpret_cast<float*>(il2cpp_array_get_elem_ptr(src, i));
        acc = d->target != nullptr
            ? reinterpret_cast<FAccDThisFn>(d->methodPtr)(d->target, acc, elem)
            : reinterpret_cast<FAccDFn>(d->methodPtr)(acc, elem);
    }
    return acc;
}

float il2cpp_linq_aggregate_seed_f(Il2CppArray* src, float seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldF(src, seed, RequireCallback(func));
}

float il2cpp_linq_aggregate_seed_f_d(Il2CppArray* src, float seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldFD(src, seed, RequireCallback(func));
}

// nb9 H-F2: a float accumulator over a REAL float[] source (the float Select).
float il2cpp_linq_aggregate_seed_f_f(Il2CppArray* src, float seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldFF(src, seed, RequireCallback(func));
}

int64_t il2cpp_linq_aggregate_seed_i8_i8(Il2CppArray* src, int64_t seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldI8I8(src, seed, RequireCallback(func));
}

double il2cpp_linq_aggregate_seed_d_d(Il2CppArray* src, double seed, Il2CppObject* func)
{
    CheckSource(src);
    return FoldDD(src, seed, RequireCallback(func));
}

int32_t il2cpp_linq_aggregate_seed_f_rs_i4(Il2CppArray* src, float seed,
                                           Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectF(rd, FoldF(src, seed, fd));
}

Il2CppObject* il2cpp_linq_aggregate_seed_f_rs_ref(Il2CppArray* src, float seed,
                                                  Il2CppObject* func, Il2CppObject* resSel)
{
    CheckSource(src);
    Il2CppDelegate* fd = RequireCallback(func);
    Il2CppDelegate* rd = RequireCallback(resSel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&rd));
    return ProjectFRef(rd, FoldF(src, seed, fd));
}

//------------------------------------------------------------------------------
// impl-arrayagg: the ARRAY (element CELL, accumulator family) seed-fold
// matrix (see linq.h). The source cell code decides how the element is read
// and which leaf ABI the accumulator callback uses; the accumulator keeps its
// OWN family across every step.
//------------------------------------------------------------------------------
namespace {

// One fold step for a numeric/decimal/reference ACCUMULATOR: the leaf is
// Acc(*)(Acc, ELEM) (static) or Acc(*)(Il2CppObject*, Acc, ELEM) (bound).
template <typename Acc, typename Elem>
Acc CellAggStep(Il2CppDelegate* d, Acc acc, Elem elem)
{
    typedef Acc (*Fn)(Acc, Elem);
    typedef Acc (*ThisFn)(Il2CppObject*, Acc, Elem);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, acc, elem)
        : reinterpret_cast<Fn>(d->methodPtr)(acc, elem);
}

// The FLOAT accumulator: the value rides the widened double F-stack slot, so
// the leaf's first parameter is double and only the RETURN is float.
template <typename AccParam, typename AccRet, typename Elem>
AccRet CellAggStepF(Il2CppDelegate* d, AccParam acc, Elem elem)
{
    typedef AccRet (*Fn)(AccParam, Elem);
    typedef AccRet (*ThisFn)(Il2CppObject*, AccParam, Elem);
    return d->target != nullptr
        ? reinterpret_cast<ThisFn>(d->methodPtr)(d->target, acc, elem)
        : reinterpret_cast<Fn>(d->methodPtr)(acc, elem);
}

template <typename Acc>
Acc CellAggNext(Il2CppDelegate* d, Acc acc, Il2CppArray* src, int32_t i,
                int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_I8:
            return CellAggStep<Acc, int64_t>(d, acc, LinqCellReadI8(src, i, cell));
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
            return CellAggStep<Acc, double>(d, acc, LinqCellReadD(src, i, cell));
        case IL2CPP_LINQ_CELL_REF:
            return CellAggStep<Acc, Il2CppObject*>(
                d, acc, LinqCellReadRef(src, i, cell));
        case IL2CPP_LINQ_CELL_DECIMAL:
            return CellAggStep<Acc, runtime::Il2CppDecimal>(
                d, acc, LinqCellReadDec(src, i));
        default:
            return CellAggStep<Acc, int32_t>(
                d, acc, LinqCellReadI4(src, i, cell));
    }
}

float CellAggNextF(Il2CppDelegate* d, float acc, Il2CppArray* src, int32_t i,
                   int32_t cell)
{
    switch (cell)
    {
        case IL2CPP_LINQ_CELL_I8:
            return CellAggStepF<double, float, int64_t>(
                d, acc, LinqCellReadI8(src, i, cell));
        case IL2CPP_LINQ_CELL_F:
        case IL2CPP_LINQ_CELL_D:
            return CellAggStepF<double, float, double>(
                d, acc, LinqCellReadD(src, i, cell));
        case IL2CPP_LINQ_CELL_REF:
            return CellAggStepF<double, float, Il2CppObject*>(
                d, acc, LinqCellReadRef(src, i, cell));
        case IL2CPP_LINQ_CELL_DECIMAL:
            return CellAggStepF<double, float, runtime::Il2CppDecimal>(
                d, acc, LinqCellReadDec(src, i));
        default:
            return CellAggStepF<double, float, int32_t>(
                d, acc, LinqCellReadI4(src, i, cell));
    }
}

} // namespace

int32_t il2cpp_linq_aggregate_seed_cell_i4(Il2CppArray* src, int32_t seed,
                                           Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int32_t acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i) acc = CellAggNext<int32_t>(d, acc, src, i, cell);
    return acc;
}

int64_t il2cpp_linq_aggregate_seed_cell_i8(Il2CppArray* src, int64_t seed,
                                           Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    int64_t acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i) acc = CellAggNext<int64_t>(d, acc, src, i, cell);
    return acc;
}

float il2cpp_linq_aggregate_seed_cell_f(Il2CppArray* src, double seed,
                                        Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    float acc = static_cast<float>(seed);
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i) acc = CellAggNextF(d, acc, src, i, cell);
    return acc;
}

double il2cpp_linq_aggregate_seed_cell_d(Il2CppArray* src, double seed,
                                         Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    double acc = seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i) acc = CellAggNext<double>(d, acc, src, i, cell);
    return acc;
}

void il2cpp_linq_aggregate_seed_cell_dec(runtime::Il2CppDecimal* result,
                                         Il2CppArray* src,
                                         const runtime::Il2CppDecimal* seed,
                                         Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    runtime::Il2CppDecimal acc = *seed;
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
        acc = CellAggNext<runtime::Il2CppDecimal>(d, acc, src, i, cell);
    std::memcpy(result, &acc, sizeof(acc));
}

Il2CppObject* il2cpp_linq_aggregate_seed_cell_ref(Il2CppArray* src,
                                                  Il2CppObject* seed,
                                                  Il2CppObject* func, int32_t cell)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(func);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppObject* acc = seed;
    roots.Add(reinterpret_cast<void**>(&acc));
    const int32_t n = src->length;
    for (int32_t i = 0; i < n; ++i)
        acc = CellAggNext<Il2CppObject*>(d, acc, src, i, cell);
    return acc;
}

//------------------------------------------------------------------------------
// nb9 fix P0-4: ToArray over ANY element family. The clone follows the
// SOURCE array's own element layout (elemSize / isRefElem), so int32/int64/
// double/reference results round-trip byte-exactly. The int32-only form
// (il2cpp_linq_to_array) remains for the int32 entry points.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_to_array_family(Il2CppArray* src)
{
    CheckSource(src);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&src));
    Il2CppArray* out = il2cpp_array_new(nullptr, src->elemSize, src->isRefElem, src->length);
    // A3/B3: the clone must carry the SOURCE's recorded element kind. Without
    // it an 8-byte cell becomes ambiguous again (long[]/double[] cloned by
    // ToArray would lose their kind -> Array.Sort rejects them and string.Join
    // falls back to the raw int64 formatting).
    out->elemKind = src->elemKind;
    roots.Add(reinterpret_cast<void**>(&out));
    if (src->length == 0)
    {
        return out;
    }
    if (src->isRefElem)
    {
        for (int32_t i = 0; i < src->length; ++i)
        {
            Il2CppObject* v = *reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(src, i));
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), v);
            *slot = v;
        }
    }
    else
    {
        // Pure value copy, no allocation inside.
        std::memcpy(out->data, src->data,
                    static_cast<size_t>(src->length) * static_cast<size_t>(src->elemSize));
    }
    return out;
}

//------------------------------------------------------------------------------
// nb15 B5 r3 -- Where / Select / First / FirstOrDefault / Last / LastOrDefault
// over the WIDE (int64/double/float) and NARROW (byte/sbyte/short/ushort/char/
// bool/uint) element families.
//
// These were DECLARED in linq.h but never defined: any call to them produced an
// "undefined reference" at link time (the transpiler emits them from
// il2cpp_linq_where_{i8,d,f,narrow} etc.). Each entry reads the element at its
// REAL cell width -- a 1/2-byte narrow cell is sign- or zero-extended into the
// int32 delegate slot, never read as a 4-byte int32 -- and a Where result keeps
// the SOURCE family's element layout AND elemKind (a double[] result without
// elemKind=4 prints as its IEEE754 bit pattern through string.Join).
//------------------------------------------------------------------------------
namespace {

// The result-array element kind of a value family (1 int32, 2 int64, 3 single,
// 4 double, 5 uint32); 0 for the narrow 1/2-byte families (unambiguous).
int32_t WideResultKind(int32_t family)
{
    switch (family)
    {
        case 1: return 1; // int32
        case 2: return 2; // int64
        case 3: return 3; // single
        case 4: return 4; // double
        case 5: return 5; // uint32
        default: return 0;
    }
}

// The narrow family's result kind. It MUST stay 0: the narrow cells are 1/2
// bytes, and string.Join's ValueArrayElement consults elemKind FIRST -- kind 1
// would make it read a 4-byte int32 out of a 1/2-byte cell (byte{2,200} printed
// 51202,-2147483448). With kind 0 the elemSize switch picks the right reader.
int32_t NarrowResultKind(int32_t elemKind)
{
    (void)elemKind;
    return 0;
}

// Read an element of the WIDE families through the family's own ABI slot.
int64_t WideElemI8(Il2CppArray* src, int32_t i)
{
    return *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(src, i));
}

double WideElemD(Il2CppArray* src, int32_t i)
{
    return il2cpp_array_get_elem_float_as_double(src, i);
}

// Allocate a Where result of the SOURCE family (same cell width + same kind).
Il2CppArray* AllocWideLike(Il2CppArray* proto, int32_t length, int32_t resultKind)
{
    if (proto->isRefElem)
        return il2cpp_array_new(nullptr, proto->elemSize, true, length);
    Il2CppArray* arr = il2cpp_array_new_typed(proto->elemSize, resultKind, length);
    return arr;
}

} // namespace

Il2CppArray* il2cpp_linq_where_i8(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    Il2CppArray* scratch = AllocWideLike(src, n, WideResultKind(2));
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const int64_t elem = WideElemI8(src, i);
        if (!InvokePredI8(d, elem)) continue;
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(scratch, w)) = elem;
        ++w;
    }
    if (w == n) return scratch;
    Il2CppArray* out = AllocWideLike(src, w, WideResultKind(2));
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
        *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(scratch, i));
    return out;
}

Il2CppArray* il2cpp_linq_where_d(Il2CppArray* src, Il2CppObject* pred)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    Il2CppArray* scratch = AllocWideLike(src, n, WideResultKind(4));
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = WideElemD(src, i);
        if (!InvokePredD(d, elem)) continue;
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(scratch, w)) = elem;
        ++w;
    }
    if (w == n) return scratch;
    Il2CppArray* out = AllocWideLike(src, w, WideResultKind(4));
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
        *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(scratch, i));
    return out;
}

Il2CppArray* il2cpp_linq_where_f(Il2CppArray* src, Il2CppObject* pred)
{
    // The float family's predicate ABI is the double slot (a float parameter
    // widens exactly); the RESULT keeps the source cell width/kind.
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    const int32_t kind = src->elemKind != 0 ? src->elemKind : 3;
    Il2CppArray* scratch = AllocWideLike(src, n, WideResultKind(3));
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        const double elem = WideElemD(src, i);
        if (!InvokePredF(d, elem)) continue;
        std::memcpy(il2cpp_array_get_elem_ptr(scratch, w),
                    il2cpp_array_get_elem_ptr(src, i),
                    static_cast<size_t>(src->elemSize));
        ++w;
    }
    (void)kind;
    if (w == n) return scratch;
    Il2CppArray* out = AllocWideLike(src, w, WideResultKind(3));
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
        std::memcpy(il2cpp_array_get_elem_ptr(out, i),
                    il2cpp_array_get_elem_ptr(scratch, i),
                    static_cast<size_t>(src->elemSize));
    return out;
}

Il2CppArray* il2cpp_linq_where_narrow(Il2CppArray* src, Il2CppObject* pred, int32_t elemKind)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(pred);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));
    const int32_t n = src->length;
    Il2CppArray* scratch = AllocWideLike(src, n, NarrowResultKind(elemKind));
    roots.Add(reinterpret_cast<void**>(&scratch));
    int32_t w = 0;
    for (int32_t i = 0; i < n; ++i)
    {
        if (!InvokePredI4(d, NarrowElemAt(src, i, elemKind))) continue;
        std::memcpy(il2cpp_array_get_elem_ptr(scratch, w),
                    il2cpp_array_get_elem_ptr(src, i),
                    static_cast<size_t>(src->elemSize));
        ++w;
    }
    if (w == n) return scratch;
    Il2CppArray* out = AllocWideLike(src, w, NarrowResultKind(elemKind));
    roots.Add(reinterpret_cast<void**>(&out));
    for (int32_t i = 0; i < w; ++i)
        std::memcpy(il2cpp_array_get_elem_ptr(out, i),
                    il2cpp_array_get_elem_ptr(scratch, i),
                    static_cast<size_t>(src->elemSize));
    return out;
}

//------------------------------------------------------------------------------
// Select over a wide/narrow source: (srcKind, resKind) select the read and the
// write family (0 int32 / 1 reference / 2 int64 / 3 float / 4 double).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_linq_select_wide(Il2CppArray* src, Il2CppObject* sel,
                                     int32_t kind, int32_t resKind)
{
    CheckSource(src);
    Il2CppDelegate* d = RequireCallback(sel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&src));

    int32_t resSize = 4;
    bool resIsRef = false;
    int32_t resElemKind = 1;
    switch (resKind)
    {
        case 1: resSize = static_cast<int32_t>(sizeof(void*)); resIsRef = true; resElemKind = 0; break;
        case 2: resSize = 8; resElemKind = 2; break;
        case 3: resSize = 8; resElemKind = 3; break; // float -> double carrier
        case 4: resSize = 8; resElemKind = 4; break;
        default: resSize = 4; resElemKind = 1; break;
    }
    Il2CppArray* out = resIsRef
        ? il2cpp_array_new(nullptr, resSize, true, src->length)
        : il2cpp_array_new_typed(resSize, resElemKind, src->length);
    roots.Add(reinterpret_cast<void**>(&out));

    for (int32_t i = 0; i < src->length; ++i)
    {
        // Read the SOURCE at its real width, widened into the selector's slot.
        int32_t vi = 0;
        int64_t v64 = 0;
        double vd = 0.0;
        Il2CppObject* vr = nullptr;
        switch (kind)
        {
            case 2: v64 = WideElemI8(src, i); break;
            case 3:
            case 4: vd = WideElemD(src, i); break;
            case 1: vr = *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(src, i)); break;
            default: vi = NarrowElemAt(src, i, kind); break; // narrow cell codes
        }

        switch (resKind)
        {
            case 1:
            {
                Il2CppObject* r;
                if (kind == 1)
                    r = d->target != nullptr
                        ? reinterpret_cast<SelRefRefThisFn2>(d->methodPtr)(d->target, vr)
                        : reinterpret_cast<SelRefRefFn2>(d->methodPtr)(vr);
                else if (kind == 2)
                    r = d->target != nullptr
                        ? reinterpret_cast<SelI8RefThisFn>(d->methodPtr)(d->target, v64)
                        : reinterpret_cast<SelI8RefFn>(d->methodPtr)(v64);
                else if (kind == 3 || kind == 4)
                    r = d->target != nullptr
                        ? reinterpret_cast<SelDRefThisFn>(d->methodPtr)(d->target, vd)
                        : reinterpret_cast<SelDRefFn>(d->methodPtr)(vd);
                else
                    r = d->target != nullptr
                        ? reinterpret_cast<SelI4RefThisFn2>(d->methodPtr)(d->target, vi)
                        : reinterpret_cast<SelI4RefFn2>(d->methodPtr)(vi);
                Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                    il2cpp_array_get_elem_ptr(out, i));
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
                *slot = r;
                break;
            }
            case 2:
            {
                int64_t r = kind == 2
                    ? (d->target != nullptr
                        ? reinterpret_cast<SelI8I8ThisFn>(d->methodPtr)(d->target, v64)
                        : reinterpret_cast<SelI8I8Fn>(d->methodPtr)(v64))
                    : kind == 3 || kind == 4
                        ? (d->target != nullptr
                            ? reinterpret_cast<SelDI8ThisFn>(d->methodPtr)(d->target, vd)
                            : reinterpret_cast<SelDI8Fn>(d->methodPtr)(vd))
                        : (d->target != nullptr
                            ? reinterpret_cast<SelI4I8ThisFn2>(d->methodPtr)(d->target, vi)
                            : reinterpret_cast<SelI4I8Fn2>(d->methodPtr)(vi));
                *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
                break;
            }
            case 3:
            case 4:
            {
                double r = kind == 2
                    ? (d->target != nullptr
                        ? reinterpret_cast<SelI8DThisFn>(d->methodPtr)(d->target, v64)
                        : reinterpret_cast<SelI8DFn>(d->methodPtr)(v64))
                    : kind == 3 || kind == 4
                        ? (d->target != nullptr
                            ? reinterpret_cast<SelDDThisFn>(d->methodPtr)(d->target, vd)
                            : reinterpret_cast<SelDDFn>(d->methodPtr)(vd))
                        : (d->target != nullptr
                            ? reinterpret_cast<SelI4DThisFn2>(d->methodPtr)(d->target, vi)
                            : reinterpret_cast<SelI4DFn2>(d->methodPtr)(vi));
                *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) = r;
                break;
            }
            default:
            {
                int32_t r = kind == 2
                    ? (d->target != nullptr
                        ? reinterpret_cast<SelI8I4ThisFn>(d->methodPtr)(d->target, v64)
                        : reinterpret_cast<SelI8I4Fn>(d->methodPtr)(v64))
                    : kind == 3 || kind == 4
                        ? (d->target != nullptr
                            ? reinterpret_cast<SelDI4ThisFn>(d->methodPtr)(d->target, vd)
                            : reinterpret_cast<SelDI4Fn>(d->methodPtr)(vd))
                        : (d->target != nullptr
                            ? reinterpret_cast<SelI4I4ThisFn2>(d->methodPtr)(d->target, vi)
                            : reinterpret_cast<SelI4I4Fn2>(d->methodPtr)(vi));
                *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) = r;
                break;
            }
        }
    }
    return out;
}

//------------------------------------------------------------------------------
// First / FirstOrDefault / Last / LastOrDefault over the wide/narrow families.
// Empty source: the *Or*Default forms return the family default (0 / 0.0); the
// plain forms throw InvalidOperationException (net48).
//------------------------------------------------------------------------------
int64_t il2cpp_linq_first_i8(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemI8(src, 0);
}

int64_t il2cpp_linq_first_or_default_i8(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemI8(src, 0) : 0;
}

double il2cpp_linq_first_f(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemD(src, 0);
}

double il2cpp_linq_first_or_default_f(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemD(src, 0) : 0.0;
}

double il2cpp_linq_first_d(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemD(src, 0);
}

double il2cpp_linq_first_or_default_d(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemD(src, 0) : 0.0;
}

int32_t il2cpp_linq_first_narrow(Il2CppArray* src, int32_t elemKind)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return NarrowElemAt(src, 0, elemKind);
}

int32_t il2cpp_linq_first_or_default_narrow(Il2CppArray* src, int32_t elemKind)
{
    CheckSource(src);
    return src->length != 0 ? NarrowElemAt(src, 0, elemKind) : 0;
}

int64_t il2cpp_linq_last_i8(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemI8(src, src->length - 1);
}

int64_t il2cpp_linq_last_or_default_i8(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemI8(src, src->length - 1) : 0;
}

double il2cpp_linq_last_f(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemD(src, src->length - 1);
}

double il2cpp_linq_last_or_default_f(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemD(src, src->length - 1) : 0.0;
}

double il2cpp_linq_last_d(Il2CppArray* src)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return WideElemD(src, src->length - 1);
}

double il2cpp_linq_last_or_default_d(Il2CppArray* src)
{
    CheckSource(src);
    return src->length != 0 ? WideElemD(src, src->length - 1) : 0.0;
}

int32_t il2cpp_linq_last_narrow(Il2CppArray* src, int32_t elemKind)
{
    CheckSource(src);
    if (src->length == 0) il2cpp_throw_invalid_operation_exception("Sequence contains no elements");
    return NarrowElemAt(src, src->length - 1, elemKind);
}

int32_t il2cpp_linq_last_or_default_narrow(Il2CppArray* src, int32_t elemKind)
{
    CheckSource(src);
    return src->length != 0 ? NarrowElemAt(src, src->length - 1, elemKind) : 0;
}

//------------------------------------------------------------------------------
// Zip over the WIDE / NARROW families (see linq.h). The two sources may have
// DIFFERENT families (List<double>.Zip(int[])), so each side is read at its OWN
// width and the selector is called through the ABI of its STATICALLY DECLARED
// parameter types. CodeGenerator maps a delegate parameter to int32_t for the
// int32 slot and EVERY narrow family, to int64_t for i8, and to double for
// float/double (the F-stack carrier) -- so a (double, int32_t) thunk called
// through a double(*)(double, double) pointer would put the second argument in
// XMM1 while the callee reads a GPR. That is the silent garbage this
// per-combination table exists to prevent (measured before the fix:
// List<double>.Zip(List<int>) returned 774904112,774904112 instead of
// 11.5,22.5).
//------------------------------------------------------------------------------
int32_t ZipSideWidthCode(int32_t side)
{
    // 0 = int32 slot, 1 = int64, 2 = double carrier, 3 = 16-byte decimal.
    return side == IL2CPP_LINQ_ZIP_I8 ? 1
         : (side == IL2CPP_LINQ_ZIP_F || side == IL2CPP_LINQ_ZIP_D) ? 2
         : side == IL2CPP_LINQ_ZIP_DEC ? 3 : 0;
}

// Read element `i` as the int32 slot. The narrow codes (0..6) disambiguate the
// 1/2-byte cells -- elemKind is 0 for every narrow family this runtime
// materializes (ValueElemKindFor), so the DECLARED side is the only authority
// there; a typed elemKind (the wide families) wins when present.
int32_t ZipReadI4(Il2CppArray* arr, int32_t i, int32_t side)
{
    char* p = il2cpp_array_get_elem_ptr(arr, i);
    switch (arr->elemKind)
    {
        case 1: return *reinterpret_cast<int32_t*>(p);
        case 2: return static_cast<int32_t>(*reinterpret_cast<int64_t*>(p));
        case 3: return static_cast<int32_t>(*reinterpret_cast<float*>(p));
        case 4: return static_cast<int32_t>(*reinterpret_cast<double*>(p));
        case 5: return static_cast<int32_t>(*reinterpret_cast<uint32_t*>(p));
        default: return NarrowElemAt(arr, i, side); // narrow codes or the I4 default
    }
}

int64_t ZipReadI8(Il2CppArray* arr, int32_t i, int32_t side)
{
    char* p = il2cpp_array_get_elem_ptr(arr, i);
    switch (arr->elemKind)
    {
        case 2: case 6: case 7: return *reinterpret_cast<int64_t*>(p);
        case 1: return static_cast<int64_t>(*reinterpret_cast<int32_t*>(p));
        case 4: return static_cast<int64_t>(*reinterpret_cast<double*>(p));
        case 3: return static_cast<int64_t>(*reinterpret_cast<float*>(p));
        default: break;
    }
    if (arr->elemSize == 8) return *reinterpret_cast<int64_t*>(p);
    return static_cast<int64_t>(NarrowElemAt(arr, i, side));
}

// Read element `i` as the double carrier. elemKind comes FIRST: a 4-byte
// single cell and an 8-byte double cell are indistinguishable from the width
// alone, and this runtime's own FLOAT-result arrays are 8-byte cells carrying
// elemKind 3 with the float's bits in the low 4 bytes -- reading one of those
// as a double would take the higher undefined bits as part of the value.
double ZipReadD(Il2CppArray* arr, int32_t i, int32_t side)
{
    char* p = il2cpp_array_get_elem_ptr(arr, i);
    switch (arr->elemKind)
    {
        case 4: return *reinterpret_cast<double*>(p);
        case 3: return static_cast<double>(*reinterpret_cast<float*>(p));
        case 1: return static_cast<double>(*reinterpret_cast<int32_t*>(p));
        case 2: case 6: case 7: return static_cast<double>(*reinterpret_cast<int64_t*>(p));
        case 5: return static_cast<double>(*reinterpret_cast<uint32_t*>(p));
        default: break;
    }
    if (arr->elemSize == 8) return *reinterpret_cast<double*>(p);
    if (arr->elemSize == 4)
        return side == IL2CPP_LINQ_ZIP_I4
            ? static_cast<double>(*reinterpret_cast<int32_t*>(p))
            : static_cast<double>(*reinterpret_cast<float*>(p));
    return static_cast<double>(NarrowElemAt(arr, i, side));
}

template <typename T>
T ZipReadAs(Il2CppArray* arr, int32_t i, int32_t side);

template <>
inline int32_t ZipReadAs<int32_t>(Il2CppArray* arr, int32_t i, int32_t side)
{
    return ZipReadI4(arr, i, side);
}

template <>
inline int64_t ZipReadAs<int64_t>(Il2CppArray* arr, int32_t i, int32_t side)
{
    return ZipReadI8(arr, i, side);
}

template <>
inline double ZipReadAs<double>(Il2CppArray* arr, int32_t i, int32_t side)
{
    return ZipReadD(arr, i, side);
}

template <>
inline runtime::Il2CppDecimal ZipReadAs<runtime::Il2CppDecimal>(Il2CppArray* arr, int32_t i, int32_t side)
{
    (void)side;
    return *reinterpret_cast<runtime::Il2CppDecimal*>(il2cpp_array_get_elem_ptr(arr, i));
}

// Call the selector through the C signature of its declared parameter types
// (T1, T2) and the result carrier R.
template <typename T1, typename T2, typename R>
R ZipCall(Il2CppDelegate* d, T1 a, T2 b)
{
    if (d->target != nullptr)
        return reinterpret_cast<R (*)(Il2CppObject*, T1, T2)>(d->methodPtr)(d->target, a, b);
    return reinterpret_cast<R (*)(T1, T2)>(d->methodPtr)(a, b);
}

template <typename T1, typename T2>
void ZipFill(Il2CppArray* first, Il2CppArray* second, Il2CppDelegate* d,
             int32_t firstSide, int32_t secondSide, int32_t resKind,
             Il2CppArray* out, int32_t n)
{
    for (int32_t i = 0; i < n; ++i)
    {
        const T1 a = ZipReadAs<T1>(first, i, firstSide);
        const T2 b = ZipReadAs<T2>(second, i, secondSide);
        if (resKind == 6)
        {
            // A reference TResult: the selector returns Il2CppObject* (a
            // reference cell, write-barriered).
            Il2CppObject* r = ZipCall<T1, T2, Il2CppObject*>(d, a, b);
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
            *slot = r;
        }
        else if (resKind == 2)
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, int64_t>(d, a, b);
        else if (resKind == 3 || resKind == 4)
            // float rides the double carrier: an 8-byte cell whose low 4 bytes
            // are the float's own bits (il2cpp_linq_select_wide, resKind 3).
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, double>(d, a, b);
        else if (resKind == 13)
        {
            // System.Decimal result: the 16-byte struct returned by value.
            const runtime::Il2CppDecimal r = ZipCall<T1, T2, runtime::Il2CppDecimal>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else if (resKind == 14)
        {
            // System.DateTime / System.TimeSpan result: the 8-byte value struct.
            const runtime::Il2CppDateTime r = ZipCall<T1, T2, runtime::Il2CppDateTime>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else if (resKind == 15)
        {
            // System.Guid result: the 16-byte raw guid image.
            const Il2CppGuid r = ZipCall<T1, T2, Il2CppGuid>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else
            // int32 and uint32 share the 4-byte cell (the caller typed it);
            // the uint32 thunk returns uint32_t, ABI-identical to int32_t.
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, int32_t>(d, a, b);
    }
}

//------------------------------------------------------------------------------
// BATCH4: the reference-source / narrow-result Zip surface.
//
// Enumerable.Zip over a reference-element source (string[] / object[] / class[])
// reads each element as Il2CppObject* and calls the selector through the mixed
// ABI. The result cell ABI is wider than the old 4/8-byte-only surface:
//   resKind 6   a reference TResult -> a REFERENCE array (Il2CppObject* cells)
//   resKind 7   sbyte   -> a 1-byte cell
//   resKind 8   byte    -> a 1-byte cell
//   resKind 9   short   -> a 2-byte cell
//   resKind 10  ushort  -> a 2-byte cell
//   resKind 11  char    -> a 2-byte cell
//   resKind 12  bool    -> a 1-byte cell
// The value-result codes (0 int32 / 2 int64 / 3 float / 4 double / 5 uint32)
// are unchanged; the float code still rides the 8-byte double carrier.
//------------------------------------------------------------------------------
template <typename T>
T ZipReadParam(Il2CppArray* arr, int32_t i, int32_t side)
{
    // A reference-element array yields Il2CppObject*; a value array yields its
    // own width (the ZIP side code).
    if constexpr (std::is_pointer<T>::value)
        return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(arr, i));
    else
        return ZipReadAs<T>(arr, i, side);
}

// Fill `out` by pairing `first[i]` and `second[i]` through the selector. T1/T2
// are the selector's STATICALLY DECLARED parameter types (Il2CppObject* for a
// reference side, int32_t/int64_t/double for a value side); R follows resKind.
template <typename T1, typename T2>
void ZipFillMixed(Il2CppArray* first, Il2CppArray* second, Il2CppDelegate* d,
                  int32_t firstSide, int32_t secondSide, int32_t resKind,
                  Il2CppArray* out, int32_t n)
{
    for (int32_t i = 0; i < n; ++i)
    {
        const T1 a = ZipReadParam<T1>(first, i, firstSide);
        const T2 b = ZipReadParam<T2>(second, i, secondSide);
        if (resKind == 6)
        {
            Il2CppObject* r = ZipCall<T1, T2, Il2CppObject*>(d, a, b);
            Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(out, i));
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), r);
            *slot = r;
        }
        else if (resKind == 2)
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, int64_t>(d, a, b);
        else if (resKind == 3 || resKind == 4)
            *reinterpret_cast<double*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, double>(d, a, b);
        else if (resKind == 5)
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                static_cast<int32_t>(ZipCall<T1, T2, uint32_t>(d, a, b));
        else if (resKind == 13)
        {
            // System.Decimal result: the selector returns the 16-byte struct by
            // value; copy it into the cell (the decimal T3 contract).
            const runtime::Il2CppDecimal r = ZipCall<T1, T2, runtime::Il2CppDecimal>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else if (resKind == 14)
        {
            // System.DateTime / System.TimeSpan result: the 8-byte runtime value
            // struct returned by value.
            const runtime::Il2CppDateTime r = ZipCall<T1, T2, runtime::Il2CppDateTime>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else if (resKind == 15)
        {
            // System.Guid result: the 16-byte raw guid image returned by value.
            const Il2CppGuid r = ZipCall<T1, T2, Il2CppGuid>(d, a, b);
            std::memcpy(il2cpp_array_get_elem_ptr(out, i), &r, sizeof(r));
        }
        else if (resKind >= 7)
        {
            // A narrow result: the CLR widens a narrow return to the int32
            // register, so read it as int32 and store the low bytes.
            const int32_t r = ZipCall<T1, T2, int32_t>(d, a, b);
            char* p = il2cpp_array_get_elem_ptr(out, i);
            if (out->elemSize == 1)
                *reinterpret_cast<uint8_t*>(p) = static_cast<uint8_t>(r);
            else
                *reinterpret_cast<uint16_t*>(p) = static_cast<uint16_t>(r);
        }
        else
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(out, i)) =
                ZipCall<T1, T2, int32_t>(d, a, b);
    }
}

Il2CppArray* il2cpp_linq_zip_wide(Il2CppArray* first, Il2CppArray* second,
                                  Il2CppObject* sel, int32_t firstSide,
                                  int32_t secondSide, int32_t resKind)
{
    CheckSource(first);
    CheckSource(second);
    Il2CppDelegate* d = RequireCallback(sel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    const int32_t n = first->length < second->length ? first->length : second->length;
    int32_t resSize = 4;
    int32_t resElemKind = 1;
    bool valResult = false; // a 16/8-byte VALUE cell (decimal / DateTime / Guid)
    bool refResult = false;
    switch (resKind)
    {
        case 2: resSize = 8; resElemKind = 2; break;
        case 3: resSize = 8; resElemKind = 3; break; // float -> double carrier
        case 4: resSize = 8; resElemKind = 4; break;
        case 5: resSize = 4; resElemKind = 5; break; // uint32
        case 6: refResult = true; resSize = 8; resElemKind = 0; break;
        // 13 System.Decimal (16-byte) / 14 DateTime|TimeSpan (8-byte) /
        // 15 Guid (16-byte): the selector returns the struct by value.
        case 13: valResult = true; resSize = 16; resElemKind = 0; break;
        case 14: valResult = true; resSize = 8; resElemKind = 0; break;
        case 15: valResult = true; resSize = 16; resElemKind = 0; break;
        default: resSize = 4; resElemKind = 1; break;
    }
    Il2CppArray* out = refResult
        ? il2cpp_array_new(nullptr, resSize, true, n)
        : (valResult ? il2cpp_array_new(nullptr, resSize, false, n)
                     : il2cpp_array_new_typed(resSize, resElemKind, n));
    roots.Add(reinterpret_cast<void**>(&out));

    const int32_t c1 = ZipSideWidthCode(firstSide);
    const int32_t c2 = ZipSideWidthCode(secondSide);
    if (c1 == 0)
    {
        if (c2 == 0)
            ZipFill<int32_t, int32_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 1)
            ZipFill<int32_t, int64_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 3)
            ZipFill<int32_t, runtime::Il2CppDecimal>(first, second, d, firstSide, secondSide, resKind, out, n);
        else
            ZipFill<int32_t, double>(first, second, d, firstSide, secondSide, resKind, out, n);
    }
    else if (c1 == 1)
    {
        if (c2 == 0)
            ZipFill<int64_t, int32_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 1)
            ZipFill<int64_t, int64_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 3)
            ZipFill<int64_t, runtime::Il2CppDecimal>(first, second, d, firstSide, secondSide, resKind, out, n);
        else
            ZipFill<int64_t, double>(first, second, d, firstSide, secondSide, resKind, out, n);
    }
    else if (c1 == 3)
    {
        if (c2 == 0)
            ZipFill<runtime::Il2CppDecimal, int32_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 1)
            ZipFill<runtime::Il2CppDecimal, int64_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 3)
            ZipFill<runtime::Il2CppDecimal, runtime::Il2CppDecimal>(first, second, d, firstSide, secondSide, resKind, out, n);
        else
            ZipFill<runtime::Il2CppDecimal, double>(first, second, d, firstSide, secondSide, resKind, out, n);
    }
    else
    {
        if (c2 == 0)
            ZipFill<double, int32_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 1)
            ZipFill<double, int64_t>(first, second, d, firstSide, secondSide, resKind, out, n);
        else if (c2 == 3)
            ZipFill<double, runtime::Il2CppDecimal>(first, second, d, firstSide, secondSide, resKind, out, n);
        else
            ZipFill<double, double>(first, second, d, firstSide, secondSide, resKind, out, n);
    }
    return out;
}

// The reference-source / narrow-result Zip entry. `firstKind` / `secondKind` are
// the IL2CPP_LIST_KIND_* codes from the MethodSpec element bytes; a reference
// element byte maps to IL2CPP_LIST_KIND_REF. `resKind` is the TResult cell code
// (see the ZipFillMixed comment).
Il2CppArray* il2cpp_linq_zip_mixed(Il2CppArray* first, Il2CppArray* second,
                                   Il2CppObject* sel, int32_t firstKind,
                                   int32_t secondKind, int32_t resKind)
{
    CheckSource(first);
    CheckSource(second);
    Il2CppDelegate* d = RequireCallback(sel);
    RootScope roots;
    roots.Add(reinterpret_cast<void**>(&d));
    roots.Add(reinterpret_cast<void**>(&first));
    roots.Add(reinterpret_cast<void**>(&second));

    const bool aRef = first->isRefElem;
    const bool bRef = second->isRefElem;
    const int32_t n = first->length < second->length ? first->length : second->length;

    // Result cell ABI.
    int32_t resSize = 4;
    int32_t resElemKind = 1;
    bool refResult = false;
    bool valResult = false; // a 16/8-byte VALUE cell (decimal / DateTime / Guid)
    switch (resKind)
    {
        case 2: resSize = 8; resElemKind = 2; break;
        case 3: resSize = 8; resElemKind = 3; break;
        case 4: resSize = 8; resElemKind = 4; break;
        case 5: resSize = 4; resElemKind = 5; break;
        case 6: refResult = true; resSize = 8; resElemKind = 0; break;
        case 7: case 8: case 12: resSize = 1; resElemKind = 0; break;
        case 9: case 10: case 11: resSize = 2; resElemKind = 0; break;
        // 13 System.Decimal (16-byte cell) / 14 DateTime|TimeSpan (8-byte) /
        // 15 Guid (16-byte): the selector returns the struct by value.
        case 13: valResult = true; resSize = 16; resElemKind = 0; break;
        case 14: valResult = true; resSize = 8; resElemKind = 0; break;
        case 15: valResult = true; resSize = 16; resElemKind = 0; break;
        default: resSize = 4; resElemKind = 1; break;
    }
    Il2CppArray* out = refResult
        ? il2cpp_array_new(nullptr, resSize, true, n)
        : (valResult ? il2cpp_array_new(nullptr, resSize, false, n)
                     : il2cpp_array_new_typed(resSize, resElemKind, n));
    roots.Add(reinterpret_cast<void**>(&out));

    // The value side's ZIP width code (-1 for a reference side, which reads
    // Il2CppObject* directly and needs no side code).
    const int32_t c1 = aRef ? -1 : ZipSideWidthCode(ZipSideOfListKind(firstKind));
    const int32_t c2 = bRef ? -1 : ZipSideWidthCode(ZipSideOfListKind(secondKind));
    const int32_t s1 = aRef ? 0 : ZipSideOfListKind(firstKind);
    const int32_t s2 = bRef ? 0 : ZipSideOfListKind(secondKind);

    if (aRef && bRef)
    {
        ZipFillMixed<Il2CppObject*, Il2CppObject*>(first, second, d, s1, s2, resKind, out, n);
    }
    else if (aRef)
    {
        if (c2 == 0)      ZipFillMixed<Il2CppObject*, int32_t>(first, second, d, s1, s2, resKind, out, n);
        else if (c2 == 1) ZipFillMixed<Il2CppObject*, int64_t>(first, second, d, s1, s2, resKind, out, n);
        else if (c2 == 3) ZipFillMixed<Il2CppObject*, runtime::Il2CppDecimal>(first, second, d, s1, s2, resKind, out, n);
        else              ZipFillMixed<Il2CppObject*, double>(first, second, d, s1, s2, resKind, out, n);
    }
    else if (bRef)
    {
        if (c1 == 0)      ZipFillMixed<int32_t, Il2CppObject*>(first, second, d, s1, s2, resKind, out, n);
        else if (c1 == 1) ZipFillMixed<int64_t, Il2CppObject*>(first, second, d, s1, s2, resKind, out, n);
        else if (c1 == 3) ZipFillMixed<runtime::Il2CppDecimal, Il2CppObject*>(first, second, d, s1, s2, resKind, out, n);
        else              ZipFillMixed<double, Il2CppObject*>(first, second, d, s1, s2, resKind, out, n);
    }
    else
    {
        return il2cpp_linq_zip_wide(first, second, sel, s1, s2, resKind);
    }
    return out;
}

} // namespace il2cpp
