#include "versionuri.h"

#include "il2cpp_exception_ext.h" // il2cpp_throw_argument_* / null_reference
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"       // il2cpp_object_new / il2cpp_class_register /
                                  // il2cpp_is_instance
#include "il2cpp_string.h"        // Il2CppString / il2cpp_string_new_from_utf8

#include <cstring>
#include <utility>

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
// System_Version_class above). instanceSize covers the header + 4 string
// pointers; slots 1..4 (scheme/host/path/opaque) ARE GC references, so
// refFieldBitmap bits 1..4 are set -- the compacting collector scans them
// and rewrites the slots on Gen0 compaction (the Version object has no such
// requirement -- its components are int32, not references). The fourth slot
// (opaque) is the verbatim "scheme:" remainder for opaque "scheme:rest"
// URIs (D-NETD2-1); null for hierarchical/network-path URIs.
//------------------------------------------------------------------------------
il2cpp::Il2CppClass System_Uri_class = {
    "System.Uri",                              // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppUriObject)), // instanceSize: header + 4 string pointers
    0b11110u,                                  // refFieldBitmap: bits 1,2,3,4 = scheme, host, path, opaque
    4,                                         // refFieldCount: four reference fields
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
            return 0; // empty authority (e.g. "http:///path" or "http://")
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
    Il2CppUriObject* u = AsUri(self);
    // Each string allocation below can trigger a compacting collection that
    // MOVES the fresh Uri (only reachable through the caller's frame slot,
    // which is rewritten): root 'self' and re-read the (possibly moved)
    // object AFTER every allocation, then store into the re-rooted copy. The
    // refFieldBitmap slots were zeroed by il2cpp_object_new, so a partially
    // initialized object is GC-safe -- but a store to the moved-out OLD
    // address would be silently lost (stackqueue2 e2e, 2026-08-13).
    GcRoot<Il2CppObject*> root(self, "icall_uri_ctor");
    Il2CppString* scheme = il2cpp_string_new_from_utf8(parsed.scheme.c_str());
    self = root.Get();
    u = AsUri(self);
    u->scheme = scheme;
    Il2CppString* host = il2cpp_string_new_from_utf8(parsed.host.c_str());
    self = root.Get();
    u = AsUri(self);
    u->host = host;
    Il2CppString* path = il2cpp_string_new_from_utf8(parsed.path.c_str());
    self = root.Get();
    u = AsUri(self);
    u->path = path;
    // Opaque (D-NETD2-1): the verbatim "scheme:" remainder, or null for
    // hierarchical/network-path URIs -- its null-ness discriminates the two
    // ToString rebuild forms.
    if (parsed.isOpaque)
    {
        Il2CppString* opaque = il2cpp_string_new_from_utf8(parsed.opaque.c_str());
        self = root.Get();
        u = AsUri(self);
        u->opaque = opaque;
    }
    else
    {
        u->opaque = nullptr;
    }
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
    return reinterpret_cast<Il2CppObject*>(AsUri(self)->path);
}

Il2CppObject* icall_uri_to_string(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    const Il2CppUriObject* u = AsUri(self);
    std::string text;
    text.reserve(static_cast<size_t>(u->scheme->length
                                     + (u->opaque != nullptr ? u->opaque->length : 0)
                                     + u->host->length + u->path->length + 3));
    AppendManagedPart(&text, u->scheme);
    if (u->opaque != nullptr)
    {
        // Opaque "scheme:rest": rebuild with the verbatim remainder.
        text += ':';
        AppendManagedPart(&text, u->opaque);
    }
    else
    {
        text += "://";
        AppendManagedPart(&text, u->host);
        AppendManagedPart(&text, u->path);
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8(text.c_str()));
}

}} // namespace il2cpp::runtime
