#pragma once
//------------------------------------------------------------------------------
// il2cpp_environment.h -- System.Environment icall contract (see
// environment.cpp for the implementation).
//
// Result conventions (aligned with the System.IO icalls in il2cpp_io.h):
//   * set-style icalls return int32 status: 0 = success, non-zero = failure,
//     -1 = null/invalid argument (the .NET argument-exception path is
//     represented as a status code until the exception-object layer lands).
//   * get-style icalls return a managed string (Il2CppObject*) or nullptr:
//     nullptr = "no such value" (missing environment variable, failure to
//     obtain the machine name / current directory, ...). Special folders
//     NEVER return nullptr -- unsupported folder ids produce an empty
//     string (simplified contract below).
//
// net48 semantics (System.Environment, .NET Framework 4.8 baseline):
//   * SetEnvironmentVariable(name, null) deletes the variable; setting an
//     EMPTY string also deletes it (verified against a net48 reference run
//     -- the empty-set probe in the environment2 e2e case returns null on
//     the subsequent read).
//   * GetEnvironmentVariable returns null for a variable with an EMPTY
//     value, exactly like a missing one (verified against a net48 reference
//     run -- the empty-value state is distinct from unset but reads the
//     same).
//   * ExitCode is a single process-lifetime int32 (default 0): the getter
//     returns the last set value and the setter always succeeds (verified
//     against a net48 reference run). Process-exit integration is the
//     runtime main's job, not this property's.
//   * TickCount is 32-bit SIGNED milliseconds since system start (wraps
//     from INT32_MAX to 0 after ~24.9 days on the POSIX branch, which
//     masks the millisecond counter modulo 2^31; the Windows branch casts
//     the DWORD GetTickCount() directly, matching net48's signed wrap).
//   * OSVersion is returned in net48 shape: "Microsoft Windows NT X.Y.Z.R" on
//     Windows (a 4-component Version with the revision component -- net48
//     probe-pinned "Microsoft Windows NT 6.2.9200.0"), "Unix <release>"
//     elsewhere.
//   * GetFolderPath is simplified to three ids -- Desktop (0),
//     MyDocuments (5), LocalApplicationData (28) -- resolved through the
//     shell known-folder API on Windows (SHGetFolderPathW, so OneDrive-redirected
//     Desktop/MyDocuments and the real LocalApplicationData match net48
//     byte-for-byte) and standard environment variables (HOME / XDG_DATA_HOME)
//     on POSIX; every other id yields "".
//
// Encoding: values cross the A-suffix Windows API boundary as UTF-8 bytes
// (see environment.cpp -- ASCII names/values, the practical .NET
// convention, are exact; full CP_ACP conversion is out of the simplified
// scope).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"

namespace il2cpp { namespace runtime {

// Environment.GetEnvironmentVariable(string) -> string | null. A missing
// variable -- or one whose value is EMPTY (net48 semantics, verified
// against a reference run) -- yields nullptr. A null NAME throws
// System.ArgumentNullException (net48 semantics, probe-pinned: ParamName
// "variable" -- net48 does NOT return null for a null name).
Il2CppObject* icall_environment_get_environment_variable(Il2CppObject* name);

// Environment.SetEnvironmentVariable(string name, string value) -> status.
// A null name yields -1. A null value -- or an EMPTY value (net48
// semantics, verified) -- deletes the variable. 0 = success, non-zero =
// the underlying OS call failed.
int32_t icall_environment_set_environment_variable(Il2CppObject* name, Il2CppObject* value);

// Environment.CurrentDirectory -> string | null (null = failure to query).
Il2CppObject* icall_environment_get_current_directory();

// Environment.set_CurrentDirectory(string) -> status. A null path yields
// -1; 0 = success, non-zero = the target directory is invalid/unreachable.
int32_t icall_environment_set_current_directory(Il2CppObject* path);

// Environment.TickCount -> int32. 32-bit signed milliseconds since system
// start (net48 semantics; see the header comment for the wrap behavior).
int32_t icall_environment_get_tick_count();

// P6: Environment.TickCount64 -> int64. 64-bit monotonic millisecond
// counter (net8+). Windows: GetTickCount64 (never wraps). POSIX: steady_clock.
int64_t icall_environment_get_tick_count64();

// P6: Environment.FailFast(string message): terminate the process immediately.
// Prints the message to stderr (if non-null), flushes, and calls std::abort().
void icall_environment_fail_fast(Il2CppObject* message);

// P6: Environment.CurrentManagedThreadId (net8+): the current thread's
// managed thread id (OS thread id, same as Thread.ManagedThreadId).
int32_t icall_environment_get_current_managed_thread_id();

// Environment.ExitCode -> int32. The process-wide exit code: a single
// process-lifetime value, default 0 (net48 semantics, verified -- the
// getter returns the last set value). The setter always succeeds.
int32_t icall_environment_get_exit_code();
int32_t icall_environment_set_exit_code(int32_t code);

// Environment.HasShutdownStarted (netcore21+ audit surface): constant false
// -- the simplified runtime never initiates shutdown while managed code runs.
int32_t icall_environment_get_has_shutdown_started();

// Environment.ProcessorCount -> int32. The number of hardware processors
// (>= 1; the OS-reported count on Windows, hardware_concurrency elsewhere
// with a fallback of 1 when the platform cannot report it).
int32_t icall_environment_get_processor_count();

// Environment.UserName -> string | null (null = cannot determine the
// current user).
Il2CppObject* icall_environment_get_user_name();

// Environment.MachineName -> string | null (null = failure to obtain the
// machine name -- the .NET contract surfaces an exception there; the
// simplified contract returns null).
Il2CppObject* icall_environment_get_machine_name();

// Environment.OSVersion -> string | null, in net48 shape: "Microsoft Windows
// NT X.Y.Z.R" on Windows (4-component Version with the revision component --
// net48 probe-pinned "Microsoft Windows NT 6.2.9200.0"), "Unix <release>"
// elsewhere.
Il2CppObject* icall_environment_get_os_version();

// Environment.Version -- returns a System.Version OBJECT (the net48
// closure: mscorlib's Environment.Version property getter, which wraps the
// CLR release version). Always reports the .NET Framework 4.x release shape
// (4.0.30319.42000) -- the same value net48 produces on this host, and the
// surface the OTHER targets (net8+/net10) never reach through this icall
// (their Environment.Version rides a different code path). The object is a
// System_Version_class instance built via icall_version_ctor_4, so
// ToString()/CompareTo() on the returned value work through the existing
// System.Version runtime surface.
Il2CppObject* icall_environment_get_version();

// Environment.GetFolderPath(int32 folderId) -> string, simplified. Only
// Desktop (0), MyDocuments (5) and LocalApplicationData (28) resolve (via
// the shell known-folder API on Windows -- SHGetFolderPathW, OneDrive-redirected
// paths matching net48 byte-for-byte -- and environment variables on POSIX);
// every other id yields an empty string (never null).
Il2CppObject* icall_environment_get_special_folder(int32_t folder_id);

// Environment.CommandLine -> string. Windows: the full process command
// line (executable path + arguments) as supplied by the OS, bridged
// directly from GetCommandLineW (UTF-16, net48 semantics). Linux/Android:
// reconstructed from /proc/self/cmdline (argv[0] + arguments joined with
// single spaces -- the exact inter-argument whitespace/quoting is not
// preserved; the same /proc source .NET Core uses). Other POSIX targets
// (macOS/OpenBSD): STUB -- the runtime has no argv injection point, so the
// command line is unknown and an empty string is returned.
Il2CppObject* icall_environment_get_command_line();

}} // namespace il2cpp::runtime
