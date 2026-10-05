//------------------------------------------------------------------------------
// test_xml_parse.cpp -- XML T1 minimal parser tests (taskcards T221).
//
// Coverage (normal + edge + error paths; see xml_ast.h for the scope):
//   1. Empty/malformed documents: "" / whitespace-only / declaration-only
//      -> XmlParseException "empty document: no root element".
//   2. Single element: "<a/>" (self-closing) and "<a></a>" -> root 'a' with
//      no children, no attributes.
//   3. Nested elements: "<a><b><c/></b><d/></a>" -> tree shape + kinds.
//   4. Text nodes: plain, mixed with elements, whitespace preserved verbatim.
//   5. Attributes: single/double quotes, order preserved, find_attribute,
//      value containing the other quote kind.
//   6. Entity references: named (&amp; &lt; &gt; &quot; &apos;) in text and
//      attribute values; numeric decimal/hex incl. a 4-byte UTF-8 char.
//   7. CDATA: raw content stored (no entity decoding, '<' allowed inside).
//   8. Comments: stored as Comment nodes inside elements (raw, '<' allowed);
//      top-level comments (before/after the root) are skipped.
//   9. XML declaration skipped (incl. multi-line); UTF-8 BOM skipped.
//   10. Error paths (ThrowsXml, substring of "line L, column C: reason"):
//       unterminated element / mismatched end tag / unquoted or unterminated
//       attribute value / duplicate attribute / unknown, unterminated and
//       malformed numeric entities / out-of-range and zero numeric entities /
//       DOCTYPE / processing instruction / content after root / unexpected
//       end tag / invalid element name.
//   11. Traversal helpers: xml_walk pre-order, xml_inner_text,
//       xml_first_element_child, xml_find_attribute; the exception derives
//       from std::exception; position (line 3) reported in the message.
//
// Plain executable + minimal assertion harness (see test_versionobj.cpp).
//------------------------------------------------------------------------------

#include "xml_ast.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <type_traits>

namespace {

using il2cpp::runtime::XmlNode;
using il2cpp::runtime::XmlNodeKind;
using il2cpp::runtime::XmlParse;
using il2cpp::runtime::XmlParseException;

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Run 'fn' and report whether it threw XmlParseException whose message
// contains 'expectedSubstr'.
template <typename Fn>
bool ThrowsXml(Fn&& fn, const char* expectedSubstr)
{
    try
    {
        fn();
    }
    catch (const XmlParseException& e)
    {
        return std::strstr(e.what(), expectedSubstr) != nullptr;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

// Element-kind check + name check on a child (bounds-safe via Expect first).
void ExpectElement(const XmlNode& parent, std::size_t index, const char* name)
{
    if (index >= parent.children.size())
    {
        Expect(false, "missing child");
        return;
    }
    const XmlNode& child = parent.children[index];
    Expect(child.kind == XmlNodeKind::Element && child.name == name, name);
}

void ExpectText(const XmlNode& parent, std::size_t index, const char* text)
{
    if (index >= parent.children.size())
    {
        Expect(false, "missing text child");
        return;
    }
    const XmlNode& child = parent.children[index];
    Expect(child.kind == XmlNodeKind::Text && child.text == text, text);
}

} // namespace

int main()
{
    static_assert(std::is_base_of<std::exception, XmlParseException>::value,
                  "XmlParseException derives from std::exception");

    // --- 1. empty / malformed documents ---
    Expect(ThrowsXml([] { XmlParse(""); }, "empty document"),
        "empty document throws");
    Expect(ThrowsXml([] { XmlParse("  \t\r\n "); }, "empty document"),
        "whitespace-only document throws");
    Expect(ThrowsXml([] { XmlParse("<?xml version=\"1.0\"?>"); }, "empty document"),
        "declaration-only document throws");
    Expect(ThrowsXml([] { XmlParse(nullptr, 0); }, "empty document"),
        "null buffer throws (empty document)");

    // --- 2. single element ---
    {
        const XmlNode root = XmlParse("<a/>");
        Expect(root.kind == XmlNodeKind::Element && root.name == "a", "root <a/> name");
        Expect(root.children.empty(), "root <a/> has no children");
        Expect(root.attributes.empty(), "root <a/> has no attributes");
        Expect(root.text.empty(), "element has no text field");
    }
    {
        const XmlNode root = XmlParse("<a></a>");
        Expect(root.kind == XmlNodeKind::Element && root.name == "a", "root <a></a> name");
        Expect(root.children.empty(), "root <a></a> has no children");
    }
    {
        // const char* + length overload
        const XmlNode root = XmlParse("<a/>", 4);
        Expect(root.kind == XmlNodeKind::Element && root.name == "a",
            "XmlParse(const char*, size_t) parses <a/>");
    }

    // --- 3. nested elements ---
    {
        const XmlNode root = XmlParse("<a><b><c/></b><d/></a>");
        ExpectElement(root, 0, "b");
        ExpectElement(root, 1, "d");
        Expect(root.children.size() == 2, "<a> has exactly 2 children");
        const XmlNode& b = root.children[0];
        ExpectElement(b, 0, "c");
        Expect(b.children.size() == 1, "<b> has exactly 1 child");
        const XmlNode& d = root.children[1];
        Expect(d.children.empty(), "<d> has no children");
    }

    // --- 4. text nodes ---
    {
        const XmlNode root = XmlParse("<a>hello</a>");
        Expect(root.children.size() == 1, "<a>hello</a> has 1 child");
        ExpectText(root, 0, "hello");
    }
    {
        const XmlNode root = XmlParse("<a>pre<b/>post</a>");
        Expect(root.children.size() == 3, "mixed content has 3 children");
        ExpectText(root, 0, "pre");
        ExpectElement(root, 1, "b");
        ExpectText(root, 2, "post");
    }
    {
        const XmlNode root = XmlParse("<a>  <b/>  </a>");
        Expect(root.children.size() == 3, "whitespace-preserving content has 3 children");
        ExpectText(root, 0, "  ");
        ExpectElement(root, 1, "b");
        ExpectText(root, 2, "  ");
    }

    // --- 5. attributes ---
    {
        const XmlNode root = XmlParse("<a x=\"1\" y='two' z=\"it's\"/>");
        Expect(root.attributes.size() == 3, "3 attributes parsed");
        Expect(root.attributes[0].name == "x" && root.attributes[0].value == "1",
            "attr x=\"1\"");
        Expect(root.attributes[1].name == "y" && root.attributes[1].value == "two",
            "attr y='two'");
        Expect(root.attributes[2].name == "z" && root.attributes[2].value == "it's",
            "attr z=\"it's\" (apostrophe inside double quotes)");
        const auto* x = il2cpp::runtime::xml_find_attribute(root, "x");
        Expect(x != nullptr && x->value == "1", "xml_find_attribute hit");
        Expect(il2cpp::runtime::xml_find_attribute(root, "missing") == nullptr,
            "xml_find_attribute miss -> nullptr");
    }
    {
        const XmlNode root = XmlParse("<a x = \"1\" ></a>");
        Expect(root.attributes.size() == 1 && root.attributes[0].value == "1",
            "whitespace around '=' allowed");
    }

    // --- 6. entity references ---
    {
        const XmlNode root = XmlParse("<a>&amp;&lt;&gt;&quot;&apos;</a>");
        Expect(root.children.size() == 1, "entity-only content is one text node");
        ExpectText(root, 0, "&<>\"'");
    }
    {
        const XmlNode root = XmlParse("<a t=\"&amp;&#65;\"/>");
        Expect(root.attributes.size() == 1 && root.attributes[0].value == "&A",
            "entities decoded in attribute values");
    }
    {
        const XmlNode root = XmlParse("<a>&#65;&#x42;&#x1F600;</a>");
        ExpectText(root, 0, "AB\xF0\x9F\x98\x80"); // 'A' 'B' + U+1F600 as UTF-8
    }

    // --- 7. CDATA ---
    {
        const XmlNode root = XmlParse("<a><![CDATA[<b> &amp; raw]]></a>");
        Expect(root.children.size() == 1, "CDATA section is one child");
        const XmlNode& cdata = root.children[0];
        Expect(cdata.kind == XmlNodeKind::CDATA && cdata.text == "<b> &amp; raw",
            "CDATA content stored raw");
    }
    {
        const XmlNode root = XmlParse("<a><![CDATA[ a ] b ]] c ]]></a>");
        Expect(root.children.size() == 1, "CDATA with ']]' inside parses");
        Expect(root.children[0].text == " a ] b ]] c ", "CDATA terminates only at ']]>'");
    }

    // --- 8. comments ---
    {
        const XmlNode root = XmlParse("<a><!-- note --><b/></a>");
        Expect(root.children.size() == 2, "comment + element = 2 children");
        Expect(root.children[0].kind == XmlNodeKind::Comment &&
               root.children[0].text == " note ", "comment stored in content order");
        ExpectElement(root, 1, "b");
    }
    {
        const XmlNode root = XmlParse("<a><!-- <x> -- ></a>"); // '<' inside comment
        Expect(root.children.size() == 1 && root.children[0].kind == XmlNodeKind::Comment &&
               root.children[0].text == " <x> -- ", "comment content may contain '<' and '--'");
    }
    {
        const XmlNode root = XmlParse("<!-- pre --><a/><!-- post -->");
        Expect(root.kind == XmlNodeKind::Element && root.name == "a",
            "top-level comments skipped");
    }

    // --- 9. declaration + BOM ---
    {
        const XmlNode root = XmlParse("<?xml version=\"1.0\"?><a/>");
        Expect(root.name == "a", "XML declaration skipped");
    }
    {
        const XmlNode root = XmlParse("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- c -->\n<a/>");
        Expect(root.name == "a", "multi-line declaration + comment before root skipped");
    }
    {
        const XmlNode root = XmlParse("\xEF\xBB\xBF<a/>"); // UTF-8 BOM
        Expect(root.name == "a", "UTF-8 BOM skipped");
    }

    // --- 10. error paths ---
    Expect(ThrowsXml([] { XmlParse("<a>"); }, "unterminated element"),
        "<a> throws (unterminated element)");
    Expect(ThrowsXml([] { XmlParse("<a></b>"); }, "mismatched end tag"),
        "<a></b> throws (mismatched end tag)");
    Expect(ThrowsXml([] { XmlParse("<a><b></a>"); }, "mismatched end tag"),
        "<a><b></a> throws (mismatched end tag, nested)");
    Expect(ThrowsXml([] { XmlParse("<a x=1/>"); }, "must be quoted"),
        "<a x=1/> throws (unquoted attribute value)");
    Expect(ThrowsXml([] { XmlParse("<a x=\"1/>"); }, "unterminated attribute value"),
        "<a x=\"1/> throws (unterminated attribute value)");
    Expect(ThrowsXml([] { XmlParse("<a x=\"1\" x=\"2\"/>"); }, "duplicate attribute"),
        "duplicate attribute throws");
    Expect(ThrowsXml([] { XmlParse("<a>&unknown;</a>"); }, "unknown entity"),
        "&unknown; throws");
    Expect(ThrowsXml([] { XmlParse("<a>&</a>"); }, "unterminated entity"),
        "bare '&' throws (unterminated entity)");
    Expect(ThrowsXml([] { XmlParse("<a>x&y</a>"); }, "unterminated entity"),
        "'x&y' throws (unterminated entity)");
    Expect(ThrowsXml([] { XmlParse("<a>&#xZZ;</a>"); }, "malformed numeric entity"),
        "&#xZZ; throws (malformed numeric entity)");
    Expect(ThrowsXml([] { XmlParse("<a>&#999999999;</a>"); }, "out of range"),
        "oversized numeric entity throws (out of range)");
    Expect(ThrowsXml([] { XmlParse("<a>&#0;</a>"); }, "out of range"),
        "zero numeric entity throws (out of range)");
    Expect(ThrowsXml([] { XmlParse("<a><!DOCTYPE x></a>"); }, "DOCTYPE"),
        "DOCTYPE throws (unsupported)");
    Expect(ThrowsXml([] { XmlParse("<a><?pi?></a>"); }, "processing instruction"),
        "processing instruction inside element throws");
    Expect(ThrowsXml([] { XmlParse("<?pi?><a/>"); }, "processing instruction"),
        "processing instruction at document start throws");
    Expect(ThrowsXml([] { XmlParse("<a/><b/>"); }, "content after root"),
        "two roots throw (content after root)");
    Expect(ThrowsXml([] { XmlParse("</a>"); }, "unexpected end tag"),
        "lone end tag throws");
    Expect(ThrowsXml([] { XmlParse("<>"); }, "invalid element name"),
        "<> throws (invalid element name)");
    Expect(ThrowsXml([] { XmlParse("<a><\xE9/></a>"); }, "invalid element name"),
        "non-ASCII name throws (invalid element name)");
    Expect(ThrowsXml([] { XmlParse("<a>x</a></a>"); }, "content after root"),
        "trailing end tag throws (content after root)");

    // --- 11. traversal helpers + position reporting ---
    {
        const XmlNode root = XmlParse("<a><b/><c><d/></c></a>");
        std::string visited;
        il2cpp::runtime::xml_walk(root, [&visited](const XmlNode& n) {
            if (n.kind == XmlNodeKind::Element)
            {
                visited += n.name;
            }
        });
        Expect(visited == "abcd", "xml_walk visits a,b,c,d pre-order");
    }
    {
        const XmlNode root = XmlParse("<a>hi<b>there</b><![CDATA[&]]></a>");
        Expect(il2cpp::runtime::xml_inner_text(root) == "hithere&",
            "xml_inner_text concatenates text + CDATA depth-first");
    }
    {
        const XmlNode root = XmlParse("<a><b/><c/></a>");
        const auto* b = il2cpp::runtime::xml_first_element_child(root);
        Expect(b != nullptr && b->name == "b", "xml_first_element_child -> <b>");
        Expect(il2cpp::runtime::xml_first_element_child(*b) == nullptr,
            "xml_first_element_child of <b/> -> nullptr");
        const XmlNode noElem = XmlParse("<a>text</a>");
        Expect(il2cpp::runtime::xml_first_element_child(noElem) == nullptr,
            "xml_first_element_child with text-only content -> nullptr");
    }
    {
        // Error position: </b> sits on line 3 of the document.
        Expect(ThrowsXml([] {
            XmlParse("<?xml version=\"1.0\"?>\n<a>\n</b></a>");
        }, "line 3"), "error message reports the line (line 3)");
        Expect(ThrowsXml([] {
            XmlParse("<?xml version=\"1.0\"?>\n<a>\n</b></a>");
        }, "column 1"), "error message reports the column (column 1)");
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
    }
    return g_failures == 0 ? 0 : 1;
}
