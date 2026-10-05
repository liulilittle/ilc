using System;
namespace Cases { public static class UriCase {
    public static void Main() {
        // Uri domain: ctor -> scheme/host/path accessors -> ToString -> error paths.
        // All bool probes (culture-independent, per e2e convention).
        // Probes are the net48/ILC COMMON subset (see versionuri.h for the
        // documented simplifications: path kept verbatim incl. query, no
        // trailing-slash normalization, no port stripping, ArgumentException
        // instead of UriFormatException -- so error probes catch SystemException).
        Uri u = new Uri("https://example.com/path/to");
        Console.WriteLine(u.Scheme == "https");                        // True
        Console.WriteLine(u.Host == "example.com");                    // True
        Console.WriteLine(u.AbsolutePath == "/path/to");               // True
        Console.WriteLine(u.ToString() == "https://example.com/path/to"); // True
        Console.WriteLine(new Uri("ftp://a/b/c").ToString() == "ftp://a/b/c"); // True
        Console.WriteLine(new Uri("file://x/y").ToString() == "file://x/y");   // True
        Console.WriteLine(new Uri("https://example.com").Scheme == "https");   // True
        // Error paths: malformed input throws (net48: UriFormatException;
        // ILC domain: ArgumentException -- SystemException is the common base)
        try { new Uri("abc"); Console.WriteLine(false); } catch (SystemException) { Console.WriteLine(true); }
        try { new Uri("http://"); Console.WriteLine(false); } catch (SystemException) { Console.WriteLine(true); }
        try { new Uri("ht tp://h"); Console.WriteLine(false); } catch (SystemException) { Console.WriteLine(true); }
        // Null input: ArgumentNullException on BOTH runtimes
        try { new Uri((string)null); Console.WriteLine(false); } catch (ArgumentNullException) { Console.WriteLine(true); }
    }
}}
