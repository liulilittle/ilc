//------------------------------------------------------------------------------
// urihash1.cs -- net48 Uri.GetHashCode() e2e reference case.
//
// REGRESSION GUARD for the `diffuri001` [uri.gethashcode] FAIL. The old runtime
// called that integer an "accepted deviation"; it was not. Measured on net48:
//
//   Uri.GetHashCode() == StringComparer.InvariantCultureIgnoreCase.GetHashCode(
//       uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped))
//
// i.e. the INVARIANT culture (not CurrentCulture) ignore-case hash of net48's
// own HttpRequestUrl projection. The projection differs from AbsoluteUri in
// ways this case pins one by one.
//
// Every record is a plain scalar (int or bool) so the output is byte-identical
// across the two runtimes. The string-comparer side of the same defect is
// pinned in tests/runtime/test_urihash.cpp (StringComparer's CULTURE members
// are not wired in the transpiler, so they cannot ride an e2e case without
// recording an unrelated EX gap).
//
// Coverage:
//   1. the baseline rows (port/dot/case normalization, unescape, fragment)
//   2. escape policy: %41/%7E/%20 decode; %2F/%3F/%5B/%23/%00 stay escaped
//   3. dot-fold AFTER unescape for the hash (%2E folds here)
//   4. userinfo dropped, default port dropped, non-default port kept
//   5. opaque: urn/custom/mailto (only mailto drops its userinfo)
//   6. Equals => equal hash over the equal-URI pairs
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public static class Prog
    {
        static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
        static void B(string id, bool v) { Console.WriteLine("[" + id + "] " + v); }

        static int UriHash(string s) { return new Uri(s).GetHashCode(); }

        static void Baseline()
        {
            I("uh.a", UriHash("http://example.com/a"));
            I("uh.case", UriHash("HTTP://Example.COM/A"));
            I("uh.port80", UriHash("http://example.com:80/a"));
            I("uh.dots", UriHash("http://example.com/a/../b/./c"));
            I("uh.space", UriHash("http://example.com/a%20b"));
            I("uh.fragment", UriHash("https://x.y/z?q=1#f"));
            I("uh.fragment.only", UriHash("http://x/#frag"));
            I("uh.utf8", UriHash("http://x/%E4%B8%AD%E6%96%87"));
            I("uh.sharp.s", UriHash("http://x/Stra%C3%9Fe"));
            I("uh.accent", UriHash("http://x/caf%C3%A9"));
        }

        static void EscapePolicy()
        {
            // %41 -> 'A', %7E -> '~' (decoded by SafeUnescaped)
            I("uh.esc.a", UriHash("http://x/?a=%41"));
            I("uh.esc.tilde", UriHash("http://x/%7E"));
            // reserved escapes are KEPT, with the original hex casing
            I("uh.esc.slash", UriHash("http://x/a%2Fb"));
            I("uh.esc.query", UriHash("http://x/a%3Fb"));
            I("uh.esc.double", UriHash("http://x/a%2520b"));
            I("uh.esc.bracket", UriHash("http://x/a%5Bb"));
            I("uh.esc.hash", UriHash("http://x/a%23b"));
            I("uh.esc.nul", UriHash("http://x/a%00b"));
            I("uh.esc.slash.lower", UriHash("http://x/a%2fb"));
            B("uh.esc.slash.case.eq",
              UriHash("http://x/a%2Fb") == UriHash("http://x/a%2fb"));
            // decoded-vs-encoded is observable for bytes SafeUnescaped KEEPS
            B("uh.esc.excl.ne", UriHash("http://x/a%21b") != UriHash("http://x/a!b"));
            B("uh.esc.tilde.eq", UriHash("http://x/a%7Eb") == UriHash("http://x/a~b"));
            B("uh.esc.space.eq", UriHash("http://x/a%20b") == UriHash("http://x/a b"));
        }

        static void DotAndAuthority()
        {
            // net48 unescapes THEN folds for the HASH
            I("uh.dot.escaped", UriHash("http://x/%2E%2E/a"));
            B("uh.dot.escaped.eq", UriHash("http://x/%2E%2E/a") == UriHash("http://x/a"));
            B("uh.dot.plain.eq",
              UriHash("http://x/a/b/../c") == UriHash("http://x/a/c"));
            I("uh.userinfo", UriHash("http://user:pw@x/a"));
            B("uh.userinfo.dropped", UriHash("http://user:pw@x/a") == UriHash("http://x/a"));
            I("uh.port.custom", UriHash("http://x:8080/a"));
            I("uh.port.https.default", UriHash("https://x:443/a"));
            B("uh.port.https.dropped",
              UriHash("https://x:443/a") == UriHash("https://x/a"));
            I("uh.double.slash", UriHash("http://h//p"));
            I("uh.empty.path", UriHash("http://x"));
            B("uh.empty.path.eq.slash", UriHash("http://x") == UriHash("http://x/"));
        }

        static void Opaque()
        {
            I("uh.mailto", UriHash("mailto:a@b"));
            B("uh.mailto.userinfo.dropped", UriHash("mailto:a@b") == UriHash("mailto:b"));
            I("uh.urn", UriHash("urn:isbn:123"));
            I("uh.custom", UriHash("custom:x@y"));
            // custom schemes keep the '@' (only mailto splits it)
            B("uh.custom.keeps.at", UriHash("custom:x@y") != UriHash("custom:y"));
            I("uh.file", UriHash("file:///C:/a"));
        }

        static void EqualHashPairs()
        {
            string[][] pairs =
            {
                new[] { "HTTP://Example.COM/A", "http://example.com/a" },
                new[] { "http://example.com:80/a", "http://example.com/a" },
                new[] { "http://example.com/a/../b/./c", "http://example.com/b/c" },
                new[] { "https://x.y/z?q=1#f", "https://x.y/z?q=1" },
                new[] { "http://x/%E4%B8%AD%E6%96%87", "http://x/%e4%b8%ad%e6%96%87" },
                new[] { "http://user@x/a", "http://x/a" },
            };
            for (int i = 0; i < pairs.Length; i++)
            {
                Uri a = new Uri(pairs[i][0]);
                Uri b = new Uri(pairs[i][1]);
                B("uh.pair" + i + ".hash", a.GetHashCode() == b.GetHashCode());
                I("uh.pair" + i + ".hashval", a.GetHashCode());
            }
        }

        public static void Main()
        {
            Console.WriteLine("-- shard urihash1 start --");
            try { Baseline(); } catch (Exception) { Console.WriteLine("[uh.baseline] EX gap"); }
            try { EscapePolicy(); } catch (Exception) { Console.WriteLine("[uh.esc] EX gap"); }
            try { DotAndAuthority(); } catch (Exception) { Console.WriteLine("[uh.dot] EX gap"); }
            try { Opaque(); } catch (Exception) { Console.WriteLine("[uh.opaque] EX gap"); }
            try { EqualHashPairs(); } catch (Exception) { Console.WriteLine("[uh.pairs] EX gap"); }
            Console.WriteLine("-- shard urihash1 end --");
        }
    }
}
