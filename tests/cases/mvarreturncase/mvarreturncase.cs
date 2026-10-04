// mvarreturncase.cs -- P2-D5 fixture: a generic METHOD whose MVAR RETURN (T)
// is now allowed by the decisioner (MethodMvarShareable relaxes rule ① when
// the body is a pure byref-slot move and no by-value MVAR param exists). The
// shared object ABI renders the definition-side MVAR return as Il2CppObject*
// and the call site must unbox by the concrete T. Main exercises:
//   * int    -> the byref arg bridge boxes/unboxes int32 AND the RETURN value
//               unboxes int32;
//   * string -> reference-typed T (identity object pass-through);
//   * double -> the byref arg bridge must box/unbox the double payload (not
//               the hardcoded int32 bridge).
using System;

public class Picker
{
    public static T Pick<T>(ref T a) { return a; }
}

namespace Cases
{
    public static class MvarReturnCase
    {
        public static void Main()
        {
            int x = 42;
            int y = Picker.Pick(ref x);
            Console.WriteLine(y);
            string s = "hi";
            string s2 = Picker.Pick(ref s);
            Console.WriteLine(s2);
            double d = 3.5;
            double d2 = Picker.Pick(ref d);
            Console.WriteLine(d2);
        }
    }
}
