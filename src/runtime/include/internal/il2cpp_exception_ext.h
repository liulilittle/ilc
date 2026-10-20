#pragma once
namespace il2cpp { struct Il2CppString; }
//------------------------------------------------------------------------------
// il2cpp_exception_ext.h -- convenience factories for common managed exception
// types (the transport itself lives in il2cpp_exception.h).
//
// The base transport exposes one generic throw primitive (il2cpp_throw_new)
// that takes a type-name chain and a message. This header adds named factories
// for the .NET exception types the generated code throws most often, so throw
// sites read as the managed API they mirror:
//
//   throw new ArgumentNullException(nameof(x));
//     -->
//   il2cpp_throw_argument_null_exception("x");
//
// Type representation: each factory throws a DotNetException whose type-name
// chain starts with the EXACT managed full name ("System.ArgumentNullException")
// and then lists the type's .NET base classes. The catch matcher
// (il2cpp_exception_matches) walks that chain, so catch clauses follow .NET
// inheritance semantics with zero runtime metadata: System.Exception catches
// any of these factories' exceptions, and catch(System.ArgumentException)
// catches ArgumentNullException / ArgumentOutOfRangeException.
//
// Chain layout note: il2cpp_exception_matches (exception.cpp) walks EVERY
// comma-separated segment of the chain, including the first -- so a catch of
// the thrown type itself matches a chain that lists the type only once
// ("{Type},{Base1},{Base2},..."). Each factory below REPEATS the type as its
// second segment ("{Type},{Type},{Base1},{Base2},..."): the repetition is
// harmless to the segment walk and keeps the first element the canonical
// managed full name (this is what e.type_name() starts with).
//
// Message contract: 'message' may be nullptr (the factory substitutes the
// standard .NET Framework default text). When provided it must be a STABLE
// string (a static literal) -- DotNetException carries the pointer without
// copying. The paramName-based factories (ArgumentNullException,
// ArgumentOutOfRangeException) compose their message into a thread-local
// scratch buffer: the text stays valid for the current thread until the NEXT
// composed-message factory call, so a catch handler must read the message
// before throwing again (see exceptionext.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_exception.h"

namespace il2cpp {

// System.ArgumentException -- 'message' or the standard default
// ("Value does not fall within the expected range.").
void il2cpp_throw_argument_exception(const char* message);

// System.ArgumentNullException -- the message is composed from 'paramName'
// in the .NET Framework two-line format: the zh-CN host resource
// "\xE5\x80\xBC..." ("value cannot be null", UTF-8 byte escapes in
// exceptionext.cpp, net48 oracle on the zh-CN e2e host) + "\r\n" +
// "Parameter name: <paramName>" (NOT .NET Core's one-line "(Parameter 'x')"
// suffix); a nullptr paramName yields the plain default text alone.
void il2cpp_throw_argument_null_exception(const char* paramName);

// System.ArgumentOutOfRangeException -- an EXPLICIT 'message' passes through
// EXACTLY (the runtime's pinned net48-resource-text convention for the
// (paramName, message) shape -- test_listi4; real net48 would append the
// parameter-name line, a documented divergence). A nullptr message composes
// the default zh-CN host resource "\xE5\x87\xBA..." ("argument out of valid
// range", UTF-8 byte escapes in exceptionext.cpp) + "\r\n" +
// "Parameter name: <paramName>" for a non-empty paramName (net48
// 1-arg ctor semantics -- excext).
void il2cpp_throw_argument_out_of_range_exception(const char* paramName, const char* message);

// System.InvalidOperationException -- 'message' or the standard default
// ("Operation is not valid due to the current state of the object.").
void il2cpp_throw_invalid_operation_exception(const char* message);

// System.IndexOutOfRangeException -- 'message' or the standard default
// ("Index was outside the bounds of the array.").
void il2cpp_throw_index_out_of_range_exception(const char* message);

// System.NullReferenceException -- 'message' or the standard default
// ("Object reference not set to an instance of an object.").
void il2cpp_throw_null_reference_exception(const char* message);

// System.DivideByZeroException -- 'message' or the standard default
// ("Attempted to divide by zero.").
void il2cpp_throw_divide_by_zero_exception(const char* message);

// System.OverflowException -- 'message' or the standard default
// ("Arithmetic operation resulted in an overflow.").
void il2cpp_throw_overflow_exception(const char* message);

// System.NotSupportedException -- 'message' or the standard default
// ("Specified method is not supported.").
void il2cpp_throw_not_supported_exception(const char* message);

// System.TimeZoneNotFoundException -- 'message' or the standard default
// ("The time zone was not found on the local computer."). TZEXC: the
// TimeZoneInfo registry's unknown-id boundary uses this (net48 alignment).
void il2cpp_throw_time_zone_not_found_exception(const char* message);

// System.Collections.Generic.KeyNotFoundException -- 'message' or the standard
// default ("The given key was not present in the dictionary.").
void il2cpp_throw_key_not_found_exception(const char* message);

// System.FormatException -- 'message' or the standard default
// ("Input string was not in a correct format.").
void il2cpp_throw_format_exception(const char* message);

// System.IO.IOException -- 'message' or the standard default
// ("I/O error."). fileio2 (m7m): File.Copy/Move failures map their icall
// status to this factory so the .NET exception semantics hold.
void il2cpp_throw_io_exception(const char* message);

// System.IO.FileNotFoundException -- 'message' or the standard default
// ("The system cannot find the file specified."). fileio2 (m7m): File.Move /
// File.Copy on a missing source throw this (net48 behavior).
void il2cpp_throw_file_not_found_exception(const char* message);


// nb9 D13: get_ParamName for a catch carrier (parameter name of the most
// recent throw; null when none).
Il2CppString* icall_exception_param_name_current();
} // namespace il2cpp
