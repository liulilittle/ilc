#pragma once
//------------------------------------------------------------------------------
// il2cpp_exception.h -- managed exception transport (M3 Task 6).
//
// .NET exceptions are typed (System.Exception subclasses). M3 transports them
// through C++ native exceptions with a TYPE TAG + message:
//
//   class DotNetException : public std::exception
//   {
//       const char* m_typeName; // the managed exception type ("System.Exception")
//       const char* m_message;  // stable diagnostic text
//   };
//
//   Generated try/catch maps to C++ try { } catch (il2cpp::DotNetException& e);
//   the runtime's type matcher (il2cpp_exception_is) decides whether the
//   caught type matches the thrown type -- a mismatch RETHROWS (C++ rethrow
//   preserves the exception; unwinding continues to the outer handler).
//
//   finally blocks map to RAII guards whose destructor runs the finally body
//   (C++ unwinding guarantees it runs on both normal and exceptional paths;
//   .NET's "finally throwing replaces the in-flight exception" is handled by
//   the guard's destructor swallowing the in-flight DotNetException and
//   rethrowing the finally's exception -- see the transpiler task's shape).
//
//   Throw sites: null check (NRE), array bounds (IOORE), unbox type (ICE),
//   and explicit throw IL all funnel through il2cpp_throw_new. The top-level
//   guard (main wrapper) catches escapes and reports the message with a
//   non-zero exit (never silent -- user iron rule).
//
//   M3 scope: typed exceptions with exact-or-base matching; the managed
//   Exception OBJECT (GC) and StackTrace arrive with the exception-object
//   task (Task 10); this carrier's contract is stable.
//------------------------------------------------------------------------------

#include <exception>
#include <cstdint>

namespace il2cpp {

// The C++ carrier for a managed exception (see header comment).
class DotNetException : public std::exception
{
public:
    DotNetException(const char* typeName, const char* message)
        : m_typeName(typeName != nullptr ? typeName : "System.Exception"),
          m_message(message != nullptr ? message : "managed exception")
    {
    }

    // what(): the message text (libc++/libstdc++-portable: the message is
    // owned by this class, not by std::exception's internal storage).
    const char* what() const noexcept override { return m_message; }

    const char* type_name() const { return m_typeName; }
    const char* message_text() const { return m_message; }

private:
    const char* m_typeName;
    const char* m_message;
};

// Throw a managed exception of 'typeName' with 'message' (see header).
// Both strings must be stable (static literals; generated code passes
// compile-time constants).
// [[noreturn]]: the implementation is a single `throw DotNetException` and
// NEVER returns. The annotation lets the compiler see the true control flow
// of the transpiler's try/finally restructuring (a finally-protected return
// carries its value in a local assigned inside the matching catch -- without
// noreturn GCC assumes the inner try body can fall out and flags the local
// as maybe-uninitialized at the merged `return loc0`, the exdeep
// -Wuninitialized false positive).
[[noreturn]] void il2cpp_throw_new(const char* typeName, const char* message);
// Same throw, but overrides the reported parameter name (nullptr = no
// override). Used where net48's ParamName is "" (Array.Copy tail overflow).
[[noreturn]] void il2cpp_throw_new_with_param(const char* typeName,
                                              const char* message,
                                              const char* paramName);

// Type-conformance test for catch matching:
//   caught  : the catch clause's exception type ("System.Exception")
//   thrown  : the in-flight exception's type chain ("System.DivideByZero
//             Exception,System.DivideByZeroException,System.Arithmetic
//             Exception,System.SystemException,System.Exception")
// The chain is a comma-separated list whose FIRST segment is the exact
// managed full name and whose remaining segments are the type's .NET base
// classes (compile-time knowledge baked into the throw site -- zero runtime
// metadata). The matcher walks EVERY segment (including the first, so a
// chain that lists the type only once -- buffer.cpp layout -- still matches
// a catch of the thrown type itself; the repeated-type exceptionext layout
// conforms identically).
// Returns true when the catch matches (the handler runs).
bool il2cpp_exception_matches(const char* caught, const char* thrown);

// The top-level guard: report the unhandled exception (message) to stderr.
// Called by the main wrapper's catch-all; returns the exit code (non-zero).
int il2cpp_report_unhandled_exception(const char* message);

//------------------------------------------------------------------------------
// Checked arithmetic (M3 Task 6): .NET integer division/remainder by zero
// throws DivideByZeroException (raw C++ would crash). Generated code
// substitutes these helpers for every int32 Div/Rem.
//------------------------------------------------------------------------------
int32_t il2cpp_checked_div(int32_t a, int32_t b);
int32_t il2cpp_checked_rem(int32_t a, int32_t b);

// D-INT-1: the checked add/sub/mul family (add.ovf/sub.ovf/mul.ovf in
// StackFold). Every helper throws System.OverflowException on wrap (net48
// checked-context semantics); the _un forms compare their operands UNSIGNED.
// The transpiler carries uint32 values in int32 slots, so the _un helpers
// take/return int32_t bit patterns (unsigned arithmetic is internal only).
int32_t il2cpp_checked_add(int32_t a, int32_t b);
int32_t il2cpp_checked_sub(int32_t a, int32_t b);
int32_t il2cpp_checked_mul(int32_t a, int32_t b);
int32_t il2cpp_checked_add_un(int32_t a, int32_t b);
int32_t il2cpp_checked_sub_un(int32_t a, int32_t b);
int32_t il2cpp_checked_mul_un(int32_t a, int32_t b);
int32_t il2cpp_checked_rem_un(int32_t a, int32_t b);

// i64ck-b23: the 64-bit checked family (add.ovf/sub.ovf/mul.ovf on long /
// ulong operands -- the int32 helpers LOUD-rejected them). The emitter routes
// a width-64 checked fold to these (CodeGenerator emits {Function}_64). The
// _un forms compare their operands UNSIGNED and take/return int64_t BIT
// PATTERNS (the transpiler carries ulong in int64_t slots -- the same
// convention as the int32 _un helpers).
int64_t il2cpp_checked_add_64(int64_t a, int64_t b);
int64_t il2cpp_checked_sub_64(int64_t a, int64_t b);
int64_t il2cpp_checked_mul_64(int64_t a, int64_t b);
int64_t il2cpp_checked_add_un_64(int64_t a, int64_t b);
int64_t il2cpp_checked_sub_un_64(int64_t a, int64_t b);
int64_t il2cpp_checked_mul_un_64(int64_t a, int64_t b);
int64_t il2cpp_checked_rem_un_64(int64_t a, int64_t b);

} // namespace il2cpp
