#pragma once
//------------------------------------------------------------------------------
// il2cpp_pinvoke_cb.h -- delegate -> native callback bridge, WindowProc form
// (WinForms stage-1 gap #10; completes the il2cpp_pinvoke_ext.h callback
// prototype -- the name-only registry there covers a bare STATIC function
// pointer, which has no delegate instance, no target binding and no
// lifecycle).
//
// WindowProc-style callbacks need all three:
//
//   void* il2cpp_pinvoke_cb_bind_wndproc(Il2CppObject* delegate, const char* name)
//   void  il2cpp_pinvoke_cb_unbind(const char* name)
//
// BIND (delegate instance binding): 'delegate' is a single-cast managed
// delegate (Il2CppDelegate). The runtime takes a STRONG GC reference -- the
// binding slot is a registered static root -- so the delegate AND its
// instance target survive collection for as long as the native side may call
// the callback. The function returns a native thunk address with the
// WindowProc form signature:
//
//   intptr_t (*)(intptr_t hwnd, int32_t msg, intptr_t wParam, intptr_t lParam)
//
//   = LRESULT CALLBACK WNDPROC(HWND, UINT, WPARAM, LPARAM): on x64 the Win32
//   calling conventions collapse to the single native convention; on x86 the
//   thunks are __stdcall (the CALLBACK ABI -- see the implementation's
//   IL2CPP_WM_CALL).
//
// The generated call site stores the returned thunk where the native side
// expects the callback pointer (WNDCLASS.lpfnWndProc for RegisterClassW).
// The native side calls it later; the thunk forwards the call into the
// delegate's methodPtr:
//
//   static-method delegate  : ((ret(*)(intptr_t, int32_t, intptr_t, intptr_t))
//                              leaf->methodPtr)(hwnd, msg, wParam, lParam)
//   instance-method delegate: ((ret(*)(Il2CppObject*, intptr_t, int32_t,
//                              intptr_t, intptr_t))leaf->methodPtr)
//                              (leaf->target, hwnd, msg, wParam, lParam)
//
//   (the generated instance ABI is {ret} M(Il2CppObject* this, ...) with
//   `this` as the FIRST parameter -- the stored target is passed there,
//   exactly like icall_multicast_invoke_i4 in delegateext.cpp).
//
// VALIDATION (loud, matching icall_delegate_get_fn_ptr): null delegate ->
// NullReferenceException; a MULTICAST container -> InvalidOperationException
// (the thunk would silently cover only one leaf of the invocation list); a
// non-delegate object -> InvalidOperationException. A null name and a full
// binding pool abort loudly (programming errors -- the transpiler emits a
// bounded set of bindings).
//
// UNBIND (lifecycle): releases the strong GC reference. The pool slot stays
// RESERVED for the name (it is never re-assigned to a different name), so a
// stale thunk pointer held by the native side after unbind ALWAYS aborts
// loudly (never a silent garbage call, never a silent wrong dispatch).
// Re-binding the same name replaces the previous binding (the old delegate
// is released) and returns the SAME thunk address, so a native side that
// already stored the pointer keeps working.
//------------------------------------------------------------------------------

#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Bind a single-cast managed delegate under 'name' as a WindowProc-form
// native callback (see header). Returns the native thunk address; the
// binding keeps a STRONG GC reference to the delegate until
// il2cpp_pinvoke_cb_unbind (or until a same-name re-bind replaces it).
// A same-name re-bind returns the SAME thunk address. Each name owns a
// permanently reserved pool slot (distinct names are bounded; see the
// implementation's pool comment). Validation and overflow policy per the
// header.
void* il2cpp_pinvoke_cb_bind_wndproc(Il2CppObject* delegate, const char* name);

// Release the binding under 'name' (strong GC reference + pool slot).
// Unknown names are a no-op. Null name aborts loudly.
void il2cpp_pinvoke_cb_unbind(const char* name);

// Diagnostic: is a binding under 'name' currently live? Returns false for a
// null/unknown name. Intended for tests and bind/unbind contract checks.
bool il2cpp_pinvoke_cb_is_bound(const char* name);

} // namespace il2cpp
