//------------------------------------------------------------------------------
// rf-rootobj.cs -- independent verification probe (审计B) for the D fix gap:
// a USER class that derives DIRECTLY from System.Object. net48 truth:
//   typeof(PlainRoot).BaseType == typeof(object)  (NON-null, Name "Object")
//   typeof(PlainMid).BaseType  == typeof(PlainRoot) (user base)
//   typeof(object).BaseType    == null
// The D emitter maps BaseTypeName == "System.Object" to baseTypeName = nullptr,
// so get_base_type(PlainRoot) answers null instead of Object.
//------------------------------------------------------------------------------
using System;

namespace Cases
{
    public class PlainRoot { }
    public class PlainMid : PlainRoot { }

    public static class RfRootObj
    {
        public static void Main()
        {
            Type b = typeof(PlainRoot).BaseType;
            Console.WriteLine("plain_root_base=" + (b != null ? b.Name : "<null>"));
            Type m = typeof(PlainMid).BaseType;
            Console.WriteLine("plain_mid_base=" + (m != null ? m.Name : "<null>"));
            Type o = typeof(object).BaseType;
            Console.WriteLine("obj_base=" + (o != null ? o.Name : "<null>"));
        }
    }
}