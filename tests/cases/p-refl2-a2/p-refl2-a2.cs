// p-refl2-a2: Type name/identity surfaces that already fold (baseline matrix).
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cases
{
    public static class PRefl2A2
    {
        static void P(string tag, object v) { Console.WriteLine("[" + tag + "]=[" + (v == null ? "<null>" : v.ToString()) + "]"); }
        public static void Main()
        {
            P("a02", typeof(int?).Name);
            P("a03", typeof(int[]).Name);
            P("a04", typeof(int[,]).Name);
            P("a05", typeof(List<int>).Name);
            P("a06", typeof(int?).FullName);
            P("a07", typeof(int[,]).FullName);
            P("a08", typeof(int[]).GetArrayRank());
            P("a09", typeof(int[,]).GetArrayRank());
            P("a11", typeof(int?).IsGenericType);
            P("a12", typeof(int?).IsGenericTypeDefinition);
            P("a16", typeof(int).IsAbstract);
            P("a17", typeof(Exception).IsAbstract);
            P("a18", typeof(int).IsSealed);
            P("a19", typeof(Exception).IsSealed);
            P("a20", typeof(int).IsPublic);
            P("a21", typeof(int).IsNested);
            P("a22", typeof(List<int>.Enumerator).IsNested);
            P("a23", typeof(int).IsValueType);
            P("a24", typeof(DayOfWeek).IsEnum);
            P("a25", typeof(IDisposable).IsInterface);
            P("a26", typeof(Exception).BaseType.Name);
            P("a27", typeof(int).DeclaringType == null ? "<null>" : "x");
            P("a28", typeof(int).MetadataToken);
            P("a29", typeof(int).GetHashCode() == typeof(int).GetHashCode());
            P("a30", typeof(int).Equals(typeof(int)));
            P("a31", typeof(int).IsPrimitive);
            P("a32", typeof(string).IsClass);
            P("a33", typeof(int).IsAssignableFrom(typeof(int)));
            P("a34", typeof(Exception).IsAssignableFrom(typeof(ArgumentException)));
            P("a35", typeof(ArgumentException).IsSubclassOf(typeof(Exception)));
            P("a36", typeof(Exception).IsInstanceOfType(new ArgumentException()));
            P("a37", typeof(IDisposable).IsAssignableFrom(typeof(System.IO.MemoryStream)));
            P("a38", typeof(System.IO.MemoryStream).GetInterfaces().Length);
            P("a39", typeof(System.IO.MemoryStream).GetInterface("IDisposable") == null ? "<null>" : "hit");
            P("a40", (int)Type.GetTypeCode(typeof(int)));
            P("a41", typeof(List<int>).GetGenericTypeDefinition().Name);
            P("a42", typeof(List<int>).GetGenericArguments().Length);
            P("a43", typeof(List<int>).GetGenericArguments()[0].Name);
            P("a44", typeof(int).MakeArrayType().Name);
            P("a45", typeof(int).MakeByRefType().Name);
            P("a46", typeof(int).MakePointerType().Name);
            P("a47", typeof(int[]).GetElementType().Name);
            P("a48", typeof(int).GetElementType() == null ? "<null>" : "x");
            P("a49", Type.GetType("System.Int32") == null ? "<null>" : "hit");
            P("a52", Type.GetType("No.Such.Type") == null ? "<null>" : "hit");
        }
    }
}