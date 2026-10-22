using Il2Cpp.Transpiler.Ir;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// DcePass -- dead-code elimination for generated code (hard principle 4: the
// generated code must contain no dead code).
//
// Problem (docs/plans/dce-plan.md): the IR materializes every call result
// into a temp (`t{count}`, StackFold.Push) and every discarded value into a
// store. Three dead-code shapes survive the existing passes (StackFold's
// OptimizeTemps single-use peephole, Normalize constant folding and
// emission-time reachability):
//   a) UNUSED SLOTS: a temp that is written but never read (sbchain t4 --
//      `t4 = append(...)` where the C# discarded the result with IL pop).
//   b) DEAD STATEMENT PAIRS: `t0 = nullptr; il2cpp_object_ctor_nop(t0);`
//      (the fold product of `throw new X()` -- the "allocation" folded to
//      null and the ctor call is a runtime no-op).
//   c) REDUNDANT COPY CHAINS: `t0 = 5LL; t1 = t0; t2 = t1; ...` (branch
//      br/dup copies) -- pure-value copies Normalize cannot fold because it
//      never propagates values through temps.
//
// Fix: two passes iterated to a fixpoint --
//   Pass 1 (DSE): delete AssignStmt whose target is NEVER read anywhere in
//     the method (zero LocExpr uses). Pure RHS -> delete the whole
//     statement; a side-effecting call RHS -> degrade to a CallStmt (the
//     call still executes; the temp store, its declaration and its GC-root
//     entry disappear). No-op runtime calls (ctor_nop) with statically-known
//     args are deleted outright. Field reads (NRE) and Div/Rem (DivideByZero)
//     RHS are conservatively untouched.
//   Pass 2 (pure-value copy propagation): for a temp with EXACTLY ONE def,
//     replace every read with a deep copy of the def RHS (restricted to
//     pure RHS, def+reads inside one straight-line run, no operand
//     reassigned between def and read). The copy def becomes zero-use ->
//     Pass 1 deletes it next iteration.
//
// Correctness contract (mirrors TempSlotPass's run rule):
//   * Dead stores: a name with ZERO reads on any path has no observable
//     effect, so every def may go regardless of control flow. Multi-def
//     (join) names with zero reads are all deleted -- no read means no path
//     semantics.
//   * Copy propagation requires the single def to DOMINATE every read: a
//     read at u sees the def at d iff d < u and no LabelStmt lies in (d, u]
//     (a label is the only way control enters a straight-line stretch mid-way
//     and bypasses the def; branch/ret evaluation is in-order on every path
//     that reaches it). Loop/back-edge shapes where a read can be reached
//     without re-executing the def (def before a loop header read) are
//     excluded -- the read would see a stale value. This is the exact safety
//     argument behind TempSlotPass's run rule, generalized so branch-
//     condition reads propagate too (the branch-chain collapse dce-plan section 1c
//     requires).
//   * Operands of the propagated RHS must not be reassigned between the def
//     and any read, and must not be address-taken (a pointer write through
//     the address could alias the value without an AssignStmt).
//   * Address-taken names (LocAddrExpr) are poisoned for both passes:
//     pointer aliasing makes liveness undecidable (TempSlotPass's rule).
//   * Excluded entirely (observable behavior / special emission shapes):
//     field reads (NRE), Div/Rem (DivideByZero), guid/dec result slots
//     (output-buffer statement form, CodeGenerator.cs:1276), MarshalUtf8Expr
//     temps (paired pinvoke_utf8_free call), static-field targets (stsfld
//     semantics).
//
// Degradation note (deviation from the plan's "degrade to ExprStmt"
// wording): the emitter has NO ExprStmt case (EmitStmt throws for it), so a
// dead call store degrades to a CallStmt -- the shape StackFold itself
// produces for void/discarded calls (rv=false path). Only CallExpr stores
// degrade (resolved icalls + `call_<token>` user calls ride the generic
// CallStmt path); box_/unbox_/newarr_/isinst_/castclass_/ldlen_ etc. have
// special expression emission and stay as stores (conservative, rare).
//
// This file is pass-only (TempSlotPass/LicmPass convention). The transform
// wiring point is CodeGenerator.cs, after StackFold.Fold and BEFORE
// Normalize.FoldStmts: propagated constants must feed the algebraic fold,
// which turns branch-chain constants into constant conditions that the
// emission-time reachability eliminates (fold-chain closure).
//------------------------------------------------------------------------------
public static class DcePass
{
    //--------------------------------------------------------------------------
    // Run: dead-store elimination + pure-value copy propagation, iterated to
    // a fixpoint. Returns the SAME list instance when nothing changed.
    //--------------------------------------------------------------------------
    public static (List<Stmt> Stmts, int StoresRemoved, int CopiesPropagated) Run(List<Stmt> stmts)
    {
        var current = stmts;
        var storesRemoved = 0;
        var copiesPropagated = 0;
        while (true)
        {
            var dse = EliminateDeadStores(current);
            var prop = PropagateCopies(dse.Stmts);
            storesRemoved += dse.Removed;
            copiesPropagated += prop.Propagated;
            if (dse.Removed == 0 && prop.Propagated == 0)
                return (current, storesRemoved, copiesPropagated);
            current = prop.Stmts;
        }
    }

    //--------------------------------------------------------------------------
    // IntegrityViolations -- DCE's completeness invariant (dce-plan section 4.3 #6):
    // every LocExpr/LocAddrExpr name must have at least one AssignStmt def in
    // the statement list. Used by the unit tests to assert the invariant holds
    // after Run (and to catch the TempSlotPass.DelegateNewExpr rename gap).
    // The plan's decision point 1 keeps this WARNING-level for now (the
    // transpiler does not hard-fail on violations); raising it to a hard error
    // is P1 after the two section 3 bugs are fixed.
    //--------------------------------------------------------------------------
    public static List<string> IntegrityViolations(IReadOnlyList<Stmt> stmts)
    {
        var defNames = new HashSet<string>(StringComparer.Ordinal);
        var readNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in stmts)
            CollectNames(s, defNames, readNames);
        var violations = new List<string>();
        foreach (var n in readNames.OrderBy(n => n, StringComparer.Ordinal))
            if (!defNames.Contains(n))
                violations.Add($"'{n}' is read but never assigned");
        return violations;
    }

    //==========================================================================
    // Pass 0 -- def/use collection (mirror of TempSlotPass.CollectUses,
    // complete over every Expr kind -- DelegateNewExpr.Target included, the
    // section 3.1 lesson). defs = AssignStmt.Target (non static-field); reads =
    // LocExpr; addrTaken = LocAddrExpr names (poison).
    //==========================================================================
    private static void CollectNames(Stmt s, HashSet<string> defs, HashSet<string> reads)
    {
        switch (s)
        {
            case AssignStmt a:
                if (!a.Target.Contains('.')) defs.Add(a.Target);
                CollectNames(a.Value, reads);
                break;
            case ExprStmt es:
                CollectNames(es.Value, reads);
                break;
            case RetStmt r when r.Value != null:
                CollectNames(r.Value, reads);
                break;
            case BranchStmt b when b.Condition != null:
                CollectNames(b.Condition, reads);
                break;
            case CallStmt c:
                foreach (var a in c.Args) CollectNames(a, reads);
                break;
            case FieldAssignStmt f:
                CollectNames(f.Object, reads);
                CollectNames(f.Value, reads);
                break;
        }
    }

    private static void CollectNames(Expr e, HashSet<string> reads)
    {
        switch (e)
        {
            case LocExpr l:
                reads.Add(l.Name);
                break;
            case LocAddrExpr la:
                reads.Add(la.Name);
                break;
            case BinaryExpr b:
                CollectNames(b.Left, reads);
                CollectNames(b.Right, reads);
                break;
            case UnaryExpr u:
                CollectNames(u.Operand, reads);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectNames(a, reads);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectNames(a, reads);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectNames(a, reads);
                break;
            case DelegateNewExpr dn:
                CollectNames(dn.Target, reads);
                break;
            case MarshalUtf8Expr m:
                CollectNames(m.Arg, reads);
                break;
            case FieldExpr f:
                CollectNames(f.Object, reads);
                break;
            case FieldAddrExpr fa:
                CollectNames(fa.Object, reads);
                break;
        }
    }

    private static void CollectStmt(Stmt s, int pos,
        List<(int Pos, string Name)> defs, List<(int Pos, string Name)> reads, HashSet<string> addrTaken)
    {
        switch (s)
        {
            case AssignStmt a:
                if (!a.Target.Contains('.')) defs.Add((pos, a.Target));
                CollectExpr(a.Value, pos, reads, addrTaken);
                break;
            case ExprStmt es:
                CollectExpr(es.Value, pos, reads, addrTaken);
                break;
            case RetStmt r when r.Value != null:
                CollectExpr(r.Value, pos, reads, addrTaken);
                break;
            case BranchStmt b when b.Condition != null:
                CollectExpr(b.Condition, pos, reads, addrTaken);
                break;
            case CallStmt c:
                foreach (var a in c.Args) CollectExpr(a, pos, reads, addrTaken);
                break;
            case FieldAssignStmt f:
                CollectExpr(f.Object, pos, reads, addrTaken);
                CollectExpr(f.Value, pos, reads, addrTaken);
                break;
        }
    }

    private static void CollectExpr(Expr e, int pos,
        List<(int Pos, string Name)> reads, HashSet<string> addrTaken)
    {
        switch (e)
        {
            case LocExpr l:
                reads.Add((pos, l.Name));
                break;
            case LocAddrExpr la:
                addrTaken.Add(la.Name);
                break;
            case BinaryExpr b:
                CollectExpr(b.Left, pos, reads, addrTaken);
                CollectExpr(b.Right, pos, reads, addrTaken);
                break;
            case UnaryExpr u:
                CollectExpr(u.Operand, pos, reads, addrTaken);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectExpr(a, pos, reads, addrTaken);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectExpr(a, pos, reads, addrTaken);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectExpr(a, pos, reads, addrTaken);
                break;
            case DelegateNewExpr dn:
                CollectExpr(dn.Target, pos, reads, addrTaken);
                break;
            case MarshalUtf8Expr m:
                CollectExpr(m.Arg, pos, reads, addrTaken);
                break;
            case FieldExpr f:
                CollectExpr(f.Object, pos, reads, addrTaken);
                break;
            case FieldAddrExpr fa:
                CollectExpr(fa.Object, pos, reads, addrTaken);
                break;
        }
    }

    //==========================================================================
    // Pass 1 -- dead-store elimination (DSE).
    //==========================================================================
    private static (List<Stmt> Stmts, int Removed) EliminateDeadStores(List<Stmt> stmts)
    {
        var defs = new List<(int Pos, string Name)>();
        var reads = new List<(int Pos, string Name)>();
        var addrTaken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < stmts.Count; i++)
            CollectStmt(stmts[i], i, defs, reads, addrTaken);

        var readNames = reads.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in defs.Select(d => d.Name).Distinct(StringComparer.Ordinal))
            if (!addrTaken.Contains(name) && !readNames.Contains(name))
                dead.Add(name);

        var result = new List<Stmt>(stmts.Count);
        var removed = 0;
        foreach (var s in stmts)
        {
            if (s is AssignStmt a && dead.Contains(a.Target))
            {
                if (IsDeletablePure(a.Value))
                {
                    removed++;
                    continue;
                }
                if (TryDegradeToCallStmt(a, out var call))
                {
                    result.Add(call);
                    removed++;
                    continue;
                }
                result.Add(s); // field read / guid-dec / marshal / exotic call: conservative
                continue;
            }
            if (s is CallStmt c && IsNoopCall(c))
            {
                removed++;
                continue;
            }
            result.Add(s);
        }
        return (removed > 0 ? result : stmts, removed);
    }

    // Pure RHS: evaluating it can neither throw nor have a side effect, so the
    // whole store (and with it the slot's declaration and GC-root entry) dies.
    // IsPureArithmetic (Normalize) plus the extended constant/value-type kinds
    // (dce-plan section 4.2 Pass 1). Div/Rem are excluded (DivideByZeroException);
    // FieldExpr/FieldAddrExpr are excluded (NullReferenceException).
    private static bool IsDeletablePure(Expr e) => e switch
    {
        IntConstExpr or Int64ConstExpr or DoubleConstExpr or StringConstExpr
            or NullExpr or LocExpr or ValueTypeNewExpr => true,
        UnaryExpr u => IsDeletablePure(u.Operand),
        BinaryExpr b => IsSafeArithOp(b.Op) && IsDeletablePure(b.Left) && IsDeletablePure(b.Right),
        _ => false,
    };

    private static bool IsSafeArithOp(BinaryOp op) =>
        op is not BinaryOp.Div and not BinaryOp.Rem and not BinaryOp.DivUn;

    // Side-effecting call store -> CallStmt (the call still executes; only the
    // temp store is dropped). The plan says ExprStmt, but EmitStmt has no
    // ExprStmt case (it throws) -- CallStmt is the exact shape StackFold
    // produces for discarded calls and the emitter's generic path handles it.
    // Restricted to the safe subset: resolved icalls + `call_<token>` user
    // calls. box_/unbox_/newarr_/isinst_/castclass_/ldlen_ have special
    // expression emission (EmitTask9Call) and are conservatively kept as
    // stores. guid/dec result slots are excluded (output-buffer emission).
    private static bool TryDegradeToCallStmt(AssignStmt a, out CallStmt call)
    {
        call = null!;
        if (a.Value is not CallExpr c) return false;
        if (c.Resolved?.ResultKind is "guid" or "dec") return false;
        if (c.Resolved == null && !c.Function.StartsWith("call_", StringComparison.Ordinal)) return false;
        var cs = new CallStmt
        {
            Function = c.Function,
            Resolved = c.Resolved,
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        };
        foreach (var arg in c.Args) cs.Args.Add(arg);
        call = cs;
        return true;
    }

    // No-op runtime calls with statically-known args: il2cpp_object_ctor_nop
    // is an empty function (runtime.cpp `(void)this_;`); deleting the call is
    // a pure no-op removal -- the static-arg guard ensures no argument
    // evaluation side effect or exception is lost. The exceptions ctor_nop
    // pair becomes `ctor_nop(nullptr)` after Pass 2 propagates `t0 = nullptr`.
    private static readonly System.Collections.Generic.HashSet<string> NoopRuntimeCalls =
        new(System.StringComparer.Ordinal) { "il2cpp::runtime::il2cpp_object_ctor_nop" };

    private static bool IsNoopCall(CallStmt c) =>
        c.Resolved != null
        && NoopRuntimeCalls.Contains(c.Resolved.CppName)
        && c.Args.All(IsStaticallyKnown);

    private static bool IsStaticallyKnown(Expr e) =>
        e is IntConstExpr or Int64ConstExpr or DoubleConstExpr or StringConstExpr or NullExpr;

    //==========================================================================
    // Pass 2 -- pure-value copy propagation.
    //==========================================================================
    private static (List<Stmt> Stmts, int Propagated) PropagateCopies(List<Stmt> stmts)
    {
        var defs = new List<(int Pos, string Name)>();
        var reads = new List<(int Pos, string Name)>();
        var addrTaken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < stmts.Count; i++)
            CollectStmt(stmts[i], i, defs, reads, addrTaken);

        // Def-dominance stretches: a LabelStmt is the ONLY way control can
        // enter a straight-line stretch mid-way and bypass an earlier def
        // (branch/ret condition/value evaluation is strictly in-order on every
        // path that reaches them). A use at u is dominated by the single def
        // at d iff d < u AND no LabelStmt lies in (d, u] -- every path to u
        // then passes through d, so the propagated value is always the def
        // value. This generalizes TempSlotPass's run rule (whose label/branch/
        // ret partition is a conservative superset): branch-condition reads
        // propagate too -- exactly what the branch-chain collapse (dce-plan
        // section 1c) requires, with the same safety argument.
        var labelBefore = new int[stmts.Count];
        var lastLabel = -1;
        for (var i = 0; i < stmts.Count; i++)
        {
            if (stmts[i] is LabelStmt) lastLabel = i;
            labelBefore[i] = lastLabel;
        }

        var defPos = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var d in defs)
        {
            if (!defPos.TryGetValue(d.Name, out var l)) defPos[d.Name] = l = new List<int>();
            l.Add(d.Pos);
        }

        var candidates = new List<(string Name, int DefPos, Expr Value, List<int> Uses)>();
        foreach (var name in defPos.Keys)
        {
            if (!IsTemp(name)) continue;
            if (defPos[name].Count != 1) continue;          // exactly one def
            if (addrTaken.Contains(name)) continue;         // address-taken: aliasing undecidable
            var d = defPos[name][0];
            var uses = reads.Where(r => r.Name == name).Select(r => r.Pos).ToList();
            if (uses.Count == 0) continue;                  // nothing to propagate (DSE owns it)
            if (uses.Any(u => u <= d || labelBefore[u] > d)) continue; // every use dominated by the def

            var v = ((AssignStmt)stmts[d]).Value;
            if (!IsPropagatable(v)) continue;
            if (ReferencesName(v, name)) continue;          // self-reference (t = t): skip
            var vNames = ReferencedNames(v);
            if (vNames.Any(n => addrTaken.Contains(n))) continue; // alias write could invalidate the value
            var reassigned = false;
            foreach (var u in uses)
            {
                foreach (var n in vNames)
                {
                    if (defPos.TryGetValue(n, out var nl) && nl.Any(dp => dp > d && dp <= u))
                    { reassigned = true; break; }
                }
                if (reassigned) break;
            }
            if (reassigned) continue;
            candidates.Add((name, d, v, uses));
        }
        if (candidates.Count == 0)
            return (stmts, 0);

        var result = new List<Stmt>(stmts.Count);
        var replaced = 0;
        for (var i = 0; i < stmts.Count; i++)
        {
            var here = candidates.Where(c => c.Uses.Contains(i)).ToList();
            if (here.Count == 0)
            {
                result.Add(stmts[i]);
                continue;
            }
            Stmt rewritten = stmts[i];
            foreach (var cand in here)
            {
                rewritten = RewriteReplaceLoc(rewritten, cand.Name, cand.Value);
                replaced++;
            }
            result.Add(rewritten);
        }
        return (replaced > 0 ? result : stmts, replaced);
    }

    private static bool IsTemp(string n) =>
        n.Length > 1 && n[0] == 't' && n.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    // Same pure set as IsDeletablePure minus ValueTypeNewExpr (a copy chain
    // into a use may not be the right place to re-declare the struct) -- pure
    // value propagation only.
    private static bool IsPropagatable(Expr e) => e switch
    {
        IntConstExpr or Int64ConstExpr or DoubleConstExpr or StringConstExpr or NullExpr or LocExpr => true,
        UnaryExpr u => IsPropagatable(u.Operand),
        BinaryExpr b => IsSafeArithOp(b.Op) && IsPropagatable(b.Left) && IsPropagatable(b.Right),
        _ => false,
    };

    private static bool ReferencesName(Expr e, string name) => e switch
    {
        LocExpr l => l.Name == name,
        BinaryExpr b => ReferencesName(b.Left, name) || ReferencesName(b.Right, name),
        UnaryExpr u => ReferencesName(u.Operand, name),
        _ => false,
    };

    private static System.Collections.Generic.HashSet<string> ReferencedNames(Expr e)
    {
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        CollectReferencedNames(e, set);
        return set;
    }

    private static void CollectReferencedNames(Expr e, System.Collections.Generic.HashSet<string> set)
    {
        switch (e)
        {
            case LocExpr l: set.Add(l.Name); break;
            case BinaryExpr b: CollectReferencedNames(b.Left, set); CollectReferencedNames(b.Right, set); break;
            case UnaryExpr u: CollectReferencedNames(u.Operand, set); break;
        }
    }

    private static Stmt RewriteReplaceLoc(Stmt s, string name, Expr value) => s switch
    {
        AssignStmt a => new AssignStmt
        {
            Target = a.Target,
            Value = ReplaceLoc(a.Value, name, value),
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        },
        ExprStmt es => new ExprStmt
        {
            Value = ReplaceLoc(es.Value, name, value),
            SourceOffset = es.SourceOffset,
            SourceOpName = es.SourceOpName,
        },
        RetStmt r => new RetStmt
        {
            Value = r.Value == null ? null : ReplaceLoc(r.Value, name, value),
            SourceOffset = r.SourceOffset,
            SourceOpName = r.SourceOpName,
        },
        BranchStmt b => new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = b.Condition == null ? null : ReplaceLoc(b.Condition, name, value),
            SourceOffset = b.SourceOffset,
            SourceOpName = b.SourceOpName,
        },
        CallStmt c => RewriteCallReplaceLoc(c, name, value),
        FieldAssignStmt f => new FieldAssignStmt
        {
            Object = ReplaceLoc(f.Object, name, value),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
            RefTargetFullName = f.RefTargetFullName,
            Value = ReplaceLoc(f.Value, name, value),
            SourceOffset = f.SourceOffset,
            SourceOpName = f.SourceOpName,
        },
        _ => s,
    };

    private static CallStmt RewriteCallReplaceLoc(CallStmt c, string name, Expr value)
    {
        var nc = new CallStmt
        {
            Function = c.Function,
            Resolved = c.Resolved,
            ThrowCtorParamTypes = c.ThrowCtorParamTypes,
            SourceOffset = c.SourceOffset,
            SourceOpName = c.SourceOpName,
        };
        foreach (var a in c.Args) nc.Args.Add(ReplaceLoc(a, name, value));
        return nc;
    }

    private static Expr ReplaceLoc(Expr e, string name, Expr value)
    {
        if (e is LocExpr l && l.Name == name)
            return DeepCopy(value);
        switch (e)
        {
            case BinaryExpr b:
                return new BinaryExpr
                {
                    Op = b.Op,
                    Left = ReplaceLoc(b.Left, name, value),
                    Right = ReplaceLoc(b.Right, name, value),
                    Kind = b.Kind,
                };
            case UnaryExpr u:
                return new UnaryExpr { Op = u.Op, Operand = ReplaceLoc(u.Operand, name, value) };
            case CallExpr c:
                var nc = new CallExpr
                {
                    Function = c.Function,
                    ReturnsRef = c.ReturnsRef,
                    ResultKind = c.ResultKind,
                    Resolved = c.Resolved,
                    IsPInvoke = c.IsPInvoke,
                    SetLastError = c.SetLastError,
                };
                foreach (var a in c.Args) nc.Args.Add(ReplaceLoc(a, name, value));
                foreach (var i in c.StringArgIndices) nc.StringArgIndices.Add(i);
                return nc;
            case VirtualCallExpr v:
                var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
                foreach (var a in v.Args) nv.Args.Add(ReplaceLoc(a, name, value));
                return nv;
            case DelegateInvokeExpr d:
                var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
                foreach (var a in d.Args) nd.Args.Add(ReplaceLoc(a, name, value));
                return nd;
            case DelegateNewExpr dn:
                return new DelegateNewExpr { MethodToken = dn.MethodToken, Target = ReplaceLoc(dn.Target, name, value), TypeId = dn.TypeId };
            case MarshalUtf8Expr m:
                return new MarshalUtf8Expr { Arg = ReplaceLoc(m.Arg, name, value), Utf16 = m.Utf16 };
            case FieldExpr f:
                return new FieldExpr
                {
                    Object = ReplaceLoc(f.Object, name, value),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                };
            case FieldAddrExpr fa:
                return new FieldAddrExpr
                {
                    Object = ReplaceLoc(fa.Object, name, value),
                    FieldCppName = fa.FieldCppName,
                    OwnerFullName = fa.OwnerFullName,
                    IsValueTypeObject = fa.IsValueTypeObject,
                };
            default:
                return e;
        }
    }

    // Deep structural copy of a pure RHS -- each use substitution gets its own
    // instance so later mutations of one never leak into another.
    private static Expr DeepCopy(Expr e) => e switch
    {
        IntConstExpr i => new IntConstExpr { Value = i.Value, IsInt64 = i.IsInt64 },
        Int64ConstExpr l => new Int64ConstExpr { Value = l.Value },
        DoubleConstExpr d => new DoubleConstExpr { Value = d.Value },
        StringConstExpr s => new StringConstExpr { Value = s.Value },
        NullExpr n => new NullExpr { TypeName = n.TypeName },
        LocExpr l => new LocExpr { Name = l.Name },
        BinaryExpr b => new BinaryExpr { Op = b.Op, Left = DeepCopy(b.Left), Right = DeepCopy(b.Right), Kind = b.Kind },
        UnaryExpr u => new UnaryExpr { Op = u.Op, Operand = DeepCopy(u.Operand) },
        _ => e, // atoms / kinds outside the propagated pure set (defensive)
    };
}
