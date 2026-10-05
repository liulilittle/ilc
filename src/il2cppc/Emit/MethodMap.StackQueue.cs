namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.StackQueue -- System.Collections.Stack / Queue (stackqueue.h) wiring SKELETON.
// Owners: System.Collections.Stack / Queue (stackqueue.h)
//
// WIRING AGENT: fill the switch(owner)/switch(name) body below with real
// mappings (copy the pattern from MethodMap.Math.cs; use the I(...) helper
// and the ps/RawSignature byte table from the MethodRefResolver.cs header).
// The dispatch case line already exists in MethodRefResolver.cs -- do NOT
// edit that file. Unwired members return null; known-unwireable members
// throw NotSupportedException (LOUD).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapStackQueue(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        return null;
    }
}
