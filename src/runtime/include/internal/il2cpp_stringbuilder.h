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
//
// GLOBAL scope (the adm1 p-adb contract, same as System_AppDomain_class):
// generated isinst/castclass on a StringBuilder-typed receiver emits
// &System_Text_StringBuilder_class, so the descriptor is a global symbol
// defined in stringbuilder.cpp (il2cpp_stringbuilder_class() returns it).
extern il2cpp::Il2CppClass System_Text_StringBuilder_class;
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

// StringBuilder(int capacity, int maxCapacity) -- SB-REST (nb9). net48
// validation: capacity < 0 -> ArgumentOutOfRangeException("capacity");
// maxCapacity < 1 -> ArgumentOutOfRangeException("maxCapacity");
// capacity > maxCapacity -> ArgumentOutOfRangeException("capacity");
// capacity == 0 -> min(DefaultCapacity, maxCapacity). The initial buffer is
// exactly 'capacity' units (capacity 5 with max 10 stays 5, unlike the
// one-argument ctor's default bump).
void icall_stringbuilder_ctor_capacity_max(Il2CppObject* self, int32_t capacity, int32_t maxCapacity);

// Append(string): appends the value's UTF-16 units; null is a no-op.
// Returns the SAME builder (chaining).
Il2CppObject* icall_stringbuilder_append_s(Il2CppObject* self, Il2CppObject* value);

// Append(string value, int startIndex, int count): appends the substring
// [startIndex, startIndex+count) of 'value' (net48 semantics). A null value
// is a no-op AFTER the index/count validation (net48 validates first). Throws
// ArgumentOutOfRangeException for startIndex < 0, count < 0, or
// startIndex+count beyond value.Length; ArgumentException when the range is
// invalid with a non-null value in the net48-specific order. Returns the SAME
// builder.
Il2CppObject* icall_stringbuilder_append_s_range(Il2CppObject* self, Il2CppObject* value, int32_t startIndex, int32_t count);

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

// String.Format(IFormatProvider, string, ...) provider-prefixed wrappers
// (D5 envnf): the provider is ignored (invariant culture contract).
Il2CppObject* icall_string_format_0_provider(Il2CppObject* provider, Il2CppObject* format);

// String.Format(string format, object arg0) -- see _0.
Il2CppObject* icall_string_format_1(Il2CppObject* format, Il2CppObject* arg0);
Il2CppObject* icall_string_format_1_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0);

// String.Format(string format, object arg0, object arg1) -- see _0.
Il2CppObject* icall_string_format_2(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1);
Il2CppObject* icall_string_format_2_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1);

// String.Format(string format, object arg0, object arg1, object arg2) -- see _0.
Il2CppObject* icall_string_format_3(Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2);
Il2CppObject* icall_string_format_3_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* arg0, Il2CppObject* arg1, Il2CppObject* arg2);

// String.Format(string format, params object[] args) -- see _0. A null args
// ARRAY -> ArgumentNullException (net48); an empty array is legal.
Il2CppObject* icall_string_format(Il2CppObject* format, Il2CppObject* argsArray);
// String.Format(IFormatProvider, string, params object[]) -- provider ignored.
Il2CppObject* icall_string_format_provider(Il2CppObject* provider, Il2CppObject* format, Il2CppObject* argsArray);

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

//------------------------------------------------------------------------------
// SB-REST (nb9): the range / buffer-array surface.
//------------------------------------------------------------------------------
// Append(string value, int startIndex, int count): appends the window
// [startIndex, startIndex + count) of value. net48 contract: a null value is
// a no-op only when (startIndex, count) is a valid empty window; otherwise
// ArgumentNullException. startIndex < 0 / count < 0 / count >
// value.Length - startIndex -> ArgumentOutOfRangeException. Returns the SAME
// builder.
Il2CppObject* icall_stringbuilder_append_s_range(Il2CppObject* self, Il2CppObject* value,
                                                int32_t startIndex, int32_t count);

// Append(char[] value, int startIndex, int count): same contract as
// append_s_range over the managed char[] payload (a null array is a no-op
// only for the empty window). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_append_c_array_range(Il2CppObject* self, Il2CppArray* value,
                                                       int32_t startIndex, int32_t count);

// Insert(int index, char[] value, int startIndex, int count): inserts the
// window of value before 'index'. net48 contract: index < 0 or index >
// Length -> ArgumentOutOfRangeException; a null array is a no-op only for the
// empty window (otherwise ArgumentNullException); startIndex < 0 / count < 0
// / count > value.Length - startIndex -> ArgumentOutOfRangeException.
// Returns the SAME builder.
Il2CppObject* icall_stringbuilder_insert_c_array_range(Il2CppObject* self, int32_t index,
                                                       Il2CppArray* value,
                                                       int32_t startIndex, int32_t count);

// CopyTo(int sourceIndex, char[] destination, int destinationIndex, int
// count): copies the builder window into the destination array (net48
// validation: null destination -> ArgumentNullException; negative indices /
// count -> ArgumentOutOfRangeException; sourceIndex > Length - count ->
// ArgumentOutOfRangeException; destinationIndex > destination.Length - count
// -> ArgumentException). The destination is a managed char[] (elemSize 2).
void icall_stringbuilder_copy_to(Il2CppObject* self, int32_t sourceIndex,
                                 Il2CppArray* destination, int32_t destinationIndex,
                                 int32_t count);

// EnsureCapacity(int capacity): returns the resulting Capacity. capacity < 0
// -> ArgumentOutOfRangeException; a capacity already covered returns the
// current Capacity; growth follows the net48 Grow policy
// (max(required, max(Length, oldCapacity) * 2)).
int32_t icall_stringbuilder_ensure_capacity(Il2CppObject* self, int32_t capacity);

// Equals(StringBuilder sb): net48 StringBuilder.Equals is REFERENCE
// equality (the object identity contract; a null other is false).
bool icall_stringbuilder_equals(Il2CppObject* self, Il2CppObject* other);

// Replace(string oldValue, string newValue, int startIndex, int count):
// replaces every non-overlapping occurrence fully inside the window
// [startIndex, startIndex + count). Same null/empty oldValue contract as
// Replace(string,string); startIndex < 0 / count < 0 / count > Length -
// startIndex -> ArgumentOutOfRangeException. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_s_s_range(Il2CppObject* self, Il2CppObject* oldValue,
                                                    Il2CppObject* newValue,
                                                    int32_t startIndex, int32_t count);

// Replace(char oldChar, char newChar, int startIndex, int count): replaces
// every matching unit inside the window. Same bounds contract as the string
// range form. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_c_c_range(Il2CppObject* self, char16_t oldChar,
                                                    char16_t newChar,
                                                    int32_t startIndex, int32_t count);

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

// ToString(int startIndex, int length): a FRESH string of the window
// [startIndex, startIndex + length). net48 AOORE contract: startIndex < 0 or
// length < 0 or startIndex + length > Length -> ArgumentOutOfRangeException.
Il2CppObject* icall_stringbuilder_to_string_range(Il2CppObject* self, int32_t startIndex, int32_t length);

// set_Length(int32 value): the net48 Length setter -- value < 0 ->
// ArgumentOutOfRangeException; value > Length grows the buffer (new units
// ZERO-FILLED), value < Length truncates (the buffer stays allocated).
void icall_stringbuilder_set_length(Il2CppObject* self, int32_t value);

// Length (getter): the current UTF-16 unit count.
int32_t icall_stringbuilder_get_length(Il2CppObject* self);
int32_t icall_stringbuilder_get_max_capacity(Il2CppObject* self);

// get_Chars(index): the indexer -- reads one UTF-16 unit from the buffer;
// out-of-range -> IndexOutOfRangeException.
int32_t icall_stringbuilder_get_chars(Il2CppObject* self, int32_t index);

// set_Chars(index, utf16): the indexer WRITER (P26 bcldeep -- the net8
// Chars[index] setter is pure-managed in CoreLib, so the transpiler needs
// the icall); same bounds contract as get_Chars.
void icall_stringbuilder_set_chars(Il2CppObject* self, int32_t index, int32_t utf16);

// P6: ReplaceBufferInternal/ReplaceBufferAnsiInternal -- net48 internal helpers.
// ReplaceBufferInternal(char* buffer): replaces the entire buffer contents
// with the given char buffer. Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_buffer_internal(Il2CppObject* self, const char16_t* buffer);
// ReplaceBufferAnsiInternal(sbyte* buffer): replaces the entire buffer contents
// with the given ANSI byte buffer (each byte -> char). Returns the SAME builder.
Il2CppObject* icall_stringbuilder_replace_buffer_ansi_internal(Il2CppObject* self, const int8_t* buffer);

// P25f (dhprobe): System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::.ctor
// (the net6+ "$...{x}..." lowering). The handler is a VALUETYPE local the C#
// compiler address-takes (ldloca); we model the handler as a StringBuilder
// object stored in that slot. `slot` is the ADDRESS of the local (Il2CppObject**),
// a GC frame root -- this icall creates the builder and writes it into the slot.
// literalLength / formattedCount are compile-time capacity HINTS (ignored: the
// builder grows); they exist so the icall arity matches the IL exactly.
void icall_interp_handler_ctor(Il2CppObject** slot, int32_t literalLength, int32_t formattedCount);

// P25g (dhprobe): AppendFormatted<T> ALIGNMENT + FORMAT-SPEC overloads -- the
// '{x,5}' field-width and '{x:D3}' format-string composite components. One
// {T} x {w,f,wf} trio per value type: 'w' = (T, int32 width), 'f' =
// (T, string format), 'wf' = (T, int32 width, string format). Each boxes the
// value and delegates to icall_format_composite_item (il2cpp_format_ext.h --
// the net48 alignment/min-width + numeric format-string engine), then appends
// the result. NULL format = default text; width 0/-/positive per net48.
// Returns the SAME builder (the net48 chain contract -- the transpiler's
// handler mapping discards the return).
Il2CppObject* icall_stringbuilder_append_i_w(Il2CppObject* self, int32_t value, int32_t width);
Il2CppObject* icall_stringbuilder_append_i_f(Il2CppObject* self, int32_t value, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_i_wf(Il2CppObject* self, int32_t value, int32_t width, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_l_w(Il2CppObject* self, int64_t value, int32_t width);
Il2CppObject* icall_stringbuilder_append_l_f(Il2CppObject* self, int64_t value, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_l_wf(Il2CppObject* self, int64_t value, int32_t width, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_d_w(Il2CppObject* self, double value, int32_t width);
Il2CppObject* icall_stringbuilder_append_d_f(Il2CppObject* self, double value, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_d_wf(Il2CppObject* self, double value, int32_t width, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_b_w(Il2CppObject* self, bool value, int32_t width);
Il2CppObject* icall_stringbuilder_append_b_f(Il2CppObject* self, bool value, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_b_wf(Il2CppObject* self, bool value, int32_t width, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_c_w(Il2CppObject* self, int32_t utf16, int32_t width);
Il2CppObject* icall_stringbuilder_append_c_f(Il2CppObject* self, int32_t utf16, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_c_wf(Il2CppObject* self, int32_t utf16, int32_t width, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_s_w(Il2CppObject* self, Il2CppObject* value, int32_t width);
Il2CppObject* icall_stringbuilder_append_s_f(Il2CppObject* self, Il2CppObject* value, Il2CppObject* format);
Il2CppObject* icall_stringbuilder_append_s_wf(Il2CppObject* self, Il2CppObject* value, int32_t width, Il2CppObject* format);

} // namespace runtime

} // namespace il2cpp
