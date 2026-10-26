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
#include "il2cpp_stringbuilder.h" // the backing StringBuilder icalls
#include "il2cpp_format.h"        // the numeric ToString kernels
#include "cultureinfo.h"          // il2cpp_culture_get_current
#include "il2cpp_encoding.h"      // icall_encoding_get_Unicode (Encoding getter)
#include "il2cpp_gc.h"            // il2cpp_write_barrier
#include "il2cpp_value_semantics.h" // icall_object_to_string (Write/WriteLine(object))
#include "il2cpp_task.h"          // icall_task_from_result_* / icall_task_delay

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
    0b00001110u,                                       // refFieldBitmap: bits 1-3 (builder/provider/newline)
    3,                                                 // refFieldCount
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
    // The content store is a managed StringBuilder (net48 StringWriter
    // internally owns one). The writer OBJECT also holds this pointer in a
    // GC-scanned reference field, so the builder stays reachable even though
    // this raw state pointer is invisible to the collector.
    Il2CppStringBuilder* builder;
};

namespace {

// Create a fresh default StringBuilder (capacity 16) through the managed
// allocator + ctor icall.
Il2CppStringBuilder* NewStringBuilder()
{
    Il2CppObject* obj = il2cpp_object_new(&System_Text_StringBuilder_class);
    icall_stringbuilder_ctor_default(obj);
    return reinterpret_cast<Il2CppStringBuilder*>(obj);
}

// The managed StringBuilder* as an Il2CppObject* (the icall ABI).
Il2CppObject* AsObject(Il2CppStringBuilder* builder)
{
    return reinterpret_cast<Il2CppObject*>(builder);
}

} // namespace

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
    w->builder = NewStringBuilder();
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
    icall_stringbuilder_append_s(AsObject(w->builder), text);
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
        icall_stringbuilder_append_s(AsObject(w->builder), text);
    }
    // CRLF: the .NET Framework default newline on Windows, aligned with
    // streamio.cpp's write_line (the object icalls use the NewLine field).
    Il2CppString* newline = il2cpp_string_new(u"\r\n", 2);
    icall_stringbuilder_append_s(AsObject(w->builder),
                                 reinterpret_cast<Il2CppObject*>(newline));
    return 0;
}

Il2CppObject* il2cpp_stringwriter_get_string(il2cpp_stringwriter_t writer)
{
    StringWriterState* w = writer;
    if (w == nullptr)
    {
        return nullptr;
    }
    return icall_stringbuilder_to_string(AsObject(w->builder));
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

int32_t icall_stringreader_read_block(Il2CppObject* self, Il2CppArray* buffer,
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

Il2CppObject* icall_stringreader_read_to_end_async(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_task_from_result_s(icall_stringreader_read_to_end(self));
}

Il2CppObject* icall_stringreader_read_line_async(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_task_from_result_s(icall_stringreader_read_line(self));
}

Il2CppObject* icall_stringreader_read_chars_async(Il2CppObject* self, Il2CppArray* buffer,
                                                  int32_t offset, int32_t count)
{
    ThrowIfNullReceiver(self);
    return icall_task_from_result(
        icall_stringreader_read_chars(self, buffer, offset, count));
}

Il2CppObject* icall_stringreader_read_block_async(Il2CppObject* self, Il2CppArray* buffer,
                                                  int32_t offset, int32_t count)
{
    ThrowIfNullReceiver(self);
    return icall_task_from_result(
        icall_stringreader_read_block(self, buffer, offset, count));
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

namespace {

// The backing builder of an initialized writer (null after close).
Il2CppStringBuilder* WriterBuilder(Il2CppObject* self)
{
    StringWriterState* state = AsWriter(self)->state;
    return state != nullptr ? state->builder : nullptr;
}

void WriterAppendText(Il2CppObject* self, Il2CppString* text)
{
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr && text != nullptr)
    {
        icall_stringbuilder_append_s(AsObject(builder),
                                     reinterpret_cast<Il2CppObject*>(text));
    }
}

void WriterAppendNewLine(Il2CppObject* self)
{
    Il2CppStringBuilder* builder = WriterBuilder(self);
    Il2CppObject* newline = AsWriter(self)->newline;
    if (builder != nullptr && newline != nullptr)
    {
        icall_stringbuilder_append_s(AsObject(builder), newline);
    }
}

// Initialize a writer with a builder/provider (the shared ctor core).
void InitWriter(Il2CppObject* self, Il2CppObject* builder, Il2CppObject* provider)
{
    Il2CppStringWriterObject* w = AsWriter(self);
    Il2CppString* newline = il2cpp_string_new(u"\r\n", 2);
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->builder), builder);
    w->builder = builder;
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->provider), provider);
    w->provider = provider;
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->newline),
                         reinterpret_cast<Il2CppObject*>(newline));
    w->newline = reinterpret_cast<Il2CppObject*>(newline);
    StringWriterState* state = new StringWriterState;
    state->builder = reinterpret_cast<Il2CppStringBuilder*>(builder);
    w->state = state;
}

// The WriteLine core: append the formatted text (if any) then the NewLine.
int32_t WriteLineText(Il2CppObject* self, Il2CppString* text)
{
    WriterAppendText(self, text);
    WriterAppendNewLine(self);
    return 0;
}

} // namespace

void icall_stringwriter_ctor(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    InitWriter(self, reinterpret_cast<Il2CppObject*>(NewStringBuilder()), nullptr);
}

void icall_stringwriter_ctor_provider(Il2CppObject* self, Il2CppObject* provider)
{
    ThrowIfNullReceiver(self);
    InitWriter(self, reinterpret_cast<Il2CppObject*>(NewStringBuilder()), provider);
}

void icall_stringwriter_ctor_sb(Il2CppObject* self, Il2CppObject* builder)
{
    ThrowIfNullReceiver(self);
    if (builder == nullptr)
    {
        il2cpp_throw_argument_null_exception("sb");
    }
    InitWriter(self, builder, nullptr);
}

void icall_stringwriter_ctor_sb_provider(Il2CppObject* self, Il2CppObject* builder,
                                         Il2CppObject* provider)
{
    ThrowIfNullReceiver(self);
    if (builder == nullptr)
    {
        il2cpp_throw_argument_null_exception("sb");
    }
    InitWriter(self, builder, provider);
}

Il2CppObject* icall_stringwriter_get_string_builder(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(WriterBuilder(self));
}

Il2CppObject* icall_stringwriter_get_encoding(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_encoding_get_Unicode();
}

Il2CppObject* icall_stringwriter_get_format_provider(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppObject* provider = AsWriter(self)->provider;
    if (provider != nullptr)
    {
        return provider;
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_culture_get_current());
}

Il2CppObject* icall_stringwriter_get_newline(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsWriter(self)->newline;
}

void icall_stringwriter_set_newline(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    Il2CppStringWriterObject* w = AsWriter(self);
    il2cpp_write_barrier(reinterpret_cast<void**>(&w->newline), value);
    w->newline = value;
}

int32_t icall_stringwriter_write(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    WriterAppendText(self, reinterpret_cast<Il2CppString*>(text));
    return 0;
}

//------------------------------------------------------------------------------
// icall_stringwriter_write_c -- StringWriter.Write(char) (batch 150e).
//------------------------------------------------------------------------------
void icall_stringwriter_write_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr)
    {
        icall_stringbuilder_append_c(AsObject(builder), value);
    }
}

//------------------------------------------------------------------------------
// icall_stringwriter_write_cs -- StringWriter.Write(char[]) (batch 150e);
// null is a no-op (matching .NET).
//------------------------------------------------------------------------------
void icall_stringwriter_write_cs(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr && chars != nullptr)
    {
        icall_stringbuilder_append_c_array(AsObject(builder), chars);
    }
}

int32_t icall_stringwriter_write_i(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_i(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_u4(Il2CppObject* self, uint32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_u4(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_l(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_l(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_u8(Il2CppObject* self, uint64_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_u8(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_d(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_d(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_b(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_b(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_o(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr) icall_stringbuilder_append_o(AsObject(builder), value);
    return 0;
}

int32_t icall_stringwriter_write_cs_range(Il2CppObject* self, Il2CppArray* chars,
                                          int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr)
    {
        icall_stringbuilder_append_c_array_range(AsObject(builder), chars, index, count);
    }
    return 0;
}

int32_t icall_stringwriter_write_s_range(Il2CppObject* self, Il2CppObject* value,
                                         int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr)
    {
        icall_stringbuilder_append_s_range(AsObject(builder), value, index, count);
    }
    return 0;
}

int32_t icall_stringwriter_write_format_1(Il2CppObject* self, Il2CppObject* format,
                                          Il2CppObject* arg0)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_1(format, arg0));
    WriterAppendText(self, text);
    return 0;
}

int32_t icall_stringwriter_write_format_2(Il2CppObject* self, Il2CppObject* format,
                                          Il2CppObject* arg0, Il2CppObject* arg1)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_2(format, arg0, arg1));
    WriterAppendText(self, text);
    return 0;
}

int32_t icall_stringwriter_write_format(Il2CppObject* self, Il2CppObject* format,
                                        Il2CppObject* argsArray)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format(format, argsArray));
    WriterAppendText(self, text);
    return 0;
}

int32_t icall_stringwriter_write_format_3(Il2CppObject* self, Il2CppObject* format,
                                          Il2CppObject* arg0, Il2CppObject* arg1,
                                          Il2CppObject* arg2)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_3(format, arg0, arg1, arg2));
    WriterAppendText(self, text);
    return 0;
}

int32_t icall_stringwriter_write_line(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    return WriteLineText(self, reinterpret_cast<Il2CppString*>(text));
}

int32_t icall_stringwriter_write_line_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    WriterAppendNewLine(self);
    return 0;
}

int32_t icall_stringwriter_write_line_i(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_int32_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_u4(Il2CppObject* self, uint32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_uint32_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_l(Il2CppObject* self, int64_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_int64_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_u8(Il2CppObject* self, uint64_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_uint64_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_d(Il2CppObject* self, double value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_double_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_b(Il2CppObject* self, int32_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_bool_to_string(value != 0);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_o(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    if (value == nullptr)
    {
        return WriteLineText(self, nullptr);
    }
    Il2CppString* text = icall_object_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = icall_char_to_string(value);
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_cs(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr && chars != nullptr)
    {
        icall_stringbuilder_append_c_array(AsObject(builder), chars);
    }
    WriterAppendNewLine(self);
    return 0;
}

int32_t icall_stringwriter_write_line_cs_range(Il2CppObject* self, Il2CppArray* chars,
                                               int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr)
    {
        icall_stringbuilder_append_c_array_range(AsObject(builder), chars, index, count);
    }
    WriterAppendNewLine(self);
    return 0;
}

int32_t icall_stringwriter_write_line_s_range(Il2CppObject* self, Il2CppObject* value,
                                              int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder != nullptr)
    {
        icall_stringbuilder_append_s_range(AsObject(builder), value, index, count);
    }
    WriterAppendNewLine(self);
    return 0;
}

int32_t icall_stringwriter_write_line_format_1(Il2CppObject* self, Il2CppObject* format,
                                               Il2CppObject* arg0)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_1(format, arg0));
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_format_2(Il2CppObject* self, Il2CppObject* format,
                                               Il2CppObject* arg0, Il2CppObject* arg1)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_2(format, arg0, arg1));
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_format(Il2CppObject* self, Il2CppObject* format,
                                             Il2CppObject* argsArray)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format(format, argsArray));
    return WriteLineText(self, text);
}

int32_t icall_stringwriter_write_line_format_3(Il2CppObject* self, Il2CppObject* format,
                                               Il2CppObject* arg0, Il2CppObject* arg1,
                                               Il2CppObject* arg2)
{
    ThrowIfNullReceiver(self);
    Il2CppString* text = reinterpret_cast<Il2CppString*>(
        icall_string_format_3(format, arg0, arg1, arg2));
    return WriteLineText(self, text);
}

Il2CppObject* icall_stringwriter_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStringBuilder* builder = WriterBuilder(self);
    if (builder == nullptr)
    {
        return nullptr;
    }
    return icall_stringbuilder_to_string(AsObject(builder));
}

void icall_stringwriter_flush(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    // In-memory writer: nothing buffered below the builder.
}

Il2CppObject* icall_stringwriter_write_async(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write(self, text);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_async_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_c(self, value);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_async_cs(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_cs(self, chars);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_async_cs_range(Il2CppObject* self, Il2CppArray* chars,
                                                      int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_cs_range(self, chars, index, count);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_line_async_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_line_default(self);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_line_async(Il2CppObject* self, Il2CppObject* text)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_line(self, text);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_line_async_c(Il2CppObject* self, char16_t value)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_line_c(self, value);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_line_async_cs(Il2CppObject* self, Il2CppArray* chars)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_line_cs(self, chars);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_write_line_async_cs_range(Il2CppObject* self, Il2CppArray* chars,
                                                           int32_t index, int32_t count)
{
    ThrowIfNullReceiver(self);
    icall_stringwriter_write_line_cs_range(self, chars, index, count);
    return icall_task_delay(0);
}

Il2CppObject* icall_stringwriter_flush_async(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return icall_task_delay(0);
}

int32_t icall_stringwriter_close(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    Il2CppStringWriterObject* w = AsWriter(self);
    if (w->state == nullptr)
    {
        return 0; // idempotent: a second close is a no-op
    }
    delete w->state;
    w->state = nullptr;
    return 0;
}

}} // namespace il2cpp::runtime
