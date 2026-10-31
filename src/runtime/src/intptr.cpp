#include "intptr.h"
#include "il2cpp_exception.h"
#include "il2cpp_format.h"

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
// static int IntPtr.Size / UIntPtr.Size -- platform pointer width in bytes
// (net48: managed property; the transpiler needs an icall so the read links.
// 8 on x64, 4 on 32-bit builds -- sizeof(void*) IS the contract).
//------------------------------------------------------------------------------
int32_t il2cpp_intptr_get_size()
{
    return static_cast<int32_t>(sizeof(void*));
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
// UIntPtr.ToUInt32() / explicit operator uint -- CHECKED conversion to uint32.
//
// net48 (probe 2026-09-14): `(uint)UIntPtr` and UIntPtr.ToUInt32 both throw
// System.OverflowException for a value above UINT32_MAX; small values pass.
// The comparison runs on the UNSIGNED bit pattern (a negative intptr_t is a
// high unsigned address), and on 32-bit targets uintptr_t is 32 bits so the
// check never fires.
//------------------------------------------------------------------------------
uint32_t il2cpp_uintptr_to_uint32(intptr_t value)
{
    if (static_cast<uint64_t>(static_cast<uintptr_t>(value)) >
        static_cast<uint64_t>(UINT32_MAX))
    {
        il2cpp_throw_new(kOverflowChain, "Arithmetic operation resulted in an overflow.");
    }
    return static_cast<uint32_t>(value);
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

//------------------------------------------------------------------------------
// IntPtr.GetHashCode() -- the LOW 32 BITS of the pointer value as a SIGNED
// int32 (net48 probe 2026-10-31: (IntPtr)0x1234567890 -> 0x34567890 =
// 878082192, (IntPtr)(-1) -> -1, (IntPtr)0x80000000 -> int.MinValue,
// (IntPtr)0x100000001 -> 1). The high word is DISCARDED -- this is not an
// identity hash and not a high^low fold.
//------------------------------------------------------------------------------
int32_t il2cpp_intptr_get_hash_code(intptr_t value)
{
    return static_cast<int32_t>(static_cast<uint32_t>(static_cast<uintptr_t>(value)));
}

//------------------------------------------------------------------------------
// UIntPtr.GetHashCode() -- the low 32 bits masked to a NON-NEGATIVE int32
// (net48 probe 2026-10-31: ulong.MaxValue -> 2147483647, 0x80000000 -> 0,
// 0xC0000000 -> 1073741824, 0x1234567890 -> 0x34567890). Because UIntPtr is
// unsigned the net48 hash is never negative.
//------------------------------------------------------------------------------
int32_t il2cpp_uintptr_get_hash_code(intptr_t value)
{
    return il2cpp_intptr_get_hash_code(value) & 0x7FFFFFFF;
}

//------------------------------------------------------------------------------
// IntPtr.ToString() -- SIGNED DECIMAL ("255", "-1", "78187493520").
//
// net48's IntPtr.ToString() is NOT hex and NOT %p: it prints the pointer
// value in decimal with a '-' for a negative (top-bit-set) pointer. The
// canonical digit loop already lives in icall_int64_to_string, so the value
// is widened (sign-preserving, CLI III.3.4) and formatted there -- one
// decimal formatter in the runtime, no duplicated digit code.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_intptr_to_string(intptr_t value)
{
    return icall_int64_to_string(static_cast<int64_t>(value));
}

//------------------------------------------------------------------------------
// UIntPtr.ToString() -- UNSIGNED DECIMAL
// (UIntPtr(ulong.MaxValue) -> "18446744073709551615"). The bit pattern is
// reinterpreted as uintptr_t (never sign-extended) and formatted by the
// unsigned int64 digit loop.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_uintptr_to_string(intptr_t value)
{
    return icall_uint64_to_string(static_cast<uint64_t>(static_cast<uintptr_t>(value)));
}

}} // namespace il2cpp::runtime
