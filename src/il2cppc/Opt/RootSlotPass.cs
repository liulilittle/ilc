using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// RootSlotPass -- GC-Root-1 reference-slot liveness analysis.
//
// Problem (user top-priority performance item): WrapFrameRoots registers EVERY
// reference local/parameter of a method as a whole-method GC root by taking
// its address (&slot). An address-taken C++ local cannot be register-
// allocated, so every managed slot stays memory-resident for the method's
// whole duration -- the root cause of the object/string benchmark gap. But
// the collector runs only at ALLOCATION POINTS (newarr / newobj / string
// allocation / any call that may allocate), so a slot whose value is never
// live across an allocation point needs no root: it is a pure local temporary
// the C++ optimizer can keep in a register.
//
// Root(S) == S is address-taken
//        OR S is an argument of an allocating RUNTIME icall
//        OR exists a reachable allocation-point statement i with
//              S live-in of i  AND  i does not redefine S
//              AND S is not merely a USER-call argument at i.
//
// Per-clause rationale:
//   * Address-taken (LocAddrExpr / FieldAddrExpr): pointer aliasing makes
//     liveness undecidable (the callee can read/write the slot -- or write
//     through an interior pointer into a GC object -- across a collection) --
//     root the whole method. Mirrors TempSlotPass's poison rule. FieldAddrExpr
//     (&obj->Field) escapes an interior pointer whose validity depends on the
//     object outliving every later allocation, so its receiver object slot is
//     address-taken too.
//   * Allocating-icall argument: the runtime keeps the caller's roots as the
//     liveness guarantee for its object arguments across its own allocations
//     (staging contract, concat.cpp; RootScope contract, stringbuilder.cpp),
//     and the transpiler cannot prove which icall is self-sufficient, so a
//     slot passed to an allocating icall stays rooted. Only USER-method calls
//     get the exclusion below.
//   * User-call argument at i: a GENERATED callee registers its own reference
//     parameters (arg0 = this for instance methods, every "r:" parameter)
//     before its first allocation, so the argument object is kept alive for
//     the call duration; and between the call and the slot's next use there
//     is no allocation point (liveness would have rooted the slot at it).
//     Excluding user-call args is what lets pure churn loops
//     (new Item; ctor(this); Get(this)) run fully in registers.
//   * Live-in of an allocation point: the collector can run at ANY allocation
//     call INSIDE the statement, not only at its end. Live-in is the set of
//     slots whose value is needed at or after the statement entry: either the
//     statement reads them itself (gen -- a use that may sit AFTER the
//     allocation call inside the statement, e.g. the receiver of a field
//     store `obj->F = AllocatingCall()`) or they flow out to a later use. A
//     slot KILLED by the statement itself (e.g. `S = new T()`) holds a
//     pre-allocation value that is dead -- no root needed at that point.
//     A statement the mayGc predicate flags but that contains NO allocating
//     sub-expression (StmtMayAllocate -- a PURE field store, all leaf reads)
//     is filtered out: it cannot trigger a collection, so it roots nothing.
//     That keeps the ctor-noop pattern (newobj + object_ctor_nop + one field
//     store) frameless -- its this/arg0 is never live across a real
//     allocation.
//
// EH boundary: methods with exception regions keep the whole-method
// conservative model (enforced by the emitter): a catch/finally handler can
// be entered from ANY statement of its try range, so the flat statement list
// cannot capture handler-entry liveness soundly. The pass is not consulted
// for such methods.
//
// The analysis tracks ALL slot names (value and reference); the emitter
// intersects the result with the actual reference slots (ref locals +
// reference parameters).
//------------------------------------------------------------------------------
public static class RootSlotPass
{
    // The per-method analysis result. Every set holds ALL slot names
    // (reference and value) -- the emitter intersects with ref slots.
    public readonly struct Analysis
    {
        public Analysis(HashSet<string> liveAcrossGc, HashSet<string> icallArgs,
            HashSet<string> addrTaken)
        {
            LiveAcrossGc = liveAcrossGc;
            IcallArgs = icallArgs;
            AddrTaken = addrTaken;
            All = new HashSet<string>(StringComparer.Ordinal);
            All.UnionWith(liveAcrossGc);
            All.UnionWith(icallArgs);
            All.UnionWith(addrTaken);
        }

        // Live at an allocation point (live-in of the statement: gen, or
        // live-out minus killed), not redefined by it, and not merely a
        // user-call argument there.
        public HashSet<string> LiveAcrossGc { get; }
        // Argument of an allocating runtime icall (always rooted).
        public HashSet<string> IcallArgs { get; }
        // Address taken somewhere (LocAddrExpr) -- always rooted.
        public HashSet<string> AddrTaken { get; }
        // Union of all three -- the complete root candidate set.
        public HashSet<string> All { get; }
    }

    // Compute the slot names that must be registered as GC roots.
    // 'reachable' is the emitter's reachability mask (dead blocks excluded --
    // the same array the declaration scan uses); 'mayGc' is the emitter's
    // per-statement allocation-capability predicate (StmtMayGc).
    public static Analysis Compute(List<Stmt> stmts, bool[] reachable, Func<Stmt, bool> mayGc)
    {
        var n = stmts.Count;
        var labelIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
            if (stmts[i] is LabelStmt l) labelIdx[l.Name] = i;

        // CFG successors. Over-approximated: a branch keeps BOTH edges unless
        // the fall-through is unreachable (constant folding already removed
        // the dead sides from 'reachable'). An over-approximation only ever
        // ADDS roots (conservative in the safe direction).
        var succ = new List<List<int>>(n);
        for (var i = 0; i < n; i++)
        {
            succ.Add(new List<int>());
            if (!reachable[i]) continue;
            switch (stmts[i])
            {
                case RetStmt:
                    break; // exit: no successors
                case BranchStmt b:
                    if (labelIdx.TryGetValue(b.TargetLabel, out var t)) succ[i].Add(t);
                    if (i + 1 < n && reachable[i + 1]) succ[i].Add(i + 1);
                    break;
                default:
                    if (i + 1 < n && reachable[i + 1]) succ[i].Add(i + 1);
                    break;
            }
        }

        // Gen (LocExpr/address reads) and Kill (assignment targets) per statement.
        var gen = new HashSet<string>[n];
        var kill = new HashSet<string>[n];
        var addrTaken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            var g = new HashSet<string>(StringComparer.Ordinal);
            var k = new HashSet<string>(StringComparer.Ordinal);
            if (reachable[i])
            {
                switch (stmts[i])
                {
                    case AssignStmt a:
                        CollectUses(a.Value, g, addrTaken);
                        k.Add(a.Target);
                        break;
                    case ExprStmt es:
                        CollectUses(es.Value, g, addrTaken);
                        break;
                    case BranchStmt b when b.Condition != null:
                        CollectUses(b.Condition, g, addrTaken);
                        break;
                    case RetStmt r when r.Value != null:
                        CollectUses(r.Value, g, addrTaken);
                        break;
                    case FilterReturnStmt fr:
                        // endfilter reads its boolean result; control then falls
                        // through to the dispatch guard (NOT a function exit).
                        CollectUses(fr.Value, g, addrTaken);
                        break;
                    case CallStmt c:
                        foreach (var a in c.Args) CollectUses(a, g, addrTaken);
                        break;
                    case FieldAssignStmt f:
                        CollectUses(f.Object, g, addrTaken);
                        CollectUses(f.Value, g, addrTaken);
                        break;
                }
            }
            gen[i] = g;
            kill[i] = k;
        }

        // Backward data-flow liveness to a fixed point:
        //   LiveIn[i] = Gen[i] | (LiveOut[i] - Kill[i])
        //   LiveOut[i] = | { LiveIn[j] : j successor of i }
        // A slot live at a point means its current value will be read on some
        // path before being overwritten (back edges included -- the loop-
        // carried case a [def,use] span would miss).
        var liveIn = new HashSet<string>[n];
        var liveOut = new HashSet<string>[n];
        for (var i = 0; i < n; i++)
        {
            liveIn[i] = new HashSet<string>(StringComparer.Ordinal);
            liveOut[i] = new HashSet<string>(StringComparer.Ordinal);
        }
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = n - 1; i >= 0; i--)
            {
                if (!reachable[i]) continue;
                var outSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var j in succ[i])
                    outSet.UnionWith(liveIn[j]);
                if (!SetEqual(liveOut[i], outSet))
                {
                    liveOut[i] = outSet;
                    changed = true;
                }
                var inSet = new HashSet<string>(gen[i], StringComparer.Ordinal);
                foreach (var nm in liveOut[i])
                    if (!kill[i].Contains(nm)) inSet.Add(nm);
                if (!SetEqual(liveIn[i], inSet))
                {
                    liveIn[i] = inSet;
                    changed = true;
                }
            }
        }

        // Root set: allocating-icall arguments (whole method) + slots live at
        // an allocation point (user-call arguments excluded).
        var icallArgs = new HashSet<string>(StringComparer.Ordinal);
        var liveAcross = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            if (!reachable[i]) continue;
            if (!mayGc(stmts[i])) continue;
            // The emitter's mayGc predicate over-approximates some statement
            // kinds (StmtMayGc's `_ => true` flags EVERY FieldAssignStmt as
            // GC-capable, even a PURE field store `obj->F = v` with no call in
            // it). Such a statement cannot trigger a collection, so it is NOT
            // an allocation point: root nothing there. Filtering it keeps the
            // ctor-noop pattern (newobj + object_ctor_nop + one field store)
            // frameless -- this arg0 is never live across a REAL allocation.
            if (!StmtMayAllocate(stmts[i])) continue;
            var userArgs = new HashSet<string>(StringComparer.Ordinal);
            CollectCallArgKinds(stmts[i], userArgs, icallArgs);
            // Live-IN, not live-out: the allocation call can sit INSIDE the
            // statement, so a slot READ by the statement (gen) is live at the
            // allocation even when it is dead after the statement -- e.g. the
            // receiver of `obj->F = AllocatingCall()` must survive the call to
            // write the field. Live-out minus kill would drop it.
            //
            // User-call-arg exclusion refinement: a slot passed to a USER call
            // is excluded ONLY when the statement contains no allocating
            // expression in CALL-ARGUMENT position. The callee roots its own
            // reference parameters at CALL ENTRY -- AFTER every argument is
            // evaluated. An earlier argument that allocates (an icall, a
            // nested newobj, a string-literal intern, a nested user call) can
            // therefore trigger a collection while a LATER argument is still
            // live but not yet rooted by the callee:
            //     Foo(AllocatingCall(), t1)
            // t1's value must survive the allocation inside arg0's evaluation;
            // excluding it would let a collection reclaim it before the callee
            // ever enters. Leaf-argument user calls (the pure churn pattern
            // `ctor(t0); Get(t0)`) keep the exclusion -- their only allocation
            // is the callee's own, which runs after entry (args already
            // rooted). FIX-FRAME2: an intermediate reference slot live across
            // such an in-statement allocation was previously dropped.
            var allocInArgs = ContainsAllocatingArg(stmts[i]);
            foreach (var nm in liveIn[i])
            {
                if (kill[i].Contains(nm)) continue;
                if (userArgs.Contains(nm) && !allocInArgs) continue;
                liveAcross.Add(nm);
            }
        }
        return new Analysis(liveAcross, icallArgs, addrTaken);
    }

    //--------------------------------------------------------------------------
    // Statement/expression "can this allocate" -- the pass's OWN conservative
    // answer, used only to filter the emitter's mayGc predicate (which
    // over-approximates FieldAssignStmt as always GC-capable). Anything
    // unknown or call-like is treated as allocating; only pure leaf reads
    // (constants, locals, static fields, value-type new, addresses) are not.
    // A statement whose every expression is pure cannot trigger a collection
    // even when the caller's predicate flags it.
    //--------------------------------------------------------------------------
    private static bool StmtMayAllocate(Stmt s) => s switch
    {
        AssignStmt a => ExprMayAllocate(a.Value),
        ExprStmt es => ExprMayAllocate(es.Value),
        BranchStmt b => b.Condition != null && ExprMayAllocate(b.Condition),
        RetStmt r => r.Value != null && ExprMayAllocate(r.Value),
        CallStmt c => !IsPureInlineHelper(c.Function) || c.Args.Any(ExprMayAllocate),
        FieldAssignStmt f => ExprMayAllocate(f.Object) || ExprMayAllocate(f.Value),
        _ => true, // unknown statement kinds are conservatively allocating
    };

    // The documented inline/no-op call names (mirrors StmtMayGc's exclusions):
    // pure memory operations and native-adjacent helpers that never allocate.
    private static bool IsPureInlineHelper(string fn)
        => fn.StartsWith("stelem_", StringComparison.Ordinal)
           || fn.StartsWith("ldelem_", StringComparison.Ordinal)
           || fn.StartsWith("ldlen_", StringComparison.Ordinal)
           || fn.StartsWith("capture_last_error", StringComparison.Ordinal)
           || fn.StartsWith("pinvoke_utf8_free", StringComparison.Ordinal)
           || fn.StartsWith("pinvoke_utf16_free", StringComparison.Ordinal)
           || fn.StartsWith("pinvoke_ansi_free", StringComparison.Ordinal);

    private static bool ExprMayAllocate(Expr e) => e switch
    {
        // A string literal may intern-allocate on a first-use miss; every
        // call-like node may allocate (all icalls are treated as allocating
        // here -- over-approximation, safe direction).
        StringConstExpr => true,
        CallExpr or VirtualCallExpr or NewObjExpr or DelegateNewExpr
            or DelegateInvokeExpr or MarshalUtf8Expr or ExceptionEntryExpr => true,
        BinaryExpr b => ExprMayAllocate(b.Left) || ExprMayAllocate(b.Right),
        UnaryExpr u => ExprMayAllocate(u.Operand),
        FieldExpr f => ExprMayAllocate(f.Object),
        FieldAddrExpr fa => ExprMayAllocate(fa.Object),
        _ => false,
    };

    //--------------------------------------------------------------------------
    // Does the statement contain an allocating expression in CALL-ARGUMENT
    // position? Such an allocation runs BEFORE the enclosing call's callee can
    // root its reference parameters (callee rooting starts at call entry, after
    // every argument is evaluated), so a live-in user-call argument is exposed
    // to a collection during the evaluation of an earlier allocating argument.
    // A PURE user call (all leaf arguments) has no such window: its only
    // allocation is the callee's own, which happens after entry -- its
    // arguments are already rooted by then.
    //--------------------------------------------------------------------------
    private static bool ContainsAllocatingArg(Stmt s) => s switch
    {
        AssignStmt a => ContainsAllocatingArgExpr(a.Value),
        ExprStmt es => ContainsAllocatingArgExpr(es.Value),
        BranchStmt b when b.Condition != null => ContainsAllocatingArgExpr(b.Condition),
        RetStmt r when r.Value != null => ContainsAllocatingArgExpr(r.Value),
        CallStmt c => c.Args.Any(ExprMayAllocate),
        FieldAssignStmt f => ContainsAllocatingArgExpr(f.Object) || ContainsAllocatingArgExpr(f.Value),
        _ => true, // unknown statement kinds are conservatively allocating
    };

    // Walk an expression: does any CALL inside it have an argument that may
    // allocate? (The call node itself may allocate, but a callee roots its own
    // arguments at entry -- the call itself is NOT a window for the caller's
    // other arguments; only allocations in ARGUMENT position are.)
    private static bool ContainsAllocatingArgExpr(Expr e) => e switch
    {
        CallExpr c => c.Args.Any(ExprMayAllocate) || c.Args.Any(ContainsAllocatingArgExpr),
        VirtualCallExpr v => v.Args.Any(ExprMayAllocate) || v.Args.Any(ContainsAllocatingArgExpr),
        DelegateInvokeExpr d => d.Args.Any(ExprMayAllocate) || d.Args.Any(ContainsAllocatingArgExpr),
        DelegateNewExpr dn => ContainsAllocatingArgExpr(dn.Target),
        MarshalUtf8Expr m => ContainsAllocatingArgExpr(m.Arg),
        BinaryExpr b => ContainsAllocatingArgExpr(b.Left) || ContainsAllocatingArgExpr(b.Right),
        UnaryExpr u => ContainsAllocatingArgExpr(u.Operand),
        FieldExpr f => ContainsAllocatingArgExpr(f.Object),
        FieldAddrExpr fa => ContainsAllocatingArgExpr(fa.Object),
        _ => false,
    };

    private static bool SetEqual(HashSet<string> a, HashSet<string> b)
        => a.Count == b.Count && a.SetEquals(b);

    //--------------------------------------------------------------------------
    // Expression/statement walks.
    //--------------------------------------------------------------------------

    // LocExpr reads (uses) + LocAddrExpr (address-taken names) in an expression.
    private static void CollectUses(Expr e, HashSet<string> uses, HashSet<string> addrTaken)
    {
        switch (e)
        {
            case LocExpr l:
                uses.Add(l.Name);
                break;
            case LocAddrExpr l:
                uses.Add(l.Name);
                addrTaken.Add(l.Name);
                break;
            case BinaryExpr b:
                CollectUses(b.Left, uses, addrTaken);
                CollectUses(b.Right, uses, addrTaken);
                break;
            case UnaryExpr u:
                CollectUses(u.Operand, uses, addrTaken);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectUses(a, uses, addrTaken);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectUses(a, uses, addrTaken);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectUses(a, uses, addrTaken);
                break;
            case FieldExpr f:
                CollectUses(f.Object, uses, addrTaken);
                break;
            case FieldAddrExpr fa:
                // &obj->Field -- the interior pointer escapes; the object slot
                // must stay rooted for the whole method (a later collection
                // could otherwise reclaim the object the pointer points into).
                CollectUses(fa.Object, uses, addrTaken);
                if (NameOf(fa.Object) is { } slot) addrTaken.Add(slot);
                break;
            case MarshalUtf8Expr m:
                CollectUses(m.Arg, uses, addrTaken);
                break;
            case DelegateNewExpr dn:
                CollectUses(dn.Target, uses, addrTaken);
                break;
        }
    }

    // The root slot name of a field-address receiver: `obj` for `&obj->A->B`
    // (walk the field chain to its base local). Null for a base that is not a
    // local (static field / constant / call result -- no local slot to root;
    // a call-result receiver's own slot, if any, is still recorded as a use).
    private static string? NameOf(Expr e) => e switch
    {
        LocExpr l => l.Name,
        LocAddrExpr l => l.Name,
        FieldExpr f => NameOf(f.Object),
        FieldAddrExpr fa => NameOf(fa.Object),
        _ => null,
    };

    // Split a statement's call arguments into user-call args (the callee roots
    // them -- the caller may omit rooting at this allocation point) and
    // runtime-icall args (the caller must keep rooting them).
    private static void CollectCallArgKinds(Stmt s, HashSet<string> userArgs,
        HashSet<string> icallArgs)
    {
        switch (s)
        {
            case AssignStmt a:
                VisitForCallArgs(a.Value, userArgs, icallArgs);
                break;
            case ExprStmt es:
                VisitForCallArgs(es.Value, userArgs, icallArgs);
                break;
            case BranchStmt b when b.Condition != null:
                VisitForCallArgs(b.Condition, userArgs, icallArgs);
                break;
            case RetStmt r when r.Value != null:
                VisitForCallArgs(r.Value, userArgs, icallArgs);
                break;
            case FilterReturnStmt fr:
                VisitForCallArgs(fr.Value, userArgs, icallArgs);
                break;
            case CallStmt c:
                if (IsUserCall(c.Function, c.Resolved))
                    foreach (var a in c.Args) CollectLocNames(a, userArgs);
                else
                    foreach (var a in c.Args) CollectLocNames(a, icallArgs);
                break;
            case FieldAssignStmt f:
                VisitForCallArgs(f.Object, userArgs, icallArgs);
                VisitForCallArgs(f.Value, userArgs, icallArgs);
                break;
        }
    }

    // Visit an expression, splitting every call it contains by kind.
    private static void VisitForCallArgs(Expr e, HashSet<string> userArgs,
        HashSet<string> icallArgs)
    {
        switch (e)
        {
            case CallExpr c:
                if (IsUserCall(c.Function, c.Resolved))
                    foreach (var a in c.Args) CollectLocNames(a, userArgs);
                else
                    foreach (var a in c.Args) CollectLocNames(a, icallArgs);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectLocNames(a, userArgs);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectLocNames(a, userArgs);
                break;
            case DelegateNewExpr dn:
                CollectLocNames(dn.Target, icallArgs);
                break;
            case MarshalUtf8Expr m:
                CollectLocNames(m.Arg, icallArgs);
                break;
            case BinaryExpr b:
                VisitForCallArgs(b.Left, userArgs, icallArgs);
                VisitForCallArgs(b.Right, userArgs, icallArgs);
                break;
            case UnaryExpr u:
                VisitForCallArgs(u.Operand, userArgs, icallArgs);
                break;
            case FieldExpr f:
                VisitForCallArgs(f.Object, userArgs, icallArgs);
                break;
            case FieldAddrExpr fa:
                VisitForCallArgs(fa.Object, userArgs, icallArgs);
                break;
        }
    }

    // A user-method call: its callee is a GENERATED (transpiled) method that
    // roots its own reference parameters (arg0/this plus every "r:" parameter)
    // before its first allocation. User calls resolve to a Sanitize'd CppName
    // (e.g. BenchArr_BenchArr_Len_<hash>); runtime icalls carry the
    // "il2cpp::" namespace prefix. Unresolved calls are user calls when folded
    // as "call_<token>".
    private static bool IsUserCall(string function, ResolvedMethod? resolved)
        => resolved == null
            ? function.StartsWith("call_", StringComparison.Ordinal)
            : !resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal);

    // All local-name leaves of an expression (LocExpr / LocAddrExpr).
    private static void CollectLocNames(Expr e, HashSet<string> into)
    {
        switch (e)
        {
            case LocExpr l:
                into.Add(l.Name);
                break;
            case LocAddrExpr l:
                into.Add(l.Name);
                break;
            case BinaryExpr b:
                CollectLocNames(b.Left, into);
                CollectLocNames(b.Right, into);
                break;
            case UnaryExpr u:
                CollectLocNames(u.Operand, into);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectLocNames(a, into);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectLocNames(a, into);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectLocNames(a, into);
                break;
            case FieldExpr f:
                CollectLocNames(f.Object, into);
                break;
            case FieldAddrExpr fa:
                CollectLocNames(fa.Object, into);
                break;
            case MarshalUtf8Expr m:
                CollectLocNames(m.Arg, into);
                break;
            case DelegateNewExpr dn:
                CollectLocNames(dn.Target, into);
                break;
        }
    }
}
