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

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <type_traits>
// GN-17: il2cpp_pinvoke_call references il2cpp::DotNetException and
// il2cpp::il2cpp_throw_new (the managed-exception transport declared in
// il2cpp_exception.h; no include cycle -- that header pulls only the C++
// standard <exception>). <exception> is brought in transitively.
#include "il2cpp_exception.h"
// array parameters: the pinned-array helpers below read Il2CppArray's payload
// and register the array with the collector, so both contracts must be visible:
//   * il2cpp_array.h -- Il2CppArray payload addressing;
//   * il2cpp_gc.h    -- il2cpp_gc_add_static_root (strong root) and
//                       il2cpp_gc_add_pinned (immovable) -- the pin alone does
//                       NOT root the object.
// Both headers are cycle-free (they pull only object/metadata).
#include "il2cpp_array.h"
#include "il2cpp_gc.h"

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
// ARRAY parameters (managed array -> native buffer).
//
// Measured net48 semantics (tests/cases/probe-pinvoke-array, raw records in
// .opencode/tmp/impl-pinvoke-array/NOTES.md):
//   * PRIMITIVE-element arrays (byte/sbyte/char(UTF-16)/short/ushort/int/uint/
//     long/ulong/float/double/IntPtr/UIntPtr) are PINNED to their own storage:
//     [In]/[Out] are IGNORED, the callee sees the managed bytes and its writes
//     are visible in the managed array immediately.
//   * STRUCT-element arrays (blittable user structs / System.Guid) are COPIED:
//     default/[In] copy in only; [In,Out] copies both ways; [Out] hands the
//     callee a ZEROED temp and copies back.
//   * a null array is a NULL pointer; a zero-length array is a NON-NULL pointer
//     to the array start.
//
// GC SAFETY (the hard requirement): the collector is a COMPACTING generational
// mark-sweep -- a Gen0 array is copied by a collection, so a raw payload address
// is only valid while the object is both rooted AND immovable. This runtime's
// pin table marks an object immovable but does NOT root it (gc.cpp mark walks
// g_staticRoots only), so every array argument registers BOTH primitives for
// the call's full-expression lifetime -- the exact double registration
// GCHandle.Pinned uses (gchandle.cpp alloc adds static root + pinned, free
// removes pinned then static root). The registration lives in the OUTERMOST
// argument temporary (the Il2CppPinvokeNarrowRef pattern: value copies carry
// owner=false), whose destructor runs after the native call returns.
//------------------------------------------------------------------------------
inline void il2cpp_pinvoke_array_element_size_mismatch(int32_t got, int32_t expected)
{
    std::fprintf(stderr,
                 "il2cpp_pinvoke array argument: array elemSize %d != the native element width %d "
                 "(transpiler emitted the wrong array shape)\n",
                 static_cast<int>(got), static_cast<int>(expected));
    std::abort();
}

namespace detail {

// Register the double root/pin pair on the CLIENT-OWNED slot 'slot' (which holds
// the array). Removal MUST mirror gchandle.cpp's free order (pin first, then
// the strong root).
inline void pin_array_slot(Il2CppArray** slot)
{
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(slot));
    il2cpp_gc_add_pinned(reinterpret_cast<void**>(slot));
}

inline void unpin_array_slot(Il2CppArray** slot)
{
    il2cpp_gc_remove_pinned(reinterpret_cast<void**>(slot));
    il2cpp_gc_remove_static_root(reinterpret_cast<void**>(slot));
}

} // namespace detail

// PRIMITIVE-element array argument: the callee receives the array payload and
// writes it in place (net48 pin semantics). Templated conversion operator so
// the native declaration (windows.h prototype or the resolve cast) names the
// exact pointer type.
struct Il2CppPinvokeArrayArg
{
    Il2CppArray* ref;   // owner's pinned slot (the GC root/pin target)
    Il2CppArray** slot; // &ref for the owner, copied verbatim into value copies
    void* data;         // payload pointer (valid while pinned)
    bool owner;

    Il2CppPinvokeArrayArg(Il2CppArray* a, int32_t expectedElemSize)
        : ref(a), slot(nullptr), data(nullptr), owner(true)
    {
        if (a == nullptr) return; // null array -> null pointer (net48)
        if (a->elemSize != expectedElemSize)
            il2cpp_pinvoke_array_element_size_mismatch(a->elemSize, expectedElemSize);
        slot = &ref;
        detail::pin_array_slot(slot);
        data = a->data; // zero-length array still yields the inline payload start
    }
    Il2CppPinvokeArrayArg(const Il2CppPinvokeArrayArg& o)
        : ref(o.ref), slot(o.slot), data(o.data), owner(false) {}
    Il2CppPinvokeArrayArg& operator=(const Il2CppPinvokeArrayArg&) = delete;
    ~Il2CppPinvokeArrayArg()
    {
        if (owner && slot != nullptr) detail::unpin_array_slot(slot);
    }
    template <typename T>
    operator T() const { return reinterpret_cast<T>(data); }
};

inline Il2CppPinvokeArrayArg il2cpp_pinvoke_array(Il2CppArray* a, int32_t expectedElemSize)
{
    return Il2CppPinvokeArrayArg(a, expectedElemSize);
}

// STRUCT-element array argument (blittable element, measured copy semantics).
// The native temp is owned by the OUTERMOST temporary; copies travel with
// owner=false. Copy-back runs in the owner's destructor AFTER the native call,
// re-reading the array header through the rooted slot (the collector rewrites a
// moved root; the pin keeps this array immovable, so the copy-back target is
// the live array in every case).
struct Il2CppPinvokeStructArrayArg
{
    Il2CppArray* ref;
    Il2CppArray** slot;
    void* buffer;
    int32_t bytes;
    bool copyBack;
    bool owner;

    Il2CppPinvokeStructArrayArg(Il2CppArray* a, int32_t copyIn, int32_t copyOut, int32_t expectedElemSize)
        : ref(a), slot(nullptr), buffer(nullptr), bytes(0), copyBack(copyOut != 0), owner(true)
    {
        if (a == nullptr) return; // null array -> null pointer (net48)
        if (a->elemSize != expectedElemSize)
            il2cpp_pinvoke_array_element_size_mismatch(a->elemSize, expectedElemSize);
        slot = &ref;
        detail::pin_array_slot(slot);
        bytes = a->length * a->elemSize;
        buffer = std::malloc(bytes > 0 ? static_cast<std::size_t>(bytes) : 1u);
        if (buffer == nullptr)
        {
            std::fprintf(stderr, "il2cpp_pinvoke struct array argument: out of memory (%d bytes)\n",
                         static_cast<int>(bytes));
            std::abort();
        }
        if (copyIn != 0)
        {
            if (bytes > 0) std::memcpy(buffer, a->data, static_cast<std::size_t>(bytes));
        }
        else
        {
            // net48 hands an [Out] array a ZEROED native temp (measured).
            std::memset(buffer, 0, bytes > 0 ? static_cast<std::size_t>(bytes) : 1u);
        }
    }
    Il2CppPinvokeStructArrayArg(const Il2CppPinvokeStructArrayArg& o)
        : ref(o.ref), slot(o.slot), buffer(o.buffer), bytes(o.bytes), copyBack(o.copyBack), owner(false) {}
    Il2CppPinvokeStructArrayArg& operator=(const Il2CppPinvokeStructArrayArg&) = delete;
    ~Il2CppPinvokeStructArrayArg()
    {
        if (!owner || slot == nullptr) return;
        if (copyBack && bytes > 0)
        {
            Il2CppArray* live = *slot; // collector-updated reference
            std::memcpy(live->data, buffer, static_cast<std::size_t>(bytes));
        }
        std::free(buffer);
        detail::unpin_array_slot(slot);
    }
    template <typename T>
    operator T() const { return reinterpret_cast<T>(buffer); }
};

inline Il2CppPinvokeStructArrayArg il2cpp_pinvoke_struct_array(
    Il2CppArray* a, int32_t copyIn, int32_t copyOut, int32_t expectedElemSize)
{
    return Il2CppPinvokeStructArrayArg(a, copyIn, copyOut, expectedElemSize);
}

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
