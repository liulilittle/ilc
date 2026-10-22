//------------------------------------------------------------------------------
// enumext.cpp -- System.Enum runtime support (see enumext.h).
//
// The registry is a fixed-size array of {type name, info pointer} entries
// keyed by the enum's Il2CppClass NAME (the same identity convention as
// icall_type_equals: the transpiler emits identical name literals per
// type). Linear scan at lookup -- the registry is small (generated code
// registers a handful of enum types) and lookups happen per Enum call;
// startup-only writes need no synchronization.
//
// Numeric parse delegates to the icall_try_parse_* family (il2cpp_parse.h)
// so enum parsing agrees with the shared whitespace/sign/range rules.
// Width selection per underlying TypeCode:
//   SByte/Byte/Int16/UInt16/Int32 -> int32 parse (all values fit int32)
//   UInt32/Int64/UInt64           -> int64 parse
// Parse semantics (net48-verified, enum2 audit 2026-08-08):
//   * member NAME matching trims ASCII whitespace (0x20 / 0x09-0x0D) from
//     both ends of the input, then compares case-sensitively -- .NET's
//     default (net48 trims Unicode whitespace too; this runtime follows
//     the project-wide ASCII-only convention, see il2cpp_parse.h).
//   * numeric text: parsed through the shared icall_try_parse_* family
//     (leading/trailing ASCII whitespace, optional '+'/'-' sign, decimal
//     digits; no hex -- net48 Enum.Parse rejects "0x" text as well), then
//     RANGE-CHECKED against the underlying width: SByte [-128,127],
//     Byte [0,255], Int16 [-32768,32767], UInt16 [0,65535], Int32,
//     UInt32 [0,4294967295], Int64, UInt64 [0, INT64_MAX]. A leading '-'
//     on an unsigned width fails the range check (.NET throws
//     OverflowException); a magnitude beyond the width fails the same way.
// Deviations (documented, small-task scope):
//   * uint64 values >= 2^63 are not reachable as decimal text (the int64
//     parser's range ends at INT64_MAX) and the numeric ToString fallback
//     prints their int64 interpretation. .NET accepts the full uint64
//     decimal range; this runtime's carrier is int64 by design.
//   * [Flags] ToString decomposition (rtflags, 2026-08-09): a flags-
//     capable enum's unnamed value decomposes into its named bit components
//     joined with ", " (net48: "F1, F2" for value 3 of {None=0, F1=1, F2=2,
//     F3=4}). The registry carries NO [Flags] attribute bit (the transpiler
//     emits only the member table), so flags-CAPABILITY is detected from the
//     member shape -- a zero-valued member AND every non-zero member an
//     exact power of two, the canonical [Flags] convention. DOCUMENTED
//     DEVIATION: a plain (non-[Flags]) enum with exactly that member shape
//     also decomposes (net48 requires the attribute); no current unit/e2e
//     case has such a shape (the Test.Color fixture {0,1,2} is the closest
//     and its tested values 7/-1 stay numeric -- partial coverage falls
//     back to the numeric string, matching net48).
//   * Enum.Parse(Type, string, bool ignoreCase) is not exposed: the
//     TryParse-shaped primitive has no case-insensitive flag. .NET's
//     DEFAULT (case-sensitive) matches this runtime exactly.
//
// GetName returns nullptr for unnamed values (.NET semantics); to_string
// falls back to the numeric decimal string -- the "no metadata" fallback
// for unregistered classes is exactly this path with the Int32 assumption.
//------------------------------------------------------------------------------

#include "enumext.h"

#include "il2cpp_exception.h" // il2cpp_throw_new (NullReferenceException)
#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_exception / _null / _null_reference
#include "il2cpp_format.h" // icall_int32_to_string / icall_int64_to_string
#include "il2cpp_parse.h"  // icall_try_parse_int32 / icall_try_parse_int64
#include "il2cpp_runtime.h"    // il2cpp_class_by_name (parse_boxed boxing)
#include "il2cpp_boxing_ext2.h" // icall_box_enum (parse_boxed result box)
#include "il2cpp_gc.h"          // il2cpp_gc_add/remove_static_root (GetNames)

#include <cstring>
#include <string>
#include <vector>

namespace il2cpp {
namespace runtime {

namespace {

constexpr int32_t kMaxRegisteredEnums = 32;

// The maximum number of member names a flags decomposition can produce
// (matches DecomposeFlagsNames' internal taken-array bound; a real flags
// enum has a handful of bits).
constexpr int32_t kMaxFlagsNames = 64;

struct RegistryEntry
{
    const char*           typeName;
    const Il2CppEnumInfo* info;
};

// Startup-only writes (generated code / tests register during static init);
// lookups are read-only from then on. No synchronization by design.
RegistryEntry g_registry[kMaxRegisteredEnums];
int32_t g_registryCount = 0;

// Look up the metadata for an enum class descriptor by NAME identity
// (null descriptor / null name -> not found -> the Int32 no-metadata path).
const Il2CppEnumInfo* FindInfo(Il2CppClass* enumClass)
{
    if (enumClass == nullptr || enumClass->name == nullptr)
    {
        return nullptr;
    }
    for (int32_t i = 0; i < g_registryCount; ++i)
    {
        if (std::strcmp(g_registry[i].typeName, enumClass->name) == 0)
        {
            return g_registry[i].info;
        }
    }
    return nullptr;
}

// Decode a managed string into a UTF-8 std::string -- the enum TYPE-NAME
// input shape of the name-string ABI (the transpiler's folded typeof value;
// ASCII by project convention, so every code unit maps 1:1). Non-ASCII
// units encode lossy ('?') -- registry keys are ASCII by construction, so a
// non-ASCII type name simply never matches (the documented no-metadata
// assumption).
std::string CsToUtf8Name(Il2CppObject* obj)
{
    if (obj == nullptr)
    {
        return std::string();
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(obj);
    std::string out;
    out.reserve(static_cast<size_t>(str->length));
    for (int32_t i = 0; i < str->length; ++i)
    {
        const char16_t c = str->chars[i];
        out.push_back((c < 0x80) ? static_cast<char>(c) : '?');
    }
    return out;
}

// The name-string registry lookup (enumface ABI): the enum metadata registry
// is keyed by exactly the names the transpiler's typeof fold produces, so an
// exact strcmp against the stored keys is the whole lookup. Empty / unknown
// -> nullptr (the Int32 no-metadata assumption).
const Il2CppEnumInfo* FindInfoByName(const std::string& name)
{
    if (name.empty())
    {
        return nullptr;
    }
    for (int32_t i = 0; i < g_registryCount; ++i)
    {
        if (g_registry[i].typeName != nullptr &&
            std::strcmp(g_registry[i].typeName, name.c_str()) == 0)
        {
            return g_registry[i].info;
        }
    }
    return nullptr;
}

// The int32-width underlying kinds (every value fits an int32 slot).
bool IsInt32Sized(int32_t underlying)
{
    return underlying == kEnumUnderlyingSByte ||
           underlying == kEnumUnderlyingByte ||
           underlying == kEnumUnderlyingInt16 ||
           underlying == kEnumUnderlyingUInt16 ||
           underlying == kEnumUnderlyingInt32;
}

// The int64-width underlying kinds (the int64 formatter prints their
// decimal text; uint64 >= 2^63 deviates -- see the file header).
bool IsInt64Sized(int32_t underlying)
{
    return underlying == kEnumUnderlyingUInt32 ||
           underlying == kEnumUnderlyingInt64 ||
           underlying == kEnumUnderlyingUInt64;
}

// The int64-BOXED underlying kinds (il2cpp_boxing_ext2.h: long/ulong boxes
// store the FULL 8-byte payload slot at offset 8; every other width uses
// the int32 slot -- a uint32's full 32-bit pattern fits that slot). The
// Enum.Equals payload compare must read the matching width, or two int64
// values differing only in their high 32 bits would compare equal.
bool IsInt64Boxed(int32_t underlying)
{
    return underlying == kEnumUnderlyingInt64 ||
           underlying == kEnumUnderlyingUInt64;
}

// net48 full names of the supported underlying primitives (whitelist-else:
// any unknown code behaves as System.Int32, the documented default).
const char* UnderlyingName(int32_t underlying)
{
    switch (underlying)
    {
    case kEnumUnderlyingSByte:  return "System.SByte";
    case kEnumUnderlyingByte:   return "System.Byte";
    case kEnumUnderlyingInt16:  return "System.Int16";
    case kEnumUnderlyingUInt16: return "System.UInt16";
    case kEnumUnderlyingInt32:  return "System.Int32";
    case kEnumUnderlyingUInt32: return "System.UInt32";
    case kEnumUnderlyingInt64:  return "System.Int64";
    case kEnumUnderlyingUInt64: return "System.UInt64";
    default:                    return "System.Int32";
    }
}

// ASCII whitespace (0x09-0x0D, 0x20) -- the project-wide whitespace
// convention shared with the numeric parse rules in il2cpp_parse.h.
bool IsAsciiWhitespace(char16_t c)
{
    return c == u'\x20' || (c >= u'\x09' && c <= u'\x0D');
}

// A slice of a managed string after ASCII-whitespace trimming. 'chars'
// points INTO the string's storage (no allocation); a null string yields
// the empty slice.
struct TrimmedText
{
    const char16_t* chars;
    int32_t         len;
};

// Trim ASCII whitespace from both ends of a managed string. .NET trims
// Unicode whitespace before name matching (net48 probe: " B " parses);
// ASCII-only is the documented project-wide deviation (the same convention
// as the numeric parse family).
TrimmedText TrimAsciiWhitespace(Il2CppObject* text)
{
    if (text == nullptr)
    {
        return {nullptr, 0};
    }
    const Il2CppString* str = reinterpret_cast<const Il2CppString*>(text);
    int32_t begin = 0;
    int32_t end = str->length;
    while (begin < end && IsAsciiWhitespace(str->chars[begin]))
    {
        ++begin;
    }
    while (end > begin && IsAsciiWhitespace(str->chars[end - 1]))
    {
        --end;
    }
    return {str->chars + begin, end - begin};
}

// Case-sensitive comparison of a managed string against an ASCII literal
// AFTER trimming ASCII whitespace from both ends (member names are ASCII by
// project convention; the literal path of new_from_cs is always legal for
// them). The trim closes the .NET name-matching gap; only the whitespace
// CLASS (ASCII vs Unicode) is the documented deviation.
bool TextEqualsAsciiTrimmed(Il2CppObject* text, const char* ascii)
{
    const TrimmedText trimmed = TrimAsciiWhitespace(text);
    const size_t asciiLen = std::strlen(ascii);
    if (static_cast<size_t>(trimmed.len) != asciiLen)
    {
        return false;
    }
    for (size_t i = 0; i < asciiLen; ++i)
    {
        if (trimmed.chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

// ASCII case-insensitive variant of TextEqualsAsciiTrimmed: each ASCII
// letter of the literal matches both its own case and the opposite case
// (net48 Enum.Parse(Type, text, ignoreCase: true)). Non-letter characters
// compare exactly. Batch 148.
bool TextEqualsAsciiTrimmedIc(Il2CppObject* text, const char* ascii)
{
    const TrimmedText trimmed = TrimAsciiWhitespace(text);
    const size_t asciiLen = std::strlen(ascii);
    if (static_cast<size_t>(trimmed.len) != asciiLen)
    {
        return false;
    }
    for (size_t i = 0; i < asciiLen; ++i)
    {
        const char16_t c = trimmed.chars[i];
        const char16_t a = static_cast<char16_t>(ascii[i]);
        if (c == a)
        {
            continue;
        }
        const char16_t lower = (c >= u'A' && c <= u'Z') ? static_cast<char16_t>(c - u'A' + u'a') : c;
        const char16_t aLower = (a >= 'A' && a <= 'Z') ? static_cast<char16_t>(a - 'A' + 'a') : a;
        if (lower != aLower)
        {
            return false;
        }
    }
    return true;
}

// net48 enum numeric parsing RANGE-CHECKS against the underlying width
// (probe: "2147483648" -> Overflow on Int32, "256" -> Overflow on Byte,
// "-129" -> Overflow on SByte, "4294967296" -> Overflow on UInt32, "-1" ->
// Overflow on every unsigned width). The shared parsers are signed int32/
// int64; the per-width check below enforces the exact interval AFTER
// parsing, and the out slot is written ONLY on success (TryParse contract).
bool ParseNumeric(const Il2CppEnumInfo* info, Il2CppObject* text,
                  int64_t* result)
{
    const int32_t underlying = (info != nullptr)
        ? info->underlying
        : kEnumUnderlyingInt32;
    if (IsInt32Sized(underlying))
    {
        int32_t value = 0;
        if (icall_try_parse_int32(text, &value) != 1)
        {
            return false;
        }
        switch (underlying)
        {
        case kEnumUnderlyingSByte:
            if (value < INT8_MIN || value > INT8_MAX)
            {
                return false;
            }
            break;
        case kEnumUnderlyingByte:
            if (value < 0 || value > static_cast<int32_t>(UINT8_MAX))
            {
                return false;
            }
            break;
        case kEnumUnderlyingInt16:
            if (value < INT16_MIN || value > INT16_MAX)
            {
                return false;
            }
            break;
        case kEnumUnderlyingUInt16:
            if (value < 0 || value > static_cast<int32_t>(UINT16_MAX))
            {
                return false;
            }
            break;
        case kEnumUnderlyingInt32:
        default:
            break; // the int32 parse already enforced the full width
        }
        *result = value;
        return true;
    }
    int64_t value = 0;
    if (icall_try_parse_int64(text, &value) != 1)
    {
        return false;
    }
    switch (underlying)
    {
    case kEnumUnderlyingUInt32:
        // net48 uint32 range is [0, 4294967295]; the int64 parser would
        // accept magnitudes up to INT64_MAX, so cap explicitly. The lower
        // bound rejects a leading '-' (net48 throws OverflowException).
        if (value < 0 || value > static_cast<int64_t>(UINT32_MAX))
        {
            return false;
        }
        break;
    case kEnumUnderlyingUInt64:
        // Lower bound: reject negatives (net48 OverflowException). The
        // upper bound is the int64 parser's own INT64_MAX cap -- the
        // [2^63, 2^64-1] decimal slice is unreachable on the int64 carrier
        // (documented deviation).
        if (value < 0)
        {
            return false;
        }
        break;
    case kEnumUnderlyingInt64:
    default:
        break; // the int64 parse already enforced the full width
    }
    *result = value;
    return true;
}

//------------------------------------------------------------------------------
// BUILT-IN BCL ENUM METADATA (runtime-owned statics -- the registry stores
// pointers only, so these must outlive it; static storage satisfies that).
//
// System.DayOfWeek -- the .NET weekday encoding (Sunday = 0 .. Saturday = 6,
// matching icall_datetime_get_dayofweek in il2cpp_time.h). Registered under
// its full name at static init (g_builtinEnumRegistrar below), so the
// name-keyed lookup works when a caller passes a "System.DayOfWeek" class
// descriptor. The NULL-descriptor path (the transpiler's interim enum:l
// ABI -- CodeGenerator emits il2cpp_enum_to_string(nullptr, ...) until
// typeof/ldtoken emission lands) falls back to this built-in table, which
// is what makes Console.WriteLine(dt.DayOfWeek) print "Monday" today.
//------------------------------------------------------------------------------
const char* const kDayOfWeekNames[] = {
    "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday",
    "Saturday",
};
const int64_t kDayOfWeekValues[] = {0, 1, 2, 3, 4, 5, 6};
const Il2CppEnumInfo kDayOfWeekInfo = {
    kEnumUnderlyingInt32, 7, kDayOfWeekNames, kDayOfWeekValues,
};

// The null-descriptor fallback list (extend on demand -- one entry per BCL
// enum the runtime ships metadata for). Values are matched by exact int64
// equality, order-insensitive.
const Il2CppEnumInfo* const kBuiltinEnumInfos[] = {
    &kDayOfWeekInfo,
};

// Register the built-ins at static init (single-threaded, before main and
// before any generated code -- the startup-only, no-sync contract of
// il2cpp_enum_register; g_registry / g_registryCount are constant-
// initialized before this ctor runs, same TU declaration order).
struct BuiltinEnumRegistrar
{
    BuiltinEnumRegistrar()
    {
        il2cpp_enum_register("System.DayOfWeek", &kDayOfWeekInfo);
    }
};
BuiltinEnumRegistrar g_builtinEnumRegistrar;

// Match a value against the built-in BCL enum metadata. Returns the member
// name, or nullptr when no built-in carries that value.
const char* FindBuiltinName(int64_t value)
{
    for (const Il2CppEnumInfo* info : kBuiltinEnumInfos)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            if (info->values[i] == value)
            {
                return info->names[i];
            }
        }
    }
    return nullptr;
}

// Does any REGISTERED NON-BUILT-IN enum own 'value'? Built-in entries are
// identified by pointer identity (their registry entries point at the
// built-in statics above). The null-descriptor fallback skips the built-in
// name when a registered USER enum could own the value -- with a null
// descriptor the identity is ambiguous, and numeric is the safe interim
// answer (the exact path arrives with the descriptor-bearing ABI).
bool IsValueOfRegisteredNonBuiltin(int64_t value)
{
    for (int32_t i = 0; i < g_registryCount; ++i)
    {
        const Il2CppEnumInfo* entry = g_registry[i].info;
        if (entry == nullptr)
        {
            continue;
        }
        bool isBuiltin = false;
        for (const Il2CppEnumInfo* builtin : kBuiltinEnumInfos)
        {
            if (entry == builtin)
            {
                isBuiltin = true;
                break;
            }
        }
        if (isBuiltin)
        {
            continue;
        }
        for (int32_t m = 0; m < entry->valueCount; ++m)
        {
            if (entry->values[m] == value)
            {
                return true;
            }
        }
    }
    return false;
}

//------------------------------------------------------------------------------
// [FLAGS] ToString DECOMPOSITION (rtflags, 2026-08-09).
//
// net48 semantics (csc probe): only a [Flags] enum decomposes an unnamed
// value into its named bit components ("F1, F2" for value 3 of {None=0,
// F1=1, F2=2, F3=4}); a NON-flags enum never decomposes (plain3=3). The
// registry carries no [Flags] attribute bit -- the transpiler emits only
// the member table (names + values, enumext.h) -- so flags-capability is
// DETECTED from the member shape: a zero-valued member AND every non-zero
// member an exact power of two, the canonical [Flags] convention. This
// distinguishes the e2e flags enum (EFlags {None=0, F1=1, F2=2, F3=4})
// from the plain enums (EInt {A=1, B=2} -- no zero member; enumdeep Color
// {Red=1, Green=2, Blue=4} -- no zero member). DOCUMENTED DEVIATION: a
// plain enum with exactly the flags member shape (zero + power-of-two
// members) also decomposes; net48 would print numeric for it. The Test.Color
// fixture {0,1,2} is such a shape -- its TESTED values (7, -1, 1) still
// print numeric/name because decomposition only fires on FULL coverage
// (7's bit 4 is uncovered; -1's high bits are uncovered), so no existing
// test regresses.
//------------------------------------------------------------------------------

// Is the enum flags-capable (the canonical [Flags] member shape -- see
// above)? The exact power-of-two test rejects composite members (3, 6, 7,
// ...) and the zero-member requirement excludes plain enums like EInt/Access
// pattern non-flags enums (which have no None member).
bool IsFlagsCapable(const Il2CppEnumInfo* info)
{
    if (info == nullptr)
    {
        return false;
    }
    bool hasZero = false;
    for (int32_t i = 0; i < info->valueCount; ++i)
    {
        const int64_t v = info->values[i];
        if (v == 0)
        {
            hasZero = true;
            continue;
        }
        // Exact power of two (v > 0 and exactly one bit set).
        if (v <= 0 || (v & (v - 1)) != 0)
        {
            return false;
        }
    }
    return hasZero;
}

// Decompose 'value' into the member names whose values fully cover it.
// GREEDY largest-subset-first (net48-probe-verified): repeatedly take the
// LARGEST member value that is a subset of the remaining bits (probe g1_7
// = "B, E" -- 7 = 2|5, NOT 1|2|4 -- and twobits=11 = "AB, F4" -- 8|3 with
// the composite AB preferred), clearing the taken bits. When every bit is
// covered, output the taken names in ASCENDING value order (net48 orders by
// value, not declaration: probe g2_all=31 -> "X1, X2, X4, X8, X16").
// Returns the number of names written (in 'outNames', ascending value
// order), or 0 when the value cannot be fully covered by named members --
// net48 keeps the numeric fallback for partial coverage (probe partial2=10
// -> "10"; value 0 with no zero member -> "0").
int32_t DecomposeFlagsNames(const Il2CppEnumInfo* info, int64_t value,
                            const char** outNames, int32_t maxOut)
{
    constexpr int32_t kMaxFlagsMembers = 64;
    if (info == nullptr || outNames == nullptr || maxOut <= 0)
    {
        return 0;
    }
    int64_t remaining = value;
    const char* taken[kMaxFlagsMembers];
    int32_t takenCount = 0;
    while (remaining != 0)
    {
        int64_t best = 0;
        const char* bestName = nullptr;
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            const int64_t v = info->values[i];
            if (v == 0)
            {
                continue; // the zero member never participates
            }
            if ((remaining & v) != v)
            {
                continue; // v is not a subset of the remaining bits
            }
            if (v > best)
            {
                best = v;
                bestName = info->names[i];
            }
        }
        if (bestName == nullptr || takenCount >= kMaxFlagsMembers)
        {
            return 0; // uncovered bits remain -- not fully decomposable
        }
        taken[takenCount++] = bestName;
        remaining &= ~best;
    }
    if (takenCount > maxOut)
    {
        return 0; // defensive; caller-provided buffer too small
    }
    // The greedy collected LARGEST-first; net48 outputs ASCENDING value
    // order, so emit the taken names in reverse.
    for (int32_t i = 0; i < takenCount; ++i)
    {
        outNames[i] = taken[takenCount - 1 - i];
    }
    return takenCount;
}

// Join 'count' ASCII member names with ", " (comma + space -- net48's flags
// separator) into a managed string. All names are ASCII by project
// convention; the single il2cpp_string_new copies the scratch buffer (the
// same creation discipline as concat.cpp).
Il2CppString* JoinFlagsNames(const char* const* names, int32_t count)
{
    int32_t total = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        total += static_cast<int32_t>(std::strlen(names[i]));
    }
    total += 2 * (count - 1); // ", " separators
    std::vector<char16_t> buf(static_cast<size_t>(total > 0 ? total : 0));
    int32_t o = 0;
    for (int32_t i = 0; i < count; ++i)
    {
        if (i > 0)
        {
            buf[o++] = u',';
            buf[o++] = u' ';
        }
        for (const char* p = names[i]; *p != '\0'; ++p)
        {
            buf[o++] = static_cast<char16_t>(*p);
        }
    }
    return il2cpp_string_new(buf.data(), total);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_enum_equals -- Enum.Equals(object) for a boxed enum (see header).
//   null self -> NullReferenceException (instance semantics); null other ->
//   0; a klass-NAME mismatch (different enum type, or a non-enum box such as
//   System.Int32) -> 0; otherwise the box payloads decide equality. The
//   payload read is WIDTH-AWARE: an int64/uint64-backed enum (the 8-byte
//   box slot, il2cpp_boxing_ext2.h) compares the full int64 at byte offset
//   8 -- otherwise two values differing only in their high 32 bits would
//   compare equal; every other width compares the int32 slot (the
//   il2cpp_box_int32 layout). Unregistered classes keep the Int32
//   assumption (documented).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_equals(Il2CppObject* self, Il2CppObject* other)
{
    if (self == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (other == nullptr)
    {
        return 0;
    }
    if (self->klass == nullptr || other->klass == nullptr ||
        self->klass->name == nullptr || other->klass->name == nullptr ||
        std::strcmp(self->klass->name, other->klass->name) != 0)
    {
        return 0; // different types are never equal (the type gate)
    }
    const Il2CppEnumInfo* info = FindInfo(self->klass);
    if (info != nullptr && IsInt64Boxed(info->underlying))
    {
        const int64_t selfPayload = *reinterpret_cast<const int64_t*>(
            reinterpret_cast<const char*>(self) + 8);
        const int64_t otherPayload = *reinterpret_cast<const int64_t*>(
            reinterpret_cast<const char*>(other) + 8);
        return selfPayload == otherPayload ? 1 : 0;
    }
    const int32_t selfPayload = *reinterpret_cast<const int32_t*>(
        reinterpret_cast<const char*>(self) + 8);
    const int32_t otherPayload = *reinterpret_cast<const int32_t*>(
        reinterpret_cast<const char*>(other) + 8);
    return selfPayload == otherPayload ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_enum_register -- publish enum metadata (see header).
//------------------------------------------------------------------------------
void il2cpp_enum_register(const char* typeName, const Il2CppEnumInfo* info)
{
    if (typeName == nullptr || info == nullptr)
    {
        return; // defensive; generated code always passes real values
    }
    for (int32_t i = 0; i < g_registryCount; ++i)
    {
        if (std::strcmp(g_registry[i].typeName, typeName) == 0)
        {
            g_registry[i].info = info; // replace-once: last write wins
            return;
        }
    }
    if (g_registryCount < kMaxRegisteredEnums)
    {
        g_registry[g_registryCount].typeName = typeName;
        g_registry[g_registryCount].info = info;
        ++g_registryCount;
    }
}

//------------------------------------------------------------------------------
// il2cpp_enum_is_registered -- registry membership probe (see header).
//------------------------------------------------------------------------------
bool il2cpp_enum_is_registered(const Il2CppClass* enumClass)
{
    return FindInfo(const_cast<Il2CppClass*>(enumClass)) != nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_enum_is_registered_name -- NAME-variant registry membership probe
// (see header). The refl6a Type.GetEnum* wrappers (typeinfo.cpp) use it to
// enforce net48's non-enum-receiver ArgumentException contract before
// delegating to the shared by_type_name query icalls.
//------------------------------------------------------------------------------
bool il2cpp_enum_is_registered_name(const char* typeName)
{
    return typeName != nullptr && FindInfoByName(typeName) != nullptr;
}

// The shared box payload slot (offset 8 -- the il2cpp_box_int32 / long box
// layout; MUST stay in sync with boxingext2.cpp's kBoxPayloadOffset).
constexpr int32_t kBoxPayloadOffset = 8;

// The boxed enum's width-aware payload: int64 for Int64/UInt64-backed enums
// (the full 8-byte slot -- a uint64's full pattern round-trips), int32
// otherwise (every other width stores its value in the int32 slot). This is
// the SAME width decision as il2cpp_enum_equals' payload read, so a boxed
// enum's ToString/hash read exactly the bits Equals compares.
int64_t BoxedEnumPayload(const Il2CppEnumInfo* info, Il2CppObject* boxed)
{
    if (info != nullptr && IsInt64Boxed(info->underlying))
    {
        return *reinterpret_cast<const int64_t*>(
            reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset);
    }
    return static_cast<int64_t>(*reinterpret_cast<const int32_t*>(
        reinterpret_cast<const char*>(boxed) + kBoxPayloadOffset));
}

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_to_string -- boxed-enum ToString (see header).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_boxed_to_string(Il2CppObject* boxed)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const Il2CppEnumInfo* info = FindInfo(boxed->klass);
    const int64_t value = BoxedEnumPayload(info, boxed);
    return il2cpp_enum_to_string(boxed->klass, value);
}

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_get_hash_code -- boxed-enum GetHashCode (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_boxed_get_hash_code(Il2CppObject* boxed)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const Il2CppEnumInfo* info = FindInfo(boxed->klass);
    if (info != nullptr && IsInt64Boxed(info->underlying))
    {
        // net48 Int64.GetHashCode: low 32 ^ high 32 (the value-semantics
        // rule, il2cpp_value_semantics.h) -- uint64's full pattern round-
        // trips through the same int64 bit compare.
        const uint64_t u = static_cast<uint64_t>(BoxedEnumPayload(info, boxed));
        return static_cast<int32_t>(u) ^ static_cast<int32_t>(u >> 32);
    }
    // Int32-width (incl. uint32) and the Int32 no-metadata assumption: the
    // value itself is the hash (Int32.GetHashCode identity).
    return static_cast<int32_t>(BoxedEnumPayload(info, boxed));
}

//------------------------------------------------------------------------------
// il2cpp_enum_get_hash_code -- enum-VALUE-CARRIER GetHashCode (see header).
// Same net48 rule as the boxed form, applied to the value slot directly.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_get_hash_code(Il2CppClass* enumClass, int64_t value)
{
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    if (info != nullptr && IsInt64Sized(info->underlying))
    {
        // net48 Int64.GetHashCode: low 32 ^ high 32. UInt32 is int64-SIZED
        // (the formatter's width rule) but BOXES in the int32 slot -- the
        // hash must follow the BOX width (IsInt64Boxed), not the format width.
        if (IsInt64Boxed(info->underlying))
        {
            const uint64_t u = static_cast<uint64_t>(value);
            return static_cast<int32_t>(u) ^ static_cast<int32_t>(u >> 32);
        }
        return static_cast<int32_t>(value);
    }
    // Int32-width (incl. uint32) and the Int32 no-metadata assumption: the
    // value itself is the hash (Int32.GetHashCode identity).
    return static_cast<int32_t>(value);
}

//------------------------------------------------------------------------------
// il2cpp_enum_get_name -- GetName(Type, value): the member name, or
// nullptr for an unnamed value / an unregistered class (see header).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_name(Il2CppClass* enumClass, int64_t value)
{
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    if (info == nullptr)
    {
        return nullptr;
    }
    for (int32_t i = 0; i < info->valueCount; ++i)
    {
        if (info->values[i] == value)
        {
            return il2cpp_string_new_from_cs(info->names[i]);
        }
    }
    return nullptr; // unnamed value: .NET GetName returns null
}

//------------------------------------------------------------------------------
// il2cpp_enum_parse -- Enum.Parse (TryParse shape): name path first (when
// the class is registered), then the numeric path (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_parse(Il2CppClass* enumClass, Il2CppObject* text,
                          int64_t* result)
{
    if (text == nullptr || result == nullptr)
    {
        return 0; // TryParse contract: null input is a failure, no throw
    }
    // .NET TryParse contract: a FAILED parse writes default(T) (=0) into the
    // out slot -- the historical "written only on success" comment was wrong
    // (icallwide E2E: Enum.TryParse<Color>("Mauve", out pc) must yield 0).
    *result = 0;
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    if (info != nullptr)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            if (TextEqualsAsciiTrimmed(text, info->names[i]))
            {
                *result = info->values[i];
                return 1;
            }
        }
    }
    return ParseNumeric(info, text, result) ? 1 : 0;
}

//------------------------------------------------------------------------------
// il2cpp_enum_parse_ic -- Enum.TryParse<T>(string, bool ignoreCase, out T):
// the ignoreCase variant of il2cpp_enum_parse -- the member-NAME path uses
// ASCII case-insensitive matching (batch 148e; the numeric path is shared
// and unaffected). Same TryParse contract (1/0, out written only on success).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_parse_ic(Il2CppClass* enumClass, Il2CppObject* text,
                             int32_t ignoreCase, int64_t* result)
{
    if (text == nullptr || result == nullptr)
    {
        return 0; // TryParse contract: null input is a failure, no throw
    }
    *result = 0; // failed parse writes default(T) (see il2cpp_enum_parse)
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    if (info != nullptr && ignoreCase != 0)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            if (TextEqualsAsciiTrimmedIc(text, info->names[i]))
            {
                *result = info->values[i];
                return 1;
            }
        }
    }
    else if (info != nullptr)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            if (TextEqualsAsciiTrimmed(text, info->names[i]))
            {
                *result = info->values[i];
                return 1;
            }
        }
    }
    return ParseNumeric(info, text, result) ? 1 : 0;
}

// Uppercase hexadecimal rendering of 'value' into 'buffer' padded to
// 'digitCount' digits (leading zeros; the X specifier's per-byte width).
// Returns the number of characters written.
int32_t HexDigitsUpper(uint64_t value, char* buffer, int32_t digitCount)
{
    static const char kHex[] = "0123456789ABCDEF";
    for (int32_t i = digitCount - 1; i >= 0; --i)
    {
        buffer[i] = kHex[value & 0xF];
        value >>= 4;
    }
    return digitCount;
}

//------------------------------------------------------------------------------
// il2cpp_enum_to_string_format -- Enum.ToString(string format): net48
// format-specifier semantics (case-insensitive) over the same member-name
// metadata as il2cpp_enum_to_string. "G" -> member name (numeric fallback
// for unnamed values, the shared by-name path), "D" -> decimal numeric in
// the underlying width, "F" -> [Flags] decomposition (fully-covered values
// only; partial coverage -> the numeric string, net48), "X" -> uppercase
// hexadecimal padded to the underlying width (2 digits per byte). Null
// format -> ArgumentNullException("format"); any other specifier ->
// FormatException (net48). Batch 148 wiring-gap closure.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_to_string_format(Il2CppClass* enumClass,
                                           int64_t value,
                                           Il2CppObject* format)
{
    char spec = 0;
    {
        // A single ASCII letter is the whole valid specifier (net48: the
        // format string is exactly one character). Null or EMPTY format
        // falls back to "G" (net48 Enum.ToString(null/"")).
        std::string text;
        if (format != nullptr)
        {
            text = CsToUtf8Name(format);
        }
        if (text.empty())
        {
            spec = 'G';
        }
        else if (text.size() != 1)
        {
            il2cpp_throw_format_exception("Input string was not in a correct format.");
        }
        else
        {
            char c = text[0];
            if (c >= 'a' && c <= 'z')
            {
                c = static_cast<char>(c - 'a' + 'A'); // case-insensitive
            }
            spec = c;
        }
    }
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    const int32_t underlying = (info != nullptr)
        ? info->underlying
        : kEnumUnderlyingInt32;
    if (spec == 'D')
    {
        // Decimal numeric in the underlying width (net48 Enum.ToString("D")).
        if (IsInt64Sized(underlying))
        {
            return icall_int64_to_string(value);
        }
        return icall_int32_to_string(static_cast<int32_t>(value));
    }
    if (spec == 'X')
    {
        // Uppercase hexadecimal, padded to the underlying width (net48
        // Enum.ToString("X"): 2 digits per byte for the underlying type).
        const int32_t digitCount = IsInt64Sized(underlying) ? 16 : 8;
        uint64_t u = static_cast<uint64_t>(value);
        char buffer[24];
        const int32_t len = HexDigitsUpper(u, buffer, digitCount);
        return il2cpp_string_new_from_cs(std::string(buffer, static_cast<size_t>(len)).c_str());
    }
    if (spec == 'F')
    {
        // net48: "F" behaves like "G" for named values (a non-flags enum's
        // named value prints its NAME -- the enumparseic e2e pinned this);
        // the [Flags] decomposition kicks in for unnamed values of flags-
        // capable enums. il2cpp_enum_to_string already implements exactly
        // that order (name -> flags decomposition -> numeric), so delegate.
        return il2cpp_enum_to_string(enumClass, value);
    }
    if (spec == 'G')
    {
        // The default path: member name, else numeric (shared by-name logic).
        return il2cpp_enum_to_string(enumClass, value);
    }
    il2cpp_throw_format_exception("Input string was not in a correct format.");
    return nullptr; // unreachable (throw is noreturn)
}

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_to_string_format -- Enum.ToString(string format) on a
// BOXED enum value (batch 148): the klass identity rides on the box, so the
// transpiler's box path needs no PrependTypeClass (see the header).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_boxed_to_string_format(Il2CppObject* boxed,
                                                 Il2CppObject* format)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const Il2CppEnumInfo* info = FindInfo(boxed->klass);
    const int64_t value = BoxedEnumPayload(info, boxed);
    return il2cpp_enum_to_string_format(boxed->klass, value, format);
}

//------------------------------------------------------------------------------
// il2cpp_enum_boxed_payload -- the boxed enum's width-aware int64 payload
// (audit H3 unbox closure: icall_unbox_long accepts enum boxes).
//------------------------------------------------------------------------------
int64_t il2cpp_enum_boxed_payload(Il2CppObject* boxed)
{
    if (boxed == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    const Il2CppEnumInfo* info = FindInfo(boxed->klass);
    return BoxedEnumPayload(info, boxed);
}

// Case-EXACT comparison of a managed string against an ASCII literal (no
// trimming -- net48 Enum.IsDefined compares the raw text; only Parse
// trims whitespace).
bool TextEqualsAsciiExact(Il2CppObject* text, const char* ascii)
{
    const Il2CppString* s = reinterpret_cast<Il2CppString*>(text);
    const size_t asciiLen = std::strlen(ascii);
    if (s == nullptr || s->length != static_cast<int32_t>(asciiLen))
    {
        return false;
    }
    for (size_t i = 0; i < asciiLen; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(ascii[i]))
        {
            return false;
        }
    }
    return true;
}

// A managed string value (the Enum.IsDefined/Format string-name form).
bool IsManagedString(const Il2CppObject* obj)
{
    return obj != nullptr && obj->klass != nullptr && obj->klass->name != nullptr
        && std::strcmp(obj->klass->name, "System.String") == 0;
}

// Resolve the IsDefined/Format value argument to its numeric member value:
// a managed string matches member NAMES (case-sensitive, trimmed); any box
// (enum box or primitive box) contributes its payload. Returns false when a
// string matches no member.
bool ResolveQueryValue(const Il2CppEnumInfo* info, Il2CppObject* value,
                       int64_t* result)
{
    if (IsManagedString(value))
    {
        if (info == nullptr)
        {
            return false;
        }
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            // Case-EXACT (no trimming) -- net48 IsDefined compares the raw
            // text (unlike Parse, which trims ASCII whitespace).
            if (TextEqualsAsciiExact(value, info->names[i]))
            {
                *result = info->values[i];
                return true;
            }
        }
        return false;
    }
    *result = BoxedEnumPayload(info, value);
    return true;
}

//------------------------------------------------------------------------------
// il2cpp_enum_is_defined -- Enum.IsDefined(Type, object) (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_is_defined(Il2CppObject* typeName, Il2CppObject* value)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    const std::string name = CsToUtf8Name(typeName);
    const Il2CppEnumInfo* info = FindInfoByName(name);
    if (info == nullptr)
    {
        // raudit4: net48 IsDefined(non-enum Type, ...) throws ArgumentException
        // ("Type provided must be an Enum."), it does not return false.
        il2cpp_throw_argument_exception("Type provided must be an Enum.");
    }
    int64_t v = 0;
    if (!ResolveQueryValue(info, value, &v))
    {
        return 0; // string name matched no member
    }
    for (int32_t i = 0; i < info->valueCount; ++i)
    {
        if (info->values[i] == v)
        {
            return 1;
        }
    }
    return 0;
}

//------------------------------------------------------------------------------
// il2cpp_enum_format -- Enum.Format(Type, object, string) (see header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_format(Il2CppObject* typeName, Il2CppObject* value,
                                 Il2CppObject* format)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    if (value == nullptr)
    {
        il2cpp_throw_argument_null_exception("value");
    }
    const std::string name = CsToUtf8Name(typeName);
    const Il2CppEnumInfo* info = FindInfoByName(name);
    // net48: Enum.Format's value must be the enum or its underlying type --
    // a member NAME string is rejected (ArgumentException). IsDefined is the
    // only query that accepts names.
    if (IsManagedString(value))
    {
        il2cpp_throw_argument_exception(
            "The value passed in must be an enum base or an underlying type for an enum.");
    }
    int64_t v = BoxedEnumPayload(info, value);
    return reinterpret_cast<Il2CppObject*>(il2cpp_enum_to_string_format(
        il2cpp_class_by_name(name.c_str()), v, format));
}

//------------------------------------------------------------------------------
// il2cpp_enum_get_values_by_type_name -- Enum.GetValues(Type) (see header):
// an int32[] of the member values in declaration order (the Int32
// no-metadata assumption -- kEnumUnderlyingInt32), matching the transpiler
// contract (MethodMap.Enum.cs GetValues arm: ReturnTypeName "System.Int32[]",
// so `(int[])Enum.GetValues(t)` castclass conformance holds by klass
// identity and ldelem reads raw int32 values). Non-reference array, so
// no rooting is needed during the fill (the collector never scans it).
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_enum_get_values_by_type_name(Il2CppObject* typeName)
{
    const Il2CppEnumInfo* info = FindInfoByName(CsToUtf8Name(typeName));
    const int32_t count = (info != nullptr) ? info->valueCount : 0;
    // .NET GetValues returns a T[] whose elements compare equal to the enum
    // VALUES as ints -- the e2e surface (`(int[])Enum.GetValues(t)` +
    // int indexing/comparison) needs raw int32 payload, and the int[]
    // klass stamp is what makes the managed castclass succeed.
    // raudit4: a 64-bit underlying (long/ulong) is different -- net48 returns
    // the ENUM array, so Array.GetValue(1) yields the value BOXED AS THE ENUM
    // and Console.WriteLine prints the member NAME ("B"), not the numeric.
    // Build an object[] of pre-boxed enum values (the generated startup block
    // registers the enum klass before any call reaches here); the int64[]
    // fallback below stays for the no-descriptor defensive path.
    const bool wide = info != nullptr
        && (info->underlying == kEnumUnderlyingInt64
            || info->underlying == kEnumUnderlyingUInt64);
    if (wide)
    {
        Il2CppClass* enumClass = il2cpp_class_by_name(CsToUtf8Name(typeName).c_str());
        if (enumClass != nullptr)
        {
            Il2CppArray* boxed = il2cpp_array_new(nullptr, sizeof(void*), true, count);
            if (count <= 0)
            {
                return boxed;
            }
            il2cpp_gc_add_static_root(reinterpret_cast<void**>(&boxed));
            Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(boxed->data);
            for (int32_t i = 0; i < count; ++i)
            {
                slots[i] = icall_box_enum(enumClass, info->values[i]);
            }
            il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&boxed));
            return boxed;
        }
    }
    Il2CppArray* result = il2cpp_array_new(nullptr,
        wide ? static_cast<int32_t>(sizeof(int64_t)) : static_cast<int32_t>(sizeof(int32_t)),
        false, count);
    if (count <= 0)
    {
        return result;
    }
    for (int32_t i = 0; i < count; ++i)
    {
        if (wide)
        {
            *reinterpret_cast<int64_t*>(il2cpp_array_get_elem_ptr(result, i)) =
                info->values[i];
        }
        else
        {
            *reinterpret_cast<int32_t*>(il2cpp_array_get_elem_ptr(result, i)) =
                static_cast<int32_t>(info->values[i]);
        }
    }
    return result;
}

//------------------------------------------------------------------------------
// il2cpp_enum_to_object -- Enum.ToObject(Type, object) (see header).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_to_object(Il2CppObject* typeName, int64_t value)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("type");
    }
    const std::string name = CsToUtf8Name(typeName);
    Il2CppClass* klass = il2cpp_class_by_name(name.c_str());
    if (klass == nullptr)
    {
        il2cpp_throw_argument_exception("Type provided must be an Enum.");
    }
    return icall_box_enum(klass, value);
}

//------------------------------------------------------------------------------
// il2cpp_enum_compare_to -- Enum.CompareTo(object) (see header).
//------------------------------------------------------------------------------
int32_t il2cpp_enum_compare_to(int64_t selfValue, Il2CppObject* other)
{
    if (other == nullptr)
    {
        return 1; // net48 value-type CompareTo(null) -> 1
    }
    if (other->klass == nullptr || other->klass->name == nullptr)
    {
        il2cpp_throw_argument_exception(
            "Object must be the same type as the enum. The type passed in was System.Object; the enum type was System.Enum.");
    }
    const Il2CppEnumInfo* info = FindInfo(other->klass);
    const int64_t otherValue = BoxedEnumPayload(info, other);
    if (selfValue < otherValue)
    {
        return -1;
    }
    if (selfValue > otherValue)
    {
        return 1;
    }
    return 0;
}

//------------------------------------------------------------------------------
// il2cpp_enum_get_underlying_type -- GetUnderlyingType: the underlying
// primitive's name as a managed string (Int32 assumption when unregistered
// or null -- see header).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_underlying_type(Il2CppClass* enumClass)
{
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    const int32_t underlying = (info != nullptr)
        ? info->underlying
        : kEnumUnderlyingInt32;
    return il2cpp_string_new_from_cs(UnderlyingName(underlying));
}

//------------------------------------------------------------------------------
// raudit4: the NAME variant of GetUnderlyingType (the transpiler passes the
// folded typeof type-name string). Same Int32 no-metadata fallback as the
// class form for unregistered names.
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_underlying_type_by_type_name(Il2CppObject* typeName)
{
    if (typeName == nullptr)
    {
        il2cpp_throw_argument_null_exception("enumType");
    }
    const Il2CppEnumInfo* info = FindInfoByName(CsToUtf8Name(typeName));
    const int32_t underlying = (info != nullptr)
        ? info->underlying
        : kEnumUnderlyingInt32;
    return il2cpp_string_new_from_cs(UnderlyingName(underlying));
}

//------------------------------------------------------------------------------
// il2cpp_enum_to_string -- enum ToString optimization: the member name
// when named, else the numeric decimal string in the underlying width
// (the int32 assumption for unregistered classes -- see header).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_to_string(Il2CppClass* enumClass, int64_t value)
{
    const Il2CppEnumInfo* info = FindInfo(enumClass);
    if (info != nullptr)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            if (info->values[i] == value)
            {
                return il2cpp_string_new_from_cs(info->names[i]);
            }
        }
        // [Flags] decomposition (rtflags): an unnamed value of a flags-
        // capable enum decomposes into its named bit components joined with
        // ", " -- value 3 of {None=0, F1=1, F2=2, F3=4} -> "F1, F2" (net48
        // ts_flags). Only fully-covered values decompose; partial coverage
        // falls through to the numeric string (net48, probe partial2=10).
        if (IsFlagsCapable(info))
        {
            const char* flagsNames[kMaxFlagsNames];
            const int32_t flagsCount =
                DecomposeFlagsNames(info, value, flagsNames, kMaxFlagsNames);
            if (flagsCount > 0)
            {
                return JoinFlagsNames(flagsNames, flagsCount);
            }
        }
    }
    else if (enumClass == nullptr)
    {
        // Interim ABI: the transpiler's enum:l cast emits a NULL class
        // descriptor until typeof/ldtoken emission lands. Fall back to the
        // BUILT-IN BCL enum metadata (DayOfWeek today) so Console.WriteLine
        // of a BCL enum value prints its member NAME -- but ONLY when no
        // registered user enum could own the value (a null-descriptor value
        // with an ambiguous owner formats numeric instead; see
        // IsValueOfRegisteredNonBuiltin and the deviation note in
        // enumext.h). The exact by-name path arrives with the
        // descriptor-bearing ABI.
        const char* builtinName = FindBuiltinName(value);
        if (builtinName != nullptr && !IsValueOfRegisteredNonBuiltin(value))
        {
            return il2cpp_string_new_from_cs(builtinName);
        }
    }
    const int32_t underlying = (info != nullptr)
        ? info->underlying
        : kEnumUnderlyingInt32;
    if (IsInt64Sized(underlying))
    {
        return icall_int64_to_string(value);
    }
    return icall_int32_to_string(static_cast<int32_t>(value));
}

//------------------------------------------------------------------------------
// NAME-STRING ABI (enumface, D-STREN-2): the transpiler's typeof(T) fold
// produces the enum's full name as a MANAGED STRING ("Mode" for a nested
// user enum, "System.DayOfWeek" for a BCL TypeRef -- the exact registry
// keys, see FindInfoByName above), so the Enum static API resolves by NAME
// STRING instead of by class descriptor. The primitives below are the
// mapping targets for GetName(Type, object) / GetNames(Type) /
// Parse(Type, string) / HasFlag(Enum).
//------------------------------------------------------------------------------

//------------------------------------------------------------------------------
// il2cpp_enum_get_name_by_type_name -- GetName(Type, object): the member name
// for the BOXED value, or nullptr for an unnamed value / unknown type name
// (net48 GetName returns null; a null value box -> null too).
//------------------------------------------------------------------------------
Il2CppString* il2cpp_enum_get_name_by_type_name(Il2CppObject* typeName,
                                                Il2CppObject* valueBox)
{
    const Il2CppEnumInfo* info = FindInfoByName(CsToUtf8Name(typeName));
    if (info == nullptr || valueBox == nullptr)
    {
        return nullptr;
    }
    const int64_t value = BoxedEnumPayload(info, valueBox);
    for (int32_t i = 0; i < info->valueCount; ++i)
    {
        if (info->values[i] == value)
        {
            return il2cpp_string_new_from_cs(info->names[i]);
        }
    }
    return nullptr; // unnamed value: .NET GetName returns null
}

//------------------------------------------------------------------------------
// il2cpp_enum_get_names_by_type_name -- GetNames(Type): a string[] of the
// member names in DECLARATION order (net48: GetNames preserves metadata
// declaration order, not value order). Unknown type name -> an EMPTY
// string[] (the no-metadata assumption).
//
// GC safety (the stringext two-phase discipline): the result array is
// ROOTED before the first per-name string allocation -- a Gen0 compaction
// during the fill would otherwise reclaim the not-yet-returned array.
//------------------------------------------------------------------------------
Il2CppArray* il2cpp_enum_get_names_by_type_name(Il2CppObject* typeName)
{
    const Il2CppEnumInfo* info = FindInfoByName(CsToUtf8Name(typeName));
    const int32_t count = (info != nullptr) ? info->valueCount : 0;
    Il2CppArray* result = il2cpp_array_new(nullptr, sizeof(void*), true, count);
    if (count <= 0)
    {
        return result;
    }
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&result));
    Il2CppObject** slots = reinterpret_cast<Il2CppObject**>(result->data);
    for (int32_t i = 0; i < count; ++i)
    {
        slots[i] = reinterpret_cast<Il2CppObject*>(
            il2cpp_string_new_from_cs(info->names[i]));
    }
    il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&result));
    return result;
}

// Box the parsed 'value' with the enum's OWN class descriptor (resolved from
// the name string via il2cpp_class_by_name -- icall_box_enum stamps the box
// with the enum klass so boxed ToString/Equals/HasFlag dispatch on the enum
// identity). An unresolvable descriptor (a BCL enum with no generated
// {T}_class) falls back to the Int64 carrier box inside icall_box_enum --
// the documented no-descriptor convention.
Il2CppObject* BoxParsedEnumByName(const std::string& name,
                                  int64_t value)
{
    Il2CppClass* enumClass = il2cpp_class_by_name(name.c_str());
    return icall_box_enum(enumClass, value);
}

//------------------------------------------------------------------------------
// ParseBoxedImpl -- the shared Enum.Parse(Type, string[, bool]) body: member
// NAME lookup (case-sensitive or ASCII-case-insensitive per 'ignoreCase'),
// then numeric decimal in the underlying width; on success returns the value
// BOXED with the enum's own class descriptor. Failures throw ArgumentException
// (net48; the OverflowException split for out-of-range numeric is the
// documented deviation of this minimal face); null text -> ArgumentNullException.
//------------------------------------------------------------------------------
Il2CppObject* ParseBoxedImpl(Il2CppObject* typeName, Il2CppObject* text,
                             bool ignoreCase)
{
    if (text == nullptr)
    {
        il2cpp_throw_argument_null_exception("text");
    }
    const std::string name = CsToUtf8Name(typeName);
    const Il2CppEnumInfo* info = FindInfoByName(name);
    if (info != nullptr)
    {
        for (int32_t i = 0; i < info->valueCount; ++i)
        {
            const bool match = ignoreCase
                ? TextEqualsAsciiTrimmedIc(text, info->names[i])
                : TextEqualsAsciiTrimmed(text, info->names[i]);
            if (match)
            {
                return BoxParsedEnumByName(name, info->values[i]);
            }
        }
    }
    int64_t value = 0;
    if (ParseNumeric(info, text, &value))
    {
        return BoxParsedEnumByName(name, value);
    }
    il2cpp_throw_argument_exception("Requested value was not found.");
    return nullptr; // unreachable (throw is noreturn)
}

//------------------------------------------------------------------------------
// il2cpp_enum_parse_boxed -- Enum.Parse(Type, string). Same grammar as
// il2cpp_enum_parse (member NAME -- case-sensitive, ASCII-whitespace-trimmed
// -- then numeric decimal in the underlying width, range-checked); on success
// the value is returned as a BOXED ENUM with the enum's own class descriptor.
// On failure: ArgumentException (net48 Enum.Parse throws on unknown text;
// the OverflowException split for out-of-range numeric is a documented
// deviation -- this minimal face reports every failure as ArgumentException).
// Null text -> ArgumentNullException (net48).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_parse_boxed(Il2CppObject* typeName,
                                      Il2CppObject* text)
{
    return ParseBoxedImpl(typeName, text, false);
}

//------------------------------------------------------------------------------
// il2cpp_enum_parse_boxed_ic -- Enum.Parse(Type, string, bool ignoreCase):
// the member-NAME match is ASCII case-insensitive when ignoreCase is
// nonzero; everything else (grammar, boxing, exception contract) is shared
// with the 2-arg overload through ParseBoxedImpl. Batch 148 wiring-gap
// closure.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_enum_parse_boxed_ic(Il2CppObject* typeName,
                                         Il2CppObject* text,
                                         int32_t ignoreCase)
{
    return ParseBoxedImpl(typeName, text, ignoreCase != 0);
}

//------------------------------------------------------------------------------
// il2cpp_enum_has_flag -- Enum.HasFlag(Enum): the flags bit test for a BOXED
// enum receiver and argument. (self & flag) == flag (net48), width-aware
// payload read (int64 for Int64/UInt64-backed enums -- the same decision as
// il2cpp_enum_equals / BoxedEnumPayload). Errors (net48): null self ->
// NullReferenceException; null flag -> ArgumentNullException; a klass-NAME
// mismatch (different enum types / non-enum boxes) -> ArgumentException.
//------------------------------------------------------------------------------
int32_t il2cpp_enum_has_flag(Il2CppObject* self, Il2CppObject* flag)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
    if (flag == nullptr)
    {
        il2cpp_throw_argument_null_exception("flag");
    }
    if (self->klass == nullptr || flag->klass == nullptr ||
        self->klass->name == nullptr || flag->klass->name == nullptr ||
        std::strcmp(self->klass->name, flag->klass->name) != 0)
    {
        il2cpp_throw_argument_exception(
            "The type of the argument is not an enum type.");
    }
    const Il2CppEnumInfo* info = FindInfo(self->klass);
    const int64_t selfValue = BoxedEnumPayload(info, self);
    const int64_t flagValue = BoxedEnumPayload(info, flag);
    return ((selfValue & flagValue) == flagValue) ? 1 : 0;
}

} // namespace runtime
} // namespace il2cpp
