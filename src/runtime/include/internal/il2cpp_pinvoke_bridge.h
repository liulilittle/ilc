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
// StringBuilder parameters: the bridge hands the callee the builder's own
// chunk buffer and recomputes Length afterwards, so the runtime struct layout
// must be visible here (il2cpp_stringbuilder.h pulls only array/object).
#include "il2cpp_stringbuilder.h"
// COM interface (byval) parameters: the holder below calls the raw
// IUnknown::AddRef / Release helpers and the borrow accessor, all declared in
// il2cpp_com.h. Cycle-free: il2cpp_com.h pulls only object/string/array.
#include "il2cpp_com.h"

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

//------------------------------------------------------------------------------
// BYVAL COM-INTERFACE argument (a [ComImport] interface DllImport parameter).
//
// net48's marshaller AddRefs the interface for the DURATION of the call and
// Releases it afterwards -- measured on the x64 oracle (com-verify NOTES
// section 3(d), case F13): the callee observes refs = n+1 on entry AND on
// exit, and the post-call count is back at n. The transpiler therefore emits
//
//     il2cpp_pinvoke_com_arg(
//         il2cpp_com_rcw_borrow_iunknown(static_cast<Il2CppObject*>(obj)))
//
// i.e. borrow the RCW's wrapped pointer (no AddRef -- see the helper's
// contract) and let THIS holder own the +1/-1 pair:
//   * construction AddRefs, destruction Releases (the temporary's lifetime is
//     the whole P/Invoke full-expression, exactly the interop stub's span),
//   * the destructor runs during stack unwinding, so a native throw still
//     balances the count (no longjmp exists in this codebase's P/Invoke path),
//   * the COPY constructor AddRefs too: il2cpp_pinvoke_call takes its
//     arguments BY VALUE, so an extra copy may exist before the callee is
//     invoked (the Il2CppPinvokeNarrowRef precedent above). Every copy owns
//     its own reference, so the ledger stays balanced no matter how many
//     copies the parameter pack makes,
//   * iface == 0 (a null managed reference) does NOT AddRef and does NOT
//     Release: net48 passes NULL and never touches the count. This is why
//     il2cpp_com_rcw_borrow_iunknown maps null to 0 instead of throwing.
//
// Assignment is deleted: no argument holder is ever reassigned.
//------------------------------------------------------------------------------
struct Il2CppPinvokeComArg
{
    intptr_t iface;
    // ADOPTS one OWNED interface reference (the QI reference from
    explicit Il2CppPinvokeComArg(intptr_t p) : iface(p)
    {
        // no AddRef: the caller (il2cpp_com_rcw_borrow_interface) hands over
        // an owned QI reference and this holder owns it from here.
    }
    Il2CppPinvokeComArg(const Il2CppPinvokeComArg& o) : iface(o.iface)
    {
        // A real copy must AddRef, or two owners would release one reference.
        // il2cpp_pinvoke_call takes its arguments by value in a C++17
        // guaranteed-copy-elision context, so the ACTUAL call site makes
        // zero copies; a regression would make call.entry.delta 2 and turn
        // the pinvoke-com-byval records red (fail-loud).
        if (iface != 0) icall_com_add_ref(iface);
    }
    Il2CppPinvokeComArg& operator=(const Il2CppPinvokeComArg&) = delete;
    ~Il2CppPinvokeComArg()
    {
        if (iface != 0) icall_com_release(iface);
    }
    template <typename T>
    operator T() const
    {
        return il2cpp_pinvoke_native_convert<T>(iface);
    }
};

inline Il2CppPinvokeComArg il2cpp_pinvoke_com_arg(intptr_t p)
{
    return Il2CppPinvokeComArg(p);
}
//------------------------------------------------------------------------------
// COM-INTERFACE out/ref parameter (`out IStream` / `ref IDataObject`).
//
// MEASURED net48 contract (phase-0 oracle: .opencode/tmp/com-outref-oracle,
// records R1-R5 / O1-O12; corpus tests/cases/pinvoke-com-outref):
//
//   * `out`: the callee hands over ONE reference and net48 ADOPTS it -- the
//     resulting RCW owns that reference and ReleaseComObject / the RCW
//     finalizer releases it exactly once (O2 == the shipped F5).
//   * `ref`, callee does NOT replace: net48 keeps the SAME wrapper (O4 / O10
//     `same_wrapper = True`) and the argument side is a +1/-1 pair around the
//     call (R1: pre refs=3 -> callee entry 4 -> post 3).
//   * `ref`, callee REPLACES: the marshaller does NOT release the old pointer
//     (R2: pre 3 -> entry 4 -> post 4, unchanged after the call). When the
//     callee honours the COM contract and releases it, the old lands back on
//     its own baseline (R3 / O6). When it does not, the old stays at
//     baseline+1 (O5 / shipped F16) -- a callee bug net48 does not paper over.
//   * a NULL written by the callee (`out` or `ref`) becomes a null managed
//     reference and never throws (O3, O7); a null IN value reaches the callee
//     as NULL (O8).
//
// THE HARD RULE (#1 risk of this task): managed code NEVER Releases the old
// RCW's pointer. The reference the callee receives comes from the EXPLICIT
// QueryInterface done at the call site (il2cpp_com_rcw_borrow_interface), so
// the callee may legitimately consume it; the RCW keeps its own separate
// reference and RcwFinalize releases that one. Releasing the RCW's pointer
// here is a DOUBLE RELEASE (measured symptom: refs going negative).
//
// The only Release this holder performs is the -1 half of the +1/-1 pair, and
// ONLY when the cell still holds the very pointer the call site handed in --
// the one reference the callee provably did not consume.
//------------------------------------------------------------------------------
struct Il2CppPinvokeComOutRefArg
{
    void* storage;          // the owner's cell (copies keep the owner's pointer)
    void** cell;            // &owner.storage -- what the callee writes
    Il2CppObject** slot;    // the caller's managed-reference slot address
    void* inRaw;            // the pointer handed in (ref) / null (out)
    int32_t isOut;          // 1 = `out` (adopt), 0 = `ref`
    bool owner;             // only the outermost temporary writes back

    Il2CppPinvokeComOutRefArg(Il2CppObject** s, void* inPointer, int32_t outFlag)
        : storage(inPointer), cell(&storage), slot(s), inRaw(inPointer),
          isOut(outFlag), owner(true)
    {
    }

    Il2CppPinvokeComOutRefArg(const Il2CppPinvokeComOutRefArg& o)
        : storage(o.storage), cell(o.cell), slot(o.slot), inRaw(o.inRaw),
          isOut(o.isOut), owner(false)
    {
    }

    Il2CppPinvokeComOutRefArg& operator=(const Il2CppPinvokeComOutRefArg&) = delete;

    ~Il2CppPinvokeComOutRefArg()
    {
        if (!owner || slot == nullptr) return;
        void* const p = *cell;
        if (!isOut && p == inRaw)
        {
            // Not replaced: the caller keeps the SAME managed object (measured
            // identity), so the slot is not rewritten. Balance the +1 the call
            // site took for the argument -- this is the one reference the
            // callee provably did not consume.
            if (inRaw != nullptr)
            {
                icall_com_release(
                    static_cast<intptr_t>(reinterpret_cast<uintptr_t>(inRaw)));
            }
            return;
        }
        // `out`, or `ref` replaced: the pointer in the cell carries the
        // reference the callee handed over -- ADOPT it (no AddRef; the RCW
        // owns it and releases it exactly once via ReleaseComObject or
        // RcwFinalize). A callee-written NULL becomes a null managed
        // reference (measured O3 / O7), never an exception.
        *slot = (p == nullptr)
            ? nullptr
            : reinterpret_cast<Il2CppObject*>(icall_com_get_object_for_iunknown(
                  static_cast<intptr_t>(reinterpret_cast<uintptr_t>(p))));
    }

    template <typename T>
    operator T() const
    {
        return reinterpret_cast<T>(cell);
    }
};

inline Il2CppPinvokeComOutRefArg il2cpp_pinvoke_com_outref(
    Il2CppObject** slot, void* inPointer, int32_t outFlag)
{
    return Il2CppPinvokeComOutRefArg(slot, inPointer, outFlag);
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
//   * bool / char / sbyte / byte / int16 / uint16 -> int32_t slot
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
// byref CHAR (ref/out char P/Invoke parameters): the CHAR SET picks the native
// cell. MEASURED on net48 (Framework64 v4.0.30319, host CP_ACP=936; pinned by
// tests/cases/pinvoke-charref and recorded in docs/audit/b5-gaps.md):
//   * CharSet.Unicode / Auto -> a 2-byte UTF-16 code unit, NO conversion. The
//     generic il2cpp_pinvoke_narrow_ref<uint16_t> holder above is exact (the
//     managed char slot in this model is a 32-bit int32_t, so the slot address
//     cannot be handed over raw -- the high 16 bits must be zeroed on the way
//     back).
//   * CharSet.Ansi -> a 1-BYTE cell holding the SYSTEM ANSI code page image:
//     the unit is converted through CP_ACP on the way in AND out (U+4E2D ->
//     0xD6 under CP936; the lone 0xD6 decodes back to '?'). MEASURED detail:
//     `ref` copies the converted current value IN, while `out` starts the cell
//     at ZERO regardless of the managed slot's prior content (net48's [Out]
//     temp is not seeded; a pre-set local still reaches the callee as 0),
//     hence the two factories below. The two helpers own the conversion
//     (pinvoke_char.cpp); the holder mirrors the narrow-ref shape: exact-width
//     cell, copy-in at construction, copy-back when the outermost temporary
//     dies.
//------------------------------------------------------------------------------
int32_t il2cpp_pinvoke_char_ansi_to_byte(int32_t utf16);
int32_t il2cpp_pinvoke_char_ansi_from_byte(uint8_t byte);

struct Il2CppPinvokeCharAnsiRef
{
    int32_t* slot;
    uint8_t* cell;
    uint8_t storage;
    bool owner;
    Il2CppPinvokeCharAnsiRef(int32_t& s, bool copyIn)
        : slot(&s), cell(&storage),
          storage(copyIn ? (uint8_t)il2cpp_pinvoke_char_ansi_to_byte(s) : (uint8_t)0),
          owner(true) {}
    Il2CppPinvokeCharAnsiRef(const Il2CppPinvokeCharAnsiRef& o)
        : slot(o.slot), cell(o.cell), storage(o.storage), owner(false) {}
    Il2CppPinvokeCharAnsiRef& operator=(const Il2CppPinvokeCharAnsiRef&) = delete;
    ~Il2CppPinvokeCharAnsiRef()
    {
        if (owner) *slot = il2cpp_pinvoke_char_ansi_from_byte(*cell);
    }
    operator uint8_t*() { return cell; }
};

// `ref`: seed the cell from the managed slot through CP_ACP.
inline Il2CppPinvokeCharAnsiRef il2cpp_pinvoke_char_ansi_ref(int32_t& slot)
{
    return Il2CppPinvokeCharAnsiRef(slot, true);
}

// `out`: the cell starts at ZERO (net48 does not seed its [Out] temp).
inline Il2CppPinvokeCharAnsiRef il2cpp_pinvoke_char_ansi_out(int32_t& slot)
{
    return Il2CppPinvokeCharAnsiRef(slot, false);
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

//------------------------------------------------------------------------------
// char[] + CharSet.Ansi / CharSet.None: net48 converts the WHOLE array to a
// NUL-terminated CP_ACP narrow buffer, so the callee's element type is one byte
// and the managed payload (2-byte UTF-16 units) CANNOT be handed over directly
// -- unlike every other primitive element, this shape needs a real conversion.
//
// Measured contract (net48 Framework64 v4.0.30319, CP_ACP=936; oracles under
// .opencode/tmp/oracle-chararr2 .. oracle-chararr4):
//   * null array          -> NULL
//   * empty array         -> NON-NULL, first byte NUL (strlen 0)
//   * {'A','\0','B'}      -> strlen 1 (the encoded buffer is NUL-terminated)
//   * default / [In]      -> copy in only, NO copy-back
//   * [In,Out]            -> copy in AND copy back
//   * [Out]               -> the callee sees a ZEROED buffer, then copy back
//
// The buffer is malloc'd (not the array payload) and freed by the owner's
// destructor, which also performs the copy-back AFTER the native call returns.
// The array itself is rooted+pinned for the call so a moving collection cannot
// relocate the copy-back target mid-call (same pair as Il2CppPinvokeArrayArg).
//------------------------------------------------------------------------------
char* il2cpp_pinvoke_char_array_ansi_encode(const Il2CppArray* a, int32_t* outLen);
void il2cpp_pinvoke_char_array_ansi_decode(const char* buf, int32_t bufLen, Il2CppArray* a);

struct Il2CppPinvokeCharArrayAnsiArg
{
    Il2CppArray* ref;   // owner's pinned slot (the GC root/pin target)
    Il2CppArray** slot; // &ref for the owner, copied verbatim into value copies
    char* buf;          // converted CP_ACP buffer (null only for a null array)
    int32_t bufLen;     // encoded byte count (excludes the terminating NUL)
    int32_t copyOut;    // 1 when [Out]/[In,Out] must be copied back
    bool owner;

    Il2CppPinvokeCharArrayAnsiArg(Il2CppArray* a, int32_t copyIn, int32_t wantCopyOut)
        : ref(a), slot(nullptr), buf(nullptr), bufLen(0), copyOut(wantCopyOut), owner(true)
    {
        if (a == nullptr) return; // null array -> NULL pointer (net48)
        slot = &ref;
        detail::pin_array_slot(slot);
        if (copyIn)
        {
            buf = il2cpp_pinvoke_char_array_ansi_encode(a, &bufLen);
        }
        else
        {
            // [Out]: net48 hands the callee a ZEROED temp (measured hash == 0).
            const size_t cap = (size_t)a->length * 2 + 1;
            buf = (char*)std::calloc(cap, 1);
            if (buf != nullptr) bufLen = (int32_t)(cap - 1);
        }
    }
    Il2CppPinvokeCharArrayAnsiArg(const Il2CppPinvokeCharArrayAnsiArg& o)
        : ref(o.ref), slot(o.slot), buf(o.buf), bufLen(o.bufLen),
          copyOut(o.copyOut), owner(false) {}
    Il2CppPinvokeCharArrayAnsiArg& operator=(const Il2CppPinvokeCharArrayAnsiArg&) = delete;
    ~Il2CppPinvokeCharArrayAnsiArg()
    {
        if (!owner) return;
        if (copyOut && buf != nullptr && ref != nullptr)
            il2cpp_pinvoke_char_array_ansi_decode(buf, bufLen, ref);
        if (slot != nullptr) detail::unpin_array_slot(slot);
        std::free(buf);
    }
    operator char*() const { return buf; }
};

inline Il2CppPinvokeCharArrayAnsiArg il2cpp_pinvoke_char_array_ansi(
    Il2CppArray* a, int32_t copyIn, int32_t copyOut)
{
    return Il2CppPinvokeCharArrayAnsiArg(a, copyIn, copyOut);
}

//------------------------------------------------------------------------------
// bool[] parameter. net48 does NOT hand the callee this model's 1-byte cells:
// it converts the array to a native BOOL/int32 buffer (4 bytes per element by
// DEFAULT) and copies back afterwards. A
// `[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U1/I1)]`
// narrows the native cell to ONE byte, which happens to match this model's
// storage and needs no conversion at all.
//
// MEASURED net48 contract (Framework64 v4.0.30319; raw byte dumps with 1-byte
// and 4-byte CONTROL arrays so the probe is known to read correctly --
// .opencode/tmp/oracle-boolsem3 / oracle-boolsem4):
//   no MarshalAs / ArraySubType=BOOL/I4 -> 4 bytes per element
//   ArraySubType=U1 / I1                -> 1 byte per element
//   null array                          -> NULL
//   empty array                         -> NON-NULL
//   seed:  default/[In,Out] converted input, [Out] a ZEROED buffer
//   copy-back: default/[In] NONE, [In,Out]/[Out] YES
//
// This holder covers the CONVERTING (4-byte) case only; the 1-byte case uses
// the existing zero-copy Il2CppPinvokeArrayArg (the widths already match).
//------------------------------------------------------------------------------
struct Il2CppPinvokeBoolArrayArg
{
    Il2CppArray* ref;   // owner's pinned slot (the GC root/pin target)
    Il2CppArray** slot; // &ref for the owner, copied verbatim into value copies
    uint8_t* buf;       // malloc'd NATIVE-ELEMENT buffer (null for a null array)
    int32_t count;      // element count
    int32_t width;      // native bytes per element: 4 (default BOOL) or 1 (U1/I1)
    bool copyBack;
    bool owner;

    Il2CppPinvokeBoolArrayArg(Il2CppArray* a, int32_t copyIn, int32_t copyOut, int32_t elemWidth)
        : ref(a), slot(nullptr), buf(nullptr), count(0),
          width(elemWidth == 1 ? 1 : 4), copyBack(copyOut != 0), owner(true)
    {
        if (a == nullptr) return; // null array -> NULL pointer (net48)
        slot = &ref;
        detail::pin_array_slot(slot);
        count = a->length;
        const size_t bytes = (size_t)(count > 0 ? count : 1) * (size_t)width;
        buf = (uint8_t*)std::calloc(bytes, 1);
        if (buf == nullptr) return;
        if (copyIn)
        {
            // Managed cells are ONE byte holding 0/1. Widen to the native cell:
            // 4-byte int32 BOOL by default, or a plain byte for U1/I1.
            const uint8_t* src = reinterpret_cast<const uint8_t*>(a->data);
            if (width == 1)
            {
                for (int32_t i = 0; i < count; i++) buf[i] = src[i] != 0 ? 1 : 0;
            }
            else
            {
                int32_t* dst = reinterpret_cast<int32_t*>(buf);
                for (int32_t i = 0; i < count; i++) dst[i] = src[i] != 0 ? 1 : 0;
            }
        }
        // [Out]: the calloc above IS net48's zeroed temp (measured).
    }
    Il2CppPinvokeBoolArrayArg(const Il2CppPinvokeBoolArrayArg& o)
        : ref(o.ref), slot(o.slot), buf(o.buf), count(o.count), width(o.width),
          copyBack(o.copyBack), owner(false) {}
    Il2CppPinvokeBoolArrayArg& operator=(const Il2CppPinvokeBoolArrayArg&) = delete;
    ~Il2CppPinvokeBoolArrayArg()
    {
        if (!owner) return;
        if (copyBack && buf != nullptr && ref != nullptr)
        {
            // Narrow each native cell back to a 0/1 managed byte (net48
            // normalizes: a non-zero BOOL becomes true, measured).
            uint8_t* dst = reinterpret_cast<uint8_t*>(ref->data);
            if (width == 1)
            {
                for (int32_t i = 0; i < count; i++) dst[i] = buf[i] != 0 ? 1 : 0;
            }
            else
            {
                const int32_t* src = reinterpret_cast<const int32_t*>(buf);
                for (int32_t i = 0; i < count; i++) dst[i] = src[i] != 0 ? 1 : 0;
            }
        }
        if (slot != nullptr) detail::unpin_array_slot(slot);
        std::free(buf);
    }
    // The native declaration decides the pointer type: int32_t* for the 4-byte
    // BOOL cell, unsigned char* for the U1/I1 cell.
    operator int32_t*() const { return reinterpret_cast<int32_t*>(buf); }
    operator uint8_t*() const { return buf; }
};

inline Il2CppPinvokeBoolArrayArg il2cpp_pinvoke_bool_array(
    Il2CppArray* a, int32_t copyIn, int32_t copyOut, int32_t elemWidth)
{
    return Il2CppPinvokeBoolArrayArg(a, copyIn, copyOut, elemWidth);
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

//------------------------------------------------------------------------------
// [MarshalAs(UnmanagedType.AsAny)] OBJECT parameter -> `void*`.
//
// MEASURED net48 semantics (executable oracle, .opencode/tmp/
// impl-pinvoke-object/phase0/oracle; both a raw byte dump and a parameter
// received as an INTEGER so "value vs address" cannot be confused):
//
//   * the callee receives the ADDRESS OF A NATIVE TEMP COPY of the boxed
//     value's unmanaged representation -- NOT the value itself and NOT the
//     managed box. Measured with `void* any_probe(void* p, ...)`: p is a
//     readable stack/heap temp whose first 8 bytes are the boxed IntPtr value
//     (the integer-typed probe reports x != payload && *(uint64_t*)x ==
//     payload). The same row measured against the REAL user32!CreateWindowEx:
//     WM_CREATE's CREATESTRUCT.lpCreateParams is that temp address and
//     *lpCreateParams is the boxed IntPtr value.
//   * a boxed user struct arrives as the struct's field bytes at offset 0 of
//     the temp (the managed box header is NOT part of the image).
//   * null -> NULL.
//   * [In, Out] copies the temp BACK into the managed box after the call;
//     [In] alone (and the default, no bits) does NOT (measured: a native write
//     to the temp is lost without Out, observable with Out).
//   * a REFERENCE-typed payload (string / array / a [StructLayout] class) is
//     marshalled by net48 as a temp copy of ITS OWN marshal image. This bridge
//     implements the VALUE-TYPE-BOX family only; a reference payload raises a
//     self-identifying managed NotSupportedException (docs/audit/b5-gaps.md
//     section 32 carries the gap and its unlock condition).
//
// GC SAFETY: the call may trigger a compacting collection (a native callee can
// callback into managed code). The managed BOX is per-argument rooted for the
// whole call full-expression, so the copy-back re-reads it through the root
// slot (collector-updated). Copies travel with owner=false.
//------------------------------------------------------------------------------
namespace detail {

// Payload offset/size of a boxed value type. Builtin box classes live at
// header + one aligned payload slot (instanceSize 16, 32 for the 16-byte
// payloads); a USER struct box is allocated with the struct's OWN payload
// bytes and its descriptor's instanceSize IS sizeof({T}) (no header), so the
// two families need different arithmetic. Returns false for anything that is
// not a value-type box (a reference type -- string/array/class).
inline bool asany_box_payload(Il2CppClass* klass, int32_t* offset, int32_t* size)
{
    *offset = static_cast<int32_t>(sizeof(Il2CppObject));
    const int32_t builtin = klass->instanceSize - static_cast<int32_t>(sizeof(Il2CppObject));
    Il2CppClass* const builtins[] = {
        &System_Boolean_class, &System_Char_class, &System_Byte_class, &System_SByte_class,
        &System_Int16_class, &System_UInt16_class, &System_Int32_class, &System_UInt32_class,
        &System_Int64_class, &System_UInt64_class, &System_Single_class, &System_Double_class,
        &System_Decimal_class, &System_DateTime_class, &System_TimeSpan_class,
        &System_Guid_class, &System_DateTimeOffset_class,
        &System_IntPtr_class, &System_UIntPtr_class,
    };
    for (Il2CppClass* b : builtins)
    {
        if (klass == b)
        {
            *size = builtin > 0 ? builtin : static_cast<int32_t>(sizeof(void*));
            return true;
        }
    }
    // A translated USER value type: baseClass chain reaches System.ValueType.
    // (An ENUM descriptor bases on System.Enum -- a BCL root that emits no
    // descriptor -- and is never a System.ValueType box; it falls through and
    // is rejected below, matching the box-site gate.)
    for (Il2CppClass* c = klass; c != nullptr; c = c->baseClass)
    {
        if (c == &System_ValueType_class)
        {
            *size = klass->instanceSize;
            return *size > 0;
        }
    }
    return false;
}

} // namespace detail

struct Il2CppPinvokeAsAnyArg
{
    Il2CppObject* obj;     // rooted box (null for a null argument)
    Il2CppObject** slot;   // &obj (the GC root target), null when obj == null
    void* data;            // native temp copy (null for a null argument)
    int32_t dataSize;
    bool copyBack;
    bool owner;

    Il2CppPinvokeAsAnyArg(Il2CppObject* o, int32_t copyBackFlag)
        : obj(o), slot(nullptr), data(nullptr), dataSize(0),
          copyBack(copyBackFlag != 0), owner(true)
    {
        if (o == nullptr) return; // null -> nullptr (net48)
        int32_t offset = 0;
        int32_t size = 0;
        if (o->klass == nullptr || !detail::asany_box_payload(o->klass, &offset, &size))
        {
            il2cpp_throw_new(
                "System.NotSupportedException,System.NotSupportedException,"
                "System.SystemException,System.Exception",
                "P/Invoke [MarshalAs(UnmanagedType.AsAny)] object argument: the argument is a "
                "REFERENCE type (string/array/class). net48 marshals it as a temp copy of its own "
                "marshal image; this bridge implements value-type boxes only. See "
                "docs/audit/b5-gaps.md section 32.");
        }
        slot = &obj;
        // Double root/pin pair (same contract as the array helpers above):
        // the callee may callback into managed code and trigger a compacting
        // collection, and a pin alone does not mark while a root alone does
        // not stop the move.
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(slot));
        il2cpp_gc_add_pinned(reinterpret_cast<void**>(slot));
        dataSize = size;
        data = std::malloc(static_cast<std::size_t>(size));
        if (data == nullptr)
        {
            std::fprintf(stderr, "il2cpp_pinvoke AsAny argument: out of memory (%d bytes)\n",
                         static_cast<int>(size));
            std::abort();
        }
        std::memcpy(data, reinterpret_cast<char*>(o) + offset, static_cast<std::size_t>(size));
    }
    Il2CppPinvokeAsAnyArg(const Il2CppPinvokeAsAnyArg& o)
        : obj(o.obj), slot(o.slot), data(o.data), dataSize(o.dataSize),
          copyBack(o.copyBack), owner(false) {}
    Il2CppPinvokeAsAnyArg& operator=(const Il2CppPinvokeAsAnyArg&) = delete;
    ~Il2CppPinvokeAsAnyArg()
    {
        if (!owner) return;
        if (slot != nullptr)
        {
            if (copyBack && dataSize > 0 && data != nullptr)
            {
                Il2CppObject* live = *slot; // collector-updated root
                if (live != nullptr)
                {
                    std::memcpy(reinterpret_cast<char*>(live) + sizeof(Il2CppObject),
                                data, static_cast<std::size_t>(dataSize));
                }
            }
            il2cpp_gc_remove_pinned(reinterpret_cast<void**>(slot));
            il2cpp_gc_remove_static_root(reinterpret_cast<void**>(slot));
        }
        std::free(data);
    }
    template <typename T>
    operator T() const { return reinterpret_cast<T>(data); }
};

inline Il2CppPinvokeAsAnyArg il2cpp_pinvoke_asany(Il2CppObject* o, int32_t copyBack)
{
    return Il2CppPinvokeAsAnyArg(o, copyBack);
}

//------------------------------------------------------------------------------
// StringBuilder parameters (System.Text.StringBuilder -> native UTF-16 buffer).
//
// MEASURED net48 semantics (tests/cases/pinvoke-stringbuilder -- the case's
// oracle records; NOT a temp-copy/copy-back marshal):
//   * the callee receives the builder's OWN chunk buffer, Capacity units wide
//     (Capacity, NOT Length: a 20-unit write into a 5-unit "hello" with
//     Capacity 32 lands and reads back whole);
//   * the buffer is prefilled with the current content and a NUL at index
//     Length (the marshaller writes that terminator);
//   * after the call Length = strnlen(buffer, Capacity): a NUL-terminated
//     shorter write SHRINKS Length, a longer one (up to Capacity) GROWS it,
//     and content with no NUL inside Capacity reads back as Capacity units;
//   * residual units between the new and the old length stay visible
//     ("ab" over "ZZZZZZ" with Capacity 8 -> "abZZZZ", Length 6).
// The net48 Capacity mutation observed when a callee writes the WHOLE buffer
// is a marshaller-internal chunk resize, not part of Length semantics: this
// model keeps the managed Capacity (the buffer the callee already received).
// The oracle scans an UNBOUNDED strlen over a Capacity+1 temp whose tail is
// uninitialised, so a no-NUL write that fills all Capacity units is not
// deterministic there; this model scans exactly Capacity units (the
// deterministic rule the case asserts -- see the residual-content shape above).
//
// CharSet.Ansi is NOT wired: net48 converts through the system ANSI code page
// (CP_ACP) and this transpiler's ANSI string marshalling is UTF-8 with no
// CP_ACP conversion, so silently handing UTF-8 to an Ansi callee would corrupt
// non-ASCII text. The declaration site raises a self-identifying transpile-time
// NotSupportedException (registered in docs/audit/b5-gaps.md section 29).
//
// GC SAFETY (hard requirement, same contract as the array bridge above): the
// callee may call back into managed code (WndProc / callback) and trigger a
// compacting collection mid-call (MEASURED: tests/cases/pinvoke-stringbuilder
// record g1 forces GC.Collect from a delegate while the native side holds the
// buffer; with the pin removed the chunk moves and the readback falls back to
// the pre-call content). The chunk address is valid only while the BUILDER and
// its chars ARRAY are BOTH rooted AND immovable, so the outermost temporary
// registers the double root/pin pair for the whole call full-expression (a pin
// alone does not mark, a root alone does not stop the move) and the destructor
// re-reads both slots (collector-updated) before writing Length back. Copies
// travel with owner=false; the owner's destructor runs after the native call.
//------------------------------------------------------------------------------
namespace detail {

// Register the double root/pin pair on any GC-reference slot (the array
// helpers above are the typed instance of this contract).
inline void pin_reference_slot(void** slot)
{
    il2cpp_gc_add_static_root(slot);
    il2cpp_gc_add_pinned(slot);
}

inline void unpin_reference_slot(void** slot)
{
    il2cpp_gc_remove_pinned(slot);
    il2cpp_gc_remove_static_root(slot);
}

} // namespace detail

struct Il2CppPinvokeStringBuilderArg
{
    Il2CppStringBuilder* builder;
    Il2CppStringBuilder** builderSlot;
    Il2CppArray* chars;
    Il2CppArray** charsSlot;
    void* data;        // UTF-16 chunk payload (valid while rooted + pinned)
    char16_t* wide;    // chunk payload as char16_t*
    int32_t capacity;  // chunk size in UTF-16 units (== chars->length)
    int32_t contentLength;
    bool isOwner;

    explicit Il2CppPinvokeStringBuilderArg(Il2CppStringBuilder* sb)
        : builder(sb), builderSlot(nullptr), chars(nullptr), charsSlot(nullptr),
          data(nullptr), wide(nullptr), capacity(0), contentLength(0), isOwner(true)
    {
        if (sb == nullptr) return; // null builder -> null pointer (net48)
        builderSlot = &builder;
        detail::pin_reference_slot(reinterpret_cast<void**>(builderSlot));
        capacity = builder->capacity;
        contentLength = builder->length;
        chars = builder->chars;
        if (chars != nullptr)
        {
            charsSlot = &chars;
            detail::pin_reference_slot(reinterpret_cast<void**>(charsSlot));
            wide = reinterpret_cast<char16_t*>(chars->data);
            data = wide;
            // net48 prefills the native buffer with the content followed by a
            // NUL at index Length. The chunk beyond Length is free space, so
            // the terminator write cannot perturb the managed content.
            if (contentLength >= 0 && contentLength < capacity && contentLength < chars->length)
                wide[contentLength] = 0;
        }
    }
    Il2CppPinvokeStringBuilderArg(const Il2CppPinvokeStringBuilderArg& o)
        : builder(o.builder), builderSlot(o.builderSlot), chars(o.chars),
          charsSlot(o.charsSlot), data(o.data), wide(o.wide), capacity(o.capacity),
          contentLength(o.contentLength), isOwner(false)
    {
    }
    Il2CppPinvokeStringBuilderArg& operator=(const Il2CppPinvokeStringBuilderArg&) = delete;

    ~Il2CppPinvokeStringBuilderArg()
    {
        if (!isOwner || builderSlot == nullptr) return;
        // Re-read both roots: a collection during the native call updates the
        // slot contents (the pin already kept these objects immovable).
        Il2CppStringBuilder* sb = *builderSlot;
        Il2CppArray* buf = charsSlot != nullptr ? *charsSlot : nullptr;
        if (sb != nullptr && buf != nullptr)
        {
            const int32_t arrayLength = buf->length;
            const int32_t limit = capacity < arrayLength ? capacity : arrayLength;
            const char16_t* text = reinterpret_cast<const char16_t*>(buf->data);
            int32_t n = 0;
            while (n < limit && text[n] != 0) ++n;
            sb->length = n;
        }
        if (charsSlot != nullptr) detail::unpin_reference_slot(reinterpret_cast<void**>(charsSlot));
        detail::unpin_reference_slot(reinterpret_cast<void**>(builderSlot));
    }

    template <typename T>
    operator T() const
    {
        return reinterpret_cast<T>(data);
    }
};

inline Il2CppPinvokeStringBuilderArg il2cpp_pinvoke_stringbuilder(Il2CppStringBuilder* sb)
{
    return Il2CppPinvokeStringBuilderArg(sb);
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
