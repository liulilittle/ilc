using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// TempSlotPass (stackframe-audit P1-1) tests -- TempSlotPass.Rename.
//
// Goal: StackFold allocates one temp per call (`t{count}`, monotonic, never
// reused -- docs/audit/stackframe-audit.md §1); stringops Main balloons to
// 32 temps / 18 GC-root slots while only <=3 references are ever live.
// Rename reuses a dead temp's slot for a later temp whose live span does not
// overlap it, so the emitted variable count (and the frame-root array size,
// which registers by slot) shrinks toward the real liveness (18 -> ~4-6).
//
// Conservative contract:
//   * Only temps whose ENTIRE def+use set lies inside ONE straight-line run
//     (no label/branch/ret between) are candidates -- execution within a run
//     is strictly in-order, so every read sees its own write even when the
//     run re-executes (a loop back-edge can only re-enter a run at its start,
//     because runs have no internal labels).
//   * Join slots (the SAME name assigned in multiple arms, StackFold
//     RegisterJoin) are multi-run by construction -> never candidates, and no
//     candidate may be renamed into any non-candidate name.
//   * Address-taken temps (LocAddrExpr) are excluded (pointer aliasing is
//     undecidable).
//   * Slots are single-kind forever (the emitter declares ONE C++ variable
//     per name, CodeGenerator locKind is per-name) -- only same-kind temps
//     share.
//   * GC safety: reference slots stay registered as conservative roots for
//     the whole method; a stale reference in a reused slot is a dead object
//     kept alive, never a dangling pointer.
//------------------------------------------------------------------------------
public class TempSlotPassTests
{
    private static Expr Call(string fn, bool returnsRef = false) =>
        new CallExpr { Function = fn, ReturnsRef = returnsRef };

    private static Expr CallKind(string fn, string kind) =>
        new CallExpr { Function = fn, ResultKind = kind };

    private static AssignStmt Assign(string target, Expr value) =>
        new AssignStmt { Target = target, Value = value };

    private static ExprStmt Use(string name) => new ExprStmt { Value = new LocExpr { Name = name } };

    private static CallStmt CallStmt(string fn, params Expr[] args)
    {
        var c = new CallStmt { Function = fn };
        c.Args.AddRange(args);
        return c;
    }

    private static bool IsTemp(string n) => n.Length > 1 && n[0] == 't' && n[1..].All(char.IsDigit);

    // The semantic invariant every renaming must preserve: each temp read at
    // position i must see the value written by ITS OWN def -- i.e. walking
    // back from a use, the nearest preceding def of the same (renamed) name
    // must be the def of the SAME original temp. The pass is a pure rename
    // (statement count and order are unchanged), so positions are stable.
    private static void AssertReadsSeeOwnWrites(List<Stmt> before, List<Stmt> after)
    {
        Assert.Equal(before.Count, after.Count);

        var origDefs = new List<(int Pos, string Name)>();
        var origUses = new List<(int Pos, string Name)>();
        var newDefs = new List<(int Pos, string Name)>();
        var newUses = new List<(int Pos, string Name)>();
        for (var i = 0; i < before.Count; i++)
        {
            CollectDefUse(before[i], i, origDefs, origUses);
            CollectDefUse(after[i], i, newDefs, newUses);
        }
        Assert.Equal(origDefs.Count, newDefs.Count);
        Assert.Equal(origUses.Count, newUses.Count);

        for (var u = 0; u < origUses.Count; u++)
        {
            var (usePos, origUseName) = origUses[u];
            var newUseName = newUses[u].Name;
            // nearest preceding def of the same NEW name
            var found = -1;
            for (var d = 0; d < newDefs.Count; d++)
            {
                if (newDefs[d].Pos >= usePos) break;
                if (newDefs[d].Name == newUseName) found = d;
            }
            Assert.True(found >= 0, $"use at {usePos} has no def");
            Assert.Equal(origUseName, origDefs[found].Name);
        }
    }

    private static void CollectDefUse(Stmt s, int pos,
        List<(int, string)> defs, List<(int, string)> uses)
    {
        switch (s)
        {
            case AssignStmt a:
                if (IsTemp(a.Target)) defs.Add((pos, a.Target));
                CollectExprDefUse(a.Value, pos, uses);
                break;
            case ExprStmt es:
                CollectExprDefUse(es.Value, pos, uses);
                break;
            case RetStmt r when r.Value != null:
                CollectExprDefUse(r.Value, pos, uses);
                break;
            case BranchStmt b when b.Condition != null:
                CollectExprDefUse(b.Condition, pos, uses);
                break;
            case CallStmt c:
                foreach (var a in c.Args) CollectExprDefUse(a, pos, uses);
                break;
            case FieldAssignStmt f:
                CollectExprDefUse(f.Object, pos, uses);
                CollectExprDefUse(f.Value, pos, uses);
                break;
        }
    }

    private static void CollectExprDefUse(Expr e, int pos, List<(int, string)> uses)
    {
        switch (e)
        {
            case LocExpr l:
                if (IsTemp(l.Name)) uses.Add((pos, l.Name));
                break;
            case LocAddrExpr l:
                if (IsTemp(l.Name)) uses.Add((pos, l.Name));
                break;
            case BinaryExpr b:
                CollectExprDefUse(b.Left, pos, uses);
                CollectExprDefUse(b.Right, pos, uses);
                break;
            case UnaryExpr u:
                CollectExprDefUse(u.Operand, pos, uses);
                break;
            case CallExpr c:
                foreach (var a in c.Args) CollectExprDefUse(a, pos, uses);
                break;
            case VirtualCallExpr v:
                foreach (var a in v.Args) CollectExprDefUse(a, pos, uses);
                break;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args) CollectExprDefUse(a, pos, uses);
                break;
            case DelegateNewExpr dn:
                CollectExprDefUse(dn.Target, pos, uses);
                break;
            case MarshalUtf8Expr m:
                CollectExprDefUse(m.Arg, pos, uses);
                break;
            case FieldExpr f:
                CollectExprDefUse(f.Object, pos, uses);
                break;
            case FieldAddrExpr f:
                CollectExprDefUse(f.Object, pos, uses);
                break;
        }
    }

    private static HashSet<string> TempTargets(IReadOnlyList<Stmt> stmts) =>
        stmts.OfType<AssignStmt>().Select(a => a.Target).Where(IsTemp).ToHashSet();

    //--------------------------------------------------------------------------
    // Straight-line reuse
    //--------------------------------------------------------------------------

    [Fact]
    public void SequentialValTemps_AllShareOneSlot()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0")), Use("t0"),
            Assign("t1", Call("f1")), Use("t1"),
            Assign("t2", Call("f2")), Use("t2"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(3, result.SlotsBefore);
        Assert.Equal(1, result.SlotsAfter);
        Assert.Single(TempTargets(result.Stmts));
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void SequentialRefTemps_AllShareOneSlot()
    {
        // reference temps: one slot -> ONE frame-root slot (the P1-1 win)
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0", true)), Use("t0"),
            Assign("t1", Call("f1", true)), Use("t1"),
            Assign("t2", Call("f2", true)), Use("t2"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(1, result.SlotsAfter);
        Assert.Single(TempTargets(result.Stmts));
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void AlternatingKinds_OneSlotPerKind()
    {
        // a slot is single-kind forever (one C++ declaration per name): ref
        // and val temps must NOT share, so refs collapse to one slot and vals
        // to another
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0", true)), Use("t0"),
            Assign("t1", Call("f1")),       Use("t1"),
            Assign("t2", Call("f2", true)), Use("t2"),
            Assign("t3", Call("f3")),       Use("t3"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void OverlappingLifetimes_NoSharedSlot()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0")),
            Assign("t1", Call("f1")),
            Use("t0"),
            Use("t1"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void CopyChain_OverlappingSpans_NoShare()
    {
        // t1 = t0 keeps t0 alive through the copy: spans overlap -> no share
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0", true)),
            Assign("t1", new LocExpr { Name = "t0" }),
            Use("t1"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void MarshalCstrTemps_Disjoint_Share()
    {
        // P/Invoke marshal buffers: char* temps with disjoint spans share a
        // slot (the first buffer is freed before the second is allocated)
        var stmts = new List<Stmt>
        {
            Assign("t0", new MarshalUtf8Expr { Arg = new LocExpr { Name = "s" } }),
            CallStmt("pinvoke_a", new LocExpr { Name = "t0" }),
            Assign("t1", new MarshalUtf8Expr { Arg = new LocExpr { Name = "s" } }),
            CallStmt("pinvoke_b", new LocExpr { Name = "t1" }),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(1, result.SlotsAfter);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // Conservative: joins, branches, loops
    //--------------------------------------------------------------------------

    [Fact]
    public void JoinShape_ArmLocals_ShareAcrossArms_JoinUntouched()
    {
        // if (c) { t1 = f1; use(t1) } else { t2 = f2; use(t2) }; t3 = f3; use(t3)
        // -- arm locals never co-live (only one arm runs), so they share;
        // labels/branches keep their exact shape
        var stmts = new List<Stmt>
        {
            Assign("t1", Call("f1")), Use("t1"),
            new BranchStmt { TargetLabel = "L_9", Condition = new LocExpr { Name = "loc0" } },
            new LabelStmt { Name = "L_8" },
            Assign("t2", Call("f2")), Use("t2"),
            new LabelStmt { Name = "L_9" },
            Assign("t3", Call("f3")), Use("t3"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(1, result.SlotsAfter);
        Assert.Equal("L_9", Assert.IsType<BranchStmt>(result.Stmts[2]).TargetLabel);
        Assert.Equal("L_8", Assert.IsType<LabelStmt>(result.Stmts[3]).Name);
        Assert.Equal("L_9", Assert.IsType<LabelStmt>(result.Stmts[6]).Name);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void JoinTemp_MultiDef_Untouched()
    {
        // the SAME name assigned in two arms + used at the join is a
        // RegisterJoin slot: multi-run by construction -> never a candidate
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0")),
            new BranchStmt { TargetLabel = "L_9", Condition = new LocExpr { Name = "loc0" } },
            new LabelStmt { Name = "L_8" },
            Assign("t0", Call("f1")),
            new LabelStmt { Name = "L_9" },
            Use("t0"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(1, result.SlotsBefore);
        Assert.Equal(1, result.SlotsAfter);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(result.Stmts[0]).Target);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(result.Stmts[3]).Target);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void LoopShape_CrossRunTemp_NotShared_NoCandidateTakesItsName()
    {
        // while-shape: t0 is defined BEFORE the loop but read INSIDE the body
        // and after the loop -- its lifetime crosses runs, and a loop back-edge
        // re-enters the body, so it must never share a slot with a body temp
        // (the def would not re-execute on every re-entry). Body temps t1/t2
        // share safely among themselves.
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0")),
            new BranchStmt { TargetLabel = "L_30" },
            new LabelStmt { Name = "L_4" },
            Use("t0"),
            Assign("t1", Call("f1")), Use("t1"),
            Assign("t2", Call("f2")), Use("t2"),
            new LabelStmt { Name = "L_30" },
            new BranchStmt { TargetLabel = "L_4", Condition = new LocExpr { Name = "loc0" } },
            Use("t0"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(result.Stmts[0]).Target);
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result.Stmts[2]).Name);
        Assert.Equal("L_30", Assert.IsType<LabelStmt>(result.Stmts[8]).Name);
        Assert.Equal("L_4", Assert.IsType<BranchStmt>(result.Stmts[9]).TargetLabel);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // Exclusions
    //--------------------------------------------------------------------------

    [Fact]
    public void AddrTakenTemp_NotRenamed_AndSlotNotTaken()
    {
        // t1's address escapes (LocAddrExpr into a call): pointer aliasing is
        // undecidable, so t1 keeps its name and no candidate may take it
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0")),
            Assign("t1", Call("f1")),
            CallStmt("sink", new LocAddrExpr { Name = "t1" }),
            Use("t0"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        Assert.Equal("t1", Assert.IsType<AssignStmt>(result.Stmts[1]).Target);
        Assert.Equal("t1", Assert.IsType<LocAddrExpr>(Assert.IsType<CallStmt>(result.Stmts[2]).Args[0]).Name);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void GuidKindTemp_Excluded()
    {
        // guid slots are uint8_t[16] arrays with special declaration/copy
        // emission -- never shared
        var stmts = new List<Stmt>
        {
            Assign("t0", CallKind("guidcall", "guid")),
            Use("t0"),
            Assign("t1", Call("f1")),
            Use("t1"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(2, result.SlotsAfter);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(result.Stmts[0]).Target);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void LicmTemp_Untouched()
    {
        // licm temps (LicmPass preheader caches) are NOT StackFold temps and
        // are never renamed
        var stmts = new List<Stmt>
        {
            Assign("licm0", Call("f0")),
            Assign("t0", Call("f1")), Use("t0"),
            Assign("t1", Call("f2")), Use("t1"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal("licm0", Assert.IsType<AssignStmt>(result.Stmts[0]).Target);
        Assert.Equal(1, result.SlotsAfter);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void UseBeforeDef_Skipped_NoCrash()
    {
        // defensive: a read before the first def (broken IR) is skipped;
        // no candidate exists, so the pass is a strict no-op (same instance)
        var stmts = new List<Stmt>
        {
            Use("t0"),
            Assign("t0", Call("f0")),
            Assign("t1", Call("f1")),
            Use("t1"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Same(stmts, result.Stmts);
        Assert.Equal(2, result.SlotsBefore);
        Assert.Equal(2, result.SlotsAfter);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(result.Stmts[1]).Target);
        Assert.Equal("t1", Assert.IsType<AssignStmt>(result.Stmts[2]).Target);
    }

    //--------------------------------------------------------------------------
    // Whole-method shape + no-op
    //--------------------------------------------------------------------------

    [Fact]
    public void MixedKinds_AuditShape_SlotsCollapseToKindCount()
    {
        // the audit's stringops shape: many single-use temps, few simultaneously
        // live references -> root slots collapse toward the kind-class count
        // (target ~4-6, docs/audit/stackframe-audit.md §5 P1-1)
        var stmts = new List<Stmt>();
        for (var i = 0; i < 10; i++)
        {
            var name = $"t{i}";
            stmts.Add(Assign(name, Call($"f{i}", i % 2 == 0)));
            stmts.Add(Use(name));
        }

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(10, result.SlotsBefore);
        Assert.True(result.SlotsAfter <= 2, $"expected ~kind-count slots, got {result.SlotsAfter}");
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void NoTemps_ReturnsSameInstance()
    {
        var stmts = new List<Stmt>
        {
            Assign("loc0", Call("f0")),
            Use("loc0"),
            new BranchStmt { TargetLabel = "L_9", Condition = new LocExpr { Name = "loc0" } },
            new LabelStmt { Name = "L_9" },
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Same(stmts, result.Stmts);
        Assert.Equal(0, result.SlotsBefore);
        Assert.Equal(0, result.SlotsAfter);
    }

    //--------------------------------------------------------------------------
    // DCE-1 §3.1 gap + full Expr-kind matrix (b23 audit)
    //--------------------------------------------------------------------------

    [Fact]
    public void DelegateNewTarget_OnlyUse_ExtendsSpan_NoWrongShare()
    {
        // Regression: CollectUses previously did not recurse into
        // DelegateNewExpr.Target (the delegate ctor's bound-instance temp --
        // StackFold.cs:2519-2525). A temp whose ONLY read is that target got
        // spanEnd from its def alone, so a later temp took its slot and the
        // rewritten target read the WRONG value. With the target read
        // collected, t0 stays live through it and t1/t2 cannot take its slot.
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "T" }),
            Assign("t1", Call("f1", true)), Use("t1"),
            Assign("t2", Call("f2", true)), Use("t2"),
            Assign("d", new DelegateNewExpr { MethodToken = 7, Target = new LocExpr { Name = "t0" } }),
        };

        var result = TempSlotPass.Rename(stmts);

        // t0 lives through the target read (pos5) -> its slot is never freed
        // for t1/t2, which share one slot among themselves: 2 slots, not 1.
        Assert.Equal(3, result.SlotsBefore);
        Assert.Equal(2, result.SlotsAfter);
        var del = Assert.IsType<DelegateNewExpr>(Assert.IsType<AssignStmt>(result.Stmts[5]).Value);
        Assert.Equal("t0", Assert.IsType<LocExpr>(del.Target).Name);
        Assert.Equal(7, del.MethodToken);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void CompositeExprMatrix_EveryNestedUseRewritten()
    {
        // Full composite-Expr matrix (all 9 kinds with nested children, plus
        // LocExpr/LocAddrExpr already covered above). Each composite t{i+1}
        // references the previous temp t{i}; the chain renames t2..t8, so the
        // matrix forces the RewriteExpr branch for EVERY kind to rebuild the
        // node and rewrite its nested LocExpr (a missing branch would leave a
        // stale name that DCE's IntegrityViolations catches). Every
        // distinguishing field must survive the rebuild.
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("mk")),
            Assign("t1", new BinaryExpr { Op = BinaryOp.Add, Left = new LocExpr { Name = "t0" }, Right = new LocExpr { Name = "t0" } }),
            Use("t1"),
            Assign("t2", new UnaryExpr { Op = UnaryOp.Not, Operand = new LocExpr { Name = "t1" } }),
            Use("t2"),
            Assign("t3", new CallExpr { Function = "g3", Args = { new LocExpr { Name = "t2" } } }),
            Use("t3"),
            Assign("t4", new VirtualCallExpr { MethodToken = 1, ReturnsRef = false, Args = { new LocExpr { Name = "t3" } } }),
            Use("t4"),
            Assign("t5", new DelegateInvokeExpr { MethodToken = 2, ReturnsRef = false, Args = { new LocExpr { Name = "t4" } } }),
            Use("t5"),
            Assign("t6", new MarshalUtf8Expr { Arg = new LocExpr { Name = "t5" } }),
            Use("t6"),
            Assign("t7", new FieldExpr { Object = new LocExpr { Name = "t6" }, FieldCppName = "fld", OwnerFullName = "T", IsRefField = true }),
            Use("t7"),
            Assign("t8", new FieldAddrExpr { Object = new LocExpr { Name = "t7" }, FieldCppName = "fld", OwnerFullName = "T" }),
            Use("t8"),
            Assign("t9", new DelegateNewExpr { MethodToken = 3, Target = new LocExpr { Name = "t8" } }),
            Use("t9"),
        };

        var result = TempSlotPass.Rename(stmts);

        Assert.Equal(10, result.SlotsBefore);
        Assert.True(result.SlotsAfter < result.SlotsBefore, $"expected reuse, got {result.SlotsAfter} of {result.SlotsBefore}");
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
        Assert.Empty(DcePass.IntegrityViolations(result.Stmts));

        Assert.Equal(BinaryOp.Add, Assert.IsType<BinaryExpr>(Assert.IsType<AssignStmt>(result.Stmts[1]).Value).Op);
        Assert.Equal(UnaryOp.Not, Assert.IsType<UnaryExpr>(Assert.IsType<AssignStmt>(result.Stmts[3]).Value).Op);
        var call = Assert.IsType<CallExpr>(Assert.IsType<AssignStmt>(result.Stmts[5]).Value);
        Assert.Equal("g3", call.Function);
        Assert.Single(call.Args);
        var vc = Assert.IsType<VirtualCallExpr>(Assert.IsType<AssignStmt>(result.Stmts[7]).Value);
        Assert.Equal(1, vc.MethodToken);
        Assert.Single(vc.Args);
        var di = Assert.IsType<DelegateInvokeExpr>(Assert.IsType<AssignStmt>(result.Stmts[9]).Value);
        Assert.Equal(2, di.MethodToken);
        Assert.Single(di.Args);
        Assert.IsType<MarshalUtf8Expr>(Assert.IsType<AssignStmt>(result.Stmts[11]).Value);
        var fe = Assert.IsType<FieldExpr>(Assert.IsType<AssignStmt>(result.Stmts[13]).Value);
        Assert.Equal("fld", fe.FieldCppName);
        Assert.Equal("T", fe.OwnerFullName);
        Assert.True(fe.IsRefField);
        var fa = Assert.IsType<FieldAddrExpr>(Assert.IsType<AssignStmt>(result.Stmts[15]).Value);
        Assert.Equal("fld", fa.FieldCppName);
        Assert.Equal("T", fa.OwnerFullName);
        var dn = Assert.IsType<DelegateNewExpr>(Assert.IsType<AssignStmt>(result.Stmts[17]).Value);
        Assert.Equal(3, dn.MethodToken);
        Assert.IsType<LocExpr>(dn.Target);
    }

    [Fact]
    public void LeafExprKinds_PreservedAsIs()
    {
        // The other half of the matrix: every LEAF Expr kind has no nested
        // temp, so the rewrite must leave the original node instance untouched
        // (default: return e). A temp pair guarantees the rebuild path runs.
        var leaves = new Expr[]
        {
            new IntConstExpr { Value = 1 },
            new Int64ConstExpr { Value = 1L },
            new DoubleConstExpr { Value = 1.0 },
            new StringConstExpr { Value = "s" },
            new NullExpr { TypeName = "T" },
            new ExceptionEntryExpr(),
            new StaticFieldExpr { FieldName = "sf" },
            new StaticFieldAddrExpr { FieldName = "sf" },
            new NewObjExpr { TypeFullName = "T" },
            new ValueTypeNewExpr { TypeFullName = "T", CtorToken = 1 },
            new FuncPtrExpr { MethodToken = 1 },
        };
        var stmts = new List<Stmt>();
        for (var i = 0; i < leaves.Length; i++) stmts.Add(Assign($"d{i}", leaves[i]));
        stmts.Add(Assign("t0", Call("f0"))); stmts.Add(Use("t0"));
        stmts.Add(Assign("t1", Call("f1"))); stmts.Add(Use("t1"));
        stmts.Add(Assign("t2", Call("f2"))); stmts.Add(Use("t2"));

        var result = TempSlotPass.Rename(stmts);

        Assert.True(result.SlotsAfter < result.SlotsBefore, $"expected reuse, got {result.SlotsAfter}");
        for (var i = 0; i < leaves.Length; i++)
            Assert.Same(leaves[i], Assert.IsType<AssignStmt>(result.Stmts[i]).Value);
        AssertReadsSeeOwnWrites(stmts, result.Stmts);
    }

    [Fact]
    public void DceInteraction_RenameAfterDce_IntegrityClean()
    {
        // Production order (CodeGenerator.cs:601 -> :624): DCE first removes
        // dead temps, then TempSlotPass renames the survivors. The rename is
        // pure (def+use renamed together), so DCE's every-read-has-a-def
        // invariant must hold on the FINAL list -- and a dead temp deleted by
        // DCE must not leak a stale name through the rename.
        var stmts = new List<Stmt>
        {
            Assign("t0", Call("f0", true)), Use("t0"),
            Assign("tdead", Call("fdead", true)),
            Assign("t1", Call("f1", true)), Use("t1"),
            Assign("t2", Call("f2", true)), Use("t2"),
        };

        var dce = DcePass.Run(stmts);
        var result = TempSlotPass.Rename(dce.Stmts);

        Assert.Empty(DcePass.IntegrityViolations(result.Stmts));
        Assert.True(result.SlotsBefore > 0);
        Assert.True(result.SlotsAfter < result.SlotsBefore, $"expected reuse, got {result.SlotsAfter}");
        AssertReadsSeeOwnWrites(dce.Stmts, result.Stmts);
    }
}
