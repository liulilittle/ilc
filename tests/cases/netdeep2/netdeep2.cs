//------------------------------------------------------------------------------
// netdeep2.cs -- System.Uri domain DEEP probe 2 (e2e batch C48). Extends the
// uricase baseline with three deeper axes over the SAME wired surface
// (MethodMap.Uri.cs -> versionuri.h: .ctor(string) / get_Scheme / get_Host /
// get_AbsolutePath / ToString):
//
//   1. PARSING (parsing): scheme grammar edges (multi-char / '+' / '.' / digit
//      continuation, single-letter, IP-literal hosts, deep paths, trailing
//      slash, root path "/"), each probed via the four accessors + ToString.
//   2. COMPOSITION (composition): the accessor COMBINATION identity
//      Scheme + "://" + Host + AbsolutePath == ToString on path-bearing URIs,
//      plus ToString round-trip stability (parse -> ToString -> parse ->
//      ToString is idempotent) and mixed-case PATH preservation (net48 keeps
//      the path case verbatim while normalizing scheme/host -- only lowercase
//      scheme/host are probed, the common subset).
//   3. RELATIVE / MALFORMED (relative): relative-form strings ("a/b", "/x",
//      "//h/x") and grammar violations (digit-leading scheme, space in scheme,
//      empty scheme, empty host) throw on BOTH runtimes -- net48
//      UriFormatException, ILC ArgumentException, common base SystemException
//      (the uricase error-probe convention). Null input throws
//      ArgumentNullException on both.
//
// net48-shape discipline (uricase precedent): only the net48/ILC COMMON
// subset is probed. NOT probed (documented divergences): default-port
// stripping / port-bearing Host (net48 strips the port, ILC keeps verbatim),
// host-only AbsolutePath/ToString (net48 normalizes "/" / trailing slash --
// so "//host" stays out of section 4's path/ToString probes, only the
// path-bearing "//host/path" form is asserted), query/fragment
// AbsolutePath (net48 splits them out, ILC keeps the path verbatim),
// scheme/host case normalization (net48 lowercases, ILC verbatim).
// Section 4 probes D-NETD2-1 -- the net48-loose grammar ILC now accepts:
// "//host/path" network-path references (net48: file://host/path) and
// opaque non-"://" schemes -- mailto: ([userinfo@]host[/path] split:
// Host=host / AbsolutePath=path) and any other scheme (Host="", the whole
// rest is AbsolutePath). Every line prints a bool; net48 is the reference
// oracle.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class NetDeep2 {
    public static void Main() {
        // ============ 1. parsing: accessors + ToString on valid URIs ============
        Uri u1 = new Uri("https://example.com/path/to");
        Console.WriteLine("s1_scheme=" + (u1.Scheme == "https"));                 // True
        Console.WriteLine("s1_host=" + (u1.Host == "example.com"));               // True
        Console.WriteLine("s1_path=" + (u1.AbsolutePath == "/path/to"));          // True
        Console.WriteLine("s1_ts=" + (u1.ToString() == "https://example.com/path/to")); // True

        Uri u2 = new Uri("http://sub.example.co.uk/deep/path/");
        Console.WriteLine("s2_host=" + (u2.Host == "sub.example.co.uk"));         // True
        Console.WriteLine("s2_path=" + (u2.AbsolutePath == "/deep/path/"));       // True
        Console.WriteLine("s2_ts=" + (u2.ToString() == "http://sub.example.co.uk/deep/path/")); // True

        Uri u3 = new Uri("https://h/");
        Console.WriteLine("s3_scheme=" + (u3.Scheme == "https"));                 // True
        Console.WriteLine("s3_host=" + (u3.Host == "h"));                         // True
        Console.WriteLine("s3_path=" + (u3.AbsolutePath == "/"));                 // True
        Console.WriteLine("s3_ts=" + (u3.ToString() == "https://h/"));            // True

        Uri u4 = new Uri("http://127.0.0.1/x");
        Console.WriteLine("s4_host=" + (u4.Host == "127.0.0.1"));                 // True
        Console.WriteLine("s4_path=" + (u4.AbsolutePath == "/x"));                // True
        Console.WriteLine("s4_ts=" + (u4.ToString() == "http://127.0.0.1/x"));    // True

        // scheme grammar: RFC 3986 ALPHA *(ALPHA/DIGIT/"+"/"-"/".") after first.
        Uri u5 = new Uri("custom+scheme://h/p");
        Console.WriteLine("s5_scheme=" + (u5.Scheme == "custom+scheme"));         // True
        Console.WriteLine("s5_ts=" + (u5.ToString() == "custom+scheme://h/p"));   // True

        Uri u6 = new Uri("a1://h/p");
        Console.WriteLine("s6_scheme=" + (u6.Scheme == "a1"));                    // True
        Console.WriteLine("s6_ts=" + (u6.ToString() == "a1://h/p"));              // True

        Uri u7 = new Uri("ftp://a/b/c");
        Console.WriteLine("s7_scheme=" + (u7.Scheme == "ftp"));                   // True
        Console.WriteLine("s7_ts=" + (u7.ToString() == "ftp://a/b/c"));           // True

        // host-only URI: accessors only (net48 ToString/AbsolutePath normalize).
        Uri u8 = new Uri("https://example.com");
        Console.WriteLine("s8_scheme=" + (u8.Scheme == "https"));                 // True
        Console.WriteLine("s8_host=" + (u8.Host == "example.com"));               // True

        // mixed-case PATH is preserved by net48 and ILC alike (scheme/host stay
        // lowercase in this probe -- the common subset).
        Uri u9 = new Uri("http://example.com/Path/With/Case");
        Console.WriteLine("s9_path=" + (u9.AbsolutePath == "/Path/With/Case"));   // True
        Console.WriteLine("s9_ts=" + (u9.ToString() == "http://example.com/Path/With/Case")); // True

        // ============ 2. composition: accessor combination == ToString ============
        string[] samples = {
            "https://example.com/path/to",
            "http://sub.example.co.uk/deep/path/",
            "https://h/",
            "http://127.0.0.1/x",
            "custom+scheme://h/p",
            "a1://h/p",
            "ftp://a/b/c",
            "http://example.com/Path/With/Case",
        };
        for (int i = 0; i < samples.Length; i++) {
            Uri cu = new Uri(samples[i]);
            Console.WriteLine("c" + i + "_comb=" +
                (cu.Scheme + "://" + cu.Host + cu.AbsolutePath == cu.ToString())); // True x8
            Console.WriteLine("c" + i + "_rt=" +
                (new Uri(cu.ToString()).ToString() == cu.ToString()));             // True x8
        }

        // ============ 3. relative / malformed: throw on both (SystemException) ============
        string[] invalid = {
            "relative/path",   // no scheme
            "/rooted/path",    // no scheme
            "1http://x",       // digit-leading scheme (RFC 3986: first char ALPHA)
            "a b://h",         // space in scheme
            "://h",            // empty scheme
            "http://",         // empty host
            "http:///path",    // empty host
        };
        for (int i = 0; i < invalid.Length; i++) {
            bool threw = false;
            try { new Uri(invalid[i]); }
            catch (SystemException) { threw = true; }
            Console.WriteLine("e" + i + "=" + threw); // True x7
        }
        // Null input: ArgumentNullException on BOTH runtimes.
        bool nullThrew = false;
        try { new Uri((string)null); }
        catch (ArgumentNullException) { nullThrew = true; }
        Console.WriteLine("e_null=" + nullThrew); // True

        // ============ 4. D-NETD2-1: net48-loose grammar ============
        // network-path reference: net48 parses "//host/path" as file://host/path.
        Uri n1 = new Uri("//host/path");
        Console.WriteLine("n1_scheme=" + (n1.Scheme == "file"));                // True
        Console.WriteLine("n1_host=" + (n1.Host == "host"));                    // True
        Console.WriteLine("n1_path=" + (n1.AbsolutePath == "/path"));           // True
        Console.WriteLine("n1_ts=" + (n1.ToString() == "file://host/path"));    // True

        // opaque mailto: "userinfo@host[/path]" split (Host=host, path after '/').
        Uri n2 = new Uri("mailto:x@y.com");
        Console.WriteLine("n2_scheme=" + (n2.Scheme == "mailto"));              // True
        Console.WriteLine("n2_host=" + (n2.Host == "y.com"));                   // True
        Console.WriteLine("n2_path=" + (n2.AbsolutePath == ""));                // True
        Console.WriteLine("n2_ts=" + (n2.ToString() == "mailto:x@y.com"));      // True

        Uri n3 = new Uri("mailto:foo/bar");
        Console.WriteLine("n3_host=" + (n3.Host == "foo"));                     // True
        Console.WriteLine("n3_path=" + (n3.AbsolutePath == "/bar"));            // True
        Console.WriteLine("n3_ts=" + (n3.ToString() == "mailto:foo/bar"));      // True

        // opaque non-mailto: the whole rest is AbsolutePath, Host is empty.
        Uri n4 = new Uri("custom:foo/bar");
        Console.WriteLine("n4_host=" + (n4.Host == ""));                        // True
        Console.WriteLine("n4_path=" + (n4.AbsolutePath == "foo/bar"));         // True
        Console.WriteLine("n4_ts=" + (n4.ToString() == "custom:foo/bar"));      // True
    }
}}
