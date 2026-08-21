//------------------------------------------------------------------------------
// test_versionobj.cpp -- System.Version OBJECT icall tests (M6 G6 object
// closure: System_Version_class descriptor + icall_version_* object icalls).
//
// Coverage (normal + edge + error paths), net48 oracle expectations
// (tests/cases/versioncase/expected.txt -- all probes print True):
//   1. Version(string) ctor -> component getters (1.2.3.4 -> 1/2/3/4).
//   2. ToString round-trips: "1.2.3.4", "10.20.30", "2.0" (segment count
//      preserved verbatim -- the domain round-trip rule).
//   3. Version() default ctor -> "0.0", Build/Revision -1.
//   4. Component ctors (2/3/4 components): getters + absent-component -1
//      (Version(2,0).Build == -1, Revision == -1 -- net48).
//   5. CompareTo: equal 0 / greater 1 / less -1 + the missing-component
//      ordering rule ((2,0) > (1,9)).
//   6. op_Equality / op_Inequality: value equality, both-null equal,
//      one-null unequal (net48 operator rules).
//   7. Equals(object): equal true, different false, null false, a
//      non-Version object false.
//   8. Parse(string): success -> object with the parsed components; null ->
//      ArgumentNullException; "abc" / "1.2.3.4.5" -> ArgumentException.
//   9. ctor(string) malformed -> ArgumentException; ctor(1,-1) ->
//      ArgumentOutOfRangeException.
//   10. Descriptor identity: obj->klass == &System_Version_class,
//       il2cpp_version_class() == &System_Version_class, isinst against
//       System.Object walks the baseClass chain, instanceSize ==
//       sizeof(Il2CppVersionObject).
//
// System.Uri object closure (same session, M6 G6): System_Uri_class
// descriptor + icall_uri_ctor/get_scheme/get_host/get_path/to_string:
//   11. Descriptor identity + layout (instanceSize, refFieldBitmap
//       bits 1..4 -- the four string slots ARE GC references; the fourth
//       is the opaque "scheme:" remainder of D-NETD2-1 opaque URIs).
//   12. ctor(string) -> getters (scheme/host/path, verbatim).
//   13. ToString round-trips (with/without path, ftp/file, port-in-host).
//   13b. D-NETD2-1: network-path "//host/path" -> file; mailto opaque
//       ([userinfo@]host[/path] host/path split) and non-mailto opaque
//       (whole rest = path) round-trip the original text.
//   14. Un-ctor'd instance reads null slots (zero-init).
//   15. ctor error paths: null -> ArgumentNullException; no "://", empty
//       host, illegal scheme char, non-ASCII -> ArgumentException.
//   16. Null receiver guards -> NullReferenceException.
//   17. GC survival: the slot strings survive a full collection and the
//       Gen0 compaction REWRITES the slots -- proves the refFieldBitmap
//       bits 1..3 root contract.
//   18. D-NETD2-1: the opaque slot (bit 4) survives the same GC stress and
//       the mailto ToString round-trip still holds after compaction.
//
// Plain executable + minimal assertion harness (see test_object.cpp).
//------------------------------------------------------------------------------

#include "versionuri.h"

#include "il2cpp_exception.h"  // DotNetException carrier
#include "il2cpp_gc.h"         // gc_collect / add_static_root / get_generation
#include "il2cpp_object.h"     // il2cpp_is_instance is in il2cpp_runtime.h
#include "il2cpp_runtime.h"    // il2cpp_is_instance / il2cpp_class_by_name
#include "il2cpp_string.h"     // il2cpp_string_new_from_cs

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

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

// Run 'fn' and report whether it threw a DotNetException whose type string
// (with base chain) exactly matches 'expectedChain'.
template <typename Fn>
bool ThrowsDotNet(Fn&& fn, const char* expectedChain)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return std::strcmp(e.type_name(), expectedChain) == 0;
    }
    catch (...)
    {
        return false;
    }
    return false;
}

const char* kArgExceptionChain =
    "System.ArgumentException,System.ArgumentException,System.SystemException,System.Exception";
const char* kArgNullExceptionChain =
    "System.ArgumentNullException,System.ArgumentNullException,System.ArgumentException,System.SystemException,System.Exception";
const char* kArgOutOfRangeChain =
    "System.ArgumentOutOfRangeException,System.ArgumentOutOfRangeException,System.ArgumentException,System.SystemException,System.Exception";
const char* kNullRefChain =
    "System.NullReferenceException,System.NullReferenceException,System.SystemException,System.Exception";

// Convert a managed string (Il2CppObject* slot) to ASCII text for
// comparison -- the Version surface is pure ASCII.
std::string ManagedText(il2cpp::Il2CppObject* s)
{
    const auto* str = reinterpret_cast<il2cpp::Il2CppString*>(s);
    std::string text;
    for (int32_t i = 0; i < str->length; ++i)
    {
        text.push_back(static_cast<char>(str->chars[i]));
    }
    return text;
}

il2cpp::Il2CppObject* VersionFrom(const char* text)
{
    using namespace il2cpp;
    Il2CppVersionObject* v = reinterpret_cast<Il2CppVersionObject*>(
        il2cpp_object_new(&System_Version_class));
    runtime::icall_version_ctor(reinterpret_cast<Il2CppObject*>(v),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(text)));
    return reinterpret_cast<Il2CppObject*>(v);
}

il2cpp::Il2CppObject* UriFrom(const char* text)
{
    using namespace il2cpp;
    Il2CppUriObject* u = reinterpret_cast<Il2CppUriObject*>(
        il2cpp_object_new(&System_Uri_class));
    runtime::icall_uri_ctor(reinterpret_cast<Il2CppObject*>(u),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(text)));
    return reinterpret_cast<Il2CppObject*>(u);
}

} // namespace

int main()
{
    using namespace il2cpp;
    using namespace il2cpp::runtime;

    // --- descriptor identity + layout ---
    Expect(il2cpp_version_class() == &System_Version_class, "il2cpp_version_class() returns &System_Version_class");
    Expect(System_Version_class.instanceSize == sizeof(Il2CppVersionObject), "System_Version_class.instanceSize == sizeof(Il2CppVersionObject)");
    Expect(il2cpp_class_by_name("System.Version") == &System_Version_class, "System.Version resolves by name (registered)");

    // --- 1. ctor(string) + getters (net48 oracle: 1.2.3.4 -> 1/2/3/4) ---
    Il2CppObject* v = VersionFrom("1.2.3.4");
    Expect(v->klass == &System_Version_class, "Version object klass stamped");
    Expect(il2cpp_is_instance(v, &System_Object_class), "a Version IS-A System.Object (baseClass chain)");
    Expect(icall_version_get_major(v) == 1, "get_Major == 1");
    Expect(icall_version_get_minor(v) == 2, "get_Minor == 2");
    Expect(icall_version_get_build(v) == 3, "get_Build == 3");
    Expect(icall_version_get_revision(v) == 4, "get_Revision == 4");

    // --- 2. ToString round-trips (count preserved verbatim) ---
    Expect(ManagedText(icall_version_to_string(v)) == "1.2.3.4", "ToString(1.2.3.4) round-trips");
    Expect(ManagedText(icall_version_to_string(VersionFrom("10.20.30"))) == "10.20.30", "ToString(10.20.30) round-trips");
    Expect(ManagedText(icall_version_to_string(VersionFrom("2.0"))) == "2.0", "ToString(2.0) round-trips");

    // --- 3. default ctor -> "0.0", Build/Revision -1 ---
    Il2CppObject* def = reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class));
    icall_version_ctor_default(def);
    Expect(ManagedText(icall_version_to_string(def)) == "0.0", "Version().ToString() == 0.0");
    Expect(icall_version_get_major(def) == 0 && icall_version_get_minor(def) == 0, "Version() major/minor 0");
    Expect(icall_version_get_build(def) == -1 && icall_version_get_revision(def) == -1, "Version() build/revision -1");

    // --- 4. component ctors + absent-component -1 ---
    Il2CppObject* v20 = reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class));
    icall_version_ctor_2(v20, 2, 0);
    Expect(ManagedText(icall_version_to_string(v20)) == "2.0", "Version(2,0).ToString() == 2.0");
    Expect(icall_version_get_build(v20) == -1, "Version(2,0).Build == -1");
    Expect(icall_version_get_revision(v20) == -1, "Version(2,0).Revision == -1");

    Il2CppObject* v312 = reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class));
    icall_version_ctor_3(v312, 3, 1, 2);
    Expect(ManagedText(icall_version_to_string(v312)) == "3.1.2", "Version(3,1,2).ToString() == 3.1.2");
    Expect(icall_version_get_build(v312) == 2, "Version(3,1,2).Build == 2");
    Expect(icall_version_get_revision(v312) == -1, "Version(3,1,2).Revision == -1");

    Il2CppObject* v1234 = reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class));
    icall_version_ctor_4(v1234, 1, 2, 3, 4);
    Expect(ManagedText(icall_version_to_string(v1234)) == "1.2.3.4", "Version(1,2,3,4).ToString() == 1.2.3.4");

    // --- 5. CompareTo: 0 / 1 / -1 + missing-component ordering ---
    Expect(icall_version_compare(v, VersionFrom("1.2.3.4")) == 0, "CompareTo equal == 0");
    Expect(icall_version_compare(v, VersionFrom("1.2.3.5")) < 0, "CompareTo less == -1");
    Expect(icall_version_compare(v, VersionFrom("1.2.3.3")) > 0, "CompareTo greater == 1");
    Expect(icall_version_compare(v20, VersionFrom("1.9")) > 0, "Version(2,0) > Version(1,9)");
    Expect(icall_version_compare(VersionFrom("1.9"), v20) < 0, "Version(1,9) < Version(2,0)");
    Expect(icall_version_compare(VersionFrom("1.2"), VersionFrom("1.2.0.0")) < 0, "(1,2) < (1,2,0,0) missing-component rule");
    Expect(icall_version_compare(v, nullptr) == 1, "CompareTo(null) == 1 (net48)");

    // --- 6. op_Equality / op_Inequality (incl. null rules) ---
    Expect(icall_version_op_equality(v, VersionFrom("1.2.3.4")), "v == Version(1.2.3.4)");
    Expect(!icall_version_op_equality(v, VersionFrom("1.2.3.5")), "v != Version(1.2.3.5)");
    Expect(icall_version_op_inequality(v, VersionFrom("1.2.3.5")), "v != Version(1.2.3.5) (operator)");
    Expect(!icall_version_op_inequality(v, VersionFrom("1.2.3.4")), "!(v != Version(1.2.3.4))");
    Expect(icall_version_op_equality(nullptr, nullptr), "null == null");
    Expect(!icall_version_op_equality(v, nullptr), "v != null");
    Expect(!icall_version_op_equality(nullptr, v), "null != v");

    // --- 7. Equals(object) ---
    Expect(icall_version_equals(v, VersionFrom("1.2.3.4")), "Equals(equal) true");
    Expect(!icall_version_equals(v, VersionFrom("1.2.3.5")), "Equals(different) false");
    Expect(!icall_version_equals(v, nullptr), "Equals(null) false");
    Il2CppObject* notVersion = reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("1.2.3.4"));
    Expect(!icall_version_equals(v, notVersion), "Equals(non-Version) false");
    Expect(!icall_version_op_equality(v, notVersion), "op_Equality(non-Version) false");

    // --- 8. Parse(string) ---
    Il2CppObject* parsed = icall_version_parse(
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("7.8.9")));
    Expect(parsed->klass == &System_Version_class, "Parse allocates a System.Version object");
    Expect(icall_version_get_major(parsed) == 7 && icall_version_get_minor(parsed) == 8
        && icall_version_get_build(parsed) == 9, "Parse components 7/8/9");
    Expect(ManagedText(icall_version_to_string(parsed)) == "7.8.9", "Parse ToString round-trips");
    Expect(ThrowsDotNet([&] { icall_version_parse(nullptr); }, kArgNullExceptionChain),
        "Parse(null) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_version_parse(reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("abc"))); }, kArgExceptionChain),
        "Parse(abc) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_version_parse(reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("1.2.3.4.5"))); }, kArgExceptionChain),
        "Parse(1.2.3.4.5) throws ArgumentException");

    // --- 9. ctor error paths ---
    Expect(ThrowsDotNet([&] { icall_version_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)), nullptr); }, kArgNullExceptionChain),
        "Version((string)null) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_version_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("abc"))); }, kArgExceptionChain),
        "Version(abc) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_version_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("1.2.3.4.5"))); }, kArgExceptionChain),
        "Version(1.2.3.4.5) throws ArgumentException");
    Expect(ThrowsDotNet([&] { icall_version_ctor_2(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)), 1, -1); }, kArgOutOfRangeChain),
        "Version(1,-1) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_version_ctor_4(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)), 1, 2, -3, 4); }, kArgOutOfRangeChain),
        "Version(1,2,-3,4) throws ArgumentOutOfRangeException");
    Expect(ThrowsDotNet([&] { icall_version_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Version_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("2147483648"))); }, kArgExceptionChain),
        "Version(2147483648) throws ArgumentException (int32 overflow)");

    // --- 10. null receiver guards ---
    Expect(ThrowsDotNet([&] { icall_version_get_major(nullptr); }, kNullRefChain),
        "null receiver get_Major throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_version_to_string(nullptr); }, kNullRefChain),
        "null receiver ToString throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_version_compare(nullptr, v); }, kNullRefChain),
        "null receiver CompareTo throws NullReferenceException");

    //--------------------------------------------------------------------------
    // System.Uri OBJECT closure (M6 G6): System_Uri_class descriptor +
    // icall_uri_ctor/get_scheme/get_host/get_path/to_string. The three string
    // slots are GC references (refFieldBitmap bits 1..3) -- test 17 proves
    // the compacting collector scans + rewrites them.
    //--------------------------------------------------------------------------

    // --- 11. descriptor identity + layout ---
    Expect(il2cpp_uri_class() == &System_Uri_class, "il2cpp_uri_class() returns &System_Uri_class");
    Expect(System_Uri_class.instanceSize == sizeof(Il2CppUriObject), "System_Uri_class.instanceSize == sizeof(Il2CppUriObject)");
    Expect(System_Uri_class.refFieldBitmap == 0b11110u, "System_Uri_class.refFieldBitmap == bits 1,2,3,4 (scheme/host/path/opaque are GC refs)");
    Expect(System_Uri_class.refFieldCount == 4, "System_Uri_class.refFieldCount == 4");
    Expect(il2cpp_class_by_name("System.Uri") == &System_Uri_class, "System.Uri resolves by name (registered)");

    // --- 12. ctor(string) + getters (net48 oracle: https://example.com/path) ---
    Il2CppObject* u = UriFrom("https://example.com/path/to?q=1");
    Expect(u->klass == &System_Uri_class, "Uri object klass stamped");
    Expect(il2cpp_is_instance(u, &System_Object_class), "a Uri IS-A System.Object (baseClass chain)");
    Expect(ManagedText(icall_uri_get_scheme(u)) == "https", "get_Scheme == https");
    Expect(ManagedText(icall_uri_get_host(u)) == "example.com", "get_Host == example.com");
    Expect(ManagedText(icall_uri_get_path(u)) == "/path/to?q=1", "get_AbsolutePath keeps the path verbatim (query included)");

    // --- 13. ToString round-trips (verbatim -- the domain round-trip rule) ---
    Expect(ManagedText(icall_uri_to_string(u)) == "https://example.com/path/to?q=1", "ToString round-trips the original");
    Expect(ManagedText(icall_uri_to_string(UriFrom("http://host"))) == "http://host", "ToString round-trips without a path");
    Expect(ManagedText(icall_uri_get_path(UriFrom("http://host"))) == "", "no path -> empty path slot");
    Expect(ManagedText(icall_uri_to_string(UriFrom("ftp://a/b/c"))) == "ftp://a/b/c", "ToString round-trips ftp");
    Expect(ManagedText(icall_uri_to_string(UriFrom("file://x/y"))) == "file://x/y", "ToString round-trips file");
    Expect(ManagedText(icall_uri_get_host(UriFrom("scheme://h:8080/p"))) == "h:8080",
        "port stays in the host verbatim (documented simplification)");

    // --- 13b. D-NETD2-1: network-path + opaque forms (net48 oracle) ---
    Expect(ManagedText(icall_uri_get_scheme(UriFrom("//host/path"))) == "file",
        "network-path //host/path -> scheme file");
    Expect(ManagedText(icall_uri_get_host(UriFrom("//host/path"))) == "host",
        "network-path //host/path -> host host");
    Expect(ManagedText(icall_uri_get_path(UriFrom("//host/path"))) == "/path",
        "network-path //host/path -> path /path");
    Expect(ManagedText(icall_uri_to_string(UriFrom("//host/path"))) == "file://host/path",
        "network-path ToString == file://host/path");
    Expect(ManagedText(icall_uri_get_host(UriFrom("mailto:x@y.com"))) == "y.com",
        "mailto:x@y.com -> host y.com (userinfo@host)");
    Expect(ManagedText(icall_uri_get_path(UriFrom("mailto:x@y.com"))) == "",
        "mailto:x@y.com -> empty AbsolutePath");
    Expect(ManagedText(icall_uri_to_string(UriFrom("mailto:x@y.com"))) == "mailto:x@y.com",
        "mailto ToString round-trips the opaque text");
    Expect(ManagedText(icall_uri_to_string(UriFrom("mailto:x@y.com/z"))) == "mailto:x@y.com/z",
        "mailto with path ToString round-trips");
    Expect(ManagedText(icall_uri_get_host(UriFrom("custom:foo/bar"))) == ""
        && ManagedText(icall_uri_get_path(UriFrom("custom:foo/bar"))) == "foo/bar",
        "non-mailto opaque: host empty, path == whole rest");
    Expect(ManagedText(icall_uri_to_string(UriFrom("custom:foo/bar"))) == "custom:foo/bar",
        "non-mailto opaque ToString round-trips");
    Expect(ManagedText(icall_uri_to_string(UriFrom("urn:isbn:123"))) == "urn:isbn:123",
        "urn opaque with embedded colon round-trips");

    // --- 14. un-ctor'd instance reads null slots (zero-init) ---
    Il2CppObject* raw = reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class));
    Expect(icall_uri_get_scheme(raw) == nullptr && icall_uri_get_host(raw) == nullptr
        && icall_uri_get_path(raw) == nullptr, "un-ctor'd Uri reads null slots (GC-safe zero-init)");

    // --- 15. ctor error paths (net48 baseline: the ctor THROWS) ---
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)), nullptr); }, kArgNullExceptionChain),
        "Uri((string)null) throws ArgumentNullException");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("abc"))); }, kArgExceptionChain),
        "Uri(abc) throws ArgumentException (no :// separator)");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("http://"))); }, kArgExceptionChain),
        "Uri(http://) throws ArgumentException (empty host)");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("ht tp://h"))); }, kArgExceptionChain),
        "Uri(ht tp://h) throws ArgumentException (illegal scheme char)");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("1http://h"))); }, kArgExceptionChain),
        "Uri(1http://h) throws ArgumentException (non-ALPHA first scheme char)");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(reinterpret_cast<Il2CppObject*>(il2cpp_object_new(&System_Uri_class)),
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_utf8("http://h\xC3\xA9llo"))); }, kArgExceptionChain),
        "Uri with non-ASCII text throws ArgumentException (ASCII-only domain)");

    // --- 16. null receiver guards ---
    Expect(ThrowsDotNet([&] { icall_uri_get_scheme(nullptr); }, kNullRefChain),
        "null receiver get_Scheme throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_uri_to_string(nullptr); }, kNullRefChain),
        "null receiver ToString throws NullReferenceException");
    Expect(ThrowsDotNet([&] { icall_uri_ctor(nullptr,
        reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs("http://h"))); }, kNullRefChain),
        "null receiver .ctor throws NullReferenceException");

    // --- 17. GC survival: the scheme/host/path strings are reachable ONLY
    //    through the rooted Uri object -- they must survive a full
    //    collection and the object's slots must be REWRITTEN by the Gen0
    //    compaction (the refFieldBitmap bits 1..3 are the roots). ---
    {
        Il2CppObject* rooted = UriFrom("https://rooted.example/x/y");
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp_gc_collect();
        // NOTE: the compacting collector MOVES Gen0 survivors and rewrites
        // every reference; 'rooted' is a GC root so it was updated -- all
        // checks below use content equality, never stale pointers.
        Expect(il2cpp_gc_get_generation(rooted) >= kGcGeneration1,
            "rooted Uri object survives collection (promoted)");
        Expect(rooted->klass == &System_Uri_class, "Uri klass intact after compaction");
        Expect(ManagedText(icall_uri_get_scheme(rooted)) == "https",
            "scheme survived via refFieldBitmap bit 1");
        Expect(ManagedText(icall_uri_get_host(rooted)) == "rooted.example",
            "host survived via refFieldBitmap bit 2");
        Expect(ManagedText(icall_uri_get_path(rooted)) == "/x/y",
            "path survived via refFieldBitmap bit 3");
        Expect(ManagedText(icall_uri_to_string(rooted)) == "https://rooted.example/x/y",
            "ToString round-trips after compaction (slots rewritten)");
        il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    // --- 18. D-NETD2-1: the OPAQUE slot (bit 4) is also a GC root -- a
    //    mailto Uri's opaque text must survive the same full collection +
    //    Gen0 compaction, and ToString still round-trips. ---
    {
        Il2CppObject* rooted = UriFrom("mailto:user@host.example/p");
        il2cpp_gc_add_static_root(reinterpret_cast<void**>(&rooted));
        il2cpp_gc_collect();
        Expect(il2cpp_gc_get_generation(rooted) >= kGcGeneration1,
            "mailto Uri survives collection (promoted)");
        Expect(ManagedText(icall_uri_get_host(rooted)) == "host.example",
            "mailto host survived via refFieldBitmap bit 2");
        Expect(ManagedText(icall_uri_get_path(rooted)) == "/p",
            "mailto path survived via refFieldBitmap bit 3");
        Expect(ManagedText(icall_uri_to_string(rooted)) == "mailto:user@host.example/p",
            "mailto ToString round-trips after compaction (opaque slot rewritten)");
        il2cpp_gc_remove_static_root(reinterpret_cast<void**>(&rooted));
    }

    if (g_failures == 0)
    {
        std::printf("ALL PASS\n");
    }
    return g_failures == 0 ? 0 : 1;
}
