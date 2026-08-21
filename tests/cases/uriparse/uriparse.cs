//------------------------------------------------------------------------------
// uriparse.cs -- System.Uri PARSING DEEP 2 e2e reference case (net48 oracle,
// byte-for-byte, dual-compiler). Created in e2e batch C80 AFTER the Uri
// domain landed (MethodMap.Uri.cs -> versionuri.h: .ctor(string) /
// get_Scheme / get_Host / get_AbsolutePath / ToString -- the wired surface).
//
// Goes deeper than uricase (C32: the basic 4 accessor + 4 error paths) and
// netdeep2 (C48: scheme grammar / composition identity / relative forms):
// THIS case drives the three axes the task names --
//
//   1) RELATIVE (相对): the THROW matrix over relative-form strings, grammar
//      violations and empty inputs, asserting BOTH runtimes raise a
//      SystemException. Every invalid input below was verified to throw on
//      net48 AND to be rejected by the ILC grammar (versionuri.h: no "//"
//      prefix + no ":" separator, empty/bad scheme, empty host, non-ASCII
//      text). Deliberately EXCLUDED (net48/ILC divergences, see GAPS
//      D-UP-1): "a:b" (net48 DOS-drive reject vs ILC single-letter opaque
//      accept), "http:" / "http:/x" (net48 rejects a "://"-less scheme part
//      that ILC parses as opaque).
//   2) COMPOSITION (组合): the accessor-combination identity
//      Scheme + "://" + Host + AbsolutePath == ToString and the ToString
//      round-trip stability (parse -> ToString -> parse -> ToString is
//      idempotent) over path-bearing hierarchical URIs -- plus the RESERVED-
//      ESCAPE verbatim rule (net48 keeps %3F/%23/%40/%3B/%2F escaped in
//      both AbsolutePath and ToString; ILC keeps everything verbatim --
//      the COMMON subset).
//   3) OPAQUE (转义/opaque): "scheme:rest" forms -- urn: with a reserved
//      escape (path = the whole rest VERBATIM, host empty), mailto: with a
//      userinfo@host split, and the "//host" network-path -> file form
//      (scheme/host probed only -- net48 normalizes its path/ToString, so
//      the path/ts probes stay out, matching netdeep2's convention).
//
// net48-shape discipline: every probe is a BOOL on the net48/ILC COMMON
// subset (no raw strings -- the e2e byte-exact convention). Unreserved
// escapes (%20 space, %7E, %41, %2d) are NOT probed: net48 UNESCAPES them
// while ILC keeps the text verbatim -- a documented divergence (D-UP-1).
//
// Every line prints "label=True/False"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class UriParse {
    public static void Main() {
        // ============ 1. RELATIVE / INVALID -> SystemException on both ============
        string[] invalid = {
            "",              // empty
            "x",             // bare word
            "?",             // bare query marker
            "#",             // bare fragment marker
            "relative/path", // no scheme separator
            "/rooted/path",  // rooted path, no scheme
            "./dot",         // dot-relative
            "../up",         // dotdot-relative
            "1http://x",     // digit-leading scheme (RFC 3986 first ALPHA)
            "a b://h",       // space inside scheme
            "://h",          // empty scheme
            "http://",       // empty host
            "http:///path",  // empty host with path
            "//",            // network-path with EMPTY authority
        };
        for (int i = 0; i < invalid.Length; i++) {
            bool threw = false;
            try { new Uri(invalid[i]); }
            catch (SystemException) { threw = true; }
            Console.WriteLine("r" + i + "=" + threw);   // True x14
        }
        // Null input: ArgumentNullException on BOTH runtimes.
        bool nullThrew = false;
        try { new Uri((string)null); }
        catch (ArgumentNullException) { nullThrew = true; }
        Console.WriteLine("r_null=" + nullThrew);       // True

        // ============ 2. COMPOSITION (accessor combination + round-trip) ============
        string[] samples = {
            "https://example.com/path/to",
            "http://sub.example.co.uk/deep/path/",
            "https://h/",
            "http://127.0.0.1/x",
            "custom+scheme://h/p",
            "a1://h/p",
            "x-y://h/p",
            "a.b://h/p",
            "ftp://a/b/c",
            "http://example.com/Path/With/Case",
        };
        for (int i = 0; i < samples.Length; i++) {
            Uri cu = new Uri(samples[i]);
            Console.WriteLine("c" + i + "_comb=" +
                (cu.Scheme + "://" + cu.Host + cu.AbsolutePath == cu.ToString())); // True x10
            Console.WriteLine("c" + i + "_rt=" +
                (new Uri(cu.ToString()).ToString() == cu.ToString()));             // True x10
        }

        // ============ 3. RESERVED ESCAPES -- verbatim in BOTH abs and ts ============
        string[] esc = {
            "https://h/a%3Fb",   // %3F '?'
            "https://h/a%23b",   // %23 '#'
            "https://h/a%40b",   // %40 '@'
            "http://h/a%3Bb",    // %3B ';'
            "https://h/%2Fb",    // %2F '/'
        };
        for (int i = 0; i < esc.Length; i++) {
            Uri eu = new Uri(esc[i]);
            // composition identity holds even with a reserved escape (net48
            // keeps the escaped text in both AbsolutePath and ToString, and
            // ILC keeps everything verbatim -- the common subset).
            Console.WriteLine("e" + i + "_comb=" +
                (eu.Scheme + "://" + eu.Host + eu.AbsolutePath == eu.ToString())); // True x5
            Console.WriteLine("e" + i + "_ts=" + (eu.ToString() == esc[i])); // True x5
            Console.WriteLine("e" + i + "_esc=" + (eu.AbsolutePath.IndexOf("%") >= 0)); // True x5
        }

        // ============ 4. OPAQUE FORMS (urn / mailto / network-path) ============
        // urn: with a reserved escape -- whole rest is AbsolutePath, Host empty.
        Uri urn = new Uri("urn:isbn%3A0451450523");
        Console.WriteLine("o_urn_scheme=" + (urn.Scheme == "urn"));                       // True
        Console.WriteLine("o_urn_path=" + (urn.AbsolutePath == "isbn%3A0451450523"));     // True
        Console.WriteLine("o_urn_host_empty=" + (urn.Host == ""));                        // True
        Console.WriteLine("o_urn_ts=" + (urn.ToString() == "urn:isbn%3A0451450523"));     // True

        // mailto: userinfo@host split -- Host after the LAST '@', path from '/'.
        Uri m1 = new Uri("mailto:alice@example.com");
        Console.WriteLine("o_m1_host=" + (m1.Host == "example.com"));                     // True
        Console.WriteLine("o_m1_path=" + (m1.AbsolutePath == ""));                        // True
        Console.WriteLine("o_m1_ts=" + (m1.ToString() == "mailto:alice@example.com"));    // True
        Uri m2 = new Uri("mailto:alice@example.com/sub");
        Console.WriteLine("o_m2_host=" + (m2.Host == "example.com"));                     // True
        Console.WriteLine("o_m2_path=" + (m2.AbsolutePath == "/sub"));                    // True
        Console.WriteLine("o_m2_ts=" + (m2.ToString() == "mailto:alice@example.com/sub"));// True
        Uri m3 = new Uri("mailto:bob");
        Console.WriteLine("o_m3_host=" + (m3.Host == "bob"));                             // True
        Console.WriteLine("o_m3_ts=" + (m3.ToString() == "mailto:bob"));                  // True

        // network-path "//host" -> file scheme (scheme/host only -- net48
        // normalizes the path/ToString, so only the common accessors are probed).
        Uri nw = new Uri("//host");
        Console.WriteLine("o_nw_scheme=" + (nw.Scheme == "file"));                        // True
        Console.WriteLine("o_nw_host=" + (nw.Host == "host"));                            // True

        // opaque custom: rest verbatim, host empty (netdeep2 n4 shape, kept here
        // as the composition round-trip over a NON-escaped opaque).
        Uri o = new Uri("custom:foo/bar");
        Console.WriteLine("o_c_host_empty=" + (o.Host == ""));                            // True
        Console.WriteLine("o_c_path=" + (o.AbsolutePath == "foo/bar"));                   // True
        Console.WriteLine("o_c_rt=" + (new Uri(o.ToString()).ToString() == o.ToString()));// True
    }
}}
