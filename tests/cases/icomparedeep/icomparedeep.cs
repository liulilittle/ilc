//------------------------------------------------------------------------------
// icomparedeep.cs -- IComparable / IComparer DEEP e2e reference case (net48
// oracle, byte-for-byte). Created in e2e batch C53 (2026-08-12), AFTER the
// comparison domain wiring (strcompare: string CompareTo / CompareOrdinal /
// StringComparison modes; sortdeep: Array.Sort<int32/string> / IndexOf /
// BinarySearch / CopyTo -- MethodMap.ArraySort.cs + MethodMap.String.cs).
//
// DEEP SEMANTICS exercised -- CUSTOM COMPARER IMPLEMENTATIONS:
//   1. A user class implementing IComparable (Person, ordered by Name with
//      string.CompareTo): self-comparison is 0, ordering across a sorted
//      triple, sign symmetry (CompareTo(a,b) == -CompareTo(b,a)), transitivity,
//      equal names (distinct ages) compare 0, and CompareTo(null) > 0.
//   2. A user class implementing IComparer (PersonAgeComparer by Age): the
//      Compare(object,object) contract -1/0/1, symmetry, equal ages compare 0.
//   3. SORTING USAGE: the wired Array.Sort<int32> / Array.Sort<string>
//      ascending sorts are verified BOTH by index AND through custom comparers
//      that implement the same order -- every adjacent sorted pair satisfies
//      comparer.Compare(prev, next) <= 0. Duplicate values sort in place and
//      equal elements compare 0. string sort keeps null first, then ASCII
//      lowercase in ordinal order (the culture-vs-ordinal divergence in the
//      net48 string sort is avoided by using ASCII lowercase inputs -- the
//      sortdeep precedent).
//   9. (cg8-b23) Array.Sort<T>(T[], Comparison<T>): the System.Comparison`1
//      delegate CONSTRUCTION (newobj placeholder + ctor rewrite in
//      CodeGenerator.cs CG8) is wired, so the DELEGATE sorts are exercised:
//      int32 arrays through il2cpp_array_sort_int32_comparison (raw values,
//      ascending/descending/duplicates), string[] / Person[] through
//      il2cpp_array_sort_ref_comparison (element slots, null-first ordinal
//      for strings, user-class Name order for Person).
//   4. Comparison-domain usage ties: Array.BinarySearch on the sorted int
//      array hits a present value at an index whose element equals it and
//      returns a negative insertion point for an absent value.
//
// DISPATCH MODEL (verified by probes): the mscorlib IComparable / IComparer
// INTERFACE dispatch is NOT wired (a callvirt on System.IComparable falls to
// the generated-name path and fails the C++ build -- recorded in GAPS). The
// user comparer classes are therefore exercised through their CONCRETE types
// (direct virtual dispatch, the wired surface); the class DECLARATIONS still
// implement the interfaces (they generate fine, only interface-member CALLS
// do not).
//
// Every line is a labeled bool, so a diff failure shows exactly which
// assertion flipped. Runs on .NET Framework 4.8 (the reference runtime).
//------------------------------------------------------------------------------
using System;
using System.Collections;

class Person : IComparable {
    public string Name;
    public int Age;
    public Person(string n, int a) { Name = n; Age = a; }
    public int CompareTo(object o) {
        if (o == null) return 1;                       // null sorts before any person
        Person other = (Person)o;
        return Name.CompareTo(other.Name);             // ordinal by name
    }
}

class PersonAgeComparer : IComparer {
    public int Compare(object a, object b) {
        Person x = (Person)a;
        Person y = (Person)b;
        if (x.Age < y.Age) return -1;
        if (x.Age > y.Age) return 1;
        return 0;
    }
}

class IntOrderComparer : IComparer {
    public int Compare(object a, object b) {
        int x = (int)a;
        int y = (int)b;
        if (x < y) return -1;
        if (x > y) return 1;
        return 0;
    }
}

class StringOrderComparer : IComparer {
    public int Compare(object a, object b) {
        string x = (string)a;
        string y = (string)b;
        if (x == null) return y == null ? 0 : -1;      // null sorts first
        if (y == null) return 1;
        return x.CompareTo(y);                         // ordinal
    }
}

class ReverseIntComparer : IComparer {
    public int Compare(object a, object b) {
        int x = (int)a;
        int y = (int)b;
        if (x > y) return -1;                          // descending order
        if (x < y) return 1;
        return 0;
    }
}

namespace Cases { public static class ICompareDeep {
    // cg8-b23 (D-ICMP-3): static comparison leaves for the delegate sort --
    // the System.Comparison`1<T> delegate construction (newobj placeholder +
    // ctor rewrite in CodeGenerator.cs CG8) feeds the runtime delegate-sort
    // entries (il2cpp_array_sort_int32_comparison / _ref_comparison).
    private static int CompareAscendingInt(int a, int b) { return a < b ? -1 : (a > b ? 1 : 0); }
    private static int CompareDescendingInt(int a, int b) { return a > b ? -1 : (a < b ? 1 : 0); }
    private static int CompareStringOrdinal(string a, string b) { if (a == null) return b == null ? 0 : -1; if (b == null) return 1; return a.CompareTo(b); }
    private static int ComparePersonByName(Person a, Person b) { return a.Name.CompareTo(b.Name); }
    public static void Main() {
        // ============ 1. IComparable implementation (Person by Name) ============
        Person alice = new Person("alice", 30);
        Person bob = new Person("bob", 25);
        Person carol = new Person("carol", 35);
        Console.WriteLine(alice.CompareTo(alice) == 0);              // True (self is 0)
        Console.WriteLine(alice.CompareTo(bob) < 0);                 // True (alice < bob)
        Console.WriteLine(bob.CompareTo(alice) > 0);                 // True (bob > alice)
        Console.WriteLine(bob.CompareTo(carol) < 0);                 // True (bob < carol)
        Console.WriteLine(alice.CompareTo(carol) < 0);               // True (transitivity alice < carol)
        Console.WriteLine(alice.CompareTo(bob) == -bob.CompareTo(alice)); // True (sign symmetry)
        Console.WriteLine(alice.CompareTo(null) > 0);                // True (null sorts before)
        Person d1 = new Person("dave", 20);
        Person d2 = new Person("dave", 99);
        Console.WriteLine(d1.CompareTo(d2) == 0);                    // True (equal names -> 0)
        Console.WriteLine(d1.CompareTo(alice) > 0);                  // True (dave > alice)
        Person empty = new Person("", 1);
        Console.WriteLine(empty.CompareTo(alice) < 0);               // True ("" sorts before "alice")
        Console.WriteLine(empty.CompareTo(empty) == 0);              // True

        // ============ 2. IComparer implementation (PersonAgeComparer by Age) ============
        PersonAgeComparer pac = new PersonAgeComparer();
        Console.WriteLine(pac.Compare(bob, alice) == -1);            // True (younger first)
        Console.WriteLine(pac.Compare(alice, bob) == 1);             // True (older last)
        Console.WriteLine(pac.Compare(alice, carol) == -1);          // True (30 < 35)
        Console.WriteLine(pac.Compare(bob, bob) == 0);               // True (self is 0)
        Person eve = new Person("eve", 40);
        Person frank = new Person("frank", 40);
        Console.WriteLine(pac.Compare(eve, frank) == 0);             // True (same age, diff names -> 0)
        Console.WriteLine(pac.Compare(alice, bob) == -pac.Compare(bob, alice)); // True (sign symmetry)
        Console.WriteLine(pac.Compare(d1, alice) < 0);               // True (20 < 30)

        // ============ 3. sorting usage: Array.Sort<int32> + comparer agreement ============
        int[] raw = new int[] { 5, 1, 4, 2, 3 };
        Array.Sort(raw);
        Console.WriteLine(raw[0] == 1 && raw[1] == 2 && raw[2] == 3 && raw[3] == 4 && raw[4] == 5); // True (ascending)
        IntOrderComparer ioc = new IntOrderComparer();
        bool intOrdered = true;
        for (int i = 1; i < raw.Length; i++) if (ioc.Compare(raw[i - 1], raw[i]) > 0) intOrdered = false;
        Console.WriteLine(intOrdered);                               // True (comparer agrees)
        int[] dup = new int[] { 7, 7, 3, 7, 1 };
        Array.Sort(dup);
        Console.WriteLine(dup[0] == 1 && dup[1] == 3 && dup[2] == 7 && dup[3] == 7 && dup[4] == 7); // True (duplicates)
        Console.WriteLine(ioc.Compare(7, 7) == 0);                   // True (equal elements -> 0)
        int[] neg = new int[] { -3, 5, 0, -8, 2 };
        Array.Sort(neg);
        Console.WriteLine(neg[0] == -8 && neg[1] == -3 && neg[2] == 0 && neg[3] == 2 && neg[4] == 5); // True

        // ============ 4. sorting usage: Array.Sort<string> + comparer agreement ============
        string[] ss = new string[] { "pear", null, "apple", "cherry", "banana" };
        Array.Sort(ss);
        Console.WriteLine(ss[0] == null);                            // True (null first)
        Console.WriteLine((string)ss[1] == "apple");                 // True
        Console.WriteLine((string)ss[2] == "banana");                // True
        Console.WriteLine((string)ss[3] == "cherry");                // True
        Console.WriteLine((string)ss[4] == "pear");                  // True
        StringOrderComparer soc = new StringOrderComparer();
        bool strOrdered = true;
        for (int i = 1; i < ss.Length; i++) if (soc.Compare(ss[i - 1], ss[i]) > 0) strOrdered = false;
        Console.WriteLine(strOrdered);                               // True (comparer agrees)
        Console.WriteLine(soc.Compare(null, null) == 0);             // True
        Console.WriteLine(soc.Compare(null, "a") < 0);               // True (null first)
        Console.WriteLine(soc.Compare("a", null) > 0);               // True
        Console.WriteLine(soc.Compare("apple", "banana") < 0);       // True (ordinal)

        // ============ 5. comparison usage: BinarySearch ties ============
        int[] sorted = new int[] { 1, 3, 5, 7, 9 };
        int hit = Array.BinarySearch(sorted, 5);
        Console.WriteLine(hit >= 0 && sorted[hit] == 5);             // True (present value)
        int miss = Array.BinarySearch(sorted, 6);
        Console.WriteLine(miss < 0);                                 // True (absent -> negative insertion point)
        int first = Array.BinarySearch(sorted, 1);
        Console.WriteLine(first == 0);                               // True (first element)
        int last = Array.BinarySearch(sorted, 9);
        Console.WriteLine(last == 4);                                // True (last element)

        // ============ 6. D-ICMP-1: IComparable/IComparer INTERFACE dispatch ============
        IComparable ic = alice;                                      // interface-typed receiver
        Console.WriteLine(ic.CompareTo(bob) < 0);                    // True (alice < bob via interface)
        Console.WriteLine(ic.CompareTo(alice) == 0);                 // True (self via interface)
        Console.WriteLine(ic.CompareTo(null) > 0);                   // True (null via interface)
        Console.WriteLine(((IComparable)bob).CompareTo(alice) > 0);  // True (cast interface)
        IComparer iocIface = ioc;                                    // IComparer-typed receiver
        Console.WriteLine(iocIface.Compare(5, 3) > 0);               // True (5 > 3 via interface)
        Console.WriteLine(iocIface.Compare(3, 5) < 0);               // True (3 < 5 via interface)
        Console.WriteLine(iocIface.Compare(7, 7) == 0);              // True (equal via interface)

        // ============ 7. D-ICMP-2: System.Collections.Comparer.Default ============
        Comparer cd = Comparer.Default;
        Console.WriteLine(cd.Compare("a", "b") < 0);                 // True (string ordinal)
        Console.WriteLine(cd.Compare("b", "a") > 0);                 // True
        Console.WriteLine(cd.Compare("a", "a") == 0);                // True
        Console.WriteLine(cd.Compare(3, 5) < 0);                     // True (boxed int)
        Console.WriteLine(cd.Compare(5, 3) > 0);                     // True
        Console.WriteLine(cd.Compare(3, 3) == 0);                    // True
        Console.WriteLine(cd.Compare(null, "a") < 0);                // True (null sorts first)
        Console.WriteLine(cd.Compare("a", null) > 0);                // True
        Console.WriteLine(cd.Compare(null, null) == 0);              // True
        Console.WriteLine(cd.Compare(d1, alice) > 0);                // True (user IComparable fallback: dave > alice)
        Console.WriteLine(cd.Compare(3.5, 3.9) < 0);                 // True (boxed double)

        // ============ 8. D-ICMP-3: Array.Sort(Array, IComparer) ============
        int[] rev = new int[] { 5, 1, 4, 2, 3 };
        Array.Sort(rev, new ReverseIntComparer());                   // non-generic comparer sort
        Console.WriteLine(rev[0] == 5 && rev[1] == 4 && rev[2] == 3 && rev[3] == 2 && rev[4] == 1); // True (descending)
        Array.Sort(rev, new IntOrderComparer());                     // ascending comparer
        Console.WriteLine(rev[0] == 1 && rev[1] == 2 && rev[2] == 3 && rev[3] == 4 && rev[4] == 5); // True
        Array.Sort(rev, Comparer.Default);                           // default comparer object
        Console.WriteLine(rev[0] == 1 && rev[1] == 2 && rev[2] == 3 && rev[3] == 4 && rev[4] == 5); // True
        Array.Sort((Array)rev, (IComparer)null);                     // null comparer -> default (net48)
        Console.WriteLine(rev[0] == 1 && rev[1] == 2 && rev[2] == 3 && rev[3] == 4 && rev[4] == 5); // True
        string[] rss = new string[] { "pear", null, "apple", "cherry", "banana" };
        Array.Sort(rss, new StringOrderComparer());                  // reference comparer sort
        Console.WriteLine(rss[0] == null);                           // True (null first)
        Console.WriteLine((string)rss[1] == "apple");                // True
        Console.WriteLine((string)rss[2] == "banana");               // True
        Console.WriteLine((string)rss[3] == "cherry");               // True
        Console.WriteLine((string)rss[4] == "pear");                 // True

        // ============ 9. D-ICMP-3: Array.Sort<T>(T[], Comparison<T>) ============
        // cg8-b23: the System.Comparison`1<T> delegate CONSTRUCTION (newobj
        // placeholder + ctor rewrite in the CodeGenerator.cs CG8 machinery) is
        // now wired -- the delegate sort is e2e-exercisable. int32 arrays
        // compare raw values (il2cpp_array_sort_int32_comparison); string /
        // Person arrays pass the element slots (il2cpp_array_sort_ref_comparison).
        int[] cint = new int[] { 5, 1, 4, 2, 3 };
        Array.Sort(cint, new Comparison<int>(CompareAscendingInt));
        Console.WriteLine(cint[0] == 1 && cint[1] == 2 && cint[2] == 3 && cint[3] == 4 && cint[4] == 5); // True (ascending)
        Array.Sort(cint, new Comparison<int>(CompareDescendingInt));
        Console.WriteLine(cint[0] == 5 && cint[1] == 4 && cint[2] == 3 && cint[3] == 2 && cint[4] == 1); // True (descending)
        int[] cdup = new int[] { 7, 7, 3, 7, 1 };
        Array.Sort(cdup, new Comparison<int>(CompareAscendingInt));
        Console.WriteLine(cdup[0] == 1 && cdup[1] == 3 && cdup[2] == 7 && cdup[3] == 7 && cdup[4] == 7); // True (duplicates)
        string[] cstr = new string[] { "pear", null, "apple", "cherry", "banana" };
        Array.Sort(cstr, new Comparison<string>(CompareStringOrdinal));
        Console.WriteLine(cstr[0] == null);                          // True (null first)
        Console.WriteLine((string)cstr[1] == "apple");               // True
        Console.WriteLine((string)cstr[2] == "banana");              // True
        Console.WriteLine((string)cstr[3] == "cherry");              // True
        Console.WriteLine((string)cstr[4] == "pear");                // True
        Person[] cp = new Person[] { bob, carol, alice, d1 };
        Array.Sort(cp, new Comparison<Person>(ComparePersonByName));
        Console.WriteLine((string)cp[0].Name == "alice");            // True
        Console.WriteLine((string)cp[1].Name == "bob");              // True
        Console.WriteLine((string)cp[2].Name == "carol");            // True
        Console.WriteLine((string)cp[3].Name == "dave");             // True
    }
}}
