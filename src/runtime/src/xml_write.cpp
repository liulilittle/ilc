//------------------------------------------------------------------------------
// xml_write.cpp -- System.Xml.XmlWriter minimal write surface (D-XMLW-1).
//
// See xml_write.h for the contract, net48 oracle and GC-safety notes. In
// short: both XmlWriter and XmlWriterSettings are raw C++ heap structs
// (handle-gap mode); the writer accumulates UTF-16 output and Flush/Close
// append it to the captured managed StringBuilder via the existing
// icall_stringbuilder_append_s.
//
// The writer is a small state machine:
//   * an element stack (name + has_child_element per open frame);
//   * a start-tag flag -- WriteStartElement emits `<name` and WAITS (so
//     WriteAttributeString can append) until content or an end tag closes
//     the '>' (net48 shape: `<root id="1">`).
//   * indentation (net48 Indent=true): before a new element/end tag, when
//     enabled, emit CRLF + 2 spaces per depth -- an element's end tag is
//     indented ONLY when the element CONTAINS child elements (net48: text
//     content keeps the end tag on the same line: `<x>v</x>`).
//
// The transcoding story is trivial: every input is a managed UTF-16 string
// and the target StringBuilder stores UTF-16 units, so the writer works
// ENTIRELY in char16_t -- no UTF-8 transcode anywhere.
//------------------------------------------------------------------------------

#include "xml_write.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_invalid_operation_exception
#include "il2cpp_gc.h"           // il2cpp_gc_add_root_range / remove (target root)
#include "il2cpp_object.h"       // il2cpp_null_check (NRE baseline)
#include "il2cpp_string.h"       // il2cpp_string_new (flush output string)
#include "il2cpp_stringbuilder.h" // icall_stringbuilder_append_s (target append)

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

namespace {

struct XmlWriterSettingsImpl
{
    bool omit_declaration = false;
    bool indent = false;
};

struct XmlWriterFrame
{
    std::u16string name;
    bool has_child_element = false;
};

// The writer struct. `target` is a managed StringBuilder reference held as a
// GC ROOT RANGE (registered at Create, removed at Close): a compacting
// collection rewrites the field through Phase C, so Flush/Close always read
// the CURRENT builder address.
struct XmlWriterImpl
{
    il2cpp::Il2CppObject* target = nullptr;
    bool root_registered = false;
    bool omit_declaration = false;
    bool indent = false;
    bool wrote_decl = false;
    bool start_tag_open = false;       // the top frame emitted `<name` but not yet '>'
    bool closed = false;               // Close ran: Write*/Flush throw, Close is a no-op
    std::u16string out;                // accumulated UTF-16 output
    std::vector<XmlWriterFrame> stack; // open element frames
};

XmlWriterImpl* AsWriter(il2cpp::Il2CppObject* writer)
{
    return reinterpret_cast<XmlWriterImpl*>(il2cpp::il2cpp_null_check(writer));
}

// Closed-writer guard. net48: every Write*/Flush on a closed writer throws
// InvalidOperationException ("The Writer is closed or in error state.");
// Close itself is idempotent -- see il2cpp_xml_writer_close.
[[noreturn]] void ThrowWriterClosed()
{
    il2cpp::il2cpp_throw_invalid_operation_exception(
        "The Writer is closed or in error state.");
    std::abort(); // defensive backstop (il2cpp_throw_... never returns)
}

void ThrowIfClosed(XmlWriterImpl* w)
{
    if (w->closed)
    {
        ThrowWriterClosed();
    }
}

void AppendName(std::u16string& out, const il2cpp::Il2CppString* name)
{
    if (name != nullptr)
    {
        out.append(name->chars, static_cast<size_t>(name->length));
    }
}

// Text content: escape & < > (plus C0 control characters other than
// tab/LF/CR, which net48 re-escapes as &#xN;).
void AppendEscapedText(std::u16string& out, const char16_t* chars, int32_t length)
{
    for (int32_t i = 0; i < length; ++i)
    {
        const char16_t c = chars[i];
        if (c == u'&')
        {
            out += u"&amp;";
        }
        else if (c == u'<')
        {
            out += u"&lt;";
        }
        else if (c == u'>')
        {
            out += u"&gt;";
        }
        else if (c < 0x20 && c != u'\t' && c != u'\n' && c != u'\r')
        {
            char buf[8];
            std::snprintf(buf, sizeof(buf), "&#x%X;", static_cast<unsigned>(c));
            for (const char* p = buf; *p != '\0'; ++p)
            {
                out.push_back(static_cast<char16_t>(*p));
            }
        }
        else
        {
            out.push_back(c);
        }
    }
}

// Attribute values: additionally escape '"' (the value delimiter). A single
// quote stays literal inside "..." attributes (net48, pinned by the probe).
void AppendEscapedAttr(std::u16string& out, const char16_t* chars, int32_t length)
{
    for (int32_t i = 0; i < length; ++i)
    {
        const char16_t c = chars[i];
        if (c == u'&')
        {
            out += u"&amp;";
        }
        else if (c == u'<')
        {
            out += u"&lt;";
        }
        else if (c == u'>')
        {
            out += u"&gt;";
        }
        else if (c == u'"')
        {
            out += u"&quot;";
        }
        else
        {
            out.push_back(c);
        }
    }
}

// net48 Indent: CRLF + 2 spaces per depth, emitted only when the output is
// non-empty and not already at line start (the first element of the document
// starts at column 0 -- pinned by the probe's StartsWith("<r>")).
void WriteIndent(XmlWriterImpl* w, size_t depth)
{
    if (!w->indent || w->out.empty())
    {
        return;
    }
    if (w->out.back() == u'\n')
    {
        return;
    }
    w->out += u"\r\n";
    for (size_t i = 0; i < depth; ++i)
    {
        w->out += u"  ";
    }
}

// Close the top frame's open start tag ('>') -- the moment content or a
// child element arrives (net48 `<root id="1">` shape).
void CloseStartTag(XmlWriterImpl* w)
{
    if (w->start_tag_open)
    {
        w->out.push_back(u'>');
        w->start_tag_open = false;
    }
}

void EmitEndTag(XmlWriterImpl* w, const std::u16string& name)
{
    w->out += u"</";
    w->out += name;
    w->out.push_back(u'>');
}

} // namespace

namespace il2cpp { namespace runtime {

Il2CppObject* il2cpp_xml_writer_settings_new()
{
    return reinterpret_cast<Il2CppObject*>(new XmlWriterSettingsImpl);
}

void il2cpp_xml_writer_settings_set_omit_declaration(Il2CppObject* self, bool value)
{
    reinterpret_cast<XmlWriterSettingsImpl*>(self)->omit_declaration = value;
}

void il2cpp_xml_writer_settings_set_indent(Il2CppObject* self, bool value)
{
    reinterpret_cast<XmlWriterSettingsImpl*>(self)->indent = value;
}

Il2CppObject* il2cpp_xml_writer_create(Il2CppObject* target)
{
    auto* w = new XmlWriterImpl;
    w->target = target;
    il2cpp_gc_add_root_range(reinterpret_cast<void**>(&w->target), 1);
    w->root_registered = true;
    return reinterpret_cast<Il2CppObject*>(w);
}

Il2CppObject* il2cpp_xml_writer_create_s(Il2CppObject* target, Il2CppObject* settings)
{
    auto* w = new XmlWriterImpl;
    w->target = target;
    if (settings != nullptr)
    {
        const auto* s = reinterpret_cast<const XmlWriterSettingsImpl*>(settings);
        w->omit_declaration = s->omit_declaration;
        w->indent = s->indent;
    }
    il2cpp_gc_add_root_range(reinterpret_cast<void**>(&w->target), 1);
    w->root_registered = true;
    return reinterpret_cast<Il2CppObject*>(w);
}

void il2cpp_xml_writer_write_start_element(Il2CppObject* writer, Il2CppString* name)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    // The declaration is emitted before the FIRST element (net48 lazy emit).
    if (!w->wrote_decl && !w->omit_declaration)
    {
        w->out += u"<?xml version=\"1.0\" encoding=\"utf-16\"?>";
        w->wrote_decl = true;
    }
    CloseStartTag(w);
    if (!w->stack.empty())
    {
        w->stack.back().has_child_element = true; // this new element is a child
    }
    WriteIndent(w, w->stack.size());
    w->out.push_back(u'<');
    AppendName(w->out, name);
    w->stack.push_back(XmlWriterFrame{ std::u16string(
        name != nullptr ? name->chars : nullptr,
        name != nullptr ? static_cast<size_t>(name->length) : 0u),
        false });
    w->start_tag_open = true;
}

void il2cpp_xml_writer_write_attribute_string(Il2CppObject* writer, Il2CppString* name,
                                              Il2CppString* value)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    w->out.push_back(u' ');
    AppendName(w->out, name);
    w->out += u"=\"";
    if (value != nullptr)
    {
        AppendEscapedAttr(w->out, value->chars, value->length);
    }
    w->out.push_back(u'"');
}

void il2cpp_xml_writer_write_element_string(Il2CppObject* writer, Il2CppString* name,
                                            Il2CppString* value)
{
    il2cpp_xml_writer_write_start_element(writer, name);
    il2cpp_xml_writer_write_string(writer, value);
    il2cpp_xml_writer_write_end_element(writer);
}

void il2cpp_xml_writer_write_string(Il2CppObject* writer, Il2CppString* text)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    if (text == nullptr || text->length <= 0)
    {
        // net48 oracle: WriteString("") / WriteString(null) is a NO-OP -- the
        // start tag stays open, so WriteElementString(name, "") still emits
        // the self-closing form `<name />` (xw_empty probe).
        return;
    }
    CloseStartTag(w);
    AppendEscapedText(w->out, text->chars, text->length);
}

void il2cpp_xml_writer_write_end_element(Il2CppObject* writer)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    if (w->stack.empty())
    {
        return; // net48 would throw InvalidOperationException; minimal face no-ops
    }
    const XmlWriterFrame frame = w->stack.back();
    w->stack.pop_back();
    if (w->start_tag_open)
    {
        // No content was written after the start tag -> self-closing
        // `<name />` (net48 shape: a space before the slash).
        w->start_tag_open = false;
        w->out += u" />";
    }
    else if (frame.has_child_element && w->indent)
    {
        // The end tag of an element WITH child elements goes on its own line
        // (indented at the element's own depth = the now-parent stack size).
        WriteIndent(w, w->stack.size());
        EmitEndTag(w, frame.name);
    }
    else
    {
        // Text content keeps the end tag on the same line: `<x>v</x>`.
        EmitEndTag(w, frame.name);
    }
}

void il2cpp_xml_writer_write_end_document(Il2CppObject* writer)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    while (!w->stack.empty())
    {
        il2cpp_xml_writer_write_end_element(writer);
    }
}

void il2cpp_xml_writer_flush(Il2CppObject* writer)
{
    XmlWriterImpl* w = AsWriter(writer);
    ThrowIfClosed(w);
    if (w->out.empty())
    {
        return;
    }
    // The target field is a registered GC ROOT RANGE, so any collection that
    // ran during the string allocation already rewrote it (Phase C): the read
    // below is always the CURRENT builder address.
    Il2CppObject* output = reinterpret_cast<Il2CppObject*>(
        il2cpp::il2cpp_string_new(w->out.data(), static_cast<int32_t>(w->out.size())));
    icall_stringbuilder_append_s(w->target, output);
    w->out.clear();
}

void il2cpp_xml_writer_close(Il2CppObject* writer)
{
    XmlWriterImpl* w = AsWriter(writer);
    if (w->closed)
    {
        return; // net48: Close is idempotent (the second call is a no-op)
    }
    while (!w->stack.empty())
    {
        il2cpp_xml_writer_write_end_element(writer);
    }
    il2cpp_xml_writer_flush(writer);
    if (w->root_registered)
    {
        il2cpp_gc_remove_root_range(reinterpret_cast<void**>(&w->target));
        w->root_registered = false;
    }
    // The struct is NOT deleted: the handle must stay dereferenceable so a
    // second Close (no-op) or a use-after-Close Write* (InvalidOperationException)
    // can be classified instead of touching freed memory (xmaudit4: the old
    // delete-on-Close was a double-free -> 0xC0000374). The dormant closed
    // writer is a small process-lifetime allocation (handle-gap contract).
    w->closed = true;
}

}} // namespace il2cpp::runtime
