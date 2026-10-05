//------------------------------------------------------------------------------
// test_environment.cpp -- System.Environment icall runtime unit tests.
// See il2cpp_environment.h for the contract under test.
//
// Coverage (normal + edge + error paths):
//   1. Environment variable round trip  : set -> get equals the value;
//      overwrite -> get equals the new value.
//   2. Deletion                          : set with a null value removes the
//      variable (get -> null); setting an EMPTY value also deletes it (net48
//      semantics, verified against the environment2 reference run); getting
//      a variable that was never set -> null.
//   2b. Empty-value variable              : a variable whose value is EMPTY
//      reads as null (net48 semantics, verified against a reference run) --
//      created directly via the OS API, since the runtime's own setter
//      deletes empty values by design.
//   3. Argument guards                   : null name -> -1 (set) / ArgumentNullException
//      (get -- net48 semantics, probe-pinned: net48 GetEnvironmentVariable(null)
//      throws, it does NOT return null).
//   4. CurrentDirectory round trip       : the getter is non-empty; setting
//      it to itself succeeds (0) and the getter is unchanged; setting it to
//      an existing directory succeeds and the getter reflects it; setting it
//      to a nonexistent path fails (non-zero); the original directory is
//      restored at the end.
//   5. TickCount                         : monotonic non-decreasing between
//      two consecutive reads (32-bit signed milliseconds; wraps only after
//      ~24.9 days of system uptime, unreachable within a test run).
//   5b. ExitCode                         : defaults to 0; set/get round trip
//      (42, -7), restored to 0 (net48 stored-int semantics).
//   6. ProcessorCount                    : > 0.
//   7. UserName / MachineName            : both non-empty.
//   8. OSVersion                         : non-empty; on Windows the net48
//      4-component "Microsoft Windows NT X.Y.Z.R" shape (probe-pinned: net48
//      reports "Microsoft Windows NT 6.2.9200.0" via GetVersionEx), on POSIX
//      the "<sysname> <release>" shape.
//   9. Special folders                   : Desktop (0), MyDocuments (5),
//      LocalApplicationData (28) resolve to the REAL known-folder paths on
//      Windows -- byte-equal to the SHGetFolderPathW result (net48 semantics,
//      probe-pinned, OneDrive-redirected) -- and non-empty on POSIX; an
//      unsupported id (999) yields an EMPTY string (never null).
//  10. CommandLine                       : Windows -- full process command
//      line from GetCommandLineW (non-empty). Linux/Android -- reconstructed
//      from /proc/self/cmdline (non-empty, contains the executable name).
//      Other POSIX -- documented stub, still an empty string.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal. All
// filesystem artifacts live under the relative directory "environment_tmp"
// (created and removed by the test).
//------------------------------------------------------------------------------

#include "il2cpp_environment.h"

#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

#if defined(_WIN32)
    #include <direct.h>   // ::_mkdir, ::_rmdir
    #include <windows.h>  // ::SetEnvironmentVariableA (empty-value probe)
    #define IL2CPP_TEST_MKDIR(p) ::_mkdir(p)
    #define IL2CPP_TEST_RMDIR(p) ::_rmdir(p)
#else
    #include <sys/stat.h> // ::mkdir
    #include <unistd.h>   // ::rmdir
    #define IL2CPP_TEST_MKDIR(p) ::mkdir(p, 0755)
    #define IL2CPP_TEST_RMDIR(p) ::rmdir(p)
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

// Runs 'fn' expecting a managed DotNetException whose type chain HEAD exactly
// equals 'expectedType' (the thrown chain is "Type,Base1,Base2,..."; only the
// head is asserted). Any other outcome is a recorded failure. Same helper as
// the other runtime tests (test_arrayext.cpp etc.).
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

// Managed string -> ASCII std::string (test comparison helper; characters
// >= 0x80 map to '?' -- the test data is pure ASCII).
std::string ManagedToAscii(il2cpp::Il2CppObject* s)
{
    if (s == nullptr)
    {
        return std::string();
    }
    const il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    std::string out;
    out.reserve(static_cast<size_t>(str->length));
    for (int32_t i = 0; i < str->length; ++i)
    {
        const char16_t c = str->chars[i];
        out.push_back((c < 0x80) ? static_cast<char>(c) : '?');
    }
    return out;
}

// Managed string <-> ASCII literal comparison (null managed string never
// equals any non-null literal).
bool StrEquals(il2cpp::Il2CppObject* s, const char* ascii)
{
    return ManagedToAscii(s) == std::string(ascii);
}

// Build a managed string from an ASCII literal (test input helper).
il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

// The test's environment-variable name (unique enough to never collide with
// other domains' tests; deleted at the end of the run).
const char* kEnvName = "IL2CPP_ENV_TEST_VAR";

} // namespace

int main()
{
    using namespace il2cpp::runtime;
    using namespace il2cpp;

    // --- 1. Environment variable round trip (set -> get -> overwrite) ---
    Expect(icall_environment_set_environment_variable(Cs(kEnvName), Cs("hello env")) == 0,
           "SetEnvironmentVariable succeeds");
    Expect(StrEquals(icall_environment_get_environment_variable(Cs(kEnvName)), "hello env"),
           "GetEnvironmentVariable reads back the value");
    Expect(icall_environment_set_environment_variable(Cs(kEnvName), Cs("second value")) == 0,
           "SetEnvironmentVariable overwrites");
    Expect(StrEquals(icall_environment_get_environment_variable(Cs(kEnvName)), "second value"),
           "GetEnvironmentVariable reads back the overwritten value");

    // --- 2. Deletion: null value, empty value (net48), missing variable ---
    Expect(icall_environment_set_environment_variable(Cs(kEnvName), nullptr) == 0,
           "SetEnvironmentVariable(null) succeeds (delete)");
    Expect(icall_environment_get_environment_variable(Cs(kEnvName)) == nullptr,
           "GetEnvironmentVariable returns null after deletion");
    Expect(icall_environment_get_environment_variable(Cs("IL2CPP_ENV_TEST_NEVER_SET")) == nullptr,
           "GetEnvironmentVariable returns null for a never-set variable");
    Expect(icall_environment_set_environment_variable(Cs(kEnvName), Cs("")) == 0,
           "SetEnvironmentVariable(\"\") succeeds");
    Expect(icall_environment_get_environment_variable(Cs(kEnvName)) == nullptr,
           "net48 semantics: an empty value deletes the variable");

    // --- 2b. A variable with an EMPTY value reads as null (net48) ---
    // Verified against a net48 reference run: the empty-value state is
    // distinct from unset but reads the same (null). The runtime's own
    // SetEnvironmentVariable deletes empty values by design, so this state
    // is created directly via the OS API (which represents it).
#if defined(_WIN32)
    ::SetEnvironmentVariableA("IL2CPP_ENV_TEST_EMPTY", ""); // creates an empty-value variable
    Expect(icall_environment_get_environment_variable(Cs("IL2CPP_ENV_TEST_EMPTY")) == nullptr,
           "net48: a variable with an empty value reads as null");
    ::SetEnvironmentVariableA("IL2CPP_ENV_TEST_EMPTY", nullptr); // cleanup (delete)
#else
    ::setenv("IL2CPP_ENV_TEST_EMPTY", "", 1); // creates an empty-value variable
    Expect(icall_environment_get_environment_variable(Cs("IL2CPP_ENV_TEST_EMPTY")) == nullptr,
           "net48: a variable with an empty value reads as null");
    ::unsetenv("IL2CPP_ENV_TEST_EMPTY");
#endif

    // --- 3. Argument guards ---
    Expect(icall_environment_set_environment_variable(nullptr, Cs("x")) == -1,
           "SetEnvironmentVariable(null name) reports -1");
    // D-ENV-4 (net48 probe-pinned): GetEnvironmentVariable(null) throws
    // ArgumentNullException with ParamName "variable" -- it does NOT return
    // null.
    ExpectThrows("System.ArgumentNullException",
                 "GetEnvironmentVariable(null) throws ArgumentNullException",
                 [] { icall_environment_get_environment_variable(nullptr); });

    // --- 4. CurrentDirectory round trip ---
    {
        const std::string original = ManagedToAscii(icall_environment_get_current_directory());
        Expect(!original.empty(), "CurrentDirectory getter returns a value");

        Expect(icall_environment_set_current_directory(Cs(original.c_str())) == 0,
               "CurrentDirectory setter accepts the current directory itself");
        Expect(ManagedToAscii(icall_environment_get_current_directory()) == original,
               "CurrentDirectory unchanged after setting it to itself");

        // --- 4b. Move into a real subdirectory, verify, restore ---
        // The getter reports the ABSOLUTE path (GetCurrentDirectoryA always
        // returns one, matching .NET): after setting the relative
        // "environment_tmp", the getter must equal the original directory
        // joined with the relative name.
        Expect(IL2CPP_TEST_MKDIR("environment_tmp") == 0,
               "test directory creation succeeds");
        Expect(icall_environment_set_current_directory(Cs("environment_tmp")) == 0,
               "CurrentDirectory setter accepts an existing directory");
#if defined(_WIN32)
        const std::string expected = original + "\\environment_tmp";
#else
        const std::string expected = original + "/environment_tmp";
#endif
        Expect(ManagedToAscii(icall_environment_get_current_directory()) == expected,
               "CurrentDirectory getter reflects the changed directory (absolute)");

        Expect(icall_environment_set_current_directory(Cs("environment_tmp_does_not_exist")) != 0,
               "CurrentDirectory setter rejects a nonexistent directory");

        Expect(icall_environment_set_current_directory(Cs(original.c_str())) == 0,
               "CurrentDirectory setter restores the original directory");
        Expect(IL2CPP_TEST_RMDIR("environment_tmp") == 0,
               "test directory removal succeeds");
    }

    // --- 5. TickCount monotonic ---
    {
        const int32_t t1 = icall_environment_get_tick_count();
        const int32_t t2 = icall_environment_get_tick_count();
        Expect(t2 >= t1, "TickCount is monotonic non-decreasing");
    }

    // --- 5b. ExitCode: default 0, set/get round trip (net48 semantics) ---
    {
        Expect(icall_environment_get_exit_code() == 0,
               "ExitCode defaults to 0");
        Expect(icall_environment_set_exit_code(42) == 0,
               "set_ExitCode(42) succeeds");
        Expect(icall_environment_get_exit_code() == 42,
               "ExitCode reads back 42");
        Expect(icall_environment_set_exit_code(-7) == 0,
               "set_ExitCode(-7) succeeds");
        Expect(icall_environment_get_exit_code() == -7,
               "ExitCode reads back -7");
        Expect(icall_environment_set_exit_code(0) == 0,
               "set_ExitCode(0) succeeds");
        Expect(icall_environment_get_exit_code() == 0,
               "ExitCode restored to 0");
    }

    // --- 6. ProcessorCount ---
    Expect(icall_environment_get_processor_count() > 0,
           "ProcessorCount is positive");

    // --- 7. UserName / MachineName ---
    Expect(ManagedToAscii(icall_environment_get_user_name()).size() > 0,
           "UserName is a non-empty string");
    Expect(ManagedToAscii(icall_environment_get_machine_name()).size() > 0,
           "MachineName is a non-empty string");

    // --- 8. OSVersion prefix + net48 4-component shape ---
    {
        const std::string os = ManagedToAscii(icall_environment_get_os_version());
        Expect(!os.empty(), "OSVersion returns a string");
#if defined(_WIN32)
        // D-ENV-1 (net48 probe-pinned): net48 Environment.OSVersion.ToString()
        // = "Microsoft Windows NT X.Y.Z.0" -- a 4-component Version (major,
        // minor, build, REVISION) from GetVersionEx (the app-compat shimmed
        // value -- 6.2.9200.0 on a non-manifested process, matching the
        // reference run). Assert the exact net48 shape: prefix + 4
        // dot-separated numeric components.
        Expect(os.compare(0, 20, "Microsoft Windows NT") == 0,
               "OSVersion starts with \"Microsoft Windows NT\" on Windows");
        const size_t verStart = 20; // index 20 is the space after the 20-char prefix
        const std::string ver = os.substr(verStart + 1);
        int dots = 0;
        bool allNumeric = !ver.empty();
        for (size_t i = 0; i < ver.size(); ++i)
        {
            if (ver[i] == '.') { ++dots; }
            else if (ver[i] < '0' || ver[i] > '9') { allNumeric = false; }
        }
        Expect(dots == 3 && allNumeric,
               "OSVersion is the net48 4-component \"X.Y.Z.R\" shape");
#elif defined(__linux__) || defined(__ANDROID__)
        Expect(os.compare(0, 6, "Linux ") == 0,
               "OSVersion starts with \"Linux \" on Linux/Android");
#elif defined(__APPLE__)
        Expect(os.compare(0, 7, "Darwin ") == 0,
               "OSVersion starts with \"Darwin \" on Apple");
#elif defined(__OpenBSD__)
        Expect(os.compare(0, 8, "OpenBSD ") == 0,
               "OSVersion starts with \"OpenBSD \" on OpenBSD");
#else
        Expect(os.find(' ') != std::string::npos,
               "OSVersion has the \"<sysname> <release>\" shape on other POSIX");
#endif
    }

    // --- 9. Special folders ---
    // D-ENV-2 (net48 probe-pinned): on Windows the three supported ids
    // resolve through the shell known-folder API (SHGetFolderPathW), so the
    // icall result must byte-equal the OS known-folder path (OneDrive-
    // redirected Desktop/MyDocuments, the real LocalApplicationData).
#if defined(_WIN32)
    {
        typedef HRESULT(WINAPI* SHGetFolderPathFn)(HWND, int, HANDLE, DWORD, LPWSTR);
        const HMODULE shell32 = ::GetModuleHandleA("shell32.dll");
        const SHGetFolderPathFn shGetFolderPath = (shell32 != nullptr)
            ? reinterpret_cast<SHGetFolderPathFn>(::GetProcAddress(shell32, "SHGetFolderPathW"))
            : nullptr;
        if (shGetFolderPath != nullptr)
        {
            struct KnownFolder
            {
                int csidl;
                int32_t folderId;
                const char* what;
            };
            const KnownFolder kFolders[] = {
                { 0, 0, "SpecialFolder.Desktop matches the shell known folder" },
                { 5, 5, "SpecialFolder.MyDocuments matches the shell known folder" },
                { 28, 28, "SpecialFolder.LocalApplicationData matches the shell known folder" },
            };
            for (const KnownFolder& kf : kFolders)
            {
                WCHAR wide[1024];
                const HRESULT hr = shGetFolderPath(nullptr, kf.csidl, nullptr, 0, wide);
                if (hr == 0 && wide[0] != L'\0')
                {
                    // The shell path, reduced through the SAME ASCII mapping as
                    // ManagedToAscii (each code unit < 0x80 verbatim, anything
                    // else -> '?'), so the comparison is encoding-consistent.
                    std::string expected;
                    for (size_t i = 0; wide[i] != L'\0'; ++i)
                    {
                        expected.push_back(wide[i] < 0x80 ? static_cast<char>(wide[i]) : '?');
                    }
                    const std::string actual = ManagedToAscii(icall_environment_get_special_folder(kf.folderId));
                    Expect(actual == expected, kf.what);
                }
            }
        }
    }
#endif
    const std::string desktop = ManagedToAscii(icall_environment_get_special_folder(0));
    const std::string documents = ManagedToAscii(icall_environment_get_special_folder(5));
    Expect(desktop.size() > 0, "SpecialFolder.Desktop resolves to a non-empty path");
    Expect(documents.size() > 0, "SpecialFolder.MyDocuments resolves to a non-empty path");
    Expect(ManagedToAscii(icall_environment_get_special_folder(28)).size() > 0,
           "SpecialFolder.LocalApplicationData resolves to a non-empty path");
    Expect(ManagedToAscii(icall_environment_get_special_folder(999)).empty(),
           "unsupported folder ids yield an empty string (never null)");
#if defined(_WIN32)
    Expect(desktop.find('\\') != std::string::npos &&
           documents.find('\\') != std::string::npos,
           "Desktop/Documents use the Windows backslash separator");
#else
    Expect(desktop.find('\\') == std::string::npos &&
           documents.find('\\') == std::string::npos,
           "Desktop/Documents use the POSIX forward-slash separator (no backslash)");
#endif

    // --- 10. CommandLine ---
#if defined(_WIN32)
    // Windows: full command line from GetCommandLineW (net48 semantics).
    Expect(!ManagedToAscii(icall_environment_get_command_line()).empty(),
           "CommandLine is the full process command line on Windows");
#elif defined(__linux__) || defined(__ANDROID__)
    // Linux/Android: reconstructed from /proc/self/cmdline -- non-empty and
    // carries argv[0] (the test executable's path contains its name).
    {
        const std::string cmd = ManagedToAscii(icall_environment_get_command_line());
        Expect(!cmd.empty(),
               "CommandLine is non-empty on Linux (/proc/self/cmdline)");
        Expect(cmd.find("test_environment") != std::string::npos,
               "CommandLine contains the executable name (argv[0]) on Linux");
    }
#else
    // Other POSIX (macOS/OpenBSD): no /proc -- documented empty stub.
    Expect(ManagedToAscii(icall_environment_get_command_line()).empty(),
           "CommandLine is an empty string on POSIX without /proc (stub)");
#endif

    if (g_failures == 0)
    {
        std::printf("all runtime environment tests passed\n");
        return 0;
    }
    std::printf("%d environment test(s) FAILED\n", g_failures);
    return 1;
}
