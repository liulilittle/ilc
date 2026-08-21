using Il2Cpp.Transpiler.Ir;
using Xunit;

//------------------------------------------------------------------------------
// IrNode coverage tests (IR-node completeness audit, batch 23) -- the SINGLE
// total clone/serialize dispatch (IrNode in Stmt.cs/Expr.cs) must cover EVERY
// Stmt/Expr subtype.
//
// Completeness contract:
//   * Every concrete Expr subclass in the assembly (reflection-enumerated) has
//     a factory here AND a non-throwing CloneExpr/SerializeExpr arm -- adding a
//     new node type without updating IrNode (or this inventory) FAILS the
//     suite loudly instead of silently missing a pass switch.
//   * Every concrete Stmt subclass likewise.
//   * Clone is DEEP: children are re-created (never the same instance), arg
//     lists are independent, null values are preserved, and the clone
//     serializes identically to the original.
//------------------------------------------------------------------------------
public class IrNodeCoverageTests
{
    //--------------------------------------------------------------------------
    // Factories -- one representative instance per node type.
    //--------------------------------------------------------------------------
    private static Expr Int1() => new IntConstExpr { Value = 1 };
    private static Expr Loc(string n) => new LocExpr { Name = n };
    private static Expr Call(string fn, params Expr[] args)
    {
        var c = new CallExpr { Function = fn, ReturnsRef = false };
        c.Args.AddRange(args);
        return c;
    }

    private static readonly Dictionary<string, Func<Expr>> ExprFactories = new(StringComparer.Ordinal)
    {
        [nameof(IntConstExpr)] = () => new IntConstExpr { Value = 7 },
        [nameof(Int64ConstExpr)] = () => new Int64ConstExpr { Value = 1L << 40 },
        [nameof(DoubleConstExpr)] = () => new DoubleConstExpr { Value = 1.5 },
        [nameof(StringConstExpr)] = () => new StringConstExpr { Value = "a\"b\\c\nd" },
        [nameof(NullExpr)] = () => new NullExpr { TypeName = "System.String" },
        [nameof(LocExpr)] = () => Loc("t0"),
        [nameof(ExceptionEntryExpr)] = () => new ExceptionEntryExpr(),
        [nameof(LocAddrExpr)] = () => new LocAddrExpr { Name = "t1" },
        [nameof(StaticFieldExpr)] = () => new StaticFieldExpr { FieldName = "Ctr.Counter" },
        [nameof(StaticFieldAddrExpr)] = () => new StaticFieldAddrExpr { FieldName = "Ctr.Counter" },
        [nameof(BinaryExpr)] = () => new BinaryExpr { Op = BinaryOp.Add, Left = Loc("a"), Right = Int1(), Kind = null },
        [nameof(UnaryExpr)] = () => new UnaryExpr { Op = UnaryOp.Neg, Operand = Loc("a") },
        [nameof(CallExpr)] = () => Call("il2cpp::f", Loc("a"), Int1()),
        [nameof(MarshalUtf8Expr)] = () => new MarshalUtf8Expr { Arg = Loc("s") },
        [nameof(VirtualCallExpr)] = () =>
        {
            var v = new VirtualCallExpr { MethodToken = 0x06000001, ReturnsRef = true };
            v.Args.Add(Loc("o"));
            v.Args.Add(Int1());
            return v;
        },
        [nameof(NewObjExpr)] = () => new NewObjExpr { TypeFullName = "Calc.Point" },
        [nameof(ValueTypeNewExpr)] = () => new ValueTypeNewExpr { TypeFullName = "Calc.Point", CtorToken = 0x06000002 },
        [nameof(FuncPtrExpr)] = () => new FuncPtrExpr { MethodToken = 0x06000003 },
        [nameof(DelegateInvokeExpr)] = () =>
        {
            var d = new DelegateInvokeExpr { MethodToken = 0x06000004, ReturnsRef = false };
            d.Args.Add(Loc("d"));
            d.Args.Add(Int1());
            return d;
        },
        [nameof(DelegateNewExpr)] = () => new DelegateNewExpr { MethodToken = 0x06000005, Target = Loc("t") },
        [nameof(FieldExpr)] = () => new FieldExpr
        {
            Object = Loc("o"),
            FieldCppName = "X",
            OwnerFullName = "Calc.Point",
            IsRefField = true,
            IsValueTypeObject = false,
        },
        [nameof(FieldAddrExpr)] = () => new FieldAddrExpr { Object = Loc("o"), FieldCppName = "X", OwnerFullName = "Calc.Point" },
        [nameof(StackFold.DiscardedValueExpr)] = () => new StackFold.DiscardedValueExpr { CallToken = 0x06000006, Offset = 0x10 },
    };

    private static readonly Dictionary<string, Func<Stmt>> StmtFactories = new(StringComparer.Ordinal)
    {
        [nameof(LabelStmt)] = () => new LabelStmt { Name = "L_4" },
        [nameof(AssignStmt)] = () => new AssignStmt { Target = "t0", Value = Loc("x") },
        [nameof(ExprStmt)] = () => new ExprStmt { Value = Call("il2cpp::f", Loc("x")) },
        [nameof(BranchStmt)] = () => new BranchStmt { TargetLabel = "L_4", Condition = Loc("c") },
        [nameof(RetStmt)] = () => new RetStmt { Value = Loc("x") },
        [nameof(CallStmt)] = () =>
        {
            var c = new CallStmt { Function = "call_0x06000007", Resolved = null };
            c.Args.Add(Loc("a"));
            return c;
        },
        [nameof(FieldAssignStmt)] = () => new FieldAssignStmt
        {
            Object = Loc("o"),
            FieldCppName = "X",
            OwnerFullName = "Calc.Point",
            IsRefField = true,
            IsValueTypeObject = false,
            RefTargetFullName = "Calc.Point",
            Value = Int1(),
        },
    };

    //--------------------------------------------------------------------------
    // Completeness: every concrete Expr/Stmt subclass in the assembly is
    // inventoried AND serializable/cloneable.
    //--------------------------------------------------------------------------

    [Fact]
    public void EveryExprSubclass_HasFactory_CloneAndSerializeWork()
    {
        var exprTypes = typeof(Expr).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Expr)) && !t.IsGenericTypeDefinition)
            .ToList();
        Assert.NotEmpty(exprTypes);

        foreach (var t in exprTypes)
        {
            Assert.True(ExprFactories.ContainsKey(t.Name),
                $"no Expr factory for {t.Name} (IR-node completeness audit)");
            var e = (Expr)ExprFactories[t.Name]();

            var text = IrNode.SerializeExpr(e);
            Assert.False(string.IsNullOrWhiteSpace(text), $"SerializeExpr({t.Name}) returned empty");

            var clone = IrNode.CloneExpr(e);
            Assert.NotSame(e, clone);
            Assert.Equal(text, IrNode.SerializeExpr(clone));
        }
    }

    [Fact]
    public void EveryStmtSubclass_HasFactory_CloneAndSerializeWork()
    {
        var stmtTypes = typeof(Stmt).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Stmt)) && !t.IsGenericTypeDefinition)
            .ToList();
        Assert.NotEmpty(stmtTypes);

        foreach (var t in stmtTypes)
        {
            Assert.True(StmtFactories.ContainsKey(t.Name),
                $"no Stmt factory for {t.Name} (IR-node completeness audit)");
            var s = (Stmt)StmtFactories[t.Name]();

            var text = IrNode.SerializeStmt(s);
            Assert.False(string.IsNullOrWhiteSpace(text), $"SerializeStmt({t.Name}) returned empty");

            var clone = IrNode.CloneStmt(s);
            Assert.NotSame(s, clone);
            Assert.Equal(text, IrNode.SerializeStmt(clone));
        }
    }

    //--------------------------------------------------------------------------
    // Representative serialization shapes (pin the format).
    //--------------------------------------------------------------------------

    [Fact]
    public void Serialize_PinsRepresentativeShapes()
    {
        Assert.Equal("Int(7)", IrNode.SerializeExpr(new IntConstExpr { Value = 7 }));
        Assert.Equal("Int(5, i64)", IrNode.SerializeExpr(new IntConstExpr { Value = 5, IsInt64 = true }));
        Assert.Equal("Int64(1099511627776)", IrNode.SerializeExpr(new Int64ConstExpr { Value = 1L << 40 }));
        Assert.Equal("Loc(t0)", IrNode.SerializeExpr(Loc("t0")));
        Assert.Equal("Null(System.String)", IrNode.SerializeExpr(new NullExpr { TypeName = "System.String" }));
        Assert.Equal("(Loc(a) + Int(1))", IrNode.SerializeExpr(new BinaryExpr { Op = BinaryOp.Add, Left = Loc("a"), Right = Int1() }));
        Assert.Equal("(Loc(a) + Int(1))[l]", IrNode.SerializeExpr(new BinaryExpr { Op = BinaryOp.Add, Left = Loc("a"), Right = Int1(), Kind = "l" }));
        Assert.Equal("(-Loc(a))", IrNode.SerializeExpr(new UnaryExpr { Op = UnaryOp.Neg, Operand = Loc("a") }));
        Assert.Equal("Str(\"a\\\"b\\\\c\\nd\")", IrNode.SerializeExpr(new StringConstExpr { Value = "a\"b\\c\nd" }));
        Assert.Equal("Call(il2cpp::f, Loc(a), Int(1))", IrNode.SerializeExpr(Call("il2cpp::f", Loc("a"), Int1())));

        Assert.Equal("Label(L_4)", IrNode.SerializeStmt(new LabelStmt { Name = "L_4" }));
        Assert.Equal("t0 = Loc(x)", IrNode.SerializeStmt(new AssignStmt { Target = "t0", Value = Loc("x") }));
        Assert.Equal("return Loc(x);", IrNode.SerializeStmt(new RetStmt { Value = Loc("x") }));
        Assert.Equal("return;", IrNode.SerializeStmt(new RetStmt()));
        Assert.Equal("goto L_4;", IrNode.SerializeStmt(new BranchStmt { TargetLabel = "L_4" }));
        Assert.Equal("if (Loc(c)) goto L_4;", IrNode.SerializeStmt(new BranchStmt { TargetLabel = "L_4", Condition = Loc("c") }));
    }

    //--------------------------------------------------------------------------
    // Deep-copy semantics: children re-created, lists independent, null kept.
    //--------------------------------------------------------------------------

    [Fact]
    public void Clone_DeepCopiesChildren()
    {
        var b = new BinaryExpr { Op = BinaryOp.Mul, Left = Loc("x"), Right = Int1() };
        var e = new AssignStmt { Target = "t0", Value = b };

        var clone = Assert.IsType<AssignStmt>(IrNode.CloneStmt(e));
        Assert.NotSame(e, clone);
        var cb = Assert.IsType<BinaryExpr>(clone.Value);
        Assert.NotSame(b, cb);
        Assert.NotSame(b.Left, cb.Left);
        Assert.NotSame(b.Right, cb.Right);
        Assert.Equal(BinaryOp.Mul, cb.Op);
        Assert.Equal("t0", clone.Target);
    }

    [Fact]
    public void Clone_CallExpr_ArgsListIsIndependent()
    {
        var c = (CallExpr)Call("il2cpp::f", Loc("a"), Int1());
        var clone = Assert.IsType<CallExpr>(IrNode.CloneExpr(c));
        Assert.NotSame(c.Args, clone.Args);
        Assert.Equal(c.Args.Count, clone.Args.Count);
        for (var i = 0; i < c.Args.Count; i++)
            Assert.NotSame(c.Args[i], clone.Args[i]);

        clone.Args[0] = new LocExpr { Name = "mutated" };
        Assert.Equal("Call(il2cpp::f, Loc(a), Int(1))", IrNode.SerializeExpr(c));
    }

    [Fact]
    public void Clone_StringArgIndices_IsIndependent()
    {
        var c = new CallExpr { Function = "il2cpp::p", IsPInvoke = true };
        c.Args.Add(new LocExpr { Name = "s" });
        c.StringArgIndices.Add(0);
        c.StringArgIndices.Add(1);

        var clone = Assert.IsType<CallExpr>(IrNode.CloneExpr(c));
        Assert.NotSame(c.StringArgIndices, clone.StringArgIndices);
        Assert.Equal(c.StringArgIndices, clone.StringArgIndices);

        clone.StringArgIndices.Add(99);
        Assert.Equal(2, c.StringArgIndices.Count);
        Assert.Contains("#str(0,1)", IrNode.SerializeExpr(c));
    }

    [Fact]
    public void Clone_PreservesNullConditionAndNullRetValue()
    {
        var b = new BranchStmt { TargetLabel = "L_4", Condition = null };
        var cb = Assert.IsType<BranchStmt>(IrNode.CloneStmt(b));
        Assert.Null(cb.Condition);
        Assert.Equal("L_4", cb.TargetLabel);

        var r = new RetStmt { Value = null };
        var cr = Assert.IsType<RetStmt>(IrNode.CloneStmt(r));
        Assert.Null(cr.Value);
        Assert.Equal("return;", IrNode.SerializeStmt(cr));
    }

    [Fact]
    public void Clone_DelegateNew_TargetDeepCopied()
    {
        var dn = new DelegateNewExpr { MethodToken = 0x06000005, Target = Loc("t0") };
        var clone = Assert.IsType<DelegateNewExpr>(IrNode.CloneExpr(dn));
        Assert.NotSame(dn.Target, clone.Target);
        Assert.Equal(0x06000005, clone.MethodToken);
    }

    [Fact]
    public void Clone_FieldAssign_AllFieldsPreserved()
    {
        var f = new FieldAssignStmt
        {
            Object = Loc("o"),
            FieldCppName = "F",
            OwnerFullName = "T",
            IsRefField = true,
            IsValueTypeObject = true,
            RefTargetFullName = "T",
            Value = Int1(),
        };
        var clone = Assert.IsType<FieldAssignStmt>(IrNode.CloneStmt(f));
        Assert.NotSame(f.Object, clone.Object);
        Assert.NotSame(f.Value, clone.Value);
        Assert.Equal("F", clone.FieldCppName);
        Assert.Equal("T", clone.OwnerFullName);
        Assert.True(clone.IsRefField);
        Assert.True(clone.IsValueTypeObject);
        Assert.Equal("T", clone.RefTargetFullName);
    }

    [Fact]
    public void CloneStmts_PreservesOrderAndLength()
    {
        var stmts = new List<Stmt>
        {
            new LabelStmt { Name = "L_4" },
            new AssignStmt { Target = "t0", Value = Loc("x") },
            new RetStmt { Value = Loc("t0") },
        };
        var clones = IrNode.CloneStmts(stmts);
        Assert.Equal(stmts.Count, clones.Count);
        for (var i = 0; i < stmts.Count; i++)
        {
            Assert.NotSame(stmts[i], clones[i]);
            Assert.Equal(IrNode.SerializeStmt(stmts[i]), IrNode.SerializeStmt(clones[i]));
        }
    }
}
