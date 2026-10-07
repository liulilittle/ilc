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
                throw new NotSupportedException($"RuntimeHelpers.GetHashCode with {ps.Length} arg(s) not wired (the runtime icall_object_get_hash_code takes exactly (object))");
            // bool Equals(object, object): reference equality (the fold's
            // RefEqMarker emits a C++ pointer comparison -- the same fold
            // Object.ReferenceEquals uses).
            case "Equals":
                return ps.Length == 2
                    ? new ResolvedMethod(ResolvedMethod.RefEqMarker, 2, false, true, false, new[] { false, false }, false)
                    : throw new NotSupportedException($"RuntimeHelpers.Equals with {ps.Length} arg(s) not wired (the reference-equality fold needs exactly (object, object))");
            // object GetObjectValue(object): the box-passthrough wrapper --
            // the transpiler's box already produces the boxed value, so the
            // call is an identity (the fold pushes the argument back).
            case "GetObjectValue":
                return ps.Length == 1
                    ? new ResolvedMethod(ResolvedMethod.IdentityMarker, 1, false, true, true, new[] { false }, false)
                    : throw new NotSupportedException($"RuntimeHelpers.GetObjectValue with {ps.Length} arg(s) not wired (the identity fold needs exactly (object))");
            // --- LOUD rejections (net48 audit section 3; no runtime counterpart) ---
            case "PrepareDelegate":
                throw new NotSupportedException("RuntimeHelpers.PrepareDelegate not wired: it is a JIT-prepare hint (netcore body is empty) and this AOT has no JIT to prepare the delegate");
            case "PrepareContractedDelegate":
                throw new NotSupportedException("RuntimeHelpers.PrepareContractedDelegate not wired: a constrained-execution JIT-prepare hint (netcore body is empty) with no runtime counterpart");
            case "EnsureSufficientExecutionStack":
                throw new NotSupportedException("RuntimeHelpers.EnsureSufficientExecutionStack not wired: the runtime has no stack-probe primitive (the AOT model runs on the host stack with no managed stack-limit checks)");
            case "ProbeForSufficientStack":
                throw new NotSupportedException("RuntimeHelpers.ProbeForSufficientStack not wired: the runtime has no stack-probe primitive (netcore body is empty)");
            case "ExecuteCodeWithGuaranteedCleanup":
                throw new NotSupportedException("RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup not wired: it takes a TryCode/CleanupCode delegate pair and the runtime has no constrained-execution support");
            case "_RunClassConstructor":
                throw new NotSupportedException("RuntimeHelpers._RunClassConstructor not wired: the transpiler drives type-initialization through generated ensure_cctor guards, not a runtime type-init driver");
            case "_RunModuleConstructor":
                throw new NotSupportedException("RuntimeHelpers._RunModuleConstructor not wired: the transpiler has no module-level constructor driver (module ctors are not in the M3 model)");
            case "InitializeArray":
                // StackFold.FoldInitializeArray intercepts the canonical
                // `newarr + dup + ldtoken<RVA field> + InitializeArray`
                // pattern BEFORE the resolver (StackFold.cs:1333); reaching
                // here means an un-folded shape -- reject, never emit a call
                // against the fold-only member.
                throw new NotSupportedException("RuntimeHelpers.InitializeArray not wired as an icall: the runtime constant-array init is a StackFold fold (ldtoken<RVA field> + newarr); the call reached the resolver without the fold pattern");
            default:
                return null;
        }
    }
}
