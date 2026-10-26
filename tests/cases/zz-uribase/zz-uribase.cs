//------------------------------------------------------------------------------
// zz-uribase -- the net48 base-URI combination ctors:
//   new Uri(baseUri, string relativeUri)   (System_net_System_URI.cs:452)
//   new Uri(baseUri, Uri relativeUri)      (System_net_System_URI.cs:504)
//
// net48 resolves the relative reference against the ABSOLUTE base: an absolute
// relative is stored verbatim; a relative one combines the base scheme/host
// with the base directory + relative text (a leading '/' is root-relative).
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class UriBase
    {
        public static void Main()
        {
            var b = new Uri("https://example.com/dir/page");
            Console.WriteLine("rel=" + new Uri(b, "sub").ToString());
            Console.WriteLine("root=" + new Uri(b, "/root").ToString());
            var rel = new Uri("rel/x", UriKind.Relative);
            Console.WriteLine("uri=" + new Uri(b, rel).ToString());
            Console.WriteLine("abs=" + new Uri(b, "https://other.example/abs").ToString());
        }
    }
}
