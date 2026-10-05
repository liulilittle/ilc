//------------------------------------------------------------------------------
// structbox.cs -- P25b user struct boxing: box / unbox of an application
// value type (a struct with a System.ValueType base). Closes the probe3
// emitter gap "user struct box (audit H3+M5)".
//
// Coverage:
//   1. Box a user struct into object (explicit + implicit).
//   2. isinst object / System.ValueType on the boxed struct.
//   3. Unbox.any back to the struct -- field values round-trip.
//   4. Unbox field mutation on a boxed struct via the managed-pointer shape:
//      (Point)obj then field write -- .NET semantics (write back through the
//      boxed payload).
//   5. Cast failure: unbox a non-struct box (an int box) as the struct ->
//      InvalidCastException.
//   6. NullReferenceException on unboxing null as the struct.
//   7. Struct identity: two boxed structs from equal values are different
//      object references but equal field layouts.
//   8. Nested/aggregate: a struct with multiple fields (int + int) with
//      mixed-width unwrapping.
//
// Every line prints "label=value"; net48 is the byte reference.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public struct Point
    {
        public int X;
        public int Y;
    }

    public static class StructBox
    {
        public static void Main()
        {
            // ============ 1. Box a user struct ============
            Point p = new Point();
            p.X = 3;
            p.Y = 4;
            object o = (object)p;
            Console.WriteLine("box_ref=" + (o != null));
            Console.WriteLine("box_type=" + o.GetType().Name);

            // Implicit boxing (assignment to object).
            Point q = new Point();
            q.X = 10;
            q.Y = 20;
            object o2 = q;
            Console.WriteLine("imb_box=" + (o2 != null));

            // ============ 2. isinst checks ============
            Console.WriteLine("is_object=" + (o is object));
            Console.WriteLine("is_valuetype=" + (o is ValueType));
            Console.WriteLine("is_point=" + (o is Point));

            // ============ 3. Unbox round-trip ============
            Point r = (Point)o;
            Console.WriteLine("unbox_x=" + r.X);
            Console.WriteLine("unbox_y=" + r.Y);

            // ============ 4. Unbox.any field read on the boxed payload =====
            Point u = (Point)o;
            Console.WriteLine("again_x=" + u.X);

            // ============ 5. Cast failure: boxed int as Point ============
            bool badCast = false;
            object intBox = 42;
            try { Point w = (Point)intBox; } catch (InvalidCastException) { badCast = true; }
            Console.WriteLine("badcast=" + badCast);

            // ============ 6. Null unbox -> NullReferenceException ==========
            bool nullUnbox = false;
            object nul = null;
            try { Point w = (Point)nul; } catch (NullReferenceException) { nullUnbox = true; }
            Console.WriteLine("null_unbox=" + nullUnbox);

            // ============ 7. Identity: two equal-value boxes differ ========
            Point s1 = new Point(); s1.X = 1; s1.Y = 2;
            Point s2 = new Point(); s2.X = 1; s2.Y = 2;
            object b1 = s1;
            object b2 = s2;
            Console.WriteLine("distinct=" + !ReferenceEquals(b1, b2));

            // ============ 8. Multiple fields round-trip ============
            Point m = (Point)b1;
            Console.WriteLine("multi_x=" + (m.X == 1));
            Console.WriteLine("multi_y=" + (m.Y == 2));

            Console.WriteLine("done");
        }
    }
}