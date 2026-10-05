namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ExprTree -- System.Linq.Expressions minimal icall mappings
// (il2cpp_exprtree.h, implementation exprtree.cpp).
// Owners: System.Linq.Expressions.Expression / LambdaExpression
//
// OBJECT MODEL: every Expression node is a managed object
// (Il2CppExpressionObject -- header + kind + left + right + value) allocated
// through its GLOBAL class descriptor (System_Linq_Expressions_*_class -- the
// transpiler's BclAllocExpr fallback emits &System_Linq_Expressions_*_class
// for newobj). The left/right slots are MANAGED REFERENCES (refFieldBitmap
// bits 1,2 -- the collector scans and rewrites them on Gen0 compaction).
//
// The factory methods (Expression.Constant/Add/Subtract/Multiply/Divide/
// Lambda) are STATIC: they allocate a node and return it. The Expression
// operands pass as "" (the objects ARE the pointers); the Constant int32
// passes as "i". The results are tracked with ReturnTypeName so the
// receiver-aware re-resolve dispatches the follow-up calls (a BinaryExpression
// receiver for nested trees, a LambdaExpression receiver for Compile, a
// "System.Delegate" receiver for DynamicInvoke -- the latter mapped in
// MethodMap.Delegate.cs).
//
// Wired surface:
//   * Expression.Constant(int32) -> ConstantExpression
//   * Expression.Add/Subtract/Multiply/Divide(Expression, Expression) ->
//     BinaryExpression
//   * Expression.Lambda(Expression) -> LambdaExpression
//   * LambdaExpression.Compile() -> "System.Delegate" (the lambda itself)
//   * Delegate.DynamicInvoke() -> boxed int32 (MethodMap.Delegate.cs)
//
// Known-unwireable members return null (the generated-name path):
//   * Expression.Parameter / Variable / the parameterized lambda surface --
//     out of scope (the int32 constant/arithmetic subset only).
//   * Compile<TDelegate>() -- the generic compile is out of scope (the
//     non-generic Compile() only).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapExprTree(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // static ConstantExpression Constant(object value): csc boxes the
            // int literal (Expression.Constant(object)); the icall unboxes it.
            case "Constant":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_expr_constant", 1, true, true, false, new[] { "" },
                        returnTypeName: "System.Linq.Expressions.ConstantExpression");
                return null;
            // static BinaryExpression Add/Subtract/Multiply/Divide(Expression,
            // Expression).
            case "Add":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_expr_add", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Linq.Expressions.BinaryExpression");
                return null;
            case "Subtract":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_expr_sub", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Linq.Expressions.BinaryExpression");
                return null;
            case "Multiply":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_expr_mul", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Linq.Expressions.BinaryExpression");
                return null;
            case "Divide":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                    return I("il2cpp::runtime::icall_expr_div", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Linq.Expressions.BinaryExpression");
                return null;
            // static LambdaExpression Lambda(Expression body, params
            // ParameterExpression[] parameters): csc emits the params array
            // (Array.Empty<ParameterExpression>() -- the "drop" cast discards
            // it; the runtime evaluates the parameterless lambda).
            case "Lambda":
                if (owner == "System.Linq.Expressions.Expression" && ps.Length == 2 && ps[0] == 0x12 && ps[1] == 0x1D)
                    return I("il2cpp::runtime::icall_expr_lambda", 2, true, true, false, new[] { "", "drop" },
                        returnTypeName: "System.Linq.Expressions.LambdaExpression");
                return null;
            // Delegate Compile(): the lambda itself, tracked as
            // "System.Delegate" (DynamicInvoke dispatches on that receiver).
            // ReturnsRef=true (the result is a managed reference).
            case "Compile":
                if (owner == "System.Linq.Expressions.LambdaExpression" && ps.Length == 0)
                    return I("il2cpp::runtime::icall_expr_compile", 0, true, true, true, null,
                        returnTypeName: "System.Delegate");
                return null;
            default: return null;
        }
    }
}
