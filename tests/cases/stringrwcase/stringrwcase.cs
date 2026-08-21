using System;
using System.IO;
namespace Cases { public static class StringRWCase {
    public static void Main() {
        // StringReader domain: read/peek/readline/readtoend over managed strings.
        // All bool probes (culture-independent; no int ToString, per e2e convention).
        // Use-after-close is NOT probed (runtime returns -1/null -- documented
        // M3 divergence from net48's ObjectDisposedException; see stringrw.h).
        StringReader r = new StringReader("ab");
        Console.WriteLine(r.Read() == 'a');          // True
        Console.WriteLine(r.Peek() == 'b');          // True
        Console.WriteLine(r.Read() == 'b');          // True
        Console.WriteLine(r.Peek() == -1);           // True
        Console.WriteLine(r.Read() == -1);           // True
        r = new StringReader("one\ntwo\r\nthree");
        Console.WriteLine(r.ReadLine() == "one");    // True
        Console.WriteLine(r.ReadLine() == "two");    // True
        Console.WriteLine(r.ReadLine() == "three");  // True
        Console.WriteLine(r.ReadLine() == null);     // True
        r = new StringReader("hello");
        r.Read();
        Console.WriteLine(r.ReadToEnd() == "ello");  // True
        Console.WriteLine(r.ReadToEnd() == "");      // True
        r = new StringReader("");
        Console.WriteLine(r.Read() == -1);           // True
        Console.WriteLine(r.ReadLine() == null);     // True
        Console.WriteLine(r.ReadToEnd() == "");      // True
        // StringWriter domain: write/writeline/tostring (+ null no-op)
        StringWriter w = new StringWriter();
        Console.WriteLine(w.ToString() == "");       // True
        w.Write("ab");
        w.WriteLine("cd");
        w.Write("ef");
        Console.WriteLine(w.ToString() == "abcd" + Environment.NewLine + "ef"); // True
        w.Write((string)null);
        Console.WriteLine(w.ToString() == "abcd" + Environment.NewLine + "ef"); // True
        // writer -> reader round trip through the object surface
        StringReader r2 = new StringReader(w.ToString());
        Console.WriteLine(r2.ReadLine() == "abcd");  // True
        Console.WriteLine(r2.ReadToEnd() == "ef");   // True
        // ctor error path (caught -> probe prints True on the expected type)
        try { new StringReader(null); Console.WriteLine(false); } catch (ArgumentNullException) { Console.WriteLine(true); }
    }
}}
