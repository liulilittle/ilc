//------------------------------------------------------------------------------
// debugext.cpp -- System.Diagnostics.Debug / System.Diagnostics.Trace stubs
// (see debugext.h for the full contract).
//
// Deliberate simplification: a failed assertion prints to stderr and the
// program CONTINUES. The .NET default Debug.Assert shows a message box (or
// terminates) when assertions are enabled -- replicating that would break
// AOT programs that ship with debug leftovers. Print + continue keeps them
// runnable while surfacing the failure.
//
// Output streams: diagnostics -> stdout (the console module's stream, so
// generated-code output stays byte-ordered), assertion failures -> stderr.
//------------------------------------------------------------------------------

#include "debugext.h"
#include "il2cpp_pinvoke_ext.h"

#include <cstdio>

namespace il2cpp { namespace runtime {

namespace {

// Print a managed string to the given stream in full UTF-8 (pinvokeext
// encoder: astral pairs + U+FFFD for lone surrogates -- .NET marshalling
// semantics). Defensive null handling: callers may pass a null string (e.g.
// a Debug.Assert with no message argument); print "(null)" instead of
// dereferencing it. The converted buffer is freed after the write.
void WriteManagedText(FILE* stream, Il2CppString* text, bool newline)
{
    if (text != nullptr)
    {
        char* utf8 = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(text);
        std::fputs(utf8, stream);
        il2cpp::il2cpp_pinvoke_utf8_free(utf8);
    }
    else
    {
        std::fputs("(null)", stream);
    }
    if (newline)
        std::fputc('\n', stream);
}

} // namespace

void il2cpp_debug_assert(int32_t condition, Il2CppString* message)
{
    if (condition == 0)
    {
        std::fputs("Assertion Failed: ", stderr);
        WriteManagedText(stderr, message, true);
    }
}

void il2cpp_debug_write_line(Il2CppString* text)
{
    WriteManagedText(stdout, text, true);
}

void il2cpp_debug_fail(Il2CppString* message)
{
    std::fputs("Assertion Failed: ", stderr);
    WriteManagedText(stderr, message, true);
}

void il2cpp_trace_write(Il2CppString* text)
{
    WriteManagedText(stdout, text, false);
}

}} // namespace il2cpp::runtime
