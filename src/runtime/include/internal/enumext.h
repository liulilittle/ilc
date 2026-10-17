#pragma once
//------------------------------------------------------------------------------
// enumext.h -- System.Enum runtime support (enum metadata registry,
// GetName / Parse / GetUnderlyingType / ToString).
//
// The transpiler's BCL mapping (System.Enum.GetName / Parse /
// GetUnderlyingType and the enum-value ToString path) resolves to these
// helpers. The runtime keeps NO member table inside Il2CppClass (see
// il2cpp_metadata.h -- the descriptor carries only layout + dispatch
// fields); enum metadata lives in a SEPARATE name-keyed registry owned by
// this module:
//
//   il2cpp_enum_register("Calc.Color", &info)   -- startup, before threads
//
// Metadata entries are CALLER-OWNED statics (exactly the Il2CppClass
// descriptor pattern): the registry stores pointers, never copies, and the
// info must outlive the registry (static storage). The registry is
// populated during single-threaded startup, so no synchronization is
// needed; registering an existing name REPLACES the entry (last write
// wins, matching the type registry's dedupe convention).
//
// TYPE-INFO MODEL (simplified -- no metadata in Il2CppClass):
//   Il2CppClass cannot express "this class is an enum", and unregistered
//   classes are indistinguishable from enum types. Every API therefore
//   follows the same fallback: when the class name is NOT in the registry
//   (or the class descriptor is null), the runtime assumes the .NET
//   default underlying type System.Int32 with NO members:
//     get_name           -> nullptr (no names to match)
//     parse (numeric)    -> int32 parse (the int assumption)
//     get_underlying_type-> "System.Int32"
//     to_string          -> numeric decimal string
//
// BUILT-IN BCL ENUM METADATA (interim-ABI note): the runtime ships metadata
// for a small set of BCL enum types (System.DayOfWeek today -- 7 members,
// Sunday = 0 .. Saturday = 6, matching icall_datetime_get_dayofweek in
// il2cpp_time.h). The info is a runtime-owned static registered at static
// init under the enum's full name (same registry, same replace-once
// semantics). This serves two paths:
//   * the name-keyed path, once a caller passes a "System.DayOfWeek" class
//     descriptor, and
//   * the NULL-descriptor interim path (see il2cpp_enum_to_string below),
//     which is what the transpiler's enum:l cast emits today.
//
// NULL-DESCRIPTOR to_string FALLBACK (documented deviation): the
// transpiler emits il2cpp_enum_to_string(nullptr, value) until
// typeof/ldtoken emission lands. A null descriptor cannot name the enum,
// so the runtime prints a built-in member NAME only when the value matches
// a built-in member AND no REGISTERED non-built-in enum owns that value
// (a user enum riding the same null-descriptor path with a coincident
// value formats numeric instead -- the identity is ambiguous, numeric is
// safe). The exact by-name dispatch arrives with the descriptor-bearing
// ABI. get_name / parse / get_underlying_type keep their documented
// null-input behavior (nullptr / 0 / "System.Int32") -- no built-in
// fallback on those paths.
//
// UNDERLYING-TYPE ENCODING: info.underlying carries the net48 TypeCode of
// the underlying primitive (consistent with icall_object_get_type_code in
// il2cpp_typecheck.h): 5=SByte 6=Byte 7=Int16 8=UInt16 9=Int32 10=UInt32
// 11=Int64 12=UInt64. 0 / any unknown code behaves as Int32 (whitelist-
// else). GetUnderlyingType returns the full type NAME as a managed string
// ("System.Int32" ...) -- the simplified Type model, a Type is its name
// (see icall_object_get_type).
//
// VALUE CARRIER: member values and API value parameters travel as int64_t
// (the generated code widens the enum's raw storage -- the common case is
// an int32 slot -- to int64 before the call). Values are compared by
// exact int64 equality, so widths never truncate at the boundary. The
// uint32/uint64 decimal round-trip is handled by the int64 formatter;
// uint64 bit patterns >= 2^63 print as their int64 interpretation
// (deviation from .NET, documented in enumext.cpp).
//
// EXCEPTION CONTRACT: parse follows the TryParse shape (no-throw, 1/0
// return + out value), matching the icall_try_parse_* family in
// il2cpp_parse.h. .NET Enum.Parse throws ArgumentException for unknown
// text; the transpiler owns that decision -- this primitive reports the
// outcome.
//
// [FLAGS] ToString DECOMPOSITION (rtflags, 2026-08-09): il2cpp_enum_to_string
// decomposes an unnamed value into its named bit components ("F1, F2" for
// value 3 of {None=0, F1=1, F2=2, F3=4}) when the enum is flags-capable.
// net48 semantics (csc probe): a NON-FLAGS enum NEVER decomposes -- an
// unnamed value prints its numeric decimal even when the value is a
// combination of member values (probe plain3=3 / plain7=7); only a [Flags]
// enum decomposes, and ONLY when the value is fully covered by named bits
// (partial coverage -- probe partial2=10 -> "10" -- stays numeric). The
// runtime has no [Flags] attribute bit in the registry (the transpiler's
// register emission carries no attribute info -- the member table is the
// only metadata), so flags-CAPABILITY is detected from the member table
// itself: an enum is treated as flags-capable when it has a zero-valued
// member AND every non-zero member value is an exact power of two -- the
// canonical [Flags] shape (the .NET [Flags] convention typically declares
// a zero member and single-bit members). DOCUMENTED DEVIATION: a plain
// (non-[Flags]) enum with exactly that member shape would also decompose
// (net48 requires the attribute); no current e2e/unit case has that shape.
//   Decomposition algorithm (net48 probe-verified, probe2/probe3):
//     1. exact member match first (a defined member name wins over
//        decomposition -- including a composite member: value 3 of
//        {None=0, F1=1, F2=2, AB=3, F4=8} -> "AB");
//     2. value 0: the zero member's name when defined, else "0";
//     3. otherwise greedily take the LARGEST member value that is a subset
//        of the remaining bits (descending by value), collecting names, and
//        OUTPUT the taken names in ASCENDING value order joined with ", "
//        (probe g2_all=31 -> "X1, X2, X4, X8, X16" -- value order, NOT
//        declaration order); if the remaining bits cannot be fully covered
//        by named members the numeric fallback applies (net48).
// Flags-style comma-joined NAMES in Parse are NOT supported (deviation).
//------------------------------------------------------------------------------

#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "il2cpp_array.h" // Il2CppArray (il2cpp_enum_get_names_by_type_name)

#include <cstdint>

namespace il2cpp {
namespace runtime {

// net48 TypeCode values of the supported enum underlying primitives
// (consistent with il2cpp_typecheck.h's icall_object_get_type_code).
enum : int32_t
{
    kEnumUnderlyingSByte  = 5,
    kEnumUnderlyingByte   = 6,
    kEnumUnderlyingInt16  = 7,
    kEnumUnderlyingUInt16 = 8,
    kEnumUnderlyingInt32  = 9,
    kEnumUnderlyingUInt32 = 10,
    kEnumUnderlyingInt64  = 11,
    kEnumUnderlyingUInt64 = 12,
};

// One enum type's metadata (caller-owned static, like Il2CppClass).
struct Il2CppEnumInfo
{
    int32_t             underlying; // TypeCode of the underlying primitive
    int32_t             valueCount; // number of members
    const char* const*  names;      // ASCII member names, one per member
    const int64_t*      values;     // member values (int64 carrier)
};

//------------------------------------------------------------------------------
// il2cpp_enum_register -- publish an enum type's metadata.
//
//   typeName: must match the Il2CppClass descriptor's name (identity by
//             name -- the same convention as icall_type_equals)
//   info:     the caller-owned metadata (static storage)
//
// Null typeName/info are ignored (defensive; generated code always passes
// real values). Re-registering an existing name replaces the entry.
// Startup-only by convention -- no synchronization (single-threaded
// static init; see the header note).
//------------------------------------------------------------------------------
void il2cpp_enum_register(const char* typeName, const Il2CppEnumInfo* info);

//------------------------------------------------------------------------------
// il2cpp_enum_is_registered -- is 'enumClass' a REGISTERED enum type?
//
//   Returns 1 when the class descriptor's name is in the enum metadata
//   registry (FindInfo hit), 0 otherwise (null class / unknown name -- the
//   documented Int32 no-metadata assumption). The boxed-value equality path
//   (icall_boxed_equals_ext2, boxingext2.cpp) uses this to route enum-klass
//   boxes to il2cpp_enum_equals instead of reference equality.
//------------------------------------------------------------------------------
bool il2cpp_enum_is_registered(const Il2CppClass* enumClass);

//------------------------------------------------------------------------------
// il2cpp_enum_is_registered_name -- NAME-variant of il2cpp_enum_is_registered
//   (the name-string ABI, same registry). refl6a Type.GetEnum* wrappers use it
//   to enforce the net48 non-enum-receiver contract (ArgumentException) before
//   delegating to the shared by_type_name query icalls -- the shared functions
//   keep their documented unknown-name fallback, only the Type-arm wrappers
//   throw.
//------------------------------------------------------------------------------
bool il2cpp_enum_is_registered_name(const char* typeName);

//------------------------------------------------------------------------------
// il2cpp_enum_get_name -- System.Enum.GetName(Type, value).
//
//   Returns the member NAME of 'value' as a managed string, or nullptr
//   when the value has no member name (.NET GetName returns null for
//   unnamed values). Unregistered class -> nullptr (no metadata).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_name(Il2CppClass* enumClass, int64_t value);

//------------------------------------------------------------------------------
// il2cpp_enum_parse -- System.Enum.Parse (TryParse shape).
//
//   text:  the managed string to parse (null -> 0). Accepted forms:
//             * a member NAME (case-sensitive -- the .NET default; ASCII
//               whitespace is trimmed from both ends before matching, .NET
//               trims Unicode whitespace -- ASCII-only is the documented
//               project-wide deviation, see il2cpp_parse.h)
//             * a numeric decimal text (leading/trailing ASCII whitespace,
//               optional '+'/'-' sign, decimal digits; no hex -- net48
//               Enum.Parse rejects "0x" text too), parsed in the underlying
//               type's WIDTH per the shared parse rules in il2cpp_parse.h
//               and RANGE-CHECKED against that width: SByte [-128,127],
//               Byte [0,255], Int16 [-32768,32767], UInt16 [0,65535],
//               Int32, UInt32 [0,4294967295], Int64, UInt64 [0, INT64_MAX]
//               (the uint64 [2^63, 2^64-1] slice is the int64-carrier
//               deviation -- see enumext.cpp). A leading '-' on an unsigned
//               width fails the range check (net48 OverflowException). A
//               numeric text succeeds even when no member has that value
//               (.NET Enum.Parse semantics: the value itself is the result).
//   result: out slot; written ONLY on success (TryParse contract).
//
//   Returns 1 + the value on success; 0 on failure with 'result'
//   untouched and NO throw (unknown text / out of range / null text).
//   Flags-style comma-joined names are NOT supported.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_parse(Il2CppClass* enumClass, Il2CppObject* text,
                          int64_t* result);

//------------------------------------------------------------------------------
// il2cpp_enum_parse_ic -- Enum.TryParse<T>(string, bool ignoreCase, out T)
// (batch 148e): the ignoreCase variant -- ASCII case-insensitive member-NAME
// matching; the numeric path is shared with il2cpp_enum_parse. Same
// TryParse contract (1/0; the out slot is written only on success).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_parse_ic(Il2CppClass* enumClass, Il2CppObject* text,
                             int32_t ignoreCase, int64_t* result);

//------------------------------------------------------------------------------
// il2cpp_enum_get_underlying_type -- System.Enum.GetUnderlyingType(Type).
//
//   Returns the underlying primitive's FULL TYPE NAME as a managed string
//   ("System.Int32", "System.Int64", ... -- the simplified Type model).
//   Unregistered (or null) class -> "System.Int32" (the int assumption).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_underlying_type(Il2CppClass* enumClass);

// raudit4: the NAME variant of GetUnderlyingType (the transpiler passes the
// folded typeof type-name string; same Int32 fallback for unregistered names).
Il2CppString* il2cpp_enum_get_underlying_type_by_type_name(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// il2cpp_enum_to_string -- enum ToString optimization.
//
//   Returns the member NAME when 'value' matches a member (the .NET
//   behavior for named values), otherwise the numeric decimal string in
//   the underlying type's width (the .NET behavior for unnamed values --
//   and the complete behavior for unregistered classes, which format as
//   int32 per the int assumption).
//
//   [FLAGS] DECOMPOSITION (rtflags, 2026-08-09): for a FLAGS-CAPABLE
//   registered enum (see the header note -- zero member + power-of-two
//   members; the transpiler emits no attribute bit today) an unnamed value
//   is decomposed into its named bit components joined with ", " -- value 3
//   of {None=0, F1=1, F2=2, F3=4} -> "F1, F2" (net48 ts_flags). The
//   decomposition follows the net48 algorithm (probe-verified):
//     * exact member match wins (a defined name beats decomposition --
//       including a composite member: value 3 of {.., AB=3, ..} -> "AB");
//     * value 0 -> the zero member's name when defined, else "0";
//     * otherwise GREEDILY take the largest member value that is a subset
//       of the remaining bits (descending by value) and OUTPUT the taken
//       names in ASCENDING value order (net48 orders by value, not
//       declaration order); when the remaining bits cannot be fully
//       covered by named members, the numeric fallback applies (net48:
//       partial coverage stays numeric -- probe partial2=10 -> "10").
//   Non-flags-capable enums (and unregistered classes) never decompose --
//   an unnamed value prints its numeric decimal (net48: only [Flags]
//   enums decompose).
//
//   NULL-DESCRIPTOR INTERIM PATH: a null class descriptor (the transpiler's
//   enum:l cast until typeof/ldtoken emission lands) additionally consults
//   the BUILT-IN BCL enum metadata -- a built-in member name is returned
//   when the value matches AND no registered non-built-in enum owns that
//   value (see the TYPE-INFO MODEL note above); otherwise the numeric
//   fallback applies as documented.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_to_string(Il2CppClass* enumClass, int64_t value);

//------------------------------------------------------------------------------
// il2cpp_enum_to_string_format -- Enum.ToString(string format) (batch 148):
// the boxed enum's value formatted per the net48 format specifier
// (case-insensitive): "G" = member name (or numeric fallback), "D" = decimal
// numeric, "F" = [Flags] name decomposition ("A, B"), "X" = uppercase
// hexadecimal padded to the underlying width. Null format ->
// ArgumentNullException; an unknown specifier -> FormatException (net48).
// The (klass, value, format) shape matches the 0-arg ToString ABI with the
// format string appended (the transpiler's unbox:l receiver shape).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_to_string_format(Il2CppClass* enumClass,
                                           int64_t value,
                                           Il2CppObject* format);

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_to_string_format -- Enum.ToString(string format) for a
// BOXED enum value (batch 148): the klass identity rides on the box itself,
// so the transpiler's box path needs no PrependTypeClass. Format semantics
// are shared with il2cpp_enum_to_string_format (null/empty -> "G", net48).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_boxed_to_string_format(Il2CppObject* boxed,
                                                 Il2CppObject* format);

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_payload -- the boxed enum's width-aware int64 payload
// (int64 for Int64/UInt64-backed enums, int32 otherwise; audit H3 unbox
// closure -- icall_unbox_long accepts enum boxes). Null boxed ->
// NullReferenceException.
//------------------------------------------------------------------------------
int64_t il2cpp_enum_boxed_payload(Il2CppObject* boxed);

//------------------------------------------------------------------------------
// il2cpp_enum_is_defined -- Enum.IsDefined(Type, object) (batch 148): 1 when
// the value is a member of the enum -- a managed STRING value matches member
// NAMES (case-sensitive, ASCII-trimmed, net48) and any boxed value (enum box
// or primitive box) matches the numeric member values. Null type/value ->
// ArgumentNullException; an unregistered type name -> 0 (defensive).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_is_defined(Il2CppObject* typeName, Il2CppObject* value);

//------------------------------------------------------------------------------
// il2cpp_enum_format -- Enum.Format(Type, object, string) (batch 148): the
// static form of ToString(format) -- resolve the value (string name or boxed
// payload) to its numeric member value, then format per
// il2cpp_enum_to_string_format (D/G/F/X, case-insensitive, null/empty -> G).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_format(Il2CppObject* typeName, Il2CppObject* value,
                                 Il2CppObject* format);

//------------------------------------------------------------------------------
// il2cpp_enum_get_values_by_type_name -- Enum.GetValues(Type) (batch 148):
// an int32[] of the member VALUES in declaration order (the Int32
// no-metadata assumption for the underlying width; net48 returns the
// underlying-typed array). Unknown type name -> an EMPTY int32[].
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_enum_get_values_by_type_name(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// il2cpp_enum_to_object -- Enum.ToObject(Type, object) (batch 148): the boxed
// enum with the enum's own class descriptor (il2cpp_class_by_name + box).
// The value arrives as its int64 carrier (the transpiler's object-argument
// constant fold passes the widened value).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_to_object(Il2CppObject* typeName, int64_t value);

//------------------------------------------------------------------------------
// il2cpp_enum_compare_to -- Enum.CompareTo(object) (batch 148): the width-
// aware payload comparison (-1/0/1). The constrained receiver arrives as its
// int64 carrier VALUE ("enumval" shape -- the enum VALUE ldloca shape), and
// the argument is a boxed enum (or a boxed underlying primitive). Null other
// -> 1 (net48 value-type CompareTo); a different-type box -> ArgumentException.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_compare_to(int64_t selfValue, Il2CppObject* other);

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_to_string -- ToString for a BOXED enum value.
//
//   boxed: an Il2CppObject* whose klass is the enum's OWN class descriptor
//          (the icall_box_enum box -- boxingext2.cpp; the enum identity is
//          carried by the klass, NOT the Int64/Int32 carrier, so the
//          klass-name-keyed registry lookup resolves the metadata).
//
//   Reads the width-aware payload at byte offset 8 (the shared box slot:
//   int64 for Int64/UInt64-backed enums, int32 otherwise) and delegates to
//   il2cpp_enum_to_string(klass, value): the member NAME when the value is
//   named, else the numeric decimal string (the documented fallback).
//   Null boxed -> NullReferenceException. This is the runtime primitive the
//   boxed-enum ToString dispatch should call (see the report).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_boxed_to_string(Il2CppObject* boxed);

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_get_hash_code -- GetHashCode for a BOXED enum value.
//
//   net48 semantics: Enum.GetHashCode() is the UNDERLYING value's hash --
//   an Int32-width enum hashes to the value itself; an Int64/UInt64-backed
//   enum hashes to low-32 XOR high-32 (Int64.GetHashCode). Unregistered
//   classes keep the Int32 assumption. Null boxed -> NullReferenceException.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_boxed_get_hash_code(Il2CppObject* boxed);

//------------------------------------------------------------------------------
// il2cpp_enum_equals -- System.Enum.Equals(object) for a boxed enum.
//
//   self:  the boxed enum receiver (an Il2CppObject* whose klass is the enum
//          type; the box payload holds the enum value)
//   other: the object to compare against
//
//   Returns 1 when the two boxes are the SAME ENUM TYPE and hold the SAME
//   value; 0 otherwise (net48 Enum.Equals semantics).
//
//   Rules:
//     null self              -> NullReferenceException (instance semantics)
//     null other             -> 0
//     different klass NAMES  -> 0 (different enum types are never equal; a
//                               non-enum box -- e.g. System.Int32 -- is a
//                               different type by the same name comparison)
//     same klass name        -> compare the box payloads for equality. The
//                               payload read is WIDTH-AWARE: an int64/
//                               uint64-backed enum compares the FULL 8-byte
//                               payload slot at byte offset 8 (the
//                               il2cpp_boxing_ext2.h long/ulong layout) --
//                               otherwise two values differing only in
//                               their high 32 bits would compare equal;
//                               every other width compares the int32 slot
//                               (the il2cpp_box_int32 layout). Unregistered
//                               classes keep the Int32 assumption.
//                               -> 1/0
//------------------------------------------------------------------------------
int32_t il2cpp_enum_equals(Il2CppObject* self, Il2CppObject* other);

//------------------------------------------------------------------------------
// NAME-STRING ABI (enumface, D-STREN-2, 2026-08-12).
//
// The transpiler's typeof(T) fold produces the enum's full NAME as a managed
// string (StackFold GetTypeFromHandle fold -- "Mode" for a nested user enum,
// "System.DayOfWeek" for a BCL TypeRef). The enum metadata registry is keyed
// by exactly those names (il2cpp_enum_register keys), so the primitives below
// resolve the metadata BY NAME STRING instead of by class descriptor -- no
// ldtoken/typeof descriptor emission needed. They are the mapping targets for
// Enum.GetName(Type, object) / Enum.GetNames(Type) / Enum.Parse(Type, string)
// / Enum.HasFlag(Enum) -- the D-STREN-2 LOUD-gap closure.
//
//   typeName: a managed string holding the enum's full name (the folded
//             typeof value); unknown / null -> the documented Int32
//             no-metadata assumption for the query primitives, and for
//             Parse a failure (ArgumentException).
//------------------------------------------------------------------------------

//------------------------------------------------------------------------------
// il2cpp_enum_get_name_by_type_name -- Enum.GetName(Type, object), name-string
//   variant of il2cpp_enum_get_name. 'valueBox' is the BOXED value (an
//   Il2CppObject* -- the boxed-enum forward ABI; the runtime reads the
//   width-aware payload via BoxedEnumPayload, so enum-klass boxes work
//   without the Int64-box klass check). Returns the member NAME for the boxed
//   value, or nullptr for an unnamed value / unknown type name / null box
//   (net48 GetName null contract).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_name_by_type_name(Il2CppObject* typeName,
                                                Il2CppObject* valueBox);

//------------------------------------------------------------------------------
// il2cpp_enum_get_names_by_type_name -- Enum.GetNames(Type). Returns a string[]
//   holding the member names in DECLARATION order (net48: GetNames preserves
//   the metadata declaration order, not value order). Unknown type name ->
//   an empty string[] (the no-metadata assumption).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_enum_get_names_by_type_name(Il2CppObject* typeName);

//------------------------------------------------------------------------------
// il2cpp_enum_parse_boxed -- Enum.Parse(Type, string). Parses the same
//   grammar as il2cpp_enum_parse (member NAME -- case-sensitive, ASCII
//   whitespace trimmed -- then numeric decimal in the underlying width,
//   range-checked), and on SUCCESS returns the value as a BOXED ENUM with the
//   enum's OWN class descriptor (il2cpp_class_by_name + icall_box_enum), so
//   boxed ToString/Equals/HasFlag dispatch on the enum identity. On FAILURE
//   it THROWS ArgumentException (net48 Enum.Parse throws on unknown text;
//   the OverflowException split for out-of-range numeric is a documented
//   deviation -- this minimal face reports every failure as ArgumentException).
//   Null text -> ArgumentNullException (net48).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_parse_boxed(Il2CppObject* typeName,
                                      Il2CppObject* text);

//------------------------------------------------------------------------------
// il2cpp_enum_parse_boxed_ic -- Enum.Parse(Type, string, bool ignoreCase).
// Same grammar and boxed result as il2cpp_enum_parse_boxed; when ignoreCase
// is nonzero the member-NAME match is ASCII case-insensitive (net48
// Enum.Parse(Type, text, true) semantics; the numeric path is unaffected,
// and unknown text still throws ArgumentException). Batch 148 wiring gap
// closure (MethodMap.Enum.cs Parse 3-arg overload).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_parse_boxed_ic(Il2CppObject* typeName,
                                         Il2CppObject* text,
                                         int32_t ignoreCase);

//------------------------------------------------------------------------------
// il2cpp_enum_has_flag -- Enum.HasFlag(Enum), the flags bit test for a BOXED
//   enum receiver and argument. Returns (self & flag) == flag (net48) with a
//   width-aware payload read (int64 for Int64/UInt64-backed enums -- the same
//   decision as il2cpp_enum_equals / BoxedEnumPayload). Errors (net48):
//   null self -> NullReferenceException; null flag -> ArgumentNullException;
//   a klass-NAME mismatch (different enum types / non-enum boxes) ->
//   ArgumentException.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_has_flag(Il2CppObject* self, Il2CppObject* flag);

} // namespace runtime
} // namespace il2cpp
