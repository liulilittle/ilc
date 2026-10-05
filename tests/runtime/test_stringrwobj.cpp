//------------------------------------------------------------------------------
// test_stringrwobj.cpp -- System.IO.StringReader / StringWriter OBJECT icall
// tests (M6 object closure: System_IO_StringReader_class /
// System_IO_StringWriter_class descriptors + icall_stringreader_* /
// icall_stringwriter_* object icalls; see stringrw.h).
//
// The handle-layer semantics (test_stringrw.cpp) carry over unchanged -- the
// object icalls delegate to the handle functions; this fixture pins the
// OBJECT surface: ctor/descriptor/close-state/null-receiver/bitmap rules.
//
// Coverage (normal + edge + error paths):
//   1. Descriptor identity: klass stamp, isinst walks the baseClass chain,
//      class_by_name resolves, instanceSize == sizeof(object struct),
//      refFieldBitmap bit 1 = text (the wrapped string stays GC-reachable).
//   2. Reader ctor + Read/Peek flow (consume + EOF -1).
//   3. ReadLine (LF / CRLF / EOF null) + ReadToEnd remainder + "" at EOF.
//   4. Writer ctor + Write/WriteLine/ToString round-trip (repeatable).
//   5. Writer -> reader round-trip through the object surface.
//   6. Close semantics: idempotent double-close (0), use-after-close returns
//      -1/nullptr (M3 result conventions -- documented divergence from
//      net48's ObjectDisposedException).
//   7. Error paths: ctor null text -> ArgumentNullException; null receivers
//      -> NullReferenceException.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "stringrw.h"

#include "il2cpp_exception.h"  // DotNetException carrier
#include "il2cpp_object.h"     // Il2CppObject
#include "il2cpp_runtime.h"    // il2cpp_object_new / il2cpp_class_by_name /
                               // il2cpp_is_instance
#include "il2cpp_string.h"     // il2cpp_string_new_from_cs

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

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

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedChain'.
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedChain)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

const char* kArgNullExceptionChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char* kNullRefChain =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

// Convert a managed string to ASCII text for comparison (the probes below
// are pure ASCII; non-ASCII round-trips are covered by test_stringrw.cpp).
std::string ManagedText(il2cpp::Il2CppObject* s)
{
    if (s == nullptr)
    {
        return "<null>";
    }
    const auto* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    std::string text;
    for (int32_t i = 0; i < str->length; ++i)
    {
        text.push_back(static_cast<char>(str->chars[i]));
    }
    return text;
}

il2cpp::Il2CppObject* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::il2cpp_string_new_from_cs(ascii));
}

il2cpp::Il2CppObject* NewReader()
{
    return il2cpp::il2cpp_object_new(&System_IO_StringReader_class);
}

il2cpp::Il2CppObject* NewWriter()
{
    return il2cpp::il2cpp_object_new(&System_IO_StringWriter_class);
}

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- 1. descriptor identity + layout ---
    Expect(il2cpp_stringreader_class() == &System_IO_StringReader_class,
           "il2cpp_stringreader_class() returns &System_IO_StringReader_class");
    Expect(il2cpp_stringwriter_class() == &System_IO_StringWriter_class,
           "il2cpp_stringwriter_class() returns &System_IO_StringWriter_class");
    Expect(System_IO_StringReader_class.instanceSize == sizeof(Il2CppStringReaderObject),
           "reader instanceSize == sizeof(Il2CppStringReaderObject)");
    Expect(System_IO_StringWriter_class.instanceSize == sizeof(Il2CppStringWriterObject),
           "writer instanceSize == sizeof(Il2CppStringWriterObject)");
    Expect(System_IO_StringReader_class.refFieldBitmap == 0b00010u,
           "reader refFieldBitmap bit 1 = the wrapped text (GC ref)");
    Expect(System_IO_StringWriter_class.refFieldBitmap == 0u,
           "writer refFieldBitmap 0 (buffer lives C++-side)");
    Expect(il2cpp_class_by_name("System.IO.StringReader") == &System_IO_StringReader_class,
           "System.IO.StringReader resolves by name (registered)");
    Expect(il2cpp_class_by_name("System.IO.StringWriter") == &System_IO_StringWriter_class,
           "System.IO.StringWriter resolves by name (registered)");

    // --- 2. reader ctor + Read/Peek flow ---
    Il2CppObject* r = NewReader();
    Expect(r->klass == &System_IO_StringReader_class, "reader klass stamped");
    Expect(il2cpp_is_instance(r, &System_Object_class),
           "a StringReader IS-A System.Object (baseClass chain)");
    icall_stringreader_ctor(r, Cs("ab"));
    Expect(icall_stringreader_read(r) == static_cast<int32_t>(u'a'),
           "read returns the first unit");
    Expect(icall_stringreader_peek(r) == static_cast<int32_t>(u'b'),
           "peek returns the next unit without consuming");
    Expect(icall_stringreader_read(r) == static_cast<int32_t>(u'b'),
           "read consumes the peeked unit");
    Expect(icall_stringreader_peek(r) == -1, "peek at end-of-string returns -1");
    Expect(icall_stringreader_read(r) == -1, "read at end-of-string returns -1");

    // --- 3. ReadLine (LF / CRLF / EOF) + ReadToEnd remainder ---
    Il2CppObject* r2 = NewReader();
    icall_stringreader_ctor(r2, Cs("one\ntwo\r\nthree"));
    Expect(ManagedText(icall_stringreader_read_line(r2)) == "one",
           "read_line returns the first line (LF)");
    Expect(ManagedText(icall_stringreader_read_line(r2)) == "two",
           "read_line strips CRLF");
    Expect(ManagedText(icall_stringreader_read_line(r2)) == "three",
           "read_line returns the last line");
    Expect(icall_stringreader_read_line(r2) == nullptr,
           "read_line at end-of-string returns null");

    Il2CppObject* r3 = NewReader();
    icall_stringreader_ctor(r3, Cs("hello"));
    Expect(icall_stringreader_read(r3) == static_cast<int32_t>(u'h'),
           "read_to_end fixture: consume the first unit");
    Expect(ManagedText(icall_stringreader_read_to_end(r3)) == "ello",
           "read_to_end returns the remainder");
    Expect(ManagedText(icall_stringreader_read_to_end(r3)) == "",
           "read_to_end at end-of-string returns \"\" (not null)");

    // --- 4. writer ctor + Write/WriteLine/ToString round-trip ---
    Il2CppObject* w = NewWriter();
    Expect(w->klass == &System_IO_StringWriter_class, "writer klass stamped");
    Expect(il2cpp_is_instance(w, &System_Object_class),
           "a StringWriter IS-A System.Object (baseClass chain)");
    icall_stringwriter_ctor(w);
    Expect(icall_stringwriter_write(w, Cs("ab")) == 0, "write(\"ab\") returns 0");
    Expect(icall_stringwriter_write_line(w, Cs("cd")) == 0, "write_line(\"cd\") returns 0");
    Expect(icall_stringwriter_write(w, Cs("ef")) == 0, "write(\"ef\") returns 0");
    Expect(ManagedText(icall_stringwriter_to_string(w)) == "abcd\r\nef",
           "ToString joins the writes with CRLF");
    Expect(ManagedText(icall_stringwriter_to_string(w)) == "abcd\r\nef",
           "ToString is repeatable and does not consume the buffer");
    Expect(icall_stringwriter_write(w, nullptr) == 0, "write(null) is a no-op success");

    // --- 5. writer -> reader round-trip through the object surface ---
    Il2CppObject* r4 = NewReader();
    icall_stringreader_ctor(r4, icall_stringwriter_to_string(w));
    Expect(ManagedText(icall_stringreader_read_line(r4)) == "abcd",
           "reader consumes the writer's first line");
    Expect(ManagedText(icall_stringreader_read_line(r4)) == "ef",
           "reader consumes the writer's second line");

    // --- 6. close semantics: idempotent + use-after-close ---
    Expect(icall_stringreader_close(r) == 0, "reader close returns 0");
    Expect(icall_stringreader_close(r) == 0, "reader double close is a no-op");
    Expect(icall_stringreader_read(r) == -1, "read after close returns -1");
    Expect(icall_stringreader_peek(r) == -1, "peek after close returns -1");
    Expect(icall_stringreader_read_line(r) == nullptr, "read_line after close returns null");
    Expect(icall_stringreader_read_to_end(r) == nullptr, "read_to_end after close returns null");
    Expect(icall_stringwriter_close(w) == 0, "writer close returns 0");
    Expect(icall_stringwriter_close(w) == 0, "writer double close is a no-op");
    Expect(icall_stringwriter_write(w, Cs("x")) == -1, "write after close returns -1");
    Expect(icall_stringwriter_to_string(w) == nullptr, "ToString after close returns null");

    // --- 7. error paths ---
    Expect(ThrowsDotNet([&] { icall_stringreader_ctor(NewReader(), nullptr); }, kArgNullExceptionChain),
           "StringReader ctor(null text) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_stringreader_read(nullptr); }, kNullRefChain),
           "null receiver read throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringreader_peek(nullptr); }, kNullRefChain),
           "null receiver peek throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringreader_read_line(nullptr); }, kNullRefChain),
           "null receiver read_line throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringreader_close(nullptr); }, kNullRefChain),
           "null receiver close throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringwriter_ctor(nullptr); }, kNullRefChain),
           "null receiver writer ctor throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringwriter_write(nullptr, Cs("x")); }, kNullRefChain),
           "null receiver write throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringwriter_to_string(nullptr); }, kNullRefChain),
           "null receiver ToString throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_stringwriter_close(nullptr); }, kNullRefChain),
           "null receiver writer close throws NullReferenceException");

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
    }
    return g_failures == 0 ? 0 : 1;
}
