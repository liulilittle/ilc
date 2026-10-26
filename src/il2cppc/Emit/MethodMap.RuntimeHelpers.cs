namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.RuntimeHelpers -- System.Runtime.CompilerServices.RuntimeHelpers
// (net48 icall audit section 3 row 977 / section 6 b-class: 0 wired / 8 gaps -- 7 foldable
// or empty, PrepareDelegate low).
//
// WIRED (the b-class foldable semantics the audit does not count as gaps):
//   * GetHashCode(object)   -> icall_object_get_hash_code (il2cpp_value_
//     semantics.h:71 -- the identity hash dispatch: string ordinal / boxed
//     int32 value / address hash).
//   * Equals(object, object)-> the RefEqMarker (the fold emits a C++
//     pointer comparison -- reference equality, the netcore
//     RuntimeHelpers.Equals contract).
//   * GetObjectValue(object)-> the IdentityMarker (the fold pushes the
//     argument -- box-passthrough semantics; the transpiler's box already
//     produces the boxed value, so the wrapper is an identity).
//   * InitializeArray        -> folded by StackFold.FoldInitializeArray
//     (the ldtoken<RVA field> + newarr pattern) BEFORE the resolver runs;
//     the LOUD below is the safety net for an un-folded shape.
//
// LOUD (net48 audit section 3 -- JIT-prepare / stack-probe / constrained-execution
// hints with no runtime counterpart; an empty implementation would be a
// silent semantic gap):
//   * PrepareDelegate / PrepareContractedDelegate  -- JIT-prepare hints
//     (netcore bodies are empty); this AOT has no JIT to prepare.
//   * EnsureSufficientExecutionStack / ProbeForSufficientStack -- host
//     stack-probe hints; the runtime has no probe primitive.
//   * ExecuteCodeWithGuaranteedCleanup             -- constrained-execution
//     try/finally (TryCode/CleanupCode delegate pair); no runtime support.
//   * _RunClassConstructor / _RunModuleConstructor -- module/type-init
//     drivers; the transpiler drives cctor guards statically.
//   * InitializeArray (fallback)                    -- see above.
//
// Anything else returns null -> the generated-name path (standard unwired
// behavior; this family is not reachable from ordinary net48 user code).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRuntimeHelpers(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // int GetHashCode(object): the identity hash (value-semantics
            // dispatch). Static, one managed-reference param, int32 result.
            case "GetHashCode":
                if (ps.Length == 1)
                    return I("il2cpp::runtime::icall_object_get_hash_code", 1, true, false, false, new[] { "" });
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): RuntimeHelpers.GetHashCode with {ps.Length} arg(s) -- " +
                    "net48 has exactly GetHashCode(object) [Reference Source runtimehelpers.cs:155]");
            // bool Equals(object, object): reference equality (the fold's
            // RefEqMarker emits a C++ pointer comparison -- the same fold
            // Object.ReferenceEquals uses).
            case "Equals":
                return ps.Length == 2
                    ? new ResolvedMethod(ResolvedMethod.RefEqMarker, 2, false, true, false, new[] { false, false }, false)
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): RuntimeHelpers.Equals with {ps.Length} arg(s) -- " +
                        "net48 has exactly Equals(object, object) [Reference Source runtimehelpers.cs:160]");
            // object GetObjectValue(object): the box-passthrough wrapper --
            // the transpiler's box already produces the boxed value, so the
            // call is an identity (the fold pushes the argument back).
            case "GetObjectValue":
                return ps.Length == 1
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 1, false, true, true, new[] { false }, false)
                    : throw new NotSupportedException(
                        $"LOUD whitelist (net48 has no such overload): RuntimeHelpers.GetObjectValue with {ps.Length} arg(s) -- " +
                        "net48 has exactly GetObjectValue(object) [Reference Source runtimehelpers.cs:50]");
            // --- LOUD rejections (net48 audit section 3; no runtime counterpart) ---
            case "PrepareDelegate":
                throw new NotSupportedException(
                    "LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareDelegate is a JIT-prepare hint " +
                    "(this AOT has no JIT to prepare the delegate) [Reference Source runtimehelpers.cs:135]");
            case "PrepareContractedDelegate":
                throw new NotSupportedException(
                    "LOUD whitelist (① machine/JIT state): RuntimeHelpers.PrepareContractedDelegate is a constrained-execution " +
                    "JIT-prepare hint (no JIT to prepare) [Reference Source runtimehelpers.cs:150]");
            case "EnsureSufficientExecutionStack":
                throw new NotSupportedException(
                    "LOUD whitelist (① machine/JIT state): RuntimeHelpers.EnsureSufficientExecutionStack is a host stack-probe " +
                    "hint (the AOT runs on the host stack with no managed stack-limit checks) [Reference Source runtimehelpers.cs:191]");
            case "ProbeForSufficientStack":
                throw new NotSupportedException(
                    "LOUD whitelist (① machine/JIT state): RuntimeHelpers.ProbeForSufficientStack is a host stack-probe hint " +
                    "(no managed stack-limit checks) [Reference Source runtimehelpers.cs:197]");
            case "ExecuteCodeWithGuaranteedCleanup":
                throw new NotSupportedException(
                    "LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup is a " +
                    "constrained-execution try/finally (TryCode/CleanupCode delegate pair); the runtime has no CER support " +
                    "[Reference Source runtimehelpers.cs:229]");
            case "_RunClassConstructor":
                throw new NotSupportedException(
                    "LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunClassConstructor is a runtime type-init " +
                    "driver; the transpiler drives type-initialization through generated ensure_cctor guards " +
                    "[Reference Source runtimehelpers.cs:63]");
            case "_RunModuleConstructor":
                throw new NotSupportedException(
                    "LOUD whitelist (③ no runtime descriptor): RuntimeHelpers._RunModuleConstructor is a module-level " +
                    "constructor driver (module ctors are not in the M3 model) [Reference Source runtimehelpers.cs:81]");
            case "InitializeArray":
                // StackFold.FoldInitializeArray intercepts the canonical
                // `newarr + dup + ldtoken<RVA field> + InitializeArray`
                // pattern BEFORE the resolver (StackFold.cs:1333); reaching
                // here means an un-folded shape -- reject, never emit a call
                // against the fold-only member.
                throw new NotSupportedException(
                    "LOUD whitelist (③ no runtime descriptor): RuntimeHelpers.InitializeArray is a StackFold fold " +
                    "(ldtoken<RVA field> + newarr); the call reached the resolver without the fold pattern " +
                    "[Reference Source runtimehelpers.cs:32]");
            default:
                return null;
        }
    }
}
