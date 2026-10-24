using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// TempSlotPass -- temp-slot reuse (stackframe-audit P1-1).
//
// Problem (docs/audit/stackframe-audit.md section 1): StackFold allocates one temp
// per call result (`t{count}`, monotonic, NEVER reused -- StackFold.cs:860
// Push / :920 RegisterJoin / :952 EmitStore); every reference temp is
// declared at function top and registered as a whole-method GC root
// (CodeGenerator.WrapFrameRoots). stringops Main: 32 temps / 18 root slots
// while <=3 references are ever live.
//
// Fix: a pure rename pass (post-Fold, pre-EmitBody). Temps whose live spans
// do not overlap are merged onto one slot name, so the emitter declares one
// variable per slot and registers one GC root per ref slot -- root count
// collapses toward real liveness (18 -> ~4-6, the audit's P1-1 target).
//
// Correctness contract (conservative by construction):
//   * CANDIDATES are temps whose ENTIRE def+use set lies inside ONE maximal
//     straight-line run (a run = consecutive Assign/Expr/Call/FieldAssign
//     statements; Label/Branch/Ret are boundaries). Within a run execution
//     is strictly in-order, so every read sees its own write. A loop back-
//     edge can only re-enter a run at its START (runs contain no labels), so
//     a re-executed run re-executes its defs before its reads. Cross-run
//     sharing is therefore safe for any CFG: two holders never co-live on
//     any path (each holder's reads always follow that holder's own writes
//     within a single run execution).
//   * A GLOBAL linear span (min..max of defs+uses) would NOT be safe alone:
//     a temp defined before a loop and read at the loop header has def/use
//     spans disjoint from body temps, yet a back-edge re-enters its read
//     without its def -- see discover.md #2. The run-containment rule
//     excludes exactly that shape (def and use in different runs).
//   * JOIN slots (the same name assigned in multiple arms, RegisterJoin) are
//     multi-run by construction -> never candidates; and no candidate is ever
//     renamed into a NON-candidate name (poisoned set).
//   * Address-taken temps (LocAddrExpr) are excluded: pointer aliasing makes
//     liveness undecidable.
//   * Slots are single-kind forever: the emitter declares ONE C++ variable
//     per name (CodeGenerator locKind is per-name, :461-487), so only
//     same-kind temps share. Kinds mirror CodeGenerator.ExprKind (:1564-1592,
//     metadata-free). Special declaration shapes (guid uint8_t[16],
//     value-type structs) are excluded entirely.
//   * GC safety: reference slots stay registered as whole-method conservative
//     roots (zero-initialized at entry). A stale reference in a reused slot
//     is a dead object kept alive -- conservative roots are exactly the
//     runtime's contract, never a dangling pointer.
//
// This file is pass-only: it never touches StackFold/CodeGenerator. The CLI
// hook (Program.cs) runs a post-Generate pre-scan (like
// StringPoolPass/LicmPass.ScanAssembly) and reports slot counts; the TRANSFORM
// wiring point (post-Fold, pre-EmitBody in CodeGenerator.cs: `stmts =
// TempSlotPass.Rename(stmts).Stmts`) is pending CodeGenerator.cs being free
// (it is shared/owned by a parallel agent, see process.md).
//------------------------------------------------------------------------------
public static class TempSlotPass
{
    // Slot kinds safe to share under the span rule. Anything with a special
    // declaration shape (guid -> uint8_t[16], value-type structs ->
    // per-type C++ structs with in-place ctor emission) is excluded.
    private static readonly System.Collections.Generic.HashSet<string> EligibleKinds =
        new(System.StringComparer.Ordinal) { "ref", "cstr", "l", "d", "dt", "ts", "val" };

    private static bool IsTemp(string n) =>
        n.Length > 1 && n[0] == 't' && n.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    //--------------------------------------------------------------------------
    // Rename: returns the transformed statement list and the temp-slot counts
    // before/after. Returns the SAME list instance when nothing was renamed.
    //--------------------------------------------------------------------------
    public static (List<Stmt> Stmts, int SlotsBefore, int SlotsAfter) Rename(
        List<Stmt> stmts,
        Func<string, bool>? isRefStaticField = null)
    {
        // Pass 1: type-kind propagation mirroring CodeGenerator.EmitBody's
        // locKind (forward, last-wins -- CodeGenerator.cs:461-487) so the
        // pass's kinds match the emitter's per-name declarations exactly.
        var locKind = new Dictionary<string, string>(StringComparer.Ordinal);
        var defs = new List<(int Pos, string Name)>();
        var uses = new List<(int Pos, string Name, bool Addr)>();
        var allNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < stmts.Count; i++)
        {
            switch (stmts[i])
            {
                case AssignStmt a:
                    allNames.Add(a.Target);
                    if (IsTemp(a.Target))
                    {
                        defs.Add((i, a.Target));
                        var k = KindOf(a.Value, locKind, isRefStaticField);
                        if (k != null) locKind[a.Target] = k;
                    }
                    CollectUses(a.Value, i, uses, allNames);
                    break;
                case ExprStmt es:
                    CollectUses(es.Value, i, uses, allNames);
                    break;
                case RetStmt r when r.Value != null:
                    CollectUses(r.Value, i, uses, allNames);
                    break;
                case BranchStmt b when b.Condition != null:
                    CollectUses(b.Condition, i, uses, allNames);
                    break;
                case CallStmt c:
                    foreach (var a in c.Args) CollectUses(a, i, uses, allNames);
                    break;
                case FieldAssignStmt f:
                    CollectUses(f.Object, i, uses, allNames);
                    CollectUses(f.Value, i, uses, allNames);
                    break;
            }
        }

        // Pass 2: maximal straight-line runs (boundaries: label/branch/ret).
        var runOf = new int[stmts.Count];
        var runId = -1;
        var inRun = false;
        for (var i = 0; i < stmts.Count; i++)
        {
            if (stmts[i] is LabelStmt or BranchStmt or RetStmt) { runOf[i] = -1; inRun = false; }
            else { if (!inRun) { runId++; inRun = true; } runOf[i] = runId; }
        }

        // Pass 3: candidates -- temps fully contained in one run, never
        // address-taken, no use before the first def, single consistent kind.
        var kindOfTemp = new Dictionary<string, string>(StringComparer.Ordinal);
        var spanStart = new Dictionary<string, int>(StringComparer.Ordinal);
        var spanEnd = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidates = new List<string>();
        foreach (var name in defs.Select(d => d.Name).Distinct(StringComparer.Ordinal))
        {
            var addrTaken = uses.Any(u => u.Name == name && u.Addr);
            if (addrTaken) continue;

            var defRuns = defs.Where(d => d.Name == name).Select(d => runOf[d.Pos]).ToList();
            if (defRuns.Any(r => r < 0)) continue;
            var run = defRuns[0];
            if (defRuns.Any(r => r != run)) continue; // multi-run defs (join slots) excluded

            var nameUses = uses.Where(u => u.Name == name).Select(u => u.Pos).ToList();
            if (nameUses.Count > 0 && nameUses.Any(u => runOf[u] != run)) continue;
            if (nameUses.Count > 0 && nameUses.Min() < defs.Where(d => d.Name == name).Min(d => d.Pos)) continue;

            var kind = locKind.TryGetValue(name, out var k) ? k : "val";
            if (!EligibleKinds.Contains(kind)) continue;
            if (defs.Where(d => d.Name == name).Any(d =>
                string.Equals(KindOf(((AssignStmt)stmts[d.Pos]).Value, locKind, isRefStaticField), kind, StringComparison.Ordinal) == false))
                continue; // mixed RHS kinds: the single declaration cannot serve both

            kindOfTemp[name] = kind;
            spanStart[name] = Math.Min(
                defs.Where(d => d.Name == name).Min(d => d.Pos),
                nameUses.Count > 0 ? nameUses.Min() : int.MaxValue);
            spanEnd[name] = Math.Max(
                defs.Where(d => d.Name == name).Max(d => d.Pos),
                nameUses.Count > 0 ? nameUses.Max() : int.MinValue);
            candidates.Add(name);
        }

        var slotsBefore = defs.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count();
        if (candidates.Count == 0)
            return (stmts, slotsBefore, slotsBefore);

        // Pass 4: greedy first-fit interval coloring in span-start order.
        // A slot name is single-kind forever; a candidate may take a name
        // only when no live holder uses it (holder span end < candidate
        // start). Non-candidate names (locals, args, licm temps, join slots,
        // address-taken temps) are poisoned.
        candidates.Sort((a, b) => spanStart[a].CompareTo(spanStart[b]));
        var poison = new HashSet<string>(allNames.Where(n => !candidates.Contains(n)), StringComparer.Ordinal);
        var maxTempIdx = -1;
        foreach (var n in allNames)
            if (IsTemp(n) && int.TryParse(n.AsSpan(1), out var idx) && idx > maxTempIdx)
                maxTempIdx = idx;
        var nameKind = new Dictionary<string, string>(StringComparer.Ordinal);
        var holderEnd = new Dictionary<string, int>(StringComparer.Ordinal);
        var rename = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextFresh = maxTempIdx + 1;
        foreach (var c in candidates)
        {
            var kind = kindOfTemp[c];
            string? picked = null;
            for (var i = 0; i <= maxTempIdx && picked == null; i++)
            {
                var n = $"t{i}";
                if (poison.Contains(n)) continue;
                if (nameKind.TryGetValue(n, out var nk) && nk != kind) continue;
                if (holderEnd.TryGetValue(n, out var he) && he >= spanStart[c]) continue;
                picked = n;
            }
            if (picked == null)
            {
                while (true)
                {
                    var n = $"t{nextFresh++}";
                    if (!poison.Contains(n) && !nameKind.ContainsKey(n)) { picked = n; break; }
                }
            }
            nameKind.TryAdd(picked, kind);
            holderEnd[picked] = spanEnd[c];
            rename[c] = picked;
        }

        var slotsAfter = rename.Values.Distinct(StringComparer.Ordinal)
            .Concat(defs.Select(d => d.Name).Distinct(StringComparer.Ordinal)
                .Where(n => !rename.ContainsKey(n)))
            .Distinct(StringComparer.Ordinal).Count();
        if (rename.Any(kv => kv.Key != kv.Value) == false)
            return (stmts, slotsBefore, slotsAfter);

        // Pass 5: apply the rename (pure name rewrite; statement count/order
        // unchanged).
        var result = new List<Stmt>(stmts.Count);
        for (var i = 0; i < stmts.Count; i++)
            result.Add(RewriteStmt(stmts[i], rename));
        return (result, slotsBefore, slotsAfter);
    }

    //--------------------------------------------------------------------------
    // Kind mirror of CodeGenerator.ExprKind (CodeGenerator.cs:1564-1592) --
    // metadata-free, so the pass reproduces the emitter's per-name C++ type.
    // Value-type newobj returns a sentinel kind (never eligible for sharing).
    //--------------------------------------------------------------------------
    private static string? KindOf(Expr e, Dictionary<string, string> locKind,
        Func<string, bool>? isRefStaticField) => e switch
    {
        // probe3 (closurecase): a reference-typed static field read (the
        // `<>c.<>9__4_0` lambda/delegate cache -- GenericInst Func`1) is a
        // REFERENCE def. The metadata-free default arm classified it "val",
        // so the pass merged its slot with an int32 bool temp and the emitter
        // declared int32_t for a pointer store (cmake FAIL). The caller
        // supplies the metadata-backed predicate (CodeGenerator
        // StaticFieldExprIsReference); null (pure-fold tests) keeps the old
        // behavior.
        StaticFieldExpr sf when isRefStaticField?.Invoke(sf.FieldName) == true => "ref",
        StringConstExpr => "ref",
        NullExpr => "ref",
        ExceptionEntryExpr => "ref",
        NewObjExpr => "ref",
        DelegateNewExpr => "ref",
        MarshalUtf8Expr m => m.Utf16 ? "cstr16" : "cstr",
        ValueTypeNewExpr v => $"vt:{v.TypeFullName}",
        Int64ConstExpr => "l",
        DoubleConstExpr => "d",
        BinaryExpr b when b.Kind is "l" or "d" => b.Kind,
            // net9/10 E2E icallcase fix: "dec"/"ip4"/"bi"/"dto" were missing
            // here (CodeGenerator.cs ExprKind lists them) -- a decimal temp
            // classified "val" shared an int32 slot and the 16-byte
            // icall_decimal_* write overran it.
            // P2-HARDEN #1 (fnptr domain b23): "p" was missing too -- a fnptr
            // void* call result (calli / fnptr DllImport return) classified
            // "val" became an eligible int32 candidate and could share an
            // int32 slot while storing a full-width pointer. "p" stays out of
            // EligibleKinds, so the classified temp is never merged and the
            // emitter declares it void*.
            CallExpr c when c.ResultKind is "l" or "d" or "dt" or "ts" or "guid" or "ptr"
                or "dec" or "ip4" or "bi" or "dto" or "p" => c.ResultKind,
        // tuple1 (ValueTuple): a user call returning a tuple ("vt:<name>")
        // carries the struct type -- a struct temp must never share an int32
        // slot (the store would truncate / not compile).
        CallExpr c when c.ResultKind?.StartsWith("vt:", StringComparison.Ordinal) == true => c.ResultKind,
        CallExpr c when c.ResultKind?.StartsWith("us:", StringComparison.Ordinal) == true => c.ResultKind,
        CallExpr c when c.ResultKind?.StartsWith("sp:", StringComparison.Ordinal) == true => c.ResultKind,
        CallExpr c when c.ReturnsRef => "ref",
        VirtualCallExpr v when v.ReturnsRef => "ref",
        // P2-HARDEN #3: a delegate Invoke returning long/double/float carries
        // ResultKind ("l"/"d"/"float") -- a width-classified temp must not fall
        // to "val" and share an int32 slot with an int64/double/full-width
        // store (the same conflation the dec/ip4 "p" arms close for icalls).
        DelegateInvokeExpr d when d.ResultKind is "l" or "d" or "float" => d.ResultKind,
        DelegateInvokeExpr d when d.ReturnsRef => "ref",
        FieldExpr f when f.IsRefField => "ref",
        // System.Guid.Empty is the shared 16-byte zero slot ("guid" -- the
        // Il2CppGuid value struct); the metadata-free default would call it
        // "val" and let it share an int32 slot.
        StaticFieldExpr sf when sf.FieldName == "System.Guid.Empty" => "guid",
        LocExpr l when locKind.TryGetValue(l.Name, out var k) => k,
        _ => "val",
    };

    private static void CollectUses(Expr e, int pos, List<(int Pos, string Name, bool Addr)> uses,
        HashSet<string> allNames)
    {
        switch (e)
        {
            case LocExpr l:
                allNames.Add(l.Name);
                if (IsTemp(l.Name)) uses.Add((pos, l.Name, false));
                break;
            case LocAddrExpr l:
                allNames.Add(l.Name);
                if (IsTemp(l.Name)) uses.Add((pos, l.Name, true));
                break;
            case BinaryExpr b:
                CollectUses(b.Left, pos, uses, allNames);
                CollectUses(b.Right, pos, uses, allNames);
                break;
            case UnaryExpr u:
                CollectUses(u.Operand, pos, uses, allNames);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectUses(a, pos, uses, allNames);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectUses(a, pos, uses, allNames);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectUses(a, pos, uses, allNames);
                break;
            case DelegateNewExpr dn:
                CollectUses(dn.Target, pos, uses, allNames);
                break;
            case MarshalUtf8Expr m:
                CollectUses(m.Arg, pos, uses, allNames);
                break;
            case FieldExpr f:
                CollectUses(f.Object, pos, uses, allNames);
                break;
            case FieldAddrExpr f:
                CollectUses(f.Object, pos, uses, allNames);
                break;
        }
    }

    private static Stmt RewriteStmt(Stmt s, Dictionary<string, string> rename) => s switch
    {
        AssignStmt a => new AssignStmt
        {
            Target = rename.GetValueOrDefault(a.Target, a.Target),
            Value = RewriteExpr(a.Value, rename),
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        },
        ExprStmt es => new ExprStmt
        {
            Value = RewriteExpr(es.Value, rename),
            SourceOffset = es.SourceOffset,
            SourceOpName = es.SourceOpName,
        },
        RetStmt r => new RetStmt
        {
            Value = r.Value == null ? null : RewriteExpr(r.Value, rename),
            SourceOffset = r.SourceOffset,
            SourceOpName = r.SourceOpName,
        },
        BranchStmt b => new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = b.Condition == null ? null : RewriteExpr(b.Condition, rename),
            SourceOffset = b.SourceOffset,
            SourceOpName = b.SourceOpName,
        },
        CallStmt c => RewriteCallStmt(c, rename),
        FieldAssignStmt f => new FieldAssignStmt
        {
            Object = RewriteExpr(f.Object, rename),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
            RefTargetFullName = f.RefTargetFullName,
            Value = RewriteExpr(f.Value, rename),
            SourceOffset = f.SourceOffset,
            SourceOpName = f.SourceOpName,
        },
        _ => s,
    };

    private static CallStmt RewriteCallStmt(CallStmt c, Dictionary<string, string> rename)
    {
        var nc = new CallStmt
        {
            Function = c.Function,
            Resolved = c.Resolved,
            ThrowCtorParamTypes = c.ThrowCtorParamTypes,
            SourceOffset = c.SourceOffset,
            SourceOpName = c.SourceOpName,
        };
        foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, rename));
        return nc;
    }

    private static Expr RewriteExpr(Expr e, Dictionary<string, string> rename)
    {
        switch (e)
        {
            case LocExpr l when rename.TryGetValue(l.Name, out var n): return new LocExpr { Name = n };
            case LocAddrExpr l when rename.TryGetValue(l.Name, out var n): return new LocAddrExpr { Name = n };
            case BinaryExpr b:
                return new BinaryExpr { Op = b.Op, Left = RewriteExpr(b.Left, rename), Right = RewriteExpr(b.Right, rename), Kind = b.Kind };
            case UnaryExpr u:
                return new UnaryExpr { Op = u.Op, Operand = RewriteExpr(u.Operand, rename) };
            case CallExpr c:
                var nc = new CallExpr { Function = c.Function, ReturnsRef = c.ReturnsRef, ResultKind = c.ResultKind, Resolved = c.Resolved, IsPInvoke = c.IsPInvoke, StringArgIndices = c.StringArgIndices, SetLastError = c.SetLastError };
                foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, rename));
                return nc;
            case VirtualCallExpr v:
                // tuple1 (Tuple/ValueTuple): the arity-8 get_Rest callvirt
                // carries ResultKind "vt:<closedName>" -- the slot-merge rename
                // rebuilds the node, so it MUST copy the kind or the emitter's
                // ExprKind falls back to "val" and declares the tuple result
                // int32_t (vtcreate1 line 23/24 build errors).
                var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName, ResultKind = v.ResultKind };
                foreach (var a in v.Args) nv.Args.Add(RewriteExpr(a, rename));
                return nv;
            case DelegateInvokeExpr d:
                var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
                foreach (var a in d.Args) nd.Args.Add(RewriteExpr(a, rename));
                return nd;
            case MarshalUtf8Expr m:
                return new MarshalUtf8Expr { Arg = RewriteExpr(m.Arg, rename), Utf16 = m.Utf16 };
            case FieldExpr f:
                return new FieldExpr { Object = RewriteExpr(f.Object, rename), FieldCppName = f.FieldCppName, OwnerFullName = f.OwnerFullName, IsRefField = f.IsRefField, IsValueTypeObject = f.IsValueTypeObject };
            case FieldAddrExpr f:
                return new FieldAddrExpr { Object = RewriteExpr(f.Object, rename), FieldCppName = f.FieldCppName, OwnerFullName = f.OwnerFullName, IsValueTypeObject = f.IsValueTypeObject };
            case DelegateNewExpr dn when rename.Count > 0:
                // DCE-1 section 3.1 (dce-plan.md): the delegate ctor's bound-instance
                // target is a temp -- Rename must rewrite it too, or a renamed
                // temp keeps its stale name and the generated code references
                // an undeclared variable (Cases_Delegateinst t4/t31).
                return new DelegateNewExpr { MethodToken = dn.MethodToken, Target = RewriteExpr(dn.Target, rename), TypeId = dn.TypeId };
            default:
                return e;
        }
    }

    //--------------------------------------------------------------------------
    // CLI pre-scan hook (Program.cs, pending): re-fold every emitted method
    // body and report the temp-slot counts the pass would produce. Diagnostic
    // only -- never changes generated code (the transform wiring into
    // Generate is pending CodeGenerator.cs availability, see the header).
    //--------------------------------------------------------------------------
    public static (int SlotsBefore, int SlotsAfter, int Methods) ScanAssembly(
        IReadOnlyList<TypeDef> types, string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var accessors = AccessorAnalyzer.Scan(pe, md, types);

        var slotsBefore = 0;
        var slotsAfter = 0;
        var methods = 0;
        foreach (var t in types)
        {
            if (t.IsInterface) continue;
            foreach (var m in t.Methods)
            {
                if (m.IsPInvoke) continue;
                if (!MethodBodyReader.HasBody(md, t, m)) continue;
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
                var (_, before, after) = Rename(stmts);
                slotsBefore += before;
                slotsAfter += after;
                methods++;
            }
        }
        return (slotsBefore, slotsAfter, methods);
    }
}
