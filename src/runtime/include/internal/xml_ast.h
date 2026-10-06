#pragma once
//------------------------------------------------------------------------------
// xml_ast.h -- XML basic-domain AST + parse contract (T1 CONTRACT-FIRST).
//
// T1 (xml_parse.cpp) is the parse domain; this header is the contract the
// whole XML stack builds against: T2 (xml_query.*) evaluates queries over
// these trees, T3 (xml_doc.cpp) delegates LoadXml here. Created contract-
// first because T1 had not landed when T3 ran.
//
// CROSS-AGENT UNIFICATION (2026-08-05): the landed code base spells the
// node kinds BOTH ways -- XmlNodeKind::Element/Text/CDATA/Comment (T1 spec)
// and XmlNodeKind::element/text/cdata (the T2/T3 layer). The two spellings
// are the SAME values via aliased enumerators, and il2cpp::runtime
// re-exports XmlNode/XmlAttr/XmlNodeKind aliases of the il2cpp:: namespace
// definitions. Both parse entry points exist: XmlParse (throwing, T1 spec)
// and il2cpp_xml_parse (TryParse, T2/T3 contract).
//
// BASIC DOMAIN (documented simplifications, .NET 4.8 XmlDocument baseline):
//   * Elements, attributes, text, CDATA, comments, one optional leading XML
//     declaration (<?xml ... ?>) and one optional UTF-8 BOM are supported.
//     Other processing instructions and DOCTYPE are parse errors.
//   * Element/attribute names: ASCII [A-Za-z_:] start, [A-Za-z0-9_.:-]
//     continue (no Unicode names -- a non-ASCII name is a parse error).
//   * Attribute values MUST be quoted ('...' or "..."); '<' inside a value
//     is an error; duplicate attribute names are an error.
//   * Entities: &amp; &lt; &gt; &quot; &apos; plus numeric character
//     references &#dd; and &#xhh; (incl. astral code points). Value 0,
//     surrogates and anything above U+10FFFF are errors ("out of range").
//   * Comments are STORED as Comment nodes in document order inside
//     elements; top-level comments (before/after the root) are skipped.
//     A comment ends at the FIRST '>' that appears at or after the FIRST
//     '--' inside its content (documented simplification: '<' and '--'
//     may appear inside comment content -- a '--' immediately before the
//     terminating '>' stays in the content).
//   * Text content is preserved verbatim: no trimming, whitespace-only
//     text nodes are kept (PreserveWhitespace-always simplification).
//   * Exactly ONE root element; trailing non-whitespace content (incl. a
//     second root) is an error. Empty / whitespace-only / comment-only
//     input is an error ("empty document: no root element").
//   * CDATA content is raw: no entity decoding; unterminated sections are
//     errors.
//
// Error convention: XmlParse reports failures by throwing XmlParseException
// whose message is "line L, column C: reason" (1-based positions over the
// raw input; the UTF-8 BOM is skipped without position effect).
// il2cpp_xml_parse (TryParse) NEVER throws: it returns 0 with the output
// root untouched.
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>
#include <exception>
#include <string>
#include <vector>

namespace il2cpp {

// Node kinds in the tree. The T1 spec spelling (Element/Text/CDATA/Comment)
// is canonical; the lowercase spellings are aliases used by the landed
// T2/T3 layer -- both are the SAME values.
enum class XmlNodeKind
{
    Element,
    Text,
    CDATA,
    Comment,
    element = Element, // lowercase alias (T2/T3 landed code)
    text = Text,
    cdata = CDATA,
    comment = Comment,
};

// A single attribute (element nodes only): name + decoded value.
struct XmlAttr
{
    std::string name;
    std::string value;
};

// Tree node. Children are VALUE-OWNED (std::vector<XmlNode>) so the tree
// copies/moves as a value; the root is a single heap allocation owned by
// the caller when produced through il2cpp_xml_load_xml (released via
// il2cpp_xml_node_free, see xml_doc.h).
struct XmlNode
{
    XmlNodeKind kind = XmlNodeKind::Element;
    std::string name;                // tag name (kind == element)
    std::string text;                // content (text/cdata/comment kinds)
    std::vector<XmlAttr> attributes; // element nodes, document order
    std::vector<XmlNode> children;   // element nodes, document order
};

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// Re-exports so the T1-spec spelling (il2cpp::runtime::XmlNode & co.)
// resolves to the same types.
using XmlNode = il2cpp::XmlNode;
using XmlAttr = il2cpp::XmlAttr;
using XmlNodeKind = il2cpp::XmlNodeKind;

// Parse failure carrying the 1-based source position and a reason phrase
// ("line L, column C: reason" in what()). Thrown by XmlParse (T1 spec).
class XmlParseException : public std::exception
{
public:
    XmlParseException(int line, int column, const char* reason);
    const char* what() const noexcept override;

private:
    std::string message_;
};

// Throwing parse (T1 spec): returns the root node BY VALUE. Throws
// XmlParseException on malformed input, including null / empty buffers
// ("empty document: no root element").
XmlNode XmlParse(const char* text);
XmlNode XmlParse(const char* text, std::size_t length);

// TryParse variant (the T2/T3 contract): parse UTF-8 XML text into
// 'outRoot'. Returns 1 on success. Returns 0 -- leaving 'outRoot'
// untouched -- when 'text' or 'outRoot' is null, or the input is not
// well-formed per the BASIC DOMAIN rules above.
int32_t il2cpp_xml_parse(const char* text, il2cpp::XmlNode* outRoot);

// --- traversal helpers (T1 spec) ---

// The first attribute named 'name', or nullptr (missing / null 'name').
const XmlAttr* xml_find_attribute(const XmlNode& node, const char* name);

// The first element child in document order, or nullptr.
const XmlNode* xml_first_element_child(const XmlNode& node);

// Concatenated text + CDATA content of all descendants, depth-first
// (comment and element structure skipped).
std::string xml_inner_text(const XmlNode& node);

// Pre-order walk: 'visit' is called once per node (element/text/cdata/
// comment), then recursion descends into the children.
template <typename Fn>
void xml_walk(const XmlNode& node, Fn visit)
{
    visit(node);
    for (const XmlNode& child : node.children)
    {
        xml_walk(child, visit);
    }
}

}} // namespace il2cpp::runtime
