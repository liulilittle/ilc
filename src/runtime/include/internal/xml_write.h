#pragma once
//------------------------------------------------------------------------------
// xml_write.h -- System.Xml.XmlWriter minimal write surface (D-XMLW-1).
//
// Handle-gap mode (the MethodMap.Xml.cs contract): the runtime has NO
// System_Xml_XmlWriter / System_Xml_XmlWriterSettings class descriptors, so
// both values are raw C++ heap structs reinterpeted as Il2CppObject* in the
// generated code (the same shape as the XmlNode handle the xml_doc domain
// uses -- the managed local carries the raw pointer).
//
//   * XmlWriterSettings: il2cpp_xml_writer_settings_new allocates a struct
//     holding the two settings the minimal face exposes (OmitXmlDeclaration
//     + Indent); the property setters write the booleans into it.
//   * XmlWriter: il2cpp_xml_writer_create / _s allocate the writer struct,
//     capturing the output TARGET (a managed StringBuilder -- the ONLY wired
//     Create target) and the settings. The write members accumulate the
//     escaped, formatted output as raw UTF-16 units; Flush/Close append the
//     accumulated buffer to the target StringBuilder through the existing
//     icall_stringbuilder_append_s.
//
// net48 ORACLE (byte-pinned by tests/cases/xmlwrite/expected.txt):
//   * Default settings -> a single-line document WITH the xml declaration,
//     `<?xml version="1.0" encoding="utf-16"?>` (a StringBuilder target is
//     UTF-16, so the declaration names utf-16 -- net48 baseline).
//   * OmitXmlDeclaration=true + Indent=true -> no declaration, an indented
//     tree using CRLF line endings and 2-space indentation.
//   * Entity escaping: < > & are escaped in text; < > & " additionally in
//     attribute values (' stays literal inside "..." attributes).
//
// GC SAFETY (stronger than the raw-handle baseline): the writer struct's
// `target` field is registered as a GC ROOT RANGE at Create
// (il2cpp_gc_add_root_range -- the same mechanism managed frames use), so a
// compacting collection rewrites the field (Phase C) and Flush/Close always
// read the CURRENT builder address -- even across the output-string
// allocation Flush performs. The range is removed at Close (1:1 pairing)
// BEFORE the writer struct is deleted.
//
// KNOWN LIMITATION (documented, minimal face): only a StringBuilder target
// is wired (the TextWriter overloads are LOUD-rejected by the resolver).
// The writer handle is INVALID after Close (the struct is deleted). Null
// writer receivers abort loudly via il2cpp_null_check (NRE baseline).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"

namespace il2cpp { namespace runtime {

// XmlWriterSettings() factory: a raw settings struct (handle-gap). Returns
// the struct pointer reinterpreted as Il2CppObject*.
Il2CppObject* il2cpp_xml_writer_settings_new();

// set_OmitXmlDeclaration(bool): net48 false by default. A true value
// suppresses the `<?xml ...?>` declaration at the first element.
void il2cpp_xml_writer_settings_set_omit_declaration(Il2CppObject* self, bool value);

// set_Indent(bool): net48 false by default. A true value switches the writer
// to CRLF + 2-space-per-depth indentation.
void il2cpp_xml_writer_settings_set_indent(Il2CppObject* self, bool value);

// XmlWriter.Create(StringBuilder): default settings (declaration on, no
// indent). 'target' must be a managed System.Text.StringBuilder (the only
// wired target -- a TextWriter target never reaches this icall; the resolver
// LOUD-rejects it). Registers the target GC root range. Returns the writer
// handle (raw struct as Il2CppObject*).
Il2CppObject* il2cpp_xml_writer_create(Il2CppObject* target);

// XmlWriter.Create(StringBuilder, XmlWriterSettings): the 2-arg form; a null
// settings arg behaves like default settings.
Il2CppObject* il2cpp_xml_writer_create_s(Il2CppObject* target, Il2CppObject* settings);

// WriteStartElement(string): opens a start tag (attributes may follow);
// writes the xml declaration first (unless omitted) and the indentation.
void il2cpp_xml_writer_write_start_element(Il2CppObject* writer, Il2CppString* name);

// WriteAttributeString(string, string): appends ` name="value"` to the open
// start tag (entity-escaped value; ' stays literal). Only valid immediately
// after WriteStartElement (the minimal face does not validate this).
void il2cpp_xml_writer_write_attribute_string(Il2CppObject* writer, Il2CppString* name,
                                              Il2CppString* value);

// WriteElementString(string, string): WriteStartElement + WriteString +
// WriteEndElement in one call.
void il2cpp_xml_writer_write_element_string(Il2CppObject* writer, Il2CppString* name,
                                            Il2CppString* value);

// WriteString(string): appends entity-escaped TEXT content.
void il2cpp_xml_writer_write_string(Il2CppObject* writer, Il2CppString* text);

// WriteEndElement(): closes the innermost element (`<name>text</name>` for
// content, `<name></name>` when no content was written). With Indent, the
// end tag of an element that CONTAINS child elements goes on its own line.
void il2cpp_xml_writer_write_end_element(Il2CppObject* writer);

// WriteEndDocument(): closes every still-open element (WriteEndElement until
// the stack is empty).
void il2cpp_xml_writer_write_end_document(Il2CppObject* writer);

// Flush(): appends the accumulated output to the target StringBuilder and
// clears the buffer (a later Close/Flush appends only NEW output).
void il2cpp_xml_writer_flush(Il2CppObject* writer);

// Close(): closes any remaining elements, flushes, removes the GC root range
// and deletes the writer struct. The handle is INVALID afterwards.
void il2cpp_xml_writer_close(Il2CppObject* writer);

}} // namespace il2cpp::runtime
