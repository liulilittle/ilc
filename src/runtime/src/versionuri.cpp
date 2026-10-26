#include "versionuri.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_* / null_reference
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register /
                                  // il2cpp_is_instance
#include "il2cpp_string.h"        // Il2CppString / il2cpp_string_new_from_utf8

#include <cstring>
#include <utility>
#include <vector>

//------------------------------------------------------------------------------
// System.Version runtime descriptor (GLOBAL scope -- the transpiler emits
// &System_Version_class for newobj/isinst/castclass, the same contract as
// System_String_class in metadata.cpp). instanceSize covers the header +
// component count + 4 int32 components; there are NO reference fields, so
// the collector never scans past the header (refFieldBitmap 0).
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Version_class = {
    "System.Version",                        // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppVersionObject)), // instanceSize: header + count + 4 int32 components
    0u,                                      // refFieldBitmap: no reference fields
    0,                                       // refFieldCount
    &System_Object_class,                 // baseClass (a Version IS-A object)
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

namespace il2cpp {

namespace {

// Layout pin: the icall accessors read the components at fixed offsets.
static_assert(offsetof(Il2CppVersionObject, componentCount) == 1 * sizeof(void*),
              "Il2CppVersionObject: componentCount must sit at slot 1 (offset 8)");
static_assert(offsetof(Il2CppVersionObject, components) == 1 * sizeof(void*) + 4,
              "Il2CppVersionObject: components must start at offset 12");

bool g_versionClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_version_class -- the System.Version descriptor (lazily registered on
// first use; idempotent -- il2cpp_class_register dedupes by pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_version_class()
{
    if (!g_versionClassRegistered)
    {
        il2cpp_class_register(&System_Version_class);
        g_versionClassRegistered = true;
    }
    return &System_Version_class;
}

} // namespace il2cpp

//------------------------------------------------------------------------------
// System.Uri runtime descriptor (GLOBAL scope -- the transpiler emits
// &System_Uri_class for newobj/isinst/castclass, the same contract as
// System_Version_class above). instanceSize covers the header + 5 string
// pointers + the isAbsolute flag; slots 1..5
// (scheme/host/path/opaque/original) ARE GC references, so refFieldBitmap
// bits 1..5 are set -- the compacting collector scans them and rewrites the
// slots on Gen0 compaction (the Version object has no such requirement --
// its components are int32, not references). The fourth slot (opaque) is
// the verbatim "scheme:" remainder for opaque "scheme:rest" URIs
// (D-NETD2-1); null for hierarchical/network-path URIs. The fifth slot
// (original) is the verbatim input text (OriginalString, URI-FIX P1).
il2cpp::Il2CppClass System_Uri_class = {
    "System.Uri",                              // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppUriObject)), // instanceSize: header + 5 string pointers + flag
    0b111110u,                                 // refFieldBitmap: bits 1..5 = scheme, host, path, opaque, original
    5,                                         // refFieldCount: five reference fields
    &System_Object_class,                      // baseClass (a Uri IS-A object)
    nullptr,                                   // vtable
    0,                                         // vtableSize
    nullptr,                                   // interfaces
    0,                                         // interfaceCount
};

namespace il2cpp {

namespace {

// Layout pin: the refFieldBitmap bits must line up with the string slots.
static_assert(offsetof(Il2CppUriObject, scheme) == 1 * sizeof(void*),
              "Il2CppUriObject: scheme must sit at slot 1 (bitmap bit 1)");
static_assert(offsetof(Il2CppUriObject, host) == 2 * sizeof(void*),
              "Il2CppUriObject: host must sit at slot 2 (bitmap bit 2)");
static_assert(offsetof(Il2CppUriObject, path) == 3 * sizeof(void*),
              "Il2CppUriObject: path must sit at slot 3 (bitmap bit 3)");
static_assert(offsetof(Il2CppUriObject, opaque) == 4 * sizeof(void*),
              "Il2CppUriObject: opaque must sit at slot 4 (bitmap bit 4)");
static_assert(offsetof(Il2CppUriObject, original) == 5 * sizeof(void*),
              "Il2CppUriObject: original must sit at slot 5 (bitmap bit 5)");
static_assert(offsetof(Il2CppUriObject, isAbsolute) ==
                  offsetof(Il2CppUriObject, original) + sizeof(il2cpp::Il2CppString*),
              "Il2CppUriObject: isAbsolute must follow the original slot");

bool g_uriClassRegistered = false;

} // namespace

//------------------------------------------------------------------------------
// il2cpp_uri_class -- the System.Uri descriptor (lazily registered on first
// use; idempotent -- il2cpp_class_register dedupes by pointer).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_uri_class()
{
    if (!g_uriClassRegistered)
    {
        il2cpp_class_register(&System_Uri_class);
        g_uriClassRegistered = true;
    }
    return &System_Uri_class;
}

} // namespace il2cpp

namespace il2cpp { namespace runtime {

namespace {

// RFC 3986 scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ): the FIRST
// character must be a letter (net48 rejects digit/+/./- leading schemes such
// as "1http://x" with UriFormatException); the continuation chars are ALPHA /
// DIGIT / '+' / '-' / '.'. Shared by the hierarchical and opaque forms.
bool IsValidScheme(const std::string& scheme)
{
    if (scheme.empty())
    {
        return false;
    }
    for (std::size_t i = 0; i < scheme.size(); ++i)
    {
        const char c = scheme[i];
        const bool isAlpha = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        if (i == 0)
        {
            if (!isAlpha)
            {
                return false; // digit/+/./- leading scheme -- invalid
            }
            continue;
        }
        const bool isAlphaNum = isAlpha || (c >= '0' && c <= '9');
        if (!(isAlphaNum || c == '+' || c == '-' || c == '.'))
        {
            return false; // illegal scheme character
        }
    }
    return true;
}

} // namespace

//------------------------------------------------------------------------------
// Version parsing.
//
// Grammar (simplified, see versionuri.h): one to four dot-separated decimal
// segments. Each segment is 1+ ASCII digits with an int32 overflow guard
// (accumulate in int64, reject the moment INT32_MAX is exceeded -- the
// running value is checked BEFORE the next multiply, so no intermediate
// overflow is possible). Error path first: any empty/non-digit segment,
// trailing garbage, a fifth segment, or an out-of-range value returns 0
// with 'components' untouched.
//------------------------------------------------------------------------------
int32_t il2cpp_version_parse(const char* text, int32_t* components)
{
    if (text == nullptr || components == nullptr)
    {
        return 0;
    }
    const char* p = text;
    int32_t count = 0;
    while (*p != '\0')
    {
        if (count >= 4)
        {
            return 0; // more than 4 segments
        }
        int64_t value = 0;
        bool anyDigit = false;
        while (*p >= '0' && *p <= '9')
        {
            anyDigit = true;
            const int32_t digit = *p - '0';
            if (value > (INT32_MAX - digit) / 10)
            {
                return 0; // segment exceeds int32 (checked BEFORE the
                          // multiply -- no overflow of the accumulator)
            }
            value = value * 10 + digit;
            ++p;
        }
        if (!anyDigit)
        {
            return 0; // empty segment, sign, or non-decimal character
        }
        components[count++] = static_cast<int32_t>(value);
        if (*p == '.')
        {
            ++p;
            if (*p == '\0')
            {
                return 0; // trailing separator ("1.", "1.2.")
            }
            continue;
        }
        if (*p == '\0')
        {
            break;
        }
        return 0; // trailing garbage (not '.', not end)
    }
    return count;
}

//------------------------------------------------------------------------------
// Version ToString: rebuild "major.minor[.build[.revision]]" from the first
// 'count' components. count is clamped to [1, 4]; the component values are
// printed verbatim (no trailing-zero trimming -- see header).
//------------------------------------------------------------------------------
std::string il2cpp_version_to_string(const int32_t* components, int32_t count)
{
    if (components == nullptr || count < 1)
    {
        return std::string();
    }
    if (count > 4)
    {
        count = 4;
    }
    std::string result;
    for (int32_t i = 0; i < count; ++i)
    {
        if (i != 0)
        {
            result.push_back('.');
        }
        result += std::to_string(components[i]);
    }
    return result;
}

//------------------------------------------------------------------------------
// Version CompareTo (net48 rule): compare major, minor, build, revision in
// order; a missing trailing component counts as -1, so (1,2) < (1,2,0,0)
// and (1,2,3) < (1,2,3,4). Null operands compare equal (defensive).
//------------------------------------------------------------------------------
int32_t il2cpp_version_compare(const int32_t* a, int32_t aCount,
                               const int32_t* b, int32_t bCount)
{
    if (a == nullptr || b == nullptr)
    {
        return 0;
    }
    for (int32_t i = 0; i < 4; ++i)
    {
        const int32_t av = (i < aCount) ? a[i] : -1;
        const int32_t bv = (i < bCount) ? b[i] : -1;
        if (av != bv)
        {
            return (av < bv) ? -1 : 1;
        }
    }
    return 0;
}

namespace {

//------------------------------------------------------------------------------
// Version object icalls (the object ABI owns the component storage; the
// parse primitives above stay buffer-based).
//------------------------------------------------------------------------------

Il2CppVersionObject* AsVersion(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppVersionObject*>(self);
}

// Defensive null-receiver guard: generated code null-checks first; this is
// the icall's contract (mirrors arraylist.cpp / stringbuilder.cpp).
void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// Convert a managed string's UTF-16 payload into the ASCII text the parse
// primitive consumes. The Version grammar is pure ASCII; the FIRST
// non-ASCII unit makes the whole input invalid (returns 0 -- the caller
// throws ArgumentException).
int32_t ParseVersionString(Il2CppString* input, int32_t* components)
{
    std::string ascii;
    ascii.reserve(static_cast<size_t>(input->length));
    for (int32_t i = 0; i < input->length; ++i)
    {
        const char16_t u = input->chars[i];
        if (u > 0x7F)
        {
            return 0; // non-ASCII unit: cannot be a Version literal
        }
        ascii.push_back(static_cast<char>(u));
    }
    return il2cpp_version_parse(ascii.c_str(), components);
}

// Store the parse result: the count is preserved verbatim (the domain
// round-trip rule), absent components are -1 (net48 getter/CompareTo rule).
void StoreVersion(Il2CppVersionObject* v, int32_t count, const int32_t* comps)
{
    v->componentCount = count;
    for (int32_t i = 0; i < 4; ++i)
    {
        v->components[i] = (i < count) ? comps[i] : -1;
    }
}

constexpr const char* kInvalidVersionMessage =
    "Version string portion was too short or too long.";

} // namespace

void icall_version_ctor(Il2CppObject* self, Il2CppObject* input)
{
    ThrowIfNullReceiver(self);
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("version");
    }
    int32_t comps[4];
    const int32_t count = ParseVersionString(reinterpret_cast<Il2CppString*>(input), comps);
    if (count == 0)
    {
        il2cpp_throw_argument_exception(kInvalidVersionMessage);
    }
    StoreVersion(AsVersion(self), count, comps);
}

void icall_version_ctor_default(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const int32_t comps[4] = { 0, 0, -1, -1 };
    StoreVersion(AsVersion(self), 2, comps);
}

// Component ctors: every component must be >= 0 -- net48 throws
// ArgumentOutOfRangeException naming the offending parameter.
void icall_version_ctor_2(Il2CppObject* self, int32_t major, int32_t minor)
{
    ThrowIfNullReceiver(self);
    if (major < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("major", "major cannot be less than zero.");
    }
    if (minor < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("minor", "minor cannot be less than zero.");
    }
    const int32_t comps[4] = { major, minor, -1, -1 };
    StoreVersion(AsVersion(self), 2, comps);
}

void icall_version_ctor_3(Il2CppObject* self, int32_t major, int32_t minor,
                          int32_t build)
{
    ThrowIfNullReceiver(self);
    if (major < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("major", "major cannot be less than zero.");
    }
    if (minor < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("minor", "minor cannot be less than zero.");
    }
    if (build < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("build", "build cannot be less than zero.");
    }
    const int32_t comps[4] = { major, minor, build, -1 };
    StoreVersion(AsVersion(self), 3, comps);
}

void icall_version_ctor_4(Il2CppObject* self, int32_t major, int32_t minor,
                          int32_t build, int32_t revision)
{
    ThrowIfNullReceiver(self);
    if (major < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("major", "major cannot be less than zero.");
    }
    if (minor < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("minor", "minor cannot be less than zero.");
    }
    if (build < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("build", "build cannot be less than zero.");
    }
    if (revision < 0)
    {
        il2cpp_throw_argument_out_of_range_exception("revision", "revision cannot be less than zero.");
    }
    const int32_t comps[4] = { major, minor, build, revision };
    StoreVersion(AsVersion(self), 4, comps);
}

int32_t icall_version_get_major(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    return v->componentCount >= 1 ? v->components[0] : 0;
}

int32_t icall_version_get_minor(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    return v->componentCount >= 2 ? v->components[1] : 0;
}

int32_t icall_version_get_build(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    return v->componentCount >= 3 ? v->components[2] : -1;
}

int32_t icall_version_get_revision(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    return v->componentCount >= 4 ? v->components[3] : -1;
}

//------------------------------------------------------------------------------
// get_MajorRevision / get_MinorRevision: the upper/lower 16-bit halves of
// Revision (net48 contract; an unwritten Revision reads as -1, whose halves
// are also -1 -- the arithmetic falls out naturally).
//------------------------------------------------------------------------------
int32_t icall_version_get_major_revision(Il2CppObject* self)
{
    const int32_t revision = icall_version_get_revision(self);
    return static_cast<int16_t>(static_cast<uint32_t>(revision) >> 16);
}

int32_t icall_version_get_minor_revision(Il2CppObject* self)
{
    const int32_t revision = icall_version_get_revision(self);
    return static_cast<int16_t>(revision & 0xFFFF);
}

//------------------------------------------------------------------------------
// GetHashCode: a deterministic component fold (net48's exact bit layout is
// implementation-defined; the E2E contract is same-value -> same-hash).
//------------------------------------------------------------------------------
int32_t icall_version_get_hash_code(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    int32_t acc = 0;
    for (int32_t i = 0; i < 4; ++i)
    {
        const int32_t c = i < v->componentCount ? v->components[i] : -1;
        acc = (acc << 5) - acc + c;
    }
    return acc;
}

Il2CppObject* icall_version_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    const std::string text = il2cpp_version_to_string(v->components, v->componentCount);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

Il2CppObject* icall_version_to_string_field(Il2CppObject* self, int32_t fieldCount)
{
    // net48 version.cs:284. fieldCount == 0 -> ""; 1 -> major; 2 ->
    // major.minor; 3 -> major.minor.build (throws when build is absent); 4 ->
    // major.minor.build.revision (throws when revision is absent); anything
    // else (negative / > 4) throws ArgumentException.
    ThrowIfNullReceiver(self);
    const Il2CppVersionObject* v = AsVersion(self);
    if (fieldCount == 0)
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(""));
    if (fieldCount < 0)
        il2cpp_throw_argument_exception("Specified argument was out of the range of valid values. (Parameter 'fieldCount')");
    const std::string major = std::to_string(v->components[0]);
    if (fieldCount == 1)
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(major.c_str()));
    if (fieldCount == 2)
    {
        const std::string text = major + "." + std::to_string(v->components[1]);
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
    }
    if (v->components[2] == -1)
        il2cpp_throw_argument_exception("Specified argument was out of the range of valid values. (Parameter 'fieldCount')");
    if (fieldCount == 3)
    {
        const std::string text = major + "." + std::to_string(v->components[1]) + "." + std::to_string(v->components[2]);
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
    }
    if (v->components[3] == -1)
        il2cpp_throw_argument_exception("Specified argument was out of the range of valid values. (Parameter 'fieldCount')");
    if (fieldCount == 4)
    {
        const std::string text = major + "." + std::to_string(v->components[1]) + "." + std::to_string(v->components[2]) + "." + std::to_string(v->components[3]);
        return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
    }
    il2cpp_throw_argument_exception("Specified argument was out of the range of valid values. (Parameter 'fieldCount')");
    return nullptr; // unreachable (il2cpp_throw_argument_exception always throws)
}

int32_t icall_version_compare(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    if (other == nullptr)
    {
        return 1; // net48: CompareTo(null) returns 1
    }
    if (!il2cpp_is_instance(other, &System_Version_class))
    {
        il2cpp_throw_argument_exception("Object must be of type System.Version.");
    }
    const Il2CppVersionObject* a = AsVersion(self);
    const Il2CppVersionObject* b = AsVersion(other);
    return il2cpp_version_compare(a->components, a->componentCount,
                                  b->components, b->componentCount);
}

bool icall_version_equals(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    if (other == nullptr)
    {
        return false;
    }
    if (!il2cpp_is_instance(other, &System_Version_class))
    {
        return false;
    }
    const Il2CppVersionObject* a = AsVersion(self);
    const Il2CppVersionObject* b = AsVersion(other);
    return il2cpp_version_compare(a->components, a->componentCount,
                                  b->components, b->componentCount) == 0;
}

bool icall_version_op_equality(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b; // both null equal; exactly one null unequal
    }
    if (!il2cpp_is_instance(a, &System_Version_class)
        || !il2cpp_is_instance(b, &System_Version_class))
    {
        return false;
    }
    const Il2CppVersionObject* av = AsVersion(a);
    const Il2CppVersionObject* bv = AsVersion(b);
    return il2cpp_version_compare(av->components, av->componentCount,
                                  bv->components, bv->componentCount) == 0;
}

bool icall_version_op_inequality(Il2CppObject* a, Il2CppObject* b)
{
    return !icall_version_op_equality(a, b);
}

//------------------------------------------------------------------------------
// op_LessThan / op_GreaterThan / op_LessThanOrEqual / op_GreaterThanOrEqual
// (static, two Version operands). Null doctrine (modern .NET): null compares
// as LESS than any non-null version; null vs null is equal. The comparison
// itself reuses il2cpp_version_compare's net48 missing-component rule.
//------------------------------------------------------------------------------
namespace {
int32_t VersionNullAwareCompare(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr || b == nullptr)
    {
        return a == b ? 0 : (a == nullptr ? -1 : 1);
    }
    const Il2CppVersionObject* av = AsVersion(a);
    const Il2CppVersionObject* bv = AsVersion(b);
    return il2cpp_version_compare(av->components, av->componentCount,
                                  bv->components, bv->componentCount);
}
}

bool icall_version_op_less_than(Il2CppObject* a, Il2CppObject* b)
{
    return VersionNullAwareCompare(a, b) < 0;
}

bool icall_version_op_greater_than(Il2CppObject* a, Il2CppObject* b)
{
    return VersionNullAwareCompare(a, b) > 0;
}

bool icall_version_op_less_or_equal(Il2CppObject* a, Il2CppObject* b)
{
    return VersionNullAwareCompare(a, b) <= 0;
}

bool icall_version_op_greater_or_equal(Il2CppObject* a, Il2CppObject* b)
{
    return VersionNullAwareCompare(a, b) >= 0;
}

Il2CppObject* icall_version_parse(Il2CppObject* input)
{
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("input");
    }
    int32_t comps[4];
    const int32_t count = ParseVersionString(reinterpret_cast<Il2CppString*>(input), comps);
    if (count == 0)
    {
        il2cpp_throw_argument_exception(kInvalidVersionMessage);
    }
    Il2CppVersionObject* v = reinterpret_cast<Il2CppVersionObject*>(
        il2cpp_object_new(&System_Version_class));
    StoreVersion(v, count, comps);
    return reinterpret_cast<Il2CppObject*>(v);
}

//------------------------------------------------------------------------------
// Uri parsing (net48-loose grammar, D-NETD2-1 -- see versionuri.h). Three
// accepted forms:
//
//   (a) hierarchical "scheme://host[/path]" -- the "://" separator is
//       located first; 'scheme' is the text before it and must be non-empty,
//       start with an ALPHA character, and consist only of RFC 3986 scheme
//       characters (ALPHA / DIGIT / '+' / '-' / '.'). 'host' is the text
//       between "://" and the first '/' (or the end); it must be non-empty.
//       'path' is everything from that first '/' on (or "").
//   (b) network-path "//host[/path]" -- net48 resolves these as FILE-scheme
//       references (Scheme "file", Host = between "//" and the first '/',
//       Path = from that '/' on), so ILC does the same; the host must be
//       non-empty. The scheme is NOT validated for this form (it is fixed
//       "file").
//   (c) opaque "scheme:rest" (no "//" prefix, no "://"): 'scheme' is the
//       text before the first ':' (same RFC 3986 validation as (a)). For
//       the MAILTO scheme the rest is split as "[userinfo@]host[/path]":
//       host = the text after the LAST '@' (the whole pre-slash text when
//       there is no '@'), path = from the first '/' on (or "") -- net48
//       reports Host=host / AbsolutePath=/path for mailto. For every OTHER
//       scheme the whole rest is the path and the host is "" -- net48's
//       Host/AbsolutePath rule. The verbatim rest is kept in 'opaque' so
//       il2cpp_uri_to_string round-trips the original "scheme:rest" text
//       exactly.
//
// Everything is preserved verbatim (no case normalization, no escape
// handling, no query/fragment splitting) so il2cpp_uri_to_string round-trips
// the original input. The result is built into a local first and only
// copied into '*out' on success, keeping the "untouched on failure"
// contract structural.
//------------------------------------------------------------------------------
int32_t il2cpp_uri_try_create(const char* text, il2cpp_uri_t* out)
{
    if (text == nullptr || out == nullptr)
    {
        return 0;
    }

    // (b) network-path reference: "//host[/path]" -> file scheme.
    if (text[0] == '/' && text[1] == '/')
    {
        const char* hostStart = text + 2;
        const char* pathStart = std::strchr(hostStart, '/');
        const std::size_t hostLen = (pathStart != nullptr)
            ? static_cast<std::size_t>(pathStart - hostStart)
            : std::strlen(hostStart);
        if (hostLen == 0)
        {
            return 0; // empty authority ("//" or "///path")
        }
        il2cpp_uri_t parsed;
        parsed.scheme = "file";
        parsed.host.assign(hostStart, hostLen);
        parsed.path = (pathStart != nullptr) ? std::string(pathStart) : std::string();
        parsed.isOpaque = false;
        *out = std::move(parsed);
        return 1;
    }

    const char* sep = std::strstr(text, "://");
    if (sep != nullptr)
    {
        // (a) hierarchical: "scheme://host[/path]".
        const std::size_t schemeLen = static_cast<std::size_t>(sep - text);
        if (schemeLen == 0)
        {
            return 0;
        }
        const std::string scheme(text, schemeLen);
        if (!IsValidScheme(scheme))
        {
            return 0; // empty / non-ALPHA-leading / illegal-char scheme
        }
        const char* hostStart = sep + 3;
        const char* pathStart = std::strchr(hostStart, '/');
        const std::size_t hostLen = (pathStart != nullptr)
            ? static_cast<std::size_t>(pathStart - hostStart)
            : std::strlen(hostStart);
        if (hostLen == 0)
        {
            // URI-FIX P3 (C7): file:///drive paths carry an EMPTY authority
            // (net48: file scheme, host "", path "/C:/..."). Every other
            // scheme still rejects the empty host (net48 rejects
            // "http:///path" too). Scheme compare is case-insensitive --
            // the stored scheme keeps its verbatim case (C1 lowercases at
            // rebuild).
            bool isFile = (scheme.size() == 4)
                && (scheme[0] == 'f' || scheme[0] == 'F')
                && (scheme[1] == 'i' || scheme[1] == 'I')
                && (scheme[2] == 'l' || scheme[2] == 'L')
                && (scheme[3] == 'e' || scheme[3] == 'E');
            if (!isFile)
            {
                return 0; // empty authority (e.g. "http:///path" or "http://")
            }
        }
        il2cpp_uri_t parsed;
        parsed.scheme = scheme;
        parsed.host.assign(hostStart, hostLen);
        parsed.path = (pathStart != nullptr) ? std::string(pathStart) : std::string();
        parsed.isOpaque = false;
        *out = std::move(parsed);
        return 1;
    }

    // (c) opaque: "scheme:rest" (no "://"). net48 accepts non-"://" schemes
    //     (D-NETD2-1): mailto:, urn:, custom:, ...
    const char* colon = std::strchr(text, ':');
    if (colon == nullptr)
    {
        return 0; // relative form ("a/b", "/x") -- no scheme separator
    }
    const std::size_t schemeLen = static_cast<std::size_t>(colon - text);
    if (schemeLen == 0)
    {
        return 0; // empty scheme (":rest")
    }
    const std::string scheme(text, schemeLen);
    if (!IsValidScheme(scheme))
    {
        return 0; // empty / non-ALPHA-leading / illegal-char scheme
    }
    const char* rest = colon + 1;
    il2cpp_uri_t parsed;
    parsed.scheme = scheme;
    parsed.isOpaque = true;
    parsed.opaque.assign(rest);
    if (scheme == "mailto")
    {
        // net48: the opaque part is "[userinfo@]host[/path]" -- host is the
        // text after the LAST '@' (or the whole pre-slash text when there is
        // no '@'), path is from the first '/' on. (net48 throws on multiple
        // '@'; ILC takes the last '@' -- documented simplification.)
        const char* slash = std::strchr(rest, '/');
        const std::size_t beforeLen = (slash != nullptr)
            ? static_cast<std::size_t>(slash - rest)
            : std::strlen(rest);
        const std::string before(rest, beforeLen);
        const std::size_t at = before.rfind('@');
        if (at != std::string::npos)
        {
            parsed.host.assign(before, at + 1, std::string::npos);
        }
        else
        {
            parsed.host = before;
        }
        parsed.path = (slash != nullptr) ? std::string(slash) : std::string();
    }
    else
    {
        // net48: the whole opaque part is the AbsolutePath; Host is empty.
        parsed.host = std::string();
        parsed.path = parsed.opaque;
    }
    *out = std::move(parsed);
    return 1;
}

const char* il2cpp_uri_get_scheme(const il2cpp_uri_t* uri)
{
    return (uri != nullptr) ? uri->scheme.c_str() : "";
}

const char* il2cpp_uri_get_host(const il2cpp_uri_t* uri)
{
    return (uri != nullptr) ? uri->host.c_str() : "";
}

const char* il2cpp_uri_get_path(const il2cpp_uri_t* uri)
{
    return (uri != nullptr) ? uri->path.c_str() : "";
}

//------------------------------------------------------------------------------
// Uri ToString (primitive): "scheme://host/path" for hierarchical and
// network-path URIs, "scheme:rest" (the verbatim opaque remainder) for
// opaque URIs. Because parsing keeps every part verbatim, the rebuild
// reproduces the original input exactly.
//------------------------------------------------------------------------------
std::string il2cpp_uri_to_string(const il2cpp_uri_t* uri)
{
    if (uri == nullptr)
    {
        return std::string();
    }
    std::string result;
    result.reserve(uri->scheme.size() + uri->opaque.size() + uri->host.size()
                   + uri->path.size() + 3);
    result += uri->scheme;
    if (uri->isOpaque)
    {
        result += ':';
        result += uri->opaque;
    }
    else
    {
        result += "://";
        result += uri->host;
        result += uri->path;
    }
    return result;
}

//------------------------------------------------------------------------------
// Uri object icalls (the object ABI owns the three string slots; the plain
// try_create/get_*/to_string functions above stay as the parse primitives).
//------------------------------------------------------------------------------

namespace {

Il2CppUriObject* AsUri(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppUriObject*>(self);
}

// Convert a managed string's UTF-16 payload into the ASCII text the parse
// primitive consumes. The Uri grammar is pure ASCII (this domain does no
// escape/Unicode handling -- documented simplification); the FIRST
// non-ASCII unit makes the whole input invalid (returns false -- the
// caller throws ArgumentException).
bool UriAsciiFromManaged(Il2CppString* input, std::string* out)
{
    out->clear();
    out->reserve(static_cast<size_t>(input->length));
    for (int32_t i = 0; i < input->length; ++i)
    {
        const char16_t u = input->chars[i];
        if (u > 0x7F)
        {
            return false;
        }
        out->push_back(static_cast<char>(u));
    }
    return true;
}

// net48's canonical message for an unparseable URI string.
constexpr const char* kInvalidUriMessage =
    "Invalid URI: The format of the URI could not be determined.";

// Append a stored managed part (ASCII by construction -- the ctor only
// stores strings derived from the ASCII parse) into the rebuilt text.
void AppendManagedPart(std::string* text, const Il2CppString* part)
{
    for (int32_t i = 0; i < part->length; ++i)
    {
        text->push_back(static_cast<char>(part->chars[i]));
    }
}

// UriKind carriers (System.UriKind: RelativeOrAbsolute=0, Absolute=1,
// Relative=2 -- net48 probe, URI-FIX P1).
constexpr int32_t kUriKindRelativeOrAbsolute = 0;
constexpr int32_t kUriKindAbsolute = 1;
constexpr int32_t kUriKindRelative = 2;

// Rebuild the absolute URI text from the stored slots (P1 verbatim form --
// identical to the old icall_uri_to_string rebuild until the C-series
// normalization lands in P3/P4). Single source for ToString, AbsoluteUri
// and Equals (C6: one rebuild path). Slots are null-checked so an
// un-ctor'd instance degrades to "" instead of faulting.
// Managed string -> ASCII std::string (null-safe; un-ctor'd slots read
// null -- GC-safe by construction; stored parts are ASCII by construction,
// the ctors reject non-ASCII before storing).
std::string ManagedToAscii(const Il2CppString* part)
{
    std::string text;
    if (part != nullptr)
    {
        text.reserve(static_cast<size_t>(part->length));
        for (int32_t i = 0; i < part->length; ++i)
        {
            text.push_back(static_cast<char>(part->chars[i]));
        }
    }
    return text;
}

// C1: ASCII lowercase in place (scheme + host proper).
void AsciiToLowerInPlace(std::string* s)
{
    for (std::size_t i = 0; i < s->size(); ++i)
    {
        char c = (*s)[i];
        if (c >= 'A' && c <= 'Z')
        {
            (*s)[i] = static_cast<char>(c + ('a' - 'A'));
        }
    }
}

// C1/C2/C5: normalize "[userinfo@]host-or-[v6][:port]" against the
// (already lowercased) scheme. userinfo keeps its verbatim case (net48
// treats it as case-sensitive); the host proper is lowercased (covers
// IPv6 hex); a numeric port is emitted in canonical digits (leading zeros
// stripped, C2) and omitted when it is the scheme default (C5:
// http/80, https/443, ftp/21). A non-numeric port or garbage after ']'
// keeps the whole authority verbatim (no vector -- minimal change).
// Bare multi-colon hosts (unbracketed IPv6) never split a port.
std::string NormalizeAuthority(const std::string& authority, const std::string& schemeLower)
{
    std::string userinfo;
    std::string hostPort = authority;
    const std::size_t at = authority.rfind('@');
    if (at != std::string::npos)
    {
        userinfo.assign(authority, 0, at + 1);
        hostPort.assign(authority, at + 1, std::string::npos);
    }
    std::string hostProper = hostPort;
    std::string port;
    bool hasPort = false;
    if (!hostPort.empty() && hostPort[0] == '[')
    {
        const std::size_t close = hostPort.find(']');
        if (close == std::string::npos)
        {
            return authority; // unbalanced -- verbatim
        }
        hostProper.assign(hostPort, 0, close + 1);
        if (close + 1 < hostPort.size())
        {
            if (hostPort[close + 1] != ':')
            {
                return authority; // garbage after ']' -- verbatim
            }
            port.assign(hostPort, close + 2, std::string::npos);
            hasPort = true;
        }
    }
    else
    {
        const std::size_t colon = hostPort.find(':');
        if (colon != std::string::npos
            && hostPort.find(':', colon + 1) == std::string::npos)
        {
            // Single colon: host:port split.
            hostProper.assign(hostPort, 0, colon);
            port.assign(hostPort, colon + 1, std::string::npos);
            hasPort = true;
        }
        // Else: plain host, or bare IPv6 (multi-colon) -- no port.
    }
    if (hasPort)
    {
        for (std::size_t i = 0; i < port.size(); ++i)
        {
            if (port[i] < '0' || port[i] > '9')
            {
                return authority; // non-numeric port -- verbatim
            }
        }
        const std::size_t nz = port.find_first_not_of('0');
        const std::string canon = (nz == std::string::npos) ? std::string("0") : port.substr(nz);
        const bool isDefault = (schemeLower == "http" && canon == "80")
            || (schemeLower == "https" && canon == "443")
            || (schemeLower == "ftp" && canon == "21");
        std::string out;
        out.reserve(userinfo.size() + hostProper.size() + canon.size() + 1);
        out += userinfo;
        std::string lowerHost = hostProper;
        AsciiToLowerInPlace(&lowerHost);
        out += lowerHost;
        if (!isDefault)
        {
            out += ':';
            out += canon;
        }
        return out;
    }
    std::string out;
    out.reserve(userinfo.size() + hostProper.size());
    out += userinfo;
    AsciiToLowerInPlace(&hostProper);
    out += hostProper;
    return out;
}

// P4 forward declarations (defined below BuildAbsoluteText -- C3 dot
// folding over the RAW path, C4 unescape of non-reserved "%XX").
std::string RemoveDotSegments(const std::string& path);
std::string UnescapePath(const std::string& path);
std::string CanonicalAbsolutePath(const std::string& raw);

// Rebuild the absolute URI text from the stored slots (URI-FIX P3
// normalized form -- single source for ToString, AbsoluteUri, Equals and
// GetHashCode (C6: one rebuild path). Slots are null-checked so an
// un-ctor'd instance degrades gracefully instead of faulting.
// P3 normalization (hierarchical only; opaque "scheme:rest" stays verbatim)
// plus P4 path canonicalization:
//   C1 scheme + host-proper ASCII-lowercased (userinfo verbatim),
//   C2/C5 numeric port canonicalized, scheme defaults omitted,
//   C3 RFC 3986 dot-segments folded over the RAW path ("%2E" never folds),
//   C4 non-reserved "%XX" unescaped (reserved "%XX" stays verbatim),
//   C6 empty path becomes "/",
//   C7 file scheme with an empty authority rebuilds "file:///path".
std::string BuildAbsoluteText(const Il2CppUriObject* u)
{
    std::string text;
    text.reserve(static_cast<size_t>(
        (u->scheme != nullptr ? u->scheme->length : 0)
        + (u->opaque != nullptr ? u->opaque->length : 0)
        + (u->host != nullptr ? u->host->length : 0)
        + (u->path != nullptr ? u->path->length : 0) + 4));
    if (u->opaque != nullptr)
    {
        // Opaque "scheme:rest": rebuild with the verbatim remainder (P3
        // leaves opaque untouched -- no vector, minimal change).
        if (u->scheme != nullptr)
        {
            AppendManagedPart(&text, u->scheme);
        }
        text += ':';
        AppendManagedPart(&text, u->opaque);
        return text;
    }
    std::string scheme = ManagedToAscii(u->scheme);
    AsciiToLowerInPlace(&scheme);
    text += scheme;
    text += "://";
    text += NormalizeAuthority(ManagedToAscii(u->host), scheme);
    std::string path = ManagedToAscii(u->path);
    if (path.empty())
    {
        path = "/"; // C6
    }
    else
    {
        // P4: dot-fold the RAW path first (C3 -- "%2E" never folds),
        // then unescape non-reserved "%XX" triples (C4).
        path = UnescapePath(RemoveDotSegments(path));
    }
    text += path;
    return text;
}

// C3: RFC 3986 section 5.2.4 dot-segment removal over the RAW path
// (runs BEFORE C4 unescaping, so encoded "%2E" never folds -- no vector,
// minimal change). "." segments drop; ".." pops the last normal segment
// (above-root ".." drops -- no vector, documented); a trailing "/." or
// "/.." yields a trailing "/" (RFC output rule).
std::string RemoveDotSegments(const std::string& path)
{
    std::vector<std::string> kept;
    std::size_t i = 0;
    const bool leadingSlash = !path.empty() && path[0] == '/';
    while (i <= path.size())
    {
        std::size_t slash = path.find('/', i);
        if (slash == std::string::npos)
        {
            slash = path.size();
        }
        const std::string seg = path.substr(i, slash - i);
        if (seg == "..")
        {
            // Pop the last NORMAL segment; at root (back is the "" root
            // slot) or against another "..", the ".." simply drops
            // (above-root -- no vector, documented).
            if (!kept.empty() && !kept.back().empty() && kept.back() != "..")
            {
                kept.pop_back();
            }
        }
        else if (seg == ".")
        {
            // Drop (but the trailing-slash rule below still applies).
        }
        else
        {
            kept.push_back(seg);
        }
        i = slash + 1;
    }
    std::string out;
    for (std::size_t k = 0; k < kept.size(); ++k)
    {
        if (k != 0)
        {
            out += '/';
        }
        out += kept[k];
    }
    if (leadingSlash && (out.empty() || out[0] != '/'))
    {
        out = "/" + out;
    }
    // Trailing "/." or "/.." (or bare "." / "..") leaves a trailing "/".
    if (path.size() >= 2 && path.compare(path.size() - 2, 2, "/.") == 0)
    {
        if (out.empty() || out[out.size() - 1] != '/')
        {
            out += '/';
        }
    }
    else if (path.size() >= 3 && path.compare(path.size() - 3, 3, "/..") == 0)
    {
        if (out.empty() || out[out.size() - 1] != '/')
        {
            out += '/';
        }
    }
    return out;
}
// C4: single-pass %XX unescape over the dot-folded path (runs AFTER C3,
// so encoded "%2E" never folds -- no vector, minimal change). A valid
// "%XX" triple decodes to the raw byte UNLESS the byte is '%' itself or
// an RFC 3986 reserved character (gen-delims ":/?#[]@" + sub-delims
// "!$&'()*+,;=") -- those stay "%XX" verbatim (original case preserved),
// so "%2F" never becomes a path separator. Invalid or trailing '%'
// stays verbatim; '+' is untouched (not a space in paths).
bool IsHexDigit(char c)
{
    return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
unsigned HexValue(char c)
{
    if (c >= '0' && c <= '9')
    {
        return static_cast<unsigned>(c - '0');
    }
    if (c >= 'a' && c <= 'f')
    {
        return static_cast<unsigned>(c - 'a') + 10u;
    }
    return static_cast<unsigned>(c - 'A') + 10u;
}
bool IsReservedByte(unsigned b)
{
    switch (static_cast<char>(b))
    {
        case ':': case '/': case '?': case '#': case '[': case ']': case '@':
        case '!': case '$': case '&': case '\'': case '(': case ')':
        case '*': case '+': case ',': case ';': case '=':
            return true;
        default:
            return false;
    }
}
std::string UnescapePath(const std::string& path)
{
    std::string out;
    out.reserve(path.size());
    std::size_t i = 0;
    while (i < path.size())
    {
        const char c = path[i];
        if (c == '%' && path.size() - i >= 3 && IsHexDigit(path[i + 1]) && IsHexDigit(path[i + 2]))
        {
            const unsigned b = HexValue(path[i + 1]) * 16u + HexValue(path[i + 2]);
            if (b == 0x25u || IsReservedByte(b))
            {
                out.append(path, i, 3); // keep "%XX" verbatim (case preserved)
            }
            else
            {
                out.push_back(static_cast<char>(b)); // raw byte (UTF-8 passes through)
            }
            i += 3;
        }
        else
        {
            out.push_back(c); // incl. invalid/trailing '%' and '+'
            ++i;
        }
    }
    return out;
}
// P4b: AbsolutePath normalization (oracle evidence or004f.txt: dots fold
// like ToString, but "%XX" stays escaped -- NO C4 here; C6 empty->"/"
// applies). Opaque/relative callers keep their verbatim slots (unchanged).
std::string CanonicalAbsolutePath(const std::string& raw)
{
    if (raw.empty())
    {
        return "/"; // C6
    }
    return RemoveDotSegments(raw); // C3 only -- escapes preserved
}
int32_t OrdinalFoldHash(const std::string& text)
{
    uint32_t h = 2166136261u;
    for (std::size_t i = 0; i < text.size(); ++i)
    {
        h ^= static_cast<uint8_t>(text[i]);
        h *= 16777619u;
    }
    return static_cast<int32_t>(h);
}

// Ordinal compare of two managed strings (null-safe; un-ctor'd slots read
// as null -- GC-safe by construction).
bool ManagedStringEquals(const Il2CppString* x, const Il2CppString* y)
{
    if (x == y)
    {
        return true;
    }
    if (x == nullptr || y == nullptr)
    {
        return false;
    }
    if (x->length != y->length)
    {
        return false;
    }
    for (int32_t i = 0; i < x->length; ++i)
    {
        if (x->chars[i] != y->chars[i])
        {
            return false;
        }
    }
    return true;
}

// Store a parsed ABSOLUTE uri plus the verbatim original text. Each string
// allocation below can trigger a compacting collection that MOVES the fresh
// Uri (only reachable through the caller's frame slot, which is rewritten):
// root 'self' and re-read the (possibly moved) object AFTER every
// allocation, then store into the re-rooted copy. The refFieldBitmap slots
// were zeroed by il2cpp_object_new, so a partially initialized object is
// GC-safe -- but a store to the moved-out OLD address would be silently
// lost (stackqueue2 e2e, 2026-08-13).
void StoreAbsoluteFromParsed(Il2CppObject* self, const il2cpp_uri_t& parsed, const char* originalAscii)
{
    GcRoot<Il2CppObject*> root(self, "StoreAbsoluteFromParsed");
    Il2CppString* scheme = il2cpp_string_new_from_utf8(parsed.scheme.c_str());
    self = root.Get();
    AsUri(self)->scheme = scheme;
    Il2CppString* host = il2cpp_string_new_from_utf8(parsed.host.c_str());
    self = root.Get();
    AsUri(self)->host = host;
    Il2CppString* path = il2cpp_string_new_from_utf8(parsed.path.c_str());
    self = root.Get();
    AsUri(self)->path = path;
    if (parsed.isOpaque)
    {
        Il2CppString* opaque = il2cpp_string_new_from_utf8(parsed.opaque.c_str());
        self = root.Get();
        AsUri(self)->opaque = opaque;
    }
    else
    {
        AsUri(self)->opaque = nullptr;
    }
    // Opaque (D-NETD2-1): the verbatim "scheme:" remainder, or null for
    // hierarchical/network-path URIs -- its null-ness discriminates the two
    // ToString rebuild forms.
    Il2CppString* original = il2cpp_string_new_from_utf8(originalAscii);
    self = root.Get();
    Il2CppUriObject* u = AsUri(self);
    u->original = original;
    u->isAbsolute = 1;
}

// Store a RELATIVE uri verbatim (UriKind.Relative -- net48:
// ToString()==OriginalString, no parsing and no normalization,
// IsAbsoluteUri==false). One shared empty string backs the three unused
// component slots (shared references are GC-safe).
void StoreRelativeVerbatim(Il2CppObject* self, const char* verbatimAscii)
{
    GcRoot<Il2CppObject*> root(self, "StoreRelativeVerbatim");
    Il2CppString* empty = il2cpp_string_new_from_utf8("");
    self = root.Get();
    Il2CppUriObject* u = AsUri(self);
    u->scheme = empty;
    u->host = empty;
    u->path = empty;
    u->opaque = nullptr;
    Il2CppString* original = il2cpp_string_new_from_utf8(verbatimAscii);
    self = root.Get();
    u = AsUri(self);
    u->original = original;
    u->isAbsolute = 0;
}

} // namespace

void icall_uri_ctor(Il2CppObject* self, Il2CppObject* input)
{
    ThrowIfNullReceiver(self);
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("uriString");
    }
    std::string ascii;
    if (!UriAsciiFromManaged(reinterpret_cast<Il2CppString*>(input), &ascii))
    {
        il2cpp_throw_argument_exception(kInvalidUriMessage);
    }
    il2cpp_uri_t parsed;
    if (!il2cpp_uri_try_create(ascii.c_str(), &parsed))
    {
        il2cpp_throw_argument_exception(kInvalidUriMessage);
    }
    StoreAbsoluteFromParsed(self, parsed, ascii.c_str());
}

// Uri(string, UriKind) (URI-FIX P1 -- B3): Relative stores verbatim;
// Absolute parses (failure -> ArgumentException -- net48 throws
// UriFormatException, recorded deviation); RelativeOrAbsolute parses when
// possible and otherwise degrades to verbatim relative storage. An
// undefined kind carrier is rejected like net48 (ArgumentException).
void icall_uri_ctor_2(Il2CppObject* self, Il2CppObject* input, int32_t kind)
{
    ThrowIfNullReceiver(self);
    if (input == nullptr)
    {
        il2cpp_throw_argument_null_exception("uriString");
    }
    if (kind != kUriKindRelativeOrAbsolute && kind != kUriKindAbsolute && kind != kUriKindRelative)
    {
        il2cpp_throw_argument_exception("Invalid UriKind value.");
    }
    std::string ascii;
    if (!UriAsciiFromManaged(reinterpret_cast<Il2CppString*>(input), &ascii))
    {
        il2cpp_throw_argument_exception(kInvalidUriMessage);
    }
    if (kind == kUriKindRelative)
    {
        StoreRelativeVerbatim(self, ascii.c_str());
        return;
    }
    il2cpp_uri_t parsed;
    if (il2cpp_uri_try_create(ascii.c_str(), &parsed))
    {
        StoreAbsoluteFromParsed(self, parsed, ascii.c_str());
        return;
    }
    if (kind == kUriKindRelativeOrAbsolute)
    {
        StoreRelativeVerbatim(self, ascii.c_str());
        return;
    }
    il2cpp_throw_argument_exception(kInvalidUriMessage);
}

Il2CppObject* icall_uri_get_scheme(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(AsUri(self)->scheme);
}

Il2CppObject* icall_uri_get_host(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(AsUri(self)->host);
}

Il2CppObject* icall_uri_get_path(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppUriObject* u = AsUri(self);
    if (u->isAbsolute == 0 || u->opaque != nullptr)
    {
        return reinterpret_cast<Il2CppObject*>(u->path); // verbatim (unchanged)
    }
    // P4b: hierarchical absolute path -- same C3 dot-folding and C6
    // empty-path rule as BuildAbsoluteText, but escapes stay verbatim
    // (net48 AbsolutePath keeps "%XX" -- oracle diffuri004).
    const std::string path = CanonicalAbsolutePath(ManagedToAscii(u->path));
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(path.c_str()));
}

Il2CppObject* icall_uri_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppUriObject* u = AsUri(self);
    // Relative verbatim storage (UriKind.Relative): net48 guarantees
    // ToString()==OriginalString with no normalization.
    if (u->isAbsolute == 0)
    {
        if (u->original == nullptr)
        {
            return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(""));
        }
        return reinterpret_cast<Il2CppObject*>(u->original);
    }
    const std::string text = BuildAbsoluteText(u);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

Il2CppObject* icall_uri_get_originalstring(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(AsUri(self)->original);
}

Il2CppObject* icall_uri_get_absoluteuri(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppUriObject* u = AsUri(self);
    if (u->isAbsolute == 0)
    {
        il2cpp_throw_invalid_operation_exception("This operation is not supported for a relative URI.");
    }
    const std::string text = BuildAbsoluteText(u);
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

bool icall_uri_get_is_absolute_uri(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsUri(self)->isAbsolute != 0;
}

// Equals(object) (URI-FIX P1, normalized P3/P4): relative vs absolute never
// equal; two relatives compare OriginalString ordinally (net48); two
// absolutes compare the normalized AbsoluteUri ordinally
// (C1/C2/C3/C4/C5/C6/C7).
bool icall_uri_equals(Il2CppObject* self, Il2CppObject* other)
{
    ThrowIfNullReceiver(self);
    if (other == nullptr)
    {
        return false;
    }
    if (!il2cpp_is_instance(other, &System_Uri_class))
    {
        return false;
    }
    const Il2CppUriObject* a = AsUri(self);
    const Il2CppUriObject* b = AsUri(other);
    if (a->isAbsolute != b->isAbsolute)
    {
        return false;
    }
    if (a->isAbsolute == 0)
    {
        return ManagedStringEquals(a->original, b->original);
    }
    return BuildAbsoluteText(a) == BuildAbsoluteText(b);
}

// GetHashCode() (URI-FIX P3 -- accepted deviation, see versionuri.h): the
// deterministic ordinal fold over the SAME text Equals compares, so
// Equals(a,b) implies GetHashCode(a)==GetHashCode(b) by construction.
int32_t icall_uri_get_hash_code(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppUriObject* u = AsUri(self);
    if (u->isAbsolute == 0)
    {
        return OrdinalFoldHash(ManagedToAscii(u->original));
    }
    return OrdinalFoldHash(BuildAbsoluteText(u));
}

}} // namespace il2cpp::runtime
