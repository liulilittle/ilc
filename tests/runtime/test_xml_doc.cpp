//------------------------------------------------------------------------------
// test_xml_doc.cpp -- XmlDocument basic-domain unit tests (T3).
//
// Coverage of the xml_doc.h surface (il2cpp_xml_load_xml /
// il2cpp_xml_outer_xml / il2cpp_xml_node_free), built on the T1 parse
// contract (xml_ast.h):
//
//   1. LoadXml -> OuterXml round-trip: Serialize(Parse(x)) must be a FIXED
//      POINT -- outer_xml(load_xml(s)) == outer_xml(load_xml(outer_xml(load_xml(s))))
//      (semantic equivalence, NOT byte-for-byte against the input: comments
//      are dropped, empty elements normalize to self-closing form, the XML
//      declaration is skipped).
//   2. Structural checks: root name, attribute order/values, text content,
//      CDATA preserved verbatim, entity decoding (named + numeric).
//   3. Empty document: "" / whitespace-only / comment-only -> load fails
//      (nullptr -- the XmlException baseline signalled by the return value).
//   4. Error paths: unterminated element, mismatched end tag, unquoted
//      attribute, unknown entity, bare '&', trailing content, duplicate
//      attribute, processing instruction, unterminated CDATA.
//   5. Null paths: null input -> nullptr; null node -> nullptr; free(null).
//
// Plain executable harness (like the other runtime tests): any failed
// Expect() prints FAIL and bumps the counter; the process exits non-zero
// on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "il2cpp_string.h"
#include "xml_doc.h"

#include <cstdint>
#include <cstdio>
#include <string>

using il2cpp::Il2CppString;
using il2cpp::XmlNode;
using il2cpp::XmlNodeKind;
using il2cpp::il2cpp_string_new_from_utf8;
using il2cpp::runtime::il2cpp_xml_load_xml;
using il2cpp::runtime::il2cpp_xml_node_free;
using il2cpp::runtime::il2cpp_xml_outer_xml;

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

// Create a managed string from an ASCII/UTF-8 literal and load it.
XmlNode* LoadUtf8(const char* text)
{
    return il2cpp_xml_load_xml(il2cpp_string_new_from_utf8(text));
}

// Read a managed string back to UTF-8 (test-side minimal BMP transcode).
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

// OuterXml of a node, as a UTF-8 std::string ("" for nullptr).
std::string Outer(const XmlNode* node)
{
    return ReadManaged(il2cpp_xml_outer_xml(node));
}

// Round-trip helper: load -> serialize -> load -> serialize. Returns the
// first serialization and asserts the fixed-point property (semantic
// equivalence via idempotence).
std::string RoundTrip(const char* source)
{
    XmlNode* a = LoadUtf8(source);
    Expect(a != nullptr, "load succeeded");
    if (a == nullptr)
    {
        return std::string();
    }
    const std::string first = Outer(a);
    XmlNode* b = LoadUtf8(first.c_str());
    Expect(b != nullptr, "second load succeeded");
    const std::string second = b != nullptr ? Outer(b) : std::string();
    Expect(first == second, "outer_xml is a fixed point (semantic round-trip)");
    il2cpp_xml_node_free(b);
    il2cpp_xml_node_free(a);
    return first;
}

} // namespace

int main()
{
    // --- 1a. Basic round trip + exact serialization shape ---
    Expect(RoundTrip("<root><a>text</a></root>") == "<root><a>text</a></root>",
           "simple element round-trips exactly");

    // --- 1b. Attributes: order preserved, values quoted ---
    Expect(RoundTrip("<root x=\"1\" y=\"two\"><c>t</c></root>") ==
               "<root x=\"1\" y=\"two\"><c>t</c></root>",
           "attributes keep order and survive round-trip");

    // --- 1c. Entity escape round trip (text + attribute) ---
    Expect(RoundTrip("<root>a &amp; b &lt;c&gt;</root>") ==
               "<root>a &amp; b &lt;c&gt;</root>",
           "text entities round-trip");
    Expect(RoundTrip("<root a=\"x&quot;y&amp;z\"/>") ==
               "<root a=\"x&quot;y&amp;z\" />",
           "attribute entities round-trip");

    // --- 1d. CDATA preserved verbatim ---
    Expect(RoundTrip("<root><![CDATA[a < b & c]]></root>") ==
               "<root><![CDATA[a < b & c]]></root>",
           "CDATA round-trips verbatim");

    // --- 1e. Self-closing and empty elements normalize ---
    Expect(RoundTrip("<root><a/><b></b></root>") == "<root><a /><b /></root>",
           "empty elements normalize to self-closing form");

    // --- 1f. Comments are dropped (documented simplification) ---
    Expect(RoundTrip("<root><!--drop--><a>1</a></root>") == "<root><a>1</a></root>",
           "comments dropped and round-trip is stable");

    // --- 1g. Leading XML declaration is skipped ---
    Expect(RoundTrip("<?xml version=\"1.0\"?><root/>") == "<root />",
           "XML declaration accepted and not serialized");

    // --- 1h. Numeric character references decode ---
    {
        XmlNode* root = LoadUtf8("<root>&#65;&#x42;</root>");
        Expect(root != nullptr, "numeric entities load");
        if (root != nullptr)
        {
            Expect(root->children.size() == 1 &&
                       root->children[0].kind == XmlNodeKind::text &&
                       root->children[0].text == "AB",
                   "&#65;&#x42; decode to \"AB\"");
            il2cpp_xml_node_free(root);
        }
    }

    // --- 1i. Nested + mixed content structure ---
    {
        XmlNode* root = LoadUtf8("<root>pre<a>1<b>2</b></a>post</root>");
        Expect(root != nullptr, "mixed content loads");
        if (root != nullptr)
        {
            Expect(root->name == "root", "root name");
            Expect(root->children.size() == 3, "root has 3 children");
            Expect(root->children[0].kind == XmlNodeKind::text &&
                       root->children[0].text == "pre",
                   "first child is text \"pre\"");
            Expect(root->children[1].kind == XmlNodeKind::element &&
                       root->children[1].name == "a",
                   "second child is element a");
            Expect(root->children[2].kind == XmlNodeKind::text &&
                       root->children[2].text == "post",
                   "third child is text \"post\"");
            const XmlNode& a = root->children[1];
            Expect(a.children.size() == 2 && a.children[0].text == "1" &&
                       a.children[1].name == "b",
                   "nested b under a");
            il2cpp_xml_node_free(root);
        }
        Expect(RoundTrip("<root>pre<a>1<b>2</b></a>post</root>") ==
                   "<root>pre<a>1<b>2</b></a>post</root>",
               "mixed content round-trips exactly");
    }

    // --- 1j. Whitespace text is preserved verbatim ---
    Expect(RoundTrip("<root>  <a/> </root>") == "<root>  <a /> </root>",
           "whitespace text preserved");

    // --- 2. Empty document ---
    Expect(LoadUtf8("") == nullptr, "empty string is not a document");
    Expect(LoadUtf8("   \t\n") == nullptr, "whitespace-only is not a document");
    Expect(LoadUtf8("<!-- only a comment -->") == nullptr,
           "comment-only is not a document");

    // --- 3. Error paths ---
    Expect(LoadUtf8("<root>") == nullptr, "unterminated element rejected");
    Expect(LoadUtf8("<a></b>") == nullptr, "mismatched end tag rejected");
    Expect(LoadUtf8("<root><a></root>") == nullptr, "unclosed child rejected");
    Expect(LoadUtf8("<a x=1/>") == nullptr, "unquoted attribute rejected");
    Expect(LoadUtf8("<root>&bad;</root>") == nullptr, "unknown entity rejected");
    Expect(LoadUtf8("<root>&amp</root>") == nullptr, "entity without ; rejected");
    Expect(LoadUtf8("<root>a & b</root>") == nullptr, "bare & rejected");
    Expect(LoadUtf8("<root/>junk") == nullptr, "trailing content rejected");
    Expect(LoadUtf8("<root a=\"1\" a=\"2\"/>") == nullptr,
           "duplicate attribute rejected");
    Expect(LoadUtf8("<root><?pi x?></root>") == nullptr,
           "processing instruction rejected");
    Expect(LoadUtf8("<root><![CDATA[oops]]>") == nullptr,
           "unterminated CDATA rejected");

    // --- 4. Null paths ---
    Expect(il2cpp_xml_load_xml(nullptr) == nullptr, "null input rejected");
    Expect(il2cpp_xml_outer_xml(nullptr) == nullptr, "null node serializes to null");
    il2cpp_xml_node_free(nullptr); // must not crash

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURE(S)\n", g_failures);
    return 1;
}
