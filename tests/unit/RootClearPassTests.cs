using Il2Cpp.Transpiler.Ir;
using Xunit;

//------------------------------------------------------------------------------
// RootClearPass (ipaudit4) tests -- liveness-based GC root clearing.
//
// Contract (see RootClearPass.cs): a reference-defined local whose last READ
// is in a linear (no exception regions, no backward branch) method gets a
// `slot = null;` inserted right after that read, so the frame-root pointer
// table stops pinning the object. The pass must be a strict no-op on any
// gated shape (loops, ret-as-last-read, non-ref definitions).
//------------------------------------------------------------------------------
public class RootClearPassTests
{
    private static AssignStmt Assign(string target, Expr value)
        => new AssignStmt { Target = target, Value = value };

    private static ExprStmt Use(string name) => new ExprStmt { Value = new LocExpr { Name = name } };

    private static Stmt Ret() => new RetStmt();

    [Fact]
    public void Linear_NewObjTemp_ClearedAfterLastRead()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Use("t0"), // stands in for the WeakReference ctor call (a read)
            new ExprStmt { Value = new CallExpr { Function = "icollect", ReturnsRef = false } },
            Ret(),
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(1, res.ClearedCount);
        Assert.Equal(5, res.Stmts.Count);
        var cleared = Assert.IsType<AssignStmt>(res.Stmts[2]);
        Assert.Equal("t0", cleared.Target);
        Assert.IsType<NullExpr>(cleared.Value);
        Assert.Same(stmts[2], res.Stmts[3]);
        Assert.Same(stmts[3], res.Stmts[4]);
    }

    [Fact]
    public void LoopMethod_Unchanged()
    {
        var stmts = new List<Stmt>
        {
            new LabelStmt { Name = "L0" },
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Use("t0"),
            new BranchStmt { TargetLabel = "L0" },
            Ret(),
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res.ClearedCount);
        Assert.Equal(stmts.Count, res.Stmts.Count);
        for (var i = 0; i < stmts.Count; i++) Assert.Same(stmts[i], res.Stmts[i]);
    }

    [Fact]
    public void RetLastRead_NotCleared()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            new RetStmt { Value = new LocExpr { Name = "t0" } },
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res.ClearedCount);
        Assert.Equal(2, res.Stmts.Count);
    }

    [Fact]
    public void StaticFieldRefPropagation()
    {
        var stmts = new List<Stmt>
        {
            Assign("t1", new StaticFieldExpr { FieldName = "F_Ref" }),
            Assign("t0", new LocExpr { Name = "t1" }),
            Use("t1"),
            Use("t0"),
            Ret(),
        };
        var res = RootClearPass.Run(stmts, name => name == "F_Ref");
        Assert.Equal(2, res.ClearedCount);
        // Inserts land after the last reads: t1 after Use(t1) (index 2), then
        // t0 after Use(t0) (shifted by the first insert).
        Assert.Equal(7, res.Stmts.Count);
        var c1 = Assert.IsType<AssignStmt>(res.Stmts[3]);
        Assert.Equal("t1", c1.Target);
        var c2 = Assert.IsType<AssignStmt>(res.Stmts[5]);
        Assert.Equal("t0", c2.Target);

        var res2 = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res2.ClearedCount);
        Assert.Equal(stmts.Count, res2.Stmts.Count);
        for (var i = 0; i < stmts.Count; i++) Assert.Same(stmts[i], res2.Stmts[i]);
    }

    // ipaudit4 regression: TempSlotPass merges many stack temps onto one slot
    // (t0); the GLOBAL last read lands after the GC.Collect, so the value held
    // by t0 when the WeakReference ctor ran stayed pinned. Epoch liveness must
    // clear the DEAD VALUE mid-method (right after its own last read), even
    // though the slot is reassigned later.
    [Fact]
    public void SlotReuse_DeadValueClearedBeforeRedefinition()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }), // 0
            Use("t0"),                                                       // 1 -- value dies here
            new ExprStmt { Value = new CallExpr { Function = "icollect", ReturnsRef = false } }, // 2 -- GC.Collect
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }), // 3 -- new value
            Use("t0"),                                                       // 4
            Ret(),                                                           // 5
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(2, res.ClearedCount);
        Assert.Equal(8, res.Stmts.Count);
        Assert.Equal("t0", Assert.IsType<AssignStmt>(res.Stmts[2]).Target); // after first read (before collect)
        Assert.Equal("t0", Assert.IsType<AssignStmt>(res.Stmts[6]).Target); // after second read
        Assert.IsType<RetStmt>(res.Stmts[7]);
    }

    // Adjacent redefinition (last read immediately followed by the next
    // definition): the store itself replaces the root -- no clear needed.
    [Fact]
    public void SlotReuse_AdjacentRedefinition_NoClear()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Use("t0"),
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Ret(),
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res.ClearedCount);
        Assert.Equal(4, res.Stmts.Count);
    }

    // The next definition's RHS reads the OLD value (s = f(s)): the slot cannot
    // be nulled without corrupting that read -- the epoch stays conservative
    // and is reported through BoundaryCount (LOUD boundary).
    [Fact]
    public void SelfUpdateRead_KeptAsBoundary()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Assign("t0", new CallExpr { Function = "f", ReturnsRef = true, Args = { new LocExpr { Name = "t0" } } }),
            Ret(),
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res.ClearedCount);
        Assert.Equal(1, res.BoundaryCount);
        Assert.Equal(3, res.Stmts.Count);
    }

    // Plain overwrite: the previous value's last read is immediately followed
    // by the redefinition -- no clear is needed (the store replaces the root).
    [Fact]
    public void ValueOverwritten_NoClearInserted()
    {
        var stmts = new List<Stmt>
        {
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Use("t0"),
            Assign("t0", new NewObjExpr { TypeFullName = "System.Object" }),
            Ret(),
        };
        var res = RootClearPass.Run(stmts, _ => false);
        Assert.Equal(0, res.ClearedCount);
        Assert.Equal(4, res.Stmts.Count);
    }
}
