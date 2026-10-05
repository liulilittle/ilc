//------------------------------------------------------------------------------
// baudit2 -- G6 container-audit probe B2: user Equals override in List<T>/ArrayList.
//
// Audit finding: runtime::icall_object_equals (valuesemantics.cpp) implements
// reference identity / ordinal strings / same-name boxed value equality only; it
// never virtual-dispatches a user class's Object.Equals override. net48
// List<T>.IndexOf/Contains/Remove use EqualityComparer<T>.Default (IEquatable<T>
// or the virtual Object.Equals) and ArrayList.* call _items[i].Equals(item)
// virtually, so a value-equal distinct P is found/removed there.
//
// Expected diff: net48 prints list_io=0 list_ct=True list_rm=True list_cnt=0
// al_io=0 al_ct=True al_cnt=0; the runtime prints -1/False/False/1/-1/False/1.
//------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;

namespace Cases
{
    public class P
    {
        public int V;
        public P(int v) { V = v; }
        public override bool Equals(object o) { P p = o as P; return p != null && p.V == V; }
        public override int GetHashCode() { return V; }
    }

    public static class Baudit2
    {
        public static void Main()
        {
            List<P> lp = new List<P>();
            lp.Add(new P(1));
            Console.WriteLine("list_io=" + lp.IndexOf(new P(1)));
            Console.WriteLine("list_ct=" + lp.Contains(new P(1)));
            Console.WriteLine("list_rm=" + lp.Remove(new P(1)));
            Console.WriteLine("list_cnt=" + lp.Count);

            ArrayList al = new ArrayList();
            al.Add(new P(2));
            Console.WriteLine("al_io=" + al.IndexOf(new P(2)));
            Console.WriteLine("al_ct=" + al.Contains(new P(2)));
            al.Remove(new P(2));
            Console.WriteLine("al_cnt=" + al.Count);
        }
    }
}
