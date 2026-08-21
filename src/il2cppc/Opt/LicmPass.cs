using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// LICM -- loop-invariant code motion (bench gap: the net48 JIT hoists
// invariant `s.Length` / `a.Length` reads out of loops; the generated C++
// re-evaluated them every iteration). docs/audit/bench-regression-check.md
// §4.4: "net48's loop-invariant hoisting vs ILC allocating per iteration".
//
//   Loop shape (conservative single-block natural loops -- the SAME shape
//   StackFold.HoistLoopGuards accepts; the analysis here is self-contained
//   because StackFold.cs is shared/owned elsewhere, see its precedent):
//     while shape:  [predecessors] goto L_cond; L_header: body; L_cond: if(c) goto L_header
//     do-while:     [predecessors] L_header: body; if(c) goto L_header
//   Reject: multi-entry headers, branches/ret inside the body, nested labels
//   (nested loops / join points), unconditional back-edges (infinite loops).
//
//   Hoist rule -- pure length icalls with ALL-invariant operands only:
//     * Whitelist: il2cpp_string_get_length / il2cpp_array_get_length /
//       icall_array_get_length (Resolved.CppName) + the raw ldlen_<token>
//       inline helper (Function prefix). These are PURE FUNCTIONS of their
//       arguments (string/array length is immutable after allocation -- no
//       observable side effect, no dependence on loop-carried state).
//     * Invariant operand: a constant, or a local never assigned AND never
//       address-taken (ldloca/ldarga -- an opaque callee can write through
//       the pointer; DcePass's address-taken poison rule) anywhere in the
//       body or the loop test (LocAddrExpr/other expression kinds are
//       conservatively NOT invariant).
//     * int32 results only (ResultKind == null, !ReturnsRef) -- the cached
//       temp needs no special C++ type.
//   Safety boundary -- a hoisted evaluation must never RUN where the
//   original never would:
//     * The loop TEST runs >= 1 time in both shapes -> every whitelisted
//       call in it is hoistable.
//     * The BODY runs >= 1 time only in the do-while shape (single fall-
//       through entry) -> every whitelisted call there is hoistable.
//     * A while-shape BODY can run ZERO times; a call that can FAULT would
//       newly execute in the preheader on a zero-trip loop. Only non-
//       faulting calls (il2cpp_array_get_length / the raw ldlen_ helper -- a
//       null receiver returns 0, no throw path) are hoisted from a
//       while-shape body. il2cpp_string_get_length throws
//       NullReferenceException on a null receiver (il2cpp_string.h) and
//       icall_array_get_length validates its receiver + dimension
//       (arrayext.cpp) -- those stay in a while-shape body.
//   NOT hoisted (observable behavior): user-method calls, non-whitelisted
//   icalls (allocating concat etc.), ref results, any call with a
//   loop-variant argument, and any potentially-faulting call in a
//   zero-trip-capable body position.
//
//   This file is pass-only: it never touches StackFold/CodeGenerator. The
//   CLI hook (Program.cs) runs a post-Generate pre-scan (like
//   StringPoolPass.ScanAssembly) and reports hoist opportunities; the
//   TRANSFORM wiring point (post-Fold, pre-EmitBody in CodeGenerator.cs:
//   `stmts = LicmPass.Hoist(stmts).Stmts`) is pending CodeGenerator.cs being
//   free (it is shared/owned by a parallel agent, see process.md).
//------------------------------------------------------------------------------
public static class LicmPass
{
    // Pure length icalls verified as inline field reads in the runtime
    // headers (mirrors CodeGenerator.PureIcallCppNames + the ldlen_ helper).
    private static readonly System.Collections.Generic.HashSet<string> PureLengthIcallCppNames =
        new(System.StringComparer.Ordinal)
        {
            "il2cpp::il2cpp_string_get_length",           // MethodMap.String.cs get_Length
            "il2cpp::il2cpp_array_get_length",            // MethodMap.Array.cs get_Length
            "il2cpp::runtime::icall_array_get_length",    // MethodMap.Array.cs GetLength(int)
        };

    //--------------------------------------------------------------------------
    // Hoist: returns the transformed statement list and the number of
    // invariant calls lifted. Returns the SAME list instance when nothing
    // was hoisted.
    //--------------------------------------------------------------------------
    public static (List<Stmt> Stmts, int Hoisted) Hoist(List<Stmt> stmts)
    {
        var current = new List<Stmt>(stmts);
        var hoisted = 0;
        var nextTempId = 0;
        // Every successful hoist shifts indices -- rescan until a fixed point
        // (same strategy as StackFold.HoistLoopGuards).
        while (true)
        {
            var (changed, insertAt, sites, backEdge, headerIdx) = FindNextHoist(current);
            if (!changed) break;

            // Assign one temp per site (no cross-site dedup -- each site
            // keeps its own cache slot; correctness is unaffected).
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in current)
            {
                if (s is AssignStmt a) usedNames.Add(a.Target);
                CollectLocNames(usedNames, s);
            }
            var rewritten = new Dictionary<CallExpr, string>();
            var preheader = new List<Stmt>();
            foreach (var c in sites)
            {
                string temp;
                do { temp = $"licm{nextTempId++}"; } while (!usedNames.Add(temp));
                // Rewrite the site's OWN operands with the temps assigned so
                // far (inner sites precede outer sites in collection order),
                // so a site nested as another site's operand reads the inner
                // temp and preheader dependencies hold. (Unreachable today:
                // length-call operands are invariant leaves -- LocExpr,
                // constants, invariant binary/unary trees -- never calls.)
                preheader.Add(new AssignStmt { Target = temp, Value = RewriteExpr(c, rewritten) });
                rewritten[c] = temp;
            }
            hoisted += preheader.Count;

            // Replace the call sites inside the body/condition with temp reads.
            for (var j = headerIdx + 1; j < backEdge; j++)
                current[j] = RewriteStmt(current[j], rewritten);
            current[backEdge] = RewriteBackEdge(current[backEdge], rewritten);

            // Insert the preheader computations (in first-use order).
            current.InsertRange(insertAt, preheader);
        }
        // Nothing hoisted: hand back the ORIGINAL list instance (callers may
        // rely on reference identity for no-op passes).
        return hoisted == 0 ? (stmts, 0) : (current, hoisted);
    }

    // Locates the next loop with hoistable invariant calls.
    // Returns the insert position, the preheader statements, and the
    // CallExpr -> temp name map for the rewrite.
    private static (bool Changed, int InsertAt, List<CallExpr> Sites, int BackEdge, int HeaderIdx)
        FindNextHoist(List<Stmt> stmts)
    {
        // Branch target counts: H must have exactly one incoming branch.
        var targetCounts = new Dictionary<string, int>();
        foreach (var b in stmts.OfType<BranchStmt>())
            targetCounts[b.TargetLabel] = targetCounts.TryGetValue(b.TargetLabel, out var n) ? n + 1 : 1;

        for (var h = 0; h < stmts.Count; h++)
        {
            if (stmts[h] is not LabelStmt header) continue;
            var headerName = header.Name;
            if (targetCounts.TryGetValue(headerName, out var hc) && hc != 1) continue;

            // Locate the back-edge: the first branch back to H; any other
            // branch / ret / second label in between rejects the loop.
            var backEdge = -1;
            var labelInBody = -1;
            var bad = false;
            for (var j = h + 1; j < stmts.Count; j++)
            {
                var s = stmts[j];
                if (s is BranchStmt bs)
                {
                    if (bs.TargetLabel == headerName) { backEdge = j; break; }
                    bad = true;
                    break;
                }
                if (s is RetStmt) { bad = true; break; }
                if (s is LabelStmt ls)
                {
                    if (labelInBody >= 0) { bad = true; break; }
                    labelInBody = j;
                    continue;
                }
            }
            if (bad || backEdge < 0) continue;
            if (stmts[backEdge] is not BranchStmt back || back.Condition == null) continue;

            // The single body label must sit right before the back-edge.
            if (labelInBody >= 0)
            {
                if (labelInBody != backEdge - 1) continue;
                if (((LabelStmt)stmts[labelInBody]).Name == headerName) continue;
            }
            var condName = labelInBody >= 0 ? ((LabelStmt)stmts[labelInBody]).Name : null;

            // Preheader position + loop shape:
            //   while shape (unconditional entry goto to the condition label):
            //     preheader before the entry goto; the BODY may run zero times.
            //   do-while shape (fall-through entry, no separate condition
            //     label): preheader before the header label; the BODY is
            //     guaranteed to execute at least once. The header can be the
            //     method's FIRST statement (p < 0) -- preheader before it.
            var p = h - 1;
            var insertAt = -1;
            var isDoWhile = false;
            if (p < 0)
            {
                if (condName != null) continue;
                insertAt = h;
                isDoWhile = true;
            }
            else if (stmts[p] is BranchStmt entryBr)
            {
                if (entryBr.Condition != null) continue;
                if (condName == null || entryBr.TargetLabel != condName) continue;
                if (targetCounts[condName] != 1) continue;
                insertAt = p;
            }
            else
            {
                if (condName != null && targetCounts[condName] != 0) continue;
                insertAt = h;
                isDoWhile = true;
            }

            // Locals assigned in the body are loop-variant; a local whose
            // ADDRESS is taken in the body or the loop test (ldloca/ldarga --
            // an opaque callee can write through the pointer, aliasing the
            // value without an AssignStmt) is variant too (DcePass's
            // address-taken poison rule).
            var variantLocals = new HashSet<string>(StringComparer.Ordinal);
            for (var j = h + 1; j < backEdge; j++)
            {
                if (stmts[j] is AssignStmt a) variantLocals.Add(a.Target);
                CollectAddrTaken(variantLocals, stmts[j]);
            }
            CollectAddrTaken(variantLocals, back.Condition);

            // Collect hoistable call sites in evaluation order (post-order
            // per statement; statement order across the body, then the loop
            // test -- inner calls before outer, so preheader deps hold).
            // Position safety (see the file header): the loop test runs >= 1
            // time in BOTH shapes, so every whitelisted call there is
            // hoistable; the BODY runs >= 1 time only in the do-while shape.
            // A potentially-faulting call must not be moved into a preheader
            // where a zero-trip while loop would newly evaluate it -- body
            // sites in the while shape are limited to non-faulting calls.
            var bodySites = new List<CallExpr>();
            for (var j = h + 1; j < backEdge; j++)
                switch (stmts[j])
                {
                    case AssignStmt a: CollectHoistable(bodySites, a.Value, variantLocals); break;
                    case ExprStmt es: CollectHoistable(bodySites, es.Value, variantLocals); break;
                }
            var condSites = new List<CallExpr>();
            CollectHoistable(condSites, back.Condition, variantLocals);

            var sites = new List<CallExpr>();
            if (isDoWhile)
            {
                sites.AddRange(bodySites);
                sites.AddRange(condSites);
            }
            else
            {
                sites.AddRange(condSites);
                foreach (var s in bodySites)
                    if (!CanFault(s)) sites.Add(s);
            }
            if (sites.Count == 0) continue;
            return (true, insertAt, sites, backEdge, h);
        }
        return (false, -1, new List<CallExpr>(), -1, -1);
    }

    private static void CollectLocNames(HashSet<string> names, Stmt s)
    {
        switch (s)
        {
            case AssignStmt a: CollectLocNames(names, a.Value); break;
            case ExprStmt es: CollectLocNames(names, es.Value); break;
            case RetStmt r: if (r.Value != null) CollectLocNames(names, r.Value); break;
            case BranchStmt b: if (b.Condition != null) CollectLocNames(names, b.Condition); break;
            case CallStmt c: foreach (var a in c.Args) CollectLocNames(names, a); break;
            case FieldAssignStmt f: CollectLocNames(names, f.Object); CollectLocNames(names, f.Value); break;
        }
    }

    private static void CollectLocNames(HashSet<string> names, Expr e)
    {
        switch (e)
        {
            case LocExpr l: names.Add(l.Name); break;
            case LocAddrExpr l: names.Add(l.Name); break;
            case BinaryExpr b: CollectLocNames(names, b.Left); CollectLocNames(names, b.Right); break;
            case UnaryExpr u: CollectLocNames(names, u.Operand); break;
            case CallExpr c: foreach (var a in c.Args) CollectLocNames(names, a); break;
            case VirtualCallExpr v: foreach (var a in v.Args) CollectLocNames(names, a); break;
            case DelegateInvokeExpr d: foreach (var a in d.Args) CollectLocNames(names, a); break;
            case MarshalUtf8Expr m: CollectLocNames(names, m.Arg); break;
            case FieldExpr f: CollectLocNames(names, f.Object); break;
            case FieldAddrExpr f: CollectLocNames(names, f.Object); break;
        }
    }

    // Address-taken locals: every LocAddrExpr (ldloca/ldarga) in the body or
    // the loop test. A callee holding the local's ADDRESS can write through
    // it without an AssignStmt (DcePass's poison rule), so an address-taken
    // local is loop-variant even though the IR never assigns it in the body.
    private static void CollectAddrTaken(HashSet<string> names, Stmt s)
    {
        switch (s)
        {
            case AssignStmt a: CollectAddrTaken(names, a.Value); break;
            case ExprStmt es: CollectAddrTaken(names, es.Value); break;
            case RetStmt r: if (r.Value != null) CollectAddrTaken(names, r.Value); break;
            case BranchStmt b: if (b.Condition != null) CollectAddrTaken(names, b.Condition); break;
            case CallStmt c: foreach (var a in c.Args) CollectAddrTaken(names, a); break;
            case FieldAssignStmt f: CollectAddrTaken(names, f.Object); CollectAddrTaken(names, f.Value); break;
        }
    }

    private static void CollectAddrTaken(HashSet<string> names, Expr e)
    {
        switch (e)
        {
            case LocAddrExpr l: names.Add(l.Name); break;
            case BinaryExpr b: CollectAddrTaken(names, b.Left); CollectAddrTaken(names, b.Right); break;
            case UnaryExpr u: CollectAddrTaken(names, u.Operand); break;
            case CallExpr c: foreach (var a in c.Args) CollectAddrTaken(names, a); break;
            case VirtualCallExpr v: foreach (var a in v.Args) CollectAddrTaken(names, a); break;
            case DelegateInvokeExpr d: foreach (var a in d.Args) CollectAddrTaken(names, a); break;
            case MarshalUtf8Expr m: CollectAddrTaken(names, m.Arg); break;
            case FieldExpr f: CollectAddrTaken(names, f.Object); break;
            case FieldAddrExpr f: CollectAddrTaken(names, f.Object); break;
        }
    }

    // Post-order collection: children before parents (a hoisted outer call
    // whose argument is itself hoistable references the inner temp).
    private static void CollectHoistable(List<CallExpr> sites, Expr e, HashSet<string> variantLocals)
    {
        switch (e)
        {
            case BinaryExpr b:
                CollectHoistable(sites, b.Left, variantLocals);
                CollectHoistable(sites, b.Right, variantLocals);
                return;
            case UnaryExpr u:
                CollectHoistable(sites, u.Operand, variantLocals);
                return;
            case CallExpr c:
                // Children first: an inner hoistable call becomes the outer
                // call's temp argument.
                foreach (var a in c.Args) CollectHoistable(sites, a, variantLocals);
                if (IsHoistable(c, variantLocals)) sites.Add(c);
                return;
            // FieldExpr / VirtualCallExpr / NewObjExpr / ... are never
            // hoistable (only whitelisted length icalls are).
        }
    }

    private static bool IsHoistable(CallExpr c, HashSet<string> variantLocals)
    {
        // int32 value results only -- the cached temp's C++ type stays int32_t.
        if (c.ReturnsRef || c.ResultKind != null) return false;
        if (!IsPureLengthCall(c)) return false;
        foreach (var a in c.Args)
            if (!IsInvariant(a, variantLocals)) return false;
        return true;
    }

    private static bool IsPureLengthCall(CallExpr c)
    {
        if (c.Resolved != null && PureLengthIcallCppNames.Contains(c.Resolved.CppName)) return true;
        return c.Function.StartsWith("ldlen_", StringComparison.Ordinal);
    }

    // Constants and locals never assigned/address-taken in the body or loop
    // test are invariant; every other expression kind is conservatively
    // variant.
    private static bool IsInvariant(Expr e, HashSet<string> variantLocals) => e switch
    {
        IntConstExpr or Int64ConstExpr or DoubleConstExpr or StringConstExpr or NullExpr => true,
        LocExpr l => !variantLocals.Contains(l.Name),
        BinaryExpr b => IsInvariant(b.Left, variantLocals) && IsInvariant(b.Right, variantLocals),
        UnaryExpr u => IsInvariant(u.Operand, variantLocals),
        _ => false,
    };

    // The whitelisted length reads with NO fault path. il2cpp_array_get_length
    // returns 0 for a null receiver (il2cpp_array.h -- a plain conditional
    // field read, no throw) and the raw ldlen_ helper emits the SAME inline
    // read (CodeGenerator's ldlen_ arm). Such a call is safe to hoist from a
    // while-shape BODY: on a zero-trip loop the hoisted evaluation merely
    // runs once and its result is unused -- nothing observable.
    private static readonly System.Collections.Generic.HashSet<string> NonFaultingLengthCppNames =
        new(System.StringComparer.Ordinal)
        {
            "il2cpp::il2cpp_array_get_length",
        };

    private static bool CanFault(CallExpr c)
    {
        if (c.Resolved != null) return !NonFaultingLengthCppNames.Contains(c.Resolved.CppName);
        return false; // the raw ldlen_ inline length helper never faults
    }

    private static Stmt RewriteStmt(Stmt s, Dictionary<CallExpr, string> rewritten) => s switch
    {
        AssignStmt a => new AssignStmt
        {
            Target = a.Target,
            Value = RewriteExpr(a.Value, rewritten),
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        },
        ExprStmt es => new ExprStmt
        {
            Value = RewriteExpr(es.Value, rewritten),
            SourceOffset = es.SourceOffset,
            SourceOpName = es.SourceOpName,
        },
        _ => s,
    };

    private static Stmt RewriteBackEdge(Stmt s, Dictionary<CallExpr, string> rewritten)
    {
        if (s is not BranchStmt b || b.Condition == null) return s;
        return new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = RewriteExpr(b.Condition, rewritten),
            SourceOffset = b.SourceOffset,
            SourceOpName = b.SourceOpName,
        };
    }

    private static Expr RewriteExpr(Expr e, Dictionary<CallExpr, string> rewritten)
    {
        if (e is CallExpr site && rewritten.TryGetValue(site, out var temp)) return new LocExpr { Name = temp };
        switch (e)
        {
            case BinaryExpr b:
                return new BinaryExpr { Op = b.Op, Left = RewriteExpr(b.Left, rewritten), Right = RewriteExpr(b.Right, rewritten), Kind = b.Kind };
            case UnaryExpr u:
                return new UnaryExpr { Op = u.Op, Operand = RewriteExpr(u.Operand, rewritten) };
            case CallExpr c:
                var nc = new CallExpr { Function = c.Function, ReturnsRef = c.ReturnsRef, ResultKind = c.ResultKind, Resolved = c.Resolved, IsPInvoke = c.IsPInvoke, StringArgIndices = c.StringArgIndices, SetLastError = c.SetLastError };
                foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, rewritten));
                return nc;
            default:
                return e;
        }
    }

    //--------------------------------------------------------------------------
    // CLI pre-scan hook (Program.cs): re-fold every emitted method body and
    // count the invariant calls LICM would hoist. Diagnostic only -- never
    // changes generated code (the transform wiring into Generate is pending
    // CodeGenerator.cs availability, see the file header).
    //--------------------------------------------------------------------------
    public static (int Hoisted, int Methods) ScanAssembly(
        IReadOnlyList<TypeDef> types, string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var accessors = AccessorAnalyzer.Scan(pe, md, types);

        var hoisted = 0;
        var methods = 0;
        foreach (var t in types)
        {
            if (t.IsInterface) continue;
            foreach (var m in t.Methods)
            {
                if (m.IsPInvoke) continue;
                var bodyInfo = MethodBodyReader.Read(pe, md, t, m);
                if (accessors.ContainsKey(bodyInfo.MethodToken)) continue;
                var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
                var stmts = StackFold.Fold(insns, new MethodContext
                {
                    ArgCount = bodyInfo.ArgCount,
                    LocNames = bodyInfo.LocNames,
                    LocalKinds = bodyInfo.LocalKinds,
                    ReturnsVoid = bodyInfo.ReturnsVoid,
                    ExceptionRegions = bodyInfo.ExceptionRegions,
                    CctorOwnerFullName = m.Name == ".cctor" ? t.FullName : null,
                }, md, types, accessors, pe);
                hoisted += Hoist(stmts).Hoisted;
                methods++;
            }
        }
        return (hoisted, methods);
    }
}
