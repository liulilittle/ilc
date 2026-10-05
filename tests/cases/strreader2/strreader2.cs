using System;
using System.IO;

namespace Cases
{
    public static class StrReader2
    {
        public static void Main()
        {
            // StringWriter: Write(char), Write(char[]), WriteLine
            StringWriter w = new StringWriter();
            w.Write('A');
            w.Write(new char[] { 'B', 'C', 'D' });
            w.Write("EF");
            w.WriteLine("GH");
            Console.WriteLine(w.ToString());
            w.Dispose();
            // StringReader: batch Read(char[], int, int) across the span
            StringReader r = new StringReader("Hello World");
            char[] buf = new char[5];
            int n = r.Read(buf, 0, 5);
            Console.WriteLine(n);
            Console.WriteLine(new string(buf, 0, n));
            n = r.Read(buf, 0, 5);
            Console.WriteLine(n);
            Console.WriteLine(new string(buf, 0, n));
            n = r.Read(buf, 0, 5); // end-of-string
            Console.WriteLine(n);
            r.Dispose();
            // reader with a leading empty read then content
            StringReader r2 = new StringReader("ab");
            char[] b2 = new char[2];
            Console.WriteLine(r2.Read(b2, 0, 0));
            Console.WriteLine(r2.Read(b2, 0, 2));
            Console.WriteLine(new string(b2, 0, 2));
        }
    }
}
