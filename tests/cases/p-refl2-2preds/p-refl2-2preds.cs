//------------------------------------------------------------------------------
// p-refl2-2preds.cs -- refl2 net48 oracle probe for the A-class TYPE PREDICATES
// (Type.IsArray/IsEnum/IsValueType/IsInterface/IsClass/IsPrimitive/
// HasElementType/GetElementType) on a name-string (Type VALUE) receiver.
// net48 semantics pinned: enums ARE value types (IsValueType True) and NOT
// primitive; arrays have element types; interfaces are not classes.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public enum Shade { Red, Green, Blue }
    public struct Pt { public int X; public int Y; }
    public interface IMarker { }
    public class Base9 { }
    public class Derived9 : Base9, IMarker { }

    public static class PRefl2Preds
    {
        public static void Main()
        {
            // Arrays
            Console.WriteLine("[preds.intarr.isarray]=" + (typeof(int[]).IsArray));
            Console.WriteLine("[preds.strarr.isarray]=" + (typeof(string[]).IsArray));
            Console.WriteLine("[preds.intarr.elem]=" + (typeof(int[]).GetElementType() == typeof(int)));
            Console.WriteLine("[preds.intarr.haselem]=" + typeof(int[]).HasElementType);
            Console.WriteLine("[preds.strarr.elem]=" + (typeof(string[]).GetElementType() == typeof(string)));
            Console.WriteLine("[preds.int.haselem]=" + typeof(int).HasElementType);
            Console.WriteLine("[preds.int.getelem]=" + (typeof(int).GetElementType() == null));
            // Primitives / value types / enums
            Console.WriteLine("[preds.int.isprim]=" + typeof(int).IsPrimitive);
            Console.WriteLine("[preds.int.isvt]=" + typeof(int).IsValueType);
            Console.WriteLine("[preds.int.isclass]=" + typeof(int).IsClass);
            Console.WriteLine("[preds.long.isprim]=" + typeof(long).IsPrimitive);
            Console.WriteLine("[preds.dbl.isprim]=" + typeof(double).IsPrimitive);
            Console.WriteLine("[preds.str.isprim]=" + typeof(string).IsPrimitive);
            Console.WriteLine("[preds.str.isclass]=" + typeof(string).IsClass);
            Console.WriteLine("[preds.enum.isenum]=" + typeof(Shade).IsEnum);
            Console.WriteLine("[preds.enum.isvt]=" + typeof(Shade).IsValueType);
            Console.WriteLine("[preds.enum.isprim]=" + typeof(Shade).IsPrimitive);
            Console.WriteLine("[preds.enum.isclass]=" + typeof(Shade).IsClass);
            Console.WriteLine("[preds.pt.isvt]=" + typeof(Pt).IsValueType);
            Console.WriteLine("[preds.pt.isenum]=" + typeof(Pt).IsEnum);
            Console.WriteLine("[preds.pt.isclass]=" + typeof(Pt).IsClass);
            // Class / interface
            Console.WriteLine("[preds.iface.isiface]=" + typeof(IMarker).IsInterface);
            Console.WriteLine("[preds.iface.isclass]=" + typeof(IMarker).IsClass);
            Console.WriteLine("[preds.iface.isvt]=" + typeof(IMarker).IsValueType);
            Console.WriteLine("[preds.derived.isclass]=" + typeof(Derived9).IsClass);
            Console.WriteLine("[preds.derived.isiface]=" + typeof(Derived9).IsInterface);
            Console.WriteLine("[preds.derived.isvt]=" + typeof(Derived9).IsValueType);
            Console.WriteLine("[preds.base.isclass]=" + typeof(Base9).IsClass);
            Console.WriteLine("[preds.derived.isprim]=" + typeof(Derived9).IsPrimitive);
        }
    }
}