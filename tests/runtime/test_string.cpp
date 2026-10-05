//------------------------------------------------------------------------------
// test_string.cpp -- managed string runtime unit tests.
//
// Coverage (normal + edge + error paths):
//   1. il2cpp_string_new_from_cs creates a non-null managed string with the
//      correct length and payload for an ASCII literal.
//   2. The empty literal ("") yields a non-null string with length 0.
//   3. Mixed ASCII payloads (digits, uppercase, punctuation) round-trip.
//   4. The string payload is an independent copy (mutating the source buffer
//      after creation does not affect the string).
//   5. Strings are GC objects: a rooted string survives collection; an
//      unrooted string's memory is reclaimable (reuse check).
//   6. il2cpp_string_new (explicit UTF-16) matches the payload exactly.
//   7. il2cpp_string_get_length: non-null strings return the UTF-16 unit
//      count; a null receiver throws System.NullReferenceException (matching
//      .NET's null.Length).
//   8. Canonical descriptor: every string's klass IS the builtin
//      System_String_class (metadata.cpp) -- the pointer generated
//      isinst/castclass code compares against -- and
//      il2cpp_string_class() returns it (boxdeep rebox-chain contract).
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"

#include <cstdint>
#include <cstdio>
#include <cstring>

namespace {

static_assert(sizeof(void*) == 8, "string test fixture assumes 8-byte pointers");

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// UTF-16 helper: build a char16_t buffer from an ASCII literal.
void AsciiToUtf16(const char* ascii, char16_t* out, int32_t count)
{
    for (int32_t i = 0; i < count; ++i)
        out[i] = static_cast<char16_t>(static_cast<unsigned char>(ascii[i]));
}

// Run 'fn' expecting a managed exception whose type-name chain starts with
// the exact managed full name (prefix match -- the chain also lists the
// type's base classes).
template <typename Fn>
void ExpectThrows(Fn fn, const char* expectedType, const char* what)
{
    bool matched = false;
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        const char* thrown = e.type_name();
        matched = std::strncmp(thrown, expectedType, std::strlen(expectedType)) == 0
                  && (thrown[std::strlen(expectedType)] == ',' || thrown[std::strlen(expectedType)] == '\0');
    }
    Expect(matched, what);
}

} // namespace

int main()
{
    // --- 1. Basic ASCII literal creation ---
    il2cpp::Il2CppString* s1 = il2cpp::il2cpp_string_new_from_cs("hello");
    Expect(s1 != nullptr, "string creation returns a non-null object");
    Expect(s1->length == 5, "string length counts char16_t units (5 for \"hello\")");
    Expect(s1->chars[0] == u'h' && s1->chars[4] == u'o', "string payload matches the ASCII literal");

    // --- 2. Empty literal ---
    il2cpp::Il2CppString* s2 = il2cpp::il2cpp_string_new_from_cs("");
    Expect(s2 != nullptr, "empty literal yields a non-null string");
    Expect(s2->length == 0, "empty literal has length 0");

    // --- 3. Mixed ASCII payload ---
    il2cpp::Il2CppString* s3 = il2cpp::il2cpp_string_new_from_cs("A1!z9");
    Expect(s3 != nullptr && s3->length == 5, "mixed ASCII payload length is 5");
    if (s3 != nullptr)
    {
        Expect(s3->chars[0] == u'A' && s3->chars[1] == u'1' && s3->chars[2] == u'!'
               && s3->chars[3] == u'z' && s3->chars[4] == u'9',
               "mixed ASCII payload content matches");
    }

    // --- 4. Payload is an independent copy ---
    char buffer[8];
    std::memcpy(buffer, "copy", 5); // "copy" + NUL (MSVC /W4 C4996-safe: no strcpy)
    il2cpp::Il2CppString* s4 = il2cpp::il2cpp_string_new_from_cs(buffer);
    buffer[0] = 'X'; // mutate the source AFTER creation
    Expect(s4 != nullptr && s4->length == 4 && s4->chars[0] == u'c',
           "string payload is an independent copy of the source buffer");

    // --- 5. GC integration: rooted string survives collection ---
    il2cpp::Il2CppString* rooted = il2cpp::il2cpp_string_new_from_cs("keep");
    il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
    il2cpp::Il2CppString* junk = il2cpp::il2cpp_string_new_from_cs("junk");
    (void)junk;
    il2cpp::il2cpp_gc_collect();
    Expect(rooted != nullptr && rooted->length == 4 && rooted->chars[0] == u'k',
           "rooted string survives collection intact");
    // The collector may reclaim 'junk' (unrooted); a fresh allocation must
    // still work after collection (heap stays usable).
    il2cpp::Il2CppString* s5 = il2cpp::il2cpp_string_new_from_cs("new");
    Expect(s5 != nullptr && s5->length == 3 && s5->chars[0] == u'n',
           "string allocation works after collection");

    // --- 6. Explicit UTF-16 creation ---
    char16_t utf16[3];
    AsciiToUtf16("abc", utf16, 3);
    il2cpp::Il2CppString* s6 = il2cpp::il2cpp_string_new(utf16, 3);
    Expect(s6 != nullptr && s6->length == 3 && s6->chars[0] == u'a' && s6->chars[2] == u'c',
           "explicit UTF-16 creation matches the payload");

    // --- 7. il2cpp_string_get_length: non-null normal path + null NRE -------
    // Fresh allocations (NOT section-1 objects: those are unrooted locals the
    // section-5 collection may have reclaimed -- see the gc.h contract).
    il2cpp::Il2CppString* len5 = il2cpp::il2cpp_string_new_from_cs("hello");
    il2cpp::Il2CppString* len0 = il2cpp::il2cpp_string_new_from_cs("");
    Expect(il2cpp::il2cpp_string_get_length(reinterpret_cast<il2cpp::Il2CppObject*>(len5)) == 5,
           "get_length(\"hello\") returns 5 UTF-16 units");
    Expect(il2cpp::il2cpp_string_get_length(reinterpret_cast<il2cpp::Il2CppObject*>(len0)) == 0,
           "get_length(\"\") returns 0");
    ExpectThrows([] { (void)il2cpp::il2cpp_string_get_length(nullptr); },
                 "System.NullReferenceException",
                 "get_length(null) throws NullReferenceException (null.Length semantics)");

    // --- 8. CANONICAL DESCRIPTOR (boxdeep contract): strings MUST carry the
    // metadata.cpp builtin System_String_class descriptor (il2cpp_metadata.h:
    // the extern System_String_class -- generated isinst/castclass compares
    // against THAT pointer). A file-local duplicate descriptor would make
    // every `os is string` / `(string)os` miss (boxdeep rebox chain). ---
    il2cpp::Il2CppString* canon = il2cpp::il2cpp_string_new_from_cs("canon");
    Expect(canon != nullptr && canon->obj.klass == &System_String_class,
           "string klass IS the builtin System_String_class descriptor");
    Expect(il2cpp::il2cpp_string_class() == &System_String_class,
           "il2cpp_string_class() returns the canonical System_String_class");
    Expect(il2cpp::il2cpp_is_instance(reinterpret_cast<il2cpp::Il2CppObject*>(canon), &System_String_class),
           "isinst(string, System_String_class) hits (pointer identity)");
    Expect(il2cpp::il2cpp_is_instance(reinterpret_cast<il2cpp::Il2CppObject*>(canon), &System_Object_class),
           "isinst(string, System_Object_class) walks the base chain (s is object)");
    Expect(!il2cpp::il2cpp_is_instance(reinterpret_cast<il2cpp::Il2CppObject*>(canon), &System_Int32_class),
           "isinst(string, System_Int32) misses (wrong canonical descriptor)");
    il2cpp::Il2CppString* canon2 = il2cpp::il2cpp_string_new(nullptr, 0);
    Expect(canon2 != nullptr && canon2->obj.klass == &System_String_class,
           "explicit-UTF16 string also carries the canonical descriptor");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d string assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime string tests passed\n");
    return 0;
}
