using System;

public struct Pt
{
    public int X;
    public Pt(int x) { X = x; }
}

namespace AuditFix
{
    public static class BoxLong
    {
        public static void Main()
        {
            // Box of a non-Int32 value type (audit H3): the transpiler must
            // reject loudly -- silently boxing as Int32 corrupts the value.
            object o = new Pt(7);
            Console.WriteLine(o == null ? 1 : 0);
        }
    }
}
