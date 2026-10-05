#pragma once
#include "il2cpp_array.h" // Il2CppArray (File.ReadAllBytes/WriteAllBytes ABI)
//------------------------------------------------------------------------------
// il2cpp_io.h -- System.IO base icalls (File / Path / Directory subset +
// FileInfo object closure).
//
// This header declares the runtime icall surface for the System.IO foundation
// that generated BCL code and hand-written tests link against. Every function
// takes/returns managed objects (Il2CppObject*): string arguments and the
// string[] array argument are managed references; the caller keeps them
// reachable while the call runs (the icalls themselves never retain them).
//
// Result conventions (M3 runtime: the managed exception system is a later
// milestone, so failures are expressed as return values, never C++ throws):
//   * Status functions (file_delete / append_all_text / write_all_text /
//     write_all_lines / directory_create_directory / directory_delete)
//     return 0 on success and a non-zero error code on failure. File.Delete
//     follows .NET semantics: deleting a missing file is a silent success
//     (0). Directory.Delete (a non-existent directory) is an error here
//     (net48 throws DirectoryNotFoundException -- not transportable in M3).
//   * Boolean functions (file_exists / directory_exists) return 1 for true,
//     0 for false -- including null/empty paths (no exception in this layer).
//   * Value-returning functions (read_all_text / read_all_lines / the Path
//     functions / fileinfo_get_length) return null (or 0 for the int64
//     length) on failure or for null inputs; a successful Path operation
//     never fails (pure string manipulation).
//
// File encoding contract: ReadAllText/ReadAllLines decode the file as UTF-8
// (the .NET Framework default for these APIs); WriteAllText/WriteAllLines
// encode UTF-8. WriteAllLines joins elements with CRLF on every platform
// (aligned with the net48 reference baseline on Windows).
//
// Path semantics are Windows-flavoured (backslash separator, as the .NET
// Framework 4.8 baseline expects on this platform); the path splitting rules
// of GetFileName/GetExtension/Combine follow the .NET behavior documented in
// io.cpp. The implementations are fully portable C++ (std::ifstream /
// std::ofstream + <sys/stat.h>) and compile under MSVC and MinGW alike.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h" // Il2CppString (Directory.Delete recursive param)
#include "il2cpp_time.h"  // Il2CppDateTime (File.GetLastWriteTime return)

#include <cstdint>

// System.IO.FileInfo runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_IO_FileInfo_class for newobj/isinst/castclass; defined in
// io.cpp, registered lazily through il2cpp_fileinfo_class()). The managed
// layout is Il2CppFileInfoObject below.
extern il2cpp::Il2CppClass System_IO_FileInfo_class;

namespace il2cpp {

// Managed System.IO.FileInfo layout: object header + the path this instance
// was constructed with (a GC reference field -- the collector scans slot 1,
// refFieldBitmap 0b10). All instance members (Length) resolve the path
// through this slot at call time, so no cached stat state exists.
struct Il2CppFileInfoObject
{
    Il2CppObject obj;       // object header (klass pointer, offset 0)
    Il2CppObject* path;     // the managed path string (slot 1, GC ref)
};

// The System.IO.FileInfo descriptor (lazily registered on first use;
// generated code allocates through &System_IO_FileInfo_class directly).
Il2CppClass* il2cpp_fileinfo_class();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// System.IO.File.
//------------------------------------------------------------------------------

// File.Exists(path): 1 when 'path' names an existing regular file, 0 when it
// names a missing path, a directory, or null. No filesystem access is
// performed for a null path.
int32_t icall_file_exists(Il2CppObject* path);

// File.Delete(path): 0 on success (or when the file does not exist -- .NET
// silent-success semantics); non-zero when the path names a DIRECTORY
// (net48 throws UnauthorizedAccessException) or cannot be removed.
int32_t icall_file_delete(Il2CppObject* path);

// File.WriteAllText(path, contents): creates/truncates the file and writes
// 'contents' as UTF-8. 0 on success, non-zero on failure (null path /
// unopenable target). A null 'contents' writes an empty file.
int32_t icall_file_write_all_text(Il2CppObject* path, Il2CppObject* contents);

// File.AppendAllText(path, contents): creates the file when missing,
// otherwise APPENDS 'contents' (UTF-8) at the end of the existing content
// (net48 semantics -- WriteAllText with std::ios::app). 0 on success,
// non-zero on failure (null path / unopenable target).
int32_t icall_file_append_all_text(Il2CppObject* path, Il2CppObject* contents);

  // File.ReadAllText(path): the file content as a managed string (UTF-8
  // decoded). Null when the file cannot be opened (missing path, null path).
  Il2CppObject* icall_file_read_all_text(Il2CppObject* path);
  
  // File.ReadAllBytes(path): the raw file content as a managed byte[].
  // Null when the file cannot be opened (missing path, null path) -- the M3
  // failure signal (net48 throws FileNotFoundException instead).
  Il2CppArray* icall_file_read_all_bytes(Il2CppObject* path);
  
  // File.WriteAllBytes(path, bytes): creates/truncates the file and writes the
  // raw bytes (embedded NULs preserved). 0 on success, non-zero on failure
  // (null path / unopenable target). A null 'bytes' writes an empty file.
  int32_t icall_file_write_all_bytes(Il2CppObject* path, Il2CppArray* bytes);

// File.WriteAllLines(path, lines): writes each string element as a UTF-8
// line terminated with CRLF. 0 on success; non-zero on failure (null path,
// unopenable target, or a null element -- the latter rolls back the
// partial file, mirroring .NET's ArgumentNullException behavior).
int32_t icall_file_write_all_lines(Il2CppObject* path, Il2CppObject* lines);

// File.ReadAllLines(path): a managed string[] holding one element per line
// (line endings stripped; \r\n, \n and \r all split -- net48
// StreamReader.ReadLine semantics, probed: "a\rb" is two lines). An empty
// file yields a zero-length array. Null when the file cannot be opened.
Il2CppObject* icall_file_read_all_lines(Il2CppObject* path);

// File.Copy(source, dest): copies the file named by 'source' to 'dest'
// WITHOUT overwriting an existing 'dest' (net48: a pre-existing destination
// throws IOException; a missing source throws FileNotFoundException -- M3
// signals both with a non-zero status). The destination receives the
// source's attributes (CopyFileW on Windows). 0 on success, -1 for a null
// path, non-zero on any failure.
int32_t icall_file_copy(Il2CppObject* source, Il2CppObject* dest);

// File.Copy(source, dest, overwrite): the 3-argument overload -- replaces
// 'dest' when 'overwrite' is non-zero, otherwise behaves exactly like the
// 2-argument form above (net48 semantics). Same status convention.
int32_t icall_file_copy_overwrite(Il2CppObject* source, Il2CppObject* dest,
                                  int32_t overwrite);

// File.Move(source, dest): moves the file named by 'source' to 'dest'
// (net48: a missing source throws FileNotFoundException, a pre-existing
// destination throws IOException -- M3 signals both with non-zero status).
// Works across volumes (Windows MoveFileW; POSIX falls back to copy+delete
// on EXDEV). 0 on success, -1 for a null path, non-zero on failure.
int32_t icall_file_move(Il2CppObject* source, Il2CppObject* dest);

// File.GetAttributes(path): the FileAttributes mask (net48 returns the
// Win32 FILE_ATTRIBUTE_* bits: a directory carries Directory=0x10, a plain
// writable file Archive=0x20, a read-only file ReadOnly=0x1). A missing or
// null path returns 0 (net48 throws FileNotFoundException -- not
// transportable in M3; 0 is never a valid mask of a real file).
int32_t icall_file_get_attributes(Il2CppObject* path);

// File.SetAttributes(path, attributes): REPLACES the attribute mask
// (net48 SetFileAttributesW semantics -- probed: setting ReadOnly clears
// the Archive bit; setting Normal yields exactly 0x80). 0 on success,
// non-zero on failure (null/missing path -- net48 FileNotFoundException).
int32_t icall_file_set_attributes(Il2CppObject* path, int32_t attributes);

// File.GetLastWriteTime(path): a LOCAL DateTime (net48 Kind=Local) of the
// file's last write. A MISSING path returns 1601-01-01 00:00 LOCAL --
// DateTime.FromFileTime(0) -- net48 does NOT throw here (probed). A null
// path returns DateTime.MinValue (M3 null-input convention).
Il2CppDateTime icall_file_get_last_write_time(Il2CppObject* path);

//------------------------------------------------------------------------------
// System.IO.Path (Windows semantics; pure string manipulation, never fails).
//------------------------------------------------------------------------------

// Path.GetFileName(path): the last path segment after the final separator
// ('\\' or '/'); empty string for a trailing separator. Null for null input.
Il2CppObject* icall_path_get_file_name(Il2CppObject* path);

// Path.GetExtension(path): the dotted suffix of the file name (".txt");
// empty for a trailing dot ("file." -- net48 semantics), for dotfiles
// (".git") and for extension-less names. Null for null input.
Il2CppObject* icall_path_get_extension(Il2CppObject* path);

// Path.Combine(first, second): 'first' + '\\' + 'second' without duplicating
// a trailing separator on the first part; a second part that starts with a
// separator is rooted and returned unchanged; an empty part yields the other
// part (net48 semantics). Null for null input.
Il2CppObject* icall_path_combine(Il2CppObject* first, Il2CppObject* second);

// Path.ChangeExtension(path, extension): replaces (or strips, when
// 'extension' is null) the extension of the last path segment per the .NET
// semantics (see io.cpp). Null for a null 'path'. A thin io-domain alias of
// il2cpp_path_change_extension (pathproc.h): the io surface is the one the
// generated include chain (il2cpp_io.h) exposes to BCL code.
Il2CppObject* icall_path_change_extension(Il2CppObject* path, Il2CppObject* extension);

//------------------------------------------------------------------------------
// System.IO.Directory.
//------------------------------------------------------------------------------

// Directory.Exists(path): 1 when 'path' names an existing directory, 0 when
// it names a missing path, a regular file, or null.
int32_t icall_directory_exists(Il2CppObject* path);

// Directory.CreateDirectory(path): recursively creates every missing level
// of 'path' (each created level is immediately visible to Directory.Exists).
// 0 on success (including an already-existing directory -- idempotent);
// non-zero when a level cannot be created (null/empty path, invalid name).
int32_t icall_directory_create_directory(Il2CppObject* path);

// Directory.Delete(path): removes the EMPTY directory named by 'path'.
// 0 on success; non-zero when the path is missing, names a file, is not
// empty, or cannot be removed (net48 throws in these cases -- not
// transportable in M3, the status code signals it).
int32_t icall_directory_delete(Il2CppObject* path);

// Directory.Delete(path, recursive): removes the directory named by 'path'.
// When 'recursive' is non-zero the ENTIRE tree is removed (net48 recursive
// semantics -- files and nested directories, post-order: children before the
// parent). When 'recursive' is zero it behaves exactly like the 1-arg form:
// only an EMPTY directory is removed. The net48 exceptions ARE thrown
// (rtdel2-b23 -- il2cpp_throw_new, chain transport): a null path ->
// ArgumentNullException; a MISSING path -> DirectoryNotFoundException (both
// modes); a path naming a FILE -> IOException; a non-empty directory with
// recursive==0 -> IOException with the tree left intact. An unremovable
// ENTRY inside a recursive delete (net48: read-only file ->
// UnauthorizedAccessException -- not transportable in M3) still signals
// non-zero instead of throwing. 0 on success.
int32_t icall_directory_delete_recursive(Il2CppString* path, int32_t recursive);

//------------------------------------------------------------------------------
// System.IO.FileInfo (object closure -- see Il2CppFileInfoObject above).
//------------------------------------------------------------------------------

// FileInfo(path) -- stores 'path' in the object's GC-ref slot. The
// allocation happens through System_IO_FileInfo_class (generated newobj);
// the icall makes the stored reference explicit. A null 'path' stores null
// (net48 would throw ArgumentNullException -- not transportable in M3).
void icall_fileinfo_ctor(Il2CppObject* self, Il2CppObject* path);

// FileInfo.Length (instance int64): the size in bytes of the file named by
// the stored path, probed at call time (wide _stat on Windows -- UTF-8
// non-ASCII names are exact). net48 semantics (D-IOEXT-1): a MISSING path or
// a DIRECTORY path throws FileNotFoundException through the DotNetException
// carrier (the File.Copy exception-chain pattern -- il2cpp_throw_file_not_
// found_exception); the size is returned only for a regular file. 0 only for
// a null stored path (net48 would throw ArgumentNullException at the FileInfo
// constructor -- not transportable in this layer).
int64_t icall_fileinfo_get_length(Il2CppObject* self);

}} // namespace il2cpp::runtime
