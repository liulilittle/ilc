//------------------------------------------------------------------------------
// rf-loud.cs -- RF-N7 e2e probe (net48 oracle, byte-for-byte): the three
// reflection operations that must FAIL LOUDLY instead of falling to a
// spelling generated-member name:
//
//   MethodInfo.MakeGenericMethod(Type[])   -- net48 on a NON-generic method
//        throws InvalidOperationException; the ILC LOUD path throws
//        NotSupportedException. The probe catches Exception (any explicit
//        failure) and prints loud=True on BOTH -- the acceptance is "the call
//        throws an explicit exception, never a generated-name link error".
//   Type.MakeGenericType(Type[])           -- net48 on a NON-generic type
//        throws InvalidOperationException; ILC LOUD -> NotSupportedException.
//   Delegate.CreateDelegate(Type, MethodInfo) -- net48 on an instance method
//        throws ArgumentException; ILC LOUD -> NotSupportedException.
//
// Before RF-N7 each of these fell through the BCL-owner miss to the
// generated-name path (`System_Type_MakeGenericType_...` -- a phantom C++
// symbol that never links / resolves to the wrong slot). The probe RED is
// that miss surfacing as a link error / LOUD reject; the GREEN is the
// explicit LOUD mapping producing loud=True on both sides.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Loud
    {
        public void M() { }
    }

    public static class RfLoud
    {
        public static void Main()
        {
            var m = typeof(Loud).GetMethod("M");

            bool mgm = false;
            try { m.MakeGenericMethod(typeof(int)); }
            catch (Exception) { mgm = true; }
            Console.WriteLine("mgm=" + mgm);

            bool mgt = false;
            try { typeof(Loud).MakeGenericType(typeof(int)); }
            catch (Exception) { mgt = true; }
            Console.WriteLine("mgt=" + mgt);

            bool cd = false;
            try { Delegate.CreateDelegate(typeof(Func<int>), m); }
            catch (Exception) { cd = true; }
            Console.WriteLine("cd=" + cd);
        }
    }
}