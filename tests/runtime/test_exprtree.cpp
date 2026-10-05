//------------------------------------------------------------------------------
// test_exprtree.cpp -- System.Linq.Expressions minimal runtime unit tests.
//
// Coverage (normal + edge paths), mirroring the functions declared in
// il2cpp_exprtree.h:
//   1. icall_expr_constant: a constant node evaluates to its value.
//   2. icall_expr_add/sub/mul/div: binary nodes evaluate to the arithmetic
//      result.
//   3. Nested trees: (2 + 3) * 4 evaluates to 20.
//   4. icall_expr_lambda + compile + dynamic_invoke: a lambda over a body
//      evaluates to the body's result (the .NET Compile/DynamicInvoke path).
//   5. The dynamic_invoke result is a BOXED int32 (il2cpp_unbox_int32 reads
//      it back).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_exprtree.h"
#include "il2cpp_runtime.h" // il2cpp_unbox_int32

#include <cstdint>
#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Box an int32 (Expression.Constant takes a boxed object).
il2cpp::Il2CppObject* Box(int32_t v) { return il2cpp::il2cpp_box_int32(nullptr, v); }

// Evaluate a node via the DynamicInvoke path and unbox the int result.
int32_t Eval(il2cpp::Il2CppObject* node)
{
    il2cpp::Il2CppObject* boxed = il2cpp::runtime::icall_expr_dynamic_invoke(node);
    return il2cpp::il2cpp_unbox_int32(boxed);
}

} // namespace

int main()
{
    using il2cpp::runtime::icall_expr_constant;
    using il2cpp::runtime::icall_expr_add;
    using il2cpp::runtime::icall_expr_sub;
    using il2cpp::runtime::icall_expr_mul;
    using il2cpp::runtime::icall_expr_div;
    using il2cpp::runtime::icall_expr_lambda;
    using il2cpp::runtime::icall_expr_compile;

    // --- 1. Constants ---
    {
        Expect(Eval(icall_expr_constant(Box(5))) == 5, "constant 5 evaluates to 5");
        Expect(Eval(icall_expr_constant(Box(0))) == 0, "constant 0 evaluates to 0");
        Expect(Eval(icall_expr_constant(Box(-7))) == -7, "constant -7 evaluates to -7");
    }

    // --- 2. Binary operators ---
    {
        Expect(Eval(icall_expr_add(icall_expr_constant(Box(2)), icall_expr_constant(Box(3)))) == 5,
               "2 + 3 = 5");
        Expect(Eval(icall_expr_sub(icall_expr_constant(Box(5)), icall_expr_constant(Box(3)))) == 2,
               "5 - 3 = 2");
        Expect(Eval(icall_expr_mul(icall_expr_constant(Box(3)), icall_expr_constant(Box(4)))) == 12,
               "3 * 4 = 12");
        Expect(Eval(icall_expr_div(icall_expr_constant(Box(10)), icall_expr_constant(Box(2)))) == 5,
               "10 / 2 = 5");
    }

    // --- 3. Nested trees ---
    {
        // (2 + 3) * 4 = 20
        il2cpp::Il2CppObject* sum = icall_expr_add(icall_expr_constant(Box(2)), icall_expr_constant(Box(3)));
        Expect(Eval(icall_expr_mul(sum, icall_expr_constant(Box(4)))) == 20,
               "(2 + 3) * 4 = 20");
        // 10 - (2 + 3) = 5
        Expect(Eval(icall_expr_sub(icall_expr_constant(Box(10)), sum)) == 5,
               "10 - (2 + 3) = 5");
    }

    // --- 4. Lambda + compile + dynamic_invoke ---
    {
        il2cpp::Il2CppObject* body = icall_expr_add(icall_expr_constant(Box(2)), icall_expr_constant(Box(3)));
        il2cpp::Il2CppObject* lambda = icall_expr_lambda(body);
        il2cpp::Il2CppObject* del = icall_expr_compile(lambda);
        Expect(il2cpp::runtime::icall_expr_dynamic_invoke(del) != nullptr,
               "DynamicInvoke returns a non-null boxed result");
        Expect(Eval(del) == 5, "lambda over (2 + 3) evaluates to 5");
    }

    // --- 5. Boxed result ---
    {
        il2cpp::Il2CppObject* boxed = il2cpp::runtime::icall_expr_dynamic_invoke(
            icall_expr_constant(Box(42)));
        Expect(il2cpp::il2cpp_unbox_int32(boxed) == 42,
               "DynamicInvoke result unboxes to the int value");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d exprtree assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime exprtree tests passed\n");
    return 0;
}
