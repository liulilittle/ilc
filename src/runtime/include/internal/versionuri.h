#pragma once
//------------------------------------------------------------------------------
// versionuri.h -- System.Version + System.Uri runtime support (basic domain).
//
// These helpers implement the core parsing/formatting/comparison surface of
// the two managed types as plain C++ functions (no IL body of their own is
// involved; the transpiler maps the BCL members onto these entry points).
//
// SEMANTIC BASELINE: .NET Framework 4.8 (net48) -- the project's reference
// baseline for behaviour -- with documented simplifications, because this is
// a BASIC DOMAIN:
//
//   * Version: "major[.minor[.build[.revision]]]" -- 1 to 4 dot-separated
//     decimal segments are accepted (net48 requires 2..4 and trims
//     whitespace; here there is no trimming, and the segment count is
//     preserved verbatim by il2cpp_version_to_string, so parse/ToString
//     round-trip exactly -- net48 ToString behaves identically: it emits
//     build/revision whenever they are present, INCLUDING explicit zeros
//     (Version("1.2.0.0").ToString() == "1.2.0.0"), so the verbatim-count
//     rebuild matches net48 component-for-component).
//   * Version comparison follows the net48 CompareTo rule: components are
//     compared major, minor, build, revision; a MISSING trailing component
//     counts as -1, so (1,2) < (1,2,0,0) and (1,2,3) < (1,2,3,4).
//   * Uri (net48-loose grammar, D-NETD2-1): three forms --
//       (a) hierarchical "scheme://host[/path]" (the historical form),
//       (b) network-path "//host[/path]" -- net48 accepts these as a
//           FILE-scheme reference, so ILC parses them with scheme "file",
//           the same host/path split and the same verbatim round-trip,
//       (c) opaque "scheme:rest" (no "://") -- net48 accepts non-"://"
//           schemes (mailto:, urn:, custom:...); for the MAILTO scheme the
//           rest is split as "[userinfo@]host[/path]" (host = the text
//           after the LAST '@', path = from the first '/' on -- net48
//           reports Host=host / AbsolutePath=/path for mailto), for every
//           OTHER scheme the whole rest is the AbsolutePath and Host is
//           empty. The opaque text is stored verbatim so
//           il2cpp_uri_to_string rebuilds "scheme:rest" exactly.
//     NO escape handling, NO query/fragment splitting ("/p?a=1" stays one
//     path), NO port or character validation of the host, NO case
//     normalization (the original text is preserved so
//     il2cpp_uri_to_string round-trips). The scheme must be non-empty,
//     start with an ALPHA character, and be made of RFC 3986 scheme
//     characters (ALPHA / DIGIT / '+' / '-' / '.') after the first
//     position -- the FIRST character MUST be a letter (RFC 3986 scheme =
//     ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ); net48 rejects
//     digit/+/./- leading schemes such as "1http://x" with
//     UriFormatException); the hierarchical/network-path HOST must be
//     non-empty (so file://-style URIs with an empty authority are
//     rejected). Documented simplifications: multiple '@' in a mailto
//     rest is accepted (net48 throws), a single-letter "x:rest" is
//     accepted (net48 reads it as a DOS drive path and throws), and
//     net48's "//host" file-URI normalization (AbsolutePath "/" /
//     trailing slash) is NOT applied -- the verbatim path is kept.
//
// All functions follow the TryParse convention: they NEVER throw on
// malformed input -- invalid input is signalled by the return value.
//
// OBJECT MODEL -- System.Version + System.Uri (M6 object closures): each
// managed instance is a managed object allocated through its GLOBAL-scope
// class descriptor (System_Version_class / System_Uri_class, defined in
// versionuri.cpp -- the same contract as System_String_class in
// il2cpp_metadata.h, because generated isinst/castclass code references
// &System_Version_class / &System_Uri_class directly).
//
//   * Il2CppVersionObject: header + component count + 4 int32 components,
//     NO GC reference fields (refFieldBitmap 0).
//   * Il2CppUriObject: header + FIVE string-pointer slots (scheme/host/
//     path/opaque/original). These ARE GC references -- refFieldBitmap bits
//     1,2,3,4,5 -- so the compacting collector (gc.cpp, bitmap-driven scan)
//     keeps the strings alive and rewrites the slots on Gen0 compaction. The
//     Version object has no such requirement (int32 components are
//     roots-free). The FOURTH slot (opaque) holds the verbatim text after
//     "scheme:" for opaque "scheme:rest" URIs (D-NETD2-1) and is null for
//     hierarchical / network-path URIs -- icall_uri_to_string uses its
//     presence to decide whether to rebuild "scheme:rest" or
//     "scheme://host/path". The FIFTH slot (original) holds the verbatim
//     input text (OriginalString, URI-FIX P1). The trailing isAbsolute flag
//     (a plain int32, not a GC reference) discriminates parsed absolute
//     URIs (1) from relative verbatim storage (0, UriKind.Relative).
//
// The object icalls (il2cpp::runtime::icall_version_* / icall_uri_*) own the
// read/write ABI; the plain parse/to_string primitives above stay as the
// parse layer. Generated newobj allocates via
// il2cpp_object_new(&System_Version_class) / il2cpp_object_new(&System_Uri_class)
// -- the transpiler's BclAllocExpr fallback emits exactly these mangled
// global symbols.
//------------------------------------------------------------------------------

#include <cstdint>
#include <string>

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

// System.Version runtime descriptor (GLOBAL scope -- the transpiler emits
// &System_Version_class for newobj/isinst/castclass; defined in
// versionuri.cpp, registered lazily through il2cpp_version_class()).
extern il2cpp::Il2CppClass System_Version_class;

// System.Uri runtime descriptor (GLOBAL scope -- same contract as
// System_Version_class; defined in versionuri.cpp, registered lazily
// through il2cpp_uri_class()).
extern il2cpp::Il2CppClass System_Uri_class;

namespace il2cpp {

struct Il2CppString; // forward (il2cpp_string.h defines the full layout)

// Managed System.Version layout: object header + component count + the four
// components. Components beyond 'componentCount' are -1 (absent) -- so
// Version(2,0).Build == -1 and CompareTo's missing-trailing-component rule
// read the storage directly. There are NO reference fields: the collector
// never scans past the header.
struct Il2CppVersionObject
{
    Il2CppObject obj;       // object header (klass pointer, offset 0)
    int32_t componentCount; // 1..4 (2 for the default ctor); 0 = uninitialized
    int32_t components[4];  // major, minor, build, revision (offset 12)
};

// The System.Version descriptor (lazily registered on first use; generated
// code allocates through &System_Version_class directly -- see above).
Il2CppClass* il2cpp_version_class();

// Managed System.Uri layout: object header + four managed-string pointers
// (the parsed scheme/host/path + the opaque rest). All four are GC
// REFERENCES -- the class descriptor's refFieldBitmap has bits 1,2,3,4 set,
// so the collector scans them and rewrites them on Gen0 compaction. The
// slots are written only by icall_uri_ctor; il2cpp_object_new
// zero-initializes, so an un-ctor'd instance reads as null in every slot
// (GC-safe). The opaque slot is null for hierarchical/network-path URIs and
// a (possibly empty) string for opaque "scheme:rest" URIs -- its presence
// discriminates the two ToString rebuild forms.
struct Il2CppUriObject
{
    Il2CppObject obj;    // object header (klass pointer, offset 0)
    Il2CppString* scheme; // slot 1 (offset 8) -- GC reference
    Il2CppString* host;   // slot 2 (offset 16) -- GC reference
    Il2CppString* path;   // slot 3 (offset 24) -- GC reference; "" when none
    Il2CppString* opaque; // slot 4 (offset 32) -- GC reference; null for
                          // hierarchical/network-path URIs, the verbatim
                          // "scheme:" remainder for opaque URIs (D-NETD2-1)
    Il2CppString* original; // slot 5 (offset 40) -- GC reference; the verbatim
                          // input text (OriginalString -- net48 returns the
                          // exact user string; URI-FIX P1)
    int32_t isAbsolute;   // (offset 48) -- 1 = parsed absolute URI, 0 =
                          // relative verbatim storage (UriKind.Relative or a
                          // RelativeOrAbsolute fallback; ToString() returns
                          // the original text with no normalization)
};

// The System.Uri descriptor (lazily registered on first use; generated code
// allocates through &System_Uri_class directly -- see above).
Il2CppClass* il2cpp_uri_class();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

//------------------------------------------------------------------------------
// Version
//------------------------------------------------------------------------------

// Parse "1.2.3.4" (1-4 dot-separated decimal segments, each 0..INT32_MAX).
// Returns the number of segments parsed (1..4), writing them into
// components[0..count-1]. Returns 0 -- leaving components untouched -- when
// 'text' or 'components' is null, text is empty, a segment is empty or
// non-decimal (incl. signs), a segment exceeds INT32_MAX, or more than 4
// segments are present. Leading zeros are accepted ("01" -> 1).
int32_t il2cpp_version_parse(const char* text, int32_t* components);

// Rebuild "1.2.3.4" from components[0..count-1] (count 1..4; count > 4 is
// clamped to 4). Returns "" when 'components' is null or count < 1.
std::string il2cpp_version_to_string(const int32_t* components, int32_t count);

// Compare two versions: -1 / 0 / 1. net48 CompareTo rule: missing trailing
// components count as -1 (so (1,2) < (1,2,0,0)). Null 'a' or 'b' is
// treated as equal (defensive, documented -- callers always pass real
// buffers).
int32_t il2cpp_version_compare(const int32_t* a, int32_t aCount,
                               const int32_t* b, int32_t bCount);

//------------------------------------------------------------------------------
// Version object icalls (see the OBJECT MODEL block at the top).
//------------------------------------------------------------------------------

// Version(string input): parses "major[.minor[.build[.revision]]]" (the
// il2cpp_version_parse grammar) and stores the components in the receiver.
// Null input -> ArgumentNullException; malformed input -> ArgumentException
// (net48 baseline -- the ctor THROWS, it does not return a parse status).
void icall_version_ctor(Il2CppObject* self, Il2CppObject* input);

// Version(): the default instance -- componentCount 2, components
// {0,0,-1,-1} (ToString "0.0", Build/Revision -1 -- net48 baseline).
void icall_version_ctor_default(Il2CppObject* self);

// Version(int major, int minor[, int build[, int revision]]): component
// ctors; any component < 0 -> ArgumentOutOfRangeException (net48).
void icall_version_ctor_2(Il2CppObject* self, int32_t major, int32_t minor);
void icall_version_ctor_3(Il2CppObject* self, int32_t major, int32_t minor,
                          int32_t build);
void icall_version_ctor_4(Il2CppObject* self, int32_t major, int32_t minor,
                          int32_t build, int32_t revision);

// Component getters: the stored value, or -1 when the component is absent
// (componentCount rules: build absent for < 3 components, revision for < 4).
int32_t icall_version_get_major(Il2CppObject* self);
int32_t icall_version_get_minor(Il2CppObject* self);
int32_t icall_version_get_build(Il2CppObject* self);
int32_t icall_version_get_revision(Il2CppObject* self);
// The 16-bit halves of Revision (net48 contract; see versionuri.cpp).
int32_t icall_version_get_major_revision(Il2CppObject* self);
int32_t icall_version_get_minor_revision(Il2CppObject* self);
// GetHashCode: the deterministic component fold (same-value -> same-hash).
int32_t icall_version_get_hash_code(Il2CppObject* self);

// ToString(): rebuilds "major.minor[.build[.revision]]" from the stored
// components (the count is preserved verbatim -- the domain round-trip rule).
// Returns the managed string as Il2CppObject* (the IR's reference shape).
Il2CppObject* icall_version_to_string(Il2CppObject* self);

// ToString(int fieldCount) (net48 version.cs:284): rebuilds the first
// 'fieldCount' components -- "" (0), major (1), "major.minor" (2),
// "major.minor.build" (3), "major.minor.build.revision" (4). A requested
// component that is ABSENT throws ArgumentException (fieldCount >= 3 with
// build == -1; fieldCount >= 4 with revision == -1) and any fieldCount > 4
// throws ArgumentException too. Returns the managed string as Il2CppObject*.
Il2CppObject* icall_version_to_string_field(Il2CppObject* self, int32_t fieldCount);

// CompareTo(object): -1 / 0 / 1 with the net48 rule (missing trailing
// component counts as -1). Null 'other' -> 1 (net48); a non-Version object
// -> ArgumentException.
int32_t icall_version_compare(Il2CppObject* self, Il2CppObject* other);

// Equals(object): component equality; null or a non-Version object -> false.
bool icall_version_equals(Il2CppObject* self, Il2CppObject* other);

// op_Equality / op_Inequality (static, two Version operands): both null
// equal, exactly one null unequal; otherwise component equality.
bool icall_version_op_equality(Il2CppObject* a, Il2CppObject* b);
bool icall_version_op_inequality(Il2CppObject* a, Il2CppObject* b);

// The relational operators (static, two Version operands): null compares as
// LESS than any non-null version; null vs null is equal.
bool icall_version_op_less_than(Il2CppObject* a, Il2CppObject* b);
bool icall_version_op_greater_than(Il2CppObject* a, Il2CppObject* b);
bool icall_version_op_less_or_equal(Il2CppObject* a, Il2CppObject* b);
bool icall_version_op_greater_or_equal(Il2CppObject* a, Il2CppObject* b);

// Parse(string) -- static: allocates a Version object through
// System_Version_class and returns it. Null input -> ArgumentNullException;
// malformed input -> ArgumentException.
Il2CppObject* icall_version_parse(Il2CppObject* input);

//------------------------------------------------------------------------------
// Uri
//------------------------------------------------------------------------------

// Value-type handle: no allocation, no free needed. Copyable.
struct il2cpp_uri_t
{
    std::string scheme;
    std::string host;
    std::string path;   // includes the leading '/'; "" when none was present
    bool isOpaque;      // "scheme:rest" (no "//") form (D-NETD2-1)
    std::string opaque; // the verbatim text after "scheme:" when isOpaque;
                        // "" for hierarchical / network-path URIs
};

// Parse one of the three net48-accepted forms (D-NETD2-1):
//   "scheme://host[/path]"   hierarchical (the historical grammar),
//   "//host[/path]"          network-path reference -> scheme "file",
//   "scheme:rest"            opaque -- for scheme "mailto" the rest is split
//                            as "[userinfo@]host[/path]" (host = after the
//                            LAST '@', path = from the first '/' on), for
//                            every other scheme host is "" and the whole
//                            rest is the path.
// Returns 1 and fills 'out' on success; returns 0 -- leaving 'out'
// untouched -- when 'text' or 'out' is null, the input is a relative form
// (no "//" prefix, no ":" separator), the scheme is empty, its first
// character is not ALPHA, it contains a character outside RFC 3986 scheme
// chars (ALPHA / DIGIT / '+' / '-' / '.'), or the hierarchical/network-path
// host is empty.
int32_t il2cpp_uri_try_create(const char* text, il2cpp_uri_t* out);

// Getters: point into the uri struct (valid while 'uri' lives).
// A null 'uri' yields the empty string "" (defensive).
const char* il2cpp_uri_get_scheme(const il2cpp_uri_t* uri);
const char* il2cpp_uri_get_host(const il2cpp_uri_t* uri);
const char* il2cpp_uri_get_path(const il2cpp_uri_t* uri);

// Rebuild the URI text exactly as parsed (round-trips the original input):
// "scheme://host/path" for hierarchical/network-path URIs, "scheme:rest"
// (the verbatim opaque remainder) for opaque URIs. Returns "" for a null
// 'uri'.
std::string il2cpp_uri_to_string(const il2cpp_uri_t* uri);

//------------------------------------------------------------------------------
// Uri object icalls (see the OBJECT MODEL block at the top -- the receiver is
// an Il2CppUriObject allocated through System_Uri_class; the five string
// slots are GC references scanned through refFieldBitmap bits 1..5, and the
// trailing isAbsolute flag is a plain int32).
//------------------------------------------------------------------------------

// Uri(string uriString): parses the three-form net48-loose grammar (the
// il2cpp_uri_try_create grammar -- hierarchical "scheme://host[/path]",
// network-path "//host[/path]" -> file, opaque "scheme:rest") and stores
// the parts as managed strings in the receiver's scheme/host/path/opaque
// slots. Null input -> ArgumentNullException; malformed input (relative
// form, empty/bad scheme, empty hierarchical/network-path host, non-ASCII
// text) -> ArgumentException (net48 baseline -- the ctor THROWS, it does
// not return a parse status).
void icall_uri_ctor(Il2CppObject* self, Il2CppObject* input);

// Getters: scheme/host return the stored part as a managed string
// (Il2CppObject* -- the IR's reference shape; the slot was created by the
// ctor, so it is never null once the object is initialized). The path
// getter (AbsolutePath, P4b) returns the C3/C6-normalized path for
// hierarchical absolute URIs (dot-folded, empty->"/", escapes preserved);
// opaque and relative URIs keep the verbatim stored path.
Il2CppObject* icall_uri_get_scheme(Il2CppObject* self);
Il2CppObject* icall_uri_get_host(Il2CppObject* self);
Il2CppObject* icall_uri_get_path(Il2CppObject* self);

// ToString(): rebuilds "scheme://host/path" (hierarchical/network-path) or
// "scheme:rest" (opaque -- the verbatim remainder) from the stored slots
// (the domain round-trip rule). Relative verbatim storage (isAbsolute == 0)
// returns the original text unchanged (net48 ToString()==OriginalString).
// Returns the managed string as Il2CppObject* (the IR's reference shape).
Il2CppObject* icall_uri_to_string(Il2CppObject* self);

// Uri(string uriString, UriKind kind) (URI-FIX P1): kind 2 (Relative)
// stores the input verbatim (no parsing -- ToString()==OriginalString,
// IsAbsoluteUri==false); kind 1 (Absolute) parses exactly like
// icall_uri_ctor; kind 0 (RelativeOrAbsolute) parses when possible and
// otherwise falls back to verbatim relative storage. Null input ->
// ArgumentNullException; absolute-parse failure -> ArgumentException
// (net48 throws UriFormatException -- recorded deviation, URI-FIX P1).
void icall_uri_ctor_2(Il2CppObject* self, Il2CppObject* input, int32_t kind);

// Uri(Uri baseUri, string relativeUri) (net48 System_net_System_URI.cs:452):
// resolve the relative reference against the ABSOLUTE base. An absolute
// relative reference is stored verbatim; a relative one combines the base
// scheme/host with the base directory (the base path up to and including its
// last '/') plus the relative text (a leading '/' makes it root-relative).
// A null base/relative -> ArgumentNullException; a relative base or an
// unparseable relative -> ArgumentException (net48 throws InvalidOperationException
// for a relative base -- recorded deviation).
void icall_uri_ctor_base_s(Il2CppObject* self, Il2CppObject* baseUri, Il2CppObject* relative);

// Uri(Uri baseUri, Uri relativeUri) (net48 System_net_System_URI.cs:504): the
// same combination over the relative Uri's stored absolute form (its
// OriginalString when relative, its rebuilt absolute form when absolute). The
// relative Uri is read through the object slots (never null once constructed).
void icall_uri_ctor_base_u(Il2CppObject* self, Il2CppObject* baseUri, Il2CppObject* relativeUri);

// OriginalString: the verbatim input text (net48 returns the exact user
// string). Never null once the object is initialized.
Il2CppObject* icall_uri_get_originalstring(Il2CppObject* self);

// AbsoluteUri (URI-FIX P1): rebuilds the absolute form from the stored
// slots -- verbatim in P1 (identical to ToString until the C-series
// normalization lands in P3/P4). Throws InvalidOperationException on
// relative URIs (net48).
Il2CppObject* icall_uri_get_absoluteuri(Il2CppObject* self);

// IsAbsoluteUri: true for parsed absolute URIs, false for relative
// verbatim storage.
bool icall_uri_get_is_absolute_uri(Il2CppObject* self);

// Equals(object) (URI-FIX P1, normalized P3): relative vs absolute never
// equal; two relatives compare OriginalString ordinally (net48); two
// absolutes compare the normalized AbsoluteUri ordinally (C1/C2/C5/C6/C7;
// dot-folding/escapes arrive with P4 -- recorded deviation).
bool icall_uri_equals(Il2CppObject* self, Il2CppObject* other);

// GetHashCode() (URI-FIX P3; INTS MADE NET48-EXACT 2026-11-01). The earlier
// "accepted deviation" note claimed net48's NLS mixing was unreproducible and
// that the oracle integer "is never asserted" -- BOTH claims were false:
// tests/cases/diffuri001/diffuri001.cs:45 asserts the integer, and net48's
// integer IS reproducible because it runs on the SAME NLS layer.
//
// Measured (net48 probe, 2026-11-01; pinned by tests/runtime/test_urihash.cpp):
//   Uri.GetHashCode() == StringComparer.InvariantCultureIgnoreCase.GetHashCode(
//       GetComponents(UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped))
// i.e. the INVARIANT culture (NOT CurrentCulture -- a tr-TR CurrentCulture
// leaves the integer unchanged), ignore-case hash of net48's own
// HttpRequestUrl projection: fragment and userinfo dropped, scheme and host
// lowercased, default port dropped, the path unescaped BEFORE dot-folding
// (so "%2E" DOES fold here, unlike the AbsoluteUri rebuild), SafeUnescaped's
// restricted decode set with original escape casing preserved, and mailto's
// userinfo dropped. The underlying culture string hash is the NLS sort-key
// byte fold implemented by il2cpp_culture_hash (valuesemantics.cpp).
// A relative Uri hashes its OriginalString the same way, so the .NET
// Equals=>equal-hash contract still holds.
int32_t icall_uri_get_hash_code(Il2CppObject* self);

}} // namespace il2cpp::runtime
