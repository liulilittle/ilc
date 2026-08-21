//------------------------------------------------------------------------------
// console.cpp -- System.Console icall extension module (see il2cpp_console.h
// for the full contract).
//
// All output goes through the C stdio stream 'stdout' so the generated-code
// output pipeline (which also prints through stdout) stays byte-ordered with
// these calls. Value types cross the icall boundary in the generated-code
// ABI form: bool -> int32_t (0/1), char -> int32_t (code unit), double ->
// double.
//
// Formatting: console_format_double implements .NET-style shortest round-trip
// rendering -- the net48 reference baseline is double.ToString(). The
// algorithm picks the smallest precision in [1,17] whose "%.*g" rendering
// parses back to exactly the same double, preferring fixed notation when both
// forms round-trip (so 150.0 renders "150", not "1.5e+02"), normalizes the
// exponent separator to the .NET 'E' form and maps the special values NaN /
// +-Infinity to .NET's text.
//------------------------------------------------------------------------------

#include "il2cpp_console.h"
#include "il2cpp_runtime.h"          // icall_console_write_line_s (the M1 string printer)
#include "il2cpp_string.h"
#include "il2cpp_value_semantics.h"  // icall_object_to_string (boxed ToString dispatch)
#include "il2cpp_exception.h"        // il2cpp_throw_new (SetOut/SetError(null) ArgumentNullException)

#include "il2cpp_gc.h"               // il2cpp_gc_add_static_root (console stream roots)

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>

// System.IO.TextWriter::Null -- the BCL static-field storage slot the
// transpiler emits ldsfld System.IO.TextWriter::Null as. In the ILC object
// model TextWriter.Null is the NULL (discarding) CONSOLE STREAM OBJECT -- a
// real, non-null object that SetOut/SetError recognize by its kConsoleStreamNull
// kind and swap in as the discard stream (net48 semantics); writing to it is a
// no-op. The slot is filled by EnsureConsoleStreams (which creates that object);
// it is NEVER nullptr once the console streams exist, so SetOut(null) -- a null
// MANAGED argument -- is distinguishable from SetOut(TextWriter.Null).
// (The stopwatch.cpp System_Diagnostics_Stopwatch_sf_Frequency precedent --
// the runtime owns mscorlib static storage.)
il2cpp::Il2CppObject* System_IO_TextWriter_sf_Null = nullptr;

#ifdef _WIN32
    #include <conio.h>
    #include <fcntl.h>
    #include <io.h>
#else
    #include <termios.h>
    #include <unistd.h>
#endif

namespace il2cpp { namespace runtime {

#ifdef _WIN32
namespace {

// stdout text mode translates every '\n' into "\r\n". Managed strings may
// carry '\r' (Environment.NewLine is "\r\n"), so writing such a string
// through a text-mode stream turns its "\r\n" into "\r\r\n" -- a byte-level
// divergence from the net48 reference output. Binary mode makes the stream
// byte-transparent; console_write_newline() then emits the explicit "\r\n"
// pair (net48 Console.WriteLine semantics on Windows). stderr gets the same
// binary mode: the console STREAM writes (Console.Error, il2cpp_console.h)
// emit the explicit "\r\n" pair through it too, and a text-mode stderr would
// double-translate it into "\r\r\n".
struct ConsoleStreamInitializer
{
    ConsoleStreamInitializer()
    {
        _setmode(_fileno(stdout), _O_BINARY);
        _setmode(_fileno(stderr), _O_BINARY);
    }
};

ConsoleStreamInitializer g_consoleStreamInitializer;

} // namespace
#endif

namespace {

// Print one character: UTF-8 encode the 16-bit code unit (the same policy
// the M1 string icalls apply to UTF-16 payloads -- see runtime.cpp
// WriteStringUtf8). ASCII units go out as-is; BMP units as 2-3 bytes; a
// surrogate code unit (a single char cannot form a pair) becomes U+FFFD,
// the .NET default replacement fallback. The stream runs in binary mode, so
// the bytes reach it verbatim. 'stream' selects the target (stdout for the
// classic Console.Write/WriteLine icalls; the console STREAM writes pass
// stdout/stderr per the stream object's kind).
void PrintCharCode(int32_t codeUnit, FILE* stream)
{
    if (stream == nullptr)
        return;
    if (codeUnit < 0)
    {
        std::fputs("\xEF\xBF\xBD", stream); // caller bug: no negative char
        return;
    }
    const uint32_t u = static_cast<uint32_t>(codeUnit);
    if (u < 0x80)
    {
        std::fputc(static_cast<char>(u), stream);
    }
    else if (u < 0x800)
    {
        std::fputc(static_cast<char>(0xC0 | (u >> 6)), stream);
        std::fputc(static_cast<char>(0x80 | (u & 0x3F)), stream);
    }
    else if (u >= 0xD800 && u <= 0xDFFF)
    {
        std::fputs("\xEF\xBF\xBD", stream); // unpaired surrogate -> U+FFFD
    }
    else
    {
        std::fputc(static_cast<char>(0xE0 | (u >> 12)), stream);
        std::fputc(static_cast<char>(0x80 | ((u >> 6) & 0x3F)), stream);
        std::fputc(static_cast<char>(0x80 | (u & 0x3F)), stream);
    }
}

// Write a managed string as UTF-8 (the PrintCharCode per-code-unit policy) to
// the target stream. A null text is a no-op (net48 TextWriter.Write(null)).
void WriteStringToStream(Il2CppObject* text, FILE* stream)
{
    if (text == nullptr || stream == nullptr)
        return;
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(text);
    for (int32_t i = 0; i < str->length; ++i)
        PrintCharCode(str->chars[i], stream);
}

} // namespace

void console_write_newline()
{
#ifdef _WIN32
    // stdout is in binary mode (see ConsoleStreamInitializer): the pair is
    // emitted verbatim, matching net48 Console.WriteLine ("\r\n" on Windows).
    std::fputs("\r\n", stdout);
#else
    std::fputc('\n', stdout);
#endif
}

std::string console_format_int32(int32_t value)
{
    return std::to_string(value);
}

std::string console_format_double(double value)
{
    // net48 renders the double DEFAULT as "G15" -- 15 significant digits,
    // correctly rounded, fixed/scientific layout switch, trailing zeros
    // stripped -- NOT the shortest round-trip text (.NET Core 3.0+
    // behavior). Measured against a net48 reference: Sqrt(2) ->
    // "1.4142135623731" (15 digits), 0.1+0.2 -> "0.3", 2^63 ->
    // "9.22337203685478E+18". The digits come from snprintf("%.15g") -- the
    // same 15-digit CRT conversion format.cpp's G15 path uses; only the
    // C-isms are adapted ('e' -> 'E', -0.0 -> "0"). +-Infinity follows the
    // net48 reference (zh-CN host, empirical capture): the culture symbol
    // U+221E '∞' -- UTF-8 bytes E2 88 9E -- with a leading ASCII '-' for
    // the negative form ("-∞"). stdout runs in binary mode
    // (ConsoleStreamInitializer), so the bytes reach the stream verbatim,
    // byte-identical to what WriteStringUtf8 produces for a managed string
    // carrying the same code units (the formatext.cpp convention).
    if (std::isnan(value))
        return "NaN";
    if (std::isinf(value))
        return std::signbit(value) ? "-\xE2\x88\x9E" : "\xE2\x88\x9E";
    if (value == 0.0)
        return "0"; // net48: the sign of -0.0 is not rendered

    char buffer[64];
    std::snprintf(buffer, sizeof(buffer), "%.15g", value);
    std::string result(buffer);
    const std::string::size_type exponentPos = result.find('e');
    if (exponentPos != std::string::npos)
        result[exponentPos] = 'E'; // "1e+21" -> "1E+21" (net48 exponent form)
    return result;
}

void icall_console_write_line_b(int32_t value)
{
    std::fputs(value != 0 ? "True" : "False", stdout);
    console_write_newline();
}

void icall_console_write_line_c(int32_t value)
{
    PrintCharCode(value, stdout);
    console_write_newline();
}

void icall_console_write_line_d(double value)
{
    const std::string text = console_format_double(value);
    std::fputs(text.c_str(), stdout);
    console_write_newline();
}

std::string console_format_u64(uint64_t value)
{
    // Full-range uint64 decimal: the same digit loop valuesemantics.cpp's
    // UInt64ToString uses (that one is file-local; uint64 values <= INT64_MAX
    // would otherwise route through icall_int64_to_string, which prints
    // UInt64.MaxValue as -1). ASCII digits transcode 1:1 to UTF-8, so the
    // emitted bytes match what WriteStringUtf8 would produce for the same
    // managed string.
    char buffer[24]; // max 20 digits + NUL
    char* p = buffer + sizeof(buffer);
    *--p = '\0';
    do
    {
        *--p = static_cast<char>('0' + (value % 10));
        value /= 10;
    } while (value != 0);
    return std::string(p);
}

void icall_console_write_line_u64(uint64_t value)
{
    const std::string text = console_format_u64(value);
    std::fputs(text.c_str(), stdout);
    console_write_newline();
}

void icall_console_write_line_u32(uint32_t value)
{
    // The uint32 promotes exactly to the u64 digit loop (console_format_u64);
    // 0xFFFFFFFF prints "4294967295", never the signed interpretation -1
    // (convcase L60/L61).
    const std::string text = console_format_u64(value);
    std::fputs(text.c_str(), stdout);
    console_write_newline();
}

void icall_console_write_u32(uint32_t value)
{
    // The no-newline variant of icall_console_write_line_u32 (System.Console
    // .Write semantics): the uint32 promotes exactly to the u64 digit loop
    // (console_format_u64), so 0xFFFFFFFF prints "4294967295", never the
    // signed interpretation -1 -- no trailing newline (the convcase Write
    // probe).
    const std::string text = console_format_u64(value);
    std::fputs(text.c_str(), stdout);
}

void icall_console_write_b(int32_t value)
{
    std::fputs(value != 0 ? "True" : "False", stdout);
}

void icall_console_write_c(int32_t value)
{
    PrintCharCode(value, stdout);
}

void icall_console_write_d(double value)
{
    const std::string text = console_format_double(value);
    std::fputs(text.c_str(), stdout);
}

void icall_console_write_line_object(Il2CppObject* value)
{
    // net48 TextWriter.WriteLine(object): a null value prints ONLY the line
    // terminator (Console.WriteLine((object)null) is an empty line, never a
    // NullReferenceException). Anything else renders through the boxed
    // ToString dispatch (icall_object_to_string) and the M1 string printer
    // (full UTF-16 -> UTF-8 + newline) -- the runtime object printer the
    // hashtable2/hashtablecase WriteLine(object) gap needs.
    if (value == nullptr)
    {
        console_write_newline();
        return;
    }
    Il2CppString* text = icall_object_to_string(value);
    icall_console_write_line_s(reinterpret_cast<Il2CppObject*>(text));
}

Il2CppObject* icall_console_read_line()
{
    std::string line;
    if (!std::getline(std::cin, line))
        return nullptr; // EOF (no characters read) or a stream error
    // Defensive CRLF handling: text-mode stdin already maps "\r\n" to "\n",
    // but strip a trailing '\r' so the managed string matches .NET ReadLine
    // semantics on every platform / binary-mode combination.
    if (!line.empty() && line.back() == '\r')
        line.pop_back();
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(line.c_str()));
}

int32_t icall_console_read_key(int32_t intercept)
{
    (void)intercept; // M4 simplification: the read is always no-echo
#ifdef _WIN32
    // _getch: blocking, no-echo, console-only single-key read (MinGW and
    // MSVC both provide it via conio.h).
    return static_cast<int32_t>(_getch());
#else
    // POSIX fallback (compiled on the WSL/GCC baseline; not exercised there):
    // switch the terminal to raw mode for one blocking character read, then
    // restore the original termios configuration (best-effort).
    struct termios original;
    if (tcgetattr(STDIN_FILENO, &original) != 0)
        return -1;
    struct termios raw = original;
    raw.c_lflag &= ~static_cast<tcflag_t>(ICANON | ECHO);
    raw.c_cc[VMIN] = 1;
    raw.c_cc[VTIME] = 0;
    if (tcsetattr(STDIN_FILENO, TCSANOW, &raw) != 0)
        return -1;
    const int ch = std::getchar();
    (void)tcsetattr(STDIN_FILENO, TCSANOW, &original); // best-effort restore
    return static_cast<int32_t>(ch);
#endif
}

//------------------------------------------------------------------------------
// Console STREAM surface (console3 work order; see il2cpp_console.h). The
// streams are ConsoleStreamObject instances (header + int32 kind). get_Out /
// get_Error return the CURRENT stream; SetOut / SetError swap it; every write
// on a stream object dispatches on its kind (Out -> stdout, Error -> stderr,
// Null -> discard). A null writer (TextWriter.Null == nullptr in the ILC
// model) or the null stream object makes the respective stream discard AND
// redirects the underlying C stdout/stderr to the null device, so even the M1
// core icalls (runtime.cpp, which print straight to stdout) are swallowed
// afterwards (net48: Console.WriteLine writes to Console.Out, the swapped
// stream).
//------------------------------------------------------------------------------

namespace {

struct ConsoleStreamObject
{
    Il2CppObject obj;   // object header (klass pointer, offset 0)
    int32_t kind;       // ConsoleStreamKind (Out / Error / Null)
};

enum ConsoleStreamKind : int32_t
{
    kConsoleStreamOut = 0,
    kConsoleStreamError = 1,
    kConsoleStreamNull = 2,
};

// Layout pin: the kind sits at slot 1 (offset 8), mirroring the gcext.cpp
// Il2CppWeakRefObject handle-slot convention.
static_assert(offsetof(ConsoleStreamObject, kind) == 1 * sizeof(void*),
              "ConsoleStreamObject: kind must sit at slot 1 (offset 8)");

// The console stream runtime descriptor: no reference fields (refFieldBitmap
// 0 -- the collector never scans past the header). Registered lazily so
// isinst/type checks see the class.
il2cpp::Il2CppClass ConsoleStreamClass = {
    "System.IO.TextWriter/ConsoleStream",                        // name
    static_cast<int32_t>(sizeof(ConsoleStreamObject)),           // instanceSize: header + kind
    0u,                                                          // refFieldBitmap: no reference fields
    0,                                                           // refFieldCount
    &System_Object_class,                                        // baseClass (a stream IS-A object)
    nullptr, 0,                                                  // vtable
    nullptr, 0,                                                  // interfaces
};

// The canonical stream objects + the CURRENT Out/Error streams. All five are
// GC roots (il2cpp_gc_add_static_root): they live for the whole process and
// must never be collected. TextWriter.Null == nullptr (not an object).
ConsoleStreamObject* g_consoleStdoutStream = nullptr; // kind Out  (the default Console.Out)
ConsoleStreamObject* g_consoleStderrStream = nullptr; // kind Error (the default Console.Error)
ConsoleStreamObject* g_consoleNullStream = nullptr;   // kind Null (SetOut/SetError(TextWriter.Null) target)
ConsoleStreamObject* g_consoleOutStream = nullptr;    // the current Console.Out
ConsoleStreamObject* g_consoleErrorStream = nullptr;  // the current Console.Error

#ifdef _WIN32
// The fd of the ORIGINAL C stream, saved across the SetOut/SetError null-device
// redirect (see RedirectStdStream) so the restore can put it back (freopen
// alone would reopen the CONSOLE device, which a redirected process cannot
// see). One-shot: saved on the FIRST null redirect, consumed on the restore.
int g_savedStdoutFd = -1;
int g_savedStderrFd = -1;
#else
int g_savedStdoutFd = -1;
int g_savedStderrFd = -1;
#endif

ConsoleStreamObject* StreamAs(Il2CppObject* self)
{
    return reinterpret_cast<ConsoleStreamObject*>(self);
}

bool IsConsoleStream(const Il2CppObject* self)
{
    return self != nullptr && self->klass == &ConsoleStreamClass;
}

// Redirect the C stream to the null device (SetOut/SetError(TextWriter.Null)
// -- the M1 core icalls print straight to stdout, so the redirect is what
// swallows them). The original fd is SAVED first (once) so the restore below
// can put it back. Restore re-arms the saved fd and re-applies binary mode
// (the ConsoleStreamInitializer contract -- the emitted "\r\n" pair must
// reach the stream verbatim).
void RedirectStdStream(FILE* stream, int& savedFd, bool toNull)
{
#ifdef _WIN32
    if (toNull)
    {
        if (savedFd == -1)
            savedFd = _dup(_fileno(stream));
        std::freopen("NUL", "wb", stream);
    }
    else
    {
        if (savedFd != -1)
        {
            _dup2(savedFd, _fileno(stream));
            _close(savedFd);
            savedFd = -1;
        }
        // else: the stream was NEVER redirected by SetOut/SetError(TextWriter
        // .Null) -- e.g. SetOut(null) THREW before any redirect could save the
        // fd (D-CON fix). Nothing to restore: leave the stream untouched.
        // (freopen("CON") here is NOT a valid fallback -- on MinGW/MSVCRT a
        // plain freopen("CON", ...) returns a valid pointer but subsequent
        // writes are silently dropped, killing all later Console output.)
        _setmode(_fileno(stream), _O_BINARY);
    }
#else
    if (toNull)
    {
        if (savedFd == -1)
            savedFd = dup(stream == stdout ? STDOUT_FILENO : STDERR_FILENO);
        if (std::freopen("/dev/null", "wb", stream) == nullptr)
        {
            // /dev/null is guaranteed on POSIX, so the redirect cannot fail;
            // leave the stream untouched rather than let a dropped return
            // value hide a failure (the _WIN32 arm does the same).
        }
    }
    else
    {
        if (savedFd != -1)
        {
            dup2(savedFd, stream == stdout ? STDOUT_FILENO : STDERR_FILENO);
            close(savedFd);
            savedFd = -1;
        }
        // else: the stream was never redirected by SetOut/SetError
        // (TextWriter.Null) -- nothing to restore (mirror of the _WIN32 arm).
    }
#endif
}

// Create the three canonical streams + register the GC roots (idempotent).
void EnsureConsoleStreams()
{
    if (g_consoleOutStream != nullptr)
        return;
    il2cpp::il2cpp_class_register(&ConsoleStreamClass);
    g_consoleStdoutStream = StreamAs(il2cpp::il2cpp_object_new(&ConsoleStreamClass));
    g_consoleStdoutStream->kind = kConsoleStreamOut;
    g_consoleStderrStream = StreamAs(il2cpp::il2cpp_object_new(&ConsoleStreamClass));
    g_consoleStderrStream->kind = kConsoleStreamError;
    g_consoleNullStream = StreamAs(il2cpp::il2cpp_object_new(&ConsoleStreamClass));
    g_consoleNullStream->kind = kConsoleStreamNull;
    g_consoleOutStream = g_consoleStdoutStream;
    g_consoleErrorStream = g_consoleStderrStream;
    // System.IO.TextWriter::Null is the NULL CONSOLE STREAM OBJECT (the
    // generated ldsfld reads this slot directly; the object must exist so a
    // later SetOut(TextWriter.Null) is a real object, NOT a null managed
    // argument -- the two are now distinguishable).
    ::System_IO_TextWriter_sf_Null = reinterpret_cast<Il2CppObject*>(g_consoleNullStream);
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_consoleStdoutStream));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_consoleStderrStream));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_consoleNullStream));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_consoleOutStream));
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_consoleErrorStream));
}

// The target stream of a console stream object (null for the discard stream).
FILE* StreamTarget(const ConsoleStreamObject* s)
{
    switch (s->kind)
    {
        case kConsoleStreamOut: return stdout;
        case kConsoleStreamError: return stderr;
        default: return nullptr; // null stream: discard
    }
}

} // namespace

// Managed exception chain for SetOut/SetError(null) -- net48 throws
// ArgumentNullException (paramName "newOut"/"newError", csc-probe pinned).
// Chain layout follows the exceptionext.cpp contract: head = the exact type,
// then the .NET base chain for catch matching.
const char kArgumentNullChain[] = "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

Il2CppObject* icall_console_get_out()
{
    EnsureConsoleStreams();
    return reinterpret_cast<Il2CppObject*>(g_consoleOutStream);
}

Il2CppObject* icall_console_get_error()
{
    EnsureConsoleStreams();
    return reinterpret_cast<Il2CppObject*>(g_consoleErrorStream);
}

void icall_console_set_out(Il2CppObject* writer)
{
    EnsureConsoleStreams();
    // net48 SetOut(null) throws ArgumentNullException (paramName "newOut",
    // csc-probe pinned) BEFORE any state change -- the stream is untouched.
    // TextWriter.Null is NOT null in the ILC model: it is the null (discarding)
    // console stream object (System_IO_TextWriter_sf_Null), whose
    // kConsoleStreamNull kind selects the discard branch below.
    if (writer == nullptr)
    {
        il2cpp_throw_new(kArgumentNullChain, "newOut");
        return; // unreachable (il2cpp_throw_new always throws)
    }
    // The null stream object (TextWriter.Null): the Out stream becomes the
    // discarding null stream AND stdout is redirected to the null device --
    // net48 SetOut(Null) swallows subsequent Console.WriteLine (the M1 icalls
    // print straight to stdout). Any other console stream object becomes the
    // new Out stream and stdout is restored.
    if (!IsConsoleStream(writer) || StreamAs(writer)->kind == kConsoleStreamNull)
    {
        g_consoleOutStream = g_consoleNullStream;
        RedirectStdStream(stdout, g_savedStdoutFd, true);
        return;
    }
    g_consoleOutStream = StreamAs(writer);
    RedirectStdStream(stdout, g_savedStdoutFd, false);
}

void icall_console_set_error(Il2CppObject* writer)
{
    EnsureConsoleStreams();
    // net48 SetError(null) throws ArgumentNullException (paramName "newError",
    // csc-probe pinned) BEFORE any state change -- the stream is untouched.
    if (writer == nullptr)
    {
        il2cpp_throw_new(kArgumentNullChain, "newError");
        return; // unreachable (il2cpp_throw_new always throws)
    }
    if (!IsConsoleStream(writer) || StreamAs(writer)->kind == kConsoleStreamNull)
    {
        g_consoleErrorStream = g_consoleNullStream;
        RedirectStdStream(stderr, g_savedStderrFd, true);
        return;
    }
    g_consoleErrorStream = StreamAs(writer);
    RedirectStdStream(stderr, g_savedStderrFd, false);
}

void icall_console_stream_write(Il2CppObject* self, Il2CppObject* text)
{
    // net48 TextWriter.Write(null): a no-op. The generated code null-checks
    // the receiver first; a foreign/unknown object discards (defensive).
    if (self == nullptr || !IsConsoleStream(self))
        return;
    const ConsoleStreamObject* s = StreamAs(self);
    FILE* stream = StreamTarget(s);
    if (stream == nullptr)
        return; // null stream: discard
    WriteStringToStream(text, stream);
}

void icall_console_stream_write_line(Il2CppObject* self, Il2CppObject* text)
{
    if (self == nullptr || !IsConsoleStream(self))
        return;
    const ConsoleStreamObject* s = StreamAs(self);
    FILE* stream = StreamTarget(s);
    if (stream == nullptr)
        return; // null stream: discard
    WriteStringToStream(text, stream);
    // net48 TextWriter.WriteLine: the text followed by the line terminator.
    // stdout/stderr run in binary mode (ConsoleStreamInitializer), so the
    // explicit "\r\n" pair (Windows) / '\n' (POSIX) reaches the stream
    // verbatim -- the console_write_newline contract.
#ifdef _WIN32
    std::fputs("\r\n", stream);
#else
    std::fputc('\n', stream);
#endif
}

}} // namespace il2cpp::runtime
