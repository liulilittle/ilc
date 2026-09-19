//------------------------------------------------------------------------------
// test_xml_write.cpp -- XmlWriter minimal write surface unit tests (D-XMLW-1).
//
// Coverage of the xml_write.h surface (il2cpp_xml_writer_settings_new /
// set_* / il2cpp_xml_writer_create(_s) / the Write* members / Flush / Close),
// mirroring the net48 oracle byte-pinned by tests/cases/xmlwrite/expected.txt:
//
//   1. Default settings -> a SINGLE-LINE document WITH the xml declaration:
//      `<?xml version="1.0" encoding="utf-16"?><root id="1"><a>text &amp;
//      more</a></root>` (the exact expected.txt line -- the StringBuilder
//      target declares the UTF-16 encoding, net48 baseline).
//   2. OmitXmlDeclaration + Indent -> no declaration, CRLF + 2-space
//      indentation (`<r>\r\n  <x>v</x>\r\n</r>` shape; the probe's
//      StartsWith("<r>") and Contains("\r\n  <x>") invariants).
//   3. Entity escaping: attribute values escape & < > " (' stays literal),
//      text escapes & < >.
//   4. WriteElementString = start + text + end; WriteEndDocument closes every
//      remaining element; an empty element (start + end with no content)
//      renders `<e></e>`.
//   5. Flush appends accumulated output and clears the buffer (a second
//      Flush does not duplicate); Close after Flush appends only new output.
//   6. Settings setters: only the wired properties (OmitXmlDeclaration /
//      Indent) take effect; a null settings arg to Create behaves like the
//      defaults.
//   7. GC safety: the target StringBuilder survives a collection cycle while
//      the writer is open (the writer's root range keeps it alive and
//      compaction-correct), then reads back correctly after Close.
//   8. Null writer receiver -> loud abort (il2cpp_null_check NRE baseline)
//      is NOT exercised here (the process would abort); the writer is never
//      reused after Close.
//
// Plain executable + minimal assertion harness (test_stringbuilder style):
// any failed Expect() prints FAIL and bumps the counter; the process exits
// non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "xml_write.h"

#include "il2cpp_gc.h"
#include "il2cpp_object.h"
#include "il2cpp_runtime.h"
#include "il2cpp_stringbuilder.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

using il2cpp::Il2CppObject;
using il2cpp::Il2CppString;
using il2cpp::il2cpp_object_new;
using il2cpp::il2cpp_stringbuilder_class;
using il2cpp::runtime::icall_stringbuilder_ctor_default;
using il2cpp::runtime::icall_stringbuilder_to_string;
using il2cpp::runtime::il2cpp_xml_writer_close;
using il2cpp::runtime::il2cpp_xml_writer_create;
using il2cpp::runtime::il2cpp_xml_writer_create_s;
using il2cpp::runtime::il2cpp_xml_writer_flush;
using il2cpp::runtime::il2cpp_xml_writer_settings_new;
using il2cpp::runtime::il2cpp_xml_writer_settings_set_indent;
using il2cpp::runtime::il2cpp_xml_writer_settings_set_omit_declaration;
using il2cpp::runtime::il2cpp_xml_writer_write_attribute_string;
using il2cpp::runtime::il2cpp_xml_writer_write_element_string;
using il2cpp::runtime::il2cpp_xml_writer_write_end_document;
using il2cpp::runtime::il2cpp_xml_writer_write_end_element;
using il2cpp::runtime::il2cpp_xml_writer_write_start_element;
using il2cpp::runtime::il2cpp_xml_writer_write_string;

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

// Expect a managed string to hold exactly the ASCII literal 'ascii'.
std::string ReadManaged(const Il2CppString* s)
{
    std::string out;
    if (s == nullptr)
    {
        return out;
    }
    for (int32_t i = 0; i < s->length; ++i)
    {
        const char16_t c = s->chars[i];
        if (c < 0x80)
        {
            out.push_back(static_cast<char>(c));
        }
        else if (c < 0x800)
        {
            out.push_back(static_cast<char>(0xC0 | (c >> 6)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
        else
        {
            out.push_back(static_cast<char>(0xE0 | (c >> 12)));
            out.push_back(static_cast<char>(0x80 | ((c >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (c & 0x3F)));
        }
    }
    return out;
}

Il2CppObject* NewBuilder()
{
    Il2CppObject* sb = il2cpp_object_new(il2cpp_stringbuilder_class());
    icall_stringbuilder_ctor_default(sb);
    return sb;
}

std::string BuilderText(Il2CppObject* sb)
{
    return ReadManaged(reinterpret_cast<const Il2CppString*>(icall_stringbuilder_to_string(sb)));
}

// A managed string from an ASCII literal (the test-side string factory).
Il2CppString* Str(const char* ascii)
{
    return il2cpp::il2cpp_string_new_from_utf8(ascii);
}

} // namespace

int main()
{
    // --- 1. Default settings: declaration + single line (expected.txt) ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* w = il2cpp_xml_writer_create(sb);
        il2cpp_xml_writer_write_start_element(w, Str("root"));
        il2cpp_xml_writer_write_attribute_string(w, Str("id"), Str("1"));
        il2cpp_xml_writer_write_element_string(w, Str("a"), Str("text & more"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        Expect(BuilderText(sb) ==
                   "<?xml version=\"1.0\" encoding=\"utf-16\"?><root id=\"1\"><a>text &amp; more</a></root>",
               "default settings: declaration + single-line element output (byte-pinned)");
    }

    // --- 2. OmitXmlDeclaration + Indent: CRLF + 2-space tree ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* s = il2cpp_xml_writer_settings_new();
        il2cpp_xml_writer_settings_set_omit_declaration(s, true);
        il2cpp_xml_writer_settings_set_indent(s, true);
        Il2CppObject* w = il2cpp_xml_writer_create_s(sb, s);
        il2cpp_xml_writer_write_start_element(w, Str("r"));
        il2cpp_xml_writer_write_element_string(w, Str("x"), Str("v"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        const std::string t = BuilderText(sb);
        Expect(t.find("\r\n  <x>") != std::string::npos, "indent: CRLF + 2-space child element");
        Expect(t.rfind("<r>", 0) == 0, "indent: document starts with the root element (no declaration)");
    }

    // --- 3. Entity escaping in attribute + text values ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* s = il2cpp_xml_writer_settings_new();
        il2cpp_xml_writer_settings_set_omit_declaration(s, true);
        Il2CppObject* w = il2cpp_xml_writer_create_s(sb, s);
        il2cpp_xml_writer_write_start_element(w, Str("e"));
        il2cpp_xml_writer_write_attribute_string(w, Str("attr"), Str("a<b>\"c\" & 'd'"));
        il2cpp_xml_writer_write_string(w, Str("x<y>&z"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        const std::string t = BuilderText(sb);
        Expect(t.find("attr=\"a&lt;b&gt;&quot;c&quot; &amp; 'd'\"") != std::string::npos,
               "attribute value escaping (& < > \" ; ' stays literal)");
        Expect(t.find("x&lt;y&gt;&amp;z") != std::string::npos, "text escaping (& < >)");
    }

    // --- 4. WriteElementString + WriteEndDocument + empty element ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* s = il2cpp_xml_writer_settings_new();
        il2cpp_xml_writer_settings_set_omit_declaration(s, true);
        Il2CppObject* w = il2cpp_xml_writer_create_s(sb, s);
        il2cpp_xml_writer_write_start_element(w, Str("doc"));
        il2cpp_xml_writer_write_element_string(w, Str("a"), Str(""));
        il2cpp_xml_writer_write_start_element(w, Str("empty"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_write_end_document(w);
        il2cpp_xml_writer_close(w);
        Expect(BuilderText(sb) == "<doc><a /><empty /></doc>",
               "WriteEndDocument closes all elements; empty elements render <e />");
    }

    // --- 5. Flush appends + clears; a second Flush does not duplicate ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* w = il2cpp_xml_writer_create(sb);
        il2cpp_xml_writer_write_start_element(w, Str("r"));
        il2cpp_xml_writer_flush(w);
        Expect(BuilderText(sb) == "<?xml version=\"1.0\" encoding=\"utf-16\"?><r",
               "Flush appends the buffered prefix");
        il2cpp_xml_writer_write_element_string(w, Str("x"), Str("v"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        Expect(BuilderText(sb) == "<?xml version=\"1.0\" encoding=\"utf-16\"?><r><x>v</x></r>",
               "Close appends only NEW output after a Flush (no duplication)");
    }

    // --- 6. Null settings behaves like defaults ---
    {
        Il2CppObject* sb = NewBuilder();
        Il2CppObject* w = il2cpp_xml_writer_create_s(sb, nullptr);
        il2cpp_xml_writer_write_start_element(w, Str("r"));
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        Expect(BuilderText(sb) == "<?xml version=\"1.0\" encoding=\"utf-16\"?><r />",
               "null settings = default settings (declaration emitted)");
    }

    // --- 7. GC safety: the target survives a collection while open ---
    {
        Il2CppObject* sb = NewBuilder();
        // Root the test's OWN reference to the builder: the compacting
        // collector rewrites registered root slots (Phase C), so after a
        // collection that moves the builder BOTH the test local AND the
        // writer's captured target stay current.
        il2cpp::il2cpp_gc_add_root_range(reinterpret_cast<void**>(&sb), 1);
        Il2CppObject* w = il2cpp_xml_writer_create(sb);
        il2cpp_xml_writer_write_start_element(w, Str("r"));
        il2cpp_xml_writer_write_element_string(w, Str("x"), Str("v"));
        il2cpp::il2cpp_gc_collect();
        il2cpp_xml_writer_write_end_element(w);
        il2cpp_xml_writer_close(w);
        const std::string text = BuilderText(sb);
        il2cpp::il2cpp_gc_remove_root_range(reinterpret_cast<void**>(&sb));
        Expect(text == "<?xml version=\"1.0\" encoding=\"utf-16\"?><r><x>v</x></r>",
               "GC between writes: the target root range keeps the builder reachable and current");
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURE(S)\n", g_failures);
    return 1;
}
