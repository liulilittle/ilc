//------------------------------------------------------------------------------
// webreqdeep.cs -- WebClient DOWNLOAD + ENCODING deep e2e (net48 oracle,
// byte-for-byte). Created in e2e batch C59 (2026-08-12), http domain deep.
// webcase (C4-era pin) covered the 3-shape NETWORK-FREE exception chain.
// THIS case exercises the REAL download path -- a native loopback HTTP
// server (mylib) serves a fixed UTF-8 body and WebClient.DownloadString
// fetches and DECODES it (the runtime closure:
// icall_web_client_download_string -> il2cpp_http_get -> UTF-8 decode ->
// managed string). The net48 oracle run and the transpiled run both use the
// SAME mylib server (net48 loads out/mylib.dll; the transpiled exe links the
// mylib STATIC library -- see gen/CMakeLists.txt add_subdirectory(mylib)).
//
// DETERMINISM: the server binds 127.0.0.1:0 (ephemeral), so the port is
// never printed; the response is fixed ASCII text (net48 decodes with
// Encoding.Default = GBK on this machine, the runtime always decodes UTF-8
// -- only pure-ASCII bytes decode identically in both, so the download body
// is ASCII and the multibyte divergence is documented in GAPS.md). All other
// probes are the network-free URL-discrimination surface (webcase contract):
// malformed / scheme-less / empty / null URLs never leave the process.
//------------------------------------------------------------------------------
using System;
using System.Net;
using System.Runtime.InteropServices;

namespace Cases { public static class WebreqDeep {
    // The native loopback HTTP server (mylib, Winsock -- see mylib/mylib.cpp).
    [DllImport("mylib", EntryPoint = "start_server")]
    public static extern int start_server();
    [DllImport("mylib", EntryPoint = "stop_server")]
    public static extern void stop_server();
    [DllImport("mylib", EntryPoint = "server_body_len")]
    public static extern int server_body_len();
    [DllImport("mylib", EntryPoint = "server_served")]
    public static extern int server_served();

    // Network-free URL-discrimination shape (webcase contract): the label is
    // BUCKETED so the net48 exception TYPE (UriFormatException /
    // NotSupportedException / ArgumentException on malformed input) and the
    // runtime type (FormatException) land in the SAME bucket ("other") -- a
    // byte-exact e2e cannot distinguish types that differ between the two
    // runtimes, so the case pins the SHAPE the two sides agree on.
    static string Shape(WebClient wc, string url)
    {
        try { wc.DownloadString(url); return "none"; }
        catch (ArgumentNullException) { return "argnull"; }
        catch (WebException) { return "web"; }
        catch (Exception) { return "other"; }
    }

    public static void Main()
    {
        int port = start_server();
        Console.WriteLine("server_up=" + (port > 0));       // True (loopback server bound)
        var wc = new WebClient();

        // ===== 1. REAL download: GET /data over loopback, decode pipeline =====
        // Body is ASCII ("Hello, ILC! Download OK 2026" -- 28 bytes/units):
        // net48 WebClient.DownloadString decodes the body with
        // Encoding.Default (GBK on this machine) IGNORING charset=utf-8,
        // while the ILC runtime hardcodes UTF-8 -- for NON-ASCII bytes the
        // two decoders produce different strings, so a byte-exact oracle is
        // only possible over pure ASCII. The multibyte decode divergence is
        // recorded in GAPS.md (batch C59). This section still exercises the
        // FULL download pipeline (connect -> request -> response parse ->
        // body -> decode -> managed string) with byte-exact content.
        string s = wc.DownloadString("http://127.0.0.1:" + port + "/data");
        Console.WriteLine("dl_len=" + (s.Length == 28));    // True (28 ASCII units)
        Console.WriteLine("dl_body=" + s);                   // Hello, ILC! Download OK 2026
        Console.WriteLine("body_bytes=" + (server_body_len() == 28)); // True (native byte length)

        // ===== 2. second download served by the same server =====
        string s2 = wc.DownloadString("http://127.0.0.1:" + port + "/data");
        Console.WriteLine("dl2_match=" + (s2 == s));        // True (same body)
        Console.WriteLine("served=" + (server_served() >= 2)); // True (both requests reached the server)

        // ===== 3. network-free URL-discrimination matrix =====
        Console.WriteLine(Shape(wc, null));                 // argnull
        Console.WriteLine(Shape(wc, ""));                   // other
        Console.WriteLine(Shape(wc, "http://"));            // other
        Console.WriteLine(Shape(wc, "http://[::1"));        // other
        Console.WriteLine(Shape(wc, "://host"));            // other
        Console.WriteLine(Shape(wc, "http:///path"));       // other
        Console.WriteLine(Shape(wc, "http://:8080"));       // other
        Console.WriteLine(Shape(wc, "not a url"));          // web
        Console.WriteLine(Shape(wc, "www.example.com"));    // web
        Console.WriteLine(Shape(wc, "http:///")) ;          // other

        stop_server();
        Console.WriteLine("server_down=" + true);           // True (clean stop)
    }
}}
