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
#include <unordered_map>
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
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
}

[[noreturn]] void ThrowArgNull(const char* argument)
{
    il2cpp_throw_new(kArgumentNullChain, argument);
#ifdef _MSC_VER
    __assume(0);
#else
    std::abort();
#endif
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

        // xmaudit3: a prefixed name test (ns:root / @xml:lang) requires an
        // XmlNamespaceManager; the string overload has none -> net48 throws
        // XPathException instead of silently missing.
        if (step.name.find(':') != std::string::npos)
        {
            ThrowXPath("prefixed XPath names require a namespace manager");
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

// xmaudit3: per-query set of elements under a NON-EMPTY default xmlns. The
// T1 tree has no parent links, so il2cpp_xml_query fills this by one DFS from
// the root before evaluating steps. Single-threaded runtime model: the vector
// is file-scope (no dangling on exception unwind) and rebuilt per query.
std::vector<const XmlNode*> g_defaultNsElements;

bool InDefaultNamespace(const XmlNode* node)
{
    return std::find(g_defaultNsElements.begin(), g_defaultNsElements.end(), node)
        != g_defaultNsElements.end();
}

// Element-name match (element nodes only; text/cdata have an empty name).
// xmaudit3: an unprefixed node test matches EMPTY-namespace elements only.
bool NameMatches(const XmlNode* node, const char* name)
{
    return node->kind == XmlNodeKind::element &&
           node->name == name &&
           !InDefaultNamespace(node);
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

// Document-order index of every tree node (DFS pre-order), rebuilt per query.
// XPath node-sets are in document order; element-step hits are sorted by this
// index so a descendant step emits DFS order instead of per-parent
// (parent-before-child) order.
std::vector<const XmlNode*> g_docOrderSeq;
std::unordered_map<const XmlNode*, int32_t> g_docOrderIndex;

void CollectDocOrder(const XmlNode* node, std::vector<const XmlNode*>& out)
{
    out.push_back(node);
    for (const XmlNode& child : node->children)
    {
        CollectDocOrder(&child, out);
    }
}

int32_t DocOrderIndex(const XmlNode* node)
{
    const auto it = g_docOrderIndex.find(node);
    return it == g_docOrderIndex.end() ? INT32_MAX : it->second;
}

// xmaudit3: mark every element whose effective default namespace is non-empty.
// An element is in the default namespace when it (or an ancestor) declares a
// non-empty `xmlns="uri"`; `xmlns=""` resets it to empty (XPath 1.0).
void CollectDefaultNsElements(const XmlNode* node, bool inDefaultNs,
                              std::vector<const XmlNode*>& out)
{
    bool effective = inDefaultNs;
    for (const XmlAttr& attr : node->attributes)
    {
        if (attr.name == "xmlns")
        {
            effective = !attr.value.empty();
        }
    }
    if (effective)
    {
        out.push_back(node);
    }
    for (const XmlNode& child : node->children)
    {
        if (child.kind == XmlNodeKind::element)
        {
            CollectDefaultNsElements(&child, effective, out);
        }
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
                // //@x: descendant-or-self::node()/@x -- the candidate's OWN
                // attributes are in scope too (net48); then every descendant
                // element's.
                targets.push_back(candidate);
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
        // xmaudit2: `//name` is descendant-or-self::node()/child::name, so a
        // positional predicate binds PER PARENT (b's second x, not the second
        // x of the merged list). Build the parent set: the candidate itself
        // (self::node()) plus every descendant element.
        std::vector<const XmlNode*> parents;
        parents.push_back(candidate);
        if (step.descendant)
        {
            CollectAllDescendants(candidate, parents);
        }
        for (const XmlNode* parent : parents)
        {
            std::vector<const XmlNode*> matched;
            for (const XmlNode& child : parent->children)
            {
                if (NameMatches(&child, step.name.c_str()))
                {
                    matched.push_back(&child);
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
    // XPath node-sets are in document order: sort the accumulated hits by the
    // DFS pre-order index (the per-parent [n] predicate is already applied).
    std::sort(out.begin(), out.end(), [](const XmlNode* a, const XmlNode* b) {
        return DocOrderIndex(a) < DocOrderIndex(b);
    });
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

    g_defaultNsElements.clear();
    CollectDefaultNsElements(root, false, g_defaultNsElements);

    g_docOrderSeq.clear();
    g_docOrderIndex.clear();
    CollectDocOrder(root, g_docOrderSeq);
    for (size_t i = 0; i < g_docOrderSeq.size(); ++i)
    {
        g_docOrderIndex[g_docOrderSeq[i]] = static_cast<int32_t>(i);
    }

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
        // The first step is // or @: candidates start at root. xmaudit2: the
        // runtime tree has NO explicit document node, so a leading `//name`
        // (document/descendant-or-self::node()/child::name) must ALSO match
        // the root element itself -- its real parent is the implicit document.
        current.push_back(root);
        std::vector<const XmlNode*> next;
        if (first.descendant && !first.attribute && NameMatches(root, first.name.c_str()))
        {
            next.push_back(root); // document-order first (deduped below)
        }
        std::vector<const XmlNode*> fromStep;
        ApplyStep(current, first, fromStep);
        next.insert(next.end(), fromStep.begin(), fromStep.end());
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
