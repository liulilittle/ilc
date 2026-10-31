#pragma once
//------------------------------------------------------------------------------
// il2cpp_handleref.h -- System.Runtime.InteropServices.HandleRef support.
//
// net48 contract (measured with tests/cases/probe-handleref on the CLR):
//   * a HandleRef P/Invoke parameter reaches native code as its .Handle value
//     (a native int), NOT as a pointer to the struct;
//   * the .Wrapper object stays reachable for the duration of the call (the
//     CLR roots it through the call; a finalizable wrapper is NOT finalized
//     while the native call is running);
//   * a null wrapper (new HandleRef(null, h)) is legal and the handle is
//     passed through unchanged (a zero handle reaches the API as NULL);
//   * SetLastError / Marshal.GetLastWin32Error are unaffected.
//
// THIS STRUCT IS THE SINGLE AUTHORITATIVE LAYOUT of the managed
// System.Runtime.InteropServices.HandleRef value (matching mscorlib's two
// fields: _wrapper then _handle). The transpiler maps the metadata value type
// to this struct (CodeGenerator.TypeCppName) and every field read goes through
// the accessors below -- no call site writes a raw offset.
//
// KEEP-ALIVE: the collector is a PRECISE root scanner (gc.cpp:
// il2cpp_gc_add_static_root / il2cpp_gc_add_root_range + the managed frame
// stack; gc.cpp:1164-1195 marks ONLY those roots -- an unregistered C++
// stack slot is NOT scanned). A wrapper read from a HandleRef rvalue (a
// temporary struct, or a value passed through a method parameter) therefore
// needs an explicit root for the call. Il2CppHandleRefArg registers its own
// wrapper slot as a root range and unregisters it in the destructor; the
// argument temporary lives until the end of the full-expression containing
// the P/Invoke call (C++ [class.temporary]), i.e. exactly the call duration,
// and the pair is 1:1 even on exception unwinding.
//------------------------------------------------------------------------------

#include <cstdint>
#include <type_traits>

#include "il2cpp_gc.h"
#include "il2cpp_object.h"

namespace il2cpp {

struct Il2CppHandleRef
{
    Il2CppObject* wrapper; // = mscorlib HandleRef._wrapper
    intptr_t      handle;  // = mscorlib HandleRef._handle
};

// new HandleRef(wrapper, handle): the transpiler's in-place value-type ctor
// shape passes the destination slot address.
inline void il2cpp_handleref_ctor(Il2CppHandleRef* self, Il2CppObject* wrapper, intptr_t handle)
{
    self->wrapper = wrapper;
    self->handle  = handle;
}

// HandleRef.Handle / HandleRef.Wrapper (value and address receiver forms).
inline intptr_t il2cpp_handleref_handle(const Il2CppHandleRef& hr)
{
    return hr.handle;
}

inline intptr_t il2cpp_handleref_handle_ptr(const Il2CppHandleRef* hr)
{
    return hr->handle;
}

inline Il2CppObject* il2cpp_handleref_wrapper(const Il2CppHandleRef& hr)
{
    return hr.wrapper;
}

inline Il2CppObject* il2cpp_handleref_wrapper_ptr(const Il2CppHandleRef* hr)
{
    return hr->wrapper;
}

//------------------------------------------------------------------------------
// Il2CppHandleRefArg -- the P/Invoke argument bridge for a HandleRef value.
//
// The emitted argument expression is `il2cpp_handleref_arg(hr)`: the proxy
// holds the wrapper and the handle, roots the wrapper for its lifetime, and
// converts to the native parameter type at the call target:
//   * a pointer native parameter (HANDLE / HWND / HIMAGELIST / ...) receives
//     the handle bit pattern (reinterpret_cast);
//   * a native-int parameter (the transpiler's own `intptr_t` declaration for
//     an out-of-SDK module) receives the handle value.
// Every temporary of this type (the returned prvalue plus any copy made while
// forwarding the argument) registers its OWN wrapper slot, so construction and
// destruction pair 1:1 and the wrapper cannot be collected before the native
// call returns.
//------------------------------------------------------------------------------
class Il2CppHandleRefArg
{
public:
    explicit Il2CppHandleRefArg(const Il2CppHandleRef& hr)
        : wrapper_(hr.wrapper), handle_(hr.handle)
    {
        Root();
    }

    Il2CppHandleRefArg(const Il2CppHandleRefArg& other)
        : wrapper_(other.wrapper_), handle_(other.handle_)
    {
        Root();
    }

    Il2CppHandleRefArg& operator=(const Il2CppHandleRefArg&) = delete;

    ~Il2CppHandleRefArg()
    {
        il2cpp_gc_remove_root_range(reinterpret_cast<void**>(&wrapper_));
    }

    operator intptr_t() const
    {
        return handle_;
    }

    template <typename T, typename = typename std::enable_if<std::is_pointer<T>::value>::type>
    operator T() const
    {
        return reinterpret_cast<T>(handle_);
    }

private:
    void Root()
    {
        il2cpp_gc_add_root_range(reinterpret_cast<void**>(&wrapper_), 1);
    }

    Il2CppObject* wrapper_;
    intptr_t      handle_;
};

inline Il2CppHandleRefArg il2cpp_handleref_arg(const Il2CppHandleRef& hr)
{
    return Il2CppHandleRefArg(hr);
}

} // namespace il2cpp
