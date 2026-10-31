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
//     the resolver arm below serves the un-folded runtime-handle shape.
//
// P0 batch A (wired -- probe-measured net48 semantics, see
// src/runtime/include/internal/il2cpp_runtime_helpers.h):
//   * PrepareDelegate / PrepareContractedDelegate  -> no-op JIT-prepare
//     hints (net48 accepts null and valid delegates without throwing).
//   * EnsureSufficientExecutionStack               -> the runtime's REAL
//     per-thread stack-limit check (throws
//     InsufficientExecutionStackException below the guard).
//   * ProbeForSufficientStack                      -> stack-touch hint no-op
//     (net48 deep recursion is a raw SOE, not a managed exception).
//   * ExecuteCodeWithGuaranteedCleanup             -> runtime try/cleanup
//     driver (cleanup runs on both paths, exception rethrown).
//   * InitializeArray                              -> StackFold handles the
//     compiler-generated RVA shape; the un-folded runtime-handle shape goes
//     to the runtime helper's net48 invalid-handle contract.
//
// LOUD (net48 audit section 3 -- type/module-init drivers):
//   * _RunClassConstructor / _RunModuleConstructor -- the transpiler drives
//     type-initialization through generated ensure_cctor guards.
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
            // --- P0 batch A: wired to the runtime helpers (net48 probe-measured;
            // see il2cpp_runtime_helpers.h / runtimehelpers.cpp) ---
            // void PrepareDelegate(Delegate): JIT-prepare hint, no-op in AOT.
            case "PrepareDelegate":
                return I("il2cpp::runtime::icall_runtime_helpers_prepare_delegate", 1, false, false, false, new[] { "" });
            // void PrepareContractedDelegate(Delegate): same JIT-prepare hint.
            case "PrepareContractedDelegate":
                return I("il2cpp::runtime::icall_runtime_helpers_prepare_contracted_delegate", 1, false, false, false, new[] { "" });
            // void EnsureSufficientExecutionStack(): the runtime performs a REAL
            // per-thread stack-limit check and throws
            // System.InsufficientExecutionStackException below the guard.
            case "EnsureSufficientExecutionStack":
                return I("il2cpp::runtime::icall_runtime_helpers_ensure_sufficient_execution_stack", 0, false, false, false);
            // void ProbeForSufficientStack(): net48 stack-touch hint (no managed
            // exception contract; deep recursion is a raw SOE).
            case "ProbeForSufficientStack":
                return I("il2cpp::runtime::icall_runtime_helpers_probe_for_sufficient_stack", 0, false, false, false);
            // void ExecuteCodeWithGuaranteedCleanup(TryCode, CleanupCode, object):
            // the runtime runs code(userData), always runs
            // backoutCode(userData, exceptionThrown) and rethrows.
            case "ExecuteCodeWithGuaranteedCleanup":
                return I("il2cpp::runtime::icall_runtime_helpers_execute_code_with_guaranteed_cleanup", 3, false, false, false,
                    new[] { "", "", "" });
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
                // pattern BEFORE the resolver, so every compiler-generated
                // `new T[] { ... }` initializer never reaches this arm.
                // A handle obtained at runtime (e.g. FieldInfo.FieldHandle)
                // has no compile-time RVA blob to fold; the runtime helper
                // reproduces net48's invalid-handle contract (default handle
                // -> ArgumentNullException, any other un-folded handle ->
                // ArgumentException) instead of rejecting the program.
                return I("il2cpp::runtime::icall_runtime_helpers_initialize_array", 2, false, false, false,
                    new[] { "", "" });
            default:
                return null;
        }
    }
}
