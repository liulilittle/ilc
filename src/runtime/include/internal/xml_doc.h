#pragma once
//------------------------------------------------------------------------------
// xml_doc.h -- XmlDocument basic-domain surface (T3: LoadXml + OuterXml).
//
// Mirrors the two entry points the transpiler maps System.Xml.XmlDocument
// member calls onto, in the runtime TryParse spirit (no throws -- failure
// is signalled by the return value):
//
//   * il2cpp_xml_load_xml:  XmlDocument.LoadXml(string) -- parses a managed
//     UTF-16 string into a tree (the T1 AST, xml_ast.h). Parsing itself is
//     delegated to the T1 contract il2cpp_xml_parse; this function only
//     transcodes the managed string to UTF-8 and owns the root allocation.
//   * il2cpp_xml_outer_xml: XmlDocument.OuterXml -- serializes the tree
//     back to a managed string: elements/attributes/text/CDATA with entity
//     escaping and NO indentation (single-line -- the .NET OuterXml shape,
//     "indentation simplified" per the domain card).
//
// The pair round-trips: Serialize(Parse(x)) is a FIXED POINT, so
// OuterXml(LoadXml(s)) is a stable normalized form of 's' -- comments and
// the XML declaration are dropped, empty elements render self-closing.
// (Comments ARE stored in the tree by the parser; only the serialization
// drops them.)
//------------------------------------------------------------------------------

#include "il2cpp_array.h"  // Il2CppArray (SelectNodes result)
#include "il2cpp_string.h" // il2cpp::Il2CppString / il2cpp_string_new
#include "xml_ast.h"       // il2cpp::XmlNode / parse contract

namespace il2cpp { namespace runtime {

// LoadXml(string): parses the managed string into a new tree and returns
// the root node. Returns nullptr for null input or malformed XML (the
// XmlException baseline is signalled as nullptr -- nothing is thrown).
// The root is heap-allocated: release it with il2cpp_xml_node_free.
il2cpp::XmlNode* il2cpp_xml_load_xml(il2cpp::Il2CppString* input);

// OuterXml(root): serializes the tree to a managed string (single line,
// entity-escaped; comment nodes are skipped). Returns nullptr for null
// 'node'.
il2cpp::Il2CppString* il2cpp_xml_outer_xml(const il2cpp::XmlNode* node);

// SelectNodes(root, path): evaluates the XPath minimal subset (see
// xml_query.h) against 'root' and returns the array of hit nodes. 'path'
// is a managed string (UTF-16), transcoded to UTF-8 internally. Same
// return-value contract as il2cpp_xml_query: element/text/cdata hits are
// pointers INTO the tree; attribute hits are new-allocated node copies the
// caller releases with il2cpp_xml_node_free. Throws ArgumentNullException
// for null 'root'/'path' and XPathException for a syntax error.
Il2CppArray* il2cpp_xml_select_nodes(const il2cpp::XmlNode* root,
                                     il2cpp::Il2CppString* path);

// xmaudit2 (codegen-decl): XmlNodeList indexer -- the SelectNodes hit list
// is an object[] whose slots are XmlNode* handles. A null list throws
// NullReferenceException; an index outside [0, Count) yields nullptr (the
// net48 XPathNodeList.Item contract for SelectNodes lists -- verified by
// the xmlnodelistops oracle, NOT ArgumentOutOfRangeException).
Il2CppObject* il2cpp_xml_nodelist_get_item(Il2CppObject* list, int32_t index);

// xmlenum: XmlNodeList enumeration (foreach over SelectNodes). The list is
// the managed object[] of node handles (see get_item above); the enumerator
// is a snapshot object wrapping that array:
//
//   struct Il2CppXmlNodeListEnumerator { obj; nodes (GC); int32 index }
//
// net48 reference semantics (xmlenum net48 oracle -- see LANDING-XMLENUM.md):
//   * document order; each GetEnumerator returns a fresh cursor over the
//     frozen hit array;
//   * get_Current before the first MoveNext / after the end yields NULL (NOT
//     InvalidOperationException -- System.Xml.XmlNodeListEnumerator);
//   * Reset rewinds the cursor (net48 Reset is supported);
//   * the enumerator is NOT IDisposable (foreach over the non-generic
//     IEnumerator emits no Dispose).
// The net48 lazy-materialization timing (a document mutation between
// SelectNodes and the first enumeration access becomes visible) is NOT
// reproducible here: the ilc xml domain has no document-mutation surface and
// the query array is frozen when SelectNodes runs (documented limitation).
struct Il2CppXmlNodeListEnumerator
{
    Il2CppObject obj;     // slot 0: object header
    Il2CppArray*  nodes;  // slot 1: SelectNodes hit array (GC ref, bitmap bit 1)
    int32_t       index;  // cursor: -1 = before the first element
};

// XmlNodeList.GetEnumerator() -> the snapshot enumerator object (allocated
// through il2cpp_object_new; null list throws NullReferenceException).
Il2CppObject* il2cpp_xml_nodelist_get_enumerator(Il2CppObject* list);

// IEnumerator::MoveNext on the snapshot enumerator: advances the cursor,
// true while it is inside the array (int32 bool carrier).
int32_t il2cpp_xml_nodelist_enumerator_move_next(Il2CppObject* self);

// IEnumerator::get_Current: the node handle at the cursor, or nullptr when
// the cursor is before the first element / past the end (net48
// XmlNodeListEnumerator contract -- verified by the xmlenumops oracle).
Il2CppObject* il2cpp_xml_nodelist_enumerator_get_current(Il2CppObject* self);

// IEnumerator::Reset: rewinds the cursor to before the first element.
void il2cpp_xml_nodelist_enumerator_reset(Il2CppObject* self);

// SelectSingleNode(root, path): the FIRST hit of SelectNodes, or nullptr
// when nothing matches (net48 SelectSingleNode returns null on a miss).
// Element/text/cdata hits are tree pointers; an attribute hit is a
// new-allocated copy the caller releases with il2cpp_xml_node_free.
// (Returns the node handle itself -- the same handle shape as
// il2cpp_xml_load_xml.)
il2cpp::XmlNode* il2cpp_xml_select_single_node(const il2cpp::XmlNode* root,
                                               il2cpp::Il2CppString* path);

// GetAttribute(node, name): the attribute value as a managed string; a
// MISSING attribute yields the EMPTY string (net48 GetAttribute returns
// String.Empty, not null). 'name' is a managed string. Throws
// ArgumentNullException for null 'node'/'name'.
Il2CppObject* il2cpp_xml_get_attribute_s(const il2cpp::XmlNode* node,
                                         il2cpp::Il2CppString* name);

// InnerText(node): net48 XmlNode.InnerText -- the concatenated text of the
// node and ALL its descendants, depth-first (text + CDATA; comments and
// element structure skipped). Empty content -> the empty string (net48
// InnerText returns String.Empty, not null). Throws ArgumentNullException
// for null 'node'.
Il2CppObject* il2cpp_xml_get_inner_text(const il2cpp::XmlNode* node);

// InnerXml(node): net48 XmlNode.InnerXml -- the markup of the node's
// CHILDREN only (the node's own tag is NOT emitted): each child is
// serialized single-line entity-escaped exactly as OuterXml renders it
// (comments dropped, empty elements self-closing -- the OuterXml fixed
// point). A text/cdata node returns its own content (text escaped, CDATA
// verbatim); empty content -> the empty string. Throws
// ArgumentNullException for null 'node'.
Il2CppObject* il2cpp_xml_get_inner_xml(const il2cpp::XmlNode* node);

// XmlDocument.get_DocumentElement(): the tree root node (the parser produces a
// single root element, so the document handle IS the root element in the
// handle-gap model). Returns nullptr for a null document.
il2cpp::XmlNode* il2cpp_xml_get_document_element(const il2cpp::XmlNode* root);

// XmlNode.getFirstChild(): the first child node of an element, or nullptr
// (a null node / a node without children).
il2cpp::XmlNode* il2cpp_xml_get_first_child(const il2cpp::XmlNode* node);

// XmlNode.getName(): the node name -- the element tag name for an element
// node; "#text" / "#cdata-section" / "#comment" for the other kinds; the
// document handle's own name is "#document" (net48 XmlDocument.Name).
Il2CppObject* il2cpp_xml_get_name(const il2cpp::XmlNode* node);

// XmlNode.getNodeType(): the net48 System.Xml.XmlNodeType value (Document 9 /
// Element 1 / Text 3 / CDATA 4 / Comment 8).
int32_t il2cpp_xml_get_node_type(const il2cpp::XmlNode* node);

// Release a tree root returned by il2cpp_xml_load_xml. Null-safe.
void il2cpp_xml_node_free(il2cpp::XmlNode* root);

}} // namespace il2cpp::runtime

//------------------------------------------------------------------------------
// GLOBAL-scope class descriptor for the XmlNodeList snapshot enumerator
// (xmlenum). Same contract as System_Collections_Stack_Enumerator_class
// (stackqueue.h): defined at global scope in xml_doc.cpp with external
// linkage, lazily registered by il2cpp_xml_nodelist_get_enumerator. The
// descriptor describes Il2CppXmlNodeListEnumerator (bitmap bit 1 = nodes).
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Xml_XmlNodeListEnumerator_class;
