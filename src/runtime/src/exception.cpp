//------------------------------------------------------------------------------
// exception.cpp -- managed exception transport (see il2cpp_exception.h).
//
// M3 scope: typed exceptions with exact-or-base matching; the managed
// Exception OBJECT (GC) and StackTrace arrive with the exception-object task
// (Task 10); this carrier's contract is stable.
//
// Type matching: M3 carries a BASE-CHAIN STRING on the exception. The
// transpiler emits the chain for each exception type it throws
// ("System.DivideByZeroException,System.Exception"); a catch matches when the
// caught type equals the thrown type or appears in the thrown type's chain.
// The matcher walks EVERY comma-separated segment -- the first segment is the
// thrown type itself, so a chain that lists the type only once (buffer.cpp
// layout) still matches a catch of the thrown type; the exceptionext layout
// repeats the type and conforms identically. The runtime does NOT walk the
// metadata type graph at runtime -- the chain is compile-time knowledge,
// baked into the throw site (zero reflection).
//------------------------------------------------------------------------------

#include "il2cpp_exception_ext.h"

#include "il2cpp_frame.h"

#include <cstdio>
#include "il2cpp_string.h"
#include <cstring>

namespace il2cpp {

// nb9 D13: the parameter name of the most recently thrown exception, captured
// from the message suffix at throw time so get_ParamName can answer it from
// the catch carrier without re-parsing a nested expression.
thread_local char g_il2cpp_last_param_name[128] = {0};
// nb9 E11: Copy's tail overflow throws ArgumentException(message, "") --
// ParamName is the EMPTY string (distinct from null). The presence flag
// keeps that distinction after CaptureParamName.
thread_local bool g_il2cpp_last_param_name_present = false;

namespace {
void CaptureParamName(const char* message)
{
    g_il2cpp_last_param_name[0] = 0;
    g_il2cpp_last_param_name_present = false;
    if (message == nullptr) return;
    const char* const zh = "\xE5\x8F\x82\xE6\x95\xB0\xE5\x90\x8D\x3A\x20";
    const char* p = std::strstr(message, zh);
    const char* marker = "Parameter name:";
    if (p == nullptr)
    {
        p = std::strstr(message, marker);
        if (p == nullptr) return;
        p += std::strlen(marker);
    }
    else
    {
        p += std::strlen(zh);
    }
    while (*p == ' ') ++p;
    const char* end = p;
    while (*end != 0 && *end != '\r' && *end != '\n') ++end;
    size_t n = static_cast<size_t>(end - p);
    if (n >= sizeof(g_il2cpp_last_param_name)) n = sizeof(g_il2cpp_last_param_name) - 1;
    std::memcpy(g_il2cpp_last_param_name, p, n);
    g_il2cpp_last_param_name[n] = 0;
    g_il2cpp_last_param_name_present = true;
}
} // namespace

//------------------------------------------------------------------------------
// il2cpp_throw_new -- throw a typed managed exception (see header).
//------------------------------------------------------------------------------
void il2cpp_throw_new(const char* typeName, const char* message)
{
    CaptureParamName(message);
    throw DotNetException(typeName, message);
}

// nb9 E11: explicit ParamName override (net48 (message, paramName) ctor).
// Used for Array.Copy's tail overflow, whose net48 ParamName is "" while its
// message carries no parameter-name suffix.
void il2cpp_throw_new_with_param(const char* typeName, const char* message,
                                 const char* paramName)
{
    CaptureParamName(message);
    if (paramName != nullptr)
    {
        size_t n = std::strlen(paramName);
        if (n >= sizeof(g_il2cpp_last_param_name))
        {
            n = sizeof(g_il2cpp_last_param_name) - 1;
        }
        std::memcpy(g_il2cpp_last_param_name, paramName, n);
        g_il2cpp_last_param_name[n] = 0;
        g_il2cpp_last_param_name_present = true;
    }
    throw DotNetException(typeName, message);
}

// excmsg: the Argument*Exception ctor shapes carry their ParamName as a real
// ctor argument. Unlike il2cpp_throw_new_with_param above, a NULL/absent name
// CLEARS the snapshot instead of leaving the previous throw's value (the old
// value would be a silent wrong ParamName on the catch carrier), and an EMPTY
// name is recorded as "" (net48 keeps ParamName == "").
void il2cpp_throw_new_param_explicit(const char* typeName, const char* message,
                                     const char* paramName, int32_t paramPresent)
{
    g_il2cpp_last_param_name[0] = 0;
    g_il2cpp_last_param_name_present = false;
    if (paramPresent != 0 && paramName != nullptr)
    {
        size_t n = std::strlen(paramName);
        if (n >= sizeof(g_il2cpp_last_param_name))
        {
            n = sizeof(g_il2cpp_last_param_name) - 1;
        }
        std::memcpy(g_il2cpp_last_param_name, paramName, n);
        g_il2cpp_last_param_name[n] = 0;
        g_il2cpp_last_param_name_present = true;
    }
    throw DotNetException(typeName, message);
}

// nb9 D13: get_ParamName for a catch carrier -- returns the parameter name
// captured by the most recent throw (null when the throw supplied none).
Il2CppString* icall_exception_param_name_current()
{
    if (!g_il2cpp_last_param_name_present) return nullptr;
    return il2cpp_string_new_from_utf8(g_il2cpp_last_param_name);
}

//------------------------------------------------------------------------------
// il2cpp_exception_matches -- catch-clause type conformance (see header).
//
// The chain walk covers EVERY comma-separated segment, INCLUDING the first
// (the thrown type itself). Walking from segment 0 makes BOTH chain layouts
// in the codebase conform:
//   * exceptionext / arrayext / streamio style: "{Type},{Type},{Base1},..."
//     (the type REPEATED as the second segment -- the layout's original
//     convention when the walk skipped the first segment);
//   * buffer.cpp style:                         "{Type},{Base1},..."
//     (the type listed ONCE, as the first segment).
// A catch of the thrown type matches the first segment in either layout; a
// catch of a base class matches a later segment. Segments are compared
// whole-string (length + strncmp), so a partial prefix never matches.
//------------------------------------------------------------------------------
bool il2cpp_exception_matches(const char* caught, const char* thrown)
{
    if (caught == nullptr || thrown == nullptr)
    {
        return false;
    }
    if (std::strcmp(caught, thrown) == 0)
    {
        return true; // whole-string exact match (a no-comma single-type chain)
    }
    const char* p = thrown;
    while (*p != '\0')
    {
        const char* comma = std::strchr(p, ',');
        const size_t len = (comma != nullptr) ? static_cast<size_t>(comma - p) : std::strlen(p);
        if (len == std::strlen(caught) && std::strncmp(p, caught, len) == 0)
        {
            return true;
        }
        // batch 156d (asynccase): the faulted-task rethrow carries the
        // M-model catch-variable SHORT type name ("Exception" -- the state
        // machine's catch materializes the short name; the task stores it
        // verbatim). A dotless thrown segment is such a short name; match it
        // against the caught name's LAST segment ("System.Exception" ->
        // "Exception"). Full-name chains (every normal throw site) are
        // unaffected -- no regular chain contains a dotless segment, so this
        // arm never fires for them.
        if (std::memchr(p, '.', len) == nullptr)
        {
            const char* dot = std::strrchr(caught, '.');
            const char* caughtShort = (dot != nullptr) ? dot + 1 : caught;
            if (std::strlen(caughtShort) == len && std::strncmp(caughtShort, p, len) == 0)
            {
                return true;
            }
        }
        if (comma == nullptr)
        {
            break;
        }
        p = comma + 1;
    }
    return false;
}

// nb9 D13: exception-chain test against a MANAGED type-name string (the
// catch carrier / `is` pattern operand). ASCII short names only.
bool il2cpp_exception_matches_string(const char* caughtChain, Il2CppString* thrownName)
{
    if (thrownName == nullptr) return false;
    char buf[256];
    int32_t n = thrownName->length;
    if (n > 255) n = 255;
    for (int32_t i = 0; i < n; ++i) buf[i] = static_cast<char>(thrownName->chars[i]);
    buf[n] = 0;
    return il2cpp_exception_matches(caughtChain, buf);
}

//------------------------------------------------------------------------------
// il2cpp_report_unhandled_exception -- top-level guard (see header).
// M3 Task 10: the report includes the managed frame stack (method names) --
// the StackTrace foundation. Frames may be partially unwound (the throwing
// method's frame was popped by its RAII destructor), so the report lists the
// frames that survived to the top-level catch.
//------------------------------------------------------------------------------
int il2cpp_report_unhandled_exception(const char* message)
{
    std::fprintf(stderr, "Unhandled managed exception: %s\n",
                 message != nullptr ? message : "(no message)");
    std::fprintf(stderr, "Managed call stack (innermost first):\n");
    int depth = 0;
    for (Il2CppFrame* frame = il2cpp_frame_top(); frame != nullptr && depth < 64; frame = frame->prev, ++depth)
    {
        // Prefer the MANAGED spelling ("Ns.Type.Method(...)") when the
        // emitter supplied one; fall back to the sanitized C++ name.
        const char* name = frame->displayName != nullptr ? frame->displayName : frame->methodName;
        std::fprintf(stderr, "  at %s\n", name != nullptr ? name : "(unknown frame)");
    }
    if (depth == 0)
    {
        std::fprintf(stderr, "  (no managed frames on the stack)\n");
    }
    return 1;
}

//------------------------------------------------------------------------------
// il2cpp_checked_div / il2cpp_checked_rem -- checked integer division.
// .NET: x / 0 (int) throws DivideByZeroException; C++ division by zero is
// UB. Audit H9: INT_MIN / -1 is also UB in C++ (the quotient 2^31 is not
// representable in int32; x86 idiv raises #DE -> SIGFPE). .NET throws
// OverflowException for it, so the helper checks and throws the managed
// carrier before the division executes.
//------------------------------------------------------------------------------
int32_t il2cpp_checked_div(int32_t a, int32_t b)
{
    if (b == 0)
    {
        // Canonical factory (il2cpp_exception_ext.h): the chain repeats the
        // exact type so a catch of the thrown type itself matches, then lists
        // the .NET base classes (ArithmeticException, SystemException,
        // Exception) -- the exceptionext chain convention.
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (a == INT32_MIN && b == -1)
    {
        // net48 message text for the JIT's INT_MIN / -1 overflow check.
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return a / b;
}

int32_t il2cpp_checked_rem(int32_t a, int32_t b)
{
    if (b == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    if (a == INT32_MIN && b == -1)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return a % b;
}

//------------------------------------------------------------------------------
// D-INT-1: il2cpp_checked_{add,sub,mul}[_un] -- the checked arithmetic family
// (StackFold folds add.ovf/sub.ovf/mul.ovf to these helpers). .NET's checked
// context throws System.OverflowException when the result does not fit the
// int32 (signed) / uint32 (the _un forms compare UNSIGNED) destination.
// Overflow detection runs on the WIDENED int64/uint64 intermediate -- both
// int32/uint32 operands always fit, so the widened arithmetic itself never
// overflows and the range test is exact (portable across MSVC/MinGW, no
// compiler builtins). The _un helpers take/return int32_t bit patterns: the
// transpiler carries uint32 in int32 slots.
//------------------------------------------------------------------------------
int32_t il2cpp_checked_add(int32_t a, int32_t b)
{
    const int64_t s = static_cast<int64_t>(a) + static_cast<int64_t>(b);
    if (s > INT32_MAX || s < INT32_MIN)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(s);
}

int32_t il2cpp_checked_sub(int32_t a, int32_t b)
{
    const int64_t d = static_cast<int64_t>(a) - static_cast<int64_t>(b);
    if (d > INT32_MAX || d < INT32_MIN)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(d);
}

int32_t il2cpp_checked_mul(int32_t a, int32_t b)
{
    const int64_t p = static_cast<int64_t>(a) * static_cast<int64_t>(b);
    if (p > INT32_MAX || p < INT32_MIN)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(p);
}

int32_t il2cpp_checked_add_un(int32_t a, int32_t b)
{
    const uint64_t sa = static_cast<uint32_t>(a);
    const uint64_t sb = static_cast<uint32_t>(b);
    if (sa + sb > UINT32_MAX)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<uint32_t>(sa + sb));
}

int32_t il2cpp_checked_sub_un(int32_t a, int32_t b)
{
    const uint32_t sa = static_cast<uint32_t>(a);
    const uint32_t sb = static_cast<uint32_t>(b);
    if (sa < sb)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(sa - sb);
}

int32_t il2cpp_checked_mul_un(int32_t a, int32_t b)
{
    const uint64_t sa = static_cast<uint32_t>(a);
    const uint64_t sb = static_cast<uint32_t>(b);
    if (sa * sb > UINT32_MAX)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int32_t>(static_cast<uint32_t>(sa * sb));
}

int32_t il2cpp_checked_rem_un(int32_t a, int32_t b)
{
    if (b == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return static_cast<int32_t>(static_cast<uint32_t>(a) % static_cast<uint32_t>(b));
}

//------------------------------------------------------------------------------
// i64ck-b23: il2cpp_checked_{add,sub,mul}[_un]_64 -- the 64-bit checked family.
// The int32 helpers (D-INT-1) widen to int64/uint64 intermediates; the 64-bit
// forms cannot widen portably (MSVC has no __int128), so overflow detection
// uses the comparison form of the standard CERT trick -- every operand/result
// arithmetic stays inside int64/uint64 (no UB on any input, no compiler
// builtins). All overflow paths throw OverflowException (net48 checked
// semantics); the _un forms compare UNSIGNED and take/return int64_t bit
// patterns (the transpiler carries ulong in int64_t slots).
//------------------------------------------------------------------------------
int64_t il2cpp_checked_add_64(int64_t a, int64_t b)
{
    if ((a > 0 && b > INT64_MAX - a) || (a < 0 && b < INT64_MIN - a))
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return a + b;
}

int64_t il2cpp_checked_sub_64(int64_t a, int64_t b)
{
    if ((b > 0 && a < INT64_MIN + b) || (b < 0 && a > INT64_MAX + b))
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return a - b;
}

int64_t il2cpp_checked_mul_64(int64_t a, int64_t b)
{
    if (a != 0 && b != 0)
    {
        // Division-based product-overflow test (|a*b| exceeds INT64_MAX):
        // every quotient below stays in range (no INT64_MIN / -1).
        const bool ovf = a > 0
            ? (b > 0 ? a > INT64_MAX / b : b < INT64_MIN / a)
            : (b > 0 ? a < INT64_MIN / b : b < INT64_MAX / a);
        if (ovf)
        {
            il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
        }
    }
    return a * b;
}

int64_t il2cpp_checked_add_un_64(int64_t a, int64_t b)
{
    const uint64_t sa = static_cast<uint64_t>(a);
    const uint64_t sb = static_cast<uint64_t>(b);
    if (sa > UINT64_MAX - sb)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int64_t>(sa + sb);
}

int64_t il2cpp_checked_sub_un_64(int64_t a, int64_t b)
{
    const uint64_t sa = static_cast<uint64_t>(a);
    const uint64_t sb = static_cast<uint64_t>(b);
    if (sa < sb)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int64_t>(sa - sb);
}

int64_t il2cpp_checked_mul_un_64(int64_t a, int64_t b)
{
    const uint64_t sa = static_cast<uint64_t>(a);
    const uint64_t sb = static_cast<uint64_t>(b);
    if (sa != 0 && sb > UINT64_MAX / sa)
    {
        il2cpp_throw_overflow_exception("Arithmetic operation resulted in an overflow.");
    }
    return static_cast<int64_t>(sa * sb);
}

int64_t il2cpp_checked_rem_un_64(int64_t a, int64_t b)
{
    if (b == 0)
    {
        il2cpp_throw_divide_by_zero_exception("Attempted to divide by zero.");
    }
    return static_cast<int64_t>(static_cast<uint64_t>(a) % static_cast<uint64_t>(b));
}

} // namespace il2cpp




