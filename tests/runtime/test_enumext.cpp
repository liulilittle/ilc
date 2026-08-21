//------------------------------------------------------------------------------
// test_enumext.cpp -- System.Enum runtime support unit tests.
//
// Coverage (normal + edge + error paths; see enumext.h for the metadata
// model -- name-keyed registry, TypeCode underlying encoding, int64 value
// carrier, Int32 no-metadata assumption):
//   1. register + GetName -- each member's name; unnamed value -> nullptr.
//   2. register replace-once -- re-registering a name replaces the entry.
//   3. ToString -- named value -> the name; unnamed -> numeric decimal.
//   4. Parse numeric -- "2" -> 2; whitespace-padded " 1 " -> 1; negative
//      "-1" -> -1 (int32 width); unnamed-but-parseable "99" -> 99 (.NET
//      Enum.Parse semantics: the number itself is the result).
//   5. Parse name -- exact case-sensitive match; wrong case / unknown
//      name -> 0; failure leaves the out slot untouched (TryParse).
//   6. Parse error paths -- non-numeric text -> 0; null text -> 0; null
//      out slot -> 0; int32 overflow text "2147483648" -> 0.
//   7. GetUnderlyingType -- "System.Int32" / "System.Int64"; unknown
//      underlying code falls back to Int32 (whitelist-else).
//   8. int64-backed enum -- big-value name lookup, parse, ToString; the
//      numeric fallback prints through the int64 formatter.
//   9. uint32-backed enum -- values above INT32_MAX round-trip as decimal
//      text via the int64 formatter ("4000000000").
//  10. Unregistered class (no metadata) -- the Int32 assumption: GetName
//      -> nullptr, ToString -> numeric, parse numeric works, underlying
//      -> "System.Int32".
//  11. Built-in DayOfWeek metadata -- registered name path + the
//      null-descriptor fallback (register -> "Monday").
//  12. Parse name trimming -- ASCII whitespace (spaces/tabs) is stripped
//      from both ends before the case-sensitive match (net48), including
//      the case that trimming does NOT relax case sensitivity.
//  13. Parse numeric per-width range checks (net48): byte "256"/"-1" ->
//      0, sbyte "-129"/"128" -> 0, int16 "32768" -> 0, uint16 "65536"/"-1"
//      -> 0, uint32 "4294967296"/"5000000000"/"-1" -> 0, int64 boundaries.
//  14. Duplicate member values -- GetName/ToString return the FIRST
//      metadata name; Parse still accepts every name.
//  15. Equals width-aware payload compare -- int64 values differing only in
//      the high 32 bits are NOT equal; same-value int64 and int32 enums,
//      cross-type and null-other cases.
//  17. [Flags] ToString decomposition (rtflags) -- a flags-capable enum
//      (zero member + power-of-two members) decomposes an unnamed value into
//      its named bit components joined with ", " (net48): combined values
//      (3=F1|F2 -> "F1, F2"), all-bits-covered (7 -> "F1, F2, F3"), single
//      member -> name, zero -> the zero member's name, partial coverage /
//      negative -> numeric fallback, and the boxed path inherits it.
//  18. Flags output ORDER -- decomposed names print in ASCENDING VALUE
//      order, not declaration order (net48 probe g2_all=31).
//  19. NON-flags enums never decompose -- a composite member (3) disqualifies
//      the flags shape, so A|B prints numeric "3" (net48 plain3=3);
//      DayOfWeek's non-power-of-two values keep the numeric fallback.
//
// Plain executable + minimal assertion harness (see test_typecheck.cpp).
//------------------------------------------------------------------------------

#include "enumext.h"

#include "il2cpp_metadata.h"
#include "il2cpp_runtime.h" // il2cpp_class_register (parse_boxed klass resolution)
#include "il2cpp_boxing_ext2.h" // icall_box_enum / icall_boxed_equals_ext2
                                // (boxed-enum klass fix, D-ENM-3/D-ENM-5)
#include "il2cpp_exception.h" // DotNetException (name-string ABI error paths)

#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

// Compare a managed string with an ASCII literal (enum names and type
// names are ASCII by construction).
bool ManagedStringEqualsAscii(il2cpp::Il2CppString* str, const char* ascii)
{
    const size_t len = std::strlen(ascii);
    if (str == nullptr || static_cast<size_t>(str->length) != len)
    {
        return false;
    }
    for (size_t i = 0; i < len; ++i)
    {
        if (str->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

//------------------------------------------------------------------------------
// Test fixtures.
//
//   Test.Color   -- int32-backed enum (Red=0, Green=1, Blue=2)
//   Test.Big     -- int64-backed enum (Small=1, Huge=2^53+1)
// Test.URange  -- uint32-backed enum (Low=0, High=4000000000)
//   Test.ByteE  -- byte-backed enum (A=1, B=255)
//   Test.SbyteE -- sbyte-backed enum (Min=-128, Max=127)
//   Test.S16    -- int16-backed enum (Neg=-32768, Max=32767)
//   Test.U16    -- uint16-backed enum (Max=65535)
//   Test.U32Max -- uint32-backed enum with a member at the uint32 top edge
//                  (Max=4294967295)
//   Test.Dup    -- int32-backed enum with duplicate values (A=1, B=2, C=2):
//                  exercises the first-match-wins GetName/ToString rule
//   Test.Wide   -- int64-backed enum whose members differ in the HIGH 32
//                  bits (Lo=1, Hi=0x100000001): exercises the width-aware
//                  Equals payload compare (an int32-slot compare would see
//                  Lo == Hi)
//   Test.Bare    -- an enum-like class NEVER registered: exercises the
//                   Int32 no-metadata assumption
//   Test.Temp    -- registered twice with different metadata: exercises
//                   the replace-once semantics
//   Test.Flags   -- flags-capable enum (rtflags): {None=0, F1=1, F2=2,
//                   F3=4} -- a zero member + power-of-two members, the
//                   canonical [Flags] shape detected by IsFlagsCapable.
//                   Exercises the [Flags] ToString decomposition.
//   Test.Order   -- flags-capable enum whose declaration order is SCRAMBLED
//                   relative to value order ({H=16, A=1, D=8, B=2, C=4}):
//                   exercises the net48 rule that decomposed names print in
//                   ASCENDING VALUE order, not declaration order (probe
//                   g2_all=31 -> "X1, X2, X4, X8, X16").
//------------------------------------------------------------------------------

il2cpp::Il2CppClass colorClass = {
    "Test.Color", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* colorNames[] = {"Red", "Green", "Blue"};
const int64_t colorValues[] = {0, 1, 2};
il2cpp::runtime::Il2CppEnumInfo colorInfo = {
    il2cpp::runtime::kEnumUnderlyingInt32, 3, colorNames, colorValues,
};

il2cpp::Il2CppClass bigClass = {
    "Test.Big", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* bigNames[] = {"Small", "Huge"};
const int64_t bigValues[] = {1, 9007199254740993LL}; // 2^53 + 1
il2cpp::runtime::Il2CppEnumInfo bigInfo = {
    il2cpp::runtime::kEnumUnderlyingInt64, 2, bigNames, bigValues,
};

il2cpp::Il2CppClass uRangeClass = {
    "Test.URange", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* uRangeNames[] = {"Low", "High"};
const int64_t uRangeValues[] = {0, 4000000000LL}; // fits the int64 carrier
il2cpp::runtime::Il2CppEnumInfo uRangeInfo = {
    il2cpp::runtime::kEnumUnderlyingUInt32, 2, uRangeNames, uRangeValues,
};

il2cpp::Il2CppClass byteEClass = {
    "Test.ByteE", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* byteENames[] = {"A", "B"};
const int64_t byteEValues[] = {1, 255};
il2cpp::runtime::Il2CppEnumInfo byteEInfo = {
    il2cpp::runtime::kEnumUnderlyingByte, 2, byteENames, byteEValues,
};

il2cpp::Il2CppClass sbyteEClass = {
    "Test.SbyteE", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* sbyteENames[] = {"Min", "Max"};
const int64_t sbyteEValues[] = {-128, 127};
il2cpp::runtime::Il2CppEnumInfo sbyteEInfo = {
    il2cpp::runtime::kEnumUnderlyingSByte, 2, sbyteENames, sbyteEValues,
};

il2cpp::Il2CppClass s16Class = {
    "Test.S16", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* s16Names[] = {"Neg", "Max"};
const int64_t s16Values[] = {-32768, 32767};
il2cpp::runtime::Il2CppEnumInfo s16Info = {
    il2cpp::runtime::kEnumUnderlyingInt16, 2, s16Names, s16Values,
};

il2cpp::Il2CppClass u16Class = {
    "Test.U16", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* u16Names[] = {"Max"};
const int64_t u16Values[] = {65535};
il2cpp::runtime::Il2CppEnumInfo u16Info = {
    il2cpp::runtime::kEnumUnderlyingUInt16, 1, u16Names, u16Values,
};

il2cpp::Il2CppClass u32MaxClass = {
    "Test.U32Max", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* u32MaxNames[] = {"Max"};
const int64_t u32MaxValues[] = {4294967295LL};
il2cpp::runtime::Il2CppEnumInfo u32MaxInfo = {
    il2cpp::runtime::kEnumUnderlyingUInt32, 1, u32MaxNames, u32MaxValues,
};

il2cpp::Il2CppClass dupClass = {
    "Test.Dup", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* dupNames[] = {"A", "B", "C"};
const int64_t dupValues[] = {1, 2, 2};
il2cpp::runtime::Il2CppEnumInfo dupInfo = {
    il2cpp::runtime::kEnumUnderlyingInt32, 3, dupNames, dupValues,
};

il2cpp::Il2CppClass wideClass = {
    "Test.Wide", 8, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* wideNames[] = {"Lo", "Hi"};
const int64_t wideValues[] = {1, 0x100000001LL};
il2cpp::runtime::Il2CppEnumInfo wideInfo = {
    il2cpp::runtime::kEnumUnderlyingInt64, 2, wideNames, wideValues,
};

il2cpp::Il2CppClass bareClass = {
    "Test.Bare", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

il2cpp::Il2CppClass tempClass = {
    "Test.Temp", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* tempNamesA[] = {"First"};
const int64_t tempValuesA[] = {10};
il2cpp::runtime::Il2CppEnumInfo tempInfoA = {
    il2cpp::runtime::kEnumUnderlyingInt32, 1, tempNamesA, tempValuesA,
};
const char* tempNamesB[] = {"Second"};
const int64_t tempValuesB[] = {20};
il2cpp::runtime::Il2CppEnumInfo tempInfoB = {
    il2cpp::runtime::kEnumUnderlyingInt32, 1, tempNamesB, tempValuesB,
};

// Test.Flags -- the [Flags] decomposition fixture (rtflags): the canonical
// [Flags] member shape (zero member + power-of-two members).
il2cpp::Il2CppClass flagsClass = {
    "Test.Flags", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* flagsNames[] = {"None", "F1", "F2", "F3"};
const int64_t flagsValues[] = {0, 1, 2, 4};
il2cpp::runtime::Il2CppEnumInfo flagsInfo = {
    il2cpp::runtime::kEnumUnderlyingInt32, 4, flagsNames, flagsValues,
};

// Test.Order -- flags decomposition output order: names print in ASCENDING
// VALUE order regardless of declaration order (net48, probe g2_all=31).
// Has a zero member (Zero=0) so IsFlagsCapable's zero-member discriminator
// fires (net48 decomposes zero-less flags enums too, but the runtime cannot
// tell those apart from plain power-of-two enums -- documented deviation;
// see the fixture comment for Test.NonFlagsPow2).
il2cpp::Il2CppClass orderClass = {
    "Test.Order", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* orderNames[] = {"Zero", "H", "A", "D", "B", "C"};
const int64_t orderValues[] = {0, 16, 1, 8, 2, 4};
il2cpp::runtime::Il2CppEnumInfo orderInfo = {
    il2cpp::runtime::kEnumUnderlyingInt32, 6, orderNames, orderValues,
};

// Test.NonFlagsPow2 -- a NON-flags enum whose members are all powers of two
// but which has NO zero member. The zero-member requirement is the runtime's
// discriminator between a [Flags] enum (canonically declares None/Zero = 0)
// and a plain enum with coincidentally power-of-two members (net48: a plain
// enum like EInt {A=1, B=2} prints "3" for value 3 -- plain3 probe). Values
// avoid the DayOfWeek range (0-6) so the null-descriptor built-in fallback
// stays unambiguous.
il2cpp::Il2CppClass nonFlagsClass = {
    "Test.NonFlagsPow2", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};
const char* nonFlagsNames[] = {"A", "B"};
const int64_t nonFlagsValues[] = {1, 8};
il2cpp::runtime::Il2CppEnumInfo nonFlagsInfo = {
    il2cpp::runtime::kEnumUnderlyingInt32, 2, nonFlagsNames, nonFlagsValues,
};

// Test.DayOfWeek -- the BCL System.DayOfWeek metadata (the runtime ships a
// BUILT-IN registration under this name, enumext.cpp: the registrar runs at
// static init, before main -- so the fixture class below is registered by
// the time the test body runs). int32 underlying, Sunday = 0 .. Saturday = 6
// (the .NET encoding, il2cpp_time.h).
il2cpp::Il2CppClass dowClass = {
    "System.DayOfWeek", 4, 0u, 0, nullptr, nullptr, 0, nullptr, 0,
};

// Wrap an ASCII literal in a managed string (the parser's input shape).
il2cpp::Il2CppObject* Str(const char* cs)
{
    return reinterpret_cast<il2cpp::Il2CppObject*>(
        il2cpp::il2cpp_string_new_from_cs(cs));
}

// A minimal boxed-enum stand-in for il2cpp_enum_equals: the object header
// (klass pointer at offset 0) plus the payload slot at byte offset 8 --
// the exact il2cpp_box_int32 / il2cpp_box_long layouts (boxing_ext2.h).
// The klass pointer is set to the enum's own descriptor, which is what a
// real enum box carries.
struct BoxedInt32
{
    il2cpp::Il2CppObject obj;
    int32_t              payload;
};

struct BoxedInt64
{
    il2cpp::Il2CppObject obj;
    int64_t              payload;
};

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    il2cpp_enum_register("Test.Color", &colorInfo);
    il2cpp_enum_register("Test.Big", &bigInfo);
    il2cpp_enum_register("Test.URange", &uRangeInfo);
    il2cpp_enum_register("Test.ByteE", &byteEInfo);
    il2cpp_enum_register("Test.SbyteE", &sbyteEInfo);
    il2cpp_enum_register("Test.S16", &s16Info);
    il2cpp_enum_register("Test.U16", &u16Info);
    il2cpp_enum_register("Test.U32Max", &u32MaxInfo);
    il2cpp_enum_register("Test.Dup", &dupInfo);
    il2cpp_enum_register("Test.Wide", &wideInfo);
    il2cpp_enum_register("Test.Flags", &flagsInfo);
    il2cpp_enum_register("Test.Order", &orderInfo);
    il2cpp_enum_register("Test.NonFlagsPow2", &nonFlagsInfo);

    // The name-string ABI's parse_boxed resolves the enum's CLASS descriptor
    // by name (il2cpp_class_by_name) to box with the enum's OWN klass -- so
    // the fixture descriptors used by parse_boxed must be in the class
    // registry (the same registration entry.cpp emits for generated enums).
    il2cpp::il2cpp_class_register(&colorClass);
    il2cpp::il2cpp_class_register(&flagsClass);
    il2cpp::il2cpp_class_register(&bigClass);

    // --- 1. GetName: named values / unnamed -> nullptr ---
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&colorClass, 0),
                                    "Red"),
           "get_name Red=0 -> \"Red\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&colorClass, 1),
                                    "Green"),
           "get_name Green=1 -> \"Green\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&colorClass, 2),
                                    "Blue"),
           "get_name Blue=2 -> \"Blue\"");
    Expect(il2cpp_enum_get_name(&colorClass, 42) == nullptr,
           "get_name unnamed value -> nullptr");
    Expect(il2cpp_enum_get_name(nullptr, 0) == nullptr,
           "get_name null class -> nullptr");

    // --- 2. register replace-once ---
    il2cpp_enum_register("Test.Temp", &tempInfoA);
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&tempClass, 10),
                                    "First"),
           "register first entry visible");
    il2cpp_enum_register("Test.Temp", &tempInfoB);
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&tempClass, 20),
                                    "Second"),
           "re-register replaces the entry (last write wins)");
    Expect(il2cpp_enum_get_name(&tempClass, 10) == nullptr,
           "replaced entry no longer visible");

    // --- 3. ToString: name when named, numeric when unnamed ---
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&colorClass, 1),
                                    "Green"),
           "to_string Green=1 -> \"Green\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&colorClass, 7),
                                    "7"),
           "to_string unnamed 7 -> \"7\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&colorClass, -1),
                                    "-1"),
           "to_string unnamed -1 -> \"-1\"");

    // --- 4. Parse numeric ---
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("2")),
                                 &r) == 1 && r == 2,
               "parse numeric \"2\" -> 2");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs(" 1 ")),
                                 &r) == 1 && r == 1,
               "parse whitespace-padded \" 1 \" -> 1");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("-1")),
                                 &r) == 1 && r == -1,
               "parse negative \"-1\" -> -1 (int32 width)");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("99")),
                                 &r) == 1 && r == 99,
               "parse unnamed-but-parseable \"99\" -> 99 (number is the result)");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("2147483647")),
                                 &r) == 1 && r == 2147483647LL,
               "parse int32 boundary \"2147483647\" -> 2147483647");
    }

    // --- 5. Parse name (exact case-sensitive match) ---
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("Green")),
                                 &r) == 1 && r == 1,
               "parse name \"Green\" -> 1");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("green")),
                                 &r) == 0,
               "parse wrong case \"green\" -> 0 (case-sensitive)");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("NoSuch")),
                                 &r) == 0,
               "parse unknown name -> 0");
    }

    // --- 6. Parse error paths (TryParse: no throw, out untouched) ---
    {
        int64_t r = 12345;
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("abc")),
                                 &r) == 0 && r == 12345,
               "parse non-numeric text -> 0, out slot untouched");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("2147483648")),
                                 &r) == 0 && r == 12345,
               "parse int32 overflow \"2147483648\" -> 0, out slot untouched");
        Expect(il2cpp_enum_parse(&colorClass, nullptr, &r) == 0,
               "parse null text -> 0");
        Expect(il2cpp_enum_parse(&colorClass,
                                 reinterpret_cast<Il2CppObject*>(
                                     il2cpp_string_new_from_cs("1")),
                                 nullptr) == 0,
               "parse null out slot -> 0");
    }

    // --- 7. GetUnderlyingType ---
    Expect(ManagedStringEqualsAscii(
               il2cpp_enum_get_underlying_type(&colorClass), "System.Int32"),
           "underlying Test.Color -> \"System.Int32\"");
    Expect(ManagedStringEqualsAscii(
               il2cpp_enum_get_underlying_type(&bigClass), "System.Int64"),
           "underlying Test.Big -> \"System.Int64\"");
    Expect(ManagedStringEqualsAscii(
               il2cpp_enum_get_underlying_type(&uRangeClass), "System.UInt32"),
           "underlying Test.URange -> \"System.UInt32\"");

    // --- 8. int64-backed enum ---
    {
        int64_t r = 0;
        Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&bigClass, 1),
                                        "Small"),
               "int64 get_name Small=1 -> \"Small\"");
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_get_name(&bigClass, 9007199254740993LL),
                   "Huge"),
               "int64 get_name Huge=2^53+1 -> \"Huge\"");
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_to_string(&bigClass, 9007199254740993LL),
                   "Huge"),
               "int64 to_string Huge -> \"Huge\"");
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_to_string(&bigClass, 9007199254740994LL),
                   "9007199254740994"),
               "int64 to_string unnamed -> int64 decimal");
        Expect(il2cpp_enum_parse(
                   &bigClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("9007199254740993")),
                   &r) == 1 && r == 9007199254740993LL,
               "int64 parse big decimal -> 2^53+1");
        Expect(il2cpp_enum_parse(
                   &bigClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("Huge")),
                   &r) == 1 && r == 9007199254740993LL,
               "int64 parse name \"Huge\" -> 2^53+1");
    }

    // --- 9. uint32-backed enum: values above INT32_MAX round-trip ---
    {
        int64_t r = 0;
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_to_string(&uRangeClass, 4000000000LL),
                   "High"),
               "uint32 to_string High=4000000000 -> \"High\" (named)");
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_to_string(&uRangeClass, 4000000001LL),
                   "4000000001"),
               "uint32 to_string unnamed 4000000001 -> \"4000000001\" (int64 formatter)");
        Expect(il2cpp_enum_parse(
                   &uRangeClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("4000000000")),
                   &r) == 1 && r == 4000000000LL,
               "uint32 parse \"4000000000\" -> 4000000000");
    }

    // --- 10. Unregistered class: the Int32 no-metadata assumption ---
    Expect(il2cpp_enum_get_name(&bareClass, 0) == nullptr,
           "bare class get_name -> nullptr (no metadata)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&bareClass, 5),
                                    "5"),
           "bare class to_string -> numeric (int assumption)");
    Expect(ManagedStringEqualsAscii(
               il2cpp_enum_get_underlying_type(&bareClass), "System.Int32"),
           "bare class underlying -> \"System.Int32\" (int assumption)");
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(
                   &bareClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("5")),
                   &r) == 1 && r == 5,
               "bare class parse numeric \"5\" -> 5 (int32 width)");
        Expect(il2cpp_enum_parse(
                   &bareClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("Bogus")),
                   &r) == 0,
               "bare class parse unknown text -> 0");
    }

    // --- 11. Built-in DayOfWeek name descriptors (register -> "Monday") ---
    // The runtime registers System.DayOfWeek's 7 names at static init (see
    // enumext.cpp). The name-keyed path (a real "System.DayOfWeek" class
    // descriptor) prints the member NAME; the interim NULL-descriptor path
    // (the transpiler's enum:l cast emits a null descriptor until
    // typeof/ldtoken emission lands) prints the built-in NAME when the
    // value is unambiguous -- a value a REGISTERED user enum also owns is
    // ambiguous with a null descriptor and formats numeric instead (the
    // documented deviation; the fixtures above own 0/1/2, so those values
    // exercise the ambiguous arm).
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&dowClass, 1),
                                    "Monday"),
           "DayOfWeek to_string Monday=1 -> \"Monday\" (registered name)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&dowClass, 0),
                                    "Sunday"),
           "DayOfWeek to_string Sunday=0 -> \"Sunday\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&dowClass, 6),
                                    "Saturday"),
           "DayOfWeek to_string Saturday=6 -> \"Saturday\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(nullptr, 6),
                                    "Saturday"),
           "DayOfWeek to_string(null descriptor, 6) -> \"Saturday\"");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(nullptr, 3),
                                    "Wednesday"),
           "DayOfWeek to_string(null descriptor, 3) -> \"Wednesday\" (unambiguous built-in match)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(nullptr, 1),
                                    "1"),
           "to_string(null descriptor, 1) -> \"1\" (registered Test.Color owns 1 -- ambiguous, numeric per the deviation note)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(nullptr, 99),
                                    "99"),
           "to_string(null descriptor, 99) -> \"99\" (no built-in match -> numeric)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&dowClass, 1),
                                    "Monday"),
           "DayOfWeek get_name Monday=1 -> \"Monday\"");
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(
                   &dowClass,
                   reinterpret_cast<Il2CppObject*>(
                       il2cpp_string_new_from_cs("Monday")),
                   &r) == 1 && r == 1,
               "DayOfWeek parse name \"Monday\" -> 1");
    }
    Expect(ManagedStringEqualsAscii(
               il2cpp_enum_get_underlying_type(&dowClass), "System.Int32"),
           "DayOfWeek underlying -> \"System.Int32\"");

    // --- 12. Parse name: ASCII whitespace is trimmed before matching (net48) ---
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(&colorClass, Str(" Green "), &r) == 1 && r == 1,
               "parse name \" Green \" -> 1 (leading/trailing ws trimmed)");
        Expect(il2cpp_enum_parse(&colorClass, Str("Blue "), &r) == 1 && r == 2,
               "parse name \"Blue \" -> 2 (trailing ws trimmed)");
        Expect(il2cpp_enum_parse(&colorClass, Str("\tBlue\t"), &r) == 1 && r == 2,
               "parse name tab-padded -> 2 (tab ws trimmed)");
        Expect(il2cpp_enum_parse(&colorClass, Str(" green "), &r) == 0,
               "parse \" green \" -> 0 (trimmed, but case stays sensitive)");
    }

    // --- 13. Parse numeric: per-width range checks (net48) ---
    {
        int64_t r = 0;
        // byte width
        Expect(il2cpp_enum_parse(&byteEClass, Str("255"), &r) == 1 && r == 255,
               "byte parse \"255\" -> 255");
        Expect(il2cpp_enum_parse(&byteEClass, Str("256"), &r) == 0,
               "byte parse \"256\" -> 0 (over byte range)");
        Expect(il2cpp_enum_parse(&byteEClass, Str("-1"), &r) == 0,
               "byte parse \"-1\" -> 0 (unsigned rejects '-')");
        Expect(il2cpp_enum_parse(&byteEClass, Str("+1"), &r) == 1 && r == 1,
               "byte parse \"+1\" -> 1 (leading '+' ok)");
        // sbyte width
        Expect(il2cpp_enum_parse(&sbyteEClass, Str("-128"), &r) == 1 && r == -128,
               "sbyte parse \"-128\" -> -128");
        Expect(il2cpp_enum_parse(&sbyteEClass, Str("-129"), &r) == 0,
               "sbyte parse \"-129\" -> 0 (over sbyte range)");
        Expect(il2cpp_enum_parse(&sbyteEClass, Str("127"), &r) == 1 && r == 127,
               "sbyte parse \"127\" -> 127");
        Expect(il2cpp_enum_parse(&sbyteEClass, Str("128"), &r) == 0,
               "sbyte parse \"128\" -> 0 (over sbyte range)");
        // int16 width
        Expect(il2cpp_enum_parse(&s16Class, Str("-32768"), &r) == 1 && r == -32768,
               "int16 parse \"-32768\" -> -32768");
        Expect(il2cpp_enum_parse(&s16Class, Str("32767"), &r) == 1 && r == 32767,
               "int16 parse \"32767\" -> 32767");
        Expect(il2cpp_enum_parse(&s16Class, Str("32768"), &r) == 0,
               "int16 parse \"32768\" -> 0 (over int16 range)");
        // uint16 width
        Expect(il2cpp_enum_parse(&u16Class, Str("65535"), &r) == 1 && r == 65535,
               "uint16 parse \"65535\" -> 65535");
        Expect(il2cpp_enum_parse(&u16Class, Str("65536"), &r) == 0,
               "uint16 parse \"65536\" -> 0 (over uint16 range)");
        Expect(il2cpp_enum_parse(&u16Class, Str("-1"), &r) == 0,
               "uint16 parse \"-1\" -> 0 (unsigned rejects '-')");
        // uint32 width (the int64 parser alone would accept up to INT64_MAX)
        Expect(il2cpp_enum_parse(&u32MaxClass, Str("4294967295"), &r) == 1 &&
                   r == 4294967295LL,
               "uint32 parse \"4294967295\" -> 4294967295");
        Expect(il2cpp_enum_parse(&u32MaxClass, Str("4294967296"), &r) == 0,
               "uint32 parse \"4294967296\" -> 0 (over uint32 range)");
        Expect(il2cpp_enum_parse(&u32MaxClass, Str("5000000000"), &r) == 0,
               "uint32 parse \"5000000000\" -> 0 (over uint32 range)");
        Expect(il2cpp_enum_parse(&u32MaxClass, Str("-1"), &r) == 0,
               "uint32 parse \"-1\" -> 0 (unsigned rejects '-')");
        // int64 width boundaries
        Expect(il2cpp_enum_parse(&bigClass, Str("-9223372036854775808"), &r) == 1 &&
                   r == INT64_MIN,
               "int64 parse \"-9223372036854775808\" -> INT64_MIN");
        Expect(il2cpp_enum_parse(&bigClass, Str("9223372036854775807"), &r) == 1 &&
                   r == INT64_MAX,
               "int64 parse \"9223372036854775807\" -> INT64_MAX");
    }

    // --- 14. GetName / ToString / Parse: first match wins (net48) ---
    Expect(ManagedStringEqualsAscii(il2cpp_enum_get_name(&dupClass, 2), "B"),
           "get_name duplicate value 2 -> \"B\" (first metadata name)");
    Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&dupClass, 2), "B"),
           "to_string duplicate value 2 -> \"B\" (first metadata name)");
    {
        int64_t r = 0;
        Expect(il2cpp_enum_parse(&dupClass, Str("C"), &r) == 1 && r == 2,
               "parse name \"C\" -> 2 (later duplicate name still parses)");
    }

    // --- 15. Equals: width-aware payload compare (net48) ---
    {
        BoxedInt32 self;
        self.obj.klass = &colorClass;
        self.payload = 1;
        BoxedInt32 same;
        same.obj.klass = &colorClass;
        same.payload = 1;
        BoxedInt32 diffVal;
        diffVal.obj.klass = &colorClass;
        diffVal.payload = 2;
        BoxedInt32 otherType;
        otherType.obj.klass = &bareClass;
        otherType.payload = 1;
        Expect(il2cpp_enum_equals(&self.obj, &same.obj) == 1,
               "equals same int32 enum value -> 1");
        Expect(il2cpp_enum_equals(&self.obj, &diffVal.obj) == 0,
               "equals different int32 enum value -> 0");
        Expect(il2cpp_enum_equals(&self.obj, &otherType.obj) == 0,
               "equals different enum type (same value) -> 0");
        Expect(il2cpp_enum_equals(&self.obj, nullptr) == 0,
               "equals null other -> 0");

        // int64-backed enum: values differing ONLY in the high 32 bits must
        // NOT compare equal (an int32-slot read would truncate them to 1).
        BoxedInt64 hi;
        hi.obj.klass = &wideClass;
        hi.payload = 0x100000001LL;
        BoxedInt64 lo;
        lo.obj.klass = &wideClass;
        lo.payload = 1LL;
        BoxedInt64 hiSame;
        hiSame.obj.klass = &wideClass;
        hiSame.payload = 0x100000001LL;
        Expect(il2cpp_enum_equals(&hi.obj, &lo.obj) == 0,
               "equals int64 high-bits differ (0x100000001 vs 1) -> 0 (full 64-bit compare)");
        Expect(il2cpp_enum_equals(&hi.obj, &hiSame.obj) == 1,
               "equals int64 same value -> 1");
    }

    // --- 16. Boxed enum carries its OWN klass (D-ENM-3/D-ENM-5 fix) ---
    // icall_box_enum(&T_class, value) stamps the box with the ENUM's own
    // class descriptor (name-bearing), NOT the Int64/Int32 carrier -- so the
    // klass-name-keyed registry lookup resolves the metadata, boxed ToString
    // prints the member NAME, cross-type Equals is False, and GetHashCode
    // follows the underlying value's hash (net48).
    {
        Il2CppObject* boxed = icall_box_enum(&colorClass, 1);
        Expect(boxed != nullptr && boxed->klass == &colorClass,
               "icall_box_enum box carries the enum's own klass (not Int64)");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_boxed_to_string(boxed),
                                        "Green"),
               "boxed enum ToString(Green=1) -> \"Green\" (member name)");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_boxed_to_string(
                                           icall_box_enum(&colorClass, 7)),
                                        "7"),
               "boxed enum ToString(unnamed 7) -> \"7\" (numeric fallback)");

        // Cross-type Equals: Color.Green (value 1) vs a DIFFERENT enum type
        // with the SAME underlying value must be False (the type gate).
        Il2CppObject* otherTypeBox = icall_box_enum(&bareClass, 1);
        Expect(otherTypeBox->klass != &colorClass,
               "different enum types have different box klasses");
        Expect(il2cpp_enum_equals(boxed, otherTypeBox) == 0,
               "boxed enum cross-type Equals (same value, different type) -> 0");
        Expect(il2cpp_enum_equals(boxed, icall_box_enum(&colorClass, 1)) == 1,
               "boxed enum same-type same-value Equals -> 1");
        Expect(il2cpp_enum_equals(boxed, icall_box_enum(&colorClass, 2)) == 0,
               "boxed enum same-type different-value Equals -> 0");
        Expect(il2cpp_enum_equals(boxed, nullptr) == 0,
               "boxed enum Equals null -> 0");

        // Transpiler-facing boxed equality (icall_boxed_equals_ext2 -- the
        // Object::Equals wiring for boxed references): enum-klass boxes are
        // routed to il2cpp_enum_equals (the boxingext2 enum arm), so the
        // D-ENM-5 cross-type gate holds at the boxed-equals entry too.
        Expect(icall_boxed_equals_ext2(boxed, icall_box_enum(&colorClass, 1)),
               "boxed_equals same-type same-value -> true");
        Expect(!icall_boxed_equals_ext2(boxed, icall_box_enum(&colorClass, 2)),
               "boxed_equals same-type different-value -> false");
        Expect(!icall_boxed_equals_ext2(boxed, otherTypeBox),
               "boxed_equals cross-type (same value) -> false (D-ENM-5)");

        // GetHashCode: net48 enum hash == the underlying value's hash --
        // int32 width -> the value itself; int64 width -> low^high.
        Expect(il2cpp_enum_boxed_get_hash_code(icall_box_enum(&colorClass, 1)) == 1,
               "boxed int32-backed enum GetHashCode(1) -> 1 (value)");
        Expect(il2cpp_enum_boxed_get_hash_code(icall_box_enum(&bigClass, 1)) == 1,
               "boxed int64-backed enum GetHashCode(1) -> 1 (low^high)");
        const uint64_t bigValue = 0x100000001ULL; // low 32 = 1, high 32 = 1
        Expect(il2cpp_enum_boxed_get_hash_code(icall_box_enum(&bigClass, 1)) ==
                   (static_cast<int32_t>(1) ^ static_cast<int32_t>(0)),
               "boxed int64-backed enum GetHashCode(1) -> low^high of the value");
        Expect(il2cpp_enum_boxed_get_hash_code(
                   icall_box_enum(&bigClass, static_cast<int64_t>(bigValue))) ==
                   (static_cast<int32_t>(bigValue) ^
                    static_cast<int32_t>(bigValue >> 32)),
               "boxed int64-backed enum GetHashCode(0x100000001) -> low^high of the value");
    }

    // --- 17. [Flags] ToString decomposition (rtflags, net48 semantics) ---
    // A flags-capable enum (zero member + power-of-two members) decomposes
    // an unnamed value into its named bit components joined with ", ".
    // Test.Flags = {None=0, F1=1, F2=2, F3=4} -- the enumdeep3 EFlags shape.
    {
        // Combined value 3 = F1|F2 -> "F1, F2" (the ts_flags oracle).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 3),
                                        "F1, F2"),
               "flags to_string 3=F1|F2 -> \"F1, F2\" (combined value)");
        // Combined value 7 = F1|F2|F3 -> all three names (all-bits-covered).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 7),
                                        "F1, F2, F3"),
               "flags to_string 7=F1|F2|F3 -> \"F1, F2, F3\" (all bits covered)");
        // Single member value -> the member name (net48: named values keep
        // their name, not a single-element decomposition).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 1),
                                        "F1"),
               "flags to_string 1 -> \"F1\" (single member)");
        // Zero -> the zero member's name (net48: the declared zero name).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 0),
                                        "None"),
               "flags to_string 0 -> \"None\" (zero member)");
        // Partial coverage -> numeric fallback (net48: bit 8 not a member).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 8),
                                        "8"),
               "flags to_string 8 -> \"8\" (uncovered bit -> numeric)");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, 10),
                                        "10"),
               "flags to_string 10=F2|8 -> \"10\" (8 uncovered -> numeric)");
        // Negative -> numeric fallback (net48 probe neg=-1).
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&flagsClass, -1),
                                        "-1"),
               "flags to_string -1 -> \"-1\" (negative stays numeric)");
        // Boxed path inherits the decomposition (enumdeep3 ts_flags shape:
        // (Enum)(EFlags.F1 | EFlags.F2)).ToString()).
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_boxed_to_string(icall_box_enum(&flagsClass, 3)),
                   "F1, F2"),
               "boxed flags to_string 3 -> \"F1, F2\" (boxed path)");
    }

    // --- 18. Flags output ORDER: ascending by value, not declaration ---
    // Test.Order = {Zero=0, H=16, A=1, D=8, B=2, C=4}. net48 prints
    // decomposed names in ASCENDING VALUE order (probe g2_all=31 ->
    // "X1, X2, X4, X8, X16" for the scrambled declaration {X1=1, X8=8,
    // X2=2, X16=16, X4=4}).
    {
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&orderClass, 31),
                                        "A, B, C, D, H"),
               "flags to_string 31 (scrambled decl) -> \"A, B, C, D, H\" (value-ascending order)");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&orderClass, 10),
                                        "B, D"),
               "flags to_string 10=D|B -> \"B, D\" (value-ascending)");
    }

    // --- 19. NON-flags enums never decompose (net48 plain3=3) ---
    // Test.NonFlagsPow2 = {A=1, B=8}: powers of two but NO zero member --
    // the zero-member requirement is the runtime's [Flags] discriminator, so
    // the enum is NOT flags-capable and value 9 (A|B) prints numeric "9".
    {
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&nonFlagsClass, 9),
                                        "9"),
               "non-flags to_string 9=A|B -> \"9\" (no zero member -> not flags-capable, numeric)");
        // Named values still print their names.
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&nonFlagsClass, 1),
                                        "A"),
               "non-flags to_string 1 -> \"A\" (named value)");
        // DayOfWeek (0..6) is not flags-shaped (values 3/5/6 are not powers
        // of two) -- its unnamed values stay numeric.
        Expect(ManagedStringEqualsAscii(il2cpp_enum_to_string(&dowClass, 9),
                                        "9"),
               "DayOfWeek to_string 9 -> \"9\" (not flags-shaped, numeric)");
    }

    // --- 20. NAME-STRING ABI (enumface, D-STREN-2, 2026-08-12) ---
    // GetName(Type, object) / GetNames(Type) / Parse(Type, string) /
    // HasFlag(Enum) resolve the enum metadata by the type's FULL-NAME STRING
    // (the transpiler's folded typeof value) instead of a class descriptor.
    {
        // GetName by name string: the BOXED value's width-aware payload.
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_get_name_by_type_name(
                       Str("Test.Color"), icall_box_enum(&colorClass, 1)),
                   "Green"),
               "get_name_by_type_name Green=1 boxed -> \"Green\"");
        Expect(ManagedStringEqualsAscii(
                   il2cpp_enum_get_name_by_type_name(
                       Str("Test.Color"), icall_box_enum(&colorClass, 2)),
                   "Blue"),
               "get_name_by_type_name Blue=2 boxed -> \"Blue\"");
        Expect(il2cpp_enum_get_name_by_type_name(
                   Str("Test.Color"), icall_box_enum(&colorClass, 42)) == nullptr,
               "get_name_by_type_name unnamed value boxed -> nullptr");
        Expect(il2cpp_enum_get_name_by_type_name(
                   Str("Test.NoSuch"), icall_box_enum(&colorClass, 1)) == nullptr,
               "get_name_by_type_name unknown type name -> nullptr");
        Expect(il2cpp_enum_get_name_by_type_name(Str("Test.Color"), nullptr) == nullptr,
               "get_name_by_type_name null value box -> nullptr");

        // GetNames by name string: a string[] in DECLARATION order.
        Il2CppArray* names = il2cpp_enum_get_names_by_type_name(Str("Test.Color"));
        Expect(names != nullptr && names->length == 3,
               "get_names_by_type_name Test.Color -> 3 members");
        if (names != nullptr && names->length == 3)
        {
            Il2CppObject* n0 = *reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(names, 0));
            Il2CppObject* n2 = *reinterpret_cast<Il2CppObject**>(
                il2cpp_array_get_elem_ptr(names, 2));
            Expect(ManagedStringEqualsAscii(
                       reinterpret_cast<il2cpp::Il2CppString*>(n0), "Red"),
                   "get_names_by_type_name[0] -> \"Red\" (declaration order)");
            Expect(ManagedStringEqualsAscii(
                       reinterpret_cast<il2cpp::Il2CppString*>(n2), "Blue"),
                   "get_names_by_type_name[2] -> \"Blue\"");
        }
        Il2CppArray* emptyNames =
            il2cpp_enum_get_names_by_type_name(Str("Test.NoSuch"));
        Expect(emptyNames != nullptr && emptyNames->length == 0,
               "get_names_by_type_name unknown type -> empty string[]");

        // Parse by name string: a BOXED enum result with the enum's own klass.
        Il2CppObject* pName =
            il2cpp_enum_parse_boxed(Str("Test.Color"), Str("Green"));
        Expect(pName != nullptr && pName->klass == &colorClass,
               "parse_boxed result box carries the enum klass");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_boxed_to_string(pName),
                                        "Green"),
               "parse_boxed name \"Green\" -> boxed Green");
        Il2CppObject* pNum =
            il2cpp_enum_parse_boxed(Str("Test.Color"), Str("5"));
        Expect(pNum != nullptr && pNum->klass == &colorClass,
               "parse_boxed numeric result box carries the enum klass");
        Expect(ManagedStringEqualsAscii(il2cpp_enum_boxed_to_string(pNum), "5"),
               "parse_boxed numeric \"5\" -> boxed value 5 (unnamed, partial flags coverage stays numeric)");
        Il2CppObject* pWs =
            il2cpp_enum_parse_boxed(Str("Test.Color"), Str(" Green "));
        Expect(ManagedStringEqualsAscii(il2cpp_enum_boxed_to_string(pWs),
                                        "Green"),
               "parse_boxed whitespace-padded name -> boxed Green");
        // Failure path: ArgumentException (net48 Enum.Parse throws).
        bool caughtParseArg = false;
        try
        {
            il2cpp_enum_parse_boxed(Str("Test.Color"), Str("Bogus"));
        }
        catch (il2cpp::DotNetException& e)
        {
            caughtParseArg =
                std::strncmp(e.type_name(), "System.ArgumentException", 24) == 0;
        }
        Expect(caughtParseArg,
               "parse_boxed unknown text -> ArgumentException");

        // HasFlag: the boxed-receiver flags bit test ((self & flag) == flag).
        Il2CppObject* f1 = icall_box_enum(&flagsClass, 1);
        Expect(il2cpp_enum_has_flag(icall_box_enum(&flagsClass, 3), f1) == 1,
               "has_flag 3=F1|F2 has F1 -> 1");
        Expect(il2cpp_enum_has_flag(icall_box_enum(&flagsClass, 2), f1) == 0,
               "has_flag F2 has F1 -> 0");
        Expect(il2cpp_enum_has_flag(icall_box_enum(&flagsClass, 4),
                                    icall_box_enum(&flagsClass, 4)) == 1,
               "has_flag F3 has F3 (self) -> 1");
        // Width-aware: an int64-backed enum whose value differs only in the
        // HIGH 32 bits still has the low flag.
        Expect(il2cpp_enum_has_flag(icall_box_enum(&bigClass, 0x100000001LL),
                                    icall_box_enum(&bigClass, 1)) == 1,
               "has_flag int64 high-bit self has low flag -> 1 (width-aware)");
        // Error paths (net48): null flag -> ArgumentNullException; a klass-
        // NAME mismatch (different enum types) -> ArgumentException.
        bool caughtNullFlag = false;
        try
        {
            il2cpp_enum_has_flag(f1, nullptr);
        }
        catch (il2cpp::DotNetException& e)
        {
            caughtNullFlag =
                std::strncmp(e.type_name(), "System.ArgumentNullException", 28) == 0;
        }
        Expect(caughtNullFlag, "has_flag null flag -> ArgumentNullException");
        bool caughtCrossFlag = false;
        try
        {
            il2cpp_enum_has_flag(icall_box_enum(&colorClass, 1), f1);
        }
        catch (il2cpp::DotNetException& e)
        {
            caughtCrossFlag =
                std::strncmp(e.type_name(), "System.ArgumentException", 24) == 0;
        }
        Expect(caughtCrossFlag, "has_flag cross-type -> ArgumentException");
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d enumext assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime enumext tests passed\n");
    return 0;
}
