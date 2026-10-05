//------------------------------------------------------------------------------
// invoker.cpp -- unified-signature reflection invoker (see invoker.h for the
// contract).
//
// Pipeline:
//   1. DecodeSignature parses the MethodSignatureCoder encoding
//      ("RET[:p1,p2,...]") into a return kind + parameter kind list. The
//      parameter kind is exactly the slot family the argbuf uses
//      (int32 / int64 / double / reference pointer).
//   2. A compile-time typed call table covers every parameter-kind sequence
//      up to kInvokerMaxParams (4 kinds ^ n, indexed as a base-4 number with
//      a per-length offset). Each entry is a generated function that
//      extracts the slots by their kinds, calls the real AOT function
//      through the matching function-pointer type, and writes the return
//      value by kind. No hand-written per-signature switch trees; the
//      fnPtr reinterpret_cast round-trips the exact stored type.
//   3. The table is built once on first use (function-local magic static:
//      thread-safe initialization); dispatch is a constant-time lookup.
//
// Error handling is verify-then-mutate: every input is validated before any
// call is dispatched, and ret is never touched on failure.
//------------------------------------------------------------------------------

#include "invoker.h"

#include <cstddef>
#include <cstring>
#include <utility>

namespace il2cpp {

namespace {

// Parameter slot kinds -- index into the base-4 sequence space of the call
// table. Must match the slot family the argbuf stores (invoker.h layout).
constexpr int32_t kKindI32 = 0;
constexpr int32_t kKindI64 = 1;
constexpr int32_t kKindDbl = 2;
constexpr int32_t kKindRef = 3;

// Return kinds -- switched inside every typed call entry.
constexpr int32_t kRetVoid = 0;
constexpr int32_t kRetI32  = 1;
constexpr int32_t kRetI64  = 2;
constexpr int32_t kRetDbl  = 3;
constexpr int32_t kRetRef  = 4;

// One decoded signature: return kind + parameter kind list.
struct DecodedSignature
{
    int32_t retKind;
    int32_t paramCount;
    int32_t paramKinds[kInvokerMaxParams];
};

//------------------------------------------------------------------------------
// Signature decoding.
//------------------------------------------------------------------------------

// Map one MethodSignatureCoder parameter encoding to its slot kind by its
// first character (the encodings are disjoint on the first character):
//   i/b/c/i1/u1/i2/u2/n -> int32 slot   (bool/char/sbyte/byte/short/ushort/
//                                        native-int live in int32 slots)
//   l/q                 -> int64 slot   (long/ulong)
//   f/d                 -> double slot  (float stored widened)
//   r:*                 -> ref slot     (string/object/array/class pointer)
//   everything else (v, V:*, B:*, T:*, unknown) -> -1 (parse failure).
int32_t ParamKind(const char* p)
{
    switch (p[0])
    {
    case 'i': case 'b': case 'c': case 'u': case 'n':
        return kKindI32;
    case 'l': case 'q':
        return kKindI64;
    case 'f': case 'd':
        return kKindDbl;
    case 'r':
        return kKindRef;
    default:
        return -1;
    }
}

// Decode "RET[:p1,p2,...]" into a DecodedSignature. Returns kInvokerOk or a
// negative InvokerError (bad signature / too many params).
int32_t DecodeSignature(const char* sig, DecodedSignature& out)
{
    out.paramCount = 0;

    switch (sig[0])
    {
    case 'V': out.retKind = kRetVoid; break;
    case 'I': out.retKind = kRetI32;  break;
    case 'L': out.retKind = kRetI64;  break;
    case 'D': out.retKind = kRetDbl;  break;
    case 'R': out.retKind = kRetRef;  break;
    default:  return kInvokerErrorBadSignature;
    }

    const char* p = std::strchr(sig, ':');
    if (p == nullptr)
    {
        return kInvokerOk; // zero-parameter signature
    }

    ++p;
    for (;;)
    {
        if (out.paramCount >= kInvokerMaxParams)
        {
            return kInvokerErrorTooManyParams;
        }
        const char* comma = std::strchr(p, ',');
        const char* end = (comma == nullptr) ? p + std::strlen(p) : comma;
        if (end == p)
        {
            return kInvokerErrorBadSignature; // empty parameter encoding
        }
        const int32_t kind = ParamKind(p);
        if (kind < 0)
        {
            return kInvokerErrorBadSignature;
        }
        out.paramKinds[out.paramCount++] = kind;
        if (comma == nullptr)
        {
            break;
        }
        p = comma + 1;
    }
    return kInvokerOk;
}

//------------------------------------------------------------------------------
// Compile-time typed call table.
//
// For every parameter-kind sequence (4 kinds ^ length) a typed call function
// is instantiated: SlotValue<Kind> extracts each slot by its family, the
// real function is called through the matching function-pointer type, and
// the return value is written by its kind. Sequences are indexed as base-4
// numbers with a per-length offset, so lengths never collide:
//   index(N, kinds) = LengthOffset(N) + sum(kinds[i] * 4^i)
// Table size for N <= kInvokerMaxParams = 4: sum(4^0..4^4) = 341 entries.
//------------------------------------------------------------------------------

constexpr int32_t Pow4(int32_t n)
{
    int32_t r = 1;
    for (int32_t i = 0; i < n; ++i)
    {
        r *= 4;
    }
    return r;
}

// sum(4^0 .. 4^(n-1)) -- start index of the length-n region of the table.
constexpr int32_t LengthOffset(int32_t n)
{
    int32_t total = 0;
    for (int32_t m = 0; m < n; ++m)
    {
        total += Pow4(m);
    }
    return total;
}

// Every table entry has this shape: dispatch one call, return an InvokerError.
using EntryFn = int32_t (*)(void* fnPtr, const il2cpp_invoker_slot* args,
                            void* ret, int32_t retKind);

constexpr int32_t kTableSize = LengthOffset(kInvokerMaxParams + 1);

// Slot extraction by kind: how the argbuf slot is read for the C++ call.
template <int32_t K>
struct SlotValue;

template <>
struct SlotValue<kKindI32>
{
    using Type = int32_t;
    static Type Get(const il2cpp_invoker_slot* s) { return s->i32; }
};

template <>
struct SlotValue<kKindI64>
{
    using Type = int64_t;
    static Type Get(const il2cpp_invoker_slot* s) { return s->i64; }
};

template <>
struct SlotValue<kKindDbl>
{
    using Type = double;
    static Type Get(const il2cpp_invoker_slot* s) { return s->dbl; }
};

template <>
struct SlotValue<kKindRef>
{
    using Type = void*;
    static Type Get(const il2cpp_invoker_slot* s) { return s->ptr; }
};

// NthKind<I, Kinds...>: the I-th kind of a sequence (compile-time).
template <int32_t I, int32_t K0, int32_t... Ks>
struct NthKind
{
    static constexpr int32_t Value = NthKind<I - 1, Ks...>::Value;
};

template <int32_t K0, int32_t... Ks>
struct NthKind<0, K0, Ks...>
{
    static constexpr int32_t Value = K0;
};

// CallArgs: build the typed call and invoke it. The index_sequence expands
// one slot extraction per parameter position, each through its own kind.
template <typename Ret, int32_t... Kinds, std::size_t... I>
Ret CallArgs(void* fnPtr, const il2cpp_invoker_slot* args,
             std::index_sequence<I...>)
{
    using FnType = Ret (*)(typename SlotValue<Kinds>::Type...);
    auto fn = reinterpret_cast<FnType>(fnPtr);
    return fn(SlotValue<NthKind<I, Kinds...>::Value>::Get(args + I)...);
}

// One typed call entry: switch on the return kind, call, write the return.
template <int32_t... Kinds>
int32_t CallEntry(void* fnPtr, const il2cpp_invoker_slot* args, void* ret,
                  int32_t retKind)
{
    using Indices = std::make_index_sequence<sizeof...(Kinds)>;
    switch (retKind)
    {
    case kRetVoid:
        CallArgs<void, Kinds...>(fnPtr, args, Indices{});
        return kInvokerOk;
    case kRetI32:
        *static_cast<int32_t*>(ret) =
            CallArgs<int32_t, Kinds...>(fnPtr, args, Indices{});
        return kInvokerOk;
    case kRetI64:
        *static_cast<int64_t*>(ret) =
            CallArgs<int64_t, Kinds...>(fnPtr, args, Indices{});
        return kInvokerOk;
    case kRetDbl:
        *static_cast<double*>(ret) =
            CallArgs<double, Kinds...>(fnPtr, args, Indices{});
        return kInvokerOk;
    case kRetRef:
        *static_cast<void**>(ret) =
            CallArgs<void*, Kinds...>(fnPtr, args, Indices{});
        return kInvokerOk;
    default:
        return kInvokerErrorBadSignature; // unreachable: decode already mapped
    }
}

// Recursive table builder: at each depth register the current sequence's
// entry, then recurse once per kind for the next position. Sequences of
// EVERY length 0..kInvokerMaxParams get an entry (zero-param calls included).
template <int32_t N, int32_t Seq, int32_t... Kinds>
struct TableBuilder
{
    static void Fill(EntryFn* table)
    {
        table[LengthOffset(N) + Seq] = &CallEntry<Kinds...>;
        if constexpr (N < kInvokerMaxParams)
        {
            TableBuilder<N + 1, Seq + kKindI32 * Pow4(N), Kinds..., kKindI32>::Fill(table);
            TableBuilder<N + 1, Seq + kKindI64 * Pow4(N), Kinds..., kKindI64>::Fill(table);
            TableBuilder<N + 1, Seq + kKindDbl * Pow4(N), Kinds..., kKindDbl>::Fill(table);
            TableBuilder<N + 1, Seq + kKindRef * Pow4(N), Kinds..., kKindRef>::Fill(table);
        }
    }
};

// Leaf depth: register the last length region, do not recurse.
template <int32_t Seq, int32_t... Kinds>
struct TableBuilder<kInvokerMaxParams, Seq, Kinds...>
{
    static void Fill(EntryFn* table)
    {
        table[LengthOffset(kInvokerMaxParams) + Seq] = &CallEntry<Kinds...>;
    }
};

// The typed call table, built once on first use. Magic static: thread-safe
// initialization guaranteed by the language (constraint: static storage,
// no global-constructor heap allocation).
EntryFn* Table()
{
    static EntryFn storage[kTableSize];
    static EntryFn* table = [] {
        TableBuilder<0, 0>::Fill(storage);
        return storage;
    }();
    return table;
}

// Validate inputs, decode, dispatch. Verify-then-mutate: nothing is written
// (and no call is made) before every check passes.
int32_t Invoke(void* fnPtr, const char* signature,
               const il2cpp_invoker_slot* args, int32_t argCount, void* ret)
{
    if (fnPtr == nullptr || signature == nullptr || signature[0] == '\0')
    {
        return kInvokerErrorInvalidArgs;
    }
    if (argCount < 0 || (argCount > 0 && args == nullptr))
    {
        return kInvokerErrorInvalidArgs;
    }

    DecodedSignature sig;
    const int32_t rc = DecodeSignature(signature, sig);
    if (rc != kInvokerOk)
    {
        return rc;
    }
    if (sig.paramCount != argCount)
    {
        return kInvokerErrorArity;
    }
    if (sig.retKind != kRetVoid && ret == nullptr)
    {
        return kInvokerErrorInvalidArgs;
    }

    int32_t seq = 0;
    for (int32_t i = 0; i < sig.paramCount; ++i)
    {
        seq += sig.paramKinds[i] * Pow4(i);
    }
    return Table()[LengthOffset(sig.paramCount) + seq](fnPtr, args, ret,
                                                       sig.retKind);
}

} // namespace

int32_t il2cpp_invoker_invoke(void* fnPtr, const char* signature,
                              const il2cpp_invoker_slot* args,
                              int32_t argCount, void* ret)
{
    return Invoke(fnPtr, signature, args, argCount, ret);
}

//------------------------------------------------------------------------------
// il2cpp_invoker_decode_signature -- public signature introspection (see
// invoker.h). Reuses the internal DecodeSignature so the R3 argbuf builder
// sees the EXACT slot kinds il2cpp_invoker_invoke dispatches on.
//------------------------------------------------------------------------------
int32_t il2cpp_invoker_decode_signature(const char* signature,
                                        int32_t* outRetKind,
                                        int32_t* outParamKinds,
                                        int32_t outParamCapacity,
                                        int32_t* outParamCount)
{
    if (signature == nullptr || outRetKind == nullptr || outParamKinds == nullptr
        || outParamCount == nullptr)
    {
        return kInvokerErrorInvalidArgs;
    }
    DecodedSignature sig;
    const int32_t rc = DecodeSignature(signature, sig);
    if (rc != kInvokerOk)
    {
        return rc;
    }
    if (outParamCapacity < sig.paramCount)
    {
        return kInvokerErrorTooManyParams;
    }
    *outRetKind = (sig.retKind == kRetVoid) ? -1
        : (sig.retKind == kRetI32) ? kKindI32
        : (sig.retKind == kRetI64) ? kKindI64
        : (sig.retKind == kRetDbl) ? kKindDbl
        : kKindRef;
    for (int32_t i = 0; i < sig.paramCount; ++i)
    {
        outParamKinds[i] = sig.paramKinds[i];
    }
    *outParamCount = sig.paramCount;
    return kInvokerOk;
}

} // namespace il2cpp
