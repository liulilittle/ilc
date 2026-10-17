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

#include "il2cpp_object.h"

#include <cstdint>

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
// stdout and stderr are redirected to a temporary file that read_stdout_line
// reads back, and its process handle is kept in a PID-keyed table. Returns
// the child's PID (> 0) on success; 0 on launch failure, -1 on null input
// (and unsupported on POSIX). A started child should be waited on with
// wait_for_exit to release its handle.
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

// Process stdout line read: one line of the child's redirected stdout with
// the line terminator stripped (.NET ReadLine semantics), or null at EOF,
// for an unknown PID, or on POSIX (unsupported). Bytes are passed through
// 1:1 (the capture is opaque binary data; only ASCII output round-trips).
Il2CppObject* il2cpp_process_read_stdout_line(int32_t pid);

// Process.GetCurrentProcess().Id / GetCurrentThread().Id: the OS process /
// thread identifiers (always > 0).
int32_t il2cpp_process_get_current_process_id();
int32_t il2cpp_process_get_current_thread_id();

// Process.Exit(code): terminates this process with the given exit code.
// Never returns.
[[noreturn]] void il2cpp_process_exit(int32_t code);

}} // namespace il2cpp::runtime
