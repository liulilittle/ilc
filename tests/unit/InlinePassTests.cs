using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// Simple-method inlining pass tests -- InlinePass.Inline.
//
// Target shape (the net48 escape-analysis/inlining gap on the bench -- the
// JIT inlines small getters/pure helpers; the generated C++ calls the
// function): a user-method call (CallExpr Function "call_<token>", Resolved
// == null, or a void CallStmt) is expanded at its call site when the callee
// is a small pure function:
//   * body <= 5 statements (conservative threshold)
//   * no exception regions
//   * not recursive (the body must not call its own Function)
//   * int32/void results only (ReturnsRef == false -- the result temp stays
//     int32_t; ref results keep the call)
//   * no byref arguments (LocAddrExpr -- the arg-temp indirection would
//     break byref write-back semantics)
//   * non-void bodies must end every path with a value (every RetStmt has a
//     non-null Value)
// The expansion preserves evaluation order (arg temps assigned left-to-right
// BEFORE the body), renames callee locals/labels to fresh caller-visible
// names, and converts every callee ret into a write of one shared result
// temp followed by a jump to the insertion point (end label).
//------------------------------------------------------------------------------
public class InlinePassTests
{
    private static Expr Loc(string name) => new LocExpr { Name = name };

    private static Expr I(long v) => new IntConstExpr { Value = v };

    private static Expr Bin(BinaryOp op, Expr l, Expr r) => new BinaryExpr { Op = op, Left = l, Right = r };

    private static InlineCandidate Candidate(string function, int paramCount, List<Stmt> body,
        bool returnsVoid = false, bool returnsRef = false, bool eh = false, bool isVirtual = false,
        string[]? localKinds = null) => new()
    {
        Function = function,
        ParamCount = paramCount,
        ReturnsVoid = returnsVoid,
        ReturnsRef = returnsRef,
        HasExceptionRegions = eh,
        IsVirtual = isVirtual,
        Body = body,
        LocalKinds = localKinds ?? Array.Empty<string>(),
    };

    private static Func<string, InlineCandidate?> Lookup(params InlineCandidate[] c)
        => fn =>
        {
            foreach (var candidate in c)
                if (candidate.Function == fn) return candidate;
            return null;
        };

    private static bool ExprContainsCall(Expr e) => e switch
    {
        CallExpr => true,
        BinaryExpr b => ExprContainsCall(b.Left) || ExprContainsCall(b.Right),
        UnaryExpr u => ExprContainsCall(u.Operand),
        FieldExpr f => ExprContainsCall(f.Object),
        FieldAddrExpr f => ExprContainsCall(f.Object),
        VirtualCallExpr v => v.Args.Any(ExprContainsCall),
        DelegateInvokeExpr d => d.Args.Any(ExprContainsCall),
        MarshalUtf8Expr m => ExprContainsCall(m.Arg),
        _ => false,
    };

    private static bool StmtContainsCall(Stmt s) => s switch
    {
        AssignStmt a => ExprContainsCall(a.Value),
        ExprStmt es => ExprContainsCall(es.Value),
        RetStmt r => r.Value != null && ExprContainsCall(r.Value),
        BranchStmt b => b.Condition != null && ExprContainsCall(b.Condition),
        CallStmt => true,
        FieldAssignStmt f => ExprContainsCall(f.Object) || ExprContainsCall(f.Value),
        _ => false,
    };

    //--------------------------------------------------------------------------
    // D1 gate (diffdt001 dt.hash): a body DEFINING a struct-typed local must
    // not inline -- the emitter declares inline temps from expression kinds
    // (int32 default for user calls), so HashProbe's `loc0 = D6()` would
    // become `inl1 = D6()` with `int32_t inl1` (g++ "cannot convert
    // Il2CppDateTime to int32_t").
    //--------------------------------------------------------------------------

    [Fact]
    public void StructLocalDefiningBody_StaysACall_NoInline()
    {
        // void HashProbe() { loc0(DateTime) = D6(); ... } -- the loc0 def
        // carries struct kind "v:System.DateTime".
        var callee = Candidate("call_20", 0, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = new CallExpr { Function = "Cases_Prog_D6_xxx" } },
            new RetStmt { Value = null },
        }, returnsVoid: true, localKinds: new[] { "v:System.DateTime" });
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_20" },
            new RetStmt { Value = null },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.IsType<CallStmt>(result.Stmts[0]);
    }

    [Fact]
    public void IntLocalDefiningBody_StillInlines()
    {
        // Control: the same shape with an int32 local kind inlines as
        // before (the gate only fires on struct kinds).
        var callee = Candidate("call_21", 0, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(42) },
            new RetStmt { Value = null },
        }, returnsVoid: true, localKinds: new[] { "i" });
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_21" },
            new RetStmt { Value = null },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
    }

    //--------------------------------------------------------------------------
    // Basic expansion
    //--------------------------------------------------------------------------

    [Fact]
    public void StaticPureFunction_InlineAtCallSite_NoCallRemains()
    {
        // int Add(int a, int b) => a + b;   body = [ret a+b]
        var callee = Candidate("call_10", 2, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), Loc("arg1")) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc2", Value = new CallExpr { Function = "call_10", Args = { Loc("loc0"), Loc("loc1") } } },
            new RetStmt { Value = Loc("loc2") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // arg temps first, in evaluation order
        var arg0 = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(arg0.Value).Name);
        var arg1 = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("loc1", Assert.IsType<LocExpr>(arg1.Value).Name);
        // inlined computation: result = arg0temp + arg1temp
        var computed = Assert.IsType<AssignStmt>(result.Stmts[2]);
        var add = Assert.IsType<BinaryExpr>(computed.Value);
        Assert.Equal(BinaryOp.Add, add.Op);
        Assert.Equal(arg0.Target, Assert.IsType<LocExpr>(add.Left).Name);
        Assert.Equal(arg1.Target, Assert.IsType<LocExpr>(add.Right).Name);
        // end label, then the original consumer reads the result temp
        Assert.IsType<LabelStmt>(result.Stmts[3]);
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[4]);
        Assert.Equal("loc2", consumer.Target);
        Assert.Equal(computed.Target, Assert.IsType<LocExpr>(consumer.Value).Name);
        Assert.IsType<RetStmt>(result.Stmts[5]);
    }

    [Fact]
    public void InstanceGetter_Inline_FieldReadOnReceiverTemp()
    {
        // int GetX() => this.x;   body = [ret this.x]
        var callee = Candidate("call_11", 1, new List<Stmt>
        {
            new RetStmt
            {
                Value = new FieldExpr { Object = Loc("arg0"), FieldCppName = "x", OwnerFullName = "Calc.Point" },
            },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_11", Args = { Loc("loc0") } } },
            new RetStmt { Value = Loc("loc1") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        var recv = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(recv.Value).Name);
        var read = Assert.IsType<AssignStmt>(result.Stmts[1]);
        var field = Assert.IsType<FieldExpr>(read.Value);
        Assert.Equal(recv.Target, Assert.IsType<LocExpr>(field.Object).Name);
        Assert.Equal("x", field.FieldCppName);
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[3]);
        Assert.Equal(read.Target, Assert.IsType<LocExpr>(consumer.Value).Name);
    }

    [Fact]
    public void VoidMethod_StatementCall_Inline_DropsTheCall()
    {
        // void Log(int v) { Helper(v); }   body = [CallStmt Helper(arg0); ret]
        var callee = Candidate("call_12", 1, new List<Stmt>
        {
            new CallStmt { Function = "call_9", Args = { Loc("arg0") } },
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_12", Args = { Loc("loc0") } },
            new RetStmt { Value = null },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, s => s is CallStmt c && c.Function == "call_12");
        var argTemp = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(argTemp.Value).Name);
        var helper = Assert.IsType<CallStmt>(result.Stmts[1]); // the callee's OWN call stays (not a candidate)
        Assert.Equal("call_9", helper.Function);
        Assert.Equal(argTemp.Target, Assert.IsType<LocExpr>(helper.Args[0]).Name);
        Assert.IsType<LabelStmt>(result.Stmts[2]);
        Assert.IsType<RetStmt>(result.Stmts[3]);
    }

    [Fact]
    public void RetPositionCall_Inline_FinalRetReadsResultTemp()
    {
        // return helper(x);  -- the call is the RetStmt value
        var callee = Candidate("call_14", 1, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        });
        var caller = new List<Stmt>
        {
            new RetStmt { Value = new CallExpr { Function = "call_14", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        var finalRet = Assert.IsType<RetStmt>(result.Stmts[^1]);
        Assert.IsType<LocExpr>(finalRet.Value);
    }

    [Fact]
    public void MultipleCallSites_AllInlined()
    {
        var callee = Candidate("call_15", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_15", Args = { Loc("loc0") } } },
            new AssignStmt { Target = "loc2", Value = new CallExpr { Function = "call_15", Args = { Loc("loc1") } } },
            new RetStmt { Value = Loc("loc2") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(2, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
    }

    //--------------------------------------------------------------------------
    // Conservative gates
    //--------------------------------------------------------------------------

    [Fact]
    public void BodyOverThreshold_NotInlined()
    {
        // 11 statements > the straight-line threshold of 10
        var callee = Candidate("call_16", 0, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(0) },
            new AssignStmt { Target = "loc1", Value = I(1) },
            new AssignStmt { Target = "loc2", Value = I(2) },
            new AssignStmt { Target = "loc3", Value = I(3) },
            new AssignStmt { Target = "loc4", Value = I(4) },
            new AssignStmt { Target = "loc5", Value = I(5) },
            new AssignStmt { Target = "loc6", Value = I(6) },
            new AssignStmt { Target = "loc7", Value = I(7) },
            new AssignStmt { Target = "loc8", Value = I(8) },
            new AssignStmt { Target = "loc9", Value = I(9) },
            new RetStmt { Value = Loc("loc0") },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "locA", Value = new CallExpr { Function = "call_16" } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void StraightLineBody_UpToExtendedThreshold_Inlined()
    {
        // 6 straight-line statements exceed the 5-statement branched cap but
        // stay under the 10-statement straight-line cap -- a flat multi-store
        // initializer (ctor chain shape) is still inlined.
        var callee = Candidate("call_24", 0, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(0) },
            new AssignStmt { Target = "loc1", Value = I(1) },
            new AssignStmt { Target = "loc2", Value = I(2) },
            new AssignStmt { Target = "loc3", Value = I(3) },
            new AssignStmt { Target = "loc4", Value = I(4) },
            new RetStmt { Value = Loc("loc0") },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc9", Value = new CallExpr { Function = "call_24" } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // the caller's consumer still reads the callee's result
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        Assert.Equal("loc9", consumer.Target);
    }

    [Fact]
    public void RecursiveBody_NotInlined()
    {
        var callee = Candidate("call_17", 1, new List<Stmt>
        {
            new RetStmt { Value = new CallExpr { Function = "call_17", Args = { Loc("arg0") } } },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_17", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void ExceptionRegions_NotInlined()
    {
        var callee = Candidate("call_18", 1, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        }, eh: true);
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_18", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void UnknownCallee_NotInlined()
    {
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_999", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup());

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void RefResult_NotInlined()
    {
        var callee = Candidate("call_19", 1, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        }, returnsRef: true);
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_19", ReturnsRef = true, Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void ByrefArgument_NotInlined()
    {
        var callee = Candidate("call_20", 1, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_20", Args = { new LocAddrExpr { Name = "loc0" } } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void MismatchedArgCount_NotInlined()
    {
        var callee = Candidate("call_21", 2, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), Loc("arg1")) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_21", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // Control flow + name hygiene
    //--------------------------------------------------------------------------

    [Fact]
    public void BranchingBody_ControlFlowPreserved()
    {
        // 5 statements (at the threshold): abs-style two-exit body
        var callee = Candidate("call_13", 1, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
            new BranchStmt { TargetLabel = "L_5", Condition = Bin(BinaryOp.Cgt, Loc("loc0"), I(0)) },
            new RetStmt { Value = Bin(BinaryOp.Sub, Loc("loc0"), I(1)) },
            new LabelStmt { Name = "L_5" },
            new RetStmt { Value = Bin(BinaryOp.Mul, Loc("loc0"), I(2)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc3", Value = new CallExpr { Function = "call_13", Args = { Loc("loc2") } } },
            new RetStmt { Value = Loc("loc3") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // both ret paths write the SAME result temp (Sub and Mul exits)
        var retWriters = result.Stmts.OfType<AssignStmt>()
            .Where(a => a.Value is BinaryExpr b && (b.Op == BinaryOp.Sub || b.Op == BinaryOp.Mul))
            .ToList();
        Assert.Equal(2, retWriters.Count);
        Assert.Equal(retWriters[0].Target, retWriters[1].Target);
        var resultTemp = retWriters[0].Target;
        // the body label was renamed (caller-scope hygiene), and both branches
        // to the end exist
        var labelNames = result.Stmts.OfType<LabelStmt>().Select(l => l.Name).ToHashSet();
        var condBranches = result.Stmts.OfType<BranchStmt>().Where(b => b.Condition != null).ToList();
        Assert.Single(condBranches);
        Assert.Contains(condBranches[0].TargetLabel, labelNames);
        var gotos = result.Stmts.OfType<BranchStmt>().Where(b => b.Condition == null).ToList();
        Assert.Single(gotos); // only the EARLY ret jumps; the last ret falls through
        Assert.Contains(gotos[0].TargetLabel, labelNames);
        // the consumer runs AFTER the end label
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[^2]);
        Assert.Equal("loc3", consumer.Target);
        Assert.Equal(resultTemp, Assert.IsType<LocExpr>(consumer.Value).Name);
    }

    [Fact]
    public void CalleeLocalsAndLabels_DoNotCollideWithCaller()
    {
        // callee uses loc0 and L_5; caller ALSO uses loc0 and L_5
        var callee = Candidate("call_22", 1, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
            new BranchStmt { TargetLabel = "L_5", Condition = Bin(BinaryOp.Cgt, Loc("loc0"), I(0)) },
            new LabelStmt { Name = "L_5" },
            new RetStmt { Value = Loc("loc0") },
        });
        var caller = new List<Stmt>
        {
            new LabelStmt { Name = "L_5" },
            new AssignStmt { Target = "loc0", Value = I(7) },
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_22", Args = { Loc("loc2") } } },
            new RetStmt { Value = Loc("loc1") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        // caller's own label/assign are untouched
        Assert.Equal("L_5", Assert.IsType<LabelStmt>(result.Stmts[0]).Name);
        Assert.Equal("loc0", Assert.IsType<AssignStmt>(result.Stmts[1]).Target);
        // the callee's inlined pieces use fresh names
        var inlinedLoc = Assert.IsType<AssignStmt>(result.Stmts[3]);
        Assert.NotEqual("loc0", inlinedLoc.Target);
        var inlinedLabels = result.Stmts.OfType<LabelStmt>().Where(l => l.Name != "L_5").ToList();
        Assert.Equal(2, inlinedLabels.Count); // the renamed body label + the end label
        Assert.NotEqual(inlinedLabels[0].Name, inlinedLabels[1].Name);
    }

    //--------------------------------------------------------------------------
    // Nested-call extension (the object-bench Get shape: `sum += it.Get() &
    // 0xFF` folds to `loc0 = loc0 + (Get(loc2) & 0xFF)` -- the call sits
    // inside a BinaryExpr, not at the statement top level).
    //--------------------------------------------------------------------------

    [Fact]
    public void NestedCallInsideBinaryExpression_Inline_RemovesCall()
    {
        // int Get() => v;  body = [ret this.v]; call nested in `sum + (Get(it) & 0xFF)`
        var callee = Candidate("call_30", 1, new List<Stmt>
        {
            new RetStmt { Value = new FieldExpr { Object = Loc("arg0"), FieldCppName = "v", OwnerFullName = "Bench.Item" } },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Bin(BinaryOp.Add, Loc("loc0"), Bin(BinaryOp.And, new CallExpr { Function = "call_30", Args = { Loc("loc2") } }, I(255))) },
            new RetStmt { Value = Loc("loc0") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // arg temp first (evaluation order), then the field read on it
        var argTemp = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc2", Assert.IsType<LocExpr>(argTemp.Value).Name);
        var read = Assert.IsType<AssignStmt>(result.Stmts[1]);
        var field = Assert.IsType<FieldExpr>(read.Value);
        Assert.Equal(argTemp.Target, Assert.IsType<LocExpr>(field.Object).Name);
        // the consumer keeps the binary shape, with the call replaced by the
        // result-temp read (the caller's trailing RetStmt is the final stmt)
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[^2]);
        var add = Assert.IsType<BinaryExpr>(consumer.Value);
        var and = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal(read.Target, Assert.IsType<LocExpr>(and.Left).Name);
    }

    [Fact]
    public void MultipleNestedCallsInOneStatement_AllInlined()
    {
        // f(loc1) + f(loc2) -- two nested calls in one binary expression
        var callee = Candidate("call_31", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Bin(BinaryOp.Add, new CallExpr { Function = "call_31", Args = { Loc("loc1") } }, new CallExpr { Function = "call_31", Args = { Loc("loc2") } }) },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(2, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
    }

    [Fact]
    public void NestedCallInArg_BothInlined()
    {
        // f(g(x)) -- the inner call is an argument of the outer call; both
        // must expand, g's result feeding f's arg temp (left-to-right order).
        var g = Candidate("call_32", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var f = Candidate("call_33", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Mul, Loc("arg0"), I(2)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc9", Value = new CallExpr { Function = "call_33", Args = { new CallExpr { Function = "call_32", Args = { Loc("loc1") } } } } },
        };

        var result = InlinePass.Inline(caller, Lookup(g, f));

        Assert.Equal(2, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // g's arg temp is the very first statement
        var first = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc1", Assert.IsType<LocExpr>(first.Value).Name);
    }

    [Fact]
    public void RefReturningNestedCall_NotInlined()
    {
        // a reference-returning callee is not inlineable -- the whole nested
        // subtree stays untouched
        var callee = Candidate("call_34", 1, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        }, returnsRef: true);
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Bin(BinaryOp.Add, new CallExpr { Function = "call_34", ReturnsRef = true, Args = { Loc("loc1") } }, I(1)) },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void UnusedArgument_NoTempMaterialized()
    {
        // body never reads arg1 -- no temp is emitted for it
        var callee = Candidate("call_23", 2, new List<Stmt>
        {
            new RetStmt { Value = Loc("arg0") },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_23", Args = { Loc("loc0"), Loc("loc9") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        Assert.DoesNotContain(result.Stmts, s => s is AssignStmt a && ExprUsesLoc(a.Value, "loc9"));
    }

    private static bool ExprUsesLoc(Expr e, string name) => e switch
    {
        LocExpr l => l.Name == name,
        BinaryExpr b => ExprUsesLoc(b.Left, name) || ExprUsesLoc(b.Right, name),
        UnaryExpr u => ExprUsesLoc(u.Operand, name),
        _ => false,
    };

    private static int ExprCallCount(Expr e) => e switch
    {
        CallExpr => 1,
        BinaryExpr b => ExprCallCount(b.Left) + ExprCallCount(b.Right),
        UnaryExpr u => ExprCallCount(u.Operand),
        FieldExpr f => ExprCallCount(f.Object),
        FieldAddrExpr f => ExprCallCount(f.Object),
        VirtualCallExpr v => v.Args.Sum(ExprCallCount),
        DelegateInvokeExpr d => d.Args.Sum(ExprCallCount),
        MarshalUtf8Expr m => ExprCallCount(m.Arg),
        _ => 0,
    };

    private static FieldAssignStmt FieldStore(string target, string receiver, Expr value) => new()
    {
        Object = Loc(receiver),
        FieldCppName = target,
        OwnerFullName = "Calc.Point",
        RefTargetFullName = "Calc.Point",
        Value = value,
    };

    //--------------------------------------------------------------------------
    // callvirt non-virtual inlining (the "opt 批" extension -- Roslyn emits
    // callvirt for EVERY instance call; a non-virtual callee is a compile-time
    // direct call and is inlineable).
    //--------------------------------------------------------------------------

    [Fact]
    public void CallvirtValuePosition_NonVirtualGetter_Inline()
    {
        // int GetX() => this.x; -- the body reads this, so the field-read
        // null-check emission preserves the callvirt's implicit NRE.
        var callee = Candidate("call_56", 1, new List<Stmt>
        {
            new RetStmt { Value = new FieldExpr { Object = Loc("arg0"), FieldCppName = "x", OwnerFullName = "Calc.Point" } },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new VirtualCallExpr { MethodToken = 56, ReturnsRef = false, Args = { Loc("loc0") } } },
            new RetStmt { Value = Loc("loc1") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
    }

    [Fact]
    public void VoidCallvirtStatement_NonVirtualBody_Inlined()
    {
        // void SetX(int v) { this.x = v; } -- StackFold folds a VOID callvirt
        // into a CallStmt Function "vcall_<token>"; the non-virtual callee is a
        // compile-time direct call and inlines (the store lands on the receiver
        // temp, keeping the field-write null-check).
        var callee = Candidate("call_42", 2, new List<Stmt>
        {
            FieldStore("x", "arg0", Loc("arg1")),
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "vcall_42", Args = { Loc("loc0"), Loc("loc1") } },
            new RetStmt { Value = null },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, s => s is CallStmt c && c.Function == "vcall_42");
        // receiver temp then arg temp (evaluation order), then the field store
        var recv = Assert.IsType<AssignStmt>(result.Stmts[0]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(recv.Value).Name);
        var arg = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("loc1", Assert.IsType<LocExpr>(arg.Value).Name);
        var store = Assert.IsType<FieldAssignStmt>(result.Stmts[2]);
        Assert.Equal(recv.Target, Assert.IsType<LocExpr>(store.Object).Name);
        Assert.Equal(arg.Target, Assert.IsType<LocExpr>(store.Value).Name);
        Assert.IsType<LabelStmt>(result.Stmts[3]);
        Assert.IsType<RetStmt>(result.Stmts[4]);
    }

    [Fact]
    public void VirtualCallee_Callvirt_NotInlined()
    {
        // virtual int GetX() -- callvirt dispatch can reach an override: never inlined.
        var callee = Candidate("call_43", 1, new List<Stmt>
        {
            new RetStmt { Value = new FieldExpr { Object = Loc("arg0"), FieldCppName = "x", OwnerFullName = "Calc.Point" } },
        }, isVirtual: true);
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new VirtualCallExpr { MethodToken = 43, ReturnsRef = false, Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void CallvirtReceiverUnusedInBody_NotInlined()
    {
        // int GetConst() => 42; -- the body never reads this; inlining a
        // callvirt would drop the dispatch's implicit null-check (a null
        // receiver must NRE even when the body ignores it).
        var callee = Candidate("call_44", 1, new List<Stmt>
        {
            new RetStmt { Value = I(42) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new VirtualCallExpr { MethodToken = 44, ReturnsRef = false, Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void VoidCallvirtReceiverUnused_NotInlined()
    {
        // void Nop() { } -- the void-callvirt form of the same receiver gate.
        var callee = Candidate("call_45", 1, new List<Stmt>
        {
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "vcall_45", Args = { Loc("loc0") } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // Static-write purity gate (opt2 -- shared state must stay in the callee).
    //--------------------------------------------------------------------------

    [Fact]
    public void StaticWriteBody_NotInlined()
    {
        // void Inc() { Counter.Count++; } -- writes a static field: inlining
        // would replicate the shared mutation at every call site.
        var callee = Candidate("call_46", 1, new List<Stmt>
        {
            new AssignStmt { Target = "Calc.Counter.Count", Value = Bin(BinaryOp.Add, new StaticFieldExpr { FieldName = "Calc.Counter.Count" }, I(1)) },
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_46", Args = { Loc("loc0") } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void StaticReadBody_StillInlined()
    {
        // int GetCount() => Counter.Count; -- reads of static state are pure.
        var callee = Candidate("call_47", 0, new List<Stmt>
        {
            new RetStmt { Value = new StaticFieldExpr { FieldName = "Calc.Counter.Count" } },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_47" } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
    }

    //--------------------------------------------------------------------------
    // Recursion detection completeness (self-calls hidden in expression trees,
    // callvirt aliases and field-assign values).
    //--------------------------------------------------------------------------

    [Fact]
    public void RecursiveBody_SelfCallViaCallvirt_NotInlined()
    {
        // int F(int x) => this.F(x - 1); -- the recursion is a VirtualCallExpr
        // whose token matches the candidate's own token (a non-virtual method
        // called via callvirt is otherwise inlineable).
        var callee = Candidate("call_49", 1, new List<Stmt>
        {
            new RetStmt { Value = new VirtualCallExpr { MethodToken = 49, ReturnsRef = false, Args = { Loc("arg0"), Bin(BinaryOp.Sub, Loc("arg0"), I(1)) } } },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_49", Args = { Loc("loc0") } } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void RecursiveBody_SelfCallInFieldAssignValue_NotInlined()
    {
        // void Set(int x) { this.x = F(x - 1); } -- self-call in a field store's value.
        var callee = Candidate("call_50", 2, new List<Stmt>
        {
            FieldStore("x", "arg0", new CallExpr { Function = "call_50", Args = { Loc("arg0"), Bin(BinaryOp.Sub, Loc("arg1"), I(1)) } }),
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_50", Args = { Loc("loc0"), Loc("loc1") } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    [Fact]
    public void RecursiveBody_SelfCallViaVoidCallvirtStatement_NotInlined()
    {
        // void F(int x) { F(x - 1); } -- the body's recursion is a VOID callvirt
        // (CallStmt "vcall_<token>"): the UserCallKey alias must be detected.
        var callee = Candidate("call_51", 2, new List<Stmt>
        {
            new CallStmt { Function = "vcall_51", Args = { Loc("arg0"), Bin(BinaryOp.Sub, Loc("arg1"), I(1)) } },
            new RetStmt { Value = null },
        }, returnsVoid: true);
        var caller = new List<Stmt>
        {
            new CallStmt { Function = "call_51", Args = { Loc("loc0"), Loc("loc1") } },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(0, result.Inlined);
        Assert.Same(caller, result.Stmts);
    }

    //--------------------------------------------------------------------------
    // Per-statement nested-expansion cap.
    //--------------------------------------------------------------------------

    [Fact]
    public void NestedExpansionCap_PerStatement_CappedAtMax()
    {
        // 20 inlineable calls in ONE binary statement -- the per-statement cap
        // (16) bounds the expansion; the remainder stays as calls.
        var callee = Candidate("call_52", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        Expr tree = I(0);
        for (var i = 0; i < 20; i++)
            tree = Bin(BinaryOp.Add, tree, new CallExpr { Function = "call_52", Args = { Loc("loc0") } });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc9", Value = tree },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(InlinePass.MaxNestedExpansionsPerStatement, result.Inlined);
        var final = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        Assert.Equal(20 - InlinePass.MaxNestedExpansionsPerStatement, ExprCallCount(final.Value));
    }

    //--------------------------------------------------------------------------
    // Nested inlining into less common expression kinds.
    //--------------------------------------------------------------------------

    [Fact]
    public void NestedCallInsideDelegateInvokeArg_Inlined()
    {
        // d.Invoke(f(x)) -- the delegate Invoke stays (runtime methodPtr
        // dispatch), but the user call f inside its args expands first.
        var f = Candidate("call_53", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = new DelegateInvokeExpr { MethodToken = 100, ReturnsRef = false, Args = { Loc("loc1"), new CallExpr { Function = "call_53", Args = { Loc("loc2") } } } } },
        };

        var result = InlinePass.Inline(caller, Lookup(f));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // the delegate Invoke survives as the consumer, with the call replaced
        // by a result-temp read
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        var invoke = Assert.IsType<DelegateInvokeExpr>(consumer.Value);
        Assert.IsType<LocExpr>(invoke.Args[1]);
    }

    [Fact]
    public void NestedCallsInDelegateInvokeArgs_BothInlined_PreludesAccumulated()
    {
        // d.Invoke(f(x), f(y)) -- BOTH arg expansions must appear in the prelude
        // (the first arg's result temp must stay defined; the delegate Invoke
        // itself remains a runtime dispatch).
        var f = Candidate("call_58", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = I(1) },
            new AssignStmt { Target = "loc2", Value = I(2) },
            new AssignStmt { Target = "loc3", Value = I(3) },
            new AssignStmt { Target = "loc0", Value = new DelegateInvokeExpr { MethodToken = 101, ReturnsRef = false, Args = { Loc("loc1"), new CallExpr { Function = "call_58", Args = { Loc("loc2") } }, new CallExpr { Function = "call_58", Args = { Loc("loc3") } } } } },
        };

        var result = InlinePass.Inline(caller, Lookup(f));

        Assert.Equal(2, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        Assert.Empty(DcePass.IntegrityViolations(result.Stmts));
        // both inlined results are read by the surviving delegate Invoke
        var consumer = Assert.IsType<AssignStmt>(result.Stmts[^1]);
        var invoke = Assert.IsType<DelegateInvokeExpr>(consumer.Value);
        Assert.IsType<LocExpr>(invoke.Args[1]);
        Assert.IsType<LocExpr>(invoke.Args[2]);
    }

    [Fact]
    public void NestedCallInFieldAssignValue_Inlined()
    {
        // it.x = f(loc) -- the call nested in the field store's value expands.
        var f = Candidate("call_54", 1, new List<Stmt>
        {
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            FieldStore("x", "loc0", new CallExpr { Function = "call_54", Args = { Loc("loc1") } }),
        };

        var result = InlinePass.Inline(caller, Lookup(f));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // the final field store reads the result temp
        var store = Assert.IsType<FieldAssignStmt>(result.Stmts[^1]);
        Assert.IsType<LocExpr>(store.Value);
    }

    //--------------------------------------------------------------------------
    // Interaction with DCE: inlined bodies can carry dead stores the pass
    // itself copied in -- DcePass must clean them and never dangle a read.
    //--------------------------------------------------------------------------

    [Fact]
    public void InlineThenDce_CleansDeadInlineTemps_NoIntegrityViolations()
    {
        // callee body writes a local it never reads (dead in the callee): the
        // inline copies the dead store into the caller, and DcePass removes it.
        var callee = Candidate("call_55", 1, new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = Loc("arg0") },
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(5) },
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_55", Args = { Loc("loc0") } } },
            new RetStmt { Value = Loc("loc1") },
        };

        var inlined = InlinePass.Inline(caller, Lookup(callee)).Stmts;
        var (dced, storesRemoved, _) = DcePass.Run(inlined);

        Assert.True(storesRemoved >= 1, "the dead inline store must be removed");
        Assert.Empty(DcePass.IntegrityViolations(dced));
        // the caller's consumer still reads the inlined result
        var consumer = dced.OfType<AssignStmt>().Last(a => a.Target == "loc1");
        Assert.IsType<LocExpr>(consumer.Value);
    }

    [Fact]
    public void CalleeLocal_DoesNotCollideWithArgTemp()
    {
        // int F(int a) { int t = a; return t + a; } -- the callee local t must
        // get a FRESH caller-scope name, never the arg temp's name (aliasing t
        // onto the arg temp would silently overwrite the argument).
        var callee = Candidate("call_57", 1, new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = Loc("arg0") },
            new RetStmt { Value = Bin(BinaryOp.Add, Loc("t0"), Loc("arg0")) },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt { Target = "loc1", Value = new CallExpr { Function = "call_57", Args = { Loc("loc0") } } },
            new RetStmt { Value = Loc("loc1") },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.DoesNotContain(result.Stmts, StmtContainsCall);
        // arg temp then the callee's t0 rename -- distinct names (regression:
        // the old `inl{locMap.Count}` counter collided with the arg temp).
        var argTemp = Assert.IsType<AssignStmt>(result.Stmts[0]).Target;
        var calleeTemp = Assert.IsType<AssignStmt>(result.Stmts[1]).Target;
        Assert.NotEqual(argTemp, calleeTemp);
        // the inlined ret computation reads the callee's renamed t0, not the arg temp
        var retWrite = Assert.IsType<AssignStmt>(result.Stmts[2]);
        var add = Assert.IsType<BinaryExpr>(retWrite.Value);
        Assert.Equal(calleeTemp, Assert.IsType<LocExpr>(add.Left).Name);
        Assert.Equal(argTemp, Assert.IsType<LocExpr>(add.Right).Name);
    }

    //--------------------------------------------------------------------------
    // M7R3 (exdeep "expected catch"): the EH walk assigns try-region
    // membership by SourceOffset, so every inlined statement must carry the
    // CALL SITE's IL offset (a default -1 would leave the enclosing try range
    // and close the try block before its catch handler opens).
    //--------------------------------------------------------------------------
    [Fact]
    public void InlinedStatements_InheritCallSiteSourceOffset()
    {
        var callee = Candidate("call_99", 1, new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = Bin(BinaryOp.Add, Loc("arg0"), I(1)) },
            new RetStmt { Value = Loc("t0") },
        });
        var caller = new List<Stmt>
        {
            new AssignStmt
            {
                Target = "loc1",
                Value = new CallExpr { Function = "call_99", Args = { Loc("loc0") } },
                SourceOffset = 0x00A8,
                SourceOpName = "call",
            },
            new RetStmt { Value = Loc("loc1"), SourceOffset = 0x00AF, SourceOpName = "ret" },
        };

        var result = InlinePass.Inline(caller, Lookup(callee));

        Assert.Equal(1, result.Inlined);
        Assert.All(result.Stmts.Take(result.Stmts.Count - 1), s => Assert.Equal(0x00A8, s.SourceOffset));
    }
}
