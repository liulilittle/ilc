//------------------------------------------------------------------------------
// test_console.cpp -- System.Console icall extension unit tests.
//
// Coverage (normal + edge + error paths):
//   1. icall_console_write_line_b renders .NET bool.ToString(): "True" /
//      "False".
//   2. icall_console_write_line_c renders the ASCII character plus newline;
//      non-ASCII code units are UTF-8 encoded (E4 B8 AD for U+4E2D).
//   3. icall_console_write_line_d renders the net48 DEFAULT double text:
//      G15 -- 15 significant digits, NOT the shortest round-trip form
//      ("1.5", "0.1", "150", "1E+21", "NaN", "\xE2\x88\x9E", "-\xE2\x88\x9E";
//      +-Infinity is the U+221E '∞' symbol in UTF-8, net48 zh-CN empirical;
//      Sqrt(2)-class values stay at 15 digits, 0.1+0.2 -> "0.3",
//      -0.0 -> "0") -- the net48 reference is .NET's ToString().
//   4. The shared formatting helpers console_format_int32 /
//      console_format_double are exercised directly (they are the formatting
//      core of the whole Write/WriteLine family).
//   5. The no-newline Write variants concatenate without separators.
//   6. icall_console_read_line reads one line per call from redirected
//      stdin: a plain line, a CRLF line (CR stripped), a blank line (empty
//      managed string, not null) and finally EOF (null).
//   7. icall_console_read_key is a blocking console-only call (not testable
//      from a redirected process); its symbol is exercised via address-of so
//      link failures surface here.
//   8. M1 core WriteLine(string): a string carrying "\r\n" (Environment
//      NewLine, StringBuilder dumps) is emitted byte-exactly -- "\r\r\n"
//      corruption never occurs (stdout runs in binary mode; the newline
//      helper writes the explicit platform terminator).
//   9. M1 core WriteLine(int) / WriteLine() emit exactly one line each.
//  10. M1 core Write(int|string) concatenate without newlines.
//  11. console_write_newline emits exactly one platform terminator.
//  12. UTF-8 output (UTF8 diagnostic C1): write_line_s / write_s UTF-8
//      encode the UTF-16 payload -- Chinese BMP ("zhong" -> E4 B8 AD, "ni hao" ->
//      E4 BD A0 E5 A5 BD), the 2-byte boundary (U+0080 -> C2 80), astral
//      pairs (U+1F600 -> F0 9F 98 80), unpaired surrogates -> U+FFFD
//      (EF BF BD, .NET default replacement fallback); the char variants
//      encode single code units the same way.
//  13. WriteLine(ulong) full-range decimal (box diagnostic work order 4): UInt64.MaxValue
//      prints "18446744073709551615" -- NEVER the int64 interpretation -1
//      (the int64 helper's behavior); 2^63 and 0 also render exactly;
//      console_format_u64 is the public formatter (UInt64ToString digit loop).
//  14. WriteLine(object) -- the runtime object printer (hashtable2/hashtablecase
//      untracked-box unblock): a null value prints ONLY the line terminator
//      (net48 TextWriter.WriteLine(object) null semantics); a boxed int/bool/
//      char/double/string renders its .NET ToString text ("42", "True", "X",
//      "2.75", "boxedstr"); a non-primitive object prints its type's full name
//      (the icall_object_to_string fallback).
//  16. Console output code-page follow (net48 Console.OutputEncoding
//      default): main() pins 65001, so every byte expectation above holds on
//      any host console; a GBK (936) console emits GBK bytes (zhong -> D6 D0)
//      while unmappable output (astral pairs, U+221E infinity) becomes '?'
//      (net48 replacement fallback, best-fit on) -- the diffuri002 parity.
//      Windows-only; skipped when the process has no console to switch.
//
// stdout is captured via freopen into a scratch file (and restored), stdin
// is redirected from a scratch file for the ReadLine section. The capture
// file is opened in BINARY mode on both ends so assertions compare the raw
// emitted bytes ("\r\n" on Windows, "\n" on POSIX -- see CONSOLE_NEWLINE).
// The restore device name follows the platform convention.
//------------------------------------------------------------------------------

// The test harness needs freopen/fopen for stdout capture and stdin
// redirection -- MSVC deprecates them (C4996) in favor of the *_s variants,
// which do not exist on POSIX. The deprecated calls are intentional and
// well-formed here (the _dup/_dup2 restore path is the reliable mechanism);
// silence the deprecation for MSVC only.
#ifdef _MSC_VER
    #define _CRT_SECURE_NO_WARNINGS
#endif

#include "il2cpp_console.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"
#include "il2cpp_boxing_ext.h"
#include "il2cpp_exception.h" // il2cpp::DotNetException (SetOut/SetError(null) ANE)

#include <cstdio>
#include <cstring>
#include <cmath>
#include <limits>
#include <string>

#ifdef _WIN32
    #include <io.h> // _dup / _dup2 (stdout handle save/restore)
    #ifndef WIN32_LEAN_AND_MEAN
        #define WIN32_LEAN_AND_MEAN
    #endif
    #ifndef NOMINMAX
        #define NOMINMAX // keep std::numeric_limits::max() usable with <windows.h>
    #endif
    #include <windows.h> // Get/SetConsoleOutputCP (output-encoding sections)
    #define CONSOLE_CAPTURE_FILE "console_capture.txt"
    #define CONSOLE_INPUT_FILE "console_input.txt"
    #define CONSOLE_RESTORE_NAME "CON"
#else
    #include <unistd.h> // dup / dup2
    #define CONSOLE_CAPTURE_FILE "/tmp/console_capture.txt"
    #define CONSOLE_INPUT_FILE "/tmp/console_input.txt"
    #define CONSOLE_RESTORE_NAME "/dev/tty"
#endif

namespace {

int g_failures = 0;

// Console line terminator as emitted by the runtime (net48 semantics):
// "\r\n" on Windows (stdout runs in binary mode), "\n" on POSIX. The capture
// file is written/read in binary mode, so assertions compare raw bytes.
#ifdef _WIN32
    #define CONSOLE_NEWLINE "\r\n"
#else
    #define CONSOLE_NEWLINE "\n"
#endif

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (the thrown chain is "Type,Base1,Base2,..."; only the
// head is asserted so a base-chain extension does not break the test). Any
// other outcome is a recorded failure.
template <typename F>
void ExpectThrows(const char* expectedType, const char* what, F&& fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        const size_t len = std::strlen(expectedType);
        const bool headMatches = std::strncmp(thrown, expectedType, len) == 0 &&
                                 (thrown[len] == ',' || thrown[len] == '\0');
        if (headMatches)
        {
            return;
        }
        std::printf("FAIL: %s (threw %s, expected %s)\n", what, thrown, expectedType);
        ++g_failures;
        return;
    }
    std::printf("FAIL: %s (no exception thrown, expected %s)\n", what, expectedType);
    ++g_failures;
}

// Saved stdout handle, taken BEFORE the capture freopen so the console
// stream can be restored afterwards. NOTE: on MinGW/MSVCRT a plain
// freopen("CON", "w", stdout) returns a valid pointer but subsequent stdout
// writes are silently dropped -- the _dup/_dup2 pair is the reliable path
// (verified experimentally; the freopen fallback is kept for POSIX).
int g_savedStdout = -1;// Switch stdout to the scratch capture file; all subsequent runtime console
// output lands there. Callers must flush before capturing.
void BeginCapture()
{
    std::fflush(stdout);
#ifdef _WIN32
    g_savedStdout = _dup(_fileno(stdout));
#else
    g_savedStdout = dup(STDOUT_FILENO);
#endif
    // Binary mode: the runtime emits explicit "\r\n" on Windows (stdout is
    // byte-transparent); capturing in text mode would translate the '\n'
    // again and the byte-level assertions below could never hold.
    std::freopen(CONSOLE_CAPTURE_FILE, "wb", stdout);
}

// Restore stdout to the console and return the captured bytes as a string.
std::string EndCapture()
{
    std::fflush(stdout);
#ifdef _WIN32
    if (g_savedStdout != -1)
    {
        _dup2(g_savedStdout, _fileno(stdout));
        _close(g_savedStdout);
        g_savedStdout = -1;
    }
    else
    {
        std::freopen(CONSOLE_RESTORE_NAME, "w", stdout);
    }
#else
    if (g_savedStdout != -1)
    {
        dup2(g_savedStdout, STDOUT_FILENO);
        close(g_savedStdout);
        g_savedStdout = -1;
    }
    else
    {
        std::freopen(CONSOLE_RESTORE_NAME, "w", stdout);
    }
#endif
    // Raw byte read: the runtime output is compared exactly as emitted.
    std::FILE* file = std::fopen(CONSOLE_CAPTURE_FILE, "rb");
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

// Write a byte string to the scratch stdin file (used by the ReadLine tests).
void WriteInputFile(const char* text)
{
    std::FILE* file = std::fopen(CONSOLE_INPUT_FILE, "wb");
    if (file != nullptr)
    {
        std::fwrite(text, 1, std::strlen(text), file);
        std::fclose(file);
    }
}

// Saved stderr handle for the console STREAM error-target tests (the
// Error stream writes to stderr; the stdout helpers above cannot see them).
int g_savedStderr = -1;

void BeginCaptureErr()
{
    std::fflush(stderr);
#ifdef _WIN32
    g_savedStderr = _dup(_fileno(stderr));
#else
    g_savedStderr = dup(STDERR_FILENO);
#endif
    std::freopen(CONSOLE_CAPTURE_FILE, "wb", stderr);
}

std::string EndCaptureErr()
{
    std::fflush(stderr);
#ifdef _WIN32
    if (g_savedStderr != -1)
    {
        _dup2(g_savedStderr, _fileno(stderr));
        _close(g_savedStderr);
        g_savedStderr = -1;
    }
    else
    {
        std::freopen(CONSOLE_RESTORE_NAME, "w", stderr);
    }
#else
    if (g_savedStderr != -1)
    {
        dup2(g_savedStderr, STDERR_FILENO);
        close(g_savedStderr);
        g_savedStderr = -1;
    }
    else
    {
        std::freopen(CONSOLE_RESTORE_NAME, "w", stderr);
    }
#endif
    std::FILE* file = std::fopen(CONSOLE_CAPTURE_FILE, "rb");
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
    using il2cpp::runtime::icall_console_write_line_b;
    using il2cpp::runtime::icall_console_write_line_c;
    using il2cpp::runtime::icall_console_write_line_d;
    using il2cpp::runtime::icall_console_write_line_f;
    using il2cpp::runtime::icall_console_write_line_u64;
    using il2cpp::runtime::icall_console_write_line_s;
    using il2cpp::runtime::icall_console_write_line_i;
    using il2cpp::runtime::icall_console_write_line_n;
    using il2cpp::runtime::icall_console_write_b;
    using il2cpp::runtime::icall_console_write_c;
    using il2cpp::runtime::icall_console_write_d;
    using il2cpp::runtime::icall_console_write_f;
    using il2cpp::runtime::icall_console_write_s;
    using il2cpp::runtime::icall_console_write_i;
    using il2cpp::runtime::icall_console_read_line;
    using il2cpp::runtime::icall_console_read_key;
    using il2cpp::runtime::console_format_int32;
    using il2cpp::runtime::console_format_u64;
    using il2cpp::runtime::console_format_double;
    using il2cpp::runtime::console_format_single;
    using il2cpp::runtime::console_write_newline;

#ifdef _WIN32
    // Pin UTF-8 output mode: every byte expectation below assumes 65001.
    // (The ambient console on zh-CN hosts is 936/GBK, which would otherwise
    // re-encode non-ASCII output -- the diffuri002 GBK finding.) The GBK
    // section (16) switches explicitly; the original CP is restored at the
    // end of main.
    const unsigned savedOutputCP = static_cast<unsigned>(::GetConsoleOutputCP());
    ::SetConsoleOutputCP(65001);
#endif

    // --- 1. WriteLine(bool): .NET bool.ToString() ---
    BeginCapture();
    icall_console_write_line_b(1);
    icall_console_write_line_b(0);
    Expect(EndCapture() == "True" CONSOLE_NEWLINE "False" CONSOLE_NEWLINE,
           "WriteLine(bool) renders True/False");

    // --- 2. WriteLine(char): ASCII char + newline; UTF-8 for non-ASCII ---
    BeginCapture();
    icall_console_write_line_c(65);   // 'A'
    icall_console_write_line_c(0x4E2D); // 'zhong' (U+4E2D -> E4 B8 AD)
    Expect(EndCapture() == "A" CONSOLE_NEWLINE "\xE4\xB8\xAD" CONSOLE_NEWLINE,
           "WriteLine(char) renders the ASCII char and UTF-8 for non-ASCII");

    // --- 2b. UTF-8 encoding: Chinese BMP + astral + unpaired surrogates ---
    // The M3 '?' policy is retired: write_line_s/write_s UTF-8 encode the
    // UTF-16 payload, so "zhong" (U+4E2D) emits E4 B8 AD byte-for-byte (stdout
    // is in binary mode -- console.cpp ConsoleStreamInitializer -- so the
    // bytes go out verbatim). Unpaired surrogates become U+FFFD (EF BF BD),
    // the .NET default replacement fallback.
    static const char16_t zhong[] = { 0x4E2D };             // "zhong"
    static const char16_t nihao[] = { 0x4F60, 0x597D };     // "ni hao"
    static const char16_t mixed[] = { 0x41, 0x4E2D, 0x42 }; // "AzhongB"
    static const char16_t u0080[] = { 0x80 };
    static const char16_t emoji[] = { 0xD83D, 0xDE00 };    // U+1F600
    static const char16_t loneHigh[] = { 0xD800 };
    static const char16_t loneLow[] = { 0xDC00 };
    static const char16_t highNonPair[] = { 0xD800, 0x41 };
    BeginCapture();
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(zhong, 1)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(nihao, 2)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(mixed, 3)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(u0080, 1)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(emoji, 2)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(loneHigh, 1)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(loneLow, 1)));
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(highNonPair, 2)));
    const std::string utf8Out = EndCapture();
    Expect(utf8Out == "\xE4\xB8\xAD" CONSOLE_NEWLINE            // zhong
           "\xE4\xBD\xA0\xE5\xA5\xBD" CONSOLE_NEWLINE           // ni hao
           "A" "\xE4\xB8\xAD" "B" CONSOLE_NEWLINE               // AzhongB
           "\xC2\x80" CONSOLE_NEWLINE                           // U+0080
           "\xF0\x9F\x98\x80" CONSOLE_NEWLINE                   // U+1F600
           "\xEF\xBF\xBD" CONSOLE_NEWLINE                       // lone high -> U+FFFD
           "\xEF\xBF\xBD" CONSOLE_NEWLINE                       // lone low -> U+FFFD
           "\xEF\xBF\xBD" "A" CONSOLE_NEWLINE,                  // high + non-low
           "WriteLine(string) UTF-8 encodes Chinese BMP, astral pairs and unpaired surrogates");

    // Write (no newline) variant: same encoding, no terminator.
    BeginCapture();
    icall_console_write_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(zhong, 1)));
    Expect(EndCapture() == "\xE4\xB8\xAD",
           "Write(string) UTF-8 encodes without a newline");

    // char variants: 2-byte boundary, BMP, unpaired surrogate.
    BeginCapture();
    icall_console_write_line_c(0x80);
    icall_console_write_line_c(0x4E2D);
    icall_console_write_line_c(0xD800);
    Expect(EndCapture() == "\xC2\x80" CONSOLE_NEWLINE
           "\xE4\xB8\xAD" CONSOLE_NEWLINE
           "\xEF\xBF\xBD" CONSOLE_NEWLINE,
           "WriteLine(char) UTF-8 encodes 2-byte, 3-byte and surrogate code units");

    // --- 3. WriteLine(double): net48 default G15 (15 sig digits) ---
    BeginCapture();
    icall_console_write_line_d(1.5);
    icall_console_write_line_d(0.1);
    icall_console_write_line_d(150.0);
    icall_console_write_line_d(-2.25);
    icall_console_write_line_d(0.0);
    icall_console_write_line_d(std::numeric_limits<double>::quiet_NaN());
    icall_console_write_line_d(std::numeric_limits<double>::infinity());
    icall_console_write_line_d(-std::numeric_limits<double>::infinity());
    icall_console_write_line_d(1e21);
    Expect(EndCapture() == "1.5" CONSOLE_NEWLINE "0.1" CONSOLE_NEWLINE
           "150" CONSOLE_NEWLINE "-2.25" CONSOLE_NEWLINE "0" CONSOLE_NEWLINE
           "NaN" CONSOLE_NEWLINE "\xE2\x88\x9E" CONSOLE_NEWLINE "-\xE2\x88\x9E" CONSOLE_NEWLINE
           "1E+21" CONSOLE_NEWLINE,
           "WriteLine(double) renders net48 G15 default text");

    // --- 3b. G15 pinning: values where G15 and shortest round-trip differ ---
    // (net48-measured: Sqrt(2) -> "1.4142135623731", 0.1+0.2 -> "0.3",
    // 2^63 -> "9.22337203685478E+18", -0.0 -> "0"; the round-trip forms
    // would be "...0951", "0.30000000000000004", "...7758E+18", "-0").
    BeginCapture();
    icall_console_write_line_d(std::sqrt(2.0));
    icall_console_write_line_d(0.1 + 0.2);
    icall_console_write_line_d(9223372036854775808.0);
    icall_console_write_line_d(-0.0);
    Expect(EndCapture() == "1.4142135623731" CONSOLE_NEWLINE "0.3" CONSOLE_NEWLINE
           "9.22337203685478E+18" CONSOLE_NEWLINE "0" CONSOLE_NEWLINE,
           "WriteLine(double) is net48 G15, not shortest round-trip");

    // --- 3d. WriteLine(float)/Write(float): net48 float G7 text (miscfmt) ---
    // net48 TextWriter.Write(float) renders the FLOAT G7 default (not the
    // double G15): 0.1f -> "0.1", 3.14159265f -> "3.141593",
    // 16777216f -> "1.677722E+07", -0.0f -> "0", 1e-8f -> "1E-08".
    BeginCapture();
    icall_console_write_line_f(static_cast<double>(0.1f));
    icall_console_write_line_f(static_cast<double>(3.14159265f));
    icall_console_write_line_f(16777216.0);
    icall_console_write_line_f(-0.0);
    icall_console_write_line_f(static_cast<double>(1e-8f));
    icall_console_write_line_f(std::numeric_limits<double>::quiet_NaN());
    icall_console_write_line_f(std::numeric_limits<double>::infinity());
    icall_console_write_line_f(-std::numeric_limits<double>::infinity());
    icall_console_write_line_f(static_cast<double>(3.40282347e38f));
    Expect(EndCapture() == "0.1" CONSOLE_NEWLINE "3.141593" CONSOLE_NEWLINE
           "1.677722E+07" CONSOLE_NEWLINE "0" CONSOLE_NEWLINE "1E-08" CONSOLE_NEWLINE
           "NaN" CONSOLE_NEWLINE "\xE2\x88\x9E" CONSOLE_NEWLINE "-\xE2\x88\x9E" CONSOLE_NEWLINE
           "3.402823E+38" CONSOLE_NEWLINE,
           "WriteLine(float) renders net48 G7 float text (miscfmt)");

    BeginCapture();
    icall_console_write_f(static_cast<double>(0.1f));
    icall_console_write_f(static_cast<double>(3.14159265f));
    icall_console_write_f(-0.0);
    Expect(EndCapture() == "0.13.1415930",
           "Write(float) renders net48 G7 float text without newlines (miscfmt)");

    Expect(console_format_single(static_cast<double>(0.1f)) == "0.1",
           "format_single renders 0.1f as \"0.1\" (G7)");
    Expect(console_format_single(static_cast<double>(3.14159265f)) == "3.141593",
           "format_single renders pi as \"3.141593\" (7 digits, not G15)");
    Expect(console_format_single(-0.0) == "0",
           "format_single drops the negative zero sign (net48)");
    Expect(console_format_single(16777216.0) == "1.677722E+07",
           "format_single renders 2^24 as \"1.677722E+07\"");
    Expect(console_format_single(std::numeric_limits<double>::quiet_NaN()) == "NaN",
           "format_single renders NaN as \"NaN\"");
    Expect(console_format_single(std::numeric_limits<double>::infinity()) == "\xE2\x88\x9E",
           "format_single renders +inf as UTF-8 U+221E (net48 zh-CN)");

    // --- 3b2. tiebreak2: the console float/double text carries the net48
    // tie rule (half AWAY FROM ZERO). console_format_single now delegates to
    // icall_format_float_custom (the Single.ToString() engine), so an EXACT
    // 7-digit tie prints the rounded-up digit instead of the CRT's half-even
    // one; console_format_double reads the shared G15 engine. Expected strings
    // are the net48 oracle capture (tests/cases/probe-tiebreak2).
    Expect(console_format_single(static_cast<double>(12345645.0f)) == "1.234565E+07",
           "format_single renders the 12345645f tie as \"1.234565E+07\" (net48)");
    Expect(console_format_single(static_cast<double>(6000000.5f)) == "6000001",
           "format_single renders the 6000000.5f tie as \"6000001\" (net48)");
    Expect(console_format_single(static_cast<double>(0.00048828125f)) == "0.0004882813",
           "format_single renders the 0.00048828125f tie as \"0.0004882813\" (net48)");
    Expect(console_format_single(static_cast<double>(-12345645.0f)) == "-1.234565E+07",
           "format_single renders the negative float tie away from zero");
    Expect(console_format_single(static_cast<double>(12345655.0f)) == "1.234566E+07",
           "format_single keeps the odd-prefix float control at one digit up");
    Expect(console_format_double(1234567890123445.0) == "1.23456789012345E+15",
           "format_double renders the 15-digit double tie as \"1.23456789012345E+15\" (net48)");
    Expect(console_format_double(10000000000000.25) == "10000000000000.3",
           "format_double renders the 10000000000000.25 tie as \"10000000000000.3\" (net48)");
    Expect(console_format_double(-1234567890123445.0) == "-1.23456789012345E+15",
           "format_double renders the negative double tie away from zero");
    Expect(console_format_double(1234567890123444.0) == "1.23456789012344E+15",
           "format_double keeps the near-tie double control unchanged");
    // --- 4. Shared formatting helpers (direct) ---
    Expect(console_format_int32(0) == "0", "format_int32 renders zero");
    Expect(console_format_int32(-12345) == "-12345", "format_int32 renders negatives");
    Expect(console_format_int32(2147483647) == "2147483647", "format_int32 renders int32 max");
    Expect(console_format_double(2.0) == "2", "format_double drops the trailing .0 (net48: \"2\")");

    // --- 3c. Special values: NaN spelling + U+221E infinity bytes ---
    // net48 zh-CN (empirical byte capture): +inf -> E2 88 9E ('∞', U+221E),
    // -inf -> 2D E2 88 9E ('-' ASCII + U+221E). The console writes raw bytes
    // (binary-mode stdout), so the string must carry the exact UTF-8 form.
    Expect(console_format_double(std::numeric_limits<double>::quiet_NaN()) == "NaN",
           "format_double renders NaN as \"NaN\"");
    Expect(console_format_double(std::numeric_limits<double>::infinity()) == "\xE2\x88\x9E",
           "format_double renders +inf as UTF-8 U+221E (E2 88 9E, net48 zh-CN)");
    Expect(console_format_double(-std::numeric_limits<double>::infinity()) == "-\xE2\x88\x9E",
           "format_double renders -inf as ASCII '-' + UTF-8 U+221E (net48 zh-CN)");

    // --- 4b. WriteLine(ulong): full-range decimal (box diagnostic work order 4) ---
    // The regression this pins: routing UInt64.MaxValue through the int64
    // helper (il2cpp_console_write_line_l) would print -1. The runtime icall
    // renders the full uint64 range through the UInt64ToString digit loop.
    BeginCapture();
    icall_console_write_line_u64(0);
    icall_console_write_line_u64(1);
    icall_console_write_line_u64(12345678901234567890ULL);
    icall_console_write_line_u64(9223372036854775808ULL); // 2^63: first value beyond int64
    icall_console_write_line_u64(std::numeric_limits<uint64_t>::max());
    Expect(EndCapture() == "0" CONSOLE_NEWLINE "1" CONSOLE_NEWLINE
           "12345678901234567890" CONSOLE_NEWLINE "9223372036854775808" CONSOLE_NEWLINE
           "18446744073709551615" CONSOLE_NEWLINE,
           "WriteLine(ulong) renders the full uint64 range (MaxValue is 18446744073709551615, never -1)");
    Expect(console_format_u64(0) == "0", "format_u64 renders zero");
    Expect(console_format_u64(10000000000000000000ULL) == "10000000000000000000",
           "format_u64 renders 10^19");
    Expect(console_format_u64(std::numeric_limits<uint64_t>::max()) == "18446744073709551615",
           "format_u64 renders uint64 max");

    // --- 5. Write variants: no newline, concatenate ---
    BeginCapture();
    icall_console_write_b(0);
    icall_console_write_c(120); // 'x'
    icall_console_write_d(2.25);
    Expect(EndCapture() == "Falsex2.25", "Write(bool|char|double) concatenate without newlines");

    // --- 6. ReadLine from redirected stdin ---
    WriteInputFile("alpha\nbeta\r\n\n");
    std::freopen(CONSOLE_INPUT_FILE, "rb", stdin);
    il2cpp::Il2CppString* first = reinterpret_cast<il2cpp::Il2CppString*>(icall_console_read_line());
    il2cpp::Il2CppString* second = reinterpret_cast<il2cpp::Il2CppString*>(icall_console_read_line());
    il2cpp::Il2CppString* empty = reinterpret_cast<il2cpp::Il2CppString*>(icall_console_read_line());
    il2cpp::Il2CppString* eof = reinterpret_cast<il2cpp::Il2CppString*>(icall_console_read_line());
    Expect(first != nullptr && first->length == 5
           && first->chars[0] == u'a' && first->chars[4] == u'a',
           "ReadLine reads the first plain line");
    Expect(second != nullptr && second->length == 4
           && second->chars[0] == u'b' && second->chars[3] == u'a',
           "ReadLine strips the CR of a CRLF line");
    Expect(empty != nullptr && empty->length == 0,
           "ReadLine returns an empty (non-null) string for a blank line");
    Expect(eof == nullptr, "ReadLine returns null at EOF");

    // --- 7. ReadKey symbol linkage (blocking console call, not exercised) ---
    int32_t (*readKeyFn)(int32_t) = &icall_console_read_key;
    Expect(readKeyFn != nullptr, "ReadKey icall symbol links");

    // --- 8. M1 core string WriteLine: CRLF byte-exactness regression ---
    // A managed string containing "\r\n" (e.g. Environment.NewLine, sbchain
    // AppendLine dumps) must be emitted verbatim: one "\r\n" per source
    // pair, NEVER "\r\r\n" (text-mode '\n' translation double-inserts '\r').
    il2cpp::Il2CppString* crlfString =
        il2cpp::il2cpp_string_new_from_cs("Name: Bob\r\nAge: 30\r\n");
    BeginCapture();
    icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(crlfString));
    const std::string crlfOut = EndCapture();
    // The embedded "\r\n" pairs are emitted VERBATIM on every platform --
    // .NET never translates CRLF inside a string (TextWriter writes the
    // characters as-is); only the TRAILING terminator is platform-specific
    // ("\r\n" on Windows, "\n" on POSIX). The net48 regression this pins is
    // byte-exactness: one "\r\n" per source pair, never "\r\r\n".
    Expect(crlfOut == "Name: Bob\r\nAge: 30\r\n" CONSOLE_NEWLINE,
           "WriteLine(string) emits string CRLFs verbatim plus one trailing newline");
    Expect(crlfOut.find("\r\r\n") == std::string::npos,
           "WriteLine(string) never emits the \\r\\r\\n corruption");

    // --- 9. M1 core int/empty WriteLine: exactly one newline per line ---
    BeginCapture();
    icall_console_write_line_i(30);
    icall_console_write_line_n();
    const std::string intOut = EndCapture();
    Expect(intOut == "30" CONSOLE_NEWLINE CONSOLE_NEWLINE,
           "WriteLine(int) + WriteLine() emit one line each");
    Expect(intOut.find("\r\r\n") == std::string::npos,
           "WriteLine(int)/WriteLine() never emit \\r\\r\\n");

    // --- 10. M1 core Write(int|string): no newline, concatenate ---
    BeginCapture();
    icall_console_write_i(7);
    icall_console_write_s(reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs("x")));
    Expect(EndCapture() == "7x", "Write(int|string) concatenate without newlines");

    // --- 11. newline helper emits exactly one platform terminator ---
    BeginCapture();
    console_write_newline();
    Expect(EndCapture() == CONSOLE_NEWLINE,
           "console_write_newline emits exactly one platform terminator");

    // --- 12. Console STREAM surface (console3 work order) ---
    // icall_console_get_out / get_error return the current stream objects;
    // writes dispatch on the object's kind (Out -> stdout, Error -> stderr,
    // Null -> discard); SetOut/SetError(TextWriter.Null) -- the null CONSOLE
    // STREAM object, not a null managed argument -- make the stream discard,
    // and SetOut/SetError(null) throw ArgumentNullException -- net48 semantics
    // (csc-probe pinned).
    using il2cpp::runtime::icall_console_get_out;
    using il2cpp::runtime::icall_console_get_error;
    using il2cpp::runtime::icall_console_set_out;
    using il2cpp::runtime::icall_console_set_error;
    using il2cpp::runtime::icall_console_stream_write;
    using il2cpp::runtime::icall_console_stream_write_line;

    // 15a. The getters return non-null, distinct stream objects.
    il2cpp::Il2CppObject* outStream = icall_console_get_out();
    il2cpp::Il2CppObject* errStream = icall_console_get_error();
    Expect(outStream != nullptr && errStream != nullptr,
           "get_Out/get_Error return live stream objects");
    Expect(outStream != errStream,
           "Console.Out and Console.Error are distinct stream objects");

    // 15b. Out-stream Write/WriteLine emit to stdout (no newline / one line).
    BeginCapture();
    icall_console_stream_write(outStream,
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("A")));
    icall_console_stream_write_line(outStream,
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("B")));
    icall_console_stream_write_line(outStream, nullptr); // null -> just the terminator
    Expect(EndCapture() == "A" "B" CONSOLE_NEWLINE CONSOLE_NEWLINE,
           "Out-stream Write/WriteLine emit text + terminator to stdout");

    // 15b2. Out-stream Write(float)/WriteLine(float): net48 float G7 text
    // (miscfmt) -- the console stream must not render the double G15 text.
    using il2cpp::runtime::icall_console_stream_write_f;
    using il2cpp::runtime::icall_console_stream_write_line_f;
    BeginCapture();
    icall_console_stream_write_f(outStream, static_cast<double>(0.1f));
    icall_console_stream_write_line_f(outStream, static_cast<double>(3.14159265f));
    icall_console_stream_write_line_f(outStream, -0.0);
    Expect(EndCapture() == "0.1" "3.141593" CONSOLE_NEWLINE "0" CONSOLE_NEWLINE,
           "Out-stream Write(float)/WriteLine(float) render net48 G7 float text (miscfmt)");

    // 15c. Error-stream writes emit to STAR ERR (the Error stream targets stderr).
    BeginCaptureErr();
    icall_console_stream_write_line(errStream,
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("E1")));
    icall_console_stream_write(errStream,
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("E2")));
    Expect(EndCaptureErr() == "E1" CONSOLE_NEWLINE "E2",
           "Error-stream Write/WriteLine emit to stderr");

    // 15d. SetOut(TextWriter.Null) / SetError(TextWriter.Null) swap in the
    // null (discarding) stream: writes vanish. TextWriter.Null is the NULL
    // CONSOLE STREAM OBJECT (System_IO_TextWriter_sf_Null, filled by
    // EnsureConsoleStreams) -- a REAL object, NOT a null managed argument.
    il2cpp::Il2CppObject* textWriterNull = System_IO_TextWriter_sf_Null;
    Expect(textWriterNull != nullptr,
           "TextWriter.Null is the null console stream object (non-null)");
    icall_console_set_out(textWriterNull);
    icall_console_set_error(textWriterNull);
    Expect(icall_console_get_out() != outStream,
           "SetOut(TextWriter.Null) swaps the Out stream");
    Expect(icall_console_get_error() != errStream,
           "SetError(TextWriter.Null) swaps the Error stream");
    BeginCapture();
    icall_console_stream_write_line(icall_console_get_out(),
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("SW")));
    icall_console_stream_write_line(icall_console_get_error(),
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("SE")));
    Expect(EndCapture() == "",
           "writes to the null-swapped streams discard (SetOut/SetError TextWriter.Null)");

    // 15e. SetOut(stream) / SetError(stream) restore the stream: writes flow
    // again (the null->Out swap re-arms stdout; the Error stream stays null).
    icall_console_set_out(outStream);
    icall_console_set_error(errStream);
    BeginCapture();
    icall_console_stream_write_line(icall_console_get_out(),
        reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs("R")));
    Expect(EndCapture() == "R" CONSOLE_NEWLINE,
           "SetOut(stream) restores the Out stream");

    // 15f. SetOut(null) / SetError(null) -- a null MANAGED argument, NOT
    // TextWriter.Null -- throws ArgumentNullException (net48 csc-probe,
    // paramName newOut/newError) and leaves the stream untouched (the
    // D-CON-1/2 defect probe in the consolestream e2e case).
    ExpectThrows("System.ArgumentNullException",
                 "SetOut(null) throws ArgumentNullException",
                 []() { icall_console_set_out(nullptr); });
    ExpectThrows("System.ArgumentNullException",
                 "SetError(null) throws ArgumentNullException",
                 []() { icall_console_set_error(nullptr); });
    Expect(icall_console_get_out() == outStream,
           "SetOut(null) leaves the Out stream untouched");
    Expect(icall_console_get_error() == errStream,
           "SetError(null) leaves the Error stream untouched");

#ifdef _WIN32
    // --- 16. GBK (936) code-page follow (net48 Console.OutputEncoding ---
    // default): zhong -> GBK D6 D0; astral/lone/infinity -> '?' (replacement
    // fallback). Asserted on every console text path: char/stream/object/
    // double/infinity + console_write_units + the M1 string icalls
    // (runtime.cpp WriteStringUtf8 dispatcher).
    // Needs a real console to switch; without one SetConsoleOutputCP fails.
    if (::SetConsoleOutputCP(936))
    {
        using il2cpp::runtime::console_output_code_page;
        using il2cpp::runtime::console_write_units;
        using il2cpp::runtime::icall_console_write_line_object;
        Expect(console_output_code_page() == 936u,
               "output code page follows SetConsoleOutputCP(936)");
        static const char16_t zhong1[] = { 0x4E2D };
        static const char16_t emoji1[] = { 0xD83D, 0xDE00 };
        static const char16_t lone1[] = { 0xD800 };
        BeginCapture();
        icall_console_write_c(0x4E2D);
        Expect(EndCapture() == "\xD6\xD0",
               "Write(char) zhong emits GBK D6 D0 under 936");
        BeginCapture();
        icall_console_stream_write(outStream,
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(zhong1, 1)));
        Expect(EndCapture() == "\xD6\xD0",
               "stream Write(zhong) emits GBK D6 D0 under 936");
        BeginCapture();
        icall_console_stream_write_line(outStream,
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(emoji1, 2)));
        Expect(EndCapture() == "?" CONSOLE_NEWLINE,
               "stream WriteLine(astral) emits '?' under 936");
        BeginCapture();
        icall_console_stream_write(outStream,
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(lone1, 1)));
        Expect(EndCapture() == "?",
               "stream Write(lone surrogate) emits '?' under 936");
        BeginCapture();
        icall_console_write_d(std::numeric_limits<double>::infinity());
        icall_console_write_d(-std::numeric_limits<double>::infinity());
        Expect(EndCapture() == "?-?",
               "Write(double) infinity emits '?/-?' under 936");
        BeginCapture();
        icall_console_write_f(std::numeric_limits<double>::infinity());
        icall_console_write_f(-std::numeric_limits<double>::infinity());
        Expect(EndCapture() == "?-?",
               "Write(float) infinity emits '?/-?' under 936");
        BeginCapture();
        icall_console_write_line_object(
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(zhong1, 1)));
        Expect(EndCapture() == "\xD6\xD0" CONSOLE_NEWLINE,
               "WriteLine(object string) emits GBK D6 D0 under 936");
        BeginCapture();
        console_write_units(zhong1, 1, stdout);
        Expect(EndCapture() == "\xD6\xD0",
               "console_write_units(zhong) emits GBK D6 D0 under 936");
        BeginCapture();
        icall_console_write_line_s(
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(zhong1, 1)));
        Expect(EndCapture() == "\xD6\xD0" CONSOLE_NEWLINE,
               "M1 WriteLine(string) zhong emits GBK D6 D0 under 936");
        BeginCapture();
        icall_console_write_s(
            reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new(emoji1, 2)));
        Expect(EndCapture() == "?",
               "M1 Write(string) astral emits '?' under 936");
        ::SetConsoleOutputCP(65001);
        Expect(console_output_code_page() == 65001u,
               "output code page restored to 65001");
    }
    else
    {
        std::printf("SKIP: no console to switch code page (section 16)\n");
    }
#endif

    if (g_failures != 0)
    {
        std::printf("%d console assertion(s) failed\n", g_failures);
#ifdef _WIN32
        ::SetConsoleOutputCP(savedOutputCP);
#endif
        return 1;
    }
    std::printf("all runtime console tests passed\n");
#ifdef _WIN32
    ::SetConsoleOutputCP(savedOutputCP);
#endif
    return 0;
}
