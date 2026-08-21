using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using System.Linq;
using Xunit;

//------------------------------------------------------------------------------
// switch (IL 0x45) folding: selector value → Ceq comparison chain
// (`if (sel == i) goto L_t`), chain tail is an unconditional goto to the default
// arm (out-of-range/negative falls to default = the next instruction in the
// stream after the switch). The decoder already produces absolute targets
// SwitchTargets (table order preserved, IlDecoderTests.Switch family); this group
// fixes StackFold's chain shape: table-order indexes 0..N-1 compared one by one
// with Ceq + a trailing unconditional default.
//------------------------------------------------------------------------------
public class StackFoldTestsSwitch
{
    [Fact]
    public void Fold_Switch_FourArms_ProducesCeqComparisonChain_WithDefaultFallthrough()
    {
        // enumdeep Main's real shape: ldloc.0; ldc.i4.1; sub; switch(4) (table
        // entries relative to instruction end 24 → absolute targets [32,26,30,28],
        // out-of-order preserved); default arm = next instruction in the stream @24.
        //   06 | 17 59 | 45 04 00 00 00 08 00 00 00 02 00 00 00 06 00 00 00 04 00 00 00
        //   @24 ldc.i4.0 ret(default) @26 ldc.i4.1 ret(arm1) @28 ldc.i4.2 ret(arm3)
        //   @30 ldc.i4.3 ret(arm2) @32 ldc.i4.4 ret(arm0)
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x06, 0x17, 0x59,
                0x45, 0x04, 0x00, 0x00, 0x00,
                0x08, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x06, 0x00, 0x00, 0x00,
                0x04, 0x00, 0x00, 0x00,
                0x16, 0x2A, // @24 default arm
                0x17, 0x2A, // @26 arm1
                0x18, 0x2A, // @28 arm3
                0x19, 0x2A, // @30 arm2
                0x1A, 0x2A, // @32 arm0
            }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        // Chain shape: 4 conditional Ceq branches (table order) + 1 unconditional
        // default branch + the default arm's stream
        var branches = stmts.OfType<BranchStmt>().ToArray();
        Assert.Equal(5, branches.Length);

        // Table order → index mapping: targets [32,26,30,28] ↔ values [0,1,2,3]
        Assert.Equal("L_32", branches[0].TargetLabel);
        Assert.Equal("L_26", branches[1].TargetLabel);
        Assert.Equal("L_30", branches[2].TargetLabel);
        Assert.Equal("L_28", branches[3].TargetLabel);
        for (var i = 0; i < 4; i++)
        {
            var cond = Assert.IsType<BinaryExpr>(branches[i].Condition);
            Assert.Equal(BinaryOp.Ceq, cond.Op);
            var sel = Assert.IsType<BinaryExpr>(cond.Left); // loc0 - 1 (real enumdeep's c-1 shape)
            Assert.Equal(BinaryOp.Sub, sel.Op);
            Assert.Equal("loc0", Assert.IsType<LocExpr>(sel.Left).Name);
            Assert.Equal(1L, Assert.IsType<IntConstExpr>(sel.Right).Value);
            Assert.Equal((long)i, Assert.IsType<IntConstExpr>(cond.Right).Value);
        }

        // Chain-tail unconditional goto default (L_24 = next instruction in the
        // stream after the switch)
        Assert.Null(branches[4].Condition);
        Assert.Equal("L_24", branches[4].TargetLabel);

        // The default arm's stream folds normally (sequential ldloc series pushes
        // the stack, ret pops the top = the last value 4)
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        Assert.Equal(4L, Assert.IsType<IntConstExpr>(ret.Value).Value);
    }

    [Fact]
    public void Fold_Switch_EmptyTable_ProducesSingleUnconditionalBranchToDefault()
    {
        // ldc.i4.0; switch N=0 (table end @6); ldc.i4.1; ret — empty table → direct
        // goto default.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x16,
                0x45, 0x00, 0x00, 0x00, 0x00,
                0x17, 0x2A,
            }),
            new MethodContext { ArgCount = 0 });

        var branches = stmts.OfType<BranchStmt>().ToArray();
        var single = Assert.Single(branches);
        Assert.Null(single.Condition);
        Assert.Equal("L_6", single.TargetLabel);
    }

    [Fact]
    public void Fold_Switch_UnmatchedValue_FallsThroughToDefaultArm()
    {
        // 2-arm out-of-order table [20,16] (entries relative to instruction end 14:
        // +6→20, +2→16); default = @14.
        // Structural guarantee: Ceq indexes cover 0..N-1 contiguously, chain tail is
        // an unconditional goto default — negative/out-of-range (sel < 0 or sel >= N)
        // makes every Ceq false → always lands on default.
        var stmts = StackFold.Fold(
            IlDecoder.DecodeMethod(new byte[]
            {
                0x06,
                0x45, 0x02, 0x00, 0x00, 0x00,
                0x06, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00,
                0x16, 0x2A, // @14 default arm
                0x17, 0x2A, // @16 arm0
                0x18, 0x2A, // @18 dead code
                0x19, 0x2A, // @20 arm1
            }),
            new MethodContext { ArgCount = 0, LocNames = new[] { "loc0" } });

        var branches = stmts.OfType<BranchStmt>().ToArray();
        Assert.Equal(3, branches.Length);
        Assert.Equal("L_20", branches[0].TargetLabel);
        Assert.Equal("L_16", branches[1].TargetLabel);
        for (var i = 0; i < 2; i++)
        {
            var cond = Assert.IsType<BinaryExpr>(branches[i].Condition);
            Assert.Equal(BinaryOp.Ceq, cond.Op);
            Assert.Equal("loc0", Assert.IsType<LocExpr>(cond.Left).Name);
            Assert.Equal((long)i, Assert.IsType<IntConstExpr>(cond.Right).Value);
        }
        Assert.Null(branches[2].Condition);
        Assert.Equal("L_14", branches[2].TargetLabel);
    }
}
