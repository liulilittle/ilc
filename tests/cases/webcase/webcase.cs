using System;
using System.Net;

namespace Cases { public static class WebCase {
    // ===== HTTP client chain (net-domain.md T3) -- pinned BEFORE the wiring =====
    // Case contract: docs/plans/e2e-sprint-worklist.md #43 (net T3 gap probe).
    // Runtime: http.h/http.cpp ARE real (il2cpp_http_get/post/free_body, flat
    // const char* engine, src/runtime/src/http.cpp) but the TRANSPILER is not
    // wired: MethodMap.Net.cs:161-181 LOUD-rejects WebClient `.ctor` (no
    // System_Net_WebClient_class descriptor, net-domain.md section 2.3) and
    // `DownloadString(string)` (marshal -> get -> ok-check -> UTF-8 -> managed
    // string -> free_body cannot be composed into one call expression; closure =
    // icall_web_client_download_string per net-domain.md section 4.4).
    // DISPATCH GAP (recorded 2026-08-06): MethodRefResolver.cs:572 routes ONLY
    // System.Net.IPAddress to TryMapNet -- the WebClient/Socket owners never
    // reach the LOUD rejections above, so the transpiler SILENTLY falls to the
    // generated-name path (newobj + generated ctor/DownloadString symbols that
    // do not exist) and the C++ build fails with 3 undeclared identifiers.
    // TRANSPILE FAILURE TODAY = the work order (#43). expected.txt is the
    // pinned net48 oracle (generated from the reference run 2026-08-06): when
    // T3 lands, this case must transpile, build, and byte-match it.
    // Probes are NETWORK-FREE and DNS-FREE (verified 2026-08-06): malformed
    // URLs hit net48 WebClient's FILE fallback (GetUri treats unparseable
    // addresses as CWD-relative file paths) -- NotSupportedException "path
    // format not supported" for bad paths, WebException "file not found" for a
    // missing relative file -- both synchronous, deterministic, sandbox-safe.
    // Shape labels (not exception message text): uri_format_exception /
    // web_exception / other -- the 3-shape chain is the future live-server
    // e2e contract (#43).
    static string Shape(WebClient wc, string url) {
        try { wc.DownloadString(url); return "none"; }
        catch (UriFormatException) { return "uri_format_exception"; }
        catch (WebException) { return "web_exception"; }
        catch (Exception) { return "other"; }
    }
    public static void Main() {
        var wc = new WebClient();
        // malformed URL -> file-fallback path-format error (NotSupportedException)
        Console.WriteLine(Shape(wc, "http://"));     // other
        // malformed IPv6 -> file-fallback path-format error
        Console.WriteLine(Shape(wc, "http://[::1")); // other
        // scheme-only garbage -> file-fallback path-format error
        Console.WriteLine(Shape(wc, "://host"));     // other
        // scheme-less relative -> file "not a url" missing (WebException)
        Console.WriteLine(Shape(wc, "not a url"));   // web_exception
    }
}}
