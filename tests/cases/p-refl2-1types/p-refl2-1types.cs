//------------------------------------------------------------------------------
// p-refl2-1types.cs -- refl2 net48 oracle probe for the two-parameter
// Type.GetMethod(string, Type[]) / Type.GetProperty(string, Type[])
// overloads. net48 binds these with Public|Instance|Static by default and
// matches the method/property by EXACT parameter-type list (arity + type
// identity): a wrong type or a wrong arity returns null.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class M1
    {
        public int Value;                 // field
        public int Ren { get; set; }      // non-indexed property
        public int this[int i] { get { return i; } }   // indexer (name "Item")
        public void Zero() { }
        public int F(long a) { return 1; }
        public int F(int a) { return 2; }
        public int G(string s, long l) { return 3; }
    }

    public static class PRefl2Types
    {
        public static void Main()
        {
            var t = typeof(M1);
            Console.WriteLine("[types.f.zero]=" + (t.GetMethod("Zero", new Type[0]) != null));
            Console.WriteLine("[types.f.long]=" + (t.GetMethod("F", new Type[] { typeof(long) }) != null));
            Console.WriteLine("[types.f.int]=" + (t.GetMethod("F", new Type[] { typeof(int) }) != null));
            Console.WriteLine("[types.f.arity0]=" + (t.GetMethod("F", new Type[0]) == null));
            Console.WriteLine("[types.f.bad]=" + (t.GetMethod("F", new Type[] { typeof(double) }) == null));
            Console.WriteLine("[types.g.sig]=" + (t.GetMethod("G", new Type[] { typeof(string), typeof(long) }) != null));
            Console.WriteLine("[types.g.bad]=" + (t.GetMethod("G", new Type[] { typeof(int), typeof(int) }) == null));
            Console.WriteLine("[types.g.arity]=" + (t.GetMethod("G", new Type[] { typeof(string) }) == null));
            Console.WriteLine("[types.miss]=" + (t.GetMethod("Nope", new Type[0]) == null));
            // Properties: empty Type[] matches a non-indexed property.
            Console.WriteLine("[types.p.zero]=" + (t.GetProperty("Ren", new Type[0]) != null));
            Console.WriteLine("[types.p.bad]=" + (t.GetProperty("Ren", new Type[] { typeof(int) }) == null));
            Console.WriteLine("[types.p.miss]=" + (t.GetProperty("Missing", new Type[0]) == null));
            // Indexer by the reflection name "Item".
            Console.WriteLine("[types.item.int]=" + (t.GetProperty("Item", new Type[] { typeof(int) }) != null));
            Console.WriteLine("[types.item.arity0]=" + (t.GetProperty("Item", new Type[0]) == null));
        }
    }
}