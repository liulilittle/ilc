using System;

namespace AuditFix
{
    public class Counter
    {
        private int _value;
        // Manual accessor pair -- exact foldable shape.
        public int Value { get { return _value; } set { _value = value; } }
        // Auto property -- CompilerGenerated backing field, foldable shape.
        public int Auto { get; set; }
        // Static auto property -- static backing field, foldable shape.
        public static int Shared { get; set; }
        // Computed getter -- NOT the 3-instruction shape (arithmetic in the
        // body); must NOT be folded (its function must still be emitted).
        public int Doubled { get { return _value * 2; } }
        // Virtual property -- must NOT be folded (polymorphism).
        public virtual int Tag { get { return 0; } set { } }
    }

    public class SubCounter : Counter
    {
        // Override of a virtual property -- never folded.
        public override int Tag { get { return 7; } set { } }
        // Non-virtual property on the derived type.
        public int Extra { get; set; }
    }

    public static class Props
    {
        public static void Main()
        {
            Counter c = new Counter();
            c.Value = 41;
            Console.WriteLine(c.Value + 1);        // 42 (folded getter + setter)
            c.Auto = 100;
            Console.WriteLine(c.Auto);             // 100 (folded auto property)
            Counter.Shared = 7;
            Console.WriteLine(Counter.Shared);     // 7 (folded static auto)
            Console.WriteLine(c.Doubled);          // 82 (computed -- function call)
            Console.WriteLine(c.Tag);              // 0 (virtual -- vtable dispatch)
            SubCounter sc = new SubCounter();
            Console.WriteLine(sc.Tag);             // 7 (virtual override dispatch)
            sc.Extra = 5;
            Console.WriteLine(sc.Extra);           // 5 (derived folded property)
            c.Value = c.Value + 1;
            Console.WriteLine(c.Value);            // 42 (read-modify-write through folds)
        }
    }
}
