#pragma once
//------------------------------------------------------------------------------
// xml_query.h -- XML query API (T2: XPath simplified subset, based on the
// T1 contract xml_ast.h).
//
// Supported XPath subset (absolute paths only):
//
//   /root/child/...     element path level by level (the first step matches
//                       the passed-in root's own name)
//   //item              descendant element at any depth (depth >= 1; root
//                       itself is not matched)
//   /root//item         any-depth descendant item of root
//   item[n]             positional predicate (1-based): each source
//                       candidate keeps its n-th match in document order;
//                       fewer than n -> no result for that candidate
//   @name               attribute step (only the last step): hits the
//                       attribute (see the return-value contract)
//   //item/@attr        the attr attribute of every item
//
// Semantic contract (aligned with the T1 tree model):
//   * The tree is read-only (the product of il2cpp_xml_parse /
//     il2cpp_xml_load_xml; after construction children/attributes no longer
//     change, so node addresses are stable).
//   * Element steps match nodes with kind==element; text/cdata nodes are
//     never matched.
//   * [n] is 1-based; [0], negative numbers, non-digits and unclosed
//     brackets -> syntax error.
//   * Attribute predicates [@a="b"], relative paths and the * wildcard are
//     outside this subset -> syntax error.
//
// Error contract: root/path/node/name == nullptr -> ArgumentNullException;
// syntax error -> XPathException (both are catchable DotNetException
// carriers, see il2cpp_exception.h). A semantic miss (valid path, no
// match) -> empty array / nullptr, no exception is thrown.
//
// Return-value contract (il2cpp_xml_query): Il2CppArray*, elements are
// XmlNode* pointers (isRefElem=false, the GC does not scan them -- pointer
// ownership is managed by the C++ side):
//   * element/text/cdata hits -> pointers into the original tree (valid as
//     long as the tree is alive; the tree is created by
//     il2cpp_xml_load_xml and released by il2cpp_xml_node_free, see
//     xml_doc.h).
//   * attribute hits -> query-layer new-allocated node copies
//     { kind: element, name: "@" + attribute name, text: attribute value,
//     no attributes, no children }; the caller releases them with
//     il2cpp_xml_node_free (document order; hit order == array order).
//------------------------------------------------------------------------------

#include "xml_ast.h"

#include "il2cpp_array.h"  // Il2CppArray
#include "il2cpp_object.h" // Il2CppObject

namespace il2cpp { namespace runtime {

// Evaluate the XPath subset 'path' against the 'root' tree and return the
// array of hit nodes (elements are XmlNode*, see the top return-value
// contract). A miss returns an empty array (length 0).
Il2CppArray* il2cpp_xml_query(const il2cpp::XmlNode* root, const char* path);

// Return the value of the attribute 'name' on 'node' (managed string); no
// such attribute -> nullptr. 'node' is an element node (attribute-hit
// copies have empty attributes -> nullptr).
Il2CppObject* il2cpp_xml_get_attribute(const il2cpp::XmlNode* node,
                                       const char* name);

// Return the text content of 'node' (managed string):
//   element node -> the direct text child nodes (text/cdata) concatenated
//                   in document order; none -> nullptr
//   attribute hit -> its text (the attribute value)
//   text/cdata node -> its text content
Il2CppObject* il2cpp_xml_get_text(const il2cpp::XmlNode* node);

}} // namespace il2cpp::runtime
