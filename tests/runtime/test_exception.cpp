//------------------------------------------------------------------------------
// test_exception.cpp -- managed exception transport unit tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_throw_new throws a DotNetException catchable by C++ catch;
//      type name and message round-trip.
//   2. il2cpp_exception_matches: exact match, base-chain match
//      ("System.DivideByZeroException" chain contains "System.Exception"),
//      and mismatch.
//   2b. EXC2 audit: both chain layouts conform -- the exceptionext/transpiler
//      layout repeats the type ("{Type},{Type},{Base1},...") and the
//      buffer.cpp layout lists it once ("{Type},{Base1},..."); the matcher
//      walks every segment INCLUDING the first, so an exact-type catch
//      matches either way (and a base catch matches a later segment).
//   3. Rethrow preserves the exception (an inner catch that doesn't match
//      rethrows; the outer handler receives the same type/message).
//   4. finally-equivalent RAII: the guard destructor runs on both the normal
//      and the exceptional path (verified via a counter).
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_exception.h"

#include <cstdio>
#include <cstring>

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

// RAII guard emulating a generated finally block: the destructor bumps a
// counter, proving it runs on every exit path (normal and exceptional).
struct FinallyGuard
{
    int* counter;
    explicit FinallyGuard(int* c) : counter(c) {}
    ~FinallyGuard() { ++(*counter); }
};

} // namespace

int main()
{
    // --- 1. Throw + catch round-trip ---
    int caught = 0;
    try
    {
        il2cpp::il2cpp_throw_new("System.DivideByZeroException", "divide by zero");
    }
    catch (const il2cpp::DotNetException& e)
    {
        caught = 1;
        Expect(std::strcmp(e.type_name(), "System.DivideByZeroException") == 0,
               "exception type name round-trips");
        Expect(std::strcmp(e.message_text(), "divide by zero") == 0,
               "exception message round-trips");
    }
    Expect(caught == 1, "throw is caught by a C++ catch(DotNetException&)");

    // --- 2. Type matching ---
    Expect(il2cpp::il2cpp_exception_matches("System.DivideByZeroException", "System.DivideByZeroException"),
           "exact type match");
    Expect(il2cpp::il2cpp_exception_matches("System.Exception", "System.DivideByZeroException,System.Exception"),
           "base-chain match (Exception catches DivideByZeroException)");
    Expect(!il2cpp::il2cpp_exception_matches("System.InvalidOperationException", "System.DivideByZeroException,System.Exception"),
           "unrelated type does not match");
    Expect(!il2cpp::il2cpp_exception_matches(nullptr, "System.Exception"),
           "null caught type never matches");

    // --- 2b. EXC2 audit: the matcher walks EVERY segment including the first,
    // so BOTH chain layouts conform (.NET catch semantics):
    //   * the exceptionext / transpiler layout repeats the type
    //     ("{Type},{Type},{Base1},..." -- CodeGenerator.ExceptionChain);
    //   * the buffer.cpp layout lists the type ONCE as the first segment
    //     ("{Type},{Base1},...").
    // A catch of the thrown type itself must match either way (the first
    // segment IS the thrown type); a base catch matches a later segment.
    // Regression: pre-audit, the walk skipped the first segment, so a
    // buffer.cpp-style chain failed an exact-type catch (the exception
    // escaped to the top-level guard as unhandled). ---
    Expect(il2cpp::il2cpp_exception_matches("System.ArgumentNullException",
           "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "buffer-style chain (type once): exact-type catch matches the first segment");
    Expect(il2cpp::il2cpp_exception_matches("System.ArgumentException",
           "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "buffer-style chain: base catch matches a later segment");
    Expect(il2cpp::il2cpp_exception_matches("System.Exception",
           "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "buffer-style chain: root catch matches the terminal segment");
    Expect(il2cpp::il2cpp_exception_matches("System.ArgumentNullException",
           "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "exceptionext-style chain (type repeated): exact-type catch still matches");
    Expect(!il2cpp::il2cpp_exception_matches("System.FormatException",
           "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "buffer-style chain: unrelated type never matches");
    Expect(!il2cpp::il2cpp_exception_matches("System.Argument",
           "System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception"),
           "partial type-name prefix never matches (whole-segment compare)");

    // --- 3. Rethrow preserves the exception ---
    int rethrown = 0;
    try
    {
        try
        {
            il2cpp::il2cpp_throw_new("System.ArithmeticException", "arith");
        }
        catch (const il2cpp::DotNetException& e)
        {
            // This inner handler does NOT match the type -> rethrow.
            if (!il2cpp::il2cpp_exception_matches("System.InvalidOperationException", e.type_name()))
            {
                throw;
            }
        }
    }
    catch (const il2cpp::DotNetException& e)
    {
        rethrown = 1;
        Expect(std::strcmp(e.type_name(), "System.ArithmeticException") == 0,
               "rethrow preserves the exception type");
    }
    Expect(rethrown == 1, "inner non-matching catch rethrows to the outer handler");

    // --- 4. Finally-equivalent RAII runs on every path ---
    int finallyRuns = 0;
    {
        FinallyGuard guard(&finallyRuns);
        // normal path: the guard's destructor runs at scope exit
    }
    Expect(finallyRuns == 1, "RAII finally guard runs on the normal path");
    try
    {
        FinallyGuard guard(&finallyRuns);
        il2cpp::il2cpp_throw_new("System.Exception", "boom");
    }
    catch (const il2cpp::DotNetException&)
    {
        // exceptional path: the guard's destructor runs during unwinding
    }
    Expect(finallyRuns == 2, "RAII finally guard runs on the exceptional path");

    // --- 5. Audit H9: INT_MIN / -1 and INT_MIN % -1 throw OverflowException
    // (the quotient 2^31 is not representable in int32; raw C++ division is
    // UB and SIGFPEs on x86). ---
    int divOverflow = 0;
    try
    {
        il2cpp::il2cpp_checked_div(INT32_MIN, -1);
    }
    catch (const il2cpp::DotNetException& e)
    {
        divOverflow = 1;
        Expect(il2cpp::il2cpp_exception_matches("System.OverflowException", e.type_name()),
               "INT_MIN / -1 throws a catchable OverflowException");
    }
    Expect(divOverflow == 1, "INT_MIN / -1 throws (does not SIGFPE)");
    int remOverflow = 0;
    try
    {
        il2cpp::il2cpp_checked_rem(INT32_MIN, -1);
    }
    catch (const il2cpp::DotNetException&)
    {
        remOverflow = 1;
    }
    Expect(remOverflow == 1, "INT_MIN % -1 throws OverflowException");
    // Normal division still works (regression).
    Expect(il2cpp::il2cpp_checked_div(INT32_MIN, 1) == INT32_MIN, "INT_MIN / 1 is unchanged");
    Expect(il2cpp::il2cpp_checked_div(-7, 2) == -3, "plain division is unchanged");
    Expect(il2cpp::il2cpp_checked_rem(-7, 2) == -1, "plain remainder is unchanged");

    // --- 6. D-INT-1: checked add/sub/mul helpers (add.ovf/sub.ovf/mul.ovf).
    // Throw OverflowException on wrap; in-range + boundary values pass. ---
    int ovfThrown = 0;
    try { il2cpp::il2cpp_checked_add(INT32_MAX, 1); }
    catch (const il2cpp::DotNetException& e)
    {
        ovfThrown = 1;
        Expect(il2cpp::il2cpp_exception_matches("System.OverflowException", e.type_name()),
               "INT_MAX + 1 (checked) throws OverflowException");
    }
    Expect(ovfThrown == 1, "checked add overflow throws");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_sub(INT32_MIN, 1); }
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "checked sub overflow throws");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_mul(INT32_MAX, 2); }
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "checked mul overflow throws");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_mul(INT32_MIN, -1); }
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "INT_MIN * -1 (checked) throws OverflowException");
    // In-range + boundary values pass unchanged.
    Expect(il2cpp::il2cpp_checked_add(INT32_MAX, 0) == INT32_MAX, "checked add in-range unchanged");
    Expect(il2cpp::il2cpp_checked_add(INT32_MAX, -1) == INT32_MAX - 1, "checked add boundary passes");
    Expect(il2cpp::il2cpp_checked_add(INT32_MIN, 1) == INT32_MIN + 1, "checked add below-max passes");
    Expect(il2cpp::il2cpp_checked_sub(INT32_MIN, 0) == INT32_MIN, "checked sub in-range unchanged");
    Expect(il2cpp::il2cpp_checked_sub(INT32_MIN, -1) == INT32_MIN + 1, "checked sub boundary passes");
    Expect(il2cpp::il2cpp_checked_mul(INT32_MAX, 1) == INT32_MAX, "checked mul in-range unchanged");
    Expect(il2cpp::il2cpp_checked_mul(INT32_MAX, -1) == INT32_MIN + 1, "checked mul of INT_MAX by -1 passes");

    // --- 7. D-INT-1: the _un forms compare UNSIGNED (bit patterns in the
    // int32 slot; uint32 value rides int32 bits). ---
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_add_un(-1, 1); } // 0xFFFFFFFF + 1
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "checked add_un wraps past UINT32_MAX -> throws");
    Expect(il2cpp::il2cpp_checked_add_un(INT32_MAX, INT32_MIN) == -1,
           "checked add_un at UINT32_MAX boundary passes (all-one bits)");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_sub_un(0, 1); } // 0 - 1 unsigned underflow
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "checked sub_un unsigned underflow throws");
    Expect(il2cpp::il2cpp_checked_sub_un(-1, 1) == -2, "checked sub_un in-range passes (0xFFFFFFFE)");
    Expect(il2cpp::il2cpp_checked_sub_un(0, 0) == 0, "checked sub_un zero passes");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_mul_un(65536, 65536); } // 2^32 exceeds uint32
    catch (const il2cpp::DotNetException&) { ovfThrown = 1; }
    Expect(ovfThrown == 1, "checked mul_un overflow throws");
    Expect(il2cpp::il2cpp_checked_mul_un(65535, 65535) == -131071,
           "checked mul_un 65535^2 passes (0xFFFFFF01)");
    Expect(il2cpp::il2cpp_checked_mul_un(0, INT32_MAX) == 0, "checked mul_un by zero passes");
    // rem.un: unsigned remainder; divide-by-zero still throws.
    Expect(il2cpp::il2cpp_checked_rem_un(7, 3) == 1, "checked rem_un plain remainder");
    Expect(il2cpp::il2cpp_checked_rem_un(-1, 4) == 3, "checked rem_un unsigned modulo (0xFFFFFFFF % 4)");
    ovfThrown = 0;
    try { il2cpp::il2cpp_checked_rem_un(5, 0); }
    catch (const il2cpp::DotNetException& e)
    {
        ovfThrown = 1;
        Expect(il2cpp::il2cpp_exception_matches("System.DivideByZeroException", e.type_name()),
               "checked rem_un by zero throws DivideByZeroException");
    }
    Expect(ovfThrown == 1, "checked rem_un by zero throws");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d exception assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime exception tests passed\n");
    return 0;
}
