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

#if defined(_WIN32)
#include <windows.h>
#elif defined(__linux__)
#include <cstdlib>
#include <cstring>
#endif

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// p0c D2/D3: the attached/logging answers are REAL process state, not
// constants.
//
// MEASURED net48 (.NET Framework 4.8 x64, probe-p0c + a cdb run):
//   * Debugger.IsAttached tracks the CLR *managed* debugger, NOT the OS
//     debugger: under cdb (IsDebuggerPresent() == TRUE) net48 answers FALSE.
//   * Debugger.IsLogging() is FALSE without a managed debugger.
// An AOT process cannot host a CLR managed debugger, so the only debugger
// that can ever attach to an ILC process is an OS-level one -- the honest
// answer is therefore the OS query (a strict superset of the net48 CLR
// answer, and never a constant lie). Windows uses the two kernel32 queries
// (local + remote); Linux reads TracerPid from /proc/self/status (the
// documented kernel ptrace-attach signal).
//------------------------------------------------------------------------------
int32_t il2cpp_debugger_is_attached()
{
#if defined(_WIN32)
    if (::IsDebuggerPresent() != 0)
    {
        return 1;
    }
    // A debugger attached through DebugActiveProcess on ANOTHER machine /
    // session: CheckRemoteDebuggerPresent covers the cross-process case.
    BOOL remote = FALSE;
    if (::CheckRemoteDebuggerPresent(::GetCurrentProcess(), &remote) != 0 && remote != 0)
    {
        return 1;
    }
    return 0;
#elif defined(__linux__)
    // /proc/self/status "TracerPid:\t<pid>": non-zero means a tracer is
    // attached (gdb / lldb / strace). A missing proc file or an unreadable
    // entry answers 0 (the same "not attached" as an unreadable debugger).
    std::FILE* f = std::fopen("/proc/self/status", "r");
    if (f == nullptr)
    {
        return 0;
    }
    int32_t attached = 0;
    char line[256];
    while (std::fgets(line, static_cast<int>(sizeof(line)), f) != nullptr)
    {
        if (std::strncmp(line, "TracerPid:", 10) == 0)
        {
            const long tracer = std::strtol(line + 10, nullptr, 10);
            attached = tracer > 0 ? 1 : 0;
            break;
        }
    }
    std::fclose(f);
    return attached;
#else
    // Apple / OpenBSD: no platform-layer tracer query yet; answer "not
    // attached" (the same value net48 reports without a managed debugger).
    return 0;
#endif
}

// Debugger.IsLogging(): net48 gates this on the attached managed debugger's
// logging flag, which is only ever set when a debugger is attached. ILC's
// Debug.Log channel is the stderr stream (always available), so the truthful
// answer is "a debugger is attached to this process" -- the same real query
// as IsAttached, never a hard-coded false.
int32_t il2cpp_debugger_is_logging()
{
    return il2cpp_debugger_is_attached();
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
