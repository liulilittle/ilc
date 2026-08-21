using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Xunit;

//------------------------------------------------------------------------------
// conv.u8 from an int32 source ZERO-extends (int32 -> uint32 -> int64) --
// convertcase L26 root cause. csc emits `ldc.i4 0x80000000; conv.u8` for an
// int64 constant whose low 32 bits are >= 2^31 (2147483648L): the int32 slot
// holds the SIGNED bit pattern (-2147483648), conv.u8 reinterprets it as
// uint32 and widens UNSIGNED to +2147483648. Treating conv.u8 as an identity
// (sign-extension) silently flipped the constant, so
// Convert.ToInt32(2147483648L) passed -2147483648 (in-range) and never threw
// the OverflowException the checked icall is documented to throw.
//------------------------------------------------------------------------------
public class StackFoldTestsConvU8
{
    [Fact]
    public void Fold_ConvU8_NegativeInt32Const_ZeroExtends()
    {
        // ldc.i4 0x80000000 (int32 -2147483648); conv.u8; ret
        // -> Int64ConstExpr(2147483648), NOT -2147483648 (sign extension).
        // (conv.u8 is byte 0x6E -- 0x6B is conv.r4; verified against the real
        // convertcase.exe IL: Ldc_i4(-2147483648) @0x01FA + Conv_u8 @0x01FF.)
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x00, 0x00, 0x00, 0x80, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var c = Assert.IsType<Int64ConstExpr>(ret.Value);
        Assert.Equal(2147483648L, c.Value);
    }

    [Fact]
    public void Fold_ConvU8_MinusOne_ZeroExtendsToUInt32Max()
    {
        // ldc.i4.m1; conv.u8; ret -> (long)(uint)-1 == 4294967295
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x15, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var c = Assert.IsType<Int64ConstExpr>(ret.Value);
        Assert.Equal(4294967295L, c.Value);
    }

    [Fact]
    public void Fold_ConvU8_PositiveInt32Const_Unchanged()
    {
        // ldc.i4 42; conv.u8; ret -> Int64ConstExpr(42) (zero-extension is an
        // identity for non-negative int32 values; the int64 TYPE still flows).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x20, 0x2A, 0x00, 0x00, 0x00, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0 });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var c = Assert.IsType<Int64ConstExpr>(ret.Value);
        Assert.Equal(42L, c.Value);
    }

    [Fact]
    public void Fold_ConvU8_Uint32Local_ZeroExtends()
    {
        // ldloc.0 (uint32); conv.u8; ret -- valdeep L26 var path: the uint32
        // local lives in an int32 slot; conv.u8 must zero-extend (icall),
        // NOT stay an identity ((ulong)u1 == 4294967295UL needs 0xFFFFFFFF ->
        // 4294967295, not -1). OptimizeTemps inlines the single-use temp.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "i" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_u8_i4", conv.Function);
        Assert.Equal("l", conv.ResultKind);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(conv.Args[0]).Name);
    }

    [Fact]
    public void Fold_ConvU8_Int64Local_Identity()
    {
        // ldloc.0 (int64); conv.u8; ret -> bit identity, NO zero-extension
        // (an int64/uint64 value is already 64 bits; extending it would
        // truncate). The local passes through untouched.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "l" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("loc0", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    [Fact]
    public void Fold_ConvU8_Uint32Arg_ZeroExtends()
    {
        // ldarg.0 (uint32); conv.u8; ret -- the ARG path: ParamTypeNames "i"
        // means a 32-bit slot -> zero-extend. (Static: arg0 == param 0.)
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 1, IsStatic = true, ParamTypeNames = new[] { "i" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_u8_i4", conv.Function);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(conv.Args[0]).Name);
    }

    [Fact]
    public void Fold_ConvU8_Int64Arg_Identity()
    {
        // ldarg.0 (int64); conv.u8; ret -- an int64 ARG is a bit identity
        // (ParamTypeNames "l"); zero-extending it would truncate.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x02, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 1, IsStatic = true, ParamTypeNames = new[] { "l" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal("arg0", Assert.IsType<LocExpr>(ret.Value).Name);
    }

    [Fact]
    public void Fold_ConvU8_Int32Arithmetic_ZeroExtends()
    {
        // ldloc.0; ldloc.1; add (int32 arithmetic); conv.u8; ret -- a 32-bit
        // BinaryExpr result zero-extends the SAME way (uint arithmetic
        // result (ulong)(a + b) must widen unsigned).
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[] { 0x06, 0x07, 0x58, 0x6E, 0x2A }),
            new MethodContext { ArgCount = 0, LocalKinds = new[] { "i", "i" } });

        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var conv = Assert.IsType<CallExpr>(ret.Value);
        Assert.Equal("il2cpp::runtime::icall_conv_u8_i4", conv.Function);
        var bin = Assert.IsType<BinaryExpr>(conv.Args[0]);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }
}
