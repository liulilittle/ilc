//------------------------------------------------------------------------------
// p-refl-accessor.cs -- PropertyInfo.GetGetMethod/GetSetMethod probe (GAP-3 /
// P1). The net48 accessor surface: GetGetMethod()/GetSetMethod() return the
// public accessor MethodInfo or null when the property lacks that accessor.
// The typeinfo property table carries getterName/setterName (TypeEmitter
// emits them), so the four accessor phases are distinguishable:
//   * getter-only   -- GetGetMethod()!=null, GetSetMethod()==null
//   * setter-only   -- GetGetMethod()==null, GetSetMethod()!=null
//   * both          -- neither null
//   * neither       -- both null (a property with no accessors is unusual;
//                      a not-found PropertyInfo (null) must be handled -- but
//                      a null receiver NREs, so the probe uses only found ones)
// Every record is a non-null/null bool verdict, byte-stable. The accessor
// String's Name (get_Name) folds to the member-name identity downstream.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class Acc
    {
        public int GetterOnly { get { return 1; } }
        public int SetterOnly { set { } }
        public int Both { get; set; }
    }

    public static class PReflAccessor
    {
        public static void Main()
        {
            var getterOnly = typeof(Acc).GetProperty("GetterOnly");
            var setterOnly = typeof(Acc).GetProperty("SetterOnly");
            var both = typeof(Acc).GetProperty("Both");

            Console.WriteLine("[go.getter]=" + (getterOnly.GetGetMethod() != null));
            Console.WriteLine("[go.setter]=" + (getterOnly.GetSetMethod() == null));
            Console.WriteLine("[so.getter]=" + (setterOnly.GetGetMethod() == null));
            Console.WriteLine("[so.setter]=" + (setterOnly.GetSetMethod() != null));
            Console.WriteLine("[bo.getter]=" + (both.GetGetMethod() != null));
            Console.WriteLine("[bo.setter]=" + (both.GetSetMethod() != null));
        }
    }
}