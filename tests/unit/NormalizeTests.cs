using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using System.Linq;
using System.Reflection.Metadata;
using Xunit;

//------------------------------------------------------------------------------
// Normalize pass (P0-1 algebraic identities + P0-3 null-comparison folding).
// Every rule must preserve .NET int32 unchecked-wrap semantics; rules that
// REMOVE an evaluation (x*0, x&0, x-x, x==x ...) are restricted to pure
// arithmetic subtrees (LocExpr/IntConstExpr only, no Div/Rem/FieldExpr --
// those can throw DivideByZeroException / NullReferenceException, and
// removing the evaluation would remove the exception).
//------------------------------------------------------------------------------
public class NormalizeTests
{
    private static Expr FoldRet(Expr value) =>
        ((RetStmt)Normalize.FoldStmts(new List<Stmt> { new RetStmt { Value = value } })[0]).Value!;

    private static Expr Loc(string name) => new LocExpr { Name = name };
    private static Expr C(long v) => new IntConstExpr { Value = v };
    private static Expr Bin(BinaryOp op, Expr l, Expr r) => new BinaryExpr { Op = op, Left = l, Right = r };
    private static Expr Neg(Expr e) => new UnaryExpr { Op = UnaryOp.Neg, Operand = e };
    private static Expr Field() => new FieldExpr
    {
        Object = Loc("obj"),
        FieldCppName = "F",
        OwnerFullName = "Calc.Point",
        IsRefField = false,
    };

    // -- neutral-element identities (evaluation-preserving: safe for ANY expr) --
    [Fact]
    public void Add_Zero_Right_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Add, x, C(0))));
    }
    [Fact]
    public void Add_Zero_Left_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Add, C(0), x)));
    }
    [Fact]
    public void Sub_Zero_Right_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Sub, x, C(0))));
    }
    [Fact]
    public void Sub_FromZero_FoldsToNeg()
    {
        var x = Loc("x");
        var r = Assert.IsType<UnaryExpr>(FoldRet(Bin(BinaryOp.Sub, C(0), x)));
        Assert.Equal(UnaryOp.Neg, r.Op);
        Assert.Same(x, r.Operand);
    }
    [Fact]
    public void Mul_One_Right_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Mul, x, C(1))));
    }
    [Fact]
    public void Mul_One_Left_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Mul, C(1), x)));
    }
    [Fact]
    public void Xor_Zero_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Xor, x, C(0))));
    }
    [Fact]
    public void Or_Zero_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Or, x, C(0))));
    }
    [Fact]
    public void And_MinusOne_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.And, x, C(-1))));
    }
    [Fact]
    public void Shl_Zero_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Shl, x, C(0))));
    }
    [Fact]
    public void Shr_Zero_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Bin(BinaryOp.Shr, x, C(0))));
    }

    // -- evaluation-removing identities (pure operands only) --
    [Fact] public void Mul_Zero_Pure_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Mul, Loc("x"), C(0)))).Value);
    [Fact] public void And_Zero_Pure_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.And, Loc("x"), C(0)))).Value);
    [Fact] public void Sub_Self_SameInstance_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Sub, Loc("x"), Loc("x")))).Value);

    // -- safety: never remove an evaluation that can throw --
    [Fact]
    public void Mul_Zero_FieldExpr_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Field(), C(0))));
        Assert.Equal(BinaryOp.Mul, r.Op);
    }
    [Fact]
    public void Mul_Zero_DivSubtree_NotFolded()
    {
        // (a/b)*0 must NOT fold: the division can throw DivideByZeroException.
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Bin(BinaryOp.Div, Loc("a"), Loc("b")), C(0))));
        Assert.Equal(BinaryOp.Mul, r.Op);
    }
    [Fact]
    public void Sub_Self_FieldExpr_NotFolded()
    {
        // dup'd field read: (obj.F - obj.F) must NOT fold (NRE must be preserved).
        var f = Field();
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Sub, f, f)));
        Assert.Equal(BinaryOp.Sub, r.Op);
    }
    [Fact]
    public void Div_ByOne_NotFolded()
    {
        // x/1 stays a division (int.MinValue / -1 wrap semantics; the runtime
        // checked_div is kept -- no division identities in the safe subset).
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, Loc("x"), C(1))));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void Div_IntMin_ByMinusOne_NotFolded()
    {
        // int.MinValue / -1 is an OverflowException in .NET (the div
        // instruction checks this pair in checked AND unchecked context) --
        // folding it to the wrapped constant -2147483648 would silently drop
        // the exception. The runtime il2cpp_checked_div re-throws it.
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, C(int.MinValue), C(-1))));
        Assert.Equal(BinaryOp.Div, r.Op);
        Assert.Equal(int.MinValue, Assert.IsType<IntConstExpr>(r.Left).Value);
        Assert.Equal(-1L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void Div_IntMax_ByMinusOne_FoldsToConstant()
    {
        // int.MaxValue / -1 = -2147483647 fits in int32 -- folding is safe.
        Assert.Equal(-int.MaxValue, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Div, C(int.MaxValue), C(-1)))).Value);
    }
    [Fact]
    public void Div_IntMin_ByOne_FoldsToConstant()
    {
        // int.MinValue / 1 = int.MinValue -- no overflow, folding is safe.
        Assert.Equal(int.MinValue, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Div, C(int.MinValue), C(1)))).Value);
    }
    [Fact]
    public void Div_IntMin_ByIntMin_FoldsToConstant()
    {
        // int.MinValue / int.MinValue = 1 -- no overflow, folding is safe.
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Div, C(int.MinValue), C(int.MinValue)))).Value);
    }
    [Fact]
    public void Rem_IntMin_ByMinusOne_FoldsToZero()
    {
        // int.MinValue % -1 = 0 -- remainder never overflows (.NET), folding is safe.
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Rem, C(int.MinValue), C(-1)))).Value);
    }

    // -- unary identities --
    [Fact]
    public void Neg_Neg_FoldsToOperand()
    {
        var x = Loc("x");
        Assert.Same(x, FoldRet(Neg(Neg(x))));
    }
    [Fact]
    public void Not_Not_FoldsToOperand()
    {
        var x = Loc("x");
        var r = FoldRet(new UnaryExpr { Op = UnaryOp.Not, Operand = new UnaryExpr { Op = UnaryOp.Not, Operand = x } });
        Assert.Same(x, r);
    }

    // -- constant merging --
    [Fact] public void BothConstants_Fold() => Assert.Equal(12L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Add, C(5), C(7)))).Value);
    [Fact]
    public void ConstantMerge_Add()
    {
        // (x+1)+2 -> x+3
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Add, Bin(BinaryOp.Add, x, C(1)), C(2))));
        Assert.Equal(BinaryOp.Add, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void ConstantMerge_Mul()
    {
        // (x*2)*3 -> x*6
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Bin(BinaryOp.Mul, x, C(2)), C(3))));
        Assert.Equal(BinaryOp.Mul, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(6L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void ConstantMerge_WrapSemantics()
    {
        // (x + 0x7FFFFFFF) + 1 -> x + 0x80000000 (int32 wrap, not int64 math)
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Add, Bin(BinaryOp.Add, x, C(int.MaxValue)), C(1))));
        Assert.Equal(BinaryOp.Add, r.Op);
        Assert.Equal(unchecked((int)(int.MaxValue + 1L)), Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void ConstantRightNormalization_EnablesMerge()
    {
        // (2+x)+3 -> x+5 (inner constant flips right, then same-op merge)
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Add, Bin(BinaryOp.Add, C(2), x), C(3))));
        Assert.Equal(BinaryOp.Add, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }

    // -- conditional identities (structural equality, pure operands only) --
    [Fact] public void Ceq_Self_FoldsToOne() => Assert.Equal(1L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Ceq, Loc("x"), Loc("x")))).Value);
    [Fact] public void Cgt_Self_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Cgt, Loc("x"), Loc("x")))).Value);
    [Fact] public void Clt_Self_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Clt, Loc("x"), Loc("x")))).Value);
    [Fact] public void CgtUn_Self_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.CgtUn, Loc("x"), Loc("x")))).Value);
    [Fact]
    public void Bne_Self_FoldsToZero()
    {
        // x != x  ->  Not(Ceq(x,x)) = Ceq(Ceq(x,x), 0) -> Ceq(1,0) -> 0
        var r = FoldRet(Bin(BinaryOp.Ceq, Bin(BinaryOp.Ceq, Loc("x"), Loc("x")), C(0)));
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(r).Value);
    }
    [Fact]
    public void Ceq_Self_FieldExpr_NotFolded()
    {
        var f = Field();
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Ceq, f, f)));
        Assert.Equal(BinaryOp.Ceq, r.Op);
    }

    // -- null comparison folding --
    [Fact] public void Null_Null_Ceq_FoldsToOne() => Assert.Equal(1L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.Ceq, new NullExpr(), new NullExpr()))).Value);
    [Fact] public void Null_Null_CgtUn_FoldsToZero() => Assert.Equal(0L, Assert.IsType<IntConstExpr>(FoldRet(Bin(BinaryOp.CgtUn, new NullExpr(), new NullExpr()))).Value);

    // known-non-null set (straight-line, cleared at labels/handlers):
    [Fact]
    public void KnownNonNull_NewObj_CeqNull_FoldsToZero()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" } },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[1];
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }
    [Fact]
    public void KnownNonNull_NewObj_CgtNull_FoldsToOne()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" } },
            new RetStmt { Value = Bin(BinaryOp.CgtUn, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[1];
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }
    [Fact]
    public void KnownNonNull_CopyChain_Propagates()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" } },
            new AssignStmt { Target = "t1", Value = Loc("t0") },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t1"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[2];
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }
    [Fact]
    public void KnownNonNull_Label_Clears()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" } },
            new LabelStmt { Name = "L_5" },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[2];
        var r = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Ceq, r.Op);
    }
    [Fact]
    public void KnownNonNull_Reassignment_Invalidates()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" } },
            new AssignStmt { Target = "t0", Value = Loc("arg0") },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[2];
        var r = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Ceq, r.Op);
    }
    [Fact]
    public void KnownNonNull_HandlerStart_Clears()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new NewObjExpr { TypeFullName = "Calc.Point" }, SourceOffset = 0 },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()), SourceOffset = 10 },
        };
        var regions = new List<IlExceptionRegion>
        {
            new() { TryOffset = 0, TryLength = 6, HandlerOffset = 10, HandlerLength = 4, Kind = ExceptionRegionKind.Catch },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts, regions)[1];
        var r = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Ceq, r.Op);
    }
    [Fact]
    public void KnownNonNull_StringConst_IsNonNull()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new StringConstExpr { Value = "hi" } },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[1];
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }
    [Fact]
    public void UnknownValue_CeqNull_NotFolded()
    {
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "t0", Value = new CallExpr { Function = "call_1", ReturnsRef = true } },
            new RetStmt { Value = Bin(BinaryOp.Ceq, Loc("t0"), new NullExpr()) },
        };
        var ret = (RetStmt)Normalize.FoldStmts(stmts)[1];
        var r = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Ceq, r.Op);
    }

    // -- branch conditions become constants (feeds the existing dead-branch machinery) --
    [Fact]
    public void BranchCondition_BecomesConstant()
    {
        var stmts = new List<Stmt>
        {
            new BranchStmt { TargetLabel = "L_9", Condition = Bin(BinaryOp.Ceq, Loc("x"), Loc("x")) },
        };
        var b = (BranchStmt)Normalize.FoldStmts(stmts)[0];
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(b.Condition).Value);
    }

    // -- call arguments are rewritten (callers benefit from folded args) --
    [Fact]
    public void CallArguments_AreRewritten()
    {
        var x = Loc("x");
        var stmts = new List<Stmt>
        {
            new CallStmt { Function = "icall_console_write_line_i", Args = { Bin(BinaryOp.Add, x, C(0)) } },
        };
        var c = (CallStmt)Normalize.FoldStmts(stmts)[0];
        Assert.Same(x, c.Args[0]);
    }

    //--------------------------------------------------------------------------
    // Constant strength reduction (P0-2): x*2^k -> x<<k ; x/2^k (signed) ->
    // rounding-corrected arithmetic shift ; x/2^k (uint) -> logical shift.
    // SAFE SUBSET: only positive power-of-2 constants; divisor 1 stays a
    // division (Div_ByOne_NotFolded); negative / non-power-of-2 constants and
    // impure dividends (FieldExpr/Div subtree -- evaluation duplication would
    // double an observable exception) are never touched.
    //--------------------------------------------------------------------------
    private static long Wrap32(long v) => unchecked((int)v);

    // int32 unchecked interpreter for the IR subset produced by the rules.
    private static long Eval(Expr e, long x)
    {
        switch (e)
        {
            case IntConstExpr c:
                return c.Value;
            case LocExpr:
                return x;
            case BinaryExpr b:
            {
                var l = Eval(b.Left, x);
                var r = Eval(b.Right, x);
                switch (b.Op)
                {
                    case BinaryOp.Add: return Wrap32(l + r);
                    case BinaryOp.Sub: return Wrap32(l - r);
                    case BinaryOp.Mul: return Wrap32(l * r);
                    case BinaryOp.Div: return Wrap32(l / r); // r is a positive power of 2 here
                    case BinaryOp.DivUn: return Wrap32((uint)l / (uint)r);
                    case BinaryOp.And: return l & r;
                    case BinaryOp.Or: return l | r;
                    case BinaryOp.Xor: return l ^ r;
                    case BinaryOp.Shl: return Wrap32(l << (int)(r & 31));
                    case BinaryOp.Shr: return Wrap32(l >> (int)(r & 31));
                    case BinaryOp.ShrUn: return Wrap32((uint)l >> (int)(r & 31));
                    default: throw new NotSupportedException($"eval {b.Op}");
                }
            }
            default:
                throw new NotSupportedException($"eval {e.GetType().Name}");
        }
    }

    [Fact]
    public void Mul_ByPowerOfTwo_FoldsToShift()
    {
        // x*4 -> x<<2
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, x, C(4))));
        Assert.Equal(BinaryOp.Shl, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void Mul_ByTwo_FoldsToShiftOne()
    {
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, x, C(2))));
        Assert.Equal(BinaryOp.Shl, r.Op);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void Mul_ByPowerOfTwo_AfterConstantMerge_FoldsToSingleShift()
    {
        // (x*2)*4: constant merge first (x*8), then strength (x<<3).
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Bin(BinaryOp.Mul, x, C(2)), C(4))));
        Assert.Equal(BinaryOp.Shl, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void Mul_ByNonPowerOfTwo_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Loc("x"), C(3))));
        Assert.Equal(BinaryOp.Mul, r.Op);
    }
    [Fact]
    public void Mul_ByNegativePowerOfTwo_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Loc("x"), C(-8))));
        Assert.Equal(BinaryOp.Mul, r.Op);
    }
    [Fact]
    public void Mul_ByPowerOfTwo_Int64Constant_NotFolded()
    {
        // int64 multiplier (ldc.i8): the int32 shift would be wrong -- stays Mul.
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Mul, Loc("x"), new IntConstExpr { Value = 8, IsInt64 = true })));
        Assert.Equal(BinaryOp.Mul, r.Op);
    }

    [Fact]
    public void Div_ByPowerOfTwo_Signed_FoldsToRoundedShift()
    {
        // x/4 -> (x + ((x>>31) & 3)) >> 2  (rounding correction for negatives)
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, x, C(4))));
        Assert.Equal(BinaryOp.Shr, r.Op);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(r.Right).Value);
        var add = Assert.IsType<BinaryExpr>(r.Left);
        Assert.Equal(BinaryOp.Add, add.Op);
        Assert.Same(x, add.Left);
        var mask = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal(BinaryOp.And, mask.Op);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(mask.Right).Value);
        var sign = Assert.IsType<BinaryExpr>(mask.Left);
        Assert.Equal(BinaryOp.Shr, sign.Op);
        Assert.Same(x, sign.Left);
        Assert.Equal(31L, Assert.IsType<IntConstExpr>(sign.Right).Value);
    }
    [Fact]
    public void Div_ByNegativePowerOfTwo_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, Loc("x"), C(-8))));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void Div_ByNonPowerOfTwo_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, Loc("x"), C(3))));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void Div_ByPowerOfTwo_Int64Kind_NotFolded()
    {
        // int64 division (Kind "l"): the int32 correction formula is wrong -- stays Div.
        var r = Assert.IsType<BinaryExpr>(FoldRet(new BinaryExpr { Op = BinaryOp.Div, Left = Loc("x"), Right = C(8), Kind = "l" }));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void Div_ByPowerOfTwo_DivSubtree_NotFolded()
    {
        // (a/b)/4 must NOT fold: the dividend is duplicated in the corrected
        // shift (a/b appears twice) -- a DivideByZeroException would fire
        // twice instead of once.
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, Bin(BinaryOp.Div, Loc("a"), Loc("b")), C(4))));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void Div_ByPowerOfTwo_FieldExpr_NotFolded()
    {
        // (obj.F)/4 must NOT fold: duplicated field read could throw NRE twice.
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Div, Field(), C(4))));
        Assert.Equal(BinaryOp.Div, r.Op);
    }
    [Fact]
    public void DivUn_ByPowerOfTwo_FoldsToLogicalShift()
    {
        // x/4 (uint) -> x>>2 (logical shift, ShrUn)
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.DivUn, x, C(4))));
        Assert.Equal(BinaryOp.ShrUn, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void DivUn_ByNonPowerOfTwo_NotFolded()
    {
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.DivUn, Loc("x"), C(3))));
        Assert.Equal(BinaryOp.DivUn, r.Op);
    }

    // -- shift-count merging: (x<<a)<<b -> x<<((a+b)&31) (strength output chains) --
    [Fact]
    public void Shl_NestedConstantCounts_Merge()
    {
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Shl, Bin(BinaryOp.Shl, x, C(1)), C(2))));
        Assert.Equal(BinaryOp.Shl, r.Op);
        Assert.Same(x, r.Left);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void Shr_NestedConstantCounts_Merge()
    {
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Shr, Bin(BinaryOp.Shr, x, C(3)), C(2))));
        Assert.Equal(BinaryOp.Shr, r.Op);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }
    [Fact]
    public void ShiftMerge_CountWrap_Masks31()
    {
        // (x<<30)<<4 -> x<<2 (34 masked to 5 bits, .NET shift semantics)
        var x = Loc("x");
        var r = Assert.IsType<BinaryExpr>(FoldRet(Bin(BinaryOp.Shl, Bin(BinaryOp.Shl, x, C(30)), C(4))));
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(r.Right).Value);
    }

    // -- semantic equivalence: folded IR must evaluate identically to the
    //    .NET int32 operation on negative/boundary values --
    private static readonly long[] BoundaryValues =
    {
        int.MinValue, int.MinValue + 1, -(1L << 30) - 1, -5, -4, -3, -2, -1, 0, 1, 2, 3, 4, 5, int.MaxValue - 1, int.MaxValue,
    };
    private static readonly long[] PowerOfTwoDivisors = { 2, 4, 8, 16, 1024, 1L << 30 };

    [Fact]
    public void Div_ByPowerOfTwo_EquivalenceOnBoundaryValues()
    {
        foreach (var d in PowerOfTwoDivisors)
        foreach (var x in BoundaryValues)
        {
            var orig = Bin(BinaryOp.Div, Loc("x"), C(d));
            var folded = FoldRet(orig);
            Assert.NotEqual(BinaryOp.Div, Assert.IsType<BinaryExpr>(folded).Op);
            Assert.Equal(Eval(orig, x), Eval(folded, x));
        }
    }
    [Fact]
    public void Mul_ByPowerOfTwo_EquivalenceOnBoundaryValues()
    {
        foreach (var m in PowerOfTwoDivisors)
        foreach (var x in BoundaryValues)
        {
            var orig = Bin(BinaryOp.Mul, Loc("x"), C(m));
            var folded = FoldRet(orig);
            Assert.NotEqual(BinaryOp.Mul, Assert.IsType<BinaryExpr>(folded).Op);
            Assert.Equal(Eval(orig, x), Eval(folded, x));
        }
    }
    [Fact]
    public void DivUn_ByPowerOfTwo_EquivalenceOnBoundaryValues()
    {
        // uint bit patterns (stored int32-wrapped): 0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF
        long[] uxs = { 0, 1, int.MaxValue, unchecked((int)0x80000000), -2, -1 };
        foreach (var d in PowerOfTwoDivisors)
        foreach (var x in uxs)
        {
            var orig = Bin(BinaryOp.DivUn, Loc("x"), C(d));
            var folded = FoldRet(orig);
            Assert.NotEqual(BinaryOp.DivUn, Assert.IsType<BinaryExpr>(folded).Op);
            Assert.Equal(Eval(orig, x), Eval(folded, x));
        }
    }
}
