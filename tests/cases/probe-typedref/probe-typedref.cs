using System;

namespace Cases
{
    public struct TRefPoint { public int X; public int Y; }
    public class TRefBox { public int F; }
    public enum TRefColor { Red = 1, Green = 2 }

    public static class ProbeTypedRef
    {
        static int sfield = 7;
        static string sname = "fld";

        static void TakeIntParam(int p)
        {
            TypedReference tr = __makeref(p);
            p = 123;
            Console.WriteLine("[param.alias]=" + __refvalue(tr, int));
        }

        static void Mismatch()
        {
            int x = 42;
            TypedReference tr = __makeref(x);
            try
            {
                Console.WriteLine("[mismatch.val]=" + __refvalue(tr, string));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[mismatch.ex]=" + ex.GetType().FullName);
            }
        }

        static void DefaultTr()
        {
            TypedReference tr = default(TypedReference);
            try
            {
                Console.WriteLine("[default.reftype]=" + __reftype(tr));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[default.reftype.ex]=" + ex.GetType().FullName);
            }
            try
            {
                Console.WriteLine("[default.refvalue]=" + __refvalue(tr, int));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[default.refvalue.ex]=" + ex.GetType().FullName);
            }
        }

        public static void Main()
        {
            // [v]/[v2]: the TypedReference is an ALIAS of the storage, not a
            // snapshot -- after x changes, the same reference reads the new value.
            int x = 42;
            TypedReference tr = __makeref(x);
            Console.WriteLine("[v]=" + __refvalue(tr, int));
            x = 99;
            Console.WriteLine("[v2]=" + __refvalue(tr, int));
            Console.WriteLine("[t]=" + __reftype(tr));

            // string: the reference type case (declared type System.String).
            string s = "hi";
            TypedReference ts = __makeref(s);
            Console.WriteLine("[sv]=" + __refvalue(ts, string));
            Console.WriteLine("[st]=" + __reftype(ts));

            // writeback: __refvalue is an lvalue (refanyval + stind).
            __refvalue(tr, int) = 5;
            Console.WriteLine("[writeback]=" + x);

            // static field source (ldsflda).
            TypedReference tf = __makeref(sfield);
            sfield = 8;
            Console.WriteLine("[sfield]=" + __refvalue(tf, int));
            Console.WriteLine("[sfield.t]=" + __reftype(tf));

            // array element source (ldelema).
            int[] arr = new int[] { 1, 2, 3 };
            TypedReference ta = __makeref(arr[1]);
            arr[1] = 22;
            Console.WriteLine("[arr]=" + __refvalue(ta, int));
            Console.WriteLine("[arr.t]=" + __reftype(ta));

            // struct local source (ldloca) + struct declared type.
            TRefPoint p = new TRefPoint();
            p.X = 1;
            TypedReference tp = __makeref(p);
            p.X = 33;
            Console.WriteLine("[struct]=" + __refvalue(tp, TRefPoint).X);
            Console.WriteLine("[struct.t]=" + __reftype(tp));
            Console.WriteLine("[struct.name]=" + __reftype(tp).Name);

            // instance field source (ldflda on a class instance).
            TRefBox b = new TRefBox();
            b.F = 11;
            TypedReference tbf = __makeref(b.F);
            b.F = 44;
            Console.WriteLine("[objfield]=" + __refvalue(tbf, int));
            Console.WriteLine("[objfield.t]=" + __reftype(tbf));

            // enum source: the int32 carrier but the declared type is the enum.
            TRefColor c = TRefColor.Green;
            TypedReference te = __makeref(c);
            Console.WriteLine("[enum]=" + __refvalue(te, TRefColor));
            Console.WriteLine("[enum.t]=" + __reftype(te));
            Console.WriteLine("[enum.name]=" + __reftype(te).Name);

            // method parameter source (ldarga).
            TakeIntParam(5);

            // type mismatch: exact identity is required (net48 throws).
            Mismatch();

            // default(TypedReference): null type -> null System.Type.
            DefaultTr();
        }
    }
}
