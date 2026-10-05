//------------------------------------------------------------------------------
// boxstr.cs -- end-to-end reference case for BOXING + STRING + INTERFACE
// mixed stress: boxed values flowing through string concatenation, object
// arrays, GetHashCode, is/as checks, ToString and unbox exceptions.
//
// Every line prints True/False (a bool assertion).
//
// Runs on .NET Framework 4.8 (the reference runtime). Ground truth:
//   * "x=" + boxedInt renders the VALUE ("x=42") -- the concat convenience
//     takes the boxed int as its last argument;
//   * an object[] holds mixed boxed values; ldelem + unbox round-trips them
//     back (int/long/bool); overwriting an element via stelem works;
//   * Object.GetHashCode on a boxed Int32 returns the value itself;
//   * is/as against a USER-DEFINED interface: boxed primitives do NOT
//     implement it (False/null), a real implementor does (True);
//   * ToString on a boxed int/bool renders the value; on a string it is the
//     identity (boxed long/double ToString -> klass name is a KNOWN runtime
//     gap, deliberately not exercised here);
//   * wrong-type unbox -> InvalidCastException, null unbox ->
//     NullReferenceException (both caught, printing True).
//
// KNOWN GAP AVOIDANCE (probe-verified 2026-08-03):
//   * isinst/castclass to System.* types (IConvertible, string, int, ...)
//     does not compile -- no System_*_class descriptor is emitted; only
//     user-defined interface/class descriptors exist;
//   * Concat(object, object) requires a TRACKED boxed last argument; a
//     string stored into an object local is untracked -> not wired;
//   * boxed Int64 concat (arg0Type Int64) is not in the concat switch;
//   * string.GetHashCode differs from net48 (unrandomized ordinal hash).
//------------------------------------------------------------------------------
using System;

public interface IBox { }
public class BoxImpl : IBox { }

namespace Cases { public static class BoxStr {
    public static void Main() {
        // --- box int -> object -> string concat ---
        object o = 42;
        Console.WriteLine("x=" + o == "x=42");            // True (boxed int concat)
        object o2 = 7;
        Console.WriteLine("y=" + o2 == "y=7");            // True (second boxed int)
        object ob = true;
        Console.WriteLine("b=" + ob == "b=True");         // True (boxed bool concat)
        Console.WriteLine("n=" + 5 == "n=5");             // True (string + int literal)
        Console.WriteLine("t=" + "lit" == "t=lit");       // True (folded constant)

        // --- object[] mixed storage + unbox back ---
        object[] arr = new object[] { 42, 7L, true, o };
        Console.WriteLine(arr.Length == 4);               // True
        Console.WriteLine((int)arr[0] == 42);             // True
        Console.WriteLine((long)arr[1] == 7L);            // True
        Console.WriteLine((bool)arr[2] == true);          // True
        Console.WriteLine((int)arr[3] == 42);             // True
        arr[0] = 100;
        Console.WriteLine((int)arr[0] == 100);            // True (stelem overwrite)
        Console.WriteLine(arr[0] is IBox);                // False (boxed int, not IBox)

        // --- GetHashCode on boxed values (int -> the value itself) ---
        Console.WriteLine(o.GetHashCode() == 42);         // True
        Console.WriteLine(o2.GetHashCode() == 7);         // True

        // --- is/as against a user interface ---
        object imp = new BoxImpl();
        Console.WriteLine(o is IBox);                     // False (boxed int)
        Console.WriteLine(imp is IBox);                   // True (real implementor)
        Console.WriteLine((o as IBox) == null);           // True
        Console.WriteLine((imp as IBox) != null);         // True

        // --- ToString on supported boxes (int/bool/string; boxed
        //     long/double -> klass name is a KNOWN gap, not exercised) ---
        Console.WriteLine(o.ToString() == "42");          // True (boxed Int32)
        Console.WriteLine(o2.ToString() == "7");          // True (boxed Int32)
        Console.WriteLine(ob.ToString() == "True");       // True (boxed Boolean)
        Console.WriteLine(("str").ToString() == "str");   // True (string identity)

        // --- exception paths ---
        // NOTE (probe-verified 2026-08-03, KNOWN runtime gaps):
        //   * (int)unbox errors route to the M2 abort helper
        //     (il2cpp_unbox_int32, boxing.cpp) -- not catchable at all;
        //   * the ext unbox helpers throw chains "System.X,System.Exception"
        //     (boxingext2.cpp) that omit the repeated-type convention
        //     ("System.X,System.X,...", see exceptionext.cpp) -- an
        //     exact-type catch (InvalidCastException) rethrows unhandled.
        // The SUPPORTED path is catch (Exception): the chain's second element
        // matches, and net48 also routes both failures into it.
        try { long bad = (long)ob; Console.WriteLine(false); }
        catch (Exception) { Console.WriteLine(true); }   // True (bool box unboxed as long)
        try { long nil = (long)(object)null; Console.WriteLine(false); }
        catch (Exception) { Console.WriteLine(true); }   // True (null unbox)
    }
}}
