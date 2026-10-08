//------------------------------------------------------------------------------
// rf-derived.cs -- O-1 probe: a DERIVED class with its OWN reference field.
//
// LayoutInstanceFields continues the slot grid from the base chain (a derived
// class's own fields start at base.InstanceSize/pointerSize). EmitClassStruct
// previously emitted ONLY the type's own fields right after _obj, so a
// reference field in a derived class landed at the WRONG byte offset -- the
// C++ struct offset did not match the slot offset the GC bitmap/collector
// scans. net48 oracle: both fields are independent and readable after
// assignment AND after a GC churn (the collector must not scan the wrong
// slot).
//
//   GcBase   { string name; }   -> name at slot 1
//   GcLeaf   : GcBase { string extra; } -> extra at slot 2 (base region first)
//
// Every probe prints a bool against the net48 oracle value.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class GcBase
    {
        public string name;
    }

    public class GcLeaf : GcBase
    {
        public string extra;
    }

    public static class RfDerived
    {
        public static void Main()
        {
            var d = new GcLeaf();
            d.name = "base";
            d.extra = "leaf";

            // Both fields must be stored at their OWN offset (base region is
            // [0, base.InstanceSize); extra sits AFTER it). A collided layout
            // makes one of the writes overwrite the other.
            Console.WriteLine("base=" + (d.name == "base"));
            Console.WriteLine("leaf=" + (d.extra == "leaf"));

            // GC churn: force the mark-sweep collector to run while the
            // instance is reachable via the frame root. If the bitmap slot for
            // the derived reference field mismatched the C++ member offset,
            // the collector would scan the WRONG slot (a neighbouring field or
            // uninitialized memory) and the field would be corrupted/collected.
            for (int i = 0; i < 20000; i++)
            {
                var g = new GcLeaf();
                if (g.extra != null) Console.WriteLine("never");
            }

            Console.WriteLine("base_after_gc=" + (d.name == "base"));
            Console.WriteLine("leaf_after_gc=" + (d.extra == "leaf"));
        }
    }
}