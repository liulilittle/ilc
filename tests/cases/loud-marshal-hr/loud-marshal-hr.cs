using System;
using System.Runtime.InteropServices;

// LOUD contract: Marshal.GetExceptionForHR has no HR->Exception conversion in
// the runtime, and its owner (System.Runtime.InteropServices.Marshal) is an
// EXTERNAL BCL type -- the input assembly never defines it. Resolving the call
// therefore produces a PHANTOM: a generated name
// (System_Runtime_InteropServices_Marshal_GetExceptionForHR_<hash>) that no
// translation unit defines.
//
// Two independent guards must reject this, and this case pins both:
//   1. MethodMap.Marshal.cs allow-lists the name, so the call is refused with
//      "LOUD whitelist (marshal member not wired)".
//   2. The emitter's phantom guard (CodeGenerator, the `resolved2` /
//      `resolvedStmt` arms) throws for any IsFictionalBclFallback result, so
//      even with the allow-list entry removed the transpile still fails loudly
//      instead of emitting an undefined symbol.
//
// The failure mode this prevents is NOT a wrong answer: it is a cmake
// "use of undeclared identifier" at C++ build time, far from the cause.
//
// Both calls are made in EXPRESSION and STATEMENT position so the two emitter
// arms are each exercised.
public static class P
{
    public static void Main()
    {
        int hr = unchecked((int)0x80070057);

        // expression position: the result is consumed
        Exception ex = Marshal.GetExceptionForHR(hr);
        Console.WriteLine("[hr] ex=" + (ex == null ? "null" : "not-null"));

        // statement position: the result is discarded
        Marshal.ThrowExceptionForHR(hr);

        Console.WriteLine("[hr] unreachable");
    }
}
