using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// Simple-method inlining (bench gap: the net48 JIT inlines small getters and
// pure helpers at their call sites; the generated C++ emitted a function
// call per site -- the escape-analysis/inlining half of the object/string
// bench gap, see docs/audit/bench-regression-check.md).
//
//   Inline rule -- conservative, small PURE functions only:
//     * callee body <= threshold (5 statements; up to 10 when the body is
//       pure straight-line -- no branches/labels, so the expansion has no
//       control-flow-merge risk)
//     * no exception regions (ExceptionRegions empty)
//     * not recursive (the body must not call its own Function -- direct
//       recursion only; virtual dispatch can never be inlined and is not
//       a candidate)
//     * int32/void results only (ReturnsRef == false -- the result temp
//       stays int32_t; ref results keep the call)
//     * caller-side call is a USER call (CallExpr Function "call_<token>",
//       Resolved == null, ResultKind == null) -- BCL icalls (Resolved set)
//       and int64/double-result calls are never candidates
//     * no byref arguments (LocAddrExpr -- the arg-temp indirection would
//       break byref write-back: the callee would write the temp, not the
//       caller's local)
//     * arg count matches ParamCount; void-position calls (CallStmt/
//       ExprStmt) require a void callee, value positions (AssignStmt/
//       RetStmt) a non-void callee
//     * non-void bodies must end every path with a value (every RetStmt
//       carries a non-null Value) -- the shared result temp is always
//       defined on every inlined path
//
//   Expansion (evaluation-order preserving -- args LEFT-TO-RIGHT before the
//   body, exactly .NET call semantics):
//     arg{k} temps (only for args the body reads)  ->  renamed callee body
//     (locals and labels get fresh caller-scope names, no collisions)
//     every callee RetStmt -> `t_res = <value>` + `goto L_end` (void: bare
//     `goto L_end`)  ->  L_end:  ->  the original consumer statement with
//     the call replaced by a read of t_res (void-position calls disappear).
//
//   Nested-call extension (the object-bench gap: `sum += it.Get() & 0xFF`
//   folds to `loc0 = loc0 + (Get(loc2) & 0xFF)` -- the call is nested inside
//   a BinaryExpr, so a top-level call-site scan misses it): every statement
//   is re-scanned for inlineable user calls ANYWHERE in its expression tree
//   (pre-order, left-to-right evaluation). Each such call expands in place:
//   its expansion becomes prelude statements and the call subtree is
//   replaced by a read of the shared result temp. Multiple calls in one
//   statement (e.g. `f(x) + g(y)`) expand one after the other, bounded by
//   MaxNestedExpansionsPerStatement. Inlined BODIES are not re-scanned
//   (the prelude statements are emitted verbatim -- no transitive/recursive
//   expansion; the cap plus the recursion rejection keeps this terminating).
//
//   Call-shape notes (validated against the real bench IR):
//   * The fold PRE-RESOLVES user-method calls (CallExpr.Resolved = the
//     generated CppName); IsUserCall accepts those (the split RootSlotPass
//     uses: a resolved USER call is inlineable, a "il2cpp::" runtime icall
//     is not).
//   * Roslyn emits callvirt for EVERY instance call (null-check), so the
//     bench getter arrives as a VirtualCallExpr. A NON-virtual callee
//     (IsVirtual == false) is a compile-time direct call -- it is inlined
//     the same way (looked up by its method token). Virtual callees are
//     never inlined (the receiver could dispatch to an override).
//   * ReturnsRef callees (string/object getters) stay as calls: the result
//     temp's reference typing is deliberately left to the emitter, and
//     inlining a reference-returning body would need cross-method type
//     propagation the pass does not do.
//
//   This file is pass-only: it never touches StackFold/CodeGenerator. The
//   CLI hook (Program.cs) runs a post-Generate pre-scan and reports the
//   inlining opportunities; the TRANSFORM wiring into the emission pipeline
//   is pending CodeGenerator.cs availability (shared/owned elsewhere -- the
//   same precedent as LicmPass). The wiring contract is documented in the
//   batch report (run the candidates table from ScanAssembly, then
//   InlinePass.Inline BEFORE DcePass.Run so the expansion feeds the DCE,
//   then AllocPromoPass.Run for the object-allocation elimination).
//------------------------------------------------------------------------------
public sealed class InlineCandidate
{
    // The callee's own Function id ("call_<token>") -- recursion detection: a body
    // that calls its own Function is rejected.
    public required string Function { get; init; }
    // Parameter slot count (arg0..argN-1; instance methods include this) -- must match
    // the call site's argument count.
    public required int ParamCount { get; init; }
    public required bool ReturnsVoid { get; init; }
    // Reference results (string/object/array...) → conservatively not inlined (the
    // result temp's type would be uncertain).
    public required bool ReturnsRef { get; init; }
    // Callee local kinds (MethodBodyReader LocalKinds, index-aligned with
    // loc{i}) -- the struct-local gate below needs them. Empty for legacy
    // and test candidates (gate allows).
    public string[] LocalKinds { get; init; } = Array.Empty<string>();
    public required bool HasExceptionRegions { get; init; }
    // Virtual methods are NEVER inlined: a callvirt receiver could dispatch to an
    // override at runtime, so the compile-time body is not the executed one. The
    // receiver's runtime type is unknowable to this pass. (The bench getter is a
    // NON-virtual method called via callvirt -- Roslyn emits callvirt for every
    // instance call to get the null check -- so it is a compile-time direct call
    // and remains inlineable.)
    public bool IsVirtual { get; init; }
    // The folded method body (StackFold.Fold output: arg{k} parameter slots + loc{k}
    // locals + ret trailer).
    public required List<Stmt> Body { get; init; }
}

public static class InlinePass
{
    // Branched/multi-exit bodies: conservative cap (the expansion merges paths
    // at the end label -- each path writes the result temp).
    public const int MaxInlineBodyStatements = 5;
    // Straight-line bodies (no BranchStmt/LabelStmt): the expansion is a flat
    // statement run with no merge risk, so larger helpers (ctor chains,
    // multi-statement pure initializers) are still inlined.
    public const int MaxStraightLineBodyStatements = 10;
    // Per-statement cap on nested expansions (a statement like
    // `a*f(x) + b*g(y) + c*h(z)` can carry many calls; the cap bounds the
    // generated-code blowup while still covering the folded shapes).
    public const int MaxNestedExpansionsPerStatement = 16;

    //--------------------------------------------------------------------------
    // Inline: expand every inlineable user call in `stmts`. Returns the
    // transformed statement list and the number of calls expanded. Returns
    // the SAME list instance when nothing was inlined.
    //--------------------------------------------------------------------------
    public static (List<Stmt> Stmts, int Inlined) Inline(
        List<Stmt> stmts, Func<string, InlineCandidate?> lookup)
    {
        var result = new List<Stmt>(stmts.Count);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        CollectNames(usedNames, stmts);
        var freshId = 0;
        var inlined = 0;

        foreach (var s in stmts)
        {
            // Top-level call site (void CallStmt / ExprStmt, or a value-position
            // call that IS the whole AssignStmt/RetStmt value -- the common
            // folded shape `t = call(...)`).
            if (TryGetCallSite(s, out var fn, out var args, out var voidPosition, out var callvirt))
            {
                var candidate = lookup(fn);
                if (candidate != null && IsInlineable(candidate, args, voidPosition)
                    // callvirt receiver gate: inlining a callvirt drops the
                    // dispatch's implicit .NET null-check on the receiver. A body
                    // that never reads arg0 (this) would lose the NRE on a null
                    // receiver -- conservatively keep the call (the dispatch stays
                    // null-checked). Bodies that read this get the same check from
                    // the FieldExpr/FieldAssignStmt emission.
                    && (!callvirt || BodyUsesArg(candidate.Body, "arg0")))
                {
                    var (prefix, resultTemp, expInlined) = BuildExpansion(candidate, args, voidPosition, usedNames, ref freshId, lookup);
                    if (!voidPosition)
                        prefix.Add(Consumer(s, resultTemp!));
                    // M7R3 (exdeep "expected catch"): stamp every inlined
                    // statement with the CALL SITE's IL offset. The inlined
                    // body is a straight-line expansion (IsInlineable rejects
                    // EH bodies) that executes AT the call site -- the EH walk
                    // (CodeGenerator EmitMethodBody) assigns try-region
                    // membership by SourceOffset, so an unstamped statement
                    // (default -1) leaves the enclosing try range: the walk
                    // closes the try block before the region's catch handler
                    // opens -> bare `try { }`, "expected catch" (exdeep f3/f7).
                    foreach (var p in prefix) StampSource(p, s);
                    result.AddRange(prefix);
                    inlined += expInlined;
                    continue;
                }
                // Top-level call not inlineable (or absent): fall through to the
                // nested scan -- the statement may carry an outer expression AND
                // an inlineable user call deeper in its tree.
            }

            // Nested-call expansion: repeatedly inline the FIRST inlineable user
            // call found anywhere in the statement's expression tree (pre-order,
            // left-to-right), rewriting the statement in place until no inlineable
            // nested call remains (or the per-statement cap is hit).
            var current = s;
            var nestedExpansions = 0;
            while (nestedExpansions < MaxNestedExpansionsPerStatement
                && TryExpandNestedCall(current, lookup, usedNames, ref freshId, out var rewritten, out var prelude, out var expInlined))
            {
                // Same stamping contract as the top-level expansion above.
                foreach (var p in prelude) StampSource(p, s);
                result.AddRange(prelude);
                current = rewritten;
                inlined += expInlined;
                nestedExpansions++;
            }
            result.Add(current);
        }
        return inlined == 0 ? (stmts, 0) : (result, inlined);
    }

    //--------------------------------------------------------------------------
    // StampSource -- M7R3 (exdeep "expected catch"): copy the call site's IL
    // offset/opcode onto an inlined statement. The inlined body executes AT
    // the call site (IsInlineable rejects EH bodies, so the expansion is a
    // straight-line fragment of the caller's control flow) -- giving the
    // fragment the call site's offset keeps the EH walk's try-region
    // membership intact (a default -1 offset leaves the enclosing try range,
    // so the walk closes the try block before its catch opens).
    //--------------------------------------------------------------------------
    private static void StampSource(Stmt target, Stmt source)
    {
        target.SourceOffset = source.SourceOffset;
        target.SourceOpName = source.SourceOpName;
    }

    //--------------------------------------------------------------------------
    // The call site of a statement: a TOP-LEVEL user call only (folded IR
    // materializes call results into temps, so calls never nest -- a call
    // nested inside a larger expression is conservatively left alone; the
    // nested-call path below handles those).
    //   value position:  AssignStmt.Value / RetStmt.Value  -> CallExpr
    //   void position:   CallStmt / ExprStmt.Value        -> CallExpr
    //--------------------------------------------------------------------------
    private static bool TryGetCallSite(Stmt s, out string fn, out List<Expr> args,
        out bool voidPosition, out bool callvirt)
    {
        switch (s)
        {
            case CallStmt c when IsUserCall(c.Function, c.Resolved):
                fn = UserCallKey(c.Function);
                args = c.Args;
                voidPosition = true;
                // "vcall_<token>" is a callvirt: the .NET receiver null-check is
                // implicit in the dispatch, so the inline needs the body to read
                // the receiver (see the gate at the call site).
                callvirt = c.Function.StartsWith("vcall_", StringComparison.Ordinal);
                return true;
            case AssignStmt a when IsUserCallExpr(a.Value, out var call):
                fn = call.Function;
                args = call.Args;
                voidPosition = false;
                callvirt = false;
                return true;
            case RetStmt r when r.Value != null && IsUserCallExpr(r.Value, out var call):
                fn = call.Function;
                args = call.Args;
                voidPosition = false;
                callvirt = false;
                return true;
            case ExprStmt e when IsUserCallExpr(e.Value, out var call):
                fn = call.Function;
                args = call.Args;
                voidPosition = true;
                callvirt = false;
                return true;
            default:
                fn = "";
                args = new List<Expr>();
                voidPosition = false;
                callvirt = false;
                return false;
        }
    }

    // User calls only: the Function name is "call_<token>" or "vcall_<token>"
    // (the fold may have pre-resolved the callee into Resolved -- a USER
    // method's CppName -- while BCL icalls carry the "il2cpp::" namespace
    // prefix). "vcall_<token>" is a VOID-position callvirt: StackFold emits it
    // for every void callvirt (Roslyn emits callvirt for EVERY instance call;
    // a non-virtual callee is still a compile-time direct call and inlineable).
    // A resolved USER call is still inlineable; a resolved RUNTIME icall never
    // is (same split as RootSlotPass.IsUserCall).
    private static bool IsUserCall(string fn, ResolvedMethod? resolved)
        => resolved == null
            ? fn.StartsWith("call_", StringComparison.Ordinal) || fn.StartsWith("vcall_", StringComparison.Ordinal)
            : !resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal);

    // The candidate-table key for a call site: the candidate table is keyed by
    // each translatable method's own token ("call_<token>"). A VOID callvirt's
    // "vcall_<token>" Function maps to the same key (the callee's body lives
    // under its method token; virtual callees are rejected by IsInlineable).
    private static string UserCallKey(string fn)
        => fn.StartsWith("vcall_", StringComparison.Ordinal) ? "call_" + fn["vcall_".Length..] : fn;

    private static bool IsUserCallExpr(Expr e, out CallExpr call)
    {
        if (e is CallExpr c && IsUserCall(c.Function, c.Resolved) && c.ResultKind == null)
        {
            call = c;
            return true;
        }
        call = null!;
        return false;
    }

    private static bool IsInlineable(InlineCandidate candidate, List<Expr> args,
        bool voidPosition)
    {
        if (candidate.HasExceptionRegions) return false;   // no EH
        if (candidate.IsVirtual) return false;             // callvirt may dispatch to an override
        if (candidate.ReturnsRef) return false; // int32/void results
        if (!BodyWithinThreshold(candidate)) return false; // conservative threshold
        // D1 (diffdt001 dt.hash): bodies DEFINING a struct-typed local must
        // not inline -- the emitter declares inline temps from expression
        // kinds (int32 default for user calls), so a callee DateTime local
        // renamed to a fresh temp misdeclares as int32_t (HashProbe's
        // `loc0 = D6()` became `inl1 = D6()` with `int32_t inl1`). Non-void
        // struct returns are already excluded by the ReturnsRef gate above;
        // this covers struct traffic through locals in void (or
        // int-returning) bodies.
        if (DefinesStructLocal(candidate)) return false;
        if (args.Count != candidate.ParamCount) return false;
        if (voidPosition != candidate.ReturnsVoid) return false; // position must match void-ness
        if (args.Any(a => a is LocAddrExpr)) return false;  // byref args rejected

        // No recursion: the body must not call its own Function -- directly
        // (CallStmt/CallExpr "call_<token>"), through the callvirt alias
        // ("vcall_<token>", a non-virtual self-call via callvirt), or nested in
        // any expression position (FieldExpr object, branch condition, delegate
        // args, field-assign value).
        if (ContainsSelfCall(candidate.Body, candidate.Function)) return false;

        // Purity gate (the pass's contract is "small PURE functions"): a
        // body that WRITES a static field ("Owner.Field" target) is not pure
        // -- inlining it replicates a shared-state mutation at every call
        // site, defeating the C++ optimizer's local reasoning and inflating
        // hot loops (bench NextRand: hash 6ms -> 8ms with it inlined). The
        // shared write must stay in the one callee function.
        if (BodyWritesStatic(candidate.Body)) return false;

        // Complete return-value paths: every RetStmt in a non-void body must carry a
        // value (t_res is always defined). A VOID body may legitimately fold to
        // zero RetStmts (the trailing IL `ret` is swallowed by the fold, e.g. a
        // ctor whose body is only field stores) -- fall-off-end is the void return.
        var retCount = 0;
        foreach (var b in candidate.Body)
        {
            if (b is not RetStmt ret) continue;
            retCount++;
            if (!candidate.ReturnsVoid && ret.Value == null) return false;
        }
        return retCount > 0 || candidate.ReturnsVoid;
    }

    // D1 gate helper: does the body define (stloc) a struct-typed local?
    // Such a definition becomes a fresh-temp definition at the call site,
    // where the emitter would declare it int32_t (see IsInlineable).
    // LocalKinds index-aligns with loc{i}; absent kinds allow (legacy and
    // test candidates carry none).
    private static bool DefinesStructLocal(InlineCandidate candidate)
    {
        var kinds = candidate.LocalKinds;
        if (kinds == null || kinds.Length == 0) return false;
        foreach (var b in candidate.Body)
        {
            if (b is AssignStmt a && LocIndex(a.Target) is int li
                && li >= 0 && li < kinds.Length && IsStructLocalKind(kinds[li]))
                return true;
        }
        return false;
    }

    private static int? LocIndex(string name)
    {
        if (!name.StartsWith("loc", StringComparison.Ordinal)) return null;
        return int.TryParse(name[3..], out var i) ? i : (int?)null;
    }

    // Struct-typed local kinds: "v:" value-type structs (except the KVP
    // ref-carrier forms the emitter declares Il2CppObject* -- the same two
    // shapes CodeGenerator.cs:2197 special-cases) and "g:"
    // generic-instance locals (the declaration is per-suffix; a renamed
    // temp loses it -- conservative, perf-only over-block for the int/ref
    // suffixes).
    private static bool IsStructLocalKind(string kind)
    {
        if (kind.StartsWith("v:", StringComparison.Ordinal))
        {
            var inner = kind[2..];
            if (inner == "System.Collections.Generic.KeyValuePair`2"
                || inner.StartsWith("System.Collections.Generic.KeyValuePair`2<", StringComparison.Ordinal))
                return false;
            return true;
        }
        if (kind.StartsWith("g:", StringComparison.Ordinal)) return true;
        return false;
    }

    // Threshold: bodies with branches/labels (control-flow merge at the end
    // label) stay at the conservative cap; pure straight-line bodies (the
    // common ctor/initializer shape) may be larger -- their expansion is a
    // flat run with no merge risk.
    private static bool BodyWithinThreshold(InlineCandidate candidate)
    {
        if (candidate.Body.Count <= MaxInlineBodyStatements) return true;
        if (candidate.Body.Count > MaxStraightLineBodyStatements) return false;
        foreach (var b in candidate.Body)
            if (b is BranchStmt or LabelStmt) return false;
        return true;
    }

    private static bool ContainsSelfCall(List<Stmt> body, string fn)
    {
        foreach (var b in body)
            if (ContainsSelfCallExpr(b, fn)) return true;
        return false;
    }

    // A static-field write ("Owner.Field" AssignStmt target) is a shared-state
    // mutation -- the inlining purity gate rejects such bodies.
    private static bool BodyWritesStatic(List<Stmt> body)
    {
        foreach (var b in body)
            if (b is AssignStmt a && a.Target.Contains('.')) return true;
        return false;
    }

    private static bool ContainsSelfCallExpr(Stmt s, string fn) => s switch
    {
        AssignStmt a => ExprCallsFunction(a.Value, fn),
        ExprStmt es => ExprCallsFunction(es.Value, fn),
        RetStmt r => r.Value != null && ExprCallsFunction(r.Value, fn),
        BranchStmt b => b.Condition != null && ExprCallsFunction(b.Condition, fn),
        CallStmt c => UserCallKey(c.Function) == fn || c.Args.Any(a => ExprCallsFunction(a, fn)),
        FieldAssignStmt f => ExprCallsFunction(f.Object, fn) || ExprCallsFunction(f.Value, fn),
        _ => false,
    };

    private static bool ExprCallsFunction(Expr e, string fn) => e switch
    {
        CallExpr c => UserCallKey(c.Function) == fn || c.Args.Any(a => ExprCallsFunction(a, fn)),
        VirtualCallExpr v => $"call_{v.MethodToken}" == fn || v.Args.Any(a => ExprCallsFunction(a, fn)),
        DelegateInvokeExpr d => d.Args.Any(a => ExprCallsFunction(a, fn)),
        BinaryExpr b => ExprCallsFunction(b.Left, fn) || ExprCallsFunction(b.Right, fn),
        UnaryExpr u => ExprCallsFunction(u.Operand, fn),
        FieldExpr f => ExprCallsFunction(f.Object, fn),
        FieldAddrExpr f => ExprCallsFunction(f.Object, fn),
        MarshalUtf8Expr m => ExprCallsFunction(m.Arg, fn),
        _ => false,
    };

    //--------------------------------------------------------------------------
    // Expand: build the replacement statement sequence for ONE top-level call
    // site (the consumer statement appended when the call returns a value).
    //--------------------------------------------------------------------------
    private static Stmt Consumer(Stmt original, string resultTemp) => original switch
    {
        AssignStmt a => new AssignStmt { Target = a.Target, Value = new LocExpr { Name = resultTemp }, SourceOffset = a.SourceOffset, SourceOpName = a.SourceOpName },
        RetStmt r => new RetStmt { Value = new LocExpr { Name = resultTemp }, SourceOffset = r.SourceOffset, SourceOpName = r.SourceOpName },
        _ => throw new InvalidOperationException($"inline consumer for {original.GetType().Name} is not supported"),
    };

    //--------------------------------------------------------------------------
    // BuildExpansion: arg temps + renamed callee body + end label (NO consumer
    // statement). The caller appends the consumer (top-level call) or embeds a
    // result-temp read into the enclosing expression (nested call).
    //
    // Inlined counts every call ACTUALLY expanded: 1 for this body plus every
    // user call found nested inside an argument (each argument is itself run
    // through the nested-call expansion first, so `f(g(x))` counts 2).
    //--------------------------------------------------------------------------
    private static (List<Stmt> Prelude, string? ResultTemp, int Inlined) BuildExpansion(
        InlineCandidate candidate, List<Expr> args, bool voidPosition,
        HashSet<string> usedNames, ref int freshId, Func<string, InlineCandidate?> lookup)
    {
        var prefix = new List<Stmt>();
        string? resultTemp = null;
        var inlined = 1; // this body expansion

        // arg{k} -> fresh temp, only for args the body reads (unused args
        // stay unmaterialized -- no dead C++ locals).
        var argMap = new Dictionary<string, string>();
        for (var k = 0; k < args.Count; k++)
        {
            var argName = $"arg{k}";
            // Inline user calls nested inside the argument (left-to-right
            // evaluation order -- a nested call's expansion runs before this
            // argument's value is consumed).
            var argValue = args[k];
            if (TryExpandInExpr(argValue, lookup, usedNames, ref freshId, out var inlinedArg, out var argPrelude, out var argInlined))
            {
                prefix.AddRange(argPrelude);
                argValue = inlinedArg;
                inlined += argInlined;
            }
            if (!BodyUsesArg(candidate.Body, argName)) continue;
            var temp = FreshName(usedNames, ref freshId);
            argMap[argName] = temp;
            prefix.Add(new AssignStmt { Target = temp, Value = argValue });
        }

        if (!candidate.ReturnsVoid)
            resultTemp = FreshName(usedNames, ref freshId);
        var endLabel = FreshName(usedNames, ref freshId);

        // Callee label rename map (first pass), then rewrite the body.
        var labelMap = new Dictionary<string, string>();
        foreach (var b in candidate.Body)
            if (b is LabelStmt l && !labelMap.ContainsKey(l.Name))
                labelMap[l.Name] = FreshName(usedNames, ref freshId);
        var locMap = new Dictionary<string, string>();

        foreach (var b in candidate.Body)
        {
            var isLastBodyStmt = ReferenceEquals(b, candidate.Body[^1]);
            switch (b)
            {
                case RetStmt ret:
                    // Fall-through optimization: the LAST ret skips the goto (t_res is
                    // written and naturally falls through to L_end); an early-exit ret
                    // still needs the goto (to skip the statements that follow).
                    if (resultTemp != null)
                        prefix.Add(new AssignStmt { Target = resultTemp, Value = RewriteExpr(ret.Value!, argMap, locMap, usedNames, ref freshId) });
                    if (!isLastBodyStmt)
                        prefix.Add(new BranchStmt { TargetLabel = endLabel });
                    break;
                case LabelStmt l:
                    prefix.Add(new LabelStmt { Name = labelMap[l.Name] });
                    break;
                case BranchStmt br:
                    prefix.Add(new BranchStmt
                    {
                        TargetLabel = labelMap.TryGetValue(br.TargetLabel, out var nl) ? nl : br.TargetLabel,
                        Condition = br.Condition != null ? RewriteExpr(br.Condition, argMap, locMap, usedNames, ref freshId) : null,
                    });
                    break;
                case AssignStmt a:
                    // Static-field targets ("Owner.Field") are shared storage,
                    // NOT callee locals: the write must stay on the static slot,
                    // not a fresh inline temp. Renaming a static target would
                    // turn the write into a dead local and the shared value
                    // would never advance (bench NextRand rngState corruption).
                    var target = a.Target.Contains('.')
                        ? a.Target
                        : RenameLoc(a.Target, argMap, locMap, usedNames, ref freshId);
                    prefix.Add(new AssignStmt { Target = target, Value = RewriteExpr(a.Value, argMap, locMap, usedNames, ref freshId) });
                    break;
                case ExprStmt es:
                    prefix.Add(new ExprStmt { Value = RewriteExpr(es.Value, argMap, locMap, usedNames, ref freshId) });
                    break;
                case CallStmt cs:
                    var nc = new CallStmt { Function = cs.Function, Resolved = cs.Resolved };
                    foreach (var arg in cs.Args) nc.Args.Add(RewriteExpr(arg, argMap, locMap, usedNames, ref freshId));
                    prefix.Add(nc);
                    break;
                case FieldAssignStmt f:
                    prefix.Add(new FieldAssignStmt
                    {
                        Object = RewriteExpr(f.Object, argMap, locMap, usedNames, ref freshId),
                        FieldCppName = f.FieldCppName,
                        OwnerFullName = f.OwnerFullName,
                        IsRefField = f.IsRefField,
                        IsValueTypeObject = f.IsValueTypeObject,
                        RefTargetFullName = f.RefTargetFullName,
                        Value = RewriteExpr(f.Value, argMap, locMap, usedNames, ref freshId),
                    });
                    break;
            }
        }

        prefix.Add(new LabelStmt { Name = endLabel });
        return (prefix, resultTemp, inlined);
    }

    //--------------------------------------------------------------------------
    // Nested-call expansion: find the FIRST inlineable user call in the
    // statement's expression tree (pre-order) and expand it in place. The
    // call subtree becomes a result-temp read; the expansion (arg temps +
    // body) becomes prelude statements. Returns false when no inlineable
    // nested call exists -- the statement is then emitted unchanged.
    // expInlined counts every call actually expanded (the outermost one plus
    // any nested calls expanded inside its arguments).
    //--------------------------------------------------------------------------
    private static bool TryExpandNestedCall(Stmt s, Func<string, InlineCandidate?> lookup,
        HashSet<string> usedNames, ref int freshId, out Stmt rewritten, out List<Stmt> prelude,
        out int expInlined)
    {
        prelude = new List<Stmt>();
        rewritten = s;
        expInlined = 0;
        switch (s)
        {
            case AssignStmt a:
                if (TryExpandInExpr(a.Value, lookup, usedNames, ref freshId, out var v, out var vp, out expInlined))
                {
                    prelude = vp;
                    rewritten = new AssignStmt { Target = a.Target, Value = v, SourceOffset = a.SourceOffset, SourceOpName = a.SourceOpName };
                    return true;
                }
                return false;
            case ExprStmt es:
                if (TryExpandInExpr(es.Value, lookup, usedNames, ref freshId, out var ev, out var ep, out expInlined))
                {
                    prelude = ep;
                    rewritten = new ExprStmt { Value = ev, SourceOffset = es.SourceOffset, SourceOpName = es.SourceOpName };
                    return true;
                }
                return false;
            case RetStmt r when r.Value != null:
                if (TryExpandInExpr(r.Value, lookup, usedNames, ref freshId, out var rv, out var rp, out expInlined))
                {
                    prelude = rp;
                    rewritten = new RetStmt { Value = rv, SourceOffset = r.SourceOffset, SourceOpName = r.SourceOpName };
                    return true;
                }
                return false;
            case BranchStmt b when b.Condition != null:
                if (TryExpandInExpr(b.Condition, lookup, usedNames, ref freshId, out var cv, out var cp, out expInlined))
                {
                    prelude = cp;
                    rewritten = new BranchStmt { TargetLabel = b.TargetLabel, Condition = cv, SourceOffset = b.SourceOffset, SourceOpName = b.SourceOpName };
                    return true;
                }
                return false;
            case FieldAssignStmt f:
                if (TryExpandInExpr(f.Object, lookup, usedNames, ref freshId, out var fo, out var fop, out expInlined))
                {
                    prelude = fop;
                    rewritten = CopyFieldAssign(f, fo, f.Value);
                    return true;
                }
                if (TryExpandInExpr(f.Value, lookup, usedNames, ref freshId, out var fv, out var fvp, out expInlined))
                {
                    prelude = fvp;
                    rewritten = CopyFieldAssign(f, f.Object, fv);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static FieldAssignStmt CopyFieldAssign(FieldAssignStmt f, Expr obj, Expr value) => new()
    {
        Object = obj,
        FieldCppName = f.FieldCppName,
        OwnerFullName = f.OwnerFullName,
        IsRefField = f.IsRefField,
        IsValueTypeObject = f.IsValueTypeObject,
        RefTargetFullName = f.RefTargetFullName,
        Value = value,
        SourceOffset = f.SourceOffset,
        SourceOpName = f.SourceOpName,
    };

    // Recursively find + expand the FIRST inlineable user call in an
    // expression (pre-order, left-to-right). On success: newE carries the
    // call replaced by a result-temp read, prelude carries the expansion,
    // expInlined counts every call actually expanded.
    private static bool TryExpandInExpr(Expr e, Func<string, InlineCandidate?> lookup,
        HashSet<string> usedNames, ref int freshId, out Expr newE, out List<Stmt> prelude,
        out int expInlined)
    {
        expInlined = 0;
        switch (e)
        {
            case CallExpr c when IsUserCall(c.Function, c.Resolved) && c.ResultKind == null:
            {
                var candidate = lookup(c.Function);
                if (candidate == null || candidate.ReturnsVoid)
                {
                    newE = e;
                    prelude = new List<Stmt>();
                    return false;
                }
                // Inline the args FIRST (left-to-right evaluation order -- a
                // nested call inside an arg evaluates before the call).
                var argPrelude = new List<Stmt>();
                var newArgs = new List<Expr>();
                var argInlined = 0;
                foreach (var a in c.Args)
                {
                    if (TryExpandInExpr(a, lookup, usedNames, ref freshId, out var na, out var ap, out var ai))
                    {
                        argPrelude.AddRange(ap);
                        newArgs.Add(na);
                        argInlined += ai;
                    }
                    else
                    {
                        newArgs.Add(a);
                    }
                }
                if (!IsInlineable(candidate, newArgs, voidPosition: false))
                {
                    // Conservative: a non-inlineable call is left untouched as a
                    // whole subtree (nothing inside it is inlined either).
                    newE = e;
                    prelude = new List<Stmt>();
                    expInlined = 0;
                    return false;
                }
                var (body, resultTemp, bodyInlined) = BuildExpansion(candidate, newArgs, false, usedNames, ref freshId, lookup);
                prelude = argPrelude;
                prelude.AddRange(body);
                expInlined = argInlined + bodyInlined;
                newE = new LocExpr { Name = resultTemp! };
                return true;
            }
            case VirtualCallExpr v:
            {
                // A NON-virtual instance method called via callvirt is a compile-time
                // direct call (the emitter resolves it to the direct function) -- its
                // body is inlineable exactly like a CallExpr. Virtual candidates are
                // rejected by IsInlineable (the receiver could dispatch to an override).
                var candidate = lookup($"call_{v.MethodToken}");
                if (candidate == null || candidate.ReturnsVoid)
                {
                    newE = e;
                    prelude = new List<Stmt>();
                    return false;
                }
                var vArgPrelude = new List<Stmt>();
                var vNewArgs = new List<Expr>();
                var vArgInlined = 0;
                foreach (var a in v.Args)
                {
                    if (TryExpandInExpr(a, lookup, usedNames, ref freshId, out var na, out var ap, out var ai))
                    {
                        vArgPrelude.AddRange(ap);
                        vNewArgs.Add(na);
                        vArgInlined += ai;
                    }
                    else
                    {
                        vNewArgs.Add(a);
                    }
                }
                if (!IsInlineable(candidate, vNewArgs, voidPosition: false)
                    // callvirt receiver gate (same as the top-level path): the
                    // dispatch's implicit null-check must survive the inline; a
                    // body that never reads arg0 (this) keeps the call.
                    || !BodyUsesArg(candidate.Body, "arg0"))
                {
                    newE = e;
                    prelude = new List<Stmt>();
                    expInlined = 0;
                    return false;
                }
                var (vBody, vResultTemp, vBodyInlined) = BuildExpansion(candidate, vNewArgs, false, usedNames, ref freshId, lookup);
                prelude = vArgPrelude;
                prelude.AddRange(vBody);
                expInlined = vArgInlined + vBodyInlined;
                newE = new LocExpr { Name = vResultTemp! };
                return true;
            }
            case BinaryExpr b:
                if (TryExpandInExpr(b.Left, lookup, usedNames, ref freshId, out var nl, out var lp, out var li))
                {
                    newE = new BinaryExpr { Op = b.Op, Left = nl, Right = b.Right, Kind = b.Kind };
                    prelude = lp;
                    expInlined = li;
                    return true;
                }
                if (TryExpandInExpr(b.Right, lookup, usedNames, ref freshId, out var nr, out var rp, out var ri))
                {
                    newE = new BinaryExpr { Op = b.Op, Left = b.Left, Right = nr, Kind = b.Kind };
                    prelude = rp;
                    expInlined = ri;
                    return true;
                }
                newE = e;
                prelude = new List<Stmt>();
                return false;
            case UnaryExpr u:
                if (TryExpandInExpr(u.Operand, lookup, usedNames, ref freshId, out var uo, out var up, out var ui))
                {
                    newE = new UnaryExpr { Op = u.Op, Operand = uo };
                    prelude = up;
                    expInlined = ui;
                    return true;
                }
                newE = e;
                prelude = new List<Stmt>();
                return false;
            case FieldExpr f:
                if (TryExpandInExpr(f.Object, lookup, usedNames, ref freshId, out var fo, out var fp, out var fi))
                {
                    newE = new FieldExpr
                    {
                        Object = fo,
                        FieldCppName = f.FieldCppName,
                        OwnerFullName = f.OwnerFullName,
                        IsRefField = f.IsRefField,
                        IsValueTypeObject = f.IsValueTypeObject,
                    };
                    prelude = fp;
                    expInlined = fi;
                    return true;
                }
                newE = e;
                prelude = new List<Stmt>();
                return false;
            case FieldAddrExpr f:
                if (TryExpandInExpr(f.Object, lookup, usedNames, ref freshId, out var fao, out var fap, out var fai))
                {
                    newE = new FieldAddrExpr
                    {
                        Object = fao,
                        FieldCppName = f.FieldCppName,
                        OwnerFullName = f.OwnerFullName,
                        IsValueTypeObject = f.IsValueTypeObject,
                    };
                    prelude = fap;
                    expInlined = fai;
                    return true;
                }
                newE = e;
                prelude = new List<Stmt>();
                return false;
            case MarshalUtf8Expr m:
                if (TryExpandInExpr(m.Arg, lookup, usedNames, ref freshId, out var ma, out var map, out var mai))
                {
                    newE = new MarshalUtf8Expr { Arg = ma, Utf16 = m.Utf16 };
                    prelude = map;
                    expInlined = mai;
                    return true;
                }
                newE = e;
                prelude = new List<Stmt>();
                return false;
            case DelegateInvokeExpr d:
            {
                // A delegate Invoke itself is never inlineable (runtime
                // methodPtr dispatch), but user calls nested in its ARGS are --
                // `d.Invoke(f(x), g(y))` still expands f and g left-to-right.
                newE = e;
                prelude = new List<Stmt>();
                expInlined = 0;
                var dArgs = new List<Expr>();
                var dPrelude = new List<Stmt>();
                var dInlined = 0;
                foreach (var a in d.Args)
                {
                    if (TryExpandInExpr(a, lookup, usedNames, ref freshId, out var na, out var ap, out var ai))
                    {
                        dArgs.Add(na);
                        dPrelude.AddRange(ap);
                        dInlined += ai;
                    }
                    else
                    {
                        dArgs.Add(a);
                    }
                }
                if (dPrelude.Count > 0 || dInlined > 0)
                {
                    newE = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
                    ((DelegateInvokeExpr)newE).Args.AddRange(dArgs);
                    prelude = dPrelude;
                    expInlined = dInlined;
                    return true;
                }
                return false;
            }
            default:
                newE = e;
                prelude = new List<Stmt>();
                return false;
        }
    }

    private static bool BodyUsesArg(List<Stmt> body, string argName)
    {
        foreach (var b in body)
            if (StmtUsesLoc(b, argName)) return true;
        return false;
    }

    private static bool StmtUsesLoc(Stmt s, string name) => s switch
    {
        AssignStmt a => ExprUsesLoc(a.Value, name),
        ExprStmt es => ExprUsesLoc(es.Value, name),
        RetStmt r => r.Value != null && ExprUsesLoc(r.Value, name),
        BranchStmt b => b.Condition != null && ExprUsesLoc(b.Condition, name),
        CallStmt c => c.Args.Any(a => ExprUsesLoc(a, name)),
        FieldAssignStmt f => ExprUsesLoc(f.Object, name) || ExprUsesLoc(f.Value, name),
        _ => false,
    };

    private static bool ExprUsesLoc(Expr e, string name) => e switch
    {
        LocExpr l => l.Name == name,
        LocAddrExpr l => l.Name == name,
        BinaryExpr b => ExprUsesLoc(b.Left, name) || ExprUsesLoc(b.Right, name),
        UnaryExpr u => ExprUsesLoc(u.Operand, name),
        CallExpr c => c.Args.Any(a => ExprUsesLoc(a, name)),
        VirtualCallExpr v => v.Args.Any(a => ExprUsesLoc(a, name)),
        DelegateInvokeExpr d => d.Args.Any(a => ExprUsesLoc(a, name)),
        FieldExpr f => ExprUsesLoc(f.Object, name),
        FieldAddrExpr f => ExprUsesLoc(f.Object, name),
        MarshalUtf8Expr m => ExprUsesLoc(m.Arg, name),
        _ => false,
    };

    // Callee-local rename: an arg name maps to its temp; every other name is
    // a callee local/temp that must not collide with caller names.
    private static string RenameLoc(string name, Dictionary<string, string> argMap,
        Dictionary<string, string> locMap, HashSet<string> usedNames, ref int freshId)
    {
        if (argMap.TryGetValue(name, out var t)) return t;
        if (!locMap.TryGetValue(name, out var n))
        {
            // A fresh caller-scope name via the SAME allocator as every other
            // inline temp/label -- the old `inl{locMap.Count}` counter could
            // collide with an already-allocated arg/result/label name, aliasing
            // the callee local onto an arg temp (e.g. a body's `loc0 = arg0`
            // became `inl0 = inl0` when the arg temp already claimed "inl0").
            n = FreshName(usedNames, ref freshId);
            locMap[name] = n;
        }
        return n;
    }

    private static Expr RewriteExpr(Expr e, Dictionary<string, string> argMap,
        Dictionary<string, string> locMap, HashSet<string> usedNames, ref int freshId) => e switch
    {
        LocExpr l => new LocExpr { Name = RenameLoc(l.Name, argMap, locMap, usedNames, ref freshId) },
        LocAddrExpr l => new LocAddrExpr { Name = RenameLoc(l.Name, argMap, locMap, usedNames, ref freshId) },
        BinaryExpr b => new BinaryExpr { Op = b.Op, Left = RewriteExpr(b.Left, argMap, locMap, usedNames, ref freshId), Right = RewriteExpr(b.Right, argMap, locMap, usedNames, ref freshId), Kind = b.Kind },
        UnaryExpr u => new UnaryExpr { Op = u.Op, Operand = RewriteExpr(u.Operand, argMap, locMap, usedNames, ref freshId) },
        CallExpr c => CopyCall(c, argMap, locMap, usedNames, ref freshId),
        VirtualCallExpr v => CopyVirtual(v, argMap, locMap, usedNames, ref freshId),
        DelegateInvokeExpr d => CopyDelegate(d, argMap, locMap, usedNames, ref freshId),
        MarshalUtf8Expr m => new MarshalUtf8Expr { Arg = RewriteExpr(m.Arg, argMap, locMap, usedNames, ref freshId), Utf16 = m.Utf16 },
        FieldExpr f => new FieldExpr
        {
            Object = RewriteExpr(f.Object, argMap, locMap, usedNames, ref freshId),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
        },
        FieldAddrExpr f => new FieldAddrExpr
        {
            Object = RewriteExpr(f.Object, argMap, locMap, usedNames, ref freshId),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsValueTypeObject = f.IsValueTypeObject,
        },
        _ => e,
    };

    private static CallExpr CopyCall(CallExpr c, Dictionary<string, string> argMap,
        Dictionary<string, string> locMap, HashSet<string> usedNames, ref int freshId)
    {
        var nc = new CallExpr
        {
            Function = c.Function,
            ReturnsRef = c.ReturnsRef,
            ResultKind = c.ResultKind,
            Resolved = c.Resolved,
            IsPInvoke = c.IsPInvoke,
            SetLastError = c.SetLastError,
        };
        nc.StringArgIndices.AddRange(c.StringArgIndices);
        foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, argMap, locMap, usedNames, ref freshId));
        return nc;
    }

    private static VirtualCallExpr CopyVirtual(VirtualCallExpr v,
        Dictionary<string, string> argMap, Dictionary<string, string> locMap,
        HashSet<string> usedNames, ref int freshId)
    {
        var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
        foreach (var a in v.Args) nv.Args.Add(RewriteExpr(a, argMap, locMap, usedNames, ref freshId));
        return nv;
    }

    private static DelegateInvokeExpr CopyDelegate(DelegateInvokeExpr d,
        Dictionary<string, string> argMap, Dictionary<string, string> locMap,
        HashSet<string> usedNames, ref int freshId)
    {
        var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
        foreach (var a in d.Args) nd.Args.Add(RewriteExpr(a, argMap, locMap, usedNames, ref freshId));
        return nd;
    }

    //--------------------------------------------------------------------------
    // Fresh-name allocation: "inl<k>" avoiding every name in the caller
    // (locals, labels, temps) -- callee locals/labels never collide.
    //--------------------------------------------------------------------------
    private static string FreshName(HashSet<string> usedNames, ref int freshId)
    {
        string name;
        do { name = $"inl{freshId++}"; } while (!usedNames.Add(name));
        return name;
    }

    private static void CollectNames(HashSet<string> names, IEnumerable<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            if (s is LabelStmt l) names.Add(l.Name);
            else if (s is AssignStmt a) { names.Add(a.Target); CollectNames(names, a.Value); }
            else if (s is BranchStmt b) { names.Add(b.TargetLabel); if (b.Condition != null) CollectNames(names, b.Condition); }
            else if (s is RetStmt r) { if (r.Value != null) CollectNames(names, r.Value); }
            else if (s is CallStmt c) { foreach (var arg in c.Args) CollectNames(names, arg); }
            else if (s is ExprStmt es) CollectNames(names, es.Value);
            else if (s is FieldAssignStmt f) { CollectNames(names, f.Object); CollectNames(names, f.Value); }
        }
    }

    private static void CollectNames(HashSet<string> names, Expr e)
    {
        switch (e)
        {
            case LocExpr l: names.Add(l.Name); break;
            case LocAddrExpr l: names.Add(l.Name); break;
            case BinaryExpr b: CollectNames(names, b.Left); CollectNames(names, b.Right); break;
            case UnaryExpr u: CollectNames(names, u.Operand); break;
            case CallExpr c: foreach (var a in c.Args) CollectNames(names, a); break;
            case VirtualCallExpr v: foreach (var a in v.Args) CollectNames(names, a); break;
            case DelegateInvokeExpr d: foreach (var a in d.Args) CollectNames(names, a); break;
            case MarshalUtf8Expr m: CollectNames(names, m.Arg); break;
            case FieldExpr f: CollectNames(names, f.Object); break;
            case FieldAddrExpr f: CollectNames(names, f.Object); break;
        }
    }

    //--------------------------------------------------------------------------
    // CLI pre-scan hook (Program.cs): re-fold every emitted method body and
    // count the call sites simple-method inlining would expand. Diagnostic
    // only -- never changes generated code (the transform wiring into the
    // emission pipeline is pending CodeGenerator.cs availability).
    //--------------------------------------------------------------------------
    public static (int Inlined, int Methods) ScanAssembly(
        IReadOnlyList<TypeDef> types, string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var accessors = AccessorAnalyzer.Scan(pe, md, types);

        // Candidate table: every translatable method body keyed by its
        // call-site Function name.
        var candidates = new Dictionary<string, InlineCandidate>();
        foreach (var t in types)
        {
            if (t.IsInterface) continue;
            foreach (var m in t.Methods)
            {
                if (m.IsPInvoke || m.IsAbstract) continue;
                if (!MethodBodyReader.HasBody(md, t, m)) continue;
                var bodyInfo = MethodBodyReader.Read(pe, md, t, m);
                if (accessors.ContainsKey(bodyInfo.MethodToken)) continue;
                var stmts = FoldBody(pe, md, t, m, bodyInfo, types, accessors);
                var fn = $"call_{bodyInfo.MethodToken}";
                candidates[fn] = new InlineCandidate
                {
                    Function = fn,
                    ParamCount = bodyInfo.ArgCount,
                    ReturnsVoid = bodyInfo.ReturnsVoid,
                    // Conservative: only int32/void returns are inlineable (the result temp
                    // type is then known).
                    ReturnsRef = !bodyInfo.ReturnsVoid && bodyInfo.ReturnType != SignatureTypeCode.Int32,
                    HasExceptionRegions = bodyInfo.ExceptionRegions.Count > 0,
                    // Virtual methods are never inlined (callvirt may dispatch to an
                    // override -- the compile-time body is not the executed one).
                    IsVirtual = m.IsVirtual,
                    Body = stmts,
                };
            }
        }

        var inlined = 0;
        var methods = 0;
        foreach (var t in types)
        {
            if (t.IsInterface) continue;
            foreach (var m in t.Methods)
            {
                if (m.IsPInvoke || m.IsAbstract) continue;
                if (!MethodBodyReader.HasBody(md, t, m)) continue;
                var bodyInfo = MethodBodyReader.Read(pe, md, t, m);
                if (accessors.ContainsKey(bodyInfo.MethodToken)) continue;
                var stmts = FoldBody(pe, md, t, m, bodyInfo, types, accessors);
                inlined += Inline(stmts, fn => candidates.TryGetValue(fn, out var c) ? c : null).Inlined;
                methods++;
            }
        }
        return (inlined, methods);
    }

    private static List<Stmt> FoldBody(PEReader pe, MetadataReader md, TypeDef t,
        MethodDef m, MethodBodyInfo bodyInfo, IReadOnlyList<TypeDef> types,
        IReadOnlyDictionary<int, Emit.FoldedAccessor> accessors)
    {
        var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
        return StackFold.Fold(insns, new MethodContext
        {
            ArgCount = bodyInfo.ArgCount,
            LocNames = bodyInfo.LocNames,
            LocalKinds = bodyInfo.LocalKinds,
            ReturnsVoid = bodyInfo.ReturnsVoid,
            ExceptionRegions = bodyInfo.ExceptionRegions,
            CctorOwnerFullName = m.Name == ".cctor" ? t.FullName : null,
        }, md, types, accessors, pe);
    }
}
