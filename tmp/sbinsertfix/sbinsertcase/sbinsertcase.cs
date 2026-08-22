using System;
using System.Text;
namespace Cases { public static class SbInsertCase {
    public static void Main() {
        // Insert(int, string) at index 0
        StringBuilder a = new StringBuilder("base");
        a.Insert(0, "str");
        Console.WriteLine(a.Length);
        Console.WriteLine(a.ToString());
        // Insert(int, int)
        StringBuilder b = new StringBuilder("base");
        b.Insert(0, 42);
        Console.WriteLine(b.Length);
        Console.WriteLine(b.ToString());
        // Insert(int, long)
        StringBuilder c = new StringBuilder("base");
        c.Insert(0, 42L);
        Console.WriteLine(c.Length);
        Console.WriteLine(c.ToString());
        // Insert(int, double)
        StringBuilder d = new StringBuilder("base");
        d.Insert(0, 3.5);
        Console.WriteLine(d.Length);
        Console.WriteLine(d.ToString());
        // Insert(int, bool) true
        StringBuilder e = new StringBuilder("base");
        e.Insert(0, true);
        Console.WriteLine(e.Length);
        Console.WriteLine(e.ToString());
        // Insert(int, char)
        StringBuilder f = new StringBuilder("base");
        f.Insert(0, 'z');
        Console.WriteLine(f.Length);
        Console.WriteLine(f.ToString());
        // Insert(int, string, int count)
        StringBuilder g = new StringBuilder("base");
        g.Insert(0, "xy", 3);
        Console.WriteLine(g.Length);
        Console.WriteLine(g.ToString());
        // Insert at the middle
        StringBuilder h = new StringBuilder("mid");
        h.Insert(2, "xy");
        Console.WriteLine(h.Length);
        Console.WriteLine(h.ToString());
        // Insert at the end
        StringBuilder i = new StringBuilder("end");
        i.Insert(i.Length, "1234");
        Console.WriteLine(i.Length);
        Console.WriteLine(i.ToString());
        // Numeric boundaries: int.MinValue, int.MaxValue
        StringBuilder j = new StringBuilder();
        j.Insert(0, -2147483648);
        Console.WriteLine(j.Length);
        Console.WriteLine(j.ToString());
        StringBuilder k = new StringBuilder();
        k.Insert(0, 2147483647);
        Console.WriteLine(k.Length);
        Console.WriteLine(k.ToString());
        // long boundary
        StringBuilder l = new StringBuilder();
        l.Insert(0, -9223372036854775808L);
        Console.WriteLine(l.Length);
        Console.WriteLine(l.ToString());
        // bool false
        StringBuilder m = new StringBuilder();
        m.Insert(0, false);
        Console.WriteLine(m.Length);
        Console.WriteLine(m.ToString());
        // insert int into the middle
        StringBuilder n = new StringBuilder("abcd");
        n.Insert(2, 99);
        Console.WriteLine(n.Length);
        Console.WriteLine(n.ToString());
        // insert empty string is a no-op
        StringBuilder o = new StringBuilder("base");
        o.Insert(2, "");
        Console.WriteLine(o.Length);
        Console.WriteLine(o.ToString());
        // insert (string,count) into the middle
        StringBuilder p = new StringBuilder("abcd");
        p.Insert(2, "xy", 2);
        Console.WriteLine(p.Length);
        Console.WriteLine(p.ToString());
        // double negative + fractional
        StringBuilder q = new StringBuilder();
        q.Insert(0, -3.25);
        Console.WriteLine(q.Length);
        Console.WriteLine(q.ToString());
        // long insert into the middle
        StringBuilder r = new StringBuilder("abcd");
        r.Insert(1, 10000000000L);
        Console.WriteLine(r.Length);
        Console.WriteLine(r.ToString());
        // char insert into the middle
        StringBuilder s = new StringBuilder("abcd");
        s.Insert(1, 'x');
        Console.WriteLine(s.Length);
        Console.WriteLine(s.ToString());
        // string insert into the middle then another insert (shifted tail)
        StringBuilder t = new StringBuilder("abcdef");
        t.Insert(3, "XY");
        t.Insert(0, "Z");
        Console.WriteLine(t.Length);
        Console.WriteLine(t.ToString());
    }
}}
