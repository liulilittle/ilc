//------------------------------------------------------------------------------
// test_invoker.cpp -- unified-signature reflection invoker unit tests
// (il2cpp_invoker_invoke; see invoker.h for the ABI contract).
//
// Coverage (normal + edge + error paths):
//   1. A FUNCTION-POINTER TABLE of ten AOT-style functions with distinct
//      signatures, each invoked through il2cpp_invoker_invoke and
//      cross-checked against a direct call:
//        "I"                     int32  Answer()                    zero params
//        "I:i"                   int32  Sqr(int32)                  int32 slot
//        "I:i,i"                 int32  Add(int32,int32)
//        "I:b,b"                 bool   Both(bool,bool)             int32-slot
//                                                                   aliases
//        "L:l,l"                 int64  AddLong(int64,int64)        int64 slots
//        "D:d,d"                 double Hypot(double,double)        double slots
//        "D:i,d,r:System.String" double Mix(int32,double,FakeString*) mixed
//        "R:r:System.String,r:System.String"
//                                FakeString* Concat(FakeString*,FakeString*)
//                                                                   ref return
//        "V:i,i"                 void   Store(int32,int32)          void return,
//                                                                   side effects
//        "I:r:System.String,i"   int32  Instance(FakeString*,int32) 'this' as
//                                                                   slot 0
//   2. Edge cases: zero-param call with a null argbuf (legal, never
//      dereferenced); int64 boundary values; exact double math.
//   3. Error paths (no call is made, ret untouched):
//      null fnPtr / null signature / empty signature / null argbuf with
//      nonzero argCount / negative argCount / arity mismatch (too few, too
//      many) / null ret for a non-void return / unsupported encodings
//      (value type "V:*", byref "B:*", void param "v", unknown return
//      code) / empty parameter encoding / parameter count over the
//      kInvokerMaxParams cap.
//
// Plain executable + minimal assertion harness (see test_typeinfo.cpp).
//------------------------------------------------------------------------------

#include "invoker.h"

#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// A stand-in for a managed reference object: the invoker treats every "r:*"
// parameter as an opaque pointer (Il2CppObject* / Il2CppString* in the real
// runtime), so the test only needs a stable address + payload.
struct FakeString
{
    const char* data;
};

// AOT-style static functions (the shape the transpiler emits; the invoker's
// fnPtr is their address stored in a uniform void* slot).
int32_t Answer() { return 42; }
int32_t Sqr(int32_t x) { return x * x; }
int32_t Add(int32_t a, int32_t b) { return a + b; }
int32_t Both(int32_t a, int32_t b) { return (a != 0) && (b != 0); }
int64_t AddLong(int64_t a, int64_t b) { return a + b; }
double Hypot(double a, double b) { return a * a + b * b; }
double Mix(int32_t n, double d, FakeString* s)
{
    return static_cast<double>(n) + d +
           (s != nullptr ? static_cast<double>(s->data[0]) : 0.0);
}
FakeString* Concat(FakeString* a, FakeString* b)
{
    return a != nullptr ? a : b;
}

int g_storeA = 0;
int g_storeB = 0;
void Store(int32_t a, int32_t b)
{
    g_storeA = a;
    g_storeB = b;
}

int32_t Instance(FakeString* self, int32_t n)
{
    return self != nullptr
               ? static_cast<int32_t>(std::strlen(self->data)) + n
               : -1;
}

// The function pointer table: {fnPtr, signature} pairs -- the slice of an
// Il2CppMethodInfo the invoker consumes (name/declaringType/flags are
// irrelevant to dispatch and omitted).
struct InvokerEntry
{
    void*       fnPtr;
    const char* signature;
};

enum TableIndex
{
    kFnAnswer,
    kFnSqr,
    kFnAdd,
    kFnBoth,
    kFnAddLong,
    kFnHypot,
    kFnMix,
    kFnConcat,
    kFnStore,
    kFnInstance,
    kTableCount,
};

const InvokerEntry kTable[kTableCount] = {
    {reinterpret_cast<void*>(&Answer),   "I"},
    {reinterpret_cast<void*>(&Sqr),      "I:i"},
    {reinterpret_cast<void*>(&Add),      "I:i,i"},
    {reinterpret_cast<void*>(&Both),     "I:b,b"},
    {reinterpret_cast<void*>(&AddLong),  "L:l,l"},
    {reinterpret_cast<void*>(&Hypot),    "D:d,d"},
    {reinterpret_cast<void*>(&Mix),      "D:i,d,r:System.String"},
    {reinterpret_cast<void*>(&Concat),   "R:r:System.String,r:System.String"},
    {reinterpret_cast<void*>(&Store),    "V:i,i"},
    {reinterpret_cast<void*>(&Instance), "I:r:System.String,i"},
};

FakeString fHello = {"hello"};
FakeString fWorld = {"world"};

} // namespace

int main()
{
    using namespace il2cpp;

    // --- 1a. int32 family: zero params / one / two / bool aliases ---
    int32_t r = 0;
    Expect(il2cpp_invoker_invoke(kTable[kFnAnswer].fnPtr,
                                 kTable[kFnAnswer].signature,
                                 nullptr, 0, &r) == kInvokerOk && r == 42,
           "Answer() -> 42 (zero params, null argbuf is legal)");

    il2cpp_invoker_slot sqrArgs[1];
    sqrArgs[0].i32 = 9;
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr,
                                 kTable[kFnSqr].signature,
                                 sqrArgs, 1, &r) == kInvokerOk && r == 81,
           "Sqr(9) -> 81 matches direct call");
    Expect(r == Sqr(9), "Sqr result equals direct call");

    il2cpp_invoker_slot addArgs[2];
    addArgs[0].i32 = 7;
    addArgs[1].i32 = -3;
    Expect(il2cpp_invoker_invoke(kTable[kFnAdd].fnPtr,
                                 kTable[kFnAdd].signature,
                                 addArgs, 2, &r) == kInvokerOk && r == 4,
           "Add(7,-3) -> 4 matches direct call");

    il2cpp_invoker_slot bothArgs[2];
    bothArgs[0].i32 = 1;
    bothArgs[1].i32 = 0;
    Expect(il2cpp_invoker_invoke(kTable[kFnBoth].fnPtr,
                                 kTable[kFnBoth].signature,
                                 bothArgs, 2, &r) == kInvokerOk && r == 0,
           "Both(true,false) -> 0 (bool params live in int32 slots)");
    bothArgs[1].i32 = 1;
    Expect(il2cpp_invoker_invoke(kTable[kFnBoth].fnPtr,
                                 kTable[kFnBoth].signature,
                                 bothArgs, 2, &r) == kInvokerOk && r == 1,
           "Both(true,true) -> 1 matches direct call");

    // --- 1b. int64 family (boundary values) ---
    const int64_t big = INT64_C(0x7FFFFFFF00000001);
    il2cpp_invoker_slot longArgs[2];
    longArgs[0].i64 = big;
    longArgs[1].i64 = 1;
    int64_t r64 = 0;
    Expect(il2cpp_invoker_invoke(kTable[kFnAddLong].fnPtr,
                                 kTable[kFnAddLong].signature,
                                 longArgs, 2, &r64) == kInvokerOk &&
               r64 == big + 1 && r64 == AddLong(big, 1),
           "AddLong(boundary,1) -> boundary+1 matches direct call");

    // --- 1c. double family (exact 3-4-5 math) ---
    il2cpp_invoker_slot hypArgs[2];
    hypArgs[0].dbl = 3.0;
    hypArgs[1].dbl = 4.0;
    double rd = 0.0;
    Expect(il2cpp_invoker_invoke(kTable[kFnHypot].fnPtr,
                                 kTable[kFnHypot].signature,
                                 hypArgs, 2, &rd) == kInvokerOk && rd == 25.0,
           "Hypot(3,4) -> 25 matches direct call");

    // --- 1d. mixed signature: int32 + double + reference ---
    il2cpp_invoker_slot mixArgs[3];
    mixArgs[0].i32 = 2;
    mixArgs[1].dbl = 0.5;
    mixArgs[2].ptr = &fHello;
    Expect(il2cpp_invoker_invoke(kTable[kFnMix].fnPtr,
                                 kTable[kFnMix].signature,
                                 mixArgs, 3, &rd) == kInvokerOk &&
               rd == Mix(2, 0.5, &fHello),
           "Mix(2,0.5,\"hello\") equals direct call");

    // --- 1e. reference return ---
    il2cpp_invoker_slot catArgs[2];
    catArgs[0].ptr = &fHello;
    catArgs[1].ptr = &fWorld;
    FakeString* rref = nullptr;
    Expect(il2cpp_invoker_invoke(kTable[kFnConcat].fnPtr,
                                 kTable[kFnConcat].signature,
                                 catArgs, 2, &rref) == kInvokerOk &&
               rref == &fHello && rref == Concat(&fHello, &fWorld),
           "Concat(hello,world) returns first ref, matches direct call");

    // --- 1f. void return with side effects ---
    g_storeA = g_storeB = 0;
    il2cpp_invoker_slot storeArgs[2];
    storeArgs[0].i32 = 7;
    storeArgs[1].i32 = -3;
    Expect(il2cpp_invoker_invoke(kTable[kFnStore].fnPtr,
                                 kTable[kFnStore].signature,
                                 storeArgs, 2, nullptr) == kInvokerOk &&
               g_storeA == 7 && g_storeB == -3,
           "Store(7,-3) writes globals, void return (null ret is legal)");

    // --- 1g. instance-method shape: 'this' as slot 0 ---
    il2cpp_invoker_slot instArgs[2];
    instArgs[0].ptr = &fHello;
    instArgs[1].i32 = 100;
    Expect(il2cpp_invoker_invoke(kTable[kFnInstance].fnPtr,
                                 kTable[kFnInstance].signature,
                                 instArgs, 2, &r) == kInvokerOk &&
               r == Instance(&fHello, 100),
           "Instance(\"hello\",100) -> 105 matches direct call (this slot 0)");

    // --- 2. Error paths: no call is made, ret stays untouched ---
    const int32_t sentinel = 0x5A5A5A5A;

    r = sentinel;
    Expect(il2cpp_invoker_invoke(nullptr, "I:i", sqrArgs, 1, &r) ==
               kInvokerErrorInvalidArgs && r == sentinel,
           "null fnPtr -> invalid args, ret untouched");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, nullptr,
                                 sqrArgs, 1, &r) == kInvokerErrorInvalidArgs,
           "null signature -> invalid args");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "",
                                 sqrArgs, 1, &r) == kInvokerErrorInvalidArgs,
           "empty signature -> invalid args");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i",
                                 nullptr, 1, &r) == kInvokerErrorInvalidArgs,
           "null argbuf with argCount 1 -> invalid args");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i",
                                 sqrArgs, -1, &r) == kInvokerErrorInvalidArgs,
           "negative argCount -> invalid args");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i",
                                 sqrArgs, 2, &r) == kInvokerErrorArity,
           "argCount 2 vs 1-param signature -> arity mismatch");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i",
                                 sqrArgs, 0, &r) == kInvokerErrorArity,
           "argCount 0 vs 1-param signature -> arity mismatch");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i",
                                 sqrArgs, 1, nullptr) ==
               kInvokerErrorInvalidArgs,
           "null ret for non-void return -> invalid args");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:V:Calc.Point",
                                 sqrArgs, 1, &r) == kInvokerErrorBadSignature,
           "value-type parameter -> bad signature");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:B:i",
                                 sqrArgs, 1, &r) == kInvokerErrorBadSignature,
           "byref parameter -> bad signature");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:v",
                                 sqrArgs, 1, &r) == kInvokerErrorBadSignature,
           "void parameter -> bad signature");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "I:i,",
                                 sqrArgs, 1, &r) == kInvokerErrorBadSignature,
           "empty parameter encoding -> bad signature");
    Expect(il2cpp_invoker_invoke(kTable[kFnSqr].fnPtr, "X:i",
                                 sqrArgs, 1, &r) == kInvokerErrorBadSignature,
           "unknown return code -> bad signature");
    Expect(il2cpp_invoker_invoke(kTable[kFnAdd].fnPtr, "I:i,i,i,i,i",
                                 sqrArgs, 5, &r) == kInvokerErrorTooManyParams,
           "5 params over the kInvokerMaxParams cap -> too many");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d invoker assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime invoker tests passed\n");
    return 0;
}
