using System;
using System.Collections.Generic;
using System.Linq;
namespace Cases
{
    public static class LoudLinqToListGuid
    {
        public static void Main()
        {
            Guid[] g = new Guid[2];
            g[0] = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            g[1] = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");

            List<Guid> l = g.ToList();
            Console.WriteLine("count=" + l.Count);
            Console.WriteLine("e0=" + l[0]);
            Console.WriteLine("e1=" + l[1]);

            l[1] = new Guid("00000000-0000-0000-0000-000000000001");
            Console.WriteLine("src_after_set=" + g[1]);
            Console.WriteLine("list_after_set=" + l[1]);
            Console.WriteLine("contains=" + l.Contains(g[0]));
            Console.WriteLine("indexof=" + l.IndexOf(g[0]));
            Guid[] back = l.ToArray();
            Console.WriteLine("backlen=" + back.Length);
            Console.WriteLine("back1=" + back[1]);
            Console.WriteLine("done");
        }
    }
}
