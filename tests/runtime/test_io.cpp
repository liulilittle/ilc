//------------------------------------------------------------------------------
// test_io.cpp -- System.IO base icall runtime unit tests (File / Path /
// Directory). See il2cpp_io.h for the icall contract under test.
//
// Coverage (normal + edge + error paths):
//   1. File.Exists   : false for missing paths, true after WriteAllText.
//   2. WriteAllText  : creates the file; overwrite truncates; empty content
//                      produces an empty file.
//   3. ReadAllText   : round-trips ASCII content; missing file throws
//                      FileNotFoundException (net48).
//   4. WriteAllLines : writes one line per element with CRLF separators.
//   5. ReadAllLines  : returns a managed string[] with one element per line;
//                      missing file throws FileNotFoundException (net48).
//   6. File.Delete   : removes the file; deleting a missing file succeeds
//                      (.NET silent-success semantics); missing -> 0.
//   7. Directory.CreateDirectory : recursive creation returns 0 and the
//                      created levels are reported by Directory.Exists;
//                      idempotent on existing directories.
//   8. Directory.Exists : false for missing paths, true for created ones.
//   9. Path.GetFileName   : last-segment extraction (backslash + forward
//                      slash), empty result for trailing separators.
//  10. Path.GetExtension  : ".ext" from dotted names, "." for trailing dot,
//                      "" for dotfiles and extension-less names.
//  11. Path.Combine  : separator joining incl. empty-part and leading-
//                      separator rules (Windows semantics).
//  12. ReadAllLines line splitting: bare \r, doubled \r, mixed \r\n/\n
//                      (net48 StreamReader.ReadLine semantics).
//  13. File.Delete on a directory -> error (net48 UnauthorizedAccessException).
//  14. File.Copy (2-arg + overwrite overloads), File.Move: success paths
//                      return 0; error paths THROW through the DotNetException
//                      carrier (existing-dest -> IOException, missing-source ->
//                      FileNotFoundException, net48 semantics); null args still
//                      return -1.
//  15. File.GetAttributes / SetAttributes: Archive/ReadOnly/Normal/Directory
//                      mask semantics (net48 probed values on Windows).
//  16. File.GetLastWriteTime: local time near now; missing path -> 1601
//                      local epoch (net48 FromFileTime(0), does not throw).
//  17. Directory.Delete(path, recursive): empty dir both modes OK; missing
//                      path -> DirectoryNotFoundException (both modes);
//                      non-empty false -> IOException with the tree intact;
//                      non-empty true -> whole tree removed (nested children
//                      too); null -> ArgumentNullException; file path ->
//                      IOException. All exceptions through the DotNetException
//                      carrier (net48 semantics, rtdel2-b23).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal. All
// test artifacts live under the relative directory "io_tmp" (created fresh
// in the working directory and removed at the end).
//------------------------------------------------------------------------------

#include "il2cpp_io.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h" // il2cpp::DotNetException (Directory.Delete throws)
#include "il2cpp_runtime.h"    // il2cpp_object_new (FileInfo allocation in 6e)
#include "il2cpp_string.h"
#include "il2cpp_time.h" // Il2CppDateTime + icall_datetime_now (GetLastWriteTime probe)
#include "platform.h" // plat_file_open (UTF-8 wide-path probe in the 6b section)

#include <cstdint>
#include <cstdio>
#include <cstring>

#if defined(_WIN32)
    #include <direct.h> // ::_rmdir
    #define IL2CPP_TEST_RMDIR(p) ::_rmdir(p)
#else
    #include <unistd.h> // ::rmdir
    #define IL2CPP_TEST_RMDIR(p) ::rmdir(p)
#endif

namespace {

static_assert(sizeof(void*) == 8, "io test fixture assumes 8-byte pointers");

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
// head is asserted so a base-chain extension does not break the test). Any
// other outcome is a recorded failure (arrayext-test convention).
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

// Managed exception type names (exact chain head, as thrown by io.cpp).
const char kDirectoryNotFoundType[] = "System.IO.DirectoryNotFoundException";
const char kFileNotFoundType[]      = "System.IO.FileNotFoundException";
const char kIOExceptionType[]       = "System.IO.IOException";
const char kArgumentNullType[]      = "System.ArgumentNullException";

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

// Element extraction helper: array element 'index' as a managed string object.
il2cpp::Il2CppObject* ArrayElementAsString(il2cpp::Il2CppObject* arrObj, int32_t index)
{
    if (arrObj == nullptr)
    {
        return nullptr;
    }
    il2cpp::Il2CppArray* arr = reinterpret_cast<il2cpp::Il2CppArray*>(arrObj);
    if (index < 0 || index >= arr->length)
    {
        return nullptr;
    }
    return *reinterpret_cast<il2cpp::Il2CppObject**>(
        il2cpp::il2cpp_array_get_elem_ptr(arr, index));
}

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

    // --- 0. Prepare the test directory (all artifacts live under io_tmp) ---
    Expect(icall_directory_create_directory(Cs("io_tmp")) == 0,
           "Directory.CreateDirectory creates the test directory");

    // --- 1. File.Exists: missing -> 0 --------------------------------------
    Expect(icall_file_exists(Cs("io_tmp/no_such.txt")) == 0,
           "File.Exists returns 0 for a missing file");

    // --- 2/3. WriteAllText then Exists + ReadAllText round-trip ------------
    Expect(icall_file_write_all_text(Cs("io_tmp/data.txt"), Cs("hello io")) == 0,
           "File.WriteAllText returns 0 on success");
    Expect(icall_file_exists(Cs("io_tmp/data.txt")) == 1,
           "File.Exists returns 1 after WriteAllText");
    Il2CppObject* readBack = icall_file_read_all_text(Cs("io_tmp/data.txt"));
    Expect(readBack != nullptr && StrEquals(readBack, "hello io"),
           "File.ReadAllText round-trips the written content");

    // --- 3b. Overwrite truncates -------------------------------------------
    Expect(icall_file_write_all_text(Cs("io_tmp/data.txt"), Cs("second")) == 0,
           "File.WriteAllText overwrite succeeds");
    Il2CppObject* afterOverwrite = icall_file_read_all_text(Cs("io_tmp/data.txt"));
    Expect(afterOverwrite != nullptr && StrEquals(afterOverwrite, "second"),
           "overwrite truncates the previous content (read-back is \"second\")");

    // --- 3c. Empty content -> empty file ------------------------------------
    Expect(icall_file_write_all_text(Cs("io_tmp/data.txt"), Cs("")) == 0,
           "File.WriteAllText with empty content succeeds");
    Il2CppObject* afterEmpty = icall_file_read_all_text(Cs("io_tmp/data.txt"));
    Expect(afterEmpty != nullptr && StrEquals(afterEmpty, ""),
           "empty content reads back as an empty string");

    // --- 4/5. WriteAllLines + ReadAllLines ---------------------------------
    Il2CppObject* lines = reinterpret_cast<Il2CppObject*>(
        il2cpp_array_new(il2cpp_string_class(), static_cast<int32_t>(sizeof(void*)), true, 3));
    *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(
        reinterpret_cast<Il2CppArray*>(lines), 0)) = Cs("one");
    *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(
        reinterpret_cast<Il2CppArray*>(lines), 1)) = Cs("two");
    *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(
        reinterpret_cast<Il2CppArray*>(lines), 2)) = Cs("three");
    Expect(icall_file_write_all_lines(Cs("io_tmp/data.txt"), lines) == 0,
           "File.WriteAllLines returns 0 on success");
    Il2CppObject* backArr = icall_file_read_all_lines(Cs("io_tmp/data.txt"));
    Expect(backArr != nullptr, "File.ReadAllLines returns a non-null array");
    if (backArr != nullptr)
    {
        const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(backArr);
        Expect(arr->length == 3, "File.ReadAllLines returns one element per line (3)");
        Expect(StrEquals(ArrayElementAsString(backArr, 0), "one")
               && StrEquals(ArrayElementAsString(backArr, 1), "two")
               && StrEquals(ArrayElementAsString(backArr, 2), "three"),
               "ReadAllLines elements match the written lines in order");
    }
    // CRLF file layout (Windows semantics): "one\r\ntwo\r\nthree\r\n" -- every
    // line is terminated, including the last (aligned with .NET behavior).
    Il2CppObject* raw = icall_file_read_all_text(Cs("io_tmp/data.txt"));
    Expect(raw != nullptr && StrEquals(raw, "one\r\ntwo\r\nthree\r\n"),
           "WriteAllLines joins lines with CRLF (Windows semantics)");

    // --- 4b. Bare-\r line splitting (net48: \r, \n and \r\n all split) -----
    // "a\rb" is TWO lines on net48 (probed) -- the splitter must treat a lone
    // '\r' as a terminator, not just a CRLF prefix.
    Expect(icall_file_write_all_text(Cs("io_tmp/cr.txt"), Cs("a\rb")) == 0,
           "File.WriteAllText writes a bare-CR content");
    Il2CppObject* crArr = icall_file_read_all_lines(Cs("io_tmp/cr.txt"));
    if (crArr != nullptr)
    {
        const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(crArr);
        Expect(arr->length == 2
               && StrEquals(ArrayElementAsString(crArr, 0), "a")
               && StrEquals(ArrayElementAsString(crArr, 1), "b"),
               "ReadAllLines splits on a lone \\r (net48 semantics)");
    }
    // "\r\r" -> three lines: "a", "" (middle empty line), "b".
    Expect(icall_file_write_all_text(Cs("io_tmp/cr.txt"), Cs("a\r\rb")) == 0,
           "File.WriteAllText writes a doubled-CR content");
    Il2CppObject* cr2Arr = icall_file_read_all_lines(Cs("io_tmp/cr.txt"));
    if (cr2Arr != nullptr)
    {
        const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(cr2Arr);
        Expect(arr->length == 3
               && StrEquals(ArrayElementAsString(cr2Arr, 0), "a")
               && StrEquals(ArrayElementAsString(cr2Arr, 1), "")
               && StrEquals(ArrayElementAsString(cr2Arr, 2), "b"),
               "ReadAllLines yields an empty middle line between two CRs");
    }
    // Mixed \r\n and bare \n in one document.
    Expect(icall_file_write_all_text(Cs("io_tmp/cr.txt"), Cs("x\r\ny\nz")) == 0,
           "File.WriteAllText writes a mixed-terminator content");
    Il2CppObject* mixedArr = icall_file_read_all_lines(Cs("io_tmp/cr.txt"));
    if (mixedArr != nullptr)
    {
        const Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(mixedArr);
        Expect(arr->length == 3
               && StrEquals(ArrayElementAsString(mixedArr, 0), "x")
               && StrEquals(ArrayElementAsString(mixedArr, 1), "y")
               && StrEquals(ArrayElementAsString(mixedArr, 2), "z"),
               "ReadAllLines splits mixed CRLF/LF content");
    }
    icall_file_delete(Cs("io_tmp/cr.txt"));

    // --- 5b. Missing file throws FileNotFoundException (net48 contract;
    // diffio001 readalltext.missing) --------------------------------------
    ExpectThrows(kFileNotFoundType,
                 "File.ReadAllText throws FileNotFoundException for a missing file",
                 [&] { icall_file_read_all_text(Cs("io_tmp/no_such.txt")); });
    ExpectThrows(kFileNotFoundType,
                 "File.ReadAllLines throws FileNotFoundException for a missing file",
                 [&] { icall_file_read_all_lines(Cs("io_tmp/no_such.txt")); });

    // --- 6. File.Delete ------------------------------------------------------
    Expect(icall_file_delete(Cs("io_tmp/data.txt")) == 0,
           "File.Delete returns 0 on success");
    Expect(icall_file_exists(Cs("io_tmp/data.txt")) == 0,
           "File.Exists returns 0 after Delete");
    Expect(icall_file_delete(Cs("io_tmp/data.txt")) == 0,
           "File.Delete of a missing file returns 0 (.NET silent success)");

    // --- 6c. File.Delete on a directory is an error -------------------------
    // net48 File.Delete on a directory throws UnauthorizedAccessException;
    // M3 signals it with a non-zero status (never a silent success).
    icall_directory_create_directory(Cs("io_tmp/deldir"));
    Expect(icall_file_delete(Cs("io_tmp/deldir")) != 0,
           "File.Delete on a directory returns an error (net48 throws)");
    Expect(icall_directory_exists(Cs("io_tmp/deldir")) == 1,
           "File.Delete on a directory leaves the directory in place");
    (void)IL2CPP_TEST_RMDIR("io_tmp/deldir");

    // --- 6b. UTF-8 non-ASCII path (Windows wide-path correctness) -----------
    // A non-ASCII UTF-8 file name written/read/deleted through the icalls.
    // On Windows the narrow CRT opens such paths through the ANSI code page,
    // which mangles (or rejects) the name; plat_file_open/_wfopen keeps the
    // exact UTF-8 name. The read-back round-trip alone would still pass with
    // both sides mangled identically, so the on-disk name is additionally
    // verified via the platform layer (wide on Windows) -- it only finds the
    // file when the on-disk name is EXACTLY the UTF-8 name.
    {
        // "数据.txt" in UTF-8 (BMP -- il2cpp_string_new_from_utf8 handles it).
        const char* kU8Path = "io_tmp/\xE6\x95\xB0\xE6\x8D\xAE.txt";
        Il2CppObject* u8 = reinterpret_cast<Il2CppObject*>(
            il2cpp::il2cpp_string_new_from_utf8(kU8Path));
        Expect(icall_file_write_all_text(u8, Cs("utf8 path ok")) == 0,
               "File.WriteAllText succeeds on a UTF-8 non-ASCII path");
        Expect(icall_file_exists(u8) == 1,
               "File.Exists finds the UTF-8-path file");
        FILE* wideProbe = plat_file_open(kU8Path, "rb");
        Expect(wideProbe != nullptr,
               "the on-disk name matches the UTF-8 name exactly (platform open)");
        if (wideProbe != nullptr)
        {
            (void)std::fclose(wideProbe);
        }
        Il2CppObject* u8Back = icall_file_read_all_text(u8);
        Expect(u8Back != nullptr && StrEquals(u8Back, "utf8 path ok"),
               "File.ReadAllText round-trips the UTF-8-path file");
        Expect(icall_file_delete(u8) == 0,
               "File.Delete succeeds on a UTF-8 non-ASCII path");
        Expect(icall_file_exists(u8) == 0,
               "File.Exists returns 0 after deleting the UTF-8-path file");
    }

    // --- 6d. File.Copy / File.Move / GetAttributes / SetAttributes / --------
    //       GetLastWriteTime (net48 gap closure; il2cpp_io.h conventions).
    {
        // --- 6d.1 File.Copy ---
        icall_file_write_all_text(Cs("io_tmp/copy_src.txt"), Cs("copy me"));
        Expect(icall_file_copy(Cs("io_tmp/copy_src.txt"), Cs("io_tmp/copy_dst.txt")) == 0,
               "File.Copy returns 0 on success");
        Il2CppObject* copyBack = icall_file_read_all_text(Cs("io_tmp/copy_dst.txt"));
        Expect(copyBack != nullptr && StrEquals(copyBack, "copy me"),
               "File.Copy copies the source content");
        // dest exists + no overwrite -> error (net48 IOException, thrown)
        ExpectThrows(kIOExceptionType,
                     "File.Copy of an existing dest without overwrite throws IOException",
                     [&] {
                         icall_file_copy(Cs("io_tmp/copy_src.txt"), Cs("io_tmp/copy_dst.txt"));
                     });
        // overwrite=true replaces the content
        icall_file_write_all_text(Cs("io_tmp/copy_src.txt"), Cs("changed"));
        Expect(icall_file_copy_overwrite(Cs("io_tmp/copy_src.txt"), Cs("io_tmp/copy_dst.txt"), 1) == 0,
               "File.Copy(overwrite=true) returns 0");
        Il2CppObject* ovwBack = icall_file_read_all_text(Cs("io_tmp/copy_dst.txt"));
        Expect(ovwBack != nullptr && StrEquals(ovwBack, "changed"),
               "File.Copy(overwrite=true) replaces the destination content");
        // missing source -> error (net48 FileNotFoundException, thrown)
        ExpectThrows(kFileNotFoundType,
                     "File.Copy of a missing source throws FileNotFoundException",
                     [&] {
                         icall_file_copy(Cs("io_tmp/no_such.txt"), Cs("io_tmp/copy_x.txt"));
                     });
        Expect(icall_file_copy(nullptr, Cs("io_tmp/copy_x.txt")) == -1,
               "File.Copy with a null source returns -1");
        // the destination must be a fresh file (attributes copied -- Archive
        // on Windows, net48 probed 32); the copy's content is what matters.
        Expect(icall_file_exists(Cs("io_tmp/copy_dst.txt")) == 1,
               "File.Copy creates the destination file");

        // --- 6d.2 File.Move ---
        Expect(icall_file_move(Cs("io_tmp/copy_src.txt"), Cs("io_tmp/move_dst.txt")) == 0,
               "File.Move returns 0 on success");
        Expect(icall_file_exists(Cs("io_tmp/copy_src.txt")) == 0,
               "File.Move removes the source");
        Il2CppObject* moveBack = icall_file_read_all_text(Cs("io_tmp/move_dst.txt"));
        Expect(moveBack != nullptr && StrEquals(moveBack, "changed"),
               "File.Move moves the content to the destination");
        // dest exists -> error (net48 IOException, thrown)
        ExpectThrows(kIOExceptionType,
                     "File.Move to an existing dest throws IOException",
                     [&] {
                         icall_file_move(Cs("io_tmp/copy_dst.txt"), Cs("io_tmp/move_dst.txt"));
                     });
        Expect(icall_file_exists(Cs("io_tmp/copy_dst.txt")) == 1,
               "File.Move failure leaves the source in place");
        // missing source -> error (net48 FileNotFoundException, thrown)
        ExpectThrows(kFileNotFoundType,
                     "File.Move of a missing source throws FileNotFoundException",
                     [&] {
                         icall_file_move(Cs("io_tmp/no_such.txt"), Cs("io_tmp/move_x.txt"));
                     });
        Expect(icall_file_move(nullptr, Cs("io_tmp/move_x.txt")) == -1,
               "File.Move with a null source returns -1");

        // --- 6d.3 File.GetAttributes / File.SetAttributes ---
        icall_file_write_all_text(Cs("io_tmp/attr.txt"), Cs("attr"));
#if defined(_WIN32)
        // Windows: a freshly written file carries Archive (0x20 = 32) --
        // net48 probed exactly 32 for a plain WriteAllText file.
        Expect(icall_file_get_attributes(Cs("io_tmp/attr.txt")) == 32,
               "GetAttributes returns Archive (32) for a plain file (Windows)");
#else
        // POSIX fallback: a regular file reports the Archive approximation.
        Expect((icall_file_get_attributes(Cs("io_tmp/attr.txt")) & 32) != 0,
               "GetAttributes sets the Archive bit for a regular file (POSIX)");
#endif
        // missing path -> 0 (net48 throws FileNotFoundException; M3 signals 0)
        Expect(icall_file_get_attributes(Cs("io_tmp/no_such.txt")) == 0,
               "GetAttributes returns 0 for a missing file");
        Expect(icall_file_get_attributes(nullptr) == 0,
               "GetAttributes returns 0 for a null path");
        // a directory carries the Directory bit (0x10 = 16)
        Expect((icall_file_get_attributes(Cs("io_tmp")) & 16) != 0,
               "GetAttributes sets the Directory bit for a directory");
        // SetAttributes REPLACES the mask (net48 SetFileAttributesW semantics).
#if defined(_WIN32)
        Expect(icall_file_set_attributes(Cs("io_tmp/attr.txt"), 1) == 0,
               "SetAttributes(ReadOnly) returns 0");
        Expect(icall_file_get_attributes(Cs("io_tmp/attr.txt")) == 1,
               "SetAttributes REPLACES the mask: ReadOnly (1) only, Archive cleared");
        Expect(icall_file_set_attributes(Cs("io_tmp/attr.txt"), 128) == 0,
               "SetAttributes(Normal) returns 0");
        Expect(icall_file_get_attributes(Cs("io_tmp/attr.txt")) == 128,
               "SetAttributes(Normal) yields exactly 128 (net48 probed)");
#else
        // POSIX: ReadOnly bit toggling is the representable subset.
        Expect(icall_file_set_attributes(Cs("io_tmp/attr.txt"), 1) == 0,
               "SetAttributes(ReadOnly) returns 0 (POSIX)");
        Expect((icall_file_get_attributes(Cs("io_tmp/attr.txt")) & 1) != 0,
               "SetAttributes(ReadOnly) sets the ReadOnly bit (POSIX)");
#endif
        Expect(icall_file_set_attributes(Cs("io_tmp/no_such.txt"), 1) != 0,
               "SetAttributes on a missing file is an error");
        Expect(icall_file_set_attributes(nullptr, 1) != 0,
               "SetAttributes with a null path is an error");

        // --- 6d.4 File.GetLastWriteTime (LOCAL ticks; net48 Kind=Local) ---
        Il2CppDateTime lwt = icall_file_get_last_write_time(Cs("io_tmp/attr.txt"));
        // the file was written moments ago -- its last-write time must be near
        // the current local time (tolerance: 5 seconds either way).
        Il2CppDateTime now = icall_datetime_now();
        const int64_t kTicksPerSecond = 10000000;
        Expect(lwt.ticks > 0, "GetLastWriteTime returns a non-zero DateTime");
        Expect(lwt.ticks >= now.ticks - 5 * kTicksPerSecond
               && lwt.ticks <= now.ticks + 5 * kTicksPerSecond,
               "GetLastWriteTime is within 5s of the current local time");
        // missing path -> 1601-01-01 LOCAL (net48 FromFileTime(0); does NOT
        // throw -- probed). The local 1601 ticks are below the 1970 epoch.
        Il2CppDateTime lwtMissing = icall_file_get_last_write_time(Cs("io_tmp/no_such.txt"));
        Expect(lwtMissing.ticks > 0 && lwtMissing.ticks < 621355968000000000LL,
               "GetLastWriteTime of a missing file returns the 1601 local epoch");
        Expect(icall_file_get_last_write_time(nullptr).ticks == 0,
               "GetLastWriteTime(null) returns DateTime.MinValue");

        // --- 6d.5 cleanup for the gap-api section ---
        icall_file_delete(Cs("io_tmp/copy_dst.txt"));
        icall_file_delete(Cs("io_tmp/move_dst.txt"));
        icall_file_delete(Cs("io_tmp/attr.txt"));
        Expect(icall_file_exists(Cs("io_tmp/copy_src.txt")) == 0,
               "gap-api cleanup removes the moved-away source");
    }

    // --- 6e. FileInfo.Length (D-IOEXT-1) -------------------------------------
    // net48 semantics: the Length property returns the regular-file size, and
    // THROWS FileNotFoundException on a missing path and on a directory path
    // (both probed on net48; previously the runtime returned 0 -- DEFECT). A
    // null stored path stays 0 (net48 throws ArgumentNullException at the
    // FileInfo ctor -- a different layer, not transportable here).
    {
        icall_file_write_all_text(Cs("io_tmp/fi_len.txt"), Cs("hello fi")); // 8 bytes
        Il2CppObject* fi = il2cpp_object_new(il2cpp_fileinfo_class());
        Expect(fi != nullptr, "FileInfo object allocates");
        icall_fileinfo_ctor(fi, Cs("io_tmp/fi_len.txt"));
        Expect(icall_fileinfo_get_length(fi) == 8,
               "FileInfo.Length returns the regular-file size in bytes");
        // directory path -> FileNotFoundException (net48 probed)
        icall_fileinfo_ctor(fi, Cs("io_tmp"));
        ExpectThrows(kFileNotFoundType,
                     "FileInfo.Length on a DIRECTORY path throws FileNotFoundException",
                     [&] { icall_fileinfo_get_length(fi); });
        // missing path -> FileNotFoundException (net48 probed)
        icall_fileinfo_ctor(fi, Cs("io_tmp/no_such.txt"));
        ExpectThrows(kFileNotFoundType,
                     "FileInfo.Length on a MISSING path throws FileNotFoundException",
                     [&] { icall_fileinfo_get_length(fi); });
        // null stored path -> 0 (ctor-layer ArgumentNullException not transported)
        icall_fileinfo_ctor(fi, nullptr);
        Expect(icall_fileinfo_get_length(fi) == 0,
               "FileInfo.Length with a null stored path returns 0");
        icall_file_delete(Cs("io_tmp/fi_len.txt"));
    }

    // --- 1b. Null path defence ----------------------------------------------
    Expect(icall_file_exists(nullptr) == 0, "File.Exists(null) returns 0");
    Expect(icall_directory_exists(nullptr) == 0, "Directory.Exists(null) returns 0");
    Expect(icall_file_read_all_text(nullptr) == nullptr,
           "File.ReadAllText(null) returns null");

    // --- 7. Directory.CreateDirectory (recursive) + Exists ------------------
    Expect(icall_directory_exists(Cs("io_tmp")) == 1,
           "Directory.Exists returns 1 for the test directory");
    Expect(icall_directory_exists(Cs("io_tmp/nested")) == 0,
           "Directory.Exists returns 0 before CreateDirectory");
    Expect(icall_directory_create_directory(Cs("io_tmp/nested/deep")) == 0,
           "Directory.CreateDirectory creates intermediate levels");
    Expect(icall_directory_exists(Cs("io_tmp/nested")) == 1,
           "created intermediate level is reported by Directory.Exists");
    Expect(icall_directory_exists(Cs("io_tmp/nested/deep")) == 1,
           "created leaf level is reported by Directory.Exists");
    Expect(icall_directory_exists(Cs("io_tmp/not_created")) == 0,
           "Directory.Exists returns 0 for a never-created path");
    Expect(icall_directory_create_directory(Cs("io_tmp/nested")) == 0,
           "Directory.CreateDirectory is idempotent on existing directories");

    // --- 8. Directory.Delete(path, recursive) ---------------------------------
    // net48 semantics (csc probe, rtdelprobe; thrown through the DotNetException
    // carrier, rtdel2-b23): a missing path throws DirectoryNotFoundException in
    // BOTH modes; an empty directory deletes with recursive=false; a non-empty
    // directory with recursive=false throws IOException and leaves the tree;
    // a FILE path throws IOException; null throws ArgumentNullException;
    // recursive=true removes the whole tree (children before parents); a
    // read-only entry blocks the delete without clearing the attribute
    // (UnauthorizedAccessException -- not transportable in M3, still a
    // non-zero status).
    {
        // --- 8.1 Empty directory: both modes succeed ------------------------
        Expect(icall_directory_create_directory(Cs("io_tmp/delrec/empty")) == 0,
               "setup: create the empty delete-test directory");
        Expect(icall_directory_delete_recursive(
                   reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/empty")), 0) == 0,
               "Delete(empty, recursive=false) returns 0");
        Expect(icall_directory_exists(Cs("io_tmp/delrec/empty")) == 0,
               "Delete(empty, false) removes the directory");
        Expect(icall_directory_create_directory(Cs("io_tmp/delrec/empty2")) == 0,
               "setup: recreate the empty delete-test directory");
        Expect(icall_directory_delete_recursive(
                   reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/empty2")), 1) == 0,
               "Delete(empty, recursive=true) returns 0");
        Expect(icall_directory_exists(Cs("io_tmp/delrec/empty2")) == 0,
               "Delete(empty, true) removes the directory");

        // --- 8.2 Missing path: DirectoryNotFoundException (both modes) -------
        ExpectThrows(kDirectoryNotFoundType, "Delete(missing, recursive=false) throws DNFE",
                     [&] {
                         icall_directory_delete_recursive(
                             reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/missing")), 0);
                     });
        ExpectThrows(kDirectoryNotFoundType, "Delete(missing, recursive=true) throws DNFE",
                     [&] {
                         icall_directory_delete_recursive(
                             reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/missing")), 1);
                     });

        // --- 8.3 Non-empty + recursive=false: IOException, tree intact -------
        Expect(icall_directory_create_directory(Cs("io_tmp/delrec/nonempty/sub")) == 0,
               "setup: create a non-empty tree");
        Expect(icall_file_write_all_text(Cs("io_tmp/delrec/nonempty/a.txt"), Cs("x")) == 0,
               "setup: create a file in the tree");
        ExpectThrows(kIOExceptionType, "Delete(non-empty, recursive=false) throws IOException",
                     [&] {
                         icall_directory_delete_recursive(
                             reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/nonempty")), 0);
                     });
        Expect(icall_directory_exists(Cs("io_tmp/delrec/nonempty")) == 1,
               "Delete(non-empty, false) leaves the tree in place");

        // --- 8.4 Non-empty + recursive=true: whole tree removed --------------
        Expect(icall_directory_delete_recursive(
                   reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/nonempty")), 1) == 0,
               "Delete(non-empty, recursive=true) returns 0");
        Expect(icall_directory_exists(Cs("io_tmp/delrec/nonempty")) == 0,
               "Delete(non-empty, true) removes the whole tree");
        Expect(icall_file_exists(Cs("io_tmp/delrec/nonempty/a.txt")) == 0
               && icall_directory_exists(Cs("io_tmp/delrec/nonempty/sub")) == 0,
               "recursive delete removes nested children (file + subdirectory)");

        // --- 8.5 Deep tree with recursive=true -------------------------------
        Expect(icall_directory_create_directory(Cs("io_tmp/delrec/deep/l1/l2/l3")) == 0,
               "setup: create a deep tree");
        Expect(icall_file_write_all_text(Cs("io_tmp/delrec/deep/l1/x.txt"), Cs("x")) == 0,
               "setup: file at depth 1");
        Expect(icall_file_write_all_text(Cs("io_tmp/delrec/deep/l1/l2/y.txt"), Cs("x")) == 0,
               "setup: file at depth 2");
        Expect(icall_directory_delete_recursive(
                   reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/deep")), 1) == 0,
               "Delete(deep tree, recursive=true) returns 0");
        Expect(icall_directory_exists(Cs("io_tmp/delrec/deep")) == 0,
               "deep recursive delete removes the root");
        Expect(icall_directory_exists(Cs("io_tmp/delrec/deep/l1/l2/l3")) == 0,
               "deep recursive delete removes the deepest level");
        Expect(icall_file_exists(Cs("io_tmp/delrec/deep/l1/x.txt")) == 0
               && icall_file_exists(Cs("io_tmp/delrec/deep/l1/l2/y.txt")) == 0,
               "deep recursive delete removes nested files");

        // --- 8.6 Null path -> ArgumentNullException (both modes) -------------
        ExpectThrows(kArgumentNullType, "Delete(null, recursive=true) throws ANE",
                     [&] { icall_directory_delete_recursive(nullptr, 1); });
        ExpectThrows(kArgumentNullType, "Delete(null, recursive=false) throws ANE",
                     [&] { icall_directory_delete_recursive(nullptr, 0); });

        // --- 8.7 Delete on a FILE path -> IOException ------------------------
        Expect(icall_file_write_all_text(Cs("io_tmp/delrec/notadir.txt"), Cs("x")) == 0,
               "setup: create a plain file");
        ExpectThrows(kIOExceptionType, "Delete(file, recursive=true) throws IOException",
                     [&] {
                         icall_directory_delete_recursive(
                             reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec/notadir.txt")), 1);
                     });
        Expect(icall_file_exists(Cs("io_tmp/delrec/notadir.txt")) == 1,
               "Delete on a file leaves the file in place");

        // --- 8.8 cleanup: remove the delete-test container --------------------
        Expect(icall_directory_delete_recursive(
                   reinterpret_cast<Il2CppString*>(Cs("io_tmp/delrec")), 1) == 0,
               "cleanup: remove the delrec container");
    }

    // --- 9. Path.GetFileName -------------------------------------------------
    Expect(StrEquals(icall_path_get_file_name(Cs("a\\b.txt")), "b.txt"),
           "GetFileName takes the last backslash-separated segment");
    Expect(StrEquals(icall_path_get_file_name(Cs("a/b/c.txt")), "c.txt"),
           "GetFileName also splits on forward slashes");
    Expect(StrEquals(icall_path_get_file_name(Cs("a\\b\\")), ""),
           "GetFileName returns empty for a trailing separator");
    Expect(StrEquals(icall_path_get_file_name(Cs("plain")), "plain"),
           "GetFileName returns the whole path when no separator exists");

    // --- 10. Path.GetExtension ------------------------------------------------
    Expect(StrEquals(icall_path_get_extension(Cs("a\\b.txt")), ".txt"),
           "GetExtension returns the dotted suffix");
    Expect(StrEquals(icall_path_get_extension(Cs("file.")), ""),
           "GetExtension returns empty for a trailing dot (net48 semantics)");
    Expect(StrEquals(icall_path_get_extension(Cs(".gitignore")), ".gitignore"),
           "GetExtension returns the dotted suffix for a dotfile name");
    Expect(StrEquals(icall_path_get_extension(Cs("noext")), ""),
           "GetExtension returns empty for an extension-less name");

    // --- 11. Path.Combine ------------------------------------------------------
    Expect(StrEquals(icall_path_combine(Cs("a"), Cs("b")), "a\\b"),
           "Combine joins parts with a backslash separator");
    Expect(StrEquals(icall_path_combine(Cs("a"), Cs("\\b")), "\\b"),
           "Combine returns the rooted second part unchanged (net48 semantics)");
    Expect(StrEquals(icall_path_combine(Cs("a\\"), Cs("b")), "a\\b"),
           "Combine does not duplicate the separator for a trailing backslash");
    Expect(StrEquals(icall_path_combine(Cs(""), Cs("b")), "b"),
           "Combine with an empty first part returns the second part");
    Expect(StrEquals(icall_path_combine(Cs("a"), Cs("")), "a"),
           "Combine with an empty second part returns the first part");
    Expect(icall_path_combine(nullptr, Cs("b")) == nullptr,
           "Combine with a null part returns null");

    // --- Cleanup: remove the file + the created directory chain --------------
    icall_file_delete(Cs("io_tmp/data.txt"));
    (void)IL2CPP_TEST_RMDIR("io_tmp/nested/deep");
    (void)IL2CPP_TEST_RMDIR("io_tmp/nested");
    (void)IL2CPP_TEST_RMDIR("io_tmp");
    Expect(icall_directory_exists(Cs("io_tmp")) == 0,
           "test directory is fully removed after cleanup");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d io assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime io tests passed\n");
    return 0;
}
