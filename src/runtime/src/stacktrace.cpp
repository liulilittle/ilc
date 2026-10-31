//------------------------------------------------------------------------------
// stacktrace.cpp -- System.Diagnostics.StackTrace / StackFrame object model and
// Environment.StackTrace (see il2cpp_stacktrace.h for the contract).
//
// The whole implementation is a formatter over the per-thread shadow frame
// stack (il2cpp_frame.h): the frames a managed call leaves behind already
// carry the managed spelling of each method (Il2CppFrame::displayName), so no
// CLR stack walker is involved. What this file adds is (a) the snapshot taken
// at StackTrace construction, (b) the two managed descriptors that make a
// StackTrace/StackFrame a REAL object, and (c) the frame text formatter.
//
// ALLOCATION DISCIPLINE (the collector is compacting):
//   Any il2cpp_object_new / il2cpp_array_new / il2cpp_string_new_* may trigger
//   a collection that MOVES every live Gen0 object and rewrites only the
//   GC-managed slots (frame refs, object fields). A raw C++ local or icall
//   parameter is NOT rewritten, so every reference this file holds across an
//   allocation is rooted through GcRoot (il2cpp_gcroot.h) and re-read with
//   Get() after the allocation. The snapshot walk itself performs NO
//   allocation: displayName is a static literal, so the frame text is copied
//   into a plain std::string first and exactly one managed string is created
//   at the very end (after which no GC reference is used again).
//------------------------------------------------------------------------------

#include "il2cpp_stacktrace.h"

#include "il2cpp_frame.h"     // il2cpp_frame_top / Il2CppFrame
#include "il2cpp_array.h"     // il2cpp_array_new / Il2CppArray
#include "il2cpp_gc.h"        // il2cpp_write_barrier
#include "il2cpp_gcroot.h"    // GcRoot (root a raw slot across an allocation)
#include "il2cpp_metadata.h"  // Il2CppClass
#include "il2cpp_runtime.h"   // il2cpp_object_new
#include "il2cpp_string.h"    // il2cpp_string_new_from_cs
#include "il2cpp_exception_ext.h" // il2cpp_throw_null_reference_exception

#include <cstring>
#include <string>

//------------------------------------------------------------------------------
// Runtime descriptors (GLOBAL scope -- see il2cpp_metadata.h's descriptor
// contract; the transpiler emits `il2cpp_object_new(&System_..._class)` for
// `new StackTrace()` / `new StackFrame()`).
//
//   instanceSize      header + payload (both classes are reference types)
//   refFieldBitmap    StackTrace slot 1 (frames) is the ONE reference field;
//                     a StackFrame stores only a static literal + two int32s,
//                     so it has no reference fields at all.
//   baseClass         System.Object (registered by the builtin ensure pass,
//                     which runs before any user code can allocate)
//------------------------------------------------------------------------------
Il2CppClass System_Diagnostics_StackTrace_class = {
    "System.Diagnostics.StackTrace",                    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStackTrace)), // instanceSize
    1u << 1,                                            // refFieldBitmap: frames
    1,                                                  // refFieldCount
    &System_Object_class,                               // baseClass
    nullptr,                                            // vtable
    0,                                                  // vtableSize
    nullptr,                                            // interfaces
    0,                                                  // interfaceCount
};

Il2CppClass System_Diagnostics_StackFrame_class = {
    "System.Diagnostics.StackFrame",                    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppStackFrame)), // instanceSize
    0u,                                                 // refFieldBitmap
    0,                                                  // refFieldCount
    &System_Object_class,                               // baseClass
    nullptr,                                            // vtable
    0,                                                  // vtableSize
    nullptr,                                            // interfaces
    0,                                                  // interfaceCount
};

namespace il2cpp { namespace runtime {

namespace {

using il2cpp::Il2CppArray;
using il2cpp::Il2CppFrame;
using il2cpp::Il2CppStackTrace;
using il2cpp::Il2CppStackFrame;

// Depth cap for one snapshot. The shadow chain is bounded by the C++ call
// stack in practice (each frame record is a stack local); the cap only keeps a
// corrupted chain from producing an unbounded trace. 512 is far above any real
// managed depth in this runtime (the crash dump uses 64).
constexpr int32_t kMaxFrames = 256;

// Frame-line prefix (net48 StackTrace.ToString / StackFrame text).
constexpr const char* kAtPrefix = "   at ";

//------------------------------------------------------------------------------
// CaptureFrames -- copy the innermost 'kMaxFrames' managed frames of the
// CALLING thread's shadow stack into 'names' (skipFrames skipped first).
//
// NO allocation: the display names are static literals owned by the generated
// code, so the pointers stay valid for the whole process lifetime and the
// snapshot never exposes a raw GC reference. A frame whose emitter supplied no
// managed spelling falls back to its C++ method name (never a null entry).
// Returns the number of captured frames.
//------------------------------------------------------------------------------
int32_t CaptureFrames(int32_t skipFrames, const char* (&names)[kMaxFrames])
{
    Il2CppFrame* frame = il2cpp_frame_top();
    for (int32_t skip = skipFrames; skip > 0 && frame != nullptr; --skip)
    {
        frame = frame->prev;
    }
    int32_t count = 0;
    for (; frame != nullptr && count < kMaxFrames; frame = frame->prev)
    {
        const char* name = frame->displayName != nullptr ? frame->displayName : frame->methodName;
        names[count] = name != nullptr ? name : "(unknown frame)";
        ++count;
    }
    return count;
}

//------------------------------------------------------------------------------
// FormatFrameText -- append one frame's text to 'out': "   at Ns.Type.M(...)",
// separated by CRLF from the previous frame (net48 StackTrace.ToString: the
// separator sits BETWEEN frames, never before the first one).
//------------------------------------------------------------------------------
void FormatFrameText(std::string& out, const char* displayName, bool first)
{
    if (!first)
    {
        out += "\r\n";
    }
    out += kAtPrefix;
    out += displayName != nullptr ? displayName : "(unknown frame)";
}

//------------------------------------------------------------------------------
// NewManagedString -- one managed string from a built std::string. The LAST
// operation of every formatter: it allocates (and may collect), so callers
// must not use a GC reference afterwards.
//------------------------------------------------------------------------------
Il2CppObject* NewManagedString(const std::string& text)
{
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

//------------------------------------------------------------------------------
// FrameText -- the "   at ..." text of every frame of a captured snapshot.
//------------------------------------------------------------------------------
std::string FrameText(Il2CppObject* framesObj, int32_t frameCount)
{
    std::string out;
    auto* frames = reinterpret_cast<Il2CppArray*>(framesObj);
    for (int32_t i = 0; i < frameCount; ++i)
    {
        auto* frame = reinterpret_cast<Il2CppStackFrame*>(
            *reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(frames, i)));
        FormatFrameText(out, frame != nullptr ? frame->displayName : nullptr, i == 0);
    }
    return out;
}

//------------------------------------------------------------------------------
// SetFrames -- publish a captured frame array on a StackTrace instance.
// 'self' and 'frames' are RAW pointers: the caller re-reads both through its
// GcRoots before calling (nothing here allocates, so no further move is
// possible after the last read).
//------------------------------------------------------------------------------
void SetFrames(Il2CppObject* self, Il2CppArray* frames, int32_t frameCount)
{
    auto* st = reinterpret_cast<Il2CppStackTrace*>(self);
    il2cpp_write_barrier(reinterpret_cast<void**>(&st->frames), reinterpret_cast<Il2CppObject*>(frames));
    st->frames = reinterpret_cast<Il2CppObject*>(frames);
    st->frameCount = frameCount;
}

//------------------------------------------------------------------------------
// CaptureInto -- take the snapshot and publish it on 'self' (the shared body
// of every capturing ctor). See the ALLOCATION DISCIPLINE note at the top of
// this file: 'self' is rooted for the whole capture because the frame objects
// are allocated one by one.
//------------------------------------------------------------------------------
void CaptureInto(Il2CppObject* self, int32_t skipFrames)
{
    const char* names[kMaxFrames];
    const int32_t count = CaptureFrames(skipFrames, names);
    GcRoot<Il2CppObject*> rootSelf(self, "stacktrace-snapshot");
    Il2CppArray* frames = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, count);
    GcRoot<Il2CppArray*> rootFrames(frames, "stacktrace-snapshot");
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject* frameObj = il2cpp_object_new(&System_Diagnostics_StackFrame_class);
        auto* frame = reinterpret_cast<Il2CppStackFrame*>(frameObj);
        frame->displayName = names[i];
        frame->fileLine = 0;
        frame->fileColumn = 0;
        // The array may have moved during the frame allocation: re-read the
        // rooted pointer, then publish the element (the array is a GC object
        // holding GC references -- barrier before the store).
        Il2CppArray* live = rootFrames.Get();
        auto** slot = reinterpret_cast<Il2CppObject**>(il2cpp_array_get_elem_ptr(live, i));
        il2cpp_write_barrier(reinterpret_cast<void**>(slot), frameObj);
        *slot = frameObj;
    }
    SetFrames(rootSelf.Get(), rootFrames.Get(), count);
}

//------------------------------------------------------------------------------
// SelfOf / FrameOf -- typed receiver views with the net48 null contract (a
// null receiver is a NullReferenceException, never a silent no-op).
//------------------------------------------------------------------------------
Il2CppStackTrace* SelfOf(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    return reinterpret_cast<Il2CppStackTrace*>(self);
}

Il2CppStackFrame* FrameOf(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(
            "Object reference not set to an instance of an object.");
    }
    return reinterpret_cast<Il2CppStackFrame*>(self);
}

//------------------------------------------------------------------------------
// SimpleMethodName -- the SIMPLE managed name of a frame's display name:
// "Ns.Type.Method(System.Int32)" -> "Method". Everything before the parameter
// list's '(' is the type-qualified name; the terminal dot segment is the
// method (nested types use '+', never '.', so the last dot is always the
// method separator).
//------------------------------------------------------------------------------
const char* SimpleMethodName(const char* displayName)
{
    if (displayName == nullptr)
    {
        return nullptr;
    }
    const char* end = std::strchr(displayName, '(');
    if (end == nullptr)
    {
        end = displayName + std::strlen(displayName);
    }
    const char* start = end;
    while (start > displayName && *(start - 1) != '.')
    {
        --start;
    }
    return start;
}

} // namespace

//------------------------------------------------------------------------------
// Environment.StackTrace
//------------------------------------------------------------------------------
Il2CppObject* icall_environment_get_stack_trace()
{
    const char* names[kMaxFrames];
    const int32_t count = CaptureFrames(0, names);
    std::string text;
    for (int32_t i = 0; i < count; ++i)
    {
        FormatFrameText(text, names[i], i == 0);
    }
    return NewManagedString(text);
}

//------------------------------------------------------------------------------
// StackTrace ctors
//------------------------------------------------------------------------------
void icall_stacktrace_ctor(Il2CppObject* self)
{
    CaptureInto(SelfOf(self), 0);
}

void icall_stacktrace_ctor_skip(Il2CppObject* self, int32_t skipFrames)
{
    // net48 ArgumentOutOfRangeException for a negative skip; this build clamps
    // (the frame selector is a diagnostic hint, not a program-visible value).
    CaptureInto(SelfOf(self), skipFrames > 0 ? skipFrames : 0);
}

void icall_stacktrace_ctor_exception(Il2CppObject* self, Il2CppObject* exception)
{
    // net48 captures the EXCEPTION's stored frames here. This runtime's
    // exception objects carry a message + type chain and no throw-time frame
    // record, so the live stack at construction is captured instead
    // (docs/audit/b5-gaps.md: exception-attached frames).
    (void)exception;
    CaptureInto(SelfOf(self), 0);
}

void icall_stacktrace_ctor_exception_skip(Il2CppObject* self, Il2CppObject* exception, int32_t skipFrames)
{
    (void)exception;
    CaptureInto(SelfOf(self), skipFrames > 0 ? skipFrames : 0);
}

void icall_stacktrace_ctor_frames(Il2CppObject* self, Il2CppObject* frames)
{
    auto* st = SelfOf(self);
    auto* arr = reinterpret_cast<Il2CppArray*>(frames);
    const int32_t count = arr != nullptr ? il2cpp_array_get_length(arr) : 0;
    SetFrames(st, arr, count);
}

void icall_stacktrace_frame_ctor(Il2CppObject* self)
{
    auto* frame = FrameOf(self);
    frame->displayName = nullptr;
    frame->fileLine = 0;
    frame->fileColumn = 0;
}

//------------------------------------------------------------------------------
// StackTrace instance surface
//------------------------------------------------------------------------------
int32_t icall_stacktrace_get_frame_count(Il2CppObject* self)
{
    return SelfOf(self)->frameCount;
}

Il2CppObject* icall_stacktrace_get_frames(Il2CppObject* self)
{
    return SelfOf(self)->frames;
}

Il2CppObject* icall_stacktrace_get_frame(Il2CppObject* self, int32_t index)
{
    Il2CppStackTrace* st = SelfOf(self);
    if (index < 0 || index >= st->frameCount || st->frames == nullptr)
    {
        return nullptr; // net48: an out-of-range frame index answers null
    }
    return *reinterpret_cast<Il2CppObject**>(
        il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(st->frames), index));
}

Il2CppObject* icall_stacktrace_to_string(Il2CppObject* self)
{
    Il2CppStackTrace* st = SelfOf(self);
    return NewManagedString(FrameText(st->frames, st->frameCount));
}

//------------------------------------------------------------------------------
// StackFrame instance surface
//------------------------------------------------------------------------------
Il2CppObject* icall_stackframe_get_method(Il2CppObject* self)
{
    Il2CppStackFrame* frame = FrameOf(self);
    const char* simple = SimpleMethodName(frame->displayName);
    if (simple == nullptr)
    {
        return nullptr; // no method on this frame (net48 `new StackFrame()`)
    }
    // Copy the name first: the allocation below may move the frame object.
    char name[256];
    const char* end = std::strchr(simple, '(');
    const size_t len = end != nullptr ? static_cast<size_t>(end - simple) : std::strlen(simple);
    if (len >= sizeof(name))
    {
        return nullptr;
    }
    std::memcpy(name, simple, len);
    name[len] = '\0';
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name));
}

Il2CppObject* icall_stackframe_get_file_name(Il2CppObject* self)
{
    (void)FrameOf(self); // null-receiver contract; no PDB -> no file name
    return nullptr;
}

int32_t icall_stackframe_get_file_line_number(Il2CppObject* self)
{
    return FrameOf(self)->fileLine;
}

int32_t icall_stackframe_get_file_column_number(Il2CppObject* self)
{
    return FrameOf(self)->fileColumn;
}

int32_t icall_stackframe_get_unknown_offset(Il2CppObject* self)
{
    (void)FrameOf(self); // null-receiver contract
    return -1;           // net48: no offset information on this frame
}

Il2CppObject* icall_stackframe_to_string(Il2CppObject* self)
{
    Il2CppStackFrame* frame = FrameOf(self);
    return NewManagedString(frame->displayName != nullptr ? frame->displayName : "");
}

}} // namespace il2cpp::runtime
