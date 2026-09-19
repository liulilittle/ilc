namespace Il2Cpp.Transpiler.Ir;

using Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// RootClearPass -- ipaudit4: emit a `slot = null;` after the last READ of each
// DEAD VALUE in a reference slot so the GC frame-root table (__frame.refs,
// which holds POINTERS to the slots) stops pinning the object. net48 JIT
// liveness: a slot pins its current value only until that value's last use.
//
// VERIFY-FIX (2.9): slot-granularity global-last-read was insufficient for
// ipaudit4 -- TempSlotPass merges many stack temps onto one slot (t0), so the
// GLOBAL last read of t0 comes far after the GC.Collect, while the value from
// the WeakReference ctor argument died right after the call. The pass now
// walks per-DEFINITION epochs: for every assignment to a ref slot (any
// assignment, including `= null`), the value is live through its last read in
// [defIndex .. nextDefIndex]; the clear is inserted after that read when it is
// strictly inside the epoch. Reads that fall ON the next definition (the
// `s = f(s)` self-update form) cannot be cleared at slot granularity -- those
// epochs stay conservative (root retained) and are reported LOUD on stderr.
//
// Gates (see the work-order patch): linear methods only -- no exception
// regions (caller) and no backward branches (here); reference-producing
// definitions only.
//
// VERIFY-FIX (2.8): the missing IR kinds were corrected against Expr.cs --
// NewArrExpr/CastExpr do not exist (newarr/castclass/isinst are CallExpr with
// ReturnsRef=true, covered by the CallExpr arm); StaticFieldAddrExpr and
// StackFold.DiscardedValueExpr are real leaf kinds and must not hit the
// default throw.
//------------------------------------------------------------------------------
public static class RootClearPass
{
    public sealed class Result
    {
        public List<Stmt> Stmts { get; init; } = new();
        public int ClearedCount { get; init; }
        public int BoundaryCount { get; init; }
    }

    public static Result Run(IReadOnlyList<Stmt> stmts, Func<string, bool> staticFieldIsRef)
    {
        if (stmts.Count == 0 || HasBackwardBranch(stmts))
        {
            return new Result { Stmts = stmts.ToList() };
        }
        // Reference-defined slots: any assign target that is not a parameter
        // and whose value produces a managed reference. Transitive closure:
        // `a = t; b = new X(); ...` -- `a` becomes ref when `t` is ref.
        var refSlots = new HashSet<string>(StringComparer.Ordinal);
        bool IsRefExpr(Expr e) => e switch
        {
            NewObjExpr => true,
            DelegateNewExpr => true,
            // newarr_/isinst_/castclass_ are CallExpr with ReturnsRef=true.
            CallExpr c => c.ReturnsRef,
            DelegateInvokeExpr d => d.ReturnsRef,
            StaticFieldExpr s => staticFieldIsRef(s.FieldName),
            LocExpr l => refSlots.Contains(l.Name),
            _ => false,
        };
        // Closure over definitions (linear def-before-use holds; two passes
        // cover a definition whose source slot is defined later in an epoch).
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < stmts.Count; i++)
            {
                if (stmts[i] is AssignStmt a && !a.Target.StartsWith("arg", StringComparison.Ordinal)
                    && !a.Target.Contains('.') && IsRefExpr(a.Value))
                {
                    refSlots.Add(a.Target);
                }
            }
        }
        if (refSlots.Count == 0)
        {
            return new Result { Stmts = stmts.ToList() };
        }
        // Per-slot definition epochs: EVERY assignment to a ref slot ends the
        // previous epoch (its value may still be read by the next definition's
        // RHS -- the self-update boundary handled below).
        var defs = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < stmts.Count; i++)
        {
            if (stmts[i] is AssignStmt a && refSlots.Contains(a.Target))
            {
                if (!defs.TryGetValue(a.Target, out var list))
                {
                    list = new List<int>();
                    defs[a.Target] = list;
                }
                list.Add(i);
            }
        }
        var readsAt = new List<string[]>(stmts.Count);
        for (var i = 0; i < stmts.Count; i++)
        {
            readsAt.Add(ReadSlots(stmts[i]).Where(n => refSlots.Contains(n)).ToArray());
        }
        var clearAfter = new Dictionary<int, List<string>>();
        var boundaries = 0;
        foreach (var pair in defs)
        {
            var slot = pair.Key;
            var dl = pair.Value;
            for (var k = 0; k < dl.Count; k++)
            {
                var start = dl[k];
                // Epoch value is live until its last read, incl. reads inside
                // the NEXT definition (s = f(s)).
                var end = (k + 1 < dl.Count) ? dl[k + 1] : stmts.Count - 1;
                var lastRead = -1;
                for (var r = start; r <= end && r < stmts.Count; r++)
                {
                    if (Array.IndexOf(readsAt[r], slot) >= 0) lastRead = r;
                }
                if (lastRead < 0) continue;
                if (k + 1 < dl.Count && lastRead == dl[k + 1])
                {
                    // The old value is read by the next definition's RHS
                    // (s = f(s)): cannot null the slot without corrupting that
                    // read. Counted on the next epoch (the same physical read
                    // is that epoch's self-read boundary), so skip silently
                    // here to avoid double counting.
                    continue;
                }
                if (lastRead == start && IsAssignTo(stmts[start], slot))
                {
                    boundaries++;
                    continue;
                }
                if (stmts[lastRead] is RetStmt) continue; // method is ending
                if (lastRead + 1 < stmts.Count && IsAssignTo(stmts[lastRead + 1], slot)) continue; // overwritten / already cleared next
                if (!clearAfter.TryGetValue(lastRead, out var list))
                {
                    list = new List<string>();
                    clearAfter[lastRead] = list;
                }
                if (!list.Contains(slot)) list.Add(slot);
            }
        }
        var result = new List<Stmt>(stmts.Count + clearAfter.Count);
        var cleared = 0;
        for (var i = 0; i < stmts.Count; i++)
        {
            result.Add(stmts[i]);
            if (!clearAfter.TryGetValue(i, out var slots)) continue;
            foreach (var slot in slots)
            {
                result.Add(new AssignStmt
                {
                    Target = slot,
                    Value = new NullExpr(),
                    SourceOffset = stmts[i].SourceOffset,
                    SourceOpName = "rootclear",
                });
                cleared++;
            }
        }
        if (boundaries > 0)
        {
            Console.Error.WriteLine(
                $"[rootclear] {boundaries} self-referential epoch(s) keep a conservative GC root (slot-granularity limit)");
        }
        return new Result { Stmts = result, ClearedCount = cleared, BoundaryCount = boundaries };
    }

    private static bool IsAssignTo(Stmt s, string slot)
    {
        return s is AssignStmt a && a.Target == slot;
    }

    // A branch whose label was already emitted above it = backward edge.
    private static bool HasBackwardBranch(IReadOnlyList<Stmt> stmts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in stmts)
        {
            if (s is LabelStmt l) { seen.Add(l.Name); continue; }
            if (s is BranchStmt b && seen.Contains(b.TargetLabel)) return true;
        }
        return false;
    }

    // Total read-slot walker (arms mirror IrNode.CloneExpr -- a new Expr kind
    // without an arm here fails LOUD, never silently misses a read).
    private static IEnumerable<string> ReadSlots(Stmt s)
    {
        var names = new List<string>();
        void Walk(Expr? e)
        {
            switch (e)
            {
                case null: break;
                case LocExpr l: names.Add(l.Name); break;
                case LocAddrExpr l: names.Add(l.Name); break;
                case BinaryExpr b: Walk(b.Left); Walk(b.Right); break;
                case UnaryExpr u: Walk(u.Operand); break;
                case CallExpr c: foreach (var a in c.Args) Walk(a); break;
                case VirtualCallExpr v: foreach (var a in v.Args) Walk(a); break;
                case DelegateInvokeExpr d: foreach (var a in d.Args) Walk(a); break;
                case DelegateNewExpr dn: Walk(dn.Target); break;
                case MarshalUtf8Expr m: Walk(m.Arg); break;
                case FieldExpr f: Walk(f.Object); break;
                case FieldAddrExpr fa: Walk(fa.Object); break;
                // Leaves: constants, null, static fields (value AND address),
                // newobj, value-type new, funcptr, exception entry, discarded
                // call values -- no local reads.
                case IntConstExpr or Int64ConstExpr or DoubleConstExpr or StringConstExpr
                     or NullExpr or StaticFieldExpr or StaticFieldAddrExpr or NewObjExpr
                     or ValueTypeNewExpr or FuncPtrExpr or ExceptionEntryExpr
                     or StackFold.DiscardedValueExpr:
                    break;
                default:
                    throw new NotSupportedException(
                        $"RootClearPass: expr {e.GetType().Name} has no read-walk arm (IR completeness audit)");
            }
        }
        switch (s)
        {
            case AssignStmt a: Walk(a.Value); break;
            case CallStmt cs: foreach (var a in cs.Args) Walk(a); break;
            case ExprStmt es: Walk(es.Value); break;
            case RetStmt r: Walk(r.Value); break;
            case BranchStmt b: Walk(b.Condition); break;
            case FieldAssignStmt f: Walk(f.Object); Walk(f.Value); break;
            case LabelStmt: break;
            default:
                throw new NotSupportedException(
                    $"RootClearPass: stmt {s.GetType().Name} has no read-walk arm");
        }
        return names;
    }
}
