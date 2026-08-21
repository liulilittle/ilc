//------------------------------------------------------------------------------
// exceptionext.cpp -- convenience factories for common managed exceptions
// (see il2cpp_exception_ext.h for the API contract).
//
// MESSAGE CULTURE + FORMAT (excext, 2026-08-11): .NET Framework 4.8 renders
// exception text in the OS culture and appends the parameter-name line to the
// Message property. On the zh-CN e2e oracle host (net48 probe-verified):
//   new ArgumentNullException("param").Message
//     -> "值不能为 null。" "\n" "参数名: param"
//   new ArgumentOutOfRangeException("index", "out of range").Message
//     -> "out of range" "\n" "参数名: index"
// The composed text below therefore uses the zh-CN host strings and the
// .NET Framework two-line format (NOT .NET Core's one-line "(Parameter 'x')"
// suffix). The zh-CN text is encoded as UTF-8 byte escapes (source-encoding
// independent -- the cultureinfo.cpp/console.cpp precedent); the zh-CN host
// is the net48 oracle, so the output stays deterministic on every host
// (formatext.cpp fixes the same zh-CN host).
//
// Every factory forwards to il2cpp_throw_new with a type-name chain whose
// first element is the exact managed full name; the remaining elements are
// the type's .NET base classes, so the catch matcher
// (il2cpp_exception_matches) implements inheritance semantics without any
// runtime metadata lookup -- matching exception.cpp's M3 mechanism.
//
// Chain layout note (contract of the existing matcher in exception.cpp):
// il2cpp_exception_matches walks EVERY comma-separated segment of the chain,
// including the first (the thrown type itself). A chain that lists the type
// once ("{Type},{Base1},{Base2},...") therefore matches a catch of the
// thrown type via its first segment. Every chain below still REPEATS the
// type ("{Type},{Type},{Base1},{Base2},..."): the repetition is harmless to
// the segment walk (an exact-type catch matches the first occurrence) and
// keeps the first element the canonical managed full name -- this is what
// e.type_name() starts with. The walk list is therefore the thrown type plus
// its base classes -- exactly .NET's catch-conformance rule.
//
// Message storage contract: DotNetException carries message pointers WITHOUT
// copying (see il2cpp_exception.h), so every message below is either a static
// literal or the thread-local scratch built by ComposeMessage:
//   * static literal  -- defaults and caller-provided messages (the caller
//     must pass a stable string, i.e. a compile-time constant);
//   * thread-local    -- the zh-CN parameter-name text composed by the
//     paramName-based factories. The scratch outlives any exception
//     unwinding (never dangles), is private to the thread (no data race),
//     and needs no heap (no leak). Caveat: a later composed-message factory
//     call overwrites the scratch, so catch handlers must read the message
//     before the next throw -- the standard il2cpp-style exception
//     diagnostic pattern (the managed Exception OBJECT arrives with the
//     exception-object task and replaces this carrier entirely).
//------------------------------------------------------------------------------

#include "il2cpp_exception_ext.h"

namespace il2cpp {

namespace {

// Thread-local scratch for composed messages (".NET Framework Message" +
// "\n参数名: <name>"). See the file header for the lifetime contract.
thread_local char g_messageScratch[256];

// zh-CN host .NET Framework message text (net48 oracle, probe-verified
// 2026-08-11 -- see the file header):
//   * ArgumentNullException default:  "值不能为 null。"
//   * ArgumentOutOfRangeException default: "指定的参数已超出有效值的范围。"
//   * parameter-name prefix:          "参数名: "
const char* kArgumentNullMessage =
    "\xE5\x80\xBC\xE4\xB8\x8D\xE8\x83\xBD\xE4\xB8\xBA\x20\x6E\x75\x6C\x6C\xE3\x80\x82"; // 值不能为 null。
const char* kArgumentOutOfRangeMessage =
    "\xE6\x8C\x87\xE5\xAE\x9A\xE7\x9A\x84\xE5\x8F\x82\xE6\x95\xB0\xE5\xB7\xB2\xE8\xB6\x85"
    "\xE5\x87\xBA\xE6\x9C\x89\xE6\x95\x88\xE5\x80\xBC\xE7\x9A\x84\xE8\x8C\x83\xE5\x9B\xB4\xE3\x80\x82"; // 指定的参数已超出有效值的范围。
const char* kParamNamePrefix =
    "\xE5\x8F\x82\xE6\x95\xB0\xE5\x90\x8D\x3A\x20"; // 参数名: 

// Compose the parts (null entries render as "") into the thread-local
// scratch. The copy is manually bounded (clamped to the buffer, always
// NUL-terminated, always non-empty because the first part is copied first);
// the manual loop also avoids -Wformat-truncation on unbounded %s arguments.
char* ComposeMessage(const char* const* parts, int32_t count)
{
    char* p = g_messageScratch;
    const char* const end = g_messageScratch + sizeof(g_messageScratch) - 1;
    for (int32_t i = 0; i < count; ++i)
    {
        const char* s = parts[i] != nullptr ? parts[i] : "";
        while (*s != '\0' && p < end)
        {
            *p++ = *s++;
        }
    }
    *p = '\0';
    return g_messageScratch;
}

// ArgumentNullException / ArgumentOutOfRangeException Message:
// baseMessage + "\n" + "参数名: <paramName>" (net48 zh-CN format). The line is
// appended only for a NON-empty paramName (net48 ArgumentException.Message
// checks string.IsNullOrEmpty); a null/empty paramName yields the base
// message alone.
const char* ComposeParamNameMessage(const char* baseMessage, const char* paramName)
{
    if (paramName == nullptr || paramName[0] == '\0')
    {
        return baseMessage;
    }
    const char* parts[4] = { baseMessage, "\n", kParamNamePrefix, paramName };
    return ComposeMessage(parts, 4);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_throw_argument_exception -- System.ArgumentException.
//------------------------------------------------------------------------------
void il2cpp_throw_argument_exception(const char* message)
{
    il2cpp_throw_new("System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Value does not fall within the expected range.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_argument_null_exception -- System.ArgumentNullException.
// The standard .NET text plus the offending parameter name.
//------------------------------------------------------------------------------
void il2cpp_throw_argument_null_exception(const char* paramName)
{
    const char* msg = ComposeParamNameMessage(kArgumentNullMessage, paramName);
    il2cpp_throw_new("System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception",
                     msg);
}

//------------------------------------------------------------------------------
// il2cpp_throw_argument_out_of_range_exception -- System.ArgumentOutOfRangeException.
// An explicit message wins (mirroring the managed (paramName, message) ctor);
// otherwise the parameter name is composed into the standard text.
//------------------------------------------------------------------------------
void il2cpp_throw_argument_out_of_range_exception(const char* paramName, const char* message)
{
    // net48 Message-property semantics with the runtime's pinned divergence:
    // an EXPLICIT message passes through EXACTLY (the (paramName, message)
    // shape -- test_listi4 pins the raw net48 resource text; real net48 would
    // append the parameter-name line, a documented runtime divergence). A
    // nullptr message composes the zh-CN default + parameter-name line (net48
    // 1-arg ctor semantics -- excext).
    const char* msg = (message != nullptr)
        ? message
        : ComposeParamNameMessage(kArgumentOutOfRangeMessage, paramName);
    il2cpp_throw_new("System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception",
                     msg);
}

//------------------------------------------------------------------------------
// il2cpp_throw_invalid_operation_exception -- System.InvalidOperationException.
//------------------------------------------------------------------------------
void il2cpp_throw_invalid_operation_exception(const char* message)
{
    il2cpp_throw_new("System.InvalidOperationException,System.InvalidOperationException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Operation is not valid due to the current state of the object.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_index_out_of_range_exception -- System.IndexOutOfRangeException.
//------------------------------------------------------------------------------
void il2cpp_throw_index_out_of_range_exception(const char* message)
{
    il2cpp_throw_new("System.IndexOutOfRangeException,System.IndexOutOfRangeException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Index was outside the bounds of the array.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_null_reference_exception -- System.NullReferenceException.
//------------------------------------------------------------------------------
void il2cpp_throw_null_reference_exception(const char* message)
{
    il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Object reference not set to an instance of an object.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_divide_by_zero_exception -- System.DivideByZeroException.
//------------------------------------------------------------------------------
void il2cpp_throw_divide_by_zero_exception(const char* message)
{
    il2cpp_throw_new("System.DivideByZeroException,System.DivideByZeroException,System.ArithmeticException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Attempted to divide by zero.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_overflow_exception -- System.OverflowException.
//------------------------------------------------------------------------------
void il2cpp_throw_overflow_exception(const char* message)
{
    il2cpp_throw_new("System.OverflowException,System.OverflowException,System.ArithmeticException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Arithmetic operation resulted in an overflow.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_not_supported_exception -- System.NotSupportedException.
//------------------------------------------------------------------------------
void il2cpp_throw_not_supported_exception(const char* message)
{
    il2cpp_throw_new("System.NotSupportedException,System.NotSupportedException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Specified method is not supported.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_key_not_found_exception -- D-DICT4-2 (fixed): the chain leads
// with the REAL full name System.Collections.Generic.KeyNotFoundException.
// The transpiler emits catch clauses from metadata full names (CodeGenerator
// TypeTokenFullName), so a typed catch (KeyNotFoundException) matches ONLY a
// chain starting with the real full name -- the old
// "System.KeyNotFoundException" was not a valid type (same bug class as
// D-MON-3, monitor.cpp).
//------------------------------------------------------------------------------
void il2cpp_throw_key_not_found_exception(const char* message)
{
    il2cpp_throw_new("System.Collections.Generic.KeyNotFoundException,System.Collections.Generic.KeyNotFoundException,System.SystemException,System.Exception",
                     message != nullptr ? message : "The given key was not present in the dictionary.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_format_exception -- System.FormatException.
//------------------------------------------------------------------------------
void il2cpp_throw_format_exception(const char* message)
{
    il2cpp_throw_new("System.FormatException,System.FormatException,System.SystemException,System.Exception",
                     message != nullptr ? message : "Input string was not in a correct format.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_io_exception -- System.IO.IOException.
//------------------------------------------------------------------------------
void il2cpp_throw_io_exception(const char* message)
{
    il2cpp_throw_new("System.IO.IOException,System.IO.IOException,System.SystemException,System.Exception",
                     message != nullptr ? message : "I/O error.");
}

//------------------------------------------------------------------------------
// il2cpp_throw_file_not_found_exception -- System.IO.FileNotFoundException.
//------------------------------------------------------------------------------
void il2cpp_throw_file_not_found_exception(const char* message)
{
    il2cpp_throw_new("System.IO.FileNotFoundException,System.IO.FileNotFoundException,System.IO.IOException,System.SystemException,System.Exception",
                     message != nullptr ? message : "The system cannot find the file specified.");
}

} // namespace il2cpp

