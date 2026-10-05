//------------------------------------------------------------------------------
// test_debugext.cpp -- System.Diagnostics.Debug / Trace stub unit tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_debug_write_line prints the text plus newline; the empty
//      string prints just the newline; a null text prints "(null)" -- and
//      never crashes.
//   2. il2cpp_trace_write concatenates without newlines (.NET Trace.Write).
//   3. il2cpp_debug_assert(true, ...) prints nothing.
//   4. il2cpp_debug_assert(false, "boom") prints "Assertion Failed: boom"
//      to stderr and the program CONTINUES (subsequent checks still run).
//   5. il2cpp_debug_fail prints "Assertion Failed: <message>" to stderr and
//      the program CONTINUES -- the stub never terminates the process.
//   6. il2cpp_debug_assert(false) -- the one-argument Debug.Assert(bool)
//      form (message defaults to null) -- prints "Assertion Failed: (null)".
//   7. Non-ASCII managed text round-trips as UTF-8 (pinvokeext encoder):
//      U+00E9 ('é') marshals back as the 2-byte sequence C3 A9.
//
// Managed strings cross as Il2CppString* (the emitter's "s" cast) and are
// built here with il2cpp_string_new_from_cs -- the debuggerext test
// convention. stdout AND stderr are captured via freopen into scratch files
// (and restored) -- the same _dup/_dup2 mechanism test_console.cpp uses, with
// separate files per stream so assertion output is verifiable independently.
//------------------------------------------------------------------------------

// The test harness needs freopen/fopen for stream capture -- MSVC deprecates
// them (C4996) in favor of the *_s variants, which do not exist on POSIX.
// The deprecated calls are intentional and well-formed here; silence the
// deprecation for MSVC only.
#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
#endif

#include "debugext.h"
#include "il2cpp_string.h"

#include <cstdio>
#include <cstring>
#include <string>

#ifdef _WIN32
    #include <io.h> // _dup / _dup2 (stream handle save/restore)
    #define DEBUGEXT_CAPTURE_FILE "debugext_capture.txt"
    #define DEBUGEXT_STDERR_FILE "debugext_stderr.txt"
    #define DEBUGEXT_RESTORE_NAME "CON"
#else
    #include <unistd.h> // dup / dup2
    #define DEBUGEXT_CAPTURE_FILE "/tmp/debugext_capture.txt"
    #define DEBUGEXT_STDERR_FILE "/tmp/debugext_stderr.txt"
    #define DEBUGEXT_RESTORE_NAME "/dev/tty"
#endif

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

// Saved stream handles, taken BEFORE the capture freopen (the same reliable
// _dup/_dup2 restore path test_console.cpp verifies -- plain freopen back to
// "CON" silently drops subsequent writes on MinGW/MSVCRT).
int g_savedOut = -1;
int g_savedErr = -1;

void BeginCapture()
{
    std::fflush(stdout);
    std::fflush(stderr);
#ifdef _WIN32
    g_savedOut = _dup(_fileno(stdout));
    g_savedErr = _dup(_fileno(stderr));
#else
    g_savedOut = dup(STDOUT_FILENO);
    g_savedErr = dup(STDERR_FILENO);
#endif
    std::freopen(DEBUGEXT_CAPTURE_FILE, "w", stdout);
    std::freopen(DEBUGEXT_STDERR_FILE, "w", stderr);
}

// Restore both streams and return the captured bytes of the given stream.
std::string EndCapture(bool wantStderr)
{
    std::fflush(stdout);
    std::fflush(stderr);
#ifdef _WIN32
    if (g_savedOut != -1)
    {
        _dup2(g_savedOut, _fileno(stdout));
        _close(g_savedOut);
        g_savedOut = -1;
    }
    else
    {
        std::freopen(DEBUGEXT_RESTORE_NAME, "w", stdout);
    }
    if (g_savedErr != -1)
    {
        _dup2(g_savedErr, _fileno(stderr));
        _close(g_savedErr);
        g_savedErr = -1;
    }
    else
    {
        std::freopen(DEBUGEXT_RESTORE_NAME, "w", stderr);
    }
#else
    if (g_savedOut != -1)
    {
        dup2(g_savedOut, STDOUT_FILENO);
        close(g_savedOut);
        g_savedOut = -1;
    }
    else
    {
        std::freopen(DEBUGEXT_RESTORE_NAME, "w", stdout);
    }
    if (g_savedErr != -1)
    {
        dup2(g_savedErr, STDERR_FILENO);
        close(g_savedErr);
        g_savedErr = -1;
    }
    else
    {
        std::freopen(DEBUGEXT_RESTORE_NAME, "w", stderr);
    }
#endif
    // Text mode read: the capture file was written in text mode ("w"), so
    // "\r\n" maps back to "\n" on Windows and the comparison is against the
    // canonical runtime output on every platform.
    std::FILE* file = std::fopen(wantStderr ? DEBUGEXT_STDERR_FILE
                                            : DEBUGEXT_CAPTURE_FILE, "r");
    if (file == nullptr)
        return "<capture open failed>";
    std::string content;
    char chunk[256];
    std::size_t read = 0;
    while ((read = std::fread(chunk, 1, sizeof(chunk), file)) != 0)
        content.append(chunk, read);
    std::fclose(file);
    return content;
}

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_debug_assert;
    using il2cpp::runtime::il2cpp_debug_write_line;
    using il2cpp::runtime::il2cpp_debug_fail;
    using il2cpp::runtime::il2cpp_trace_write;

    const auto S = il2cpp::il2cpp_string_new_from_cs;

    // --- 1. write_line: text + newline; empty string; null text ---
    BeginCapture();
    il2cpp_debug_write_line(S("hello debugext"));
    il2cpp_debug_write_line(S(""));
    il2cpp_debug_write_line(nullptr);
    Expect(EndCapture(false) == "hello debugext\n\n(null)\n",
           "write_line prints text + newline, empty line and (null) for null");

    // --- 2. trace_write: concatenates without newlines ---
    BeginCapture();
    il2cpp_trace_write(S("a"));
    il2cpp_trace_write(S("b"));
    il2cpp_trace_write(nullptr);
    Expect(EndCapture(false) == "ab(null)",
           "trace_write concatenates without newlines");

    // --- 3. assert(true): no stderr output ---
    BeginCapture();
    il2cpp_debug_assert(1, S("must not appear"));
    Expect(EndCapture(true).empty(),
           "assert(true) produces no stderr output");

    // --- 4. assert(false): message on stderr, execution CONTINUES ---
    BeginCapture();
    il2cpp_debug_assert(0, S("boom"));
    const std::string assertOutput = EndCapture(true);
    Expect(assertOutput == "Assertion Failed: boom\n",
           "assert(false) prints 'Assertion Failed: boom' to stderr");
    Expect(true, "execution continues after a failed assert"); // reached => no abort

    // --- 5. fail: message on stderr, execution CONTINUES ---
    BeginCapture();
    il2cpp_debug_fail(S("fatal stub"));
    const std::string failOutput = EndCapture(true);
    Expect(failOutput == "Assertion Failed: fatal stub\n",
           "fail prints 'Assertion Failed: fatal stub' to stderr");
    Expect(true, "execution continues after fail"); // reached => no abort

    // --- 6. assert(false, null): null message is handled defensively ---
    BeginCapture();
    il2cpp_debug_assert(0, nullptr);
    Expect(EndCapture(true) == "Assertion Failed: (null)\n",
           "assert with a null message prints '(null)'");

    // --- 7. assert(false): the one-argument Debug.Assert(bool) form ---
    // The transpiler emits il2cpp_debug_assert(cond) for Debug.Assert(bool);
    // the message parameter defaults to null, so a false condition prints
    // "(null)" (the documented defensive stub convention).
    BeginCapture();
    il2cpp_debug_assert(0);
    Expect(EndCapture(true) == "Assertion Failed: (null)\n",
           "one-argument assert(false) prints 'Assertion Failed: (null)'");

    // --- 8. assert(true): the one-argument form also stays silent ---
    BeginCapture();
    il2cpp_debug_assert(1);
    Expect(EndCapture(true).empty(),
           "one-argument assert(true) produces no stderr output");

    // --- 9. non-ASCII message round-trips as UTF-8 (pinvokeext encoder) ---
    // A managed string holding the UTF-16 unit U+00E9 ('é') marshals back to
    // the 2-byte UTF-8 sequence C3 A9 (il2cpp_string_new_from_cs is ASCII-only,
    // so the unit is built directly).
    {
        char16_t units[] = { static_cast<char16_t>(0x00E9) };
        BeginCapture();
        il2cpp_debug_assert(0, il2cpp::il2cpp_string_new(units, 1));
        Expect(EndCapture(true) == std::string("Assertion Failed: ")
                   + std::string("\xC3\xA9") + "\n",
               "assert marshals non-ASCII text as UTF-8");
    }

    if (g_failures != 0)
    {
        std::printf("%d debugext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime debugext tests passed\n");
    return 0;
}
