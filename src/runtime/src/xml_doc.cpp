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
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

#include <cstdint>
#include <cstdlib>
#include <string>
#include <vector>

namespace {

const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

[[noreturn]] void ThrowArgNull(const char* argument)
{
    il2cpp::il2cpp_throw_new(kArgumentNullChain, argument);
    std::abort(); // defensive backstop (same pattern as xml_query.cpp)
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

// Text content: escape & < > so a re-parse yields the identical text node.
void EscapeText(const std::string& in, std::string& out)
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
                out += "/>"; // empty element renders self-closing
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
