//------------------------------------------------------------------------------
// xml_doc.cpp -- XmlDocument basic-domain entry points (T3).
//
//   * il2cpp_xml_load_xml: transcode the managed UTF-16 string to UTF-8 and
//     delegate to the T1 parse contract (xml_ast.h / xml_parse.cpp).
//   * il2cpp_xml_outer_xml: serialize the tree to UTF-8 (single line,
//     entity-escaped, CDATA kept verbatim) and transcode back to a managed
//     string via il2cpp_string_new.
//
// The UTF-8 <-> UTF-16 transcoders are self-contained (BMP units + astral
// surrogate pairs; unpaired/malformed sequences become U+FFFD) --
// deliberately NOT reusing the in-flight encoding module so this domain
// stays independent of that work.
//------------------------------------------------------------------------------

#include "xml_doc.h"

#include "xml_query.h"       // il2cpp_xml_query (SelectNodes engine)

#include "il2cpp_exception.h" // il2cpp_throw_new (DotNetException carrier)
#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception
#include "il2cpp_gcroot.h"    // GcRoot (enumerator allocation rooting)
#include "il2cpp_runtime.h"   // il2cpp_object_new / il2cpp_class_register
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

//------------------------------------------------------------------------------
// xmlenum: the XmlNodeList snapshot-enumerator runtime descriptor.
// GLOBAL scope (external linkage) like System_Collections_Stack_Enumerator_class
// (stackqueue.cpp): allocated through il2cpp_object_new by
// il2cpp_xml_nodelist_get_enumerator, registered lazily there.
//   instanceSize sizeof(Il2CppXmlNodeListEnumerator) (obj 8 + nodes 8 + index 4,
//     padded to alignment), refFieldBitmap bit 1: 'nodes' (the frozen hit array)
//     is the only GC ref.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Xml_XmlNodeListEnumerator_class = {
    "System.Xml.XmlNodeListEnumerator", // name (net48 internal type name)
    static_cast<int32_t>(sizeof(il2cpp::runtime::Il2CppXmlNodeListEnumerator)), // instanceSize
    0b00010u,                   // refFieldBitmap: bit 1 = nodes
    1,                          // refFieldCount
    nullptr,                    // baseClass
    nullptr,                    // vtable
    0,                          // vtableSize
    nullptr,                    // interfaces
    0,                          // interfaceCount
};

namespace {

// xmlenum: layout verification -- the enumerator descriptor's refFieldBitmap
// bit 1 (nodes) must track the struct slot; index is an int (not scanned).
static_assert(offsetof(il2cpp::runtime::Il2CppXmlNodeListEnumerator, nodes) == 1 * sizeof(void*),
              "Il2CppXmlNodeListEnumerator: nodes must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(il2cpp::runtime::Il2CppXmlNodeListEnumerator, index) == 2 * sizeof(void*),
              "Il2CppXmlNodeListEnumerator: index sits at slot 2 (NOT in the bitmap)");

bool g_xmlNodeListEnumeratorRegistered = false;

// Lazy descriptor registration (constraint #12: no global-ctor allocation;
// idempotent). GC must scan the enumerator's frozen hit array (bitmap bit 1).
void EnsureXmlNodeListEnumeratorRegistered()
{
    if (g_xmlNodeListEnumeratorRegistered) return;
    il2cpp::il2cpp_class_register(&System_Xml_XmlNodeListEnumerator_class);
    g_xmlNodeListEnumeratorRegistered = true;
}

const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

[[noreturn]] void ThrowArgNull(const char* argument)
{
    il2cpp::il2cpp_throw_new(kArgumentNullChain, argument);
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort(); // defensive backstop (same pattern as xml_query.cpp)
#endif
}

// UTF-16 -> UTF-8: BMP units direct, astral surrogate pairs expanded to
// 4-byte sequences; unpaired surrogates become U+FFFD (documented).
std::string Utf16ToUtf8(const char16_t* chars, int32_t length)
{
    std::string out;
    out.reserve(static_cast<size_t>(length));
    for (int32_t i = 0; i < length; ++i)
    {
        const char16_t c = chars[i];
        if (c >= 0xD800 && c <= 0xDBFF && i + 1 < length &&
            chars[i + 1] >= 0xDC00 && chars[i + 1] <= 0xDFFF)
        {
            const uint32_t cp = 0x10000u +
                                ((static_cast<uint32_t>(c) - 0xD800u) << 10) +
                                (static_cast<uint32_t>(chars[i + 1]) - 0xDC00u);
            out.push_back(static_cast<char>(0xF0u | (cp >> 18)));
            out.push_back(static_cast<char>(0x80u | ((cp >> 12) & 0x3Fu)));
            out.push_back(static_cast<char>(0x80u | ((cp >> 6) & 0x3Fu)));
            out.push_back(static_cast<char>(0x80u | (cp & 0x3Fu)));
            ++i;
        }
        else if (c >= 0xD800 && c <= 0xDFFF)
        {
            out += "\xEF\xBF\xBD"; // unpaired surrogate -> U+FFFD
        }
        else if (c < 0x80)
        {
            out.push_back(static_cast<char>(c));
        }
        else if (c < 0x800)
        {
            out.push_back(static_cast<char>(0xC0u | (c >> 6)));
            out.push_back(static_cast<char>(0x80u | (c & 0x3Fu)));
        }
        else
        {
            out.push_back(static_cast<char>(0xE0u | (c >> 12)));
            out.push_back(static_cast<char>(0x80u | ((c >> 6) & 0x3Fu)));
            out.push_back(static_cast<char>(0x80u | (c & 0x3Fu)));
        }
    }
    return out;
}

// UTF-8 -> UTF-16: BMP direct, 4-byte sequences expanded to surrogate
// pairs; malformed/overlong sequences and encoded surrogates -> U+FFFD.
void Utf8ToUtf16(const std::string& utf8, std::vector<char16_t>& out)
{
    out.clear();
    out.reserve(utf8.size());
    const size_t n = utf8.size();
    for (size_t i = 0; i < n;)
    {
        const unsigned char b = static_cast<unsigned char>(utf8[i]);
        if (b < 0x80u)
        {
            out.push_back(static_cast<char16_t>(b));
            ++i;
        }
        else if ((b & 0xE0u) == 0xC0u && i + 1 < n)
        {
            const unsigned char c1 = static_cast<unsigned char>(utf8[i + 1]);
            const uint32_t cp = (static_cast<uint32_t>(b & 0x1Fu) << 6) |
                                static_cast<uint32_t>(c1 & 0x3Fu);
            out.push_back(cp < 0x80u ? static_cast<char16_t>(0xFFFD)
                                     : static_cast<char16_t>(cp));
            i += 2;
        }
        else if ((b & 0xF0u) == 0xE0u && i + 2 < n)
        {
            const unsigned char c1 = static_cast<unsigned char>(utf8[i + 1]);
            const unsigned char c2 = static_cast<unsigned char>(utf8[i + 2]);
            const uint32_t cp = (static_cast<uint32_t>(b & 0x0Fu) << 12) |
                                (static_cast<uint32_t>(c1 & 0x3Fu) << 6) |
                                static_cast<uint32_t>(c2 & 0x3Fu);
            const bool bad = cp < 0x800u || (cp >= 0xD800u && cp <= 0xDFFFu);
            out.push_back(bad ? static_cast<char16_t>(0xFFFD)
                              : static_cast<char16_t>(cp));
            i += 3;
        }
        else if ((b & 0xF8u) == 0xF0u && i + 3 < n)
        {
            const unsigned char c1 = static_cast<unsigned char>(utf8[i + 1]);
            const unsigned char c2 = static_cast<unsigned char>(utf8[i + 2]);
            const unsigned char c3 = static_cast<unsigned char>(utf8[i + 3]);
            const uint32_t cp = (static_cast<uint32_t>(b & 0x07u) << 18) |
                                (static_cast<uint32_t>(c1 & 0x3Fu) << 12) |
                                (static_cast<uint32_t>(c2 & 0x3Fu) << 6) |
                                static_cast<uint32_t>(c3 & 0x3Fu);
            if (cp >= 0x10000u && cp <= 0x10FFFFu)
            {
                out.push_back(static_cast<char16_t>(0xD800u + ((cp - 0x10000u) >> 10)));
                out.push_back(static_cast<char16_t>(0xDC00u + ((cp - 0x10000u) & 0x3FFu)));
            }
            else
            {
                out.push_back(static_cast<char16_t>(0xFFFD));
            }
            i += 4;
        }
        else
        {
            out.push_back(static_cast<char16_t>(0xFFFD));
            ++i;
        }
    }
}

// Text content: escape & < > (plus C0 control characters other than
// tab/LF/CR, which net48 re-escapes as &#xN;) so a re-parse yields the
// identical text node.
void EscapeText(const std::string& in, std::string& out)
{
    for (const char ch : in)
    {
        const unsigned char u = static_cast<unsigned char>(ch);
        if (ch == '&')
        {
            out += "&amp;";
        }
        else if (ch == '<')
        {
            out += "&lt;";
        }
        else if (ch == '>')
        {
            out += "&gt;";
        }
        else if (u < 0x20u && u != 0x09u && u != 0x0Au && u != 0x0Du)
        {
            char buf[8];
            std::snprintf(buf, sizeof(buf), "&#x%X;", u);
            out += buf;
        }
        else
        {
            out.push_back(ch);
        }
    }
}

// Attribute values: additionally escape '"' (the value delimiter).
void EscapeAttr(const std::string& in, std::string& out)
{
    for (const char ch : in)
    {
        if (ch == '&')
        {
            out += "&amp;";
        }
        else if (ch == '<')
        {
            out += "&lt;";
        }
        else if (ch == '>')
        {
            out += "&gt;";
        }
        else if (ch == '"')
        {
            out += "&quot;";
        }
        else
        {
            out.push_back(ch);
        }
    }
}

// Single-line serialization (no indentation -- .NET OuterXml shape).
// Comment nodes are NOT serialized (documented simplification: the tree
// stores them, OuterXml drops them).
void SerializeNode(const il2cpp::XmlNode& node, std::string& out)
{
    switch (node.kind)
    {
        case il2cpp::XmlNodeKind::Text:
            EscapeText(node.text, out);
            break;
        case il2cpp::XmlNodeKind::CDATA:
            out += "<![CDATA[";
            out += node.text;
            out += "]]>";
            break;
        case il2cpp::XmlNodeKind::Comment:
            break; // comments never serialize back
        case il2cpp::XmlNodeKind::Element:
            out.push_back('<');
            out += node.name;
            for (const auto& attr : node.attributes)
            {
                out.push_back(' ');
                out += attr.name;
                out += "=\"";
                EscapeAttr(attr.value, out);
                out.push_back('"');
            }
            if (node.children.empty())
            {
                out += " />"; // empty element: self-closing with a space (net48)
                break;
            }
            out.push_back('>');
            for (const auto& child : node.children)
            {
                SerializeNode(child, out);
            }
            out += "</";
            out += node.name;
            out.push_back('>');
            break;
    }
}

// Single-line serialization of the node's CHILDREN only (InnerXml shape:
// the node's own tag is not emitted; each child serializes exactly as
// OuterXml renders it -- comments dropped, empty elements self-closing).
void SerializeChildren(const il2cpp::XmlNode& node, std::string& out)
{
    for (const auto& child : node.children)
    {
        SerializeNode(child, out);
    }
}

// UTF-8 -> managed string (full astral transcode via Utf8ToUtf16). The
// empty string produces an empty managed string (never nullptr).
il2cpp::Il2CppObject* ManagedFromUtf8(const std::string& utf8)
{
    std::vector<char16_t> units;
    Utf8ToUtf16(utf8, units);
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new(units.empty() ? nullptr : units.data(),
                                  static_cast<int32_t>(units.size())));
}

} // namespace

namespace il2cpp { namespace runtime {

il2cpp::XmlNode* il2cpp_xml_load_xml(il2cpp::Il2CppString* input)
{
    if (input == nullptr)
    {
        return nullptr;
    }
    const std::string utf8 = Utf16ToUtf8(input->chars, input->length);
    il2cpp::XmlNode* root = new il2cpp::XmlNode;
    if (il2cpp_xml_parse(utf8.c_str(), root) != 1)
    {
        delete root;
        return nullptr;
    }
    return root;
}

il2cpp::Il2CppString* il2cpp_xml_outer_xml(const il2cpp::XmlNode* node)
{
    if (node == nullptr)
    {
        return nullptr;
    }
    std::string utf8;
    SerializeNode(*node, utf8);
    std::vector<char16_t> units;
    Utf8ToUtf16(utf8, units);
    return il2cpp_string_new(units.empty() ? nullptr : units.data(),
                             static_cast<int32_t>(units.size()));
}

Il2CppArray* il2cpp_xml_select_nodes(const il2cpp::XmlNode* root,
                                     il2cpp::Il2CppString* path)
{
    if (root == nullptr)
    {
        ThrowArgNull("root");
    }
    if (path == nullptr)
    {
        ThrowArgNull("path");
    }
    const std::string utf8 = Utf16ToUtf8(path->chars, path->length);
    return il2cpp_xml_query(root, utf8.c_str());
}

Il2CppObject* il2cpp_xml_nodelist_get_item(Il2CppObject* list, int32_t index)
{
    if (list == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(list);
    if (index < 0 || index >= arr->length)
    {
        return nullptr; // net48 XPathNodeList.Item: out-of-range yields null
    }
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(arr->data);
    return slots[index];
}

//------------------------------------------------------------------------------
// xmlenum: XmlNodeList enumeration (foreach over SelectNodes).
//
// The list is the managed object[] of node handles; the enumerator wraps the
// FROZEN array (the query result) plus a cursor. net48 reference semantics
// (System.Xml.XmlNodeListEnumerator, pinned by the xmlenumops oracle):
// get_Current before the first MoveNext and after the end is NULL (not
// InvalidOperationException), Reset rewinds, MoveNext after the end stays
// false. The enumerator is NOT IDisposable, so a foreach over the non-generic
// IEnumerator emits no Dispose.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_xml_nodelist_get_enumerator(Il2CppObject* list)
{
    if (list == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    EnsureXmlNodeListEnumeratorRegistered();
    // One allocation below can trigger a compacting collection: root the list
    // for the whole body and re-read it after the allocation.
    GcRoot<Il2CppObject*> root(list, "icall_xml_nodelist_get_enumerator");
    Il2CppXmlNodeListEnumerator* en = reinterpret_cast<Il2CppXmlNodeListEnumerator*>(
        il2cpp_object_new(&System_Xml_XmlNodeListEnumerator_class));
    list = root.Get();
    en->nodes = reinterpret_cast<Il2CppArray*>(list);
    il2cpp_write_barrier(reinterpret_cast<void**>(&en->nodes),
                         reinterpret_cast<Il2CppObject*>(en->nodes));
    en->index = -1;
    return reinterpret_cast<Il2CppObject*>(en);
}

int32_t il2cpp_xml_nodelist_enumerator_move_next(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppXmlNodeListEnumerator* en = reinterpret_cast<Il2CppXmlNodeListEnumerator*>(self);
    const int32_t count = il2cpp_array_get_length(en->nodes);
    if (en->index < count)
    {
        ++en->index;
    }
    return en->index < count ? 1 : 0;
}

Il2CppObject* il2cpp_xml_nodelist_enumerator_get_current(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    Il2CppXmlNodeListEnumerator* en = reinterpret_cast<Il2CppXmlNodeListEnumerator*>(self);
    const int32_t count = il2cpp_array_get_length(en->nodes);
    if (en->index < 0 || en->index >= count)
    {
        return nullptr; // net48: Current before the first / after the last -> null
    }
    return *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(en->nodes, en->index));
}

void il2cpp_xml_nodelist_enumerator_reset(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    reinterpret_cast<Il2CppXmlNodeListEnumerator*>(self)->index = -1;
}

il2cpp::XmlNode* il2cpp_xml_select_single_node(const il2cpp::XmlNode* root,
                                               il2cpp::Il2CppString* path)
{
    Il2CppArray* hits = il2cpp_xml_select_nodes(root, path);
    const int32_t count = il2cpp_array_get_length(hits);
    if (count == 0)
    {
        return nullptr;
    }
    // The first hit is returned. Attribute-hit copies beyond it (identified
    // by the '@' name prefix -- parser names cannot start with '@') are
    // new-allocated and MUST be released here so they do not leak.
    il2cpp::XmlNode* first = *reinterpret_cast<il2cpp::XmlNode**>(
        il2cpp_array_get_elem_ptr(hits, 0));
    for (int32_t i = 1; i < count; ++i)
    {
        il2cpp::XmlNode* other = *reinterpret_cast<il2cpp::XmlNode**>(
            il2cpp_array_get_elem_ptr(hits, i));
        if (other != nullptr && other->kind == il2cpp::XmlNodeKind::element &&
            !other->name.empty() && other->name[0] == '@')
        {
            il2cpp_xml_node_free(other);
        }
    }
    return first;
}

Il2CppObject* il2cpp_xml_get_attribute_s(const il2cpp::XmlNode* node,
                                         il2cpp::Il2CppString* name)
{
    if (node == nullptr)
    {
        ThrowArgNull("node");
    }
    if (name == nullptr)
    {
        ThrowArgNull("name");
    }
    const std::string utf8 = Utf16ToUtf8(name->chars, name->length);
    Il2CppObject* value = il2cpp_xml_get_attribute(node, utf8.c_str());
    if (value == nullptr)
    {
        // net48 GetAttribute returns String.Empty for a missing attribute.
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(""));
    }
    return value;
}

Il2CppObject* il2cpp_xml_get_inner_text(const il2cpp::XmlNode* node)
{
    if (node == nullptr)
    {
        ThrowArgNull("node");
    }
    std::string text;
    if (node->kind == il2cpp::XmlNodeKind::text ||
        node->kind == il2cpp::XmlNodeKind::cdata ||
        node->kind == il2cpp::XmlNodeKind::comment)
    {
        text = node->text; // net48 InnerText of a leaf = its own content
    }
    else if (node->kind == il2cpp::XmlNodeKind::element && !node->name.empty() &&
             node->name[0] == '@')
    {
        text = node->text; // attribute-hit copy: InnerText = the attribute value
    }
    else
    {
        text = xml_inner_text(*node); // descendant text + CDATA, depth-first
    }
    return ManagedFromUtf8(text); // empty content -> empty string (net48)
}

Il2CppObject* il2cpp_xml_get_inner_xml(const il2cpp::XmlNode* node)
{
    if (node == nullptr)
    {
        ThrowArgNull("node");
    }
    std::string out;
    switch (node->kind)
    {
        case il2cpp::XmlNodeKind::Element:
            SerializeChildren(*node, out);
            break;
        case il2cpp::XmlNodeKind::Text:
            EscapeText(node->text, out);
            break;
        case il2cpp::XmlNodeKind::CDATA:
            out += node->text;
            break;
        case il2cpp::XmlNodeKind::Comment:
            break; // comments never serialize (OuterXml fixed point)
    }
    return ManagedFromUtf8(out);
}

void il2cpp_xml_node_free(il2cpp::XmlNode* root)
{
    delete root;
}

}} // namespace il2cpp::runtime
