namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Xml -- System.Xml.XmlDocument basic-domain mappings
// (src/runtime/include/internal/xml_doc.h -- il2cpp_xml_load_xml /
// il2cpp_xml_outer_xml; the T1/T2 parse+query stack is xml_ast.h/xml_query.h).
// Owners: System.Xml.XmlDocument
//
// HANDLE-GAP MODE (the "XmlDocument reduced to a handle" contract): the runtime xml
// domain (xml_parse/xml_query/xml_doc -- all green) has NO System_Xml_XmlDocument
// class descriptor, so XmlDocument is NOT an allocated object. The document IS
// its root node: the managed XmlDocument variable holds the XmlNode* handle
// returned by il2cpp_xml_load_xml, stored in the generated code's Il2CppObject*
// local via the ResultIsReceiver assignment (doc = load_xml(s) -- the emitter's
// reference-typed ResultIsReceiver branch, CodeGenerator EmitCallStmt).
//
//   .ctor()               -> il2cpp_object_ctor_nop (the newobj folds to
//                            nullptr via BclAllocExpr -- no descriptor, and a
//                            fresh XmlDocument has no document until LoadXml)
//   LoadXml(string)       -> il2cpp_xml_load_xml(Il2CppString*) -> root handle
//                            (ResultIsReceiver: the root lands IN the receiver
//                            local; casts ["", "s"] -- receiver dropped, the
//                            string passed as Il2CppString*; ReturnTypeName
//                            keeps the local tracked as "System.Xml.XmlDocument"
//                            so get_OuterXml resolves on it)
//   get_OuterXml          -> il2cpp_xml_outer_xml(const XmlNode*) -> string
//                            (receiver cast "node": the Il2CppObject*-typed
//                            handle slot is reinterpreted to the runtime node
//                            pointer the serializer takes)
//   SelectNodes(string)   -> il2cpp_xml_select_nodes(root, path) -> XmlNodeList
//                            (the il2cpp_xml_query XPath subset; the hit list
//                            is a managed object[] tracked as XmlNodeList)
//   SelectSingleNode(xpath)-> il2cpp_xml_select_single_node -> XmlNode handle
//                            (first hit or null; returnTypeName tracks XmlNode)
//   GetAttribute(string)  -> il2cpp_xml_get_attribute_s -> string (missing -> empty)
//   get_InnerText         -> il2cpp_xml_get_inner_text  -> string (all text)
//   get_InnerXml          -> il2cpp_xml_get_inner_xml   -> string (child markup)
//
// xml-b23 closure: the runtime surface is now the 5-member net48 face
// (xml_doc.h) -- the SelectNodes const char* marshalling gap was closed by
// taking the XPath as a managed Il2CppString* (the "s" cast) instead of a
// C string. XmlElement routes to the same mapping (GetAttribute is declared
// on XmlElement; see MethodRefResolver.cs).
//
// KNOWN LIMITATIONS (documented, handle-gap contract):
//   * The handle lives in a GC-ROOT reference local while raw XmlNode* memory
//     is NOT a managed object -- a GC collection interleaving LoadXml/OuterXml
//     would scan it as an object (gc.cpp MarkSlot pushes any non-null root).
//     Safe while no collection runs (the probe allocates too little to
//     trigger collect-then-retry); GC.Collect() between LoadXml and OuterXml
//     is UNSAFE -- the version-object closure is the fix (runtime side).
//   * LoadXml failure (malformed XML) -> nullptr handle; OuterXml then yields
//     null (the XmlException baseline is NOT thrown -- TryParse spirit).
//   * XPath syntax errors -> XPathException (propagated from the query engine).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapXml(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                // Parameterless only (the basic domain). The newobj already
                // folds to nullptr (BclAllocExpr) -- this no-op consumes the
                // receiver and does nothing else.
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, true);
                throw new NotSupportedException(
                    $"LOUD whitelist (no runtime descriptor): XmlDocument ctor with {count} param(s) -- net48's " +
                    "public ctors are () and (XmlNameTable) (xmldocument.cs:85,90); the runtime xml domain is a " +
                    "handle-gap model (xml_doc.h) with no XmlNameTable descriptor, so the parameterless form is the " +
                    "only wireable one [Reference Source System.Xml/System/Xml/Dom/XmlDocument.cs:85,90]");
            case "LoadXml":
                // LoadXml(string): parse into a new tree; the root handle
                // replaces the receiver local (ResultIsReceiver -- the
                // reference-typed assignment shape, see the file comment).
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_load_xml", 1, false, true, true,
                        new[] { "", "s" }, returnTypeName: "System.Xml.XmlDocument",
                        resultIsReceiver: true);
                return null;
            case "get_OuterXml":
                // OuterXml: serialize the receiver's tree to a managed string
                // (single line, entity-escaped -- .NET shape; comments dropped).
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_outer_xml", 0, true, true, true,
                        new[] { "node" });
                return null;
            // ---- xml-b23: the 5-member surface (net48 System.Xml face) ----
            case "SelectNodes":
                // XmlNodeList SelectNodes(string xpath): XPath minimal subset
                // (the il2cpp_xml_query engine) -- the hit list is a managed
                // object[] (returnTypeName tracks it as XmlNodeList so Count
                // etc. resolve). Il2CppString* path via the "s" cast.
                if (count == 1 && ps[0] == 0x0E)
                    // xmaudit2: the result is CONSUMED (XmlNodeList l = ...;
                    // l.Count; l[0]) -- rv must be true, like SelectSingleNode.
                    return I("il2cpp::runtime::il2cpp_xml_select_nodes", 1, true, true, true,
                        new[] { "node", "s" }, returnTypeName: "System.Xml.XmlNodeList");
                return null;
            case "SelectSingleNode":
                // XmlNode SelectSingleNode(string xpath): first hit or null.
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_select_single_node", 1, false, true, true,
                        new[] { "node", "s" }, returnTypeName: "System.Xml.XmlNode");
                return null;
            // xmaudit2 (codegen-decl): the XmlNodeList surface -- the
            // SelectNodes hit list is an object[] of node handles, so Count
            // reuses the array length and the indexer returns the slot with
            // net48 XmlNodeList semantics (ArgumentOutOfRangeException).
            case "get_Count":
                if (count == 0)
                    return I("il2cpp::il2cpp_array_get_length", 0, true, false, true, new[] { "a" });
                return null;
            case "get_ItemOf":
                if (count == 1 && ps.Length == 1 && ps[0] == 0x08)
                    return I("il2cpp::runtime::il2cpp_xml_nodelist_get_item", 1, true, true, true,
                        new[] { "", "i" }, returnTypeName: "System.Xml.XmlNode");
                return null;
            // xmlenum: XmlNodeList.GetEnumerator -> the snapshot enumerator
            // object over the SelectNodes hit array (the returnTypeName tracks
            // the enumerator pseudo-type so the IEnumerator::MoveNext /
            // get_Current calls of a foreach resolve on the receiver hint --
            // MethodMap.Collections.cs).
            case "GetEnumerator":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_nodelist_get_enumerator", 0, true, true, true,
                        new[] { "" }, returnTypeName: "System.Xml.XmlNodeList.Enumerator");
                return null;
            case "GetAttribute":
                // XmlElement.GetAttribute(string) -> string (missing -> empty).
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_get_attribute_s", 1, true, true, true,
                        new[] { "node", "s" });
                return null;
            case "get_InnerText":
                // InnerText: node + all descendant text (depth-first).
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_inner_text", 0, true, true, true,
                        new[] { "node" });
                return null;
            case "get_InnerXml":
                // InnerXml: child markup only (comments dropped).
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_inner_xml", 0, true, true, true,
                        new[] { "node" });
                return null;
            // ---- xml-b23 residual: the XmlNode member surface ----
            // get_DocumentElement: the tree root (the document handle IS the
            // root element -- handle-gap). get_FirstChild / get_Name /
            // get_NodeType: the node predicates.
            case "get_DocumentElement":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_document_element", 0, true, true, true,
                        new[] { "node" }, returnTypeName: "System.Xml.XmlElement");
                return null;
            case "get_FirstChild":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_first_child", 0, true, true, true,
                        new[] { "node" }, returnTypeName: "System.Xml.XmlNode");
                return null;
            case "get_Name":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_name", 0, true, true, true,
                        new[] { "node" });
                return null;
            case "get_NodeType":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_get_node_type", 0, true, false, true,
                        new[] { "node" });
                return null;
            default:
                throw new NotSupportedException(
                    $"LOUD whitelist (no runtime descriptor): System.Xml.XmlDocument member '{name}' -- the runtime " +
                    "xml domain is a handle-gap model (xml_doc.h) whose surface is the net48 face " +
                    "(.ctor/LoadXml/get_OuterXml/SelectNodes/SelectSingleNode/GetAttribute/get_InnerText/get_InnerXml); " +
                    "the remaining net48 members have no runtime primitive/descriptor " +
                    "[src/runtime/include/internal/xml_doc.h]");
        }
    }

    //------------------------------------------------------------------------------
    // D-XMLW-1: System.Xml.XmlWriter / XmlWriterSettings minimal write surface
    // (src/runtime/include/internal/xml_write.h -- xml_write.cpp). Handle-gap
    // mode: both values are raw C++ heap structs reinterpreted as Il2CppObject*.
    //
    //   XmlWriterSettings:
    //     .ctor()                    -> il2cpp_object_ctor_nop (the newobj allocates
    //                                   via the CodeGenerator BclAllocExpr entry
    //                                   il2cpp_xml_writer_settings_new)
    //     set_OmitXmlDeclaration(bool)-> il2cpp_xml_writer_settings_set_omit_declaration
    //     set_Indent(bool)           -> il2cpp_xml_writer_settings_set_indent
    //   XmlWriter:
    //     Create(StringBuilder)       -> il2cpp_xml_writer_create (returns the writer
    //                                   handle; a NON-StringBuilder target -- TextWriter
    //                                   -- is LOUD-rejected via arg0Type)
    //     Create(StringBuilder, XmlWriterSettings) -> il2cpp_xml_writer_create_s
    //     WriteStartElement(string)  -> il2cpp_xml_writer_write_start_element
    //     WriteAttributeString(string, string) -> il2cpp_xml_writer_write_attribute_string
    //     WriteElementString(string, string)   -> il2cpp_xml_writer_write_element_string
    //     WriteString(string)        -> il2cpp_xml_writer_write_string
    //     WriteEndElement()          -> il2cpp_xml_writer_write_end_element
    //     WriteEndDocument()         -> il2cpp_xml_writer_write_end_document
    //     Flush()                    -> il2cpp_xml_writer_flush
    //     Close()                    -> il2cpp_xml_writer_close
    //
    // The net48 oracle is byte-pinned by tests/cases/xmlwrite/expected.txt
    // (declaration `<?xml version="1.0" encoding="utf-16"?>`, CRLF + 2-space
    // indentation, entity escaping in text and attribute values).
    //------------------------------------------------------------------------------
    private static ResolvedMethod? TryMapXmlWriter(string owner, string name, byte[] ps, int count,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, true);
                throw new NotSupportedException(
                    $"LOUD whitelist (net48 has no such overload): XmlWriterSettings ctor with {count} param(s) -- " +
                    "net48's only PUBLIC ctor is the parameterless form (the other two are internal) " +
                    "[Reference Source System.Xml/System/Xml/Core/XmlWriterSettings.cs:101]");
            case "set_OmitXmlDeclaration":
                if (count == 1 && ps[0] == 0x02) // bool
                    return I("il2cpp::runtime::il2cpp_xml_writer_settings_set_omit_declaration", 1, false, false, true,
                        new[] { "", "b" });
                return null;
            case "set_Indent":
                if (count == 1 && ps[0] == 0x02) // bool
                    return I("il2cpp::runtime::il2cpp_xml_writer_settings_set_indent", 1, false, false, true,
                        new[] { "", "b" });
                return null;
            case "Create":
                // XmlWriter.Create(StringBuilder) / (StringBuilder, XmlWriterSettings).
                // The declared param type is System.IO.TextWriter (CLASS byte 0x12),
                // so ps cannot distinguish the target kind -- the tracked type of
                // the StringBuilder argument does: for the 1-arg form it is the
                // first stack value (arg0Type); for the 2-arg form the settings
                // is pushed LAST (the stack top = arg0Type), so the target is the
                // SECOND stack value (arg1Type). Only a StringBuilder target is
                // wired; a TextWriter/untracked target is LOUD-rejected (no silent
                // fallback to the generated-name path).
                if (count == 1 && ps[0] == 0x12)
                {
                    if (arg0Type == "System.Text.StringBuilder")
                        return I("il2cpp::runtime::il2cpp_xml_writer_create", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Xml.XmlWriter");
                    // The StringWriter target: the runtime writes into the
                    // StringWriter's backing StringBuilder (the sole content
                    // store), so StringWriter.ToString() returns the markup.
                    if (arg0Type == "System.IO.StringWriter")
                        return I("il2cpp::runtime::il2cpp_xml_writer_create_sw", 1, true, true, false,
                            new[] { "" }, returnTypeName: "System.Xml.XmlWriter");
                    throw new NotSupportedException(
                        $"LOUD whitelist (dynamic dispatch): XmlWriter.Create target type '{arg0Type ?? "untracked"}' -- " +
                        "the declared param type is TextWriter (CLASS), so the target kind is only known from the " +
                        "tracked argument type; only a StringBuilder / StringWriter target has a runtime icall " +
                        "[src/runtime/include/internal/xml_write.h]");
                }
                if (count == 2 && ps[0] == 0x12 && ps[1] == 0x12)
                {
                    if (arg1Type == "System.Text.StringBuilder")
                        return I("il2cpp::runtime::il2cpp_xml_writer_create_s", 2, true, true, false,
                            new[] { "", "" }, returnTypeName: "System.Xml.XmlWriter");
                    if (arg1Type == "System.IO.StringWriter")
                        return I("il2cpp::runtime::il2cpp_xml_writer_create_sw_s", 2, true, true, false,
                            new[] { "", "" }, returnTypeName: "System.Xml.XmlWriter");
                    throw new NotSupportedException(
                        $"LOUD whitelist (dynamic dispatch): XmlWriter.Create target type '{arg1Type ?? "untracked"}' -- " +
                        "the declared param type is TextWriter (CLASS), so the target kind is only known from the " +
                        "tracked argument type; only a StringBuilder / StringWriter target has a runtime icall " +
                        "[src/runtime/include/internal/xml_write.h]");
                }
                return null;
            case "WriteStartElement":
                if (count == 1 && ps[0] == 0x0E) // string
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_start_element", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteAttributeString":
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_attribute_string", 2, false, false, true,
                        new[] { "", "s", "s" });
                return null;
            case "WriteElementString":
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_element_string", 2, false, false, true,
                        new[] { "", "s", "s" });
                return null;
            case "WriteString":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_string", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteStartDocument":
                // WriteStartDocument() / WriteStartDocument(bool standalone):
                // emit the XML declaration eagerly (the lazy declaration in
                // WriteStartElement is suppressed). The standalone flag rides
                // the int32 carrier; the 0-arg form appends 0.
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_start_document", 0, false, false, true,
                        new[] { "" }, appendLiterals: new[] { "0" });
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_start_document", 1, false, false, true,
                        new[] { "", "i" });
                return null;
            case "WriteFullEndElement":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_full_end_element", 0, false, false, true,
                        new[] { "" });
                return null;
            case "WriteCData":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_cdata", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteComment":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_comment", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteWhitespace":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_whitespace", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteEntityRef":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_entity_ref", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "WriteRaw":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_raw", 1, false, false, true,
                        new[] { "", "s" });
                return null;
            case "get_WriteState":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_get_write_state", 0, true, false, true,
                        new[] { "" });
                return null;
            case "WriteEndElement":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_end_element", 0, false, false, true,
                        new[] { "" });
                return null;
            case "WriteEndDocument":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_write_end_document", 0, false, false, true,
                        new[] { "" });
                return null;
            case "Flush":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_flush", 0, false, false, true,
                        new[] { "" });
                return null;
            case "Close":
                if (count == 0)
                    return I("il2cpp::runtime::il2cpp_xml_writer_close", 0, false, false, true,
                        new[] { "" });
                return null;
            default:
                throw new NotSupportedException(
                    $"LOUD whitelist (no runtime descriptor): System.Xml.XmlWriter/XmlWriterSettings member '{name}' -- " +
                    "the runtime xml_write.h surface is the minimal write face (Create/WriteStartElement/" +
                    "WriteAttributeString/WriteElementString/WriteString/WriteEndElement/WriteEndDocument/Flush/" +
                    "Close + XmlWriterSettings(.ctor/set_OmitXmlDeclaration/set_Indent)); the remaining net48 " +
                    "members have no runtime primitive/descriptor [src/runtime/include/internal/xml_write.h]");
        }
    }
}

