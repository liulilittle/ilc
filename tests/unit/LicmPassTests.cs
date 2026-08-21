using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// LICM (loop-invariant code motion) pass tests -- LicmPass.Hoist.
//
// Target shape (single-block natural loops, the SAME conservative shape
// StackFold.HoistLoopGuards accepts -- see StackFoldTests for the precedent):
//   while shape (Roslyn for/while default):
//       ...predecessors
//       goto L_cond;             // unconditional entry jump
//       L_header: body
//       L_cond: if (cond) goto L_header;    // back-edge
//   do-while shape (fall-through entry):
//       ...predecessors
//       L_header: body
//       if (cond) goto L_header;  // back-edge (no separate cond label)
//
// Hoistable = pure whitelisted length icalls (string/array Length) whose
// EVERY argument is loop-invariant (constant or local never assigned AND
// never address-taken -- ldloca/ldarga escapes -- in the body or loop test).
// Safety boundary: the loop TEST runs >= 1 time in both shapes, but the BODY
// runs >= 1 time only in the do-while shape. A potentially-FAULTING length
// read (string length -> NRE on a null receiver, Array.GetLength -> receiver
// + dimension validation) is hoisted only from a position guaranteed to
// execute; a NON-faulting read (array length / raw ldlen -- a null receiver
// returns 0) is hoistable anywhere. Anything with observable behavior (user
// calls, non-whitelisted icalls, ref results, non-int32 results) stays in the
// loop.
//------------------------------------------------------------------------------
public class LicmPassTests
{
    // il2cpp::il2cpp_string_get_length -- string Length (MethodMap.String.cs)
    private static Expr StrLen(string local) => new CallExpr
    {
        Function = "call_100",
        Resolved = new ResolvedMethod("il2cpp::il2cpp_string_get_length", 1, false, true, false, new[] { false }, true),
        Args = { new LocExpr { Name = local } },
    };

    // il2cpp::il2cpp_array_get_length -- array Length (MethodMap.Array.cs)
    private static Expr ArrLen(string local) => new CallExpr
    {
        Function = "call_101",
        Resolved = new ResolvedMethod("il2cpp::il2cpp_array_get_length", 1, false, true, false, new[] { false }, true),
        Args = { new LocExpr { Name = local } },
    };

    // raw ldlen (IL ldlen on an array reference)
    private static Expr RawLdlen(string local) => new CallExpr
    {
        Function = "ldlen_42",
        Args = { new LocExpr { Name = local } },
    };

    // il2cpp::runtime::icall_array_get_length -- Array.GetLength(int)
    // (MethodMap.Array.cs); faulting: receiver + dimension validation.
    private static Expr GetLen(string local, Expr dimension) => new CallExpr
    {
        Function = "call_102",
        Resolved = new ResolvedMethod("il2cpp::runtime::icall_array_get_length", 1, false, true, false, new[] { false, false }, true),
        Args = { new LocExpr { Name = local }, dimension },
    };

    private static Expr Bin(BinaryOp op, Expr l, Expr r) => new BinaryExpr { Op = op, Left = l, Right = r };

    private static Expr I(long v) => new IntConstExpr { Value = v };

    // while-shape loop; the body statement is `loc1 = bodyValue`
    private static List<Stmt> WhileShapeLoop(Expr bodyValue, Expr? backCond = null) => new()
    {
        new AssignStmt { Target = "loc0", Value = I(0) },           // i = 0
        new AssignStmt { Target = "loc1", Value = I(0) },           // sum = 0
        new BranchStmt { TargetLabel = "L_30" },                    // entry goto
        new LabelStmt { Name = "L_4" },                             // header
        new AssignStmt { Target = "loc1", Value = bodyValue },      // body
        new LabelStmt { Name = "L_30" },                            // cond label
        new BranchStmt { TargetLabel = "L_4", Condition = backCond ?? Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(20000000)) },
    };

    // do-while shape; body = `loc1 = bodyValue`
    private static List<Stmt> DoWhileShapeLoop(Expr bodyValue) => new()
    {
        new AssignStmt { Target = "loc0", Value = I(0) },
        new LabelStmt { Name = "L_4" },                             // header (fall-through entry)
        new AssignStmt { Target = "loc1", Value = bodyValue },      // body
        new BranchStmt { TargetLabel = "L_4", Condition = Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(100)) },
    };

    // asserts the statement at index `bodyIdx` (the LOOP BODY -- preheader
    // statements legitimately retain their hoisted call) carries no call
    private static void AssertBodyHasNoCall(IReadOnlyList<Stmt> stmts, int bodyIdx)
        => Assert.False(ExprContainsCall(Assert.IsType<AssignStmt>(stmts[bodyIdx]).Value));

    private static bool ExprContainsCall(Expr e) => e switch
    {
        CallExpr => true,
        BinaryExpr b => ExprContainsCall(b.Left) || ExprContainsCall(b.Right),
        UnaryExpr u => ExprContainsCall(u.Operand),
        _ => false,
    };

    private static bool ExprUsesLoc(Expr e, string name) => e switch
    {
        LocExpr l => l.Name == name,
        BinaryExpr b => ExprUsesLoc(b.Left, name) || ExprUsesLoc(b.Right, name),
        UnaryExpr u => ExprUsesLoc(u.Operand, name),
        CallExpr c => c.Args.Any(a => ExprUsesLoc(a, name)),
        _ => false,
    };

    [Fact]
    public void WhileShape_NonFaultingBodyLength_HoistedToPreheader()
    {
        // sum += a.Length & 0xFF  -- a (loc2) never assigned/address-taken in
        // the loop. il2cpp_array_get_length is NON-FAULTING (a null receiver
        // returns 0), so it is safe to hoist even from a while-shape BODY
        // that can run ZERO times (the hoisted read executes once, result
        // unused -- nothing observable). A faulting read (string length ->
        // NRE on null) is NOT hoisted from a while-shape body -- see
        // WhileShape_FaultingBodyCall_NotHoisted.
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            Bin(BinaryOp.And, ArrLen("loc2"), I(0xFF)));
        var result = LicmPass.Hoist(WhileShapeLoop(body));

        Assert.Equal(1, result.Hoisted);
        // preheader: the hoisted computation sits BEFORE the entry goto
        var hoisted = Assert.IsType<AssignStmt>(result.Stmts[2]);
        Assert.Equal("licm0", hoisted.Target);
        Assert.Equal("il2cpp::il2cpp_array_get_length", Assert.IsType<CallExpr>(hoisted.Value).Resolved?.CppName);
        Assert.Equal("L_30", Assert.IsType<BranchStmt>(result.Stmts[3]).TargetLabel); // entry goto follows
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result.Stmts[4]).Name);          // header follows
        // the body now reads the cached temp instead of calling
        var bodyStmt = Assert.IsType<AssignStmt>(result.Stmts[5]);
        Assert.True(ExprUsesLoc(bodyStmt.Value, "licm0"));
        AssertBodyHasNoCall(result.Stmts, 5);
    }

    [Fact]
    public void DoWhileShape_InvariantArrayLength_HoistedToPreheader()
    {
        // sum += a.Length -- a (loc2) never assigned in the loop
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, ArrLen("loc2"));
        var result = LicmPass.Hoist(DoWhileShapeLoop(body));

        Assert.Equal(1, result.Hoisted);
        var hoisted = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("licm0", hoisted.Target);
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result.Stmts[2]).Name); // header follows the hoisted stmt
        AssertBodyHasNoCall(result.Stmts, 3);
    }

    [Fact]
    public void RawLdlen_InvariantReceiver_Hoisted()
    {
        // ldlen_<token> -- inline pure length read (CodeGenerator ldlen_ emission)
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, RawLdlen("loc2"));
        var result = LicmPass.Hoist(WhileShapeLoop(body));

        Assert.Equal(1, result.Hoisted);
        Assert.Equal("ldlen_42", Assert.IsType<CallExpr>(Assert.IsType<AssignStmt>(result.Stmts[2]).Value).Function);
    }

    [Fact]
    public void ReceiverAssignedInBody_NotHoisted()
    {
        // s (loc2) is REASSIGNED in the body -> its Length is loop-variant
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")));
        stmts.Insert(4, new AssignStmt { Target = "loc2", Value = I(1) });
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void UserMethodCall_NotHoisted()
    {
        // a USER method call (no Resolved icall identity) may have side effects
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            new CallExpr { Function = "call_999", Args = { new LocExpr { Name = "loc2" } } });
        var stmts = WhileShapeLoop(body);
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
        Assert.True(ExprContainsCall(Assert.IsType<AssignStmt>(result.Stmts[4]).Value));
    }

    [Fact]
    public void NonWhitelistedIcall_NotHoisted()
    {
        // an icall NOT on the pure whitelist (allocating concat) stays in the loop
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            new CallExpr
            {
                Function = "call_200",
                Resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_string_concat_many", 2, false, true, false, new[] { false, false }, true),
                Args = { new LocExpr { Name = "loc2" }, new LocExpr { Name = "loc2" } },
            });
        var result = LicmPass.Hoist(WhileShapeLoop(body));

        Assert.Equal(0, result.Hoisted);
    }

    [Fact]
    public void RefResultCall_NotHoisted()
    {
        // a ref-returning call (isinst/castclass/newarr) is never hoisted
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            new CallExpr { Function = "isinst_42", ReturnsRef = true, Args = { new LocExpr { Name = "loc2" } } });
        var result = LicmPass.Hoist(WhileShapeLoop(body));

        Assert.Equal(0, result.Hoisted);
    }

    [Fact]
    public void NonInt32ResultCall_NotHoisted()
    {
        // int64/double results would need typed temps -- conservative skip
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            new CallExpr
            {
                Function = "call_201",
                ResultKind = "l",
                Resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_stream_get_length", 0, false, true, false, Array.Empty<bool>(), true, ResultKind: "l"),
                Args = { new LocExpr { Name = "loc2" } },
            });
        var result = LicmPass.Hoist(WhileShapeLoop(body));

        Assert.Equal(0, result.Hoisted);
    }

    [Fact]
    public void InvariantCallInBackEdgeCondition_Hoisted()
    {
        // while (s.Length > i) -- the loop test's invariant call is hoisted
        // too. The test is evaluated >= 1 time in BOTH shapes, so even a
        // FAULTING read (string length -> NRE on null) is safe here: on a
        // zero-trip loop the original test would have evaluated it anyway.
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, I(1));
        var cond = Bin(BinaryOp.Cgt, StrLen("loc2"), new LocExpr { Name = "loc0" });
        var result = LicmPass.Hoist(WhileShapeLoop(body, cond));

        Assert.Equal(1, result.Hoisted);
        var back = Assert.IsType<BranchStmt>(result.Stmts[^1]);
        var newCond = Assert.IsType<BinaryExpr>(back.Condition);
        Assert.Equal("licm0", Assert.IsType<LocExpr>(newCond.Left).Name);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(newCond.Right).Name);
    }

    [Fact]
    public void BodyBranchToElsewhere_ShapeRejected_NoHoist()
    {
        // a branch in the body (break/continue) makes the loop non-single-block
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")));
        stmts.Insert(5, new BranchStmt { TargetLabel = "L_99", Condition = new LocExpr { Name = "loc0" } });
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void NestedLabelInBody_ShapeRejected_NoHoist()
    {
        // a second label in the body (nested loop / join point) is rejected
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")));
        stmts.Insert(5, new LabelStmt { Name = "L_99" });
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
    }

    [Fact]
    public void TwoInvariantSites_TwoTemps()
    {
        // s.Length + s.Length -- each site gets its own cached temp. do-while
        // shape: the body is guaranteed to run, so a faulting string-length
        // read is hoistable from it.
        var body = Bin(BinaryOp.Add,
            Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")),
            StrLen("loc2"));
        var result = LicmPass.Hoist(DoWhileShapeLoop(body));

        Assert.Equal(2, result.Hoisted);
        Assert.Equal("licm0", Assert.IsType<AssignStmt>(result.Stmts[1]).Target);
        Assert.Equal("licm1", Assert.IsType<AssignStmt>(result.Stmts[2]).Target);
        AssertBodyHasNoCall(result.Stmts, 4);
    }

    [Fact]
    public void WhileShape_FaultingBodyCall_NotHoisted()
    {
        // string Length (il2cpp_string_get_length) THROWS NRE on a null
        // receiver. A while-shape BODY can run ZERO times -- hoisting the
        // read into the preheader would newly execute it on a zero-trip loop,
        // so it must stay in the body. (The loop-test position IS safe --
        // see InvariantCallInBackEdgeCondition_Hoisted.)
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")));
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
        Assert.True(ExprContainsCall(Assert.IsType<AssignStmt>(result.Stmts[4]).Value));
    }

    [Fact]
    public void DoWhileShape_FaultingBodyCall_Hoisted()
    {
        // A do-while BODY runs >= 1 time (single fall-through entry), so a
        // faulting string-length read IS safe to hoist from it.
        var result = LicmPass.Hoist(DoWhileShapeLoop(
            Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2"))));

        Assert.Equal(1, result.Hoisted);
        var hoisted = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("licm0", hoisted.Target);
        Assert.Equal("il2cpp::il2cpp_string_get_length", Assert.IsType<CallExpr>(hoisted.Value).Resolved?.CppName);
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result.Stmts[2]).Name); // header follows the preheader
        var bodyStmt = Assert.IsType<AssignStmt>(result.Stmts[3]);
        Assert.True(ExprUsesLoc(bodyStmt.Value, "licm0"));
        AssertBodyHasNoCall(result.Stmts, 3);
    }

    [Fact]
    public void LocAddrEscapeInBody_ReceiverVariant_NotHoisted()
    {
        // The body takes the ADDRESS of the receiver (ldloca/ldarga -- an
        // opaque callee can write through the pointer, aliasing the value
        // without an AssignStmt): the receiver is loop-variant, so the length
        // read must not be hoisted (DcePass's address-taken poison rule).
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, StrLen("loc2")));
        stmts.Insert(4, new CallStmt { Function = "call_77", Args = { new LocAddrExpr { Name = "loc2" } } });
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void LocAddrInLoopTest_ReceiverVariant_NotHoisted()
    {
        // An address-taking call in the LOOP TEST also poisons the receiver:
        // even the guaranteed-executed test position must not hoist a read of
        // a local whose value can change through an escaped address.
        var cond = Bin(BinaryOp.Cgt, StrLen("loc2"),
            new CallExpr { Function = "call_90", Args = { new LocAddrExpr { Name = "loc2" } } });
        var stmts = WhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" }, I(1)), cond);
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
    }

    [Fact]
    public void TwoLoops_BothHoisted()
    {
        // Two independent while loops in one method: the fixed-point rescan
        // hoists the non-faulting array-length read out of BOTH.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(0) },
            new BranchStmt { TargetLabel = "L_40" },
            new LabelStmt { Name = "L_10" },
            new AssignStmt { Target = "loc1", Value = ArrLen("loc2") },
            new LabelStmt { Name = "L_40" },
            new BranchStmt { TargetLabel = "L_10", Condition = Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(100)) },
            new AssignStmt { Target = "loc0", Value = I(0) },
            new BranchStmt { TargetLabel = "L_41" },
            new LabelStmt { Name = "L_11" },
            new AssignStmt { Target = "loc1", Value = ArrLen("loc2") },
            new LabelStmt { Name = "L_41" },
            new BranchStmt { TargetLabel = "L_11", Condition = Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(100)) },
        };
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(2, result.Hoisted);
        Assert.Equal("licm0", Assert.IsType<AssignStmt>(result.Stmts[1]).Target);
        Assert.Equal("licm1", Assert.IsType<AssignStmt>(result.Stmts[8]).Target);
    }

    [Fact]
    public void DoWhileAtMethodStart_Hoisted()
    {
        // A do-while loop whose header is the method's FIRST statement (no
        // predecessor): the preheader lands before it at index 0.
        var stmts = new List<Stmt>
        {
            new LabelStmt { Name = "L_4" },
            new AssignStmt { Target = "loc1", Value = ArrLen("loc2") },
            new BranchStmt { TargetLabel = "L_4", Condition = Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(100)) },
        };
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(1, result.Hoisted);
        Assert.Equal("licm0", Assert.IsType<AssignStmt>(result.Stmts[0]).Target);
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result.Stmts[1]).Name);
        AssertBodyHasNoCall(result.Stmts, 2);
    }

    [Fact]
    public void ExprStmtBody_Hoisted()
    {
        // A discarded-expression statement in the body (ExprStmt) is scanned
        // for hoistable calls too.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = I(0) },
            new LabelStmt { Name = "L_4" },
            new ExprStmt { Value = ArrLen("loc2") },
            new BranchStmt { TargetLabel = "L_4", Condition = Bin(BinaryOp.Clt, new LocExpr { Name = "loc0" }, I(100)) },
        };
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(1, result.Hoisted);
        Assert.False(ExprContainsCall(Assert.IsType<ExprStmt>(result.Stmts[3]).Value));
    }

    [Fact]
    public void InvariantLengthAsArgOfUserCall_HoistedInner()
    {
        // foo(len(s)) -- the INNER length call is hoistable, the outer USER
        // call is not; the outer call's argument is rewritten to read the
        // hoisted temp (evaluation-order preserving).
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            new CallExpr { Function = "call_999", Args = { StrLen("loc2") } });
        var result = LicmPass.Hoist(DoWhileShapeLoop(body));

        Assert.Equal(1, result.Hoisted);
        var bodyStmt = Assert.IsType<AssignStmt>(result.Stmts[3]);
        var outer = Assert.IsType<CallExpr>(Assert.IsType<BinaryExpr>(bodyStmt.Value).Right);
        Assert.Equal("licm0", Assert.IsType<LocExpr>(outer.Args[0]).Name);
    }

    [Fact]
    public void GetLengthWithInvariantDimension_Hoisted()
    {
        // Array.GetLength(int) (icall_array_get_length) with an invariant
        // dimension argument. Faulting (receiver + dimension validation) but
        // the do-while body is guaranteed to execute, so it is hoistable.
        var body = Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            GetLen("loc2", I(0)));
        var result = LicmPass.Hoist(DoWhileShapeLoop(body));

        Assert.Equal(1, result.Hoisted);
        var hoisted = Assert.IsType<AssignStmt>(result.Stmts[1]);
        Assert.Equal("il2cpp::runtime::icall_array_get_length", Assert.IsType<CallExpr>(hoisted.Value).Resolved?.CppName);
    }

    [Fact]
    public void GetLengthWithVariantDimension_NotHoisted()
    {
        // Array.GetLength(int) with a LOOP-VARIANT dimension (loc0 reassigned
        // in the body): the call is loop-variant, so nothing is hoisted.
        var stmts = DoWhileShapeLoop(Bin(BinaryOp.Add, new LocExpr { Name = "loc1" },
            GetLen("loc2", new LocExpr { Name = "loc0" })));
        stmts.Insert(3, new AssignStmt { Target = "loc0", Value = I(1) });
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
    }

    [Fact]
    public void NoLoop_Unchanged()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = StrLen("loc2") },
            new AssignStmt { Target = "loc1", Value = I(0) },
        };
        var result = LicmPass.Hoist(stmts);

        Assert.Equal(0, result.Hoisted);
        Assert.Same(stmts, result.Stmts);
    }
}
