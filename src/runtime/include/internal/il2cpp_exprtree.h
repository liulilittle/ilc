#pragma once
//------------------------------------------------------------------------------
// il2cpp_exprtree.h -- System.Linq.Expressions minimal runtime support
// (exprtree.cpp).
//
// SCOPE (the taskcard T2xx exprtree): a minimal expression-tree object model
// for the int32 arithmetic subset:
//   * Expression.Constant(int32) -> a ConstantExpression node
//   * Expression.Add/Subtract/Multiply/Divide(Expression, Expression) ->
//     BinaryExpression nodes
//   * Expression.Lambda(Expression) -> a LambdaExpression node
//   * lambda.Compile() -> a Delegate (the lambda itself, tracked as
//     "System.Delegate")
//   * delegate.DynamicInvoke() -> the evaluated result as a BOXED int32
//
// OBJECT MODEL: every Expression node is a managed object
// (Il2CppExpressionObject -- header + kind + left + right + value) allocated
// through its GLOBAL class descriptor (System_Linq_Expressions_Expression_class
// / BinaryExpression_class / ConstantExpression_class / LambdaExpression_class
// -- the transpiler's BclAllocExpr fallback emits &System_Linq_Expressions_*_class
// for newobj). The left/right slots are MANAGED REFERENCES (the collector
// scans them through refFieldBitmap bits 1,2 and rewrites them on Gen0
// compaction); the value slot is an int32 (constant nodes).
//
// The factory icalls are STATIC (Expression.Add etc. are static methods):
// they allocate a node and return it. The Compile/DynamicInvoke icalls are
// instance methods on the lambda/delegate.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

#include <cstdint>

// System.Linq.Expressions runtime descriptors (GLOBAL scope -- the transpiler
// emits &System_Linq_Expressions_*_class for newobj/isinst/castclass; defined
// in exprtree.cpp).
extern il2cpp::Il2CppClass System_Linq_Expressions_Expression_class;
extern il2cpp::Il2CppClass System_Linq_Expressions_BinaryExpression_class;
extern il2cpp::Il2CppClass System_Linq_Expressions_ConstantExpression_class;
extern il2cpp::Il2CppClass System_Linq_Expressions_LambdaExpression_class;

namespace il2cpp { namespace runtime {

// Managed Expression-node layout: object header + kind + left + right + value.
// The left/right slots are MANAGED REFERENCES (refFieldBitmap bits 1,2); the
// value slot is an int32 (constant nodes).
struct Il2CppExpressionObject
{
    Il2CppObject obj;    // object header (klass pointer, offset 0)
    int32_t kind;        // 0 = constant, 1 = add, 2 = sub, 3 = mul, 4 = div,
                         // 5 = lambda
    Il2CppObject* left;  // slot 2 (offset 16) -- GC reference (binary left /
                         // lambda body)
    Il2CppObject* right; // slot 3 (offset 24) -- GC reference (binary right)
    int32_t value;       // constant value (constant nodes)
};

// Register the four Expression descriptors once (idempotent).
void il2cpp_exprtree_register_classes();

// --- static factories (allocate + return) -----------------------------------

// Expression.Constant(object value): a ConstantExpression node. The value is
// a BOXED int32 (csc boxes the int literal -- Expression.Constant(object));
// the icall unboxes it.
Il2CppObject* icall_expr_constant(Il2CppObject* value);

// Expression.Add/Subtract/Multiply/Divide(Expression, Expression):
// BinaryExpression nodes.
Il2CppObject* icall_expr_add(Il2CppObject* left, Il2CppObject* right);
Il2CppObject* icall_expr_sub(Il2CppObject* left, Il2CppObject* right);
Il2CppObject* icall_expr_mul(Il2CppObject* left, Il2CppObject* right);
Il2CppObject* icall_expr_div(Il2CppObject* left, Il2CppObject* right);

// Expression.Lambda(Expression body): a LambdaExpression node (the body is
// stored in the left slot).
Il2CppObject* icall_expr_lambda(Il2CppObject* body);

// --- compile / invoke -------------------------------------------------------

// LambdaExpression.Compile(): returns the lambda itself (tracked as
// "System.Delegate" by the transpiler -- the delegate IS the lambda).
Il2CppObject* icall_expr_compile(Il2CppObject* self);

// Delegate.DynamicInvoke(): evaluates the lambda's body and returns the
// result as a BOXED int32 (the .NET DynamicInvoke object return).
Il2CppObject* icall_expr_dynamic_invoke(Il2CppObject* self);

}} // namespace il2cpp::runtime
