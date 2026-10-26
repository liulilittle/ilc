//------------------------------------------------------------------------------
// zz-slcctor -- the net48 SortedList ctor overloads:
//   SortedList(IComparer comparer)              (collections_sortedlist.cs:129)
//   SortedList(IComparer comparer, int capacity)(collections_sortedlist.cs:143)
//   SortedList(IDictionary d)                   (collections_sortedlist.cs:154)
//   SortedList(IDictionary d, IComparer comparer)(collections_sortedlist.cs:166)
//
// net48 allocates the items array (capacity defaults to the minimum), installs
// the comparer (null = default order), and the IDictionary forms copy every
// key/value pair (the copy sorts, so the source order does not matter).
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases
{
    public static class SLCtor
    {
        static void Dump(SortedList sl)
        {
            for (int i = 0; i < sl.Count; i++)
                Console.WriteLine(sl.GetKey(i) + "=" + sl.GetByIndex(i));
        }

        public static void Main()
        {
            var sl = new SortedList(Comparer.Default);
            sl["b"] = 2;
            sl["a"] = 1;
            Dump(sl);

            var sl2 = new SortedList(Comparer.Default, 4);
            sl2.Add("x", 10);
            Console.WriteLine("cap=" + sl2.Capacity + " x=" + sl2["x"]);

            var ht = new Hashtable();
            ht["k1"] = 1;
            ht["k2"] = 2;
            var sl3 = new SortedList(ht);
            Console.WriteLine("k2=" + sl3["k2"]);

            var sl4 = new SortedList(ht, Comparer.Default);
            Dump(sl4);
        }
    }
}

