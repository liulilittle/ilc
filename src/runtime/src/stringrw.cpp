//------------------------------------------------------------------------------
// stringrw.cpp -- System.IO.StringReader / StringWriter icall implementations
// (see stringrw.h for the contract and result conventions).
//
// Backends:
//   * A reader holds the managed string it was created with (a plain
//     reference -- the caller keeps the GC object reachable) plus an int32
//     cursor over UTF-16 code units. Read / peek operate directly on
//     chars[cursor]; read_line scans for the next CR or LF (a CRLF pair is
//     one terminator, net48); read_to_end copies the
//     remaining units into a fresh managed string.
//   * A writer owns a std::vector<char16_t> buffer that grows on demand;
//     get_string builds a managed string copy via il2cpp_string_new. The
//     buffer is never consumed, so get_string is repeatable.
//
// There is no UTF-8 involvement anywhere in this domain: reader and writer
// exchange UTF-16 units directly, so non-ASCII content round-trips unit for
// unit by construction.
//------------------------------------------------------------------------------

#include "stringrw.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_* / null_reference
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register /
                                  // il2cpp_is_instance
#include "il2cpp_string.h"

#include <cstdint>
#include <vector>

//------------------------------------------------------------------------------
// System.IO.StringReader / System.IO.StringWriter runtime descriptors
// (GLOBAL scope -- the transpiler emits &System_IO_StringReader_class /
// &System_IO_StringWriter_class for newobj/isinst/castclass, the same
// contract as System_Version_class in versionuri.cpp).
//   * Reader: instanceSize 24 (header + text slot + state slot). refField
//     bitmap bit 1: 'text' is a managed reference the collector scans,
//     keeping the wrapped string reachable for the reader's whole lifetime;
//     'state' (slot 2) is a raw C++ pointer, never in the bitmap.
//   * Writer: instanceSize 16 (header + state slot), no reference fields
//     (the buffer lives C++-side).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_IO_StringReader_class = {
    "System.IO.StringReader",                          // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStringReaderObject)), // instanceSize
    0b00010u,                                          // refFieldBitmap: bit 1 = text
    1,                                                 // refFieldCount
    &System_Object_class,                              // baseClass
    nullptr,                                           // vtable
    0,                                                 // vtableSize
    nullptr,                                           // interfaces
    0,                                                 // interfaceCount
};

il2cpp::Il2CppClass System_IO_StringWriter_class = {
    "System.IO.StringWriter",                          // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStringWriterObject)), // instanceSize
    0u,                                                // refFieldBitmap: no ref fields
    0,                                                 // refFieldCount
    &System_Object_class,                              // baseClass
    nullptr,                                           // vtable
    0,                                                 // vtableSize
    nullptr,                                           // interfaces
    0,                                                 // interfaceCount
};

namespace il2cpp {

namespace {

bool g_stringReaderClassRegistered = false;
bool g_stringWriterClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_stringreader_class / il2cpp_stringwriter_class -- the descriptors
// (lazily registered on first use; idempotent -- il2cpp_class_register
// dedupes by pointer; mirrors il2cpp_version_class).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_stringreader_class()
{
    if (!g_stringReaderClassRegistered)
    {
        il2cpp_class_register(&System_IO_StringReader_class);
        g_stringReaderClassRegistered = true;
    }
    return &System_IO_StringReader_class;
}

Il2CppClass* il2cpp_stringwriter_class()
{
    if (!g_stringWriterClassRegistered)
    {
        il2cpp_class_register(&System_IO_StringWriter_class);
        g_stringWriterClassRegistered = true;
    }
    return &System_IO_StringWriter_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// StringReader backend state (opaque to callers; see stringrw.h).
//------------------------------------------------------------------------------

struct StringReaderState
{
    Il2CppString* text; // the managed string under read (caller keeps it reachable)
    int32_t       pos;  // cursor: index of the next UTF-16 unit to read
};

//------------------------------------------------------------------------------
// StringWriter backend state (opaque to callers; see stringrw.h).
//------------------------------------------------------------------------------

struct StringWriterState
{
    std::vector<char16_t> units; // growable UTF-16 buffer (content written so far)
};

//------------------------------------------------------------------------------
// StringReader.
//------------------------------------------------------------------------------

il2cpp_stringreader_t il2cpp_stringreader_create(Il2CppObject* text)
{
    if (text == nullptr)
    {
        return nullptr;
    }
    StringReaderState* r = new StringReaderState;
    r->text = reinterpret_cast<Il2CppString*>(text);
    r->pos = 0;
    return r;
}

int32_t il2cpp_stringreader_read(il2cpp_stringreader_t reader)
{
    StringReaderState* r = reader;
    if (r == nullptr)
    {
        return -1;
    }
    if (r->pos >= r->text->length)
    {
        return -1; // end-of-string
    }
    return r->text->chars[r->pos++];
}

int32_t il2cpp_stringreader_peek(il2cpp_stringreader_t reader)
{
    StringReaderState* r = reader;
    if (r == nullptr)
    {
        return -1;
    }
    if (r->pos >= r->text->length)
    {
        return -1; // end-of-string
    }
    return r->text->chars[r->pos];
}

Il2CppObject* il2cpp_stringreader_read_line(il2cpp_stringreader_t reader)
{
    StringReaderState* r = reader;
    if (r == nullptr)
    {
        return nullptr;
    }
    const char16_t* chars = r->text->chars;
    const int32_t length = r->text->length;
    if (r->pos >= length)
    {
        return nullptr; // end-of-string without a line start
    }
    // Scan to the first line terminator. net48 StringReader.ReadLine treats
    // CR, LF and CRLF ALL as terminators (corert StringReader.cs:182-189): a
    // lone CR ends the line -- it is NOT content (net48 parity; the previous
    // LF-only scan treated a lone CR as content, diverging from net48).
    const int32_t start = r->pos;
    int32_t i = r->pos;
    while (i < length && chars[i] != u'\r' && chars[i] != u'\n')
    {
        ++i;
    }
    const int32_t end = i; // exclusive; points at the terminator (or the end)
    if (i < length)
    {
        r->pos = i + 1; // consume the terminator
        if (chars[i] == u'\r' && r->pos < length && chars[r->pos] == u'\n')
        {
            ++r->pos; // consume the LF of a CRLF pair as ONE terminator
        }
    }
    else
    {
        r->pos = i; // no terminator: consume the remainder
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(chars + start, end - start));
}

Il2CppObject* il2cpp_stringreader_read_to_end(il2cpp_stringreader_t reader)
{
    StringReaderState* r = reader;
    if (r == nullptr)
    {
        return nullptr;
    }
    const int32_t start = r->pos;
    r->pos = r->text->length; // consume everything
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(r->text->chars + start, r->text->length - start));
}

int32_t il2cpp_stringreader_close(il2cpp_stringreader_t reader)
{
    StringReaderState* r = reader;
    if (r == nullptr)
    {
        return 1;
    }
    delete r;
    return 0; // the handle is dangling after this call
}

//------------------------------------------------------------------------------
// StringWriter.
//------------------------------------------------------------------------------

il2cpp_stringwriter_t il2cpp_stringwriter_create(void)
{
    StringWriterState* w = new StringWriterState;
    return w; // nullptr on allocation failure (new throws only if exceptions on)
}

int32_t il2cpp_stringwriter_write(il2cpp_stringwriter_t writer, Il2CppObject* text)
{
    StringWriterState* w = writer;
    if (w == nullptr)
    {
        return -1;
    }
    if (text == nullptr)
    {
        return 0; // .NET StringWriter.Write(null) is a no-op
    }
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(text);
    w->units.insert(w->units.end(), s->chars, s->chars + s->length);
    return 0;
}

int32_t il2cpp_stringwriter_write_line(il2cpp_stringwriter_t writer, Il2CppObject* text)
{
    StringWriterState* w = writer;
    if (w == nullptr)
    {
        return -1;
    }
    if (text != nullptr)
    {
        const Il2CppString* s = reinterpret_cast<Il2CppString*>(text);
        w->units.insert(w->units.end(), s->chars, s->chars + s->length);
    }
    // CRLF: the .NET Framework default newline on Windows, aligned with
    // streamio.cpp's write_line.
    w->units.push_back(u'\r');
    w->units.push_back(u'\n');
    return 0;
}

Il2CppObject* il2cpp_stringwriter_get_string(il2cpp_stringwriter_t writer)
{
    StringWriterState* w = writer;
    if (w == nullptr)
    {
        return nullptr;
    }
    return reinterpret_cast<Il2CppObject*>(
        il2cpp_string_new(w->units.data(), static_cast<int32_t>(w->units.size())));
}

int32_t il2cpp_stringwriter_close(il2cpp_stringwriter_t writer)
{
    StringWriterState* w = writer;
    if (w == nullptr)
    {
        return 1;
    }
    delete w;
    return 0; // the handle is dangling after this call
}

//------------------------------------------------------------------------------
// StringReader / StringWriter OBJECT icalls (M6 object closure). The managed
// object owns the opaque backend handle (StringReaderState* / 
// StringWriterState*); every icall delegates to the handle layer above, so
// the semantics tested by test_stringrw.cpp carry over unchanged.
//------------------------------------------------------------------------------

namespace {

Il2CppStringReaderObject* AsReader(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppStringReaderObject*>(self);
}

Il2CppStringWriterObject* AsWriter(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppStringWriterObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors versionuri.cpp / arraylist.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

} // namespace

void icall_stringreader_ctor(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("s");
    }
    Il2CppStringReaderObject* r = AsReader(self);
    r->text = text; // GC-ref slot: keeps the wrapped string reachable
    r->state = il2cpp_stringreader_create(text);
}

int32_t icall_stringreader_read(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppStringReaderObject* r = AsReader(self);
    return il2cpp_stringreader_read(r->state);
}

//------------------------------------------------------------------------------
// icall_stringreader_read_chars -- StringReader.Read(char[], int, int)
// (batch 150e): reads up to 'count' units into the buffer at 'offset'
// (bounds as .NET: ArgumentNullException for a null buffer,
// ArgumentOutOfRangeException for offset/count); returns the units read (0
// at end-of-string / after close).
//------------------------------------------------------------------------------
int32_t icall_stringreader_read_chars(Il2CppObject* self, Il2CppArray* buffer,
                                      int32_t offset, int32_t count)
{
    ThrowIfNullReceiver(self);
    if (buffer == nullptr)
    {
        il2cpp_throw_argument_null_exception("buffer");
    }
    if (offset < 0 || count < 0 ||
        static_cast<int64_t>(offset) + static_cast<int64_t>(count) > buffer->length)
    {
        il2cpp_throw_argument_out_of_range_exception("offset or count", nullptr);
    }
    const Il2CppStringReaderObject* r = AsReader(self);
    char16_t* dst = reinterpret_cast<char16_t*>(buffer->data) + offset;
    int32_t got = 0;
    while (got < count)
    {
        const int32_t c = il2cpp_stringreader_read(r->state);
        if (c < 0)
        {
            break;
        }
        dst[got++] = static_cast<char16_t>(c);
    }
    return got;
}

int32_t icall_stringreader_peek(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppStringReaderObject* r = AsReader(self);
    return il2cpp_stringreader_peek(r->state);
}

Il2CppObject* icall_stringreader_read_line(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppStringReaderObject* r = AsReader(self);
    return il2cpp_stringreader_read_line(r->state);
}

Il2CppObject* icall_stringreader_read_to_end(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppStringReaderObject* r = AsReader(self);
    return il2cpp_stringreader_read_to_end(r->state);
}

int32_t icall_stringreader_close(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStringReaderObject* r = AsReader(self);
    if (r->state == nullptr)
    {
        return 0; // idempotent: a second close is a no-op
    }
    const int32_t status = il2cpp_stringreader_close(r->state);
    r->state = nullptr;
    r->text = nullptr; // drop the wrapped-string reference
    return status;
}

void icall_stringwriter_ctor(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    AsWriter(self)->state = il2cpp_stringwriter_create();
}

int32_t icall_stringwriter_write(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    return il2cpp_stringwriter_write(AsWriter(self)->state, text);
}

//------------------------------------------------------------------------------
// icall_stringwriter_write_c -- StringWriter.Write(char) (batch 150e).
//------------------------------------------------------------------------------
void icall_stringwriter_write_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    StringWriterState* w = AsWriter(self)->state;
    if (w == nullptr)
    {
        return; // closed writer: silent no-op (M3 conventions)
    }
    w->units.push_back(value);
}

//------------------------------------------------------------------------------
// icall_stringwriter_write_cs -- StringWriter.Write(char[]) (batch 150e);
// null is a no-op (matching .NET).
//------------------------------------------------------------------------------
void icall_stringwriter_write_cs(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    StringWriterState* w = AsWriter(self)->state;
    if (w == nullptr || chars == nullptr)
    {
        return;
    }
    const char16_t* src = reinterpret_cast<const char16_t*>(chars->data);
    w->units.insert(w->units.end(), src, src + chars->length);
}

int32_t icall_stringwriter_write_line(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    return il2cpp_stringwriter_write_line(AsWriter(self)->state, text);
}

Il2CppObject* icall_stringwriter_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return il2cpp_stringwriter_get_string(AsWriter(self)->state);
}

int32_t icall_stringwriter_close(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStringWriterObject* w = AsWriter(self);
    if (w->state == nullptr)
    {
        return 0; // idempotent: a second close is a no-op
    }
    const int32_t status = il2cpp_stringwriter_close(w->state);
    w->state = nullptr;
    return status;
}

}} // namespace il2cpp::runtime
