#pragma once
//------------------------------------------------------------------------------
// stringrw.h -- System.IO.StringReader / StringWriter runtime icalls
// (in-memory character-level text I/O).
//
// This header declares the runtime icall surface for text readers/writers
// that work directly on managed strings -- no file, no byte stream. It
// complements streamio.h (FileStream / MemoryStream): the string layer is
// the character counterpart of that stream layer and follows the same
// conventions.
//
// Handle model (mirrors streamio.h):
//   * An opaque handle (il2cpp_stringreader_t / il2cpp_stringwriter_t) is
//     obtained from il2cpp_stringreader_create / il2cpp_stringwriter_create
//     and passed to every other function. The handle is owned by the caller
//     and MUST be released with the matching close exactly once -- after
//     close the handle is dangling and must not be used again.
//   * A reader wraps the managed string passed to create (no copy) plus a
//     position cursor over UTF-16 code units -- .NET's char semantics, so
//     astral characters (surrogate pairs) are read as TWO units. The caller
//     must keep the wrapped string reachable for the reader's lifetime (GC
//     object; see il2cpp_string.h).
//   * A writer owns a growable UTF-16 buffer; get_string hands out a COPY as
//     a new managed string and does not consume the buffer, so it can be
//     called repeatedly (matching StringWriter.ToString).
//
// Text conventions (aligned with streamio.h):
//   * Positions and lengths count UTF-16 code units, NOT bytes.
//   * read_line terminates on CR, LF, or CRLF (net48 StringReader.ReadLine:
//     a CRLF pair is consumed as ONE terminator; a lone CR terminates the
//     line). End-of-string without a line start returns nullptr; an empty
//     line returns an empty string.
//   * write_line appends CRLF (the .NET Framework default on Windows,
//     aligned with streamio.cpp's write_line).
//
// Result conventions (M3 runtime: failures are return values, never C++
// throws -- aligned with streamio.h):
//   * Create functions return nullptr on failure (null input text,
//     allocation failure).
//   * Character functions (read / peek) return the UTF-16 unit value
//     (0..0xFFFF) or -1 at end-of-string (and for null handles).
//   * Status functions (write / write_line / close) return 0 on success and
//     non-zero on failure (null handle). Write with a null text is a no-op
//     success, matching .NET StringWriter semantics.
//   * String-returning functions (read_line / read_to_end / get_string)
//     return a managed string, or nullptr for a null handle. read_to_end
//     always returns a string (possibly empty) -- never nullptr -- when the
//     handle is valid.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)
#include "il2cpp_array.h"    // Il2CppArray (the char[] buffer of the batch-150e Read/Write array overloads)

#include <cstdint>

// System.IO.StringReader / System.IO.StringWriter runtime descriptors
// (GLOBAL scope -- the transpiler emits &System_IO_StringReader_class /
// &System_IO_StringWriter_class for newobj/isinst/castclass, the same
// contract as System_Version_class / System_String_class; defined in
// stringrw.cpp, registered lazily through il2cpp_stringreader_class() /
// il2cpp_stringwriter_class()).
extern il2cpp::Il2CppClass System_IO_StringReader_class;
extern il2cpp::Il2CppClass System_IO_StringWriter_class;

namespace il2cpp { namespace runtime {

// Opaque StringReader / StringWriter backends (defined in stringrw.cpp;
// never dereferenced by callers). The handles are owned by the caller and
// freed by close.
struct StringReaderState;
struct StringWriterState;

typedef StringReaderState* il2cpp_stringreader_t;
typedef StringWriterState* il2cpp_stringwriter_t;

}} // namespace il2cpp::runtime

namespace il2cpp {

// Managed System.IO.StringReader layout: object header + the wrapped managed
// string (a GC reference -- refFieldBitmap bit 1, keeps the text reachable
// for the reader's whole lifetime) + the opaque backend handle (a raw C++
// pointer, never a managed reference). 'state' is null between allocation
// and the ctor icall, and after Close.
struct Il2CppStringReaderObject
{
    Il2CppObject obj;                        // object header (klass pointer)
    Il2CppObject* text;                      // wrapped managed string (GC ref)
    runtime::StringReaderState* state;       // opaque handle; null = closed/uninitialized
};

// Managed System.IO.StringWriter layout: object header + the opaque backend
// handle (the growable buffer lives C++-side). No reference fields
// (refFieldBitmap 0 -- the collector never scans past the header).
struct Il2CppStringWriterObject
{
    Il2CppObject obj;                        // object header (klass pointer)
    runtime::StringWriterState* state;       // opaque handle; null = closed/uninitialized
};

// The StringReader / StringWriter descriptors (lazily registered on first
// use; generated code allocates through &System_IO_*_class directly).
Il2CppClass* il2cpp_stringreader_class();
Il2CppClass* il2cpp_stringwriter_class();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// StringReader.
//------------------------------------------------------------------------------

// StringReader(text): a reader over 'text' (no copy -- the caller must keep
// the managed string reachable for the reader's lifetime). The cursor starts
// at the first unit. Returns nullptr for a null input string.
il2cpp_stringreader_t il2cpp_stringreader_create(Il2CppObject* text);

// StringReader.Read(): the next UTF-16 unit as an int32 (0..0xFFFF), or -1
// at end-of-string / for a null handle. Astral characters are read as two
// units (surrogate pair), matching .NET char semantics.
int32_t il2cpp_stringreader_read(il2cpp_stringreader_t reader);

// StringReader.Peek(): the next UTF-16 unit WITHOUT consuming it, or -1 at
// end-of-string / for a null handle. Peek does not move the cursor.
int32_t il2cpp_stringreader_peek(il2cpp_stringreader_t reader);

// StringReader.ReadLine(): the next line as a managed string with the
// terminator stripped (CR, LF, or CRLF -- a CRLF pair is consumed as one
// terminator; a lone CR terminates the line, net48). nullptr at
// end-of-string without a line start; an empty line returns an empty string.
Il2CppObject* il2cpp_stringreader_read_line(il2cpp_stringreader_t reader);

// StringReader.ReadToEnd(): everything from the current cursor to the end as
// a managed string; the cursor moves to the end. Returns an empty string
// (not nullptr) at end-of-string, nullptr only for a null handle.
Il2CppObject* il2cpp_stringreader_read_to_end(il2cpp_stringreader_t reader);

// StringReader.Close(): releases the handle (the wrapped string is NOT
// released -- the caller keeps its own reference). The handle is dangling
// afterwards and must not be used again. 0 on success, non-zero for a null
// handle.
int32_t il2cpp_stringreader_close(il2cpp_stringreader_t reader);

//------------------------------------------------------------------------------
// StringWriter.
//------------------------------------------------------------------------------

// StringWriter(): an empty growable UTF-16 buffer. Returns nullptr on
// allocation failure.
il2cpp_stringwriter_t il2cpp_stringwriter_create(void);

// StringWriter.Write(text): appends 'text' to the buffer. A null text is a
// no-op success (matching .NET StringWriter.Write(null)). 0 on success,
// -1 for a null handle.
int32_t il2cpp_stringwriter_write(il2cpp_stringwriter_t writer, Il2CppObject* text);

// StringWriter.WriteLine(text): appends 'text' followed by CRLF. A null
// text writes just the CRLF. 0 on success, -1 for a null handle.
int32_t il2cpp_stringwriter_write_line(il2cpp_stringwriter_t writer, Il2CppObject* text);

// StringWriter.ToString(): a managed string COPY of the buffer content
// (UTF-16, unit for unit). The buffer is not consumed, so repeated calls
// return the same content. nullptr for a null handle.
Il2CppObject* il2cpp_stringwriter_get_string(il2cpp_stringwriter_t writer);

// StringWriter.Close(): releases the handle. The handle is dangling
// afterwards and must not be used again. 0 on success, non-zero for a null
// handle.
int32_t il2cpp_stringwriter_close(il2cpp_stringwriter_t writer);

//------------------------------------------------------------------------------
// StringReader / StringWriter OBJECT icalls (M6 object closure: the managed
// objects above own the backend handle; every icall takes the receiver as
// Il2CppObject* self and delegates to the handle functions -- see the OBJECT
// MODEL in stringrw.h's header comment for the shape of the objects).
//
// Object-state rules:
//   * A null receiver throws NullReferenceException (the generated code
//     null-checks first; mirrors versionuri.cpp / arraylist.cpp).
//   * StringReader ctor with a null text throws ArgumentNullException (net48
//     baseline -- the ctor THROWS, it does not return a status).
//   * After Close the object's handle is null: read/peek return -1,
//     read_line/read_to_end/to_string return nullptr, write/write_line
//     return -1, and Close is an idempotent no-op returning 0. net48 throws
//     ObjectDisposedException on use-after-close; this runtime follows the
//     M3 result conventions instead (failures are return values) -- a
//     documented divergence, consistent with the handle layer.
//------------------------------------------------------------------------------

// StringReader(string): wraps 'text' (no copy -- the OBJECT holds the
// managed string, so it stays reachable for the reader's lifetime).
// Null text -> ArgumentNullException.
void icall_stringreader_ctor(Il2CppObject* self, Il2CppObject* text);

// StringReader.Read(): the next UTF-16 unit as an int32 (0..0xFFFF), or -1
// at end-of-string / after close. Astral characters read as two units.
int32_t icall_stringreader_read(Il2CppObject* self);

// StringReader.Read(char[] buffer, int offset, int count) -- batch 150e:
// reads up to 'count' units into the buffer starting at 'offset'; returns
// the number read (0 at end-of-string / after close). Bounds as .NET.
int32_t icall_stringreader_read_chars(Il2CppObject* self, Il2CppArray* buffer,
                                      int32_t offset, int32_t count);

// StringReader.Peek(): the next UTF-16 unit WITHOUT consuming it, or -1 at
// end-of-string / after close.
int32_t icall_stringreader_peek(Il2CppObject* self);

// StringReader.ReadLine(): the next line as a managed string (terminator
// stripped; LF / CRLF), nullptr at end-of-string / after close.
Il2CppObject* icall_stringreader_read_line(Il2CppObject* self);

// StringReader.ReadToEnd(): the remainder as a managed string ("" at
// end-of-string), nullptr after close.
Il2CppObject* icall_stringreader_read_to_end(Il2CppObject* self);

// StringReader.Close(): releases the backend handle; the object's text
// reference is dropped. Idempotent: a second Close returns 0. 0 on success.
int32_t icall_stringreader_close(Il2CppObject* self);

// StringWriter(): an empty growable UTF-16 buffer.
void icall_stringwriter_ctor(Il2CppObject* self);

// StringWriter.Write(string): appends 'text' (null is a no-op, matching
// .NET StringWriter.Write(null)). 0 on success, -1 after close.
int32_t icall_stringwriter_write(Il2CppObject* self, Il2CppObject* text);

// StringWriter.Write(char) -- batch 150e: appends the single unit.
void icall_stringwriter_write_c(Il2CppObject* self, char16_t value);

// StringWriter.Write(char[]) -- batch 150e: appends the array (null is a
// no-op, matching .NET).
void icall_stringwriter_write_cs(Il2CppObject* self, Il2CppArray* chars);

// StringWriter.WriteLine(string): appends 'text' + CRLF (null writes just
// the CRLF). 0 on success, -1 after close.
int32_t icall_stringwriter_write_line(Il2CppObject* self, Il2CppObject* text);

// StringWriter.ToString(): a managed string COPY of the buffer (repeatable;
// the buffer is not consumed), nullptr after close.
Il2CppObject* icall_stringwriter_to_string(Il2CppObject* self);

// StringWriter.Close(): releases the backend handle. Idempotent: a second
// Close returns 0. 0 on success.
int32_t icall_stringwriter_close(Il2CppObject* self);

}} // namespace il2cpp::runtime
