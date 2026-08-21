//------------------------------------------------------------------------------
// typecheckcase.cs -- net48 reference case for the runtime type-check domain
// (isinst / castclass / Type.GetTypeCode / GetType().Name semantics).
//
// This program runs ONLY on the real .NET Framework 4.8 reference runtime;
// its stdout is the ground truth the runtime's icall_type_* implementations
// must match. The C++ runtime primitives under test are:
//
//   icall_type_is_instance     (isinst / as operator semantics)
//   icall_type_cast            (castclass semantics -- mismatch -> null)
//   icall_type_is_subclass_of  (Type.IsSubclassOf)
//   icall_object_get_type_code (TypeCode mapping)
//   icall_object_get_type      (GetType().Name / Type.ToString())
//
// IMPORTANT (empirical): the brief's TypeCode table said Double = 13, but the
// real net48 enum is Single = 13, Double = 14 -- line 8 below prints the
// empirical value the runtime must match.
//
// All output is deterministic ASCII; exception demos print only the caught
// type name (never the localized message -- net48 messages are OS-language
// dependent and would break byte-diff comparisons).
//------------------------------------------------------------------------------

using System;

class TypeCheckCase
{
    static void Main()
    {
        // --- boxed primitive TypeCode mapping (net48 empirical ground truth) ---
        object boxedInt = 42;
        object boxedDbl = 3.14;
        object boxedBool = true;
        object boxedChar = 'A';
        object boxedStr = "hello";
        Console.WriteLine(boxedInt.GetType().Name);      // Int32
        Console.WriteLine(((int)((IConvertible)boxedInt).GetTypeCode()));  // 9
        Console.WriteLine(boxedDbl.GetType().Name);      // Double
        Console.WriteLine(((int)((IConvertible)boxedDbl).GetTypeCode()));  // 14 (empirical! Single=13)
        Console.WriteLine(boxedBool.GetType().Name);     // Boolean
        Console.WriteLine(((int)((IConvertible)boxedBool).GetTypeCode())); // 3
        Console.WriteLine(boxedChar.GetType().Name);     // Char
        Console.WriteLine(((int)((IConvertible)boxedChar).GetTypeCode())); // 4
        Console.WriteLine(boxedStr.GetType().Name);      // String
        Console.WriteLine(((int)((IConvertible)boxedStr).GetTypeCode()));  // 18
        Console.WriteLine((int)Type.GetTypeCode(typeof(Base)));  // 1 (other -> Object)
        Console.WriteLine((int)Type.GetTypeCode(typeof(int)));   // 9

        // --- isinst semantics (is / as operators) ---
        object o = new Derived();
        Console.WriteLine(o is Base);      // True (base-class chain)
        Console.WriteLine(o is Derived);   // True (exact type)
        Console.WriteLine(o is string);    // False (unrelated)
        Console.WriteLine(o is IFoo);      // True (interface table)
        Console.WriteLine(o is Base2);     // False (unrelated base)
        string s = o as string;
        Console.WriteLine(s == null);      // True (cast mismatch -> null)
        Derived d = o as Derived;
        Console.WriteLine(d != null);      // True (cast match -> obj)
        Base b = o as Base;
        Console.WriteLine(b != null);      // True (base cast -> obj)

        // --- null semantics ---
        object nil = null;
        Console.WriteLine(nil == null);        // True
        Console.WriteLine(nil is Base);        // False (null isinst -> false)
        Base nb = nil as Base;
        Console.WriteLine(nb == null);         // True (null cast -> null)

        // --- GetType() null -> NullReferenceException ---
        try
        {
            Console.WriteLine(nil.GetType().Name);
        }
        catch (NullReferenceException)
        {
            Console.WriteLine("NullReferenceException"); // expected
        }

        // --- arrays (M4 simplification: '[' name-prefix match) ---
        object arr = new int[3];
        Console.WriteLine(arr.GetType().Name); // Int32[]
        Console.WriteLine(arr is int[]);       // True
        Console.WriteLine(arr is object);      // True (array -> object)
        Console.WriteLine(arr is double[]);    // False (element type differs)
    }
}

class Base
{
}

class Base2
{
}

class Derived : Base, IFoo
{
}

interface IFoo
{
}
