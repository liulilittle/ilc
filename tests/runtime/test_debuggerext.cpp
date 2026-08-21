//------------------------------------------------------------------------------
// test_debuggerext.cpp -- System.Diagnostics.Debugger stub unit tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_debugger_is_attached returns 0 (no debugger in ILC).
//   2. il2cpp_debugger_is_logging returns 0 (no logging channel).
//   3. il2cpp_debugger_break is a callable no-op (never crashes).
//   4. il2cpp_debugger_log(level, "cat", "msg") prints "cat: msg" to stderr.
//   5. log with an empty category prints just the message.
//   6. log with a null category prints just the message.
//   7. log with a null message prints "(null)" (defensive).
//   8. log with both null prints "(null)".
//
// stderr is captured via the _dup/_dup2 mechanism test_debugext.cpp uses
// (separate file per stream, restored afterwards).
//------------------------------------------------------------------------------

// The test harness needs freopen/fopen for stream capture -- MSVC deprecates
// them (C4996) in favor of the *_s variants, which do not exist on POSIX.
// The deprecated calls are intentional and well-formed here; silence the
// deprecation for MSVC only.
#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
#endif

#include "debuggerext.h"
#include "il2cpp_string.h"

#include <cstdio>
#include <cstring>
#include <string>

#ifdef _WIN32
    #include <io.h> // _dup / _dup2 (stream handle save/restore)
    #define DEBUGGEREXT_STDERR_FILE "debuggerext_stderr.txt"
    #define DEBUGGEREXT_RESTORE_NAME "CON"
#else
    #include <unistd.h> // dup / dup2
    #define DEBUGGEREXT_STDERR_FILE "/tmp/debuggerext_stderr.txt"
    #define DEBUGGEREXT_RESTORE_NAME "/dev/tty"
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

// Saved stderr handle, taken BEFORE the capture freopen (the same reliable
// _dup/_dup2 restore path test_debugext.cpp verifies -- plain freopen back to
// "CON" silently drops subsequent writes on MinGW/MSVCRT).
int g_savedErr = -1;

void BeginCapture()
{
    std::fflush(stderr);
#ifdef _WIN32
    g_savedErr = _dup(_fileno(stderr));
#else
    g_savedErr = dup(STDERR_FILENO);
#endif
    std::freopen(DEBUGGEREXT_STDERR_FILE, "w", stderr);
}

// Restore stderr and return the captured bytes.
std::string EndCapture()
{
    std::fflush(stderr);
#ifdef _WIN32
    if (g_savedErr != -1)
    {
        _dup2(g_savedErr, _fileno(stderr));
        _close(g_savedErr);
        g_savedErr = -1;
    }
    else
    {
        std::freopen(DEBUGGEREXT_RESTORE_NAME, "w", stderr);
    }
#else
    if (g_savedErr != -1)
    {
        dup2(g_savedErr, STDERR_FILENO);
        close(g_savedErr);
        g_savedErr = -1;
    }
    else
    {
        std::freopen(DEBUGGEREXT_RESTORE_NAME, "w", stderr);
    }
#endif
    // Text mode read: the capture file was written in text mode ("w"), so
    // "\r\n" maps back to "\n" on Windows and the comparison is against the
    // canonical runtime output on every platform.
    std::FILE* file = std::fopen(DEBUGGEREXT_STDERR_FILE, "r");
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
    using il2cpp::runtime::il2cpp_debugger_is_attached;
    using il2cpp::runtime::il2cpp_debugger_is_logging;
    using il2cpp::runtime::il2cpp_debugger_break;
    using il2cpp::runtime::il2cpp_debugger_log;

    // --- 1/2. attached / logging state: always false ---
    Expect(il2cpp_debugger_is_attached() == 0,
           "is_attached returns 0 (no debugger in ILC)");
    Expect(il2cpp_debugger_is_logging() == 0,
           "is_logging returns 0 (no logging channel)");

    // --- 3. break: callable no-op ---
    il2cpp_debugger_break();
    Expect(true, "break is a callable no-op");

    // --- 4. log with a category: "cat: msg" on stderr ---
    BeginCapture();
    il2cpp_debugger_log(0, il2cpp::il2cpp_string_new_from_cs("cat"),
                        il2cpp::il2cpp_string_new_from_cs("hello"));
    Expect(EndCapture() == "cat: hello\n",
           "log(level, cat, msg) prints 'cat: msg' to stderr");

    // --- 5. log with an empty category: just the message ---
    BeginCapture();
    il2cpp_debugger_log(0, il2cpp::il2cpp_string_new_from_cs(""),
                        il2cpp::il2cpp_string_new_from_cs("bare"));
    Expect(EndCapture() == "bare\n",
           "log with an empty category prints just the message");

    // --- 6. log with a null category: just the message ---
    BeginCapture();
    il2cpp_debugger_log(0, nullptr, il2cpp::il2cpp_string_new_from_cs("bare2"));
    Expect(EndCapture() == "bare2\n",
           "log with a null category prints just the message");

    // --- 7. log with a null message: "(null)" (defensive) ---
    BeginCapture();
    il2cpp_debugger_log(0, il2cpp::il2cpp_string_new_from_cs("cat"), nullptr);
    Expect(EndCapture() == "cat: (null)\n",
           "log with a null message prints 'cat: (null)'");

    // --- 8. log with both null: "(null)" ---
    BeginCapture();
    il2cpp_debugger_log(7, nullptr, nullptr);
    Expect(EndCapture() == "(null)\n",
           "log with both null prints '(null)'");

    // --- 9. non-ASCII message round-trips as UTF-8 (pinvokeext encoder) ---
    // A managed string holding the UTF-16 unit U+00E9 ('é') marshals back to
    // the 2-byte UTF-8 sequence C3 A9 (il2cpp_string_new_from_cs is ASCII-only,
    // so the unit is built directly).
    {
        char16_t units[] = { static_cast<char16_t>(0x00E9) };
        BeginCapture();
        il2cpp_debugger_log(0, il2cpp::il2cpp_string_new_from_cs("cat"),
                            il2cpp::il2cpp_string_new(units, 1));
        Expect(EndCapture() == std::string("cat: ") + std::string("\xC3\xA9") + "\n",
               "log marshals non-ASCII text as UTF-8");
    }

    if (g_failures != 0)
    {
        std::printf("%d debuggerext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime debuggerext tests passed\n");
    return 0;
}
