//------------------------------------------------------------------------------
// il2cpp_valuetuple.h -- System.ValueTuple`0..8 support (C# 7 tuple syntax).
//
// A C# tuple `(int, string)` IS the ordinary BCL value type
// System.ValueTuple`2<Int32,String>: csc emits a normal `newobj` of the
// struct's .ctor, `ldfld` of its Item1/Item2 fields, and calls to its
// Equals/GetHashCode/ToString overrides. The transpiled assembly references
// the type through a TypeRef (mscorlib) and therefore has NO TypeDef for it,
// so the transpiler must provide the struct itself.
//
// MODEL: the generated C++ struct is emitted file-scope in generated.h, e.g.
//     struct System_ValueTuple_2_int_string { int32_t Item1; Il2CppObject* Item2; };
// so the local, the ctor call, the field reads and the icall return path all
// agree on ONE type. Element kinds are decided STRUCTURALLY (from the metadata
// signature), never from the flattened name: a nested ValueTuple element is
// carried BY VALUE, a user struct by value, a BCL value struct by value.
//
// This header supplies the members that are NOT field access:
//   * ToString()   -- net48 text: "(1, a)"; a null reference element renders
//                     as the EMPTY string ("(1, )"); a 1-tuple renders "(5)";
//                     an 8-tuple flattens its TRest ("(1, ..., 8)").
//   * ToStringInner() -- the ITupleInternal.ToStringEnd contract (from the
//                     first TRest element through ')', leading ", ").
//   * GetHashCode()-- net48: Combine(h, e) = unchecked((rol5(h) + h) ^ e),
//                     seeded with the FIRST element's hash; an 8-tuple keeps
//                     only its LAST 8 slots (the TRest tail wins).
//   * Equals(...)  -- net48 structural equality (same arity/args).
//
// The functions are name-mangled per instantiation by the TRANSPILER (they are
// emitted as ordinary generated methods, exactly like a user struct's), so
// this header only declares the runtime primitives the emitted bodies call.
//------------------------------------------------------------------------------
#pragma once

#include <cstdint>

#include "guid.h"
#include "il2cpp_nullable.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"

namespace il2cpp {
namespace runtime {

//------------------------------------------------------------------------------
// il2cpp_tuple_element_to_string -- the ToString() text of ONE tuple element.
//
//   kind:  the element's slot kind ('i' int32, 'l' int64, 'd' double,
//          'f' float, 'b' bool, 'c' char, 'r' managed reference)
//   bits:  the element's RAW SLOT BITS (the transpiler passes every element
//          through the same 64-bit carrier so one helper serves all kinds)
//   obj:   the managed reference for kind 'r' (unused otherwise; a null
//          reference renders as the EMPTY string, matching net48's
//          ValueTuple.ToString, which prints nothing for a null element)
//------------------------------------------------------------------------------
Il2CppString* il2cpp_tuple_element_to_string(char kind, int64_t bits,
                                             Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_ref_to_string -- the text of a REFERENCE element: null renders
// as the empty string, otherwise the object's own ToString.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_tuple_ref_to_string(Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_combine_hash -- net48 ValueTuple hash step:
//   h = unchecked((rol5(h) + h) ^ elementHash)
// (System.Numerics.Hashing.HashHelpers.Combine; the "(h * 397) ^ e" form this
// replaced was a different algorithm and produced wrong tuple hashes.)
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_combine_hash(int32_t h, int32_t elementHash);

//------------------------------------------------------------------------------
// il2cpp_tuple_element_hash -- the net48 hash of ONE tuple element.
//
//   Same (kind, bits, obj) contract as il2cpp_tuple_element_to_string; a null
//   reference hashes to 0. Primitives use the net48 per-type rules (Int32 ->
//   value, Int64 -> low^high, Double -> the bit-pattern rule, Boolean -> 0/1,
//   Char -> value, reference -> string ordinal hash / object identity).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_element_hash(char kind, int64_t bits, Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_single_hash -- net48 Single.GetHashCode: the 32-bit float
// pattern (0 for +-0). The value arrives widened to double (the tuple element
// slot model) and is narrowed back exactly.
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_single_hash(double value);

//------------------------------------------------------------------------------
// il2cpp_tuple_ref_hash -- a reference element's hash (0 for null).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_ref_hash(Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_double_bits -- reinterpret a double as its 64-bit pattern (the
// element helpers carry every element in one int64 slot; a double must ride
// its BITS, not a value cast).
//------------------------------------------------------------------------------
int64_t il2cpp_tuple_double_bits(double value);

//------------------------------------------------------------------------------
// USER-STRUCT elements (a translated value type used as a tuple element).
//
// net48 dispatches ToString/GetHashCode/Equals to the struct's own overrides
// when it has them (constrained. on a value type does NOT box), and falls back
// to the ValueType default otherwise:
//   * ToString  -> the type's full name;
//   * Equals    -> field-image equality (the CLR's CanCompareBits path);
//   * GetHashCode -> a CLR-internal MethodTable function that depends on the
//     loading MethodTable; no AOT runtime can reproduce its exact value, so a
//     deterministic field-image hash is used instead (documented deviation).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_tuple_user_struct_to_string(Il2CppClass* klass, const void* valueAddr);
int32_t il2cpp_tuple_user_struct_hash(Il2CppClass* klass, const void* valueAddr);
int32_t il2cpp_tuple_user_struct_equals(Il2CppClass* klass, const void* a, const void* b);
// RF-REFL: a translated USER struct element's ORDER comparison (the tuple
// IComparable/IStructuralComparable element-wise compare). net48 dispatches
// through Comparer<object>.Default, i.e. boxes the two values and calls
// IComparable.CompareTo; the runtime mirrors that by boxing both operands and
// delegating to il2cpp_icomparable_compare_to. A struct without an
// IComparable CompareTo throws NotSupportedException through the dispatcher
// (the net48 ArgumentException family is not reproduced -- documented).
int32_t il2cpp_tuple_user_struct_compare(Il2CppClass* klass, const void* a, const void* b);

} // namespace runtime
} // namespace il2cpp
