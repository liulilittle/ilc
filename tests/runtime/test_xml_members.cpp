//------------------------------------------------------------------------------
// test_xml_members.cpp -- XmlDocument common-member surface tests (T3 ext).
//
// Coverage (normal + edge + error paths) for the 5 new members on the
// xml_doc.h surface, built on LoadXml (il2cpp_xml_load_xml) so the full
// "load -> traverse -> attribute/text query" pipeline is exercised:
//
//   1. SelectNodes: path hits (root-level + nested), attribute steps
//      (/root/item/@id -> attribute-hit copies, freed), misses -> empty array.
//   2. SelectSingleNode: first-hit identity, indexed hit, miss -> nullptr,
//      attribute hit -> owned copy (freed); non-returned attribute-hit copies
//      must NOT leak (exercised through a multi-hit attribute path).
//   3. GetAttribute (il2cpp_xml_get_attribute_s): hit value, MISSING ->
//      the empty string (net48 GetAttribute returns String.Empty, never null).
//   4. InnerText: element with mixed content (recursive depth-first), text
//      leaf, CDATA leaf, empty element -> the empty string.
//   5. InnerXml: element children serialized single-line, empty element ->
//      the empty string, text node escaped, CDATA verbatim, comment dropped.
//   6. Exception paths: null root/path/node/name -> ArgumentNullException;
//      syntax error -> XPathException.
//
// Plain executable harness (same as the other test_xml_* tests).
//------------------------------------------------------------------------------

#include "xml_doc.h"

#include "xml_query.h"        // exception chains (attribute-hit contract)

#include "il2cpp_array.h"     // array shape/length
#include "il2cpp_exception.h" // DotNetException carrier
#include "il2cpp_string.h"    // Il2CppString layout

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace {

using il2cpp::Il2CppArray;
using il2cpp::Il2CppObject;
using il2cpp::Il2CppString;
using il2cpp::XmlNode;
using il2cpp::XmlNodeKind;
using il2cpp::il2cpp_string_new_from_utf8;
using il2cpp::runtime::il2cpp_xml_load_xml;
using il2cpp::runtime::il2cpp_xml_get_attribute_s;
using il2cpp::runtime::il2cpp_xml_get_inner_text;
using il2cpp::runtime::il2cpp_xml_get_inner_xml;
using il2cpp::runtime::il2cpp_xml_node_free;
using il2cpp::runtime::il2cpp_xml_outer_xml;
using il2cpp::runtime::il2cpp_xml_select_nodes;
using il2cpp::runtime::il2cpp_xml_select_single_node;

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

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

const char* kXPathExceptionChain =
    "System.Xml.XPath.XPathException,System.Xml.XPath.XPathException,System.SystemException,System.Exception";
const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

// Create a managed string from an ASCII literal (XPath/attribute names).
Il2CppString* S(const char* ascii)
{
    return il2cpp_string_new_from_utf8(ascii);
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

std::string Str(Il2CppObject* s)
{
    return ReadManaged(reinterpret_cast<Il2CppString*>(s));
}

//------------------------------------------------------------------------------
// Shared tree: loaded through il2cpp_xml_load_xml (the real LoadXml path).
//   <root a="1" b="two">
//     |- <item id="x"> hi <b>deep</b> tail </item>
//     |- <item id="y"/>
//     `- <![CDATA[raw & stuff]]>
//------------------------------------------------------------------------------
XmlNode* g_root = nullptr;
const XmlNode* g_item1;
const XmlNode* g_item2;
const XmlNode* g_textHi;
const XmlNode* g_textTail;
const XmlNode* g_b;
const XmlNode* g_cdata;

void BuildTree()
{
    g_root = il2cpp_xml_load_xml(S("<root a=\"1\" b=\"two\">"
                                   "<item id=\"x\">hi<b>deep</b>tail</item>"
                                   "<item id=\"y\"/><![CDATA[raw & stuff]]></root>"));
    Expect(g_root != nullptr, "LoadXml succeeds on the shared document");
    Expect(g_root != nullptr && g_root->children.size() == 3,
           "root has 3 children");
    if (g_root == nullptr || g_root->children.size() != 3)
    {
        return;
    }
    g_item1 = &g_root->children[0];
    g_item2 = &g_root->children[1];
    g_cdata = &g_root->children[2];
    g_textHi = &g_item1->children[0];
    g_b = &g_item1->children[1];
    g_textTail = &g_item1->children[2];
}

// Array of XmlNode* elements -> the i-th element pointer (bounds-safe).
XmlNode* ElemAt(Il2CppArray* arr, int32_t i)
{
    if (arr == nullptr || i < 0 || i >= arr->length)
    {
        return nullptr;
    }
    return *reinterpret_cast<XmlNode**>(il2cpp_array_get_elem_ptr(arr, i));
}

// Assert an array of N XmlNode* elements equals the expected pointer seq.
void ExpectElemPtrs(Il2CppArray* arr, const XmlNode* const* expected, int32_t n)
{
    Expect(arr != nullptr && arr->length == n, "result array length");
    if (arr == nullptr || arr->length != n)
    {
        return;
    }
    for (int32_t i = 0; i < n; ++i)
    {
        char what[96];
        std::snprintf(what, sizeof(what), "element [%d] identity", i);
        Expect(ElemAt(arr, i) == expected[i], what);
    }
}

//------------------------------------------------------------------------------
// 1. SelectNodes.
//------------------------------------------------------------------------------
void TestSelectNodes()
{
    // Root-level element step.
    const XmlNode* expItems[] = {g_item1, g_item2};
    ExpectElemPtrs(il2cpp_xml_select_nodes(g_root, S("/root/item")),
                   expItems, 2);
    // Nested path.
    const XmlNode* expB[] = {g_b};
    ExpectElemPtrs(il2cpp_xml_select_nodes(g_root, S("/root/item/b")),
                   expB, 1);
    // Miss -> empty array (not null, no throw).
    Expect(il2cpp_xml_select_nodes(g_root, S("/root/zzz"))->length == 0,
           "missed path -> empty array");
    // Attribute step -> new-allocated attribute-hit copies (name "@id",
    // text = the value); caller owns them -> free.
    Il2CppArray* attrHits = il2cpp_xml_select_nodes(g_root, S("/root/item/@id"));
    Expect(attrHits != nullptr && attrHits->length == 2,
           "/root/item/@id -> 2 attribute hits");
    if (attrHits != nullptr && attrHits->length == 2)
    {
        XmlNode* a0 = ElemAt(attrHits, 0);
        XmlNode* a1 = ElemAt(attrHits, 1);
        Expect(a0 != nullptr && a0->name == "@id" && a0->text == "x",
               "attribute hit 0: name @id, value x");
        Expect(a1 != nullptr && a1->name == "@id" && a1->text == "y",
               "attribute hit 1: name @id, value y");
        il2cpp_xml_node_free(a0);
        il2cpp_xml_node_free(a1);
    }
    // Descendant step.
    const XmlNode* expAll[] = {g_item1, g_item2};
    ExpectElemPtrs(il2cpp_xml_select_nodes(g_root, S("//item")), expAll, 2);
}

//------------------------------------------------------------------------------
// 2. SelectSingleNode.
//------------------------------------------------------------------------------
void TestSelectSingleNode()
{
    // First hit IS the tree node (identity, zero copy).
    Expect(il2cpp_xml_select_single_node(g_root, S("/root/item")) == g_item1,
           "SelectSingleNode first hit == tree node");
    // Indexed hit.
    Expect(il2cpp_xml_select_single_node(g_root, S("/root/item[2]")) == g_item2,
           "SelectSingleNode [2] == item2");
    // Miss -> nullptr.
    Expect(il2cpp_xml_select_single_node(g_root, S("/root/zzz")) == nullptr,
           "SelectSingleNode miss -> nullptr");
    // Attribute hit -> owned copy (free); the non-returned attribute copy
    // from the 2-hit path must NOT leak (freed inside the runtime).
    XmlNode* attr = il2cpp_xml_select_single_node(g_root, S("/root/item/@id"));
    Expect(attr != nullptr && attr->name == "@id" && attr->text == "x",
           "SelectSingleNode @id -> copy with value x");
    il2cpp_xml_node_free(attr);
}

//------------------------------------------------------------------------------
// 3. GetAttribute.
//------------------------------------------------------------------------------
void TestGetAttribute()
{
    Expect(Str(il2cpp_xml_get_attribute_s(g_root, S("a"))) == "1",
           "GetAttribute(root, a) == 1");
    Expect(Str(il2cpp_xml_get_attribute_s(g_root, S("b"))) == "two",
           "GetAttribute(root, b) == two");
    Expect(Str(il2cpp_xml_get_attribute_s(g_item1, S("id"))) == "x",
           "GetAttribute(item1, id) == x");
    // MISSING attribute -> the EMPTY string, never null (net48 shape).
    Il2CppObject* missing = il2cpp_xml_get_attribute_s(g_root, S("zzz"));
    Expect(missing != nullptr && Str(missing).empty(),
           "GetAttribute missing -> empty string (non-null)");
    // Element without attributes.
    Expect(Str(il2cpp_xml_get_attribute_s(g_b, S("id"))) == "",
           "GetAttribute on attribute-less element -> empty string");
}

//------------------------------------------------------------------------------
// 4. InnerText.
//------------------------------------------------------------------------------
void TestInnerText()
{
    // Element with mixed content: recursive depth-first text + CDATA.
    Expect(Str(il2cpp_xml_get_inner_text(g_item1)) == "hideeptail",
           "InnerText(item1) == hideeptail (recursive)");
    // Text leaf.
    Expect(Str(il2cpp_xml_get_inner_text(g_textHi)) == "hi",
           "InnerText(text leaf) == hi");
    // CDATA leaf (verbatim content).
    Expect(Str(il2cpp_xml_get_inner_text(g_cdata)) == "raw & stuff",
           "InnerText(cdata leaf) == raw & stuff");
    // Empty element -> the empty string.
    Expect(Str(il2cpp_xml_get_inner_text(g_item2)) == "",
           "InnerText(empty element) == empty string");
    // Root: item1 text + item2(empty) + CDATA.
    Expect(Str(il2cpp_xml_get_inner_text(g_root)) == "hideeptailraw & stuff",
           "InnerText(root) == concatenated descendants");
}

//------------------------------------------------------------------------------
// 5. InnerXml.
//------------------------------------------------------------------------------
void TestInnerXml()
{
    // Element children, single-line serialization.
    Expect(Str(il2cpp_xml_get_inner_xml(g_item1)) == "hi<b>deep</b>tail",
           "InnerXml(item1) == hi<b>deep</b>tail");
    // Nested element inner xml.
    Expect(Str(il2cpp_xml_get_inner_xml(g_b)) == "deep",
           "InnerXml(b) == deep");
    // Empty element -> the empty string.
    Expect(Str(il2cpp_xml_get_inner_xml(g_item2)) == "",
           "InnerXml(empty element) == empty string");
    // Text node -> escaped text.
    Expect(Str(il2cpp_xml_get_inner_xml(g_textHi)) == "hi",
           "InnerXml(text node) == escaped text");
    // CDATA node -> verbatim content (no CDATA wrapper).
    Expect(Str(il2cpp_xml_get_inner_xml(g_cdata)) == "raw & stuff",
           "InnerXml(cdata node) == raw content");
    // Root inner xml (children only; the root tag is NOT emitted).
    Expect(Str(il2cpp_xml_get_inner_xml(g_root)) ==
               "<item id=\"x\">hi<b>deep</b>tail</item><item id=\"y\"/><![CDATA[raw & stuff]]>",
           "InnerXml(root) == children markup only");
}

//------------------------------------------------------------------------------
// 6. Exception paths.
//------------------------------------------------------------------------------
void TestExceptions()
{
    Expect(ThrowsDotNet([] { il2cpp_xml_select_nodes(nullptr, S("/root")); },
                        kArgumentNullChain),
           "SelectNodes(null root) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_select_nodes(g_root, nullptr); },
                        kArgumentNullChain),
           "SelectNodes(null path) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_select_single_node(nullptr, S("/root")); },
                        kArgumentNullChain),
           "SelectSingleNode(null root) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_select_single_node(g_root, nullptr); },
                        kArgumentNullChain),
           "SelectSingleNode(null path) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_attribute_s(nullptr, S("a")); },
                        kArgumentNullChain),
           "GetAttribute(null node) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_attribute_s(g_root, nullptr); },
                        kArgumentNullChain),
           "GetAttribute(null name) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_inner_text(nullptr); },
                        kArgumentNullChain),
           "InnerText(null node) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_inner_xml(nullptr); },
                        kArgumentNullChain),
           "InnerXml(null node) -> ArgumentNullException");
    // Syntax error -> XPathException (propagated from the query engine).
    Expect(ThrowsDotNet([] { il2cpp_xml_select_nodes(g_root, S("/root/item[")); },
                        kXPathExceptionChain),
           "SelectNodes(bad path) -> XPathException");
    Expect(ThrowsDotNet([] { il2cpp_xml_select_single_node(g_root, S("nope")); },
                        kXPathExceptionChain),
           "SelectSingleNode(relative path) -> XPathException");
}

//------------------------------------------------------------------------------
// 7. Comment semantics: InnerText SKIPS comments (net48 InnerText excludes
//    comments); InnerXml DROPS comment children (OuterXml fixed point).
//------------------------------------------------------------------------------
void TestComments()
{
    XmlNode* doc = il2cpp_xml_load_xml(S("<root>a<!--x-->b</root>"));
    Expect(doc != nullptr, "comment document loads");
    if (doc != nullptr)
    {
        Expect(doc->children.size() == 3 &&
                   doc->children[1].kind == XmlNodeKind::comment,
               "comment node stored in the tree");
        Expect(Str(il2cpp_xml_get_inner_text(doc)) == "ab",
               "InnerText skips the comment");
        Expect(Str(il2cpp_xml_get_inner_xml(doc)) == "ab",
               "InnerXml drops the comment child (OuterXml fixed point)");
        Expect(Str(il2cpp_xml_get_inner_text(&doc->children[1])) == "x",
               "InnerText of a comment node returns its own content");
        il2cpp_xml_node_free(doc);
    }
}

//------------------------------------------------------------------------------
// 8. Integration: full pipeline + OuterXml normalization on a text/entity doc.
//------------------------------------------------------------------------------
void TestEntityEscaping()
{
    XmlNode* doc = il2cpp_xml_load_xml(S("<root>a &amp; b</root>"));
    Expect(doc != nullptr, "entity document loads");
    if (doc != nullptr)
    {
        // InnerText decodes entities; InnerXml re-escapes for re-parse.
        Expect(Str(il2cpp_xml_get_inner_text(doc)) == "a & b",
               "InnerText decodes &amp;");
        Expect(Str(il2cpp_xml_get_inner_xml(doc)) == "a &amp; b",
               "InnerXml re-escapes &");
        Expect(ReadManaged(il2cpp_xml_outer_xml(doc)) == "<root>a &amp; b</root>",
               "OuterXml round-trip preserved");
        // SelectSingleNode -> InnerText on the returned handle (traverse ->
        // query chaining through the runtime surface).
        const XmlNode* hit = il2cpp_xml_select_single_node(doc, S("/root"));
        Expect(hit == doc, "SelectSingleNode /root == the root handle");
        il2cpp_xml_node_free(doc);
    }
}

} // namespace

int main()
{
    BuildTree();
    if (g_root != nullptr)
    {
        TestSelectNodes();
        TestSelectSingleNode();
        TestGetAttribute();
        TestInnerText();
        TestInnerXml();
        TestEntityEscaping();
        TestComments();
        TestExceptions(); // needs a live tree for the XPathException cases
        il2cpp_xml_node_free(g_root);
        g_root = nullptr;
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURES\n", g_failures);
    return 1;
}
