using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// Allocation-promotion pass tests -- AllocPromoPass.Run.
//
// Target shape (the object-bench gap): a pure-local object
//   t = new Item();  ctor(t);  ... field reads/writes on t (and its copy
// aliases) ...  is replaced by one int32 scalar temp per VALUE field; the
// allocation statement, the alias copies and the no-op ctor_nop call all
// disappear. The runtime has no stack-allocation form (il2cpp_object_new
// delegates to the GC heap), so this IR-level scalar replacement is the
// only escape-analysis win available.
//
// Escape-analysis gates (conservative, per object): the object must be
// allocated exactly once, never address-taken, never an argument of any
// call except il2cpp_object_ctor_nop, never stored into another object's
// field, never returned, never used in a branch/comparison; every field
// access must be a plain `alias->F` shape on a VALUE field.
//------------------------------------------------------------------------------
public class AllocPromoPassTests
{
    private static Expr Loc(string name) => new LocExpr { Name = name };
    private static Expr NewObj(string type) => new NewObjExpr { TypeFullName = type };
    private static Expr I(long v) => new IntConstExpr { Value = v };

    private static FieldAssignStmt SetField(string obj, string field, Expr value) => new()
    {
        Object = Loc(obj),
        FieldCppName = field,
        OwnerFullName = "Bench.Item",
        IsRefField = false,
        RefTargetFullName = "",
        Value = value,
    };

    private static Expr GetField(string obj, string field) => new FieldExpr
    {
        Object = Loc(obj),
        FieldCppName = field,
        OwnerFullName = "Bench.Item",
        IsRefField = false,
    };

    private static CallStmt CtorNop(string obj) => new()
    {
        Function = "il2cpp::runtime::il2cpp_object_ctor_nop",
        Resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", 1, false, false, false, Array.Empty<bool>(), true),
        Args = { Loc(obj) },
    };

    //--------------------------------------------------------------------------
    // Promotion: basic shapes
    //--------------------------------------------------------------------------

    [Fact]
    public void PureLocalObject_FieldWriteThenRead_Promoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            SetField("t0", "v", I(5)),
            new AssignStmt { Target = "x", Value = GetField("t0", "v") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(1, result.Promoted);
        // no allocation remains
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        // allocation replaced by a zero-init scalar store
        var init = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("t0_prm_v", init.Target);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(init.Value).Value);
        // field write -> scalar write
        var write = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("t0_prm_v", write.Target);
        // field read -> scalar read
        var read = Assert.IsType<AssignStmt>(result.Stmts[2]);
        Assert.Equal("t0_prm_v", Assert.IsType<LocExpr>(read.Value).Name);
    }

    [Fact]
    public void CopyAliasesAndCtorNop_Dropped()
    {
        // the post-inline object-bench shape: alloc + ctor_nop(copy) + field
        // write through the copy + copies + field read through a copy chain
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "inl0", Value = Loc("t0") },
            CtorNop("inl0"),
            SetField("inl0", "v", Loc("inl1")),
            new AssignStmt { Target = "loc2", Value = Loc("t0") },
            new AssignStmt { Target = "inl2", Value = Loc("loc2") },
            new AssignStmt { Target = "inl3", Value = GetField("inl2", "v") },
            new AssignStmt { Target = "loc0", Value = new BinaryExpr { Op = BinaryOp.Add, Left = Loc("loc0"), Right = new BinaryExpr { Op = BinaryOp.And, Left = Loc("inl3"), Right = I(255) } } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(1, result.Promoted);
        // the object-bench loop collapses to: init, scalar write, scalar read, consumer
        Assert.Equal(4, result.Stmts.Count);
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        Assert.DoesNotContain(result.Stmts, s => s is CallStmt c && c.Function.Contains("ctor_nop"));
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is LocExpr l && l.Name is "t0" or "loc2" or "inl0" or "inl2");
        // scalar init
        var init = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("t0_prm_v", init.Target);
        // scalar field write (the inlined ctor body)
        var write = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("t0_prm_v", write.Target);
        Assert.Equal("inl1", Assert.IsType<LocExpr>(write.Value).Name);
        // scalar field read (the inlined getter body)
        var read = Assert.IsType<AssignStmt>(result.Stmts[2]);
        Assert.Equal("t0_prm_v", Assert.IsType<LocExpr>(read.Value).Name);
        // the consumer keeps its shape
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[3]);
        Assert.Equal("loc0", consumer.Target);
    }

    [Fact]
    public void ZeroFieldObject_DroppedEntirely()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Empty") },
            new AssignStmt { Target = "loc0", Value = I(1) },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(1, result.Promoted);
        Assert.Single(result.Stmts); // only the unrelated store remains
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
    }

    [Fact]
    public void MultiFieldObject_AllFieldsPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Pair") },
            SetField("t0", "a", I(1)),
            SetField("t0", "b", I(2)),
            new AssignStmt { Target = "s", Value = new BinaryExpr { Op = BinaryOp.Add, Left = GetField("t0", "a"), Right = GetField("t0", "b") } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(1, result.Promoted);
        // the two zero-valued init stores replace the allocation
        var inits = result.Stmts.OfType<AssignStmt>()
            .Where(a => a.Value is IntConstExpr ic && ic.Value == 0 && (a.Target == "t0_prm_a" || a.Target == "t0_prm_b"))
            .ToList();
        Assert.Equal(2, inits.Count);
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        // the sum reads the promoted scalars
        var sum = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        var add = Assert.IsType<BinaryExpr>(sum.Value);
        Assert.Equal("t0_prm_a", Assert.IsType<LocExpr>(add.Left).Name);
        Assert.Equal("t0_prm_b", Assert.IsType<LocExpr>(add.Right).Name);
    }

    [Fact]
    public void TwoIndependentObjects_BothPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            SetField("t0", "v", I(1)),
            new AssignStmt { Target = "t1", Value = NewObj("Bench.Item") },
            SetField("t1", "v", I(2)),
            new AssignStmt { Target = "s", Value = new BinaryExpr { Op = BinaryOp.Add, Left = GetField("t0", "v"), Right = GetField("t1", "v") } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(2, result.Promoted);
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        var sum = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        var add = Assert.IsType<BinaryExpr>(sum.Value);
        Assert.Equal("t0_prm_v", Assert.IsType<LocExpr>(add.Left).Name);
        Assert.Equal("t1_prm_v", Assert.IsType<LocExpr>(add.Right).Name);
    }

    //--------------------------------------------------------------------------
    // Escape-analysis gates
    //--------------------------------------------------------------------------

    [Fact]
    public void ObjectPassedToOtherCall_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "x", Value = new CallExpr { Function = "call_99", Args = { Loc("t0") } } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ObjectReturned_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new RetStmt { Value = Loc("t0") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ObjectAddressTaken_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "x", Value = new LocAddrExpr { Name = "t0" } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void RefFieldObject_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new FieldAssignStmt
            {
                Object = Loc("t0"),
                FieldCppName = "next",
                OwnerFullName = "Bench.Item",
                IsRefField = true,
                RefTargetFullName = "Bench.Item",
                Value = Loc("other"),
            },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ObjectStoredIntoAnotherField_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new FieldAssignStmt
            {
                Object = Loc("owner"),
                FieldCppName = "item",
                OwnerFullName = "Bench.Owner",
                IsRefField = true,
                RefTargetFullName = "Bench.Item",
                Value = Loc("t0"),
            },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ObjectStoredToStaticField_NotPromoted()
    {
        // stsfld of the object (AssignStmt Target "Owner.Field", Value = the
        // object) stores it into shared static storage -- it escapes. The
        // store must NOT be mistaken for an alias copy and dropped.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "Bench.Item.lastItem", Value = Loc("t0") },
            new AssignStmt { Target = "x", Value = GetField("t0", "v") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void StaticFieldStoreOfFieldValue_StillPromoted()
    {
        // A static store of a FIELD VALUE (Bench.staticCache = t0->v) is a
        // scalar copy -- it must not be rejected by the static-escape guard
        // and must read the promoted scalar.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            SetField("t0", "v", I(5)),
            new AssignStmt { Target = "Bench.Item.cached", Value = GetField("t0", "v") },
            new AssignStmt { Target = "x", Value = GetField("t0", "v") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(1, result.Promoted);
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        var cached = Assert.Single(result.Stmts, s => s is AssignStmt a && a.Target == "Bench.Item.cached");
        Assert.Equal("t0_prm_v", Assert.IsType<LocExpr>(Assert.IsType<AssignStmt>(cached).Value).Name);
    }

    [Fact]
    public void ObjectStoredIntoArray_NotPromoted()
    {
        // stelem.ref folds to a CallStmt "stelem_<op>" with the value as an
        // arg (StackFold) -- the object crossing into an array escapes.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new CallStmt { Function = "stelem_Stelem_ref", Args = { Loc("arr"), I(0), Loc("t0") } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void FieldWriteBeforeAllocation_NotPromoted()
    {
        // A field write on the object BEFORE its allocation would be silently
        // clobbered by the zero-init stores that replace the allocation.
        var stmts = new List<Stmt>
        {
            SetField("t0", "v", I(5)),
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "x", Value = GetField("t0", "v") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void FieldReadBeforeAllocation_NotPromoted()
    {
        // A field read on the object before its allocation would read an
        // uninitialized scalar (the .NET semantics is an NRE on null).
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "x", Value = GetField("t0", "v") },
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ReassignedObject_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            SetField("t0", "v", I(1)),
            new AssignStmt { Target = "t0", Value = Loc("other") },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void ObjectInBranchCondition_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new BranchStmt { TargetLabel = "L_1", Condition = new BinaryExpr { Op = BinaryOp.Ceq, Left = Loc("t0"), Right = new NullExpr() } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void FieldAddressTaken_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "x", Value = new FieldAddrExpr { Object = Loc("t0"), FieldCppName = "v", OwnerFullName = "Bench.Item" } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void SelfCycleFieldWrite_NotPromoted()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new FieldAssignStmt
            {
                Object = Loc("t0"),
                FieldCppName = "self",
                OwnerFullName = "Bench.Item",
                IsRefField = true,
                RefTargetFullName = "Bench.Item",
                Value = Loc("t0"),
            },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void AliasUsedInNonCopyExpression_NotPromoted()
    {
        // `int x = t != null ? 1 : 0;` uses the object in a comparison
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new AssignStmt { Target = "c", Value = Loc("t0") },
            new AssignStmt { Target = "x", Value = new BinaryExpr { Op = BinaryOp.Ceq, Left = Loc("c"), Right = new NullExpr() } },
        };

        var result = AllocPromoPass.Run(stmts);

        Assert.Equal(0, result.Promoted);
        Assert.Same(stmts, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // InlinePass integration (the object-bench pipeline: ctor/getter inline,
    // then promote). The call chain must be expanded BEFORE promotion -- the
    // inlined ctor/getter bodies expose the field write/read on the local
    // object that the scalar model replaces.
    //--------------------------------------------------------------------------

    private static InlineCandidate CtorCandidate() => new()
    {
        Function = "call_100663310",
        ParamCount = 2,
        ReturnsVoid = true,
        ReturnsRef = false,
        HasExceptionRegions = false,
        Body = new List<Stmt>
        {
            // Item(int x) { v = x; } -- base-ctor no-op (resolved to
            // il2cpp_object_ctor_nop) + field store + void ret
            new CallStmt
            {
                Function = "call_167772174",
                Resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", 1, false, false, false, Array.Empty<bool>(), true),
                Args = { Loc("arg0") },
            },
            SetField("arg0", "v", Loc("arg1")),
            new RetStmt { Value = null },
        },
    };

    private static InlineCandidate GetterCandidate() => new()
    {
        Function = "call_100663311",
        ParamCount = 1,
        ReturnsVoid = false,
        ReturnsRef = false,
        HasExceptionRegions = false,
        Body = new List<Stmt>
        {
            // int Get() { return v; }
            new RetStmt { Value = GetField("arg0", "v") },
        },
    };

    private static Func<string, InlineCandidate?> BenchLookup(InlineCandidate ctor, InlineCandidate getter)
        => fn => fn == ctor.Function ? ctor : fn == getter.Function ? getter : null;

    [Fact]
    public void ObjectBenchShape_InlineThenPromote_LoopIsScalar()
    {
        // The exact StackFold.Fold output of Bench.Bench.ObjectBench (see the
        // opt-b23 IR dump): loop body = alloc + ctor call + alias + getter read.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(0) },
            new AssignStmt { Target = "loc1", Value = I(0) },
            new BranchStmt { TargetLabel = "L_32" },
            new LabelStmt { Name = "L_6" },
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new CallStmt { Function = "call_100663310", Args = { Loc("t0"), Loc("loc1") } },
            new AssignStmt { Target = "loc2", Value = Loc("t0") },
            new AssignStmt { Target = "loc0", Value = new BinaryExpr { Op = BinaryOp.Add, Left = Loc("loc0"), Right = new BinaryExpr { Op = BinaryOp.And, Left = new VirtualCallExpr { MethodToken = 100663311, ReturnsRef = false, Args = { Loc("loc2") } }, Right = I(255) } } },
            new AssignStmt { Target = "loc1", Value = new BinaryExpr { Op = BinaryOp.Add, Left = Loc("loc1"), Right = I(1) } },
            new LabelStmt { Name = "L_32" },
            new BranchStmt { TargetLabel = "L_6", Condition = new BinaryExpr { Op = BinaryOp.Clt, Left = Loc("loc1"), Right = I(5000000) } },
            new RetStmt { Value = Loc("loc0") },
        };

        var inlined = InlinePass.Inline(stmts, BenchLookup(CtorCandidate(), GetterCandidate()));
        var promoted = AllocPromoPass.Run(inlined.Stmts);

        Assert.Equal(1, promoted.Promoted);
        // no allocation, no ctor/getter calls, no object locals remain
        Assert.DoesNotContain(promoted.Stmts, s => s is AssignStmt a && a.Value is NewObjExpr);
        Assert.DoesNotContain(promoted.Stmts, s => s is CallStmt c && c.Function is "call_100663310" or "call_100663311");
        // the promoted scalar is written by the inlined ctor and read by the inlined getter
        Assert.Contains(promoted.Stmts, s => s is AssignStmt a && a.Target == "t0_prm_v");
        Assert.Contains(promoted.Stmts, s => s is AssignStmt a && a.Value is LocExpr l && l.Name == "t0_prm_v");
    }

    [Fact]
    public void Inline_StaticWritingCallee_NotInlined()
    {
        // Purity gate: a body that writes a static field is not a "small pure
        // function" -- inlining it replicates shared-state mutation at every
        // call site and defeats the C++ optimizer (bench hash 6ms -> 8ms).
        var callee = new InlineCandidate
        {
            Function = "call_21",
            ParamCount = 0,
            ReturnsVoid = false,
            ReturnsRef = false,
            HasExceptionRegions = false,
            Body = new List<Stmt>
            {
                new AssignStmt { Target = "Bench.Bench.rngState", Value = I(1) },
                new RetStmt { Value = I(1) },
            },
        };
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = new CallExpr { Function = "call_21" } },
        };

        var result = InlinePass.Inline(caller, fn => fn == "call_21" ? callee : null);

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void Inline_VoidBodyWithoutRet_IsInlineable()
    {
        // Relaxation: a void callee whose body folds to zero RetStmts (the
        // trailing IL `ret` is swallowed by the fold -- e.g. a ctor that is
        // only field stores) is still inlineable; fall-off-end is the void
        // return.
        var ctor = new InlineCandidate
        {
            Function = "call_30",
            ParamCount = 2,
            ReturnsVoid = true,
            ReturnsRef = false,
            HasExceptionRegions = false,
            Body = new List<Stmt>
            {
                SetField("arg0", "v", Loc("arg1")),
            },
        };
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = NewObj("Bench.Item") },
            new CallStmt { Function = "call_30", Args = { Loc("t0"), Loc("x") } },
        };

        var result = InlinePass.Inline(caller, fn => fn == "call_30" ? ctor : null);

        Assert.Equal(1, result.Inlined);
        // the inlined ctor body becomes a field write on the arg temp
        Assert.Contains(result.Stmts, s => s is FieldAssignStmt f && f.FieldCppName == "v");
        Assert.DoesNotContain(result.Stmts, s => s is CallStmt c && c.Function == "call_30");
    }
}
