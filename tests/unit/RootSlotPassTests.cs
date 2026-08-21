using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// RootSlotPass (GC-Root-1) tests -- the reference-slot liveness analysis.
//
// Contract (see RootSlotPass.cs): a slot must be registered as a frame root
// exactly when it is address-taken (LocAddrExpr, or FieldAddrExpr -- an
// interior pointer into a GC object escapes), OR an argument of an allocating
// runtime icall, OR live AT a reachable allocation point (live-in of the
// statement: the statement reads it, or it flows out to a later use) without
// being redefined there and without being merely a USER-call argument at that
// point (a generated callee roots its own reference parameters for the call).
//
// These tests exercise the PASS logic (liveness + arg-classification) with a
// simplified mayGc predicate; the emitter's real StmtMayGc/ExprMayGc selects
// the same statement set (any call/newobj may allocate).
//------------------------------------------------------------------------------
public class RootSlotPassTests
{
    private static Expr UserCall(string fn, bool returnsRef = true)
        => new CallExpr { Function = fn, ReturnsRef = returnsRef };

    private static Expr UserCallWithArgs(string fn, params Expr[] args)
    {
        var c = new CallExpr { Function = fn, ReturnsRef = false };
        c.Args.AddRange(args);
        return c;
    }

    private static Expr Icall(string fn, params Expr[] args)
    {
        var c = new CallExpr
        {
            Function = fn,
            ReturnsRef = true,
            Resolved = new ResolvedMethod(fn, args.Length, false, true, true,
                new bool[args.Length], false),
        };
        c.Args.AddRange(args);
        return c;
    }

    private static Expr NewArr() => new CallExpr { Function = "newarr_1", ReturnsRef = true };

    private static AssignStmt Assign(string target, Expr value)
        => new AssignStmt { Target = target, Value = value };

    private static ExprStmt Use(string name) => new ExprStmt { Value = new LocExpr { Name = name } };

    private static CallStmt UserCallStmt(string fn, params Expr[] args)
    {
        var c = new CallStmt { Function = fn };
        c.Args.AddRange(args);
        return c;
    }

    // Conservative mayGc for these tests: a statement may allocate when it
    // contains any call/newobj (the emitter's real predicate is stricter; the
    // pass's liveness + arg-classification is what is under test). A
    // FieldAssignStmt mirrors the emitter's StmtMayGc `_ => true` -- unknown
    // statement kinds are treated as GC-capable (conservative).
    private static bool MayGc(Stmt s) => s switch
    {
        AssignStmt a => ExprMayGc(a.Value),
        ExprStmt e => ExprMayGc(e.Value),
        CallStmt => true,
        RetStmt r when r.Value != null => ExprMayGc(r.Value),
        BranchStmt b when b.Condition != null => ExprMayGc(b.Condition),
        FieldAssignStmt => true,
        _ => false,
    };

    private static bool ExprMayGc(Expr e) => e switch
    {
        CallExpr => true,
        BinaryExpr b => ExprMayGc(b.Left) || ExprMayGc(b.Right),
        FieldExpr f => ExprMayGc(f.Object),
        _ => false,
    };

    private static HashSet<string> Roots(params Stmt[] stmts)
    {
        var list = stmts.ToList();
        var reachable = Enumerable.Repeat(true, list.Count).ToArray();
        return RootSlotPass.Compute(list, reachable, MayGc).All;
    }

    //--------------------------------------------------------------------------
    // 1. Pure local temporary (the object-churn shape):
    //      t0 = new Item(); Item..ctor(t0); Get(t0)
    //    t0 is killed by its own allocation and only ever passed to USER calls
    //    (which root their own arguments) -- it must NOT be rooted.
    //--------------------------------------------------------------------------
    [Fact]
    public void PureLocalTemp_UsedOnlyAsUserCallArg_NotRooted()
    {
        var roots = Roots(
            Assign("t0", NewArr()),                       // def; the allocation point KILLS t0
            UserCallStmt("call_1", new LocExpr { Name = "t0" }), // ctor(t0): user-call arg -> excluded
            Assign("sum", new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new LocExpr { Name = "sum" },
                Right = UserCallWithArgs("call_2", new LocExpr { Name = "t0" }),
            })); // Get(t0): user-call arg -> excluded
        Assert.DoesNotContain("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 2. Cross-allocation root: t0 defined, then an unrelated allocation point
    //    while t0 is still live (used afterwards), then the use.
    //    t0 MUST be rooted.
    //--------------------------------------------------------------------------
    [Fact]
    public void LiveAcrossUnrelatedAllocation_Rooted()
    {
        var roots = Roots(
            Assign("t0", UserCall("call_1")), // def; the call kills t0 there
            Assign("t1", NewArr()),           // allocation while t0 still live
            Use("t0"));
        Assert.Contains("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 3. Argument of an allocating RUNTIME icall: rooted even when dead after
    //    the call (the runtime relies on the caller's root for its object
    //    arguments across its own allocations -- conservative).
    //--------------------------------------------------------------------------
    [Fact]
    public void AllocatingIcallArg_RootedEvenIfDeadAfter()
    {
        var roots = Roots(
            Assign("t0", UserCall("call_1")),
            Assign("t1", Icall("il2cpp::runtime::icall_x", new LocExpr { Name = "t0" }))); // icall arg, dead after
        Assert.Contains("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 4. Loop-carried slot: t0 read at the loop top, redefined at the bottom,
    //    with an unrelated allocation in the body. The slot is live across
    //    that allocation (back edge) -- it MUST be rooted. (A simple
    //    [def,use] linear span would miss this shape.)
    //--------------------------------------------------------------------------
    [Fact]
    public void LoopCarriedSlot_Rooted()
    {
        var roots = Roots(
            Assign("t0", UserCall("call_1")),      // pre-loop def
            new LabelStmt { Name = "L_top" },
            Assign("t1", NewArr()),                // allocation while t0 live (loop-carried)
            Use("t0"),                             // use at loop top
            Assign("t0", UserCall("call_2")),      // redefinition (kills the old value)
            new BranchStmt { TargetLabel = "L_top" });
        Assert.Contains("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 5. Address-taken slot (LocAddrExpr): pointer aliasing makes liveness
    //    undecidable -- always rooted.
    //--------------------------------------------------------------------------
    [Fact]
    public void AddressTakenSlot_Rooted()
    {
        var roots = Roots(
            Assign("t0", UserCall("call_1")),
            UserCallStmt("call_2", new LocAddrExpr { Name = "t0" }));
        Assert.Contains("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 6. Killed at the allocation point: `t0 = new T()` allocates AND defines
    //    t0 -- the pre-allocation value is dead, the fresh value is stored
    //    after the allocation. Not rooted.
    //--------------------------------------------------------------------------
    [Fact]
    public void KilledByOwnAllocation_NotRooted()
    {
        var roots = Roots(
            Assign("t0", NewArr()), // def + allocation in one statement
            Use("t0"));
        Assert.DoesNotContain("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 7. Pure-leaf body (no allocation point at all): the root set is empty
    //    regardless of ref slots -- this subsumes the P0-2 whole-body elision.
    //--------------------------------------------------------------------------
    [Fact]
    public void NoAllocationPoint_EmptyRoots()
    {
        var roots = Roots(
            Assign("t0", new StringConstExpr { Value = "hi" }),
            Use("t0"));
        Assert.Empty(roots);
    }

    //--------------------------------------------------------------------------
    // 8. `S = f(S)` self-argument shape: t0's old value is passed to the call
    //    (a user call -- callee roots it) and the result overwrites t0. If t0
    //    is not used after, it must NOT be rooted.
    //--------------------------------------------------------------------------
    [Fact]
    public void RedefinedByCall_PassedAsOwnUserArg_NotRooted()
    {
        var roots = Roots(
            Assign("t0", UserCall("call_1")),
            Assign("t0", UserCallWithArgs("call_2", new LocExpr { Name = "t0" })),
            Use("len")); // t0 dead after the self-call
        Assert.DoesNotContain("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 9. Field-store receiver live across an IN-STATEMENT allocation:
    //      obj = GetObj(); obj->F = newarr_()
    //    The allocation call sits inside the field-store statement; obj is read
    //    AFTER it (the store writes through obj) but is dead after the
    //    statement, so it is NOT live-out. A live-out-only rule drops the root
    //    and a collection inside the allocation would reclaim obj before the
    //    store writes through a dangling pointer. Live-IN (gen covers the
    //    statement's own read) must root obj.
    //--------------------------------------------------------------------------
    [Fact]
    public void FieldStoreReceiver_ReadAfterInStatementAllocation_Rooted()
    {
        var roots = Roots(
            Assign("obj", UserCall("call_1")),       // obj = GetObj()
            new FieldAssignStmt
            {
                Object = new LocExpr { Name = "obj" },   // obj->F = newarr_() -- the
                FieldCppName = "F",                      // allocation happens INSIDE the
                OwnerFullName = "C",                     // statement, before the store.
                IsRefField = true,
                RefTargetFullName = "System.Int32",
                Value = NewArr(),
            }); // obj is dead after the store (no later use)
        Assert.Contains("obj", roots);
    }

    //--------------------------------------------------------------------------
    // 10. Address-taken of a GC OBJECT's FIELD (FieldAddrExpr): &obj->F is an
    //     interior pointer whose validity depends on the object staying alive
    //     across every later allocation. The receiver object slot is
    //     address-taken (whole-method root) -- the same poison rule as a
    //     LocAddrExpr local -- even when the slot is dead after the address is
    //     stored and never live across an allocation point by liveness.
    //--------------------------------------------------------------------------
    [Fact]
    public void FieldAddr_InteriorPointerEscape_RootsReceiverSlot()
    {
        var roots = Roots(
            Assign("obj", UserCall("call_1")),      // obj = GetObj()
            Assign("g", new FieldAddrExpr           // g = &obj->F (interior pointer)
            {
                Object = new LocExpr { Name = "obj" },
                FieldCppName = "F",
                OwnerFullName = "C",
            }),
            Assign("t1", NewArr()),                 // unrelated allocation
            Use("g"));                              // g used after -- obj must be alive
        Assert.Contains("obj", roots);
    }

    //--------------------------------------------------------------------------
    // 11. Loop-carried fixed point (regression guard for the live-IN root
    //     rule): the churn shape `t0 = new; ctor(t0); Get(t0)` must STILL run
    //     with zero roots -- t0 is read by (and passed to) user calls only,
    //     and killed by its own allocation at the loop top. Switching the root
    //     set from live-out to live-in must not resurrect the pure local temp.
    //--------------------------------------------------------------------------
    [Fact]
    public void ChurnLoop_StillNotRooted_UnderLiveInRule()
    {
        var roots = Roots(
            new LabelStmt { Name = "L_top" },
            Assign("t0", NewArr()),                       // kills the old t0
            UserCallStmt("call_ctor", new LocExpr { Name = "t0" }), // user-call arg -> excluded
            Assign("sum", new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = new LocExpr { Name = "sum" },
                Right = UserCallWithArgs("call_get", new LocExpr { Name = "t0" }),
            }), // user-call arg -> excluded
            new BranchStmt { TargetLabel = "L_top" });
        Assert.DoesNotContain("t0", roots);
    }

    //--------------------------------------------------------------------------
    // 12. Pure field store (no allocating sub-expression) -- the ctor-noop
    //     pattern: `arg0 = newobj; arg0->V = arg1` where the store's value is a
    //     VALUE slot (int param) and the only reference slot (arg0) is killed
    //     by its own allocation. The emitter's StmtMayGc conservatively flags
    //     EVERY FieldAssignStmt as GC-capable, but a store built only from leaf
    //     reads cannot trigger a collection -- StmtMayAllocate filters it out,
    //     so arg0 stays unrooted (frameless ctor: no allocation point with a
    //     live reference).
    //--------------------------------------------------------------------------
    [Fact]
    public void PureFieldStore_CtorNoopShape_ReceiverNotRooted()
    {
        var roots = Roots(
            Assign("arg0", NewArr()),        // arg0 = newobj Node -- allocation; KILLS arg0
            new FieldAssignStmt
            {
                Object = new LocExpr { Name = "arg0" },  // arg0->V = arg1: both leaves,
                FieldCppName = "V",                      // no call/newobj/literal -- a PURE
                OwnerFullName = "Node",                  // store that cannot allocate. Filtered
                IsRefField = false,                      // out, so it roots nothing.
                RefTargetFullName = "System.Int32",
                Value = new LocExpr { Name = "arg1" },   // int param -- a VALUE slot
            },
            Use("arg0"));                                // arg0 live-out of the store -- but the
                                                         // store has no allocation to survive.
        Assert.DoesNotContain("arg0", roots);
    }

    //--------------------------------------------------------------------------
    // 13. FIX-FRAME2 concat chain (GC-root completeness for string chains):
    //       t0 = icall_int32_to_string(x)
    //       t1 = icall_string_concat_2("prefix=", t0)
    //       icall_console_write_line_s(t1)
    //    t0 is an ARGUMENT of an allocating runtime icall (the concat) and t1
    //    an ARGUMENT of the allocating write icall -- the concat chain's
    //    INTERMEDIATE string slots must BOTH be rooted. A collection inside
    //    either icall would otherwise reclaim the intermediate string and the
    //    concat/write would read freed memory (the winforms1 forced-GC
    //    investigation family: Main's concat temps t2/t3 must stay rooted).
    //--------------------------------------------------------------------------
    [Fact]
    public void ConcatChainIntermediates_Rooted()
    {
        var roots = Roots(
            Assign("t0", Icall("il2cpp::runtime::icall_int32_to_string",
                new LocExpr { Name = "loc0" })),
            Assign("t1", Icall("il2cpp::runtime::icall_string_concat_2",
                new StringConstExpr { Value = "prefix=" }, new LocExpr { Name = "t0" })),
            new ExprStmt
            {
                Value = Icall("il2cpp::runtime::icall_console_write_line_s",
                    new LocExpr { Name = "t1" }),
            });
        Assert.Contains("t0", roots);
        Assert.Contains("t1", roots);
    }

    //--------------------------------------------------------------------------
    // 14. FIX-FRAME2 -- a user-call argument live across an IN-STATEMENT
    //     allocation must be rooted:
    //       t1 = GetString();  t0 = Foo(AllocatingCall(), t1)
    //    The callee Foo roots its own reference parameters only at CALL ENTRY
    //    -- after every argument is evaluated. Evaluating the earlier argument
    //    AllocatingCall() can run a collection while t1 is still live (its
    //    value is pushed next, and it is used after the call). Excluding t1 as
    //    a "mere user-call argument" would let that collection reclaim the
    //    string before Foo ever enters. A pure leaf-argument user call has no
    //    such window (the existing churn tests cover the exclusion side).
    //--------------------------------------------------------------------------
    [Fact]
    public void UserCallArg_LiveAcrossEarlierArgumentAllocation_Rooted()
    {
        var roots = Roots(
            Assign("t1", UserCall("call_get")), // t1 = GetString() -- a reference
            Assign("t0", UserCallWithArgs("call_outer",
                Icall("il2cpp::runtime::icall_alloc"),       // allocating EARLIER arg
                new LocExpr { Name = "t1" })),               // t1 is a later argument
            Use("t1"));                                      // live after the call
        Assert.Contains("t1", roots);
    }
}
