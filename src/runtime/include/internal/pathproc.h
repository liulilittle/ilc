#pragma once
//------------------------------------------------------------------------------
// pathproc.h -- System.IO.Path extensions + System.Diagnostics.Process
// (simplified) runtime surface.
//
// Complements il2cpp_io.h: the base icall layer already covers
// GetFileName / GetExtension / Combine; this header adds the four Path
// functions that require Win32 or .NET-specific semantics, plus a minimal
// Process domain:
//
//   Path (Windows semantics, net48 reference -- verified by probe; the OS
//   queries delegate to the platform layer, platform.h):
//     * get_temp_path     -- plat_path_tmpdir (GetTempPathW / $TMPDIR) plus
//       the trailing separator the API guarantees (Path.GetTempPath
//       contract).
//     * get_full_path     -- plat_path_full (GetFullPathNameW / realpath):
//       relative input is resolved against the current directory; on Windows
//       no filesystem access, so non-existent paths normalize too (POSIX
//       keeps a lexical fallback for non-existent paths -- realpath
//       requires existence). Does not resolve symlinks on Windows. Both
//       branches apply the net48 dot-run rule (a segment of "..." or longer
//       ALL-dot characters is ONE parent reference, like ".."; probed) and
//       Windows rejects a ':' beyond the drive separator at index 1
//       (net48 NotSupportedException -> null, probed).
//     * change_extension  -- .NET Path.ChangeExtension: the extension is
//       the '.' suffix of the last path segment; a separator (or volume
//       separator on Windows) before any dot means there is none, and the
//       new extension is appended to the whole path. A null extension
//       strips; an empty one appends a trailing dot (net48, probed).
//     * has_extension     -- .NET Path.HasExtension scan rule: true when a
//       '.' appears after the last separator and is not the final
//       character ("a.txt" 1, "file." 0, ".git" 1 -- net48, probed).
//       Note this differs from il2cpp_io.h GetExtension for dotfiles.
//
//   Process (simplified domain):
//     * process_start     -- CreateProcessW with an exe path + command
//       line; stdout/stderr are redirected to a temporary file (read back
//       via read_stdout_line) and the child process handle is kept in a
//       PID-keyed table until wait_for_exit. Returns the child PID (> 0)
//       on success; non-positive on failure.
//     * process ids       -- GetCurrentProcessId / GetCurrentThreadId,
//       delegated to the platform layer (plat_process_id /
//       plat_thread_current_id).
//     * process_exit      -- ExitProcess (never returns).
//     * process_wait_for_exit / get_exit_code / read_stdout_line -- the
//       wait/exit-code/stdout trio; see the declarations below.
//
// Result conventions follow il2cpp_io.h: managed strings in/out; failures
// and null inputs are null / non-zero returns, never C++ throws (the
// managed exception system is a later milestone).
//
// The POSIX branch mirrors the Windows semantics where the platform allows:
// get_temp_path uses the platform-layer temp directory ($TMPDIR, else /tmp)
// and guarantees the trailing '/' (Path.GetTempPath contract); get_full_path
// is plat_path_full (realpath()) with a handwritten lexical fallback for
// non-existent paths (cwd join + "."/".." collapsing, no filesystem access
// -- non-existent paths normalize like the Win32 branch; note realpath()
// resolves symlinks for existing paths, unlike GetFullPathNameW); process
// ids via the platform layer (plat_process_id / plat_thread_current_id); the
// Process domain remains unsupported (non-zero / null returns).
//------------------------------------------------------------------------------

#include "il2cpp_metadata.h" // Il2CppClass (ProcessStartInfo GLOBAL descriptor)
#include "il2cpp_object.h"
#include "il2cpp_string.h" // Il2CppString (ProcessStartInfo fields)
#include "il2cpp_time.h"   // Il2CppDateTime (Process.StartTime / ExitTime)

#include <cstdint>

// GLOBAL-scope ProcessStartInfo descriptor (the regex_obj.h / sync_lite.h
// contract): the transpiler's newobj fallback emits
// il2cpp_object_new(&System_Diagnostics_ProcessStartInfo_class) from generated
// code at GLOBAL scope, so the extern must live OUTSIDE the il2cpp::runtime
// namespace. Defined in pathproc.cpp.
extern il2cpp::Il2CppClass System_Diagnostics_ProcessStartInfo_class;

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// System.IO.Path (extensions).
//------------------------------------------------------------------------------

// Path.GetTempPath(): the system temp directory with a trailing separator
// (backslash on Windows per the GetTempPathW contract; '/' on POSIX via
// $TMPDIR, else /tmp). Non-null on success; null only when the API reports
// failure (effectively never on a healthy system).
Il2CppObject* il2cpp_path_get_temp_path();

// Path.GetFullPath(path): 'path' resolved to an absolute normalized path
// (GetFullPathNameW). Null for a null/empty input or when the API fails.
// Non-existent paths are normalized, not verified.
Il2CppObject* il2cpp_path_get_full_path(Il2CppObject* path);

// Path.ChangeExtension(path, extension): replaces (or strips, when
// 'extension' is null) the extension of the last path segment per the .NET
// semantics above. Null for a null 'path'.
Il2CppObject* il2cpp_path_change_extension(Il2CppObject* path, Il2CppObject* extension);

// Path.HasExtension(path): 1 when the last path segment carries a
// non-trailing '.' (net48 scan rule; see the header comment). 0 for a null
// path, extension-less names, trailing dots and separator-led names.
int32_t il2cpp_path_has_extension(Il2CppObject* path);

// Path.GetDirectoryName(path): the directory portion of 'path' -- the text
// before the last segment separator, trailing separators stripped
// ("a\\b\\c.txt" -> "a\\b", "a\\b\\" -> "a\\b", "a\\b" -> "a", "plain" ->
// "", "\\a" -> "\\" -- net48, probe-pinned). Boundary cases (net48 oracle):
// a null path -> null; "" -> net48 ArgumentException (M3: null); a path
// that is all separators ("\\", the root) -> null. A drive colon keeps its
// separator ("C:\\a" -> "C:\\"). Both separators are recognized.
Il2CppObject* il2cpp_path_get_directory_name(Il2CppObject* path);

// Path.GetFileNameWithoutExtension(path): the last path segment with the
// extension the GetExtension rule identifies stripped ("a\\b\\c.txt" -> "c",
// "name" -> "name"). A LEADING dot is part of the extension (".git" -> "" --
// net48, probe-pinned), a trailing dot is NOT (net48: "file." -> "file.").
// Returns "" for a trailing-separator path ("a\\" -> "") and null for a null
// path (net48 "" -> ArgumentException -- M3 null).
Il2CppObject* il2cpp_path_get_file_name_without_extension(Il2CppObject* path);

//------------------------------------------------------------------------------
// System.Diagnostics.Process (simplified).
//------------------------------------------------------------------------------

// Process.Start(exePath, args): launches 'exePath' with the command line
// 'args' via CreateProcessW (CREATE_NO_WINDOW child console). The child's
// stdout and stderr are redirected to anonymous pipes (CreatePipe; nb7
// PROC-STARTINFO replaced the legacy temp-file capture) that
// read_stdout_to_end / read_stderr_to_end drain, and its process handle is
// kept in a PID-keyed table. Returns the child's PID (> 0) on success; 0 on
// launch failure, -1 on null input (and unsupported on POSIX). A started
// child should be waited on with wait_for_exit to release its handle.
int32_t il2cpp_process_start(Il2CppObject* exePath, Il2CppObject* args);

// Process.WaitForExit(pid): blocks until the child with PID 'pid' exits,
// caches its exit code (see get_exit_code) and closes its handle. 0 on
// success; non-zero for an unknown PID or on POSIX (unsupported). Idempotent:
// calling it again after the child exited still returns 0.
int32_t il2cpp_process_wait_for_exit(int32_t pid);

// Process.ExitCode(pid): the exit code of a child that wait_for_exit already
// completed for; -1 when 'pid' is unknown, the child has not been waited on,
// or on POSIX (unsupported).
int32_t il2cpp_process_get_exit_code(int32_t pid);

// Process StandardOutput.ReadToEnd(pid) / StandardError.ReadToEnd(pid) (nb7
// PROC-STARTINFO): drain the child's redirected stdout / stderr anonymous
// pipe to EOF and return the FULL captured text (the legacy temp-file first-
// line read is retired). Bytes are passed through 1:1 (the pipe carries
// opaque bytes; only ASCII output round-trips -- the ReadToEnd encoding
// divergence is documented). Once drained, a later call returns "" (net48
// EOF semantics). Throws InvalidOperationException (managed transport) when
// that stream was NOT redirected -- the net48 "StandardOutput has not been
// redirected" contract; returns null for an unknown PID or on POSIX.
//
// DEADLOCK BOUNDARY (honest simplification): the drain is a BLOCKING read on
// the calling thread. The supported pattern is a SEQUENTIAL ReadToEnd (one
// stream at a time, the child's total buffered output below the pipe buffer
// -- 64KB default) -- exactly the net48 documented-recommended use. A child
// that simultaneously fills BOTH redirected pipes past the buffer deadlocks,
// the same class of hazard net48's own ReadToEnd carries.
Il2CppObject* il2cpp_process_read_stdout_to_end(int32_t pid);
Il2CppObject* il2cpp_process_read_stderr_to_end(int32_t pid);

// Process.GetCurrentProcess().Id / GetCurrentThread().Id: the OS process /
// thread identifiers (always > 0).
int32_t il2cpp_process_get_current_process_id();
int32_t il2cpp_process_get_current_thread_id();

// Process.ProcessName(pid) (nb3 PROC-GAP): the FILE STEM of the process
// image -- no directory, no ".exe" (net48 ProcessName semantics:
// "cmd.exe" -> "cmd"). Non-null for a process that can be queried
// (the tracked table handle is preferred, otherwise the PID is opened
// directly -- the current process included); null when the PID cannot be
// opened or on POSIX (unsupported).
Il2CppObject* il2cpp_process_get_name(int32_t pid);

// Process.HasExited(pid) (nb3 PROC-GAP): 1 when the process has terminated,
// 0 while it is still running -- a NON-BLOCKING state probe
// (WaitForSingleObject(handle, 0)); a tracked child whose wait_for_exit
// completed is already known exited. 1 for an unopenable PID (M3: net48
// would throw) and on POSIX (unsupported).
int32_t il2cpp_process_has_exited(int32_t pid);

// Process.Kill(pid) (nb3 PROC-GAP): terminates the process (TerminateProcess,
// exit code 1). A tracked child's handle is folded into the waited state so
// a later wait_for_exit / get_exit_code / has_exited stay consistent without
// an explicit wait. 0 on success, non-zero for an unopenable PID (and on
// POSIX, unsupported).
int32_t il2cpp_process_kill(int32_t pid);

// Process.Exit(code): terminates this process with the given exit code.
// Never returns.
[[noreturn]] void il2cpp_process_exit(int32_t code);

//------------------------------------------------------------------------------
// nb4 PROC-DIAG: the diagnostic Process properties -- pure Win32 queries over
// the tracked-child table / a direct PID open, exact/boolean gates only.
//------------------------------------------------------------------------------

// Process.StartTime(pid): the process CREATION time (GetProcessTimesW -> the
// FILETIME converted to a DateTime ticks value, 1601->0001 epoch shift).
// Returns a zero DateTime for an unopenable PID / query failure (net48: the
// same lookups throw -- null/zero is the simplified-domain convention).
// net48 semantics caveat: reading StartTime on an EXITED process throws
// InvalidOperationException; the runtime documents the tracked-slot shape
// (waited) as the same guard where the observation is deterministic.
il2cpp::runtime::Il2CppDateTime il2cpp_process_get_start_time(int32_t pid);

// Process.ExitTime(pid): the process EXIT time (GetProcessTimesW exit
// FILETIME -> DateTime ticks). net48: only readable after the process exited
// (reading it while alive throws InvalidOperationException); a zero DateTime
// signals an unqueryable PID.
il2cpp::runtime::Il2CppDateTime il2cpp_process_get_exit_time(int32_t pid);

// Process.WorkingSet64(pid): the process working-set size in bytes
// (GetProcessMemoryInfo -> PROCESS_MEMORY_COUNTERS.WorkingSetSize).
// net48: accessing it on an EXITED process throws InvalidOperationException
// ("Cannot process request because the process has exited") -- the runtime
// throws the same managed exception for a waited (exited) tracked child and
// for an unopenable PID. Returns the byte count on success.
int64_t il2cpp_process_get_working_set64(int32_t pid);

//------------------------------------------------------------------------------
// nb7 PROC-STARTINFO: the System.Diagnostics.ProcessStartInfo object model
// (GLOBAL descriptor -- the regex_obj / synclite / Version contract), the
// Start(ProcessStartInfo) factory and the FULL anonymous-pipe StandardOutput /
// StandardError read.
//
// OBJECT MODEL: ProcessStartInfo is a light managed object -- header + the
// FileName/Arguments string fields (GC reference slots 1/2) + the boolean
// flags (UseShellExecute / RedirectStandardOutput / RedirectStandardError /
// CreateNoWindow). The transpiler emits no descriptor for it; the generated
// newobj fallback allocates through il2cpp_object_new(&System_Diagnostics_
// ProcessStartInfo_class) and the property accessors below own the storage.
//------------------------------------------------------------------------------

// Managed System.Diagnostics.ProcessStartInfo layout. The GC reference fields
// sit in slots 1/2 (bit i marks the field at offset i*sizeof(void*), so the
// refFieldBitmap 0x03 is pointer-size independent).
struct Il2CppProcessStartInfo
{
    Il2CppObject obj;              // object header (klass pointer, offset 0)
    Il2CppString* fileName;        // FileName (GC reference -- slot 1)
    Il2CppString* arguments;       // Arguments (GC reference -- slot 2)
    int32_t useShellExecute;       // (net48 default 1)
    int32_t redirectStandardOutput;
    int32_t redirectStandardError;
    int32_t createNoWindow;
};

// The GLOBAL class descriptor is declared above (outside this namespace);
// defined in pathproc.cpp.

// Register the descriptor once (idempotent; il2cpp_class_register dedupes by
// pointer). Called defensively by the ctors.
void il2cpp_process_start_info_register_classes();

// --- ProcessStartInfo icalls (receiver = Il2CppObject* self) -----------------
// String parameters arrive as Il2CppObject* (the generated code passes the
// uniform reference slot); the implementation casts to Il2CppString*.

// new ProcessStartInfo() / (string fileName) / (string fileName, string args).
// The parameterless form sets the net48 defaults: FileName/Arguments ""
// (empty, never null) and UseShellExecute true.
void icall_processstartinfo_ctor(Il2CppObject* self);
void icall_processstartinfo_ctor_fn(Il2CppObject* self, Il2CppObject* fileName);
void icall_processstartinfo_ctor_fn_args(Il2CppObject* self, Il2CppObject* fileName, Il2CppObject* arguments);

// Property accessors (string / bool). A null setter argument is stored as the
// empty string (the net48 non-null contract).
Il2CppObject* icall_processstartinfo_get_filename(Il2CppObject* self);
void icall_processstartinfo_set_filename(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* icall_processstartinfo_get_arguments(Il2CppObject* self);
void icall_processstartinfo_set_arguments(Il2CppObject* self, Il2CppObject* value);
int32_t icall_processstartinfo_get_useshellexecute(Il2CppObject* self);
void icall_processstartinfo_set_useshellexecute(Il2CppObject* self, int32_t value);
int32_t icall_processstartinfo_get_redirectstdout(Il2CppObject* self);
void icall_processstartinfo_set_redirectstdout(Il2CppObject* self, int32_t value);
int32_t icall_processstartinfo_get_redirectstderr(Il2CppObject* self);
void icall_processstartinfo_set_redirectstderr(Il2CppObject* self, int32_t value);
int32_t icall_processstartinfo_get_createnowindow(Il2CppObject* self);
void icall_processstartinfo_set_createnowindow(Il2CppObject* self, int32_t value);

// Process.Start(ProcessStartInfo): launches the psi's FileName + Arguments via
// CreateProcessW. RedirectStandardOutput/Error route the child stream into an
// anonymous pipe (read back by read_stdout_to_end / read_stderr_to_end); when
// a redirect is requested while UseShellExecute is true the call throws
// InvalidOperationException through the managed transport (net48 semantics).
// UseShellExecute is otherwise recorded but not consulted (the runtime always
// launches through CreateProcessW -- the ShellExecuteEx/DDE path is out of
// scope, a documented deviation). Returns the child PID (> 0) on success, 0
// on launch failure, -1 for a null input (and unsupported on POSIX).
int32_t il2cpp_process_start_info(Il2CppObject* psi);

}} // namespace il2cpp::runtime
