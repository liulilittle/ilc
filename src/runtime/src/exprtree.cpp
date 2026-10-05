//------------------------------------------------------------------------------
// exprtree.cpp -- System.Linq.Expressions minimal runtime support (see
// il2cpp_exprtree.h).
//
// The Expression nodes are managed objects (Il2CppExpressionObject -- header
// + kind + left + right + value). The left/right slots are MANAGED REFERENCES
// (refFieldBitmap bits 1,2 -- the collector scans and rewrites them on Gen0
// compaction); the value slot is an int32 (constant nodes). The four class
// descriptors are GLOBAL (the transpiler emits &System_Linq_Expressions_*_class
// for newobj).
//------------------------------------------------------------------------------

#include "il2cpp_exprtree.h"

#include "il2cpp_boxing_ext.h" // il2cpp_box_int32
#include "il2cpp_runtime.h"    // il2cpp_object_new / il2cpp_class_register

#include <cstdint>

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope). All four share the Il2CppExpressionObject
// layout; the left/right slots are GC references (refFieldBitmap bits 1,2).
//------------------------------------------------------------------------------
#define EXPR_CLASS(SYM, DOTNAME) \
    ::il2cpp::Il2CppClass SYM = {   \
        DOTNAME,                                     \
        static_cast<int32_t>(sizeof(::il2cpp::runtime::Il2CppExpressionObject)), \
        0b110u,                                      \
        2,                                           \
        &System_Object_class,                        \
        nullptr, 0, nullptr, 0,                      \
    }

EXPR_CLASS(System_Linq_Expressions_Expression_class, "System.Linq.Expressions.Expression");
EXPR_CLASS(System_Linq_Expressions_BinaryExpression_class, "System.Linq.Expressions.BinaryExpression");
EXPR_CLASS(System_Linq_Expressions_ConstantExpression_class, "System.Linq.Expressions.ConstantExpression");
EXPR_CLASS(System_Linq_Expressions_LambdaExpression_class, "System.Linq.Expressions.LambdaExpression");

#undef EXPR_CLASS

namespace il2cpp { namespace runtime {

namespace {

constexpr int32_t kKindConstant = 0;
constexpr int32_t kKindAdd = 1;
constexpr int32_t kKindSub = 2;
constexpr int32_t kKindMul = 3;
constexpr int32_t kKindDiv = 4;
constexpr int32_t kKindLambda = 5;

bool g_exprClassesRegistered = false;

Il2CppExpressionObject* AsExpr(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppExpressionObject*>(self);
}

// Evaluate the expression tree rooted at 'node' (int32 result).
int32_t Evaluate(Il2CppObject* node)
{
    Il2CppExpressionObject* e = AsExpr(node);
    switch (e->kind)
    {
    case kKindConstant:
        return e->value;
    case kKindAdd:
        return Evaluate(e->left) + Evaluate(e->right);
    case kKindSub:
        return Evaluate(e->left) - Evaluate(e->right);
    case kKindMul:
        return Evaluate(e->left) * Evaluate(e->right);
    case kKindDiv:
        return Evaluate(e->left) / Evaluate(e->right);
    case kKindLambda:
        return Evaluate(e->left); // the body
    default:
        return 0;
    }
}

} // namespace

void il2cpp_exprtree_register_classes()
{
    if (g_exprClassesRegistered)
    {
        return;
    }
    il2cpp_class_register(&System_Linq_Expressions_Expression_class);
    il2cpp_class_register(&System_Linq_Expressions_BinaryExpression_class);
    il2cpp_class_register(&System_Linq_Expressions_ConstantExpression_class);
    il2cpp_class_register(&System_Linq_Expressions_LambdaExpression_class);
    g_exprClassesRegistered = true;
}

//------------------------------------------------------------------------------
// Static factories.
//------------------------------------------------------------------------------
Il2CppObject* icall_expr_constant(Il2CppObject* value)
{
    il2cpp_exprtree_register_classes();
    Il2CppObject* obj = il2cpp_object_new(&System_Linq_Expressions_ConstantExpression_class);
    Il2CppExpressionObject* e = AsExpr(obj);
    e->kind = kKindConstant;
    e->value = il2cpp::il2cpp_unbox_int32(value);
    return obj;
}

Il2CppObject* MakeBinary(int32_t kind, Il2CppObject* left, Il2CppObject* right)
{
    il2cpp_exprtree_register_classes();
    Il2CppObject* obj = il2cpp_object_new(&System_Linq_Expressions_BinaryExpression_class);
    Il2CppExpressionObject* e = AsExpr(obj);
    e->kind = kind;
    e->left = left;
    e->right = right;
    return obj;
}

Il2CppObject* icall_expr_add(Il2CppObject* left, Il2CppObject* right)
{
    return MakeBinary(kKindAdd, left, right);
}

Il2CppObject* icall_expr_sub(Il2CppObject* left, Il2CppObject* right)
{
    return MakeBinary(kKindSub, left, right);
}

Il2CppObject* icall_expr_mul(Il2CppObject* left, Il2CppObject* right)
{
    return MakeBinary(kKindMul, left, right);
}

Il2CppObject* icall_expr_div(Il2CppObject* left, Il2CppObject* right)
{
    return MakeBinary(kKindDiv, left, right);
}

Il2CppObject* icall_expr_lambda(Il2CppObject* body)
{
    il2cpp_exprtree_register_classes();
    Il2CppObject* obj = il2cpp_object_new(&System_Linq_Expressions_LambdaExpression_class);
    Il2CppExpressionObject* e = AsExpr(obj);
    e->kind = kKindLambda;
    e->left = body;
    return obj;
}

//------------------------------------------------------------------------------
// Compile / invoke.
//------------------------------------------------------------------------------
Il2CppObject* icall_expr_compile(Il2CppObject* self)
{
    // The compiled delegate IS the lambda (the transpiler tracks the result
    // as "System.Delegate"; DynamicInvoke evaluates the lambda's body).
    return self;
}

Il2CppObject* icall_expr_dynamic_invoke(Il2CppObject* self)
{
    const int32_t result = Evaluate(self);
    return il2cpp::il2cpp_box_int32(nullptr, result);
}

}} // namespace il2cpp::runtime
