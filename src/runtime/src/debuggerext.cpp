//------------------------------------------------------------------------------
// debuggerext.cpp -- System.Diagnostics.Debugger stubs (see debuggerext.h for
// the full contract).
//
// Output policy: everything goes to the platform-independent C stderr stream,
// so AOT diagnostics surface without any debugger or OS-specific logging API.
// The managed strings are marshalled to heap UTF-8 via the pinvokeext encoder
// (full UTF-8 incl. astral pairs, U+FFFD for lone surrogates -- .NET
// marshalling semantics) and freed after use.
//------------------------------------------------------------------------------

#include "debuggerext.h"
#include "il2cpp_pinvoke_ext.h"

#include <cstdio>

namespace il2cpp { namespace runtime {

int32_t il2cpp_debugger_is_attached()
{
    return 0;
}

int32_t il2cpp_debugger_is_logging()
{
    return 0;
}

void il2cpp_debugger_break()
{
    // No debugger in ILC: nothing to break into.
}

void il2cpp_debugger_log(int32_t level, Il2CppString* category, Il2CppString* message)
{
    (void)level; // accepted for signature compatibility (a debugger filter hint)
    char* cat = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(category);
    char* msg = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(message);
    const char* msgText = message != nullptr ? msg : "(null)";
    if (cat[0] != '\0')
        std::fprintf(stderr, "%s: %s\n", cat, msgText);
    else
        std::fprintf(stderr, "%s\n", msgText);
    il2cpp::il2cpp_pinvoke_utf8_free(cat);
    il2cpp::il2cpp_pinvoke_utf8_free(msg);
}

}} // namespace il2cpp::runtime
