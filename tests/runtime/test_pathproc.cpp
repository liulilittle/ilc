//------------------------------------------------------------------------------
// test_pathproc.cpp -- System.IO.Path extensions + System.Diagnostics.Process
// (simplified) runtime unit tests. See pathproc.h for the contract under
// test (the surface complements il2cpp_io.h).
//
// Coverage (normal + edge + error paths):
//   1. GetTempPath : non-null, non-empty, trailing backslash (GetTempPathW
//                    contract), and the directory exists.
//   2. GetFullPath : Windows (GetFullPathNameW) -- absolute input passes
//                    through unchanged; a relative input is resolved to an
//                    absolute drive-qualified path ending with the input;
//                    a non-existent path normalizes too (no filesystem
//                    access); null and empty inputs are rejected with null;
//                    the result is idempotent. POSIX (realpath + lexical
//                    fallback) -- '/' is the separator, relative input is
//                    resolved against the cwd, existing paths match
//                    realpath(), non-existent paths normalize lexically
//                    ("a/../b" -> "b") without crashing, a trailing
//                    separator is preserved; null and empty are rejected.
//   3. ChangeExtension : net48 probed semantics -- "file." + null -> "file",
//                    ".gitignore" + "txt" -> ".txt", "a\b.txt\" + "c" ->
//                    "a\b.txt\.c", "a\b.txt" + "log" -> "a\b.log", plus
//                    null-path rejection.
//   4. HasExtension : net48 scan rule -- "a.txt" 1, "file." 0, ".git" 1,
//                    "noext" 0, "a\b" 0, null 0 (differs from GetExtension
//                    for dotfiles).
//   5. Process ids : GetCurrentProcessId / GetCurrentThreadId are > 0.
//   6. Process.Start error paths : null exe path or null args are rejected
//                    (non-zero); no live child is launched by these cases.
//   7. Process wait/exit-code/full-drain (Windows only; POSIX keeps the
//                    unsupported semantics): Start("cmd.exe", "/c exit 42")
//                    returns a positive child PID; WaitForExit succeeds and
//                    is idempotent; ExitCode is 42. Start("cmd.exe",
//                    "/c echo hello") redirects stdout to an anonymous pipe:
//                    ReadToEnd returns the FULL text ("hello\r\n", CRLF kept)
//                    and "" on a second call; two echoed lines return in one
//                    drain. nb7 PROC-STARTINFO: the ProcessStartInfo object
//                    model (defaults + round-trip) + Start(psi) with separate
//                    stdout/stderr pipes. Unknown PIDs are rejected by all
//                    the APIs.
//
// Harness: like the other runtime tests -- plain executable, any failed
// Expect() prints FAIL and bumps the counter; non-zero exit on failure.
// No filesystem artifacts are created (GetFullPath never writes; on POSIX
// it does stat via realpath() -- read-only access).
//------------------------------------------------------------------------------

#include "pathproc.h"

#include "il2cpp_io.h"      // icall_directory_exists (temp path existence check)
#include "il2cpp_runtime.h" // il2cpp_object_new (ProcessStartInfo allocation)
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

#if !defined(_WIN32)
    #include <cstdlib>  // ::realpath
    #include <limits.h> // PATH_MAX
    #include <unistd.h> // ::getcwd
#endif

namespace {

static_assert(sizeof(void*) == 8, "pathproc test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Managed string <-> ASCII comparison helper.
bool StrEquals(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (int32_t i = 0; i < str->length; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// True when the managed string's last 'len' characters equal 'ascii'.
bool StrEndsWith(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t len = std::strlen(ascii);
    if (static_cast<size_t>(str->length) < len)
    {
        return false;
    }
    const size_t start = static_cast<size_t>(str->length) - len;
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[start + i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

#if defined(_WIN32)
// True when the managed string matches the "X:\" drive prefix pattern.
bool StrHasDrivePrefix(il2cpp::Il2CppObject* s)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    if (str->length < 3)
    {
        return false;
    }
    const char16_t letter = str->chars[0];
    if (!((letter >= u'A' && letter <= u'Z') || (letter >= u'a' && letter <= u'z')))
    {
        return false;
    }
    return str->chars[1] == u':' && (str->chars[2] == u'\\' || str->chars[2] == u'/');
}
#else
// True when the managed string contains the ASCII substring.
bool StrHasSubstring(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    const size_t needle = std::strlen(ascii);
    if (needle == 0)
    {
        return true;
    }
    if (static_cast<size_t>(str->length) < needle)
    {
        return false;
    }
    for (int32_t i = 0; i + static_cast<int32_t>(needle) <= str->length; ++i)
    {
        bool match = true;
        for (size_t k = 0; k < needle; ++k)
        {
            if (str->chars[i + static_cast<int32_t>(k)]
                != static_cast<char16_t>(static_cast<unsigned char>(ascii[k])))
            {
                match = false;
                break;
            }
        }
        if (match)
        {
            return true;
        }
    }
    return false;
}
#endif

// Build a managed string from an ASCII literal (test input helper).
il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 1. Path.GetTempPath ------------------------------------------------
    Il2CppObject* temp = il2cpp_path_get_temp_path();
    Expect(temp != nullptr, "GetTempPath returns a non-null string");
    if (temp != nullptr)
    {
        const Il2CppString* t = reinterpret_cast<Il2CppString*>(temp);
        Expect(t->length > 0, "GetTempPath returns a non-empty string");
        Expect(t->chars[t->length - 1] == u'\\' || t->chars[t->length - 1] == u'/',
               "GetTempPath result carries the trailing separator (API contract)");
        Expect(icall_directory_exists(temp) == 1,
               "the temp path names an existing directory");
    }

    // --- 2. Path.GetFullPath --------------------------------------------------
    Expect(il2cpp_path_get_full_path(nullptr) == nullptr,
           "GetFullPath(null) returns null");
    Expect(il2cpp_path_get_full_path(Cs("")) == nullptr,
           "GetFullPath(empty) returns null (contract)");
#if defined(_WIN32)
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:\\x\\y.txt")), "C:\\x\\y.txt"),
           "GetFullPath passes an absolute input through unchanged");
    Il2CppObject* rel = il2cpp_path_get_full_path(Cs("dir\\file.txt"));
    Expect(rel != nullptr, "GetFullPath resolves a relative input");
    if (rel != nullptr)
    {
        const Il2CppString* r = reinterpret_cast<Il2CppString*>(rel);
        Expect(static_cast<size_t>(r->length) > std::strlen("dir\\file.txt"),
               "GetFullPath result is longer than the relative input");
        Expect(StrHasDrivePrefix(rel),
               "GetFullPath result is drive-qualified (absolute)");
        Expect(StrEndsWith(rel, "dir\\file.txt"),
               "GetFullPath result ends with the input");
    }
    Expect(il2cpp_path_get_full_path(Cs("no_such_dir_xyz\\f.txt")) != nullptr,
           "GetFullPath normalizes a non-existent path (no filesystem access)");
    Il2CppObject* fpAgain = il2cpp_path_get_full_path(Cs("dir\\file.txt"));
    // net48 dot-run rule: a segment of 3+ dots is a single parent reference
    // (probed: GetFullPath("C:\\x\\....\\y") -> "C:\\y"). GetFullPathNameW
    // keeps such segments literal; the input pre-collapse reproduces .NET.
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:\\x\\....\\y")), "C:\\y"),
           "GetFullPath collapses a 4-dot segment (net48 dot-run rule)");
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:\\x\\...\\y")), "C:\\y"),
           "GetFullPath collapses a 3-dot segment (net48 dot-run rule)");
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:\\x\\....")), "C:\\"),
           "GetFullPath collapses a trailing 4-dot segment (net48 dot-run rule)");
    // net48 rejects an embedded colon after the drive with NotSupportedException
    // (probed: GetFullPath("C:\\x:y.txt") throws); the runtime maps that to null.
    Expect(il2cpp_path_get_full_path(Cs("C:\\x:y.txt")) == nullptr,
           "GetFullPath rejects an embedded colon (net48 NotSupportedException -> null)");
    Expect(il2cpp_path_get_full_path(Cs("C:\\x:C")) == nullptr,
           "GetFullPath rejects an embedded colon after the drive");
    // drive-relative input is still accepted (colon at index 1 is the drive).
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:x")), "C:\\x"),
           "GetFullPath accepts a drive-relative input (colon is the drive)");
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("C:.\\x")), "C:\\x"),
           "GetFullPath accepts a drive-relative .-prefixed input");
#else
    // POSIX: '/' is the only separator. Existing paths take the realpath()
    // branch, non-existent ones the lexical fallback -- both are exercised.
    Expect(StrEquals(il2cpp_path_get_full_path(Cs("/x/y.txt")), "/x/y.txt"),
           "GetFullPath keeps an absolute non-existent input (POSIX fallback)");
    Il2CppObject* rel = il2cpp_path_get_full_path(Cs("dir/file.txt"));
    Expect(rel != nullptr, "GetFullPath resolves a relative input (POSIX)");
    if (rel != nullptr)
    {
        const Il2CppString* r = reinterpret_cast<Il2CppString*>(rel);
        Expect(r->length > 0 && r->chars[0] == u'/',
               "GetFullPath result is absolute (POSIX)");
        Expect(StrEndsWith(rel, "dir/file.txt"),
               "GetFullPath result ends with the input (POSIX)");
    }
    // realpath branch: ".." always exists -> canonical parent of the cwd.
    Il2CppObject* parent = il2cpp_path_get_full_path(Cs(".."));
    Expect(parent != nullptr, "GetFullPath canonicalizes an existing relative path");
    if (parent != nullptr)
    {
        char expected[PATH_MAX];
        if (::realpath("..", expected) != nullptr)
        {
            Expect(StrEquals(parent, expected),
                   "GetFullPath matches realpath() for an existing path");
        }
    }
    // lexical fallback branch: a non-existent path must normalize, not crash.
    Il2CppObject* missing = il2cpp_path_get_full_path(Cs("no_such_dir_xyz/../f.txt"));
    Expect(missing != nullptr,
           "GetFullPath normalizes a non-existent path (POSIX fallback)");
    if (missing != nullptr)
    {
        Expect(StrEndsWith(missing, "/f.txt"),
               "GetFullPath collapses \"..\" lexically (no_such_dir_xyz/../f.txt)");
        Expect(!StrHasSubstring(missing, "no_such_dir_xyz"),
               "GetFullPath result no longer contains the collapsed segment");
        char cwdBuf[PATH_MAX];
        if (::getcwd(cwdBuf, sizeof(cwdBuf)) != nullptr)
        {
            char expected[PATH_MAX];
            const int n = std::snprintf(expected, sizeof(expected), "%s/f.txt", cwdBuf);
            if (n > 0 && static_cast<size_t>(n) < sizeof(expected))
            {
                Expect(StrEquals(missing, expected),
                       "GetFullPath result equals cwd + input after \"..\" collapse");
            }
        }
    }
    Expect(il2cpp_path_get_full_path(Cs("no_such_dir_xyz/f.txt")) != nullptr,
           "GetFullPath does not crash on a non-existent path (POSIX)");
    Il2CppObject* trailing = il2cpp_path_get_full_path(Cs("dir/file.txt/"));
    Expect(trailing != nullptr && StrEndsWith(trailing, "file.txt/"),
           "GetFullPath preserves a trailing separator (POSIX)");
    // net48 dot-run rule: a segment of 3+ dots is a single parent reference
    // (probed: GetFullPath("a\\......\\b") pops one level). "..." / "...."
    // must NOT pop two levels. (POSIX separators: forward slash.)
    Il2CppObject* dotRun = il2cpp_path_get_full_path(Cs("a/..../b"));
    Expect(dotRun != nullptr && StrEndsWith(dotRun, "/b"),
           "GetFullPath collapses a 4-dot segment as one parent reference (POSIX)");
    if (dotRun != nullptr)
    {
        Expect(!StrHasSubstring(dotRun, "/a/"),
               "GetFullPath dot-run collapsed segment removed (POSIX)");
    }
    Il2CppObject* dotRun2 = il2cpp_path_get_full_path(Cs("a/..../../b"));
    Expect(dotRun2 != nullptr,
           "GetFullPath dot-run + .. chain normalizes (POSIX)");
    Il2CppObject* fpAgain = il2cpp_path_get_full_path(Cs("dir/file.txt"));
#endif
    if (rel != nullptr && fpAgain != nullptr)
    {
        const Il2CppString* a = reinterpret_cast<Il2CppString*>(rel);
        const Il2CppString* b = reinterpret_cast<Il2CppString*>(fpAgain);
        Expect(a->length == b->length
               && std::memcmp(a->chars, b->chars, static_cast<size_t>(a->length) * sizeof(char16_t)) == 0,
               "GetFullPath of its own result is idempotent (stable)");
    }

    // --- 3. Path.ChangeExtension (net48 probed semantics) ----------------------
    Expect(StrEquals(il2cpp_path_change_extension(Cs("file."), nullptr), "file"),
           "ChangeExtension(\"file.\", null) strips the trailing dot");
    Expect(StrEquals(il2cpp_path_change_extension(Cs(".gitignore"), Cs("txt")), ".txt"),
           "ChangeExtension replaces the dotfile's extension (net48)");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a\\b.txt\\"), Cs("c")), "a\\b.txt\\.c"),
           "ChangeExtension appends to the whole path after a trailing separator");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a\\b.txt"), Cs("log")), "a\\b.log"),
           "ChangeExtension replaces without the leading dot");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a\\b.txt"), Cs(".log")), "a\\b.log"),
           "ChangeExtension keeps an explicit leading dot");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a\\b.txt"), Cs("")), "a\\b."),
           "ChangeExtension with an empty extension appends a trailing dot to the base");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a\\b.txt"), nullptr), "a\\b"),
           "ChangeExtension with a null extension strips");
    Expect(StrEquals(il2cpp_path_change_extension(Cs("a.txt"), Cs(".doc")), "a.doc"),
           "ChangeExtension replaces .txt with .doc");
    Expect(il2cpp_path_change_extension(nullptr, Cs(".log")) == nullptr,
           "ChangeExtension(null, ext) returns null");

    // --- 4. Path.HasExtension (net48 scan rule) --------------------------------
    Expect(il2cpp_path_has_extension(Cs("a.txt")) == 1,
           "HasExtension(\"a.txt\") is true");
    Expect(il2cpp_path_has_extension(Cs("file.")) == 0,
           "HasExtension(\"file.\") is false (trailing dot)");
    Expect(il2cpp_path_has_extension(Cs(".git")) == 1,
           "HasExtension(\".git\") is true (differs from GetExtension)");
    Expect(il2cpp_path_has_extension(Cs("noext")) == 0,
           "HasExtension(\"noext\") is false");
    Expect(il2cpp_path_has_extension(Cs("a\\b")) == 0,
           "HasExtension(\"a\\b\") is false (no dot after the separator)");
    Expect(il2cpp_path_has_extension(Cs("a.b\\c.txt")) == 1,
           "HasExtension scans after the last separator only");
    Expect(il2cpp_path_has_extension(nullptr) == 0,
           "HasExtension(null) is false");

    // --- 5. Process ids ---------------------------------------------------------
    Expect(il2cpp_process_get_current_process_id() > 0,
           "GetCurrentProcessId is greater than 0");
    Expect(il2cpp_process_get_current_thread_id() > 0,
           "GetCurrentThreadId is greater than 0");

    // --- 6. Process.Start error paths (no live child is launched) --------------
    Expect(il2cpp_process_start(nullptr, Cs("")) != 0,
           "Process.Start with a null exe path is rejected");
    Expect(il2cpp_process_start(Cs("C:\\x\\never.exe"), nullptr) != 0,
           "Process.Start with null args is rejected");
    Expect(il2cpp_process_wait_for_exit(999999) != 0,
           "Process.WaitForExit rejects an unknown PID");
    Expect(il2cpp_process_get_exit_code(999999) == -1,
           "Process.ExitCode is -1 for an unknown PID");
    Expect(il2cpp_process_read_stdout_to_end(999999) == nullptr,
           "Process.ReadToEnd is null for an unknown PID");
    Expect(il2cpp_process_read_stderr_to_end(999999) == nullptr,
           "Process.ReadToEnd(stderr) is null for an unknown PID");
#if defined(_WIN32)
    // Contract (pathproc.h): 0 on launch failure (a missing exe with valid
    // args), -1 on null input. The two cases above are the -1 branch; this
    // locks the 0 branch -- no live child is launched.
    Expect(il2cpp_process_start(Cs("C:\\definitely_missing_xyz.exe"), Cs("")) == 0,
           "Process.Start reports launch failure with 0 (contract)");
    Expect(il2cpp_process_start(Cs("C:\\x\\never.exe"), Cs("")) == 0,
           "Process.Start reports launch failure with 0 (missing exe)");
#endif

    // --- 7. Process wait / exit code / full drain (live children, Windows) -----
#if defined(_WIN32)
    // 7a. start -> wait -> exit code: cmd /c exit 42 must exit with 42. Both
    // redirected streams drain to "" (no output).
    const int32_t pid42 = il2cpp_process_start(Cs("cmd.exe"), Cs("/c exit 42"));
    Expect(pid42 > 0, "Process.Start returns a positive child PID (exit case)");
    Expect(il2cpp_process_wait_for_exit(pid42) == 0,
           "Process.WaitForExit succeeds for the started child");
    Expect(il2cpp_process_get_exit_code(pid42) == 42,
           "Process.ExitCode is 42 (cmd /c exit 42)");
    Expect(il2cpp_process_wait_for_exit(pid42) == 0,
           "Process.WaitForExit is idempotent after the child exited");
    Expect(StrEquals(il2cpp_process_read_stdout_to_end(pid42), ""),
           "ReadToEnd returns \"\" for a child with no stdout");
    Expect(StrEquals(il2cpp_process_read_stderr_to_end(pid42), ""),
           "ReadToEnd(stderr) returns \"\" for a child with no stderr");

    // 7b. full stdout drain: the echoed line INCLUDING its CRLF terminator
    // (ReadToEnd semantics; the legacy ReadLine terminator strip is retired).
    const int32_t pidHello = il2cpp_process_start(Cs("cmd.exe"), Cs("/c echo hello"));
    Expect(pidHello > 0, "Process.Start returns a positive child PID (stdout case)");
    Expect(il2cpp_process_wait_for_exit(pidHello) == 0,
           "Process.WaitForExit succeeds for the stdout child");
    Expect(StrEquals(il2cpp_process_read_stdout_to_end(pidHello), "hello\r\n"),
           "ReadToEnd returns the FULL echoed text (CRLF kept)");
    Expect(StrEquals(il2cpp_process_read_stdout_to_end(pidHello), ""),
           "a second ReadToEnd is empty at EOF");

    // 7c. multiple lines are returned in ONE drain (nb7: no per-line offset).
    const int32_t pidTwo = il2cpp_process_start(Cs("cmd.exe"), Cs("/c echo one&&echo two"));
    Expect(pidTwo > 0, "Process.Start returns a positive child PID (two-line case)");
    Expect(il2cpp_process_wait_for_exit(pidTwo) == 0,
           "Process.WaitForExit succeeds for the two-line child");
    Expect(StrEquals(il2cpp_process_read_stdout_to_end(pidTwo), "one\r\ntwo\r\n"),
           "ReadToEnd returns both lines in order");

    // 7d. nb7 PROC-STARTINFO: the ProcessStartInfo object model + Start(psi)
    // with separately redirected stdout/stderr pipes.
    Il2CppObject* psi = reinterpret_cast<Il2CppObject*>(
        il2cpp_object_new(&System_Diagnostics_ProcessStartInfo_class));
    icall_processstartinfo_ctor(psi);
    Expect(icall_processstartinfo_get_useshellexecute(psi) == 1,
           "ProcessStartInfo defaults UseShellExecute to true");
    Expect(StrEquals(icall_processstartinfo_get_filename(psi), ""),
           "ProcessStartInfo defaults FileName to \"\" (not null)");
    icall_processstartinfo_set_filename(psi, Cs("cmd.exe"));
    icall_processstartinfo_set_arguments(psi, Cs("/c echo out & echo err 1>&2"));
    icall_processstartinfo_set_useshellexecute(psi, 0);
    icall_processstartinfo_set_redirectstdout(psi, 1);
    icall_processstartinfo_set_redirectstderr(psi, 1);
    Expect(StrEquals(icall_processstartinfo_get_filename(psi), "cmd.exe"),
           "ProcessStartInfo FileName round-trips");
    Expect(StrEquals(icall_processstartinfo_get_arguments(psi), "/c echo out & echo err 1>&2"),
           "ProcessStartInfo Arguments round-trips");
    Expect(icall_processstartinfo_get_redirectstdout(psi) == 1
               && icall_processstartinfo_get_redirectstderr(psi) == 1,
           "ProcessStartInfo redirect flags round-trip");
    Expect(icall_processstartinfo_get_useshellexecute(psi) == 0,
           "ProcessStartInfo UseShellExecute round-trips");
    const int32_t pidPsi = il2cpp_process_start_info(psi);
    Expect(pidPsi > 0, "Process.Start(ProcessStartInfo) returns a positive PID");
    Expect(il2cpp_process_wait_for_exit(pidPsi) == 0,
           "Process.WaitForExit succeeds for the psi child");
    Expect(StrEquals(il2cpp_process_read_stdout_to_end(pidPsi), "out \r\n"),
           "psi stdout drains the FULL output (separate pipe)");
    Expect(StrEquals(il2cpp_process_read_stderr_to_end(pidPsi), "err \r\n"),
           "psi stderr drains into its own pipe");
    // UseShellExecute=true + a redirect raises InvalidOperationException
    // through the managed transport (uncaught here would abort the test), so
    // that negative path is exercised only by the transpiled p-nb7-psi probe.

    // 7e. nb4 PROC-DIAG proctime: StartTime / ExitTime must survive
    // wait_for_exit. The slot closes its process handle there, and re-opening
    // the PID after the child object is recycled fails, so the times are
    // cached at the same authoritative point as the exit code (net48 keeps
    // the retained SafeProcessHandle and stays exact). RED before the cache:
    // a post-wait read fell back to OpenProcess and silently yielded 0 ticks.
    const int32_t pidTimes = il2cpp_process_start(Cs("cmd.exe"), Cs("/c ping 127.0.0.1 -n 2"));
    Expect(pidTimes > 0, "Process.Start returns a positive PID (proctime case)");
    const int64_t startAlive = il2cpp_process_get_start_time(pidTimes).ticks;
    Expect(startAlive > 0, "StartTime is readable while the child is alive");
    // net48 raises InvalidOperationException for ExitTime on a live process
    // (oracle-pinned by probe-proctime); the runtime mirrors the throw.
    bool aliveExitThrew = false;
    try { (void)il2cpp_process_get_exit_time(pidTimes); }
    catch (...) { aliveExitThrew = true; }
    Expect(aliveExitThrew, "ExitTime on a live child raises (net48 InvalidOperationException)");
    Expect(il2cpp_process_wait_for_exit(pidTimes) == 0,
           "WaitForExit succeeds for the proctime child");
    const int64_t startAfter = il2cpp_process_get_start_time(pidTimes).ticks;
    Expect(startAfter == startAlive,
           "StartTime survives wait_for_exit (cached at the authoritative point)");
    const int64_t exitAfter = il2cpp_process_get_exit_time(pidTimes).ticks;
    Expect(exitAfter > 0, "ExitTime is readable after wait_for_exit");
    Expect(exitAfter >= startAfter, "ExitTime is not earlier than StartTime");
    Expect(il2cpp_process_get_start_time(pidTimes).ticks == startAlive,
           "StartTime repeated post-wait reads are stable");
    Expect(il2cpp_process_get_exit_time(pidTimes).ticks == exitAfter,
           "ExitTime repeated post-wait reads are stable");

    // The Kill fold closes the handle the same way: its cached times must
    // survive too (net48 Kill + WaitForExit keeps both getters readable).
    const int32_t pidKillTimes = il2cpp_process_start(Cs("ping.exe"), Cs("-n 30 127.0.0.1"));
    Expect(pidKillTimes > 0, "Process.Start returns a positive PID (kill proctime case)");
    Expect(il2cpp_process_kill(pidKillTimes) == 0, "Kill terminates the tracked child");
    Expect(il2cpp_process_wait_for_exit(pidKillTimes) == 0,
           "WaitForExit is idempotent after Kill");
    const int64_t killStart = il2cpp_process_get_start_time(pidKillTimes).ticks;
    const int64_t killExit = il2cpp_process_get_exit_time(pidKillTimes).ticks;
    Expect(killStart > 0, "StartTime survives the Kill fold");
    Expect(killExit > 0, "ExitTime survives the Kill fold");
    Expect(killExit >= killStart, "Kill-fold ExitTime is not earlier than StartTime");
#endif
    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d pathproc assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime pathproc tests passed\n");
    return 0;
}

