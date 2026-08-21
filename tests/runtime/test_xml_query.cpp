//------------------------------------------------------------------------------
// test_xml_query.cpp -- XML query API (T2: XPath simplified subset) tests.
//
// Coverage (normal + edge + error paths):
//   1. Path hits: /root, /root/item, /root/other/item, nested
//      /root/item[1]/sub/item.
//   2. Path misses: /other (root name mismatch), /root/zzz -> empty array.
//   3. Index: /root/item[1], [2] hit; [3] (not enough) -> empty; index on
//      the root step -> syntax error.
//   4. // any depth: //item full document order, /root//item,
//      /root//item[2].
//   5. Attributes: /root/@id returns an attribute-hit copy (name=="@id",
//      text is the attribute value); //@lang compound step;
//      /root/@missing -> empty.
//   6. get_attribute: hit value / miss nullptr / null argument throws.
//   7. get_text: element direct text concatenation, no text -> nullptr,
//      attribute hit -> attribute value, text node -> its text.
//   8. Error paths (syntax -> XPathException, null -> ArgumentNullException).
//   9. Result array shape: length / elemSize / element pointer identity
//      (an element hit IS the original tree node).
//  10. Integration: queries over a tree produced by the T1 parser
//      (il2cpp_xml_parse).
//
// The tree is built per the T1 contract (xml_ast.h: value ownership,
// children/attributes vectors, document order).
//------------------------------------------------------------------------------

#include "xml_query.h"

#include "xml_ast.h"       // T1 contract + il2cpp_xml_parse
#include "xml_doc.h"       // il2cpp_xml_node_free (attribute hit release)

#include "il2cpp_array.h"    // il2cpp_array_get_length / _get_elem_ptr
#include "il2cpp_exception.h"// DotNetException carrier
#include "il2cpp_string.h"   // Il2CppString layout

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace {

using il2cpp::XmlNode;
using il2cpp::XmlNodeKind;
using il2cpp::runtime::il2cpp_xml_query;
using il2cpp::runtime::il2cpp_xml_get_attribute;
using il2cpp::runtime::il2cpp_xml_get_text;
using il2cpp::runtime::il2cpp_xml_parse;
using il2cpp::runtime::il2cpp_xml_node_free;

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Run 'fn' and assert it throws DotNetException whose type chain matches
// 'expectedChain'.
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

//------------------------------------------------------------------------------
// Test tree construction (T1 contract: value ownership).
//   root (id="r1", lang="en")
//     |- item1 (id="i1", lang="fr", direct text "text1")
//     |    `- sub
//     |         `- item3 (lang="de")
//     |- item2 (id="i2")
//     `- other
//          `- item4
//------------------------------------------------------------------------------
XmlNode g_root;
const XmlNode* g_item1;
const XmlNode* g_item2;
const XmlNode* g_item3;
const XmlNode* g_item4;

XmlNode MakeElem(const char* name)
{
    XmlNode n;
    n.kind = XmlNodeKind::element;
    n.name = name;
    return n;
}

XmlNode MakeText(const char* text)
{
    XmlNode n;
    n.kind = XmlNodeKind::text;
    n.text = text;
    return n;
}

void BuildTree()
{
    g_root = MakeElem("root");
    g_root.attributes.push_back({"id", "r1"});
    g_root.attributes.push_back({"lang", "en"});

    XmlNode item1 = MakeElem("item");
    item1.attributes.push_back({"id", "i1"});
    item1.attributes.push_back({"lang", "fr"});
    item1.children.push_back(MakeText("text1"));
    XmlNode sub = MakeElem("sub");
    XmlNode item3 = MakeElem("item");
    item3.attributes.push_back({"lang", "de"});
    sub.children.push_back(item3);
    item1.children.push_back(sub);

    XmlNode item2 = MakeElem("item");
    item2.attributes.push_back({"id", "i2"});

    XmlNode other = MakeElem("other");
    XmlNode item4 = MakeElem("item");
    other.children.push_back(item4);

    g_root.children.push_back(item1);
    g_root.children.push_back(item2);
    g_root.children.push_back(other);

    // Pointers are fixed: children are never modified after the tree is
    // built (read-only tree contract).
    g_item1 = &g_root.children[0];
    g_item2 = &g_root.children[1];
    g_item3 = &g_root.children[0].children[1].children[0];
    g_item4 = &g_root.children[2].children[0];
}

//------------------------------------------------------------------------------
// Assertion helpers.
//------------------------------------------------------------------------------

int32_t QueryCount(const XmlNode* root, const char* path)
{
    il2cpp::Il2CppArray* arr = il2cpp_xml_query(root, path);
    return il2cpp_array_get_length(arr);
}

XmlNode* QueryNode(const XmlNode* root, const char* path, int32_t i)
{
    il2cpp::Il2CppArray* arr = il2cpp_xml_query(root, path);
    if (i < 0 || i >= il2cpp_array_get_length(arr))
    {
        return nullptr;
    }
    return *reinterpret_cast<XmlNode**>(il2cpp_array_get_elem_ptr(arr, i));
}

// Assert the query hit sequence equals the expected node pointer sequence
// exactly (order-sensitive).
void ExpectNodes(const XmlNode* root, const char* path,
                 const XmlNode* const* expected, int32_t expectedCount)
{
    il2cpp::Il2CppArray* arr = il2cpp_xml_query(root, path);
    int32_t count = il2cpp_array_get_length(arr);
    char what[256];
    std::snprintf(what, sizeof(what), "query \"%s\" count == %d", path, expectedCount);
    Expect(count == expectedCount, what);
    if (count != expectedCount)
    {
        return;
    }
    for (int32_t i = 0; i < count; ++i)
    {
        XmlNode* got = *reinterpret_cast<XmlNode**>(il2cpp_array_get_elem_ptr(arr, i));
        std::snprintf(what, sizeof(what), "query \"%s\" [%d] == expected node", path, i);
        Expect(got == expected[i], what);
    }
}

#define EXPECT_NODES(root, path, ...)                                          \
    do                                                                         \
    {                                                                          \
        const XmlNode* const kExpected[] = {__VA_ARGS__};                      \
        ExpectNodes((root), (path), kExpected,                                 \
                    static_cast<int32_t>(sizeof(kExpected) / sizeof(kExpected[0]))); \
    } while (0)

// Assert the managed string content == ASCII text.
bool StringEqualsAscii(il2cpp::Il2CppObject* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    il2cpp::Il2CppString* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    int32_t len = static_cast<int32_t>(std::strlen(ascii));
    if (str->length != len)
    {
        return false;
    }
    for (int32_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// Test groups.
//------------------------------------------------------------------------------

void TestPathHits()
{
    EXPECT_NODES(&g_root, "/root", &g_root);
    EXPECT_NODES(&g_root, "/root/item", g_item1, g_item2);
    EXPECT_NODES(&g_root, "/root/other/item", g_item4);
    EXPECT_NODES(&g_root, "/root/item[1]/sub/item", g_item3);
}

void TestPathMisses()
{
    Expect(QueryCount(&g_root, "/other") == 0, "/other -> 空 (根名不匹配)");
    Expect(QueryCount(&g_root, "/root/zzz") == 0, "/root/zzz -> 空");
    Expect(QueryCount(&g_root, "/root/sub") == 0, "/root/sub -> 空 (非直接子)");
    Expect(QueryCount(&g_root, "/root/item/zzz") == 0, "/root/item/zzz -> 空");
}

void TestIndex()
{
    EXPECT_NODES(&g_root, "/root/item[1]", g_item1);
    EXPECT_NODES(&g_root, "/root/item[2]", g_item2);
    Expect(QueryCount(&g_root, "/root/item[3]") == 0, "/root/item[3] -> 空 (不足)");
    Expect(QueryCount(&g_root, "/root/item[2]/sub") == 0,
           "/root/item[2]/sub -> 空 (item2 无子)");
    Expect(QueryCount(&g_root, "/root/item[1]/sub/item[1]") == 1,
           "/root/item[1]/sub/item[1] -> 1");
    Expect(QueryCount(&g_root, "/root/item[1]/sub/item[2]") == 0,
           "/root/item[1]/sub/item[2] -> 0");
}

void TestDescendant()
{
    // Document order (DFS pre-order): item1, item3, item2, item4.
    EXPECT_NODES(&g_root, "//item", g_item1, g_item3, g_item2, g_item4);
    EXPECT_NODES(&g_root, "/root//item", g_item1, g_item3, g_item2, g_item4);
    EXPECT_NODES(&g_root, "/root//item[2]", g_item3); // the 2nd descendant item
    EXPECT_NODES(&g_root, "/root/other//item", g_item4);
    EXPECT_NODES(&g_root, "/root/item//item", g_item3); // item1's descendant item
}

void TestAttributes()
{
    // /root/@id -> attribute hit copy: kind=element, name=="@id", text=="r1".
    XmlNode* idHit = QueryNode(&g_root, "/root/@id", 0);
    Expect(idHit != nullptr, "/root/@id 命中");
    Expect(idHit != nullptr && idHit->name == "@id", "/root/@id name == \"@id\"");
    Expect(idHit != nullptr && idHit->text == "r1", "/root/@id text == r1");
    Expect(idHit != nullptr && idHit->attributes.empty(), "属性命中无属性");
    il2cpp_xml_node_free(idHit);

    // //@lang: the lang attribute of every descendant element (root itself
    // is not a descendant, so root's lang is excluded).
    Expect(QueryCount(&g_root, "//@lang") == 2, "//@lang 计数 == 2");
    XmlNode* lang1 = QueryNode(&g_root, "//@lang", 0);
    XmlNode* lang2 = QueryNode(&g_root, "//@lang", 1);
    Expect(lang1 != nullptr && lang1->name == "@lang" && lang1->text == "fr",
           "//@lang [0] == fr");
    Expect(lang2 != nullptr && lang2->name == "@lang" && lang2->text == "de",
           "//@lang [1] == de");
    il2cpp_xml_node_free(lang1);
    il2cpp_xml_node_free(lang2);

    // //item/@lang: item1(fr) + item3(de).
    Expect(QueryCount(&g_root, "//item/@lang") == 2, "//item/@lang 计数 == 2");
    Expect(QueryCount(&g_root, "/root/item/@lang") == 1, "/root/item/@lang 计数 == 1");
    Expect(QueryCount(&g_root, "/root/@missing") == 0, "/root/@missing -> 空");
    Expect(QueryCount(&g_root, "/root/item[2]/@id") == 1, "/root/item[2]/@id 计数 == 1");
}

void TestGetAttribute()
{
    il2cpp::Il2CppObject* v = il2cpp_xml_get_attribute(&g_root, "id");
    Expect(StringEqualsAscii(v, "r1"), "get_attribute(root, id) == r1");
    Expect(il2cpp_xml_get_attribute(&g_root, "zzz") == nullptr,
           "get_attribute(root, zzz) == nullptr");
    Expect(StringEqualsAscii(il2cpp_xml_get_attribute(g_item1, "lang"), "fr"),
           "get_attribute(item1, lang) == fr");
    Expect(il2cpp_xml_get_attribute(g_item3, "id") == nullptr,
           "get_attribute(item3, id) == nullptr (无 id 属性)");
    Expect(il2cpp_xml_get_attribute(g_item4, "zzz") == nullptr,
           "get_attribute(item4, zzz) == nullptr");
}

void TestGetText()
{
    Expect(StringEqualsAscii(il2cpp_xml_get_text(g_item1), "text1"),
           "get_text(item1) == text1");
    Expect(il2cpp_xml_get_text(g_item2) == nullptr,
           "get_text(item2) == nullptr (无文本)");
    Expect(il2cpp_xml_get_text(&g_root) == nullptr,
           "get_text(root) == nullptr (无直接文本)");
    // The text of an attribute-hit node = the attribute value.
    XmlNode* idHit = QueryNode(&g_root, "/root/@id", 0);
    Expect(idHit != nullptr && StringEqualsAscii(il2cpp_xml_get_text(idHit), "r1"),
           "get_text(属性命中) == 属性值");
    il2cpp_xml_node_free(idHit);
}

void TestSyntaxErrors()
{
    const char* bad[] = {
        "",
        "/",
        "root/item",
        "/root/",
        "/root//",
        "/root/[2]",
        "/root/item[0]",
        "/root/item[-1]",
        "/root/item[x]",
        "/root/item[",
        "/root[2]",
        "/root/@a/@b",
        "/root/@a[1]",
        "/root/$x",
        "/root/item/",
    };
    char what[256];
    for (const char* path : bad)
    {
        std::snprintf(what, sizeof(what), "syntax error: \"%s\" -> XPathException", path);
        Expect(ThrowsDotNet([path] { il2cpp_xml_query(&g_root, path); },
                            kXPathExceptionChain),
               what);
    }
}

void TestNullArguments()
{
    Expect(ThrowsDotNet([] { il2cpp_xml_query(nullptr, "/root"); },
                        kArgumentNullChain),
           "query(null root) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_query(&g_root, nullptr); },
                        kArgumentNullChain),
           "query(null path) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_attribute(nullptr, "id"); },
                        kArgumentNullChain),
           "get_attribute(null node) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_attribute(&g_root, nullptr); },
                        kArgumentNullChain),
           "get_attribute(null name) -> ArgumentNullException");
    Expect(ThrowsDotNet([] { il2cpp_xml_get_text(nullptr); },
                        kArgumentNullChain),
           "get_text(null node) -> ArgumentNullException");
}

void TestArrayShape()
{
    il2cpp::Il2CppArray* arr = il2cpp_xml_query(&g_root, "/root/item");
    Expect(il2cpp_array_get_length(arr) == 2, "数组长度 == 2");
    Expect(!arr->isRefElem, "数组 isRefElem == false (原生指针元素)");
    Expect(arr->elemSize == static_cast<int32_t>(sizeof(XmlNode*)),
           "数组 elemSize == sizeof(XmlNode*)");
    // A hit node IS the original tree node pointer (identity, not a copy).
    Expect(QueryNode(&g_root, "/root", 0) == &g_root, "命中即原节点指针");
    // Empty hit array.
    Expect(il2cpp_array_get_length(il2cpp_xml_query(&g_root, "/nope")) == 0,
           "未命中 -> 空数组 (length 0)");
}

// Integration: queries over a real tree produced by the T1 parser.
void TestParsedTreeIntegration()
{
    XmlNode doc;
    const char* xml =
        "<root><a id='1'>hi</a><b/><a id='2'/><a><c/></a></root>";
    Expect(il2cpp_xml_parse(xml, &doc) == 1, "il2cpp_xml_parse 成功");

    EXPECT_NODES(&doc, "/root/a", &doc.children[0], &doc.children[2],
                 &doc.children[3]);
    Expect(QueryCount(&doc, "//a") == 3, "//a 计数 == 3");
    Expect(QueryCount(&doc, "/root/a[2]") == 1, "/root/a[2] 计数 == 1");
    XmlNode* a2 = QueryNode(&doc, "/root/a[2]", 0);
    Expect(a2 == &doc.children[2], "/root/a[2] 命中 == children[2]");

    // Attribute hits (real parsed attributes).
    XmlNode* idHit = QueryNode(&doc, "/root/a[1]/@id", 0);
    Expect(idHit != nullptr && idHit->name == "@id" && idHit->text == "1",
           "解析树 /root/a[1]/@id == 1");
    il2cpp_xml_node_free(idHit);
    idHit = QueryNode(&doc, "/root/a[2]/@id", 0);
    Expect(idHit != nullptr && idHit->text == "2",
           "解析树 /root/a[2]/@id == 2");
    il2cpp_xml_node_free(idHit);

    // get_text on the parsed tree (a[0]'s direct text "hi").
    Expect(StringEqualsAscii(il2cpp_xml_get_text(&doc.children[0]), "hi"),
           "解析树 get_text(a[0]) == hi");
    Expect(il2cpp_xml_get_text(&doc.children[1]) == nullptr,
           "解析树 get_text(b) == nullptr");
    // Missed path (legal) -> empty.
    Expect(QueryCount(&doc, "/root/zzz") == 0, "解析树未命中 -> 空");
}

// New contract: Comment nodes are also stored in children (document order);
// queries must skip them.
void TestCommentSkipped()
{
    XmlNode doc;
    const char* xml = "<root><!--c--><a id='1'>hi</a></root>";
    Expect(il2cpp_xml_parse(xml, &doc) == 1, "il2cpp_xml_parse (带注释) 成功");
    Expect(doc.children.size() == 2 && doc.children[0].kind == XmlNodeKind::comment,
           "注释节点按契约存入 children[0]");
    EXPECT_NODES(&doc, "/root/a", &doc.children[1]);
    Expect(QueryCount(&doc, "//a") == 1, "//a 跳过注释 -> 1");
    Expect(QueryCount(&doc, "//comment") == 0, "注释不参与元素匹配");
    Expect(il2cpp_xml_get_text(&doc) == nullptr, "get_text 跳过注释 (无文本)");
    // A comment is not an element; text concatenation excludes comments.
    Expect(il2cpp_xml_get_text(&doc.children[0]) == nullptr,
           "get_text(注释节点) == nullptr");
}

} // namespace

int main()
{
    BuildTree();

    TestPathHits();
    TestPathMisses();
    TestIndex();
    TestDescendant();
    TestAttributes();
    TestGetAttribute();
    TestGetText();
    TestSyntaxErrors();
    TestNullArguments();
    TestArrayShape();
    TestParsedTreeIntegration();
    TestCommentSkipped();

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
        return 0;
    }
    std::printf("%d FAILURES\n", g_failures);
    return 1;
}
