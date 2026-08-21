#pragma once
//------------------------------------------------------------------------------
// invoker.h -- unified-signature reflection invoker (design section 3.1:
// "unified-signature thin wrapper over void* fnPtr + argbuf", the
// recommended first landing; docs/architecture/reflection-runtime-design.md).
//
// ONE runtime dispatcher serves every reflected method:
//   il2cpp_invoker_invoke(fnPtr, signature, args, argCount, ret)
// interprets the shared MethodSignatureCoder signature encoding, unpacks the
// argbuf slots by their types, calls the AOT static function through a
// compile-time typed call table, and writes the return value into ret.
//
// The method table (Il2CppMethodInfo.fnPtr, typeinfo.h) stays a uniform
// void* slot; the transpiler emits one call to this function per reflected
// method (or routes the icall_method_invoke path here) -- no per-method
// wrapper code needs to be generated. Reflection is not a hot path, so the
// generic dispatch cost is acceptable (design section 3.1/3.3 trade-off).
//
// Signature encoding (shared with the transpiler, MethodSignatureCoder):
//   signature := RET [':' PARAM (',' PARAM)*]
//   RET  := 'V' void | 'I' int32 | 'L' int64 | 'D' double | 'R' reference
//   PARAM (MethodSignatureCoder encodings verbatim):
//     int32 slot  : "i" "b" "c" "i1" "u1" "i2" "u2" "n"
//                   (int/bool/char/sbyte/byte/short/ushort/native-int)
//     int64 slot  : "l" "q"  (long/ulong)
//     double slot : "f" "d"  (float is stored widened in the double slot)
//     ref slot    : "r:FullName"  (string/object/array/class -> pointer slot)
//   Unsupported encodings ("v" void param, "V:" value type, "B:" byref,
//   "T:" type handle, unknown codes) fail loudly with kInvokerErrorBadSignature
//   -- v1 ABI covers the four slot families only.
//   Examples: "I:i,i"  "L:l,l"  "D:i,d,r:System.String"
//             "R:r:System.String"  "V:i,i"  "I"  (zero params, int32 return)
//
// Argbuf layout (design section 3.1: "value types are copied raw, reference types take the pointer",
// this = slot 0 for instance methods):
//   args is an array of argCount il2cpp_invoker_slot entries, one per
//   parameter (including the implicit 'this' as the first reference slot).
//   Value types are copied raw into the slot; reference types store the
//   object pointer (Il2CppObject* / Il2CppString* / array) directly.
//   Ret value: same convention -- raw for value returns, pointer for
//   reference returns; never written for void returns (ret may be null).
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp {

// One argbuf slot: 8 bytes, every parameter occupies exactly one slot.
union il2cpp_invoker_slot
{
    int32_t i32;    // int32 family (int/bool/char/sbyte/byte/short/ushort/native)
    int64_t i64;    // int64 family (long/ulong)
    double  dbl;    // double family (float/double)
    void*   ptr;    // reference family (string/object/array/class pointer)
};

// v1 cap on the parameter count per call (including the 'this' slot).
// Signatures beyond this fail loudly with kInvokerErrorTooManyParams.
constexpr int32_t kInvokerMaxParams = 4;

// Return codes of il2cpp_invoker_invoke: 0 on success, negative on failure.
enum InvokerError : int32_t
{
    kInvokerOk                = 0,   // call dispatched, ret written (unless void)
    kInvokerErrorInvalidArgs  = -1,  // null fnPtr / null or empty signature /
                                     // null args with argCount > 0 / null ret
                                     // for a non-void return / argCount < 0
    kInvokerErrorBadSignature = -2,  // unparseable or unsupported encoding
    kInvokerErrorArity        = -3,  // argCount != parameter count in signature
    kInvokerErrorTooManyParams = -4, // parameter count > kInvokerMaxParams
};

//------------------------------------------------------------------------------
// il2cpp_invoker_invoke -- unified-signature reflective call.
//
//   fnPtr     -- AOT static function address (uniform void* slot of
//                Il2CppMethodInfo). null -> kInvokerErrorInvalidArgs.
//   signature -- MethodSignatureCoder encoding per the contract above.
//                null or empty -> kInvokerErrorInvalidArgs.
//   args      -- argbuf: array of argCount slots (see layout above). May be
//                null only when argCount == 0 (never dereferenced then).
//   argCount  -- slot count; must equal the signature's parameter count.
//   ret       -- output buffer for the return value: int32_t*/int64_t*/double*/
//                void** by return kind. Required for non-void returns.
//
// Returns kInvokerOk (0) on success. On failure the call is NOT made and ret
// is left untouched (verify-then-mutate: all validation precedes dispatch).
//------------------------------------------------------------------------------
int32_t il2cpp_invoker_invoke(void* fnPtr, const char* signature,
    const il2cpp_invoker_slot* args, int32_t argCount, void* ret);

} // namespace il2cpp
