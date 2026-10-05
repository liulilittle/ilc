//------------------------------------------------------------------------------
// exprtreecase.cs -- System.Linq.Expressions minimal e2e oracle. The
// transpiler's exprtree domain (il2cpp_exprtree.h) wires the int32
// constant/arithmetic expression-tree subset: Expression.Constant/Add/
// Subtract/Multiply/Divide/Lambda, LambdaExpression.Compile and
// Delegate.DynamicInvoke. Every output is a deterministic bool; expected.txt
// is generated from a net48 reference run.
//------------------------------------------------------------------------------

using System;
using System.Linq.Expressions;

class ExprTreeCase
{
    static void Main()
    {
        // ---- 1. Constant + Add ----
        var expr = Expression.Add(Expression.Constant(2), Expression.Constant(3));
        var lambda = Expression.Lambda(expr);
        var del = lambda.Compile();
        var result = del.DynamicInvoke();
        Console.WriteLine((int)result == 5);

        // ---- 2. Nested: (2 + 3) * 4 ----
        var expr2 = Expression.Multiply(
            Expression.Add(Expression.Constant(2), Expression.Constant(3)),
            Expression.Constant(4));
        var lambda2 = Expression.Lambda(expr2);
        var result2 = lambda2.Compile().DynamicInvoke();
        Console.WriteLine((int)result2 == 20);

        // ---- 3. Subtract / Divide ----
        var expr3 = Expression.Subtract(Expression.Constant(10), Expression.Constant(3));
        var result3 = Expression.Lambda(expr3).Compile().DynamicInvoke();
        Console.WriteLine((int)result3 == 7);
        var expr4 = Expression.Divide(Expression.Constant(10), Expression.Constant(2));
        var result4 = Expression.Lambda(expr4).Compile().DynamicInvoke();
        Console.WriteLine((int)result4 == 5);
    }
}
