//------------------------------------------------------------------------------
// test_versionuri.cpp -- System.Version + System.Uri basic-domain unit tests.
//
// Coverage (normal + edge + error paths), mirroring the functions declared
// in versionuri.h:
//   1. il2cpp_version_parse: 1..4 segments, leading zeros, segment values
//      at the int32 boundary; malformed input (empty, null, empty segment,
//      signs, non-digits, trailing garbage, 5 segments, int32 overflow)
//      returns 0 with the output buffer untouched.
//   2. il2cpp_version_to_string: exact round-trip of every parsed form.
//   3. il2cpp_version_compare: ordering across all four positions, ties,
//      and the net48 missing-component rule ((1,2) < (1,2,0,0)).
//   4. il2cpp_uri_try_create + getters: scheme/host/path extraction,
//      no-path form, query text staying in the path, verbatim round-trip
//      via il2cpp_uri_to_string; failures (null, empty, no "://", empty
//      scheme, empty host, illegal scheme character, NON-ALPHA first scheme
//      char -- digit/+/./- leading) leave the output struct untouched.
//   5. D-NETD2-1 (net48-loose grammar): network-path "//host/path" parses
//      as scheme "file" and round-trips as file://host/path; opaque
//      "scheme:rest" forms -- mailto ([userinfo@]host[/path] host/path
//      split, empty rest accepted) and non-mailto (whole rest is the path,
//      host empty, embedded ':' allowed) -- round-trip the original text;
//      relative forms, empty opaque scheme, digit/space-leading opaque
//      schemes and network-path URIs with an empty host are rejected.
//
// Like the other runtime tests: a plain executable with a minimal assertion
// harness -- any failed Expect() prints FAIL and bumps the counter; the
// process exits non-zero on any failure so CI observes a red signal.
//------------------------------------------------------------------------------

#include "versionuri.h"

#include <cstdint>
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

int32_t ParseInto(const char* text, int32_t* components)
{
    return il2cpp::runtime::il2cpp_version_parse(text, components);
}

bool ExpectParse(const char* text, int32_t expectedCount,
                 const int32_t* expectedComponents)
{
    int32_t components[4] = {-1, -1, -1, -1};
    const int32_t count = ParseInto(text, components);
    if (count != expectedCount)
    {
        return false;
    }
    for (int32_t i = 0; i < expectedCount; ++i)
    {
        if (components[i] != expectedComponents[i])
        {
            return false;
        }
    }
    return true;
}

} // namespace

int main()
{
    using il2cpp::runtime::il2cpp_uri_get_host;
    using il2cpp::runtime::il2cpp_uri_get_path;
    using il2cpp::runtime::il2cpp_uri_get_scheme;
    using il2cpp::runtime::il2cpp_uri_to_string;
    using il2cpp::runtime::il2cpp_uri_try_create;
    using il2cpp::runtime::il2cpp_uri_t;
    using il2cpp::runtime::il2cpp_version_compare;
    using il2cpp::runtime::il2cpp_version_parse;
    using il2cpp::runtime::il2cpp_version_to_string;

    // --- 1a. Version parse: valid inputs (1-4 segments) ---
    {
        const int32_t expected[] = {1, 2, 3, 4};
        Expect(ExpectParse("1.2.3.4", 4, expected), "parse(\"1.2.3.4\") == 1.2.3.4");
    }
    {
        const int32_t expected[] = {1, 2, 3};
        Expect(ExpectParse("1.2.3", 3, expected), "parse(\"1.2.3\") == 1.2.3");
    }
    {
        const int32_t expected[] = {1, 2};
        Expect(ExpectParse("1.2", 2, expected), "parse(\"1.2\") == 1.2");
    }
    {
        const int32_t expected[] = {1};
        Expect(ExpectParse("1", 1, expected), "parse(\"1\") == 1");
    }
    {
        const int32_t expected[] = {10, 20, 30, 40};
        Expect(ExpectParse("10.20.30.40", 4, expected), "parse(\"10.20.30.40\") == 10.20.30.40");
    }
    {
        const int32_t expected[] = {0};
        Expect(ExpectParse("0", 1, expected), "parse(\"0\") == 0");
    }
    {
        const int32_t expected[] = {0, 0, 0, 0};
        Expect(ExpectParse("0.0.0.0", 4, expected), "parse(\"0.0.0.0\") == 0.0.0.0");
    }
    {
        const int32_t expected[] = {1, 2};
        Expect(ExpectParse("01.02", 2, expected), "leading zeros accepted (\"01.02\" -> 1.2)");
    }
    {
        const int32_t expected[] = {INT32_MAX};
        Expect(ExpectParse("2147483647", 1, expected), "parse(\"2147483647\") == INT32_MAX");
    }

    // --- 1b. Version parse: malformed input -> 0 (no throw, buffer untouched) ---
    int32_t sink[4];
    Expect(ParseInto("", sink) == 0, "parse(\"\") == 0");
    Expect(ParseInto(nullptr, sink) == 0, "parse(null) == 0");
    Expect(il2cpp_version_parse("1.2", nullptr) == 0, "parse(text, null buffer) == 0");
    Expect(il2cpp_version_parse("", nullptr) == 0, "parse(\"\", null buffer) == 0");
    Expect(ParseInto("1.", sink) == 0, "parse(\"1.\") == 0");
    Expect(ParseInto("1.2.", sink) == 0, "parse(\"1.2.\") == 0");
    Expect(ParseInto(".1", sink) == 0, "parse(\".1\") == 0");
    Expect(ParseInto(".", sink) == 0, "parse(\".\") == 0");
    Expect(ParseInto("1..2", sink) == 0, "parse(\"1..2\") == 0");
    Expect(ParseInto("a", sink) == 0, "parse(\"a\") == 0");
    Expect(ParseInto("1.a", sink) == 0, "parse(\"1.a\") == 0");
    Expect(ParseInto("1a.2", sink) == 0, "parse(\"1a.2\") == 0");
    Expect(ParseInto("-1.2", sink) == 0, "parse(\"-1.2\") == 0");
    Expect(ParseInto("+1.2", sink) == 0, "parse(\"+1.2\") == 0");
    Expect(ParseInto("1 2", sink) == 0, "parse(\"1 2\") == 0");
    Expect(ParseInto("1.2.3.4.5", sink) == 0, "parse(\"1.2.3.4.5\") == 0 (5 segments)");
    Expect(ParseInto("2147483648", sink) == 0, "parse(\"2147483648\") == 0 (int32 overflow)");
    Expect(ParseInto("99999999999999999999", sink) == 0, "parse(19 nines) == 0 (overflow)");
    {
        // Failure must leave the caller's buffer untouched (TryParse rule).
        int32_t sentinel[4] = {42, 43, 44, 45};
        Expect(il2cpp_version_parse("bad", sentinel) == 0 && sentinel[0] == 42 &&
               sentinel[1] == 43 && sentinel[2] == 44 && sentinel[3] == 45,
               "failed parse leaves components untouched");
    }

    // --- 2. Version ToString: exact round-trip ---
    {
        const int32_t v[] = {1, 2, 3, 4};
        Expect(il2cpp_version_to_string(v, 4) == "1.2.3.4", "to_string(1,2,3,4) == \"1.2.3.4\"");
    }
    {
        const int32_t v[] = {1, 2};
        Expect(il2cpp_version_to_string(v, 2) == "1.2", "to_string(1,2) == \"1.2\"");
    }
    {
        const int32_t v[] = {1};
        Expect(il2cpp_version_to_string(v, 1) == "1", "to_string(1) == \"1\"");
    }
    {
        const int32_t v[] = {0, 0};
        Expect(il2cpp_version_to_string(v, 2) == "0.0", "to_string(0,0) == \"0.0\"");
    }
    {
        const int32_t v[] = {10, 20, 30, 40};
        Expect(il2cpp_version_to_string(v, 4) == "10.20.30.40", "to_string(10,20,30,40) == \"10.20.30.40\"");
    }
    Expect(il2cpp_version_to_string(nullptr, 2) == "", "to_string(null,2) == \"\"");
    {
        const int32_t v[] = {1};
        Expect(il2cpp_version_to_string(v, 0) == "", "to_string(v,0) == \"\"");
        Expect(il2cpp_version_to_string(v, -1) == "", "to_string(v,-1) == \"\"");
    }
    {
        const int32_t v[] = {1, 2, 3, 4};
        Expect(il2cpp_version_to_string(v, 9) == "1.2.3.4", "to_string(v,9) clamps to 4");
    }
    {
        // Parse -> ToString round trip over every accepted form.
        int32_t components[4];
        const char* forms[] = {"1", "1.2", "1.2.3", "1.2.3.4", "0.0.0.0", "10.20.30.40"};
        for (const char* form : forms)
        {
            const int32_t count = il2cpp_version_parse(form, components);
            Expect(il2cpp_version_to_string(components, count) == form,
                   "parse/to_string round-trip");
        }
    }

    // --- 3. Version compare: ordering, ties, net48 missing-component rule ---
    {
        const int32_t a[] = {1, 2, 3, 4};
        const int32_t b[] = {1, 2, 3, 4};
        Expect(il2cpp_version_compare(a, 4, b, 4) == 0, "compare(1.2.3.4, 1.2.3.4) == 0");
    }
    {
        const int32_t a[] = {1, 2, 3, 4};
        const int32_t b[] = {1, 2, 3, 5};
        Expect(il2cpp_version_compare(a, 4, b, 4) == -1, "compare(1.2.3.4, 1.2.3.5) == -1");
        Expect(il2cpp_version_compare(b, 4, a, 4) == 1, "compare(1.2.3.5, 1.2.3.4) == 1");
    }
    {
        const int32_t a[] = {1, 2, 4, 0};
        const int32_t b[] = {1, 2, 3, 9};
        Expect(il2cpp_version_compare(a, 4, b, 4) == 1, "compare(1.2.4.0, 1.2.3.9) == 1 (build wins)");
    }
    {
        const int32_t a[] = {1, 3};
        const int32_t b[] = {1, 2, 9, 9};
        Expect(il2cpp_version_compare(a, 2, b, 4) == 1, "compare(1.3, 1.2.9.9) == 1 (minor wins)");
    }
    {
        const int32_t a[] = {1, 2};
        const int32_t b[] = {1, 2, 0, 0};
        Expect(il2cpp_version_compare(a, 2, b, 4) == -1,
               "compare(1.2, 1.2.0.0) == -1 (net48: missing build == -1)");
    }
    {
        const int32_t a[] = {1, 2, 3};
        const int32_t b[] = {1, 2, 3, 4};
        Expect(il2cpp_version_compare(a, 3, b, 4) == -1,
               "compare(1.2.3, 1.2.3.4) == -1 (missing revision == -1)");
    }
    {
        const int32_t a[] = {1};
        const int32_t b[] = {1, 0};
        Expect(il2cpp_version_compare(a, 1, b, 2) == -1,
               "compare(\"1\", \"1.0\") == -1 (missing minor == -1)");
    }
    {
        const int32_t a[] = {1, 2, 3};
        const int32_t b[] = {1, 2};
        Expect(il2cpp_version_compare(a, 3, b, 2) == 1, "compare(1.2.3, 1.2) == 1");
    }
    Expect(il2cpp_version_compare(nullptr, 0, nullptr, 0) == 0, "compare(null, null) == 0");

    // --- 4a. Uri: scheme/host/path extraction + round-trip ---
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("http://example.com/path", &uri) == 1,
               "try_create(\"http://example.com/path\") succeeds");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "http") == 0, "scheme == \"http\"");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "example.com") == 0, "host == \"example.com\"");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "/path") == 0, "path == \"/path\"");
        Expect(il2cpp_uri_to_string(&uri) == "http://example.com/path", "uri round-trip");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("https://example.com", &uri) == 1,
               "try_create(\"https://example.com\") succeeds");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "https") == 0, "scheme == \"https\"");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "example.com") == 0, "host == \"example.com\"");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "") == 0, "path == \"\" (no path)");
        Expect(il2cpp_uri_to_string(&uri) == "https://example.com", "uri round-trip (no path)");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("ftp://192.168.1.1/pub", &uri) == 1,
               "try_create(ftp, IP host) succeeds");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "192.168.1.1") == 0, "IP host extracted");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("http://host:8080/x", &uri) == 1,
               "port is part of the host (simplified)");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "host:8080") == 0, "host == \"host:8080\"");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("http://x/p?a=1&b=2", &uri) == 1,
               "query text stays in the path (simplified)");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "/p?a=1&b=2") == 0,
               "path == \"/p?a=1&b=2\" (no query splitting)");
        Expect(il2cpp_uri_to_string(&uri) == "http://x/p?a=1&b=2", "uri round-trip (query verbatim)");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("a+b-c.d://host/x", &uri) == 1,
               "RFC 3986 scheme chars accepted");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "a+b-c.d") == 0, "scheme == \"a+b-c.d\"");
    }

    // --- 4b. Uri: malformed input -> 0 (no throw, output untouched) ---
    Expect(il2cpp_uri_try_create(nullptr, nullptr) == 0, "try_create(null, null) == 0");
    Expect(il2cpp_uri_try_create("", nullptr) == 0, "try_create(\"\") == 0");
    Expect(il2cpp_uri_try_create("http://x", nullptr) == 0, "try_create(text, null out) == 0");
    Expect(il2cpp_uri_try_create("no-scheme-here", nullptr) == 0, "no \"://\" -> 0");
    Expect(il2cpp_uri_try_create("http:example.com", nullptr) == 0, "\"http:...\" (no ://) -> 0");
    Expect(il2cpp_uri_try_create("://host", nullptr) == 0, "empty scheme -> 0");
    Expect(il2cpp_uri_try_create("http://", nullptr) == 0, "empty host -> 0");
    Expect(il2cpp_uri_try_create("http:///path", nullptr) == 0, "empty authority -> 0");
    Expect(il2cpp_uri_try_create("ht tp://x", nullptr) == 0, "illegal scheme character -> 0");
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("1http://x", &uri) == 0,
               "digit-leading scheme rejected (RFC 3986: first char MUST be ALPHA)");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("+http://x", &uri) == 0,
               "plus-leading scheme rejected (RFC 3986: first char MUST be ALPHA)");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create(".http://x", &uri) == 0,
               "dot-leading scheme rejected (RFC 3986: first char MUST be ALPHA)");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("a1+b-c.d://host/x", &uri) == 1,
               "ALPHA-first scheme with digits/+/-/. accepted");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "a1+b-c.d") == 0,
               "scheme == \"a1+b-c.d\"");
    }
    {
        // Failure must leave the caller's struct untouched (TryParse rule).
        il2cpp_uri_t sentinel;
        sentinel.scheme = "s";
        sentinel.host = "h";
        sentinel.path = "/p";
        Expect(il2cpp_uri_try_create("bad", &sentinel) == 0 &&
               sentinel.scheme == "s" && sentinel.host == "h" && sentinel.path == "/p",
               "failed try_create leaves output untouched");
    }

    // --- 4c. Uri getters: null-safe ---
    Expect(std::strcmp(il2cpp_uri_get_scheme(nullptr), "") == 0, "get_scheme(null) == \"\"");
    Expect(std::strcmp(il2cpp_uri_get_host(nullptr), "") == 0, "get_host(null) == \"\"");
    Expect(std::strcmp(il2cpp_uri_get_path(nullptr), "") == 0, "get_path(null) == \"\"");
    Expect(il2cpp_uri_to_string(nullptr) == "", "to_string(null) == \"\"");

    // --- 4d. D-NETD2-1: network-path + opaque forms (net48 oracle:
    //      netdeep2 probe -- //host/path -> file://, mailto: -> host-bearing
    //      opaque, other schemes -> path-bearing opaque) ---
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("//host/path", &uri) == 1,
               "network-path \"//host/path\" succeeds (net48: file scheme)");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "file") == 0,
               "network-path scheme == \"file\"");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "host") == 0,
               "network-path host == \"host\"");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "/path") == 0,
               "network-path path == \"/path\"");
        Expect(il2cpp_uri_to_string(&uri) == "file://host/path",
               "network-path round-trips as file://host/path");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("//h/p", &uri) == 1,
               "network-path \"//h/p\" succeeds");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "file") == 0
               && std::strcmp(il2cpp_uri_get_host(&uri), "h") == 0
               && std::strcmp(il2cpp_uri_get_path(&uri), "/p") == 0,
               "network-path parts extracted verbatim");
        Expect(il2cpp_uri_to_string(&uri) == "file://h/p",
               "network-path round-trips as file://h/p");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("mailto:x@y.com", &uri) == 1,
               "mailto opaque succeeds (net48 accepts non-:// schemes)");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "mailto") == 0,
               "mailto scheme == \"mailto\"");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "y.com") == 0,
               "mailto host == \"y.com\" (net48: userinfo@host)");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "") == 0,
               "mailto path == \"\" (net48 AbsolutePath)");
        Expect(il2cpp_uri_to_string(&uri) == "mailto:x@y.com",
               "mailto round-trips the original text");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("mailto:foo/bar", &uri) == 1,
               "mailto with path succeeds");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "foo") == 0,
               "mailto:foo/bar host == \"foo\" (no @ -> whole pre-slash part)");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "/bar") == 0,
               "mailto:foo/bar path == \"/bar\"");
        Expect(il2cpp_uri_to_string(&uri) == "mailto:foo/bar",
               "mailto:foo/bar round-trips");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("mailto:a@b/c", &uri) == 1,
               "mailto userinfo+path succeeds");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "b") == 0,
               "mailto:a@b/c host == \"b\"");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "/c") == 0,
               "mailto:a@b/c path == \"/c\"");
        Expect(il2cpp_uri_to_string(&uri) == "mailto:a@b/c",
               "mailto:a@b/c round-trips");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("mailto:", &uri) == 1,
               "empty mailto rest succeeds (net48 accepts)");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "") == 0
               && std::strcmp(il2cpp_uri_get_path(&uri), "") == 0,
               "empty mailto rest -> empty host/path");
        Expect(il2cpp_uri_to_string(&uri) == "mailto:",
               "empty mailto round-trips");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("custom:foo/bar", &uri) == 1,
               "non-mailto opaque succeeds");
        Expect(std::strcmp(il2cpp_uri_get_scheme(&uri), "custom") == 0,
               "custom scheme == \"custom\"");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "") == 0,
               "non-mailto opaque host == \"\" (net48)");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "foo/bar") == 0,
               "non-mailto opaque path == whole rest");
        Expect(il2cpp_uri_to_string(&uri) == "custom:foo/bar",
               "non-mailto opaque round-trips");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("urn:isbn:123", &uri) == 1,
               "urn opaque with embedded colon succeeds");
        Expect(std::strcmp(il2cpp_uri_get_host(&uri), "") == 0,
               "urn opaque host == \"\"");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "isbn:123") == 0,
               "urn opaque path == \"isbn:123\"");
        Expect(il2cpp_uri_to_string(&uri) == "urn:isbn:123",
               "urn opaque round-trips");
    }
    {
        il2cpp_uri_t uri;
        Expect(il2cpp_uri_try_create("custom:", &uri) == 1,
               "empty non-mailto rest succeeds (net48 accepts)");
        Expect(std::strcmp(il2cpp_uri_get_path(&uri), "") == 0
               && std::strcmp(il2cpp_uri_get_host(&uri), "") == 0,
               "custom: -> empty host/path");
        Expect(il2cpp_uri_to_string(&uri) == "custom:",
               "custom: round-trips");
    }

    // --- 4e. D-NETD2-1: still-rejected forms (relative / bad opaque scheme) ---
    Expect(il2cpp_uri_try_create("relative/path", nullptr) == 0,
           "relative form (no ':' at all) still rejected");
    Expect(il2cpp_uri_try_create("/rooted/path", nullptr) == 0,
           "single-slash rooted path still rejected");
    Expect(il2cpp_uri_try_create(":rest", nullptr) == 0, "empty opaque scheme -> 0");
    Expect(il2cpp_uri_try_create("1mailto:x", nullptr) == 0,
           "digit-leading opaque scheme -> 0");
    Expect(il2cpp_uri_try_create("a b:x", nullptr) == 0,
           "space in opaque scheme -> 0");
    Expect(il2cpp_uri_try_create("//", nullptr) == 0,
           "network-path with empty host -> 0");
    Expect(il2cpp_uri_try_create("///path", nullptr) == 0,
           "network-path with empty host (///path) -> 0");

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d version/uri assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime version/uri tests passed\n");
    return 0;
}
