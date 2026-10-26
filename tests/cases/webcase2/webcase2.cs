using System;
using System.Net;

namespace Cases { public static class WebCase2 {
    //------------------------------------------------------------------------------
    // webcase2.cs -- WebClient.DownloadString(Uri) e2e reference case (net48
    // oracle, byte-for-byte). Complements webcase (the string overload) with the
    // Uri overload: the runtime marshals the Uri to its string form and runs
    // the same HTTP GET closure.
    //
    // Probes are NETWORK-FREE: a null Uri throws ArgumentNullException (caught
    // -> "other"), and an unroutable loopback port gives a connection-refused
    // WebException on both sides. Shape labels (not exception message text).
    //------------------------------------------------------------------------------
    static string Shape(WebClient wc, Uri uri) {
        try { wc.DownloadString(uri); return "none"; }
        catch (UriFormatException) { return "uri_format_exception"; }
        catch (WebException) { return "web_exception"; }
        catch (Exception) { return "other"; }
    }
    public static void Main() {
        var wc = new WebClient();
        Console.WriteLine(Shape(wc, (Uri)null));                       // other (ArgumentNullException)
        Console.WriteLine(Shape(wc, new Uri("http://127.0.0.1:1/")));  // web_exception (connection refused)
    }
} }
