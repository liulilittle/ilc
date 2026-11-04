using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

public class StackFoldTests
{
    private static List<IlInstruction> DecodeAdd()
    {
        // Reads Calc.Program.Add's IL from calc.dll (simplified: fixed byte
        // sequence 02 03 58 2A)
        return IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x58, 0x2A });
    }

    [Fact]
    public void Fold_Add_ProducesReturnBinary()
    {
        var stmts = StackFold.Fold(DecodeAdd(), new MethodContext { ArgCount = 2 });
        // The final statement should be a RetStmt of return arg0 + arg1 containing
        // BinaryExpr(Add, Loc(arg0), Loc(arg1))
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(bin.Right).Name);
    }

    [Fact]
    public void Fold_ShortFormBranch_ProducesBranchStmtWithCondition()
    {
        // ldc.i4.0; brfalse.s +2; ldc.i4.1; ret → 0x16 0x2C 0x02 0x17 0x2A
        // brfalse_s@1, target = 1+2+2 = 5 → BranchStmt(L_5, Condition). Before the
        // fix Brfalse_s had no case → NotSupportedException.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x16, 0x2C, 0x02, 0x17, 0x2A });
        var br = insns.Single(i => i.OpCode == ILOpCode.Brfalse_s);
        Assert.Equal(1, br.Offset);
        Assert.Equal(5, br.Operand);

        var stmts = StackFold.Fold(insns, new MethodContext { ArgCount = 0 });
        var branch = Assert.IsType<BranchStmt>(stmts[0]);
        Assert.Equal("L_5", branch.TargetLabel);
        var cond = Assert.IsType<BinaryExpr>(branch.Condition);
        Assert.Equal(BinaryOp.Ceq, cond.Op); // Not(IntConst 0) = Ceq(IntConst 0, IntConst 0)
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(cond.Left).Value);
    }

    [Fact]
    public void Fold_Bge_ProducesNotClt()
    {
        // ldarg.0; ldarg.1; bge <target=0>; ret → 0x02 0x03 0x3C 00 00 00 00 2A
        // bge@2 target = 2+5+0 = 7. Bge is "signed ≥" ≡ !(left < right) =
        // Not(Clt(arg0, arg1)). Before the fix it mapped to CgtUn (unsigned >),
        // so the shape assertion failed (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 2 });

        var branch = Assert.IsType<BranchStmt>(stmts[0]);
        var cond = Assert.IsType<BinaryExpr>(branch.Condition);
        Assert.Equal(BinaryOp.Ceq, cond.Op); // Not(...) = Ceq(..., 0)
        var inner = Assert.IsType<BinaryExpr>(cond.Left);
        Assert.Equal(BinaryOp.Clt, inner.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(inner.Left).Name);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(inner.Right).Name);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(cond.Right).Value);
    }

    [Fact]
    public void Fold_Neg_ProducesUnaryExpr()
    {
        // ldc.i4.1; neg; ret → 0x17 0x65 0x2A. Before the fix Neg had no case →
        // NotSupportedException (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x65, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var unary = Assert.IsType<UnaryExpr>(ret.Value);
        Assert.Equal(UnaryOp.Neg, unary.Op);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(unary.Operand).Value);
    }

    [Fact]
    public void Fold_Not_ProducesUnaryExpr()
    {
        // ldc.i4.0; not; ret → 0x16 0x66 0x2A. Before the fix Not had no case →
        // NotSupportedException (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x66, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var unary = Assert.IsType<UnaryExpr>(ret.Value);
        Assert.Equal(UnaryOp.Not, unary.Op);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(unary.Operand).Value);
    }

    [Fact]
    public void Fold_Dup_DuplicatesTopOfStack()
    {
        // ldarg.0; dup; sub; ret → 0x02 0x25 0x59 0x2A (arg0 subtracted from
        // itself, verifying dup duplicates the stack top).
        // Before the fix Dup had no case → NotSupportedException (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x25, 0x59, 0x2A }),
            new MethodContext { ArgCount = 1 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Sub, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Right).Name);
    }

    [Fact]
    public void Fold_Nop_EmitsNoStatements()
    {
        // ldc.i4.1; nop; nop; ret -> 0x17 0x00 0x00 0x2A. Debug/alignment
        // NOPs must fold to NOTHING -- no C++ no-op, no `;` statement, no
        // stack effect. The only emitted statement is the Ret.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_Nop_BetweenCallAndRet_EmitsNoExtraStatements()
    {
        // call <any>; nop; ret -- the NOP between contributes zero statements:
        // the single-use call inlines into the ret exactly as if the NOP were
        // absent (one RetStmt only -- no no-op stmt, no interference).
        int tok = 0x06000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x00, 0x2A,
        }), new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts)); // no stmt from the NOP
        Assert.IsType<CallExpr>(ret.Value);
        Assert.Empty(stmts.OfType<AssignStmt>());
    }

    [Fact]
    public void Fold_Dup_CallResult_SingleEvaluationSharesTemp()
    {
        // call <any>; dup; add; ret -- the call result is materialized into a
        // temp ONCE (t0 = call()); the dup duplicates the temp READ, so the
        // add consumes t0 on both sides. Single evaluation: exactly ONE CallExpr
        // in the whole fold, no redundant copy of the call.
        int tok = 0x06000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x25, 0x58, 0x2A,
        }), new MethodContext { ArgCount = 0 });

        Assert.Equal(2, stmts.Count); // t0 = call(); ret (t0 + t0)
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal("t0", assign.Target);
        Assert.IsType<CallExpr>(assign.Value);
        var ret = Assert.IsType<RetStmt>(stmts[1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
        // both sides of the add read the SAME temp -- the dup copy is a shared
        // reference to the single evaluated value, never a re-evaluation
        Assert.Equal("t0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal("t0", Assert.IsType<LocExpr>(bin.Right).Name);
        Assert.Equal(1, CountCallExprs(stmts)); // the call ran exactly once
    }

    [Fact]
    public void Fold_Dup_ReferenceResult_TwoStoresShareSameObjectRef()
    {
        // call <ref>; dup; stloc.0; stloc.1; ret -- a reference-typed call
        // result. The dup is a SHALLOW copy of the reference: both locals
        // read the same temp (same object address at runtime), the call is
        // evaluated once.
        using var pe = new PEReader(SynthesizeUserMethod(new byte[] { 0x00, 0x00, 0x0E })); // static, 0 params, STRING (ref)
        var md = pe.GetMetadataReader();
        var token = UserMethodToken(md);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x25, 0x0A, 0x0B, 0x2A,
        }), new MethodContext { ArgCount = 0, LocNames = new[] { "loc0", "loc1" } }, md, Array.Empty<TypeDef>());

        Assert.Equal(3, stmts.OfType<AssignStmt>().Count()); // t0=call, loc0=t0, loc1=t0
        var loc0 = Assert.IsType<AssignStmt>(stmts[1]);
        var loc1 = Assert.IsType<AssignStmt>(stmts[2]);
        // both stores copy the SAME temp reference (shared object identity),
        // and it is a READ of the temp -- not an address of it
        Assert.Equal("t0", Assert.IsType<LocExpr>(loc0.Value).Name);
        Assert.Equal("t0", Assert.IsType<LocExpr>(loc1.Value).Name);
        Assert.IsType<CallExpr>(Assert.IsType<AssignStmt>(stmts[0]).Value);
        Assert.Equal(1, CountCallExprs(stmts)); // reference shared, evaluated once
    }

    [Fact]
    public void Fold_Dup_ValueArg_IndependentValueReads()
    {
        // ldarg.0; dup; stloc.0; stloc.1; ldloc.0; ret -- a VALUE-typed dup:
        // each copy is an independent BY-VALUE read of arg0 (no address, no
        // aliasing). Modifying one local cannot affect the other.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x25, 0x0A, 0x0B, 0x06, 0x2A }),
            new MethodContext { ArgCount = 1, LocNames = new[] { "loc0", "loc1" } });

        var loc0 = Assert.IsType<AssignStmt>(stmts[0]);
        var loc1 = Assert.IsType<AssignStmt>(stmts[1]);
        // both copies read the SOURCE value (arg0) by value -- LocExpr reads,
        // never LocAddrExpr (an address would alias the parameter slot)
        Assert.Equal("arg0", Assert.IsType<LocExpr>(loc0.Value).Name);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(loc1.Value).Name);
        Assert.IsNotType<LocAddrExpr>(loc0.Value);
        Assert.IsNotType<LocAddrExpr>(loc1.Value);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    private static int CountCallExprs(List<Stmt> stmts)
    {
        int n = 0;
        foreach (var st in stmts)
        {
            if (st is AssignStmt a && a.Value is CallExpr) n++;
            else if (st is RetStmt r && r.Value is CallExpr) n++;
            else if (st is CallStmt) n++;
        }
        return n;
    }

    [Fact]
    public void Fold_StargS_ProducesArgAssign()
    {
        // ldarg.0; ldc.i4.1; sub; starg.s 0; ret → 0x02 0x17 0x59 0x10 0x00 0x2A
        // (Pow2's `n-- > 0` post-decrement of the parameter). Before the fix
        // Starg_s had no case → NotSupportedException (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x17, 0x59, 0x10, 0x00, 0x2A }),
            new MethodContext { ArgCount = 1 });

        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal("arg0", assign.Target);
        var bin = Assert.IsType<BinaryExpr>(assign.Value);
        Assert.Equal(BinaryOp.Sub, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(bin.Right).Value);
    }

    [Fact]
    public void Fold_BneUn_ProducesNotCeq()
    {
        // ldarg.0; ldarg.1; bne.un <target=0>; ret → 0x02 0x03 0x40 00 00 00 00 2A
        // Bne_un ≡ left != right = Not(Ceq(left, right)). Before the fix MapBinary
        // had no Bne_un mapping → NotSupportedException (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x40, 0x00, 0x00, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 2 });

        var branch = Assert.IsType<BranchStmt>(stmts[0]);
        var cond = Assert.IsType<BinaryExpr>(branch.Condition);
        Assert.Equal(BinaryOp.Ceq, cond.Op); // Not(Ceq(...)) = Ceq(Ceq, 0)
        var inner = Assert.IsType<BinaryExpr>(cond.Left);
        Assert.Equal(BinaryOp.Ceq, inner.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(inner.Left).Name);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(inner.Right).Name);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(cond.Right).Value);
    }

    [Fact]
    public void Fold_Ternary_JoinCarriedValuesUseSharedTemp()
    {
        // ldc.i4.0; brfalse.s +3(Lf@6); ldc.i4.1; br.s +1(Lend@7); Lf: ldc.i4.2; Lend: stloc.0
        // Bytes: 0x16 0x2C 0x03 0x17 0x2B 0x01 0x18 0x0A (brfalse.s@1 target=1+2+3=6;
        // br.s@4 target=4+2+1=7)
        // Before the fix: the 1 carried by br.s was swallowed by the sequential
        // stack, so the join only saw the true arm's 2 → stloc couldn't read a
        // shared temp (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x2C, 0x03, 0x17, 0x2B, 0x01, 0x18, 0x0A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        // stloc.0's value must be the shared join temp (both branch arms assign it)
        var stloc = Assert.IsType<AssignStmt>(stmts[^1]);
        Assert.Equal("loc0", stloc.Target);
        var joinVal = Assert.IsType<LocExpr>(stloc.Value);
        Assert.StartsWith("t", joinVal.Name);
        var assigns = stmts.OfType<AssignStmt>().Where(a => a.Target == joinVal.Name).ToList();
        Assert.True(assigns.Count >= 2,
            $"expected >=2 AssignStmt targeting {joinVal.Name} (one per arm), got {assigns.Count}");
    }

    [Fact]
    public void Fold_VoidTernary_RetHasNoValue()
    {
        // ldc.i4.0; brfalse.s +3(Lf@6); ldc.i4.1; br.s +1(Lend@7); Lf: ldc.i4.2; Lend: ret
        // Bytes: 0x16 0x2C 0x03 0x17 0x2B 0x01 0x18 0x2A
        // Before the fix: ret took the residual stack value at the join as its
        // return value → RetStmt.Value != null → the void method emitted
        // `return 2` (compile failure) (RED).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x2C, 0x03, 0x17, 0x2B, 0x01, 0x18, 0x2A }),
            new MethodContext { ArgCount = 0, ReturnsVoid = true });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Null(ret.Value); // void method: the residual stack value after the join must not become the return value
    }

    //--------------------------------------------------------------------------
    // Audit C3 (superseded by M5 int64 wiring) -- ldc.i8 constants outside the
    // int32 range are now CARRIED by Int64ConstExpr (the M5 int64/double chain:
    // they flow into int64 locals/params and widen in C++). The value must
    // survive folding un-truncated (previously the C3 path rejected loudly;
    // the silent-truncation hazard it guarded is now closed by the carry).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_LdcI8_OutOfRange_CarriedByInt64ConstExpr()
    {
        // ldc.i8 4294967296 (0x100000000); ret  → 0x21 00 00 00 00 01 00 00 00 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x21, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(4294967296L, Assert.IsType<Int64ConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_LdcI8_InRange_FoldsToIntConst()
    {
        // ldc.i8 5; ret → 0x21 05 00 00 00 00 00 00 00 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x21, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_LdcI8_NegativeInRange_FoldsToIntConst()
    {
        // ldc.i8 -2 (0xFFFFFFFFFFFFFFFE) → 0x21 FE FF FF FF FF FF FF FF 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x21, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(-2L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    //--------------------------------------------------------------------------
    // Audit H2 -- 'leave' folding must verify the target: real Roslyn IL ends
    // the try body AND the handler body with a leave targeting the code AFTER
    // the handler (handlerOffset + handlerLength) -- the emitted C++ shape's
    // natural continuation. Any other target (try { break; } etc.) is
    // unsupported and must be rejected loudly. batch 156c (asynccase): the
    // async state machine's MoveNext leaves from the catch handler (and the
    // AwaitUnsafeOnCompleted suspension path) to the METHOD'S FINAL ret --
    // the "return from the state machine" shape -- which is accepted.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_Leave_OutOfRegionTarget_WarnsAndSkips()
    {
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x16, 0xDE, 0x06, 0x00, 0x00, 0x2A, 0x00 });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 1, Kind = ExceptionRegionKind.Catch },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Empty(stmts);
    }

    [Fact]
    public void Fold_Leave_ToMethodTerminalRet_IsAccepted()
    {
        // batch 156c (asynccase): the async state machine's MoveNext catch
        // handler leaves to the method's FINAL ret (skipping the SetResult
        // tail -- an exception must not complete the task). ldc.i4.0 @0;
        // leave.s +2 (target 5) @1; ret @5 → leave target == the terminal
        // ret offset: accepted (no-op; the emitted C++ falls off the end of
        // the handler chain to the function's closing brace, and the
        // SetException/AwaitUnsafeOnCompleted paths are unreachable at
        // runtime in the minimal synchronous surface).
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x16, 0xDE, 0x02, 0x00, 0x00, 0x2A });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 1, Kind = ExceptionRegionKind.Catch },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Single(stmts.OfType<RetStmt>());
    }

    [Fact]
    public void Fold_Leave_ToHandlerEnd_IsNoOp()
    {
        // ldc.i4.0 @0; leave.s +4 (target 7 = handlerEnd) @1; ret @7
        // 0x16 0xDE 0x04 0x00 0x00 0x00 0x00 0x2A
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x16, 0xDE, 0x04, 0x00, 0x00, 0x00, 0x00, 0x2A });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 1, Kind = ExceptionRegionKind.Catch },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Empty(stmts.OfType<CallStmt>());
        Assert.Single(stmts.OfType<RetStmt>());
    }

    [Fact]
    public void Fold_Leave_SiblingCatchChainEnd_IsNoOp()
    {
        // Multi-catch constructs: Roslyn ends EVERY handler with a leave to
        // the CHAIN end (past the last sibling), not to its own handler end
        // (exdeep f9 catch1 leave @0x153 -> 0x162 while handler1 ends at
        // 0x155). The emitted C++ shape (sequential catch blocks) lands at
        // the chain end naturally -- both targets are valid no-ops.
        // try=[0,6) handler1=[6,11) handler2=[11,16); leave.s @6 -> target 16
        // = chain end: 0x16 0x00x5 0xDE 0x08 0x00x3 0x00x5 0x2A
        var insns = IlDecoder.DecodeMethod(new byte[]
        {
            0x16, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xDE, 0x08,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00,
            0x2A,
        });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ReturnsVoid = true,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 5, Kind = ExceptionRegionKind.Catch },
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 11, HandlerLength = 5, Kind = ExceptionRegionKind.Catch },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Single(stmts.OfType<RetStmt>());
    }

    [Fact]
    public void Fold_Leave_OutermostChainEnd_IsNoOp()
    {
        // catch-then-finally construct (exdeep CatchRet): the catch handler's
        // leave targets the OUTERMOST construct's end (the finally handler's
        // end), not the catch's own handler end. try=[0,6) catch handler
        // [6,10); finally try=[0,14) handler[10,14); leave.s @6 -> target 14:
        // 0x16 0x00x5 0xDE 0x06 0x00x7 0x2A
        var insns = IlDecoder.DecodeMethod(new byte[]
        {
            0x16, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xDE, 0x06,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2A,
        });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ReturnsVoid = true,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 4, Kind = ExceptionRegionKind.Catch },
                new() { TryOffset = 0, TryLength = 14, HandlerOffset = 10, HandlerLength = 4, Kind = ExceptionRegionKind.Finally },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Single(stmts.OfType<RetStmt>());
    }

    [Fact]
    public void Fold_Leave_LockLoweringMiddleChainEnd_IsNoOp()
    {
        // lock lowering (lockcase probe 2, nested lock): the INNER try body's
        // normal-completion leave targets the IMMEDIATELY enclosing construct's
        // chain end -- not its own handler end and not the outermost end:
        //   inner finally  try=[0,0x0C) handler=[0x0C,0x14)
        //   middle finally try=[0,0x14) handler=[0x14,0x1C)
        //   outer finally  try=[0,0x24) handler=[0x24,0x2C)
        //   leave.s @0x0A -> 0x1C = the MIDDLE lock's chain end (real IL:
        //   lockcase Main leave @0x00C1 -> 0x00DB while the inner finally ends
        //   at 0x00CF and the outermost at 0x00F6). M3 emits each finally
        //   inline after its try body, so falling through the innermost body
        //   runs exactly the enclosing finallys and lands at the target --
        //   a no-op like every other chain end on the containment chain
        //   (audit H2 chain-end extension).
        // 10x nop; leave.s 0x10; 8x nop; 8x nop; 8x nop; 8x nop; ret
        var insns = IlDecoder.DecodeMethod(new byte[]
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xDE, 0x10,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2A,
        });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            ReturnsVoid = true,
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0x00, TryLength = 0x0C, HandlerOffset = 0x0C, HandlerLength = 0x08, Kind = ExceptionRegionKind.Finally },
                new() { TryOffset = 0x00, TryLength = 0x14, HandlerOffset = 0x14, HandlerLength = 0x08, Kind = ExceptionRegionKind.Finally },
                new() { TryOffset = 0x00, TryLength = 0x24, HandlerOffset = 0x24, HandlerLength = 0x08, Kind = ExceptionRegionKind.Finally },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);
        Assert.Empty(stmts.OfType<CallStmt>());
        Assert.Single(stmts.OfType<RetStmt>());
    }

    [Fact]
    public void Fold_Rethrow_EmitsRethrowCallStmt()
    {
        // rethrow (0xFE 0x1A, bare `throw;`): the exception object is implicit (not
        // on the IL stack) — no pop, emit CallStmt{Function="rethrow"} (CodeGenerator
        // special-cases it → a bare C++ throw; inside the generated catch block,
        // preserving the original exception type). Before the fix 0xFE1A threw at
        // decode time.
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x06, 0xFE, 0x1A });
        var stmts = StackFold.Fold(insns, new MethodContext { ArgCount = 0 });

        var call = Assert.Single(stmts.OfType<CallStmt>());
        Assert.Equal("rethrow", call.Function);
    }

    [Fact]
    public void Fold_ThrowNull_EmitsNullReferenceExceptionThrow()
    {
        // `throw null` = ldnull(0x14); throw(0x7A) (exdeep f16). The .NET runtime
        // converts an empty exception object to NullReferenceException (net48
        // verified) — throw must type the null constant as
        // System.NullReferenceException, otherwise catch(NRE) does not match
        // (before the fix it fell back to System.Exception → unhandled-exception
        // crash; the f17+ cascade never ran).
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x14, 0x7A });
        var stmts = StackFold.Fold(insns, new MethodContext { ArgCount = 0 });

        var call = Assert.Single(stmts.OfType<CallStmt>());
        Assert.Equal("throw_System.NullReferenceException", call.Function);
    }


    //--------------------------------------------------------------------------
    // Cctor guard loop hoisting (generated-code audit #5, phase 2 -- conservative):
    // a single-block loop whose body runs NO code that could trigger ANY cctor
    // (no calls / newobj / materializations) may have its ensure_cctor_* calls
    // hoisted to the loop preheader -- .NET JIT does the same for the
    // beforefieldinit class-init check. Hoisting is spec-legal ONLY for
    // BeforeFieldInit types (ECMA-335 II.7.5.4.1 permits running their
    // initializer any time before the first static-field access); precise-init
    // types (explicit .cctor) keep the per-access check.
    //--------------------------------------------------------------------------
    private static TypeDef CasesStatics(bool beforeFieldInit = true) => new()
    {
        Name = "Statics",
        Namespace = "Cases",
        BeforeFieldInit = beforeFieldInit,
    };

    private static List<Stmt> WhileShapeLoop() => new()
    {
        // while-shape (Roslyn default): entry via unconditional 'goto L_30',
        // header L_4 reached only through the back-edge.
        new AssignStmt { Target = "loc0", Value = new IntConstExpr { Value = 0 } },
        new BranchStmt { TargetLabel = "L_30" },
        new LabelStmt { Name = "L_4" },
        new CallStmt { Function = "ensure_cctor_Cases_Statics" },
        new AssignStmt { Target = "Cases_Statics_sf_counter", Value = new LocExpr { Name = "loc0" } },
        new LabelStmt { Name = "L_30" },
        new BranchStmt
        {
            TargetLabel = "L_4",
            Condition = new BinaryExpr
            {
                Op = BinaryOp.Clt,
                Left = new LocExpr { Name = "loc0" },
                Right = new IntConstExpr { Value = 5 },
            },
        },
    };

    [Fact]
    public void HoistLoopGuards_WhileShape_HoistsGuardToPreheader()
    {
        var result = StackFold.HoistLoopGuards(WhileShapeLoop(), new[] { CasesStatics() });

        Assert.Equal("loc0", Assert.IsType<AssignStmt>(result[0]).Target);
        var hoisted = Assert.IsType<CallStmt>(result[1]);
        Assert.Equal("ensure_cctor_Cases_Statics", hoisted.Function);
        Assert.Equal("L_30", Assert.IsType<BranchStmt>(result[2]).TargetLabel); // entry goto still after the guard
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result[3]).Name);           // header label follows
        // the loop body no longer contains any guard
        Assert.DoesNotContain(result, s => s is CallStmt cs
            && cs.Function.StartsWith("ensure_cctor_") && !ReferenceEquals(cs, hoisted));
    }

    [Fact]
    public void HoistLoopGuards_DoWhileShape_HoistsGuardToPreheader()
    {
        // do-while shape: no entry branch -- control falls through into the header.
        var stmts = new List<Stmt>
        {
            new AssignStmt { Target = "loc0", Value = new IntConstExpr { Value = 0 } },
            new LabelStmt { Name = "L_4" },
            new CallStmt { Function = "ensure_cctor_Cases_Statics" },
            new AssignStmt { Target = "Cases_Statics_sf_counter", Value = new LocExpr { Name = "loc0" } },
            new BranchStmt
            {
                TargetLabel = "L_4",
                Condition = new BinaryExpr
                {
                    Op = BinaryOp.Clt,
                    Left = new LocExpr { Name = "loc0" },
                    Right = new IntConstExpr { Value = 5 },
                },
            },
        };
        var result = StackFold.HoistLoopGuards(stmts, new[] { CasesStatics() });

        Assert.Equal("loc0", Assert.IsType<AssignStmt>(result[0]).Target);
        Assert.Equal("ensure_cctor_Cases_Statics", Assert.IsType<CallStmt>(result[1]).Function);
        Assert.Equal("L_4", Assert.IsType<LabelStmt>(result[2]).Name); // header label follows the guard
    }

    [Fact]
    public void HoistLoopGuards_PreciseInitType_KeepsGuardInLoop()
    {
        // Explicit static constructor (non-beforefieldinit): ECMA precise
        // initialization forbids running the cctor early -- no hoisting.
        var result = StackFold.HoistLoopGuards(WhileShapeLoop(), new[] { CasesStatics(beforeFieldInit: false) });

        Assert.Equal("L_30", Assert.IsType<BranchStmt>(result[1]).TargetLabel);
        Assert.Equal("ensure_cctor_Cases_Statics", Assert.IsType<CallStmt>(result[3]).Function);
    }

    [Fact]
    public void HoistLoopGuards_LoopWithCall_NoHoist()
    {
        // A call in the body could run user code that triggers a cctor of
        // ANOTHER type -- hoisting would flip the cctor ORDER. Conservative:
        // keep the per-access check.
        var stmts = WhileShapeLoop();
        stmts.Insert(4, new CallStmt { Function = "call_12345" });
        var result = StackFold.HoistLoopGuards(stmts, new[] { CasesStatics() });

        Assert.Equal("ensure_cctor_Cases_Statics", Assert.IsType<CallStmt>(result[3]).Function);
    }

    [Fact]
    public void HoistLoopGuards_ForwardBranchToHeader_NoHoist()
    {
        // A second entry into the loop header would bypass the hoisted guard.
        var stmts = WhileShapeLoop();
        stmts.Insert(0, new BranchStmt { TargetLabel = "L_4", Condition = new LocExpr { Name = "arg0" } });
        var result = StackFold.HoistLoopGuards(stmts, new[] { CasesStatics() });

        Assert.Equal("ensure_cctor_Cases_Statics", Assert.IsType<CallStmt>(result[4]).Function);
    }

    [Fact]
    public void HoistLoopGuards_AdjacentSameTypeEnsures_Merged()
    {
        // Two same-type guards (e.g. an ldsfld+stsfld pair) hoist as ONE check.
        var stmts = WhileShapeLoop();
        stmts.Insert(4, new CallStmt { Function = "ensure_cctor_Cases_Statics" });
        var result = StackFold.HoistLoopGuards(stmts, new[] { CasesStatics() });

        var ensures = result.OfType<CallStmt>().Where(c => c.Function.StartsWith("ensure_cctor_")).ToList();
        Assert.Single(ensures);
    }

    [Fact]
    public void HoistLoopGuards_NullTypes_NoChange()
    {
        var stmts = WhileShapeLoop();
        var result = StackFold.HoistLoopGuards(stmts, null);

        Assert.Same(stmts, result);
        Assert.Equal("ensure_cctor_Cases_Statics", Assert.IsType<CallStmt>(result[3]).Function);
    }

    //--------------------------------------------------------------------------
    // P0-2 constant local propagation: a local assigned a constant ONCE (and
    // never address-taken) is replaced by the constant at every read, and the
    // now-dead store is removed. Labels (join points) and exception-handler
    // starts (non-sequential entry) invalidate the table.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_ConstProp_ChainFoldsToConstant_AndDeadStoreRemoved()
    {
        // ldc.i4.5; stloc.0; ldloc.0; ret  → 0x20 05 00 00 00 0x0A 0x06 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x06, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(ret.Value).Value);
        // the store is dead (all reads propagated) -> the local is never declared
        Assert.Empty(stmts.OfType<AssignStmt>().Where(a => a.Target == "loc0"));
    }

    [Fact]
    public void Fold_ConstProp_TwoStores_NoPropagation()
    {
        // ldc.i4.5; stloc.0; ldc.i4.6; stloc.0; ldloc.0; ret
        // 0x20 05 00 00 00 0x0A 0x20 06 00 00 00 0x0A 0x06 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x20, 0x06, 0x00, 0x00, 0x00, 0x0A, 0x06, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
        Assert.Equal(2, stmts.OfType<AssignStmt>().Count(a => a.Target == "loc0"));
    }

    [Fact]
    public void Fold_ConstProp_LabelJoin_NoPropagation()
    {
        // ldc.i4.5; stloc.0; br.s 0 (target 8); ldloc.0; ret
        // 0x20 05 00 00 00 0x0A 0x2B 0x00 0x06 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x2B, 0x00, 0x06, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
        Assert.Single(stmts.OfType<AssignStmt>().Where(a => a.Target == "loc0"));
    }

    [Fact]
    public void Fold_ConstProp_AddressTaken_NoPropagation()
    {
        // ldc.i4.5; stloc.0; ldloca.s 0; pop; ldloc.0; ret
        // 0x20 05 00 00 00 0x0A 0x12 0x00 0x26 0x06 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x12, 0x00, 0x26, 0x06, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
        Assert.Single(stmts.OfType<AssignStmt>().Where(a => a.Target == "loc0"));
    }

    [Fact]
    public void Fold_ConstProp_ChainAcrossTwoLocals()
    {
        // ldc.i4.5; stloc.0; ldloc.0; stloc.1; ldloc.1; ret
        // 0x20 05 00 00 00 0x0A 0x06 0x0B 0x07 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x06, 0x0B, 0x07, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0", "loc1" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(ret.Value).Value);
        Assert.Empty(stmts.OfType<AssignStmt>().Where(a => a.Target is "loc0" or "loc1"));
    }

    [Fact]
    public void Fold_ConstProp_HandlerStart_NoPropagation()
    {
        // ldc.i4.5 @0; stloc.0 @5; ldloc.0 @6 (in handler); ret @7
        // 0x20 05 00 00 00 0x0A 0x06 0x2A
        var insns = IlDecoder.DecodeMethod(new byte[] { 0x20, 0x05, 0x00, 0x00, 0x00, 0x0A, 0x06, 0x2A });
        var ctx = new MethodContext
        {
            ArgCount = 0,
            LocNames = new[] { "loc0" },
            ExceptionRegions = new List<IlExceptionRegion>
            {
                new() { TryOffset = 0, TryLength = 6, HandlerOffset = 6, HandlerLength = 1, Kind = ExceptionRegionKind.Catch },
            },
        };
        var stmts = StackFold.Fold(insns, ctx);

        // the read inside the handler may be reached via the exceptional path
        // (the store may not have executed) -> the local must NOT be replaced.
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
        Assert.Single(stmts.OfType<AssignStmt>().Where(a => a.Target == "loc0"));
    }

    //--------------------------------------------------------------------------
    // shr.un (logical right shift) -- `uint x >> n` emits shr.un (0x64).
    // The right shift must be LOGICAL ((uint32_t)x >> n), never arithmetic --
    // .NET's shr.un semantics are logical regardless of the slot type
    // (previously shr.un was rejected at decode; the fix maps it to ShrUn).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_ShrUn_ProducesShrUnBinary()
    {
        // ldarg.0; ldc.i4.5; shr.un; ret → 0x02 0x1B 0x64 0x2A
        // (verified: Roslyn net48 emits `uint x >> 2` as ldarg.0 ldc.i4.2 shr.un)
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x1B, 0x64, 0x2A }),
            new MethodContext { ArgCount = 1 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.ShrUn, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(bin.Right).Value);
    }

    //--------------------------------------------------------------------------
    // int64 mixed arithmetic -- the M5 int64/double wiring (parallel domain)
    // handles in-range int64 constants by WIDENING in C++ (int32_t + LL ->
    // int64). The IsInt64 marker (ldc.i8 provenance) ensures the int32
    // constant-folder never folds int64-typed values (CodeGenerator
    // TryFoldInt32), and conv.i4 explicitly narrows them back to int32.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_LdcI8_InRange_ExplicitConvI4Narrowing_AllowsInt32Arithmetic()
    {
        // ldarg.0; ldc.i8 1; conv.i4; add; ret → 0x02 0x21 01 00 00 00 00 00 00 00 0x69 0x58 0x2A
        // `(int)(x + 1L)` equivalent: conv.i4 explicitly narrows the int64
        // value to int32 (bit-identical for in-range constants) -- afterwards
        // the value is int32-typed and may participate in int32 arithmetic.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x21, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x69, 0x58, 0x2A }),
            new MethodContext { ArgCount = 1 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(bin.Right).Value);
    }

    [Fact]
    public void Fold_LdcI8_InRange_MixedWithInt32_WidensViaCppEmission()
    {
        // ldarg.0; ldc.i8 1; add; ret → 0x02 0x21 01 00 00 00 00 00 00 00 0x58 0x2A
        // `x + 1L`: the int64-typed constant must SURVIVE to the emission
        // layer as an int64-marked constant (TryFoldInt32 must not int32-fold
        // it -- the C++ emission `(arg0 + 1LL)` widens to int64 correctly).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x21, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x58, 0x2A }),
            new MethodContext { ArgCount = 1 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(bin.Left).Name);
        Assert.True(Assert.IsType<IntConstExpr>(bin.Right).IsInt64,
            "the ldc.i8 constant must keep its int64 provenance through folding");
    }

    //--------------------------------------------------------------------------
    // Audit H1 (verification) -- Cgt_un/Clt_un must fold to the UNSIGNED
    // compare ops (a high-bit-set uint must compare larger than a small one).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_CgtUn_ProducesUnsignedCompare()
    {
        // ldc.i4.m1 (0xFFFFFFFF); ldc.i4.1; cgt.un; ret → 0x15 0x17 0xFE 0x03 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x15, 0x17, 0xFE, 0x03, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.CgtUn, bin.Op);
        Assert.Equal(-1L, Assert.IsType<IntConstExpr>(bin.Left).Value);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(bin.Right).Value);
    }

    [Fact]
    public void Fold_CltUn_ProducesUnsignedCompare()
    {
        // ldc.i4.m1; ldc.i4.1; clt.un; ret → 0x15 0x17 0xFE 0x05 0x2A
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x15, 0x17, 0xFE, 0x05, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.CltUn, bin.Op);
    }

    [Fact]
    public void Fold_BgeUn_ProducesNotCltUn()
    {
        // ldarg.0; ldarg.1; bge.un <target=0>; ret → 0x02 0x03 0x41 00 00 00 00 0x2A
        // Bge_un (unsigned ≥) ≡ !(left < right) = Not(CltUn(left, right)).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x41, 0x00, 0x00, 0x00, 0x00, 0x2A }),
            new MethodContext { ArgCount = 2 });

        var branch = Assert.IsType<BranchStmt>(stmts[0]);
        var cond = Assert.IsType<BinaryExpr>(branch.Condition);
        Assert.Equal(BinaryOp.Ceq, cond.Op); // Not(...) = Ceq(..., 0)
        var inner = Assert.IsType<BinaryExpr>(cond.Left);
        Assert.Equal(BinaryOp.CltUn, inner.Op);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(inner.Left).Name);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(inner.Right).Name);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(cond.Right).Value);
    }

    //--------------------------------------------------------------------------
    // P0 (recursive_arith e2e): a USER-method call result must propagate its
    // int64/double return width to the receiving temp. The resolver's
    // user-method path never records ResultKind (null = int32), so the Fact
    // recursion temp was declared int32_t and 15! = 1307674368000 truncated
    // to 2004310016. StackFold decodes the callee's return element type from
    // the method signature (I8/U8 -> "l", R4/R8 -> "d") -- the emitter's
    // ExprKind maps ResultKind "l" to the int64_t declaration.
    //--------------------------------------------------------------------------
    private static MemoryStream SynthesizeUserMethod(byte[] signature)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var method = mdb.AddMethodDefinition(
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static
            | System.Reflection.MethodAttributes.HideBySig,
            System.Reflection.MethodImplAttributes.IL,
            mdb.GetOrAddString("Nope"),
            mdb.GetOrAddBlob(signature),
            0, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            mdb.GetOrAddString("Acme"), mdb.GetOrAddString("Util"), objRef, default, method);

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    private static int UserMethodToken(MetadataReader md)
    {
        var mh = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Nope");
        return MetadataTokens.GetToken(mh);
    }

    [Fact]
    public void Fold_UserCall_Int64Return_PropagatesResultKindToTemp()
    {
        // call Acme.Util::Nope() : int64; call Nope(); mul; ret -- the FIRST
        // result temp must be typed int64_t (ResultKind "l"), else 15!
        // truncates to int32. Two calls keep the first temp's use
        // non-adjacent so it survives as an assignment (robust under the
        // single-use inlining pass).
        using var pe = new PEReader(SynthesizeUserMethod(new byte[] { 0x00, 0x00, 0x0A })); // static, 0 params, int64
        var md = pe.GetMetadataReader();
        var token = UserMethodToken(md);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x58, 0x2A,
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal("t0", assign.Target);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("l", call.ResultKind); // -> emitter declares int64_t
    }

    [Fact]
    public void Fold_UserCall_Int32Return_KeepsNullResultKind()
    {
        // int32-returning user methods must NOT be reclassified (null stays
        // the int32 default; only I8/U8/R4/R8 returns carry a kind).
        using var pe = new PEReader(SynthesizeUserMethod(new byte[] { 0x00, 0x00, 0x08 })); // static, 0 params, int32
        var md = pe.GetMetadataReader();
        var token = UserMethodToken(md);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x58, 0x2A,
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var call = Assert.IsType<CallExpr>(Assert.IsType<AssignStmt>(stmts[0]).Value);
        Assert.Null(call.ResultKind);
    }

    //--------------------------------------------------------------------------
    // P1 (stackframe-audit P0-2): box -> unbox round trip. t0 = box(v);
    // t1 = unbox(t0) with t0 consumed ONLY by the unbox: the box allocation
    // is dead work (box->unbox of the same type is an identity) -- fold to
    // t1 = v, removing the temp and its GC root slot.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_BoxUnbox_RoundTrip_EliminatesBoxAllocation()
    {
        // ldc.i4.5; box <Int32>; unbox.any <Int32>; ret
        // 0x1B 0x8C <tok4> 0xA5 <tok4> 0x2A
        int tok = 0x1B000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x1B,
            0x8C, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0xA5, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x2A,
        }), new MethodContext { ArgCount = 0 });

        Assert.DoesNotContain(stmts, s => s is AssignStmt a && a.Target == "t0");
        var assign = Assert.Single(stmts.OfType<AssignStmt>());
        Assert.Equal("t1", assign.Target);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(assign.Value).Value);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("t1", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    //--------------------------------------------------------------------------
    // P1 (stackframe-audit P0-1): single-use temp forward substitution.
    // t = <call>; <NEXT statement uses t once> -> inline the call at the use
    // and drop the temp. The adjacency keeps side-effect order (nothing can
    // run between the call and its consumption); value-typed results only
    // (reference-typed inlining is the P2-3 GC whitelist, per the audit).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_SingleUseCallTemp_InlinedIntoNextStatement()
    {
        // call <any>; ret -- t0 = call(); return t0 -> return call()
        int tok = 0x06000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x2A,
        }), new MethodContext { ArgCount = 0 });

        var ret = Assert.Single(stmts.OfType<RetStmt>());
        var call = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal($"call_{tok}", call.Function);
        Assert.Empty(stmts.OfType<AssignStmt>()); // the temp assignment folded away
    }

    //--------------------------------------------------------------------------
    // P1 (stackframe-audit P0-1 copy chains + P1-2 join copies): t = <call>;
    // X = t (a PURE copy in the next statement) -> X = <call> -- the call
    // keeps its statement position, so this is a pure rename of the
    // assignment target. Allowed for reference results too (the target
    // becomes the root slot; covers t30 -> loc0 and t11 = t10 join arms).
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_CallThenLocalCopy_TempRenamedAway()
    {
        // call <any>; stloc.0 -- t0 = call(); loc0 = t0 -> loc0 = call()
        int tok = 0x06000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x0A,
        }), new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var assign = Assert.Single(stmts.OfType<AssignStmt>());
        Assert.Equal("loc0", assign.Target);
        Assert.IsType<CallExpr>(assign.Value);
        Assert.DoesNotContain(stmts, s => s is AssignStmt a && a.Target == "t0");
    }

    [Fact]
    public void Fold_TempUsedTwice_NotFolded()
    {
        // call; dup; stloc.0; ldloc.0; add; ret -- t0 feeds BOTH the loc0
        // copy and the return expression: folding would duplicate the call
        // (two side effects).
        int tok = 0x06000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x25, 0x0A, 0x06, 0x58, 0x2A,
        }), new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        Assert.Equal(2, stmts.OfType<AssignStmt>().Count()); // t0 = call(); loc0 = t0
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal("t0", Assert.IsType<LocExpr>(bin.Left).Name);
    }

    [Fact]
    public void Fold_SingleUseTemp_NonAdjacentUse_NotFolded()
    {
        // call; ldc.i4.7; stsfld; stloc.0 -- t0's only use is the loc0 copy,
        // but the static-field store sits between: inlining would move the
        // call AFTER the store (side-effect reorder).
        // 0x28 <tok4> 0x1D 0x80 <fieldtok4> 0x0A
        int tok = 0x06000001;
        int fieldTok = 0x04000001;
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)tok, (byte)(tok >> 8), (byte)(tok >> 16), (byte)(tok >> 24),
            0x1D,
            0x80, (byte)fieldTok, (byte)(fieldTok >> 8), (byte)(fieldTok >> 16), (byte)(fieldTok >> 24),
            0x0A,
        }), new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        Assert.Equal(3, stmts.Count);
        var locStore = Assert.IsType<AssignStmt>(stmts[2]);
        Assert.Equal("loc0", locStore.Target);
        Assert.Equal("t0", Assert.IsType<LocExpr>(locStore.Value).Name);
    }

    //--------------------------------------------------------------------------
    // Closure model (delecb2): a C# lambda capturing a method parameter
    // (`x => x + n`) compiles to a compiler-generated DISPLAY CLASS
    // `<>c__DisplayClass3_0` (nested type, instance field `n` for the capture)
    // + an instance closure method `<MakeAdder>b__0` reading `this.n`. The
    // calling method folds the closure construction as:
    //   t0 = new <>c__DisplayClass3_0 ; ctor(t0) ; (t0)->n = n ; ldftn b__0
    //   t1 = delegate_new(&b__0, target=t0)   <- the closure OBJECT is the
    //                                           delegate's bound target (#42)
    //   ret t1
    // The fold must pass the display-class object through as the delegate
    // Target (the runtime dispatch calls b__0(target, x) -- target's captured
    // field n supplies the bound value). The transpile-level blockers for the
    // generic Func<int,int> RETURN are emission-side (ReturnTypeCpp / IsRefType
    // / LocalTypeKind) -- this test pins the StackFold-side closure IR, which
    // is complete and correct.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_ClosureReturn_MakeAdder_BindsDisplayClassAsDelegateTarget()
    {
        using var pe = OpenCaseExe("delecb2");
        var md = pe.GetMetadataReader();
        var types = MetadataLoader.LoadAssembly(CaseExePath("delecb2"));
        TypeEmitter.ExpandNestedTypes(md, types);

        var owner = types.Single(t => t.FullName == "Cases.Delecb2");
        var makeAdder = owner.Methods.Single(m => m.Name == "MakeAdder");
        var body = MethodBodyReader.Read(pe, md, owner, makeAdder);
        var insns = IlDecoder.DecodeMethod(body.IlBytes, md);
        var ctx = new MethodContext
        {
            ArgCount = body.ArgCount,
            LocNames = body.LocNames,
            LocalKinds = body.LocalKinds,
            ReturnsVoid = body.ReturnsVoid,
            MethodCppName = MethodRefResolver.UserMethodCppName(owner.FullName, "MakeAdder", "i"),
            IsStatic = true,
            ParamTypeNames = new[] { "i" },
        };
        var stmts = StackFold.Fold(insns, ctx, md, types, null, pe);

        // 1) the display class is allocated first (the closure object)
        var alloc = Assert.IsType<AssignStmt>(stmts[0]);
        Assert.Equal("t0", alloc.Target);
        var newObj = Assert.IsType<NewObjExpr>(alloc.Value);
        Assert.Equal("Cases.Delecb2+<>c__DisplayClass3_0", newObj.TypeFullName);

        // 2) the captured parameter is stored into the display-class field n
        var cap = Assert.IsType<FieldAssignStmt>(stmts[2]);
        Assert.Equal("n", cap.FieldCppName);
        Assert.Equal("t0", Assert.IsType<LocExpr>(cap.Object).Name);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(cap.Value).Name);

        // 3) the delegate wraps the closure method with the closure OBJECT as
        //    the bound target (NOT null -- this is the closure binding)
        var del = Assert.IsType<AssignStmt>(stmts[3]);
        Assert.Equal("t1", del.Target);
        var delExpr = Assert.IsType<DelegateNewExpr>(del.Value);
        var target = Assert.IsType<LocExpr>(delExpr.Target);
        Assert.Equal("t0", target.Name);
        // the wrapped method is the closure method <MakeAdder>b__0 (0x06000009)
        Assert.Equal(0x06000009, delExpr.MethodToken);

        // 4) the delegate is returned
        var ret = Assert.IsType<RetStmt>(stmts[4]);
        Assert.Equal("t1", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    [Fact]
    public void Fold_ClosureMethod_B__0_ReadsCapturedFieldViaThis()
    {
        using var pe = OpenCaseExe("delecb2");
        var md = pe.GetMetadataReader();
        var types = MetadataLoader.LoadAssembly(CaseExePath("delecb2"));
        TypeEmitter.ExpandNestedTypes(md, types);

        var display = types.Single(t => t.FullName == "<>c__DisplayClass3_0");
        var b0 = display.Methods.Single(m => m.Name == "<MakeAdder>b__0");
        var body = MethodBodyReader.Read(pe, md, display, b0);
        var insns = IlDecoder.DecodeMethod(body.IlBytes, md);
        var ctx = new MethodContext
        {
            ArgCount = body.ArgCount,
            LocNames = body.LocNames,
            LocalKinds = body.LocalKinds,
            ReturnsVoid = body.ReturnsVoid,
            MethodCppName = MethodRefResolver.UserMethodCppName(display.FullName, "<MakeAdder>b__0", "i"),
            IsStatic = false,
            ParamTypeNames = new[] { "i" },
        };
        var stmts = StackFold.Fold(insns, ctx, md, types, null, pe);

        // x + this.n -> (arg1 Add (arg0)->n): the closure method reads its own
        // captured field through this (arg0)
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.Equal("arg1", Assert.IsType<LocExpr>(bin.Left).Name);
        var fld = Assert.IsType<FieldExpr>(bin.Right);
        Assert.Equal("n", fld.FieldCppName);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(fld.Object).Name);
    }

    //--------------------------------------------------------------------------
    // conv.i4 of a CALL RESULT -- numchain e2e `(int)double.Parse("2.9") == 2`
    // (line 158 prints False vs expected True). The old conv.i4 fold only
    // matched DoubleConstExpr/LocExpr, so a double-typed call result
    // (ResultKind "d") was DROPPED and the double 2.9 survived to the `== 2`
    // comparison. ResultKind "l" (int64 call results) had the same drop.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_ConvI4_DoubleCallResult_EmitsConvI4D()
    {
        // call Nope() : double; conv.i4; ret -- the double result must route
        // through icall_conv_i4_d (truncate toward zero), not stay a double.
        using var pe = new PEReader(SynthesizeUserMethod(new byte[] { 0x00, 0x00, 0x0D })); // static, 0 params, double
        var md = pe.GetMetadataReader();
        var token = UserMethodToken(md);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x69, 0x2A, // conv.i4; ret
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var assign = Assert.IsType<AssignStmt>(stmts[0]); // t0 = call Nope()
        Assert.Equal("t0", assign.Target);
        Assert.Equal("d", Assert.IsType<CallExpr>(assign.Value).ResultKind);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_i4_d", conv.Function);
        Assert.Equal("t0", Assert.IsType<LocExpr>(conv.Args[0]).Name);
    }

    [Fact]
    public void Fold_ConvI4_Int64CallResult_EmitsConvI4I8()
    {
        // call Nope() : int64; conv.i4; ret -- the int64 result narrows via
        // icall_conv_i4_i8 (low 32 bits), the same icall an int64 LOCAL uses.
        using var pe = new PEReader(SynthesizeUserMethod(new byte[] { 0x00, 0x00, 0x0A })); // static, 0 params, int64
        var md = pe.GetMetadataReader();
        var token = UserMethodToken(md);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x28, (byte)(token & 0xFF), (byte)((token >> 8) & 0xFF),
            (byte)((token >> 16) & 0xFF), (byte)((token >> 24) & 0xFF),
            0x69, 0x2A, // conv.i4; ret
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_i4_i8", conv.Function);
        Assert.Equal("t0", Assert.IsType<LocExpr>(conv.Args[0]).Name);
    }

    //--------------------------------------------------------------------------
    // Generic-instantiation arg expansion (CG-2 out-of-domain follow-up): the
    // StackFold TypeSpec decoder (StackFold.cs GenericInstantiationName) threw
    // "generic argument byte 0x0E/0x02 in M3" for a non-int32 generic arg.
    // dictcase Dictionary<string,int> hit 0x0E, linqcase Func<int,bool> hit
    // 0x02 -- both died at transpile before codegen.
    //--------------------------------------------------------------------------
    [Fact]
    public void Fold_Ldsfld_StringGenericArg_DecodesInstanceName()
    {
        // ldsfld <MemberRef field on Dictionary<string,int>> -- the field
        // MemberRef's parent is a TypeSpec (GenericInst, arg0 = STRING 0x0E,
        // arg1 = int32 0x08). The field path (StackFold ResolveField) must
        // decode "System.Collections.Generic.Dictionary`2_string_int" instead
        // of throwing "generic argument byte 0x0E in M3".
        using var pe = new PEReader(SynthesizeGenericField("System.Collections.Generic", "Dictionary`2", new byte[] { 0x15, 0x12, 0x09, 0x02, 0x0E, 0x08 }));
        var md = pe.GetMetadataReader();
        var token = md.MemberReferences
            .Select(h => (Handle: h, MR: md.GetMemberReference(h)))
            .Single(t => md.GetString(t.MR.Name) == "Version");
        var tok = MetadataTokens.GetToken(token.Handle);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x7E, (byte)(tok & 0xFF), (byte)((tok >> 8) & 0xFF),
            (byte)((tok >> 16) & 0xFF), (byte)((tok >> 24) & 0xFF),
            0x2A, // ret (the field value is returned)
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        var expr = Assert.IsType<StaticFieldExpr>(ret.Value);
        Assert.Equal("System.Collections.Generic.Dictionary`2_string_int.Version", expr.FieldName);
    }

    [Fact]
    public void Fold_Ldsfld_BoolGenericArg_DecodesInstanceName()
    {
        // ldsfld on a Func<int,bool>-shaped TypeSpec (arg0 = int32 0x08, arg1
        // = BOOLEAN 0x02) -- linqcase's Func<int,bool> generic-arg shape.
        using var pe = new PEReader(SynthesizeGenericField("System", "Func`2", new byte[] { 0x15, 0x12, 0x09, 0x02, 0x08, 0x02 }));
        var md = pe.GetMetadataReader();
        var token = md.MemberReferences
            .Select(h => (Handle: h, MR: md.GetMemberReference(h)))
            .Single(t => md.GetString(t.MR.Name) == "Version");
        var tok = MetadataTokens.GetToken(token.Handle);
        var stmts = StackFold.Fold(IlDecoder.DecodeMethod(new byte[]
        {
            0x7E, (byte)(tok & 0xFF), (byte)((tok >> 8) & 0xFF),
            (byte)((tok >> 16) & 0xFF), (byte)((tok >> 24) & 0xFF),
            0x2A, // ret (the field value is returned)
        }), new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());

        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        var expr = Assert.IsType<StaticFieldExpr>(ret.Value);
        Assert.Equal("System.Func`2_int_bool.Version", expr.FieldName);
    }

    // TypeSpec-parented field MemberRef: a TypeRef (row 2 -> coded index
    // 0x09, TypeRef tag 1) + the GenericInst TypeSpec + a "Version" MemberRef
    // whose parent is the TypeSpec (FIELD signature 0x06 + int32 0x08).
    private static MemoryStream SynthesizeGenericField(string ns, string defName, byte[] typeSpec)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var asmRef = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(asmRef, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var defRef = mdb.AddTypeReference(asmRef,
            mdb.GetOrAddString(ns), mdb.GetOrAddString(defName));
        var typeSpecHandle = mdb.AddTypeSpecification(mdb.GetOrAddBlob(typeSpec));
        mdb.AddMemberReference(typeSpecHandle, mdb.GetOrAddString("Version"),
            mdb.GetOrAddBlob(new byte[] { 0x06, 0x08 })); // FIELD: int32
        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, new BlobBuilder());
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new MemoryStream(blob.ToArray());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "il2cppc", "il2cppc.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the repo root from " + AppContext.BaseDirectory);
    }

    private static string CaseExePath(string name) =>
        Path.Combine(RepoRoot(), "tests", "cases", name, "obj", "Release", "net48", name + ".exe");

    private static PEReader OpenCaseExe(string name)
    {
        var path = CaseExePath(name);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"fixture {path} not found -- build the case first (dotnet build tests/cases/{name}/{name}.csproj -c Release)");
        return new PEReader(File.OpenRead(path));
    }

    //--------------------------------------------------------------------------
    // IL gap batch b23 -- StackFold folds for the audit's 13 big gaps.
    // The decoder already reads every opcode (217/218 decode; Calli = FNPTR
    // domain); these tests pin the FOLD shapes the CodeGenerator patches (see
    // %TEMP%\opencode\ilop2-b23-report.md) consume. Pure-byte folds carry the
    // non-metadata families (ldind/stind/cpblk/initblk/localloc/ckfinite/ovf/
    // rem.un/LOUD); the synthetic-PE folds carry the token-based families
    // (ldobj/stobj/initobj/sizeof/ldelema/ldelem/stelem/jmp).
    //--------------------------------------------------------------------------

    [Fact]
    public void Fold_LdindI4_ProducesLdindCall()
    {
        // ldc.i4.0; ldind.i4; ret -- pointer dereference read: the address 0 is
        // popped, the fold pushes CallExpr "ldind_Ldind_i4" (the emitter
        // substitutes *reinterpret_cast<int32_t*>(addr), CodeGenerator patch).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x4A, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldind_Ldind_i4", call.Function);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(Assert.Single(call.Args)).Value);
    }

    [Fact]
    public void Fold_LdindI8_ProducesInt64ResultCall()
    {
        // ldind.i8 (0x4C) reads a 64-bit integer -> ResultKind "l" (int64_t
        // receiving temp).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x4C, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldind_Ldind_i8", call.Function);
        Assert.Equal("l", call.ResultKind);
    }

    [Fact]
    public void Fold_LdindR8_ProducesDoubleResultCall()
    {
        // ldind.r8 (0x4F) reads a double -> ResultKind "d".
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x4F, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldind_Ldind_r8", call.Function);
        Assert.Equal("d", call.ResultKind);
    }

    [Fact]
    public void Fold_LdindI_NativeInt_ProducesPtrResultCall()
    {
        // ldind.i (0x4D) reads a native-sized int -> ResultKind "ptr"
        // (intptr_t receiving temp).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x4D, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldind_Ldind_i", call.Function);
        Assert.Equal("ptr", call.ResultKind);
    }

    [Fact]
    public void Fold_LdindRef_KeepsReferenceTemp()
    {
        // ldind.ref (0x50) reads a GC reference through the address -- a
        // reference-typed result is NOT inlined by OptimizeTemps (rule 3 is
        // value-typed only), so the fold keeps the reference temp assignment.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x50, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var assign = Assert.IsType<AssignStmt>(stmts[0]);
        var call = Assert.IsType<CallExpr>(assign.Value);
        Assert.Equal("ldind_Ldind_ref", call.Function);
        Assert.True(call.ReturnsRef);
        Assert.Equal("t0", assign.Target);
        Assert.Equal("t0", Assert.IsType<LocExpr>(Assert.IsType<RetStmt>(stmts[1]).Value).Name);
    }

    [Fact]
    public void Fold_LdelemI8_ProducesInt64ResultCall()
    {
        // ldc.i4.0; ldc.i4.0; ldelem.i8 (0x96); ret -- an 8-byte element read
        // -> ResultKind "l" (int64_t receiving temp); a 4-byte temp would
        // truncate the long before the consumer sees it (arrayops long[]).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x16, 0x96, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldelem_Ldelem_i8", call.Function);
        Assert.Equal("l", call.ResultKind);
    }

    [Fact]
    public void Fold_LdelemR8_ProducesDoubleResultCall()
    {
        // ldc.i4.0; ldc.i4.0; ldelem.r8 (0x99); ret -- a double element read
        // -> ResultKind "d" (double receiving temp).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x16, 0x99, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldelem_Ldelem_r8", call.Function);
        Assert.Equal("d", call.ResultKind);
    }

    [Fact]
    public void Fold_LdelemU4_ProducesInt32SlotCall()
    {
        // ldc.i4.0; ldc.i4.0; ldelem.u4 (0x95); ret -- a uint32 element read
        // stays in the uniform int32 slot (ResultKind null).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x16, 0x95, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("ldelem_Ldelem_u4", call.Function);
        Assert.Null(call.ResultKind);
    }

    [Fact]
    public void Fold_StelemI8_ProducesStelemStatement()
    {
        // ldc.i4.0; ldc.i4.0; ldc.i4.0; stelem.i8 (0x9F); ret -- an 8-byte
        // element write folds to the stelem_Stelem_i8 CallStmt (the emitter
        // renders the int64_t store).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0x16, 0x16, 0x9F, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("stelem_Stelem_i8", stmt.Function);
    }

    [Fact]
    public void Fold_StindI4_ProducesStindStatement()
    {
        // ldc.i4.1; ldc.i4.2; stind.i4; ret -- the address is pushed first,
        // the value on top; the fold pops value then addr. Side effect ->
        // CallStmt (the emitter substitutes (*reinterpret_cast<int32_t*>(a) = v)).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0x54, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("stind_Stind_i4", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value); // addr
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value); // value
    }

    [Fact]
    public void Fold_StindRef_ProducesStindRefStatement()
    {
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0x51, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("stind_Stind_ref", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value);
    }

    [Fact]
    public void Fold_Ckfinite_ProducesRuntimeIcall()
    {
        // ldc.r8 1.0; ckfinite; ret -- the double is popped and the fold emits
        // the runtime conversion icall (icall_conv_* precedent); the runtime
        // patch adds icall_ckfinite (throws OverflowException on NaN/Inf).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x23, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F, 0xC3, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::runtime::icall_ckfinite", call.Function);
        Assert.Equal("d", call.ResultKind);
        Assert.Equal(1.0, Assert.IsType<DoubleConstExpr>(Assert.Single(call.Args)).Value);
    }

    [Fact]
    public void Fold_Localloc_ProducesAllocaMarkerCall()
    {
        // ldc.i4 256; localloc; ret -- the frame allocation folds to the
        // il2cpp::il2cpp_localloc marker with a "ptr" result temp; the emitter
        // patch substitutes alloca(static_cast<size_t>(size)) INLINE (alloca
        // cannot leave the caller's frame).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x00, 0x01, 0x00, 0x00, 0xFE, 0x0F, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_localloc", call.Function);
        Assert.Equal("ptr", call.ResultKind);
        Assert.Equal(256L, Assert.IsType<IntConstExpr>(Assert.Single(call.Args)).Value);
    }

    [Fact]
    public void Fold_Cpblk_ProducesMemcpyStatement()
    {
        // ldc.i4.1; ldc.i4.2; ldc.i4.3; cpblk; ret -- stack [dest, src, count];
        // the emitter patch substitutes memcpy(dest, src, count).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0x19, 0xFE, 0x17, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("cpblk", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value); // dest
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value); // src
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(stmt.Args[2]).Value); // count
    }

    [Fact]
    public void Fold_Initblk_ProducesMemsetStatement()
    {
        // ldc.i4.1; ldc.i4.2; ldc.i4.3; initblk; ret -- stack [dest, value,
        // count]; the emitter patch substitutes memset(dest, value, count).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0x19, 0xFE, 0x18, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.Equal("initblk", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value); // dest
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value); // value
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(stmt.Args[2]).Value); // count
    }

    [Fact]
    public void Fold_AddOvf_ProducesCheckedAddCall()
    {
        // ldc.i4.1; ldc.i4.2; add.ovf; ret -- checked arithmetic folds to the
        // runtime helper (il2cpp_checked_div/rem precedent); the runtime patch
        // adds il2cpp_checked_add (throws OverflowException on wrap).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0xD6, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_add", call.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(call.Args[0]).Value);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(call.Args[1]).Value);
    }

    [Fact]
    public void Fold_AddOvfUn_ProducesCheckedAddUnCall()
    {
        // add.ovf.un (0xD7) compares UNSIGNED -> the _un helper.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0xD7, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_add_un", call.Function);
    }

    [Fact]
    public void Fold_SubOvf_ProducesCheckedSubCall()
    {
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0xDA, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_sub", call.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(call.Args[0]).Value);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(call.Args[1]).Value);
    }

    [Fact]
    public void Fold_MulOvf_ProducesCheckedMulCall()
    {
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x18, 0x19, 0xD8, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_mul", call.Function);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(call.Args[0]).Value);
        Assert.Equal(3L, Assert.IsType<IntConstExpr>(call.Args[1]).Value);
    }

    [Fact]
    public void Fold_RemUn_ProducesCheckedRemUnCall()
    {
        // ldc.i4.5; ldc.i4.2; rem.un; ret -- unsigned int32 remainder folds to
        // the il2cpp_checked_rem_un runtime helper (uint32 modulo + zero check).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x1B, 0x18, 0x5E, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_rem_un", call.Function);
        Assert.Equal(5L, Assert.IsType<IntConstExpr>(call.Args[0]).Value);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(call.Args[1]).Value);
    }

    [Fact]
    public void Fold_AddOvf_Int32Consts_NoResultKind()
    {
        // i64ck-b23 regression: an int32 checked fold carries NO width
        // (ResultKind null) -- the emitter keeps the int32 helper path with
        // its explicit int32_t casts.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x17, 0x18, 0xD6, 0x2A }),
            new MethodContext { ArgCount = 0 });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_add", call.Function);
        Assert.Null(call.ResultKind);
    }

    [Fact]
    public void Fold_AddOvf_Int64Local_SetsResultKindL()
    {
        // ldloc.0 (int64 local); ldc.i8 1; add.ovf; ret -- a 64-bit checked
        // fold sets ResultKind "l" (the emitter routes to il2cpp_checked_add_64
        // and the receiving temp is typed int64_t; the int32 helper would
        // truncate the operands).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x21, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xD6, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" }, LocalKinds = new[] { "l" } });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_add", call.Function);
        Assert.Equal("l", call.ResultKind);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(call.Args[0]).Name);
        var one = Assert.IsAssignableFrom<IntConstExpr>(call.Args[1]);
        Assert.Equal(1L, one.Value);
    }

    [Fact]
    public void Fold_AddOvfUn_Int64Local_SetsResultKindL()
    {
        // ldloc.0 (ulong); ldc.i8 1; add.ovf.un; ret -- the unsigned 64-bit
        // checked fold also sets ResultKind "l" (il2cpp_checked_add_un_64).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x21, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xD7, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" }, LocalKinds = new[] { "l" } });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_add_un", call.Function);
        Assert.Equal("l", call.ResultKind);
    }

    [Fact]
    public void Fold_RemUn_Int64Locals_SetsResultKindL()
    {
        // ldloc.0; ldloc.1 (both ulong); rem.un; ret -- uint64 remainder folds
        // with ResultKind "l" (il2cpp_checked_rem_un_64).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x07, 0x5E, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0", "loc1" }, LocalKinds = new[] { "l", "l" } });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("il2cpp::il2cpp_checked_rem_un", call.Function);
        Assert.Equal("l", call.ResultKind);
    }

    [Fact]
    public void Fold_Arglist_PushesVarArgsFrameHandle()
    {
        // arglist: pushes the CURRENT method's varargs frame handle -- the
        // trailing intptr_t va_frame parameter every variadic method receives.
        // `arglist; pop; ret` folds without error and the handle is referenced.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0xFE, 0x00, 0x26, 0x2A }),
            new MethodContext { ArgCount = 0 });
        Assert.NotEmpty(stmts);
    }

    [Fact]
    public void Fold_Endfilter_ThrowsNotSupported()
    {
        // ldc.i4.0; endfilter with NO filter region: endfilter is legal only
        // as the last instruction of a FILTER block (ECMA-335 III.3.21).
        Assert.Throws<NotSupportedException>(() => StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0xFE, 0x11, 0x2A }),
            new MethodContext { ArgCount = 0 }));
    }

    [Fact]
    public void Fold_Mkrefany_ThrowsNotSupported()
    {
        // ldc.i4.0; mkrefany <tok> (TypedReference -- no IR representation): LOUD.
        Assert.Throws<NotSupportedException>(() => StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x16, 0xC6, 0x0D, 0x00, 0x00, 0x01, 0x2A }),
            new MethodContext { ArgCount = 0 }));
    }

    [Fact]
    public void Fold_Refanytype_PushesTypeHandleCall()
    {
        // ldloc.0 (TypedReference); refanytype; ret -- the fold pushes the
        // DECLARED-TYPE HANDLE call (the descriptor address as an intptr_t);
        // the C# `__reftype` shape consumes it with
        // System.Type::GetTypeFromHandle, which yields the same
        // full-name-string Type value the typeof chain produces.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0xFE, 0x1D, 0x2A }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" }, LocalKinds = new[] { "tr" } });
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.Equal("typedref_handle", call.Function);
        Assert.Equal("ptr", call.ResultKind);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(Assert.Single(call.Args)).Name);
    }

    [Fact]
    public void Fold_Jmp_WithoutMetadata_ThrowsNotSupported()
    {
        // jmp (tail jump) resolves the target arity from the method signature
        // -- the pure-byte path cannot: LOUD.
        Assert.Throws<NotSupportedException>(() => StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x27, 0x06, 0x00, 0x00, 0x0A, 0x2A }),
            new MethodContext { ArgCount = 0 }));
    }

    //--------------------------------------------------------------------------
    // Token-based families (metadata required) -- synthetic PE (the
    // StackFoldTests.BclField.cs Synthesize pattern).
    //--------------------------------------------------------------------------

    [Fact]
    public void Fold_LdelemToken_Int32Element_MapsToFixedWidthI4()
    {
        // ldc.i4.1; newarr Int32; ldc.i4.0; ldelem Int32; ret -- the generic-
        // token ldelem resolves the element type from the TOKEN and reuses the
        // EXISTING fixed-width emission (ldelem_Ldelem_i4 -- no CodeGenerator
        // change needed).
        using var pe = SynthesizeGap(GapBodyKind.LdelemInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var call = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("ldelem_Ldelem_i4", call.Function);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(call.Args[1]).Value); // idx
        Assert.False(call.ReturnsRef);
    }

    [Fact]
    public void Fold_StelemToken_StringElement_MapsToFixedWidthRef()
    {
        // ldc.i4.1; newarr String; ldc.i4.0; ldnull; stelem String; ret -- a
        // reference element maps to the existing stelem_Stelem_ref emission.
        using var pe = SynthesizeGap(GapBodyKind.StelemString);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var stmt = stmts.OfType<CallStmt>().Single(s => s.Function.StartsWith("stelem_", StringComparison.Ordinal));
        Assert.Equal("stelem_Stelem_ref", stmt.Function);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value); // idx
        Assert.IsType<NullExpr>(stmt.Args[2]); // value
    }

    [Fact]
    public void Fold_LdelemToken_Int64Element_MapsToFixedWidthI8()
    {
        // ldc.i4.1; newarr Int64; ldc.i4.0; ldelem Int64; ret -- the generic-
        // token ldelem on a long[] element maps to the fixed-width i8 emission
        // (b23 arrayops: Ldelem_i8 no longer LOUDs) with an int64 result slot.
        using var pe = SynthesizeGap(GapBodyKind.LdelemInt64);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var call = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("ldelem_Ldelem_i8", call.Function);
        Assert.Equal("l", call.ResultKind);
        Assert.False(call.ReturnsRef);
    }

    [Fact]
    public void Fold_LdelemToken_DoubleElement_MapsToFixedWidthR8()
    {
        // ldc.i4.1; newarr Double; ldc.i4.0; ldelem Double; ret -- a double
        // element maps to the r8 emission with a double result slot.
        using var pe = SynthesizeGap(GapBodyKind.LdelemDouble);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var call = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("ldelem_Ldelem_r8", call.Function);
        Assert.Equal("d", call.ResultKind);
    }

    [Fact]
    public void Fold_StelemToken_Int64Element_MapsToFixedWidthI8()
    {
        // ldc.i4.1; newarr Int64; ldc.i4.0; ldc.i8.0; stelem Int64; ret -- a
        // long[] element write maps to the fixed-width stelem_i8 statement.
        using var pe = SynthesizeGap(GapBodyKind.StelemInt64);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var stmt = stmts.OfType<CallStmt>().Single(s => s.Function.StartsWith("stelem_", StringComparison.Ordinal));
        Assert.Equal("stelem_Stelem_i8", stmt.Function);
    }

    [Fact]
    public void Fold_LdelemaToken_ProducesLdelemaCall()
    {
        // ldc.i4.1; newarr Int32; ldc.i4.0; ldelema Int32; ret -- the element
        // ADDRESS folds to the ldelema_{token} marker with a "ptr" result temp
        // (the emitter patch renders il2cpp_array_get_elem_ptr).
        using var pe = SynthesizeGap(GapBodyKind.LdelemaInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(stmts[^1]).Value);
        Assert.StartsWith("ldelema_", call.Function);
        Assert.Equal("ptr", call.ResultKind);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(call.Args[1]).Value); // idx
    }

    [Fact]
    public void Fold_LdobjToken_Int32_ProducesLdobjCall()
    {
        // ldc.i4.0; ldobj Int32; ret -- the value-type LOAD by token folds to
        // the ldobj_{token} marker (the emitter patch resolves the token to the
        // C++ type).
        using var pe = SynthesizeGap(GapBodyKind.LdobjInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.StartsWith("ldobj_", call.Function);
        Assert.False(call.ReturnsRef);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(Assert.Single(call.Args)).Value);
    }

    [Fact]
    public void Fold_StobjToken_Int32_ProducesStobjStatement()
    {
        // ldc.i4.1 (addr); ldc.i4.2 (value); stobj Int32; ret -- a CallStmt
        // (the emitter patch renders (*reinterpret_cast<T*>(a) = v)).
        using var pe = SynthesizeGap(GapBodyKind.StobjInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.StartsWith("stobj_", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value); // addr
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value); // value
    }

    [Fact]
    public void Fold_InitobjToken_Int32_ProducesInitobjStatement()
    {
        // ldc.i4.0; initobj Int32; ret -- a CallStmt (the emitter patch
        // zero-fills the slot).
        using var pe = SynthesizeGap(GapBodyKind.InitobjInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.StartsWith("initobj_", stmt.Function);
        Assert.Equal(0L, Assert.IsType<IntConstExpr>(Assert.Single(stmt.Args)).Value);
    }

    [Fact]
    public void Fold_Sizeof_Int32_FoldsToConstant()
    {
        // sizeof Int32; ret -- primitive widths are compile-time constants.
        using var pe = SynthesizeGap(GapBodyKind.SizeofInt32);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var ret = Assert.IsType<RetStmt>(Assert.Single(stmts));
        Assert.Equal(4L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_Sizeof_Decimal_ProducesSizeofCall()
    {
        // sizeof System.Decimal; ret -- a non-primitive value type (16 bytes)
        // folds to the sizeof_{token} marker (the emitter patch renders
        // sizeof({TypeCppName}) on the runtime struct).
        using var pe = SynthesizeGap(GapBodyKind.SizeofDecimal);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var call = Assert.IsType<CallExpr>(Assert.IsType<RetStmt>(Assert.Single(stmts)).Value);
        Assert.StartsWith("sizeof_", call.Function);
    }

    [Fact]
    public void Fold_Jmp_StaticMethod_ProducesJmpCall()
    {
        // ldc.i4.1; ldc.i4.2; jmp Target; ret -- the target arity comes from
        // the method token signature (2 int32 params, static -> no this); the
        // fold pops the args and emits CallStmt{Function="jmp_{token}"} (the
        // emitter patch renders `return {CppName}(args...);`).
        using var pe = SynthesizeGap(GapBodyKind.JmpStatic);
        var md = pe.GetMetadataReader();
        var stmts = Fold(pe, md);
        var stmt = Assert.IsType<CallStmt>(stmts[0]);
        Assert.StartsWith("jmp_", stmt.Function);
        Assert.Equal(1L, Assert.IsType<IntConstExpr>(stmt.Args[0]).Value);
        Assert.Equal(2L, Assert.IsType<IntConstExpr>(stmt.Args[1]).Value);
    }

    private enum GapBodyKind
    {
        LdelemInt32,
        LdelemInt64,
        LdelemDouble,
        StelemString,
        StelemInt64,
        LdelemaInt32,
        LdobjInt32,
        StobjInt32,
        InitobjInt32,
        SizeofInt32,
        SizeofDecimal,
        JmpStatic,
    }

    // Fold Cases.Gap::Main from the synthetic PE (the standard StackFold
    // test-harness shape: metadata present, model types empty).
    private static List<Stmt> Fold(PEReader pe, MetadataReader md)
    {
        var main = md.TypeDefinitions
            .SelectMany(t => md.GetTypeDefinition(t).GetMethods())
            .Single(h => md.GetString(md.GetMethodDefinition(h).Name) == "Main");
        var bodyInfo = pe.GetMethodBody(md.GetMethodDefinition(main).RelativeVirtualAddress);
        var body = IlDecoder.DecodeMethod(bodyInfo!.GetILBytes()!, md);
        return StackFold.Fold(body, new MethodContext { ArgCount = 0 }, md, Array.Empty<TypeDef>());
    }

    // Synthetic PE: a Cases.Gap::Main body carrying the instruction under test
    // (the StackFoldTests.BclField.cs Synthesize pattern). A separate
    // Cases.Gap::Target(int,int):int method (RVA 0 -- no body) backs the jmp
    // fold's signature resolution.
    private static PEReader SynthesizeGap(GapBodyKind kind)
    {
        var mdb = new MetadataBuilder();
        mdb.AddModule(0, mdb.GetOrAddString("synthetic.dll"), mdb.GetOrAddGuid(Guid.NewGuid()),
            default(GuidHandle), default(GuidHandle));
        var mscorlib = mdb.AddAssemblyReference(mdb.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0), default(StringHandle), default(BlobHandle), 0, default(BlobHandle));
        var objRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Object"));
        var int32Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int32"));
        var int64Ref = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Int64"));
        var doubleRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Double"));
        var stringRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("String"));
        var decimalRef = mdb.AddTypeReference(mscorlib, mdb.GetOrAddString("System"), mdb.GetOrAddString("Decimal"));

        // Target(int,int):int -- RVA 0 (no body); the jmp fold resolves only
        // the signature (param count + instance flag).
        var target = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Target"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x02, 0x08, 0x08, 0x08 }),
            0, default(ParameterHandle));

        var il = new BlobBuilder();
        var encoder = new InstructionEncoder(il);
        switch (kind)
        {
            case GapBodyKind.LdelemInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(int32Ref));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldelem); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.LdelemInt64:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(int64Ref));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldelem); encoder.Token(MetadataTokens.GetToken(int64Ref));
                break;
            case GapBodyKind.LdelemDouble:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(doubleRef));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldelem); encoder.Token(MetadataTokens.GetToken(doubleRef));
                break;
            case GapBodyKind.StelemString:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(stringRef));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldnull);
                encoder.OpCode(ILOpCode.Stelem); encoder.Token(MetadataTokens.GetToken(stringRef));
                break;
            case GapBodyKind.StelemInt64:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(int64Ref));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Stelem); encoder.Token(MetadataTokens.GetToken(int64Ref));
                break;
            case GapBodyKind.LdelemaInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Newarr); encoder.Token(MetadataTokens.GetToken(int32Ref));
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldelema); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.LdobjInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Ldobj); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.StobjInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Ldc_i4_2);
                encoder.OpCode(ILOpCode.Stobj); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.InitobjInt32:
                encoder.OpCode(ILOpCode.Ldc_i4_0);
                encoder.OpCode(ILOpCode.Initobj); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.SizeofInt32:
                encoder.OpCode(ILOpCode.Sizeof); encoder.Token(MetadataTokens.GetToken(int32Ref));
                break;
            case GapBodyKind.SizeofDecimal:
                encoder.OpCode(ILOpCode.Sizeof); encoder.Token(MetadataTokens.GetToken(decimalRef));
                break;
            case GapBodyKind.JmpStatic:
                encoder.OpCode(ILOpCode.Ldc_i4_1);
                encoder.OpCode(ILOpCode.Ldc_i4_2);
                encoder.OpCode(ILOpCode.Jmp); encoder.Token(MetadataTokens.GetToken(target));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        encoder.OpCode(ILOpCode.Ret);
        il.Align(4);
        var stream = new MethodBodyStreamEncoder(il);
        var bodyOffset = stream.AddMethodBody(encoder, 3, default(StandaloneSignatureHandle), MethodBodyAttributes.None);
        var main = mdb.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.IL, mdb.GetOrAddString("Main"),
            mdb.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }), bodyOffset, default(ParameterHandle));
        mdb.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Class,
            mdb.GetOrAddString("Cases"), mdb.GetOrAddString("Gap"), objRef, default, main);
        _ = target;

        var root = new MetadataRootBuilder(mdb, "v4.0.30319");
        var peBuilder = new ManagedPEBuilder(new PEHeaderBuilder(
                machine: Machine.I386,
                imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            root, il);
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        return new PEReader(new MemoryStream(blob.ToArray()));
    }
}
