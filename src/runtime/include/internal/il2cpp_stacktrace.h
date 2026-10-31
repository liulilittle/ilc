#pragma once
//------------------------------------------------------------------------------
// il2cpp_stacktrace.h -- System.Diagnostics.StackTrace / StackFrame object
// model and the Environment.StackTrace surface (M7 stacktrace-real).
//
// WHY THIS EXISTS
//   The runtime has always had a per-thread shadow frame stack
//   (il2cpp_frame.h) whose only consumer was the collector's root scan and the
//   crash dump. That stack ALREADY holds exactly what a managed stack trace
//   needs -- one record per live managed call, innermost first, carrying the
//   method's managed spelling (Il2CppFrame::displayName). So StackTrace is not
//   a CLR-internal-state problem: it is a formatting problem over the shadow
//   stack. This header is that object model.
//
// SEMANTICS (net48 reference implementation, System.Diagnostics.StackTrace)
//   * Frame capture happens at CONSTRUCTION time, not at read time -- net48
//     stores the frames in the ctor, so `var st = new StackTrace()` observes
//     the call stack as it was when the ctor ran.
//   * skipFrames skips that many INNERMOST frames. The ctor's own frame is
//     never part of the result: an icall frame is not pushed on the shadow
//     stack, so the captured chain already starts at the caller.
//   * Frame text is "   at Ns.Type.Method(params)" per line, \r\n separated,
//     innermost first (net48 StackTrace.ToString).
//   * File/line info is NOT reproduced: a transpiled AOT build has no PDB.
//     GetFileName() answers null, GetFileLineNumber()/GetFileColumnNumber()
//     answer 0 -- net48's own answer for a frame built without file info. The
//     net48 text prints " in <abs path>:line N" in that case; a frame with no
//     file info simply prints no such suffix here. This is a FORMAT
//     divergence, deliberately NOT a rejection of the caller.
//
// OBJECT LAYOUT
//   Both classes are REAL runtime descriptors (allocated through
//   il2cpp_object_new, so obj->klass is a genuine type identity and the
//   collector scans them through their bitmaps):
//
//     Il2CppStackTrace          Il2CppStackFrame
//       +0 object header          +0 object header
//       +1 frames (StackFrame[])  +1 displayName (const char*, NOT managed)
//       +2 frameCount (int32)     +2 fileLine (int32, always 0)
//       +3 pad                    +3 fileColumn (int32, always 0)
//
//   frameCount is the LENGTH of the frames array (kept in the object so
//   FrameCount never dereferences the array). The StackFrame's displayName is
//   a STATIC string literal owned by the generated translation unit (the
//   emitter emits it in the method prologue), never a managed string: literals
//   have process lifetime, so storing one in a non-reference slot is safe and
//   makes the snapshot allocation-free apart from the array and the frame
//   objects themselves. A frame with no method (net48 `new StackFrame()`,
//   or an exception path with no captured frames) carries a null displayName
//   and answers GetMethod() == null like net48.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// System.Diagnostics.StackTrace instance payload (see the layout table above).
struct Il2CppStackTrace : Il2CppObject
{
    Il2CppObject* frames;     // StackFrame[] (managed reference -- GC traced)
    int32_t       frameCount; // elements in 'frames'
    int32_t       pad;        // keeps the payload pointer-slot aligned
};

// System.Diagnostics.StackFrame instance payload (see the layout table above).
struct Il2CppStackFrame : Il2CppObject
{
    const char* displayName; // static literal "Ns.Type.Method(params)" or null
    int32_t     fileLine;    // always 0: no PDB in an AOT build
    int32_t     fileColumn;  // always 0
};

} // namespace il2cpp

// Runtime descriptors (GLOBAL scope, the builtin-descriptor contract in
// il2cpp_metadata.h: the transpiler emits `il2cpp_object_new(&System_..._class)`
// from generated code, and the runtime allocates the frame objects itself).
extern il2cpp::Il2CppClass System_Diagnostics_StackTrace_class;
extern il2cpp::Il2CppClass System_Diagnostics_StackFrame_class;

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// System.Environment.StackTrace (net48: `new StackTrace(true).ToString()`
// without the file info). Walks the CALLING thread's shadow frame stack and
// returns the formatted text of every managed frame, innermost first.
//------------------------------------------------------------------------------
Il2CppObject* icall_environment_get_stack_trace();

//------------------------------------------------------------------------------
// System.Diagnostics.StackTrace ctors -- capture the shadow frame stack into
// 'self' (net48 captures at construction). Every net48 ctor shape is covered;
// the needFileInfo flag is accepted and ignored (there is no PDB -- see the
// file comment) and never selects a different behaviour.
//
//   icall_stacktrace_ctor                  -> ()
//   icall_stacktrace_ctor_skip             -> (int skipFrames)
//   icall_stacktrace_ctor_exception        -> (Exception e)  [, bool]
//   icall_stacktrace_ctor_exception_skip   -> (Exception e, int skipFrames)
//   icall_stacktrace_ctor_frames           -> (StackFrame[] frames) [, bool]
//
// The Exception overloads have no throw-time frame record in this runtime
// (exceptions are message/chain carriers), so they capture the LIVE stack at
// construction. Documented divergence -- docs/audit/b5-gaps.md -- never a
// rejection.
//------------------------------------------------------------------------------
void icall_stacktrace_ctor(Il2CppObject* self);
void icall_stacktrace_ctor_skip(Il2CppObject* self, int32_t skipFrames);
void icall_stacktrace_ctor_exception(Il2CppObject* self, Il2CppObject* exception);
void icall_stacktrace_ctor_exception_skip(Il2CppObject* self, Il2CppObject* exception, int32_t skipFrames);
void icall_stacktrace_ctor_frames(Il2CppObject* self, Il2CppObject* frames);

// System.Diagnostics.StackFrame ctors (net48 `new StackFrame(...)`): a frame
// object with no method -- GetMethod()/GetFileName() answer null and the line
// numbers answer 0, exactly like net48's "no frame" instance. All ctor arities
// map here (the frame selector arguments describe a CLR frame pointer this
// build does not have).
void icall_stacktrace_frame_ctor(Il2CppObject* self);

//------------------------------------------------------------------------------
// System.Diagnostics.StackTrace instance surface.
//------------------------------------------------------------------------------
int32_t       icall_stacktrace_get_frame_count(Il2CppObject* self);
Il2CppObject* icall_stacktrace_get_frames(Il2CppObject* self);
Il2CppObject* icall_stacktrace_get_frame(Il2CppObject* self, int32_t index);
Il2CppObject* icall_stacktrace_to_string(Il2CppObject* self);

//------------------------------------------------------------------------------
// System.Diagnostics.StackFrame instance surface.
//------------------------------------------------------------------------------
// GetMethod(): the MethodBase VALUE -- a managed string holding the method's
// SIMPLE managed name ("Main"), the simplified member model the runtime's
// reflection surface uses (MethodBase.GetParameters / MemberInfo.get_Name /
// MemberInfo.get_DeclaringType all resolve it through the typeinfo registry).
// null when the frame has no method.
Il2CppObject* icall_stackframe_get_method(Il2CppObject* self);
// GetFileName(): null -- no PDB (see the file comment).
Il2CppObject* icall_stackframe_get_file_name(Il2CppObject* self);
int32_t       icall_stackframe_get_file_line_number(Il2CppObject* self);
int32_t       icall_stackframe_get_file_column_number(Il2CppObject* self);
// GetILOffset()/GetNativeOffset(): -1 -- net48's own answer for a frame with
// no offset information. Both offsets describe CLR code layout, which an AOT
// build does not carry.
int32_t       icall_stackframe_get_unknown_offset(Il2CppObject* self);
// ToString(): the frame text without the leading "   at " (net48 shape).
Il2CppObject* icall_stackframe_to_string(Il2CppObject* self);

}} // namespace il2cpp::runtime
