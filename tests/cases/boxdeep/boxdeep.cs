//------------------------------------------------------------------------------
// boxdeep.cs -- end-to-end reference case for DEEP boxing chains:
//   box int -> object -> unbox -> rebox double -> interface casts ->
//   is/as checks -> GetType check -> ToString check.
//
// Every line prints True/False (a bool assertion): each cast/unbox/type
// check in the chain reports its success or failure.
//
// Runs on .NET Framework 4.8 (the reference runtime). Ground truth:
//   * a boxed Int32 and a boxed Double each implement the BCL interfaces
//     IConvertible / IComparable / IFormattable (is/as -> True);
//   * neither boxed primitive implements a user-defined interface
//     (IDeep / IDeep2 -> False, as -> null);
//   * GetType().Name is the primitive's short name ("Int32"/"Double");
//   * ToString on the boxed value prints the VALUE ("42"/"43");
//   * wrong-type unbox throws InvalidCastException (True via catch);
//   * is/as on null are False/null.
//
// The runtime domain must match this byte-for-byte once the box classes
// gain interface tables and the transpiler gains BCL interface descriptors.
//------------------------------------------------------------------------------
using System;

public interface IDeep { }
public interface IDeep2 { }
public class DeepImpl : IDeep { }

namespace Cases { public static class BoxDeep {
    public static void Main() {
        // --- chain: box int -> object -> unbox -> rebox double ---
        object o1 = 42;
        Console.WriteLine(o1.GetType().Name == "Int32");  // True
        int i1 = (int)o1;
        Console.WriteLine(i1 == 42);                      // True
        object od = (double)(i1 + 1);                     // rebox as Double
        Console.WriteLine(od.GetType().Name == "Double"); // True
        double d1 = (double)od;
        Console.WriteLine(d1 == 43.0);                    // True

        // --- interface casts on the boxed double (BCL interfaces) ---
        Console.WriteLine(od is IConvertible);            // True
        Console.WriteLine(od is IComparable);             // True
        Console.WriteLine(od is IFormattable);            // True
        Console.WriteLine((od as IConvertible) != null);  // True
        Console.WriteLine((od as IComparable) != null);   // True

        // --- interface casts on the boxed int (BCL interfaces) ---
        Console.WriteLine(o1 is IConvertible);            // True
        Console.WriteLine(o1 is IComparable);             // True

        // --- user-defined interfaces: boxed primitives do NOT implement ---
        Console.WriteLine(o1 is IDeep);                   // False
        Console.WriteLine(od is IDeep);                   // False
        Console.WriteLine(o1 is IDeep2);                  // False
        Console.WriteLine((o1 as IDeep) == null);         // True
        Console.WriteLine((od as IDeep) == null);         // True

        // --- positive control: a real implementor ---
        object imp = new DeepImpl();
        Console.WriteLine(imp is IDeep);                  // True
        Console.WriteLine(imp is IDeep2);                 // False
        Console.WriteLine((imp as IDeep) != null);        // True

        // --- GetType + ToString on the chain ---
        Console.WriteLine(o1.GetType().Name == "Int32");  // True
        Console.WriteLine(od.GetType().Name == "Double"); // True
        Console.WriteLine(o1.ToString() == "42");         // True (boxed Int32)
        Console.WriteLine(i1.ToString() == "42");         // True (int)
        Console.WriteLine(d1.ToString() == "43");         // True (double)
        Console.WriteLine(od.ToString() == "43");         // True (boxed Double -> value)

        // --- wrong-type unbox on the chain ---
        try { int x = (int)od; Console.WriteLine(false); }
        catch (InvalidCastException) { Console.WriteLine(true); } // True

        // --- null is/as ---
        object nil = null;
        Console.WriteLine(nil is IDeep);                  // False
        Console.WriteLine((nil as IDeep) == null);        // True
        Console.WriteLine(nil is IConvertible);           // False
        Console.WriteLine((nil as IComparable) == null);  // True

        // --- rebox chain: string round trip through object ---
        object os = i1.ToString();
        Console.WriteLine(os is string);                  // True
        Console.WriteLine((string)os == "42");            // True
    }
}}
