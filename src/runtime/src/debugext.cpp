//------------------------------------------------------------------------------
// debugext.cpp -- System.Diagnostics.Debug / System.Diagnostics.Trace stubs
// (see debugext.h for the full contract).
//
// Deliberate simplification: a failed assertion prints to stderr and the
// program CONTINUES. The .NET default Debug.Assert shows a message box (or
// terminates) when assertions are enabled -- replicating that would break
// AOT programs that ship with debug leftovers. Print + continue keeps them
// runnable while surfacing the failure.
//
// Output streams: diagnostics -> stdout (the console module's stream, so
// generated-code output stays byte-ordered), assertion failures -> stderr.
//------------------------------------------------------------------------------

#include "debugext.h"
#include "il2cpp_pinvoke_ext.h"
#include "il2cpp_stringbuilder.h"    // icall_string_format (the composite-format kernel)

#include <cstdio>

namespace il2cpp { namespace runtime {

namespace {

// Print a managed string to the given stream in full UTF-8 (pinvokeext
// encoder: astral pairs + U+FFFD for lone surrogates -- .NET marshalling
// semantics). Defensive null handling: callers may pass a null string (e.g.
// a Debug.Assert with no message argument); print "(null)" instead of
// dereferencing it. The converted buffer is freed after the write.
void WriteManagedText(FILE* stream, Il2CppString* text, bool newline)
{
    if (text != nullptr)
    {
        char* utf8 = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(text);
        std::fputs(utf8, stream);
        il2cpp::il2cpp_pinvoke_utf8_free(utf8);
    }
    else
    {
        std::fputs("(null)", stream);
    }
    if (newline)
        std::fputc('\n', stream);
}

} // namespace

void il2cpp_debug_assert(int32_t condition, Il2CppString* message)
{
    if (condition == 0)
    {
        std::fputs("Assertion Failed: ", stderr);
        WriteManagedText(stderr, message, true);
    }
}

void il2cpp_debug_write_line(Il2CppString* text)
{
    WriteManagedText(stdout, text, true);
}

void il2cpp_debug_fail(Il2CppString* message)
{
    std::fputs("Assertion Failed: ", stderr);
    WriteManagedText(stderr, message, true);
}

void il2cpp_trace_write(Il2CppString* text)
{
    WriteManagedText(stdout, text, false);
}

void il2cpp_debug_write_line_obj(Il2CppObject* value)
{
    il2cpp_debug_write_line(icall_object_to_string(value));
}

void il2cpp_trace_write_obj(Il2CppObject* value)
{
    il2cpp_trace_write(icall_object_to_string(value));
}

//-----------------------------------------------------------------------------
// D-DEBUG-REST: the net48 category / composite-format / Fail / Assert forms.
//
// The category forms render `category + ":" + (message ?? "")` (the Trace
// class implementation, reference lines 497/507/522/532): a null category
// degrades to the category-less form, a null message/value to the empty
// string. The composite-format form is String.Format(format, args) then the
// line writer (reference lines 240/502).
//-----------------------------------------------------------------------------
namespace {

// Write "category:" (when category != null) followed by the message text
// (null -> nothing, net48's `?? String.Empty`), then an optional newline.
void WriteCategoryText(FILE* stream, Il2CppString* message, Il2CppString* category, bool newline)
{
    if (category != nullptr)
    {
        char* utf8 = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(category);
        std::fputs(utf8, stream);
        il2cpp::il2cpp_pinvoke_utf8_free(utf8);
        std::fputc(':', stream);
    }
    if (message != nullptr)
    {
        char* utf8 = il2cpp::il2cpp_pinvoke_string_to_utf8_alloc(message);
        std::fputs(utf8, stream);
        il2cpp::il2cpp_pinvoke_utf8_free(utf8);
    }
    if (newline)
        std::fputc('\n', stream);
}

} // namespace

void il2cpp_debug_write_line_category(Il2CppString* message, Il2CppString* category)
{
    WriteCategoryText(stdout, message, category, true);
}

void il2cpp_debug_write_category(Il2CppString* message, Il2CppString* category)
{
    WriteCategoryText(stdout, message, category, false);
}

void il2cpp_debug_write_line_obj_category(Il2CppObject* value, Il2CppString* category)
{
    WriteCategoryText(stdout, value != nullptr ? icall_object_to_string(value) : nullptr, category, true);
}

void il2cpp_debug_write_obj_category(Il2CppObject* value, Il2CppString* category)
{
    WriteCategoryText(stdout, value != nullptr ? icall_object_to_string(value) : nullptr, category, false);
}

void il2cpp_trace_write_line_category(Il2CppString* message, Il2CppString* category)
{
    WriteCategoryText(stdout, message, category, true);
}

void il2cpp_trace_write_category(Il2CppString* message, Il2CppString* category)
{
    WriteCategoryText(stdout, message, category, false);
}

void il2cpp_trace_write_line_obj_category(Il2CppObject* value, Il2CppString* category)
{
    WriteCategoryText(stdout, value != nullptr ? icall_object_to_string(value) : nullptr, category, true);
}

void il2cpp_trace_write_obj_category(Il2CppObject* value, Il2CppString* category)
{
    WriteCategoryText(stdout, value != nullptr ? icall_object_to_string(value) : nullptr, category, false);
}

void il2cpp_debug_write_line_fmt(Il2CppObject* format, Il2CppArray* args)
{
    il2cpp_debug_write_line(reinterpret_cast<Il2CppString*>(
        icall_string_format(format, reinterpret_cast<Il2CppObject*>(args))));
}

void il2cpp_trace_write_line_fmt(Il2CppObject* format, Il2CppArray* args)
{
    il2cpp_debug_write_line(reinterpret_cast<Il2CppString*>(
        icall_string_format(format, reinterpret_cast<Il2CppObject*>(args))));
}

void il2cpp_debug_fail_detail(Il2CppString* message, Il2CppString* detailMessage)
{
    std::fputs("Assertion Failed: ", stderr);
    WriteManagedText(stderr, message, true);
    WriteManagedText(stderr, detailMessage, true);
}

void il2cpp_trace_fail(Il2CppString* message)
{
    il2cpp_debug_fail(message);
}

void il2cpp_trace_fail_detail(Il2CppString* message, Il2CppString* detailMessage)
{
    il2cpp_debug_fail_detail(message, detailMessage);
}

void il2cpp_trace_assert(int32_t condition)
{
    il2cpp_debug_assert(condition, nullptr);
}

void il2cpp_trace_assert_msg(int32_t condition, Il2CppString* message)
{
    il2cpp_debug_assert(condition, message);
}

void il2cpp_trace_assert_detail(int32_t condition, Il2CppString* message, Il2CppString* detailMessage)
{
    if (condition == 0)
        il2cpp_debug_fail_detail(message, detailMessage);
}

void il2cpp_trace_assert_fmt(int32_t condition, Il2CppString* message,
                             Il2CppObject* detailFormat, Il2CppArray* args)
{
    if (condition == 0)
    {
        Il2CppString* detail = reinterpret_cast<Il2CppString*>(
            icall_string_format(detailFormat, reinterpret_cast<Il2CppObject*>(args)));
        il2cpp_debug_fail_detail(message, detail);
    }
}

}} // namespace il2cpp::runtime
