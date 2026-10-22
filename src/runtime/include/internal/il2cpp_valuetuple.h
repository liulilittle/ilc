//------------------------------------------------------------------------------
// il2cpp_valuetuple.h -- System.ValueTuple`1..8 support (C# 7 tuple syntax).
//
// A C# tuple `(int, string)` IS the ordinary BCL value type
// System.ValueTuple`2<Int32,String>: csc emits a normal `newobj` of the
// struct's .ctor, `ldfld` of its Item1/Item2 fields, and calls to its
// Equals/GetHashCode/ToString overrides. The transpiled assembly references
// the type through a TypeRef (mscorlib) and therefore has NO TypeDef for it,
// so the transpiler must provide the struct itself.
//
// MODEL (matches the transpiler's existing flat-primitive tuple structs, see
// CodeGenerator.TupleStructCppName): the generated C++ struct is
//     struct System_ValueTuple_2_int_string { int32_t Item1; Il2CppObject* Item2; };
// emitted file-scope in generated.h, so the local, the ctor call, the field
// reads and the P5 icall return path all agree on ONE type.
//
// This header supplies the members that are NOT field access:
//   * ToString()   -- net48 text: "(1, a)"; a null reference element renders
//                     as the EMPTY string ("(1, )"); a 1-tuple renders "(5)".
//   * GetHashCode()-- net48: ValueTuple.GetHashCode combines the element
//                     hashes as h = (h * 397) ^ elementHash, seeded with 0.
//   * Equals(...)  -- net48 structural equality (same arity/args, null-safe).
//
// The functions are name-mangled per instantiation by the TRANSPILER (they are
// emitted as ordinary generated methods, exactly like a user struct's), so
// this header only declares the runtime primitive the emitted bodies call:
// a single element-formatting helper plus a hash combiner.
//------------------------------------------------------------------------------
#pragma once

#include <cstdint>

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
//
// Returns the interned element text.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_tuple_element_to_string(char kind, int64_t bits,
                                             Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_combine_hash -- net48 ValueTuple.GetHashCode step:
//   h = (h * 397) ^ elementHash
// (unchecked int32 arithmetic). Callers seed h = 0 and fold every element.
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_combine_hash(int32_t h, int32_t elementHash);

//------------------------------------------------------------------------------
// il2cpp_tuple_element_hash -- the net48 hash of ONE tuple element.
//
//   Same (kind, bits, obj) contract as il2cpp_tuple_element_to_string; a null
//   reference hashes to 0 (net48 EqualityComparer<T>.Default.GetHashCode(null)
//   == 0). Primitives use the net48 per-type rules (Int32 -> value,
//   Int64 -> low^high, Double -> the bit-pattern rule, Boolean -> 0/1,
//   Char -> value, reference -> string ordinal hash / object identity).
//------------------------------------------------------------------------------
int32_t il2cpp_tuple_element_hash(char kind, int64_t bits, Il2CppObject* obj);

//------------------------------------------------------------------------------
// il2cpp_tuple_double_bits -- reinterpret a double as its 64-bit pattern (the
// element helpers carry every element in one int64 slot; a double must ride
// its BITS, not a value cast).
//------------------------------------------------------------------------------
int64_t il2cpp_tuple_double_bits(double value);

} // namespace runtime
} // namespace il2cpp
