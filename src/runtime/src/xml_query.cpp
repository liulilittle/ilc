//------------------------------------------------------------------------------
// xml_query.cpp -- XML query API (T2: XPath simplified-subset evaluation; see xml_query.h).
//
// Evaluation model:
//   * A path is parsed into a sequence of steps (the /, //, @, [n] subset);
//     a syntax error throws XPathException.
//   * Evaluation is a set transformation: each step maps the "current
//     candidate set" into a "hit set".
//     - First step (no //, no @): matches the passed-in root's own name
//       (no match -> empty).
//     - Element step: for each candidate, take its direct child elements
//       (with //, all descendant elements at depth >= 1) whose names match;
//       with [n], each candidate keeps only the n-th matching one in
//       document order (1-based; fewer than n -> no result for that
//       candidate).
//     - Attribute step (only the last step): for each candidate element,
//       take the attributes whose names match in its attributes; a hit is
//       represented as a new-allocated node copy (see the return-value
//       contract in xml_query.h).
//   * Results are collected in document order and deduplicated (// can
//     produce duplicate pointers across candidates).
//   * The tree is read-only (T1 contract): children/attributes are frozen
//     after parsing, so node addresses are stable and element-step hits
//     return the original tree-node pointers directly (zero copy).
//------------------------------------------------------------------------------

#include "xml_query.h"

#include "xml_doc.h"       // il2cpp_xml_node_free (release contract for attribute-hit copies)

#include "il2cpp_exception.h" // il2cpp_throw_new (DotNetException carrier)
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs

#include <algorithm> // std::sort / std::unique
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace il2cpp { namespace runtime {

namespace {

const char* kXPathExceptionChain =
    "System.Xml.XPath.XPathException,System.Xml.XPath.XPathException,System.SystemException,System.Exception";
const char* kArgumentNullChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";

[[noreturn]] void ThrowXPath(const char* message)
{
    il2cpp_throw_new(kXPathExceptionChain, message);
    // il2cpp_throw_new throws a C++ exception and never returns; abort is a
    // defensive backstop that also silences -Winvalid-noreturn (the same
    // pattern as convertbase64/monitor).
    std::abort();
}

[[noreturn]] void ThrowArgNull(const char* argument)
{
    il2cpp_throw_new(kArgumentNullChain, argument);
    std::abort();
}

// A single path step (the parsed form).
struct QueryStep
{
    bool        descendant = false; // '//' prefix: match descendant elements at any depth (depth >= 1)
    bool        attribute  = false; // '@' prefix: attribute step (only the last step)
    std::string name;               // element name / attribute name
    int32_t     index       = 0;    // [n] positional predicate (1-based); 0 = none
};

// Name character set (aligned with the T1 contract element-name rules: ASCII [A-Za-z0-9_.:-]).
bool IsNameChar(char c)
{
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
           (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-' || c == ':';
}

// Split an absolute path into a step sequence. Syntax error -> XPathException.
std::vector<QueryStep> ParsePath(const char* path)
{
    std::vector<QueryStep> steps;
    if (path[0] != '/')
    {
        ThrowXPath("relative XPath expressions are not supported");
    }

    const char* p = path;
    while (*p != '\0')
    {
        if (*p != '/')
        {
            ThrowXPath("expected '/' before step");
        }
        ++p;

        QueryStep step;
        if (*p == '/')
        {
            step.descendant = true;
            ++p;
        }
        if (*p == '\0')
        {
            ThrowXPath("trailing '/'");
        }
        if (*p == '@')
        {
            step.attribute = true;
            ++p;
            if (*p == '\0' || !IsNameChar(*p))
            {
                ThrowXPath("attribute step is missing its name");
            }
        }

        while (*p != '\0' && IsNameChar(*p))
        {
            step.name.push_back(*p);
            ++p;
        }
        if (step.name.empty())
        {
            ThrowXPath("step is missing its name");
        }

        if (*p == '[')
        {
            if (step.attribute)
            {
                ThrowXPath("index predicate on an attribute step is not supported");
            }
            ++p;
            int64_t index = 0;
            bool anyDigit = false;
            while (*p >= '0' && *p <= '9')
            {
                index = index * 10 + (*p - '0');
                if (index > INT32_MAX)
                {
                    ThrowXPath("index is too large");
                }
                anyDigit = true;
                ++p;
            }
            if (!anyDigit)
            {
                ThrowXPath("expected index digits after '['");
            }
            if (index < 1)
            {
                ThrowXPath("index must be >= 1");
            }
            if (*p != ']')
            {
                ThrowXPath("missing ']'");
            }
            ++p;
            step.index = static_cast<int32_t>(index);
        }

        if (*p != '\0' && *p != '/')
        {
            ThrowXPath("invalid character in path");
        }
        if (step.attribute && *p != '\0')
        {
            ThrowXPath("attribute step must be the last step");
        }

        steps.push_back(step);
    }
    return steps;
}

// Element-name match (matches element nodes only; text/cdata nodes have an
// empty name in the T1 tree).
bool NameMatches(const XmlNode* node, const char* name)
{
    return node->kind == XmlNodeKind::element &&
           node->name == name;
}

// Collect the pointers of all descendants of 'node' (depth >= 1) whose name
// is 'name', in document order (DFS pre-order).
void CollectDescendants(const XmlNode* node, const char* name,
                        std::vector<const XmlNode*>& out)
{
    for (const XmlNode& child : node->children)
    {
        if (NameMatches(&child, name))
        {
            out.push_back(&child);
        }
        CollectDescendants(&child, name, out);
    }
}

// Collect all descendant elements of 'node' (any name), in document order
// (DFS pre-order).
void CollectAllDescendants(const XmlNode* node, std::vector<const XmlNode*>& out)
{
    for (const XmlNode& child : node->children)
    {
        if (child.kind == XmlNodeKind::element)
        {
            out.push_back(&child);
        }
        CollectAllDescendants(&child, out);
    }
}

// Build an attribute-hit copy: { kind: element, name: "@" + attribute name,
// text: attribute value }. new-allocated; the caller releases it with
// il2cpp_xml_node_free (xml_query.h return-value contract).
XmlNode* MakeAttributeHit(const XmlAttr& attr)
{
    XmlNode* hit = new XmlNode;
    hit->kind = XmlNodeKind::element;
    hit->name = "@" + attr.name;
    hit->text = attr.value;
    return hit;
}

// Apply one step: transform the candidate set 'in' into the hit set 'out'
// (see the file-header evaluation model).
void ApplyStep(const std::vector<const XmlNode*>& in, const QueryStep& step,
               std::vector<const XmlNode*>& out)
{
    if (step.attribute)
    {
        for (const XmlNode* candidate : in)
        {
            std::vector<const XmlNode*> targets;
            if (step.descendant)
            {
                // //@x: take the attribute of every descendant element of the
                // candidate (depth >= 1).
                CollectAllDescendants(candidate, targets);
            }
            else
            {
                targets.push_back(candidate);
            }
            for (const XmlNode* target : targets)
            {
                for (const XmlAttr& attr : target->attributes)
                {
                    if (attr.name == step.name)
                    {
                        out.push_back(MakeAttributeHit(attr));
                    }
                }
            }
        }
        return;
    }

    for (const XmlNode* candidate : in)
    {
        std::vector<const XmlNode*> matched;
        if (step.descendant)
        {
            CollectDescendants(candidate, step.name.c_str(), matched);
        }
        else
        {
            for (const XmlNode& child : candidate->children)
            {
                if (NameMatches(&child, step.name.c_str()))
                {
                    matched.push_back(&child);
                }
            }
        }
        if (step.index > 0)
        {
            if (static_cast<size_t>(step.index) <= matched.size())
            {
                out.push_back(matched[static_cast<size_t>(step.index) - 1]);
            }
        }
        else
        {
            out.insert(out.end(), matched.begin(), matched.end());
        }
    }
}

// Return the node text (managed string); nullptr means no text.
Il2CppObject* TextAsString(const std::string& text)
{
    return text.empty()
        ? nullptr
        : reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(text.c_str()));
}

} // namespace

Il2CppArray* il2cpp_xml_query(const il2cpp::XmlNode* root, const char* path)
{
    if (root == nullptr)
    {
        ThrowArgNull("root");
    }
    if (path == nullptr)
    {
        ThrowArgNull("path");
    }

    std::vector<QueryStep> steps = ParsePath(path);

    std::vector<const XmlNode*> current;
    const QueryStep& first = steps[0];
    if (!first.descendant && !first.attribute)
    {
        // The first step matches root itself (no parent context; [n] is
        // meaningless -> syntax error).
        if (first.index != 0)
        {
            ThrowXPath("index predicate on the root step is not supported");
        }
        if (NameMatches(root, first.name.c_str()))
        {
            current.push_back(root);
        }
    }
    else
    {
        // The first step is // or @: candidates start at root, handled by
        // the unified step logic.
        current.push_back(root);
        std::vector<const XmlNode*> next;
        ApplyStep(current, first, next);
        current.swap(next);
    }

    for (size_t i = 1; i < steps.size(); ++i)
    {
        std::vector<const XmlNode*> next;
        ApplyStep(current, steps[i], next);
        current.swap(next);
    }

    // Deduplicate (// can produce duplicate pointers across candidates),
    // keeping document order (address order is irrelevant).
    std::vector<const XmlNode*> deduped;
    for (const XmlNode* node : current)
    {
        if (std::find(deduped.begin(), deduped.end(), node) == deduped.end())
        {
            deduped.push_back(node);
        }
    }
    current.swap(deduped);

    Il2CppArray* result = il2cpp_array_new(
        nullptr, static_cast<int32_t>(sizeof(XmlNode*)), false,
        static_cast<int32_t>(current.size()));
    for (size_t i = 0; i < current.size(); ++i)
    {
        XmlNode** slot = reinterpret_cast<XmlNode**>(
            il2cpp_array_get_elem_ptr(result, static_cast<int32_t>(i)));
        *slot = const_cast<XmlNode*>(current[i]);
    }
    return result;
}

Il2CppObject* il2cpp_xml_get_attribute(const il2cpp::XmlNode* node,
                                       const char* name)
{
    if (node == nullptr)
    {
        ThrowArgNull("node");
    }
    if (name == nullptr)
    {
        ThrowArgNull("name");
    }
    for (const XmlAttr& attr : node->attributes)
    {
        if (attr.name == name)
        {
            return TextAsString(attr.value);
        }
    }
    return nullptr;
}

Il2CppObject* il2cpp_xml_get_text(const il2cpp::XmlNode* node)
{
    if (node == nullptr)
    {
        ThrowArgNull("node");
    }
    // Attribute hit (query-layer copy, name starts with '@'): its text is
    // the attribute value.
    if (node->kind == XmlNodeKind::element && !node->name.empty() &&
        node->name[0] == '@')
    {
        return TextAsString(node->text);
    }
    if (node->kind == XmlNodeKind::text || node->kind == XmlNodeKind::cdata)
    {
        return TextAsString(node->text);
    }
    // Element: concatenate the direct text child nodes (text/cdata) in
    // document order.
    std::string joined;
    for (const XmlNode& child : node->children)
    {
        if (child.kind == XmlNodeKind::text || child.kind == XmlNodeKind::cdata)
        {
            joined += child.text;
        }
    }
    return TextAsString(joined);
}

}} // namespace il2cpp::runtime
