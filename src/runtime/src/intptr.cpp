#include "intptr.h"
#include "il2cpp_exception.h"

namespace il2cpp { namespace runtime {

namespace {

// Managed exception type chain (type + base classes, the exception.cpp
// catch-matcher walks the chain after the first comma).
const char kOverflowChain[] =
    "System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception";

} // namespace

//------------------------------------------------------------------------------
// IntPtr(int32) ctor -- the int32 value IS the pointer carrier (wf2,
// winforms1: `new IntPtr(1)` produces the intptr_t carrier 1).
//------------------------------------------------------------------------------
intptr_t il2cpp_intptr_ctor(intptr_t value)
{
    return static_cast<intptr_t>(value);
}

//------------------------------------------------------------------------------
// IntPtr.Add(IntPtr, int) -- pointer + offset.
//
// The offset is a SIGNED int32 and must be sign-extended to the pointer
// width before the addition: on 64-bit targets Add(p, int.MinValue) moves
// p DOWN by 0x80000000 (C# widens int to IntPtr the same way). The addition
// itself runs in the UNSIGNED domain: .NET pointer arithmetic is unchecked
// and wraps, and unsigned arithmetic is fully defined for every bit pattern
// (no signed-overflow UB for high-bit addresses). The static_cast chain is
// the exact hardware widening: int32 -> intptr_t (sign-extending widening
// conversion) -> uintptr_t (bit-preserving re-interpretation) -> sum ->
// back to intptr_t (bit-preserving re-interpretation).
//------------------------------------------------------------------------------
intptr_t il2cpp_intptr_add(intptr_t pointer, int32_t offset)
{
    const uintptr_t base = static_cast<uintptr_t>(pointer);
    const uintptr_t delta = static_cast<uintptr_t>(static_cast<intptr_t>(offset));
    return static_cast<intptr_t>(base + delta);
}

//------------------------------------------------------------------------------
// IntPtr.Subtract(IntPtr, int) -- pointer - offset.
//
// Same contract as Add: the int32 offset is sign-extended first (so
// Subtract(p, int.MinValue) moves p UP by 0x80000000), and the subtraction
// is performed in the unsigned domain for wrap-defined behaviour.
//------------------------------------------------------------------------------
intptr_t il2cpp_intptr_subtract(intptr_t pointer, int32_t offset)
{
    const uintptr_t base = static_cast<uintptr_t>(pointer);
    const uintptr_t delta = static_cast<uintptr_t>(static_cast<intptr_t>(offset));
    return static_cast<intptr_t>(base - delta);
}

//------------------------------------------------------------------------------
// IntPtr.ToInt64() -- pointer value as int64.
//
// CLI III.3.4: pointer<->integer conversions are sign-preserving, so the
// value is SIGN-extended to int64. On 64-bit targets intptr_t is already
// int64 and this is the identity; on 32-bit targets a pointer whose top
// bit is set becomes a negative int64. The conversion is exact: intptr_t
// is either int32_t or int64_t, and the signed widening conversion is
// always sign-extending.
//------------------------------------------------------------------------------
int64_t il2cpp_intptr_to_int64(intptr_t value)
{
    return static_cast<int64_t>(value);
}

//------------------------------------------------------------------------------
// IntPtr.ToInt32() -- CHECKED conversion to int32 (net48 semantics).
//
// net48's IntPtr.ToInt32() is a CHECKED conversion: `(int)m_value` in a
// checked context (probe-verified 2026-08-09 against csc net48 -- a value
// outside [INT32_MIN, INT32_MAX] throws System.OverflowException, it does NOT
// truncate). On 64-bit targets a pointer may exceed the int32 range; the
// conversion must throw. On 32-bit targets intptr_t is int32 already and the
// range check is a constant false (compiled away). The managed exception
// carrier preserves the checked-conversion contract end to end.
//------------------------------------------------------------------------------
int32_t il2cpp_intptr_to_int32(intptr_t value)
{
    if (value < static_cast<intptr_t>(INT32_MIN) ||
        value > static_cast<intptr_t>(INT32_MAX))
    {
        il2cpp_throw_new(kOverflowChain, "Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// IntPtr.op_Equality(IntPtr, IntPtr) -- bit-pattern equality.
//------------------------------------------------------------------------------
bool il2cpp_intptr_op_equality(intptr_t a, intptr_t b)
{
    return a == b;
}

//------------------------------------------------------------------------------
// IntPtr.op_Inequality(IntPtr, IntPtr) -- negation of the bit-pattern
// equality (the Version.op_Inequality pattern, versionuri.cpp).
//------------------------------------------------------------------------------
bool il2cpp_intptr_op_inequality(intptr_t a, intptr_t b)
{
    return !il2cpp_intptr_op_equality(a, b);
}

//------------------------------------------------------------------------------
// IntPtr.CompareTo(IntPtr) -- -1 / 0 / 1.
//
// Pointers are ordered as UNSIGNED addresses: C# void* relational operators
// and IntPtr.CompareTo both interpret the value as an address, so a pointer
// with the top bit set sorts HIGHER than any pointer with it clear (e.g.
// 0x8000... sorts above 0x7FFF...). The comparison therefore runs on the
// uintptr_t bit pattern, not on the signed intptr_t value.
//------------------------------------------------------------------------------
int32_t il2cpp_intptr_compare(intptr_t a, intptr_t b)
{
    const uintptr_t ua = static_cast<uintptr_t>(a);
    const uintptr_t ub = static_cast<uintptr_t>(b);
    return (ua < ub) ? -1 : (ua > ub) ? 1 : 0;
}

}} // namespace il2cpp::runtime
