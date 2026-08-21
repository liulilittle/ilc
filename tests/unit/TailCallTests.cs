using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using Xunit;

// Transpiler-level tail-call optimization (recursion-audit §6a): a self-recursive
// tail call `tN = F(args); return tN;` → argument update + goto entry loop (O(1)
// stack). Four conditions: self-recursion (CppName match), tail position (ret
// directly consumes the call result), no EH regions, and GC-frame safety (static
// method + no ref parameters + pure-value argument expressions).
public class TailCallTests
{
    private const string SelfName = "Cases_Recurse_TailSum_abc123";
    private const string OtherName = "Cases_Recurse_Fib_xyz789";
    private const string EntryLabel = "L_tail_entry";

    private static ResolvedMethod Self(int pc) => new(SelfName, pc, false, true, false, new bool[pc], false);
    private static ResolvedMethod Other(int pc) => new(OtherName, pc, false, true, false, new bool[pc], false);

    // t{target} = callee(args...); return t{target};
    private static (AssignStmt, RetStmt) TailPair(string target, ResolvedMethod callee, params Expr[] args)
    {
        var call = new CallExpr { Function = "call_123", Resolved = callee };
        foreach (var a in args) call.Args.Add(a);
        return (
            new AssignStmt { Target = target, Value = call },
            new RetStmt { Value = new LocExpr { Name = target } });
    }

    private static MethodContext Ctx(string? self = SelfName, bool isStatic = true,
        string[]? paramTypes = null, IReadOnlyList<IlExceptionRegion>? regions = null)
        => new()
        {
            MethodCppName = self,
            IsStatic = isStatic,
            ParamTypeNames = paramTypes,
            ArgCount = paramTypes?.Length ?? 0,
            ExceptionRegions = regions,
        };

    private static bool HasRet(List<Stmt> stmts) => stmts.Any(s => s is RetStmt);
    private static bool HasAssign(List<Stmt> stmts, string target)
        => stmts.Any(s => s is AssignStmt a && a.Target == target);

    [Fact]
    public void TailCall_StaticSelfRecursion_TransformsToLoop()
    {
        // TailSum(n, acc) = n==0 ? acc : TailSum(n-1, acc+n)
        var (assign, ret) = TailPair("t5", Self(2),
            new BinaryExpr { Op = BinaryOp.Sub, Left = new LocExpr { Name = "arg0" }, Right = new IntConstExpr { Value = 1 } },
            new BinaryExpr { Op = BinaryOp.Add, Left = new LocExpr { Name = "arg1" }, Right = new LocExpr { Name = "arg0" } });

        var result = StackFold.ApplyTailCallLoop(new List<Stmt> { assign, ret }, Ctx(paramTypes: new[] { "i", "i" }), null);

        // Entry label inserted at the head
        var label = Assert.IsType<LabelStmt>(result[0]);
        Assert.Equal(EntryLabel, label.Name);
        // The original call assignment and return are removed
        Assert.False(HasAssign(result, "t5"));
        Assert.False(HasRet(result));
        // Argument updates: arg0 = tN (value from a new temp), arg1 = tN+1
        var arg0Assign = result.OfType<AssignStmt>().Single(a => a.Target == "arg0");
        var arg0Src = Assert.IsType<LocExpr>(arg0Assign.Value);
        var arg0Def = result.OfType<AssignStmt>().Single(a => a.Target == arg0Src.Name);
        var sub = Assert.IsType<BinaryExpr>(arg0Def.Value);
        Assert.Equal(BinaryOp.Sub, sub.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(sub.Left).Name);
        // Finally an unconditional branch back to the entry
        var branch = Assert.IsType<BranchStmt>(result[^1]);
        Assert.Equal(EntryLabel, branch.TargetLabel);
        Assert.Null(branch.Condition);
    }

    [Fact]
    public void TailCall_NewArgValuesEvaluatedBeforeArgSlotsAreWritten()
    {
        // Alias safety: arg1's new value references arg0's old value — all new
        // argument values must be evaluated before the slots are written.
        var (assign, ret) = TailPair("t5", Self(2),
            new BinaryExpr { Op = BinaryOp.Sub, Left = new LocExpr { Name = "arg0" }, Right = new IntConstExpr { Value = 1 } },
            new BinaryExpr { Op = BinaryOp.Add, Left = new LocExpr { Name = "arg1" }, Right = new LocExpr { Name = "arg0" } });

        var result = StackFold.ApplyTailCallLoop(new List<Stmt> { assign, ret }, Ctx(paramTypes: new[] { "i", "i" }), null);

        var arg0Assign = result.OfType<AssignStmt>().Single(a => a.Target == "arg0");
        var arg1Assign = result.OfType<AssignStmt>().Single(a => a.Target == "arg1");
        var arg1Src = Assert.IsType<LocExpr>(arg1Assign.Value);
        // The arg1 slot write references the new temp (already evaluated before the
        // arg0 slot write), not the arg0 slot itself
        Assert.NotEqual("arg0", arg1Src.Name);
        var arg1Def = result.OfType<AssignStmt>().Single(a => a.Target == arg1Src.Name);
        var add = Assert.IsType<BinaryExpr>(arg1Def.Value);
        Assert.Equal(BinaryOp.Add, add.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(add.Right).Name);
    }

    [Fact]
    public void TailCall_OtherCallee_NotTransformed()
    {
        var (assign, ret) = TailPair("t5", Other(2), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(new List<Stmt> { assign, ret }, Ctx(paramTypes: new[] { "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
        Assert.DoesNotContain(result, s => s is LabelStmt l && l.Name == EntryLabel);
    }

    [Fact]
    public void TailCall_NonTailPosition_NotTransformed()
    {
        // t5 = call; return arg1; — ret does not consume the call result
        var (assign, _) = TailPair("t5", Self(2), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { assign, new RetStmt { Value = new LocExpr { Name = "arg1" } } },
            Ctx(paramTypes: new[] { "i", "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        var ret = Assert.IsType<RetStmt>(result[^1]);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    [Fact]
    public void TailCall_ResultConsumedByExpression_NotTransformed()
    {
        // t5 = call; return t5 + 1; — the call result feeds a later operation
        // (fib shape)
        var (assign, _) = TailPair("t5", Self(1), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt>
            {
                assign,
                new RetStmt
                {
                    Value = new BinaryExpr
                    {
                        Op = BinaryOp.Add,
                        Left = new LocExpr { Name = "t5" },
                        Right = new IntConstExpr { Value = 1 },
                    },
                },
            },
            Ctx(paramTypes: new[] { "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
        Assert.DoesNotContain(result, s => s is LabelStmt l && l.Name == EntryLabel);
    }

    [Fact]
    public void TailCall_MethodWithExceptionRegions_NotTransformed()
    {
        var (assign, ret) = TailPair("t5", Self(1), new LocExpr { Name = "arg0" });
        var regions = new List<IlExceptionRegion>
        {
            new() { TryOffset = 0, TryLength = 8, HandlerOffset = 8, HandlerLength = 4, Kind = ExceptionRegionKind.Catch },
        };
        var result = StackFold.ApplyTailCallLoop(new List<Stmt> { assign, ret }, Ctx(paramTypes: new[] { "i" }, regions: regions), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
        Assert.DoesNotContain(result, s => s is LabelStmt l && l.Name == EntryLabel);
    }

    [Fact]
    public void TailCall_InstanceMethod_NotTransformed()
    {
        // Instance method arg0 = this (ref slot): the transform would reassign
        // this, a GC-root semantics risk
        var (assign, ret) = TailPair("t5", Self(2), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { assign, ret },
            Ctx(isStatic: false, paramTypes: new[] { "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
    }

    [Fact]
    public void TailCall_RefParameter_NotTransformed()
    {
        // Parameter contains a managed reference ("r:" prefix): the arg slot is a
        // GC root; the temp relay window has no root protection
        var (assign, ret) = TailPair("t5", Self(2), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { assign, ret },
            Ctx(paramTypes: new[] { "r:System.String", "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
    }

    [Fact]
    public void TailCall_TrackedRefArg_NotTransformed()
    {
        // Arg is a tracked ref temp (an object reference recorded in objTypes)
        var (assign, ret) = TailPair("t5", Self(1), new LocExpr { Name = "t9" });
        var objTypes = new Dictionary<string, string> { ["t9"] = "System.String" };
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { assign, ret },
            Ctx(paramTypes: new[] { "i" }), objTypes);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
    }

    [Fact]
    public void TailCall_ArgWithCallLikeNode_NotTransformed()
    {
        // Arg expression contains a call (an allocation point): the temp relay
        // window may trigger GC with no root protection
        var (assign, ret) = TailPair("t5", Self(1), new CallExpr { Function = "call_9", Resolved = Other(0) });
        var result = StackFold.ApplyTailCallLoop(new List<Stmt> { assign, ret }, Ctx(paramTypes: new[] { "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
    }

    [Fact]
    public void TailCall_TwoTailSites_ShareOneEntryLabel()
    {
        var (a1, r1) = TailPair("t5", Self(1), new LocExpr { Name = "arg0" });
        var (a2, r2) = TailPair("t9", Self(1), new LocExpr { Name = "arg1" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { a1, r1, new LabelStmt { Name = "L_9" }, a2, r2 },
            Ctx(paramTypes: new[] { "i", "i" }), null);

        Assert.False(HasAssign(result, "t5"));
        Assert.False(HasAssign(result, "t9"));
        Assert.False(HasRet(result));
        Assert.Single(result, s => s is LabelStmt l && l.Name == EntryLabel);
        // Both tail-call paths jump back to the entry
        Assert.Equal(2, result.Count(s => s is BranchStmt b && b.TargetLabel == EntryLabel));
    }

    [Fact]
    public void TailCall_StatementsBeforeTailSite_EntryLabelAtHead()
    {
        // Real TailSum shape: the n==0 guard (compare + conditional branch +
        // non-tail return) precedes the tail call. The entry label must be inserted
        // at the first statement of the function body — jumping back re-executes the
        // guard; otherwise the n==0 exit check is skipped → infinite loop (semantic
        // corruption). The implementation comment says "insert before the first
        // statement of the function body", but the code inserted at the first tail
        // site — this test pins the head position.
        var (assign, ret) = TailPair("t5", Self(2),
            new BinaryExpr { Op = BinaryOp.Sub, Left = new LocExpr { Name = "arg0" }, Right = new IntConstExpr { Value = 1 } },
            new BinaryExpr { Op = BinaryOp.Add, Left = new LocExpr { Name = "arg1" }, Right = new LocExpr { Name = "arg0" } });

        var result = StackFold.ApplyTailCallLoop(new List<Stmt>
        {
            new AssignStmt
            {
                Target = "t0",
                Value = new BinaryExpr
                {
                    Op = BinaryOp.Ceq,
                    Left = new LocExpr { Name = "arg0" },
                    Right = new IntConstExpr { Value = 0 },
                },
            },
            new BranchStmt { TargetLabel = "L_exit", Condition = new LocExpr { Name = "t0" } },
            new RetStmt { Value = new LocExpr { Name = "arg1" } },
            new LabelStmt { Name = "L_exit" },
            assign,
            ret,
        }, Ctx(paramTypes: new[] { "i", "i" }), null);

        var label = Assert.IsType<LabelStmt>(result[0]);
        Assert.Equal(EntryLabel, label.Name);
        // The tail-call t5 assignment is removed; the guard's `ret arg1` (the n==0
        // exit) must be preserved
        Assert.False(HasAssign(result, "t5"));
        var guardRet = Assert.IsType<RetStmt>(result.Single(s => s is RetStmt));
        Assert.Equal("arg1", Assert.IsType<LocExpr>(guardRet.Value).Name);
        Assert.Equal(1, result.Count(s => s is BranchStmt b && b.TargetLabel == EntryLabel));
    }

    [Fact]
    public void TailCall_RetCallShape_TransformsToLoop()
    {
        // Real pipeline shape: OptimizeTemps rule 3 forward-replaces
        // `t5 = call; ret t5` with `ret call` (single assignment + single read).
        // The transform must recognize this shape, otherwise the real TailSum is
        // never looped (probe verified: the generated code remains a recursive call).
        var call = new CallExpr { Function = "call_123", Resolved = Self(2) };
        call.Args.Add(new BinaryExpr { Op = BinaryOp.Sub, Left = new LocExpr { Name = "arg0" }, Right = new IntConstExpr { Value = 1 } });
        call.Args.Add(new BinaryExpr { Op = BinaryOp.Add, Left = new LocExpr { Name = "arg1" }, Right = new LocExpr { Name = "arg0" } });

        var result = StackFold.ApplyTailCallLoop(new List<Stmt>
        {
            new BranchStmt
            {
                TargetLabel = "L_15",
                Condition = new BinaryExpr { Op = BinaryOp.Ceq, Left = new LocExpr { Name = "arg0" }, Right = new IntConstExpr { Value = 0 } },
            },
            new RetStmt { Value = call },
            new LabelStmt { Name = "L_15" },
            new RetStmt { Value = new LocExpr { Name = "arg1" } },
        }, Ctx(paramTypes: new[] { "i", "i" }), null);

        var label = Assert.IsType<LabelStmt>(result[0]);
        Assert.Equal(EntryLabel, label.Name);
        // The tail-call ret disappears; the guard's `ret arg1` (the n==0 exit) is
        // preserved
        var guardRet = Assert.IsType<RetStmt>(result.Single(s => s is RetStmt));
        Assert.Equal("arg1", Assert.IsType<LocExpr>(guardRet.Value).Name);
        // Argument updates arg0/arg1 reference new temps, with exactly one
        // unconditional branch back to the entry
        Assert.Contains(result, s => s is AssignStmt a && a.Target == "arg0");
        Assert.Contains(result, s => s is AssignStmt a && a.Target == "arg1");
        var toEntry = result.OfType<BranchStmt>().Single(b => b.TargetLabel == EntryLabel);
        Assert.Null(toEntry.Condition);
    }

    [Fact]
    public void TailCall_NoMethodCppName_NotTransformed()
    {
        // MethodCppName missing (md=null pure-IR folding path): self-recursion
        // cannot be confirmed, so conservatively no transform
        var (assign, ret) = TailPair("t5", Self(1), new LocExpr { Name = "arg0" });
        var result = StackFold.ApplyTailCallLoop(
            new List<Stmt> { assign, ret },
            Ctx(self: null, paramTypes: new[] { "i" }), null);

        Assert.True(HasAssign(result, "t5"));
        Assert.True(HasRet(result));
    }
}
