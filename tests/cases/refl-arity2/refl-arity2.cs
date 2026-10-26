//------------------------------------------------------------------------------
// refl-arity2.cs -- REFL-ARITY e2e probe (impl-type2, net48 oracle,
// byte-for-byte):
//
//   The two remaining net48 binder-carrying overloads whose parameter shape is
//   compile-time known (arity + signature bytes) but had no mapping arm:
//
//     PropertyInfo.GetValue(object, BindingFlags, Binder, object[],
//                           CultureInfo)
//       -- net48's 5-param form (reflection_propertyinfo.cs:114). The
//          invokeAttr/binder/culture tail is dropped (this AOT build models no
//          binder); the target/index slots keep their positions.
//     ConstructorInfo.Invoke(BindingFlags, Binder, object[], CultureInfo)
//       -- net48's 4-param form (the MethodBase.Invoke binder-arity
//          reflection_methodinfo.cs:710, overridden by ConstructorInfo). Same
//          drop; the args array keeps its slot.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class Host2
    {
        private int _a;
        private int _b;
        public Host2(int a, int b) { _a = a; _b = b; }
        public int Sum { get { return _a + _b; } }
    }

    public static class ReflArity2
    {
        public static void Main()
        {
            Type t = typeof(Host2);

            // ---- PropertyInfo.GetValue(object, BindingFlags, Binder, object[], CultureInfo) ----
            var prop = t.GetProperty("Sum");
            var v = prop.GetValue(new Host2(10, 20), BindingFlags.Public | BindingFlags.Instance,
                null, null, null);
            Console.WriteLine("prop_get=" + v);

            // ---- ConstructorInfo.Invoke(BindingFlags, Binder, object[], CultureInfo) ----
            var ctor = t.GetConstructor(new Type[] { typeof(int), typeof(int) });
            var o = ctor.Invoke(BindingFlags.Public | BindingFlags.Instance, null,
                new object[] { 7, 35 }, null);
            Console.WriteLine("ctor_inv=" + ((Host2)o).Sum);
        }
    }
}
