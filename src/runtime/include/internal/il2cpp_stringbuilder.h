#pragma once
//------------------------------------------------------------------------------
// il2cpp_stringbuilder.h -- managed System.Text.StringBuilder type (M5 BCL
// icall domain).
//
// Layout (aligned with the object-model convention; all sizes are for
// 8-byte pointers):
//
//   struct Il2CppStringBuilder
//   {
//       Il2CppObject obj;      // object header: klass pointer (System.Text.StringBuilder)
//       int32_t      capacity; // allocated UTF-16 units in the 'chars' buffer
//       int32_t      length;   // used UTF-16 units (StringBuilder.Length)
//       Il2CppArray* chars;    // UTF-16 buffer: a managed array (GC reference)
//   };
//
//   Slot grid: slot 0 = klass header (offset 0, not scanned), slot 1 =
//   capacity+length (offsets 8/12, value data), slot 2 = chars (offset 16,
//   a GC REFERENCE). The descriptor's refFieldBitmap therefore has bit 2
//   set: the collector scans the chars array through the bitmap, and the
//   array's payload is pure char16_t value data (isRefElem false -- never
//   scanned as references). instanceSize = 24.
//
//   GC safety contract:
//     * The builder is allocated through il2cpp_gc_alloc (fixed-size object)
//       -- never on the C++ stack.
//     * Every buffer REPLACEMENT (growth) stores the fresh array into
//       sb->chars immediately after allocation, with the write barrier
//       first (il2cpp_write_barrier) so old->young references stay
//       trackable. Between the array allocation and the store there is no
//       second allocation, so no collection can sweep the fresh array
//       before it is rooted by the field store.
//     * Callers must keep the builder reachable (frame root / field) while
//       methods that allocate are in flight -- same contract as every
//       managed object (see il2cpp_gc.h).
//
// Method mapping (the transpiler routes System.Text.StringBuilder members
// here; constructor icalls follow the System.Random convention -- generated
// newobj allocates the object and then calls the ctor icall):
//   StringBuilder()              -> icall_stringbuilder_ctor_default
//   StringBuilder(int capacity)  -> icall_stringbuilder_ctor_capacity
//   StringBuilder(string value)  -> icall_stringbuilder_ctor_string
//   Append(string)               -> icall_stringbuilder_append_s
//   Append(char)                 -> icall_stringbuilder_append_c
//   Append(int)                  -> icall_stringbuilder_append_i
//   Append(object)               -> icall_stringbuilder_append_o
//   AppendLine()                 -> icall_stringbuilder_append_line
//   AppendLine(string)           -> icall_stringbuilder_append_line_s
//   AppendFormat(string, object) -> icall_stringbuilder_append_format_1
//   AppendFormat(string, object, object) -> icall_stringbuilder_append_format_2
//   AppendFormat(string, object, object, object) -> icall_stringbuilder_append_format_3
//   String.Format(...)            -> icall_string_format_0/1/2/3 /
//                                    icall_string_format (see below)
//   Replace(string, string)      -> icall_stringbuilder_replace_s_s
//   Replace(char, char)          -> icall_stringbuilder_replace_c_c
//   Insert(int, string)          -> icall_stringbuilder_insert
//   Insert(int, int)             -> icall_stringbuilder_insert_i
//   Insert(int, long)            -> icall_stringbuilder_insert_l
//   Insert(int, double)          -> icall_stringbuilder_insert_d
//   Insert(int, bool)            -> icall_stringbuilder_insert_b
//   Insert(int, char)            -> icall_stringbuilder_insert_c
//   Insert(int, string, int)     -> icall_stringbuilder_insert_s_count
//   Remove(int, int)             -> icall_stringbuilder_remove
//   ToString()                   -> icall_stringbuilder_to_string
//   Length (get)                 -> icall_stringbuilder_get_length
//
// .NET semantics (net48 BASELINE, empirically verified against the
// reference runtime -- see the e2e case):
//   * Default capacity is 16; StringBuilder(int 0) falls back to 16;
//     StringBuilder(int < 0) throws ArgumentOutOfRangeException.
//   * StringBuilder(string value) preallocates max(16, value.Length) -- the
//     value never travels through the Append growth path (a 20-unit value
//     lands in a 20-unit buffer, not a 32-unit one).
//   * Append growth follows the net48 Grow policy:
//       newCapacity = max(required, max(Length, capacity) * 2)
//     (e.g. 16 + 17 units -> 32; 4 + 10 units -> 10; capacity 4 +
//     "abc"+"def"+"ghi" -> 16).
//   * Insert growth follows the net48 chunk policy: the new capacity is the
//     OLD capacity plus the inserted value's length (e.g. 2 + "abcde" -> 7,
//     4 + "abcdefghij" -> 14).
//   * Insert(int, value) numeric overloads (batch 170) format the value's
//     net48 ToString text FIRST and insert that text: int uses the invariant
//     decimal (same kernel as Append(int)), long/double/bool use the shared
//     to-string icalls (icall_int64_to_string / icall_double_to_string /
//     icall_bool_to_string -- the Append(long/double/bool) kernels), char
//     inserts its single unit. Insert(int, string, int count) repeats the
//     value 'count' times (count < 0 throws ArgumentOutOfRangeException).
//   * Append(string null) / Append(object null) / Insert(any, null) are
//     no-ops returning the same builder. Append returns the builder for
//     chaining.
//   * Insert: index < 0 or index > Length throws ArgumentOutOfRangeException.
//   * Remove: startIndex < 0, startIndex > Length, length < 0, or
//     startIndex > Length - length throws ArgumentOutOfRangeException.
//   * Append(int) formats the invariant decimal representation ("0",
//     "-12345", "2147483647", "-2147483648").
//   * AppendLine() appends the Windows newline "\r\n" (net48
//     Environment.NewLine); AppendLine(string null) appends the newline only.
//   * AppendFormat(format, args...) appends the composite-format expansion
//     of the args: "{{"/"}}" escape to a single brace, "{N[,alignment][:fmt]}"
//     expands to arg N formatted per the optional alignment/specifier (see
//     the full grammar note below). A null format throws
//     ArgumentNullException; an index >= arg count or a malformed placeholder
//     (unclosed brace, trailing brace, non-digit index, malformed alignment)
//     throws FormatException; a null arg expands to nothing (net48:
//     String.Empty).
//   * Replace(string, string) replaces EVERY occurrence of oldValue with
//     newValue (null newValue removes the occurrences). A null oldValue
//     throws ArgumentNullException; an EMPTY oldValue throws
//     ArgumentException ("String cannot be of zero length."). Replace(char,
//     char) replaces every matching unit in place. Growth follows the net48
//     chunk policy (empirically verified): a net growth (newValue.Length >
//     oldValue.Length) sets the capacity to the OLD capacity plus the total
//     delta (newValue.Length - oldValue.Length) * occurrenceCount (e.g.
//     capacity 4 + "ab"->"abc" -> 5; capacity 4 + "ab"->"abcdefghij" -> 12);
//     a shrink or same-width replace keeps the current capacity (net48 may
//     shrink through chunk merging; keeping it matches Remove's "does not
//     reduce the capacity" contract).
//
// Append(object) follows net48: null is a no-op, a string appends its
// payload, a boxed Int32 appends its invariant decimal, and any other
// managed object appends its ToString text via the icall_object_to_string
// dispatch (valuesemantics.cpp): boxed primitives format their value, any
// other object prints its type's full name -- the same dispatch String.
// Format / AppendFormat use (see the AppendFormat note below).
//
// AppendFormat / String.Format support the FULL composite-format item
// grammar "{N[,alignment][:formatString]}": "{{"/"}}" escaping, an optional
// ALIGNMENT width (positive = right-align, negative = left-align; the width
// is a MINIMUM -- a longer formatted text is never truncated; padding is
// spaces) and an optional FORMAT-STRING component, which the composite-format
// engine (formatext.cpp icall_format_composite_item) applies per the arg's
// type: boxed numerics route through the ToString(format) icalls, so
// "f2"/"x"/"X"/"D5"/... work; a string arg returns its payload unchanged
// (System.String is not IFormattable -- the specifier is ignored). Format
// arguments follow net48 ToString semantics (the icall_object_to_string
// dispatch): strings append their payload, boxed primitives format their
// VALUE, any other object prints its type's full name -- the same dispatch
// String.Format uses (see icall_string_format below; the AppendFormat
// overloads share this kernel).
//
// String.Format mapping (System.String statics, wired by MethodMap.String.cs
// "Format" -- the ps dispatch): 1-arg format, 2-arg object, 3/4-arg object
// forms, and the params object[] form:
//   Format(string)                   -> icall_string_format_0
//   Format(string, object)           -> icall_string_format_1
//   Format(string, object, object)   -> icall_string_format_2
//   Format(string, object, object, object) -> icall_string_format_3
//   Format(string, params object[])  -> icall_string_format
// Each returns the composite-format expansion of the args (net48 semantics,
// the AppendFormatCore kernel); a null format -> ArgumentNullException; a
// malformed placeholder or an index >= the arg count -> FormatException;
// a null params ARRAY -> ArgumentNullException (net48; the fixed-arity
// forms never see a null array).
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"

#include <cstdint>

namespace il2cpp {

// Managed System.Text.StringBuilder: object header + capacity + length + a
// managed UTF-16 char buffer (see header comment for the GC contract).
struct Il2CppStringBuilder
{
    Il2CppObject obj;      // object header (klass pointer)
    int32_t      capacity; // allocated UTF-16 units in 'chars'
    int32_t      length;   // used UTF-16 units (StringBuilder.Length)
    Il2CppArray* chars;    // UTF-16 buffer: managed array (GC reference, slot 2)
};

// The System.Text.StringBuilder runtime descriptor (registered lazily on
// first use). instanceSize = header + capacity + length + buffer pointer;
// refFieldBitmap has bit 2 set (the chars field is a GC reference).
Il2CppClass* il2cpp_stringbuilder_class();

namespace runtime {

// StringBuilder() -- parameterless ctor: capacity 16, length 0, a fresh
// 16-unit char buffer.
void icall_stringbuilder_ctor_default(Il2CppObject* self);

// StringBuilder(int capacity) -- capacity < 0 throws
// ArgumentOutOfRangeException; capacity == 0 falls back to the default
// capacity 16 (net48 baseline).
void icall_stringbuilder_ctor_capacity(Il2CppObject* self, int32_t capacity);

// StringBuilder(string value) -- the buffer is preallocated to
// max(DefaultCapacity, value.Length) (net48 baseline, empirically verified:
// a 20-unit value lands in a 20-unit buffer, NOT the Append growth path); a
// null value yields an empty builder.
void icall_stringbuilder_ctor_string(Il2CppObject* self, Il2CppObject* value);

// Append(string): appends the value's UTF-16 units; null is a no-op.
// Returns the SAME builder (chaining).
Il2CppObject* icall_stringbuilder_append_s(Il2CppObject* self, Il2CppObject* value);

// Append(char): appends one UTF-16 unit. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_c(Il2CppObject* self, char16_t value);

// Append(int): appends the invariant decimal representation of 'value'
// ("0", "-12345", "2147483647", "-2147483648"). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_i(Il2CppObject* self, int32_t value);

//------------------------------------------------------------------------------
// Append(long) / Append(double) / Append(bool) (batch 151): the invariant
// text via the shared to-string icalls (net48 semantics).
//------------------------------------------------------------------------------
Il2CppObject* icall_stringbuilder_append_l(Il2CppObject* self, int64_t value);
Il2CppObject* icall_stringbuilder_append_d(Il2CppObject* self, double value);
Il2CppObject* icall_stringbuilder_append_b(Il2CppObject* self, int32_t value);

// Append(object): net48 semantics -- null is a no-op; a string value appends
// its payload; a boxed Int32 appends its invariant decimal; any other managed
// object appends its ToString text (icall_object_to_string dispatch: boxed
// primitives format their value, other objects print their type's full name).
// Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_o(Il2CppObject* self, Il2CppObject* value);

// AppendLine(): appends the Windows newline "\r\n" (net48
// Environment.NewLine). Returns the SAME builder (chaining).
Il2CppObject* icall_stringbuilder_append_line(Il2CppObject* self);

// AppendLine(string): appends the value's units followed by the newline;
// a null value appends the newline only. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_line_s(Il2CppObject* self, Il2CppObject* value);

// AppendFormat(string format, object arg0): composite-format expansion
// ("{N}" placeholders, "{{"/"}}" escaping -- see the header semantics).
// Null format -> ArgumentNullException; malformed format or out-of-range
// index -> FormatException. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_format_1(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0);

// AppendFormat(string format, object arg0, object arg1) -- see _1.
Il2CppObject* icall_stringbuilder_append_format_2(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1);

// AppendFormat(string format, object arg0, object arg1, object arg2) -- see _1.
Il2CppObject* icall_stringbuilder_append_format_3(Il2CppObject* self, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2);

// String.Format(string format): composite-format expansion through a fresh
// temporary builder (net48 semantics, AppendFormatCore kernel). Null format
// -> ArgumentNullException; malformed format or out-of-range index ->
// FormatException. Returns the formatted string (a managed string).
Il2CppObject* icall_string_format_0(Il2CppObject* format);

// String.Format(string format, object arg0) -- see _0.
Il2CppObject* icall_string_format_1(Il2CppObject* format, Il2CppObject* arg0);

// String.Format(string format, object arg0, object arg1) -- see _0.
Il2CppObject* icall_string_format_2(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1);

// String.Format(string format, object arg0, object arg1, object arg2) -- see _0.
Il2CppObject* icall_string_format_3(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2);

// String.Format(string format, params object[] args) -- see _0. A null args
// ARRAY -> ArgumentNullException (net48); an empty array is legal.
Il2CppObject* icall_string_format(Il2CppObject* format, Il2CppObject* argsArray);

// Replace(string oldValue, string newValue): replaces EVERY occurrence of
// oldValue with newValue (null newValue removes the occurrences). Null
// oldValue -> ArgumentNullException; empty oldValue -> ArgumentException.
// Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_s_s(Il2CppObject* self, Il2CppObject* oldValue, Il2CppObject* newValue);

// Replace(char oldChar, char newChar): replaces every matching unit in
// place. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_c_c(Il2CppObject* self, char16_t oldChar, char16_t newChar);

// Insert(int index, string value): inserts the value's units before
// 'index'; index < 0 or index > Length throws
// ArgumentOutOfRangeException; null value is a no-op. Grows the buffer
// exactly when needed. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert(Il2CppObject* self, int32_t index, Il2CppObject* value);

// Insert(int index, int value) (batch 170): the invariant decimal
// representation of 'value' via the Append(int) kernel -- the text is
// inserted, not the raw int (net48 ToString-then-insert semantics).
// Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_i(Il2CppObject* self, int32_t index, int32_t value);

// Insert(int index, long value): the invariant decimal text via
// icall_int64_to_string (net48 Insert(long) semantics). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_l(Il2CppObject* self, int32_t index, int64_t value);

// Insert(int index, double value): the net48 "G15" text via
// icall_double_to_string. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_d(Il2CppObject* self, int32_t index, double value);

// Insert(int index, bool value): "True"/"False" via icall_bool_to_string.
// Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_b(Il2CppObject* self, int32_t index, int32_t value);

// Insert(int index, char value): inserts one UTF-16 unit. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_c(Il2CppObject* self, int32_t index, char16_t value);

// Insert(int index, string value, int count): inserts 'value' repeated
// 'count' times before 'index' (net48 semantics). count < 0 -> 
// ArgumentOutOfRangeException; index < 0 or index > Length throws
// ArgumentOutOfRangeException; a null/empty value or count == 0 is a no-op;
// the total repeated length (count * value.Length) must fit int32 or the
// call throws ArgumentOutOfRangeException. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_s_count(Il2CppObject* self, int32_t index, Il2CppObject* value, int32_t count);

// Remove(int startIndex, int length): removes 'length' units starting at
// 'startIndex' (the tail shifts left). Throws ArgumentOutOfRangeException
// when startIndex < 0, startIndex > Length, length < 0, or
// startIndex > Length - length (checked in 64-bit). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_remove(Il2CppObject* self, int32_t startIndex, int32_t length);

// Clear() (netcore21+ audit surface): Length drops to 0 (the buffer stays
// allocated). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_clear(Il2CppObject* self);

// ToString(): a managed string snapshot of the current content (a fresh
// copy -- later mutations never change an earlier result).
Il2CppObject* icall_stringbuilder_to_string(Il2CppObject* self);

// Length (getter): the current UTF-16 unit count.
int32_t icall_stringbuilder_get_length(Il2CppObject* self);

// get_Chars(index): the indexer -- reads one UTF-16 unit from the buffer;
// out-of-range -> IndexOutOfRangeException.
int32_t icall_stringbuilder_get_chars(Il2CppObject* self, int32_t index);

// P6: ReplaceBufferInternal/ReplaceBufferAnsiInternal — net48 internal helpers.
// ReplaceBufferInternal(char* buffer): replaces the entire buffer contents
// with the given char buffer. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_buffer_internal(Il2CppObject* self, const char16_t* buffer);
// ReplaceBufferAnsiInternal(sbyte* buffer): replaces the entire buffer contents
// with the given ANSI byte buffer (each byte → char). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_buffer_ansi_internal(Il2CppObject* self, const int8_t* buffer);

// P25f (dhprobe): System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::.ctor
// (the net6+ "$...{x}..." lowering). The handler is a VALUETYPE local the C#
// compiler address-takes (ldloca); we model the handler as a StringBuilder
// object stored in that slot. `slot` is the ADDRESS of the local (Il2CppObject**),
// a GC frame root -- this icall creates the builder and writes it into the slot.
// literalLength / formattedCount are compile-time capacity HINTS (ignored: the
// builder grows); they exist so the icall arity matches the IL exactly.
void icall_interp_handler_ctor(Il2CppObject** slot, int32_t literalLength, int32_t formattedCount);

} // namespace runtime

} // namespace il2cpp
