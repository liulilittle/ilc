//------------------------------------------------------------------------------
// concat.cpp -- single-allocation multi-segment string concatenation.
//
// The three-step shape shared by every function here (see il2cpp_concat.h):
//
//   pass 1 : sum the segment lengths -- a single traversal of metadata only.
//            All managed memory reads (lengths, and later payloads) happen
//            BEFORE the one GC allocation below.
//   stage  : copy every payload into a NON-GC pool scratch buffer
//            (il2cpp::alloc::il2cpp_alloc_fast -- an O(1) bump; it can never
//            trigger a collection, unlike the managed heap).
//   finish : ONE il2cpp_string_new(scratch, total) -- the exact creation API
//            string.cpp uses for every string (gc_alloc_size + fill), called
//            exactly once per result.
//
// WHY THE STAGING IS MANDATORY (physical contract, il2cpp_gc.h)
//   il2cpp_gc_alloc_size may collect when the heap cannot satisfy the
//   request (collect-then-retry). If the result were allocated first and the
//   payloads copied afterwards, a mid-call collection could reclaim the
//   unrooted INPUT strings before their characters were read -- a
//   use-after-free. Staging the content in non-GC memory first removes the
//   window entirely: after the stage, no managed memory is read at all.
//
// ALLOCATION COMPLEXITY (documented per function in il2cpp_concat.h)
//   Every function here performs exactly ONE managed allocation (the final
//   il2cpp_string_new) plus exactly ONE non-GC scratch buffer that is freed
//   immediately -- versus N-1 managed allocations for naive chained concat.
//   The identity fast paths (single non-null input) allocate NOTHING.
//
//   NOTE (pool free semantics): il2cpp_free_fast links the freed block into
//   the pool's free list by WRITING the free-list head INTO the block's
//   payload. A freed block must therefore never be read afterwards -- all
//   values needed from scratch/staging memory are extracted BEFORE the free.
//------------------------------------------------------------------------------

#include "il2cpp_concat.h"

#include "il2cpp_allocator.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_string_intern.h"
#include "il2cpp_value_semantics.h"

#include <cstdarg>
#include <cstdint>
#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

// The largest length a managed string can carry (.NET string.Length).
constexpr int32_t kMaxStringLength = 2147483647;

// Abort the operation with a managed OverflowException when the total length
// of the concatenation exceeds Int32.MaxValue (unreachable in practice --
// ~2GB of UTF-16 units -- but the guard keeps the contract honest instead of
// silently truncating; .NET throws the same exception in its Concat paths).
void ThrowLengthOverflow()
{
    il2cpp_throw_overflow_exception(
        "The concatenated string length exceeds Int32.MaxValue");
}

// Null-safe segment length: null counts as an empty string.
int32_t SegmentLength(const Il2CppString* seg)
{
    return seg != nullptr ? seg->length : 0;
}

// Payload size in bytes for 'units' UTF-16 units (the scratch is sized as the
// exact payload extent; the managed string's own extent is computed inside
// il2cpp_string_new, keeping the creation formula in string.cpp authoritative).
size_t ScratchBytes(int32_t units)
{
    return static_cast<size_t>(units) * sizeof(char16_t);
}

// Is 'obj' a managed string? NAME comparison (not klass pointer equality)
// follows stringext2.cpp's IsStringObject: the runtime has two System.String
// descriptors (the allocator's g_stringClass and the metadata
// System_String_class), both named "System.String", and either may appear.
bool IsStringObject(Il2CppObject* obj)
{
    return obj->klass != nullptr &&
           std::strcmp(obj->klass->name, "System.String") == 0;
}

// A segment as its string view: null -> null (empty); a string -> the string
// ITSELF (identity); any other object -> its net48 ToString() text
// (icall_object_to_string -- .NET Concat(object,..) / Concat(object[]) calls
// ToString() on each non-string argument/element: boxed primitives format
// their value, other objects print their type's full name). The conversion
// may ALLOCATE, so callers must keep the source reachable for the whole call
// (see the header's GC-ROOTING CONTRACT).
Il2CppString* SegmentForObject(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return nullptr;
    }
    if (IsStringObject(obj))
    {
        return reinterpret_cast<Il2CppString*>(obj);
    }
    return icall_object_to_string(obj);
}

// Element of a reference array as an object (null slot -> null).
Il2CppObject* ArrayElementObject(Il2CppArray* array, int32_t index)
{
    return *reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(array, index));
}

// net48 String.Empty -- the INTERNED empty string (the same instance the
// intern pool returns for the "" literal, and the instance StackFold's
// String.Empty fold resolves to). Returning it for empty concat results makes
// ReferenceEquals(Concat("", ""), "") == true, matching net48 identity.
Il2CppString* EmptyString()
{
    return reinterpret_cast<Il2CppString*>(il2cpp_string_intern_literal(""));
}

// The shared array-based single-allocation pipeline (concat_array: addSep =
// false; builder_join: addSep = true -- 'separator' is written between
// consecutive elements, never leading/trailing).
//
// Sequence: element pointers are read twice (pass 1 lengths, pass 2 payloads)
// -- both passes run BEFORE the single managed allocation at the end, and the
// only allocation between them is the non-GC scratch, which cannot trigger a
// collection. Result: exactly 1 managed + 1 non-GC allocation, or ZERO on the
// identity fast path (single non-null element).
Il2CppString* ConcatFromArray(Il2CppArray* array, bool addSep, char16_t separator)
{
    if (array == nullptr || array->length <= 0 || !array->isRefElem)
    {
        return EmptyString();
    }
    const int32_t count = array->length;
    if (count == 1)
    {
        // Single-element fast path: a STRING element returns itself (identity,
        // zero allocations -- matches .NET Concat/Join(single string)); a
        // non-string element returns its net48 ToString() text (a new string,
        // never the box itself); a null element -> String.Empty (net48
        // Concat(object[]) single -> args[0].ToString(), null -> String.Empty).
        Il2CppObject* only = ArrayElementObject(array, 0);
        if (only == nullptr)
        {
            return EmptyString();
        }
        return SegmentForObject(only);
    }

    // Pass 1: total length in 64-bit arithmetic (overflow-safe accumulation).
    // Non-string elements are converted with the per-element segment helper --
    // each conversion may allocate (a ToString result), so callers must keep
    // the array reachable for the whole call (see the header's GC-ROOTING
    // CONTRACT). Only the LENGTH is retained: a pass-1 conversion result is
    // unreferenced garbage once a later conversion collects it, so pass 2
    // re-converts instead of caching pointers (the join precedent,
    // stringext2.cpp JoinFromArray).
    int64_t total = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        total += SegmentLength(SegmentForObject(ArrayElementObject(array, i)));
    }
    if (addSep)
    {
        total += static_cast<int64_t>(count) - 1; // one separator BETWEEN elements
    }
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    if (total == 0)
    {
        return EmptyString();
    }

    // Stage: copy all payloads (and separators) into non-GC memory BEFORE the
    // single managed allocation (see the header's GC-safety contract). Each
    // non-string element is re-converted and its payload copied IMMEDIATELY
    // (no allocation between conversion and copy), so a later conversion can
    // never reclaim content that is already staged.
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(ScratchBytes(static_cast<int32_t>(total))));
    size_t offset = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppString* seg = SegmentForObject(ArrayElementObject(array, i));
        if (seg != nullptr && seg->length > 0)
        {
            std::memcpy(scratch + offset, seg->chars, ScratchBytes(seg->length));
            offset += static_cast<size_t>(seg->length);
        }
        if (addSep && i + 1 < count)
        {
            scratch[offset++] = separator;
        }
    }
    // Finish: the ONLY managed allocation of this call.
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return result;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_string_concat_many -- variadic multi-segment concatenation.
//
// Complexity: 1 managed allocation (+1 non-GC scratch) vs N-1 naive chained
// concat allocations. count <= 0 -> empty string; count == 1 with a non-null
// segment -> the segment itself (identity, ZERO allocations).
//
// The variadic list is walked TWICE: a va_copy snapshot sums the lengths
// (pass 1), then the original list copies the payloads into the scratch
// (pass 2) -- no intermediate array is needed, so exactly one non-GC
// allocation happens per call. Both walks read managed memory BEFORE the
// single GC allocation at the end (see the GC-safety contract above).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_concat_many(int32_t count, ...)
{
    if (count <= 0)
    {
        return EmptyString();
    }
    va_list args;
    va_start(args, count);
    if (count == 1)
    {
        // Identity fast path: concatenating a single non-empty STRING is a
        // no-op (matches .NET Concat(single) -- zero allocations); a single
        // non-string object yields its net48 ToString() text (a new string);
        // null or empty -> String.Empty (net48 Concat(str0):
        // IsNullOrEmpty(str0) ? String.Empty : str0).
        Il2CppObject* only = va_arg(args, Il2CppObject*);
        va_end(args);
        if (only == nullptr)
        {
            return EmptyString();
        }
        Il2CppString* seg = SegmentForObject(only);
        return seg != nullptr && seg->length > 0 ? seg : EmptyString();
    }

    // Pass 1: total length (va_copy snapshot; the original stays for pass 2).
    // Non-string arguments are converted with the per-segment helper -- each
    // conversion may allocate (a ToString result), so callers must keep the
    // arguments reachable for the whole call (see the header's GC-ROOTING
    // CONTRACT). Only the LENGTH is retained: a pass-1 conversion result is
    // unreferenced garbage once a later conversion collects it, so pass 2
    // re-converts instead of caching pointers. Also track the net48
    // empty-segment identity fast path: exactly ONE non-empty STRING segment
    // (all others null/empty) -> return it unchanged (zero allocations).
    va_list scan;
    va_copy(scan, args);
    int64_t total = 0;
    int32_t nonEmptyCount = 0;
    const Il2CppString* onlyString = nullptr;
    bool anyConversion = false;
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject* arg = va_arg(scan, Il2CppObject*);
        Il2CppString* seg = SegmentForObject(arg);
        total += SegmentLength(seg);
        if (arg != nullptr && !IsStringObject(arg))
        {
            anyConversion = true;
        }
        if (seg != nullptr && seg->length > 0)
        {
            ++nonEmptyCount;
            if (IsStringObject(arg))
            {
                onlyString = seg;
            }
        }
    }
    va_end(scan);
    if (total > kMaxStringLength)
    {
        va_end(args);
        ThrowLengthOverflow();
    }
    if (total == 0)
    {
        va_end(args);
        return EmptyString();
    }
    // net48 empty-segment identity (Concat(s0, s1[, ...]): if exactly one
    // argument is non-empty the result IS that argument -- the input string
    // by reference, zero allocations). Only a STRING segment is eligible
    // (a boxed object's ToString result is a fresh allocation we cannot safely
    // retain across later conversions; it falls through to the allocation path
    // with identical content). Additionally gated on NO conversion having
    // happened: a pass-1 ToString conversion may collect, and a compaction
    // would move the input strings (C++ locals are not rewritten) -- the
    // identity fast path must be allocation-free to be movement-free.
    if (nonEmptyCount == 1 && onlyString != nullptr && !anyConversion)
    {
        va_end(args);
        return const_cast<Il2CppString*>(onlyString);
    }

    // Stage: copy all payloads into non-GC memory BEFORE the single managed
    // allocation (see the header's GC-safety contract). Each non-string
    // argument is re-converted and its payload copied IMMEDIATELY (no
    // allocation between conversion and copy), so a later conversion can never
    // reclaim content that is already staged.
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(ScratchBytes(static_cast<int32_t>(total))));
    size_t offset = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        const Il2CppString* seg = SegmentForObject(va_arg(args, Il2CppObject*));
        if (seg != nullptr && seg->length > 0)
        {
            std::memcpy(scratch + offset, seg->chars, ScratchBytes(seg->length));
            offset += static_cast<size_t>(seg->length);
        }
    }
    va_end(args);
    // Finish: the ONLY managed allocation of this call.
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return result;
}

//------------------------------------------------------------------------------
// il2cpp_string_concat_array -- String.Concat(string[]) semantics.
//
// Complexity: 1 managed allocation (+1 non-GC scratch) vs N-1 naive. Null
// array -> ArgumentNullException (net48: Concat(string[ ]) / Concat(object[])
// both throw ANE on a null array -- the earlier "defensive empty string"
// behavior is retired, verified by the net48 probe p-excvalidate); null
// elements -> empty; single non-null element -> identity (zero allocations).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_concat_array(Il2CppObject* arr)
{
    if (arr == nullptr)
    {
        il2cpp_throw_argument_null_exception("values");
    }
    return ConcatFromArray(reinterpret_cast<Il2CppArray*>(arr), false, u'\0');
}

//------------------------------------------------------------------------------
// il2cpp_string_builder_join -- string.Join semantics with a single UTF-16
// separator unit between elements (never leading/trailing).
//
// Complexity: 1 managed allocation (+1 non-GC scratch) vs N-1 naive concats
// with N-1 separator insertions. Null array -> empty string (defensive);
// null elements -> empty; single non-null element -> identity.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_builder_join(char16_t separator, Il2CppObject* values)
{
    return ConcatFromArray(reinterpret_cast<Il2CppArray*>(values), true, separator);
}

//------------------------------------------------------------------------------
// il2cpp_string_repeat -- repeat 's' 'count' times (the optimization of a
// count-way chained Concat).
//
// Complexity: 1 managed allocation (+1 non-GC scratch) vs count-1 naive.
// count <= 0 -> empty string; count == 1 -> 's' itself (identity, ZERO
// allocations).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_string_repeat(Il2CppObject* s, int32_t count)
{
    Il2CppString* str = reinterpret_cast<Il2CppString*>(s);
    if (count <= 0)
    {
        return EmptyString();
    }
    if (count == 1)
    {
        // Identity fast path: repeating once is a no-op (zero allocations).
        return str != nullptr ? str : EmptyString();
    }
    if (str == nullptr)
    {
        return EmptyString();
    }
    // Pass 1: total length in 64-bit arithmetic (overflow-safe product).
    const int64_t total = static_cast<int64_t>(str->length) * static_cast<int64_t>(count);
    if (total > kMaxStringLength)
    {
        ThrowLengthOverflow();
    }
    if (total == 0)
    {
        return EmptyString();
    }
    // Stage: repeat the payload 'count' times into non-GC memory.
    char16_t* scratch = static_cast<char16_t*>(
        alloc::il2cpp_alloc_fast(ScratchBytes(static_cast<int32_t>(total))));
    size_t offset = 0;
    const size_t unitBytes = ScratchBytes(str->length);
    for (int32_t i = 0; i < count; ++i)
    {
        std::memcpy(scratch + offset, str->chars, unitBytes);
        offset += static_cast<size_t>(str->length);
    }
    // Finish: the ONLY managed allocation of this call.
    Il2CppString* result = il2cpp_string_new(scratch, static_cast<int32_t>(total));
    alloc::il2cpp_free_fast(scratch);
    return result;
}

} // namespace runtime
} // namespace il2cpp
