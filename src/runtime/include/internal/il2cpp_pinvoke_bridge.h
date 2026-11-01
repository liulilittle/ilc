#pragma once
//------------------------------------------------------------------------------
// il2cpp_pinvoke_bridge.h -- P/Invoke native-ABI argument/result bridge.
//
// WHY: a DllImport call compiles against the NATIVE declaration of the import
// symbol. On Windows the generated code does not restate the prototype (it
// would collide with windows.h's OWN declaration -- see CodeGenerator's
// "not redeclared to avoid overload conflicts" emission), so the call must
// satisfy the SDK prototype, whose parameter/return types are the concrete
// Windows types (HWND / HMODULE / HGLOBAL / LPCWSTR / WNDCLASSEXW* / MSG* /
// LRESULT / ATOM / ...) -- NOT the transpiler's internal C++ carriers
// (intptr_t / char16_t* / the sanitized managed mirror structs).
//
// The transpiler's managed view and the native view are layout-compatible but
// not type-identical:
//   * System.IntPtr param vs HWND/HMODULE/HGLOBAL/void* -- intptr_t must be
//     reinterpreted back to the pointer type;
//   * System.IntPtr return vs HWND/HMODULE/LRESULT -- pointer/size result
//     must be reinterpreted to the intptr_t carrier;
//   * string (CharSet.Unicode) vs LPCWSTR -- char16_t* vs wchar_t* (MinGW
//     and MSVC wchar_t are 16-bit on Windows) -- a reinterpret;
//   * ref/out managed struct vs const WNDCLASSEXW*/MSG* -- the managed mirror
//     pointer must be reinterpreted to the SDK pointer type.
//
// il2cpp_pinvoke_arg<U> is passed IN PLACE OF the argument expression: its
// templated conversion operator deduces the native parameter type T from the
// call target and performs exactly the cast above (pointer target ->
// reinterpret_cast, arithmetic target -> static_cast, same-layout struct ->
// identity). il2cpp_pinvoke_result<T> is the result-side counterpart the
// emitter applies when the managed return is an IntPtr carrier ("ptr" kind).
//
// GN-17 (pinvoke exception boundary): a native library may throw a C++
// exception that would otherwise escape the P/Invoke call and terminate the
// process (the generated catch is type-matched against DotNetException only).
// il2cpp_pinvoke_call<Ret>(fn, args...) wraps the whole call so every such
// escape surfaces to the managed side as
// System.Runtime.InteropServices.SEHException -- the net48 P/Invoke boundary
// contract. Catch ORDER matters: il2cpp::DotNetException IS a std::exception
// subclass, so the managed-exception rethrow must come FIRST or the
// std::exception handler would swallow it (the generated try/catch depends on
// the DotNetException unwinding to its type-matched clause).
//
// All helpers are inline templates -- zero runtime cost, no symbol emitted.
//------------------------------------------------------------------------------

#include <cstdint>
#include <type_traits>
// GN-17: il2cpp_pinvoke_call references il2cpp::DotNetException and
// il2cpp::il2cpp_throw_new (the managed-exception transport declared in
// il2cpp_exception.h; no include cycle -- that header pulls only the C++
// standard <exception>). <exception> is brought in transitively.
#include "il2cpp_exception.h"

namespace il2cpp {
namespace runtime {

// Convert one internal carrier value `u` to the native type `T` deduced at the
// call target. Pointer targets keep the exact bit pattern (reinterpret_cast);
// an arithmetic/enum target reached from a POINTER result (e.g. GlobalFree's
// HGLOBAL -> the intptr_t IntPtr carrier) is the reverse exact reinterpret;
// everything else uses the value-preserving static_cast.
template <typename T, typename U>
inline T il2cpp_pinvoke_native_convert(U u)
{
    if constexpr (std::is_pointer<T>::value)
    {
        return reinterpret_cast<T>(u);
    }
    else if constexpr (std::is_pointer<U>::value)
    {
        return reinterpret_cast<T>(u);
    }
    else
    {
        return static_cast<T>(u);
    }
}

// Argument wrapper: `native_fn(il2cpp_pinvoke_arg(x))` converts x to whatever
// parameter type the visible native declaration names.
template <typename U>
struct Il2CppPinvokeArg
{
    U value;
    template <typename T>
    operator T() const
    {
        return il2cpp_pinvoke_native_convert<T>(value);
    }
};

template <typename U>
inline Il2CppPinvokeArg<U> il2cpp_pinvoke_arg(U u)
{
    return Il2CppPinvokeArg<U>{u};
}

// Result wrapper: `il2cpp_pinvoke_result<intptr_t>(native_call(...))` folds a
// native pointer/integer result back into the intptr_t carrier.
template <typename T, typename R>
inline T il2cpp_pinvoke_result(R r)
{
    return il2cpp_pinvoke_native_convert<T>(r);
}

//------------------------------------------------------------------------------
// byref-scalar (ref/out SCALAR DllImport parameters): NARROW native cells.
//
// net48 hands the native callee the ADDRESS of the managed slot for a
// blittable `ref`/`out` scalar. This model stores some managed scalars in
// WIDER C++ slots than the native cell the DllImport signature names:
//   * bool / sbyte / byte / int16 / uint16 -> int32_t slot
//   * Single                                -> double slot
// so the slot address is NOT the native image and cannot be passed directly.
// The generated call site wraps such an argument in
// il2cpp_pinvoke_narrow_ref<Narrow>(slot):
//   * the owner temporary holds a Narrow cell initialised from the slot (the
//     `ref` copy-IN -- net48 reads the managed value),
//   * the conversion operator yields &cell, the exact native pointer the
//     native declaration / dynamic resolve cast names,
//   * the callee writes the cell; the OWNER's destructor copies it BACK into
//     the managed slot at the end of the call's full-expression -- after the
//     native call returns, exactly the interop stub's copy-out point.
// The argument travels BY VALUE through il2cpp_pinvoke_call, so every copy
// propagates the OWNER's cell pointer (`cell`), never a per-copy buffer: the
// bytes the callee writes are the bytes copied back regardless of how many
// copies the parameter pack makes. The owner is the outermost temporary and is
// therefore destroyed LAST (reverse construction order).
//
// bool is the BOOL (4-byte) special case: net48 normalises the copy-back (a
// native 2 reads back as managed true), so il2cpp_pinvoke_bool_ref stores 0/1
// in both directions.
//------------------------------------------------------------------------------
template <typename Narrow, typename Wide>
struct Il2CppPinvokeNarrowRef
{
    Wide* slot;
    Narrow* cell;
    Narrow storage;
    bool owner;
    explicit Il2CppPinvokeNarrowRef(Wide& s)
        : slot(&s), cell(&storage), storage(static_cast<Narrow>(s)), owner(true) {}
    Il2CppPinvokeNarrowRef(const Il2CppPinvokeNarrowRef& o)
        : slot(o.slot), cell(o.cell), storage(o.storage), owner(false) {}
    Il2CppPinvokeNarrowRef& operator=(const Il2CppPinvokeNarrowRef&) = delete;
    ~Il2CppPinvokeNarrowRef()
    {
        if (owner) *slot = static_cast<Wide>(*cell);
    }
    operator Narrow*() { return cell; }
};

template <typename Narrow, typename Wide>
inline Il2CppPinvokeNarrowRef<Narrow, Wide> il2cpp_pinvoke_narrow_ref(Wide& slot)
{
    return Il2CppPinvokeNarrowRef<Narrow, Wide>(slot);
}

// The managed bool slot is the 4-byte BOOL cell already; only the copy-back
// normalisation differs.
struct Il2CppPinvokeBoolRef
{
    int32_t* slot;
    int32_t* cell;
    int32_t storage;
    bool owner;
    explicit Il2CppPinvokeBoolRef(int32_t& s)
        : slot(&s), cell(&storage), storage(s != 0 ? 1 : 0), owner(true) {}
    Il2CppPinvokeBoolRef(const Il2CppPinvokeBoolRef& o)
        : slot(o.slot), cell(o.cell), storage(o.storage), owner(false) {}
    Il2CppPinvokeBoolRef& operator=(const Il2CppPinvokeBoolRef&) = delete;
    ~Il2CppPinvokeBoolRef()
    {
        if (owner) *slot = (*cell != 0) ? 1 : 0;
    }
    operator int32_t*() { return cell; }
};

inline Il2CppPinvokeBoolRef il2cpp_pinvoke_bool_ref(int32_t& slot)
{
    return Il2CppPinvokeBoolRef(slot);
}

//------------------------------------------------------------------------------
// GN-17: P/Invoke exception boundary (see the header comment).
//
// The chain handed to il2cpp_throw_new follows the exceptionext/com.cpp
// convention "{Type},{Type},{Base1},..." -- the first element is the exact
// managed full name (what e.type_name() reports, so GetType().FullName of the
// catch variable reads the full name) and the remaining elements are the
// .NET base classes, so a catch of SEHException / ExternalException /
// SystemException / Exception all match.
//------------------------------------------------------------------------------
const char kPinvokeSehChain[] =
    "System.Runtime.InteropServices.SEHException,"
    "System.Runtime.InteropServices.SEHException,"
    "System.Runtime.InteropServices.ExternalException,"
    "System.SystemException,System.Exception";

// The message is a fixed STATIC literal (never e.what()): DotNetException
// stores the message POINTER without copying (il2cpp_exception.h), so a
// caller-buffer pointer would dangle once the catch block that owned the
// native exception unwinds.
const char kPinvokeSehMessage[] = "External component has thrown an exception.";

// Wrap a native P/Invoke call so a C++ exception escaping from the library
// surfaces as SEHException on the managed side. `fn` is the native callable
// (a statically-linked extern "C" symbol or the il2cpp_pinvoke_resolve cast
// expression); `args` are the SAME il2cpp_pinvoke_arg(...) bridge arguments
// the unwrapped call would pass -- their conversion operators still deduce
// the native parameter types at the point the template calls fn(args...), so
// the arg-bridge semantics are preserved. Ret is the managed-visible return
// CARRIER (int32_t/int64_t/double/intptr_t -- or void for a statement-position
// call); the native result is converted to it through il2cpp_pinvoke_native_
// convert, so a Windows-SDK pointer result (HWND/HMODULE/...) reinterprets
// back to the intptr_t carrier exactly as the standalone il2cpp_pinvoke_result
// bridge did.
template <typename Ret, typename Fn, typename... Args>
Ret il2cpp_pinvoke_call(Fn fn, Args... args)
{
    try
    {
        if constexpr (std::is_void<Ret>::value)
        {
            fn(args...);
            // GN-20.4 F1: the void arm must RETURN here. Without it control
            // leaves the try block normally (no exception), skips the catch
            // arms and reaches the __builtin_unreachable() tail below -- with
            // GCC the fall-through re-entered the catch(...) handler, so a
            // statement-position P/Invoke call ("(P/Invoke SEH boundary)")
            // never came back and the process died (0xC0000005 in gdifont /
            // gdidraw; the rethrow path in the new test_pinvoke section 4).
            return;
        }
        else
        {
            return il2cpp_pinvoke_native_convert<Ret>(fn(args...));
        }
    }
    catch (const il2cpp::DotNetException&)
    {
        // Managed exception: rethrow FIRST so the generated try/catch still
        // type-matches it (DotNetException derives from std::exception; a
        // std::exception catch below must not swallow it).
        throw;
    }
    catch (const std::exception&)
    {
        // Native C++ exception escaping the P/Invoke boundary -> SEHException.
        il2cpp::il2cpp_throw_new(kPinvokeSehChain, kPinvokeSehMessage);
    }
    catch (...)
    {
        // Any other escape (SEH-style) -> the same managed SEHException.
        il2cpp::il2cpp_throw_new(kPinvokeSehChain, kPinvokeSehMessage);
    }
#ifdef _MSC_VER
    __assume(0);
#else
    __builtin_unreachable();
#endif
}

}} // namespace il2cpp::runtime
